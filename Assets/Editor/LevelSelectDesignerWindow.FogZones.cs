using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Fog zones — square fog sheets at their own heights, each shown only while the boat is inside
/// its own authored bounds. Used when the Fog section's Plane Mode is Zones; Single (the default)
/// keeps the one landscape-wide sheet exactly as it was.
///
/// Every sheet shows the SAME fog. FogFieldManager paints one field and the sheet material looks
/// it up by world X and Z, so a zone adds one flat mesh, not another simulation. What a zone owns
/// is where its sheet is, how high, how soft its edge is, and the box that switches it — see
/// <see cref="FogZone"/> for the runtime side.
///
/// On the canvas a zone is two squares: the sheet (a fill) and its bounds (a line); in the Scene
/// view, while in Fog Zones mode, the sheet's mesh as a wireframe and the bounds' box. Click inside a sheet to carry the whole zone, sheet and bounds together; click inside
/// bounds but outside the sheet to carry only the bounds.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const string FogZonesParent = "FOG_ZONES";

    private string  _selectedFogZoneId;
    private bool    _isDraggingFogZone;
    private bool    _fogZoneDragBoundsOnly;
    private Vector2 _fogZoneDragOffset;       // world XZ, from the mouse to what is being carried
    private Vector2 _fogZoneListScroll;

    private LevelSelectDesignerData.DesignerFogZone SelectedFogZone =>
        _data?.fogZones?.Find(z => z.zoneId == _selectedFogZoneId);

    // ─────────────────────────────────────────────
    // CANVAS INPUT
    // ─────────────────────────────────────────────

    private void HandleFogZoneMode(Event e)
    {
        if (e.type == EventType.KeyDown &&
            (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
        {
            DeleteFogZone(SelectedFogZone);
            e.Use();
            return;
        }

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.D && SelectedFogZone != null)
        {
            DuplicateSelectedFogZone();
            e.Use();
            return;
        }

        if (HandleFogZoneDrag(e)) return;

        if (e.type != EventType.MouseDown || e.button != 0) return;

        if (TryPickFogZone(e)) return;

        AddFogZone(CanvasToWorld2D(e.mousePosition));
        e.Use();
    }

    /// <summary>
    /// Picks the zone under the mouse and starts carrying it: a handle under the mouse carries
    /// its whole zone, ahead of anything else; then the smallest sheet containing the point
    /// carries the whole zone; failing that, the smallest bounds carries only the bounds.
    /// </summary>
    private bool TryPickFogZone(Event e)
    {
        if (_data?.fogZones == null) return false;

        Vector2 p = CanvasToWorld2D(e.mousePosition);

        var handleHit = _data.fogZones
            .Where(z => z != null && Vector2.Distance(FogZoneHandleCanvas(z), e.mousePosition) <=
                                     FogZoneHandleRadius(z) + 2f)
            .OrderBy(z => Vector2.Distance(FogZoneHandleCanvas(z), e.mousePosition))
            .FirstOrDefault();

        var sheetHit = handleHit ?? _data.fogZones
            .Where(z => z != null && SheetContains(z, p))
            .OrderBy(z => z.size)
            .FirstOrDefault();

        var boundsHit = sheetHit != null ? null : _data.fogZones
            .Where(z => z != null && SquareContains(z.boundsCentreXZ, z.boundsSizeXZ, p))
            .OrderBy(z => z.boundsSizeXZ.x * z.boundsSizeXZ.y)
            .FirstOrDefault();

        var hit = sheetHit ?? boundsHit;
        if (hit == null) return false;

        _selectedFogZoneId     = hit.zoneId;
        _fogZoneDragBoundsOnly = sheetHit == null;
        _fogZoneDragOffset     = (_fogZoneDragBoundsOnly ? hit.boundsCentreXZ : hit.centreXZ) - p;
        _isDraggingFogZone     = true;
        e.Use();
        Repaint();
        SceneView.RepaintAll();
        return true;
    }

    private static bool SheetContains(LevelSelectDesignerData.DesignerFogZone zone, Vector2 p) =>
        zone.shape == FogZoneShape.Circle
            ? Vector2.Distance(zone.centreXZ, p) <= zone.size * 0.5f
            : SquareContains(zone.centreXZ, new Vector2(zone.size, zone.size), p, zone.rotation);

    private static bool SquareContains(Vector2 centre, Vector2 size, Vector2 p, float rotation = 0f)
    {
        // Into the square's own unturned frame.
        Vector3 o = Quaternion.Euler(0f, -rotation, 0f) * new Vector3(p.x - centre.x, 0f, p.y - centre.y);
        return Mathf.Abs(o.x) <= size.x * 0.5f && Mathf.Abs(o.z) <= size.y * 0.5f;
    }

    /// <summary>Moves the carried zone (or its bounds) and lets it go. True when it took the event.</summary>
    private bool HandleFogZoneDrag(Event e)
    {
        if (!_isDraggingFogZone) return false;

        if (e.type == EventType.MouseDrag)
        {
            var zone = SelectedFogZone;
            if (zone != null)
            {
                Undo.RecordObject(_data, "Move Fog Zone");
                Vector2 to = CanvasToWorld2D(e.mousePosition) + _fogZoneDragOffset;
                if (_fogZoneDragBoundsOnly)
                {
                    zone.boundsCentreXZ = to;
                }
                else
                {
                    // The bounds keep where they sit relative to the sheet.
                    zone.boundsCentreXZ += to - zone.centreXZ;
                    zone.centreXZ        = to;
                }
                MarkDirty();
            }
            Repaint();
            e.Use();
            return true;
        }

        if (e.type == EventType.MouseUp)
        {
            // Built once on release rather than on every step of the drag.
            _isDraggingFogZone = false;
            var moved = SelectedFogZone;
            if (moved != null) RebuildFogZone(moved);
            e.Use();
            return true;
        }

        return false;
    }

    private void AddFogZone(Vector2 at)
    {
        Undo.RecordObject(_data, "Add Fog Zone");

        var zone = new LevelSelectDesignerData.DesignerFogZone
        {
            zoneId         = System.Guid.NewGuid().ToString(),
            centreXZ       = at,
            height         = _data.fogHeightOffset,
            boundsCentreXZ = at,
        };
        zone.boundsSizeXZ = new Vector2(zone.size, zone.size);
        _data.fogZones.Add(zone);

        _selectedFogZoneId = zone.zoneId;
        MarkDirty();
        RebuildFogZone(zone);
        Repaint();
    }

    /// <summary>Copies the selected zone, sheet and bounds, set one sheet-width beside it, and selects the copy.</summary>
    private void DuplicateSelectedFogZone()
    {
        var selected = SelectedFogZone;
        if (selected == null) return;

        Undo.RecordObject(_data, "Duplicate Fog Zone");

        var     dupe  = selected.Clone();
        Vector2 shift = new Vector2(selected.size, 0f);
        dupe.zoneId          = System.Guid.NewGuid().ToString();
        dupe.centreXZ       += shift;
        dupe.boundsCentreXZ += shift;

        _data.fogZones.Insert(_data.fogZones.IndexOf(selected) + 1, dupe);
        _selectedFogZoneId = dupe.zoneId;
        _isDraggingFogZone = false;
        MarkDirty();
        RebuildFogZone(dupe);
        Repaint();
        SceneView.RepaintAll();
    }

    private void DeleteFogZone(LevelSelectDesignerData.DesignerFogZone zone)
    {
        if (zone == null) return;

        Undo.RecordObject(_data, "Delete Fog Zone");
        _data.fogZones.Remove(zone);
        if (_selectedFogZoneId == zone.zoneId) _selectedFogZoneId = null;
        _isDraggingFogZone = false;
        MarkDirty();

        var go = FindFogZoneObject(zone.zoneId);
        if (go != null) Undo.DestroyObjectImmediate(go);
        Repaint();
    }

    // ─────────────────────────────────────────────
    // CANVAS DRAWING
    // ─────────────────────────────────────────────

    /// <summary>
    /// Each zone's sheet as a fill and its trigger bounds as a line, the selected zone in its
    /// own colours; both styled in Level Select Settings > Fog Zones. Drawn only in Fog Zones mode.
    /// </summary>
    private void DrawFogZones()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (_mode != DesignerMode.FogZone) return;

        // The structures' repel circles go over the sheets, so the zones don't wash them out.
        if (_data?.fogZones == null || _data.fogZones.Count == 0)
        {
            DrawStructureFogRepelCircles();
            return;
        }

        foreach (var zone in _data.fogZones)
        {
            if (zone == null) continue;
            bool selected = zone.zoneId == _selectedFogZoneId;

            Color fill = selected ? LevelSelectDesignerSettings.FogZoneSelFillColour : LevelSelectDesignerSettings.FogZoneFillColour;
            if (zone.shape == FogZoneShape.Circle)
            {
                Handles.color = fill;
                Handles.DrawSolidDisc(FogZoneHandleCanvas(zone), Vector3.forward, zone.size * 0.5f * _zoom);
            }
            else
            {
                Handles.DrawSolidRectangleWithOutline(
                    CanvasSquareCorners(zone.centreXZ, new Vector2(zone.size, zone.size), zone.rotation),
                    fill, Color.clear);
            }

            var trigger = CanvasSquareCorners(zone.boundsCentreXZ, zone.boundsSizeXZ);
            Handles.color = selected ? LevelSelectDesignerSettings.FogTriggerSelColour : LevelSelectDesignerSettings.FogTriggerColour;
            Handles.DrawAAPolyLine(LevelSelectDesignerSettings.FogTriggerWidth,
                                   trigger[0], trigger[1], trigger[2], trigger[3], trigger[0]);
            Handles.color = Color.white;
        }

        DrawStructureFogRepelCircles();

        // Handles last, over every sheet and bounds, so an overlapped zone can still be grabbed.
        foreach (var zone in _data.fogZones)
        {
            if (zone == null) continue;
            bool    selected = zone.zoneId == _selectedFogZoneId;
            Vector2 at       = FogZoneHandleCanvas(zone);
            float   radius   = FogZoneHandleRadius(zone);

            Handles.color = selected ? LevelSelectDesignerSettings.FogZoneHandleSelColour
                                     : LevelSelectDesignerSettings.FogZoneHandleColour;
            Handles.DrawSolidDisc(at, Vector3.forward, radius);
            Handles.color = Color.white;

            Handles.Label(at + new Vector2(radius + 3f, -7f), FogZoneLabel(zone), EditorStyles.miniLabel);
        }
    }

    // The handle sits on the sheet's centre, a fixed size on screen whatever the zoom.
    private Vector2 FogZoneHandleCanvas(LevelSelectDesignerData.DesignerFogZone zone) =>
        WorldToCanvas(new Vector3(zone.centreXZ.x, 0f, zone.centreXZ.y));

    private float FogZoneHandleRadius(LevelSelectDesignerData.DesignerFogZone zone) =>
        zone.zoneId == _selectedFogZoneId ? LevelSelectDesignerSettings.FogZoneHandleSelSize
                                          : LevelSelectDesignerSettings.FogZoneHandleSize;

    private Vector3[] CanvasSquareCorners(Vector2 centre, Vector2 size, float rotation = 0f)
    {
        Vector2 h    = size * 0.5f;
        var     turn = Quaternion.Euler(0f, rotation, 0f);
        var     c    = new Vector3(centre.x, 0f, centre.y);
        return new Vector3[]
        {
            WorldToCanvas(c + turn * new Vector3(-h.x, 0f, -h.y)),
            WorldToCanvas(c + turn * new Vector3(-h.x, 0f,  h.y)),
            WorldToCanvas(c + turn * new Vector3( h.x, 0f,  h.y)),
            WorldToCanvas(c + turn * new Vector3( h.x, 0f, -h.y)),
        };
    }

    // ─────────────────────────────────────────────
    // SCENE VIEW
    // ─────────────────────────────────────────────

    // Each sheet mesh's edges as index pairs, worked out once per mesh rather than every repaint;
    // the triangle count tells a re-baked mesh from the one the edges came from.
    private readonly System.Collections.Generic.Dictionary<Mesh, (int tris, int[] edges)> _fogSheetEdges =
        new System.Collections.Generic.Dictionary<Mesh, (int, int[])>();

    private void FogZoneSceneEnable()  => SceneView.duringSceneGui += DrawFogZonesInScene;
    private void FogZoneSceneDisable() => SceneView.duringSceneGui -= DrawFogZonesInScene;

    /// <summary>
    /// While in Fog Zones mode, every zone's sheet mesh as a wireframe in the Scene view, the
    /// selected zone's in its own colour and with its trigger box as lines. Styled in Level Select
    /// Settings > Fog Zones.
    /// </summary>
    private void DrawFogZonesInScene(SceneView view)
    {
        if (Event.current.type != EventType.Repaint) return;
        if (_mode != DesignerMode.FogZone || _data?.fogZones == null) return;

        var prevMatrix = Handles.matrix;
        var prevColour = Handles.color;

        foreach (var zone in _data.fogZones)
        {
            if (zone == null) continue;
            var root = FindFogZoneObject(zone.zoneId);
            if (root == null) continue;
            bool selected = zone.zoneId == _selectedFogZoneId;

            var sheet = root.transform.Find("Sheet");
            var filter = sheet != null ? sheet.GetComponent<MeshFilter>() : null;
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null)
            {
                Handles.matrix = sheet.localToWorldMatrix;
                Handles.color  = selected ? LevelSelectDesignerSettings.FogSceneMeshSelColour
                                          : LevelSelectDesignerSettings.FogSceneMeshColour;
                Handles.DrawLines(mesh.vertices, FogSheetEdges(mesh));
            }

            // The bounds' box only for the selected zone.
            var box = selected ? root.GetComponent<BoxCollider>() : null;
            if (box != null)
            {
                Handles.matrix = root.transform.localToWorldMatrix;
                Handles.color  = LevelSelectDesignerSettings.FogTriggerSelColour;
                DrawWireBox(box.center, box.size, LevelSelectDesignerSettings.FogTriggerWidth);
            }
        }

        Handles.matrix = prevMatrix;
        Handles.color  = prevColour;
    }

    private int[] FogSheetEdges(Mesh mesh)
    {
        int[] tris = mesh.triangles;
        if (_fogSheetEdges.TryGetValue(mesh, out var held) && held.tris == tris.Length) return held.edges;

        // Each shared edge once, however many triangles it borders.
        var seen  = new System.Collections.Generic.HashSet<long>();
        var edges = new System.Collections.Generic.List<int>(tris.Length * 2);
        for (int t = 0; t < tris.Length; t += 3)
            for (int k = 0; k < 3; k++)
            {
                int a = tris[t + k], b = tris[t + (k + 1) % 3];
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!seen.Add(key)) continue;
                edges.Add(a);
                edges.Add(b);
            }

        var result = edges.ToArray();
        _fogSheetEdges[mesh] = (tris.Length, result);
        return result;
    }

    private static void DrawWireBox(Vector3 centre, Vector3 size, float thickness)
    {
        Vector3 h = size * 0.5f;
        Vector3 C(float x, float y, float z) => centre + new Vector3(h.x * x, h.y * y, h.z * z);
        Vector3[] lo = { C(-1, -1, -1), C(-1, -1, 1), C(1, -1, 1), C(1, -1, -1) };
        Vector3[] hi = { C(-1,  1, -1), C(-1,  1, 1), C(1,  1, 1), C(1,  1, -1) };
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            Handles.DrawLine(lo[i], lo[j], thickness);
            Handles.DrawLine(hi[i], hi[j], thickness);
            Handles.DrawLine(lo[i], hi[i], thickness);
        }
    }

    private string FogZoneLabel(LevelSelectDesignerData.DesignerFogZone zone) =>
        $"Fog Zone {_data.fogZones.IndexOf(zone) + 1}";

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    /// <summary>The fog zones, listed with the selected one opened up. Shown in Fog Zones mode.</summary>
    private void DrawFogZonePanel()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Fog Zones", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Click the canvas to drop one. The filled shape is the fog " +
                                   "sheet, the outlined square its bounds — the boat inside the bounds " +
                                   "shows the sheet. Drag a sheet's centre handle (or the sheet) to " +
                                   "carry the whole zone, drag bounds outside the sheet to move only " +
                                   "the bounds. D duplicates the selected zone. Every sheet " +
                                   "shows the same fog; the Fog Map and Fade Time are in Setup > Fog.",
                                   EditorStyles.wordWrappedMiniLabel);

        if (_data.fogPlaneMode != FogPlaneMode.Zones)
            EditorGUILayout.HelpBox(
                "This world's Fog Plane Mode is Single, so zones are not built into the scene. " +
                "Set it to Zones in Setup > Fog to use them.", MessageType.Info);

        EditorGUILayout.Space(4);

        if (_data.fogZones.Count == 0)
        {
            EditorGUILayout.LabelField("None yet.", EditorStyles.miniLabel);
            return;
        }

        _fogZoneListScroll = EditorGUILayout.BeginScrollView(_fogZoneListScroll, GUILayout.MaxHeight(360f));

        LevelSelectDesignerData.DesignerFogZone toDelete = null;

        foreach (var zone in _data.fogZones)
        {
            if (zone == null) continue;

            bool open = zone.zoneId == _selectedFogZoneId;

            using (new EditorGUILayout.HorizontalScope())
            {
                string label = FogZoneLabel(zone);
                if (GUILayout.Button(open ? $"▾ {label}" : $"▸ {label}", EditorStyles.miniButton))
                {
                    _selectedFogZoneId = open ? null : zone.zoneId;
                    Repaint();
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22f)))
                    toDelete = zone;
            }

            if (!open) continue;

            EditorGUI.indentLevel++;
            DrawFogZoneFields(zone);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndScrollView();

        if (toDelete != null) DeleteFogZone(toDelete);

        EditorGUILayout.Space(2);
        if (GUILayout.Button("Rebuild Fog Zones", EditorStyles.miniButton))
            RebuildFogZones();
    }

    private void DrawFogZoneFields(LevelSelectDesignerData.DesignerFogZone zone)
    {
        EditorGUILayout.LabelField("Sheet", EditorStyles.miniBoldLabel);

        // A dropdown, so it rebuilds at once rather than after the Rebuild Delay.
        EditorGUI.BeginChangeCheck();
        var shape = (FogZoneShape)EditorGUILayout.EnumPopup(
            new GUIContent("Shape", "The fog sheet's outline: a circle or a square."), zone.shape);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Fog Zone Shape");
            zone.shape = shape;
            MarkDirty();
            RebuildFogZone(zone);
            Repaint();
            SceneView.RepaintAll();
        }

        bool round = zone.shape == FogZoneShape.Circle;

        EditorGUI.BeginChangeCheck();

        Vector2 centre = EditorGUILayout.Vector2Field(
            new GUIContent("Centre", "Centre of the fog sheet on the map, world X and Z."), zone.centreXZ);
        float size = Mathf.Max(0.1f, EditorGUILayout.FloatField(
            new GUIContent("Size", round ? "Diameter of the round fog sheet, in world units."
                                         : "Width of the square fog sheet, in world units."), zone.size));
        // A circle looks the same at any turn, so only a square shows it.
        float rotation = !round ? EditorGUILayout.Slider(
            new GUIContent("Rotation", "How far the sheet is turned round its centre, in degrees. " +
                                       "The bounds stay square to the map."),
            zone.rotation, -180f, 180f) : zone.rotation;
        float height = EditorGUILayout.FloatField(
            new GUIContent("Height", "How far this sheet hovers above the landscape surface."), zone.height);
        float edgeFade = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Edge Fade", "How far in from the sheet's edge the fog fades to nothing, " +
                                        "in world units. 0 = a hard edge."), zone.edgeFade));
        int subdivisions = EditorGUILayout.IntSlider(
            new GUIContent(round ? "Rings" : "Subdivisions",
                           round ? "How many rings this sheet is divided into from its centre out to " +
                                   "its rim. Only matters once Landscape Influence is above 0."
                                 : "How many squares this sheet is divided into along a side. " +
                                   "Only matters once Landscape Influence is above 0."),
            zone.subdivisions, 1, 64);
        float landscapeInfluence = EditorGUILayout.Slider(
            new GUIContent("Landscape Influence", "How much this sheet follows the landscape under it. " +
                                                  "0 = flat at its height; 1 = its height above the ground " +
                                                  "at every point, rising and falling with the hills."),
            zone.landscapeInfluence, 0f, 1f);
        int smoothing = EditorGUILayout.IntSlider(
            new GUIContent("Smoothing", "Mesh smoothing passes, rounding off the creases between the " +
                                        "squares. 0 = none."),
            zone.smoothing, 0, 20);

        EditorGUILayout.LabelField("Bounds", EditorStyles.miniBoldLabel);
        Vector2 boundsCentre = EditorGUILayout.Vector2Field(
            new GUIContent("Centre", "Centre of the zone's bounds, world X and Z."), zone.boundsCentreXZ);
        Vector2 boundsSize = EditorGUILayout.Vector2Field(
            new GUIContent("Size", "Width (X) and depth (Z) of the zone's bounds, in world units."),
            zone.boundsSizeXZ);
        boundsSize = new Vector2(Mathf.Max(0.1f, boundsSize.x), Mathf.Max(0.1f, boundsSize.y));

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Fog Zone");
            zone.centreXZ       = centre;
            zone.size           = size;
            zone.rotation       = rotation;
            zone.height         = height;
            zone.edgeFade       = edgeFade;
            zone.subdivisions       = subdivisions;
            zone.landscapeInfluence = landscapeInfluence;
            zone.smoothing          = smoothing;
            zone.boundsCentreXZ = boundsCentre;
            zone.boundsSizeXZ   = boundsSize;
            MarkDirty();
            RebuildAfterEdit(zone, () => RebuildFogZone(zone));
            Repaint();
        }
    }

    // ─────────────────────────────────────────────
    // GENERATION
    // ─────────────────────────────────────────────

    private static string FogZoneObjectName(string zoneId) => $"FogZone_{SanitiseAssetName(zoneId)}";

    /// <summary>
    /// FOG_ZONES, found even while Single mode has it switched off — GameObject.Find does not see
    /// inactive objects, and a second parent made beside a hidden one would split the zones.
    /// </summary>
    private static GameObject FogZonesRoot(bool create)
    {
        var found = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
            .FirstOrDefault(g => g.name == FogZonesParent);
        if (found != null || !create) return found;
        return FindOrCreateParent(FogZonesParent);
    }

    private GameObject FindFogZoneObject(string zoneId)
    {
        var parent = FogZonesRoot(false);
        if (parent == null) return null;
        var child = parent.transform.Find(FogZoneObjectName(zoneId));
        return child != null ? child.gameObject : null;
    }

    /// <summary>
    /// Builds every zone afresh and applies the plane mode: in Zones the zones are on and the
    /// single landscape sheet is switched off, in Single the other way round. Nothing is deleted
    /// by switching, so going back is lossless. A world with no zones and no FOG_ZONES parent is
    /// left alone rather than given an empty one.
    /// </summary>
    private void RebuildFogZones()
    {
        if (_data == null) return;

        bool anyZones = _data.fogZones != null && _data.fogZones.Count > 0;
        var  parent   = FogZonesRoot(false);

        if (anyZones || parent != null)
        {
            if (parent == null) parent = FogZonesRoot(true);

            var wanted = new System.Collections.Generic.HashSet<string>(
                _data.fogZones.Where(z => z != null).Select(z => FogZoneObjectName(z.zoneId)));

            for (int i = parent.transform.childCount - 1; i >= 0; i--)
            {
                var child = parent.transform.GetChild(i).gameObject;
                if (!wanted.Contains(child.name)) Undo.DestroyObjectImmediate(child);
            }

            foreach (var zone in _data.fogZones)
                if (zone != null) BuildFogZone(zone, parent);
        }

        ApplyFogPlaneMode();
    }

    /// <summary>Rebuilds one zone, in place.</summary>
    private void RebuildFogZone(LevelSelectDesignerData.DesignerFogZone zone)
    {
        if (zone == null) return;
        BuildFogZone(zone, FogZonesRoot(true));
        ApplyFogPlaneMode();
    }

    /// <summary>
    /// Zones mode switches the zones on and the single sheet off; Single the reverse. Both only
    /// SetActive, so either can be switched back without anything being rebuilt.
    /// </summary>
    private void ApplyFogPlaneMode()
    {
        bool zones = _data.fogPlaneMode == FogPlaneMode.Zones;

        var parent = FogZonesRoot(false);
        if (parent != null && parent.activeSelf != zones)
        {
            Undo.RecordObject(parent, "Fog Plane Mode");
            parent.SetActive(zones);
        }

        var single = FindSingleFogSheet();
        if (single != null && single.gameObject.activeSelf == zones)
        {
            Undo.RecordObject(single.gameObject, "Fog Plane Mode");
            single.gameObject.SetActive(!zones);
        }
    }

    /// <summary>The landscape-wide sheet, found even while switched off, and never a zone's.</summary>
    private static FogSheetMesh FindSingleFogSheet()
    {
        var scripts = FindParent("LEVELSELECT_SCRIPTS");
        if (scripts == null) return null;
        return scripts.GetComponentsInChildren<FogSheetMesh>(true)
                      .FirstOrDefault(s => s.GetComponentInParent<FogZone>(true) == null);
    }

    private void BuildFogZone(LevelSelectDesignerData.DesignerFogZone zone, GameObject parent)
    {
        string name    = FogZoneObjectName(zone.zoneId);
        float  surfaceY = _data.LandscapeCentre.y;

        var root = FindFogZoneObject(zone.zoneId);
        if (root == null)
        {
            root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, "Generate Fog Zone");
            root.transform.SetParent(parent.transform, false);
        }

        Undo.RecordObject(root.transform, "Place Fog Zone");
        root.transform.position   = new Vector3(zone.centreXZ.x, surfaceY, zone.centreXZ.y);
        root.transform.rotation   = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        // Ignore Raycast, so the boat's water raycasts go straight through the bounds.
        root.layer = 2;

        // ── bounds ───────────────────────────────────────────────────────────
        // Only X and Z decide anything; the height is just tall enough to be visible round the
        // sheet in the scene view.
        var box = root.GetComponent<BoxCollider>();
        if (box == null) box = Undo.AddComponent<BoxCollider>(root);
        Undo.RecordObject(box, "Fog Zone Bounds");
        float boxH = Mathf.Max(Mathf.Abs(zone.height) + 1f, 1f);
        box.isTrigger = true;
        box.center    = new Vector3(zone.boundsCentreXZ.x - zone.centreXZ.x,
                                    zone.height * 0.5f,
                                    zone.boundsCentreXZ.y - zone.centreXZ.y);
        box.size      = new Vector3(zone.boundsSizeXZ.x, boxH, zone.boundsSizeXZ.y);

        // ── sheet ────────────────────────────────────────────────────────────
        var sheetTf = root.transform.Find("Sheet");
        GameObject sheetGo;
        if (sheetTf != null) sheetGo = sheetTf.gameObject;
        else
        {
            sheetGo = new GameObject("Sheet");
            Undo.RegisterCreatedObjectUndo(sheetGo, "Generate Fog Zone Sheet");
            sheetGo.transform.SetParent(root.transform, false);
        }
        sheetGo.layer = 2;
        sheetGo.transform.position = new Vector3(zone.centreXZ.x, surfaceY + zone.height, zone.centreXZ.y);

        var sheet = sheetGo.GetComponent<FogSheetMesh>();
        if (sheet == null) sheet = Undo.AddComponent<FogSheetMesh>(sheetGo);

        var mr = sheetGo.GetComponent<MeshRenderer>();
        if (mr != null && mr.sharedMaterial == null)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(FOG_SHEET_MAT);
            if (mat != null) mr.sharedMaterial = mat;
            else Debug.LogWarning($"[LevelSelectDesigner] {FOG_SHEET_MAT} missing — fog zone sheets have no material and nothing will draw.");
        }

        // Its own size, not the landscape's, and its own spot rather than a wave plane's.
        var so = new SerializedObject(sheet);
        so.Update();
        so.FindProperty("matchFieldCoverage").boolValue   = false;
        so.FindProperty("size").floatValue                = zone.size;
        so.FindProperty("matchWavePlaneCentre").boolValue = false;
        so.FindProperty("waterlineY").floatValue          = surfaceY;
        so.FindProperty("heightOffset").floatValue        = zone.height;
        so.FindProperty("circular").boolValue             = zone.shape == FogZoneShape.Circle;
        so.FindProperty("bakedMesh").objectReferenceValue = BakeFogZoneSheetMesh(zone, surfaceY);
        so.ApplyModifiedProperties();
        sheet.Generate();
        EditorUtility.SetDirty(sheet);

        // ── the switch ───────────────────────────────────────────────────────
        var fz = root.GetComponent<FogZone>();
        if (fz == null) fz = Undo.AddComponent<FogZone>(root);
        Undo.RecordObject(fz, "Configure Fog Zone");
        fz.Configure(mr, box, zone.size, zone.edgeFade, _data.fogZoneFadeTime, zone.shape);
        EditorUtility.SetDirty(fz);
    }

    /// <summary>
    /// The zone's sheet, shaped by the zone's own settings and saved to Generated: a grid of
    /// Subdivisions squares (a square sheet) or that many rings out from the centre (a round one),
    /// bent by Landscape Influence to follow the ground's rise and fall from the zone's centre,
    /// then given Smoothing passes.
    ///
    /// The ground is the smooth landscape height — hills and the river lift, not the rocky noise,
    /// which sampled this coarsely would only come out as random spikes.
    /// </summary>
    private Mesh BakeFogZoneSheetMesh(LevelSelectDesignerData.DesignerFogZone zone, float surfaceY)
    {
        int     subs    = Mathf.Clamp(zone.subdivisions, 1, 64);
        int     rows    = subs + 1;
        bool    round   = zone.shape == FogZoneShape.Circle;
        int     segs    = FogSheetMesh.CircleSegments(subs);
        var     heights = new float[round ? 1 + subs * segs : rows * rows];
        float   pull    = Mathf.Clamp01(zone.landscapeInfluence);

        if (pull > 0f)
        {
            // Found the same way the spikes find it: the Height Multiplier and the river edges
            // live on the scene's LandscapeTool, not in the data.
            var   container    = FindHillContainer();
            var   tool         = container != null ? container.parent.GetComponent<LandscapeTool>() : null;
            float globalHeight = tool != null ? tool.heightMultiplier : 1f;

            // Points marked Omit from Fog are left out, so the sheet doesn't rise or dip over them.
            var hills = ActiveHillPoints.FindAll(hp => !hp.omitFromFog);

            var ground = LandscapeHillHeight.BuildSurface(
                hills, globalHeight, _data.landscapeNoiseScale,
                tool != null ? tool.riverEdgePoints   : null,
                tool != null ? tool.riverEdgeReach    : null,
                tool != null ? tool.riverEdgeStrength : null,
                tool != null ? tool.riverEdgeShelf    : null,
                null, null,
                tool != null ? tool.maps              : null,
                tool != null ? tool.mapArea           : default);

            // Measured from the ground at the zone's centre, not from the landscape base: the
            // sheet stays at its Height where it was put and only bends with the ground round
            // that point. From the base, ground lifted across the whole zone (a broad hill, the
            // rise to a river rim) lifted the whole sheet off with it.
            float centreGround = ground.SmoothHeight(zone.centreXZ, surfaceY);

            if (round)
            {
                // Same rings, same order as FogSheetMesh.BuildCircleMesh lays its points.
                for (int k = 0; k <= subs; k++)
                for (int s = 0; s < (k == 0 ? 1 : segs); s++)
                {
                    var xz = zone.centreXZ + FogSheetMesh.CirclePoint(k, s, subs, zone.size);
                    heights[k == 0 ? 0 : 1 + (k - 1) * segs + s] =
                        pull * (ground.SmoothHeight(xz, surfaceY) - centreGround);
                }

                SmoothRoundFogSheetHeights(heights, subs, segs, Mathf.Max(0, zone.smoothing));
            }
            else
            {
                // Same grid, same order and same turn as FogSheetMesh.BuildMesh lays its points.
                float step = zone.size / subs;
                float half = zone.size * 0.5f;
                var   turn = Quaternion.Euler(0f, zone.rotation, 0f);
                for (int y = 0; y < rows; y++)
                for (int x = 0; x < rows; x++)
                {
                    Vector3 o  = turn * new Vector3(x * step - half, 0f, y * step - half);
                    var     xz = zone.centreXZ + new Vector2(o.x, o.z);
                    heights[y * rows + x] = pull * (ground.SmoothHeight(xz, surfaceY) - centreGround);
                }

                SmoothFogSheetHeights(heights, rows, Mathf.Max(0, zone.smoothing));
            }
        }

        var mesh = round ? FogSheetMesh.BuildCircleMesh(subs, zone.size, heights)
                         : FogSheetMesh.BuildMesh(subs, zone.size, heights, zone.rotation);
        return SaveGeneratedMesh($"FogZoneSheet_{SanitiseAssetName(zone.zoneId)}", mesh);
    }

    /// <summary>
    /// Mesh smoothing: each pass moves every point half way to the average of the points beside
    /// it (edge and corner points have fewer), rounding off the creases between squares.
    /// </summary>
    private static void SmoothFogSheetHeights(float[] heights, int rows, int passes)
    {
        var next = new float[heights.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < rows; x++)
            {
                float sum = 0f;
                int   n   = 0;
                if (x > 0)        { sum += heights[y * rows + x - 1];   n++; }
                if (x < rows - 1) { sum += heights[y * rows + x + 1];   n++; }
                if (y > 0)        { sum += heights[(y - 1) * rows + x]; n++; }
                if (y < rows - 1) { sum += heights[(y + 1) * rows + x]; n++; }

                float h = heights[y * rows + x];
                next[y * rows + x] = n > 0 ? h + 0.5f * (sum / n - h) : h;
            }
            System.Array.Copy(next, heights, heights.Length);
        }
    }

    /// <summary>
    /// The same mesh smoothing on a round sheet: a point's neighbours are the points either side
    /// round its ring and the ones in and out along its spoke. The centre's are the whole first
    /// ring; the rim has no ring outside it.
    /// </summary>
    private static void SmoothRoundFogSheetHeights(float[] heights, int rings, int segs, int passes)
    {
        int Index(int k, int s) => k == 0 ? 0 : 1 + (k - 1) * segs + (s + segs) % segs;

        var next = new float[heights.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            float ring1 = 0f;
            for (int s = 0; s < segs; s++) ring1 += heights[Index(1, s)];
            next[0] = heights[0] + 0.5f * (ring1 / segs - heights[0]);

            for (int k = 1; k <= rings; k++)
            for (int s = 0; s < segs; s++)
            {
                float sum = heights[Index(k, s - 1)] + heights[Index(k, s + 1)] + heights[Index(k - 1, s)];
                int   n   = 3;
                if (k < rings) { sum += heights[Index(k + 1, s)]; n++; }

                float h = heights[Index(k, s)];
                next[Index(k, s)] = h + 0.5f * (sum / n - h);
            }
            System.Array.Copy(next, heights, heights.Length);
        }
    }
}

#endif
