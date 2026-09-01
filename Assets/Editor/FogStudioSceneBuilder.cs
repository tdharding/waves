using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Builds everything the fog needs to be judged, into whatever scene is open.
///
/// The point of a studio scene is that the gameplay scene is too full to see fog in. Fog is a
/// subtle, low, wide effect and it gets lost among rocks, souls, walls and lights. So this puts
/// down the minimum that makes each fog decision judgeable, and nothing else:
///
///   Rocks         — standoff and the wrap behaviour cannot be judged against empty water.
///   A street light — REAL, from the prefab, not a stand-in. InstancedLights reads bare $Globals,
///                   so a faked light array is overwritten by InstancedLightManager on the next
///                   frame and the result looks like a broken shader rather than a missing rig.
///   The fog sheet  — a plane just above the waterline. Without it the field runs and paints, and
///                   absolutely nothing appears on screen.
///
/// Everything lands under one root so a rebuild replaces it cleanly rather than piling up.
/// </summary>
public static class FogStudioSceneBuilder
{
    const string ROOT = "— Fog Studio Rig —";

    const string SPIKE_PREFAB  = "Assets/Prefab/MazePieces/ProceduralSpikePrefab.prefab";
    const string LIGHT_PREFAB  = "Assets/Prefab/SetPieces/StreetLight.prefab";
    const string SPIKE_PRESET  = "Assets/Resources/Spikes/SpikeShapePreset1.asset";
    const string PAINT_MAT     = "Assets/ScriptsData/FogScripts/FogPaint.mat";
    const string BLUR_MAT      = "Assets/ScriptsData/FogScripts/FogBlur.mat";
    const string SHEET_MAT     = "Assets/ScriptsData/FogScripts/FogSheet.mat";

    // Matches the measured arena: radius ~20, so 40 across, 32 grid cells at 1.25 units each.
    const float COVERAGE = 40f;

