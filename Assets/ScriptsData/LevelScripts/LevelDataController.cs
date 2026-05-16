using UnityEngine;
using System.Collections;

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

        float radius = profile.droppedSoulBoundsRadius;
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
        if (arenaProfile != null && wavePlaneObject != null)
        {
            Renderer waveRend = wavePlaneObject.GetComponent<Renderer>();
            if (waveRend != null)
            {
                waveRend.sharedMaterial.SetFloat("_ArenaRadius1", arenaProfile.arenaRadius1);

                // Reset wave plane Y and _ArenaMask.y to authored defaults —
                // WaterLevelModifier uses sharedMaterial so changes persist between sessions.
                Vector4 mask = waveRend.sharedMaterial.GetVector("_ArenaMask");
                mask.y = 0f;
                waveRend.sharedMaterial.SetVector("_ArenaMask", mask);
            }

            Vector3 wavePos = wavePlaneObject.transform.position;
            wavePos.y = 0f;
            wavePlaneObject.transform.position = wavePos;

            wavePlaneObject.transform.localScale = arenaProfile.wavePlaneScale;
        }

        if (arenaProfile != null && mapPointer.MapSurface != null)
        {
            Renderer mapRend = mapPointer.MapSurface.GetComponent<Renderer>();
            if (mapRend != null)
                mapRend.material.SetVector("_MapGridTiling", arenaProfile.mapGridTiling);
        }

        int visitCount = GameProgressData.GetCompletionCount(activeGridData?.levelID);

        spawnedSnake = levelSpawner.SpawnConditionals(visitCount);
        if (spawnedSnake != null)
            snakeController?.OnSnakeSpawned(spawnedSnake);

        Debug.Log($"[LevelDataController] Level '{activeGridData?.levelID}' completions: {visitCount}");

        if (mapPointer != null)
        {
            mapPointer.BuildMazeWallMap();
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
            waveController.ApplyPresetInstant(preset);
            waveController.ApplySoulFishMaskSettings();
        }

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
            Transform soulBoatTransform = null;

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
                    Debug.LogWarning($"[LDC] No entrance found — spawning soul boat at default position.");
                }

                gameplayBoat.transform.position = spawnPos;
                gameplayBoat.transform.rotation = Quaternion.Euler(0f, spawnAngle, 0f);

                // Always spawn soul boat — fishing depends on it regardless of entrance config
                GameObject soulBoat = levelSpawner.SpawnSoulBoat(spawnPos, spawnAngle);
                soulBoatTransform = soulBoat != null ? soulBoat.transform : null;
                Debug.Log($"[LDC] SoulBoat spawned: {(soulBoat != null ? soulBoat.name : "NULL")}");
            }
            else
            {
                Debug.LogWarning($"[LDC] Skipping entrance spawn — mazeSpawned: {levelSpawner.mazeSpawned}, " +
                                 $"gridData: {(activeGridData != null ? "OK" : "NULL")}");
            }

            // Distribute SoulBoat reference to all consumers
            if (soulBoatTransform != null)
            {
                foreach (var shoal in FindObjectsOfType<SoulShoalController>())
                    shoal.SetSoulBoat(soulBoatTransform);
                FindObjectOfType<SonarGridController>()?.SetSoulBoat(soulBoatTransform);
                FindObjectOfType<SonarUIMapController>()?.SetSoulBoat(soulBoatTransform);
                FindObjectOfType<SonarCameraFollow>()?.BeginTracking(soulBoatTransform);

                // Wire lure controller from soul boat to the boat control router and tool manager
                var lureController = soulBoatTransform.GetComponentInChildren<LureController>();
                if (lureController != null)
                {
                    FindObjectOfType<BoatControlRouter>()?.SetLureController(lureController);
                    FindObjectOfType<BoatToolManager>()?.SetLureController(lureController);
                }
            }

            gameplayBoat.SetActive(true);

            sonarController?.SetBoat(gameplayBoat.transform);
            FindObjectOfType<BoatHUD>()?.SetBoatTransform(gameplayBoat.transform);

            // Re-resolve whirl direction sources now that the boat is active —
            // they use FindGameObjectWithTag("Boat") which fails while the boat is inactive.
            if (soulBoatTransform != null)
                foreach (var whirl in soulBoatTransform.GetComponentsInChildren<SoulWhirlDirection>(true))
                    whirl.ResolveDirectionSource();

            BoatStartupCoordinator coordinator = gameplayBoat.GetComponent<BoatStartupCoordinator>();
            coordinator.BeginStartup();

            FindObjectOfType<SonarProxyBoat>()?.BeginTracking();
        }
    }

    private void OnTimeTrialExpired()
    {
        gongWavesController?.StartGongSequence();
    }

    // =====================================================
    // SHARED SCENE CONTEXT
    // =====================================================

    public Transform GetWaveTransform() => wavePlaneObject != null ? wavePlaneObject.transform : null;

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
