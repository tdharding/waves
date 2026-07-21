using UnityEngine;
using System.Collections.Generic;

public class SoulFishMapLinker : MonoBehaviour
{
    [Header("Map Renderer")]
    [SerializeField] private Renderer mapRenderer;

    [Header("Mask Settings")]
[SerializeField] private float soulFishMapRadius = 0.1f;

static readonly int RadiusID = Shader.PropertyToID("_SoulFishMarkerRadius");

    const int MAX_POINTS = 10;

    // _Map_ infix is deliberate: these are bare $Globals uniforms in SoulFishMapMask.hlsl, i.e. one
    // slot shared project-wide rather than per-material. They used to be named _SoulFishPositions /
    // _SoulFishCount — exactly what SoulFishWaveMask declares — so this linker's writes were
    // clobbering the wave's, truncating its 17-point river to the map's 10-point cap. Keep the
    // names distinct from the wave mask's.
    static readonly int PositionsArrayID = Shader.PropertyToID("_SoulFishMapPositions");
    static readonly int PositionCountID  = Shader.PropertyToID("_SoulFishMapCount");
    static readonly int MapCenterID      = Shader.PropertyToID("_MapCenter");
    static readonly int MapSizeID        = Shader.PropertyToID("_MapSize");

    static readonly Vector4 OffMapPosition = new Vector4(99999f, 99999f, 99999f, 0f);



    static readonly List<Transform> activeFish = new List<Transform>();

    // Mirrors SoulFishWaveLinker.ZoneEntry so the map can draw the same footprint the wave mask
    // does: the authored Grid Designer swim radius, and whether the node path loops back on itself.
    public struct ZoneEntry
    {
        public List<Vector3> nodes;   // final (post-rotation) world positions
        public bool          closed;  // node path closes back to node 0
        public float         radius;  // authored swim radius in world units; <= 0 = use the fallback
    }

    static readonly List<ZoneEntry> activeZones = new List<ZoneEntry>();

    public static IReadOnlyList<Transform> ActiveFish  => activeFish;
    public static IReadOnlyList<ZoneEntry> ActiveZones => activeZones;

    readonly Vector4[] positionBuffer = new Vector4[MAX_POINTS];
    readonly Vector3[] gizmoPositions = new Vector3[MAX_POINTS];
    int gizmoCount = 0;

    private Material mapMaterialInstance;

    public static SoulFishMapLinker Instance { get; private set; }

    // ─────────────────────────────────────────
    // LIFECYCLE
    // ─────────────────────────────────────────

    void Awake()
    {
        Instance = this;

        // Statics survive scene loads — clear them here (as SoulFishWaveLinker does) so a reloaded
        // level never inherits zones/fish from the previous one.
        activeFish.Clear();
        activeZones.Clear();

        if (!mapRenderer)
        {
            Debug.LogWarning("[SoulFishMapLinker] No renderer assigned.");
            enabled = false;
            return;
        }

        mapMaterialInstance = mapRenderer.material;
    }

    void Start()
    {
        BakePositionsOnce();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        activeFish.Clear();
        activeZones.Clear();
    }

    // ─────────────────────────────────────────
    // REGISTRATION
    // ─────────────────────────────────────────

    public static void Register(Transform fish)
    {
        if (activeFish.Contains(fish)) return;
        activeFish.Add(fish);
        Instance?.BakePositionsOnce();
    }

    public static void Unregister(Transform fish)
    {
        if (!activeFish.Contains(fish)) return;
        activeFish.Remove(fish);
        Instance?.BakePositionsOnce();
    }

    // radius: the zone's authored swim radius (Grid Designer). radius <= 0 leaves the drawn zone
    // on UIMapController's fallback radius — same contract as SoulFishWaveLinker.RegisterZone.
    // Dedupes on the node list reference, so the radius-carrying call from LevelSpawner wins and
    // SoulShoalController's later bare call is a no-op.
    public static void RegisterZone(List<Vector3> nodes, bool closed = false, float radius = 0f)
    {
        if (nodes == null || nodes.Count == 0) return;
        if (activeZones.Exists(e => e.nodes == nodes)) return;
        activeZones.Add(new ZoneEntry { nodes = nodes, closed = closed, radius = radius });
        Instance?.BakePositionsOnce();
    }

    public static void UnregisterZone(List<Vector3> nodes)
    {
        int idx = activeZones.FindIndex(e => e.nodes == nodes);
        if (idx < 0) return;
        activeZones.RemoveAt(idx);
        Instance?.BakePositionsOnce();
    }

    // ─────────────────────────────────────────
    // BAKE
    // ─────────────────────────────────────────

