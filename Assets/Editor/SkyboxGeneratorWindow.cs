using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tunes a sky and saves what you land on to a <see cref="SkyboxPreset"/> for the Level Select
/// Designer to hold.
///
/// The same arrangement as the other tuners: the window edits its own working copy, and while
/// Apply Live is on that copy drives the active preset's skybox material, so a scene wearing that
/// sky moves with the field. Nothing reaches the preset asset until Save. Turning the toggle off,
/// loading, or closing the window gives the material back to the preset's saved settings.
/// </summary>
public class SkyboxGeneratorWindow : EditorWindow
{
    public const string Folder = "Assets/ScriptsData/DataScripts/SkyboxPresets";

    [MenuItem("Tools/Level Design/Skybox Generator", false, 103)]
    public static void Open() => GetWindow<SkyboxGeneratorWindow>("Skybox Generator").Show();

    public static void Open(SkyboxPreset preset)
    {
        var w = GetWindow<SkyboxGeneratorWindow>("Skybox Generator");
        if (preset != null && preset != w.activePreset) w.SetActivePreset(preset);
        w.Show();
    }

    private const string PrefPreset = "SkyboxGeneratorWindow_PresetGUID";

    [SerializeField] private SkyboxPreset activePreset;
    [SerializeField] private bool         applyLive = true;
    [SerializeField] private SkyboxPreset working;

    private SerializedObject _so;
    private Vector2          _scroll;

