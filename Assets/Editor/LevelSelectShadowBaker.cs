using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Bakes the level select's shadows: how far the world is from the <see cref="LevelSelectLight"/>
/// in every direction, saved as one shadow map. The light never moves, so this is done once, on
/// the Bake Shadows button — not every frame.
///
/// A ray goes out from the light through every texel of the map and stops at the first thing it
/// meets; the map holds that distance. A surface farther from the light than the map says, along
/// its own direction, is behind something — in shadow. The <c>LevelSelectShadow</c> subgraph does
/// that comparison.
///
/// The map is octahedral: the whole sphere of directions folded flat, straight down at its centre,
/// so the ground below the light sits in the middle of the map, away from the folds.
/// <c>LevelSelectShadow.hlsl</c> folds directions the same way — the two must agree.
///
/// What casts: every drawn mesh under the containers in <see cref="CasterParents"/> (the landscape
/// tiles too, so the hills shadow themselves), except the river water — it is on the Water layer.
/// A renderer's Cast Shadows setting is NOT read: that is for Unity's own shadows, and the
/// landscape tile prefab has it Off, which would leave every hill out. The rays are cast against
/// copies of those meshes in a preview scene of their own, so nothing in the open scene is touched.
/// </summary>
public static class LevelSelectShadowBaker
{
    /// <summary>The Shadow Map Size choices, texels along each side of the map.</summary>
    public static readonly int[] Sizes = { 1024, 2048, 4096, 8192 };

    /// <summary>The light's Shadow Map Size, or the nearest choice to it.</summary>
    public static int SizeOf(LevelSelectLight light)
    {
        int best = Sizes[0];
        foreach (int s in Sizes)
            if (Mathf.Abs(s - light.shadowMapSize) < Mathf.Abs(best - light.shadowMapSize)) best = s;
        return best;
    }

    /// <summary>A ray that meets nothing stores this — farther than anything, so never shadowed.</summary>
    private const float Nothing = 1e20f;

    private const string Folder = "Assets/Prefab/LevelSelectPrefabs/Generated";

    // The world's containers, as the designer and the tools build them. The boat, the camera, the
    // fog, the canvas and the trigger boxes are left out on purpose.
    internal static readonly string[] CasterParents =
    {
        "LANDSCAPETILES", "MAINRIVERVISUALS", "RIVERBRANCHES", "RIVERJUNCTIONS", "RIVERGATEsobstacles",
        "RIVERPOOLS", "OUTPOSTS", "PIPES", "WALLS", "ARENAS", "SHOPS", "SPIKES", "FREESTANDINGSTRUCTURES"
    };

    // Rays per batch — enough to keep the job system busy without holding all 4 million hits at once.
    private const int BatchRows = 128;

    public static void Bake(LevelSelectLight light)
    {
        if (light == null) return;

        var casters = FindCasters(light.gameObject.scene, out string summary);
        if (casters.Count == 0)
        {
            Debug.LogWarning("[LevelSelectShadow] Nothing to cast shadows — none of the world's " +
                             "containers has a drawn mesh. No shadow map baked.");
            return;
        }

        Vector3 from = light.transform.position;
        int     size = SizeOf(light);
        float[] distances;

        var preview = EditorSceneManager.NewPreviewScene();
        var meshes  = new List<Mesh>();
        try
        {
            var physics = preview.GetPhysicsScene();
            if (!physics.IsValid() || physics == Physics.defaultPhysicsScene)
            {
                Debug.LogError("[LevelSelectShadow] The preview scene has no physics of its own, so " +
                               "the rays would hit the open scene's colliders. No shadow map baked.");
                return;
            }

            BuildColliders(casters, preview, meshes);
            distances = CastRays(physics, from, size);
            if (distances == null) return;   // cancelled
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            EditorSceneManager.ClosePreviewScene(preview);
            foreach (var m in meshes) Object.DestroyImmediate(m);
        }

        var map = SaveMap(light.gameObject.scene, distances, size);

        Undo.RecordObject(light, "Bake Shadows");
        light.shadowMap       = map;
        light.shadowBakedFrom = from;
        EditorUtility.SetDirty(light);
        EditorSceneManager.MarkSceneDirty(light.gameObject.scene);

        LevelSelectLight.Push();
        SceneView.RepaintAll();

        Debug.Log($"[LevelSelectShadow] Baked {size}×{size} from {from} — " +
                  $"{casters.Count} casters ({summary}). Saved to {AssetDatabase.GetAssetPath(map)}.", map);
    }

