using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Lollipop towers — a cylinder stem with an orb on top. Every installation outpost carries one
/// (built into the outpost's own mesh, see the Outposts partial); a pool can stand one in the
/// middle of its island, which is what this file builds.
///
/// A pool tower is its own object under the pool, in the river material, with its base on the
/// island top — flush with the rim, so at the pool's own origin. It is only built while the
/// island's radius is above <see cref="LevelSelectDesignerData.PoolTowerMinIsland"/>.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    /// <summary>
    /// Stem and orb fields for a tower. Hands back an edited copy, so the caller writes it only
    /// inside its own change check.
    /// </summary>
    private static LollipopTower DrawLollipopTowerFields(LollipopTower tower)
    {
        var edited = (tower ?? new LollipopTower()).Clone();

        edited.stemRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Stem Radius", "Radius of the cylinder stem."), edited.stemRadius));
        edited.stemHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Stem Height", "Height of the stem, from its base up to where it meets the orb."),
            edited.stemHeight));
        edited.orbRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Orb Radius", "Radius of the orb on top."), edited.orbRadius));
        edited.baseRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Base Radius", "Radius of the round base the stem stands on."), edited.baseRadius));
        edited.baseHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Base Height", "Height of the round base. 0 = no base, and the stem stands on the ground."),
            edited.baseHeight));
        edited.rampHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Ramp Height", "Height of the ramp on top of the base, sloping in from the base radius " +
                                          "to the base 2 radius. 0 = no ramp."), edited.rampHeight));
        edited.base2Radius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Base 2 Radius", "Radius of the second base on top of the ramp — and the radius the ramp slopes in to."),
            edited.base2Radius));
        edited.base2Height = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Base 2 Height", "Height of the second base, on top of the ramp. 0 = no second base."),
            edited.base2Height));

        return edited;
    }

    private const string LollipopPresetFolder = "Assets/ScriptsData/DataScripts/LollipopTowerPresets";

    /// <summary>
    /// A tower's preset: pick one from the dropdown to put its sizes on this tower, Save to write
    /// this tower's sizes back into it, or Save as New to make one from them. Sizes are copied
    /// either way. <paramref name="setPreset"/> writes which preset the tower remembers, and
    /// <paramref name="setTower"/> puts a loaded tower on it — both called inside an Undo record
    /// of the designer data. Returns true when a preset was loaded.
    /// </summary>
    private bool DrawLollipopPresetRow(LollipopTowerPreset current, LollipopTower tower,
                                       System.Action<LollipopTowerPreset> setPreset,
                                       System.Action<LollipopTower> setTower)
    {
        bool loaded = false;

        var picked = LevelSelectRiverPresetLibrary.DrawPicker(
            new GUIContent("Tower Preset", "The lollipop tower presets kept in " + LollipopPresetFolder +
                                           ". Picking one puts its sizes on this tower."),
            current, LollipopPresetFolder);

        if (picked != current)
        {
            Undo.RecordObject(_data, "Load Lollipop Tower Preset");
            setPreset(picked);
            if (picked != null)
            {
                setTower(picked.Build());
                loaded = true;
            }
            MarkDirty();
            GUI.FocusControl(null);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(current == null))
            {
                if (GUILayout.Button(new GUIContent("Save", "Overwrite the tower preset with this tower's sizes.")) &&
                    EditorUtility.DisplayDialog("Save Lollipop Tower Preset",
                        $"Overwrite '{current.name}' with this tower's sizes?", "Save", "Cancel"))
                {
                    Undo.RecordObject(current, "Save Lollipop Tower Preset");
                    current.CopyFrom(tower);
                    EditorUtility.SetDirty(current);
                    AssetDatabase.SaveAssetIfDirty(current);
                }
            }

            if (GUILayout.Button(new GUIContent("Save as New", "Make a new tower preset from this tower's sizes.")))
            {
                // Opened after this GUI pass, so the file panel never lands inside a change check.
                var snapshot = (tower ?? new LollipopTower()).Clone();
                EditorApplication.delayCall += () =>
                {
                    string assetPath = EditorUtility.SaveFilePanelInProject(
                        "Save Lollipop Tower Preset", "NewLollipopTowerPreset", "asset", "Choose save location",
                        LevelSelectRiverPresetLibrary.EnsureFolder(LollipopPresetFolder));
                    if (string.IsNullOrEmpty(assetPath) || _data == null) return;

                    var preset = CreateInstance<LollipopTowerPreset>();
                    preset.CopyFrom(snapshot);
                    AssetDatabase.CreateAsset(preset, assetPath);
                    AssetDatabase.SaveAssets();

                    Undo.RecordObject(_data, "Save Lollipop Tower Preset");
                    setPreset(preset);
                    MarkDirty();
                    Repaint();
                };
            }
        }

        return loaded;
    }

    /// <summary>The tower toggle and sizes for the selected pool, in the Pools list.</summary>
    private void DrawPoolTowerFields(LevelSelectDesignerData.DesignerPool pool)
    {
        float island = _data.PoolShapeFor(pool).islandRadius;

        EditorGUI.BeginChangeCheck();
        bool hasTower = EditorGUILayout.Toggle(
            new GUIContent("Lollipop Tower", "Stand a lollipop tower in the middle of the island."),
            pool.hasTower);

        LollipopTower tower = pool.tower;
        if (hasTower)
        {
            if (island <= LevelSelectDesignerData.PoolTowerMinIsland)
                EditorGUILayout.LabelField(
                    $"Island radius {island:F2} — needs to be above " +
                    $"{LevelSelectDesignerData.PoolTowerMinIsland:F1} for the tower to be built.",
                    EditorStyles.miniLabel);

            DrawLollipopPresetRow(pool.towerPreset, pool.tower,
                                  p => pool.towerPreset = p, t => pool.tower = t);
            tower = DrawLollipopTowerFields(pool.tower);
        }

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Pool Tower");
            pool.hasTower = hasTower;
            pool.tower    = tower;
            MarkDirty();
            RebuildPoolTower(pool);
        }
    }

    // ─────────────────────────────────────────────
    // GENERATION
    // ─────────────────────────────────────────────

    /// <summary>
    /// Builds, rebuilds or clears one pool's tower, under the generated pool it belongs to. Does
    /// nothing when that pool has not been generated.
    /// </summary>
    private void RebuildPoolTower(LevelSelectDesignerData.DesignerPool pool)
    {
        if (pool == null || string.IsNullOrEmpty(pool.nodeId)) return;

        string poolName = PoolMeshName(pool);
        var record = FindObjectsOfType<RiverPoolMesh>().FirstOrDefault(r => r.meshAssetName == poolName);
        if (record == null) return;

        // The tower goes on the pool's own object, beside the pool mesh rather than inside it.
        Transform poolRoot = record.transform.parent != null ? record.transform.parent : record.transform;

        string    towerName = PoolTowerMeshName(pool);
        Transform existing  = poolRoot.Find(towerName);

        bool wanted = pool.hasTower && pool.tower != null &&
                      record.islandRadius > LevelSelectDesignerData.PoolTowerMinIsland;
        if (!wanted)
        {
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            return;
        }

        var mesh = SaveGeneratedMesh(towerName, pool.tower.Build(StoneShadingForBuild));
        if (mesh == null) return;

        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(towerName);
            Undo.RegisterCreatedObjectUndo(go, "Generate Pool Tower");
            go.transform.SetParent(poolRoot, false);
        }

        // The island top is flush with the rim, which is the pool's origin.
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;

        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        EditorUtility.SetDirty(filter);

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _data.riverMaterial;
        EditorUtility.SetDirty(renderer);

        AssetDatabase.SaveAssets();
    }

    private void RebuildPoolTowers()
    {
        foreach (var pool in _data.pools) RebuildPoolTower(pool);
    }

    private static string PoolTowerMeshName(LevelSelectDesignerData.DesignerPool pool)
        => $"PoolTower_{SanitiseAssetName(pool.nodeId)}";

    // ─────────────────────────────────────────────
    // CANVAS
    // ─────────────────────────────────────────────

    /// <summary>A solid dot the size of the orb in the middle of every pool that will get a tower.</summary>
    private void DrawPoolTowers()
    {
        if (Event.current.type != EventType.Repaint) return;

        foreach (var pool in _data.pools)
        {
            if (pool == null || !pool.hasTower || pool.tower == null) continue;
            if (_data.PoolShapeFor(pool).islandRadius <= LevelSelectDesignerData.PoolTowerMinIsland) continue;

            var node = _data.nodes.Find(n => n.id == pool.nodeId);
            if (node == null) continue;

            Handles.color = pool.nodeId == _selectedPoolNodeId ? Color.white : pool.editorColor;
            Handles.DrawSolidDisc(WorldToCanvas(node.worldPosition), Vector3.forward,
                                  Mathf.Max(2f, pool.tower.orbRadius * _zoom));
        }
    }
}

#endif
