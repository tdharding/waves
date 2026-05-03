using UnityEngine;
using UnityEngine.InputSystem;

public class BoatPlayerScript : MonoBehaviour
{
    [Header("Movement Settings")]
    public float maxSpeed = 6f;
    public float acceleration = 4f;
    public float deceleration = 3f;

    [Header("Rotation Settings")]
    public float turnSpeed = 120f; // degrees per second

    private float currentSpeed = 0f;
    private Vector3 targetDirection;

    void Update()
    {
        UpdateTargetDirection();
        RotateBoat();
        MoveBoat();
    }

    void UpdateTargetDirection()
    {
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = Camera.main.ScreenPointToRay(mousePos);

        Plane plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);

            targetDirection = (hitPoint - transform.position);
            targetDirection.y = 0f;
            targetDirection.Normalize();
        }
    }

    void RotateBoat()
    {
        if (targetDirection.sqrMagnitude < 0.001f)
            return;

        Quaternion targetRot = Quaternion.LookRotation(targetDirection);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRot,
            turnSpeed * Time.deltaTime
        );
    }

    void MoveBoat()
    {
        float turnAmount = Quaternion.Angle(transform.rotation, Quaternion.LookRotation(targetDirection));
        float turnSlowdown = Mathf.Lerp(1f, 0.7f, turnAmount / 90f);

        currentSpeed += acceleration * Time.deltaTime;
        currentSpeed = Mathf.Min(currentSpeed, maxSpeed * turnSlowdown);

        transform.position += transform.forward * currentSpeed * Time.deltaTime;
    }
}
