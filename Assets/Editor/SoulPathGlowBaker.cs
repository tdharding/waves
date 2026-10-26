using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// Bakes the soul path glow: the light each soul route's band throws onto everything around it —
/// the channel walls, the rims, the banks, the hills and the air the fog sits in — with the walls
/// blocking it, the same way <see cref="RiverGlowBaker"/> bakes the river's. Bake Soul Path Glow,
/// in the Lighting window; its Glow Reach and Width are the river water preset's, set in the Soul
/// Zone section of the Level Select River Tuner.
///
/// Each route's band is laid down the middle of its rivers (<see cref="SoulRouteCurve"/>, the line
/// the band is drawn along and the fish swim), as wide as the route's Band Width × the tuner's
/// Width, and dropped onto the water's surface. It is cut into patches, each a little light that
/// knows how far along its route it lies.
///
/// Inside a pool the band's light carries farther: its whole fall-off is stretched by the pool's
/// width of water over its river's channel width, so the pool's walls are lit as a river's are.
///
/// Every point keeps the glow of the ONE route that lights it most (routes do not add on top of
/// one another), and from how far along that route its light comes — the middle and the spread.
/// The River Glow shader shows a point's glow only once that route's band front has passed there,
/// so the glow comes up behind the front as the band draws out (<see cref="SoulPathGlow"/>).
///
/// One set of points, as close together as the river glow's near points, out to the whole reach.
/// Saved beside the river glow: <c>SoulGlowAtlas_&lt;scene&gt;</c> (xyz the way the glow comes from,
/// w how much — blended), <c>SoulGlowRoutes_&lt;scene&gt;</c> (route slot, middle, spread, how much
/// — read point by point, routes cannot be blended) and <c>SoulGlowBricks_&lt;scene&gt;</c>, written
/// onto the <see cref="LevelSelectLight"/>.
/// </summary>
public static class SoulPathGlowBaker
{
    private const string Title = "Bake Soul Path Glow";
    private const string Tag   = "SoulPathGlow";

    // The same detail as the river glow's near points.
    private const float MostSpacing   = 0.375f;
    private const float MostPatch     = 0.75f;
    private const float FinestSpacing = 0.1f;
    private const float FinestPatch   = 0.2f;

    // Two atlases of the same size, so half the river glow's limit keeps them both within 64 MB.
    private const int MostPoints = RiverGlowBaker.MostPoints / 2;

    // How far above the route's line the drop onto the water starts, and how far it looks.
    private const float DropFrom   = 5f;
    private const float DropLength = 100f;

    private const int Brick = RiverGlowBaker.Brick;
    private const int Span  = RiverGlowBaker.Span;

    /// <summary>The routes the bake gives slots to, in slot order.</summary>
    private static List<LevelSelectDesignerData.SoulRoute> Routes(LevelSelectDesignerData data)
    {
        var routes = new List<LevelSelectDesignerData.SoulRoute>();
        foreach (var route in data.soulRoutes)
        {
            if (route == null || string.IsNullOrEmpty(route.routeId)) continue;
            if (routes.Count == SoulPathGlow.MaxRoutes) break;
            routes.Add(route);
        }
        return routes;
    }

    /// <summary>True when the light's bake gave its slots to the routes the designer data has now.</summary>
    public static bool RoutesMatch(LevelSelectLight light, LevelSelectDesignerData data)
    {
        var routes = Routes(data);
        var baked  = light.soulGlowRouteIds;
        if (baked == null || baked.Count != routes.Count) return false;
        for (int i = 0; i < routes.Count; i++)
            if (baked[i] != routes[i].routeId) return false;
        return true;
    }

