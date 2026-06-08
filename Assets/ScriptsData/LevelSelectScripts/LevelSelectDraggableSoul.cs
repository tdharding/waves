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
            Time.timeScale = 1f;
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
                bool inserted = pipeTrigger.TryInsertSoul(this.soulDataIdentity);
                Debug.Log($"[DraggableSoul] SoulEnterPipeTrigger '{hit.collider.gameObject.name}' TryInsert → {inserted}");
                if (inserted) { OnPlacedSuccessfully(); return; }
            }

            if (gameplaySlot == null && soulSlot == null && pipeTrigger == null)
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
        GameProgressData.RemoveSoulFromBoat(soulDataIdentity);
        SoulsOnBoatDisplayManager.Instance?.ConsumeSoulFromDisplay(soulDataIdentity);
        Destroy(gameObject);
    }

    private void ReturnToInventory()
    {
        isHolding = false;
        Time.timeScale = 1f;
        transform.SetParent(startParent);
        rect.anchoredPosition = startPosition;
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(startParent.GetComponent<RectTransform>());
    }
}
