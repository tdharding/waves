using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Reflection;

public class WaveEffectsLiveTuner : EditorWindow
{
    // Shift+3 shortcut
    [MenuItem("Tools/Waves/Wave Effects Tuner #3")]
    public static void Open() => GetWindow<WaveEffectsLiveTuner>("Wave Effects Tuner");

    // ─────────────────────────────────────────────────────────────────────────
    // SERIALIZED STATE
    // ─────────────────────────────────────────────────────────────────────────

    [SerializeField] Material   waveMaterial;
    [SerializeField] WavePreset activePreset;
    [SerializeField] bool       applyLive   = true;
    [SerializeField] bool tunerActive = false; 

    // Wave Motion
    [SerializeField] float   waveIncreaseFactor = 1f;
    [SerializeField] float   baseFrequency    = 1f;
    [SerializeField] float   baseSpeed        = 1f;
    [SerializeField] float   baseRippleDepth  = 1f;
    [SerializeField] float   frequency    = 1f;
    [SerializeField] float   speed        = 1f;
    [SerializeField] float   rippleDepth  = 1f;
    [SerializeField] float   waveStepRate = 10f;
    [SerializeField] Vector4 waveCenter   = Vector4.zero;

    // Transition Preview
    [SerializeField] float   previewTargetValue = 3f;
    [SerializeField] float   previewDuration = 1.5f;
    [SerializeField] float   previewHoldDuration = 2.0f;
    bool isPreviewing = false;
    double previewStartTime;
    float tunerPhase = 0f;
    double lastUpdateTime;

    // Surface
    [SerializeField] float smoothness   = 0.5f;
    [SerializeField] float transparency = 0.5f;
    [SerializeField] float strength     = 1f;
    [SerializeField] Color baseColor    = Color.white;

    // Peaks & Troughs
    [SerializeField] float troughBrightness = 0.5f;
    [SerializeField] float peakBrightness   = 1.5f;
    [SerializeField] float     soulFishMaskStrength = 1f;
    [SerializeField] float     soulFishRadius       = 2f;
    [SerializeField] float     soulFishBrightness1  = 1f;
    [SerializeField] float     soulFishFadeStart         = 0.35f;
    [SerializeField] float     soulFishEdgeNoiseStrength = 0.25f;
    [SerializeField] float     soulFishEdgeNoiseScale    = 1f;
    [SerializeField] float     soulFishEdgeNoiseScale2   = 4f;
    [SerializeField] float     soulFishEdgeNoiseSpeed    = 0.2f;
    [SerializeField] Vector2   zoneTiling           = new Vector2(0.5f, 0.5f);
    [SerializeField] float     zoneScrollSpeed      = 0.05f;
    [SerializeField] float     zoneNoiseStrength    = 0.3f;
    [SerializeField] Texture2D zoneTexture          = null;

    // Twirl
    [SerializeField] float twirlBaseStrength = 0.5f;
    [SerializeField] float twirlSlopeBoost   = 1f;
    [SerializeField] float twirlScale        = 1f;
    [SerializeField] float depthTwirlStrength = 1f;
    [SerializeField] float depthFadeStrength  = 1f;
    [SerializeField] float depthFadeLine      = 0f;
    [SerializeField] float whirlpoolTwirlStrength     = 2f;
    [SerializeField] float whirlpoolAreaTwirlStrength = 1f;
    [SerializeField] float soulFishTwirlStrength      = 1f;

    // Whirlpool FX
    [SerializeField] float whirlpoolTaper         = 1f;
    [SerializeField] float whirlpoolDarkRadiusMult = 1.5f;
    [SerializeField] float whirlpoolDarkStrength   = 0.6f;
    [SerializeField] float whirlpoolFalloffPower   = 1f;

    // Foam
    [SerializeField] float foamDitherSize    = 1f;
    [SerializeField] float foamDistanceDepth = 0f;
    [SerializeField] float foamDepthFade     = 0f;
    [SerializeField] Color foamColor  = Color.white;
    [SerializeField] float foamLine  = 0.4f;
    [SerializeField] float depthFade = 3.0f;

    // Depth
    [SerializeField] Color   depthColour    = Color.black;
    [SerializeField] float   distanceDepth  = 1f;

    // Light Direction
    [SerializeField] Vector3 lightDirection = new Vector3(0f, 1f, 0f);

    // Normal Texture
    [SerializeField] Texture2D normalTexture;
    [SerializeField] string    normalTextureProp = "_NormalTexture";

    // Music
    [SerializeField] AudioClip musicIntro;
    [SerializeField] AudioClip musicLoop;

    // Lights
    [SerializeField] List<LightEntry> lights = new List<LightEntry>();

    [System.Serializable]
    class LightEntry
    {
        public Light  light;
        public Color  color     = Color.white;
        public float  intensity = 1f;
        public float  range     = 10f;
    }

    // ── Test FX (not saved to presets) ────────────────────────────────────────
    [SerializeField] bool testWhirlpoolsEnabled  = false;
    [SerializeField] bool testSoulFishEnabled    = false;
    [SerializeField] List<TestWhirlpoolEntry> testWhirlpools      = new List<TestWhirlpoolEntry>();
    [SerializeField] List<Vector3>            testSoulFishPositions = new List<Vector3>();

    [System.Serializable]
    class TestWhirlpoolEntry
    {
        public Vector3 position = Vector3.zero;
        public float   radius   = 5f;
    }

    static readonly Vector3 OffMap = new Vector3(99999f, 99999f, 99999f);

    // ─────────────────────────────────────────────────────────────────────────
    // FOLDOUT STATE
    // ─────────────────────────────────────────────────────────────────────────