    internal static List<(Mesh mesh, Matrix4x4 toWorld)> FindCasters(Scene scene, out string summary)
    {
        var casters = new List<(Mesh, Matrix4x4)>();
        var counts  = new List<string>();
        int water   = LayerMask.NameToLayer("Water");

        foreach (var root in scene.GetRootGameObjects())
        {
            if (System.Array.IndexOf(CasterParents, root.name) < 0) continue;

            int n = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled) continue;
                if (water >= 0 && r.gameObject.layer == water) continue;
                if (!r.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) continue;

                casters.Add((mf.sharedMesh, r.localToWorldMatrix));
                n++;
            }
            if (n > 0) counts.Add($"{root.name} {n}");
        }

        summary = string.Join(", ", counts);
        return casters;
    }

    /// <summary>
    /// One collider per caster, its mesh copied into world space — that carries any skew or
    /// mirroring the transform has, which a collider's own transform could not.
    /// </summary>
    internal static void BuildColliders(List<(Mesh mesh, Matrix4x4 toWorld)> casters, Scene preview,
                                       List<Mesh> made, string title = "Bake Shadows")
    {
        int skipped = 0;
        for (int i = 0; i < casters.Count; i++)
        {
            if (i % 16 == 0)
                EditorUtility.DisplayProgressBar(title, "Copying the world's meshes…",
                                                 (float)i / casters.Count * 0.1f);

            var (mesh, toWorld) = casters[i];
            var parts = new List<CombineInstance>();
            for (int s = 0; s < mesh.subMeshCount; s++)
                if (mesh.GetTopology(s) == MeshTopology.Triangles)
                    parts.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = toWorld });
            if (parts.Count == 0) continue;

            var world = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
            try
            {
                world.CombineMeshes(parts.ToArray(), true, true);
            }
            catch (System.Exception)
            {
                Object.DestroyImmediate(world);
                skipped++;
                continue;
            }
            made.Add(world);

            var go = new GameObject("ShadowCaster") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(go, preview);
            go.AddComponent<MeshCollider>().sharedMesh = world;
        }

        if (skipped > 0)
            Debug.LogWarning($"[LevelSelectShadow] {skipped} meshes could not be read and cast no shadow.");
    }

    /// <summary>
    /// The distance from the light to the first thing each texel's ray meets, row by row. Null if
    /// the bake was cancelled.
    /// </summary>
    private static float[] CastRays(PhysicsScene physics, Vector3 from, int size)
    {
        var distances = new float[size * size];
        // Back faces too: a door sheet or an open-ended mesh still blocks the light from behind.
        var query = new QueryParameters(~0, false, QueryTriggerInteraction.Ignore, true);

        int perBatch = BatchRows * size;
        var commands = new NativeArray<RaycastCommand>(perBatch, Allocator.TempJob);
        var hits     = new NativeArray<RaycastHit>(perBatch, Allocator.TempJob);
        try
        {
            for (int row0 = 0; row0 < size; row0 += BatchRows)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Bake Shadows", "Casting rays from the light…",
                                                               0.1f + 0.9f * row0 / size))
                {
                    Debug.Log("[LevelSelectShadow] Bake cancelled — the shadow map was left as it was.");
                    return null;
                }

                for (int y = 0; y < BatchRows; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector2 uv = new Vector2((x + 0.5f) / size, (row0 + y + 0.5f) / size);
                    commands[y * size + x] = new RaycastCommand(physics, from, Unfold(uv), query, Nothing);
                }

                RaycastCommand.ScheduleBatch(commands, hits, 256, 1).Complete();

                for (int i = 0; i < perBatch; i++)
                {
                    // A miss comes back with no distance; a hit at the light itself is not a shadow.
                    float d = hits[i].distance;
                    distances[row0 * size + i] = d > 0f ? d : Nothing;
                }
            }
        }
        finally
        {
            commands.Dispose();
            hits.Dispose();
        }

        return distances;
    }

    /// <summary>
    /// The direction a map position stands for — the inverse of <c>LSShadow_Fold</c> in
    /// LevelSelectShadow.hlsl. The centre is straight down; the upper half of the sphere is folded
    /// out into the corners.
    /// </summary>
    public static Vector3 Unfold(Vector2 uv)
    {
        float a = uv.x * 2f - 1f;
        float b = uv.y * 2f - 1f;
        float c = 1f - Mathf.Abs(a) - Mathf.Abs(b);   // > 0 below the light, < 0 above it
        float t = Mathf.Max(-c, 0f);
        a += a >= 0f ? -t : t;
        b += b >= 0f ? -t : t;
        return new Vector3(a, -c, b).normalized;
    }

    private static Texture2D SaveMap(Scene scene, float[] distances, int size)
    {
        string sceneName = string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name;
        string path      = $"{Folder}/LevelSelectShadow_{sceneName}.asset";
        Directory.CreateDirectory(Folder);

        // Refill the asset already there, so its GUID — and anything pointing at it — survives a re-bake.
        var map = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        bool fresh = map == null || map.width != size || map.height != size || map.format != TextureFormat.RFloat;
        if (fresh)
        {
            if (map != null) AssetDatabase.DeleteAsset(path);
            map = new Texture2D(size, size, TextureFormat.RFloat, false, true) { name = Path.GetFileNameWithoutExtension(path) };
        }

        map.filterMode = FilterMode.Point;
        map.wrapMode   = TextureWrapMode.Clamp;
        map.SetPixelData(distances, 0);
        map.Apply(false, false);

        if (fresh) AssetDatabase.CreateAsset(map, path);
        else       EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssets();
        return map;
    }
}
