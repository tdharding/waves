using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Procedural spikes in the landscape — the Grid Designer's generated rocks, brought onto the map.
///
/// In a level a spike is a rock standing in water, built with its surface line at y = 0. On the
/// map there is no water around it: it is part of the landscape, so that y = 0 goes on the
/// landscape's surface right under the spike — the tile base (World Y + Height Offset) plus
/// whatever the hills lift or sink it by there, read off <see cref="LandscapeHillHeight"/> — and
/// whatever the preset puts below it is buried. The hills are raised in the shader, so nothing in
/// the scene has that height to raycast against; it is worked out from the hill list instead.
/// Spikes are re-seated whenever the hills are synced, so they ride a hill as it is edited.
///
/// Shape comes from a Spike Studio preset, so the carve and twist are the level's exactly. The
/// look is the landscape's: <c>LevelSelectSpikeMat</c> wears the variant picked under Spikes in
/// the Level Select Landscape Tuner, with its grain, the landscape light and the spiral grooves
/// darkened. Scenery only — no boat collider. The tip can be an angel perch.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const string SpikesParent        = "SPIKES";
    private const string DefaultSpikeMatPath =
        "Assets/TextureMatShader/LevelSelectMaterials/LevelSelectSpikeMat.mat";

    private string  _selectedSpikeId;
    private bool    _isDraggingSpike;
    private Vector2 _spikeDragOffset;

    // What the next spike dropped on the canvas wears — the same preset + size pair the Grid
    // Designer's ▲ Spikes panel places with.
    private SpikeShapePreset _spikeBrushPreset;
    private float            _spikeBrushScale = 1f;

    private LevelSelectDesignerData.DesignerSpike SelectedSpike =>
        _data?.spikes?.Find(s => s.spikeId == _selectedSpikeId);

    private float SpikeBaselineY => _data.landscapeWorldY + _data.landscapeHeightOffset;

    /// <summary>
    /// The landscape's surface under this spike's centre: the tile base plus the hills there,
    /// smooth height and rocky noise both, exactly as the shader raises them.
    /// </summary>
    private float SpikeGroundY(LevelSelectDesignerData.DesignerSpike spike)
    {
        // The Height Multiplier lives on the scene's LandscapeTool, not in the data; found the
        // same way the hill sync finds it.
        var   container    = FindHillContainer();
        var   tool         = container != null ? container.parent.GetComponent<LandscapeTool>() : null;
        float globalHeight = tool != null ? tool.heightMultiplier : 1f;

        // The river edges too, so a spike beside a run with Landscape Influence stands on the
        // lifted ground.
        return SpikeBaselineY + LandscapeHillHeight.At(_data.hillPoints, spike.positionXZ,
                                                        globalHeight, _data.landscapeNoiseScale,
                                                        tool != null ? tool.riverEdgePoints : null,
                                                        tool != null ? tool.riverEdgeReach  : null,
                                                        SpikeBaselineY);
    }

    // ─────────────────────────────────────────────
    // CANVAS INPUT
    // ─────────────────────────────────────────────

    private void HandleSpikeMode(Event e)
    {
        if (e.type == EventType.KeyDown &&
            (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
        {
            DeleteSelectedSpike();
            e.Use();
            return;
        }

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.D && SelectedSpike != null)
        {
            DuplicateSelectedSpike();
            e.Use();
            return;
        }

        if (HandleSpikeDrag(e)) return;

        if (e.type != EventType.MouseDown || e.button != 0) return;

        // A click on one already there picks it up rather than stacking another on it.
        if (TryPickSpike(e)) return;

        AddSpike(e.mousePosition);
        e.Use();
    }

    /// <summary>Removes the selected spike, from the data and the scene. Shared with Select mode.</summary>
    private void DeleteSelectedSpike()
    {
        var selected = SelectedSpike;
        if (selected == null) return;

        Undo.RecordObject(_data, "Delete Spike");
        _data.spikes.Remove(selected);
        _selectedSpikeId = null;
        _isDraggingSpike = false;
        MarkDirty();
        RemoveSpikeObject(selected.spikeId);
        Repaint();
    }

    /// <summary>
    /// Copies the selected spike — shape, size and perch block — stood just beside it, and selects
    /// the copy. Shared with Select mode.
    /// </summary>
    private void DuplicateSelectedSpike()
    {
        var selected = SelectedSpike;
        if (selected == null) return;

        Undo.RecordObject(_data, "Duplicate Spike");

        var dupe = selected.Clone();
        dupe.spikeId     = System.Guid.NewGuid().ToString();
        dupe.positionXZ += new Vector2(SpikeFootprint(selected) * 2f, 0f);
        // Only one perch should start her on the map — the copy doesn't take that over.
        if (dupe.perch != null) dupe.perch.startPerch = false;

        _data.spikes.Insert(_data.spikes.IndexOf(selected) + 1, dupe);
        _selectedSpikeId = dupe.spikeId;
        _isDraggingSpike = false;
        MarkDirty();
        RebuildSpike(dupe);
        Repaint();
    }

    /// <summary>Selects the spike under the mouse and starts carrying it. Shared with Select mode.</summary>
    private bool TryPickSpike(Event e)
    {
        if (!FindSpikeAtCanvas(e.mousePosition, out string hit)) return false;

        _selectedSpikeId = hit;
        var picked = SelectedSpike;
        _spikeDragOffset = e.mousePosition -
                           WorldToCanvas(new Vector3(picked.positionXZ.x, 0f, picked.positionXZ.y));
        _isDraggingSpike = true;
        e.Use();
        Repaint();
        return true;
    }

    /// <summary>Moves the carried spike and lets it go. True when it took the event.</summary>
    private bool HandleSpikeDrag(Event e)
    {
        if (!_isDraggingSpike) return false;

        if (e.type == EventType.MouseDrag)
        {
            var dragged = SelectedSpike;
            if (dragged != null)
            {
                Undo.RecordObject(_data, "Move Spike");
                dragged.positionXZ = CanvasToWorld2D(e.mousePosition - _spikeDragOffset);
                MarkDirty();
            }
            Repaint();
            e.Use();
            return true;
        }

        if (e.type == EventType.MouseUp)
        {
            // Placed once on release rather than on every step of the drag. The shape has not
            // changed, so the rock is only moved, not rebuilt.
            _isDraggingSpike = false;
            var moved = SelectedSpike;
            if (moved != null) PlaceSpikeObject(moved);
            e.Use();
            return true;
        }

        return false;
    }

    private void AddSpike(Vector2 canvas)
    {
        Undo.RecordObject(_data, "Add Spike");

        // A GUID, not a count: the mesh asset is named off it, and the Generated folder is shared
        // by every world, so "spike1" in two worlds would build over each other.
        string id = System.Guid.NewGuid().ToString();

        var spike = new LevelSelectDesignerData.DesignerSpike
        {
            spikeId    = id,
            positionXZ = CanvasToWorld2D(canvas),
            preset     = _spikeBrushPreset != null ? _spikeBrushPreset : SpikePresets().FirstOrDefault(),
            scale      = Mathf.Max(0.01f, _spikeBrushScale),
        };
        _data.spikes.Add(spike);

        _selectedSpikeId = id;
        MarkDirty();
        RebuildSpike(spike);
        Repaint();
    }

    private bool FindSpikeAtCanvas(Vector2 canvas, out string spikeId)
    {
        spikeId = null;
        if (_data?.spikes == null) return false;

        float best = float.MaxValue;

        foreach (var spike in _data.spikes)
        {
            if (spike == null) continue;

            Vector2 at    = WorldToCanvas(new Vector3(spike.positionXZ.x, 0f, spike.positionXZ.y));
            float   reach = Mathf.Max(8f, SpikeFootprint(spike) * PerchCanvasScale);
            float   d     = Vector2.Distance(at, canvas);
            if (d > reach || d >= best) continue;

            best    = d;
            spikeId = spike.spikeId;
        }

        return spikeId != null;
    }

    // ─────────────────────────────────────────────
    // CANVAS DRAWING
    // ─────────────────────────────────────────────

    /// <summary>
    /// Each spike's footprint at the baseline as a solid stone-grey disc, with a darker dot at its
    /// tip — the selected one lighter. The perch range is washed round it when it is a perch.
    /// </summary>
    private void DrawSpikes()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (_data?.spikes == null) return;

        foreach (var spike in _data.spikes)
        {
            if (spike == null) continue;

            Vector2 at       = WorldToCanvas(new Vector3(spike.positionXZ.x, 0f, spike.positionXZ.y));
            bool    selected = spike.spikeId == _selectedSpikeId;

            if (spike.perch != null && spike.perch.enabled && spike.perch.perchRadius > 0f)
            {
                Handles.color = new Color(1f, 0.93f, 0.55f, selected ? 0.16f : 0.08f);
                Handles.DrawSolidDisc(at, Vector3.forward, spike.perch.perchRadius * PerchCanvasScale);
            }

            float radius = Mathf.Max(3f, SpikeFootprint(spike) * PerchCanvasScale);
            Handles.color = selected ? new Color(0.82f, 0.82f, 0.82f, 1f)
                                     : new Color(0.55f, 0.55f, 0.55f, 1f);
            Handles.DrawSolidDisc(at, Vector3.forward, radius);

            Handles.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            Handles.DrawSolidDisc(at, Vector3.forward, Mathf.Min(radius * 0.35f, 3f));
        }
    }

    /// <summary>How wide the rock is where it meets the baseline, in world units.</summary>
    private static float SpikeFootprint(LevelSelectDesignerData.DesignerSpike spike)
    {
        var cfg = spike.preset != null ? spike.preset.config : new SpikeShapeConfig();
        return Mathf.Max(0.01f, SpikeProfile.From(cfg, spike.scale).RadiusAt(0f));
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    private Vector2 _spikeListScroll;

    private static SpikeShapePreset[] SpikePresets()
    {
        if (!AssetDatabase.IsValidFolder(SpikeShapePreset.AssetFolder))
            return System.Array.Empty<SpikeShapePreset>();

        return AssetDatabase.FindAssets("t:SpikeShapePreset", new[] { SpikeShapePreset.AssetFolder })
            .Select(g => AssetDatabase.LoadAssetAtPath<SpikeShapePreset>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(p => p != null)
            .OrderBy(p => p.name)
            .ToArray();
    }

    /// <summary>A dropdown of the Spike Studio's presets. Hands back the one picked.</summary>
    private static SpikeShapePreset DrawSpikePresetPopup(GUIContent label, SpikeShapePreset current,
                                                         SpikeShapePreset[] presets)
    {
        if (presets.Length == 0)
        {
            EditorGUILayout.HelpBox($"No presets in {SpikeShapePreset.AssetFolder} yet — shape one " +
                                    "in Tools ▸ Waves ▸ Spike Studio.", MessageType.Info);
            return current;
        }

        int index = System.Array.IndexOf(presets, current);
        int shown = EditorGUILayout.Popup(label, Mathf.Max(index, 0),
                                          presets.Select(p => p.name).ToArray());
        return index < 0 && shown == 0 && current != null ? current : presets[shown];
    }

    /// <summary>The spikes, listed with the selected one opened up. Shown in Spikes mode.</summary>
    private void DrawSpikePanel()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Spikes", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Click the canvas to drop one. Each stands on the landscape " +
                                   "surface under it — the tile base plus any hill there — and " +
                                   "follows the hill as it is edited. Anything the preset puts " +
                                   "below its surface line is buried. Their look is the Level " +
                                   "Select Landscape Tuner's Spikes section.",
                                   EditorStyles.wordWrappedMiniLabel);

        EditorGUI.BeginChangeCheck();
        var material = (Material)EditorGUILayout.ObjectField(
            new GUIContent("Material", "What the spikes are drawn with. Empty uses LevelSelectSpikeMat."),
            _data.spikeMaterial, typeof(Material), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Spike Material");
            _data.spikeMaterial = material;
            MarkDirty();
            RebuildSpikes();
        }

        var presets = SpikePresets();

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("New Spikes", EditorStyles.miniBoldLabel);
        _spikeBrushPreset = DrawSpikePresetPopup(new GUIContent("Preset", "The shape the next spike dropped wears."),
                                                 _spikeBrushPreset, presets);
        _spikeBrushScale  = Mathf.Max(0.01f, EditorGUILayout.FloatField(
            new GUIContent("Size", "Size multiplier the next spike dropped gets. 1 = the preset's own size."),
            _spikeBrushScale));

        EditorGUILayout.Space(4);

        if (_data.spikes.Count == 0)
        {
            EditorGUILayout.LabelField("None yet.", EditorStyles.miniLabel);
            return;
        }

        _spikeListScroll = EditorGUILayout.BeginScrollView(_spikeListScroll, GUILayout.MaxHeight(360f));

        LevelSelectDesignerData.DesignerSpike toDelete = null;

        foreach (var spike in _data.spikes)
        {
            if (spike == null) continue;

            bool   open  = spike.spikeId == _selectedSpikeId;
            string label = SpikeLabel(spike);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(open ? $"▾ {label}" : $"▸ {label}",
                                     EditorStyles.miniButton))
                {
                    _selectedSpikeId = open ? null : spike.spikeId;
                    Repaint();
                }
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22f)))
                    toDelete = spike;
            }

            if (!open) continue;

            EditorGUI.indentLevel++;
            DrawSpikeFields(spike, presets);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndScrollView();

        if (toDelete != null)
        {
            Undo.RecordObject(_data, "Delete Spike");
            _data.spikes.Remove(toDelete);
            if (_selectedSpikeId == toDelete.spikeId) _selectedSpikeId = null;
            MarkDirty();
            RemoveSpikeObject(toDelete.spikeId);
            Repaint();
        }

        EditorGUILayout.Space(2);
        if (GUILayout.Button("Rebuild Spikes", EditorStyles.miniButton))
            RebuildSpikes();
    }

    /// <summary>"Spike 3 — SpikeRidged1": its place in the list and the shape it wears.</summary>
    private string SpikeLabel(LevelSelectDesignerData.DesignerSpike spike) =>
        $"Spike {_data.spikes.IndexOf(spike) + 1} — {(spike.preset != null ? spike.preset.name : "default shape")}";

    /// <summary>One spike's own fields — where, which shape, how big, and its perch block.</summary>
    private void DrawSpikeFields(LevelSelectDesignerData.DesignerSpike spike, SpikeShapePreset[] presets)
    {
        EditorGUI.BeginChangeCheck();

        Vector2 at = EditorGUILayout.Vector2Field(
            new GUIContent("Position", "Where it stands on the map, in world X and Z."),
            spike.positionXZ);

        var preset = DrawSpikePresetPopup(new GUIContent("Preset", "The shape this rock wears."),
                                          spike.preset, presets);

        float scale = Mathf.Max(0.01f, EditorGUILayout.FloatField(
            new GUIContent("Size", "Size multiplier on the whole preset. 1 = the preset's own size."),
            spike.scale));

        var perch = DrawPerchBlock(spike.perch);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Spike");
            spike.positionXZ = at;
            spike.preset     = preset;
            spike.scale      = scale;
            spike.perch      = perch;
            _spikeBrushPreset = preset;
            _spikeBrushScale  = scale;
            MarkDirty();
            RebuildSpike(spike);
        }

        // The studio's stage in the map's look, the rock standing on this spike's own spot so the
        // map's light falls on it the way it does here.
        if (GUILayout.Button(new GUIContent("Edit Shape",
                "Opens this spike's preset in the Spike Studio, on a stage of its own in the map's " +
                "look. Saving the preset rebuilds every spike using it.")))
            SpikeStudio.OpenForSpike(spike.preset, spike.scale, SpikeStage.Look.LevelSelect,
                                     new Vector3(spike.positionXZ.x, SpikeGroundY(spike), spike.positionXZ.y),
                                     SpikeMaterial());
    }

    /// <summary>
    /// A preset was overwritten in the Spike Studio. Each spike here is a saved mesh, so the ones
    /// wearing that preset are rebuilt or they would keep the old shape. Only when the map's
    /// SPIKES are in the open scene — never builds them into some other scene.
    /// </summary>
    private void OnSpikePresetSaved(SpikeShapePreset preset)
    {
        if (preset == null || _data?.spikes == null) return;

        var parent = GameObject.Find(SpikesParent);
        if (parent == null) return;

        bool rebuilt = false;
        foreach (var spike in _data.spikes)
        {
            if (spike == null || spike.preset != preset) continue;
            BuildSpike(spike, parent);
            rebuilt = true;
        }

        if (rebuilt) AssetDatabase.SaveAssets();
    }

    /// <summary>The spike picked in Select mode, shown on its own. Spikes mode lists them all instead.</summary>
    private void DrawSelectedSpikeProps()
    {
        if (_mode == DesignerMode.Spike) return;

        var spike = SelectedSpike;
        if (spike == null) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(SpikeLabel(spike), EditorStyles.boldLabel);
        DrawSpikeFields(spike, SpikePresets());
    }

    // ─────────────────────────────────────────────
    // GENERATION
    // ─────────────────────────────────────────────

    private static string SpikeObjectName(string spikeId) => $"Spike_{SanitiseAssetName(spikeId)}";

    private GameObject FindSpikeObject(string spikeId)
    {
        var parent = GameObject.Find(SpikesParent);
        if (parent == null) return null;
        var child = parent.transform.Find(SpikeObjectName(spikeId));
        return child != null ? child.gameObject : null;
    }

    private void RemoveSpikeObject(string spikeId)
    {
        var go = FindSpikeObject(spikeId);
        if (go != null) Undo.DestroyObjectImmediate(go);
    }

    /// <summary>
    /// Builds every spike afresh. Anything under the SPIKES parent that no longer has a spike in
    /// the data is cleared, so one deleted in the panel leaves the scene too.
    /// </summary>
    private void RebuildSpikes()
    {
        var parent = FindOrCreateParent(SpikesParent);

        var wanted = new System.Collections.Generic.HashSet<string>(
            _data.spikes.Where(s => s != null).Select(s => SpikeObjectName(s.spikeId)));

        for (int i = parent.transform.childCount - 1; i >= 0; i--)
        {
            var child = parent.transform.GetChild(i).gameObject;
            if (!wanted.Contains(child.name)) Undo.DestroyObjectImmediate(child);
        }

        foreach (var spike in _data.spikes)
            if (spike != null) BuildSpike(spike, parent);

        AssetDatabase.SaveAssets();
    }

    /// <summary>Rebuilds one spike — its mesh, its place, its perch.</summary>
    private void RebuildSpike(LevelSelectDesignerData.DesignerSpike spike)
    {
        if (spike == null) return;
        BuildSpike(spike, FindOrCreateParent(SpikesParent));
        AssetDatabase.SaveAssets();
    }

    private void BuildSpike(LevelSelectDesignerData.DesignerSpike spike, GameObject parent)
    {
        string name = SpikeObjectName(spike.spikeId);

        var go = FindSpikeObject(spike.spikeId);
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Generate Spike");
            go.transform.SetParent(parent.transform, false);
        }

        PlaceSpikeObject(spike, go);

        // The same build a level's ProceduralSpike does, so the carve and twist match exactly.
        var  cfg     = spike.preset != null ? spike.preset.config : new SpikeShapeConfig();
        var  profile = SpikeProfile.From(cfg, spike.scale);
        var  built   = ProceduralSpikeMesh.Build(profile, cfg.sidesAround, cfg.heightSubdivisions,
                                                 SpikeRidge.From(cfg, spike.scale), cfg.twistTurns);
        var  mesh    = SaveGeneratedMesh(name, built);
        if (mesh == null) return;

        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = Undo.AddComponent<MeshFilter>(go);
        filter.sharedMesh = mesh;
        EditorUtility.SetDirty(filter);

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = Undo.AddComponent<MeshRenderer>(go);
        renderer.sharedMaterial = SpikeMaterial();
        EditorUtility.SetDirty(renderer);

        // She stands on the tip the mesh was just built to.
        ApplyPerch(go, spike.perch, Vector3.up * profile.topY);
    }

    /// <summary>Stands the rock where it belongs — its surface line on the landscape under it.</summary>
    private void PlaceSpikeObject(LevelSelectDesignerData.DesignerSpike spike, GameObject go = null,
                                  bool recordUndo = true)
    {
        if (go == null) go = FindSpikeObject(spike.spikeId);
        if (go == null) return;

        if (recordUndo) Undo.RecordObject(go.transform, "Place Spike");
        go.transform.localPosition = new Vector3(spike.positionXZ.x, SpikeGroundY(spike), spike.positionXZ.y);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;
    }

    /// <summary>
    /// Re-seats every spike on the landscape as it now stands. Called from the hill sync, which
    /// runs on every hill edit (and on World Y / Height Offset changes), so a spike rides the hill
    /// it stands on. Only moved — the shape has not changed, so nothing is rebuilt. No undo record:
    /// it follows the hill edit, which is what gets undone.
    /// </summary>
    private void PlaceAllSpikes()
    {
        if (_data?.spikes == null || _data.spikes.Count == 0) return;
        if (GameObject.Find(SpikesParent) == null) return;

        foreach (var spike in _data.spikes)
            if (spike != null) PlaceSpikeObject(spike, recordUndo: false);
    }

    /// <summary>The designer's spike material, falling back to LevelSelectSpikeMat when none is set.</summary>
    private Material SpikeMaterial()
    {
        if (_data.spikeMaterial != null) return _data.spikeMaterial;
        return AssetDatabase.LoadAssetAtPath<Material>(DefaultSpikeMatPath);
    }
}

#endif
