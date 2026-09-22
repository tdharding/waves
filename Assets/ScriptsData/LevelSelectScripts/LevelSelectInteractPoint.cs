using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Somewhere on the map worth stopping at: stand the boat near it and a prompt comes up, press
/// the interact key and whatever sits beside it — a poster, a conversation — runs.
///
/// The point itself holds only how near is near enough and what the prompt reads. What happens
/// is the <see cref="LevelSelectInteractAction"/> on the same object, so the display points on
/// a rim node, an outpost's characters and anything added later all work off this one script.
///
/// Placed and filled in by the Level Select Designer, on the prefab it stands up. Every point
/// signs itself in here as it wakes, so the boat's <see cref="LevelSelectInteractor"/> can look
/// them over without hunting the scene each frame.
/// </summary>
public class LevelSelectInteractPoint : MonoBehaviour
{
    // ─────────────────────────────────────────────
    // THE ONES IN THE SCENE
    // ─────────────────────────────────────────────

    private static readonly List<LevelSelectInteractPoint> Live = new();

    /// <summary>Every interact point currently in the scene. Do not hold onto it — it changes
    /// as pieces of the map are turned on and off.</summary>
    public static IReadOnlyList<LevelSelectInteractPoint> All => Live;

    // ─────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────

    [Tooltip("What the prompt reads when the boat is near enough. The key itself is added by " +
             "the prompt, so this is just the doing — \"Look at the poster\".")]
    public string prompt = "Look";

    [Tooltip("How near the boat has to be, measured flat across the water — the height it " +
             "stands at makes no difference.")]
    [Min(0f)] public float radius = 3f;

    [Tooltip("Where the boat is measured to, when the point should be answered from somewhere " +
             "other than this object — the foot of a display, the middle of a platform. Left " +
             "empty, it is this object.")]
    public Transform measureFrom;

    // ─────────────────────────────────────────────
    // LIFECYCLE
    // ─────────────────────────────────────────────

    private LevelSelectInteractAction _action;

    /// <summary>What this point does. Found once, beside it.</summary>
    public LevelSelectInteractAction Action
    {
        get
        {
            if (_action == null) _action = GetComponent<LevelSelectInteractAction>();
            return _action;
        }
    }

    private void OnEnable()
    {
        Live.Add(this);

        if (Action == null)
            Debug.LogWarning($"[LevelSelectInteractPoint] '{name}' has nothing beside it to do — " +
                             "add an interact action component and it will work.", this);
    }

    private void OnDisable() => Live.Remove(this);

    // ─────────────────────────────────────────────
    // REACH
    // ─────────────────────────────────────────────

    public Vector3 Position => measureFrom != null ? measureFrom.position : transform.position;

    /// <summary>How far the boat is, flat across the water.</summary>
    public float FlatDistanceTo(Vector3 worldPosition)
    {
        Vector3 here = Position;
        here.y = 0f;
        worldPosition.y = 0f;
        return Vector3.Distance(here, worldPosition);
    }

    /// <summary>Whether this point can be interacted with from where the boat is standing.</summary>
    public bool InReach(Vector3 boatPosition) =>
        Action != null && FlatDistanceTo(boatPosition) <= radius;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.8f);
        Vector3 at = Position;

        // Flat across the water, the way the reach is measured — a ring lying on the surface
        // rather than a sphere standing in the air.
        const int steps = 48;
        Vector3 previous = at + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= steps; i++)
        {
            float angle = i / (float)steps * Mathf.PI * 2f;
            Vector3 next = at + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
#endif
}
