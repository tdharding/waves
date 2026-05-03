using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

public class SplineRiverManager : MonoBehaviour
{
    public static SplineRiverManager Instance;

    [Header("Target Setup")]
    [SerializeField] private SplineContainer _mainContainer;
    [SerializeField] private SplineExtrude _mainExtrude;

    [Header("Branch Template")]
    [SerializeField] private GameObject _branchPrefab;

    [Header("Source Segments")]
    [SerializeField] private List<SplineContainer> _sourceSegments = new();

    [Header("Settings")]
    [SerializeField] private float _extrudeSpeed = 1f;
    [SerializeField] private float _overshoot    = 0.05f;
    [SerializeField] private float _branchOvershoot = 0.5f;

    [Header("Barriers")]
    [SerializeField] private GameObject _barrierPrefab; // Sphere collider tagged "LevelSelectPathObstacle"
    [SerializeField] private float _barrierCompleteThreshold = 0.99f; // Disable barrier above this T

    private float _mainCurrentT = 0f;

    // Expose for external systems (e.g. LevelSelectBoatControl if needed)
    public float MainCurrentT => _mainCurrentT;

    private List<BranchTracker> _branches = new();

    // Barrier GameObjects — one per spline (main + each branch)
    private GameObject _mainBarrier;
    private List<GameObject> _branchBarriers = new();

    // ─────────────────────────────────────────────────────────────
    // BranchTracker
    // ─────────────────────────────────────────────────────────────
    private class BranchTracker
    {
        public SplineExtrude   Extruder;
        public SplineContainer Container;
        public float           TriggerT;
        public BranchTracker   Parent;
        public float           CurrentT;
        public bool            IsStarted;
        public string          JunctionGroup;
        public int             Depth;
        public float           NormalizedSpeed;
        // T on the branch spline corresponding to the junction point (skip backward overshoot section)
        public float           BranchStartT;
        // World-space junction position, used post-stitch to recompute TriggerT as parametric T
        public Vector3         JunctionWorldPos;

        // ExtrudeOnExit gating
        public bool   ExtrudeOnExit;  // copied from source RiverSegmentID
        public bool   ExitUnlocked;   // set by NotifyLevelExited or restored from save
        public string SegmentID;      // stable ID used for persistence
        public bool   ArenaIsAtEnd;   // true → arena at t=1, extrude backwards (1-t → 1)
    }

    // ─────────────────────────────────────────────────────────────
    public void SetupContainers(SplineContainer mainContainer, SplineExtrude mainExtrude, GameObject barrierPrefab)
    {
        _mainContainer = mainContainer;
        _mainExtrude   = mainExtrude;
        if (barrierPrefab != null) _barrierPrefab = barrierPrefab;
    }

    // Lifecycle
    // ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        Instance = this;
        Stitch();
        SpawnBarriers();

        _mainCurrentT = GameProgressData.GetRiverProgress(0f);
        ApplyMainExtrude(_mainCurrentT);
        RestoreExitUnlocked();   // must run before UpdateBranchStates so saved unlocks are respected
        UpdateBranchStates(true);

