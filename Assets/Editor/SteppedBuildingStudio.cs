using UnityEditor;
using UnityEngine;

// Authoring front-end for stepped rooftops. Spawns a throwaway preview building in the
// open scene, lets you tweak the shape live, and saves the result as a reusable
// SteppedBuildingPreset asset. The actual mesh is built by SteppedBuildingMesh — the same
// code the level spawner runs — so what you see here is what ships.
//
// Tools ▸ Waves ▸ Stepped Building Studio.
public class SteppedBuildingStudio : EditorWindow
{
    const string PreviewName = "— Stepped Building Preview —";

    // Test-building dimensions (the preview only; presets are dimension-agnostic).
    [SerializeField] float width  = 8f;
    [SerializeField] float length = 8f;
    [SerializeField] float top    = 10f;
    [SerializeField] float depth  = 5f;

    [SerializeField] int seed = 12345;
    [SerializeField] SteppedBuildingConfig cfg = new SteppedBuildingConfig();
    [SerializeField] SteppedBuildingPreset loaded;

    GameObject preview;

    [MenuItem("Tools/Waves/Stepped Building Studio")]
    static void Open() => GetWindow<SteppedBuildingStudio>("Stepped Building");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Preview building size", EditorStyles.boldLabel);
        width  = EditorGUILayout.FloatField("Width (X)", width);
        length = EditorGUILayout.FloatField("Length (Z)", length);
        top    = EditorGUILayout.FloatField("Height above water", top);
        depth  = EditorGUILayout.FloatField("Depth below water", depth);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rooftop shape", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        cfg.cellSize     = Mathf.Max(0.05f, EditorGUILayout.FloatField("Cell size", cfg.cellSize));
        cfg.rimCells     = EditorGUILayout.IntSlider("Rim thickness (cells)", cfg.rimCells, 1, 8);
        cfg.stepCount    = EditorGUILayout.IntSlider("Steps around edge", cfg.stepCount, 1, 40);
        cfg.levelCount   = EditorGUILayout.IntSlider("Height levels", cfg.levelCount, 1, 10);
        cfg.levelSpacing = Mathf.Max(0.01f, EditorGUILayout.FloatField("Level spacing", cfg.levelSpacing));
        cfg.variation    = EditorGUILayout.Slider("Change chance", cfg.variation, 0f, 1f);
        cfg.persistence  = EditorGUILayout.Slider("Persistence", cfg.persistence, 0f, 1f);
        cfg.dropFraction = EditorGUILayout.Slider("Centre drop", cfg.dropFraction, 0f, 0.9f);
        bool changed = EditorGUI.EndChangeCheck();

        using (new EditorGUILayout.HorizontalScope())
        {
            seed = EditorGUILayout.IntField("Seed", seed);
            if (GUILayout.Button("Reroll", GUILayout.Width(70))) { seed = Random.Range(0, 999999); changed = true; }
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(preview == null ? "Spawn Preview" : "Refresh Preview")) Refresh();
            if (GUILayout.Button("Remove Preview")) RemovePreview();
        }
        if (changed && preview != null) Refresh();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preset", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        loaded = (SteppedBuildingPreset)EditorGUILayout.ObjectField("Load from", loaded, typeof(SteppedBuildingPreset), false);
        if (EditorGUI.EndChangeCheck() && loaded != null)
        {
            cfg = loaded.config.Copy();
            Refresh();
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Save As New Preset…")) SaveAsNew();
            using (new EditorGUI.DisabledScope(loaded == null))
                if (GUILayout.Button("Overwrite Loaded")) Overwrite();
        }
    }

    void Refresh()
    {
        if (preview == null)
        {
            preview = GameObject.Find(PreviewName);
            if (preview == null)
            {
                preview = new GameObject(PreviewName, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                preview.GetComponent<MeshRenderer>().sharedMaterial = FindWindowMaterial();
            }
        }

        var mesh = SteppedBuildingMesh.Build(width, length, top, depth, cfg, seed);
        preview.GetComponent<MeshFilter>().sharedMesh   = mesh;
        preview.GetComponent<MeshCollider>().sharedMesh = mesh;
        Selection.activeObject = preview;
    }

    void RemovePreview()
    {
        var go = preview != null ? preview : GameObject.Find(PreviewName);
        if (go != null) DestroyImmediate(go);
        preview = null;
    }

    // Creates the target folder (and any missing parents) so the save panel opens there.
    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string[] parts = folder.Split('/');
        string path = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = path + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(path, parts[i]);
            path = next;
        }
    }

    static Material FindWindowMaterial()
    {
        foreach (var guid in AssetDatabase.FindAssets("WindowsBlockMat t:Material"))
            return AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
        return null;
    }

    // A Resources folder so LevelSpawner can load every preset at runtime with no wiring —
    // drop a preset here and it joins the random pool (matches Resources/Levels, Resources/Souls).
    const string PresetFolder = "Assets/Resources/Buildings";

    void SaveAsNew()
    {
        EnsureFolder(PresetFolder);
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Stepped Building Preset", "SteppedBuildingPreset1", "asset",
            "Choose where to save the preset.", PresetFolder);
        if (string.IsNullOrEmpty(path)) return;

        var asset = ScriptableObject.CreateInstance<SteppedBuildingPreset>();
        asset.config = cfg.Copy();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        loaded = asset;
        EditorGUIUtility.PingObject(asset);
    }

    void Overwrite()
    {
        if (loaded == null) return;
        loaded.config = cfg.Copy();
        EditorUtility.SetDirty(loaded);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(loaded);
    }
}
