using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Splines;
using Unity.Mathematics;

public class LevelSpawner : MonoBehaviour
{
#if UNITY_EDITOR
    // Set by SaveDataEditorWindow. Editor/playtest only — has no effect in builds.
    public static bool ForceDisableSnake = false;
#endif

    [Header("Soul Fish Zone")]
    [SerializeField] GameObject soulFishContainerPrefab;

    [Header("Orbs of Omalon")]
    [SerializeField] GameObject orbPrefab;

    [Header("Modifiers")]
    [SerializeField] GameObject waterLevelModifierPrefab;
    [SerializeField] GameObject waveModifierPrefab;
    [SerializeField] GameObject soulFishInputTubePrefab;
    [SerializeField] WaveMaterialController waveController;

    [Header("Tier Config")]
    public TierConfig tierConfig;

    [Header("Spawn References")]
    [SerializeField] Transform spawnParent;
    [SerializeField] Transform soulSpawnParent;
    public Renderer referencePlane;

    [Header("Orientation")]
    [SerializeField] bool applyMinus90XRotation = true;
    [SerializeField] bool applyPostSpawnY180Rotation = true;

    [Header("Post Spawn Offset")]
    [SerializeField] Vector3 postSpawnPositionOffset;
    [SerializeField] Vector3 soulPostSpawnPositionOffset;

    [Header("Maze Reveal")]
    [SerializeField] float mazeStartY = 0f;
    [SerializeField] float mazeEndY = 5f;
    [SerializeField] float mazeMoveDuration = 2f;
    [SerializeField] AudioSource mazeSound;

    [Header("Spline Walls")]
    [SerializeField] GameObject splineWallPrefab;
    [SerializeField] GameObject splineNodePointPrefab;

    [Header("Fishing")]
    [SerializeField] FishingController fishingController;

    [Header("FX")]
    [SerializeField] private WhirlFXController whirlFX;
    [SerializeField] private WhirlpoolManager  whirlpoolManager;

    [Header("Sonar")]
    [SerializeField] SonarController sonarController;

    public bool mazeSpawned;
    bool mazeRotated;

    private GridData activeGridData;
    private ArenaProfile activeArenaProfile;
    public ArenaProfile GetArenaProfile() => activeArenaProfile;
    private Bounds cachedArenaBounds;
    public Bounds GetArenaBounds() => cachedArenaBounds;
    private float spawnedBaselineWaterY;
    public float GetBaselineWaterY() => spawnedBaselineWaterY;
    private BaselineMarker activeBaselineMarker;
    public BaselineMarker GetBaselineMarker() => activeBaselineMarker;
    // Absolute world-Y per tier slot. Populated from BaselineMarker.spawnTiers when defined;
    // null otherwise so all existing TierConfig paths run unchanged.
    private float[] absoluteTierHeights;

    // =====================================================
    // GRID DATA INJECTION
    // =====================================================

    public void ApplyGridData(GridData data)
    {
        if (data == null)
        {
            Debug.LogError("LevelSpawner: GridData is null.");
            return;
        }

        activeGridData     = data;
        activeArenaProfile = data.arenaProfile;

        if (activeArenaProfile == null)
            Debug.LogWarning($"[LevelSpawner] No ArenaProfile on GridData '{data.name}'. Assign one in the Grid Designer.");

    }

    public void SetGridData(GridData data) => activeGridData = data;

    private void InitializeWaveModifier(GameObject go)
    {
        // Handle TypeA
        go.GetComponent<LevelWaveModifierControllerTypeA>()?.Init(waveController);

        // Handle TypeB
        var controllerB = go.GetComponent<LevelWaveModifierControllerTypeB>();
        if (controllerB != null)
        {
            controllerB.Init(waveController);
        }
    }

    // Applies per-placement TypeB overrides (speed / frequency / ripple depth boost)
    // set in the Grid Designer. When the placement doesn't override, the prefab's
    // own default values are left untouched.
    private void ApplyModifierOverrides(GameObject go, GridData.PrefabPlacement pp)
    {
        if (pp == null || !pp.overrideModifierSettings) return;

        var controllerB = go.GetComponent<LevelWaveModifierControllerTypeB>();
        if (controllerB == null) return;

        controllerB.speedBoost       = pp.speedBoost;
        controllerB.frequencyBoost   = pp.frequencyBoost;
        controllerB.rippleDepthBoost = pp.rippleDepthBoost;
    }

    // Applies the designer-authored uniform scale to a spawned placement instance.
    // A stored scale of 0 (legacy placements from before the field existed) is
    // treated as 1 so existing levels are unaffected.
    private void ApplyPlacementScale(GameObject go, GridData.PrefabPlacement pp)
    {
        if (go == null || pp == null) return;
        float s = pp.scale > 0f ? pp.scale : 1f;
        if (!Mathf.Approximately(s, 1f))
            go.transform.localScale *= s;
    }

    // Statues spawned this pass, keyed by PrefabPlacement.statueId, so guarded soul-fish
    // zones can find their statue to gate catchability. Cleared at the start of each spawn.
    private readonly Dictionary<int, StatueBehaviour> _statuesById = new Dictionary<int, StatueBehaviour>();

    // Stamps the placement's statueId onto the spawned StatueBehaviour and registers it.
    private void RegisterStatue(GameObject go, GridData.PrefabPlacement pp)
    {
        if (go == null || pp == null || pp.statueId == 0) return;
        var statue = go.GetComponent<StatueBehaviour>();
        if (statue == null) return;
        statue.statueId = pp.statueId;
        _statuesById[pp.statueId] = statue;
    }

    // Fish-bowl towers spawned this pass, keyed by PrefabPlacement.statueId (reused as the guard id),
    // so tower-guarded zones can find their tower to hand it the bowl container. Cleared each spawn.
    private readonly Dictionary<int, FishBowlTowerController> _towersById = new Dictionary<int, FishBowlTowerController>();

    // Registers a spawned FishBowlTower so its zone can link the aloft shoal container to it.
    private void RegisterTower(GameObject go, GridData.PrefabPlacement pp)
    {
        if (go == null || pp == null || pp.statueId == 0) return;
        var tower = go.GetComponentInChildren<FishBowlTowerController>(true);
        if (tower == null) return;
        _towersById[pp.statueId] = tower;
    }

    // =====================================================
    // SPAWN
    // =====================================================

