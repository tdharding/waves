using UnityEngine;

public class SnakeHeadFollow : MonoBehaviour
{
    public BadGuySnakeMovement snake;
    public Transform headVisual;
    public Transform boatVisualOverride;

    [Header("Rotation")]
    public float turnSpeed = 5f;

    [Header("Position")]
    public float forwardOffset = 1f;

    private Transform target;

    void Start()
    {
        TryResolveTarget();
    }

    void TryResolveTarget()
    {
        if (boatVisualOverride != null)
        {
            target = boatVisualOverride;
            return;
        }

        if (snake != null && snake.boat != null)
        {
            target = snake.boat;
            return;
        }

        if (LevelDataController.Instance != null)
        {
            target = LevelDataController.Instance.GetBoatRoot();
        }
    }

    void LateUpdate()
    {
        if (snake == null || snake.p0 == null)
            return;

        // Calculate forward direction from p0 toward p1
        Vector3 moveDirection = Vector3.zero;
        if (snake.p1 != null)
        {
            moveDirection = (snake.p0.position - snake.p1.position);
            moveDirection.y = 0f;
            moveDirection.Normalize();
        }

        transform.position = snake.p0.position + moveDirection * forwardOffset;

        if (target == null)
            TryResolveTarget();

      if (headVisual != null)
{
    if (snake.IsChasing && target != null)
    {
        // ── Look at boat ──
        Vector3 directionToBoat = target.position - headVisual.position;
        directionToBoat.y = 0f;

        if (directionToBoat.sqrMagnitude > 0.001f)
        {
            float angle = Mathf.Atan2(directionToBoat.x, directionToBoat.z) * Mathf.Rad2Deg;
            Quaternion targetRotation = Quaternion.Euler(0f, angle + 180f, 0f);
            headVisual.rotation = Quaternion.RotateTowards(headVisual.rotation, targetRotation, turnSpeed * Time.deltaTime);
        }
    }
    else if (moveDirection.sqrMagnitude > 0.001f)
    {
        // ── Face direction of travel ──
        float angle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
        Quaternion targetRotation = Quaternion.Euler(0f, angle + 180f, 0f);
        headVisual.rotation = Quaternion.RotateTowards(headVisual.rotation, targetRotation, turnSpeed * Time.deltaTime);
    }
}
    }
}