    [SerializeField] bool foldTestFX        = true;
    [SerializeField] bool foldWaveMotion    = true;
    [SerializeField] bool foldSurface       = true;
    [SerializeField] bool foldPeaksTroughs  = true;
    [SerializeField] bool foldTwirl         = false;
    [SerializeField] bool foldWhirlpoolFX   = false;
    [SerializeField] bool foldFoam          = false;
    [SerializeField] bool foldDepth         = false;
    [SerializeField] bool foldNormal        = false;
    [SerializeField] bool foldLights        = true;
    [SerializeField] bool foldMusic         = true;

    Vector2 scroll;
    SerializedObject serializedWindow;

    // ─────────────────────────────────────────────────────────────────────────
    // LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    const string PrefMaterial = "WaveEffectsTuner_MaterialGUID";
    const string PrefPreset   = "WaveEffectsTuner_PresetGUID";

    void OnEnable()
    {
        serializedWindow = new SerializedObject(this);
        EditorApplication.update += LiveUpdate;
        RestoreFromPrefs();
        lastUpdateTime = EditorApplication.timeSinceStartup;
    }

    void OnDisable()
    {
        EditorApplication.update -= LiveUpdate;
        StopAllPreviewClips();
        if (testWhirlpoolsEnabled) ClearTestWhirlpools();
        if (testSoulFishEnabled)   ClearTestSoulFish();
    }

