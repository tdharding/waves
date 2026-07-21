using UnityEngine;
using System.Collections.Generic;

public class SoulFishWaveLinker : MonoBehaviour
{
    [SerializeField] private WaveMaterialController waveController;

    [Tooltip("Log every packed point on each bake. Off by default — the bake now runs per frame, so this floods the console. Turn on only while diagnosing a zone.")]
    [SerializeField] private bool verboseLogging = false;

    const int MAX_POINTS = 20;

    static readonly Vector3 OffMapPosition = new Vector3(99999f, 99999f, 99999f);

    static readonly int[] PositionIDs =
    {
        Shader.PropertyToID("_SoulFishPosition1"),
        Shader.PropertyToID("_SoulFishPosition2"),
        Shader.PropertyToID("_SoulFishPosition3"),
        Shader.PropertyToID("_SoulFishPosition4"),
        Shader.PropertyToID("_SoulFishPosition5"),
        Shader.PropertyToID("_SoulFishPosition6"),
        Shader.PropertyToID("_SoulFishPosition7"),
        Shader.PropertyToID("_SoulFishPosition8"),
        Shader.PropertyToID("_SoulFishPosition9"),
        Shader.PropertyToID("_SoulFishPosition10"),
        Shader.PropertyToID("_SoulFishPosition11"),
        Shader.PropertyToID("_SoulFishPosition12"),
        Shader.PropertyToID("_SoulFishPosition13"),
        Shader.PropertyToID("_SoulFishPosition14"),
        Shader.PropertyToID("_SoulFishPosition15"),
        Shader.PropertyToID("_SoulFishPosition16"),
        Shader.PropertyToID("_SoulFishPosition17"),
        Shader.PropertyToID("_SoulFishPosition18"),
        Shader.PropertyToID("_SoulFishPosition19"),
        Shader.PropertyToID("_SoulFishPosition20"),
    };

    static readonly int CountID = Shader.PropertyToID("_SoulFishCount");
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
    // register/unregister meant nothing ever put them back. This costs ~21 setter calls a frame.
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

        List<Vector4> packedPoints = new List<Vector4>();

        foreach (var entry in activeZones)
        {
            if (packedPoints.Count >= MAX_POINTS) break;
            PackZone(entry.nodes, entry.closed, entry.radius, packedPoints);
        }

        foreach (var fish in activeFish)
        {
            if (packedPoints.Count >= MAX_POINTS) break;
            if (fish == null) continue;
            // Loose fish carry no zone radius — y = 0 makes the shader use the global _SoulFishRadius.
            packedPoints.Add(new Vector4(fish.position.x, 0f, fish.position.z, 1f));
        }

        int count = packedPoints.Count;

        for (int i = 0; i < count; i++)
            SetPoint(mat, i, packedPoints[i]);

        for (int i = count; i < MAX_POINTS; i++)
            SetPoint(mat, i, (Vector4)OffMapPosition);

        mat.SetFloat(CountID, count);
        Shader.SetGlobalFloat(CountID, count);

        if (verboseLogging)
        {
            Debug.Log($"[SoulFishWaveLinker] BakePositionsOnce — zones={activeZones.Count} fish={activeFish.Count} packed={count} mat='{mat.name}'");
            for (int i = 0; i < count; i++)
                Debug.Log($"  [{i}] pos=({packedPoints[i].x:F1},{packedPoints[i].y:F1},{packedPoints[i].z:F1}) w={packedPoints[i].w}");
        }

