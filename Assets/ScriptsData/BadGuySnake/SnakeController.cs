using UnityEngine;
using UnityEngine.VFX;

public class SnakeController : MonoBehaviour
{
    private BadGuySnakeMovement movement;
    private SnakeHeadFollow headFollow;
    private VisualEffect snakeVFX;

    public void OnSnakeSpawned(GameObject snake)
    {
        movement = snake.GetComponentInChildren<BadGuySnakeMovement>();
        headFollow = snake.GetComponentInChildren<SnakeHeadFollow>();
        snakeVFX = snake.GetComponentInChildren<VisualEffect>();

        if (movement != null) movement.enabled = false;
        if (headFollow != null) headFollow.enabled = false;
        if (snakeVFX != null) snakeVFX.enabled = false;

        Debug.Log($"[SnakeController] movement: {movement != null}, headFollow: {headFollow != null}, vfx: {snakeVFX != null}");
    }

    public void Initialise()
    {
        if (movement != null) movement.enabled = true;
        if (headFollow != null) headFollow.enabled = true;
        if (snakeVFX != null) snakeVFX.enabled = true;
    }
}