    public static void Bake(LevelSelectLight light, LevelSelectDesignerData data, RiverSoulZoneSettings look)
    {
        if (light == null || data == null || look == null) return;
        var scene = light.gameObject.scene;

        float reach     = Mathf.Max(0.01f, look.glowReach);
        // Resolution packs the points closer; the band's patches stay as they are.
        float resolution = Mathf.Max(1f, light.soulGlowResolution);
        float spacing   = Mathf.Clamp(reach / 8f, FinestSpacing, MostSpacing) / resolution;
        float patchSize = Mathf.Clamp(reach / 4f, FinestPatch, MostPatch);
        float width     = Mathf.Max(look.width, 0f);

        var routes = Routes(data);
        if (routes.Count == 0)
        {
            Debug.LogWarning($"[{Tag}] The designer data has no soul routes. No glow baked.");
            return;
        }
        int skippedRoutes = 0;
        foreach (var route in data.soulRoutes)
            if (route != null && !string.IsNullOrEmpty(route.routeId)) skippedRoutes++;
        skippedRoutes -= routes.Count;
        if (skippedRoutes > 0)
            Debug.LogWarning($"[{Tag}] Only the first {SoulPathGlow.MaxRoutes} soul routes get a glow — " +
                             $"{skippedRoutes} more are left out. Raise SoulPathGlow.MaxRoutes and " +
                             "SOULGLOW_MAX_ROUTES in RiverGlow.hlsl together.");

        var timer = System.Diagnostics.Stopwatch.StartNew();
        long rays = 0;

        var waterScene = EditorSceneManager.NewPreviewScene();
        var preview    = EditorSceneManager.NewPreviewScene();
        var meshes     = new List<Mesh>();
        try
        {
            var waterPhysics = waterScene.GetPhysicsScene();
            var physics      = preview.GetPhysicsScene();
            if (!physics.IsValid() || physics == Physics.defaultPhysicsScene ||
                !waterPhysics.IsValid() || waterPhysics == Physics.defaultPhysicsScene)
            {
                Debug.LogError($"[{Tag}] The preview scene has no physics of its own, so the rays would " +
                               "hit the open scene's colliders. No glow baked.");
                return;
            }

            // The water on its own, so the bands can be dropped onto its surface.
            var water = RiverGlowBaker.FindWater(scene, out string waterSummary);
            LevelSelectShadowBaker.BuildColliders(water, waterScene, meshes, Title);

            // Every route's band, cut into patches.
            var runLines  = SoulRouteCurve.SampleRuns(Object.FindObjectsOfType<RiverRunMesh>());
            var pools     = Object.FindObjectsOfType<RiverPoolMesh>();
            var stretches = PoolStretches(data, pools);
            var bands     = new List<RiverGlowBaker.Patch[]>();
            var union     = new List<RiverGlowBaker.Patch>();
            int notOnWater = 0, inPools = 0;
            for (int r = 0; r < routes.Count; r++)
            {
                var route    = routes[r];
                var problems = new List<string>();
                var line     = SoulRouteCurve.Build(data, data.BuildSoulRouteNodeIds(route), runLines, pools,
                                                    out _, problems);
                foreach (string problem in problems)
                    Debug.LogWarning($"[{Tag}] Route '{route.displayName}': {problem}");

                var band = Band(line, Mathf.Max(route.bandWidth, 0.0001f) * width, patchSize,
                                waterPhysics, stretches, ref notOnWater, ref inPools);
                if (band.Length == 0)
                    Debug.LogWarning($"[{Tag}] Route '{route.displayName}' has no line to glow along — " +
                                     "it gets no glow.");
                bands.Add(band);
                union.AddRange(band);
            }
            if (union.Count == 0)
            {
                Debug.LogWarning($"[{Tag}] No soul route has a line to glow along. No glow baked.");
                return;
            }

            var bounds = new Bounds(union[0].position, Vector3.zero);
            foreach (var p in union) bounds.Encapsulate(p.position);

            // The points reach a patch-width past the reach, so the glow is whole even where a
            // patch's middle sits back from the band's edge.
            // In a pool, (stretch − 1) × the reach farther.
            var level = RiverGlowBaker.MakeLevel(union.ToArray(), bounds, spacing, reach + patchSize, reach);
            long points = (long)level.bricks.Count * Brick * Brick * Brick;
            if (points > MostPoints)
            {
                Debug.LogError($"[{Tag}] A Glow Reach of {reach:0.##} m at a Resolution of " +
                               $"{resolution:0.##} needs {level.bricks.Count} bricks " +
                               $"({points / 1000000f:0.#} million points) — too many to save. Lower the " +
                               "Glow Reach or the Resolution. No glow baked.");
                return;
            }

            var casters = LevelSelectShadowBaker.FindCasters(scene, out string casterSummary);
            LevelSelectShadowBaker.BuildColliders(casters, preview, meshes, Title);

            RiverGlowBaker.BuildPoints(level);
            int count   = level.points.Count;
            var all     = new bool[count];
            System.Array.Fill(all, true);
            if (!RiverGlowBaker.FindInside(physics, level, all, "soul path", Title, Tag, ref rays, out var inside))
                return;

            // Route by route, each point keeping the one that lights it most: xyz the route's slot,
            // the middle of where along it the light comes from, and the spread; w how much.
            var tags    = new Vector4[count];
            var glow    = new Vector4[count];
            var arcs    = new Vector4[count];
            var compute = new bool[count];
            float soften = patchSize * patchSize;
            for (int r = 0; r < routes.Count; r++)
            {
                if (bands[r].Length == 0) continue;

                System.Array.Clear(glow, 0, count);
                System.Array.Clear(arcs, 0, count);
                MarkNear(level, bands[r], reach + patchSize, reach, compute);

                // Grouped, and looked up, out to the farthest any of its patches' light carries.
                float most = 1f;
                foreach (var p in bands[r]) most = Mathf.Max(most, p.Stretch);
                var patches = RiverGlowBaker.GroupPatches(bands[r], patchSize, reach * most);
                if (!RiverGlowBaker.CastToPatches(physics, level, compute, inside, patches, reach, soften,
                                                  $"route {r + 1} of {routes.Count}", Title, Tag, ref rays,
                                                  glow, arcs))
                    return;

                for (int i = 0; i < count; i++)
                {
                    float w = glow[i].w;
                    if (!compute[i] || w <= level.glow[i].w) continue;

                    float middle = arcs[i].x / w;
                    float spread = Mathf.Sqrt(Mathf.Max(0f, arcs[i].y / w - middle * middle));
                    level.glow[i] = glow[i];
                    tags[i]       = new Vector4(r, middle, spread, w);
                }
            }

            RiverGlowBaker.FillInside(level, inside, tags);
            for (int i = 0; i < count; i++) tags[i].w = level.glow[i].w;

            SaveTextures(scene, level, tags, out var atlas, out var routeMap, out var brickMap);

            Undo.RecordObject(light, Title);
            light.soulGlowAtlas      = atlas;
            light.soulGlowRoutes     = routeMap;
            light.soulGlowBricks     = brickMap;
            light.soulGlowOrigin     = new Vector4(level.origin.x, level.origin.y, level.origin.z, level.spacing);
            light.soulGlowBakedReach      = look.glowReach;
            light.soulGlowBakedResolution = light.soulGlowResolution;
            light.soulGlowRouteIds   = routes.ConvertAll(route => route.routeId);
            EditorUtility.SetDirty(light);
            EditorSceneManager.MarkSceneDirty(scene);

            LevelSelectLight.Push();
            SceneView.RepaintAll();

            Debug.Log($"[{Tag}] Baked {routes.Count} route(s) with a reach of {reach:0.##} m in " +
                      $"{timer.Elapsed.TotalSeconds:0.#} s — {union.Count} band patches {patchSize:0.##} m " +
                      $"across, dropped onto {water.Count} water surfaces ({waterSummary})" +
                      (notOnWater > 0 ? $", {notOnWater} with no water under them left on the route's line" : "") +
                      (inPools > 0 ? $", {inPools} in pools carrying farther ({PoolSummary(stretches)})" : "") +
                      $"; points {level.spacing:0.###} m apart ({level.bricks.Count} bricks, {count} points); " +
                      $"{rays} rays; {casters.Count} blockers ({casterSummary}). " +
                      $"Atlas {atlas.width}×{atlas.height}×{atlas.depth}. Saved to {AssetDatabase.GetAssetPath(atlas)}.",
                      atlas);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            EditorSceneManager.ClosePreviewScene(preview);
            EditorSceneManager.ClosePreviewScene(waterScene);
            foreach (var m in meshes) Object.DestroyImmediate(m);
        }
    }

