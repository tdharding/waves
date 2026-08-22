using UnityEngine;
using UnityEngine.EventSystems;

public class LevelSelectDraggableSoul : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Time Warp")]
    [SerializeField] private float heldTimeScale = 0.3f;

    private bool isHolding;
    private bool justPickedUp;
    private RectTransform rect;
    private Vector2 startPosition;
    private Transform startParent;

    public int soulDataIdentity = -1;

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        startParent = transform.parent;
        startPosition = rect.anchoredPosition;
    }

    /// <summary>
    /// Called by SoulsOnBoatDisplayManager after spawning to assign the home slot position.
    /// Overrides the Awake-captured startPosition.
    /// </summary>
    public void SetHomePosition(Transform parent, Vector2 anchoredPosition)
    {
        startParent   = parent;
        startPosition = anchoredPosition;
        transform.SetParent(parent, false);
        rect.anchoredPosition = anchoredPosition;
    }

    private void OnDestroy()
    {
        if (isHolding)
        {
            Time.timeScale = 1f;
            SoulPickupEvents.FireReleased();
        }
    }

    // --- MOUSE OVER ---

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isHolding && soulDataIdentity != -1)
            VideoPlayerController.Instance?.StartPreview(soulDataIdentity);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        VideoPlayerController.Instance?.StopPreview();
    }

    // --- DRAG ---

    private void Update()
    {
        if (!isHolding) return;

        rect.position = Input.mousePosition;

        if (justPickedUp)
        {
            if (!Input.GetMouseButton(0))
                justPickedUp = false;
            return;
        }

        if (Input.GetMouseButtonDown(0))
            TryPlaceSoul();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (isHolding) return;
        isHolding = true;
        justPickedUp = true;
        transform.SetAsLastSibling();
        Time.timeScale = heldTimeScale;
        SoulPickupEvents.FirePickedUp();
    }

    private void TryPlaceSoul()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int interactionMask = LayerMask.GetMask("Interaction");
        Debug.Log($"[DraggableSoul] TryPlaceSoul — identity={soulDataIdentity}, mousePos={Input.mousePosition}");

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, interactionMask))
        {
            Debug.Log($"[DraggableSoul] Raycast hit: '{hit.collider.gameObject.name}' (layer={LayerMask.LayerToName(hit.collider.gameObject.layer)})");

            GameplaySoulSlot gameplaySlot = hit.collider.GetComponent<GameplaySoulSlot>();
            if (gameplaySlot != null)
            {
                bool inserted = gameplaySlot.TryInsertSoul(this.soulDataIdentity);
                Debug.Log($"[DraggableSoul] GameplaySoulSlot '{hit.collider.gameObject.name}' TryInsert → {inserted}");
                if (inserted) { OnPlacedSuccessfully(); return; }
            }

            SoulSlot soulSlot = hit.collider.GetComponent<SoulSlot>();
            if (soulSlot != null)
            {
                bool inserted = soulSlot.TryInsertSoul(this.soulDataIdentity);
                Debug.Log($"[DraggableSoul] SoulSlot '{hit.collider.gameObject.name}' TryInsert → {inserted}");
                if (inserted) { OnPlacedSuccessfully(); return; }
            }

            SoulEnterPipeTrigger pipeTrigger = hit.collider.GetComponent<SoulEnterPipeTrigger>();
            if (pipeTrigger != null)
            {
                var indicator = pipeTrigger.GetComponent<SoulSlotIndicator>()
                             ?? pipeTrigger.GetComponentInParent<SoulSlotIndicator>();
                bool inRange = indicator == null || indicator.IsInRange();
                if (!inRange)
                {
                    Debug.Log($"[DraggableSoul] SoulEnterPipeTrigger '{hit.collider.gameObject.name}' out of range — rejected.");
                    ReturnToInventory();
                    return;
                }
                bool inserted = pipeTrigger.TryInsertSoul(this.soulDataIdentity);
                Debug.Log($"[DraggableSoul] SoulEnterPipeTrigger '{hit.collider.gameObject.name}' TryInsert → {inserted}");
                if (inserted) { OnPlacedSuccessfully(); return; }
            }

            // Street light bowls take a soul the same way as pipe triggers (range-gated, no tube)
            StreetLightController streetLight = hit.collider.GetComponent<StreetLightController>()
                                             ?? hit.collider.GetComponentInParent<StreetLightController>();
            if (streetLight != null)
            {
                var lightIndicator = streetLight.GetComponent<SoulSlotIndicator>()
                                  ?? streetLight.GetComponentInParent<SoulSlotIndicator>();
                bool lightInRange = lightIndicator == null || lightIndicator.IsInRange();
                if (!lightInRange)
                {
                    Debug.Log($"[DraggableSoul] StreetLight '{hit.collider.gameObject.name}' out of range — rejected.");
                    ReturnToInventory();
                    return;
                }
                bool inserted = streetLight.TryInsertSoul(this.soulDataIdentity);
                Debug.Log($"[DraggableSoul] StreetLight '{hit.collider.gameObject.name}' TryInsert → {inserted}");
                if (inserted) { OnPlacedSuccessfully(); return; }
            }

            if (gameplaySlot == null && soulSlot == null && pipeTrigger == null && streetLight == null)
Debug.Log($"[DraggableSoul] Hit object has neither GameplaySoulSlot nor SoulSlot component.");
        }
        else
        {
            Debug.Log("[DraggableSoul] Raycast missed — no Interaction layer collider hit.");
        }

        ReturnToInventory();
    }

    private void OnPlacedSuccessfully()
    {
        Debug.Log($"[DraggableSoul] OnPlacedSuccessfully — identity={soulDataIdentity}");
        Time.timeScale = 1f;
        SoulPickupEvents.FireReleased();
        SoulConsumption.SpendSoulFromBoat(soulDataIdentity);
        Destroy(gameObject);
    }

    private void ReturnToInventory()
    {
        isHolding = false;
        Time.timeScale = 1f;
        SoulPickupEvents.FireReleased();
        transform.SetParent(startParent);
        rect.anchoredPosition = startPosition;
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(startParent.GetComponent<RectTransform>());
    }
}
