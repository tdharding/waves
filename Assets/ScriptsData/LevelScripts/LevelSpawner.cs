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
                    float[] offsets0 = tierConfig?.offsets;
                    go.GetComponent<WaterLevelModifier>()?.Init(FindGroundFloorSlot(offsets0), offsets0, "G", spawnedBaselineWaterY);
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
                float contactOffsetY  = baselineAlign != null ? baselineAlign.transform.position.y : 0f;
                float baselineSpawnY  = spawnedBaselineWaterY - contactOffsetY;
                float spawnY          = (pp.isWorldSpaceProp || hasBaselineAlign) ? baselineSpawnY : par.position.y;
                Vector3 pos   = new Vector3(
                    origin.x + cellX    * tileX + tileX * 0.5f,
                    spawnY,
                    origin.z + flippedY * tileZ + tileZ * 0.5f
                );
                Quaternion rot = hasBaselineAlign ? baselineRot : par.rotation;
                var instance = Instantiate(pp.prefab, pos, rot, par);
                spawnedByCell[$"-1_{pp.cellIndex}"] = instance;
                InitializeWaveModifier(instance);
            }
        }

        // ── Extra Tiers ──
        if (activeGridData.tiers != null)
        {
            for (int ti = 0; ti < activeGridData.tiers.Count; ti++)
            {
                var tier = activeGridData.tiers[ti];
                float[] offsets = tierConfig?.offsets;
                float yOff = (offsets != null && tier.yOffsetSlot < offsets.Length)
                    ? offsets[tier.yOffsetSlot] : tier.yOffset;

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
                            go.GetComponent<WaterLevelModifier>()?.Init(tier.yOffsetSlot, offsets, tier.name, spawnedBaselineWaterY);
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
                        float tierContactY  = tierAlign != null ? tierAlign.transform.position.y : 0f;
                        Vector3 pos2 = new Vector3(
                            origin.x + cellX    * tileX + tileX * 0.5f,
                            spawnedBaselineWaterY + yOff - tierContactY,
                            origin.z + flippedY * tileZ + tileZ * 0.5f
                        );
                        Quaternion rot2 = spawnParent.rotation;
                        if (applyMinus90XRotation) rot2 *= Quaternion.Euler(-90f, 0f, 0f);
                        var instance = Instantiate(pp.prefab, pos2, rot2, spawnParent);
                        spawnedByCell[$"{ti}_{pp.cellIndex}"] = instance;
                        InitializeWaveModifier(instance);
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

            var align    = prefab.GetComponentInChildren<PrefabBaselineAlignment>();
            float cY     = align != null ? align.transform.localPosition.y : defaultContactY;
            float spawnY = spawnedBaselineWaterY - cY;

            // tileSpacing is world units; convert to normalised space for WalkSpline
            float normStep = Mathf.Max(0.001f, path.tileSpacing / arenaWidth);

            WalkSpline(path.nodes, path.isClosed, path.IsSegmentCurved, normStep, (pos2d, tangent) =>
            {
                float   angle = Mathf.Atan2(tangent.x, tangent.y) * Mathf.Rad2Deg;
                Vector3 pos   = new Vector3(pos2d.x * arenaWidth, spawnY, pos2d.y * arenaWidth);
                Instantiate(prefab, pos, Quaternion.Euler(0f, angle, 0f), spawnParent);
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

    static void WalkSpline(List<Vector2> pts, bool closed, System.Func<int,bool> isCurvedSeg, float spacing, System.Action<Vector2, Vector2> onSpawn)
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
                    onSpawn(spawnPt, tangent);
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

        float[] tierOffsets = tierConfig?.offsets;

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
                    var controller = instance.GetComponent<LevelExitController>();
                    if (controller != null) controller.portalIndex = i;
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
            y += (tierOffsets != null && tier.yOffsetSlot < tierOffsets.Length)
                 ? tierOffsets[tier.yOffsetSlot]
                 : tier.yOffset;
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

        Quaternion rot = Quaternion.Euler(0f, angle, 0f);
if (applyMinus90XRotation) rot *= Quaternion.Euler(-90f, 0f, 0f);
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
            if (zone.nodes == null || zone.nodes.Count == 0)
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

            // Convert node cell indices → world positions (pre-rotation, for instantiation)
            var nodeWorldPositions = new List<Vector3>(zone.nodes.Count);
            foreach (int nodeCell in zone.nodes)
                nodeWorldPositions.Add(CellToWorldPos(nodeCell, origin, tileX, tileZ));

            // Pre-compute post-rotation positions for material mask registration
            var nodeRegPositions = new List<Vector3>(nodeWorldPositions.Count);
            foreach (var p in nodeWorldPositions)
                nodeRegPositions.Add(ComputeFinalNodePos(p));

            // Detect closed loop: 3+ nodes and last == first
            bool isClosedLoop = zone.nodes.Count >= 3
                             && zone.nodes[zone.nodes.Count - 1] == zone.nodes[0];

            // Generate spline knots
            List<Vector3> splineKnots;
            bool closedSpline;
            SplineAnimate.LoopMode loopMode;

            if (zone.nodes.Count == 1)
            {
                splineKnots  = GenerateSingleNodeKnots(nodeWorldPositions[0], zone.radius, zone.knotCount);
                closedSpline = true;
                loopMode     = SplineAnimate.LoopMode.Loop;
            }
            else if (isClosedLoop)
            {
                var loopNodes = new List<Vector3>(nodeWorldPositions);
                loopNodes.RemoveAt(loopNodes.Count - 1); // strip duplicate last node
                splineKnots  = GenerateLoopingZoneKnots(loopNodes, zone.knotCount, zone.radius);
                closedSpline = true;
                loopMode     = SplineAnimate.LoopMode.Loop;
            }
            else
            {
                splineKnots  = GenerateLoopingZoneKnots(nodeWorldPositions, zone.knotCount, zone.radius);
                closedSpline = true;
                loopMode     = SplineAnimate.LoopMode.Loop;
            }

            // Spawn shoal container at first node — reality layer.
            // No -90° X: SplineContainer must stay axis-aligned so
            // InverseTransformPoint maps world XZ → local XZ without distortion.
            GameObject containerInstance = Instantiate(
                soulFishContainerPrefab, nodeWorldPositions[0], spawnParent.rotation, spawnParent);

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

            // Configure SoulShoalController
            var shoal = containerInstance.GetComponent<SoulShoalController>();
            if (shoal != null)
            {
                shoal.splineContainer   = splineContainer;
                shoal.fishingController = fishingController;
                shoal.InitZone(nodeRegPositions);
                shoal.SpawnFish(activeGridData.soulZones, zoneIndex, levelID);
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

            Debug.Log($"[LevelSpawner] Zone {zoneIndex} spawned — {zone.nodes.Count} node(s), {zone.souls.Count} soul(s), closed={isClosedLoop}.");
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
