using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Bakes the river's glow: how much light the river water throws onto everything around it —
/// the channel walls, the rims, the natural runs' banks, the hills and the air the fog sits in —
/// with the walls blocking it. The water never moves once the runs are built, so this is done
/// once, on the Bake River Glow button in the Lighting window, not every frame.
///
/// The water surfaces are cut into small patches, each a little light. The air within Glow Reach
/// of the water is filled with sample points; from every point a ray goes to each patch within
/// reach, and a patch counts only if the ray gets there without meeting anything — so a wall
/// between the two leaves that patch out, and the glow falls into shadow behind it. A point keeps
/// how much glow reaches it and which way it mostly comes from, so a wall facing the river lights
/// up and one facing away does not.
///
/// The detail near the water never depends on the reach: within <see cref="NearBand"/> of it — the
/// channel, its rims and the inner banks — the points stay <see cref="NearSpacing"/> apart and the
/// patches <see cref="NearPatchSize"/> across. A longer reach adds a second, coarser set of points
/// (a reach / 8 apart) that carries the glow the rest of the way, the last metre of the near
/// points blending into it. Far-off water is taken a bigger patch at a time — the farther, the
/// bigger — so a long reach does not multiply the rays.
///
/// Points inside walls or under the ground cannot see the water and would read black; they are
/// found with a ray straight up (it meets the inside of whatever they are in) and filled from
/// their neighbours in the open, so the blend between points never darkens a surface.
///
/// The points are kept only near water, in bricks of 8×8×8, so the miles of empty air between
/// the rivers cost nothing. Three 3D textures are saved: every brick of both sets packed together
/// (the atlas), and for each set a coarse grid over the world saying where in the atlas each of
/// its bricks is. <c>RiverGlow.hlsl</c> reads them the same way — they must agree.
///
/// What blocks: the same drawn meshes that cast the level select's shadows
/// (<see cref="LevelSelectShadowBaker.CasterParents"/>). What glows: the meshes under those same
/// containers on the Water layer — the river water and the pool water.
/// </summary>
public static class RiverGlowBaker
{
    /// <summary>Sample points along each side of a brick.</summary>
    public const int Brick = 8;

    // Steps between sample points a brick spans. Neighbouring bricks share their faces' points,
    // so the glow is blended across a brick's edge with no seam.
    internal const int Span = Brick - 1;

    internal const string Folder = "Assets/Prefab/LevelSelectPrefabs/Generated";

    // The detail near the water, in metres, whatever the reach. A short reach gets finer still
    // (a reach / 8 apart, patches a reach / 4 across), down to the finest below.
    private const float NearSpacing   = 0.375f;
    private const float NearPatchSize = 0.75f;
    private const float FinestSpacing = 0.1f;
    private const float FinestPatch   = 0.2f;

    // How far from the water the near points go when the reach is longer, and how much of that
    // blends into the far points. A reach no longer than NearBand × 1.5 is all near points.
    private const float NearBand  = 2.5f;
    private const float BlendBand = 1f;

    // A group of water patches is taken as one only when the point is at least this many of its
    // widths away from it; nearer, its smaller patches are taken instead.
    private const float OpenAt = 2f;

    // How far up the inside test looks, and how many rings of neighbours fill a point found inside.
    private const float InsideTestLength = 200f;
    private const int   FillPasses       = 2;

    // Past this the bake stops before it starts: the atlas would pass 256 MB. Room for twice the
    // Resolution, which is eight times the points.
    internal const int MostPoints = 32 * 1024 * 1024;

    // Rays per batch — enough to keep the job system busy without holding millions of hits at once.
    private const int RaysPerBatch = 1 << 18;

    internal struct Patch
    {
        public Vector3 position;
        public Vector3 normal;
        public float   area;

        // How far along its soul route a patch of soul path glow lies, and that squared — for a
        // group, the area-weighted means of both, so the spread along the route survives grouping.
        // Unused (0) for the river's water.
        public float arc;
        public float arcSq;

        // How much farther this patch's light carries than the reach: its whole fall-off is
        // stretched by this, so at this many times the distance it lights as a plain patch would.
        // A soul path band crossing a pool takes the pool's width over the river's, so the pool's
        // walls, that much farther off, are lit as the river's are. 0 (the river's water) is 1.
        // For a group, the area-weighted mean, and the least and most of what it holds.
        public float stretch;
        public float stretchMin;
        public float stretchMax;

        public float Stretch    => stretch    > 0f ? stretch    : 1f;
        public float StretchMin => stretchMin > 0f ? stretchMin : Stretch;
        public float StretchMax => stretchMax > 0f ? stretchMax : Stretch;
    }

    /// <summary>
    /// The water's patches, smallest first, each size grouping the one below it into patches
    /// twice as wide, up to about half the reach.
    /// </summary>
    internal sealed class PatchLevels
    {
        public readonly List<Patch[]>     levels   = new List<Patch[]>();
        public readonly List<float>       sizes    = new List<float>();
        public readonly List<List<int>[]> children = new List<List<int>[]>();   // [0] is null

