using UnityEngine;

[CreateAssetMenu(fileName = "ArenaProfile", menuName = "Levels/Arena Profile")]
public class ArenaProfile : ScriptableObject
{
    [Tooltip("Controls the tiling of the map grid material. Match to arena size.")]
    public Vector2 mapGridTiling = Vector2.one;

    [Tooltip("Outer walls prefab. Parent at 0,0,0 — child visual pre-positioned.")]
    public GameObject outerWallsPrefab;

    [Header("Manual Overrides")]
    public bool  overrideArenaRadius1 = false;
    public float manualArenaRadius1   = -20.12f;

    [Tooltip("Value for _ArenaRadius1 on the wave material. Derived from Baseline Disc Radius.")]
    public float arenaRadius1 => overrideArenaRadius1 ? manualArenaRadius1 : GetDiscRadius();

    [Tooltip("Percentage of the radius where the mask begins to fade (0-1). Matches BaselineMarker.")]
    public float maskFadeStartPct
    {
        get
        {
            if (outerWallsPrefab != null)
            {
                var marker = outerWallsPrefab.GetComponentInChildren<BaselineMarker>();
                if (marker != null) return marker.maskFadeStartPct;
            }
            return 0.85f;
        }
    }

    public float WorldArenaRadius => GetDiscRadius();
    public float WorldArenaWidth  => GetDiscRadius() * 2f;

    [Tooltip("No longer used — RefMapPlane bounds are the authority for map sizing.")]
    [HideInInspector] public float mapWidth  = 1.379f;
    [HideInInspector] public float mapHeight = 1.267f;

    [Tooltip("Scale applied to all maze wall map markers for this arena size.")]
    public float mazeWallMarkerScale = 1f;

    [Tooltip("How much larger the wave plane should be compared to the arena diameter (e.g. 1.5).")]
    public float wavePlaneCoverageMultiplier = 1.5f;

    [Header("Entrance Prefab Override")]
    [Tooltip("When set, overrides the prefab on every ArenaEntrance in GridData for this arena size. " +
             "Use to supply a variant whose radial offset matches this arena's perimeter radius.")]
    public GameObject entrancePrefabOverride;

    [Header("Dropped Soul Containment")]
    [Tooltip("XZ radius from the arena centre within which DroppedSouls are hard-clamped.")]
    public float droppedSoulBoundsRadius = 20f;

    public float GetDiscRadius()
{
        if (outerWallsPrefab != null)
        {
            var marker = outerWallsPrefab.GetComponentInChildren<BaselineMarker>();
            if (marker != null)
            {
                return marker.discRadius;
            }
        }
        return droppedSoulBoundsRadius;
    }

    [Tooltip("XZ offset of the arena centre from world origin. X = world X, Y = world Z.")]
    public Vector2 arenaCentreOffset = new Vector2(0.27f, 0.03f);
}