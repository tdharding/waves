using UnityEngine;

/// <summary>
/// Place on the SoulEnterPipeTrigger (or its parent SoulFishInputTube).
/// Assign indicatorRoot to a child GameObject that holds your soulfish indicator mesh.
/// When a soul is held, shows/hides the bobbing indicator based on whether the boat
/// is within SoulRangeRing.Instance.interactionRadius.
/// </summary>
public class SoulSlotIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject indicatorRoot;

    [Header("Bob Settings")]
    [SerializeField] private float bobHeight    = 0.25f;
    [SerializeField] private float bobSpeed     = 2.2f;
    [SerializeField] private float bobOffset    = 0f;   // phase offset so tubes don't sync perfectly

    private Vector3 indicatorBaseLocalPos;
    private bool isVisible;

    private void Awake()
    {
        if (indicatorRoot != null)
            indicatorBaseLocalPos = indicatorRoot.transform.localPosition;
        SetVisible(false);
    }

    private void OnEnable()
    {
        SoulPickupEvents.OnSoulPickedUp += OnSoulPickedUp;
        SoulPickupEvents.OnSoulReleased += OnSoulReleased;
    }

    private void OnDisable()
    {
        SoulPickupEvents.OnSoulPickedUp -= OnSoulPickedUp;
        SoulPickupEvents.OnSoulReleased -= OnSoulReleased;
        SetVisible(false);
    }

    private void Update()
    {
        if (!isVisible || indicatorRoot == null) return;

        float yOffset = Mathf.Sin((Time.time + bobOffset) * bobSpeed) * bobHeight;
        indicatorRoot.transform.localPosition = indicatorBaseLocalPos + Vector3.up * yOffset;
    }

    private void OnSoulPickedUp()
    {
        // Only show if boat is in range
        SetVisible(IsInRange());
    }

    private void OnSoulReleased() => SetVisible(false);

    public bool IsInRange()
    {
        if (SoulRangeRing.Instance == null) return true; // no ring = no restriction

        GameObject boat = GameObject.FindWithTag("Boat");
        if (boat == null) return true;

        float dist = Vector3.Distance(boat.transform.position, transform.position);
        return dist <= SoulRangeRing.Instance.interactionRadius;
    }

    private void SetVisible(bool state)
    {
        isVisible = state;
        if (indicatorRoot != null)
            indicatorRoot.SetActive(state);
    }
}
