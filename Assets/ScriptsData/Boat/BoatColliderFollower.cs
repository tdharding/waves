using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class BoatColliderFollower : MonoBehaviour
{
    public Transform visual; // Assign BoatVisual here

    private BoxCollider col;
    private Vector3 initialCenter;

    void Start()
    {
        col = GetComponent<BoxCollider>();
        initialCenter = col.center;
    }

    void LateUpdate()
    {
        // Read visual bob height (local Y)
        float yOffset = visual.localPosition.y;

        // Apply to collider center (keep original X/Z)
        Vector3 c = initialCenter;
        c.y += yOffset;

        col.center = c;
    }
}
