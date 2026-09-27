using UnityEngine;

[RequireComponent(typeof(Collider))]
public class LevelSelectIntroEndTrigger : MonoBehaviour
{
    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("BoatPrefab")) return;

        FindObjectOfType<LevelSelectOpeningSequence>()?.NotifyEndTrigger();
        gameObject.SetActive(false);
    }
}
