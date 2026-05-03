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

    static readonly int PositionsArrayID = Shader.PropertyToID("_SoulFishPositions");
    static readonly int PositionCountID  = Shader.PropertyToID("_SoulFishCount");
    static readonly int MapCenterID      = Shader.PropertyToID("_MapCenter");
    static readonly int MapSizeID        = Shader.PropertyToID("_MapSize");

    static readonly Vector4 OffMapPosition = new Vector4(99999f, 99999f, 99999f, 0f);



    static readonly List<Transform> activeFish = new List<Transform>();

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

    // ─────────────────────────────────────────
    // BAKE
    // ─────────────────────────────────────────

    public void BakePositionsOnce()
    {
        if (mapMaterialInstance == null && mapRenderer != null)
            mapMaterialInstance = mapRenderer.material;

        if (!mapMaterialInstance) return;


mapMaterialInstance.SetFloat(RadiusID, soulFishMapRadius);
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

        int count = Mathf.Min(activeFish.Count, MAX_POINTS);
        gizmoCount = 0;

        for (int i = 0; i < count; i++)
        {
            if (activeFish[i] == null)
            {
                positionBuffer[i] = OffMapPosition;
                continue;
            }

            // Convert fish world position to map surface world position
            Vector3 mapPos = MapProjection.WorldToMap(activeFish[i].position);

            positionBuffer[i] = new Vector4(mapPos.x, mapPos.y, mapPos.z, 1f);
            gizmoPositions[gizmoCount++] = mapPos;
        }

        for (int i = count; i < MAX_POINTS; i++)
            positionBuffer[i] = OffMapPosition;

        mapMaterialInstance.SetVectorArray(PositionsArrayID, positionBuffer);
        mapMaterialInstance.SetFloat(PositionCountID, (float)count);
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