    // ─────────────────────────────────────────────────────────────
    // LIFETIME
    // ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (working == null)
        {
            working = CreateInstance<SkyboxPreset>();

            if (activePreset == null && EditorPrefs.HasKey(PrefPreset))
            {
                string path = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(PrefPreset));
                if (!string.IsNullOrEmpty(path))
                    activePreset = AssetDatabase.LoadAssetAtPath<SkyboxPreset>(path);
            }
            if (activePreset != null) working.CopySettingsFrom(activePreset);
        }

        // DontSave, not HideAndDontSave — that one carries NotEditable, which greys out every
        // field drawn from this copy. Set every enable so a window opened before the fix heals.
        working.hideFlags = HideFlags.DontSave;

        _so = new SerializedObject(working);
        Undo.undoRedoPerformed += OnUndoRedo;
        TakeOver();
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        HandBack();
    }

    private void OnDestroy()
    {
        if (working != null) DestroyImmediate(working);
    }

    private void TakeOver()
    {
        if (applyLive && activePreset != null) SkyboxPresetBuilder.Preview(working, activePreset);
        SceneView.RepaintAll();
    }

    private void HandBack()
    {
        if (activePreset != null) SkyboxPresetBuilder.Preview(activePreset, activePreset);
        SceneView.RepaintAll();
    }

    private bool ApplyLive
    {
        get => applyLive;
        set
        {
            if (value == applyLive) return;
            applyLive = value;
            if (applyLive) TakeOver(); else HandBack();
            Repaint();
        }
    }

    private void OnUndoRedo()
    {
        _so?.Update();
        TakeOver();
        Repaint();
    }

    // The gradient editor is its own popup; this keeps the window (and so the sky) following it.
    private void OnInspectorUpdate() => Repaint();

    // ─────────────────────────────────────────────────────────────
    // GUI
    // ─────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.FlexibleSpace();
            ApplyLive = GUILayout.Toggle(applyLive, "Apply Live",
                                         EditorStyles.toolbarButton, GUILayout.Width(80));
        }

        DrawPresetRow();

        if (_so == null || _so.targetObject != working) _so = new SerializedObject(working);
        _so.Update();

        EditorGUILayout.Space();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("Sky", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_so.FindProperty(nameof(SkyboxPreset.skyGradient)));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Clouds", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_so.FindProperty(nameof(SkyboxPreset.clouds)), true);
        EditorGUILayout.PropertyField(_so.FindProperty(nameof(SkyboxPreset.opacity)));
        EditorGUILayout.PropertyField(_so.FindProperty(nameof(SkyboxPreset.amount)));
        EditorGUILayout.PropertyField(_so.FindProperty(nameof(SkyboxPreset.sizeMin)), new GUIContent("Size Min"));
        EditorGUILayout.PropertyField(_so.FindProperty(nameof(SkyboxPreset.sizeMax)), new GUIContent("Size Max"));
        EditorGUILayout.PropertyField(_so.FindProperty(nameof(SkyboxPreset.spread)));
        EditorGUILayout.PropertyField(_so.FindProperty(nameof(SkyboxPreset.height)));

        var seed = _so.FindProperty(nameof(SkyboxPreset.seed));
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(seed);
            if (GUILayout.Button("Reshuffle", GUILayout.Width(80))) seed.intValue = Random.Range(1, 1000000);
        }

        EditorGUILayout.PropertyField(_so.FindProperty(nameof(SkyboxPreset.panSpeed)));

        EditorGUILayout.EndScrollView();

        if (_so.ApplyModifiedProperties()) TakeOver();
    }

    private void DrawPresetRow()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            var picked = LevelSelectRiverPresetLibrary.DrawPicker(
                new GUIContent("Active Preset",
                               "The sky presets kept in " + Folder + ". A world wears whichever " +
                               "the Level Select Designer holds."),
                activePreset, Folder);

            var chosen = (SkyboxPreset)EditorGUILayout.ObjectField(
                picked, typeof(SkyboxPreset), false, GUILayout.Width(150f));

            if (chosen != activePreset) SetActivePreset(chosen);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(activePreset == null))
            {
                if (GUILayout.Button("Load")) LoadFromPreset();
                if (GUILayout.Button("Save")) SaveToPreset();
            }
            if (GUILayout.Button("Save as New")) SaveAsNewPreset();
        }

        if (activePreset == null)
        {
            EditorGUILayout.HelpBox(
                "Assign a preset to enable Load and Save, or tune first and Save as New. Apply " +
                "Live drives the active preset's skybox, so there is nothing to see until one " +
                "is assigned and worn by the scene (Level Select Designer > Aesthetics > Sky " +
                "Preset).",
                MessageType.Info);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // PRESETS
    // ─────────────────────────────────────────────────────────────

    private void SetActivePreset(SkyboxPreset preset)
    {
        HandBack();
        activePreset = preset;
        RememberPreset();
        if (activePreset != null) LoadFromPreset();
    }

    private void RememberPreset()
    {
        if (activePreset == null)
        {
            EditorPrefs.DeleteKey(PrefPreset);
            return;
        }

        EditorPrefs.SetString(
            PrefPreset,
            AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(activePreset)));
    }

    private void LoadFromPreset()
    {
        if (activePreset == null) return;

        working.CopySettingsFrom(activePreset);
        _so = new SerializedObject(working);
        TakeOver();
        Repaint();
    }

    private void SaveToPreset()
    {
        if (activePreset == null) return;

        if (!EditorUtility.DisplayDialog(
                "Save Sky",
                $"Overwrite the sky on '{activePreset.name}' with the current tuner values?\n\n" +
                TunerSaveChanges.Describe(activePreset, p => p.CopySettingsFrom(working)),
                "Save", "Cancel"))
            return;

        Undo.RecordObject(activePreset, "Save Sky");
        activePreset.CopySettingsFrom(working);
        SkyboxPresetBuilder.Save(activePreset);

        Debug.Log($"[SkyboxGeneratorWindow] Saved to {activePreset.name}");
    }

    private void SaveAsNewPreset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Sky Preset", "NewSkyPreset", "asset",
            "Choose save location", LevelSelectRiverPresetLibrary.EnsureFolder(Folder));
        if (string.IsNullOrEmpty(path)) return;

        HandBack();

        var preset = CreateInstance<SkyboxPreset>();
        preset.CopySettingsFrom(working);
        AssetDatabase.CreateAsset(preset, path);
        SkyboxPresetBuilder.Save(preset);

        activePreset = preset;
        RememberPreset();
        TakeOver();

        Debug.Log($"[SkyboxGeneratorWindow] Saved new preset at {path}");
    }
}

/// <summary>
/// Writes a sky's settings into what the skybox shader reads: the material, the gradient strip,
/// the cloud placement data (all sub-assets of the preset) and the cloud atlas.
///
/// <see cref="Preview"/> only changes them in memory — an unsaved cloud list gets an in-memory
/// atlas — so a live tuner never writes the preset's files. <see cref="Save"/> writes the atlas
/// PNG beside the preset and saves the preset asset.
/// </summary>
public static class SkyboxPresetBuilder
{
    private const string ShaderName   = "Waves/Skybox/Gradient Clouds";
    private const int    GradientSize = 256;
    private const int    MaxAtlasSize = 4096;
    private const int    MaxCellSize  = 512;
    private const int    CellPadPx    = 2;

