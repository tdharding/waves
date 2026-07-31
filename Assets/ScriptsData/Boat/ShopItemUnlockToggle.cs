using UnityEngine;

/// <summary>
/// Shows or hides a GameObject depending on whether a shop item has been bought.
/// Put this on the boat (or any persistent object), set <see cref="itemID"/> to match
/// the ShopItemDisplay's itemID, and drag the disabled prefab instance into <see cref="target"/>.
/// </summary>
public class ShopItemUnlockToggle : MonoBehaviour
{
    /// <summary>Shared ID so the shop, the boat and the tester tool all agree.</summary>
    public const string FigureheadItemID = "Figurehead";

    [Tooltip("Save ID of the purchase, must match the ShopItemDisplay's itemID (e.g. \"Figurehead\").")]
    public string itemID = FigureheadItemID;

    [Tooltip("Object to enable once the item has been bought. Leave it disabled in the scene/prefab.")]
    public GameObject target;

    private void Awake()
    {
        if (target == null) return;
        Apply(GameProgressData.IsItemPurchased(itemID));
    }

    private void OnEnable()
    {
        GameProgressData.ShopItemOwnershipChanged += OnOwnershipChanged;
    }

    private void OnDisable()
    {
        GameProgressData.ShopItemOwnershipChanged -= OnOwnershipChanged;
    }

    private void OnOwnershipChanged(string changedID, bool owned)
    {
        if (changedID == itemID) Apply(owned);
    }

    private void Apply(bool owned)
    {
        if (target != null) target.SetActive(owned);
    }
}
