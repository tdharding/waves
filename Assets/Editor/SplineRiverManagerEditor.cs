using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(SplineRiverManager))]
public class SplineRiverManagerEditor : Editor
{
    private enum SimState { Idle, Running, PausedAtObstacle }

    private SimState _state       = SimState.Idle;
    private double   _lastTime;
    private string   _blockedByID = string.Empty;  // main river blocker ID

    private readonly List<ObstacleEntry>     _obstacles         = new();
    private readonly HashSet<string>         _editorObsUnlocked = new();
    private readonly Dictionary<int, string> _blockedBranches   = new(); // branch idx → obstacleID

    private bool  _useEditorOverrides = true;
    private bool  _showBranches       = true;
    private bool  _showObstacles      = true;
    private float _speedMultiplier    = 1f;

    private struct ObstacleEntry
    {
        public LevelSelectObstacleManager Manager;
        public float T;
        public int   SplineIndex; // -1 = main river, 0+ = branch index
    }

    private SplineRiverManager Mgr => (SplineRiverManager)target;

    // ─────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    // ─────────────────────────────────────────────────────────────
    // Inspector
    // ─────────────────────────────────────────────────────────────

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        DrawHRule();
        EditorGUILayout.LabelField("River Extrude Simulation", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        if (GUILayout.Button("Re-Stitch River"))
        {
            Mgr.Stitch();
            EditorUtility.SetDirty(target);
        }

        bool ready = Mgr.Editor_GetMainContainer() != null;
        if (!ready)
        {
            EditorGUILayout.HelpBox("Click Re-Stitch River to build splines before simulating.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(4);

        _useEditorOverrides = EditorGUILayout.Toggle("Editor-Only Unlocks", _useEditorOverrides);
        EditorGUILayout.HelpBox(
            _useEditorOverrides
                ? "Obstacle unlocks are session-only — save file unchanged."
                : "Obstacle unlocks write to GameProgressData (affects save file).",
            _useEditorOverrides ? MessageType.None : MessageType.Warning);

        _speedMultiplier = EditorGUILayout.Slider("Speed Multiplier", _speedMultiplier, 0.1f, 10f);

        EditorGUILayout.Space(6);
        DrawSimControls();
        EditorGUILayout.Space(4);
        DrawManualScrubber();
        EditorGUILayout.Space(8);
        DrawBranchesSection();
        EditorGUILayout.Space(6);
        DrawObstaclesSection();
    }

    // ─────────────────────────────────────────────────────────────
    // Simulation controls
    // ─────────────────────────────────────────────────────────────

    private void DrawSimControls()
    {
        switch (_state)
        {
            case SimState.Idle:
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("▶  Start")) StartSim();
                if (GUILayout.Button("↺  Reset")) ResetSim();
                EditorGUILayout.EndHorizontal();
                break;

            case SimState.Running:
            {
                float mainT    = Mgr.Editor_GetMainCurrentT();
                var   branches = Mgr.Editor_GetBranchInfos();
                int   active   = branches.FindAll(b => b.IsStarted && b.CurrentT < 1f).Count;

                string nextStop = "Free run to end";
                foreach (var obs in _obstacles)
                {
                    if (obs.SplineIndex != -1) continue;
                    if (obs.T < mainT - 0.002f) continue;
                    if (!IsObsUnlocked(obs.Manager.obstacleID))
                    { nextStop = $"Next stop: '{obs.Manager.obstacleID}'  T={obs.T:F4}"; break; }
                }

                EditorGUILayout.HelpBox(
                    $"Simulating — Main T: {mainT:F3}" +
                    (active > 0 ? $"  |  {active} branch(es) active" : string.Empty) +
                    $"\n{nextStop}",
                    MessageType.None);

                var bar = EditorGUILayout.GetControlRect(false, 6f);
                EditorGUI.DrawRect(bar, new Color(0.25f, 0.25f, 0.25f));
                var fill = bar; fill.width = bar.width * mainT;
                EditorGUI.DrawRect(fill, new Color(0.2f, 0.55f, 1f));

                if (GUILayout.Button("■  Stop")) StopSim();
                break;
            }

            case SimState.PausedAtObstacle:
                EditorGUILayout.HelpBox(
                    $"Main river blocked at '{_blockedByID}' — other rivers still animating.\nUnlock to continue.",
                    MessageType.Warning);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("▶  Unlock & Continue"))
                {
                    SetObsUnlocked(_blockedByID, true);
                    _state = SimState.Running; // loop is still running
                    Repaint();
                }
                if (GUILayout.Button("■  Stop"))  StopSim();
                if (GUILayout.Button("↺  Reset")) ResetSim();
                EditorGUILayout.EndHorizontal();
                break;
        }
    }

