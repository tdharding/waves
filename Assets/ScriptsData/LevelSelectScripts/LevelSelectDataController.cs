using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

[DefaultExecutionOrder(-50)]
public class LevelSelectDataController : MonoBehaviour
{
    [Header("Cursor")]
    [SerializeField] private bool unlockCursor = true;
    [SerializeField] private bool showCursor = true;

    [Header("Collision Material")]
    [SerializeField] private Material collisionMaterial;

    [Header("Time")]
    [SerializeField] private float timeScale = 1f;

    [Header("Audio")]
    [SerializeField] private bool resumeAudio = true;

    [Header("Event System")]
    [SerializeField] private bool enforceEventSystem = true;

    [Header("Boat Position")]
    [SerializeField] private LevelSelectBoatControl boatControl;

    [Header("World Data")]
    [Tooltip("The world's designer data, which carries this world's fog switch and fog map, and " +
             "its aesthetic settings. Wired by the Level Select Designer's Scene Deploy panel. " +
             "Null means no fog and no look pushed.")]
    [SerializeField] private LevelSelectDesignerData designerData;

    /// <summary>The world this scene was built from, for editor tools that need to read it.</summary>
    public LevelSelectDesignerData DesignerData => designerData;


    void Awake()
    {
        LevelSelectionCache.CurrentWorldScene = SceneManager.GetActiveScene().name;

        if (boatControl == null)
            boatControl = FindObjectOfType<LevelSelectBoatControl>();

        ResetTime();
        ResetCursor();
        ResetAudio();
        EnsureEventSystem();
        RemoveUnlockedObstacles();
        ResetCollisionMaterial();
        WireSoulFishDisplay();
        WireBoatReferences();
        WireBoatHUD();
    }

    /// <summary>
    /// Publishes the world's look every frame. The aesthetic uniforms are bare globals with no
    /// material value behind them, so this cannot be a set-once call in Awake: a shader or
    /// material reimport drops them back to 0 and only a per-frame push brings them back.
    /// </summary>
    void Update()
    {
        if (designerData != null) designerData.ApplyAesthetics();
    }

    void Start()
    {
        RestoreBoatPosition();
        WireCameraFollowTarget();   // after RestoreBoatPosition so camera locks on to the correct spline position
        ApplyWorldFog();            // also after RestoreBoatPosition — the first fill lands around the boat
        NotifyExitIfReturningFromLevel();
    }

    /// <summary>
    /// Hand this world's fog to the field, the same way a level hands over its own in
    /// LevelDataController.ApplyLevelFog: the map when fog is on, null when it is not, and no
    /// fallback behind either. Fog appearing where nobody put it is worse than no fog, because it
    /// looks like the map is working when it is not.
    ///
    /// In Start rather than Awake, and deliberately. This controller runs at -50 and the fog
    /// manager at 50, so every one of these statics would find a null instance in Awake and no-op
    /// without saying anything. By Start the manager exists — and so does the boat, which matters
    /// because handing over a map primes a burst of fog around wherever the boat is standing.
    ///
    /// Two things a level does not have to do, because a level select scene is not an arena:
    /// there is no BoatToWaterMaterial pushing _BoatWorldCenter out here, so the boat is named
    /// explicitly; and there is no arena width, so the sheet is sized to the landscape instead.
    /// </summary>
    void ApplyWorldFog()
    {
        // The boat first, and outside the data check. The field centres on it whether the map
        // comes from here or from what the scene was deployed with, and a field centred on world
        // zero paints its window in a corner of the map — which looks exactly like fog that is
        // switched off.
        var boatGo = GameObject.Find("LevelSelectBoat");
        if (boatGo != null) FogFieldManager.SetBoat(boatGo.transform);
        else Debug.LogWarning("[Fog] No LevelSelectBoat in the scene, so the fog field has nothing " +
                              "to centre on and will paint its window at world zero.");

        // No data wired is NOT the same as no fog authored, and the difference only shows up in a
        // build. This used to hand over a null map, which WIPED the map the Fog Field was deployed
        // with — a scene with a perfectly good rig shipped with no fog and nothing said why.
        // Left alone, the field runs on what the scene holds and primes itself on Start.
        if (designerData == null)
        {
            Debug.LogError("[Fog] LevelSelectDataController has no designer data, so this world's " +
                           "fog map cannot be handed over. Run Wire All in the Level Select " +
                           "Designer and save the scene. The fog field is left on the map it was " +
                           "deployed with.", this);
            return;
        }

        bool on = designerData.fogEnabled && designerData.fogMap != null;

        if (designerData.fogEnabled && designerData.fogMap == null)
            Debug.LogWarning($"[Fog] {designerData.name} has fog enabled but no Fog Map assigned, " +
                             $"so this world gets no fog. Assign one in the Level Select " +
                             $"Designer's Fog section.", designerData);

        // The sheet is static geometry covering the whole landscape, so it needs the landscape's
        // real span. Re-sent here as well as at deploy so resizing the tile family and pressing
        // play does not leave the sheet at the size it was deployed at.
        if (designerData.LandscapeSpan > 0f)
            FogFieldManager.SetArenaWidth(designerData.LandscapeSpan);

        FogFieldManager.SetEnabled(on);

        // ApplyArenaMap seeds density from the map's own numbers, so the order matters: enable
        // first, then hand over the map, and the density arrives with it.
        FogFieldManager.ApplyArenaMap(on ? designerData.fogMap : null);
    }

