using UnityEngine;

public class LevelSelectPathObstacleObject : MonoBehaviour
{
    public enum BlockingType
    {
        ForwardOnly,    // Standard for river tip barriers
        BackwardOnly,
        BothWays        // For permanent gates/walls
    }

    [Header("Identification")]
    public string obstacleID;

    [Header("Behavior")]
    public BlockingType blockingType = BlockingType.ForwardOnly;
}