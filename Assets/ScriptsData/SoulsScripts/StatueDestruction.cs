using UnityEngine;

public class StatueDestruction : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Root of the intact statue visuals — will be disabled on hit.")]
    public GameObject intactRoot;
    [Tooltip("Root of the broken pieces — must be disabled in the prefab. Each piece needs a Rigidbody.")]
    public GameObject brokenRoot;

    [Header("Explosion Physics")]
    public float explosionForce    = 400f;
    public float explosionRadius   = 3f;
    public float upwardsModifier   = 0.6f;
    public float randomTorque      = 5f;

    [Header("Cleanup")]
    public float destroyDelay = 5f;

    private bool _triggered;

    // Fired when this object is smashed (after the break physics are applied). A FishBowlTowerController
    // subscribes to this to release its bowl container. Statues leave it unsubscribed (no-op).
    public event System.Action<Vector3> OnTriggered;

    public void Trigger(Vector3 hitPosition)
    {
        if (_triggered) return;
        _triggered = true;

        if (intactRoot != null)
            intactRoot.SetActive(false);

        if (brokenRoot != null)
        {
            brokenRoot.SetActive(true);

            foreach (Rigidbody rb in brokenRoot.GetComponentsInChildren<Rigidbody>())
            {
                rb.AddExplosionForce(explosionForce, hitPosition, explosionRadius,
                                     upwardsModifier, ForceMode.Impulse);

                rb.AddTorque(Random.insideUnitSphere * randomTorque, ForceMode.Impulse);
            }
        }

        // Open the catch gate on this statue's guarded soul-fish zone
        var statueBehaviour = GetComponent<StatueBehaviour>();
        if (statueBehaviour != null) statueBehaviour.MarkDestroyed();

        if (intactRoot != null)  Destroy(intactRoot,  destroyDelay);
        if (brokenRoot != null)  Destroy(brokenRoot,  destroyDelay);

        // Notify any listener (e.g. FishBowlTowerController) that the structure has broken.
        OnTriggered?.Invoke(hitPosition);
    }
}
