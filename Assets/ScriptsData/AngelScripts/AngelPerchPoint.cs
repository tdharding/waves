using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// The tip of a rock the angel can land on. ProceduralSpike adds one of these at spawn to any
/// spike the Grid Designer marked as an angel perch point, and hands it the tip height it has
/// just built the mesh to — so she lands on the rock the designer drew rather than on a
/// separately dialled-in position.
///
/// The perch is held as an offset in THIS object's local space and converted to world on demand,
/// so a scaled placement gets a proportionally higher tip while the angel herself stays a fixed
/// size, and LevelSpawner's post-spawn Y180 is picked up automatically.
/// </summary>
[DisallowMultipleComponent]
public class AngelPerchPoint : MonoBehaviour
{
    [Tooltip("The tip, offset from this object. The spike mesh puts its waterline at the origin, " +
             "so this is normally straight up the rock's own axis.")]
    [SerializeField] Vector3 tipOffset;

    [Tooltip("Boat inside this and she comes down here; boat back outside it and she leaves.")]
    [SerializeField] float perchRadius = 12f;

    [Tooltip("Boat inside this, with her perched here, and the talk key becomes live.")]
    [SerializeField] float talkRadius = 4f;

    [Tooltip("Always come down the moment the boat enters, rather than only when she happens to be " +
             "looking for somewhere to land.")]
    [SerializeField] bool priority;

    [Tooltip("Radius of the curve she lands along. 0 = straight at the rock.")]
    [SerializeField] float landingCurveSize = 2f;

    [Tooltip("Talk feature armed on this perch: inside the talk range, the talk key opens the talk " +
             "camera + dialogue. Off = she just perches here.")]
    [SerializeField] bool talkEnabled;

    [Tooltip("What she says when talked to here. Only shown when Talk is enabled.")]
    [TextArea(1, 4)]
    [SerializeField] string talkText = "";

    [Tooltip("She starts the scene stood here, rather than flying. She holds it however far off " +
             "the boat is, and only joins the flight once the boat has been inside the perch " +
             "radius once — so the opening is the same wherever the boat happens to start.")]
    [SerializeField] bool startPerch;

    [Tooltip("What the prompt reads where a scene prompts for its interactions, such as the level " +
             "select map. The key is added by the prompt itself, so this is just the doing.")]
    [SerializeField] string talkPrompt = "Talk";

    [Tooltip("How far her talk camera sits from her while she talks here, along its own view. " +
             "0 = the distance authored on the angel prefab's camera.")]
    [SerializeField] float talkCameraDistance;

