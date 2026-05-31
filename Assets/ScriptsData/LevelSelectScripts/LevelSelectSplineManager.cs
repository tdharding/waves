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