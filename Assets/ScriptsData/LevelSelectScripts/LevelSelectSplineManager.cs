using UnityEngine;
using UnityEngine.Splines;
using System.Collections;

public class LevelSelectSplineManager : MonoBehaviour
{
    public static LevelSelectSplineManager Instance;

    [SerializeField] private SplineRiverManager _riverManager;

    [Header("Initial Advance")]
    [Tooltip("The first obstacle in the scene — the river will extrude up to this point on load.")]
    [SerializeField] private Transform _firstObstacleTransform;

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
        if (_firstObstacleTransform != null)
        {
            _riverManager?.AdvanceToPosition(_firstObstacleTransform.position);
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