    void ResetCollisionMaterial()
{
    if (collisionMaterial == null) return;
    collisionMaterial.SetFloat(Shader.PropertyToID("_Factor"), 0f);
    collisionMaterial.SetVector(Shader.PropertyToID("_Offset"), Vector2.zero);
}

    /// <summary>
    /// Puts the boat back where the player left it.
    ///
    /// Three answers, in order. Where the boat actually got to, if it has been anywhere. Then
    /// the river and distance a door names, which is how coming out of a level places it —
    /// and which clears the pose when it is written, so it wins on that trip. Then the head of
    /// the main river, for a save that has never seen the map.
    /// </summary>
    void RestoreBoatPosition()
    {
        if (boatControl == null)
        {
            Debug.LogWarning("LevelSelectDataController: No boatControl assigned.");
            return;
        }

        if (GameProgressData.TryGetBoatPose(out Vector3 pose, out float heading))
        {
            boatControl.PlaceAt(pose, Quaternion.Euler(0f, heading, 0f));
            Debug.Log($"LevelSelectDataController: Boat restored to {pose}, heading {heading:F1}.");
            return;
        }

        string savedSegmentID = GameProgressData.GetBoatSegmentID();
        float  savedProgress  = GameProgressData.GetBoatProgress(0f);

        if (!string.IsNullOrEmpty(savedSegmentID))
        {
            if (boatControl.PlaceOnSegment(savedSegmentID, savedProgress))
            {
                Debug.Log($"LevelSelectDataController: Boat placed on '{savedSegmentID}' at {savedProgress}.");
                return;
            }
            Debug.LogWarning($"LevelSelectDataController: Segment '{savedSegmentID}' not in the registry — falling back to the main river.");
        }

        // A main river that begins in a pool never has its head on the water the pool holds:
        // the run is cut off outside that pool's rim, so the head of its curve stands out on
        // the river with the pool still ahead of it. The designer knows where the pool is, so
        // a boat that has never been anywhere is put straight on it.
        if (designerData != null &&
            designerData.TryGetBoatStart(out Vector3 startPos, out Vector3 startForward,
                                         out bool startInPool) && startInPool)
        {
            boatControl.PlaceAt(startPos, Quaternion.LookRotation(startForward, Vector3.up));
            Debug.Log($"LevelSelectDataController: Nothing saved — boat placed on the pool at " +
                      $"the head of the main river, {startPos}.");
            return;
        }

        if (boatControl.PlaceOnSegment("MainRiver", 0f))
        {
            Debug.Log("LevelSelectDataController: Nothing saved — boat placed at the head of MainRiver.");
            return;
        }

        Debug.LogWarning("LevelSelectDataController: No saved place and no MainRiver — boat stays where the scene put it.");
    }

    void ResetTime()
    {
        Time.timeScale = timeScale;
    }

    void ResetCursor()
    {
        if (!unlockCursor) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = showCursor;
    }

    void ResetAudio()
    {
        if (!resumeAudio) return;
        AudioListener.pause = false;
    }