    [MenuItem("Waves/Fog Studio Scene/Build Rig")]
    public static void Build()
    {
        var existing = GameObject.Find(ROOT);
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Rebuild fog rig?",
                    "A fog rig is already in this scene. Replace it?", "Replace", "Cancel"))
                return;
            Undo.DestroyObjectImmediate(existing);
        }

        var root = new GameObject(ROOT);
        Undo.RegisterCreatedObjectUndo(root, "Build Fog Rig");

        var boat  = BuildBoatStandIn(root.transform);
        BuildWater(root.transform);
        BuildFogSheet(root.transform);
        BuildSpikes(root.transform);
        BuildLights(root.transform);
        BuildManager(root.transform, boat);
        BuildCamera(root.transform);

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Fog Studio] Rig built. Press play — fog forms around the boat stand-in and the " +
                  "lit street light, and is pushed off the rocks.", root);
    }

    // ────────────────────────────────────────────────────────────────────────
    static Transform BuildBoatStandIn(Transform parent)
    {
        // The field centres on _BoatWorldCenter in the real game, and nothing pushes that global
        // in a scene with no boat. So the manager gets an explicit transform instead — move this
        // around in play mode to watch the grid follow and blobs recycle around it.
        var go = new GameObject("Boat Stand-In (drag me in play mode)");
        go.transform.SetParent(parent, false);
        go.transform.position = Vector3.zero;
        return go.transform;
    }

    static void BuildWater(Transform parent)
    {
        var water = GameObject.CreatePrimitive(PrimitiveType.Quad);
        water.name = "Water (flat stand-in)";
        water.transform.SetParent(parent, false);
        water.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        water.transform.localScale = Vector3.one * (COVERAGE * 1.6f);
        Object.DestroyImmediate(water.GetComponent<Collider>());

        // Deliberately not the real wave material: this is a dark ground to read fog against, not
        // an attempt to reproduce the water. Judging fog COLOUR here would be misleading.
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.name = "FogStudioWater";
        mat.color = new Color(0.05f, 0.07f, 0.10f);
        water.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static void BuildFogSheet(Transform parent)
    {
        var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
        sheet.name = "Fog Sheet";
        sheet.transform.SetParent(parent, false);
        sheet.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // Just clear of the water. The waterline black gradient on this level's preset rises 0.07,
        // so the vertical scale here is small — sitting the sheet any higher and fog visibly floats.
        sheet.transform.position = new Vector3(0f, 0.03f, 0f);
        sheet.transform.localScale = Vector3.one * COVERAGE;
        Object.DestroyImmediate(sheet.GetComponent<Collider>());

        var mat = AssetDatabase.LoadAssetAtPath<Material>(SHEET_MAT);
        if (mat != null) sheet.GetComponent<MeshRenderer>().sharedMaterial = mat;
        else Debug.LogWarning($"[Fog Studio] {SHEET_MAT} missing — the sheet has no fog material.");

        var mr = sheet.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    static void BuildSpikes(Transform parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SPIKE_PREFAB);
        var preset = AssetDatabase.LoadAssetAtPath<SpikeShapePreset>(SPIKE_PRESET);
        if (prefab == null)
        {
            Debug.LogWarning($"[Fog Studio] {SPIKE_PREFAB} missing — no rocks, so standoff and the " +
                             "wrap behaviour cannot be judged.");
            return;
        }

        var group = new GameObject("Rocks");
        group.transform.SetParent(parent, false);

        // Spaced so a drifting mass has to pass BETWEEN two of them as well as around one. A blob
        // squeezing through a gap is where standoff shows itself most clearly.
        Vector3[] spots =
        {
            new Vector3(-4.5f, 0f,  2.0f),
            new Vector3( 3.2f, 0f,  4.4f),
            new Vector3( 1.0f, 0f, -5.0f),
        };
        float[] scales = { 1.0f, 1.35f, 0.8f };

        for (int i = 0; i < spots.Length; i++)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
            go.name = $"Rock {i + 1}";
            go.transform.position = spots[i];

            var spike = go.GetComponent<ProceduralSpike>();
            if (spike != null)
            {
                // climbable false: this is scenery for the fog to wrap, not part of anyone's route.
                spike.Build(preset != null ? preset.config : new SpikeShapeConfig(), scales[i], false);
            }
        }

        if (preset == null)
            Debug.LogWarning($"[Fog Studio] {SPIKE_PRESET} missing — rocks built from defaults, so " +
                             "their waterline radius will not match the real level.");
    }

    static void BuildLights(Transform parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LIGHT_PREFAB);
        if (prefab == null)
        {
            Debug.LogWarning($"[Fog Studio] {LIGHT_PREFAB} missing — no lit lamp, so the lip and " +
                             "the clear pool cannot be judged. Fog will be flat and dark, which is " +
                             "correct behaviour with no light, not a bug.");
            return;
        }

        var group = new GameObject("Street Lights");
        group.transform.SetParent(parent, false);

        // Two, one lit and one not, so the difference is visible side by side without having to
        // toggle anything: fog banks up glowing around the lit one and ignores the dark one.
        var lit = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
        lit.name = "Street Light (lit)";
        lit.transform.position = new Vector3(-6.5f, 0f, -3.5f);
        lit.GetComponent<StreetLightController>()?.SetLit(true);

        var dark = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
        dark.name = "Street Light (unlit)";
        dark.transform.position = new Vector3(7.0f, 0f, -1.0f);
        dark.GetComponent<StreetLightController>()?.SetLit(false);
    }

    static void BuildManager(Transform parent, Transform boat)
    {
        var go = new GameObject("Fog Field Manager");
        go.transform.SetParent(parent, false);
        var mgr = go.AddComponent<FogFieldManager>();

        // Private serialized fields, so they go in through SerializedObject rather than being made
        // public purely for this builder's convenience.
        var so = new SerializedObject(mgr);
        SetObj(so, "paintMaterial", AssetDatabase.LoadAssetAtPath<Material>(PAINT_MAT), PAINT_MAT);
        SetObj(so, "blurMaterial",  AssetDatabase.LoadAssetAtPath<Material>(BLUR_MAT),  BLUR_MAT);
        so.FindProperty("boat").objectReferenceValue = boat;
        so.FindProperty("coverage").floatValue = COVERAGE;
        so.FindProperty("fogEnabled").boolValue = true;

        // Nothing to assign for shapes: a mass is made of properties held inline on the FogMap
        // now, so there is no shape library to point at. Author them in Waves > Fog Map.
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetObj(SerializedObject so, string prop, Object value, string path)
    {
        so.FindProperty(prop).objectReferenceValue = value;
        if (value == null) Debug.LogWarning($"[Fog Studio] {path} missing — assign it by hand.");
    }

    // ────────────────────────────────────────────────────────────────────────
    static void BuildCamera(Transform parent)
    {
        if (Object.FindAnyObjectByType<Camera>() != null) return;

        var go = new GameObject("Studio Camera");
        go.transform.SetParent(parent, false);

        // A raised, angled view — the same read the gameplay camera holds most of the time. Orbit
        // it to check the volume holds up; it is free in yaw and floored above the water there too.
        go.transform.position = new Vector3(0f, 12f, -14f);
        go.transform.rotation = Quaternion.Euler(38f, 0f, 0f);

        var cam = go.AddComponent<Camera>();
        cam.backgroundColor = new Color(0.03f, 0.04f, 0.06f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        go.AddComponent<AudioListener>();
    }
}
