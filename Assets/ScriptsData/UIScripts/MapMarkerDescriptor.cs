using UnityEngine;

/// <summary>
/// Procedurally-generated map icon shapes, drawn with the shared MapProcedural material.
/// Add new shapes here and in UIMapController.BuildIconMarker.
/// </summary>
public enum MapIcon
{
    BigSpike,     // triangle, gradient fill, bottom ~10% fades out
    FishBowl,     // lollipop: rectangle stick + circle on top
    StreetLight,  // lollipop with a bulb (black off / white on) + halo behind (shown when lit)
}

/// <summary>
/// Optional per-prefab description of how a level prefab is represented on the Map UI.
/// Read off the prefab ASSET at map-build time (no scene lookup, no tags), so the Grid
/// Designer's placements are the single authority for what the map draws.
///
/// Tags are deliberately NOT used here — they're owned by the collision system, and
/// overloading them for map selection was the coupling we're removing. If a prefab has
/// no descriptor, or leaves the marker unset, the Map UI falls back to its default
/// "big spike" marker (UIMapController.defaultMazeMarkerPrefab).
/// </summary>
[DisallowMultipleComponent]
public class MapMarkerDescriptor : MonoBehaviour
{
    [Tooltip("Uncheck to keep this prefab off the map entirely (e.g. hidden tubes, decorative props).")]
    public bool drawOnMap = true;

    [Tooltip("Procedural icon shape drawn for this prefab (used when no marker prefab is set).")]
    public MapIcon icon = MapIcon.BigSpike;

    [Tooltip("Optional prefab marker. If set it OVERRIDES the procedural icon. Leave null to use the icon above.")]
    public GameObject mapMarkerPrefab;

    [Tooltip("Add a SortingGroup to the generated map marker so it sorts correctly against other transparent map elements (needed for e.g. fish bowls).")]
    public bool addSortingGroup = false;

    [Tooltip("Sorting order for the added SortingGroup. Higher = drawn on top.")]
    public int sortingOrder = 0;
}
