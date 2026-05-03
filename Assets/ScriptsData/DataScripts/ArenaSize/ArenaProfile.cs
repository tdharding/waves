using UnityEngine;

[CreateAssetMenu(fileName = "ArenaProfile", menuName = "Levels/Arena Profile")]
public class ArenaProfile : ScriptableObject
{
    public ArenaSize arenaSize;

    [Tooltip("Controls the tiling of the map grid material. Match to arena size.")]
    public Vector2 mapGridTiling = Vector2.one;

    [Tooltip("Outer walls prefab. Parent at 0,0,0 — child visual pre-positioned.")]
    public GameObject outerWallsPrefab;

    [Tooltip("Reference plane used to calculate tile bounds during maze spawn.")]
    public GameObject arenaSizeReferencePlane;

    [Tooltip("Value for _ArenaRadius1 on the wave material. Default = -4.54, Small = -2.")]
    public float arenaRadius1 = -4.54f;

    [Tooltip("No longer used — RefMapPlane bounds are the authority for map sizing.")]
    [HideInInspector] public float mapWidth  = 1.379f;
    [HideInInspector] public float mapHeight = 1.267f;

    [Tooltip("Scale applied to all maze wall map markers for this arena size.")]
    public float mazeWallMarkerScale = 1f;

    [Tooltip("Scale applied to the wave plane object for this arena size.")]
    public Vector3 wavePlaneScale = Vector3.one;

    [Header("Entrance Prefab Override")]
    [Tooltip("When set, overrides the prefab on every ArenaEntrance in GridData for this arena size. " +
             "Use to supply a variant whose radial offset matches this arena's perimeter radius.")]
    public GameObject entrancePrefabOverride;

    [Header("Dropped Soul Containment")]
    [Tooltip("XZ radius from the arena centre within which DroppedSouls are hard-clamped.")]
    public float droppedSoulBoundsRadius = 20f;
    [Tooltip("XZ offset of the arena centre from world origin. X = world X, Y = world Z.")]
    public Vector2 arenaCentreOffset = new Vector2(0.27f, 0.03f);
}