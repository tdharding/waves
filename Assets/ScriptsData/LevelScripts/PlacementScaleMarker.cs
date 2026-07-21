using UnityEngine;

// Stamped at spawn onto prefab placements that use a non-default scale, so downstream
// systems (e.g. the UI minimap) can size their markers to match the placement's scale.
// Kept deliberately tiny — it only carries the scale factor authored in the Grid Designer.
public class PlacementScaleMarker : MonoBehaviour
{
    public float scale = 1f;
}