        // The biggest patches, by cells this wide.
        public Dictionary<long, List<int>> top;
        public float topCell;

        public int Top => levels.Count - 1;
    }

    /// <summary>One set of sample points: near (fine) or far (coarse).</summary>
    internal sealed class Level
    {
        public float      spacing;
        public Vector3    origin;
        public Vector3Int grid;
        public List<Vector3Int> bricks;

        public readonly Dictionary<long, int> index  = new Dictionary<long, int>();
        public readonly List<Vector3Int>      points = new List<Vector3Int>();
        public Vector4[] glow;

        public Vector3 At(int i) => origin + (Vector3)points[i] * spacing;

        /// <summary>The glow at a place, blended between the eight points around it.</summary>
        public Vector4 Sample(Vector3 p)
        {
            Vector3 g = (p - origin) / spacing;
            int x0 = Mathf.FloorToInt(g.x), y0 = Mathf.FloorToInt(g.y), z0 = Mathf.FloorToInt(g.z);
            float fx = g.x - x0, fy = g.y - y0, fz = g.z - z0;

            Vector4 sum = Vector4.zero;
            for (int c = 0; c < 8; c++)
            {
                int x = x0 + (c & 1), y = y0 + ((c >> 1) & 1), z = z0 + ((c >> 2) & 1);
                if (x < 0 || y < 0 || z < 0) continue;
                if (!index.TryGetValue(Key(x, y, z), out int i)) continue;
                float w = ((c & 1) != 0 ? fx : 1f - fx) * (((c >> 1) & 1) != 0 ? fy : 1f - fy) *
                          (((c >> 2) & 1) != 0 ? fz : 1f - fz);
                sum += glow[i] * w;
            }
            return sum;
        }
    }

    /// <summary>Rays sent in batches; each hit (or miss) handed back with the tag it was sent with.</summary>
    private sealed class RayBatch : System.IDisposable
    {
        private NativeArray<RaycastCommand> _commands = new NativeArray<RaycastCommand>(RaysPerBatch, Allocator.Persistent);
        private NativeArray<RaycastHit>     _hits     = new NativeArray<RaycastHit>(RaysPerBatch, Allocator.Persistent);
        private readonly int[]     _tags   = new int[RaysPerBatch];
        private readonly Vector4[] _carry  = new Vector4[RaysPerBatch];
        private readonly Vector4[] _carry2 = new Vector4[RaysPerBatch];
        private int _count;

        public System.Action<int, Vector4, Vector4, RaycastHit> onResult;

        public void Add(RaycastCommand command, int tag, Vector4 carry) => Add(command, tag, carry, Vector4.zero);

        public void Add(RaycastCommand command, int tag, Vector4 carry, Vector4 carry2)
        {
            _commands[_count] = command;
            _tags[_count]     = tag;
            _carry[_count]    = carry;
            _carry2[_count]   = carry2;
            if (++_count == RaysPerBatch) Flush();
        }

        public void Flush()
        {
            if (_count == 0) return;
            RaycastCommand.ScheduleBatch(_commands.GetSubArray(0, _count), _hits.GetSubArray(0, _count), 256, 1)
                          .Complete();
            for (int r = 0; r < _count; r++) onResult(_tags[r], _carry[r], _carry2[r], _hits[r]);
            _count = 0;
        }

        public void Dispose()
        {
            if (_commands.IsCreated) _commands.Dispose();
            if (_hits.IsCreated)     _hits.Dispose();
        }
    }

