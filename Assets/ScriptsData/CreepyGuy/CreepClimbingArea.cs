using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// A rock the creepy guy can climb. This holds the SHAPE — where he can stand on this particular
// rock — while CreepyGuyController holds the behaviour. That split is what lets him move between
// rocks: he arrives, reads this rock's rings, and fits it without knowing anything about it.
//
// The three rings are authored in THIS object's local space and converted to world on demand, so
// a scaled placement gets proportionally scaled rings while the creepy guy himself stays a fixed
// size (he is never parented to the rock). Reading the transform live also means the post-spawn
// Y180 that LevelSpawner applies to spawnParent is picked up automatically.
[DisallowMultipleComponent]
public class CreepClimbingArea : MonoBehaviour
{
    [Header("Occupancy")]
    [Tooltip("How many creepers can be on this rock at once. One rock, one creeper — raise it only " +
             "if a rock is big enough that two would not read as overlapping.")]
    [SerializeField] int maxCreepers = 1;

    [Header("Centre")]
    [Tooltip("The rock's vertical axis, offset from this object. All three rings are centred on it " +
             "and the boat's distance is measured to it. The spike mesh is usually NOT at the object's " +
             "origin, so X/Z will normally be non-zero. Y shifts all three rings together; the " +
             "per-ring heights below are measured from it.")]
    [SerializeField] Vector3 centreOffset;

    [Header("Lower Ring — wide, low on the rock")]
    [SerializeField] float lowerRingRadius = 0.31f;
    [SerializeField] float lowerRingHeight = -1.41f;

    [Header("Upper Ring — tight, high on the rock. He leaps from here")]
    [SerializeField] float upperRingRadius = 0.09f;
    [SerializeField] float upperRingHeight = -1f;

    [Header("Spawn Ring — widest, at the base, below the water")]
    [Tooltip("Where he crawls up from. Sit it under the waterline so he surfaces rather than fading in.")]
    [SerializeField] float spawnRingRadius = 0.35f;
    [SerializeField] float spawnRingHeight = -1.9f;

