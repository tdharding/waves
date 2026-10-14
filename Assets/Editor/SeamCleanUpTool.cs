using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR

/// <summary>
/// Seam Clean Up Tool: takes away seam lines the stone shading drew where they aren't wanted.
/// Pick a wall, turn on Edit Seams, and every seam line on it is drawn in the Scene view — click
/// one to remove it, click a removed one to bring it back. The removed seams are listed by chunk,
/// each with an X to bring it back.
///
/// The seams are still made by the generator exactly as before; a removal only stops one being
/// DRAWN. It is remembered on the wall in the Level Select Designer's data as a spot on the wall
/// (see <see cref="WallSeamSpot"/>), so it is found again on every rebuild — and saved with the
/// designer's own Save. A removal that no longer lands on any seam after an edit is listed under
/// Not Found rather than moved onto some other seam.
///
/// Walls only for now; runs and pools are meant to join them here.
/// </summary>
public class SeamCleanUpTool : EditorWindow
{
    [MenuItem("Tools/Level Design/Seam Clean Up Tool", false, 104)]
    public static void Open() => GetWindow<SeamCleanUpTool>("Seam Clean Up");

    const string K = "SeamCleanUpTool.";

    // What clicking in the Scene view does: nothing, remove seams, or group faces.
    enum Mode { Off, Seams, Faces }

    string  _wallId;
    Mode    _mode;
    bool    _showDisplay;
    bool    _hideOverlays;   // Scene view drawing (and clicking) off, mode kept
    Vector2 _scroll;
    readonly HashSet<int> _shutGroups     = new HashSet<int>();   // chunk groups folded shut; -1 = Not Found
    readonly HashSet<int> _shutFaceGroups = new HashSet<int>();   // face groups folded shut

    // The face group clicked faces join.
    int _currentGroup;

    // The line under the mouse in the Scene view, and the spot on it the mouse is nearest — in
    // the wall's own frame, ready to hand the designer.
    int     _hoverLine = -1;
    float   _hoverPixels;
    Vector3 _hoverPoint, _hoverDir;

    // The surface under the mouse in Face Groups, and the face hit — wall frame.
    int     _hoverPatch = -1;
    Vector3 _hoverFacePoint, _hoverFaceNormal;

    // Display — the user's own, kept between sessions.
    float _lineWidth;
    float _pickPixels;
    Color _seamColour, _hoverColour, _removedColour;
    Color _groupColour, _currentGroupColour;

    static readonly int s_Hash = "SeamCleanUpTool".GetHashCode();

    void OnEnable()
    {
        _wallId        = EditorPrefs.GetString(K + "Wall", "");
        _lineWidth     = EditorPrefs.GetFloat(K + "LineWidth", 5f);
        _pickPixels    = EditorPrefs.GetFloat(K + "PickPixels", 10f);
        _seamColour    = LoadColour("SeamColour",    new Color(0.25f, 0.8f, 1f, 1f));
        _hoverColour   = LoadColour("HoverColour",   new Color(1f, 0.85f, 0.2f, 1f));
        _removedColour = LoadColour("RemovedColour", new Color(1f, 0.3f, 0.3f, 0.8f));
        _groupColour        = LoadColour("GroupColour",        new Color(0.3f, 0.9f, 0.5f, 0.3f));
        _currentGroupColour = LoadColour("CurrentGroupColour", new Color(0.3f, 1f, 0.5f, 0.55f));

        SceneView.duringSceneGui    += OnSceneGUI;
        Selection.selectionChanged  += OnSelectionChanged;
        Undo.undoRedoPerformed      += OnUndoRedo;
        OnSelectionChanged();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui    -= OnSceneGUI;
        Selection.selectionChanged  -= OnSelectionChanged;
        Undo.undoRedoPerformed      -= OnUndoRedo;
        SceneView.RepaintAll();
    }

    static Color LoadColour(string key, Color fallback) =>
        ColorUtility.TryParseHtmlString("#" + EditorPrefs.GetString(K + key, ""), out Color c) ? c : fallback;

