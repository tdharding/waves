using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Universal soul slot. Any manager or modifier can subscribe to onFilled/onEmptied
/// and control removal permission via SetAllowRemoval.
/// Requires a Collider on this GameObject.
/// </summary>
public class SoulSlot : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] private GameObject filledVisual;

    [Header("Settings")]
    [Tooltip("If false, clicking the slot will not return the soul to the player.")]
    [SerializeField] private bool allowRemoval = true;

    [Header("Events")]
    public UnityEvent<int> onFilled;
    public UnityEvent<int> onEmptied;

    private bool filled;
    private int occupantSoulIdentity = -1;
    private bool interactable = true;
    private float _removalCooldown;

    public bool IsFilled     => filled;
    public int  SoulIdentity => occupantSoulIdentity;

    private void Start()
    {
        if (filledVisual != null)
            filledVisual.SetActive(filled);
    }

    public void SetInteractable(bool state) => interactable = state;

    /// <summary>
    /// Called by the owning manager/modifier to control whether the soul can be
    /// clicked out. Obstacles pass false; modifiers pass true.
    /// </summary>
    public void SetAllowRemoval(bool allow) => allowRemoval = allow;

    public bool TryInsertSoul(int soulIdentity)
    {
        Debug.Log($"[SoulSlot] '{gameObject.name}' TryInsertSoul({soulIdentity}) — interactable={interactable}, filled={filled}");
        if (!interactable || filled) return false;

        filled               = true;
        occupantSoulIdentity = soulIdentity;

        if (filledVisual != null)
            filledVisual.SetActive(true);

        _removalCooldown = 0.2f;
        Debug.Log($"[SoulSlot] '{gameObject.name}' filled with soul {soulIdentity} — invoking onFilled (listeners={onFilled.GetPersistentEventCount()})");
        onFilled?.Invoke(soulIdentity);
        return true;
    }

    private int _interactionMask;

    private void Awake()
    {
        _interactionMask = LayerMask.GetMask("Interaction");
    }

    private void Update()
    {
        if (_removalCooldown > 0f)
            _removalCooldown -= Time.deltaTime;

        if (!filled || !interactable || !allowRemoval || _removalCooldown > 0f) return;
        if (!Input.GetMouseButtonDown(0)) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, _interactionMask, QueryTriggerInteraction.Collide))
        {
            Debug.Log($"[SoulSlot] Click — hit '{hit.collider.gameObject.name}', this='{gameObject.name}', cooldown={_removalCooldown:F3}");
            if (hit.collider.GetComponentInParent<SoulSlot>() == this)
                RemoveSoul();
        }
    }

    private void RemoveSoul()
    {
        int identityToReturn = occupantSoulIdentity;
        ClearSlot();

        GameProgressData.AddSoulToBoat(identityToReturn);
        SoulsOnBoatDisplayManager.Instance?.ReturnSoulToDisplay(identityToReturn);
        onEmptied?.Invoke(identityToReturn);
        Debug.Log($"[SoulSlot] Soul {identityToReturn} returned to boat.");
    }

    /// <summary>
    /// Clears the slot without returning the soul to inventory.
    /// Used by the catapult when the soul is launched — it was already
    /// removed from GameProgressData when placed into the slot.
    /// </summary>
    public void EjectSoul()
    {
        if (!filled) return;
        int identity = occupantSoulIdentity;
        ClearSlot();
        onEmptied?.Invoke(identity);
    }

    private void ClearSlot()
    {
        filled               = false;
        occupantSoulIdentity = -1;

        if (filledVisual != null)
            filledVisual.SetActive(false);
    }
}
