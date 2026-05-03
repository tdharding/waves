using UnityEngine;

public class OrbOfOmalonCollect : MonoBehaviour
{
private void OnTriggerEnter(Collider other)
{
    if (!other.CompareTag("BoatPrefab"))
        return;

    OrbCollectCounter.AddOrb(); // ← add this
    Destroy(transform.parent.gameObject);
}
}
