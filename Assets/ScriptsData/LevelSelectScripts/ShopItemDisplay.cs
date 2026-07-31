using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Collider))]
public class ShopItemDisplay : MonoBehaviour
{
    [Header("Item Data")]
    public string itemName = "Shop Item";
    public int price = 10;

    [Tooltip("Save ID for this purchase, e.g. \"Figurehead\". Leave blank for items that can be bought repeatedly.")]
    public string itemID = "";
    [Tooltip("Hide the item from the shop once it has been bought.")]
    public bool hideOnceOwned = true;

    [Header("Positioning")]
    [Tooltip("How far above the spawn point the item floats.")]
    public float verticalOffset = 0.5f;

    [Header("Bob")]
    [Tooltip("Peak distance above and below the resting position.")]
    public float bobAmplitude = 0.15f;
    [Tooltip("Full cycles per second.")]
    public float bobSpeed = 1.2f;
    [Tooltip("Phase offset in seconds — stagger multiple items so they don't bob in sync.")]
    public float bobPhase = 0f;

    [Header("Spin")]
    [Tooltip("Degrees per second around the world-up axis.")]
    public float spinSpeed = 60f;

    [Header("Click")]
    public UnityEvent onClick;

    private Vector3 _baseLocalPosition;

    private void Start()
    {
        _baseLocalPosition      = transform.localPosition + Vector3.up * verticalOffset;
        transform.localPosition = _baseLocalPosition;

        if (hideOnceOwned && GameProgressData.IsItemPurchased(itemID))
            gameObject.SetActive(false);
    }

    private void Update()
    {
        float bob = Mathf.Sin((Time.time + bobPhase) * bobSpeed * Mathf.PI * 2f) * bobAmplitude;
        transform.localPosition = _baseLocalPosition + Vector3.up * bob;
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }

    private void OnMouseEnter()
    {
        ShopItemTooltipHUD.Instance?.Show(transform, price);
    }

    private void OnMouseExit()
    {
        ShopItemTooltipHUD.Instance?.Hide();
    }

    private void OnMouseDown()
    {
        // Don't trigger if clicking through UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        int currentOrbs = GameProgressData.GetCollectedOrbs();
        
        if (currentOrbs >= price)
        {
            PortalConfirmUI.Instance?.Show(
                $"Buy {itemName} for {price} Orbs?",
                () => BuyItem(),
                null);
        }
        else
        {
            PortalConfirmUI.Instance?.Show(
                "You don't have enough Orbs",
                null,
                null,
                showCancel: false,
                yesLabel: "OK");
        }
    }

    private void BuyItem()
    {
        if (OrbCollectCounter.SpendOrbs(price))
        {
            Debug.Log($"[ShopItemDisplay] Bought {itemName} for {price} Orbs.");
            GameProgressData.PurchaseItem(itemID);
            onClick.Invoke();

            if (hideOnceOwned && !string.IsNullOrEmpty(itemID))
            {
                ShopItemTooltipHUD.Instance?.Hide();
                gameObject.SetActive(false);
            }
        }
    }
}