    private void DrawManualScrubber()
    {
        using (new EditorGUI.DisabledScope(_state != SimState.Idle))
        {
            float current = Mgr.Editor_GetMainCurrentT();
            float newT    = EditorGUILayout.Slider("Main River T", current, 0f, 1f);
            if (!Mathf.Approximately(newT, current))
            {
                Mgr.Editor_SetMainExtrudeT(newT);
                EditorUtility.SetDirty(target);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Branches panel
    // ─────────────────────────────────────────────────────────────

    private void DrawBranchesSection()
    {
        _showBranches = EditorGUILayout.Foldout(_showBranches, "Branches", true, EditorStyles.foldoutHeader);
        if (!_showBranches) return;

        var branches = Mgr.Editor_GetBranchInfos();
        if (branches.Count == 0)
        {
            EditorGUILayout.HelpBox("No branches — only a main river.", MessageType.None);
            return;
        }

        EditorGUI.indentLevel++;
        for (int i = 0; i < branches.Count; i++)
            DrawBranchRow(branches[i], i);
        EditorGUI.indentLevel--;
    }

    private void DrawBranchRow(SplineRiverManager.EditorBranchInfo b, int index)
    {
        bool branchBlocked = _blockedBranches.ContainsKey(index);

        Color  dot;
        string status;

        if (!b.IsStarted)
        {
            if (b.ExtrudeOnExit && !b.ExitUnlocked)
            {
                dot    = new Color(1f, 0.6f, 0f);
                status = "Waiting: exit gate";
            }
            else
            {
                dot    = new Color(0.55f, 0.55f, 0.55f);
                status = $"Trigger T={b.TriggerT:F2}";
            }
        }
        else if (b.CurrentT >= 1f)
        {
            dot    = new Color(0.2f, 0.8f, 0.2f);
            status = "Done";
        }
        else if (branchBlocked)
        {
            dot    = new Color(1f, 0.4f, 0.2f); // red-orange — blocked at obstacle
            status = $"Blocked T={b.CurrentT:F3}";
        }
        else
        {
            dot    = new Color(0.2f, 0.55f, 1f);
            status = $"T={b.CurrentT:F3}";
        }

        EditorGUILayout.BeginHorizontal();

        var dotStyle = new GUIStyle(EditorStyles.label);
        dotStyle.normal.textColor = dot;
        EditorGUILayout.LabelField("●", dotStyle, GUILayout.Width(16));
        EditorGUILayout.LabelField($"D{b.Depth}", GUILayout.Width(24));

        string label = string.IsNullOrEmpty(b.SegmentID) ? "(unnamed)" : b.SegmentID;
        if (!string.IsNullOrEmpty(b.JunctionGroup) && b.JunctionGroup != b.SegmentID)
            label += $" [{b.JunctionGroup}]";
        EditorGUILayout.LabelField(label);
        EditorGUILayout.LabelField(status, GUILayout.Width(120));

        // Exit gate unlock button
        if (b.ExtrudeOnExit)
        {
            if (!b.ExitUnlocked)
            {
                if (GUILayout.Button("Unlock", GUILayout.Width(54)))
                { Mgr.Editor_UnlockSegment(b.SegmentID); EditorUtility.SetDirty(target); }
            }
            else
            {
                if (GUILayout.Button("Lock", GUILayout.Width(54)))
                { Mgr.Editor_LockSegment(b.SegmentID); EditorUtility.SetDirty(target); }
            }
        }

        // Branch obstacle unlock button
        if (branchBlocked)
        {
            string obsID = _blockedBranches[index];
            if (GUILayout.Button("Unblock", GUILayout.Width(60)))
                SetObsUnlocked(obsID, true);
        }

        EditorGUILayout.EndHorizontal();

        if (b.IsStarted)
        {
            int indent = EditorGUI.indentLevel;
            var barRect = EditorGUILayout.GetControlRect(false, 4f);
            barRect.x     += 16 * indent;
            barRect.width -= 16 * indent;
            EditorGUI.DrawRect(barRect, new Color(0.25f, 0.25f, 0.25f));
            var fillRect  = barRect;
            fillRect.width = barRect.width * b.CurrentT;
            Color barCol = branchBlocked
                ? new Color(1f, 0.4f, 0.2f)
                : (b.CurrentT >= 1f ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.2f, 0.55f, 1f));
            EditorGUI.DrawRect(fillRect, barCol);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Obstacles panel
    // ─────────────────────────────────────────────────────────────

    private void DrawObstaclesSection()
    {
        _showObstacles = EditorGUILayout.Foldout(_showObstacles, "Spatial Obstacles", true, EditorStyles.foldoutHeader);
        if (!_showObstacles) return;

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh"))    RefreshObstacles();
        if (GUILayout.Button("Unlock All")) { foreach (var o in _obstacles) SetObsUnlocked(o.Manager.obstacleID, true); }
        if (GUILayout.Button("Lock All"))   { _editorObsUnlocked.Clear(); foreach (var o in _obstacles) SetObsUnlocked(o.Manager.obstacleID, false); }
        EditorGUILayout.EndHorizontal();

        if (_obstacles.Count == 0)
        {
            EditorGUILayout.HelpBox("No LevelSelectObstacleManagers found. Click Refresh.", MessageType.Info);
            return;
        }

        EditorGUI.indentLevel++;
        foreach (var obs in _obstacles)
        {
            bool unlocked = IsObsUnlocked(obs.Manager.obstacleID);

            EditorGUILayout.BeginHorizontal();

            var dot = new GUIStyle(EditorStyles.label);
            dot.normal.textColor = unlocked ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.9f, 0.3f, 0.3f);
            EditorGUILayout.LabelField(unlocked ? "●" : "○", dot, GUILayout.Width(14));

            // Which spline this obstacle belongs to
            string splineLabel = obs.SplineIndex == -1 ? "Main" : $"Branch[{obs.SplineIndex}]";
            EditorGUILayout.LabelField(splineLabel, GUILayout.Width(72));

            var tStyle = new GUIStyle(EditorStyles.label);
            if (obs.T < 0.005f) tStyle.normal.textColor = new Color(1f, 0.7f, 0f);
            EditorGUILayout.LabelField($"T={obs.T:F4}", tStyle, GUILayout.Width(64));

            EditorGUILayout.LabelField(obs.Manager.obstacleID);

            if (unlocked)
            {
                if (GUILayout.Button("Lock",   GUILayout.Width(46))) SetObsUnlocked(obs.Manager.obstacleID, false);
            }
            else
            {
                if (GUILayout.Button("Unlock", GUILayout.Width(46))) SetObsUnlocked(obs.Manager.obstacleID, true);
            }

            if (GUILayout.Button("⦿", GUILayout.Width(24)))
                Selection.activeGameObject = obs.Manager.gameObject;

            EditorGUILayout.EndHorizontal();
        }
        EditorGUI.indentLevel--;
    }

    // ─────────────────────────────────────────────────────────────
    // Simulation lifecycle
    // ─────────────────────────────────────────────────────────────

    private void StartSim()
    {
        RefreshObstacles();
        _lastTime = EditorApplication.timeSinceStartup;
        _state    = SimState.Running;
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
    }

    private void StopSim()
    {
        _state = SimState.Idle;
        _blockedBranches.Clear();
        EditorApplication.update -= OnEditorUpdate;
        Repaint();
    }

    private void ResetSim()
    {
        StopSim();
        _editorObsUnlocked.Clear();
        Mgr.Editor_ResetSimulation();
        EditorUtility.SetDirty(target);
    }

    private void OnEditorUpdate()
    {
        if (target == null || _state == SimState.Idle)
        {
            EditorApplication.update -= OnEditorUpdate;
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        float  dt  = (float)(now - _lastTime);
        _lastTime = now;

        float mainT    = Mgr.Editor_GetMainCurrentT();
        var   branches = Mgr.Editor_GetBranchInfos();

        // ── Main river target ──────────────────────────────────
        // Keep locked if already paused (unless it has just been unlocked this frame)
        float  mainTargetT = 1f;
        string newBlockID  = string.Empty;

        if (_state == SimState.PausedAtObstacle && !IsObsUnlocked(_blockedByID))
        {
            mainTargetT = mainT; // hold position
        }
        else
        {
            if (_state == SimState.PausedAtObstacle)
                _state = SimState.Running; // was just unlocked

            foreach (var obs in _obstacles)
            {
                if (obs.SplineIndex != -1) continue;
                if (obs.T < mainT - 0.002f) continue;
                if (!IsObsUnlocked(obs.Manager.obstacleID))
                { mainTargetT = obs.T; newBlockID = obs.Manager.obstacleID; break; }
            }
        }

        // ── Branch targets (one per branch, independent) ───────
        _blockedBranches.Clear();
        float[] branchTargetTs = new float[branches.Count];
        for (int i = 0; i < branchTargetTs.Length; i++) branchTargetTs[i] = 1f;

        for (int i = 0; i < branches.Count; i++)
        {
            float branchT = branches[i].CurrentT;
            foreach (var obs in _obstacles)
            {
                if (obs.SplineIndex != i) continue;
                if (obs.T < branchT - 0.002f) continue;
                if (!IsObsUnlocked(obs.Manager.obstacleID))
                {
                    branchTargetTs[i]   = obs.T;
                    _blockedBranches[i] = obs.Manager.obstacleID;
                    break;
                }
            }
        }

        // ── Step ───────────────────────────────────────────────
        Mgr.Editor_StepSimulation(dt * _speedMultiplier, mainTargetT, branchTargetTs);
        EditorUtility.SetDirty(target);
        Repaint();

        float newMainT = Mgr.Editor_GetMainCurrentT();

        // ── Detect main river hitting obstacle ─────────────────
        if (_state == SimState.Running &&
            newMainT >= mainTargetT - 0.0001f &&
            mainTargetT < 1f - 0.001f)
        {
            _blockedByID = newBlockID;
            _state       = SimState.PausedAtObstacle;
            // Loop keeps running so branches continue animating
            Repaint();
        }

        // ── Detect completion ───────────────────────────────────
        var updated = Mgr.Editor_GetBranchInfos();
        if (newMainT >= 1f - 0.001f &&
            updated.TrueForAll(b => !b.IsStarted || b.CurrentT >= 1f))
        {
            _state = SimState.Idle;
            EditorApplication.update -= OnEditorUpdate;
            Repaint();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Obstacle helpers
    // ─────────────────────────────────────────────────────────────

    private void RefreshObstacles()
    {
        _obstacles.Clear();
        foreach (var mgr in FindObjectsOfType<LevelSelectObstacleManager>())
        {
            if (string.IsNullOrEmpty(mgr.obstacleID)) continue;
            Vector3 samplePos = mgr.RiverStopPoint != null ? mgr.RiverStopPoint.position : mgr.transform.position;
            var match = Mgr.Editor_FindNearestSpline(samplePos);
            _obstacles.Add(new ObstacleEntry
            {
                Manager    = mgr,
                T          = match.T,
                SplineIndex = match.Index,
            });
        }
        // Sort by spline then by T so the per-spline "find first ahead" loop works
        _obstacles.Sort((a, b) =>
        {
            int cmp = a.SplineIndex.CompareTo(b.SplineIndex);
            return cmp != 0 ? cmp : a.T.CompareTo(b.T);
        });
        Repaint();
    }

    private bool IsObsUnlocked(string id)
    {
        if (string.IsNullOrEmpty(id)) return true;
        return _useEditorOverrides
            ? _editorObsUnlocked.Contains(id)
            : GameProgressData.IsUnlocked(id);
    }

    private void SetObsUnlocked(string id, bool unlocked)
    {
        if (_useEditorOverrides)
        {
            if (unlocked) _editorObsUnlocked.Add(id);
            else          _editorObsUnlocked.Remove(id);
        }
        else
        {
            if (unlocked) GameProgressData.UnlockObstacle(id);
            else          GameProgressData.LockObstacle(id);
        }
        Repaint();
    }

    // ─────────────────────────────────────────────────────────────
    // Utility
    // ─────────────────────────────────────────────────────────────

    private static void DrawHRule()
    {
        var r = EditorGUILayout.GetControlRect(false, 1f);
        EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.4f));
        EditorGUILayout.Space(2);
    }
}
