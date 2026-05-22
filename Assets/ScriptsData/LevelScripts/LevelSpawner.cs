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

    [Header("Fishing")]
    [SerializeField] FishingController fishingController;

    [Header("FX")]
    [SerializeField] private WhirlFXController whirlFX;
    [SerializeField] private WhirlpoolManager  whirlpoolManager;

    public bool mazeSpawned;
    bool mazeRotated;

    private GridData activeGridData;
    private ArenaProfile activeArenaProfile;
    public ArenaProfile GetArenaProfile() => activeArenaProfile;
    private Bounds cachedArenaBounds;
    public Bounds GetArenaBounds() => cachedArenaBounds;

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

        if (activeArenaProfile?.arenaSizeReferencePlane != null)
        {
            GameObject refObj  = Instantiate(activeArenaProfile.arenaSizeReferencePlane);
            referencePlane     = refObj.GetComponent<Renderer>();
            referencePlane.enabled = false;

        }

    }

    public void SetGridData(GridData data) => activeGridData = data;

    // =====================================================
    // SPAWN
    // =====================================================

    public void SpawnMaze()
    {
        if (mazeSpawned) return;
        if (!activeGridData || !referencePlane || !spawnParent) return;

        if (activeGridData.orbCellIndices == null)
            activeGridData.orbCellIndices = new List<int>();

        Bounds  b      = referencePlane.bounds;
        cachedArenaBounds = b;
        float   tileX  = b.size.x / GridData.GridSize;
        float   tileZ  = b.size.z / GridData.GridSize;
        Vector3 origin = b.min;

        // ── Reality Layer — orbs / water / wave modifiers ──
        for (int y = 0; y < GridData.GridSize; y++)
        {
            int flippedY = GridData.GridSize - 1 - y;

            for (int x = 0; x < GridData.GridSize; x++)
            {
                int index = flippedY * GridData.GridSize + x;

                Vector3 pos = new Vector3(
                    origin.x + x * tileX + tileX * 0.5f,
                    spawnParent.position.y,
                    origin.z + y * tileZ + tileZ * 0.5f
                );

                Quaternion rot = spawnParent.rotation;
                if (applyMinus90XRotation) rot *= Quaternion.Euler(-90f, 0f, 0f);

                if (activeGridData.orbCellIndices.Contains(index) && orbPrefab)
                    Instantiate(orbPrefab, pos, rot, spawnParent);

                if (activeGridData.waterLevelModifierCellIndices != null &&
                    activeGridData.waterLevelModifierCellIndices.Contains(index) && waterLevelModifierPrefab)
                    Instantiate(waterLevelModifierPrefab, pos, Quaternion.identity, spawnParent);

                if (activeGridData.waveModifierCellIndices != null &&
                    activeGridData.waveModifierCellIndices.Contains(index) && waveModifierPrefab)
                    Instantiate(waveModifierPrefab, pos, Quaternion.identity, spawnParent);
            }
        }

        // ── Arena Portals (Entrances & Exits) ──
        SpawnArenaPortals();

        // ── Direct Prefab Placements (base layer) ──
        var   baselineMarker   = activeArenaProfile?.outerWallsPrefab?.GetComponentInChildren<BaselineMarker>();
        float baselineY        = baselineMarker?.height ?? spawnParent.position.y;
        Quaternion baselineRot = baselineMarker != null
            ? Quaternion.LookRotation(baselineMarker.transform.forward, Vector3.up)
            : Quaternion.identity;

        if (activeGridData.prefabPlacements != null)
        {
            foreach (var pp in activeGridData.prefabPlacements)
            {
                if (pp.prefab == null) continue;
                int cellX    = pp.cellIndex % GridData.GridSize;
                int cellY    = pp.cellIndex / GridData.GridSize;
                int flippedY = GridData.GridSize - 1 - cellY;
                Transform par = (pp.isCircle && soulSpawnParent) ? soulSpawnParent : spawnParent;
                float spawnY  = pp.isWorldSpaceProp ? baselineY : par.position.y;
                Vector3 pos   = new Vector3(
                    origin.x + cellX    * tileX + tileX * 0.5f,
                    spawnY,
                    origin.z + flippedY * tileZ + tileZ * 0.5f
                );
                if (pp.isWorldSpaceProp)
                {
                    // Apply the same XZ flip that spawnParent will receive post-spawn
                    Vector3 worldPos = pos;
                    if (applyPostSpawnY180Rotation)
                    {
                        float cx = spawnParent.position.x + postSpawnPositionOffset.x;
                        float cz = spawnParent.position.z + postSpawnPositionOffset.z;
                        worldPos.x = 2f * cx - worldPos.x;
                        worldPos.z = 2f * cz - worldPos.z;
                    }
                    else
                    {
                        worldPos.x += postSpawnPositionOffset.x;
                        worldPos.z += postSpawnPositionOffset.z;
                    }
                    bool align = pp.prefab.GetComponentInChildren<PrefabBaselineAlignment>() != null;
                    Instantiate(pp.prefab, worldPos, align ? baselineRot : Quaternion.identity, null);
                }
                else
                {
                    Quaternion rot = par.rotation;
                    if (applyMinus90XRotation) rot *= Quaternion.Euler(-90f, 0f, 0f);
                    Instantiate(pp.prefab, pos, rot, par);
                }
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
                            spawnParent.position.y + yOff,
                            origin.z + y * tileZ + tileZ * 0.5f
                        );
                        Quaternion rot = spawnParent.rotation;
                        if (applyMinus90XRotation) rot *= Quaternion.Euler(-90f, 0f, 0f);

                        if (tier.waterLevelModifierCellIndices != null &&
                            tier.waterLevelModifierCellIndices.Contains(index) && waterLevelModifierPrefab)
                        {
                            var go = Instantiate(waterLevelModifierPrefab, pos, Quaternion.identity, spawnParent);
                            go.GetComponent<WaterLevelModifier>()?.Init(tier.yOffsetSlot, offsets, tier.name);
                        }

                        if (tier.waveModifierCellIndices != null &&
                            tier.waveModifierCellIndices.Contains(index) && waveModifierPrefab)
                            Instantiate(waveModifierPrefab, pos, Quaternion.identity, spawnParent);
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
                        Vector3 pos2 = new Vector3(
                            origin.x + cellX    * tileX + tileX * 0.5f,
                            spawnParent.position.y + yOff,
                            origin.z + flippedY * tileZ + tileZ * 0.5f
                        );
                        Quaternion rot2 = spawnParent.rotation;
                        if (applyMinus90XRotation) rot2 *= Quaternion.Euler(-90f, 0f, 0f);
                        Instantiate(pp.prefab, pos2, rot2, spawnParent);
                    }
                }
            }
        }

        // ── Soul Fish — spawned before offset/rotation so they move with spawnParent ──
        SpawnSoulFish(origin, tileX, tileZ);

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
                go.transform.position   = new Vector3(wx, spawnParent.position.y, wz);
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

        mazeSpawned = true;
        Debug.Log("MazeSpawned = true");
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
                    soulPrefab:  entrance.soulPrefab,
                    angle:       entrance.perimeterAngle,
                    tierSlot:    entrance.tierSlot,
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
    private GameObject SpawnPortalPrefab(GameObject prefab, GameObject soulPrefab,
                                         float angle, int tierSlot,
                                         Vector3 centre, float[] tierOffsets)
    {
        GameObject realityInstance = null;

        // Reality layer
        if (prefab != null)
        {
            float y = spawnParent.position.y;
            if (tierSlot >= 0 && activeGridData.tiers != null && tierSlot < activeGridData.tiers.Count)
            {
                var tier = activeGridData.tiers[tierSlot];
                y += (tierOffsets != null && tier.yOffsetSlot < tierOffsets.Length)
                     ? tierOffsets[tier.yOffsetSlot]
                     : tier.yOffset;
            }

            Vector3    pos = new Vector3(centre.x, y, centre.z);
            Quaternion rot = Quaternion.Euler(0f, angle, 0f);
            if (applyMinus90XRotation) rot *= Quaternion.Euler(-90f, 0f, 0f);
            realityInstance = Instantiate(prefab, pos, rot, spawnParent);
        }

        // Soul plane layer
        if (soulPrefab != null && soulSpawnParent != null)
        {
            float y = soulSpawnParent.position.y;
            if (tierSlot >= 0 && activeGridData.tiers != null && tierSlot < activeGridData.tiers.Count)
            {
                var tier = activeGridData.tiers[tierSlot];
                y += (tierOffsets != null && tier.yOffsetSlot < tierOffsets.Length)
                     ? tierOffsets[tier.yOffsetSlot]
                     : tier.yOffset;
            }

            Vector3    soulPos = new Vector3(centre.x, y, centre.z);
            Quaternion soulRot = Quaternion.Euler(0f, angle, 0f);
            if (applyMinus90XRotation) soulRot *= Quaternion.Euler(-90f, 0f, 0f);
            Instantiate(soulPrefab, soulPos, soulRot, soulSpawnParent);
        }

        return realityInstance;
    }

    // =====================================================
    // SOUL BOAT SPAWN
    // =====================================================

    // =====================================================
    // SOUL FISH SPAWN
    // =====================================================

    private void SpawnSoulFish(Vector3 origin, float tileX, float tileZ)
    {
        if (activeGridData.soulZones == null || activeGridData.soulZones.Count == 0) return;

        if (soulFishContainerPrefab == null)
        {
            Debug.LogWarning("[LevelSpawner] soulFishContainerPrefab not assigned — soul fish not spawned.");
            return;
        }

        var    linkingController = FindObjectOfType<SoulFishLinkingController>();
        string levelID           = activeGridData.levelID;

        for (int zoneIndex = 0; zoneIndex < activeGridData.soulZones.Count; zoneIndex++)
        {
            var zone = activeGridData.soulZones[zoneIndex];
            if (zone.nodes == null || zone.nodes.Count == 0) continue;
            if (zone.souls == null || zone.souls.Count == 0) continue;

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