    /// <summary>A pool the bands cross, and how much farther a band's light carries in it.</summary>
    private struct PoolStretch
    {
        public string  name;
        public Vector3 centre;
        public float   radius;
        public float   stretch;
    }

    /// <summary>
    /// Every generated pool and its stretch: the width of water the band crosses it on over its
    /// river's own channel width — round the ring, island edge to channel edge; an open bowl, the
    /// whole way across. Never below 1: a pool narrower than its river is lit as the river is.
    /// </summary>
    private static List<PoolStretch> PoolStretches(LevelSelectDesignerData data, IList<RiverPoolMesh> pools)
    {
        var list = new List<PoolStretch>();
        foreach (var pool in pools)
        {
            if (pool == null || pool.poolRadius <= 0f) continue;
            float poolWidth  = pool.islandRadius > 0f ? pool.poolRadius - pool.islandRadius : 2f * pool.poolRadius;
            float riverWidth = Mathf.Max(0.001f, data.ProfileFor(pool.riverName).innerWidth);
            list.Add(new PoolStretch
            {
                name    = pool.meshAssetName,
                centre  = pool.transform.position,
                radius  = pool.poolRadius,
                stretch = Mathf.Max(1f, poolWidth / riverWidth),
            });
        }
        return list;
    }

    private static string PoolSummary(List<PoolStretch> stretches)
        => string.Join(", ", stretches.ConvertAll(s => $"{s.name} ×{s.stretch:0.##}"));

