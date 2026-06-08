using UnityEngine;

public class SoulEnterPipeTrigger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SoulFishInputTube inputTube;
    private bool interactable = true;

    public bool TryInsertSoul(int identity)
    {
        if (!interactable || inputTube == null)
        {
            Debug.LogWarning($"[SoulEnterPipeTrigger] {gameObject.name} rejected soul {identity}. Interactable: {interactable}");
            return false;
        }

        if (inputTube.IsBusy)
        {
            Debug.Log($"[SoulEnterPipeTrigger] {gameObject.name} input tube is busy.");
            return false;
        }

        inputTube.StartSoulDelivery(identity);
        return true;
    }

    private void Awake()
    {
        if (inputTube == null)
            inputTube = GetComponentInParent<SoulFishInputTube>();
    }

    public void SetLocked(bool state)
    {
        interactable = !state;
        Debug.Log($"[SoulEnterPipeTrigger] {gameObject.name} locked: {state}");
    }
}
