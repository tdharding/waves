using UnityEngine;
using System.Collections.Generic;

public class SoulFishWaveLinker : MonoBehaviour
{
    [SerializeField] private WaveMaterialController waveController;

    [Tooltip("Log every packed point on each bake. Off by default — the bake now runs per frame, so this floods the console. Turn on only while diagnosing a zone.")]
    [SerializeField] private bool verboseLogging = false;

    // Shader-side budget (SOULFISH_MAX_POINTS in SoulFishWaveMask.hlsl). Curved zones densify
    // into more points than their authored nodes, so this is generous; PackZone warns when a
    // level actually overflows it.
    const int MAX_POINTS = 40;

    static readonly Vector3 OffMapPosition = new Vector3(99999f, 99999f, 99999f);

    // Packed as one flat array. Per point: .xz = world position, .y = mask radius,
    // .w = ±(1 + cumulative XZ arc length along the zone) — positive connects to the next
    // point, negative ends a chain. The arc length is what SoulFishWaveMaskPath uses to build
    // a continuous along-path UV.
    static readonly int PositionsArrayID = Shader.PropertyToID("_SoulFishWavePositions");
    static readonly int CountID          = Shader.PropertyToID("_SoulFishCount");

    // Legacy individual uniforms, still written (old w = 2/1 encoding) because
    // SoulWellShaderGraph declares its own _SoulFishPosition1..10 properties and reads these.
    // The wave mask itself no longer looks at them.
    const int LEGACY_POINTS = 20;
    static readonly int[] LegacyPositionIDs;

    static SoulFishWaveLinker()
    {
        LegacyPositionIDs = new int[LEGACY_POINTS];
        for (int i = 0; i < LEGACY_POINTS; i++)
            LegacyPositionIDs[i] = Shader.PropertyToID("_SoulFishPosition" + (i + 1));
    }

    static readonly Vector4[] PositionBuffer = new Vector4[MAX_POINTS];

    static readonly List<Transform> activeFish = new List<Transform>();
    private struct ZoneEntry { public List<Vector3> nodes; public bool closed; public float radius; }
    static readonly List<ZoneEntry> activeZones = new List<ZoneEntry>();

    void Awake()
    {
        Debug.Log($"[SoulFishWaveLinker] Awake — clearing {activeZones.Count} zone(s) and {activeFish.Count} fish from statics.");
        activeFish.Clear();
        activeZones.Clear();
    }

    // Re-pushes every frame. The point/count uniforms are bare $Globals declared in
    // SoulFishWaveMask.hlsl, so they live nowhere on disk — reimporting the shader (i.e. saving ANY
    // shadergraph, or any file that triggers a reload) rebuilds the material from its serialized
    // state and wipes them, dropping _SoulFishCount to 0 and making the zone vanish. Baking only on
    // register/unregister meant nothing ever put them back.
    void LateUpdate()
    {
        if (activeZones.Count == 0 && activeFish.Count == 0) return;
        BakePositionsOnce();
    }

    public void BakePositionsOnce()
    {
        if (!waveController)
        {
            Debug.LogError("[SoulFishWaveLinker] BakePositionsOnce — waveController is NULL.");
            return;
        }
        if (!waveController.waveMaterial)
        {
            Debug.LogError("[SoulFishWaveLinker] BakePositionsOnce — waveMaterial is NULL.");
            return;
        }

        Material mat = waveController.waveMaterial;

        List<Vector4> packedPoints = PackAll();
        PushPacked(mat, packedPoints);

        if (verboseLogging)
        {
            Debug.Log($"[SoulFishWaveLinker] BakePositionsOnce — zones={activeZones.Count} fish={activeFish.Count} packed={packedPoints.Count} mat='{mat.name}'");
            for (int i = 0; i < packedPoints.Count; i++)
                Debug.Log($"  [{i}] pos=({packedPoints[i].x:F1},{packedPoints[i].y:F1},{packedPoints[i].z:F1}) w={packedPoints[i].w:F2}");
        }

        waveController.SyncMapWaves();
    }

