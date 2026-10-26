using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A character that walks a travel path made in the NPC Path Tool: the path's points in turn,
/// over the ground fitted between them, standing a while at each (turning to the point's Rot
/// first), then back to the first — or back and forth. The path is the only thing that moves it;
/// with no path it stands still.
///
/// Feet and facing come from the prefab's PrefabBaselineAlignment: the disc is what goes on the
/// path, the forward override is the way it walks.
/// </summary>
[RequireComponent(typeof(Animator))]
public class ObserverManNPCController : MonoBehaviour
{
    private const string StandingState = "StandingState";
    private const string WalkingState  = "WalkingState";

    [Tooltip("Which of the level's characters this is — its id in the level's NPC Character Registry " +
             "(set in the NPC Path Tool).")]
    [SerializeField, HideInInspector] private string characterId;

    public string CharacterId => characterId;

    [Header("Travel Path (set in the NPC Path Tool)")]
    [Tooltip("The path file it walks: points in turn, standing a while at each. None, or no points = " +
             "it stands still.")]
    [SerializeField] private NPCTravelPath travelPath;

    // Paths drawn before they were saved as files. Kept only so the NPC Path Tool can move them
    // into one; nothing walks them.
    [SerializeField, HideInInspector] private List<Vector3>                  pathPoints = new();
    [SerializeField, HideInInspector] private NPCTravelPath.PathWalk          pathWalk   = NPCTravelPath.PathWalk.Loop;
    [SerializeField, HideInInspector] private List<NPCTravelPath.PathLeg>     pathGround = new();
    [SerializeField, HideInInspector] private List<NPCTravelPath.PathPause>   pathPauses = new();

    public NPCTravelPath TravelPath => travelPath;

    private List<Vector3>                 Points => travelPath != null ? travelPath.points : null;
    private List<NPCTravelPath.PathLeg>   Ground => travelPath != null ? travelPath.ground : null;
    private List<NPCTravelPath.PathPause> Pauses => travelPath != null ? travelPath.pauses : null;

#if UNITY_EDITOR
    /// <summary>
    /// A path drawn before paths were files, handed over once and cleared — the NPC Path Tool moves
    /// it into a new file. False when there's none.
    /// </summary>
    public bool TakeScenePath(NPCTravelPath into)
    {
        if (pathPoints == null || pathPoints.Count == 0) return false;
        into.points = new List<Vector3>(pathPoints);
        into.walk   = pathWalk;
        into.ground = pathGround ?? new List<NPCTravelPath.PathLeg>();
        into.pauses = pathPauses ?? new List<NPCTravelPath.PathPause>();
        pathPoints  = new List<Vector3>();
        pathGround  = new List<NPCTravelPath.PathLeg>();
        pathPauses  = new List<NPCTravelPath.PathPause>();
        return true;
    }

    public bool HasScenePath => pathPoints != null && pathPoints.Count > 0;
#endif

    [Header("Standing")]
    [Tooltip("Shortest time it stands at a point with Pause off, in seconds.")]
    [Min(0f)] public float standTimeMin = 3f;
    [Tooltip("Longest time it stands at a point with Pause off, in seconds.")]
    [Min(0f)] public float standTimeMax = 8f;

    [Header("Walking")]
    [Tooltip("How fast it walks, in world units per second.")]
    [Min(0f)] public float walkSpeed = 0.1f;
    [Tooltip("How fast it turns towards where it's walking, in degrees per second.")]
    [Min(0f)] public float turnSpeed = 270f;
    [Tooltip("How long it takes to turn 90° to a path point's Rot before its pause starts, in seconds — " +
             "a 180° turn takes twice as long (set in the NPC Path Tool).")]
    [Min(0f)] public float rotSecondsPer90 = 0.5f;

    [Header("Animation")]
    [Tooltip("How long the blend between standing and walking takes, in seconds.")]
    [Min(0f)] public float blendTime = 0.25f;

    private enum Mode { Standing, Walking }

    private Animator                _animator;
    private PrefabBaselineAlignment _aligner;
    private Mode                    _mode;
    private float                   _standTimer;
    private int                     _pathIndex = -1;
    private int                     _pathStep  = 1;
    private readonly List<Vector3>  _route     = new();   // the ground on the way, then the point
    private int                     _routeIndex;
    private float                   _rotTotal;   // degrees to turn round to a point's Rot
    private float                   _rotDone;
    private float                   _rotTime;