        waveController.SyncMapWaves();
    }

    // Dual-write, matching WaveMaterialController.SetGlobalsBackedFloat. These uniforms are bare
    // $Globals rather than blackboard properties, so the material write alone isn't dependable —
    // the global write is what actually survives a material rebuild.
    static void SetPoint(Material mat, int index, Vector4 value)
    {
        mat.SetVector(PositionIDs[index], value);
        Shader.SetGlobalVector(PositionIDs[index], value);
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

    // radius is packed into the .y channel — the wave mask reads it per point (see
    // SoulFishWaveMask.hlsl). The world-space height (nodes[i].y) is unused by the mask.
    static void PackZone(List<Vector3> nodes, bool closed, float radius, List<Vector4> output)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (output.Count >= MAX_POINTS) break;
            bool isLast = (i == nodes.Count - 1);
            // closed loop: last real node connects back to first
            float w = (!isLast || closed) ? 2f : 1f;
            output.Add(new Vector4(nodes[i].x, radius, nodes[i].z, w));
        }
        // For closed loops, append first node as endpoint so the closing segment renders
        if (closed && nodes.Count > 0 && output.Count < MAX_POINTS)
            output.Add(new Vector4(nodes[0].x, radius, nodes[0].z, 1f));
    }

    public static IReadOnlyList<Transform> ActiveFish => activeFish;

    void OnDrawGizmos()
    {
        var packed = new List<Vector4>();
        foreach (var entry in activeZones)
            PackZone(entry.nodes, entry.closed, entry.radius, packed);
        foreach (var fish in activeFish)
        {
            if (fish == null || packed.Count >= MAX_POINTS) continue;
            packed.Add(new Vector4(fish.position.x, 0f, fish.position.z, 1f));
        }

        float fallbackR = waveController != null ? waveController.soulFishRadius : 1f;
        for (int i = 0; i < packed.Count; i++)
        {
            // .y carries the per-point mask radius, not a world height — draw on the water plane.
            Vector3 pos = new Vector3(packed[i].x, 0f, packed[i].z);
            float   r   = packed[i].y > 0.0001f ? packed[i].y : fallbackR;
            bool connects = packed[i].w > 1.5f && i < packed.Count - 1;

            Gizmos.color = connects ? Color.cyan : Color.yellow;
            Gizmos.DrawWireSphere(pos, r);

            if (connects)
            {
                Vector3 next = new Vector3(packed[i + 1].x, 0f, packed[i + 1].z);
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(pos, next);
            }

#if UNITY_EDITOR
            UnityEditor.Handles.Label(pos + Vector3.up * 0.5f, $"[{i}] w={packed[i].w}");
#endif
        }
    }

    public void LogBakedState()
    {
        var packed = new List<Vector4>();
        foreach (var entry in activeZones)
            PackZone(entry.nodes, entry.closed, entry.radius, packed);

        Debug.Log($"[SoulFishWaveLinker] {activeZones.Count} zone(s), {packed.Count} packed point(s):");
        for (int i = 0; i < packed.Count; i++)
            Debug.Log($"  [{i}] pos=({packed[i].x:F1},{packed[i].y:F1},{packed[i].z:F1}) w={packed[i].w} (connects={packed[i].w > 1.5f})");
    }

    public static void BakeToMaterial(Material mat)
    {
        if (!mat) return;

        var packedPoints = new List<Vector4>();

        foreach (var entry in activeZones)
        {
            if (packedPoints.Count >= MAX_POINTS) break;
            PackZone(entry.nodes, entry.closed, entry.radius, packedPoints);
        }

        foreach (var fish in activeFish)
        {
            if (packedPoints.Count >= MAX_POINTS) break;
            if (fish == null) continue;
            // Loose fish carry no zone radius — y = 0 makes the shader use the global _SoulFishRadius.
            packedPoints.Add(new Vector4(fish.position.x, 0f, fish.position.z, 1f));
        }

        int count = packedPoints.Count;
        for (int i = 0; i < count; i++)
            SetPoint(mat, i, packedPoints[i]);
        for (int i = count; i < MAX_POINTS; i++)
            SetPoint(mat, i, (Vector4)OffMapPosition);
        mat.SetFloat(CountID, count);
        Shader.SetGlobalFloat(CountID, count);
    }
}