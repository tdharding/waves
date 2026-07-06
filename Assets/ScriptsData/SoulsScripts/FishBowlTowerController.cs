using UnityEngine;

// Lives on a FishBowlTower prefab. The tower holds a spherical "fish bowl" aloft; its soul-fish
// shoal container is spawned up in the bowl by LevelSpawner (see SoulShoalController bowl mode).
//
// This component is a thin relay: when the tower's StatueDestruction fires (catapult smash) it
// cuts the bowl container loose so it drops into the water. Catchability and water-snapping reopen
// when the container lands (handled by SoulShoalController), NOT when the tower breaks — so the
// tower carries StatueDestruction but no StatueBehaviour.
//
// The bowl is defined entirely by the prefab: assign the bowl object as `bowlCenter` and set the
// swim `bowlRadius`. Fish spawn at the bowl's TRUE world position and are contained within that
// radius — there is no height number to guess or keep in sync.
public class FishBowlTowerController : MonoBehaviour
{
    [Header("Bowl")]
    [Tooltip("The centre of the fish bowl — fish swim around this point. Assign the bowl sphere " +
             "(or an empty at its centre). Fish spawn exactly here, so no height field is needed.")]
    public Transform bowlCenter;

    [Tooltip("Swim radius around the bowl centre (world units). Fish stay within this — keep it just " +
             "inside the visible bowl so they don't clip through the glass.")]
    public float bowlRadius = 1f;

    // World-space centre of the bowl (falls back to this transform if bowlCenter is unassigned).
    public Vector3 BowlCenterWorld => bowlCenter != null ? bowlCenter.position : transform.position;

    // Swim radius in world units, scaled by the tower's overall scale so a scaled tower still fits.
    public float BowlWorldRadius => bowlRadius * transform.lossyScale.x;

    // Set by LevelSpawner once the shoal container for this tower's zone is spawned.
    private SoulShoalController _container;

    // Set true when StatueDestruction has fired; guards against re-subscribing / double release.
    private StatueDestruction _destruction;

    void Awake()
    {
        _destruction = GetComponentInChildren<StatueDestruction>(true);
        if (_destruction != null)
            _destruction.OnTriggered += OnTowerSmashed;
        else
            Debug.LogWarning("[FishBowlTower] No StatueDestruction found — the bowl will never drop.", this);
    }

    void OnDestroy()
    {
        if (_destruction != null)
            _destruction.OnTriggered -= OnTowerSmashed;
    }

    // Called by LevelSpawner to link the aloft shoal container to its tower.
    public void SetContainer(SoulShoalController container)
    {
        _container = container;
    }

    private void OnTowerSmashed(Vector3 hitPosition)
    {
        Debug.Log($"[FishBowlTower] Tower smashed — releasing bowl on '{name}'.", this);
        if (_container != null)
            _container.ReleaseBowl();
        else
            Debug.LogWarning("[FishBowlTower] Tower smashed but no container linked — bowl won't drop.", this);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 centre = BowlCenterWorld;
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireSphere(centre, BowlWorldRadius);
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.25f);
        Gizmos.DrawLine(transform.position, centre);
    }
#endif
}