    public bool HasPath        => Points != null && Points.Count > 0;
    public int  PathPointCount => Points?.Count ?? 0;
    public bool PathLoops      => travelPath != null && travelPath.walk == NPCTravelPath.PathWalk.Loop;
    public bool PathStatic     => travelPath != null && travelPath.walk == NPCTravelPath.PathWalk.Static;
    public bool GroundFitted   => Ground != null && Ground.Count == PathPointCount;

    /// <summary>A travel path point, in the world.</summary>
    public Vector3 PathPoint(int i) => Points[i];

    /// <summary>The fitted ground from point i to the next — empty when not fitted.</summary>
    public IReadOnlyList<Vector3> LegGround(int i)
        => GroundFitted && Ground[i]?.ground != null ? Ground[i].ground : System.Array.Empty<Vector3>();

    /// <summary>The way it faces at path point i while standing there, flat — when one is set.</summary>
    public bool TryPathFacing(int i, out Vector3 world)
    {
        world = default;
        if (Pauses == null || i < 0 || i >= Pauses.Count || Pauses[i] == null || !Pauses[i].face)
            return false;
        world = Quaternion.Euler(0f, Pauses[i].faceAngle, 0f) * Vector3.forward;
        return true;
    }

    /// <summary>Where its feet are — the PrefabBaselineAlignment disc, or the root without one.</summary>
    public Vector3 FeetWorld()
    {
        var aligner = Aligner;
        return aligner != null ? aligner.transform.position : transform.position;
    }

