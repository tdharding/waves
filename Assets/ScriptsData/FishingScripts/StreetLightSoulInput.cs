using UnityEngine;

/// <summary>
/// Hold I to feed the nearest street light without dragging a soul icon.
///
/// Picks the first soul on the boat and the nearest street light that will actually take it
/// (unlit, next in its chain's path order, inside the boat's SoulRangeRing radius), then runs the
/// exact same insertion + bookkeeping as the drag-and-drop path in LevelSelectDraggableSoul:
/// StreetLightController.TryInsertSoul, then the soul comes off the boat save data and its icon
/// leaves the boat display.
///
/// While the key is held the normal pickup feedback plays (range ring + slot indicators), so the
/// player can see which lamps are reachable; releasing early cancels with nothing spent.
///
/// Place on any always-present object in the level — it finds the boat by the "Boat" tag.
/// </summary>
public class StreetLightSoulInput : MonoBehaviour
{
    [Header("Hold")]
    [Tooltip("Seconds the key must be held before the soul is fed to the lamp.")]
    [SerializeField] private float holdDuration = 0.6f;

    private const KeyCode FeedKey = KeyCode.I;

    private bool  isHolding;
    private bool  dragInProgress;   // a soul icon is being dragged — don't double-spend
    private float holdTimer;
    private Transform boat;

    private void OnEnable()
    {
        SoulPickupEvents.OnSoulPickedUp += HandlePickedUp;
        SoulPickupEvents.OnSoulReleased += HandleReleased;
    }

    private void OnDisable()
    {
        SoulPickupEvents.OnSoulPickedUp -= HandlePickedUp;
        SoulPickupEvents.OnSoulReleased -= HandleReleased;
        if (isHolding) CancelHold();
    }

    // Only track holds that came from somewhere else (the dragged icon); our own hold sets
    // isHolding before firing, so these see it and leave dragInProgress alone.
    private void HandlePickedUp() { if (!isHolding) dragInProgress = true;  }
    private void HandleReleased() { if (!isHolding) dragInProgress = false; }

    private void Update()
    {
        if (Input.GetKeyDown(FeedKey)) TryBeginHold();

        if (!isHolding) return;

        if (!Input.GetKey(FeedKey))
        {
            Debug.Log("[StreetLightSoulInput] Key released before the hold completed — cancelled.");
            CancelHold();
            return;
        }

        // Unscaled: the drag path slows time while a soul is held, and the hold should feel the
        // same length either way.
        holdTimer += Time.unscaledDeltaTime;
        if (holdTimer >= holdDuration) CompleteHold();
    }

    private void TryBeginHold()
    {
        if (isHolding) return;

        if (dragInProgress)
        {
            Debug.Log("[StreetLightSoulInput] A soul icon is already being dragged — hold ignored.");
            return;
        }

        if (GetFirstSoulOnBoat() < 0)
        {
            Debug.Log("[StreetLightSoulInput] No souls on the boat — hold ignored.");
            return;
        }

        isHolding = true;
        holdTimer = 0f;
        SoulPickupEvents.FirePickedUp();
    }

    private void CancelHold()
    {
        SoulPickupEvents.FireReleased();
        isHolding = false;
        holdTimer = 0f;
    }

    private void CompleteHold()
    {
        // Ends the held state (ring + indicators) before the insert, so the lamp's own feed
        // visuals are what's left on screen.
        CancelHold();

        int identity = GetFirstSoulOnBoat();
        if (identity < 0)
        {
            Debug.Log("[StreetLightSoulInput] Souls ran out during the hold — nothing fed.");
            return;
        }

        StreetLightController lamp = FindNearestFeedableLight();
        if (lamp == null)
        {
            Debug.Log($"[StreetLightSoulInput] Hold completed but no street light in range will take soul {identity}.");
            return;
        }

        if (!lamp.TryInsertSoul(identity))
        {
            Debug.Log($"[StreetLightSoulInput] '{lamp.name}' rejected soul {identity} — it stays on the boat.");
            return;
        }

        // Same bookkeeping as the drag path — tracker, save data, display icon and boat visual,
        // so the soul is spent rather than left on deck to be dropped and re-caught.
        SoulConsumption.SpendSoulFromBoat(identity);
        Debug.Log($"[StreetLightSoulInput] Soul {identity} fed to '{lamp.name}'.");
    }

    /// <summary>
    /// Identity of the first soul the boat is carrying, or -1 if it has none.
    /// Reads the live tracker list first: souls caught this session are not written to
    /// GameProgressData until a tracker write happens, so the save data alone reads as empty.
    /// </summary>
    private static int GetFirstSoulOnBoat()
    {
        if (LevelSoulTracker.Instance != null)
        {
            var live = LevelSoulTracker.Instance.GetAllCaughtIdentities();
            if (live != null && live.Count > 0) return live[0];
        }

        var saved = GameProgressData.GetSoulsOnBoatIdentities();
        return saved != null && saved.Count > 0 ? saved[0] : -1;
    }

    /// <summary>
    /// Nearest lamp to the boat that is unlit, next in its chain, and inside the pickup range ring.
    /// </summary>
    private StreetLightController FindNearestFeedableLight()
    {
        Transform boatTransform = ResolveBoat();
        if (boatTransform == null)
        {
            Debug.LogWarning("[StreetLightSoulInput] No object tagged 'Boat' — cannot measure range.");
            return null;
        }

        float range = SoulRangeRing.Instance != null
            ? SoulRangeRing.Instance.interactionRadius
            : Mathf.Infinity;   // no ring in the scene = no range restriction, as SoulSlotIndicator treats it

        StreetLightController nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (StreetLightController lamp in StreetLightController.All)
        {
            if (lamp == null || lamp.IsLit) continue;
            if (lamp.chain == null || !lamp.chain.CanFeed(lamp)) continue;

            float distance = Vector3.Distance(boatTransform.position, lamp.transform.position);
            if (distance > range || distance >= nearestDistance) continue;

            nearest         = lamp;
            nearestDistance = distance;
        }

        return nearest;
    }

    private Transform ResolveBoat()
    {
        if (boat == null)
        {
            GameObject boatObject = GameObject.FindWithTag("Boat");
            if (boatObject != null) boat = boatObject.transform;
        }
        return boat;
    }
}
