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

        float radius = GetArenaRadius();
        if (radius <= 0f) return;

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

    /// <summary>
    /// Hand this level's fog to the field: the arena map when fog is on, null when it is not.
    ///
    /// There is deliberately no fallback anywhere in the fog system — an arena map is the only
    /// thing that decides where fog sits — so "fog on with no map" is a level that gets no fog,
    /// and it says so rather than quietly scattering something.
    /// </summary>
    void ApplyLevelFog(GridData data)
    {
        if (data == null) { FogFieldManager.ApplyArenaMap(null); return; }

        bool on = data.fogEnabled && data.fogMap != null;

        if (data.fogEnabled && data.fogMap == null)
            Debug.LogWarning($"[Fog] {data.levelID} has fog enabled but no Fog Arena Map assigned, " +
                             $"so it gets no fog. Assign one in the Grid Designer's Fog section.", data);

        // The painted field covers the arena, so it needs the arena's real width. Without this it
        // runs on a fallback that is only right for one level's size.
        if (data.WorldArenaWidth > 0f)
            FogFieldManager.SetArenaWidth(data.WorldArenaWidth);

        FogFieldManager.SetEnabled(on);

        // ApplyArenaMap seeds blob density from the map's own Starting Density, so the order
        // matters: enabling first, then handing over the map, means the density arrives with it.
        FogFieldManager.ApplyArenaMap(on ? data.fogMap : null);
    }

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

        Vector2 arenaCentreOffset = activeGridData != null ? activeGridData.arenaCentreOffset : Vector2.zero;
        float baselineWaterY = levelSpawner.GetBaselineWaterY();

        if (activeGridData != null && wavePlaneObject != null)
        {
            Renderer waveRend = wavePlaneObject.GetComponent<Renderer>();
            if (waveRend != null)
            {
                float waveRadius = levelSpawner.GetArenaMaskRadius();
                waveRend.sharedMaterial.SetFloat("_ArenaRadius1", waveRadius);

                // Update ArenaMask with center and water height
                waveRend.sharedMaterial.SetVector("_ArenaMask", new Vector4(
                    arenaCentreOffset.x, 
                    baselineWaterY, 
                    arenaCentreOffset.y, 
                    0f));
            }

            Vector3 wavePos = wavePlaneObject.transform.position;
            wavePos.y = baselineWaterY;
            wavePlaneObject.transform.position = wavePos;

            wavePlaneObject.transform.localScale = Vector3.one;
            var meshGen = wavePlaneObject.GetComponent<WaveMeshGenerator>();
            if (meshGen != null)
            {
                meshGen.UpdateMeshSize(activeGridData.WorldArenaWidth * activeGridData.wavePlaneCoverageMultiplier);
            }
        }

        if (sonarGridParent != null)
        {
            // The lattice is arena-centred and static, so sit it on the same centre the
            // shader masks against (_ArenaMask below) rather than wherever the scene left it.
            Vector3 sp = sonarGridParent.position;
            sp.y = baselineWaterY;
            sp.x = arenaCentreOffset.x;
            sp.z = arenaCentreOffset.y;
            sonarGridParent.position = sp;

            {
                // Include inactive — the grid parent is switched off while sonar is idle
                var sonarGen = sonarGridParent.GetComponentInChildren<SonarPlaneGenerator>(true);

                // Load this level's sonar grid formation (set in the Grid Designer).
                // Null keeps whatever formation is already on the scene generator.
                if (sonarGen != null && activeGridData != null && activeGridData.sonarGridType != null)
                    sonarGen.SetGridType(activeGridData.sonarGridType);

                // Fog is loaded the same way and from the same place — an arena map is level
                // geography, so it arrives with the level rather than with the weather.
                //
                // Handed over unconditionally, INCLUDING when the level has no map or fog is off.
                // Passing null is what clears the previous level's fog: skipping the call would
                // leave the last level's banks sitting on this one, which is the sort of thing that
                // only shows up two levels later and looks like the map is haunted.
                ApplyLevelFog(activeGridData);

                // Lattice size is not pushed from here — SonarController derives the arena square
                // from the BaselineMarker handed to it by LevelSpawner (BaselineMarker.discRadius x 2).

                Material sonarMat = sonarGen?.GridType?.planeMaterial;
                if (sonarMat != null)
                {
                    float sonarRadius = levelSpawner.GetArenaMaskRadius();
                    sonarMat.SetFloat("_ArenaRadius", sonarRadius);
                    
                    sonarMat.SetVector("_ArenaMask", new Vector4(
                        arenaCentreOffset.x, 
                        baselineWaterY, 
                        arenaCentreOffset.y, 
                        0f));
                }
}
        }

        if (activeGridData != null && mapPointer.MapSurface != null)
        {
            Renderer mapRend = mapPointer.MapSurface.GetComponent<Renderer>();
            if (mapRend != null)
                mapRend.material.SetVector("_MapGridTiling", activeGridData.mapGridTiling);
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
            mapPointer.BuildProceduralSpikeMap();
            mapPointer.BuildStreetLightMap();
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
        Bounds arenaBounds = levelSpawner.GetArenaBounds();
        mapPointer.InitialiseMapProjection(arenaBounds, activeGridData);
    }

    private void ApplyMapRefPlaneOffset()
    {
        mapPointer.ApplyRefPlaneRotation();
    }

    private void MoveWavePlaneToArenaCenter()
    {
        if (wavePlaneObject == null) return;

        if (activeGridData == null) return;

        Vector3 pos = wavePlaneObject.transform.position;
        pos.x = activeGridData.arenaCentreOffset.x;
        pos.z = activeGridData.arenaCentreOffset.y;
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
        var sonarGen = sonarGridParent.GetComponentInChildren<SonarPlaneGenerator>(true);
        return sonarGen != null && sonarGen.GridType != null ? sonarGen.GridType.planeMaterial : null;
    }

    public Transform GetBoatRoot() => gameplayBoat != null ? gameplayBoat.transform : null;

    public BoatMovement GetBoatMovement() => boatMovement;

    // The level's arena radius — the radius the walls were generated to.
    public float GetArenaRadius() => levelSpawner != null ? levelSpawner.GetArenaRadius() : 0f;

    public Vector3 GetArenaCentre()
    {
        if (activeGridData == null) return Vector3.zero;
        return new Vector3(activeGridData.arenaCentreOffset.x, 0f, activeGridData.arenaCentreOffset.y);
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