    private static readonly int GradientTexId = Shader.PropertyToID("_GradientTex");
    private static readonly int CloudAtlasId  = Shader.PropertyToID("_CloudAtlas");
    private static readonly int CloudDataId   = Shader.PropertyToID("_CloudData");
    private static readonly int CloudCountId  = Shader.PropertyToID("_CloudCount");
    private static readonly int AtlasGridId   = Shader.PropertyToID("_AtlasGrid");
    private static readonly int AtlasPadId    = Shader.PropertyToID("_AtlasPad");
    private static readonly int PanSpeedId    = Shader.PropertyToID("_PanSpeed");
    private static readonly int OpacityId     = Shader.PropertyToID("_CloudOpacity");

    private static Texture2D _previewAtlas;
    private static string    _previewAtlasKey;

    /// <summary>Shows <paramref name="settings"/> on <paramref name="target"/>'s material, in memory only.</summary>
    public static void Preview(SkyboxPreset settings, SkyboxPreset target) => Apply(settings, target, false);

    /// <summary>Writes the preset's own settings into its material and files, and saves it.</summary>
    public static void Save(SkyboxPreset preset) => Apply(preset, preset, true);

    private static void Apply(SkyboxPreset settings, SkyboxPreset target, bool save)
    {
        if (settings == null || target == null || !AssetDatabase.Contains(target)) return;

        EnsureSubAssets(target);

        var cloudTextures = settings.clouds.Where(t => t != null).ToList();
        var atlas = ResolveAtlas(cloudTextures, target, save);
        WriteGradient(settings, target.gradientTexture);
        int placed = WriteCloudData(settings, cloudTextures, target.cloudDataTexture);

        int grid = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(cloudTextures.Count)));
        int cell = Mathf.Min(MaxCellSize, MaxAtlasSize / grid);

        var mat = target.material;
        mat.SetTexture(GradientTexId, target.gradientTexture);
        mat.SetTexture(CloudDataId,   target.cloudDataTexture);
        mat.SetTexture(CloudAtlasId,  atlas);
        mat.SetFloat(CloudCountId, atlas != null ? placed : 0);
        mat.SetFloat(AtlasGridId,  grid);
        mat.SetFloat(AtlasPadId,   CellPadPx / (float)cell);
        mat.SetFloat(PanSpeedId,   settings.panSpeed);
        mat.SetFloat(OpacityId,    settings.opacity);

        if (!save) return;

        EditorUtility.SetDirty(mat);
        EditorUtility.SetDirty(target.gradientTexture);
        EditorUtility.SetDirty(target.cloudDataTexture);
        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssetIfDirty(target);
    }

    private static void EnsureSubAssets(SkyboxPreset preset)
    {
        bool added = false;

        if (preset.material == null)
        {
            preset.material = new Material(Shader.Find(ShaderName)) { name = preset.name + " Skybox" };
            AssetDatabase.AddObjectToAsset(preset.material, preset);
            added = true;
        }

        if (preset.gradientTexture == null)
        {
            preset.gradientTexture = new Texture2D(GradientSize, 1, TextureFormat.RGBA32, false, false)
            {
                name       = preset.name + " Gradient",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            AssetDatabase.AddObjectToAsset(preset.gradientTexture, preset);
            added = true;
        }

        if (preset.cloudDataTexture == null)
        {
            preset.cloudDataTexture = new Texture2D(SkyboxPreset.MaxClouds, 2, TextureFormat.RGBAFloat, false, true)
            {
                name       = preset.name + " Cloud Data",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            AssetDatabase.AddObjectToAsset(preset.cloudDataTexture, preset);
            added = true;
        }

        if (added) AssetDatabase.SaveAssetIfDirty(preset);
    }

    private static void WriteGradient(SkyboxPreset settings, Texture2D tex)
    {
        var px = new Color[GradientSize];
        for (int i = 0; i < GradientSize; i++)
            px[i] = settings.skyGradient.Evaluate(i / (float)(GradientSize - 1));
        tex.SetPixels(px);
        tex.Apply(false);
    }

    /// <summary>Scatters the clouds and writes where each one sits. Returns how many.</summary>
    private static int WriteCloudData(SkyboxPreset settings, List<Texture2D> cloudTextures, Texture2D tex)
    {
        const int w = SkyboxPreset.MaxClouds;
        var px = new Color[w * 2];
        int count = cloudTextures.Count == 0 ? 0 : Mathf.Clamp(settings.amount, 0, w);

        var rng = new System.Random(settings.seed);
        float sizeMin = Mathf.Min(settings.sizeMin, settings.sizeMax);
        float sizeMax = Mathf.Max(settings.sizeMin, settings.sizeMax);

        for (int i = 0; i < count; i++)
        {
            int   cell      = rng.Next(cloudTextures.Count);
            float azimuth   = (float)rng.NextDouble() * Mathf.PI * 2f;
            float elevation = settings.height + ((float)rng.NextDouble() - 0.5f) * settings.spread;
            float size      = Mathf.Lerp(sizeMin, sizeMax, (float)rng.NextDouble());

            var   cloud  = cloudTextures[cell];
            float aspect = cloud.height > 0 ? cloud.width / (float)cloud.height : 1f;
            float halfW  = Mathf.Tan(Mathf.Clamp(size, 0.1f, 170f) * 0.5f * Mathf.Deg2Rad);

            px[i]     = new Color(azimuth, Mathf.Clamp(elevation, -89f, 89f) * Mathf.Deg2Rad, halfW, halfW / aspect);
            px[w + i] = new Color(cell, 0f, 0f, 0f);
        }

        tex.SetPixels(px);
        tex.Apply(false);
        return count;
    }

    // ── Atlas ────────────────────────────────────────────────

    /// <summary>
    /// The cloud images packed into a square grid of cells, one per list entry. The preset's saved
    /// PNG when the list matches it; otherwise built fresh — in memory for a preview, or written
    /// beside the preset on save.
    /// </summary>
    private static Texture2D ResolveAtlas(List<Texture2D> cloudTextures, SkyboxPreset target, bool save)
    {
        if (cloudTextures.Count == 0)
        {
            if (save) { target.cloudAtlas = null; target.cloudAtlasKey = ""; }
            return null;
        }

        string key = string.Join("|", cloudTextures.Select(t =>
        {
            string p = AssetDatabase.GetAssetPath(t);
            return AssetDatabase.AssetPathToGUID(p) + ":" + AssetDatabase.GetAssetDependencyHash(p);
        }));

        if (key == target.cloudAtlasKey && target.cloudAtlas != null) return target.cloudAtlas;

        if (!save)
        {
            if (key != _previewAtlasKey || _previewAtlas == null)
            {
                if (_previewAtlas != null) Object.DestroyImmediate(_previewAtlas);
                _previewAtlas = BuildAtlas(cloudTextures);
                _previewAtlas.hideFlags = HideFlags.HideAndDontSave;
                _previewAtlasKey = key;
            }
            return _previewAtlas;
        }

        var atlas = BuildAtlas(cloudTextures);
        string presetPath = AssetDatabase.GetAssetPath(target);
        string atlasPath  = Path.ChangeExtension(presetPath, null) + "_Clouds.png";
        File.WriteAllBytes(atlasPath, atlas.EncodeToPNG());
        Object.DestroyImmediate(atlas);

        AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(atlasPath) is TextureImporter importer)
        {
            bool changed = importer.mipmapEnabled
                           || importer.wrapMode != TextureWrapMode.Clamp
                           || !importer.alphaIsTransparency
                           || importer.maxTextureSize < MaxAtlasSize;
            if (changed)
            {
                importer.mipmapEnabled       = false;
                importer.wrapMode            = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize      = MaxAtlasSize;
                importer.SaveAndReimport();
            }
        }

        target.cloudAtlas    = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
        target.cloudAtlasKey = key;
        return target.cloudAtlas;
    }

    private static Texture2D BuildAtlas(List<Texture2D> cloudTextures)
    {
        int grid  = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(cloudTextures.Count)));
        int cell  = Mathf.Min(MaxCellSize, MaxAtlasSize / grid);
        int size  = grid * cell;
        int inner = cell - CellPadPx * 2;

        var atlas = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            wrapMode = TextureWrapMode.Clamp,
        };
        atlas.SetPixels32(new Color32[size * size]);

        var prevActive = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(inner, inner, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        for (int i = 0; i < cloudTextures.Count; i++)
        {
            Graphics.Blit(cloudTextures[i], rt);
            RenderTexture.active = rt;
            atlas.ReadPixels(new Rect(0, 0, inner, inner),
                             (i % grid) * cell + CellPadPx,
                             (i / grid) * cell + CellPadPx, false);
        }
        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(rt);
        atlas.Apply(false);
        return atlas;
    }
}
