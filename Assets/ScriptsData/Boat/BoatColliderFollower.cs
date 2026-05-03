using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class BoatColliderFollower : MonoBehaviour
{
    public Transform visual; // Assign BoatVisual here

    private CharacterController cc;
    private Vector3 initialCenter;

    void Start()
    {
        cc = GetComponent<CharacterController>();
        initialCenter = cc.center;
    }

    void LateUpdate()
    {
        // Read visual bob height (local Y)
        float yOffset = visual.localPosition.y;

        // Apply to collider center (keep original X/Z)
        Vector3 c = initialCenter;
        c.y += yOffset;

        cc.center = c;
    }
}