    static List<Vector4> PackAll()
    {
        var packedPoints = new List<Vector4>();

        foreach (var entry in activeZones)
        {
            if (packedPoints.Count >= MAX_POINTS) { WarnBudget(); break; }
            PackZone(entry.nodes, entry.closed, entry.radius, packedPoints);
        }

        foreach (var fish in activeFish)
        {
            if (packedPoints.Count >= MAX_POINTS) { WarnBudget(); break; }
            if (fish == null) continue;
            // Loose fish carry no zone radius — y = 0 makes the shader use the global
            // _SoulFishRadius. w = -1: lone point, arc length 0.
            packedPoints.Add(new Vector4(fish.position.x, 0f, fish.position.z, -1f));
        }

        return packedPoints;
    }

    static bool _budgetWarned;
    static void WarnBudget()
    {
        if (_budgetWarned) return;
        _budgetWarned = true;
        Debug.LogWarning($"[SoulFishWaveLinker] More than {MAX_POINTS} packed zone points on this level — later zones/fish are dropped from the wave mask. Reduce zone node counts or raise MAX_POINTS + SOULFISH_MAX_POINTS together.");
    }

    // Dual-write, matching WaveMaterialController.SetGlobalsBackedFloat. These uniforms are bare
    // $Globals rather than blackboard properties, so the material write alone isn't dependable —
    // the global write is what actually survives a material rebuild. Global arrays lock their
    // size on first set, which is why the full MAX_POINTS buffer is pushed every time.
    static void PushPacked(Material mat, List<Vector4> packedPoints)
    {
        int count = Mathf.Min(packedPoints.Count, MAX_POINTS);

        for (int i = 0; i < count; i++)
            PositionBuffer[i] = packedPoints[i];
        for (int i = count; i < MAX_POINTS; i++)
            PositionBuffer[i] = new Vector4(OffMapPosition.x, 0f, OffMapPosition.z, -1f);

        if (mat != null)
        {
            mat.SetVectorArray(PositionsArrayID, PositionBuffer);
            mat.SetFloat(CountID, count);
        }
        Shader.SetGlobalVectorArray(PositionsArrayID, PositionBuffer);
        Shader.SetGlobalFloat(CountID, count);

        // Legacy mirror for SoulWellShaderGraph (first 20 points, old w = 2/1 encoding).
        for (int i = 0; i < LEGACY_POINTS; i++)
        {
            Vector4 v = i < count ? packedPoints[i] : (Vector4)OffMapPosition;
            if (i < count) v.w = v.w > 0f ? 2f : 1f;
            if (mat != null) mat.SetVector(LegacyPositionIDs[i], v);
            Shader.SetGlobalVector(LegacyPositionIDs[i], v);
        }
    }

    public static void Register(Transform fish)
    {
        if (activeFish.Contains(fish)) return;
        activeFish.Add(fish);
        FindObjectOfType<SoulFishWaveLinker>()?.BakePositionsOnce();
    }

    public static void Unregister(Transform fish)
    {
        if (!activeFish.Contains(fish)) return;
        activeFish.Remove(fish);
        FindObjectOfType<SoulFishWaveLinker>()?.BakePositionsOnce();
    }

    // radius: the zone's authored swim radius (Grid Designer). It becomes the mask footprint
    // for this zone's points. radius <= 0 leaves points on the global _SoulFishRadius fallback.
    public static void RegisterZone(List<Vector3> nodes, bool closed = false, float radius = 0f)
    {
        if (activeZones.Exists(e => e.nodes == nodes)) return;
        activeZones.Add(new ZoneEntry { nodes = nodes, closed = closed, radius = radius });
        var linker = FindObjectOfType<SoulFishWaveLinker>();
        Debug.Log($"[SoulFishWaveLinker] RegisterZone — nodes={nodes.Count} closed={closed} radius={radius} linkerFound={linker != null}");
        linker?.BakePositionsOnce();
    }