    public void SpawnMaze()
    {
        if (mazeSpawned) { Debug.Log("[LevelSpawner] SpawnMaze — already spawned, skipping."); return; }
        
        if (!activeGridData || !spawnParent)
        {
            Debug.LogWarning($"[LevelSpawner] SpawnMaze EARLY EXIT — activeGridData={activeGridData != null} spawnParent={spawnParent != null}");
            return;
        }

        Dictionary<string, GameObject> spawnedByCell = new Dictionary<string, GameObject>();

        if (activeGridData.orbCellIndices == null)
            activeGridData.orbCellIndices = new List<int>();

        float r = activeArenaProfile != null ? activeArenaProfile.WorldArenaRadius : 20f;
        Bounds b = new Bounds(Vector3.zero, new Vector3(r * 2, 0, r * 2));
        
        cachedArenaBounds = b;
float   tileX  = b.size.x / GridData.GridSize;
float   tileZ  = b.size.z / GridData.GridSize;
        Vector3 origin = b.min;

        // ── Baseline water Y — single source of truth for all tier/water positioning ──
        var baselineMarker = activeArenaProfile?.outerWallsPrefab?.GetComponentInChildren<BaselineMarker>();
        activeBaselineMarker = baselineMarker;
        spawnedBaselineWaterY = baselineMarker?.height ?? spawnParent.position.y;
        Quaternion baselineRot = baselineMarker != null
            ? Quaternion.LookRotation(baselineMarker.transform.forward, Vector3.up)
            : Quaternion.identity;

        if (baselineMarker != null) sonarController?.SetBaselineMarker(baselineMarker);

        // Build absolute tier heights from BaselineMarker.spawnTiers when defined.
        // Left null if not defined — all downstream tier code falls back to TierConfig offsets unchanged.
        absoluteTierHeights = null;
        if (baselineMarker?.spawnTiers != null && baselineMarker.spawnTiers.Length > 0)
        {
            absoluteTierHeights = new float[baselineMarker.spawnTiers.Length];
            for (int i = 0; i < absoluteTierHeights.Length; i++)
                absoluteTierHeights[i] = baselineMarker.spawnTiers[i].height;
        }

        // ── Reality Layer — orbs / water / wave modifiers ──
        var  waveModAlign    = waveModifierPrefab != null ? waveModifierPrefab.GetComponentInChildren<PrefabBaselineAlignment>() : null;
        bool waveModHasAlign = waveModAlign != null;
        float waveModContactY = waveModAlign != null ? waveModAlign.transform.position.y : 0f;

        for (int y = 0; y < GridData.GridSize; y++)
        {
            int flippedY = GridData.GridSize - 1 - y;

            for (int x = 0; x < GridData.GridSize; x++)
            {
                int index = flippedY * GridData.GridSize + x;

                Vector3 pos = new Vector3(
                    origin.x + x * tileX + tileX * 0.5f,
                    spawnedBaselineWaterY,
                    origin.z + y * tileZ + tileZ * 0.5f
                );

                Quaternion rot = spawnParent.rotation;
                if (applyMinus90XRotation) rot *= Quaternion.Euler(-90f, 0f, 0f);

                if (activeGridData.orbCellIndices.Contains(index) && orbPrefab)
                    Instantiate(orbPrefab, pos, rot, spawnParent);

                if (activeGridData.waterLevelModifierCellIndices != null &&
                    activeGridData.waterLevelModifierCellIndices.Contains(index) && waterLevelModifierPrefab)
                {
                    var go = Instantiate(waterLevelModifierPrefab, pos, Quaternion.identity, spawnParent);
                    if (absoluteTierHeights != null)
                    {
                        int gSlot = FindGroundFloorSlot(absoluteTierHeights, spawnedBaselineWaterY);
                        float[] rel = ToRelativeOffsets(absoluteTierHeights, spawnedBaselineWaterY);
                        go.GetComponent<WaterLevelModifier>()?.Init(gSlot, rel, "G", spawnedBaselineWaterY);
                    }
                    else
                    {
                        float[] offsets0 = tierConfig?.offsets;
                        go.GetComponent<WaterLevelModifier>()?.Init(FindGroundFloorSlot(offsets0), offsets0, "G", spawnedBaselineWaterY);
                    }
                }

                if (activeGridData.waveModifierCellIndices != null &&
                    activeGridData.waveModifierCellIndices.Contains(index) && waveModifierPrefab)
                {
                    float waveSpawnY = spawnedBaselineWaterY - waveModContactY;
                    Quaternion waveRot = waveModHasAlign ? baselineRot : Quaternion.identity;
                    var go = Instantiate(waveModifierPrefab, new Vector3(pos.x, waveSpawnY, pos.z), waveRot, spawnParent);
                    InitializeWaveModifier(go);
                }
            }
        }

        // ── Arena Portals (Entrances & Exits) ──
        SpawnArenaPortals();

        // ── Direct Prefab Placements (base layer) ──

        _statuesById.Clear();
        _towersById.Clear();

        if (activeGridData.prefabPlacements != null)
        {
            foreach (var pp in activeGridData.prefabPlacements)
            {
                if (pp.prefab == null) continue;
                int cellX    = pp.cellIndex % GridData.GridSize;
                int cellY    = pp.cellIndex / GridData.GridSize;
                int flippedY = GridData.GridSize - 1 - cellY;
                Transform par = (pp.isCircle && soulSpawnParent) ? soulSpawnParent : spawnParent;
                var  baselineAlign    = pp.prefab.GetComponentInChildren<PrefabBaselineAlignment>();
                bool hasBaselineAlign = baselineAlign != null;
                float placementScale  = pp.scale > 0f ? pp.scale : 1f;
                // The baseline contact offset scales with the object, so the waterline
                // stays aligned when the placement is scaled up/down.
                float contactOffsetY  = baselineAlign != null ? baselineAlign.transform.position.y * placementScale : 0f;
                float baselineSpawnY  = spawnedBaselineWaterY - contactOffsetY;
                float spawnY          = (pp.isWorldSpaceProp || hasBaselineAlign) ? baselineSpawnY : par.position.y;
                Vector3 pos   = new Vector3(
                    origin.x + cellX    * tileX + tileX * 0.5f,
                    spawnY,
                    origin.z + flippedY * tileZ + tileZ * 0.5f
                );
                Quaternion rot = hasBaselineAlign ? baselineRot : par.rotation;
                var instance = Instantiate(pp.prefab, pos, rot, par);
                ApplyPlacementScale(instance, pp);
                RegisterStatue(instance, pp);
                RegisterTower(instance, pp);
                spawnedByCell[$"-1_{pp.cellIndex}"] = instance;
                InitializeWaveModifier(instance);
                ApplyModifierOverrides(instance, pp);
            }
        }

        // ── Extra Tiers ──
        if (activeGridData.tiers != null)
        {
            for (int ti = 0; ti < activeGridData.tiers.Count; ti++)
            {
                var tier = activeGridData.tiers[ti];
                float[] offsets = tierConfig?.offsets;
                float tierAbsY;
                float yOff;
                if (absoluteTierHeights != null && tier.yOffsetSlot < absoluteTierHeights.Length)
                {
                    tierAbsY = absoluteTierHeights[tier.yOffsetSlot];
                    yOff     = tierAbsY - spawnedBaselineWaterY;
                }
                else
                {
                    yOff     = (offsets != null && tier.yOffsetSlot < offsets.Length) ? offsets[tier.yOffsetSlot] : tier.yOffset;
                    tierAbsY = spawnedBaselineWaterY + yOff;
                }

                for (int y = 0; y < GridData.GridSize; y++)
                {
                    int flippedY = GridData.GridSize - 1 - y;
                    for (int x = 0; x < GridData.GridSize; x++)
                    {
                        int index = flippedY * GridData.GridSize + x;
                        Vector3 pos = new Vector3(
                            origin.x + x * tileX + tileX * 0.5f,
                            spawnedBaselineWaterY + yOff,
                            origin.z + y * tileZ + tileZ * 0.5f
                        );
                        Quaternion rot = spawnParent.rotation;
                        if (applyMinus90XRotation) rot *= Quaternion.Euler(-90f, 0f, 0f);

                        if (tier.waterLevelModifierCellIndices != null &&
                            tier.waterLevelModifierCellIndices.Contains(index) && waterLevelModifierPrefab)
                        {
                            var go = Instantiate(waterLevelModifierPrefab, pos, Quaternion.identity, spawnParent);
                            if (absoluteTierHeights != null)
                            {
                                float[] rel = ToRelativeOffsets(absoluteTierHeights, spawnedBaselineWaterY);
                                go.GetComponent<WaterLevelModifier>()?.Init(tier.yOffsetSlot, rel, tier.name, spawnedBaselineWaterY);
                            }
                            else
                            {
                                go.GetComponent<WaterLevelModifier>()?.Init(tier.yOffsetSlot, offsets, tier.name, spawnedBaselineWaterY);
                            }
                        }

                        if (tier.waveModifierCellIndices != null &&
                            tier.waveModifierCellIndices.Contains(index) && waveModifierPrefab)
                        {
                            float waveSpawnY = pos.y - waveModContactY;
                            Quaternion waveRot = waveModHasAlign ? baselineRot : Quaternion.identity;
                            var go = Instantiate(waveModifierPrefab, new Vector3(pos.x, waveSpawnY, pos.z), waveRot, spawnParent);
                            InitializeWaveModifier(go);
                        }
                    }
                }

                // Direct prefab placements for this tier
                if (tier.prefabPlacements != null)
                {
                    foreach (var pp in tier.prefabPlacements)
                    {
                        if (pp.prefab == null) continue;
                        int cellX    = pp.cellIndex % GridData.GridSize;
                        int cellY    = pp.cellIndex / GridData.GridSize;
                        int flippedY = GridData.GridSize - 1 - cellY;
                        var  tierAlign      = pp.prefab.GetComponentInChildren<PrefabBaselineAlignment>();
                        float tierScale     = pp.scale > 0f ? pp.scale : 1f;
                        float tierContactY  = tierAlign != null ? tierAlign.transform.position.y * tierScale : 0f;
                        Vector3 pos2 = new Vector3(
                            origin.x + cellX    * tileX + tileX * 0.5f,
                            spawnedBaselineWaterY + yOff - tierContactY,
                            origin.z + flippedY * tileZ + tileZ * 0.5f
                        );
                        Quaternion rot2 = spawnParent.rotation;
                        if (applyMinus90XRotation) rot2 *= Quaternion.Euler(-90f, 0f, 0f);
                        var instance = Instantiate(pp.prefab, pos2, rot2, spawnParent);
                        ApplyPlacementScale(instance, pp);
                        RegisterStatue(instance, pp);
                        RegisterTower(instance, pp);
                        spawnedByCell[$"{ti}_{pp.cellIndex}"] = instance;
                        InitializeWaveModifier(instance);
                        ApplyModifierOverrides(instance, pp);
                    }
                }
            }
        }

        // ── Soul Fish — spawned before offset/rotation so they move with spawnParent ──
        SpawnSoulFish(origin, tileX, tileZ);

        // ── Spline Walls — must be before Y180 rotation so they move with spawnParent ──
        SpawnSplineWalls();

        if (!mazeRotated)
        {
            spawnParent.position += postSpawnPositionOffset;
            if (applyPostSpawnY180Rotation)
                spawnParent.rotation *= Quaternion.Euler(0f, 180f, 0f);

            if (soulSpawnParent)
            {
                soulSpawnParent.position += soulPostSpawnPositionOffset;
                if (applyPostSpawnY180Rotation)
                    soulSpawnParent.rotation *= Quaternion.Euler(0f, 180f, 0f);
            }

            mazeRotated = true;
        }

        // ── Whirlpools ──
        if (whirlpoolManager != null && activeGridData.whirlpools != null && activeGridData.whirlpools.Count > 0)
        {
            Transform handlesParent = whirlpoolManager.whirlpoolHandlesParent != null
                ? whirlpoolManager.whirlpoolHandlesParent
                : whirlpoolManager.transform;

            int wpCount = Mathf.Min(activeGridData.whirlpools.Count, 8);
            for (int i = 0; i < wpCount; i++)
            {
                var   wp       = activeGridData.whirlpools[i];
                int   cellX    = wp.cellIndex % GridData.GridSize;
                int   cellY    = wp.cellIndex / GridData.GridSize;
                int   flippedY = GridData.GridSize - 1 - cellY;
                float wx       = origin.x + cellX    * tileX + tileX * 0.5f;
                float wz       = origin.z + flippedY * tileZ + tileZ * 0.5f;

                if (applyPostSpawnY180Rotation)
                {
                    float centerX = origin.x + b.size.x * 0.5f;
                    float centerZ = origin.z + b.size.z * 0.5f;
                    wx = 2f * centerX - wx;
                    wz = 2f * centerZ - wz;
                }

                var go         = new GameObject($"Whirlpool_{i}");
                go.transform.SetParent(handlesParent);
                go.transform.position   = new Vector3(wx, spawnedBaselineWaterY, wz);
                go.transform.localScale = Vector3.one * wp.radius;

                var wpTrigger = go.AddComponent<WhirlpoolExitTrigger>();
                wpTrigger.triggerRadius = wp.radius;
            }

            whirlpoolManager.globalDepth = activeGridData.whirlpoolDepth;
            whirlpoolManager.globalSwirl = activeGridData.whirlpoolSwirl;
        }

        if (activeGridData.sculptureSetPiecePrefab)
            Instantiate(activeGridData.sculptureSetPiecePrefab, Vector3.zero, Quaternion.identity);

        if (activeArenaProfile?.outerWallsPrefab != null)
            Instantiate(activeArenaProfile.outerWallsPrefab, Vector3.zero, Quaternion.identity);

        ProcessLinkedPairs(spawnedByCell);

        mazeSpawned = true;
        Debug.Log("MazeSpawned = true");
    }

