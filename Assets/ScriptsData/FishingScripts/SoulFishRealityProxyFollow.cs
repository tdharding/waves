using UnityEngine;

public class SoulFishRealityProxyFollow : MonoBehaviour
{
    [Header("Follow Target")]
    public Transform soulFish;

    [Header("Vertical Offset")]
    [Tooltip("Additional vertical offset below the reality layer")]
    public float verticalOffset = -1.716f;

    [Header("Fixed Reality Layer Y")]
    [Tooltip("Base Y position of the reality layer")]
    public float realityLayerY;

    [Header("Debug")]
    [SerializeField] private bool hasBeenAssigned;
    [SerializeField] private string assignedFishName;

    public void AssignFish(Transform fish)
    {
        soulFish = fish;
        hasBeenAssigned = fish != null;
        assignedFishName = fish ? fish.name : "<null>";
    }

public void OnFishDestroyed()
{
    if (!this) return;   // Unity-safe destroyed check
    Destroy(gameObject);
}

    void LateUpdate()
    {
        if (!hasBeenAssigned || soulFish == null)
            return;

        transform.position = new Vector3(
            soulFish.position.x,
            realityLayerY + verticalOffset,
            soulFish.position.z
        );
    }
}
