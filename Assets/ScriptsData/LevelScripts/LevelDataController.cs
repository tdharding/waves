using UnityEngine;
using System.Collections;
using Unity.Cinemachine;

public class LevelDataController : MonoBehaviour
{
    public static LevelDataController Instance;

    [Header("Level")]
    [SerializeField] private LevelSpawner levelSpawner;
    [SerializeField] private GridData fallbackGridData;

    [Header("Intro / Ritual")]
    [SerializeField] private LevelStartRitual startRitual;

    [Header("Wave System")]
    [SerializeField] private WaveMaterialController waveController;
    [SerializeField] private GameObject wavePlaneObject;
    [SerializeField] private Transform  sonarGridParent;

    [Header("Gameplay Camera")]
    [SerializeField] private GameObject gameplayFollowCamera;
    [SerializeField] private BoatCameraZoom zoomController;

    [SerializeField] private CameraProfile levelCameraProfile;

    [Header("Gameplay Boat")]
    [SerializeField] private GameObject gameplayBoat;

    [Header("Gameplay Systems")]
    [SerializeField] private BoatMovement boatMovement;
    [SerializeField] private BoatVisual boatVisual;
    [SerializeField] private float gameplayExtraYOffset = 0.05f;

    [Header("UI")]
    [SerializeField] private UIOverlayCameraController uiOverlay;
    [SerializeField] private SteeringWheelControllerV2 steeringWheel;
    [SerializeField] private GameObject mapUI;
    [SerializeField] private GameObject skipInstructionUI;

    [Header("Map")]
    [SerializeField] private UIMapController mapPointer;
    [SerializeField] private SoulFishWaveLinker soulFishWaveLinker;
    [SerializeField] private SoulFishMapLinker soulFishMapLinker;

    [Header("Sonar")]
    [SerializeField] private SonarController sonarController;

    [Header("Enemies")]
    private GameObject spawnedSnake;

    [Header("Snake")]
    [SerializeField] private SnakeController snakeController;

    [Header("Time Trial")]
    [SerializeField] private TimeTrialTimer timeTrialTimer;
    [SerializeField] private GongWavesController gongWavesController;

    [Header("Fishing")]
    [SerializeField] private FishingController fishingController;
    public FishingController FishingController => fishingController;
    [SerializeField] private bool enableVideoPlayback = true;
    public bool EnableVideoPlayback => enableVideoPlayback;

    [Header("Debug")]
    [SerializeField] private bool showArenaBoundsDebug = false;

    private bool isTimeTrial;
    private float timeLimitSeconds;
    private GridData activeGridData;
    private bool gameplayStarted;

    public void ShowSkipInstruction() => skipInstructionUI?.SetActive(true);
    public void HideSkipInstruction() => skipInstructionUI?.SetActive(false);

    // =====================================================
    // DEBUG
    // =====================================================