    // =====================================================
    // SPLINE WALL SPAWNING
    // =====================================================

    private void SpawnSplineWalls()
    {
        if (activeGridData?.splineWallPaths == null || activeGridData.splineWallPaths.Count == 0) return;

        var baselineAlign = splineWallPrefab != null
            ? splineWallPrefab.GetComponentInChildren<PrefabBaselineAlignment>() : null;
        float defaultContactY = baselineAlign != null ? baselineAlign.transform.localPosition.y : 0f;

        // Nodes are stored in normalised grid space (-0.5..0.5). Scale by arena width for world positions.
        float arenaWidth = activeArenaProfile != null ? activeArenaProfile.WorldArenaWidth : 12f;

        foreach (var path in activeGridData.splineWallPaths)
        {
            if (path?.nodes == null || path.nodes.Count < 2) continue;

            var prefab = path.prefabOverride != null ? path.prefabOverride : splineWallPrefab;
            if (prefab == null) { Debug.LogWarning("[LevelSpawner] SplineWall: no prefab assigned."); continue; }

            float spawnY = SplineWallSpawnY(prefab, spawnedBaselineWaterY, defaultContactY);

            // Destructible segments spawn this prefab instead (falls back to the normal wall prefab if unassigned).
            var   destructiblePrefab = path.destructiblePrefabOverride;
            float destructibleSpawnY = destructiblePrefab != null
                ? SplineWallSpawnY(destructiblePrefab, spawnedBaselineWaterY, defaultContactY)
                : spawnY;

            // tileSpacing is world units; convert to normalised space for WalkSpline
            float normStep = Mathf.Max(0.001f, path.tileSpacing / arenaWidth);

            WalkSpline(path.nodes, path.isClosed, path.IsSegmentCurved, path.IsSegmentGap, normStep, (pos2d, tangent, seg) =>
            {
                bool      destructible = destructiblePrefab != null && path.IsSegmentDestructible(seg);
                GameObject tilePrefab  = destructible ? destructiblePrefab : prefab;
                float      tileY        = destructible ? destructibleSpawnY : spawnY;

                float   angle = Mathf.Atan2(tangent.x, tangent.y) * Mathf.Rad2Deg;
                Vector3 pos   = new Vector3(pos2d.x * arenaWidth, tileY, pos2d.y * arenaWidth);
                Instantiate(tilePrefab, pos, Quaternion.Euler(0f, angle, 0f), spawnParent);
            });

            // Spawn node point markers at each control node
            if (splineNodePointPrefab != null)
            {
                var nodeAlign      = splineNodePointPrefab.GetComponentInChildren<PrefabBaselineAlignment>();
                float nodeContactY = nodeAlign != null ? nodeAlign.transform.localPosition.y : 0f;
                float nodeSpawnY   = spawnedBaselineWaterY - nodeContactY;

                foreach (var node in path.nodes)
                {
                    Vector3 nodePos = new Vector3(node.x * arenaWidth, nodeSpawnY, node.y * arenaWidth);
                    Instantiate(splineNodePointPrefab, nodePos, Quaternion.identity, spawnParent);
                }
            }
        }
    }

