using System;

/// <summary>
/// Lightweight static event bus for soul-pickup state.
/// Fired by LevelSelectDraggableSoul; consumed by SoulSlotIndicator and SoulRangeRing.
/// </summary>
public static class SoulPickupEvents
{
    public static event Action OnSoulPickedUp;
    public static event Action OnSoulReleased;

    public static void FirePickedUp()  => OnSoulPickedUp?.Invoke();
    public static void FireReleased()  => OnSoulReleased?.Invoke();
}
