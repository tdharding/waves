using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Splines;

public class RiverSegmentID : MonoBehaviour
{
    public enum SegmentType { MainRiver, PrimaryBranch, SecondaryBranch, TertiaryBranch }

    [SerializeField] private string segmentID;
    [FormerlySerializedAs("isLeftPath")]
    [SerializeField] private bool isLeftPath;
    [FormerlySerializedAs("isRightPath")]
    [SerializeField] private bool isRightPath;
    [SerializeField] private SegmentType segmentType;
    [SerializeField] private string junctionGroup; // branches sharing this name fire together

    [Header("Arena Connection")]
    [Tooltip("True if one end of this segment terminates at an arena entrance portal.")]
    [SerializeField] private bool leadsToArena = false;
    [Tooltip("True = the arena portal is at t=1 (end knot). False = t=0 (start knot).")]
    [SerializeField] private bool arenaIsAtEnd = true;

    [Header("Extrude Gating")]
    [Tooltip("If true, this segment will not extrude when its trigger T is reached. " +
             "It waits until the player exits an arena (SplineRiverManager.NotifyLevelExited).")]
    [SerializeField] private bool extrudeOnExit = false;

    [Tooltip("If true, this segment extrudes from its END knot (t=1) backward. " +
             "Set automatically on the left-half of a T-junction bidirectional split.")]
    [SerializeField] private bool reverseExtrude = false;

    [Header("Registry")]
    [Tooltip("If true, this segment will not register in RiverSegmentRegistry. " +
             "Set on visual source segments — only baked boat-path highways should be registered.")]
    [SerializeField] private bool skipRegistration = false;

    public bool SkipRegistration => skipRegistration;

    public string SegmentID      => segmentID;
    public bool IsLeftPath        => isLeftPath;
    public bool IsRightPath     => isRightPath;
    public SegmentType Type      => segmentType;
    public int BranchDepth       => (int)segmentType;
    public string JunctionGroup  => junctionGroup;
    public bool LeadsToArena     => leadsToArena;
    public bool ArenaIsAtEnd     => arenaIsAtEnd;
    public bool ExtrudeOnExit    => extrudeOnExit;
    public bool ReverseExtrude   => reverseExtrude;

    private void OnEnable()
    {
        if (!skipRegistration)
            RiverSegmentRegistry.Instance?.Register(this);
    }

    private void OnDisable()
    {
        if (!skipRegistration)
            RiverSegmentRegistry.Instance?.Unregister(this);
    }

    public void SetSegmentID(string newID)
    {
        segmentID = newID;
    }

    public void CopyFrom(RiverSegmentID source)
    {
        isLeftPath      = source.isLeftPath;
        isRightPath   = source.isRightPath;
        segmentType    = source.segmentType;
        extrudeOnExit  = source.extrudeOnExit;
    }

    public void SetArenaConnection(bool leads, bool atEnd)
    {
        leadsToArena = leads;
        arenaIsAtEnd = atEnd;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!leadsToArena) return;

        var container = GetComponent<SplineContainer>();
        if (container == null || container.Spline == null || container.Spline.Count == 0) return;

        var spline = container.Spline;
        int knotIndex = arenaIsAtEnd ? spline.Count - 1 : 0;
        string label  = arenaIsAtEnd ? "ARENA END (t=1)" : "ARENA START (t=0)";

        Vector3 worldPos = container.transform.TransformPoint((Vector3)spline[knotIndex].Position);

        // Direction the spline is travelling at that knot
        Vector3 tangentLocal = arenaIsAtEnd
            ? (Vector3)spline[knotIndex].TangentOut
            : -(Vector3)spline[knotIndex].TangentIn;
        Vector3 tangentWorld = container.transform.TransformVector(tangentLocal).normalized;

        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(worldPos, 0.4f);
        Gizmos.DrawRay(worldPos, tangentWorld * 3f);

        UnityEditor.Handles.color = Color.magenta;
        UnityEditor.Handles.Label(worldPos + Vector3.up * 1f, label);
    }
#endif
}