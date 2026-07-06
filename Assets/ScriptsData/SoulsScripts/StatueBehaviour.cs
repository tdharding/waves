using UnityEngine;

// A statue guards a soul-fish zone: fish swim its authored ring but can't be caught
// until the statue is destroyed (see SoulShoalController). The ring geometry is authored
// in the Grid Designer — this component only carries the link id and marks the object as
// a statue. Destruction is handled by StatueDestruction.
public class StatueBehaviour : MonoBehaviour
{
    [Tooltip("Set by LevelSpawner from the placement. Matches the guarding SoulZone.linkedStatueId.")]
    public int statueId;

    // Flipped by StatueDestruction when the statue breaks. Guarded fish become catchable
    // the moment this is true (see FishFishingBehaviour / SoulShoalController).
    public bool IsDestroyed { get; private set; }
    public void MarkDestroyed() => IsDestroyed = true;

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        UnityEditor.Handles.color = new Color(0.6f, 0.2f, 1f, 0.9f);
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.3f, $"Statue #{statueId}");
    }
#endif
}
