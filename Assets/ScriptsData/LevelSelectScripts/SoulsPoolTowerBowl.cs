using UnityEngine;

/// <summary>
/// Put on a generated souls pool tower by the Level Select Designer — nothing to set by hand.
/// Tells the runtime where the tower's water is and how far across it is wet, so
/// <see cref="TravellingSoulFishController"/> can draw the souls waiting in it swimming inside the
/// bowl. Regenerating the tower rewrites it.
///
/// It also lowers the water when the souls are released: <see cref="SetDrop"/> moves it down the
/// basin, and the wet radius narrows with the basin's curve. Sizes are in the tower's own units.
/// </summary>
public class SoulsPoolTowerBowl : MonoBehaviour
{
    [Tooltip("The designer node of the pool this tower stands in — what a soul route names as its origin.")]
    public string poolNodeId;

    [Tooltip("The water in the bowl. Its position is the middle of the water surface.")]
    public Transform water;

    [Tooltip("Radius of the water's edge in the bowl, in world units — where the basin comes up " +
             "through the surface.")]
    public float waterRadius;

    [Tooltip("Radius of the basin at the rim — the inside edge of the rim.")]
    public float innerRadius;

    [Tooltip("Depth of the basin at its middle, below the rim.")]
    public float basinDepth;

    [Tooltip("How far below the rim the water sits when full.")]
    public float waterDrop;

    [Tooltip("How far below the rim the water is once drained — the hole's edge, or the basin's floor.")]
    public float emptyDrop;

    // How far below the rim the water is now. Starts full.
    private float _drop = -1f;
    private float _restY;
    private Vector3 _restScale;
    private bool  _restTaken;

    /// <summary>How far below the rim the water is now, tower units.</summary>
    public float Drop => _drop < 0f ? waterDrop : _drop;

    /// <summary>Whether the water has gone — drained down to the hole or the basin's floor.</summary>
    // A tower generated before the drain was added has no empty depth, and empties at the first lowering.
    public bool Drained => emptyDrop > waterDrop ? Drop >= emptyDrop - 0.00001f : _drop >= 0f;

    /// <summary>The middle of the water surface, in world space.</summary>
    public Vector3 WaterCentre => water != null ? water.position : transform.position;

    /// <summary>The wet radius now, in world space, allowing for any scale above the tower.</summary>
    public float WorldWaterRadius
    {
        get
        {
            if (Drained) return 0f;

            // A tower generated before the drain was added has no basin numbers: it stays full.
            if (basinDepth <= 0f || innerRadius <= 0f) return waterRadius * Scale;

            float sunk = Mathf.Clamp01(Drop / basinDepth);
            return innerRadius * Mathf.Sqrt(Mathf.Max(0f, 1f - sunk * sunk)) * Scale;
        }
    }

    private float Scale => Mathf.Abs(transform.lossyScale.x);

    /// <summary>
    /// Lowers the water by <paramref name="worldStep"/> world units, down to empty. The water
    /// is hidden once drained, so nothing lies across the hole.
    /// </summary>
    public void Lower(float worldStep) => SetDrop(Drop + worldStep / Mathf.Max(Scale, 0.0001f));

    /// <summary>Puts the water <paramref name="drop"/> below the rim, tower units.</summary>
    public void SetDrop(float drop)
    {
        if (water == null) return;

        if (!_restTaken)
        {
            _restY     = water.localPosition.y;
            _restScale = water.localScale;
            _restTaken = true;
        }

        _drop = Mathf.Clamp(drop, waterDrop, Mathf.Max(waterDrop, emptyDrop));

        Vector3 p = water.localPosition;
        p.y = _restY - (_drop - waterDrop);
        water.localPosition = p;

        // The basin narrows as it deepens, so the water shrinks with it — its edge stays where the
        // basin comes up through it.
        float k = WetFraction(_drop);
        water.localScale = new Vector3(_restScale.x * k, _restScale.y, _restScale.z * k);

        water.gameObject.SetActive(!Drained);
    }

    // Wet radius at a drop, as a fraction of the full water's.
    private float WetFraction(float drop)
    {
        if (basinDepth <= 0f) return 1f;
        float full = Mathf.Sqrt(Mathf.Max(0f, 1f - Sq(Mathf.Clamp01(waterDrop / basinDepth))));
        float now  = Mathf.Sqrt(Mathf.Max(0f, 1f - Sq(Mathf.Clamp01(drop / basinDepth))));
        return full > 0.0001f ? now / full : 1f;
    }

    private static float Sq(float x) => x * x;

    /// <summary>Straight to empty — a tower already released when the map loads.</summary>
    public void Empty() => SetDrop(emptyDrop);
}