    static float SplineWallSpawnY(GameObject prefab, float baselineWaterY, float defaultContactY)
    {
        var   align = prefab.GetComponentInChildren<PrefabBaselineAlignment>();
        float cY    = align != null ? align.transform.localPosition.y : defaultContactY;
        return baselineWaterY - cY;
    }

    static void WalkSpline(List<Vector2> pts, bool closed, System.Func<int,bool> isCurvedSeg, System.Func<int,bool> isGapSeg, float spacing, System.Action<Vector2, Vector2, int> onSpawn)
    {
        int       n        = pts.Count;
        int       segCount = closed ? n : n - 1;
        const int steps    = 30;

        float   accumulated = 0f;
        float   nextSpawn   = 0f;
        bool    firstCurved = isCurvedSeg(0);
        Vector2 prev        = firstCurved ? SplineSample(pts, 0, 0f, closed) : pts[0];

        for (int seg = 0; seg < segCount; seg++)
        {
            int i2End = closed ? (seg + 1) % n : Mathf.Min(seg + 1, n - 1);

            // Gap segment: leave empty space, jump to the end node and resume spawning at the next segment.
            if (isGapSeg != null && isGapSeg(seg))
            {
                prev      = pts[i2End];
                nextSpawn = accumulated;
                continue;
            }

            bool curved      = isCurvedSeg(seg);
            int stepsThisSeg = curved ? steps : 1;
            for (int s = 1; s <= stepsThisSeg; s++)
            {
                float   lt   = (float)s / stepsThisSeg;
                int     i2   = closed ? (seg + 1) % n : Mathf.Min(seg + 1, n - 1);
                Vector2 curr = curved ? SplineSample(pts, seg, lt, closed) : Vector2.Lerp(pts[seg], pts[i2], lt);
                float   dist = Vector2.Distance(prev, curr);
                accumulated += dist;

                while (nextSpawn <= accumulated)
                {
                    float   back    = accumulated - nextSpawn;
                    float   frac    = dist > 0.0001f ? 1f - back / dist : 1f;
                    Vector2 spawnPt = Vector2.Lerp(prev, curr, frac);
                    Vector2 tangent = dist > 0.0001f ? (curr - prev).normalized : Vector2.up;
                    onSpawn(spawnPt, tangent, seg);
                    nextSpawn += spacing;
                }
                prev = curr;
            }
        }
    }

    static Vector2 SplineSample(List<Vector2> pts, int seg, float t, bool closed)
    {
        int n  = pts.Count;
        int i0 = closed ? (seg - 1 + n) % n : Mathf.Max(seg - 1, 0);
        int i1 = seg;
        int i2 = closed ? (seg + 1) % n : Mathf.Min(seg + 1, n - 1);
        int i3 = closed ? (seg + 2) % n : Mathf.Min(seg + 2, n - 1);
        return CatmullRom2D(pts[i0], pts[i1], pts[i2], pts[i3], t);
    }

