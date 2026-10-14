using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Shot cameras on the map — a camera symbol wherever a camera sequence preset's shot camera stands
/// (every preset in <see cref="CameraSequencePresetIO.Folder"/>, on a node or not). Clicking one in
/// Select mode shows it alone in the left panel, with a Clamp toggle and what it is clamped to:
/// a river node, a wall tower, an outpost, a structure or a spike.
///
/// A clamp works one way. When the target moves, the camera moves the same amount (position only),
/// and so do the camera's saved copies in every preset, so Load puts it back in the new place.
/// Moving the camera itself leaves the target alone — its new spot is simply its new offset.
/// A clamped camera is left out of its node's Pin Cameras; the rest of that preset still pins.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private sealed class ShotCamera
    {
        public string                     link;
        public Transform                  transform;
        public readonly List<CameraSequencePreset> presets = new();
    }

    // Read again at most once a second — presets and scene cameras rarely change under the window.
    private const double ShotCameraRefresh = 1.0;

    private readonly List<ShotCamera>           _shotCameras       = new();
    private readonly List<CameraSequencePreset> _shotCameraPresets = new();
    private double _shotCamerasReadAt = double.MinValue;

    private string _selectedShotCamera;   // CameraSequenceLink id of the picked camera
    private bool   _clampPicking;         // Pick on Map: the next click on the canvas picks the target

    private static readonly string[] ClampTargetNames = { "River Node", "Wall Tower", "Outpost", "Structure", "Spike" };

    // ─────────────────────────────────────────────
    // THE CAMERAS
    // ─────────────────────────────────────────────

    private void RefreshShotCameras(bool force = false)
    {
        double now = EditorApplication.timeSinceStartup;
        if (!force && now - _shotCamerasReadAt < ShotCameraRefresh) return;
        _shotCamerasReadAt = now;

        _shotCameraPresets.Clear();
        if (AssetDatabase.IsValidFolder(CameraSequencePresetIO.Folder))
            foreach (string guid in AssetDatabase.FindAssets("t:CameraSequencePreset", new[] { CameraSequencePresetIO.Folder }))
            {
                var preset = AssetDatabase.LoadAssetAtPath<CameraSequencePreset>(AssetDatabase.GUIDToAssetPath(guid));
                if (preset != null) _shotCameraPresets.Add(preset);
            }

        var inScene = new Dictionary<string, Transform>();
        foreach (var link in FindObjectsByType<CameraSequenceLink>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!string.IsNullOrEmpty(link.id)) inScene[link.id] = link.transform;

        _shotCameras.Clear();
        foreach (var preset in _shotCameraPresets)
            foreach (var shot in preset.shots)
            {
                if (shot == null || string.IsNullOrEmpty(shot.cameraLink)) continue;
                if (!inScene.TryGetValue(shot.cameraLink, out var t)) continue;

                var cam = _shotCameras.Find(c => c.link == shot.cameraLink);
                if (cam == null)
                {
                    cam = new ShotCamera { link = shot.cameraLink, transform = t };
                    _shotCameras.Add(cam);
                }
                if (!cam.presets.Contains(preset)) cam.presets.Add(preset);
            }
    }

    private ShotCamera SelectedShotCamera =>
        _selectedShotCamera == null ? null : _shotCameras.Find(c => c.link == _selectedShotCamera);

    private LevelSelectDesignerData.CameraClamp ClampFor(string cameraLink) =>
        _data?.cameraClamps?.Find(c => c.cameraLink == cameraLink);

    /// <summary>True when the camera follows its own clamp, so Pin Cameras leaves it be.</summary>
    private bool IsCameraClamped(string cameraLink)
    {
        if (string.IsNullOrEmpty(cameraLink)) return false;
        var clamp = ClampFor(cameraLink);
        return clamp != null && clamp.on && !string.IsNullOrEmpty(clamp.targetId);
    }

    // ─────────────────────────────────────────────
    // TARGETS
    // ─────────────────────────────────────────────

    /// <summary>Where the clamp's target stands now. False when it has none or it is gone.</summary>
    private bool TryClampTargetPosition(LevelSelectDesignerData.CameraClamp clamp, out Vector3 at)
    {
        at = default;
        if (clamp == null || string.IsNullOrEmpty(clamp.targetId)) return false;

        switch (clamp.target)
        {
            case LevelSelectDesignerData.ClampTarget.Node:
            {
                var node = _data.nodes.Find(n => n.id == clamp.targetId);
                if (node == null) return false;
                at = node.worldPosition;
                return true;
            }
            case LevelSelectDesignerData.ClampTarget.WallTower:
            {
                var wall = _data.walls.Find(w => w.wallId == clamp.targetId);
                if (wall == null || clamp.targetIndex < 0 || clamp.targetIndex >= wall.nodes.Count) return false;
                var wallNode = wall.nodes[clamp.targetIndex];
                if (!wallNode.hasTower) return false;
                at = WallNodeWorldFlat(wallNode);
                return true;
            }
            case LevelSelectDesignerData.ClampTarget.Outpost:
            {
                // Its spot on the river, not its floor — so switching its bank doesn't throw the
                // camera across the river.
                var outpost = _data.outposts.Find(o => o.outpostId == clamp.targetId);
                var path    = outpost == null ? null : _data.paths.Find(p => p.pathId == outpost.pathId);
                if (path == null || path.nodeIds.Count < 2) return false;
                PathFrameAt(path, outpost.pathT, out at, out _);
                return true;
            }
            case LevelSelectDesignerData.ClampTarget.Structure:
            {
                var s = _data.structures.Find(x => x.structureId == clamp.targetId);
                if (s == null) return false;
                at = new Vector3(s.positionXZ.x, 0f, s.positionXZ.y);
                return true;
            }
            case LevelSelectDesignerData.ClampTarget.Spike:
            {
                var spike = _data.spikes.Find(x => x.spikeId == clamp.targetId);
                if (spike == null) return false;
                at = new Vector3(spike.positionXZ.x, 0f, spike.positionXZ.y);
                return true;
            }
        }
        return false;
    }

    // Every target of one kind, as (id, wall node index, popup label).
    private List<(string id, int index, string label)> ClampTargetChoices(LevelSelectDesignerData.ClampTarget kind)
    {
        var list = new List<(string, int, string)>();
        switch (kind)
        {
            case LevelSelectDesignerData.ClampTarget.Node:
            {
                var listed = new HashSet<string>();
                foreach (var path in _data.paths)
                    for (int i = 0; i < path.nodeIds.Count; i++)
                        if (listed.Add(path.nodeIds[i]))
                            list.Add((path.nodeIds[i], -1, $"{PathDisplayName(path).Replace('/', '-')}/Node {i}"));
                foreach (var node in _data.nodes)
                    if (node != null && listed.Add(node.id))
                        list.Add((node.id, -1, $"Other/{NodeLabel(node.id).Replace('/', '-')}"));
                break;
            }
            case LevelSelectDesignerData.ClampTarget.WallTower:
                for (int w = 0; w < _data.walls.Count; w++)
                    for (int i = 0; i < _data.walls[w].nodes.Count; i++)
                        if (_data.walls[w].nodes[i].hasTower)
                            list.Add((_data.walls[w].wallId, i, $"Wall {w + 1}/Tower on node {i + 1}"));
                break;
            case LevelSelectDesignerData.ClampTarget.Outpost:
                for (int i = 0; i < _data.outposts.Count; i++)
                {
                    var o    = _data.outposts[i];
                    var path = _data.paths.Find(p => p.pathId == o.pathId);
                    list.Add((o.outpostId, -1,
                              $"Outpost {i + 1} — on {(path != null ? PathDisplayName(path) : "(missing path)")}".Replace('/', '-')));
                }
                break;
            case LevelSelectDesignerData.ClampTarget.Structure:
                foreach (var s in _data.structures)
                    if (s != null) list.Add((s.structureId, -1, StructureLabel(s).Replace('/', '-')));
                break;
            case LevelSelectDesignerData.ClampTarget.Spike:
                foreach (var spike in _data.spikes)
                    if (spike != null) list.Add((spike.spikeId, -1, SpikeLabel(spike).Replace('/', '-')));
                break;
        }
        return list;
    }

    /// <summary>Points the selected camera's clamp at a target, starting from where it stands now.</summary>
    private void SetClampTarget(LevelSelectDesignerData.ClampTarget kind, string id, int index)
    {
        if (_selectedShotCamera == null) return;
        Undo.RecordObject(_data, "Camera Clamp");
        var clamp = ClampFor(_selectedShotCamera);
        if (clamp == null)
        {
            clamp = new LevelSelectDesignerData.CameraClamp { cameraLink = _selectedShotCamera, on = true };
            _data.cameraClamps.Add(clamp);
        }
        clamp.target      = kind;
        clamp.targetId    = id;
        clamp.targetIndex = index;
        if (TryClampTargetPosition(clamp, out Vector3 at)) clamp.targetAt = at;
        MarkDirty();
        Repaint();
    }

    // ─────────────────────────────────────────────
    // FOLLOWING
    // ─────────────────────────────────────────────

    /// <summary>
    /// Moves every clamped camera by however far its target has moved since the camera last moved
    /// (target position − targetAt), and its saved copies in every preset with it. Called from
    /// MarkDirty, inside the same undo step as the target's move.
    /// </summary>
    private void SyncCameraClamps()
    {
        if (_data?.cameraClamps == null || _data.cameraClamps.Count == 0) return;

        bool presetsRead = false;
        foreach (var clamp in _data.cameraClamps)
        {
            if (clamp == null || !clamp.on) continue;
            if (!TryClampTargetPosition(clamp, out Vector3 at)) continue;

            Vector3 delta = at - clamp.targetAt;
            if (delta.sqrMagnitude < 1e-10f) continue;

            Undo.RecordObject(_data, "Move");
            clamp.targetAt = at;

            var link = CameraSequenceLink.Find(clamp.cameraLink);
            if (link != null)
            {
                Undo.RecordObject(link.transform, "Move");
                link.transform.position += delta;
                PrefabUtility.RecordPrefabInstancePropertyModifications(link.transform);
            }

            if (!presetsRead) { RefreshShotCameras(); presetsRead = true; }
            foreach (var preset in _shotCameraPresets)
            {
                if (preset == null) continue;
                bool touched = false;
                foreach (var stored in preset.storedObjects)
                {
                    if (stored.linkId != clamp.cameraLink) continue;
                    if (!touched) { Undo.RecordObject(preset, "Move"); touched = true; }
                    stored.position += delta;
                }
                if (touched) EditorUtility.SetDirty(preset);
            }
        }
    }

    // ─────────────────────────────────────────────
    // CANVAS
    // ─────────────────────────────────────────────

    /// <summary>A camera symbol where each shot camera stands; the picked one ringed.</summary>
    private void DrawShotCameras()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (!LevelSelectDesignerSettings.ShotCameras) return;
        RefreshShotCameras();

        float w = LevelSelectDesignerSettings.ShotCameraSize;
        float h = w * 0.6f;
        foreach (var cam in _shotCameras)
        {
            if (cam.transform == null) continue;
            Vector2 pos = WorldToCanvas(cam.transform.position);

            if (cam.link == _selectedShotCamera) DrawNodeSelectedRing(pos);
            Handles.color = Color.black;
            DrawCameraShape(pos + Vector2.one, w, h);
            Handles.color = LevelSelectDesignerSettings.ShotCameraColour;
            DrawCameraShape(pos, w, h);
        }
    }

    private bool FindShotCameraAtCanvas(Vector2 canvas, out string link)
    {
        link = null;
        if (!LevelSelectDesignerSettings.ShotCameras) return false;
        RefreshShotCameras();

        float reach = Mathf.Max(8f, LevelSelectDesignerSettings.ShotCameraSize * 0.6f);
        // Last drawn is on top, so it is the one a click lands on.
        for (int i = _shotCameras.Count - 1; i >= 0; i--)
        {
            var cam = _shotCameras[i];
            if (cam.transform == null) continue;
            if (Vector2.Distance(WorldToCanvas(cam.transform.position), canvas) > reach) continue;
            link = cam.link;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Select mode, before anything else: Pick on Map takes the click, else a click on a camera
    /// symbol picks that camera alone. True when it took the event.
    /// </summary>
    private bool HandleShotCameraInput(Event e)
    {
        if (_clampPicking)
        {
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _clampPicking = false;
                Repaint();
                e.Use();
                return true;
            }
            if (e.type != EventType.MouseDown || e.button != 0) return false;

            _clampPicking = false;
            Vector2 m = e.mousePosition;
            if      (FindSpikeAtCanvas(m, out string spike))           SetClampTarget(LevelSelectDesignerData.ClampTarget.Spike, spike, -1);
            else if (FindStructureAtCanvas(m, out string structure))   SetClampTarget(LevelSelectDesignerData.ClampTarget.Structure, structure, -1);
            else if (FindWallNodeAtCanvas(m, out string wall, out int wallNode) &&
                     _data.walls.Find(x => x.wallId == wall).nodes[wallNode].hasTower)
                                                                       SetClampTarget(LevelSelectDesignerData.ClampTarget.WallTower, wall, wallNode);
            else if (FindNodeAtCanvas(m) is string node)               SetClampTarget(LevelSelectDesignerData.ClampTarget.Node, node, -1);
            else if (FindOutpostAtCanvas(m) is string outpost)         SetClampTarget(LevelSelectDesignerData.ClampTarget.Outpost, outpost, -1);
            Repaint();
            e.Use();
            return true;
        }

        if (e.type != EventType.MouseDown || e.button != 0) return false;
        if (!FindShotCameraAtCanvas(e.mousePosition, out string link)) return false;

        ClearSelectionForLandscape();
        _selectedShotCamera = link;
        Repaint();
        e.Use();
        return true;
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    /// <summary>The picked shot camera, alone in the left panel: which presets use it, and its clamp.</summary>
    private void DrawShotCameraPanel()
    {
        RefreshShotCameras();
        var cam = SelectedShotCamera;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Shot Camera", EditorStyles.boldLabel);
        if (cam == null || cam.transform == null)
        {
            EditorGUILayout.HelpBox("This camera is no longer in the scene.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField(cam.transform.name);
        EditorGUILayout.LabelField("In " + string.Join(", ", cam.presets.Where(p => p != null).Select(p => p.name)),
                                   EditorStyles.wordWrappedMiniLabel);

        var presets = cam.presets.Where(p => p != null).ToList();
        using (new EditorGUI.DisabledScope(presets.Count == 0))
        {
            var pingContent = new GUIContent(presets.Count > 1 ? "Ping in Camera Sequence ▾" : "Ping in Camera Sequence",
                "Opens the Camera Sequence window on the preset this camera is in, scrolls to its shot and flashes it. " +
                "In more than one preset: pick which.");
            Rect pingRect = GUILayoutUtility.GetRect(pingContent, EditorStyles.miniButton);
            if (GUI.Button(pingRect, pingContent, EditorStyles.miniButton))
            {
                string link = cam.link;
                if (presets.Count == 1) CameraSequenceWindow.PingCameraFromDesigner(presets[0], link);
                else
                {
                    var menu = new GenericMenu();
                    foreach (var p in presets)
                    {
                        var preset = p;
                        menu.AddItem(new GUIContent(preset.name.Replace('/', '-')), false,
                                     () => CameraSequenceWindow.PingCameraFromDesigner(preset, link));
                    }
                    menu.DropDown(pingRect);
                }
            }
        }

        var  clamp  = ClampFor(cam.link);
        bool on     = clamp != null && clamp.on;
        var  kind   = clamp?.target ?? LevelSelectDesignerData.ClampTarget.Node;

        EditorGUILayout.Space(6);
        EditorGUI.BeginChangeCheck();
        on = EditorGUILayout.ToggleLeft(new GUIContent("Clamp",
            "The camera moves with what it is clamped to: move that and the camera goes along, keeping " +
            "where it stands round it. Moving the camera itself moves nothing else. Its saved copies in " +
            "the presets move too, so Load puts it back in the new place."), on, EditorStyles.boldLabel);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Camera Clamp");
            if (clamp == null)
            {
                clamp = new LevelSelectDesignerData.CameraClamp { cameraLink = cam.link };
                _data.cameraClamps.Add(clamp);
            }
            clamp.on = on;
            // Following starts from where the target stands now.
            if (on && TryClampTargetPosition(clamp, out Vector3 at)) clamp.targetAt = at;
            MarkDirty();
        }

        using (new EditorGUI.DisabledScope(!on))
        {
            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();
            kind = (LevelSelectDesignerData.ClampTarget)EditorGUILayout.Popup(
                new GUIContent("Clamp To", "What kind of thing on the map it follows."), (int)kind, ClampTargetNames);
            bool kindChanged = EditorGUI.EndChangeCheck();

            var choices = ClampTargetChoices(kind);
            int current = kindChanged || clamp == null ? -1
                : choices.FindIndex(c => c.id == clamp.targetId &&
                                         (kind != LevelSelectDesignerData.ClampTarget.WallTower || c.index == clamp.targetIndex));
            var labels = new GUIContent[choices.Count + 1];
            labels[0] = new GUIContent(choices.Count == 0 ? "(none on the map)" : "(none)");
            for (int i = 0; i < choices.Count; i++) labels[i + 1] = new GUIContent(choices[i].label);

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(new GUIContent("Target", "Which one it follows."), current + 1, labels) - 1;
            if (EditorGUI.EndChangeCheck() || kindChanged)
            {
                if (picked >= 0) SetClampTarget(kind, choices[picked].id, choices[picked].index);
                else             SetClampTarget(kind, null, -1);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                string pickLabel = _clampPicking ? "Click a target on the map… (Esc)" : "Pick on Map";
                bool   picking   = GUILayout.Toggle(_clampPicking, new GUIContent(pickLabel,
                    "Then click a river node, a wall tower, an outpost, a structure or a spike on the map."),
                    EditorStyles.miniButton);
                if (picking != _clampPicking) { _clampPicking = picking; Repaint(); }
            }

            if (on && clamp != null && !string.IsNullOrEmpty(clamp.targetId) && !TryClampTargetPosition(clamp, out _))
                EditorGUILayout.HelpBox("Its target is gone — the camera stays where it is.", MessageType.Warning);

            EditorGUI.indentLevel--;
        }
    }
}

#endif
