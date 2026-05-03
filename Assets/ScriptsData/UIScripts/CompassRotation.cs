using UnityEngine;

public class CompassRotation : MonoBehaviour
{
    [Header("References")]
    public Transform boatTransform;      
    public Transform compassObject;      

    [Header("Settings")]
    public float smoothSpeed = 10f;

    private float currentAngle;

    // Offset between boat yaw and compass alignment
    private const float OFFSET = 60f;

    void Update()
    {
        if (boatTransform == null || compassObject == null)
            return;

        // Boat yaw
        float boatY = boatTransform.eulerAngles.y;

        // Convert to compass angle
        float targetAngle = boatY + OFFSET;

        // Smooth rotation
        currentAngle = Mathf.LerpAngle(
            currentAngle,
            targetAngle,
            Time.deltaTime * smoothSpeed
        );

        // APPLY ROTATION ON THE LOCAL Y AXIS
        compassObject.localRotation = Quaternion.Euler(0f, currentAngle, 0f);
    }
}