    /// <summary>
    /// A route's band cut into patches about <paramref name="size"/> long and across, facing up,
    /// each knowing how far along the route it lies. Each is dropped onto the water under it;
    /// one with no water under it stays on the line, and is counted. A patch inside a pool carries
    /// that pool's stretch.
    /// </summary>
    private static RiverGlowBaker.Patch[] Band(List<Vector3> line, float halfWidth, float size,
                                               PhysicsScene water, List<PoolStretch> pools,
                                               ref int notOnWater, ref int inPools)
    {
        var band = new List<RiverGlowBaker.Patch>();
        if (line == null || line.Count < 2 || halfWidth <= 0f) return band.ToArray();

        var cumArc = new List<float> { 0f };
        for (int i = 1; i < line.Count; i++) cumArc.Add(cumArc[i - 1] + Vector3.Distance(line[i - 1], line[i]));
        float length = cumArc[cumArc.Count - 1];
        if (length <= 0f) return band.ToArray();

        int   along  = Mathf.Max(1, Mathf.CeilToInt(length / size));
        float step   = length / along;
        int   across = Mathf.Max(1, Mathf.RoundToInt(2f * halfWidth / size));
        float cross  = 2f * halfWidth / across;

        for (int a = 0; a < along; a++)
        {
            float   s      = (a + 0.5f) * step;
            Vector3 centre = SoulRouteCurve.PointAt(line, cumArc, s);
            Vector3 ahead  = SoulRouteCurve.PointAt(line, cumArc, Mathf.Min(length, s + step * 0.5f)) -
                             SoulRouteCurve.PointAt(line, cumArc, Mathf.Max(0f, s - step * 0.5f));
            ahead.y = 0f;
            if (ahead.sqrMagnitude < 1e-10f) ahead = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, ahead.normalized);

            for (int c = 0; c < across; c++)
            {
                Vector3 at = centre + side * (-halfWidth + (c + 0.5f) * cross);
                if (water.Raycast(at + Vector3.up * DropFrom, Vector3.down, out RaycastHit hit, DropLength))
                    at.y = hit.point.y;
                else
                    notOnWater++;

                float stretch = 1f;
                foreach (var pool in pools)
                {
                    float dx = at.x - pool.centre.x, dz = at.z - pool.centre.z;
                    if (dx * dx + dz * dz < pool.radius * pool.radius) stretch = Mathf.Max(stretch, pool.stretch);
                }
                if (stretch > 1f) inPools++;

                band.Add(new RiverGlowBaker.Patch
                {
                    position = at,
                    normal   = Vector3.up,
                    area     = step * cross,
                    arc      = s,
                    arcSq    = s * s + step * step / 12f,
                    stretch  = stretch,
                });
            }
        }
        return band.ToArray();
    }

    /// <summary>
    /// Marks the points in the bricks within <paramref name="coverage"/> of a route's band — the
    /// only ones that route can light — so each route's rays are cast from its own stretch alone.
    /// A patch in a pool reaches (stretch − 1) × <paramref name="stretchReach"/> farther.
    /// </summary>
    private static void MarkNear(RiverGlowBaker.Level level, RiverGlowBaker.Patch[] band, float coverage,
                                 float stretchReach, bool[] compute)
    {
        System.Array.Clear(compute, 0, compute.Length);
        float brickSpan = Span * level.spacing;
        var   grid      = level.grid;
        var   seen      = new HashSet<long>();
        foreach (var p in band)
        {
            float   cover = RiverGlowBaker.Coverage(p, coverage, stretchReach);
            Vector3 lo = (p.position - Vector3.one * cover - level.origin) / brickSpan;
            Vector3 hi = (p.position + Vector3.one * cover - level.origin) / brickSpan;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(lo.x)), x1 = Mathf.Min(grid.x - 1, Mathf.FloorToInt(hi.x));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(lo.y)), y1 = Mathf.Min(grid.y - 1, Mathf.FloorToInt(hi.y));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(lo.z)), z1 = Mathf.Min(grid.z - 1, Mathf.FloorToInt(hi.z));
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (!seen.Add(RiverGlowBaker.Key(x, y, z))) continue;
                for (int k = 0; k < Brick; k++)
                for (int j = 0; j < Brick; j++)
                for (int i = 0; i < Brick; i++)
                    if (level.index.TryGetValue(RiverGlowBaker.Key(x * Span + i, y * Span + j, z * Span + k), out int n))
                        compute[n] = true;
            }
        }
    }

    /// <summary>
    /// The bricks packed into two atlases laid out the same — the glow, and which route lights each
    /// point and from where along it — and the grid saying where each brick is.
    /// </summary>
    private static void SaveTextures(UnityEngine.SceneManagement.Scene scene, RiverGlowBaker.Level level,
                                     Vector4[] tags, out Texture3D atlas, out Texture3D routeMap,
                                     out Texture3D brickMap)
    {
        int total = level.bricks.Count;

        // Packed into a near-cube, at most 255 bricks a side (the grid holds each brick's place in
        // a byte per axis).
        int bx = Mathf.Max(1, Mathf.CeilToInt(Mathf.Pow(total, 1f / 3f) - 1e-4f));
        int by = bx;
        int bz = Mathf.Max(1, Mathf.CeilToInt(total / (float)(bx * by)));
        int ax = bx * Brick, ay = by * Brick, az = bz * Brick;
        var glowTexels  = new ushort[ax * ay * az * 4];
        var routeTexels = new ushort[ax * ay * az * 4];

        var grid  = level.grid;
        var cells = new byte[grid.x * grid.y * grid.z * 4];
        for (int n = 0; n < total; n++)
        {
            var b = level.bricks[n];
            int u = n % bx, v = (n / bx) % by, w = n / (bx * by);

            int c = (b.x + grid.x * (b.y + grid.y * b.z)) * 4;
            cells[c] = (byte)u; cells[c + 1] = (byte)v; cells[c + 2] = (byte)w; cells[c + 3] = 255;

            for (int z = 0; z < Brick; z++)
            for (int y = 0; y < Brick; y++)
            for (int x = 0; x < Brick; x++)
            {
                int i = level.index[RiverGlowBaker.Key(b.x * Span + x, b.y * Span + y, b.z * Span + z)];
                int t = ((u * Brick + x) + ax * ((v * Brick + y) + ay * (w * Brick + z))) * 4;
                var g = level.glow[i];
                var r = tags[i];
                glowTexels[t]      = Mathf.FloatToHalf(g.x);
                glowTexels[t + 1]  = Mathf.FloatToHalf(g.y);
                glowTexels[t + 2]  = Mathf.FloatToHalf(g.z);
                glowTexels[t + 3]  = Mathf.FloatToHalf(g.w);
                routeTexels[t]     = Mathf.FloatToHalf(r.x);
                routeTexels[t + 1] = Mathf.FloatToHalf(r.y);
                routeTexels[t + 2] = Mathf.FloatToHalf(r.z);
                routeTexels[t + 3] = Mathf.FloatToHalf(r.w);
            }
        }

        string sceneName = string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name;
        string folder    = RiverGlowBaker.Folder;
        Directory.CreateDirectory(folder);

        // Blended between points — the glow is meant to be soft.
        atlas = RiverGlowBaker.Refill($"{folder}/SoulGlowAtlas_{sceneName}.asset", ax, ay, az,
                                      GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, glowTexels);
        // Read point by point: a route's slot cannot be blended with its neighbour's.
        routeMap = RiverGlowBaker.Refill($"{folder}/SoulGlowRoutes_{sceneName}.asset", ax, ay, az,
                                         GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, routeTexels);
        brickMap = RiverGlowBaker.Refill($"{folder}/SoulGlowBricks_{sceneName}.asset", grid.x, grid.y, grid.z,
                                         GraphicsFormat.R8G8B8A8_UNorm, FilterMode.Point, cells);
        AssetDatabase.SaveAssets();
    }
}