    // Every climbable rock in the level. LevelSpawner builds the whole maze in one pass before the
    // boat moves, so this is complete by the time anything asks. Domain-reload-safe, matching
    // StreetLightController — with "Reload Domain" off the list would otherwise carry over.
    public static readonly List<CreepClimbingArea> All = new List<CreepClimbingArea>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    void OnEnable()  { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // ── Occupancy ──
    // Claimed the moment a creeper commits to coming here — at the START of a hop, not on arrival —
    // so two creepers cannot pick the same rock mid-flight and land on top of each other.

    readonly List<CreepyGuyController> _occupants = new List<CreepyGuyController>();

    /// <summary>True when another creeper could still take a place here.</summary>
    public bool HasRoom => _occupants.Count < Mathf.Max(1, maxCreepers);

    /// <summary>Room here, or already his — the test for whether he may move here.</summary>
    public bool HasRoomFor(CreepyGuyController creeper) => HasRoom || _occupants.Contains(creeper);

    public void Claim(CreepyGuyController creeper)
    {
        if (creeper != null && !_occupants.Contains(creeper)) _occupants.Add(creeper);
    }

    public void Release(CreepyGuyController creeper) => _occupants.Remove(creeper);

    /// <summary>
    /// Fits the three rings to a rock whose shape is generated rather than modelled — the
    /// procedural spikes drawn in the Grid Designer call this at spawn so the creepy guy sits
    /// on the surface the designer authored, without anyone dialling rings in by hand.
    /// Hand-authored rocks like BigSpike never call it and keep their Inspector values.
    /// </summary>
    public void Configure(Vector3 centre,
                          float spawnRadius, float spawnHeight,
                          float lowerRadius, float lowerHeight,
                          float upperRadius, float upperHeight)
    {
        centreOffset    = centre;
        spawnRingRadius = Mathf.Max(0f, spawnRadius);
        spawnRingHeight = spawnHeight;
        lowerRingRadius = Mathf.Max(0f, lowerRadius);
        lowerRingHeight = lowerHeight;
        upperRingRadius = Mathf.Max(0f, upperRadius);
        upperRingHeight = upperHeight;
    }

    public float LowerRingRadius => lowerRingRadius;
    public float LowerRingHeight => lowerRingHeight;
    public float UpperRingRadius => upperRingRadius;
    public float UpperRingHeight => upperRingHeight;
    public float SpawnRingRadius => spawnRingRadius;
    public float SpawnRingHeight => spawnRingHeight;

    // The rock's vertical axis in world space — what boat distance is measured against.
    public Vector3 CentreWorld => transform.TransformPoint(centreOffset);

    // Centre in this object's own local space, which is the space the rings live in.
    Vector3 CentreLocal => centreOffset;

    /// <summary>
    /// Bearing of a world position around this rock, in degrees. Atan2(x, z) puts 0° at local +Z
    /// increasing clockwise — Unity's yaw convention — so the result doubles as a rotation.
    /// </summary>
    public float BearingOf(Vector3 worldPos)
    {
        Vector3 c = CentreLocal;
        Vector3 p = transform.InverseTransformPoint(worldPos);
        return Mathf.Atan2(p.x - c.x, p.z - c.z) * Mathf.Rad2Deg;
    }

    /// <summary>World position at the given bearing on a ring of this radius and height.</summary>
    public Vector3 RingPointToWorld(float bearingDegrees, float radius, float height)
    {
        Vector3 c   = CentreLocal;
        float   rad = bearingDegrees * Mathf.Deg2Rad;
        return transform.TransformPoint(new Vector3(c.x + Mathf.Sin(rad) * radius,
                                                    c.y + height,
                                                    c.z + Mathf.Cos(rad) * radius));
    }

    /// <summary>World rotation for a yaw expressed in this rock's local space.</summary>
    public Quaternion RingRotation(float yawDegrees) => transform.rotation * Quaternion.Euler(0f, yawDegrees, 0f);

    void OnValidate()
    {
        lowerRingRadius = Mathf.Max(0f, lowerRingRadius);
        upperRingRadius = Mathf.Max(0f, upperRingRadius);
        spawnRingRadius = Mathf.Max(0f, spawnRingRadius);
    }

#if UNITY_EDITOR
    // The rings belong to the rock. Drawn always, not only when selected, so several rocks can be
    // compared at once while laying them out. The creature's own gizmos (activation, jump band,
    // climb thresholds) stay on CreepyGuyController.
    void OnDrawGizmos()
    {
        Vector3 c = CentreLocal;

        // The axis itself, so the centre offset can be dialled in by eye against the rock.
        Handles.color = new Color(1f, 1f, 1f, 0.8f);
        Handles.DrawLine(transform.TransformPoint(c + Vector3.up * (spawnRingHeight - 0.2f)),
                         transform.TransformPoint(c + Vector3.up * (upperRingHeight + 0.2f)));

        Vector3 spawn = transform.TransformPoint(c + Vector3.up * spawnRingHeight);
        Vector3 low   = transform.TransformPoint(c + Vector3.up * lowerRingHeight);
        Vector3 high  = transform.TransformPoint(c + Vector3.up * upperRingHeight);

        Handles.color = new Color(0.4f, 1f, 0.4f, 0.9f);
        Handles.DrawWireDisc(spawn, transform.up, spawnRingRadius);
        Handles.Label(spawn + transform.right * spawnRingRadius, $"spawn r={spawnRingRadius:0.###} (crawls up from here)");

        Handles.color = new Color(0.2f, 0.9f, 1f, 0.9f);
        Handles.DrawWireDisc(low, transform.up, lowerRingRadius);
        Handles.Label(low + transform.right * lowerRingRadius, $"lower r={lowerRingRadius:0.###}");

        Handles.color = new Color(1f, 0.5f, 1f, 0.95f);
        Handles.DrawWireDisc(high, transform.up, upperRingRadius);
        Handles.Label(high + transform.right * upperRingRadius, $"upper r={upperRingRadius:0.###} (leaps from here)");

        // Struts, so the climb path reads as the cone surface it should follow.
        Handles.color = new Color(0.6f, 0.7f, 1f, 0.4f);
        for (int i = 0; i < 8; i++)
        {
            float a = i * (360f / 8f) * Mathf.Deg2Rad;
            Vector3 dir = transform.TransformDirection(new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)));
            Handles.DrawLine(spawn + dir * spawnRingRadius, low  + dir * lowerRingRadius);
            Handles.DrawLine(low   + dir * lowerRingRadius, high + dir * upperRingRadius);
        }
    }
#endif
}