        // Sync barrier positions to saved state immediately
        UpdateBarriers();
    }

    // ─────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────
    public void AdvanceToPosition(Vector3 worldPosition)
    {
        float t = WorldPositionToT(worldPosition);
        AdvanceToT(t + _overshoot);
    }

    public void AdvanceToT(float targetT)
    {
        float target = Mathf.Clamp01(targetT);
        if (target > _mainCurrentT)
            StartCoroutine(AnimateMainExtrude(target));
    }

    // ─────────────────────────────────────────────────────────────
    // Barriers — Spawn
    // ─────────────────────────────────────────────────────────────
    private void SpawnBarriers()
    {
        if (_barrierPrefab == null)
        {
            Debug.LogWarning("SplineRiverManager: No barrier prefab assigned — barriers will not be created.");
            return;
        }

        // Main river barrier
        _mainBarrier = Instantiate(_barrierPrefab, transform);
        _mainBarrier.name = "Barrier_MainRiver";

        // One barrier per branch tracker
        _branchBarriers.Clear();
        for (int i = 0; i < _branches.Count; i++)
        {
            var barrier = Instantiate(_barrierPrefab, transform);
            barrier.name = $"Barrier_Branch_{i}_{_branches[i].JunctionGroup}";
            _branchBarriers.Add(barrier);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Barriers — Update Positions
    // ─────────────────────────────────────────────────────────────
    private void UpdateBarriers()
    {
        // — Main river barrier —
        if (_mainBarrier != null)
        {
            var col = _mainBarrier.GetComponent<Collider>();

            if (_mainCurrentT >= _barrierCompleteThreshold)
            {
                // River fully extruded — disable barrier so the end of the river is accessible
                if (col != null) col.enabled = false;
            }
            else
            {
                if (col != null) col.enabled = true;
                Vector3 localPos = _mainContainer.Spline.EvaluatePosition(_mainCurrentT);
                _mainBarrier.transform.position = _mainContainer.transform.TransformPoint(localPos);
            }
        }

        // — Branch barriers —
        for (int i = 0; i < _branches.Count; i++)
        {
            // Guard against barrier list getting out of sync with branch list
            if (i >= _branchBarriers.Count) break;

            var branch  = _branches[i];
            var barrier = _branchBarriers[i];
            if (barrier == null) continue;

            var col = barrier.GetComponent<Collider>();

            if (!branch.IsStarted)
            {
                // Branch hasn't started extruding yet — park barrier at spline start and disable
                if (col != null) col.enabled = false;
                Vector3 localPos = branch.Container.Spline.EvaluatePosition(0f);
                barrier.transform.position = branch.Container.transform.TransformPoint(localPos);
            }
            else if (branch.CurrentT >= _barrierCompleteThreshold)
            {
                // Branch fully extruded — disable barrier
                if (col != null) col.enabled = false;
            }
            else
            {
                if (col != null) col.enabled = true;
                // For reversed extrusion (ArenaIsAtEnd), the visible front is at 1-currentT
                float barrierT = (branch.ExtrudeOnExit && branch.ArenaIsAtEnd)
                    ? 1f - branch.CurrentT
                    : branch.CurrentT;
                Vector3 localPos = branch.Container.Spline.EvaluatePosition(barrierT);
                barrier.transform.position = branch.Container.transform.TransformPoint(localPos);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Animation
    // ─────────────────────────────────────────────────────────────
private IEnumerator AnimateMainExtrude(float target)
{
    float totalLength     = _mainContainer.Spline.GetLength();
    float normalizedSpeed = totalLength > 0 ? _extrudeSpeed / totalLength : _extrudeSpeed;

    while (_mainCurrentT < target)
    {
        _mainCurrentT = Mathf.MoveTowards(_mainCurrentT, target, Time.deltaTime * normalizedSpeed);
        ApplyMainExtrude(_mainCurrentT);
        UpdateBranchStates(false);
        UpdateBarriers();
        yield return null;
    }

    UpdateBarriers();
    GameProgressData.SaveRiverProgress(_mainCurrentT); // already here — good
}

    private void UpdateBranchStates(bool immediate)
    {
        for (int i = 0; i < _branches.Count; i++)
        {
            var b = _branches[i];
            if (b.IsStarted) continue;

            float parentT = b.Parent == null ? _mainCurrentT : b.Parent.CurrentT;
            bool parentReachedTrigger = parentT >= b.TriggerT;

            bool previousInGroupFinished = true;
            if (i > 0)
            {
                var prev = _branches[i - 1];
                if (prev.JunctionGroup == b.JunctionGroup && !string.IsNullOrEmpty(b.JunctionGroup))
                {
                    if (prev.CurrentT < 1.0f)
                        previousInGroupFinished = false;
                }
            }

            bool exitGatePassed = !b.ExtrudeOnExit || b.ExitUnlocked;

            if (parentReachedTrigger && previousInGroupFinished && exitGatePassed)
            {
                b.IsStarted = true;
                if (immediate)
                {
                    b.CurrentT = 1f;
                    b.Extruder.Range = new Vector2(0, 1);
                    b.Extruder.Rebuild();
                }
                else
                {
                    StartCoroutine(AnimateBranch(b));
                }
            }
        }
    }

    private void OnDestroy()
{
    GameProgressData.SaveRiverProgress(_mainCurrentT);
}

    // ─────────────────────────────────────────────────────────────
    // ExtrudeOnExit — Restore and Unlock
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Restores ExitUnlocked state from save data for all ExtrudeOnExit branches.
    /// Called in Awake before UpdateBranchStates so previously-unlocked segments
    /// extrude immediately on scene reload.
    /// </summary>
    private void RestoreExitUnlocked()
    {
        // ── DEBUG: dump all branch trackers so we can verify ExtrudeOnExit was read correctly
        Debug.Log($"[SplineRiverManager] RestoreExitUnlocked — {_branches.Count} branch(es) total:");
        foreach (var b in _branches)
        {
            bool savedUnlock = !string.IsNullOrEmpty(b.SegmentID) && GameProgressData.IsSegmentExitUnlocked(b.SegmentID);
            Debug.Log($"  Branch '{b.SegmentID}'  ExtrudeOnExit={b.ExtrudeOnExit}  SavedUnlock={savedUnlock}  TriggerT={b.TriggerT:F3}  Group='{b.JunctionGroup}'");

            if (!b.ExtrudeOnExit || string.IsNullOrEmpty(b.SegmentID)) continue;
            b.ExitUnlocked = savedUnlock;
        }
    }

    /// <summary>
    /// Call this when the player exits an arena (returns to the level select scene).
    /// Finds the LevelSelectArenaController for <paramref name="levelID"/>, collects the
    /// segment IDs from portal links whose arenaPath also has ExtrudeOnExit ticked, then
    /// unlocks only the branches matching those IDs — the river paths behind that specific arena.
    /// </summary>
    public void NotifyLevelExited(string levelID)
    {
        Debug.Log($"[SplineRiverManager] NotifyLevelExited called — levelID='{levelID}'");

        if (string.IsNullOrEmpty(levelID)) { Debug.LogWarning("[SplineRiverManager] levelID is empty — aborting."); return; }

        // ── DEBUG: show every arena controller and whether it matches
        var allArenas = FindObjectsOfType<LevelSelectArenaController>();
        Debug.Log($"[SplineRiverManager] Found {allArenas.Length} LevelSelectArenaController(s) in scene:");
        foreach (var arena in allArenas)
        {
            string aID = arena.gridData != null ? arena.gridData.levelID : "(no gridData)";
            bool matches = aID == levelID;
            Debug.Log($"  Arena '{arena.name}'  levelID='{aID}'  MATCH={matches}  portalLinks={arena.portalLinks.Count}");

            if (!matches) continue;

            foreach (var link in arena.portalLinks)
            {
                if (link.arenaPath == null) { Debug.Log($"    Link {arena.portalLinks.IndexOf(link)}: arenaPath is NULL"); continue; }
                var segID = link.arenaPath.GetComponent<RiverSegmentID>();
                Debug.Log($"    Link {arena.portalLinks.IndexOf(link)}: arenaPath='{link.arenaPath.name}'  RiverSegmentID={(segID != null ? "found" : "MISSING")}  SegmentID='{segID?.SegmentID}'  ExtrudeOnExit={segID?.ExtrudeOnExit}");
            }
        }

        // Collect segment IDs from this arena's portal links that have ExtrudeOnExit flagged
        var targetSegmentIDs = new System.Collections.Generic.HashSet<string>();
        foreach (var arena in allArenas)
        {
            if (arena.gridData == null || arena.gridData.levelID != levelID) continue;
            foreach (var link in arena.portalLinks)
            {
                if (link.arenaPath == null) continue;
                var segID = link.arenaPath.GetComponent<RiverSegmentID>();
                if (segID != null && segID.ExtrudeOnExit && !string.IsNullOrEmpty(segID.SegmentID))
                    targetSegmentIDs.Add(segID.SegmentID);
            }
        }

        Debug.Log($"[SplineRiverManager] Target segment IDs to unlock: [{string.Join(", ", targetSegmentIDs)}]");

        if (targetSegmentIDs.Count == 0)
        {
            Debug.LogWarning($"[SplineRiverManager] No arenaPath segments with ExtrudeOnExit=true found for '{levelID}'. " +
                             $"Check that the arenaPath's RiverSegmentID has ExtrudeOnExit ticked.");
            return;
        }

        // ── DEBUG: show each branch and why it passes/fails the unlock check
        Debug.Log($"[SplineRiverManager] Checking {_branches.Count} branch(es) against target IDs:");
        bool anyUnlocked = false;
        foreach (var b in _branches)
        {
            bool idMatch    = targetSegmentIDs.Contains(b.SegmentID);
            bool canUnlock  = b.ExtrudeOnExit && !b.IsStarted && !b.ExitUnlocked && idMatch;
            Debug.Log($"  Branch '{b.SegmentID}'  ExtrudeOnExit={b.ExtrudeOnExit}  IsStarted={b.IsStarted}  ExitUnlocked={b.ExitUnlocked}  IDMatch={idMatch}  → WillUnlock={canUnlock}");

            if (!canUnlock) continue;

            b.ExitUnlocked = true;
            GameProgressData.UnlockSegmentOnExit(b.SegmentID);
            anyUnlocked = true;
            Debug.Log($"[SplineRiverManager] ✓ Exit-unlocked segment '{b.SegmentID}' (arena: '{levelID}').");
        }

        if (anyUnlocked)
            UpdateBranchStates(false);
        else
            Debug.LogWarning($"[SplineRiverManager] No branches were unlocked. See branch dump above.");
    }

    private IEnumerator AnimateBranch(BranchTracker tracker)
    {
        while (tracker.CurrentT < 1f)
        {
            tracker.CurrentT = Mathf.MoveTowards(
                tracker.CurrentT, 1f, Time.deltaTime * tracker.NormalizedSpeed);

            tracker.Extruder.Range = ExtrudeRange(tracker);
            tracker.Extruder.Rebuild();
            UpdateBranchStates(false);
            UpdateBarriers();
            yield return null;
        }

        UpdateBarriers(); // Final sync after branch animation completes
    }

    // Returns the correct SplineExtrude range for a branch, respecting extrude direction.
    // ArenaIsAtEnd=true → arena at t=1, so grow from t=1 backwards: range=(1-currentT, 1)
    // ArenaIsAtEnd=false (default) → grow forwards from t=0: range=(0, currentT)
    // The backward pre-junction overshoot section (t=0 to BranchStartT) is covered by the
    // main river mesh when the branch fires, so it fills the gap without looking early.
    private static Vector2 ExtrudeRange(BranchTracker tracker)
    {
        if (tracker.ExtrudeOnExit && tracker.ArenaIsAtEnd)
            return new Vector2(1f - tracker.CurrentT, 1f);
        return new Vector2(0f, tracker.CurrentT);
    }

    private void ApplyMainExtrude(float t)
    {
        if (_mainExtrude == null) return;
        _mainExtrude.Range = new Vector2(0f, t);
        _mainExtrude.Rebuild();
    }

    // ─────────────────────────────────────────────────────────────
    // Stitch
    // ─────────────────────────────────────────────────────────────
    [ContextMenu("Re-Stitch River")]
    public void Stitch()
    {
        if (_sourceSegments == null || _sourceSegments.Count == 0) return;
        if (_branchPrefab == null) { Debug.LogError("[SplineRiverManager] No branch prefab assigned.", this); return; }

        // 1. Cleanup existing children
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        _branches.Clear();
        _branchBarriers.Clear();

        // 2. Spawn main river from branchPrefab — same as branches, no pre-wired container needed
        var mainObj = Instantiate(_branchPrefab, transform);
        mainObj.name = "MainRiver";
        _mainContainer = mainObj.GetComponent<SplineContainer>();
        _mainExtrude   = mainObj.GetComponent<SplineExtrude>();

        while (_mainContainer.Splines.Count > 0)
            _mainContainer.RemoveSplineAt(0);

        // 3. Prepare the Main River Spline
        Spline mainSpline = new Spline();
        _mainContainer.AddSpline(mainSpline);

        // 3. Pre-calculate Total Lengths for Trigger Mapping
        float totalMainLength = 0f;
        var groupTotalLengths = new Dictionary<string, float>();

        foreach (var source in _sourceSegments)
        {
            float len = source.Spline.GetLength();
            int d = GetBranchDepth(source);
            if (d == 0) totalMainLength += len;
            else
            {
                var id = source.GetComponent<RiverSegmentID>();
                string g = id != null ? id.JunctionGroup : string.Empty;
                if (!string.IsNullOrEmpty(g))
                {
                    if (!groupTotalLengths.ContainsKey(g)) groupTotalLengths[g] = 0f;
                    groupTotalLengths[g] += len;
                }
            }
        }

        // 4. The Stitching Loop
        float mainAccum = 0f;
        var groupAccum    = new Dictionary<string, float>();
        var groupTrackers = new Dictionary<string, BranchTracker>();
        BranchTracker lastTracker = null;

        for (int i = 0; i < _sourceSegments.Count; i++)
        {
            var source = _sourceSegments[i];
            var id     = source.GetComponent<RiverSegmentID>();
            int depth  = GetBranchDepth(source);
            string group = id != null ? id.JunctionGroup : string.Empty;

            if (depth == 0)
            {
                CopySplineData(source, mainSpline, _mainContainer);
                mainAccum   += source.Spline.GetLength();
                lastTracker  = null;
            }
            else
            {
                if (!string.IsNullOrEmpty(group) && groupTrackers.TryGetValue(group, out var existingTracker))
                {
                    CopySplineData(source, existingTracker.Container.Splines[0], existingTracker.Container);
                    groupAccum[group] += source.Spline.GetLength();
                }
                else
                {
                    BranchTracker parent = null;
                    float triggerT = 0f;

                    if (depth == 1)
                    {
                        parent   = null;
                        triggerT = totalMainLength > 0 ? mainAccum / totalMainLength : 0f;
                    }
                    else if (depth >= 2)
                    {
                        parent = lastTracker;
                        float pTotal = 1f;
                        float pAccum = 0f;

                        if (parent != null && !string.IsNullOrEmpty(parent.JunctionGroup))
                        {
                            pTotal = groupTotalLengths.ContainsKey(parent.JunctionGroup) ? groupTotalLengths[parent.JunctionGroup] : 1f;
                            pAccum = groupAccum.ContainsKey(parent.JunctionGroup) ? groupAccum[parent.JunctionGroup] : 0f;
                        }
                        triggerT = pTotal > 0 ? pAccum / pTotal : 0f;
                    }

                    var newTracker = CreateBranchTracker(source, parent, triggerT, depth, group);
                    lastTracker = newTracker;

                    if (!string.IsNullOrEmpty(group))
                    {
                        groupTrackers[group] = newTracker;
                        groupAccum[group]    = source.Spline.GetLength();
                    }
                }
            }
        }

        // Post-stitch: recompute TriggerT as parametric T on the fully-built main spline.
        // The loop above used arc-length fractions (mainAccum/totalMainLength), but
        // _mainCurrentT and SplineExtrude.Range both use parametric T — they diverge on
        // curved splines, causing branches to fire before the visual tip reaches the junction.
        foreach (var branch in _branches)
        {
            if (branch.Depth != 1) continue; // depth-2+ stay arc-length relative to parent for now
            float3 jLocal = _mainContainer.transform.InverseTransformPoint(branch.JunctionWorldPos);
            SplineUtility.GetNearestPoint(_mainContainer.Spline, jLocal, out _, out float parametricT);
            branch.TriggerT = Mathf.Clamp01(parametricT);
        }

        if (_mainExtrude != null) _mainExtrude.Rebuild();
    }

    // ─────────────────────────────────────────────────────────────
    // Branch tracker factory
    // ─────────────────────────────────────────────────────────────
    private BranchTracker CreateBranchTracker(
        SplineContainer source,
        BranchTracker   parent,
        float           triggerT,
        int             depth,
        string          group)
    {
        GameObject branchObj = Instantiate(_branchPrefab, transform);
        branchObj.name = string.IsNullOrEmpty(group)
            ? $"Branch_D{depth}_{source.name}"
            : $"Group_{group}_D{depth}";

        var branchContainer = branchObj.GetComponent<SplineContainer>();
        var branchExtrude   = branchObj.GetComponent<SplineExtrude>();

        branchExtrude.Range = Vector2.zero;

        var srcID = source.GetComponent<RiverSegmentID>();
        string segID = !string.IsNullOrEmpty(group) ? group
                     : (srcID != null && !string.IsNullOrEmpty(srcID.SegmentID) ? srcID.SegmentID : source.name);

        // Capture junction world pos before OffsetBranchStartKnot moves the first knot
        Vector3 junctionWorld = source.transform.TransformPoint((Vector3)source.Spline[0].Position);

        var tracker = new BranchTracker
        {
            Extruder         = branchExtrude,
            Container        = branchContainer,
            TriggerT         = triggerT,
            Parent           = parent,
            CurrentT         = 0f,
            IsStarted        = false,
            JunctionGroup    = group,
            Depth            = depth,
            SegmentID        = segID,
            ExtrudeOnExit    = srcID != null && srcID.ExtrudeOnExit,
            ExitUnlocked     = false,
            ArenaIsAtEnd     = srcID != null && srcID.ArenaIsAtEnd,
            JunctionWorldPos = junctionWorld,
        };

        _branches.Add(tracker);

        if (branchContainer.Splines.Count > 0) branchContainer.RemoveSplineAt(0);
        Spline newSpline = new Spline();
        branchContainer.AddSpline(newSpline);

        CopySplineData(source, newSpline, branchContainer);
        OffsetBranchStartKnot(newSpline, branchContainer);

        float totalLen = newSpline.GetLength();
        tracker.NormalizedSpeed = totalLen > 0 ? _extrudeSpeed / totalLen : _extrudeSpeed;
        // BranchStartT = fraction of the spline that is the backward pre-junction section.
        // Extruding from here means the branch only grows forward from the junction, never backwards.
        tracker.BranchStartT = totalLen > 0 ? Mathf.Clamp01(_branchOvershoot / totalLen) : 0f;

        return tracker;
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────
    private int GetBranchDepth(SplineContainer source)
    {
        var id = source.GetComponent<RiverSegmentID>();
        return id != null ? id.BranchDepth : 0;
    }

    private void OffsetBranchStartKnot(Spline spline, SplineContainer container)
    {
        if (spline.Count == 0) return;

        float3  worldTangent = SplineUtility.EvaluateTangent(spline, 0f);
        Vector3 worldDir     = math.normalize(worldTangent);
        Vector3 localDir     = container.transform.InverseTransformDirection(worldDir);

        var knot = spline[0];
        knot.Position -= (float3)(localDir * _branchOvershoot);
        spline[0] = knot;
    }

    private void CopySplineData(
        SplineContainer source,
        Spline          destSpline,
        SplineContainer destContainer)
    {
        foreach (var s in source.Splines)
        {
            for (int k = 0; k < s.Count; k++)
            {
                var knot = s[k];
                float3     worldPos = source.transform.TransformPoint((Vector3)knot.Position);
                float3     localPos = destContainer.transform.InverseTransformPoint((Vector3)worldPos);
                Quaternion worldRot = source.transform.rotation * (Quaternion)knot.Rotation;
                Quaternion localRot = Quaternion.Inverse(destContainer.transform.rotation) * worldRot;

                destSpline.Add(
                    new BezierKnot(localPos, knot.TangentIn, knot.TangentOut, (quaternion)localRot),
                    s.GetTangentMode(k));
            }
        }
    }

    public float WorldPositionToT(Vector3 worldPosition)
    {
        if (_mainContainer == null || _mainContainer.Spline == null) return 0f;
        SplineUtility.GetNearestPoint(
            _mainContainer.Spline,
            _mainContainer.transform.InverseTransformPoint(worldPosition),
            out _, out float t);
        return Mathf.Clamp01(t);
    }
}