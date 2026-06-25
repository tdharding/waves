using UnityEngine;
using UnityEngine.Splines;
using System.Collections;

public class LevelSelectSplineManager : MonoBehaviour
{
    public static LevelSelectSplineManager Instance;

    [SerializeField] private SplineRiverManager _riverManager;

    [Header("Initial Advance")]
    [Tooltip("The first obstacle in the scene — the river will extrude up to its RiverStopPoint on load.")]
    [SerializeField] private LevelSelectObstacleManager _firstObstacle;

    private void Awake()
    {
        Instance = this;

        if (_riverManager == null)
        {
            _riverManager = SplineRiverManager.Instance ?? FindObjectOfType<SplineRiverManager>();
            if (_riverManager == null)
                Debug.LogError("[LevelSelectSplineManager] No SplineRiverManager found in scene.", this);
        }
    }

    private void Start()
    {
        if (_firstObstacle != null)
        {
            Vector3 stopPos = _firstObstacle.RiverStopPoint != null
                ? _firstObstacle.RiverStopPoint.position
                : _firstObstacle.transform.position;
            _riverManager?.AdvanceToPosition(stopPos);
            return;
        }

        // No obstacle pre-assigned — find the first one in the scene
        var firstObstacle = FindObjectOfType<LevelSelectPathObstacleObject>();
        if (firstObstacle != null)
        {
            _riverManager?.AdvanceToPosition(firstObstacle.transform.position);
        }
        else
        {
            // No obstacles at all — extrude the full river
            _riverManager?.AdvanceToT(1f);
        }
    }

    // Called after a skip-intro force-jump. Finds the nearest obstacle whose
    // river-stop T is strictly ahead of the current extrude T and advances to it.
    // Falls back to T=1 if nothing is ahead (no obstacle blocking the path).
    public void RefreshAdvance()
    {
        if (_riverManager == null) return;

        float currentT = _riverManager.MainCurrentT;
        var allObstacles = FindObjectsByType<LevelSelectObstacleManager>(FindObjectsSortMode.None);

        float bestT   = float.MaxValue;
        Vector3 bestPos = Vector3.zero;
        bool found = false;

        foreach (var obs in allObstacles)
        {
            Vector3 stopPos = obs.RiverStopPoint != null ? obs.RiverStopPoint.position : obs.transform.position;
            float t = _riverManager.WorldPositionToT(stopPos);
            if (t > currentT && t < bestT)
            {
                bestT   = t;
                bestPos = stopPos;
                found   = true;
            }
        }

        if (found)
        {
            Debug.Log($"[LevelSelectSplineManager] RefreshAdvance: advancing to next obstacle at T={bestT:F3}.");
            _riverManager.AdvanceToPosition(bestPos);
        }
        else
        {
            Debug.Log("[LevelSelectSplineManager] RefreshAdvance: no obstacle ahead — advancing to T=1.");
            _riverManager.AdvanceToT(1f);
        }
    }

    // Kept for any existing callers — delegates straight through.
    public void AdvanceSpline(float targetPercentage)
    {
        _riverManager?.AdvanceToT(targetPercentage / 100f);
    }

    public void AdvanceSplineToPosition(Vector3 worldPosition)
    {
        _riverManager?.AdvanceToPosition(worldPosition);
    }
}