    void RestoreFromPrefs()
    {
        if (!waveMaterial && EditorPrefs.HasKey(PrefMaterial))
        {
            string path = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(PrefMaterial));
            if (!string.IsNullOrEmpty(path))
                waveMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        if (activePreset == null && EditorPrefs.HasKey(PrefPreset))
        {
            string path = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(PrefPreset));
            if (!string.IsNullOrEmpty(path))
                activePreset = AssetDatabase.LoadAssetAtPath<WavePreset>(path);
            // Values are NOT loaded — press Load to apply a preset to the tuner
        }
    }

    // Called by SaveDataEditorWindow before entering play mode so the tuner
    // reflects the same preset the scene is about to use.
    public static void InstallPreset(WavePreset preset)
    {
        if (preset == null) return;

        // Persist so RestoreFromPrefs picks it up if the window re-opens
        EditorPrefs.SetString(PrefPreset,
            AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(preset)));

        // If the window is already open, update the reference only — press Load to apply values
        if (HasOpenInstances<WaveEffectsLiveTuner>())
        {
            var win = GetWindow<WaveEffectsLiveTuner>("Wave Effects Tuner", false);
            win.activePreset = preset;
            win.Repaint();
        }
    }

    void SaveToPrefs()
    {
        if (waveMaterial)
            EditorPrefs.SetString(PrefMaterial, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(waveMaterial)));
        if (activePreset)
            EditorPrefs.SetString(PrefPreset, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(activePreset)));
    }

    void LiveUpdate()
    {
        if (!tunerActive || !waveMaterial) return;

        double currentTime = EditorApplication.timeSinceStartup;
        float deltaTime = (float)(currentTime - lastUpdateTime);
        lastUpdateTime = currentTime;

        if (isPreviewing)
        {
            float elapsed = (float)(currentTime - previewStartTime);
            float totalDuration = previewDuration + previewHoldDuration;

            if (elapsed <= previewDuration)
            {
                // Phase 1: Ramping up
                float tRamp = Mathf.Clamp01(elapsed / Mathf.Max(previewDuration, 0.01f));
                waveIncreaseFactor = Mathf.Lerp(1.0f, previewTargetValue, tRamp);
            }
            else
            {
                // Phase 2: Holding at target
                waveIncreaseFactor = previewTargetValue;
            }
            
            float curFreq = baseFrequency * waveIncreaseFactor;
            float curSpeed = baseSpeed * waveIncreaseFactor;
            float curRipple = baseRippleDepth * waveIncreaseFactor;

            // Apply preview values directly to material
            waveMaterial.SetFloat("_Frequency",   curFreq);
            waveMaterial.SetFloat("_Speed",       curSpeed);
            waveMaterial.SetFloat("_RippleDepth", curRipple);

            // Accumulate phase
            tunerPhase += curSpeed * deltaTime;
            if (tunerPhase > 6.283185f) tunerPhase -= 6.283185f;
            Shader.SetGlobalFloat("_WavePhase", tunerPhase);

            if (elapsed >= totalDuration) isPreviewing = false;
            
            Repaint();
            SceneView.RepaintAll();
        }
        else if (applyLive)
        {
            if (Application.isPlaying)
            {
                var controller = Object.FindFirstObjectByType<WaveMaterialController>();
                if (controller != null)
                {
                    controller.ApplyStateInstant(BuildState());
                    ApplyToLights();
                    ApplyTestWhirlpools();
                    ApplyTestSoulFish();
                    SceneView.RepaintAll();
                    return;
                }
            }

            ApplyToMaterial();
            ApplyToLights();
            ApplyTestWhirlpools();
            ApplyTestSoulFish();

            // Accumulate phase based on current speed
            tunerPhase += speed * deltaTime;
            if (tunerPhase > 6.283185f) tunerPhase -= 6.283185f;
            Shader.SetGlobalFloat("_WavePhase", tunerPhase);

            SceneView.RepaintAll();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GUI
    // ─────────────────────────────────────────────────────────────────────────

    void OnGUI()
    {
        serializedWindow.Update();

        DrawToolbar();
        DrawPresetRow();
        EditorGUILayout.LabelField(string.Empty, GUI.skin.horizontalSlider);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUI.BeginChangeCheck();

        DrawSection("Test FX",          ref foldTestFX,       DrawTestFX);
        DrawSection("Wave Motion",      ref foldWaveMotion,   DrawWaveMotion);
        DrawSection("Surface",          ref foldSurface,      DrawSurface);
        DrawSection("Peaks & Troughs",  ref foldPeaksTroughs, DrawPeaksTroughs);
        DrawSection("Twirl",            ref foldTwirl,        DrawTwirl);
        DrawSection("Whirlpool FX",    ref foldWhirlpoolFX,  DrawWhirlpoolFX);
        DrawSection("Foam",             ref foldFoam,         DrawFoam);
        DrawSection("Depth",            ref foldDepth,        DrawDepth);
        DrawSection("Normal Texture",   ref foldNormal,       DrawNormal);
        DrawSection("Lights",           ref foldLights,       DrawLights);
        DrawSection("Music Preview",    ref foldMusic,        DrawMusicPreview);

        if (EditorGUI.EndChangeCheck())
            serializedWindow.ApplyModifiedPropertiesWithoutUndo();

        EditorGUILayout.EndScrollView();
    }

    // ── Toolbar ───────────────────────────────────────────────────────────────

    void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Wave Effects Tuner", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            applyLive = GUILayout.Toggle(applyLive, "Apply Live", EditorStyles.toolbarButton, GUILayout.Width(80));
        }
    }

    // ── Preset row ────────────────────────────────────────────────────────────

    void DrawPresetRow()
    {
        GUILayout.Space(4);
        var newMaterial = (Material)EditorGUILayout.ObjectField("Material", waveMaterial, typeof(Material), false);
        if (newMaterial != waveMaterial) { waveMaterial = newMaterial; SaveToPrefs(); }
        GUILayout.Space(2);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel("Active Preset");
            var newPreset = (WavePreset)EditorGUILayout.ObjectField(activePreset, typeof(WavePreset), false);
            if (newPreset != activePreset)
            {
                activePreset = newPreset;
                SaveToPrefs();
                // Values are NOT loaded automatically — press Load to apply to the tuner
            }
        }

        GUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(activePreset == null))
            {
                if (GUILayout.Button("Load/Preview")) { LoadOrPreview(); }
                if (GUILayout.Button("Save"))  SaveToActivePreset();
            }
            if (GUILayout.Button("Save as New")) ExportAsNewPreset();
        }

        if (activePreset == null)
            EditorGUILayout.HelpBox("Assign an Active Preset to enable Load and Save.", MessageType.None);
        else if (!tunerActive)
            EditorGUILayout.HelpBox("Tuner inactive — click Load to begin editing. No changes are being applied to the scene.", MessageType.Info);
    }

    // ── Section helper ────────────────────────────────────────────────────────

    void DrawSection(string title, ref bool foldout, System.Action drawContent)
    {
        foldout = EditorGUILayout.BeginFoldoutHeaderGroup(foldout, title);
        if (foldout)
        {
            EditorGUI.indentLevel++;
            drawContent();
            EditorGUI.indentLevel--;
            GUILayout.Space(4);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SECTION CONTENT
    // ─────────────────────────────────────────────────────────────────────────

    void DrawWaveMotion()
    {
        EditorGUI.BeginChangeCheck();
        using (new EditorGUILayout.HorizontalScope())
        {
            waveIncreaseFactor = EditorGUILayout.Slider("Wave Increase Factor", waveIncreaseFactor, 0.1f, 5f);
            if (GUILayout.Button("Reset", GUILayout.Width(50)))
            {
                waveIncreaseFactor = 1f;
                frequency   = baseFrequency;
                speed       = baseSpeed;
                rippleDepth = baseRippleDepth;
                GUI.FocusControl(null);
            }
        }
        if (EditorGUI.EndChangeCheck())
        {
            frequency   = baseFrequency * waveIncreaseFactor;
            speed       = baseSpeed * waveIncreaseFactor;
            rippleDepth = baseRippleDepth * waveIncreaseFactor;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(EditorGUIUtility.labelWidth);
            if (GUILayout.Button(isPreviewing ? "■ Stop Preview" : "▶ Play Transition", GUILayout.Height(25)))
            {
                if (isPreviewing)
                {
                    isPreviewing = false;
                }
                else
                {
                    isPreviewing = true;
                    previewStartTime = EditorApplication.timeSinceStartup;
                    // Ensure slider starts at 1.0 when we play
                    waveIncreaseFactor = 1f;
                }
            }
            
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 50;
            previewTargetValue = EditorGUILayout.FloatField("Target", previewTargetValue, GUILayout.Width(90), GUILayout.Height(25));
            EditorGUIUtility.labelWidth = 60;
            previewDuration = EditorGUILayout.FloatField("Ramp", previewDuration, GUILayout.Width(100), GUILayout.Height(25));
            EditorGUIUtility.labelWidth = 40;
            previewHoldDuration = EditorGUILayout.FloatField("Hold", previewHoldDuration, GUILayout.Width(80), GUILayout.Height(25));
            EditorGUIUtility.labelWidth = oldLabelWidth;
        }

        if (isPreviewing)
        {
            float elapsed = (float)(EditorApplication.timeSinceStartup - previewStartTime);
            float total = previewDuration + previewHoldDuration;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(total, 0.01f));
            Rect rect = EditorGUILayout.GetControlRect(false, 5);
            rect.xMin += EditorGUIUtility.labelWidth;
            EditorGUI.ProgressBar(rect, progress, elapsed <= previewDuration ? "Ramping..." : "Holding...");
        }

        EditorGUILayout.Space(4);

        EditorGUI.BeginChangeCheck();
        frequency    = EditorGUILayout.FloatField("Frequency",      frequency);
        if (EditorGUI.EndChangeCheck()) baseFrequency = frequency / Mathf.Max(waveIncreaseFactor, 0.001f);

        EditorGUI.BeginChangeCheck();
        speed        = EditorGUILayout.FloatField("Speed",          speed);
        if (EditorGUI.EndChangeCheck()) baseSpeed = speed / Mathf.Max(waveIncreaseFactor, 0.001f);

        EditorGUI.BeginChangeCheck();
        rippleDepth  = EditorGUILayout.FloatField("Ripple Depth",   rippleDepth);
        if (EditorGUI.EndChangeCheck()) baseRippleDepth = rippleDepth / Mathf.Max(waveIncreaseFactor, 0.001f);

        waveStepRate = EditorGUILayout.FloatField("Wave Step Rate", waveStepRate);
        waveCenter   = EditorGUILayout.Vector4Field("Wave Center",  waveCenter);
    }

    void DrawSurface()
    {
        smoothness   = EditorGUILayout.Slider("Smoothness",   smoothness,   0f, 1f);
        transparency = EditorGUILayout.Slider("Transparency", transparency, 0f, 1f);
        strength     = EditorGUILayout.FloatField("Strength",              strength);
        baseColor    = EditorGUILayout.ColorField("Base Color",            baseColor);
    }

    void DrawPeaksTroughs()
    {
        troughBrightness     = EditorGUILayout.FloatField("Trough Brightness",       troughBrightness);
        peakBrightness       = EditorGUILayout.FloatField("Peak Brightness",         peakBrightness);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Soul Fish Zone", EditorStyles.boldLabel);
        soulFishMaskStrength = EditorGUILayout.Slider(   "Mask Strength",           soulFishMaskStrength, 0f, 1f);
        // Radius removed from the tuner — zone radius is authored per-zone in the Grid Designer and
        // baked per-point at spawn. _SoulFishRadius now only serves as the loose-fish fallback,
        // set at runtime by WaveMaterialController.
        soulFishBrightness1  = EditorGUILayout.FloatField("Brightness 1",            soulFishBrightness1);
        EditorGUILayout.LabelField("Edge Noise", EditorStyles.miniBoldLabel);
        soulFishFadeStart         = EditorGUILayout.Slider(    "Fade Start",         soulFishFadeStart, 0f, 1f);
        soulFishEdgeNoiseStrength = EditorGUILayout.FloatField("Edge Noise Strength",  soulFishEdgeNoiseStrength);
        soulFishEdgeNoiseScale    = EditorGUILayout.FloatField("Edge Noise Scale (base)", soulFishEdgeNoiseScale);
        soulFishEdgeNoiseScale2   = EditorGUILayout.FloatField("Edge Noise Scale (fine)", soulFishEdgeNoiseScale2);
        soulFishEdgeNoiseSpeed    = EditorGUILayout.FloatField("Edge Noise Speed",     soulFishEdgeNoiseSpeed);
        zoneTiling           = EditorGUILayout.Vector2Field("Zone Tiling",            zoneTiling);
        zoneScrollSpeed      = EditorGUILayout.FloatField("Scroll Speed",            zoneScrollSpeed);
        zoneNoiseStrength    = EditorGUILayout.Slider(    "Noise Strength",          zoneNoiseStrength, 0f, 1f);
        zoneTexture          = (Texture2D)EditorGUILayout.ObjectField("Zone Texture", zoneTexture, typeof(Texture2D), false);

        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Log Baked State"))
        {
            var linker = FindObjectOfType<SoulFishWaveLinker>();
            if (linker != null) linker.LogBakedState();
            else Debug.LogWarning("[WaveEffectsLiveTuner] SoulFishWaveLinker not found.");
        }
        if (GUILayout.Button("Rebake Soul Fish Mask"))
        {
            if (waveMaterial)
            {
                ApplyToMaterial();
                SoulFishWaveLinker.BakeToMaterial(waveMaterial);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    void DrawTwirl()
    {
        twirlBaseStrength = EditorGUILayout.FloatField("Base Strength", twirlBaseStrength);
        twirlSlopeBoost   = EditorGUILayout.FloatField("Slope Boost",  twirlSlopeBoost);
        twirlScale        = EditorGUILayout.FloatField("Scale",        twirlScale);
        depthTwirlStrength = EditorGUILayout.FloatField("Depth Twirl", depthTwirlStrength);
        whirlpoolTwirlStrength     = EditorGUILayout.FloatField("Whirlpool Twirl",      whirlpoolTwirlStrength);
        whirlpoolAreaTwirlStrength = EditorGUILayout.FloatField("Whirlpool Area Twirl", whirlpoolAreaTwirlStrength);
        soulFishTwirlStrength      = EditorGUILayout.FloatField("Soul Fish Twirl",      soulFishTwirlStrength);
    }

    void DrawWhirlpoolFX()
    {
        whirlpoolTaper          = EditorGUILayout.Slider("Taper",          whirlpoolTaper,          0.1f, 4f);
        whirlpoolDarkRadiusMult = EditorGUILayout.Slider("Dark Radius Mult",whirlpoolDarkRadiusMult, 0.5f, 4f);
        whirlpoolDarkStrength   = EditorGUILayout.Slider("Dark Strength",  whirlpoolDarkStrength,   0f,   1f);
        whirlpoolFalloffPower   = EditorGUILayout.Slider("Falloff Power",  whirlpoolFalloffPower,   0.1f, 4f);
    }

    void DrawTestFX()
    {
        EditorGUILayout.HelpBox("Temporary — not saved to presets.", MessageType.None);

        // ── Test Whirlpools ──
        GUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Whirlpools", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            bool wasEnabled = testWhirlpoolsEnabled;
            testWhirlpoolsEnabled = EditorGUILayout.Toggle(testWhirlpoolsEnabled, GUILayout.Width(20));
            if (wasEnabled && !testWhirlpoolsEnabled) ClearTestWhirlpools();
        }

        for (int i = 0; i < testWhirlpools.Count; i++)
        {
            var wp = testWhirlpools[i];
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"#{i + 1}", GUILayout.Width(24));
                wp.position = EditorGUILayout.Vector3Field(GUIContent.none, wp.position);
                wp.radius   = EditorGUILayout.FloatField(wp.radius, GUILayout.Width(48));
                if (GUILayout.Button("−", GUILayout.Width(22))) { testWhirlpools.RemoveAt(i); return; }
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(testWhirlpools.Count >= 8))
                if (GUILayout.Button("+ Whirlpool")) testWhirlpools.Add(new TestWhirlpoolEntry());
            if (GUILayout.Button("Clear")) { testWhirlpools.Clear(); ClearTestWhirlpools(); }
        }

        GUILayout.Space(6);

        // ── Test Soul Fish ──
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Soul Fish", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            bool wasEnabled = testSoulFishEnabled;
            testSoulFishEnabled = EditorGUILayout.Toggle(testSoulFishEnabled, GUILayout.Width(20));
            if (wasEnabled && !testSoulFishEnabled) ClearTestSoulFish();
        }

        for (int i = 0; i < testSoulFishPositions.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"#{i + 1}", GUILayout.Width(24));
                testSoulFishPositions[i] = EditorGUILayout.Vector3Field(GUIContent.none, testSoulFishPositions[i]);
                if (GUILayout.Button("−", GUILayout.Width(22))) { testSoulFishPositions.RemoveAt(i); return; }
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(testSoulFishPositions.Count >= 10))
                if (GUILayout.Button("+ Soul Fish")) testSoulFishPositions.Add(Vector3.zero);
            if (GUILayout.Button("Clear")) { testSoulFishPositions.Clear(); ClearTestSoulFish(); }
        }
    }

    void ApplyTestWhirlpools()
    {
        var wm = WhirlpoolManager.Instance;
        if (wm == null) return;

        if (!testWhirlpoolsEnabled || testWhirlpools.Count == 0)
            return;

        int count = Mathf.Min(testWhirlpools.Count, 8);
        var data  = new Vector4[8];
        float lossyScale = wm.wavePlaneTransform != null ? wm.wavePlaneTransform.lossyScale.x : 1f;

        for (int i = 0; i < count; i++)
        {
            var wp = testWhirlpools[i];
            Vector3 local = wm.wavePlaneTransform != null
                ? wm.wavePlaneTransform.InverseTransformPoint(wp.position)
                : new Vector3(wp.position.x, -wp.position.z, wp.position.y);
            data[i] = new Vector4(local.x, local.y, local.z, wp.radius / lossyScale);
        }

        wm.ApplyPositions(data, count, wm.globalDepth, wm.globalSwirl);
    }

    void ClearTestWhirlpools()
    {
        var wm = WhirlpoolManager.Instance;
        if (wm != null) wm.ResetOverride();
    }

    void ApplyTestSoulFish()
    {
        if (!waveMaterial || !testSoulFishEnabled || testSoulFishPositions.Count == 0) return;

        int count = Mathf.Min(testSoulFishPositions.Count, 10);
        for (int i = 0; i < count; i++)
            waveMaterial.SetVector("_SoulFishPosition" + (i + 1), testSoulFishPositions[i]);
        for (int i = count; i < 10; i++)
            waveMaterial.SetVector("_SoulFishPosition" + (i + 1), OffMap);
        waveMaterial.SetFloat("_SoulFishCount", count);
    }

    void ClearTestSoulFish()
    {
        if (!waveMaterial) return;
        // Re-bake from live linker data so runtime zones/fish aren't lost.
        // If no zones are registered (empty session), this writes OffMap which is correct.
        SoulFishWaveLinker.BakeToMaterial(waveMaterial);
    }

    void DrawFoam()
    {
        foamLine          = EditorGUILayout.FloatField("FoamLine",          foamLine);
        foamColor         = EditorGUILayout.ColorField("FoamColor",         foamColor);
        foamDistanceDepth = EditorGUILayout.FloatField("FoamDistanceDepth", foamDistanceDepth);
        foamDepthFade     = EditorGUILayout.FloatField("FoamDepthFade",     foamDepthFade);
        foamDitherSize    = EditorGUILayout.FloatField("FoamDitherSize",    foamDitherSize);
    }

    void DrawDepth()
    {
        depthFade         = EditorGUILayout.FloatField("DepthFade",         depthFade);
        distanceDepth     = EditorGUILayout.FloatField("DistanceDepth",     distanceDepth);
        depthFadeLine     = EditorGUILayout.FloatField("DepthFadeLine",     depthFadeLine);
        depthColour       = EditorGUILayout.ColorField("DepthColour",       depthColour);
        depthFadeStrength = EditorGUILayout.FloatField("DepthFadeStrength", depthFadeStrength);
        lightDirection    = EditorGUILayout.Vector3Field("LightDirection",  lightDirection);
    }

    void DrawNormal()
    {
        normalTexture     = (Texture2D)EditorGUILayout.ObjectField("Normal Texture", normalTexture, typeof(Texture2D), false);
        normalTextureProp = EditorGUILayout.TextField("Property Name", normalTextureProp);
    }

    void DrawLights()
    {
        for (int i = 0; i < lights.Count; i++)
        {
            var entry = lights[i];
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"Light {i + 1}", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("−", GUILayout.Width(24)))
                    {
                        lights.RemoveAt(i);
                        return; // avoid iterating modified list
                    }
                }
                entry.light     = (Light)EditorGUILayout.ObjectField("Scene Light", entry.light, typeof(Light), true);
                entry.color     = EditorGUILayout.ColorField("Color",     entry.color);
                entry.intensity = EditorGUILayout.FloatField("Intensity", entry.intensity);
                entry.range     = EditorGUILayout.FloatField("Range",     entry.range);

                // Sync from live scene light
                if (entry.light != null && GUILayout.Button("Capture from Light"))
                {
                    entry.color     = entry.light.color;
                    entry.intensity = entry.light.intensity;
                    entry.range     = entry.light.range;
                }
            }
            GUILayout.Space(2);
        }

        if (GUILayout.Button("+ Add Light"))
            lights.Add(new LightEntry());
    }

    void DrawMusicPreview()
    {
        musicIntro = (AudioClip)EditorGUILayout.ObjectField("Music Intro", musicIntro, typeof(AudioClip), false);
        musicLoop  = (AudioClip)EditorGUILayout.ObjectField("Music Loop",  musicLoop,  typeof(AudioClip), false);

        GUILayout.Space(4);

        bool hasIntro = musicIntro != null;
        bool hasLoop  = musicLoop  != null;

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!hasIntro))
            {
                if (GUILayout.Button("▶  Intro")) PlayClipInEditor(musicIntro);
            }
            using (new EditorGUI.DisabledScope(!hasLoop))
            {
                if (GUILayout.Button("▶  Loop"))  PlayClipInEditor(musicLoop);
            }
            if (GUILayout.Button("■  Stop")) StopAllPreviewClips();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // APPLY
    // ─────────────────────────────────────────────────────────────────────────

    void ApplyToMaterial()
    {
        waveMaterial.SetFloat("_Frequency",    frequency);
        waveMaterial.SetFloat("_Speed",        speed);
        waveMaterial.SetFloat("_RippleDepth",  rippleDepth);
        waveMaterial.SetFloat("_WaveStepRate", waveStepRate);

        waveMaterial.SetFloat("_Smoothness",   smoothness);
        waveMaterial.SetFloat("_Transparency", transparency);
        waveMaterial.SetFloat("_Strength",     strength);
        waveMaterial.SetColor("_BaseColor",    baseColor);

        waveMaterial.SetFloat("_TroughRingWave1Brightness", troughBrightness);
        waveMaterial.SetFloat("_PeakRingWave2Brightness",   peakBrightness);
        waveMaterial.SetFloat("_SoulFishMaskStrength",      soulFishMaskStrength);
        waveMaterial.SetFloat("_SoulFishRadius",            soulFishRadius);
        waveMaterial.SetFloat("_SoulFishBrightness1",       soulFishBrightness1);
        waveMaterial.SetFloat("_SoulFishFadeStart",         soulFishFadeStart);
        waveMaterial.SetFloat("_SoulFishEdgeNoiseStrength", soulFishEdgeNoiseStrength);
        waveMaterial.SetFloat("_SoulFishEdgeNoiseScale",    soulFishEdgeNoiseScale);
        waveMaterial.SetFloat("_SoulFishEdgeNoiseScale2",   soulFishEdgeNoiseScale2);
        waveMaterial.SetFloat("_SoulFishEdgeNoiseSpeed",    soulFishEdgeNoiseSpeed);
        waveMaterial.SetVector("_ZoneTiling",              zoneTiling);
        waveMaterial.SetFloat("_ZoneScrollSpeed",          zoneScrollSpeed);
        waveMaterial.SetFloat("_ZoneNoiseStrength",        zoneNoiseStrength);
        if (zoneTexture != null) waveMaterial.SetTexture("_ZoneTexture", zoneTexture);

        waveMaterial.SetFloat("_TwirlBaseStrength", twirlBaseStrength);
        waveMaterial.SetFloat("_TwirlSlopeBoost",  twirlSlopeBoost);
        waveMaterial.SetFloat("_TwirlScale",       twirlScale);
        waveMaterial.SetFloat("_DepthTwirlStrength", depthTwirlStrength);
        waveMaterial.SetFloat("_DepthFadeStrength",  depthFadeStrength);
        waveMaterial.SetFloat("_DepthFadeLine",      depthFadeLine);
        waveMaterial.SetFloat("_WhirlpoolTwirlStrength",     whirlpoolTwirlStrength);
        waveMaterial.SetFloat("_WhirlpoolAreaTwirlStrength", whirlpoolAreaTwirlStrength);
        waveMaterial.SetFloat("_SoulFishTwirlStrength",      soulFishTwirlStrength);

        waveMaterial.SetFloat("_WhirlpoolTaper",          whirlpoolTaper);
        waveMaterial.SetFloat("_WhirlpoolDarkRadiusMult", whirlpoolDarkRadiusMult);
        waveMaterial.SetFloat("_WhirlpoolDarkStrength",   whirlpoolDarkStrength);
        waveMaterial.SetFloat("_WhirlpoolFalloffPower",   whirlpoolFalloffPower);

        waveMaterial.SetFloat("_FoamDitherSize",    foamDitherSize);
        waveMaterial.SetFloat("_FoamDistanceDepth", foamDistanceDepth);
        waveMaterial.SetFloat("_FoamDepthFade",     foamDepthFade);
        waveMaterial.SetColor("_FoamColor",  foamColor);
        waveMaterial.SetFloat("_FoamLine",  foamLine);
        waveMaterial.SetFloat("_DepthFade", depthFade);

        waveMaterial.SetColor("_DepthColour",     depthColour);
        waveMaterial.SetFloat("_DistanceDepth",   distanceDepth);
        waveMaterial.SetVector("_LightDirection", lightDirection);

        if (normalTexture && !string.IsNullOrEmpty(normalTextureProp))
            waveMaterial.SetTexture(normalTextureProp, normalTexture);

        waveMaterial.SetVector("_WaveCenter", waveCenter);
    }

    void ApplyToLights()
    {
        foreach (var e in lights)
        {
            if (e.light == null) continue;
            e.light.color     = e.color;
            e.light.intensity = e.intensity;
            e.light.range     = e.range;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LOAD / SAVE
    // ─────────────────────────────────────────────────────────────────────────

    void LoadFromPreset(WavePreset preset)
    {
        if (preset == null) return;

        var s = preset.state;
        waveIncreaseFactor    = 1f;
        baseFrequency         = s.Frequency;
        baseSpeed             = s.Speed;
        baseRippleDepth       = s.RippleDepth;

        frequency             = s.Frequency;
        speed                 = s.Speed;
        rippleDepth           = s.RippleDepth;
        waveStepRate          = s.WaveStepRate;
        smoothness            = s.Smoothness;
        transparency          = s.Transparency;
        strength              = s.Strength;
        baseColor             = s.BaseColor;
        troughBrightness       = s.TroughRingWave1Brightness;
        peakBrightness         = s.PeakRingWave2Brightness;
        soulFishMaskStrength    = s.SoulFishMaskStrength;
        soulFishBrightness1     = s.SoulFishBrightness1;
        // SoulFishRadius wasn't in older presets — fall back to the material's live value rather than
        // overwriting with the default 0, which would silently kill the mask.
        soulFishRadius = s.SoulFishRadius > 0f
            ? s.SoulFishRadius
            : (waveMaterial != null ? waveMaterial.GetFloat("_SoulFishRadius") : soulFishRadius);
        // Edge-noise wasn't in older presets either — a zero noise scale marks "unset",
        // so keep the tuner's current values rather than zeroing the effect (same
        // pattern as the SoulFishRadius fallback above).
        if (s.SoulFishEdgeNoiseScale > 0f)
        {
            soulFishFadeStart         = s.SoulFishFadeStart;
            soulFishEdgeNoiseStrength = s.SoulFishEdgeNoiseStrength;
            soulFishEdgeNoiseScale    = s.SoulFishEdgeNoiseScale;
            soulFishEdgeNoiseScale2   = s.SoulFishEdgeNoiseScale2;
            soulFishEdgeNoiseSpeed    = s.SoulFishEdgeNoiseSpeed;
        }
        zoneTiling              = s.ZoneTiling;
        zoneScrollSpeed         = s.ZoneScrollSpeed;
        zoneNoiseStrength       = s.ZoneNoiseStrength;
        if (s.ZoneTexture) zoneTexture = s.ZoneTexture;
        twirlBaseStrength = s.TwirlBaseStrength;
        twirlSlopeBoost   = s.TwirlSlopeBoost;
        twirlScale        = s.TwirlScale;
        depthTwirlStrength = s.DepthTwirlStrength;
        depthFadeStrength  = s.DepthFadeStrength;
        depthFadeLine      = s.DepthFadeLine;
        whirlpoolTwirlStrength     = s.WhirlpoolTwirlStrength;
        whirlpoolAreaTwirlStrength = s.WhirlpoolAreaTwirlStrength;
        soulFishTwirlStrength      = s.SoulFishTwirlStrength;
        whirlpoolTaper          = s.WhirlpoolTaper;
        whirlpoolDarkRadiusMult = s.WhirlpoolDarkRadiusMult;
        whirlpoolDarkStrength   = s.WhirlpoolDarkStrength;
        whirlpoolFalloffPower   = s.WhirlpoolFalloffPower;
        foamDitherSize    = s.FoamDitherSize;
        foamDistanceDepth = s.FoamDistanceDepth;
        foamDepthFade     = s.FoamDepthFade;
        foamColor              = s.FoamColor;
        foamLine  = s.FoamLine;
        depthFade = s.DepthFade;
        depthColour            = s.DepthColour;
        distanceDepth          = s.DistanceDepth;
        lightDirection         = s.LightDirection;
        if (s.NormalTexture) normalTexture = s.NormalTexture;
        waveCenter = s.WaveCenter;

        musicIntro = preset.musicIntro;
        musicLoop  = preset.musicLoop;

        // Load light settings into existing entries (preserves scene Light refs)
        if (preset.lights != null)
        {
            int count = Mathf.Min(lights.Count, preset.lights.Length);
            for (int i = 0; i < count; i++)
            {
                lights[i].color     = preset.lights[i].color;
                lights[i].intensity = preset.lights[i].intensity;
                lights[i].range     = preset.lights[i].range;
            }
        }

        Repaint();
    }

    void LoadOrPreview()
    {
        if (activePreset == null) return;

        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Waves1")
        {
            LoadFromPreset(activePreset);
            tunerActive = true;
            FrameSceneViewOnWaves();
            return;
        }

        bool open = EditorUtility.DisplayDialog(
            "Open Waves1?",
            $"You are not in the Waves1 scene.\n\nOpen Waves1 and load preset '{activePreset.name}'?",
            "Open Scene", "Cancel");

        if (!open) return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EditorSceneManager.OpenScene("Assets/Scenes/Waves1.unity",
            OpenSceneMode.Single);

        LoadFromPreset(activePreset);
        tunerActive = true;
        FrameSceneViewOnWaves();
    }

    void FrameSceneViewOnWaves()
    {
        SceneView sv = SceneView.lastActiveSceneView;
        if (sv == null) return;

        var controller = Object.FindFirstObjectByType<WaveMaterialController>();
        if (controller != null)
        {
            sv.Frame(new Bounds(controller.transform.position, Vector3.one * 20f), false);
            return;
        }

        if (waveMaterial != null)
        {
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == waveMaterial)
                    {
                        sv.Frame(r.bounds, false);
                        return;
                    }
                }
            }
        }

        sv.FrameSelected();
    }

    void SaveToActivePreset()
    {
        if (activePreset == null)
        {
            Debug.LogWarning("[WaveEffectsLiveTuner] No active preset assigned.");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "Save Wave Preset",
                $"Overwrite '{activePreset.name}' with the current tuner values?\n\nThis cannot be undone from disk.",
                "Save", "Cancel"))
            return;

        Undo.RecordObject(activePreset, "Save Wave Preset");
        activePreset.state      = BuildState();
        activePreset.lights     = BuildLightEntries();
        activePreset.musicIntro = musicIntro;
        activePreset.musicLoop  = musicLoop;
        EditorUtility.SetDirty(activePreset);
        AssetDatabase.SaveAssetIfDirty(activePreset);
        Debug.Log($"[WaveEffectsLiveTuner] Saved to {activePreset.name}");
    }

    void ExportAsNewPreset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Wave Preset", "NewWavePreset", "asset", "Choose save location",
            "Assets/ScriptsData/DataScripts/WavePreset");
        if (string.IsNullOrEmpty(path)) return;

        var preset = CreateInstance<WavePreset>();
        preset.state  = BuildState();
        preset.lights = BuildLightEntries();

        preset.musicIntro = musicIntro;
        preset.musicLoop  = musicLoop;
        if (activePreset != null)
            preset.sonarEnabled = activePreset.sonarEnabled;

        AssetDatabase.CreateAsset(preset, path);
        AssetDatabase.SaveAssets();
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = preset;
        activePreset = preset;
        Repaint();

        Debug.Log($"[WaveEffectsLiveTuner] WavePreset saved to {path}");
    }

    WaveMaterialController.WaveState BuildState() => new WaveMaterialController.WaveState
    {
        Frequency                 = frequency,
        Speed                     = speed,
        RippleDepth               = rippleDepth,
        WaveStepRate              = waveStepRate,
        Smoothness                = smoothness,
        Transparency              = transparency,
        Strength                  = strength,
        BaseColor                 = baseColor,
        TroughRingWave1Brightness = troughBrightness,
        PeakRingWave2Brightness   = peakBrightness,
        SoulFishMaskStrength      = soulFishMaskStrength,
        SoulFishRadius            = soulFishRadius,
        SoulFishBrightness1       = soulFishBrightness1,
        SoulFishFadeStart         = soulFishFadeStart,
        SoulFishEdgeNoiseStrength = soulFishEdgeNoiseStrength,
        SoulFishEdgeNoiseScale    = soulFishEdgeNoiseScale,
        SoulFishEdgeNoiseScale2   = soulFishEdgeNoiseScale2,
        SoulFishEdgeNoiseSpeed    = soulFishEdgeNoiseSpeed,
        ZoneTiling                = zoneTiling,
        ZoneScrollSpeed           = zoneScrollSpeed,
        ZoneNoiseStrength         = zoneNoiseStrength,
        ZoneTexture               = zoneTexture,
        TwirlBaseStrength = twirlBaseStrength,
        TwirlSlopeBoost   = twirlSlopeBoost,
        TwirlScale        = twirlScale,
        DepthTwirlStrength = depthTwirlStrength,
        DepthFadeStrength  = depthFadeStrength,
        DepthFadeLine      = depthFadeLine,
        WhirlpoolTwirlStrength     = whirlpoolTwirlStrength,
        WhirlpoolAreaTwirlStrength = whirlpoolAreaTwirlStrength,
        SoulFishTwirlStrength      = soulFishTwirlStrength,
        WhirlpoolTaper            = whirlpoolTaper,
        WhirlpoolDarkRadiusMult   = whirlpoolDarkRadiusMult,
        WhirlpoolDarkStrength     = whirlpoolDarkStrength,
        WhirlpoolFalloffPower     = whirlpoolFalloffPower,
        LightDirection            = lightDirection,
        FoamDitherSize    = foamDitherSize,
        FoamDistanceDepth = foamDistanceDepth,
        FoamDepthFade     = foamDepthFade,
        FoamColor                 = foamColor,
        FoamLine  = foamLine,
        DepthFade = depthFade,
        DepthColour               = depthColour,
        DistanceDepth             = distanceDepth,
        NormalTexture             = normalTexture,
        WaveCenter                = waveCenter,
    };

    WavePreset.WaveLightEntry[] BuildLightEntries()
    {
        if (lights == null || lights.Count == 0) return null;
        var entries = new WavePreset.WaveLightEntry[lights.Count];
        for (int i = 0; i < lights.Count; i++)
            entries[i] = new WavePreset.WaveLightEntry
            {
                color     = lights[i].color,
                intensity = lights[i].intensity,
                range     = lights[i].range,
            };
        return entries;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUDIO PREVIEW
    // ─────────────────────────────────────────────────────────────────────────

    static readonly System.Type AudioUtilType =
        typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

    static void PlayClipInEditor(AudioClip clip)
    {
        if (clip == null || AudioUtilType == null) return;
        StopAllPreviewClips();

        MethodInfo method =
            AudioUtilType.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public) ??
            AudioUtilType.GetMethod("PlayClip",        BindingFlags.Static | BindingFlags.Public);

        if (method == null) return;
        var parms = method.GetParameters();
        if (parms.Length >= 3)
            method.Invoke(null, new object[] { clip, 0, false });
        else if (parms.Length == 1)
            method.Invoke(null, new object[] { clip });
    }

    static void StopAllPreviewClips()
    {
        if (AudioUtilType == null) return;
        MethodInfo method =
            AudioUtilType.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public) ??
            AudioUtilType.GetMethod("StopAllClips",        BindingFlags.Static | BindingFlags.Public);
        method?.Invoke(null, null);
    }
}
