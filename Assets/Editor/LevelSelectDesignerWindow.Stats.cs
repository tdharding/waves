using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR

/// <summary>
/// Performance stats under the Stats foldout — how much mesh, material and texture data the open
/// scene carries, split by top-level container (MAINRIVERVISUALS, RIVERPOOLS, LANDSCAPETILES…),
/// plus what the Generated mesh folder weighs on disk.
///
/// Walking the scene is not free, so it is scanned once when the foldout first opens and again
/// only when Rescan is pressed — never every repaint.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private class SceneStatsGroup
    {
        public string name;
        public int    objects, renderers, drawnRenderers;
        public long   triangles, vertices;
        public long   colliderTriangles;
        public readonly HashSet<Mesh> meshes = new HashSet<Mesh>();
        public long   meshBytes;
    }

    private class SceneStats
    {
        public double scannedAt;
        public readonly List<SceneStatsGroup> groups = new List<SceneStatsGroup>();

        public int  objects, renderers, drawnRenderers, shadowCasters;
        public long drawnTriangles, drawnVertices, colliderTriangles;
        public int  uniqueMeshes, meshColliders, colliders;
        public long meshBytes;
        public int  materials, shaders, textures;
        public long textureBytes;
        public int  realtimeLights;

        public int  generatedFiles;
        public long generatedBytes;
        public long sceneFileBytes;

        public readonly List<(Mesh mesh, long bytes, long tris)> heaviestMeshes = new List<(Mesh, long, long)>();
    }

    private SceneStats _sceneStats;
    private bool       _foldSceneStatsGroups = false;
    private bool       _foldSceneStatsMeshes = false;

    private void DrawSceneStats()
    {
        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Scene performance", EditorStyles.boldLabel);
            if (GUILayout.Button("Rescan", GUILayout.Width(64))) _sceneStats = ScanScene();
        }

        if (_sceneStats == null) _sceneStats = ScanScene();
        var s = _sceneStats;

        double age = EditorApplication.timeSinceStartup - s.scannedAt;
        EditorGUILayout.LabelField($"Scanned {age:F0}s ago", EditorStyles.miniLabel);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Drawn (active renderers)", EditorStyles.miniBoldLabel);
        Stat("Renderers",      $"{s.drawnRenderers:N0} of {s.renderers:N0}");
        Stat("Triangles",      s.drawnTriangles.ToString("N0"));
        Stat("Vertices",       s.drawnVertices.ToString("N0"));
        Stat("Shadow casters", s.shadowCasters.ToString("N0"));
        Stat("Realtime lights", s.realtimeLights.ToString("N0"));

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Data in scene", EditorStyles.miniBoldLabel);
        Stat("Unique meshes",  $"{s.uniqueMeshes:N0}  ({EditorUtility.FormatBytes(s.meshBytes)})");
        Stat("Colliders",      $"{s.colliders:N0}  ({s.meshColliders:N0} mesh, {s.colliderTriangles:N0} tris)");
        Stat("Materials",      s.materials.ToString("N0"));
        Stat("Shaders",        s.shaders.ToString("N0"));
        Stat("Textures",       $"{s.textures:N0}  ({EditorUtility.FormatBytes(s.textureBytes)})");
        Stat("GameObjects",    s.objects.ToString("N0"));

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("On disk", EditorStyles.miniBoldLabel);
        Stat("Generated folder", $"{s.generatedFiles:N0} meshes  ({EditorUtility.FormatBytes(s.generatedBytes)})");
        Stat("Scene file",       EditorUtility.FormatBytes(s.sceneFileBytes));

        EditorGUILayout.Space(2);
        _foldSceneStatsGroups = EditorGUILayout.Foldout(_foldSceneStatsGroups, "By container", true);
        if (_foldSceneStatsGroups)
        {
            EditorGUI.indentLevel++;
            foreach (var g in s.groups)
            {
                EditorGUILayout.LabelField(g.name, EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                Stat("Tris / verts", $"{g.triangles:N0} / {g.vertices:N0}");
                Stat("Renderers",    $"{g.drawnRenderers:N0} of {g.renderers:N0}");
                Stat("Meshes",       $"{g.meshes.Count:N0}  ({EditorUtility.FormatBytes(g.meshBytes)})");
                if (g.colliderTriangles > 0) Stat("Collider tris", g.colliderTriangles.ToString("N0"));
                Stat("Objects",      g.objects.ToString("N0"));
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }

        _foldSceneStatsMeshes = EditorGUILayout.Foldout(_foldSceneStatsMeshes, "Heaviest meshes", true);
        if (_foldSceneStatsMeshes)
        {
            EditorGUI.indentLevel++;
            foreach (var (mesh, bytes, tris) in s.heaviestMeshes)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{EditorUtility.FormatBytes(bytes)} · {tris:N0} tris",
                        EditorStyles.miniLabel, GUILayout.Width(150));
                    if (GUILayout.Button(mesh.name, EditorStyles.miniButton))
                        EditorGUIUtility.PingObject(mesh);
                }
            }
            EditorGUI.indentLevel--;
        }
    }

    private static void Stat(string label, string value)
        => EditorGUILayout.LabelField(label, value, EditorStyles.miniLabel);

    private SceneStats ScanScene()
    {
        var s          = new SceneStats { scannedAt = EditorApplication.timeSinceStartup };
        var allMeshes  = new Dictionary<Mesh, long>();
        var materials  = new HashSet<Material>();
        var shaders    = new HashSet<Shader>();
        var textures   = new HashSet<Texture>();

        for (int si = 0; si < SceneManager.sceneCount; si++)
        {
            var scene = SceneManager.GetSceneAt(si);
            if (!scene.isLoaded) continue;

            if (!string.IsNullOrEmpty(scene.path) && File.Exists(scene.path))
                s.sceneFileBytes += new FileInfo(scene.path).Length;

            foreach (var root in scene.GetRootGameObjects())
            {
                var g = new SceneStatsGroup { name = root.name };
                g.objects = root.GetComponentsInChildren<Transform>(true).Length;

                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    g.renderers++;
                    Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh
                              : r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
                    if (mesh != null) g.meshes.Add(mesh);

                    foreach (var m in r.sharedMaterials)
                        if (m != null) materials.Add(m);

                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    g.drawnRenderers++;
                    if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) s.shadowCasters++;
                    if (mesh != null)
                    {
                        g.triangles += TriangleCount(mesh);
                        g.vertices  += mesh.vertexCount;
                    }
                }

                foreach (var c in root.GetComponentsInChildren<Collider>(true))
                {
                    s.colliders++;
                    if (c is MeshCollider mc && mc.sharedMesh != null)
                    {
                        s.meshColliders++;
                        g.meshes.Add(mc.sharedMesh);
                        if (mc.enabled && mc.gameObject.activeInHierarchy)
                            g.colliderTriangles += TriangleCount(mc.sharedMesh);
                    }
                }

                foreach (var l in root.GetComponentsInChildren<Light>(false))
                    if (l.enabled && l.lightmapBakeType != LightmapBakeType.Baked) s.realtimeLights++;

                foreach (var mesh in g.meshes)
                {
                    if (!allMeshes.TryGetValue(mesh, out long bytes))
                        allMeshes[mesh] = bytes = Profiler.GetRuntimeMemorySizeLong(mesh);
                    g.meshBytes += bytes;
                }

                s.objects           += g.objects;
                s.renderers         += g.renderers;
                s.drawnRenderers    += g.drawnRenderers;
                s.drawnTriangles    += g.triangles;
                s.drawnVertices     += g.vertices;
                s.colliderTriangles += g.colliderTriangles;
                s.groups.Add(g);
            }
        }

        // Heaviest containers first — where the budget is going is the question being asked.
        s.groups.Sort((a, b) => b.triangles.CompareTo(a.triangles));

        s.uniqueMeshes = allMeshes.Count;
        s.meshBytes    = allMeshes.Values.Sum();
        foreach (var kv in allMeshes.OrderByDescending(kv => kv.Value).Take(10))
            s.heaviestMeshes.Add((kv.Key, kv.Value, TriangleCount(kv.Key)));

        foreach (var m in materials)
        {
            if (m.shader != null) shaders.Add(m.shader);
            foreach (int id in m.GetTexturePropertyNameIDs())
            {
                var t = m.GetTexture(id);
                if (t != null) textures.Add(t);
            }
        }
        s.materials    = materials.Count;
        s.shaders      = shaders.Count;
        s.textures     = textures.Count;
        s.textureBytes = textures.Sum(t => Profiler.GetRuntimeMemorySizeLong(t));

        if (Directory.Exists(GeneratedDir))
        {
            foreach (var file in Directory.GetFiles(GeneratedDir, "*.asset"))
            {
                s.generatedFiles++;
                s.generatedBytes += new FileInfo(file).Length;
            }
        }

        return s;
    }

    private static long TriangleCount(Mesh mesh)
    {
        long tris = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
            if (mesh.GetTopology(i) == MeshTopology.Triangles)
                tris += mesh.GetIndexCount(i) / 3;
        return tris;
    }
}

#endif