    static void SaveColour(string key, Color c) => EditorPrefs.SetString(K + key, ColorUtility.ToHtmlStringRGBA(c));

    void SetWall(string id)
    {
        if (_wallId == id) return;
        _wallId    = id;
        _hoverLine  = -1;
        _hoverPatch = -1;
        EditorPrefs.SetString(K + "Wall", id ?? "");
        Repaint();
        SceneView.RepaintAll();
    }

    // Picking a wall chunk in the Hierarchy or the Scene view switches to that wall.
    void OnSelectionChanged()
    {
        var go     = Selection.activeGameObject;
        var record = go != null ? go.GetComponentInParent<WallMesh>() : null;
        if (record == null) return;

        var designer = LevelSelectDesignerWindow.FindOpen();
        if (designer == null || !designer.SeamWalls().Any(w => w.id == record.wallId)) return;
        SetWall(record.wallId);
    }

    void OnUndoRedo()
    {
        LevelSelectDesignerWindow.FindOpen()?.SyncWallSeams(_wallId);
        _hoverLine  = -1;
        _hoverPatch = -1;
        Repaint();
        SceneView.RepaintAll();
    }

    // ─────────────────────────────────────────────
    // WINDOW
    // ─────────────────────────────────────────────

    void OnGUI()
    {
        var designer = LevelSelectDesignerWindow.FindOpen();
        if (designer == null || !designer.HasData)
        {
            EditorGUILayout.HelpBox("Open the Level Select Designer — the removed seams are kept on its walls " +
                                    "and saved with it.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(4);
        var walls = designer.SeamWalls();
        if (walls.Count == 0)
        {
            EditorGUILayout.HelpBox("No walls yet.", MessageType.Info);
            return;
        }

        int current = walls.FindIndex(w => w.id == _wallId);
        if (current < 0) { current = 0; SetWall(walls[0].id); }
        int picked = EditorGUILayout.Popup(new GUIContent("Wall", "Selecting one of a wall's chunks picks it too."),
                                           current, walls.Select(w => new GUIContent(w.label)).ToArray());
        if (picked != current) SetWall(walls[picked].id);

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            ModeButton(Mode.Seams, new GUIContent("Edit Seams",
                "Draw this wall's seam lines in the Scene view. Click one to remove it; click a removed one to bring it back."));
            ModeButton(Mode.Faces, new GUIContent("Face Groups",
                "Click a face in the Scene view: the whole surface it is on joins the current group, and every surface " +
                "in a group gets one even seam fade. Click a grouped surface to take it out."));
        }

        bool hide = EditorGUILayout.ToggleLeft(
            new GUIContent("Hide Overlays", "Hide the lines and tints in the Scene view to see the stone as it is. " +
                                            "Clicking does nothing while they're hidden."), _hideOverlays);
        if (hide != _hideOverlays)
        {
            _hideOverlays = hide;
            _hoverLine    = -1;
            _hoverPatch   = -1;
            SceneView.RepaintAll();
        }

        var report = designer.WallSeams(_wallId);
        var spots  = designer.RemovedWallSeams(_wallId);
        var groups = designer.WallFaceGroups(_wallId);

        if (designer.HasUnsaved)
            EditorGUILayout.LabelField("Saved with the Level Select Designer's Save.", EditorStyles.miniLabel);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField($"Removed Seams  ({spots.Count})", EditorStyles.boldLabel);
        DrawRemovedList(designer, report, spots);

        EditorGUILayout.Space(8);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"Face Groups  ({groups.Count})", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("New Group", "Start a group; faces you click join it."), GUILayout.Width(80)))
            {
                int made = designer.AddWallFaceGroup(_wallId);
                if (made >= 0) _currentGroup = made;
                GUIUtility.ExitGUI();
            }
        }
        DrawFaceGroups(designer, report, groups);

        EditorGUILayout.EndScrollView();

        DrawDisplay();
    }

    // One of the two mode buttons: pressing the one that's on turns it off.
    void ModeButton(Mode mode, GUIContent label)
    {
        bool on  = _mode == mode;
        bool now = GUILayout.Toggle(on, label, "Button", GUILayout.Height(24));
        if (now == on) return;

        _mode       = now ? mode : Mode.Off;
        _hoverLine  = -1;
        _hoverPatch = -1;
        SceneView.RepaintAll();
    }

    void DrawFaceGroups(LevelSelectDesignerWindow designer, WallMesh.SeamReport report, IReadOnlyList<WallFaceGroup> groups)
    {
        if (groups.Count == 0)
        {
            EditorGUILayout.LabelField("None — clicking a face in Face Groups starts one.", EditorStyles.miniLabel);
            return;
        }
        if (_currentGroup >= groups.Count) _currentGroup = groups.Count - 1;

        for (int g = 0; g < groups.Count; g++)
        {
            var spots = groups[g]?.surfaces ?? new List<WallSeamSpot>();
            bool[] found = report != null && g < report.groupFound.Length ? report.groupFound[g] : null;
            float  width = report != null && g < report.groupWidth.Length ? report.groupWidth[g] : 0f;

            using (new EditorGUILayout.HorizontalScope())
            {
                bool current = GUILayout.Toggle(_currentGroup == g,
                    new GUIContent("", "The group faces you click join."), EditorStyles.radioButton, GUILayout.Width(16));
                if (current && _currentGroup != g) { _currentGroup = g; SceneView.RepaintAll(); }

                string title = $"Group {g + 1}  ({spots.Count})" + (width > 0f ? $"  · width {width:0.000} m" : "");
                bool   open  = EditorGUILayout.Foldout(!_shutFaceGroups.Contains(g), title, true);
                if (open) _shutFaceGroups.Remove(g); else _shutFaceGroups.Add(g);

                if (GUILayout.Button(new GUIContent("✕", "Delete this group — its surfaces go back to their own fades."),
                                     GUILayout.Width(22)))
                {
                    designer.DeleteWallFaceGroup(_wallId, g);
                    if (_currentGroup >= g && _currentGroup > 0) _currentGroup--;
                    AfterEdit();
                    GUIUtility.ExitGUI();
                }
            }

            if (_shutFaceGroups.Contains(g)) continue;

            EditorGUI.indentLevel++;
            if (spots.Count == 0) EditorGUILayout.LabelField("Empty.", EditorStyles.miniLabel);
            for (int i = 0; i < spots.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool ok = found != null && i < found.Length && found[i];
                    using (new EditorGUI.DisabledScope(!ok))
                    {
                        var rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
                        string text = (ok ? "" : "Not found · ") + Describe(spots[i]);
                        if (GUI.Button(rect, new GUIContent(text, ok ? "Show this surface in the Scene view." : ""),
                                       EditorStyles.label))
                            FrameSurface(designer, report, g, i);
                    }

                    if (GUILayout.Button(new GUIContent("✕", "Take this surface out of the group."), GUILayout.Width(22)))
                    {
                        designer.RemoveWallGroupFaces(_wallId, g, new[] { i });
                        AfterEdit();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            EditorGUI.indentLevel--;
        }
    }

    void AfterEdit()
    {
        _hoverLine  = -1;
        _hoverPatch = -1;
        Repaint();
        SceneView.RepaintAll();
    }

    void FrameSurface(LevelSelectDesignerWindow designer, WallMesh.SeamReport report, int group, int spot)
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null || report == null) return;

        Vector3 origin = designer.WallFrameOrigin;
        bool    any    = false;
        var     bounds = new Bounds();
        foreach (var patch in report.surfaces)
        {
            if (patch.group != group || patch.spot != spot) continue;
            foreach (var p in patch.corners)
            {
                if (!any) { bounds = new Bounds(p + origin, Vector3.zero); any = true; }
                else bounds.Encapsulate(p + origin);
            }
        }
        if (!any) return;

        bounds.Expand(0.1f);
        view.Frame(bounds, false);
    }

    void DrawRemovedList(LevelSelectDesignerWindow designer, WallMesh.SeamReport report, IReadOnlyList<WallSeamSpot> spots)
    {
        if (spots.Count == 0)
        {
            EditorGUILayout.LabelField("None.", EditorStyles.miniLabel);
            return;
        }

        // Each spot under the first chunk it took a line from; spots that took none are Not Found.
        var groups = new SortedDictionary<int, List<int>>();
        for (int i = 0; i < spots.Count; i++)
        {
            int chunk = int.MaxValue;
            if (report != null)
                foreach (var line in report.lines)
                    if (line.removedBy.Contains(i)) chunk = Mathf.Min(chunk, line.chunk);
            int key = chunk == int.MaxValue ? -1 : chunk;
            if (!groups.TryGetValue(key, out var list)) groups.Add(key, list = new List<int>());
            list.Add(i);
        }

        // Chunks in order, Not Found last.
        var order = groups.Keys.Where(k => k >= 0).ToList();
        if (groups.ContainsKey(-1)) order.Add(-1);

        foreach (int key in order)
        {
            var members = groups[key];
            using (new EditorGUILayout.HorizontalScope())
            {
                string title = key < 0 ? $"Not Found  ({members.Count})" : $"Chunk {key + 1}  ({members.Count})";
                bool   open  = EditorGUILayout.Foldout(!_shutGroups.Contains(key), title, true);
                if (open) _shutGroups.Remove(key); else _shutGroups.Add(key);

                if (GUILayout.Button(new GUIContent("✕", key < 0 ? "Clear every removal that wasn't found."
                                                                 : "Bring back every seam removed in this chunk."),
                                     GUILayout.Width(22)))
                {
                    Restore(designer, members);
                    GUIUtility.ExitGUI();
                }
            }

            if (_shutGroups.Contains(key)) continue;

            EditorGUI.indentLevel++;
            foreach (int i in members)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var  s     = spots[i];
                    bool found = key >= 0;
                    using (new EditorGUI.DisabledScope(!found))
                    {
                        var rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
                        if (GUI.Button(rect, new GUIContent(Describe(s), found ? "Show this seam in the Scene view." : ""),
                                       EditorStyles.label))
                            Frame(designer, report, i);
                    }

                    if (GUILayout.Button(new GUIContent("✕", found ? "Bring this seam back." : "Clear this removal."),
                                         GUILayout.Width(22)))
                    {
                        Restore(designer, new[] { i });
                        GUIUtility.ExitGUI();
                    }
                }
            }
            EditorGUI.indentLevel--;
        }
    }

    static string Describe(WallSeamSpot s) =>
        $"Node {s.node + 1} · {s.along:+0.000;-0.000;0.000} m along · " +
        $"{s.height:0.000} m {(s.fromTop ? "below the top" : "up from the base")}";

    void Restore(LevelSelectDesignerWindow designer, IEnumerable<int> spots)
    {
        designer.RestoreWallSeams(_wallId, spots);
        _hoverLine  = -1;
        _hoverPatch = -1;
        Repaint();
        SceneView.RepaintAll();
    }

    void Frame(LevelSelectDesignerWindow designer, WallMesh.SeamReport report, int spot)
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null || report == null) return;

        Vector3 origin = designer.WallFrameOrigin;
        bool    any    = false;
        var     bounds = new Bounds();
        foreach (var line in report.lines)
        {
            if (!line.removedBy.Contains(spot)) continue;
            foreach (var p in line.points)
            {
                if (!any) { bounds = new Bounds(p + origin, Vector3.zero); any = true; }
                else bounds.Encapsulate(p + origin);
            }
        }
        if (!any) return;

        bounds.Expand(0.1f);
        view.Frame(bounds, false);
    }

    void DrawDisplay()
    {
        EditorGUILayout.Space(6);
        _showDisplay = EditorGUILayout.Foldout(_showDisplay, "Display", true);
        if (!_showDisplay) return;

        EditorGUI.BeginChangeCheck();
        float width   = EditorGUILayout.Slider(new GUIContent("Line Width", "How thick the seam lines are drawn, in pixels."),
                                               _lineWidth, 1f, 16f);
        float pick    = EditorGUILayout.Slider(new GUIContent("Pick Distance", "How near the mouse has to be to a line, in pixels."),
                                               _pickPixels, 2f, 30f);
        Color seam    = EditorGUILayout.ColorField("Seam", _seamColour);
        Color hover   = EditorGUILayout.ColorField("Under Mouse", _hoverColour);
        Color removed = EditorGUILayout.ColorField("Removed", _removedColour);
        Color grouped = EditorGUILayout.ColorField(new GUIContent("Grouped", "Surfaces in a face group."), _groupColour);
        Color currentG = EditorGUILayout.ColorField(new GUIContent("Current Group", "Surfaces in the group clicks join."),
                                                    _currentGroupColour);
        if (!EditorGUI.EndChangeCheck()) return;

        _groupColour = grouped;         SaveColour("GroupColour", grouped);
        _currentGroupColour = currentG; SaveColour("CurrentGroupColour", currentG);

        _lineWidth = width;   EditorPrefs.SetFloat(K + "LineWidth", width);
        _pickPixels = pick;   EditorPrefs.SetFloat(K + "PickPixels", pick);
        _seamColour = seam;       SaveColour("SeamColour", seam);
        _hoverColour = hover;     SaveColour("HoverColour", hover);
        _removedColour = removed; SaveColour("RemovedColour", removed);
        SceneView.RepaintAll();
    }

    // ─────────────────────────────────────────────
    // SCENE VIEW
    // ─────────────────────────────────────────────

    void OnSceneGUI(SceneView view)
    {
        if (_mode == Mode.Off || _hideOverlays) return;
        var designer = LevelSelectDesignerWindow.FindOpen();
        if (designer == null || !designer.HasData) return;
        var report = designer.WallSeams(_wallId);
        if (report == null) return;

        Vector3 origin = designer.WallFrameOrigin;
        Event   e      = Event.current;
        int     id     = GUIUtility.GetControlID(s_Hash, FocusType.Passive);

        if (_mode == Mode.Faces)
        {
            FacesSceneGUI(view, report, origin, id);
            return;
        }

        switch (e.type)
        {
            case EventType.Layout:
                FindHover(view, report, origin);
                if (_hoverLine >= 0) HandleUtility.AddControl(id, _hoverPixels);
                break;

            case EventType.MouseMove:
                view.Repaint();
                break;

            case EventType.MouseDown:
                if (e.button != 0 || e.alt || HandleUtility.nearestControl != id || _hoverLine < 0) break;
                if (_hoverLine >= report.lines.Count) break;

                // The wall is rebuilt — its chunks destroyed and made again — so that waits until
                // the Scene view has finished with this event.
                var    line    = report.lines[_hoverLine];
                string wallId  = _wallId;
                var    restore = new List<int>(line.removedBy);
                Vector3 point  = _hoverPoint, dir = _hoverDir;
                EditorApplication.delayCall += () =>
                {
                    var open = LevelSelectDesignerWindow.FindOpen();
                    if (open == null) return;
                    if (restore.Count > 0) open.RestoreWallSeams(wallId, restore);
                    else                   open.RemoveWallSeam(wallId, point, dir);
                    if (this != null) Repaint();
                    SceneView.RepaintAll();
                };

                _hoverLine = -1;
                e.Use();
                break;

            case EventType.Repaint:
                Draw(view, report, origin);
                break;
        }
    }

    // ── Face Groups ──────────────────────────────

    void FacesSceneGUI(SceneView view, WallMesh.SeamReport report, Vector3 origin, int id)
    {
        Event e = Event.current;
        switch (e.type)
        {
            case EventType.Layout:
                FindHoverFace(report, origin);
                if (_hoverPatch >= 0) HandleUtility.AddControl(id, 0f);
                break;

            case EventType.MouseMove:
                view.Repaint();
                break;

            case EventType.MouseDown:
                if (e.button != 0 || e.alt || HandleUtility.nearestControl != id || _hoverPatch < 0) break;
                if (_hoverPatch >= report.surfaces.Count) break;

                // A grouped surface comes out of its group; any other joins the current one. The
                // wall is rebuilt, so that waits until the Scene view has finished with this event.
                var     patch  = report.surfaces[_hoverPatch];
                string  wallId = _wallId;
                int     group  = _currentGroup;
                Vector3 point  = _hoverFacePoint, normal = _hoverFaceNormal;
                EditorApplication.delayCall += () =>
                {
                    var open = LevelSelectDesignerWindow.FindOpen();
                    if (open == null) return;
                    if (patch.group >= 0) open.RemoveWallGroupFaces(wallId, patch.group, new[] { patch.spot });
                    else                  open.AddWallFaceToGroup(wallId, group, point, normal);
                    if (this == null) return;
                    _currentGroup = Mathf.Clamp(_currentGroup, 0, Mathf.Max(0, open.WallFaceGroups(wallId).Count - 1));
                    Repaint();
                    SceneView.RepaintAll();
                };

                _hoverPatch = -1;
                e.Use();
                break;

            case EventType.Repaint:
                DrawFaces(view, report, origin);
                break;
        }
    }

    // The surface whose face the mouse ray meets first.
    void FindHoverFace(WallMesh.SeamReport report, Vector3 origin)
    {
        _hoverPatch = -1;
        Ray   ray  = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        Vector3 from = ray.origin - origin;
        float best = float.MaxValue;

        for (int j = 0; j < report.surfaces.Count; j++)
        {
            var c = report.surfaces[j].corners;
            for (int i = 0; i + 2 < c.Count; i += 3)
            {
                if (!RayHitsTriangle(from, ray.direction, c[i], c[i + 1], c[i + 2], out float t) || t >= best) continue;
                best             = t;
                _hoverPatch      = j;
                _hoverFacePoint  = from + ray.direction * t;
                _hoverFaceNormal = Vector3.Cross(c[i + 1] - c[i], c[i + 2] - c[i + 1]).normalized;
            }
        }
    }

    // Möller–Trumbore, either side of the face.
    static bool RayHitsTriangle(Vector3 from, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0f;
        Vector3 ab = b - a, ac = c - a;
        Vector3 p  = Vector3.Cross(dir, ac);
        float   det = Vector3.Dot(ab, p);
        if (Mathf.Abs(det) < 1e-12f) return false;

        float   inv = 1f / det;
        Vector3 s   = from - a;
        float   u   = Vector3.Dot(s, p) * inv;
        if (u < 0f || u > 1f) return false;

        Vector3 q = Vector3.Cross(s, ab);
        float   v = Vector3.Dot(dir, q) * inv;
        if (v < 0f || u + v > 1f) return false;

        t = Vector3.Dot(ac, q) * inv;
        return t > 0f;
    }

    void DrawFaces(SceneView view, WallMesh.SeamReport report, Vector3 origin)
    {
        var cam = view.camera;
        if (cam == null) return;
        Vector3 eye = cam.transform.position;

        var zTest = Handles.zTest;
        Handles.zTest = CompareFunction.LessEqual;

        var tri = new Vector3[3];
        for (int j = 0; j < report.surfaces.Count; j++)
        {
            var patch = report.surfaces[j];
            if (j != _hoverPatch && patch.group < 0) continue;

            Handles.color = j == _hoverPatch            ? new Color(_hoverColour.r, _hoverColour.g, _hoverColour.b, 0.45f)
                          : patch.group == _currentGroup ? _currentGroupColour
                                                         : _groupColour;

            var c = patch.corners;
            for (int i = 0; i + 2 < c.Count; i += 3)
            {
                for (int e = 0; e < 3; e++) tri[e] = TowardsEye(c[i + e] + origin, eye);
                Handles.DrawAAConvexPolygon(tri);
            }
        }

        Handles.zTest = zTest;
    }

    // A hair towards the camera, so what's drawn sits on the stone rather than in it.
    static Vector3 TowardsEye(Vector3 p, Vector3 eye)
    {
        Vector3 back = eye - p;
        float   dist = back.magnitude;
        return dist > 1e-4f ? p + back / dist * Mathf.Min(dist * 0.004f, 0.01f) : p;
    }

    // ── Seams ────────────────────────────────────

    // The line nearest the mouse, within Pick Distance, and not hidden behind anything.
    void FindHover(SceneView view, WallMesh.SeamReport report, Vector3 origin)
    {
        _hoverLine  = -1;
        _hoverPatch = -1;
        Vector2 mouse = Event.current.mousePosition;
        var     cam   = view.camera;
        if (cam == null) return;

        var near = new List<(int line, float pixels)>();
        for (int j = 0; j < report.lines.Count; j++)
        {
            var pts = report.lines[j].points;
            if (pts == null || pts.Length < 2) continue;
            var world = new Vector3[pts.Length];
            for (int i = 0; i < pts.Length; i++) world[i] = pts[i] + origin;
            float d = HandleUtility.DistanceToPolyLine(world);
            if (d <= _pickPixels) near.Add((j, d));
        }
        near.Sort((a, b) => a.pixels.CompareTo(b.pixels));

        foreach (var (j, pixels) in near)
        {
            NearestOnLine(report.lines[j].points, origin, mouse, out Vector3 point, out Vector3 dir);
            if (Hidden(cam, point + origin)) continue;

            _hoverLine   = j;
            _hoverPixels = pixels;
            _hoverPoint  = point;
            _hoverDir    = dir;
            return;
        }
    }

    // The point on a line nearest the mouse on screen (wall frame), and which way the line runs there.
    static void NearestOnLine(Vector3[] pts, Vector3 origin, Vector2 mouse, out Vector3 point, out Vector3 dir)
    {
        point = pts[0];
        dir   = (pts[1] - pts[0]).normalized;
        float best = float.MaxValue;
        for (int i = 0; i + 1 < pts.Length; i++)
        {
            Vector2 a = HandleUtility.WorldToGUIPoint(pts[i] + origin);
            Vector2 b = HandleUtility.WorldToGUIPoint(pts[i + 1] + origin);
            Vector2 ab = b - a;
            float   t  = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(mouse - a, ab) / ab.sqrMagnitude) : 0f;
            float   d  = (a + ab * t - mouse).sqrMagnitude;
            if (d >= best) continue;

            Vector3 seg = pts[i + 1] - pts[i];
            if (seg.sqrMagnitude < 1e-12f) continue;
            best  = d;
            point = pts[i] + seg * t;
            dir   = seg.normalized;
        }
    }

    // Something stands between the camera and the point — the far side of the wall, another chunk.
    static bool Hidden(Camera cam, Vector3 point)
    {
        Vector3 from = cam.transform.position;
        Vector3 to   = point - from;
        float   dist = to.magnitude;
        if (dist < 1e-4f) return false;
        float   slack = Mathf.Max(0.003f, dist * 0.01f);
        return Physics.Raycast(from, to / dist, dist - slack, ~0, QueryTriggerInteraction.Ignore);
    }

    void Draw(SceneView view, WallMesh.SeamReport report, Vector3 origin)
    {
        var cam = view.camera;
        if (cam == null) return;
        Vector3 eye = cam.transform.position;

        var zTest = Handles.zTest;
        Handles.zTest = CompareFunction.LessEqual;

        for (int j = 0; j < report.lines.Count; j++)
        {
            var line = report.lines[j];
            if (line.points == null || line.points.Length < 2) continue;

            // Drawn a hair towards the camera, so the line sits on the stone rather than in it.
            var pts = new Vector3[line.points.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 p    = line.points[i] + origin;
                Vector3 back = eye - p;
                float   dist = back.magnitude;
                pts[i] = dist > 1e-4f ? p + back / dist * Mathf.Min(dist * 0.004f, 0.01f) : p;
            }

            Handles.color = j == _hoverLine          ? _hoverColour
                          : line.removedBy.Count > 0 ? _removedColour
                                                     : _seamColour;
            Handles.DrawAAPolyLine(Texture2D.whiteTexture, _lineWidth, pts);
        }

        Handles.zTest = zTest;
    }
}

#endif