    // Every perch in the level. LevelSpawner builds the whole maze in one pass before the boat
    // moves, so this is complete by the time the angel asks. Domain-reload-safe, matching
    // CreepClimbingArea — with "Reload Domain" off the list would otherwise carry over.
    public static readonly List<AngelPerchPoint> All = new List<AngelPerchPoint>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    void OnEnable()  { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    /// <summary>Called by ProceduralSpike once the mesh is built. Tip is in this object's local space.</summary>
    public void Configure(Vector3 tipLocal, float perch, float talk, bool isPriority,
                          bool canTalk, string say, float curveSize,
                          bool isStart = false, string prompt = null, float cameraDistance = 0f)
    {
        talkCameraDistance = Mathf.Max(0f, cameraDistance);
        startPerch  = isStart;
        if (prompt != null) talkPrompt = prompt;
        landingCurveSize = Mathf.Max(0f, curveSize);
        tipOffset   = tipLocal;
        perchRadius = Mathf.Max(0f, perch);
        // Kept inside the perch radius: a talk range reaching further than the range that brought
        // her here would arm the key for a boat she is already leaving.
        talkRadius  = Mathf.Clamp(talk, 0f, perchRadius);
        priority    = isPriority;
        talkEnabled = canTalk;
        talkText    = say ?? "";
        _talkLines  = SplitTalkLines(talkText);
    }

    /// <summary>
    /// One authored string becomes the run of lines she says here, split on "/" so a whole
    /// conversation can be typed into a single Grid Designer field. Split once at spawn rather
    /// than every time she is spoken to.
    ///
    /// Blank pieces are dropped, so a trailing slash or a double slash is a typo, not an empty
    /// beat sitting on screen with nothing in it.
    /// </summary>
    static string[] SplitTalkLines(string say)
    {
        if (string.IsNullOrWhiteSpace(say)) return System.Array.Empty<string>();

        var pieces = say.Split('/');
        var kept   = new List<string>(pieces.Length);
        foreach (var piece in pieces)
        {
            string line = piece.Trim();
            if (line.Length > 0) kept.Add(line);
        }
        return kept.ToArray();
    }

    /// <summary>Where her feet go — the point of the rock, in world space.</summary>
    public Vector3 PerchWorld => transform.TransformPoint(tipOffset);

    /// <summary>Boat inside this and she comes down here; outside it and she leaves.</summary>
    public float PerchRadius => perchRadius;

    /// <summary>Boat inside this, with her here, and the talk key is live.</summary>
    public float TalkRadius => talkRadius;

    /// <summary>She always comes down here on entry, rather than only when she is looking to land.</summary>
    public bool IsPriority => priority;

    /// <summary>Radius of the curve she lands along here. 0 = straight in.</summary>
    public float LandingCurveSize => landingCurveSize;

    /// <summary>Talk armed on this perch — otherwise the talk key does nothing here.</summary>
    public bool TalkEnabled => talkEnabled;

    /// <summary>What she says when talked to here, as authored — slashes and all.</summary>
    public string TalkText => talkText;

    /// <summary>She opens the scene stood here. Only one perch in a scene should say yes.</summary>
    public bool IsStartPerch => startPerch;

    /// <summary>What a scene's own prompt reads for talking to her here.</summary>
    public string TalkPrompt => talkPrompt;

    /// <summary>Her talk camera's distance while she talks here. 0 = the prefab's own.</summary>
    public float TalkCameraDistance => talkCameraDistance;

    /// <summary>
    /// The perch a scene opens on, or none. The first one found wins: two start perches is an
    /// authoring mistake, not an arrangement worth honouring, and picking one quietly beats
    /// having her flicker between them.
    /// </summary>
    public static AngelPerchPoint FindStartPerch()
    {
        for (int i = 0; i < All.Count; i++)
        {
            var p = All[i];
            if (p != null && p.isActiveAndEnabled && p.startPerch) return p;
        }
        return null;
    }

    /// <summary>
    /// What she says, one line at a time, already split on "/". Empty when there is nothing to say.
    /// Rebuilt on demand in the editor so a line typed into the Inspector shows without a respawn.
    /// </summary>
    public string[] TalkLines
    {
        get
        {
            if (_talkLines == null || (_talkLines.Length == 0 && !string.IsNullOrWhiteSpace(talkText)))
                _talkLines = SplitTalkLines(talkText);
            return _talkLines;
        }
    }

    string[] _talkLines;

    /// <summary>Distance from a world position to this rock, measured flat — height is not the point.</summary>
    public float FlatDistanceTo(Vector3 worldPos)
    {
        Vector3 d = PerchWorld - worldPos;
        d.y = 0f;
        return d.magnitude;
    }

#if UNITY_EDITOR
    // Drawn always, not only when selected, so a run of perches can be read at a glance while
    // laying a level out.
    void OnDrawGizmos()
    {
        Vector3 tip = PerchWorld;

        // Exactly where she stands: the tip plus the angel's own foot offset, the same sum
        // AngelCompanion lands her on. Kept a fixed size on screen and drawn through whatever it
        // sits on, so it reads as a clear dot at any zoom rather than half-buried in the rock.
        if (_gizmoAngel == null) _gizmoAngel = FindFirstObjectByType<AngelCompanion>();
        Vector3 feet = tip + Vector3.up * (_gizmoAngel != null ? _gizmoAngel.PerchFootOffset : 0f);

        var dotColor = priority ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(1f, 0.95f, 0.6f, 1f);
        var zTest    = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = dotColor;
        Handles.SphereHandleCap(0, feet, Quaternion.identity,
                                HandleUtility.GetHandleSize(feet) * (startPerch ? 0.12f : 0.08f),
                                EventType.Repaint);
        Handles.zTest = zTest;

        Gizmos.color = dotColor;
        Gizmos.DrawLine(feet, feet + Vector3.up * 0.25f);
        if (startPerch) Handles.Label(feet + Vector3.up * 0.28f, "start");

        // The two ranges, drawn at the waterline where the boat actually crosses them rather than
        // up at the tip. Same convention as CreepClimbingArea's rings.
        Vector3 atWater = new Vector3(tip.x, transform.position.y, tip.z);

        Handles.color = new Color(1f, 0.93f, 0.55f, 0.55f);
        Handles.DrawWireDisc(atWater, Vector3.up, perchRadius);
        Handles.Label(atWater + Vector3.right * perchRadius,
                      priority ? $"perch r={perchRadius:0.#} (priority)" : $"perch r={perchRadius:0.#} (watching)");

        if (talkEnabled)
        {
            Handles.color = new Color(0.5f, 0.9f, 1f, 0.75f);
            Handles.DrawWireDisc(atWater, Vector3.up, talkRadius);
            Handles.Label(atWater + Vector3.right * talkRadius, $"talk r={talkRadius:0.#}");
        }
    }

    // Looked up once and held, rather than searched for by every perch on every redraw.
    static AngelCompanion _gizmoAngel;
#endif
}
