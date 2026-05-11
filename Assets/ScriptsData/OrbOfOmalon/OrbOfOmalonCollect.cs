using UnityEngine;

public class OrbOfOmalonCollect : MonoBehaviour
{
private void OnTriggerEnter(Collider other)
{
    if (!other.CompareTag("BoatPrefab"))
        return;

    OrbsOfOmalonCounter.AddOrb(); // ← add this
Destroy(transform.parent.gameObject);
}
}
