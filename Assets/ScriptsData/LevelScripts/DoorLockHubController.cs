using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Sits on the DoorLockHub prefab. Receives soul delivery and unlocks the linked door.
/// Assign pipeConnector in the prefab Inspector. linkedDoor is wired by LevelSpawner at runtime.
/// </summary>
public class DoorLockHubController : MonoBehaviour
{
    [Tooltip("The PipeConnector child SplineContainer — assign in prefab Inspector.")]
    public SplineContainer pipeConnector;

    [Tooltip("Renderer on the hub whose emission is set black when locked, emissive when unlocked.")]
    public Renderer emissionRenderer;

    [Tooltip("HDR emission colour applied to the hub when unlocked.")]
    public Color unlockedEmission = Color.white * 8f;

    [HideInInspector] public LockedDoorController linkedDoor;

    private void Start()
    {
        ApplyEmission(locked: true);
    }

    public void OnSoulArrived()
    {
        ApplyEmission(locked: false);
        linkedDoor?.Unlock();
    }

    private void ApplyEmission(bool locked)
    {
        if (emissionRenderer == null) return;
        Color emission = locked ? Color.black : unlockedEmission;
        var mat = emissionRenderer.material;
        mat.SetColor("_EmissionColor", emission);
        if (locked)
            mat.DisableKeyword("_EMISSION");
        else
            mat.EnableKeyword("_EMISSION");
    }
}
