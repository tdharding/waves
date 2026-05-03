using UnityEngine;
using UnityEngine.Splines;
using System.Collections;
using Unity.Mathematics;

[System.Serializable]
public class JunctionSide
{
    public string segmentID;
    [HideInInspector] public SplineContainer segment; // Found at runtime
    
    public bool useNearestPoint = false; 
    public bool entryFromStart = true;
    public bool autoReturn = false;
}

public class SplineRiverJunctionNodeV2 : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LevelSelectBoatControl boatControl;

    [Header("Junction Sides")]
    [SerializeField] private JunctionSide sideA;
    [SerializeField] private JunctionSide sideB;

    [Header("Transition")]
    [SerializeField] private float transitionDuration = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;


    private LevelSelectBoatControl _boat;
    private JunctionSide _activeSide;
    private bool  _inZone;
    private bool  _transitioning;
    private float _reEntryCooldown;

    // ─────────────────────────────────────────────
    // INITIALIZATION
    // ─────────────────────────────────────────────

    public void SetBoatControl(LevelSelectBoatControl bc) => boatControl = bc;

    private void Start()
    {
        // Link the IDs to the actual baked SplineContainers via the Registry
        ResolveSegmentReferences();
    }

    private void ResolveSegmentReferences()
    {
        if (RiverSegmentRegistry.Instance == null)
        {
            Debug.LogError($"[Junction {name}] RiverSegmentRegistry not found.");
            return;
        }

        var segmentAID = RiverSegmentRegistry.Instance.GetSegment(sideA.segmentID);
        var segmentBID = RiverSegmentRegistry.Instance.GetSegment(sideB.segmentID);

        if (segmentAID != null) sideA.segment = segmentAID.GetComponent<SplineContainer>();
        if (segmentBID != null) sideB.segment = segmentBID.GetComponent<SplineContainer>();

        if (debugLog)
            Debug.Log($"[Junction {name}] Resolved — " +
                      $"sideA '{sideA.segmentID}' {(sideA.segment != null ? "OK" : "NULL")} isLeft={SideIsLeft(sideA)} | " +
                      $"sideB '{sideB.segmentID}' {(sideB.segment != null ? "OK" : "NULL")} isLeft={SideIsLeft(sideB)} isRight={SideIsRight(sideB)}");

        if (sideA.segment == null) Debug.LogWarning($"[Junction {name}] sideA '{sideA.segmentID}' not in registry.");
        if (sideB.segment == null) Debug.LogWarning($"[Junction {name}] sideB '{sideB.segmentID}' not in registry.");
    }

    // Called by the designer after BakePaths — assigns sideA/sideB IDs from the two
    // baked highways whose splines pass closest to this junction's world position.
    public void AssignSegmentIDsFromBaked(System.Collections.Generic.IEnumerable<RiverSegmentID> bakedSegments)
    {
        Vector3 juncPos = transform.position;
        var ranked = new System.Collections.Generic.List<(float dist, RiverSegmentID seg)>();

        foreach (var seg in bakedSegments)
        {
            var container = seg.GetComponent<SplineContainer>();
            if (container == null || container.Spline == null) continue;

            Vector3 localPos = container.transform.InverseTransformPoint(juncPos);
            SplineUtility.GetNearestPoint(container.Spline,
                (Unity.Mathematics.float3)localPos, out _, out float t);
            Vector3 nearest = container.transform.TransformPoint(
                (Vector3)container.Spline.EvaluatePosition(t));
            ranked.Add((Vector3.Distance(juncPos, nearest), seg));
        }

        ranked.Sort((a, b) => a.dist.CompareTo(b.dist));

        if (ranked.Count < 2) return;

        // From the 4 closest candidates, pick by branch depth:
        // sideA = shallowest (parent / main river), sideB = deepest (child branch).
        // Works for any nesting level: main→primary, primary→secondary, secondary→tertiary.
        var pool = ranked.GetRange(0, Mathf.Min(ranked.Count, 4));
        pool.Sort((a, b) => a.seg.BranchDepth.CompareTo(b.seg.BranchDepth));

        RiverSegmentID pickedA = pool[0].seg;
        RiverSegmentID pickedB = pool[pool.Count - 1].seg;

        // If depths are equal fall back to the two closest by distance
        if (pickedA == pickedB)
        {
            pickedA = ranked[0].seg;
            pickedB = ranked[1].seg;
        }

        sideA.segmentID = pickedA.SegmentID;
        sideB.segmentID = pickedB.SegmentID;
        Debug.Log($"[Junction {name}] sideA → '{sideA.segmentID}' (depth {pickedA.BranchDepth}) | " +
                  $"sideB → '{sideB.segmentID}' (depth {pickedB.BranchDepth})");
    }

    // ─────────────────────────────────────────────
    // TRIGGER ZONE
    // ─────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("BoatPrefab")) return;
        if (_transitioning || Time.time < _reEntryCooldown) return;

        // Ensure we have the boat reference
        _boat = boatControl;
        if (_boat == null) return;

        // Safety check: if segments didn't resolve, try one last time
        if (sideA.segment == null || sideB.segment == null) ResolveSegmentReferences();

        _inZone = true;

        var current = _boat.GetCurrentContainer();

        if (current == sideA.segment)      _activeSide = sideB;
        else if (current == sideB.segment) _activeSide = sideA;
        else                               _activeSide = null;

        if (debugLog)
            Debug.Log($"[Junction {name}] Entered — boat on '{current?.name}' | " +
                      $"sideA='{sideA.segmentID}'({(sideA.segment != null ? "OK" : "NULL")}) " +
                      $"sideB='{sideB.segmentID}'({(sideB.segment != null ? "OK" : "NULL")}) | " +
                      $"isLeft(A)={SideIsLeft(sideA)} isLeft(B)={SideIsLeft(sideB)} " +
                      $"isRight(A)={SideIsRight(sideA)} isRight(B)={SideIsRight(sideB)}");
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("BoatPrefab")) return;

        _inZone = false;
        _activeSide = null;
        JunctionPromptUI.Instance?.Hide();
    }

    // ─────────────────────────────────────────────
    // UPDATE — LISTEN FOR SWITCH INPUT
    // ─────────────────────────────────────────────

    private void Update()
    {
        if (!_inZone || _boat == null || _transitioning) return;

        // Auto-return (e.g. coming back from a branch to the main river)
        if (_activeSide != null && _activeSide.autoReturn)
        {
            float startT = GetTargetT(_activeSide);
            StartCoroutine(TransitionToSegment(_activeSide, startT, GetSplineRotation(_activeSide.segment, startT)));
            return;
        }

        // Update prompt arrows to match which key the player should press
        UpdatePrompt();

        if (_activeSide == null || _activeSide.segment == null) return;

        bool destIsLeft = IsDestinationOnLeft(_activeSide);
        bool wantsDestination = (destIsLeft && _boat.RawWantsLeft) || (!destIsLeft && _boat.RawWantsRight);

        if (wantsDestination)
        {
            if (debugLog) Debug.Log($"[Junction {name}] Transitioning to '{_activeSide.segmentID}'");
            float startT = GetTargetT(_activeSide);
            StartCoroutine(TransitionToSegment(_activeSide, startT, GetSplineRotation(_activeSide.segment, startT)));
        }
    }

    private void UpdatePrompt()
    {
        if (_activeSide == null || _activeSide.segment == null)
        {
            JunctionPromptUI.Instance?.Hide();
            return;
        }

        bool destIsLeft = IsDestinationOnLeft(_activeSide);
        JunctionPromptUI.Instance?.Show(destIsLeft, !destIsLeft);
    }

    // Returns true if the destination segment's entry point is to the LEFT of the boat's
    // current travel direction (accounting for reverse).
    private bool IsDestinationOnLeft(JunctionSide destination)
    {
        if (_boat == null || destination.segment == null) return false;

        Vector3 boatForward = _boat.BoatTransform.forward;
        if (_boat.IsReversed) boatForward = -boatForward;
        boatForward.y = 0f;

        float destT = destination.entryFromStart ? 0f : 1f;
        Vector3 destPos  = GetSplineWorldPosition(destination.segment, destT);
        Vector3 toBranch = destPos - transform.position;
        toBranch.y = 0f;

        // XZ cross product: positive = destPos is to the LEFT of boatForward
        float cross = boatForward.x * toBranch.z - boatForward.z * toBranch.x;
        return cross > 0f;
    }

    private JunctionSide SideWithFlag(bool isLeft)
    {
        bool MatchA = isLeft ? SideIsLeft(sideA)  : SideIsRight(sideA);
        bool MatchB = isLeft ? SideIsLeft(sideB)   : SideIsRight(sideB);
        if (MatchA) return sideA;
        if (MatchB) return sideB;
        return null;
    }

    private bool SideIsLeft(JunctionSide side)
        => side?.segment?.GetComponent<RiverSegmentID>()?.IsLeftPath ?? false;

    private bool SideIsRight(JunctionSide side)
        => side?.segment?.GetComponent<RiverSegmentID>()?.IsRightPath ?? false;

    // ─────────────────────────────────────────────
    // TRANSITION COROUTINE
    // ─────────────────────────────────────────────

    private IEnumerator TransitionToSegment(JunctionSide target, float targetT, Quaternion targetRot)
    {
        _transitioning = true;
        _inZone = false;
        _boat.HandOffToJunction();
        JunctionPromptUI.Instance?.Hide();

        Vector3 startPos = _boat.BoatTransform.position;
        Quaternion startRot = _boat.BoatTransform.rotation;
        Vector3 endPos = GetSplineWorldPosition(target.segment, targetT);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / transitionDuration;
            float smooth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            _boat.BoatTransform.position = Vector3.Lerp(startPos, endPos, smooth);
            _boat.BoatTransform.rotation = Quaternion.Slerp(startRot, targetRot, smooth);
            yield return null;
        }

        var segID = target.segment.GetComponent<RiverSegmentID>();
        bool isTop    = segID != null && segID.IsLeftPath;
        bool isBottom = segID != null && segID.IsRightPath;

        _boat.AttachToSegment(target.segment, targetT, isTop, isBottom);
        _boat.ResumeMovement();
        _reEntryCooldown = Time.time + 1f;
        _transitioning = false;

        if (target.autoReturn)
        {
            _inZone = true;
            var current = _boat.GetCurrentContainer();
            if (current == sideA.segment)       _activeSide = sideB;
            else if (current == sideB.segment)  _activeSide = sideA;
            else                                _activeSide = null;

        }
    }

    // ─────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────

    private float GetTargetT(JunctionSide side)
    {
        if (side.useNearestPoint)
            return GetNearestT(side.segment);

        return side.entryFromStart ? 0f : 1f;
    }

    private float GetNearestT(SplineContainer container)
    {
        SplineUtility.GetNearestPoint(
            container.Spline,
            container.transform.InverseTransformPoint(_boat.BoatTransform.position),
            out _,
            out float t
        );
        return Mathf.Clamp01(t);
    }

    private Vector3 GetSplineWorldPosition(SplineContainer container, float t)
    {
        float3 localPos = container.Spline.EvaluatePosition(t);
        return container.transform.TransformPoint(localPos);
    }

    private Quaternion GetSplineRotation(SplineContainer container, float t)
    {
        float3 tangent = container.Spline.EvaluateTangent(t);
        Vector3 worldTangent = container.transform.TransformDirection(tangent);
        if (worldTangent == Vector3.zero) return Quaternion.identity;
        return Quaternion.LookRotation(worldTangent.normalized, Vector3.up);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        DrawSideGizmo(sideA, new Color(0f, 1f, 1f, 1f));   // cyan
        DrawSideGizmo(sideB, new Color(1f, 0.5f, 0f, 1f)); // orange
    }

    private void DrawSideGizmo(JunctionSide side, Color colour)
    {
        if (side == null || string.IsNullOrEmpty(side.segmentID)) return;

        // Prefer the already-resolved container; fall back to a scene search
        SplineContainer container = side.segment;
        if (container == null)
        {
            foreach (var rid in FindObjectsOfType<RiverSegmentID>())
            {
                if (rid.SegmentID == side.segmentID)
                {
                    container = rid.GetComponent<SplineContainer>();
                    break;
                }
            }
        }

        if (container == null || container.Spline == null) return;

        const int steps = 64;
        var spline = container.Spline;
        var tf = container.transform;

        Vector3 prev = tf.TransformPoint(spline.EvaluatePosition(0f));
        for (int i = 1; i <= steps; i++)
        {
            Vector3 next = tf.TransformPoint(spline.EvaluatePosition(i / (float)steps));
            UnityEditor.Handles.color = colour;
            UnityEditor.Handles.DrawLine(prev, next, 3f);
            prev = next;
        }

        // Label at midpoint
        Vector3 mid = tf.TransformPoint(spline.EvaluatePosition(0.5f));
        UnityEditor.Handles.color = colour;
        UnityEditor.Handles.Label(mid + Vector3.up * 1f, side.segmentID);
    }
#endif
}