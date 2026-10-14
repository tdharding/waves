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
/// On the canvas a zone is two squares: the sheet (the stronger fill) and its bounds (the fainter
/// one). Click inside a sheet to carry the whole zone, sheet and bounds together; click inside
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

        if (HandleFogZoneDrag(e)) return;

        if (e.type != EventType.MouseDown || e.button != 0) return;

        if (TryPickFogZone(e)) return;

        AddFogZone(CanvasToWorld2D(e.mousePosition));
        e.Use();
    }

    /// <summary>
    /// Picks the zone under the mouse and starts carrying it: the smallest sheet containing the
    /// point carries the whole zone; failing that, the smallest bounds carries only the bounds.
    /// </summary>
    private bool TryPickFogZone(Event e)
    {
        if (_data?.fogZones == null) return false;

        Vector2 p = CanvasToWorld2D(e.mousePosition);

        var sheetHit = _data.fogZones
            .Where(z => z != null && SquareContains(z.centreXZ, new Vector2(z.size, z.size), p))
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
        return true;
    }

    private static bool SquareContains(Vector2 centre, Vector2 size, Vector2 p) =>
        Mathf.Abs(p.x - centre.x) <= size.x * 0.5f && Mathf.Abs(p.y - centre.y) <= size.y * 0.5f;

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
    /// Each zone's bounds as a faint fill and its sheet as a stronger one, the selected zone
    /// brighter. Drawn only in Fog Zones mode.
    /// </summary>
    private void DrawFogZones()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (_data?.fogZones == null || _data.fogZones.Count == 0) return;
        if (_mode != DesignerMode.FogZone) return;

        foreach (var zone in _data.fogZones)
        {
            if (zone == null) continue;
            bool selected = zone.zoneId == _selectedFogZoneId;

            DrawCanvasSquare(zone.boundsCentreXZ, zone.boundsSizeXZ,
                             new Color(0.55f, 0.75f, 1f, selected ? 0.10f : 0.05f),
                             new Color(0.55f, 0.75f, 1f, selected ? 0.70f : 0.35f));

            DrawCanvasSquare(zone.centreXZ, new Vector2(zone.size, zone.size),
                             new Color(0.85f, 0.9f, 1f, selected ? 0.35f : 0.20f),
                             new Color(0.85f, 0.9f, 1f, selected ? 0.90f : 0.50f));

            Vector2 label = WorldToCanvas(new Vector3(zone.centreXZ.x, 0f, zone.centreXZ.y));
            Handles.Label(label, FogZoneLabel(zone), EditorStyles.miniLabel);
        }
    }

    private void DrawCanvasSquare(Vector2 centre, Vector2 size, Color fill, Color outline)
    {
        Vector2 h = size * 0.5f;
        var corners = new Vector3[]
        {
            WorldToCanvas(new Vector3(centre.x - h.x, 0f, centre.y - h.y)),
            WorldToCanvas(new Vector3(centre.x - h.x, 0f, centre.y + h.y)),
            WorldToCanvas(new Vector3(centre.x + h.x, 0f, centre.y + h.y)),
            WorldToCanvas(new Vector3(centre.x + h.x, 0f, centre.y - h.y)),
        };
        Handles.DrawSolidRectangleWithOutline(corners, fill, outline);
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
        EditorGUILayout.LabelField("Click the canvas to drop one. The strong square is the fog " +
                                   "sheet, the faint one its bounds — the boat inside the bounds " +
                                   "shows the sheet. Drag a sheet to carry the whole zone, drag " +
                                   "bounds outside the sheet to move only the bounds. Every sheet " +
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
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("Sheet", EditorStyles.miniBoldLabel);
        Vector2 centre = EditorGUILayout.Vector2Field(
            new GUIContent("Centre", "Centre of the fog sheet on the map, world X and Z."), zone.centreXZ);
        float size = Mathf.Max(0.1f, EditorGUILayout.FloatField(
            new GUIContent("Size", "Width of the square fog sheet, in world units."), zone.size));
        float height = EditorGUILayout.FloatField(
            new GUIContent("Height", "How far this sheet hovers above the landscape surface."), zone.height);
        float edgeFade = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Edge Fade", "How far in from the sheet's edge the fog fades to nothing, " +
                                        "in world units. 0 = a hard edge."), zone.edgeFade));
        int subdivisions = EditorGUILayout.IntSlider(
            new GUIContent("Subdivisions", "How many squares this sheet is divided into along a side. " +
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
        so.FindProperty("bakedMesh").objectReferenceValue = BakeFogZoneSheetMesh(zone, surfaceY);
        so.ApplyModifiedProperties();
        sheet.Generate();
        EditorUtility.SetDirty(sheet);

        // ── the switch ───────────────────────────────────────────────────────
        var fz = root.GetComponent<FogZone>();
        if (fz == null) fz = Undo.AddComponent<FogZone>(root);
        Undo.RecordObject(fz, "Configure Fog Zone");
        fz.Configure(mr, box, zone.size, zone.edgeFade, _data.fogZoneFadeTime);
        EditorUtility.SetDirty(fz);
    }

    /// <summary>
    /// The zone's sheet, shaped by the zone's own settings and saved to Generated: a grid of
    /// Subdivisions squares bent by Landscape Influence to follow the ground's rise and fall from
    /// the zone's centre, then given Smoothing passes.
    ///
    /// The ground is the smooth landscape height — hills and the river lift, not the rocky noise,
    /// which sampled this coarsely would only come out as random spikes.
    /// </summary>
    private Mesh BakeFogZoneSheetMesh(LevelSelectDesignerData.DesignerFogZone zone, float surfaceY)
    {
        int     subs    = Mathf.Clamp(zone.subdivisions, 1, 64);
        int     rows    = subs + 1;
        var     heights = new float[rows * rows];
        float   pull    = Mathf.Clamp01(zone.landscapeInfluence);

        if (pull > 0f)
        {
            // Found the same way the spikes find it: the Height Multiplier and the river edges
            // live on the scene's LandscapeTool, not in the data.
            var   container    = FindHillContainer();
            var   tool         = container != null ? container.parent.GetComponent<LandscapeTool>() : null;
            float globalHeight = tool != null ? tool.heightMultiplier : 1f;

            // Points marked Omit from Fog are left out, so the sheet doesn't rise or dip over them.
            var hills = _data.hillPoints.FindAll(hp => !hp.omitFromFog);

            var ground = LandscapeHillHeight.BuildSurface(
                hills, globalHeight, _data.landscapeNoiseScale,
                tool != null ? tool.riverEdgePoints   : null,
                tool != null ? tool.riverEdgeReach    : null,
                tool != null ? tool.riverEdgeStrength : null,
                tool != null ? tool.riverEdgeShelf    : null);

            // Measured from the ground at the zone's centre, not from the landscape base: the
            // sheet stays at its Height where it was put and only bends with the ground round
            // that point. From the base, ground lifted across the whole zone (a broad hill, the
            // rise to a river rim) lifted the whole sheet off with it.
            float centreGround = ground.SmoothHeight(zone.centreXZ, surfaceY);

            // Same grid, same order as FogSheetMesh.BuildMesh lays its points.
            float step = zone.size / subs;
            float half = zone.size * 0.5f;
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < rows; x++)
            {
                var xz = zone.centreXZ + new Vector2(x * step - half, y * step - half);
                heights[y * rows + x] = pull * (ground.SmoothHeight(xz, surfaceY) - centreGround);
            }

            SmoothFogSheetHeights(heights, rows, Mathf.Max(0, zone.smoothing));
        }

        var mesh = FogSheetMesh.BuildMesh(subs, zone.size, heights);
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
}

#endif
