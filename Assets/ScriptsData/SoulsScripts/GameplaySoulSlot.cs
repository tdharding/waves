using UnityEngine;

public class GameplaySoulSlot : MonoBehaviour
{
    [Header("State")]
    [SerializeField] private bool filled;
    private int occupantSoulIdentity = -1; 

    [Header("Memory Tree Association (Optional)")]
    [SerializeField] private MemoryTreePlayer treePlayer;

    [Header("Visuals")]
    [SerializeField] private GameObject filledVisual;
    [SerializeField] private float interactionRadius = 2f;

    public bool IsFilled => filled;
    public int SoulIdentity => occupantSoulIdentity;

    private void Start()
    {
        if (filledVisual != null)
            filledVisual.SetActive(filled);
    }

    public bool TryInsertSoul(int soulIdentity)
    {
        if (filled) return false;

        filled = true;
        occupantSoulIdentity = soulIdentity;

        if (filledVisual != null)
            filledVisual.SetActive(true);

        if (treePlayer != null) treePlayer.RefreshState();

        return true;
    }

    private void OnMouseDown()
    {
        if (!filled) return;

        int identityToReturn = occupantSoulIdentity;

        GameProgressData.AddSoulToBoat(identityToReturn);
        SoulsOnBoatDisplayManager.Instance?.ReturnSoulToDisplay(identityToReturn);

        ClearSlot();
    }

    public void ClearSlot()
    {
        filled = false;
        occupantSoulIdentity = -1;

        if (filledVisual != null)
            filledVisual.SetActive(false);

        if (treePlayer != null) treePlayer.RefreshState();
    }
}