    public void BakePositionsOnce()
    {
        if (mapMaterialInstance == null && mapRenderer != null)
            mapMaterialInstance = mapRenderer.material;

        if (!mapMaterialInstance) return;


        // The map's zoom transform. Positions go through MapContentPoint (exactly what the boat
        // pointer uses) and radii multiply by the zoom scale — that pair is what keeps the painted
        // zone locked to the geometry that scales by riding the content root.
        UIMapController map = UIMapController.Instance;
        float zoom       = map != null ? map.MapZoomScale : 1f;
        float mapPerWorld = map != null ? map.MapUnitsPerWorldUnit() : 1f;

        mapMaterialInstance.SetFloat(RadiusID, soulFishMapRadius * zoom);

        // Push map bounds to HLSL regardless of MapProjection state
        // so _MapCenter and _MapSize are always correct
        Bounds b = mapRenderer.bounds;
        mapMaterialInstance.SetVector(MapCenterID, new Vector4(
            b.center.x, b.center.y, b.center.z, 0f));
        mapMaterialInstance.SetVector(MapSizeID, new Vector4(
            b.size.x, b.size.y, b.size.z, 0f));

        // If MapProjection isn't ready yet, push zeros and bail
        if (!MapProjection.IsReady)
        {
            mapMaterialInstance.SetFloat(PositionCountID, 0f);
            return;
        }

        List<Vector4> packedPoints = new List<Vector4>();
        gizmoCount = 0;   // refilled by ToMapSurface as points are packed

        // 1. Pack Zones
        foreach (var entry in activeZones)
        {
            if (packedPoints.Count >= MAX_POINTS) break;
            var nodes = entry.nodes;
            if (nodes == null) continue;

            // Authored swim radius (world units) -> map units -> zoomed. Zones registered without
            // one pack 0, which leaves the shader on its _SoulFishMarkerRadius fallback.
            float r = entry.radius > 0.0001f ? entry.radius * mapPerWorld * zoom : 0f;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (packedPoints.Count >= MAX_POINTS) break;
                Vector3 mapPos = ToMapSurface(map, nodes[i]);
                bool isLast = (i == nodes.Count - 1);
                // w = 2 means "connect to the next point"; a closed loop keeps connecting off the end.
                float w = (!isLast || entry.closed) ? 2f : 1f;
                // .y carries the radius, not a height — the mask compares in XZ.
                packedPoints.Add(new Vector4(mapPos.x, r, mapPos.z, w));
            }

            // Closed loops need node 0 repeated as the endpoint so the closing segment renders.
            if (entry.closed && nodes.Count > 0 && packedPoints.Count < MAX_POINTS)
            {
                Vector3 first = ToMapSurface(map, nodes[0]);
                packedPoints.Add(new Vector4(first.x, r, first.z, 1f));
            }
        }

        // 2. Pack Fish — loose fish carry no zone radius, so y = 0 puts them on the fallback.
        foreach (var fish in activeFish)
        {
            if (packedPoints.Count >= MAX_POINTS) break;
            if (fish == null) continue;
            Vector3 mapPos = ToMapSurface(map, fish.position);
            packedPoints.Add(new Vector4(mapPos.x, 0f, mapPos.z, 1f));
        }

        int count = packedPoints.Count;

        for (int i = 0; i < count; i++)
            positionBuffer[i] = packedPoints[i];

        for (int i = count; i < MAX_POINTS; i++)
            positionBuffer[i] = OffMapPosition;

        mapMaterialInstance.SetVectorArray(PositionsArrayID, positionBuffer);
        mapMaterialInstance.SetFloat(PositionCountID, (float)count);
    }

    // World position -> map surface, through the map's zoom/recentre when it's active. This is the
    // same call the boat pointer makes, which is precisely why the mask stays in register with it.
    // Falls back to the raw projection if the controller isn't up yet.
    Vector3 ToMapSurface(UIMapController map, Vector3 world)
    {
        Vector3 mapPos = map != null ? map.MapContentPoint(world)
                                     : MapProjection.WorldToMap(world);

        if (gizmoCount < MAX_POINTS) gizmoPositions[gizmoCount++] = mapPos;
        return mapPos;
    }

    // ─────────────────────────────────────────
    // GIZMOS
    // ─────────────────────────────────────────

    void OnDrawGizmos()
    {
        if (!mapRenderer) return;

        Bounds bounds = mapRenderer.bounds;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(bounds.center, bounds.size);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(bounds.center - Vector3.right   * 0.5f,
                        bounds.center + Vector3.right   * 0.5f);
        Gizmos.DrawLine(bounds.center - Vector3.forward * 0.5f,
                        bounds.center + Vector3.forward * 0.5f);

        if (!Application.isPlaying) return;

        // Green = fish world positions
        Gizmos.color = Color.green;
        for (int i = 0; i < activeFish.Count; i++)
            if (activeFish[i] != null)
                Gizmos.DrawSphere(activeFish[i].position, 0.3f);

        // Magenta = mapped positions on map surface
        Gizmos.color = Color.magenta;
        for (int i = 0; i < gizmoCount; i++)
            Gizmos.DrawSphere(gizmoPositions[i], 0.05f);
    }
}