    public static void UnregisterZone(List<Vector3> nodes)
    {
        int idx = activeZones.FindIndex(e => e.nodes == nodes);
        if (idx < 0) return;
        activeZones.RemoveAt(idx);
        FindObjectOfType<SoulFishWaveLinker>()?.BakePositionsOnce();
    }

    // Changes a registered zone's radius in place (found by list reference). Used by
    // SoulZoneStreetLightChain to bloom a pool open; the per-frame re-bake picks it up.
    public static void UpdateZoneRadius(List<Vector3> nodes, float radius)
    {
        int idx = activeZones.FindIndex(e => e.nodes == nodes);
        if (idx < 0) return;
        var entry = activeZones[idx];
        entry.radius = radius;
        activeZones[idx] = entry;
    }

    // radius is packed into the .y channel — the wave mask reads it per point. The world-space
    // height (nodes[i].y) is unused by the mask; arc length accumulates in XZ to match it.
    static void PackZone(List<Vector3> nodes, bool closed, float radius, List<Vector4> output)
    {
        float cum = 0f;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (output.Count >= MAX_POINTS) { WarnBudget(); break; }
            bool isLast    = (i == nodes.Count - 1);
            bool connects  = !isLast || closed;   // closed loop: last real node connects on to the appended duplicate
            float sign     = connects ? 1f : -1f;
            output.Add(new Vector4(nodes[i].x, radius, nodes[i].z, sign * (1f + cum)));

            if (!isLast)
                cum += DistXZ(nodes[i], nodes[i + 1]);
        }
        // For closed loops, append first node as endpoint so the closing segment renders.
        if (closed && nodes.Count > 0 && output.Count < MAX_POINTS)
        {
            cum += DistXZ(nodes[nodes.Count - 1], nodes[0]);
            output.Add(new Vector4(nodes[0].x, radius, nodes[0].z, -(1f + cum)));
        }
    }

    static float DistXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    public static IReadOnlyList<Transform> ActiveFish => activeFish;

    // Debug surface (SoulFishDebugTracer): how many zone entries and packed points are live.
    public static int ActiveZoneCount  => activeZones.Count;
    public static int PackedPointCount => PackAll().Count;

    void OnDrawGizmos()
    {
        var packed = PackAll();

        float fallbackR = waveController != null ? waveController.soulFishRadius : 1f;
        for (int i = 0; i < packed.Count; i++)
        {
            // .y carries the per-point mask radius, not a world height — draw on the water plane.
            Vector3 pos = new Vector3(packed[i].x, 0f, packed[i].z);
            float   r   = packed[i].y > 0.0001f ? packed[i].y : fallbackR;
            bool connects = packed[i].w > 0f && i < packed.Count - 1;

            Gizmos.color = connects ? Color.cyan : Color.yellow;
            Gizmos.DrawWireSphere(pos, r);

            if (connects)
            {
                Vector3 next = new Vector3(packed[i + 1].x, 0f, packed[i + 1].z);
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(pos, next);
            }

#if UNITY_EDITOR
            UnityEditor.Handles.Label(pos + Vector3.up * 0.5f, $"[{i}] s={Mathf.Abs(packed[i].w) - 1f:F1}");
#endif
        }
    }

    public void LogBakedState()
    {
        var packed = PackAll();

        Debug.Log($"[SoulFishWaveLinker] {activeZones.Count} zone(s), {packed.Count} packed point(s):");
        for (int i = 0; i < packed.Count; i++)
            Debug.Log($"  [{i}] pos=({packed[i].x:F1},{packed[i].y:F1},{packed[i].z:F1}) w={packed[i].w:F2} (connects={packed[i].w > 0f}, s={Mathf.Abs(packed[i].w) - 1f:F1})");
    }

    public static void BakeToMaterial(Material mat)
    {
        if (!mat) return;
        PushPacked(mat, PackAll());
    }
}
