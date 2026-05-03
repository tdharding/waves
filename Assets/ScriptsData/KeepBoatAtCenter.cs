using UnityEngine;

public class KeepBoatAtCenter : MonoBehaviour
{
    private Vector3 initialLocalPos;

    void Awake()
    {
        // Record whatever the boat's centered position should be
        initialLocalPos = transform.localPosition;
    }

    void LateUpdate()
    {
        // Force the boat to remain at the plane's local center
        transform.localPosition = initialLocalPos;
        transform.localRotation = Quaternion.identity; // optional: prevent rotation drift
    }
}