    /// <summary>
    /// Stands it on the first path point, facing along the path — towards the first bit of
    /// fitted ground, or the second point. One point and it keeps the way it faces. A Static
    /// path faces the first point's Rot instead, when it has one.
    /// </summary>
    public void PlaceAtPathStart()
    {
        if (!HasPath) return;

        Vector3 start = PathPoint(0);
        var     leg   = LegGround(0);
        Vector3? next = leg.Count > 0      ? leg[0]
                      : PathPointCount > 1 ? PathPoint(1)
                      : (Vector3?)null;

        Vector3? along = next.HasValue ? next.Value - start : (Vector3?)null;
        if (PathStatic && TryPathFacing(0, out Vector3 rot)) along = rot;

        if (along.HasValue)
        {
            Vector3 want = along.Value;
            want.y = 0f;
            Vector3 face = FacingWorld();
            if (want.sqrMagnitude > 1e-10f && face.sqrMagnitude > 1e-10f)
                transform.RotateAround(FeetWorld(), Vector3.up, Vector3.SignedAngle(face, want, Vector3.up));
        }

        transform.position += start - FeetWorld();
    }

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _aligner  = GetComponentInChildren<PrefabBaselineAlignment>(true);
        if (_aligner == null)
            Debug.LogWarning($"[ObserverManNPC] {name} has no PrefabBaselineAlignment — walking off its root.", this);
    }

    private void Start()
    {
        if (!HasPath)
        {
            Debug.LogWarning($"[ObserverManNPC] {name} has no travel path — give it one in the NPC Path " +
                             "Tool. Standing still.", this);
            enabled = false;
            return;
        }

        // Sets off from the first point, wherever it was left in the scene.
        PlaceAtPathStart();

        // Static: stays there, starting no animation of its own.
        if (PathStatic)
        {
            enabled = false;
            return;
        }

        _pathIndex = 0;
        Stand();
    }

    private void Update()
    {
        if (!HasPath) return;

        if (_mode == Mode.Standing)
        {
            // A point with Rot turns it to face that way first; the pause starts once it does.
            if (_rotTotal != 0f && !RotateToFacing()) return;
            _standTimer -= Time.deltaTime;
            if (_standTimer <= 0f) Walk();
            return;
        }

        WalkPath();
    }

    // Walks the feet along the route to the current path point — over the fitted ground on the
    // way, rising and falling with it — and stands once it gets there.
    private void WalkPath()
    {
        if (_route.Count == 0) _route.Add(Points[Mathf.Clamp(_pathIndex, 0, Points.Count - 1)]);

        float step = walkSpeed * Time.deltaTime;
        for (int guard = 0; step > 0f && guard < 64; guard++)
        {
            Vector3 feet     = FeetWorld();
            Vector3 toTarget = _route[_routeIndex] - feet;
            float   distance = toTarget.magnitude;

            if (distance > step)
            {
                TurnTowards(toTarget, feet);
                transform.position += toTarget / distance * step;
                return;
            }

            transform.position += toTarget;
            step -= distance;
            if (_routeIndex >= _route.Count - 1)
            {
                // A 0-second pause: straight on to the next point, still walking.
                if (Points.Count > 1 && PauseAt(_pathIndex) == 0f && !TryPathFacing(_pathIndex, out _))
                {
                    Walk();
                    continue;
                }
                Stand();
                return;
            }
            _routeIndex++;
        }
    }

    // The pause set on a path point, in seconds, or -1 when it has none.
    private float PauseAt(int i)
        => Pauses != null && i >= 0 && i < Pauses.Count && Pauses[i] != null && Pauses[i].pause
               ? Mathf.Max(0f, Pauses[i].seconds) : -1f;

    // The way from one path point to the next: the fitted ground between them (walked backwards
    // when it's heading back down the path), then the point itself.
    private void PlanRoute(int from, int to)
    {
        _route.Clear();
        _routeIndex = 0;
        int count = Points.Count;
        if (from >= 0 && from != to && GroundFitted)
        {
            if (to == (from + 1) % count)
            {
                _route.AddRange(LegGround(from));
            }
            else if (from == (to + 1) % count)
            {
                var leg = LegGround(to);
                for (int i = leg.Count - 1; i >= 0; i--) _route.Add(leg[i]);
            }
        }
        _route.Add(Points[to]);
    }

    // Turns towards a way round the feet, so the feet don't slide as it turns.
    private void TurnTowards(Vector3 wantWorld, Vector3 feet)
    {
        wantWorld.y = 0f;
        Vector3 faceWorld = FacingWorld();
        if (wantWorld.sqrMagnitude <= 1e-10f || faceWorld.sqrMagnitude <= 1e-10f) return;

        float angle = Vector3.SignedAngle(faceWorld, wantWorld, Vector3.up);
        float most  = turnSpeed * Time.deltaTime;
        transform.RotateAround(feet, Vector3.up, Mathf.Clamp(angle, -most, most));
    }

    private void Stand()
    {
        _mode = Mode.Standing;
        float pause = PauseAt(_pathIndex);
        _standTimer = pause >= 0f ? pause : Random.Range(standTimeMin, Mathf.Max(standTimeMin, standTimeMax));
        _animator.CrossFadeInFixedTime(StandingState, blendTime);

        // How far round the point's Rot is from the way it arrived facing — turned at Rot (s per 90°).
        _rotTime  = 0f;
        _rotDone  = 0f;
        _rotTotal = TryPathFacing(_pathIndex, out Vector3 face) && FacingWorld().sqrMagnitude > 1e-10f
                        ? Vector3.SignedAngle(FacingWorld(), face, Vector3.up) : 0f;
    }

    // Turns a share of the Rot each frame at Rot (s per 90°). True once it's round.
    private bool RotateToFacing()
    {
        _rotTime += Time.deltaTime;
        float time = rotSecondsPer90 * Mathf.Abs(_rotTotal) / 90f;
        float t    = time > 0f ? Mathf.Clamp01(_rotTime / time) : 1f;
        float want = _rotTotal * t;
        transform.RotateAround(FeetWorld(), Vector3.up, want - _rotDone);
        _rotDone = want;
        return t >= 1f;
    }

    private void Walk()
    {
        bool wasWalking = _mode == Mode.Walking;
        _mode = Mode.Walking;

        int count = Points.Count;
        int from  = _pathIndex;
        if (PathLoops || count == 1)
        {
            _pathIndex = (_pathIndex + 1) % count;
        }
        else
        {
            // Turns round at either end.
            if (_pathIndex + _pathStep < 0 || _pathIndex + _pathStep >= count) _pathStep = -_pathStep;
            _pathIndex = Mathf.Clamp(_pathIndex + _pathStep, 0, count - 1);
        }
        PlanRoute(from, _pathIndex);

        if (!wasWalking) _animator.CrossFadeInFixedTime(WalkingState, blendTime);
    }

    private Vector3 FacingWorld()
    {
        var     aligner = Aligner;
        Vector3 f = aligner != null ? aligner.transform.TransformDirection(aligner.LocalForward)
                                    : transform.forward;
        f.y = 0f;
        return f;
    }

    // Found here when asked before Awake — in the editor, out of play mode.
    private PrefabBaselineAlignment Aligner
        => _aligner != null ? _aligner : GetComponentInChildren<PrefabBaselineAlignment>(true);
}