    void EnsureEventSystem()
    {
        if (!enforceEventSystem) return;
        if (EventSystem.current != null) return;

        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();

#if ENABLE_INPUT_SYSTEM
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    void NotifyExitIfReturningFromLevel()
    {
        string levelID     = LevelSelectionCache.JustExitedLevelID;
        int    portalIndex = LevelSelectionCache.JustExitedEntranceIndex;
        Debug.Log($"[LevelSelectDataController] JustExitedLevelID='{levelID}'  portalIndex={portalIndex}  SplineRiverManager={(SplineRiverManager.Instance != null ? "found" : "NULL")}");

        if (string.IsNullOrEmpty(levelID)) return;

        SplineRiverManager.Instance?.NotifyLevelExited(levelID, portalIndex);
        LevelSelectionCache.JustExitedLevelID       = string.Empty;
        LevelSelectionCache.JustExitedEntranceIndex = -1;
    }

    void WireSoulFishDisplay()
    {
        var display = FindObjectOfType<LevelSelectSoulFishDisplay>();
        if (display == null)
        {
            Debug.Log("[LevelSelectDataController] No LevelSelectSoulFishDisplay found in scene — skipping wire.");
            return;
        }

        var arenas = FindObjectsOfType<LevelSelectArenaController>();
        foreach (var arena in arenas)
            arena.soulFishDisplay = display;

        Debug.Log($"[LevelSelectDataController] Wired LevelSelectSoulFishDisplay to {arenas.Length} arena(s).");
    }

    void WireBoatHUD()
    {
        var hud = FindObjectOfType<BoatHUD>();
        if (hud == null) return;

        var boat = GameObject.Find("LevelSelectBoat");
        if (boat == null) return;

        hud.SetBoatTransform(boat.transform);
        Debug.Log("[LevelSelectDataController] BoatHUD wired to LevelSelectBoat.");
    }

    void WireBoatReferences()
    {
        if (boatControl == null) return;

        var boatGo = GameObject.Find("LevelSelectBoat");
        if (boatGo == null) return;

        // The body has to be on the boat itself, not on something hanging off it: the camera,
        // the map and every trigger read the boat's own transform, and a body on a child would
        // drive the child away and leave all of them looking at a boat that never moved.
        var body = boatGo.GetComponent<Rigidbody>();
        if (body == null)
        {
            var onChild = boatGo.GetComponentInChildren<Rigidbody>();
            if (onChild != null)
                Debug.LogError($"[LevelSelectDataController] LevelSelectBoat's Rigidbody is on " +
                               $"'{onChild.name}', not on the boat itself. Move the Rigidbody and " +
                               $"its collider up onto LevelSelectBoat, and give it the BoatPrefab " +
                               $"tag, or the boat will not move.", onChild);
            else
                Debug.LogError("[LevelSelectDataController] LevelSelectBoat has no Rigidbody — " +
                               "it cannot be driven, and the banks cannot stop it.", boatGo);
        }

        // Find TheBoatVis by tag "Boat" for the hull transform
        Transform meshTransform = null;
        foreach (Transform child in boatGo.GetComponentsInChildren<Transform>())
        {
            if (child.CompareTag("Boat")) { meshTransform = child; break; }
        }

        boatControl.WireBoatReferences(boatGo.transform, body, meshTransform);
        Debug.Log("[LevelSelectDataController] Boat references wired from PlayerBoat.");
    }

    void WireCameraFollowTarget()
    {
        var camController = FindObjectOfType<LevelSelectCameraController>();
        if (camController == null)
        {
            Debug.LogWarning("[LevelSelectDataController] WireCameraFollowTarget: No LevelSelectCameraController found in scene.");
            return;
        }
        if (camController.cam == null)
        {
            Debug.LogWarning("[LevelSelectDataController] WireCameraFollowTarget: CameraController found but 'cam' (CinemachineCamera) is null.");
            return;
        }

        Transform followTarget = GameObject.Find("LevelSelectBoat")?.transform;
        if (followTarget != null)
        {
            Debug.Log($"[LevelSelectDataController] WireCameraFollowTarget: found boat at {followTarget.position}, calling SetFollowTarget.");
            camController.SetFollowTarget(followTarget);
        }
        else
        {
            Debug.LogWarning("[LevelSelectDataController] WireCameraFollowTarget: No GameObject named 'LevelSelectBoat' found in scene.");
        }
    }

    void RemoveUnlockedObstacles()
    {
        LevelSelectPathObstacleObject[] obstacles =
            FindObjectsOfType<LevelSelectPathObstacleObject>();

        foreach (var obstacle in obstacles)
        {
            if (GameProgressData.IsUnlocked(obstacle.obstacleID))
            {
                Debug.Log($"Removing unlocked obstacle: {obstacle.obstacleID}");
                Destroy(obstacle.gameObject);
            }
        }
    }
}