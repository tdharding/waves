using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class LevelSpawner : MonoBehaviour
{
#if UNITY_EDITOR
    // Set by SaveDataEditorWindow. Editor/playtest only — has no effect in builds.
    public static bool ForceDisableSnake = false;
#endif

    [Header("Soul → Reality Proxy Prefab")]
    [SerializeField] GameObject soulToRealityProxyPrefab;

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

    [Header("Soul Boat")]
    [Tooltip("The SoulBoat prefab spawned at the chosen entrance. " +
             "Distinct from ArenaEntrance.soulPrefab (that is the soul-plane door).")]
    [SerializeField] private GameObject soulBoatPrefab;

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

        EnsurePrefabSlotsFromGrid();

        Bounds  b      = referencePlane.bounds;
        cachedArenaBounds = b;
        float   tileX  = b.size.x / GridData.GridSize;
        float   tileZ  = b.size.z / GridData.GridSize;
        Vector3 origin = b.min;

        List<GameObject> prefabs = activeGridData.prefabs ?? new List<GameObject>();

        // ── Reality Layer ──
        for (int y = 0; y < GridData.GridSize; y++)
        {
            int flippedY = GridData.GridSize - 1 - y;

            for (int x = 0; x < GridData.GridSize; x++)
            {
                int index = flippedY * GridData.GridSize + x;
                int cell  = activeGridData.cells[index];

                Vector3 pos = new Vector3(
                    origin.x + x * tileX + tileX * 0.5f,
                    spawnParent.position.y,
                    origin.z + y * tileZ + tileZ * 0.5f
                );

                Quaternion rot = spawnParent.rotation;
                if (applyMinus90XRotation) rot *= Quaternion.Euler(-90f, 0f, 0f);

                if (cell > 0 && cell <= prefabs.Count && prefabs[cell - 1] != null)
                    Instantiate(prefabs[cell - 1], pos, rot, spawnParent);

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

        // ── Soul Plane — overlay objects (non-soul) + start/finish ──
        if (activeGridData.overlayCells != null && soulSpawnParent)
        {
            for (int y = 0; y < GridData.GridSize; y++)
            {
                int flippedY = GridData.GridSize - 1 - y;

                for (int x = 0; x < GridData.GridSize; x++)
                {
                    int index = flippedY * GridData.GridSize + x;
                    int cell  = activeGridData.overlayCells[index];

                    Vector3 soulPos = new Vector3(
                        origin.x + x * tileX + tileX * 0.5f,
                        soulSpawnParent.position.y,
                        origin.z + y * tileZ + tileZ * 0.5f
                    );

                    Quaternion soulRot = soulSpawnParent.rotation;
                    if (applyMinus90XRotation) soulRot *= Quaternion.Euler(-90f, 0f, 0f);

                    // Non-soul overlay objects
                    if (cell > 0 && cell <= prefabs.Count && prefabs[cell - 1] != null)
                        Instantiate(prefabs[cell - 1], soulPos, soulRot, soulSpawnParent);
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
                Vector3 pos   = new Vector3(
                    origin.x + cellX    * tileX + tileX * 0.5f,
                    par.position.y,
                    origin.z + flippedY * tileZ + tileZ * 0.5f
                );
                Quaternion rot = par.rotation;
                if (applyMinus90XRotation) rot *= Quaternion.Euler(-90f, 0f, 0f);
                Instantiate(pp.prefab, pos, rot, par);
            }
        }

        // ── Extra Tiers ──
        if (activeGridData.tiers != null)
        {
            for (int ti = 0; ti < activeGridData.tiers.Count; ti++)
            {
                var tier = activeGridData.tiers[ti];
                if (tier.cells == null) continue;
                float[] offsets = tierConfig?.offsets;
                float yOff = (offsets != null && tier.yOffsetSlot < offsets.Length)
                    ? offsets[tier.yOffsetSlot] : tier.yOffset;

                for (int y = 0; y < GridData.GridSize; y++)
                {
                    int flippedY = GridData.GridSize - 1 - y;
                    for (int x = 0; x < GridData.GridSize; x++)
                    {
                        int index = flippedY * GridData.GridSize + x;
                        int cell  = tier.cells[index];
                        Vector3 pos = new Vector3(
                            origin.x + x * tileX + tileX * 0.5f,
                            spawnParent.position.y + yOff,
                            origin.z + y * tileZ + tileZ * 0.5f
                        );
                        Quaternion rot = spawnParent.rotation;
                        if (applyMinus90XRotation) rot *= Quaternion.Euler(-90f, 0f, 0f);

                        if (cell > 0 && cell <= prefabs.Count && prefabs[cell - 1] != null)
                            Instantiate(prefabs[cell - 1], pos, rot, spawnParent);

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

        // ── Soul Fish — explicit spawn points ──
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

    /// <summary>
    /// Spawns the SoulBoat at the given world position, wires fishing and whirl FX,
    /// and returns the spawned instance so LevelDataController can distribute its Transform.
    /// </summary>
    public GameObject SpawnSoulBoat(Vector3 realityPos, float facingAngle)
    {
        if (soulBoatPrefab == null)
        {
            Debug.LogWarning("[LevelSpawner] No soulBoatPrefab assigned — SoulBoat not spawned.");
            return null;
        }
        if (soulSpawnParent == null)
        {
            Debug.LogWarning("[LevelSpawner] No soulSpawnParent assigned — SoulBoat not spawned.");
            return null;
        }

        // Mirror the same rotation convention used for all soul-plane objects in SpawnMaze:
        // start from the soul parent's current world rotation (includes post-spawn Y180),
        // add the facing angle, then apply the -90° X correction if the scene uses it.
        Vector3    soulPos = new Vector3(realityPos.x, soulSpawnParent.position.y, realityPos.z);
        Quaternion soulRot = soulSpawnParent.rotation * Quaternion.Euler(0f, facingAngle, 0f);
        if (applyMinus90XRotation) soulRot *= Quaternion.Euler(-90f, 0f, 0f);

        GameObject soulBoat = Instantiate(soulBoatPrefab, soulPos, soulRot, soulSpawnParent);

        if (fishingController != null)
        {
            fishingController.dummyBoatTarget = soulBoat.transform;
            fishingController.SetWhirlFX(whirlFX);
        }

        if (whirlFX != null)
        {
            var animator = soulBoat.GetComponentInChildren<Animator>(true);
            whirlFX.SetNetAnimator(animator);
            whirlFX.SetFishingController(fishingController);
            soulBoat.GetComponentInChildren<NetAnimationEventReceiver>(true)?.SetWhirlFX(whirlFX);
            whirlFX.SetTargetRenderers(soulBoat.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        }

        return soulBoat;
    }

    // =====================================================
    // SOUL FISH SPAWN
    // =====================================================

    private void SpawnSoulFish(Vector3 origin, float tileX, float tileZ)
    {
        if (activeGridData.soulSpawnPoints == null || activeGridData.soulSpawnPoints.Count == 0) return;

        var    linkingController = FindObjectOfType<SoulFishLinkingController>();
        string levelID           = activeGridData.levelID;

        for (int i = 0; i < activeGridData.soulSpawnPoints.Count; i++)
        {
            var spawnPoint = activeGridData.soulSpawnPoints[i];
            int cellIndex  = spawnPoint.cellIndex;

            // Skip if already caught
            if (GameProgressData.IsSoulCaught(levelID, cellIndex))
            {
                Debug.Log($"[LevelSpawner] Soul at cell {cellIndex} already caught — skipping.");
                continue;
            }

            // Soul data comes directly from the spawn point
            SoulData soul = spawnPoint.soulData;
            if (soul == null)
            {
                Debug.LogWarning($"[LevelSpawner] No SoulData assigned to spawn point {i} (cell {cellIndex}) " +
                                 $"in '{levelID}'. Assign one in the Grid Designer.");
                continue;
            }

            // Stamp home level at runtime
            soul.homeLevelID = levelID;

            int identityToAssign = soul.soulDataIdentity;

            // Resolve fish prefab from the soul
            GameObject fishPrefab = soul.fishPrefab;
            if (fishPrefab == null)
            {
                Debug.LogWarning($"[LevelSpawner] SoulData '{soul.name}' (identity {identityToAssign}) has no fishPrefab assigned.");
                continue;
            }

            // Cell index → world position
            int cellX    = cellIndex % GridData.GridSize;
            int cellY    = cellIndex / GridData.GridSize;
            int flippedY = GridData.GridSize - 1 - cellY;

            Vector3 soulPos = new Vector3(
                origin.x + cellX    * tileX + tileX * 0.5f,
                soulSpawnParent.position.y,
                origin.z + flippedY * tileZ + tileZ * 0.5f
            );

            Quaternion soulRot = soulSpawnParent.rotation;
            if (applyMinus90XRotation) soulRot *= Quaternion.Euler(-90f, 0f, 0f);

            GameObject soulInstance = Instantiate(fishPrefab, soulPos, soulRot, soulSpawnParent);

            // Stamp passport
            var fishLabel = soulInstance.GetComponent<LinkIdentityLabel>();
            if (fishLabel != null)
            {
                fishLabel.SetLabel(cellIndex, "SoulFish");
                fishLabel.soulDataIdentity = identityToAssign;

                if (linkingController != null)
                    linkingController.RegisterSoulFish(fishLabel.linkID, soulInstance.transform);
            }

            // Connect fishing behaviour + spawn reality proxy
            var fishes = soulInstance.GetComponentsInChildren<FishFishingBehaviour>(true);
            foreach (var fish in fishes)
            {
                fish.fishing = fishingController;

                if (soulToRealityProxyPrefab)
                {
                    Vector3    realityPos = new Vector3(fish.transform.position.x, spawnParent.position.y, fish.transform.position.z);
                    Quaternion realityRot = spawnParent.rotation;
                    if (applyMinus90XRotation) realityRot *= Quaternion.Euler(-90f, 0f, 0f);

                    GameObject proxy      = Instantiate(soulToRealityProxyPrefab, realityPos, realityRot, spawnParent);
                    var        proxyLabel = proxy.GetComponent<LinkIdentityLabel>();

                    if (proxyLabel != null)
                    {
                        proxyLabel.SetLabel(cellIndex, "RealityProxy");
                        proxyLabel.soulDataIdentity = identityToAssign;

                        var follow = proxy.GetComponent<SoulFishRealityProxyFollow>();
                        if (follow != null && linkingController != null)
                            linkingController.RegisterRealityProxy(proxyLabel.linkID, follow);
                    }
                }
            }

            Debug.Log($"[LevelSpawner] Soul fish spawned — cell {cellIndex}, identity {identityToAssign}.");
        }
    }

    // =====================================================
    // HELPERS
    // =====================================================

    void EnsurePrefabSlotsFromGrid()
    {
        if (activeGridData == null) return;
        int required = 0;
        foreach (int c in activeGridData.cells) required = Mathf.Max(required, c);
        if (activeGridData.overlayCells != null)
            foreach (int c in activeGridData.overlayCells) required = Mathf.Max(required, c);
        var prefabs = activeGridData.prefabs;
        if (prefabs == null) { activeGridData.prefabs = new List<GameObject>(); prefabs = activeGridData.prefabs; }
        while (prefabs.Count < required) prefabs.Add(null);
    }

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