    public static void Bake(LevelSelectLight light)
    {
        if (light == null) return;
        var scene = light.gameObject.scene;
        float reach = Mathf.Max(0.01f, light.glowReach);

        var water = FindWater(scene, out string waterSummary);
        if (water.Count == 0)
        {
            Debug.LogWarning("[RiverGlow] No river water found — nothing under the world's containers " +
                             "is on the Water layer. No glow baked.");
            return;
        }

        // Resolution packs the points closer; the water's patches stay as they are.
        float resolution  = Mathf.Max(1f, light.riverGlowResolution);
        float nearSpacing = Mathf.Clamp(reach / 8f, FinestSpacing, NearSpacing) / resolution;
        float patchSize   = Mathf.Clamp(reach / 4f, FinestPatch, NearPatchSize);
        bool  hasFar      = reach > NearBand * 1.5f;
        float nearBand    = hasFar ? NearBand : reach;

        var patches = BuildPatches(water, patchSize, reach, out Bounds waterBounds);
        if (patches.levels[0].Length == 0)
        {
            Debug.LogWarning("[RiverGlow] The river water has no surface to glow. No glow baked.");
            return;
        }

        // The near points reach a patch-width past the band, so the band is whole even where a
        // patch's middle sits back from the water's edge.
        var near = MakeLevel(patches.levels[0], waterBounds, nearSpacing, nearBand + patchSize);
        var far  = hasFar ? MakeLevel(patches.levels[0], waterBounds, reach / 8f / resolution, reach) : null;

        long points = ((long)near.bricks.Count + (far?.bricks.Count ?? 0)) * Brick * Brick * Brick;
        if (points > MostPoints)
        {
            Debug.LogError($"[RiverGlow] A Glow Reach of {reach:0.##} m at a Resolution of " +
                           $"{resolution:0.##} needs {near.bricks.Count + (far?.bricks.Count ?? 0)} bricks " +
                           $"({points / 1000000f:0.#} million points) — too many to save. Lower the " +
                           "Glow Reach or the Resolution. No glow baked.");
            return;
        }

        var casters = LevelSelectShadowBaker.FindCasters(scene, out string casterSummary);
        var timer   = System.Diagnostics.Stopwatch.StartNew();
        long rays   = 0;

        var preview = EditorSceneManager.NewPreviewScene();
        var meshes  = new List<Mesh>();
        try
        {
            var physics = preview.GetPhysicsScene();
            if (!physics.IsValid() || physics == Physics.defaultPhysicsScene)
            {
                Debug.LogError("[RiverGlow] The preview scene has no physics of its own, so the rays " +
                               "would hit the open scene's colliders. No glow baked.");
                return;
            }

            LevelSelectShadowBaker.BuildColliders(casters, preview, meshes, "Bake River Glow");

            float soften = patchSize * patchSize;   // keeps a patch right beside a point from blowing up
            if (far != null)
            {
                BuildPoints(far);
                var all = new bool[far.points.Count];
                System.Array.Fill(all, true);
                if (!Gather(physics, far, all, patches, reach, soften, "far", ref rays, out var inside)) return;
                FillInside(far, inside);
            }

            BuildPoints(near);
            var compute  = new bool[near.points.Count];
            var distance = NearestWater(near, patches, nearBand, hasFar, compute);
            if (!Gather(physics, near, compute, patches, reach, soften, "near", ref rays, out var nearInside)) return;

            if (far != null)
            {
                // Past the band the far points carry the glow; the near points there just copy it.
                for (int i = 0; i < near.points.Count; i++)
                    if (!compute[i]) near.glow[i] = far.Sample(near.At(i));
            }
            FillInside(near, nearInside);
            if (far != null)
            {
                // The band's last metre blends into the far points, so there is no step where they meet.
                for (int i = 0; i < near.points.Count; i++)
                {
                    float t = Mathf.InverseLerp(nearBand - BlendBand, nearBand, distance[i]);
                    if (compute[i] && t > 0f)
                        near.glow[i] = Vector4.Lerp(near.glow[i], far.Sample(near.At(i)), t);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            EditorSceneManager.ClosePreviewScene(preview);
            foreach (var m in meshes) Object.DestroyImmediate(m);
        }

        SaveTextures(scene, near, far, out var atlas, out var nearMap, out var farMap);

        Undo.RecordObject(light, "Bake River Glow");
        light.riverGlowAtlas      = atlas;
        light.riverGlowBricks     = nearMap;
        light.riverGlowOrigin     = new Vector4(near.origin.x, near.origin.y, near.origin.z, near.spacing);
        light.riverGlowFarBricks  = farMap;
        light.riverGlowFarOrigin  = far != null
            ? new Vector4(far.origin.x, far.origin.y, far.origin.z, far.spacing)
            : Vector4.zero;
        light.riverGlowBakedReach      = light.glowReach;
        light.riverGlowBakedResolution = light.riverGlowResolution;
        EditorUtility.SetDirty(light);
        EditorSceneManager.MarkSceneDirty(scene);

        LevelSelectLight.Push();
        SceneView.RepaintAll();

        string sizes = string.Join(", ", patches.sizes.ConvertAll(s => s.ToString("0.##")));
        Debug.Log($"[RiverGlow] Baked with a reach of {reach:0.##} m in {timer.Elapsed.TotalSeconds:0.#} s — " +
                  $"{water.Count} water surfaces ({waterSummary}) cut into {patches.levels[0].Length} patches, " +
                  $"grouped at {sizes} m; near points {near.spacing:0.###} m apart within {nearBand:0.##} m " +
                  $"of the water ({near.bricks.Count} bricks, {near.points.Count} points)" +
                  (far != null
                      ? $"; far points {far.spacing:0.###} m apart ({far.bricks.Count} bricks, {far.points.Count} points)"
                      : "; no far points") +
                  $"; {rays} rays; {casters.Count} blockers ({casterSummary}). " +
                  $"Atlas {atlas.width}×{atlas.height}×{atlas.depth}. Saved to {AssetDatabase.GetAssetPath(atlas)}.",
                  atlas);
    }

    internal static List<(Mesh mesh, Matrix4x4 toWorld)> FindWater(Scene scene, out string summary)
    {
        var found  = new List<(Mesh, Matrix4x4)>();
        var counts = new List<string>();
        int layer  = LayerMask.NameToLayer("Water");
        if (layer < 0)
        {
            summary = "";
            return found;
        }

        foreach (var root in scene.GetRootGameObjects())
        {
            if (System.Array.IndexOf(LevelSelectShadowBaker.CasterParents, root.name) < 0) continue;

            int n = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled || r.gameObject.layer != layer) continue;
                if (!r.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) continue;

                found.Add((mf.sharedMesh, r.localToWorldMatrix));
                n++;
            }
            if (n > 0) counts.Add($"{root.name} {n}");
        }

        summary = string.Join(", ", counts);
        return found;
    }