    private void Update()
    {
        if (!showArenaBoundsDebug) return;

        ArenaProfile profile = GetArenaProfile();
        if (profile == null) return;

        float radius = profile.WorldArenaRadius;
Vector3 centre = GetArenaCentre();
        const int segments = 64;
        float angleStep = 360f / segments * Mathf.Deg2Rad;

        Vector3 prev = centre + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * angleStep;
            Vector3 next = centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Debug.DrawLine(prev, next, Color.yellow);
            prev = next;
        }
    }

    // =====================================================
    // ENTRY
    // =====================================================

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        ResolveGridData();
        PrepareLevel();
        PlayIntro();
    }

    // =====================================================
    // RESOLVE
    // =====================================================

    private void ResolveGridData()
    {
        Debug.LogWarning($"[LevelDataController] ResolveGridData started. Initial activeGridData: {(activeGridData != null ? activeGridData.levelID : "NULL")}. LevelSelectionCache.SelectedGridData: {(LevelSelectionCache.SelectedGridData != null ? LevelSelectionCache.SelectedGridData.levelID : "NULL")}");

        activeGridData = LevelSelectionCache.SelectedGridData;

    #if UNITY_EDITOR
        // Save Data Monitor can pre-select a level via EditorPrefs
        const string key = "SaveDataMonitor_OverrideLevel";
        string overridePath = UnityEditor.EditorPrefs.GetString(key, "");
        Debug.LogWarning($"[LevelDataController] Checking SaveDataMonitor override. Path in EditorPrefs: '{(string.IsNullOrEmpty(overridePath) ? "(empty)" : overridePath)}'");

        if (!string.IsNullOrEmpty(overridePath))
        {
            var overrideData = UnityEditor.AssetDatabase.LoadAssetAtPath<GridData>(overridePath);
            if (overrideData != null)
            {
                activeGridData = overrideData;
                Debug.LogWarning($"[LevelDataController] SUCCESS: Overrode level from Save Data Monitor: '{overrideData.levelID}' (Path: {overridePath})");
            }
            else
            {
                Debug.LogError($"[LevelDataController] ERROR: Failed to load GridData at path: '{overridePath}'. AssetDatabase.LoadAssetAtPath returned null.");
            }
            UnityEditor.EditorPrefs.DeleteKey(key);
        }

        const string waveKey = "SaveDataMonitor_OverrideWavePreset";
        string wavePath = UnityEditor.EditorPrefs.GetString(waveKey, "");
        if (!string.IsNullOrEmpty(wavePath) && activeGridData != null)
        {
            var overridePreset = UnityEditor.AssetDatabase.LoadAssetAtPath<WavePreset>(wavePath);
            if (overridePreset != null)
            {
                // Clone the grid data so we don't dirty the ScriptableObject asset on disk
                activeGridData = Instantiate(activeGridData);
                activeGridData.runtimeWavePresetOverride = overridePreset;
                Debug.Log($"[LevelDataController] Override wave preset from Save Data Monitor: '{overridePreset.name}'");
            }
            UnityEditor.EditorPrefs.DeleteKey(waveKey);
        }
#endif

        if (activeGridData == null)
        {
            Debug.LogWarning("LevelDataController: No grid data passed, using fallback.");
            activeGridData = fallbackGridData;
        }

        if (activeGridData == null)
            Debug.LogError("LevelDataController: No valid GridData.");

        isTimeTrial      = activeGridData != null && activeGridData.isTimeTrial;
        timeLimitSeconds = activeGridData != null ? activeGridData.timeLimitSeconds : 0f;

        if (gongWavesController != null && activeGridData != null)
            gongWavesController.SetGongWavePreset(activeGridData.gongWavePreset);
    }

    // =====================================================
    // PREPARE
    // =====================================================

    private void PrepareLevel()
    {
        LevelSoulTracker.Instance?.InitialiseForLevel(activeGridData?.levelID);

        levelSpawner.ApplyGridData(activeGridData);
        levelSpawner.SpawnMaze();

        var whirlpoolManager = FindObjectOfType<WhirlpoolManager>();
        if (whirlpoolManager != null && wavePlaneObject != null)
            whirlpoolManager.wavePlaneTransform = wavePlaneObject.transform;

        InitialiseMapProjection();
        ApplyMapRefPlaneOffset();

        ArenaProfile arenaProfile = levelSpawner.GetArenaProfile();
        float baselineWaterY = levelSpawner.GetBaselineWaterY();

        if (arenaProfile != null && wavePlaneObject != null)
        {
            Renderer waveRend = wavePlaneObject.GetComponent<Renderer>();
            if (waveRend != null)
            {
                float waveRadius = arenaProfile.arenaRadius1;
                waveRend.sharedMaterial.SetFloat("_ArenaRadius1", waveRadius);

                // Update ArenaMask with center and water height
                waveRend.sharedMaterial.SetVector("_ArenaMask", new Vector4(
                    arenaProfile.arenaCentreOffset.x, 
                    baselineWaterY, 
                    arenaProfile.arenaCentreOffset.y, 
                    0f));
            }

            Vector3 wavePos = wavePlaneObject.transform.position;
            wavePos.y = baselineWaterY;
            wavePlaneObject.transform.position = wavePos;

            wavePlaneObject.transform.localScale = Vector3.one;
            var meshGen = wavePlaneObject.GetComponent<WaveMeshGenerator>();
            if (meshGen != null)
            {
                meshGen.UpdateMeshSize(arenaProfile.WorldArenaWidth * arenaProfile.wavePlaneCoverageMultiplier);
            }
        }

        if (sonarGridParent != null)
        {
            Vector3 sp = sonarGridParent.position;
            sp.y = baselineWaterY;
            sonarGridParent.position = sp;

            if (arenaProfile != null)
            {
                var sonarGen = sonarGridParent.GetComponentInChildren<SonarPlaneGenerator>();

                // Load this level's sonar grid formation (set in the Grid Designer).
                // Null keeps whatever formation is already on the scene generator.
                if (sonarGen != null && activeGridData != null && activeGridData.sonarGridType != null)
                    sonarGen.SetGridType(activeGridData.sonarGridType);

                if (sonarController != null)
                {
                    float sonarScale = arenaProfile.WorldArenaWidth * arenaProfile.wavePlaneCoverageMultiplier;
                    sonarController.SetGridArea(sonarScale, 5f);
                }

                Material sonarMat = sonarGen?.GridType?.planeMaterial;
                if (sonarMat != null)
                {
                    float sonarRadius = arenaProfile.arenaRadius1;
                    sonarMat.SetFloat("_ArenaRadius", sonarRadius);
                    
                    sonarMat.SetVector("_ArenaMask", new Vector4(
                        arenaProfile.arenaCentreOffset.x, 
                        baselineWaterY, 
                        arenaProfile.arenaCentreOffset.y, 
                        0f));
                }
}
        }

        if (arenaProfile != null && mapPointer.MapSurface != null)
        {
            Renderer mapRend = mapPointer.MapSurface.GetComponent<Renderer>();
            if (mapRend != null)
                mapRend.material.SetVector("_MapGridTiling", arenaProfile.mapGridTiling);
        }

        ConfigureGongTower();

        int visitCount = GameProgressData.GetCompletionCount(activeGridData?.levelID);

        spawnedSnake = levelSpawner.SpawnConditionals(visitCount);
        if (spawnedSnake != null)
            snakeController?.OnSnakeSpawned(spawnedSnake);

        Debug.Log($"[LevelDataController] Level '{activeGridData?.levelID}' completions: {visitCount}");

        if (mapPointer != null)
        {
            mapPointer.SetSpawnFinalizeMatrix(levelSpawner.SpawnFinalizeMatrix);
            mapPointer.PrepareMapContentRoot();
            mapPointer.BuildMazeWallMap();
            mapPointer.BuildSplineWallMap();
            mapPointer.BuildCubeBuildingMap();
            mapPointer.UpdateExitMarkers();
            mapPointer.UpdateEntranceMarkers();
        }

        mapPointer.UpdateWaveCenter();
        MoveWavePlaneToArenaCenter();

        if (wavePlaneObject != null)
            wavePlaneObject.SetActive(true);

        if (boatMovement) boatMovement.controlsEnabled = false;

        gameplayBoat.SetActive(false);
        gameplayFollowCamera.SetActive(false);

        mapUI?.SetActive(false);
        uiOverlay?.HideUI();
        steeringWheel?.DisableWheel();

        int soulsOnBoat = GameProgressData.GetSoulsOnBoat();
        if (soulsOnBoat > 0)
            fishingController?.RestoreSoulVisuals(soulsOnBoat);
    }

    private void InitialiseMapProjection()
    {
        if (activeGridData == null) return;
        Bounds arenaBounds   = levelSpawner.GetArenaBounds();
        ArenaProfile arenaProfile = levelSpawner.GetArenaProfile();
        mapPointer.InitialiseMapProjection(arenaBounds, activeGridData, arenaProfile);
    }

    private void ApplyMapRefPlaneOffset()
    {
        mapPointer.ApplyRefPlaneRotation();
    }

    private void MoveWavePlaneToArenaCenter()
    {
        if (wavePlaneObject == null) return;

        ArenaProfile profile = levelSpawner.GetArenaProfile();
        if (profile == null) return;

        Vector3 pos = wavePlaneObject.transform.position;
        pos.x = profile.arenaCentreOffset.x;
        pos.z = profile.arenaCentreOffset.y;
        wavePlaneObject.transform.position = pos;
    }

    // =====================================================
    // INTRO / RITUAL
    // =====================================================

    private void PlayIntro()
    {
        LevelStartRitual ritualToPlay = startRitual;

        if (activeGridData != null && activeGridData.startRitual != null)
            ritualToPlay = activeGridData.startRitual;

        if (ritualToPlay != null)
            StartCoroutine(ritualToPlay.Play(levelSpawner, OnIntroComplete));
        else
            OnIntroComplete();
    }

    private void OnIntroComplete()
    {
        BeginGameplay();
    }

    // =====================================================
    // GONG TOWER
    // =====================================================

    private void ConfigureGongTower()
    {
        bool needsGong = isTimeTrial || FindObjectOfType<GongWavesTrigger>() != null;

        // GongCamAnimationRelay is on the GongCam1 animator GO inside the GongTowerPrefab (may be inactive)
        var relays = FindObjectsOfType<GongCamAnimationRelay>(true);
        if (relays == null || relays.Length == 0)
        {
            Debug.Log("[LDC] ConfigureGongTower — no GongCamAnimationRelay found; GongTower not present in walls prefab.");
            return;
        }

        var gongCamRelay  = relays[0];
        var gongTowerRoot = gongCamRelay.transform.parent;

        gongTowerRoot.gameObject.SetActive(needsGong);

        if (!needsGong || gongWavesController == null)
            return;

        // Wire camera rig references from tower into GongWavesController
        gongWavesController.gongCamAnimatorGO = gongCamRelay.gameObject;
        gongWavesController.gongAnimator      = gongCamRelay.GetComponent<Animator>();
        gongWavesController.gongCam           = gongCamRelay.GetComponentInChildren<CinemachineCamera>(true);

        // Wire SisterNomAnimationRelay so its animation events reach GongWavesController
        var sisterRelay = gongTowerRoot.GetComponentInChildren<SisterNomAnimationRelay>(true);
        if (sisterRelay != null)
            sisterRelay.gongController = gongWavesController;

        Debug.Log($"[LDC] GongTower enabled — isTimeTrial={isTimeTrial}, gongCam={(gongWavesController.gongCam != null ? "OK" : "NULL")}");
    }

    // =====================================================
    // BEGIN GAMEPLAY
    // =====================================================

    private void BeginGameplay()
    {
        if (gameplayStarted) return;
        gameplayStarted = true;

        LevelExitController.ResetPortalGuard();

        snakeController?.Initialise();

        if (waveController != null && activeGridData != null)
        {
            WavePreset preset = activeGridData.runtimeWavePresetOverride ?? activeGridData.gameplayWavePreset;
            float maskStrength = preset != null ? preset.state.SoulFishMaskStrength : -1f;
            Debug.Log($"[LDC] BeginGameplay — preset='{preset?.name ?? "NULL"}' SoulFishMaskStrength={maskStrength} soulFishRadius={waveController.soulFishRadius} soulFishStrength={waveController.soulFishStrength}");
            waveController.ApplyPresetInstant(preset);
            waveController.ApplySoulFishMaskSettings();
        }
        else
        {
            Debug.LogWarning($"[LDC] BeginGameplay — waveController={waveController != null} activeGridData={activeGridData != null} — skipping wave apply.");
        }

        Debug.Log($"[LDC] BeginGameplay — soulFishWaveLinker={(soulFishWaveLinker != null ? "assigned" : "NULL")}");
        soulFishWaveLinker?.BakePositionsOnce();
        mapPointer?.InitialiseSnakeMarker(GetSnake());
        soulFishMapLinker?.BakePositionsOnce();

        gameplayFollowCamera.SetActive(true);

        if (zoomController != null && activeGridData != null)
        {
            zoomController.defaultFOV = activeGridData.cameraProfile != null
                ? activeGridData.cameraProfile.fieldOfView
                : 7f;
            zoomController.ResetToDefaultFOV();
        }

        if (CameraController.Instance != null && activeGridData != null)
        {
            CameraController.Instance.ApplyProfile(activeGridData.cameraProfile);

            GameObject center = GameObject.FindGameObjectWithTag("LevelCenter");

            if (center != null && gameplayBoat != null)
                CameraController.Instance.SetTargets(center.transform, gameplayBoat.transform);
        }

        if (boatVisual != null)
            boatVisual.extraYOffset = gameplayExtraYOffset;

        uiOverlay?.ShowUI();
        steeringWheel?.EnableWheel();
        mapUI?.SetActive(true);

        if (isTimeTrial && timeTrialTimer != null)
            timeTrialTimer.StartTimer(timeLimitSeconds, OnTimeTrialExpired);

        if (gameplayBoat != null)
        {
            if (levelSpawner.mazeSpawned && activeGridData != null)
            {
                int selectedIndex = LevelSelectionCache.SelectedEntranceIndex;

                Debug.Log($"[LDC] BeginGameplay — SelectedEntranceIndex: {selectedIndex}, " +
                          $"entrances in GridData: {activeGridData.entrances?.Count ?? 0}");

                GridData.ArenaEntrance chosenEntrance =
                    (activeGridData.entrances != null && selectedIndex >= 0 && selectedIndex < activeGridData.entrances.Count)
                        ? activeGridData.entrances[selectedIndex]
                        : activeGridData.entrances != null && activeGridData.entrances.Count > 0 ? activeGridData.entrances[0] : null;

                Vector3 spawnPos   = gameplayBoat.transform.position;
                float   spawnAngle = gameplayBoat.transform.eulerAngles.y;

                if (chosenEntrance != null)
                {
                    spawnAngle = chosenEntrance.perimeterAngle + 180f;

                    int effectivePortalIndex = (selectedIndex >= 0 && selectedIndex < activeGridData.entrances.Count)
                        ? selectedIndex : 0;

                    LevelExitController[] allPortals = FindObjectsOfType<LevelExitController>();
                    LevelExitController   portal     = System.Array.Find(allPortals,
                        p => p.portalIndex == effectivePortalIndex);

                    ArenaEntranceSpawnPoint spawnPoint = portal != null
                        ? portal.GetComponentInChildren<ArenaEntranceSpawnPoint>()
                        : null;

                    if (spawnPoint != null)
                    {
                        Vector3 sp = spawnPoint.transform.position;
                        spawnPos   = new Vector3(sp.x, gameplayBoat.transform.position.y, sp.z);
                        spawnAngle = spawnPoint.FacingAngleY;
                        Debug.Log($"[LDC] Entrance '{chosenEntrance.id}' — spawn point at {spawnPos}, facing {spawnAngle}°");
                    }
                    else
                    {
                        Debug.LogWarning($"[LDC] No ArenaEntranceSpawnPoint found for entrance index {selectedIndex}. " +
                                         $"Add the component as a child of the door prefab.");
                    }
                }
                else
                {
                    Debug.LogWarning($"[LDC] No entrance found — using default boat position.");
                }

                gameplayBoat.transform.position = spawnPos;
                gameplayBoat.transform.rotation = Quaternion.Euler(0f, spawnAngle, 0f);
            }
            else
            {
                Debug.LogWarning($"[LDC] Skipping entrance spawn — mazeSpawned: {levelSpawner.mazeSpawned}, " +
                                 $"gridData: {(activeGridData != null ? "OK" : "NULL")}");
            }

            gameplayBoat.SetActive(true);

            sonarController?.SetBoat(gameplayBoat.transform);
            FindObjectOfType<BoatHUD>()?.SetBoatTransform(gameplayBoat.transform);

            BoatStartupCoordinator coordinator = gameplayBoat.GetComponent<BoatStartupCoordinator>();
            coordinator.BeginStartup();

        }
    }

    private void OnTimeTrialExpired()
    {
        gongWavesController?.StartGongSequence();
    }

    // =====================================================
    // SHARED SCENE CONTEXT
    // =====================================================

    public Transform GetWaveTransform()    => wavePlaneObject != null ? wavePlaneObject.transform : null;
    public Transform GetSonarGridParent() => sonarGridParent;

    // Wave plane material — the same sharedMaterial whose _ArenaMask is set at spawn (see Setup).
    public Material GetWaveMaterial()
    {
        var rend = wavePlaneObject != null ? wavePlaneObject.GetComponent<Renderer>() : null;
        return rend != null ? rend.sharedMaterial : null;
    }

    // Sonar grid material — the SonarGridType.planeMaterial whose _ArenaMask is set alongside the
    // wave material at spawn. Exposed so water-level changes can keep the sonar edge-mask in sync.
    public Material GetSonarGridMaterial()
    {
        if (sonarGridParent == null) return null;
        var sonarGen = sonarGridParent.GetComponentInChildren<SonarPlaneGenerator>();
        return sonarGen != null && sonarGen.GridType != null ? sonarGen.GridType.planeMaterial : null;
    }

    public Transform GetBoatRoot() => gameplayBoat != null ? gameplayBoat.transform : null;

    public BoatMovement GetBoatMovement() => boatMovement;

    /// <summary>
    /// Returns the ArenaProfile for the current level.
    /// Used by DroppedSoul to read bounds radius.
    /// </summary>
    public ArenaProfile GetArenaProfile() => levelSpawner != null ? levelSpawner.GetArenaProfile() : null;

    public Vector3 GetArenaCentre()
    {
        ArenaProfile profile = GetArenaProfile();
        if (profile == null) return Vector3.zero;
        return new Vector3(profile.arenaCentreOffset.x, 0f, profile.arenaCentreOffset.y);
    }

    private WavePreset _activePresetOverride;

    /// <summary>
    /// Overrides the active wave preset returned by GetActiveWavePreset.
    /// Call this whenever a runtime system (e.g. GongWavesController) switches preset.
    /// </summary>
    public void SetActiveWavePreset(WavePreset preset)
    {
        _activePresetOverride = preset;
    }

    public WavePreset GetActiveWavePreset()
    {
        if (_activePresetOverride != null) return _activePresetOverride;
        if (activeGridData == null) return null;
        return activeGridData.runtimeWavePresetOverride ?? activeGridData.gameplayWavePreset;
    }

    public BadGuySnakeMovement GetSnake()
    {
        if (spawnedSnake == null) return null;
        return spawnedSnake.GetComponentInChildren<BadGuySnakeMovement>();
    }
}
