using UnityEngine;

public class LockedDoorController : MonoBehaviour
{
    [Tooltip("Entrance ID from the grid designer — used as the save key.")]
    public string entranceID;

    [Tooltip("If true this door starts locked and requires a soul delivery to open.")]
    public bool isLocked = true;

    [Tooltip("Renderer whose _EmissionColor is set black when locked, emissive when unlocked.")]
    public Renderer emissionRenderer;

    [Tooltip("HDR emission colour applied when unlocked.")]
    public Color unlockedEmission = Color.white * 8f;

    private Collider _collider;

    private void Start()
    {
        _collider = GetComponent<Collider>();

        if (isLocked && IsSavedUnlocked())
            isLocked = false;

        ApplyState();
    }

    public void Unlock()
    {
        if (!isLocked) return;
        isLocked = false;
        SaveUnlocked();
        ApplyState();
    }

    private void ApplyState()
    {
        if (_collider != null) _collider.enabled = !isLocked;

        Color emission = isLocked ? Color.black : unlockedEmission;
        if (emissionRenderer != null)
        {
            var mat = emissionRenderer.materials[1];
            mat.SetColor("_EmissionColor", emission);
            if (isLocked)
                mat.DisableKeyword("_EMISSION");
            else
                mat.EnableKeyword("_EMISSION");
        }
    }

    private string SaveKey()
    {
        string levelID = LevelSelectionCache.SelectedGridData?.levelID ?? string.Empty;
        return $"{levelID}_{entranceID}";
    }

    private bool IsSavedUnlocked() => GameProgressData.IsUnlocked(SaveKey());

    private void SaveUnlocked() => GameProgressData.UnlockObstacle(SaveKey());
}
