using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

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
        WireCameraFollowTarget();
    }

    void Start()
    {
        RestoreBoatPosition();
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
            Debug.Log("LevelSelectDataController: No saved segment — boat stays on default.");
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
        string levelID = LevelSelectionCache.JustExitedLevelID;
        Debug.Log($"[LevelSelectDataController] JustExitedLevelID='{levelID}'  SplineRiverManager={(SplineRiverManager.Instance != null ? "found" : "NULL")}");

        if (string.IsNullOrEmpty(levelID)) return;

        SplineRiverManager.Instance?.NotifyLevelExited(levelID);
        LevelSelectionCache.JustExitedLevelID = string.Empty;
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

    void WireBoatReferences()
    {
        if (boatControl == null) return;

        var playerBoat = GameObject.Find("PlayerBoat");
        if (playerBoat == null || playerBoat.transform.childCount == 0) return;

        var boatGo = playerBoat.transform.GetChild(0).gameObject;

        var splineAnimate = boatGo.GetComponentInChildren<SplineAnimate>();
        var boatCollider  = boatGo.GetComponentInChildren<Collider>();
        var meshTransform = boatGo.transform.childCount > 0 ? boatGo.transform.GetChild(0) : null;

        boatControl.WireBoatReferences(splineAnimate, boatCollider, boatGo.transform, meshTransform);
        Debug.Log("[LevelSelectDataController] Boat references wired from PlayerBoat.");
    }

    void WireCameraFollowTarget()
    {
        var camController = FindObjectOfType<LevelSelectCameraController>();
        if (camController == null || camController.cam == null) return;

        var playerBoat = GameObject.Find("PlayerBoat");
        if (playerBoat != null && playerBoat.transform.childCount > 0)
        {
            camController.cam.Follow = playerBoat.transform.GetChild(0);
            Debug.Log("[LevelSelectDataController] Camera follow target set to LevelSelectBoat.");
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