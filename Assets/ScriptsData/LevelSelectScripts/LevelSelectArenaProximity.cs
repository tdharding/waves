using UnityEngine;

/// <summary>
/// Place this on a child GameObject with a large trigger Collider around the arena.
/// When the boat (or any collider tagged <see cref="boatTag"/>) enters the zone,
/// the shared <see cref="LevelSelectSoulFishDisplay"/> is told to show the soul fish
/// count for this arena's level.  When the boat leaves (and no arena overrides it),
/// the display is hidden.
///
/// Requires:
///   - A Collider (Is Trigger = true) on the same GameObject.
///   - <see cref="arenaController"/> assigned to the parent LevelSelectArenaController.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LevelSelectArenaProximity : MonoBehaviour
{
    [Tooltip("The arena this proximity zone belongs to.")]
    public LevelSelectArenaController arenaController;

    [Tooltip("Tag used to identify the boat. Must match the boat GameObject's tag.")]
    public string boatTag = "Boat";

    private void Awake()
    {
        // Ensure the collider is set to trigger
        var col = GetComponent<Collider>();
        if (!col.isTrigger)
        {
            col.isTrigger = true;
            Debug.LogWarning($"[ArenaProximity] '{name}': Collider was not set to trigger — fixed automatically.");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[ArenaProximity] '{name}' TriggerEnter: '{other.name}' (tag='{other.tag}')");

        if (!other.CompareTag(boatTag)) return;
        if (arenaController == null)
        {
            Debug.LogWarning($"[ArenaProximity] '{name}': arenaController not assigned.");
            return;
        }

        string levelID = arenaController.gridData != null ? arenaController.gridData.levelID : "null";
        Debug.Log($"[ArenaProximity] '{name}': Showing display for level '{levelID}'.");
        arenaController.ShowSoulFishDisplay();
    }

    private void OnTriggerExit(Collider other)
    {
        Debug.Log($"[ArenaProximity] '{name}' TriggerExit: '{other.name}' (tag='{other.tag}')");

        if (!other.CompareTag(boatTag)) return;

        string levelID = arenaController?.gridData != null ? arenaController.gridData.levelID : "null";
        Debug.Log($"[ArenaProximity] '{name}': Hiding display (was showing level '{levelID}').");
        arenaController.HideSoulFishDisplay();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.15f);
        if (col is SphereCollider sc)
            Gizmos.DrawSphere(transform.position, sc.radius * transform.lossyScale.x);
        else if (col is BoxCollider bc)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(bc.center, bc.size);
        }
    }
#endif
}
