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
        WireJunctionNodes();
        WireBoatReferences();
        WireBoatHUD();
    }

    void Start()
    {
        RestoreBoatPosition();
        WireCameraFollowTarget();   // after RestoreBoatPosition so camera locks on to the correct spline position
        NotifyExitIfReturningFromLevel();
    }

    void ResetCollisionMaterial()
{
    if (collisionMaterial == null) return;
    collisionMaterial.SetFloat(Shader.PropertyToID("_Factor"), 0f);
    collisionMaterial.SetVector(Shader.PropertyToID("_Offset"), Vector2.zero);
}

    void RestoreBoatPosition()
    {
        if (boatControl == null)
        {
            Debug.LogWarning("LevelSelectDataController: No boatControl assigned.");
            return;
        }

        string savedSegmentID = GameProgressData.GetBoatSegmentID();
        float savedProgress   = GameProgressData.GetBoatProgress(0f);

        if (string.IsNullOrEmpty(savedSegmentID))
        {
            // No save data — snap boat to the start of the main river so it's in a valid position.
            var registry = RiverSegmentRegistry.Instance;
            if (registry != null)
            {
                var mainSegment = registry.GetSegment("MainRiver");
                if (mainSegment != null)
                {
                    var mainContainer = mainSegment.GetComponent<SplineContainer>();
                    if (mainContainer != null)
                    {
                        boatControl.RestoreToSegment(mainContainer, 0f);
                        Debug.Log("LevelSelectDataController: No saved segment — snapped boat to MainRiver t=0.");
                        return;
                    }
                }
            }
            Debug.Log("LevelSelectDataController: No saved segment and no MainRiver found — boat stays on default.");
            return;
        }

        var segmentID = RiverSegmentRegistry.Instance?.GetSegment(savedSegmentID);
        if (segmentID == null)
        {
            Debug.LogWarning($"LevelSelectDataController: Segment '{savedSegmentID}' not found in registry.");
            return;
        }

        var container = segmentID.GetComponent<SplineContainer>();
        if (container == null)
        {
            Debug.LogWarning($"LevelSelectDataController: No SplineContainer on segment '{savedSegmentID}'.");
            return;
        }

        boatControl.RestoreToSegment(container, savedProgress);
        Debug.Log($"LevelSelectDataController: Boat restored to '{savedSegmentID}' at {savedProgress}");
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

    void WireJunctionNodes()
    {
        if (boatControl == null) return;

        var junctions = FindObjectsOfType<SplineRiverJunctionNodeV2>();
        foreach (var j in junctions)
            j.SetBoatControl(boatControl);

        if (junctions.Length > 0)
            Debug.Log($"[LevelSelectDataController] Wired LevelSelectBoatControl to {junctions.Length} junction(s).");
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

        var splineAnimate = boatGo.GetComponentInChildren<SplineAnimate>();
        var boatCollider  = boatGo.GetComponentInChildren<Collider>();

        // Find TheBoatVis by tag "Boat" for the flip mesh transform
        Transform meshTransform = null;
        foreach (Transform child in boatGo.GetComponentsInChildren<Transform>())
        {
            if (child.CompareTag("Boat")) { meshTransform = child; break; }
        }

        boatControl.WireBoatReferences(splineAnimate, boatCollider, boatGo.transform, meshTransform);
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