    static Vector2 CatmullRom2D(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private void ProcessLinkedPairs(Dictionary<string, GameObject> spawnedByCell)
    {
        if (activeGridData.linkedPairs == null) return;

        foreach (var pair in activeGridData.linkedPairs)
        {
            string modKey = $"{pair.modifierTierIndex}_{pair.modifierCellIndex}";
            string tubeKey = $"{pair.inputTubeTierIndex}_{pair.inputTubeCellIndex}";

            if (spawnedByCell.TryGetValue(modKey, out var modGo) && 
                spawnedByCell.TryGetValue(tubeKey, out var tubeGo))
            {
                var controller = modGo.GetComponent<LevelWaveModifierControllerTypeB>();
                var tube = tubeGo.GetComponentInChildren<SoulFishInputTube>();
                var trigger = tubeGo.GetComponentInChildren<SoulEnterPipeTrigger>();
                
                if (controller != null && tube != null)
                {
                    // Tube faces the modifier; modifier faces back toward the pipe so its
                    // wave (forward-aligned via PrefabBaselineAlignment) drives toward the pipe.
                    FaceForwardTowardTarget(tubeGo, modGo.transform.position);
                    FaceForwardTowardTarget(modGo, tubeGo.transform.position);
                    tube.SetTargetModifier(controller);
                    tube.SetFishingController(fishingController);
                    if (trigger != null) controller.SetTrigger(trigger);
                    Debug.Log($"[LevelSpawner] Linked modifier at {modKey} to tube at {tubeKey} (Trigger found: {trigger != null})");
                }
else if (controller != null)
                {
                    // Fallback for old system if needed, but LevelWaveModifierControllerTypeB no longer has LinkSoulSlot
                    Debug.LogWarning($"[LevelSpawner] Could not find SoulFishInputTube on {tubeKey} to link to TypeB modifier.");
                }
            }
        }
    }

    private static int FindGroundFloorSlot(float[] offsets)
    {
        if (offsets == null || offsets.Length == 0) return 0;
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < offsets.Length; i++)
        {
            float d = Mathf.Abs(offsets[i]);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    // Overload for absolute heights — finds slot whose height is closest to the baseline Y.
    private static int FindGroundFloorSlot(float[] absoluteHeights, float baselineY)
    {
        if (absoluteHeights == null || absoluteHeights.Length == 0) return 0;
        int best = 0; float bestDist = float.MaxValue;
        for (int i = 0; i < absoluteHeights.Length; i++)
        {
            float d = Mathf.Abs(absoluteHeights[i] - baselineY);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    // Converts absolute tier heights to relative offsets so WaterLevelModifier.Init stays unchanged.
    private static float[] ToRelativeOffsets(float[] absoluteHeights, float baselineY)
    {
        var rel = new float[absoluteHeights.Length];
        for (int i = 0; i < rel.Length; i++) rel[i] = absoluteHeights[i] - baselineY;
        return rel;
    }

    // =====================================================
    // ARENA PORTAL SPAWN
    // =====================================================

    private void SpawnArenaPortals()
    {
        if (activeGridData == null) return;

        // Arena centre in world space — XZ from bounds, Y from spawn parent
        Vector3 centre = new Vector3(
            cachedArenaBounds.center.x,
            spawnParent.position.y,
            cachedArenaBounds.center.z
        );

        // Grid-to-world conversion values (mirrors SpawnMaze computation)
        float r      = activeArenaProfile != null ? activeArenaProfile.WorldArenaRadius : 20f;
        float tileX  = (r * 2f) / GridData.GridSize;
        float tileZ  = (r * 2f) / GridData.GridSize;
        Vector3 gridOrigin = new Vector3(-r, 0f, -r);

        // Pass absolute heights to SpawnPortalPrefab when available; otherwise raw TierConfig offsets (existing behaviour).
        float[] tierOffsets = absoluteTierHeights ?? tierConfig?.offsets;

        // ── Entrances ──
        if (activeGridData.entrances != null)
        {
            for (int i = 0; i < activeGridData.entrances.Count; i++)
            {
                var entrance = activeGridData.entrances[i];
                GameObject instance = SpawnPortalPrefab(
                    prefab:      activeArenaProfile?.entrancePrefabOverride ?? entrance.prefab,
                    angle:       entrance.perimeterAngle,
                    tierSlot:    entrance.tierSlot,
                    spawnRadius: entrance.spawnRadius,
                    centre:      centre,
                    tierOffsets: tierOffsets
                );
                if (instance != null)
                {
                    var exitController = instance.GetComponent<LevelExitController>();
                    if (exitController != null) exitController.portalIndex = i;

                    var doorCtrl = instance.GetComponent<LockedDoorController>();
                    if (doorCtrl != null)
                    {
                        doorCtrl.entranceID = entrance.id;
                        doorCtrl.isLocked   = entrance.isLocked;
                    }
                }

                // ── Spawn DoorLockHub ──
                if (entrance.isLocked && entrance.lockHubPrefab != null)
                {
                    GameObject hub = SpawnPortalPrefab(
                        prefab:      entrance.lockHubPrefab,
                        angle:       entrance.lockHubAngle,
                        tierSlot:    entrance.tierSlot,
                        spawnRadius: 0f,
                        centre:      centre,
                        tierOffsets: tierOffsets
                    );

                    if (hub != null && instance != null)
                    {
                        var hubCtrl  = hub.GetComponent<DoorLockHubController>();
                        var doorCtrl = instance.GetComponent<LockedDoorController>();

                        if (hubCtrl != null && doorCtrl != null)
                            hubCtrl.linkedDoor = doorCtrl;

                        // ── Spawn input tube and build spline from authored grid waypoints ──
                        if (entrance.tubePath != null && entrance.tubePath.Count >= 2 && soulFishInputTubePrefab != null)
                        {
                            // Compute Y and rotation using PrefabBaselineAlignment (same as prefabPlacements)
                            var tubeAlign      = soulFishInputTubePrefab.GetComponentInChildren<PrefabBaselineAlignment>();
                            float contactOffsetY = tubeAlign != null ? tubeAlign.transform.localPosition.y : 0f;
                            float tubeSpawnY     = spawnedBaselineWaterY - contactOffsetY;

                            Quaternion baselineRot = activeBaselineMarker != null
                                ? Quaternion.LookRotation(activeBaselineMarker.transform.forward, Vector3.up)
                                : Quaternion.identity;
                            Quaternion tubeRot = tubeAlign != null ? baselineRot : spawnParent.rotation;

                            // Spawn tube prefab at the first waypoint (input tube position)
                            var firstCell  = entrance.tubePath[0];
                            int firstIndex = firstCell.y * GridData.GridSize + firstCell.x;
                            Vector3 tubePos = CellToWorldPos(firstIndex, gridOrigin, tileX, tileZ);
                            tubePos.y = tubeSpawnY;
                            GameObject tubeGo = Instantiate(soulFishInputTubePrefab, tubePos, tubeRot, spawnParent);

                            var tube = tubeGo.GetComponent<SoulFishInputTube>();
                            if (tube != null)
                            {
                                // Waypoints: all tubePath nodes except first (tube spawn) and last (hub anchor).
                                // InitializeSystem inserts them between the local spline start and the hub pipeConnector.
                                int lastNode = entrance.tubePath.Count - 1;
                                var waypoints = new List<Vector3>(lastNode - 1);
                                for (int wi = 1; wi < lastNode; wi++)
                                {
                                    var cell      = entrance.tubePath[wi];
                                    int cellIndex = cell.y * GridData.GridSize + cell.x;
                                    Vector3 wp    = CellToWorldPos(cellIndex, gridOrigin, tileX, tileZ);
                                    waypoints.Add(wp);
                                }
                                tube.SetWaypoints(waypoints);
                                tube.SetFishingController(fishingController);
                                FaceForwardTowardTarget(tubeGo, hub.transform.position);
                                tube.SetTargetLockHub(hubCtrl);
                            }
                            else
                            {
                                Debug.LogWarning($"[LevelSpawner] soulFishInputTubePrefab has no SoulFishInputTube component — tube not connected for entrance {i}.");
                            }
                        }
                        else if (entrance.tubePath != null && entrance.tubePath.Count >= 2 && soulFishInputTubePrefab == null)
                        {
                            Debug.LogWarning($"[LevelSpawner] soulFishInputTubePrefab not assigned — cannot spawn input tube for locked entrance {i}.");
                        }
                    }
                }
            }
        }
    }

    // Returns the spawned reality-layer instance (null if no prefab). Used by SpawnArenaPortals to stamp LevelExitController.
    private GameObject SpawnPortalPrefab(GameObject prefab,
                                         float angle, int tierSlot, float spawnRadius,
                                         Vector3 centre, float[] tierOffsets)
    {
        if (prefab == null) return null;

        float y = spawnedBaselineWaterY;

        var baseline = prefab.GetComponentInChildren<PrefabBaselineAlignment>();
        if (baseline != null)
            y -= baseline.transform.position.y;

        if (tierSlot >= 0 && activeGridData.tiers != null && tierSlot < activeGridData.tiers.Count)
        {
            var tier = activeGridData.tiers[tierSlot];
            if (absoluteTierHeights != null && tier.yOffsetSlot < absoluteTierHeights.Length)
            {
                // Replace the spawnedBaselineWaterY base with this tier's absolute height
                y = absoluteTierHeights[tier.yOffsetSlot];
                var baseline2 = prefab.GetComponentInChildren<PrefabBaselineAlignment>();
                if (baseline2 != null) y -= baseline2.transform.position.y;
            }
            else
            {
                y += (tierOffsets != null && tier.yOffsetSlot < tierOffsets.Length)
                     ? tierOffsets[tier.yOffsetSlot]
                     : tier.yOffset;
            }
        }

        Vector3 pos;
        if (activeArenaProfile != null)
        {
            float r = activeArenaProfile.WorldArenaRadius;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            // spawnRadius is "Inward" offset from the discRadius perimeter
            pos = new Vector3(centre.x + dir.x * (r - spawnRadius), y, centre.z + dir.z * (r - spawnRadius));
        }
        else
        {
            pos = new Vector3(centre.x, y, centre.z);
        }

        var align = prefab.GetComponentInChildren<PrefabBaselineAlignment>();
        Quaternion rot;
        if (align != null && align.UseForwardOverride)
        {
            Vector3 fwd = align.LocalForward;
            Vector3 up  = align.UseUpOverride ? align.LocalUp : Vector3.up;
            rot = Quaternion.Euler(0f, angle, 0f) * Quaternion.LookRotation(fwd, up);
        }
        else
        {
            rot = Quaternion.Euler(0f, angle, 0f);
            if (applyMinus90XRotation) rot *= Quaternion.Euler(-90f, 0f, 0f);
        }
        return Instantiate(prefab, pos, rot, spawnParent);
    }

    // =====================================================
    // SOUL BOAT SPAWN
    // =====================================================

    // =====================================================
    // SOUL FISH SPAWN
    // =====================================================

    private void SpawnSoulFish(Vector3 origin, float tileX, float tileZ)
    {
        if (activeGridData.soulZones == null || activeGridData.soulZones.Count == 0)
        {
            Debug.Log("[LevelSpawner] SpawnSoulFish — no soulZones on GridData, skipping.");
            return;
        }

        if (soulFishContainerPrefab == null)
        {
            Debug.LogWarning("[LevelSpawner] soulFishContainerPrefab not assigned — soul fish not spawned.");
            return;
        }

        Debug.Log($"[LevelSpawner] SpawnSoulFish — processing {activeGridData.soulZones.Count} zone(s).");

        var    linkingController = FindObjectOfType<SoulFishLinkingController>();
        string levelID           = activeGridData.levelID;

        for (int zoneIndex = 0; zoneIndex < activeGridData.soulZones.Count; zoneIndex++)
        {
            var zone = activeGridData.soulZones[zoneIndex];
            zone.MigrateNodesIfNeeded();
            if (zone.nodePositions == null || zone.nodePositions.Count == 0)
            {
                Debug.Log($"[LevelSpawner]   Zone {zoneIndex} SKIPPED — no nodes.");
                continue;
            }
            if (zone.souls == null || zone.souls.Count == 0)
            {
                Debug.Log($"[LevelSpawner]   Zone {zoneIndex} SKIPPED — no souls assigned.");
                continue;
            }

            // Stamp home level on all souls in zone
            foreach (var s in zone.souls)
                if (s != null) s.homeLevelID = levelID;

            // Convert normalized node positions → world positions (pre-rotation, for instantiation)
            var nodeWorldPositions = new List<Vector3>(zone.nodePositions.Count);
            foreach (var n in zone.nodePositions)
                nodeWorldPositions.Add(NormalizedToWorldPos(n, origin, tileX, tileZ));

            // Pre-compute post-rotation positions for material mask registration.
            // Kept at water level even for tower zones — this is where fish end up after the bowl lands.
            var nodeRegPositions = new List<Vector3>(nodeWorldPositions.Count);
            foreach (var p in nodeWorldPositions)
                nodeRegPositions.Add(ComputeFinalNodePos(p));

            // Fish-bowl tower: the bowl's TRUE world position and swim radius come from the tower
            // prefab's FishBowlTowerController (bowlCenter transform + bowlRadius). Fish spawn at the
            // real bowl location — no guessed height — and are contained within the radius. The
            // container drops to the water plane (bowlWaterY) when the tower is smashed.
            float bowlWaterY = nodeWorldPositions[0].y;
            FishBowlTowerController bowlTower = null;
            float   swimRadius   = zone.radius;
            Vector3 containerPos = nodeWorldPositions[0];
            if (zone.towerGuarded)
            {
                _towersById.TryGetValue(zone.linkedStatueId, out bowlTower);
                if (bowlTower != null)
                {
                    containerPos = bowlTower.BowlCenterWorld;   // actual bowl position in the world
                    swimRadius   = bowlTower.BowlWorldRadius;
                }
                else
                {
                    Debug.LogWarning($"[LevelSpawner]   Zone {zoneIndex} is towerGuarded but no FishBowlTower with id {zone.linkedStatueId} was spawned — using fallback.");
                    containerPos = nodeWorldPositions[0] + Vector3.up * 6f;
                    swimRadius   = 2f;
                }
            }

            bool isClosedLoop = zone.closedLoop && zone.nodePositions.Count >= 3;

            // Generate spline knots — always a looping swim path
            List<Vector3> splineKnots;
            bool closedSpline = true;
            SplineAnimate.LoopMode loopMode = SplineAnimate.LoopMode.Loop;

            // Tower zones always swim a single contained disc around the real bowl centre, ignoring
            // any authored node ring (older tower zones may still carry one in their data).
            if (zone.towerGuarded)
                splineKnots = GenerateSingleNodeKnots(containerPos, swimRadius, zone.knotCount);
            else if (zone.nodePositions.Count == 1)
                splineKnots = GenerateSingleNodeKnots(nodeWorldPositions[0], swimRadius, zone.knotCount);
            else
                splineKnots = GenerateLoopingZoneKnots(nodeWorldPositions, zone.knotCount, swimRadius);

            // Spawn shoal container. For towers this is the true bowl position; for normal zones the
            // first node. No -90° X: SplineContainer must stay axis-aligned so InverseTransformPoint
            // maps world XZ → local XZ without distortion.
            GameObject containerInstance = Instantiate(
                soulFishContainerPrefab, containerPos, spawnParent.rotation, spawnParent);

            // Container-level identity label (zone-level, not registered individually)
            var containerLabel = containerInstance.GetComponent<LinkIdentityLabel>();
            if (containerLabel != null)
                containerLabel.SetLabel(zoneIndex * 100, "SoulFishZone");

            // Find SplineContainer in hierarchy
            var splineContainer = containerInstance.GetComponentInChildren<SplineContainer>(true);
            if (splineContainer == null)
            {
                Debug.LogWarning($"[LevelSpawner] No SplineContainer in soulFishContainerPrefab for zone {zoneIndex}.");
                continue;
            }

            // Inject generated knots
            InjectSplineKnots(splineContainer, splineKnots, closedSpline);

            // Configure SplineAnimate loop mode on any already-present animate components
            foreach (var sa in containerInstance.GetComponentsInChildren<SplineAnimate>(true))
                sa.Loop = loopMode;

            // Register post-rotation positions so the wave/map mask aligns with where fish actually swim
            SoulFishWaveLinker.RegisterZone(nodeRegPositions, isClosedLoop);

            // Resolve the guarding statue for a statue-guarded zone (spawned earlier this pass)
            StatueBehaviour guardStatue = null;
            if (zone.statueGuarded)
            {
                _statuesById.TryGetValue(zone.linkedStatueId, out guardStatue);
                if (guardStatue == null)
                    Debug.LogWarning($"[LevelSpawner]   Zone {zoneIndex} is statueGuarded but no statue with id {zone.linkedStatueId} was spawned.");
            }

            // Configure SoulShoalController
            var shoal = containerInstance.GetComponent<SoulShoalController>();
            if (shoal != null)
            {
                shoal.splineContainer   = splineContainer;
                shoal.fishingController = fishingController;
                shoal.SetGuardStatue(guardStatue);
                shoal.InitZone(nodeRegPositions);
                shoal.SpawnFish(activeGridData.soulZones, zoneIndex, levelID);

                // Block capture on each guarded fish until the statue is destroyed
                if (guardStatue != null)
                    foreach (var fishTransform in shoal.FishList)
                        fishTransform?.GetComponent<FishFishingBehaviour>()?.SetGuardStatue(guardStatue);

                // Fish-bowl tower: arm bowl mode (fish suppressed aloft) and hand the container to
                // its tower so a catapult smash drops it. Catchability reopens on landing.
                if (zone.towerGuarded)
                {
                    shoal.InitBowl(bowlWaterY);
                    if (bowlTower != null) bowlTower.SetContainer(shoal);
                }
            }

            // Register each spawned fish with the linking controller
            if (shoal != null)
            {
                foreach (var fishTransform in shoal.FishList)
                {
                    if (fishTransform == null) continue;

                    var fishLabel = fishTransform.GetComponent<LinkIdentityLabel>();
                    if (fishLabel == null) continue;

                    if (linkingController != null)
                        linkingController.RegisterSoulFish(fishLabel.linkID, fishTransform);
                }
            }

            Debug.Log($"[LevelSpawner] Zone {zoneIndex} spawned — {zone.nodePositions.Count} node(s), {zone.souls.Count} soul(s), closed={isClosedLoop}.");
        }
    }

    // =====================================================
    // SPLINE KNOT GENERATION HELPERS
    // =====================================================

    // Returns where a node world position will be after postSpawnPositionOffset and the Y-180 rotation.
    // Used to register soul fish zone positions with the material linkers before the transform is applied.
    private Vector3 ComputeFinalNodePos(Vector3 nodeWorldPos)
    {
        if (!applyPostSpawnY180Rotation)
            return nodeWorldPos + postSpawnPositionOffset;

        // Pivot is spawnParent's position after the offset is applied.
        // When the parent rotates, the child's local offset from the pivot is flipped on X and Z.
        // The postSpawnPositionOffset cancels in the relative calculation, leaving just the
        // original relative offset rotated 180° Y plus the new pivot.
        Vector3 pivot = spawnParent.position + postSpawnPositionOffset;
        Vector3 rel   = nodeWorldPos - spawnParent.position;
        return pivot + new Vector3(-rel.x, rel.y, -rel.z);
    }

    private Vector3 CellToWorldPos(int cellIndex, Vector3 origin, float tileX, float tileZ)
    {
        int cellX    = cellIndex % GridData.GridSize;
        int cellY    = cellIndex / GridData.GridSize;
        int flippedY = GridData.GridSize - 1 - cellY;
        return new Vector3(
            origin.x + cellX    * tileX + tileX * 0.5f,
            spawnParent.position.y,
            origin.z + flippedY * tileZ + tileZ * 0.5f);
    }

    private List<Vector3> GenerateSingleNodeKnots(Vector3 center, float radius, int count)
    {
        var knots = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            Vector2 circle = UnityEngine.Random.insideUnitCircle * radius;
            knots.Add(new Vector3(center.x + circle.x, center.y, center.z + circle.y));
        }
        return knots;
    }

    // Normalized grid coord (-0.5..0.5 from centre) -> world position, in the same frame as
    // CellToWorldPos so migrated soul-zone nodes land exactly where their old cells were.
    private Vector3 NormalizedToWorldPos(Vector2 norm, Vector3 origin, float tileX, float tileZ)
    {
        float colF = (norm.x + 0.5f) * GridData.GridSize;
        float rowF = (0.5f - norm.y) * GridData.GridSize;
        return new Vector3(
            origin.x + colF * tileX,
            spawnParent.position.y,
            origin.z + (GridData.GridSize - rowF) * tileZ);
    }

    // Distributes knots evenly around the node loop, then scatters each within radius on XZ.
    // Used for both open-path and explicit closed-loop designer zones — all movement is circular.
    private List<Vector3> GenerateLoopingZoneKnots(List<Vector3> nodes, int count, float scatter)
    {
        var baseKnots = DistributeAlongPath(nodes, count, true);
        var result    = new List<Vector3>(count);
        foreach (var pt in baseKnots)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * scatter;
            result.Add(new Vector3(pt.x + offset.x, pt.y, pt.z + offset.y));
        }
        return result;
    }

    private List<Vector3> DistributeAlongPath(List<Vector3> path, int count, bool wrapToClose)
    {
        if (path.Count == 0 || count == 0) return new List<Vector3>();
        if (path.Count == 1)
        {
            var single = new List<Vector3>(count);
            for (int i = 0; i < count; i++) single.Add(path[0]);
            return single;
        }

        // Build segment list
        var segments = new List<(Vector3 a, Vector3 b, float len)>();
        float totalLength = 0f;
        for (int i = 0; i < path.Count - 1; i++)
        {
            float len = Vector3.Distance(path[i], path[i + 1]);
            segments.Add((path[i], path[i + 1], len));
            totalLength += len;
        }
        if (wrapToClose)
        {
            float len = Vector3.Distance(path[path.Count - 1], path[0]);
            segments.Add((path[path.Count - 1], path[0], len));
            totalLength += len;
        }

        float spacing = totalLength / count;
        var result = new List<Vector3>(count);

        for (int i = 0; i < count; i++)
        {
            float target      = i * spacing;
            float accumulated = 0f;
            Vector3 point     = path[0];

            foreach (var seg in segments)
            {
                if (accumulated + seg.len >= target || Mathf.Approximately(accumulated + seg.len, target))
                {
                    float t = seg.len > 0f ? (target - accumulated) / seg.len : 0f;
                    point = Vector3.Lerp(seg.a, seg.b, Mathf.Clamp01(t));
                    break;
                }
                accumulated += seg.len;
            }

            result.Add(point);
        }

        return result;
    }

    private void InjectSplineKnots(SplineContainer container, List<Vector3> worldPositions, bool closed)
    {
        var spline = container.Spline;
        spline.Clear();

        foreach (var worldPos in worldPositions)
        {
            float3 localPos = container.transform.InverseTransformPoint(worldPos);
            spline.Add(new BezierKnot(localPos), TangentMode.AutoSmooth);
        }

        spline.Closed = closed;
    }

    // =====================================================
    // HELPERS
    // =====================================================

    public void RevealMaze()
    {
        spawnParent.gameObject.SetActive(true);
        StartCoroutine(MoveMaze());
    }

    public void RevealMazeInstant()
    {
        StopAllCoroutines();
        spawnParent.gameObject.SetActive(true);
        Vector3 p = spawnParent.position;
        p.y = mazeEndY;
        spawnParent.position = p;
    }

    public void SetLevelIntroPos()
    {
        if (!spawnParent) return;
        StopAllCoroutines();
        Vector3 p = spawnParent.position;
        p.y = mazeStartY;
        spawnParent.position = p;
        spawnParent.gameObject.SetActive(false);
    }

    IEnumerator MoveMaze()
    {
        spawnParent.gameObject.SetActive(true);
        if (mazeSound) mazeSound.Play();

        float t = 0f;
        Vector3 pos = spawnParent.position;
        pos.y = mazeStartY;
        spawnParent.position = pos;

        while (t < mazeMoveDuration)
        {
            t    += Time.deltaTime;
            pos.y = Mathf.Lerp(mazeStartY, mazeEndY, t / mazeMoveDuration);
            spawnParent.position = pos;
            yield return null;
        }

        pos.y = mazeEndY;
        spawnParent.position = pos;
    }

    // Rotates a spawned instance so its PrefabBaselineAlignment.LocalForward faces the target (XZ only).
    // No-ops if the object has no PrefabBaselineAlignment or UseForwardOverride is false.
    // For the input tube this must be called BEFORE SetTargetModifier / SetTargetLockHub so the pipe
    // is built from the correct orientation.
    private void FaceForwardTowardTarget(GameObject go, Vector3 targetWorldPos)
    {
        var align = go.GetComponentInChildren<PrefabBaselineAlignment>();
        if (align == null || !align.UseForwardOverride) return;

        Vector3 toTarget = targetWorldPos - go.transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f) return;

        Vector3 currentWorldFwd = go.transform.rotation * align.LocalForward;
        currentWorldFwd.y = 0f;
        if (currentWorldFwd.sqrMagnitude < 0.001f) return;

        Quaternion delta = Quaternion.FromToRotation(currentWorldFwd.normalized, toTarget.normalized);
        go.transform.rotation = delta * go.transform.rotation;
    }

    public GameObject SpawnConditionals(int visitCount)
    {
        if (activeGridData == null) return null;

#if UNITY_EDITOR
        if (ForceDisableSnake) return null;
#endif

        EnemyProfile enemy = activeGridData.enemyProfile;
        if (enemy != null && enemy.prefab != null && visitCount >= enemy.spawnOnVisit)
        {
            GameObject spawned = Instantiate(enemy.prefab, Vector3.zero, Quaternion.identity);
            Debug.Log($"Enemy spawned on visit {visitCount}.");
            return spawned;
        }

        return null;
    }
}