    /// <summary>
    /// The water cut into patches <paramref name="size"/> across, then grouped into patches twice
    /// as wide, again and again, until the biggest are about half the reach.
    /// </summary>
    private static PatchLevels BuildPatches(List<(Mesh mesh, Matrix4x4 toWorld)> water, float size,
                                            float reach, out Bounds bounds) =>
        GroupPatches(CutIntoPatches(water, size, out bounds).ToArray(), size, reach);

    /// <summary>
    /// Patches <paramref name="size"/> across grouped into patches twice as wide, again and again,
    /// until the biggest are about half the reach.
    /// </summary>
    internal static PatchLevels GroupPatches(Patch[] first, float size, float reach)
    {
        var levels = new PatchLevels();
        levels.levels.Add(first);
        levels.sizes.Add(size);
        levels.children.Add(null);

        while (size * OpenAt < reach && levels.levels[levels.Top].Length > 1)
        {
            size *= 2f;
            var below  = levels.levels[levels.Top];
            var groups = new Dictionary<long, int>();
            var sums   = new List<(Vector3 weighted, Vector3 normal, float area, float arc, float arcSq,
                                   float stretch, float stretchMin, float stretchMax)>();
            var kids   = new List<List<int>>();
            for (int i = 0; i < below.Length; i++)
            {
                var b = below[i];
                long key = CellKey(b.position, size);
                if (!groups.TryGetValue(key, out int g))
                {
                    g = sums.Count;
                    groups[key] = g;
                    sums.Add((Vector3.zero, Vector3.zero, 0f, 0f, 0f, 0f, float.MaxValue, 0f));
                    kids.Add(new List<int>());
                }
                var s = sums[g];
                sums[g] = (s.weighted + b.position * b.area, s.normal + b.normal * b.area, s.area + b.area,
                           s.arc + b.arc * b.area, s.arcSq + b.arcSq * b.area, s.stretch + b.Stretch * b.area,
                           Mathf.Min(s.stretchMin, b.StretchMin), Mathf.Max(s.stretchMax, b.StretchMax));
                kids[g].Add(i);
            }

            var level = new Patch[sums.Count];
            for (int g = 0; g < level.Length; g++)
            {
                var s = sums[g];
                level[g] = new Patch
                {
                    position   = s.weighted / s.area,
                    normal     = s.normal.sqrMagnitude > 1e-12f ? s.normal.normalized : Vector3.up,
                    area       = s.area,
                    arc        = s.arc / s.area,
                    arcSq      = s.arcSq / s.area,
                    stretch    = s.stretch / s.area,
                    stretchMin = s.stretchMin,
                    stretchMax = s.stretchMax,
                };
            }
            levels.levels.Add(level);
            levels.sizes.Add(size);
            levels.children.Add(kids.ToArray());
        }

        // The biggest patches by cells a reach and a patch wide: the 27 cells around a point hold
        // every one with anything inside the reach.
        levels.topCell = reach + levels.sizes[levels.Top];
        levels.top     = new Dictionary<long, List<int>>();
        var top = levels.levels[levels.Top];
        for (int i = 0; i < top.Length; i++)
        {
            long key = CellKey(top[i].position, levels.topCell);
            if (!levels.top.TryGetValue(key, out var list)) levels.top[key] = list = new List<int>();
            list.Add(i);
        }
        return levels;
    }

