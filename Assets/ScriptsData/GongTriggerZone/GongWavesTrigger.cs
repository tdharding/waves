using UnityEngine;

public class GongWavesTrigger : MonoBehaviour
{
    [Header("Runtime Resolved References")]
    public Transform boatTransform;
    public GongWavesController controller;

    private bool hasTriggered = false;

    private void Awake()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (boatTransform == null)
        {
            GameObject boat = GameObject.FindGameObjectWithTag("BoatPrefab");
            if (boat != null)
            {
                boatTransform = boat.transform;
            }
            else
            {
                Debug.LogWarning("GongWavesTrigger: Boat not found (tagged 'Boat').");
            }
        }

        if (controller == null)
        {
            controller = FindObjectOfType<GongWavesController>();
            if (controller == null)
            {
                Debug.LogWarning("GongWavesTrigger: GongWavesController not found.");
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasTriggered) return;
        if (boatTransform == null || controller == null) return;

        if (other.transform == boatTransform)
        {
            hasTriggered = true;
            controller.StartGongSequence();
        }
    }
}
