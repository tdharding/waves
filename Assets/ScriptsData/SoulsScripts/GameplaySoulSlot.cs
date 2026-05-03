using UnityEngine;

public class GameplaySoulSlot : MonoBehaviour
{
    [Header("State")]
    [SerializeField] private bool filled;
    private int occupantSoulIdentity = -1; 

    [Header("Manager Reference")]
    // Point this to whatever script manages your tree's logic
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

    /// <summary>
    /// Called when a player/system inserts a soul into this specific tree slot.
    /// </summary>
    public bool TryInsertSoul(int soulIdentity)
    {
        if (filled) return false;

        filled = true;
        occupantSoulIdentity = soulIdentity;

        if (filledVisual != null)
            filledVisual.SetActive(true);

        // Tell the tree to update its orbs immediately
        if (treePlayer != null) treePlayer.RefreshState();

        return true;
    }

    /// <summary>
    /// Manual removal (e.g., player clicking the slot to take the soul back).
    /// </summary>
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