    /// <summary>
    /// The water's surface gathered into patches about <paramref name="size"/> across: every
    /// triangle (split until it is no bigger than a patch) goes into the patch its middle lies in,
    /// and a patch stands at the middle of all it holds, facing their average way.
    /// </summary>
    private static List<Patch> CutIntoPatches(List<(Mesh mesh, Matrix4x4 toWorld)> water, float size,
                                              out Bounds bounds)
    {
        var sums  = new Dictionary<long, (Vector3 weighted, Vector3 normal, float area)>();
        bool any  = false;
        bounds    = default;
        float most = size * size * 0.5f;

        var stack = new Stack<(Vector3 a, Vector3 b, Vector3 c)>();
        foreach (var (mesh, toWorld) in water)
        {
            Vector3[] verts;
            try { verts = mesh.vertices; }
            catch (System.Exception) { continue; }

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var tris = mesh.GetTriangles(s);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    stack.Push((toWorld.MultiplyPoint3x4(verts[tris[t]]),
                                toWorld.MultiplyPoint3x4(verts[tris[t + 1]]),
                                toWorld.MultiplyPoint3x4(verts[tris[t + 2]])));

                    while (stack.Count > 0)
                    {
                        var (a, b, c) = stack.Pop();
                        Vector3 cross = Vector3.Cross(b - a, c - a);
                        float area = cross.magnitude * 0.5f;
                        if (area < 1e-8f) continue;

                        if (area > most)
                        {
                            // Split across its longest side.
                            float ab = (b - a).sqrMagnitude, bc = (c - b).sqrMagnitude, ca = (a - c).sqrMagnitude;
                            if (ab >= bc && ab >= ca) { var m = (a + b) * 0.5f; stack.Push((a, m, c)); stack.Push((m, b, c)); }
                            else if (bc >= ca)        { var m = (b + c) * 0.5f; stack.Push((a, b, m)); stack.Push((a, m, c)); }
                            else                      { var m = (c + a) * 0.5f; stack.Push((a, b, m)); stack.Push((m, b, c)); }
                            continue;
                        }

                        Vector3 mid = (a + b + c) / 3f;
                        long key = CellKey(mid, size);
                        sums.TryGetValue(key, out var sum);
                        sums[key] = (sum.weighted + mid * area, sum.normal + cross.normalized * area,
                                     sum.area + area);

                        if (!any) { bounds = new Bounds(a, Vector3.zero); any = true; }
                        bounds.Encapsulate(a);
                        bounds.Encapsulate(b);
                        bounds.Encapsulate(c);
                    }
                }
            }
        }

        var patches = new List<Patch>(sums.Count);
        foreach (var sum in sums.Values)
        {
            Vector3 n = sum.normal.sqrMagnitude > 1e-12f ? sum.normal.normalized : Vector3.up;
            patches.Add(new Patch { position = sum.weighted / sum.area, normal = n, area = sum.area });
        }
        return patches;
    }

    /// <summary>
    /// A set of points <paramref name="spacing"/> apart over the water's bounds, with the bricks
    /// that lie within <paramref name="coverage"/> of any patch — and, for a patch whose light is
    /// stretched, (stretch − 1) × <paramref name="stretchReach"/> farther.
    /// </summary>
    internal static Level MakeLevel(Patch[] patches, Bounds waterBounds, float spacing, float coverage,
                                    float stretchReach = 0f)
    {
        float most = coverage;
        foreach (var p in patches) most = Mathf.Max(most, Coverage(p, coverage, stretchReach));

        var level = new Level { spacing = spacing };
        level.origin = waterBounds.min - Vector3.one * (most + spacing);
        Vector3 extent    = waterBounds.size + Vector3.one * 2f * (most + spacing);
        float   brickSpan = Span * spacing;
        level.grid = new Vector3Int(Mathf.CeilToInt(extent.x / brickSpan) + 1,
                                    Mathf.CeilToInt(extent.y / brickSpan) + 1,
                                    Mathf.CeilToInt(extent.z / brickSpan) + 1);

        var seen = new HashSet<long>();
        level.bricks = new List<Vector3Int>();
        var grid = level.grid;
        foreach (var p in patches)
        {
            float   cover = Coverage(p, coverage, stretchReach);
            Vector3 lo = (p.position - Vector3.one * cover - level.origin) / brickSpan;
            Vector3 hi = (p.position + Vector3.one * cover - level.origin) / brickSpan;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(lo.x)), x1 = Mathf.Min(grid.x - 1, Mathf.FloorToInt(hi.x));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(lo.y)), y1 = Mathf.Min(grid.y - 1, Mathf.FloorToInt(hi.y));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(lo.z)), z1 = Mathf.Min(grid.z - 1, Mathf.FloorToInt(hi.z));
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (seen.Add(Key(x, y, z))) level.bricks.Add(new Vector3Int(x, y, z));
        }
        return level;
    }

    /// <summary>How far round a patch its points are kept: farther for a patch whose light is stretched.</summary>
    internal static float Coverage(Patch p, float coverage, float stretchReach)
        => coverage + (p.StretchMax - 1f) * stretchReach;

    /// <summary>Every point of every brick once — neighbouring bricks share the points on their faces.</summary>
    internal static void BuildPoints(Level level)
    {
        foreach (var b in level.bricks)
            for (int z = 0; z < Brick; z++)
            for (int y = 0; y < Brick; y++)
            for (int x = 0; x < Brick; x++)
            {
                var l = new Vector3Int(b.x * Span + x, b.y * Span + y, b.z * Span + z);
                long key = Key(l.x, l.y, l.z);
                if (level.index.ContainsKey(key)) continue;
                level.index[key] = level.points.Count;
                level.points.Add(l);
            }
        level.glow = new Vector4[level.points.Count];
    }

    /// <summary>
    /// How far each near point is from the nearest water patch, and which of them are worked out
    /// from the water (<paramref name="compute"/>) — those within the band. With no far points,
    /// all of them.
    /// </summary>
    private static float[] NearestWater(Level near, PatchLevels patches, float band, bool hasFar, bool[] compute)
    {
        var distance = new float[near.points.Count];
        if (!hasFar)
        {
            System.Array.Fill(compute, true);
            return distance;
        }

        var level0 = patches.levels[0];
        var cells  = new Dictionary<long, List<int>>();
        for (int i = 0; i < level0.Length; i++)
        {
            long key = CellKey(level0[i].position, band);
            if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
            list.Add(i);
        }

        for (int i = 0; i < near.points.Count; i++)
        {
            Vector3 at = near.At(i);
            int cx = Mathf.FloorToInt(at.x / band), cy = Mathf.FloorToInt(at.y / band), cz = Mathf.FloorToInt(at.z / band);
            float best = float.PositiveInfinity;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!cells.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out var list)) continue;
                foreach (int p in list) best = Mathf.Min(best, (level0[p].position - at).sqrMagnitude);
            }
            distance[i] = Mathf.Sqrt(best);
            compute[i]  = distance[i] < band;
        }
        return distance;
    }

    // What the progress bar and the log call a bake — the river's here; the soul path glow passes its own.
    internal const string RiverTitle = "Bake River Glow";
    internal const string RiverTag   = "RiverGlow";

    /// <summary>
    /// The glow at the points marked in <paramref name="compute"/>: first which of them are inside
    /// something, then, for the rest, a ray to each water patch in reach. xyz the way the glow
    /// mostly comes from (weighted by how much comes), w how much comes in all. False if cancelled.
    /// </summary>
    private static bool Gather(PhysicsScene physics, Level level, bool[] compute, PatchLevels patches,
                               float reach, float soften, string label, ref long rays, out bool[] inside)
    {
        if (!FindInside(physics, level, compute, label, RiverTitle, RiverTag, ref rays, out inside)) return false;
        return CastToPatches(physics, level, compute, inside, patches, reach, soften, label, RiverTitle, RiverTag,
                             ref rays, level.glow, null);
    }

    /// <summary>
    /// Which of the points marked in <paramref name="compute"/> are inside something: a ray
    /// straight up that meets a back face before any front face is inside whatever that face
    /// belongs to — a wall, a rim, the ground. False if cancelled.
    /// </summary>
    internal static bool FindInside(PhysicsScene physics, Level level, bool[] compute, string label,
                                    string title, string tag, ref long rays, out bool[] inside)
    {
        int count = level.points.Count;
        inside = new bool[count];

        var both  = new QueryParameters(~0, false, QueryTriggerInteraction.Ignore, true);
        var front = new QueryParameters(~0, false, QueryTriggerInteraction.Ignore, false);
        var bothDistance  = new float[count];
        var frontDistance = new float[count];
        System.Array.Fill(bothDistance, float.PositiveInfinity);
        System.Array.Fill(frontDistance, float.PositiveInfinity);

        using (var batch = new RayBatch())
        {
            batch.onResult = (point, carry, carry2, hit) =>
            {
                if (hit.normal.sqrMagnitude == 0f) return;   // met nothing
                if (carry.x == 0f) bothDistance[point]  = hit.distance;
                else               frontDistance[point] = hit.distance;
            };
            for (int i = 0; i < count; i++)
            {
                if (!compute[i]) continue;
                if ((i & 16383) == 0 && Cancelled(title, tag, $"Finding the {label} points inside walls and ground…", 0.1f + 0.1f * i / count))
                    return false;
                Vector3 at = level.At(i);
                batch.Add(new RaycastCommand(physics, at, Vector3.up, both, InsideTestLength), i, Vector4.zero);
                batch.Add(new RaycastCommand(physics, at, Vector3.up, front, InsideTestLength), i, Vector4.one);
                rays += 2;
            }
            batch.Flush();
        }
        for (int i = 0; i < count; i++)
            inside[i] = compute[i] && bothDistance[i] < frontDistance[i] - 1e-4f;
        return true;
    }

    /// <summary>
    /// A ray from each point in the open (marked in <paramref name="compute"/>, not
    /// <paramref name="inside"/>) to each patch in reach, adding into <paramref name="glow"/> what
    /// it sees: xyz the way the glow mostly comes from (weighted by how much comes), w how much.
    /// When <paramref name="arcs"/> is given, the same amounts weight the patches' arc (x) and arc
    /// squared (y) into it too. False if cancelled.
    /// </summary>
    internal static bool CastToPatches(PhysicsScene physics, Level level, bool[] compute, bool[] inside,
                                       PatchLevels patches, float reach, float soften, string label,
                                       string title, string tag, ref long rays, Vector4[] glow, Vector4[] arcs)
    {
        int count = level.points.Count;
        var both  = new QueryParameters(~0, false, QueryTriggerInteraction.Ignore, true);
        var stack = new Stack<(int level, int patch)>();
        float reach2 = reach * reach;
        int   top    = patches.Top;
        string what  = arcs != null ? "bands" : "water";
        using (var batch = new RayBatch())
        {
            batch.onResult = (point, carry, carry2, hit) =>
            {
                if (hit.normal.sqrMagnitude != 0f) return;   // met something: the patch is hidden
                glow[point] += carry;
                if (arcs != null) arcs[point] += carry2;
            };

            for (int i = 0; i < count; i++)
            {
                if (!compute[i] || inside[i]) continue;
                if ((i & 4095) == 0 && Cancelled(title, tag, $"Casting rays from the {label} points to the {what}…", 0.2f + 0.8f * i / count))
                    return false;

                Vector3 at = level.At(i);
                int cx = Mathf.FloorToInt(at.x / patches.topCell);
                int cy = Mathf.FloorToInt(at.y / patches.topCell);
                int cz = Mathf.FloorToInt(at.z / patches.topCell);
                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (patches.top.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out var list))
                        foreach (int p in list) stack.Push((top, p));

                while (stack.Count > 0)
                {
                    var (k, p) = stack.Pop();
                    var   patch = patches.levels[k][p];
                    float size  = patches.sizes[k];
                    Vector3 v  = patch.position - at;
                    float   d2 = v.sqrMagnitude;
                    float   d  = Mathf.Sqrt(d2);

                    // Too close to take as one, or straddling the reach (any of the reaches its
                    // smaller patches have, when their light is stretched): take its smaller patches.
                    if (k > 0 && (d < OpenAt * size ||
                                  (d >= reach * patch.StretchMin && d < reach * patch.StretchMax + size)))
                    {
                        foreach (int c in patches.children[k][p]) stack.Push((k - 1, c));
                        continue;
                    }

                    // A stretched patch's light carries that much farther: its distance is
                    // measured in stretched metres, and it fades out at its own reach.
                    float stretch = patch.Stretch;
                    if (d >= reach * stretch || d2 < 1e-8f) continue;
                    float sd2 = d2 / (stretch * stretch);

                    Vector3 dir = v / d;

                    // How much of the patch faces the point (either side — the water glows from
                    // both, so the waterline is not dark), as a share of everything the point can
                    // see, faded to nothing at the reach.
                    float facing = Mathf.Abs(Vector3.Dot(patch.normal, dir));
                    float fade   = 1f - sd2 / reach2;
                    float amount = patch.area * facing / (Mathf.PI * (sd2 + soften)) * fade * fade;
                    if (amount <= 0f) continue;

                    // Stop just short of the water, so the ray does not meet what it is aimed at.
                    batch.Add(new RaycastCommand(physics, at, dir, both, d - Mathf.Min(0.02f, d * 0.5f)), i,
                              new Vector4(dir.x * amount, dir.y * amount, dir.z * amount, amount),
                              new Vector4(patch.arc * amount, patch.arcSq * amount, 0f, 0f));
                    rays++;
                }
            }
            batch.Flush();
        }
        return true;
    }

    /// <summary>
    /// Points inside walls or the ground take the average of their neighbours in the open, ring by
    /// ring, so a surface blended from the points either side of it is never darkened. When
    /// <paramref name="tags"/> is given, a filled point takes the tag of its strongest neighbour —
    /// for what cannot be averaged, like which soul route lights a point.
    /// </summary>
    internal static void FillInside(Level level, bool[] inside, Vector4[] tags = null)
    {
        var fills = new List<(int point, Vector4 glow, Vector4 tag)>();
        for (int pass = 0; pass < FillPasses; pass++)
        {
            fills.Clear();
            for (int i = 0; i < inside.Length; i++)
            {
                if (!inside[i]) continue;
                var l = level.points[i];
                Vector4 sum = Vector4.zero, strongest = Vector4.zero;
                float most = -1f;
                int n = 0;
                for (int side = 0; side < 6; side++)
                {
                    int axis = side >> 1, step = (side & 1) == 0 ? -1 : 1;
                    var o = l;
                    o[axis] += step;
                    if (o[axis] < 0 || !level.index.TryGetValue(Key(o.x, o.y, o.z), out int j) || inside[j]) continue;
                    sum += level.glow[j];
                    n++;
                    if (tags != null && level.glow[j].w > most)
                    {
                        most      = level.glow[j].w;
                        strongest = tags[j];
                    }
                }
                if (n > 0) fills.Add((i, sum / n, strongest));
            }
            foreach (var (point, glow, tag) in fills)
            {
                level.glow[point] = glow;
                if (tags != null) tags[point] = tag;
                inside[point]     = false;
            }
        }
    }

    internal static bool Cancelled(string title, string tag, string what, float progress)
    {
        if (!EditorUtility.DisplayCancelableProgressBar(title, what, progress)) return false;
        Debug.Log($"[{tag}] Bake cancelled — the glow was left as it was.");
        return true;
    }

    /// <summary>
    /// Both sets' bricks packed into one atlas (near first), and a brick grid for each saying
    /// where its bricks are. The far grid is null when there are no far points.
    /// </summary>
    private static void SaveTextures(Scene scene, Level near, Level far,
                                     out Texture3D atlas, out Texture3D nearMap, out Texture3D farMap)
    {
        int total = near.bricks.Count + (far?.bricks.Count ?? 0);

        // Packed into a near-cube, at most 255 bricks a side (the grids hold each brick's place in
        // a byte per axis).
        int bx = Mathf.Max(1, Mathf.CeilToInt(Mathf.Pow(total, 1f / 3f) - 1e-4f));
        int by = bx;
        int bz = Mathf.Max(1, Mathf.CeilToInt(total / (float)(bx * by)));
        int ax = bx * Brick, ay = by * Brick, az = bz * Brick;
        var texels = new ushort[ax * ay * az * 4];

        byte[] Pack(Level level, int first)
        {
            var grid  = level.grid;
            var cells = new byte[grid.x * grid.y * grid.z * 4];
            for (int n = 0; n < level.bricks.Count; n++)
            {
                var b = level.bricks[n];
                int slot = first + n;
                int u = slot % bx, v = (slot / bx) % by, w = slot / (bx * by);

                int c = (b.x + grid.x * (b.y + grid.y * b.z)) * 4;
                cells[c] = (byte)u; cells[c + 1] = (byte)v; cells[c + 2] = (byte)w; cells[c + 3] = 255;

                for (int z = 0; z < Brick; z++)
                for (int y = 0; y < Brick; y++)
                for (int x = 0; x < Brick; x++)
                {
                    var g = level.glow[level.index[Key(b.x * Span + x, b.y * Span + y, b.z * Span + z)]];
                    int t = ((u * Brick + x) + ax * ((v * Brick + y) + ay * (w * Brick + z))) * 4;
                    texels[t]     = Mathf.FloatToHalf(g.x);
                    texels[t + 1] = Mathf.FloatToHalf(g.y);
                    texels[t + 2] = Mathf.FloatToHalf(g.z);
                    texels[t + 3] = Mathf.FloatToHalf(g.w);
                }
            }
            return cells;
        }

        var nearCells = Pack(near, 0);
        var farCells  = far != null ? Pack(far, near.bricks.Count) : null;

        string sceneName = string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name;
        Directory.CreateDirectory(Folder);

        // Blended between points — the glow is meant to be soft.
        atlas = Refill($"{Folder}/RiverGlowAtlas_{sceneName}.asset", ax, ay, az,
                       GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, texels);
        // Read exactly, cell by cell.
        nearMap = Refill($"{Folder}/RiverGlowBricks_{sceneName}.asset", near.grid.x, near.grid.y, near.grid.z,
                         GraphicsFormat.R8G8B8A8_UNorm, FilterMode.Point, nearCells);

        string farPath = $"{Folder}/RiverGlowFarBricks_{sceneName}.asset";
        if (far != null)
            farMap = Refill(farPath, far.grid.x, far.grid.y, far.grid.z,
                            GraphicsFormat.R8G8B8A8_UNorm, FilterMode.Point, farCells);
        else
        {
            farMap = null;
            if (AssetDatabase.LoadAssetAtPath<Texture3D>(farPath) != null) AssetDatabase.DeleteAsset(farPath);
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Fills the 3D texture already at <paramref name="path"/> when it is the right size, so its
    /// GUID — and the light pointing at it — survives a re-bake; otherwise makes it anew.
    /// </summary>
    internal static Texture3D Refill<T>(string path, int w, int h, int d, GraphicsFormat format,
                                       FilterMode filter, T[] data) where T : struct
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture3D>(path);
        bool fresh = tex == null || tex.width != w || tex.height != h || tex.depth != d ||
                     tex.graphicsFormat != format;
        if (fresh)
        {
            if (tex != null) AssetDatabase.DeleteAsset(path);
            tex = new Texture3D(w, h, d, format, TextureCreationFlags.None)
                  { name = Path.GetFileNameWithoutExtension(path) };
        }

        tex.filterMode = filter;
        tex.wrapMode   = TextureWrapMode.Clamp;
        tex.SetPixelData(data, 0);
        tex.Apply(false, false);

        if (fresh) AssetDatabase.CreateAsset(tex, path);
        else       EditorUtility.SetDirty(tex);
        return tex;
    }

    internal static long CellKey(Vector3 p, float cell) =>
        Key(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));

    internal static long Key(int x, int y, int z) =>
        ((long)(x & 0x1FFFFF)) | ((long)(y & 0x1FFFFF) << 21) | ((long)(z & 0x1FFFFF) << 42);
}
