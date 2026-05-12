using UnityEngine;
using System.Collections.Generic;

public class SoulFishWaveLinker : MonoBehaviour
{
    [SerializeField] private WaveMaterialController waveController;

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
    private struct ZoneEntry { public List<Vector3> nodes; public bool closed; }
    static readonly List<ZoneEntry> activeZones = new List<ZoneEntry>();

    public void BakePositionsOnce()
    {
        if (!waveController || !waveController.waveMaterial) return;

        Material mat = waveController.waveMaterial;

        List<Vector4> packedPoints = new List<Vector4>();

        // 1. Pack Zones first (as they define the areas)
        foreach (var entry in activeZones)
        {
            if (packedPoints.Count >= MAX_POINTS) break;
            PackZone(entry.nodes, entry.closed, packedPoints);
        }

        // 2. Pack individual fish if space remains
        foreach (var fish in activeFish)
        {
            if (packedPoints.Count >= MAX_POINTS) break;
            if (fish == null) continue;

            packedPoints.Add(new Vector4(fish.position.x, fish.position.y, fish.position.z, 1f));
        }

        int count = packedPoints.Count;

        for (int i = 0; i < count; i++)
            mat.SetVector(PositionIDs[i], packedPoints[i]);

        // Push unused slots off-map
        for (int i = count; i < MAX_POINTS; i++)
            mat.SetVector(PositionIDs[i], (Vector4)OffMapPosition);

        mat.SetFloat(CountID, count);

        // Keep map wave renderer in sync
        waveController.SyncMapWaves();
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

    public static void RegisterZone(List<Vector3> nodes, bool closed = false)
    {
        if (activeZones.Exists(e => e.nodes == nodes)) return;
        activeZones.Add(new ZoneEntry { nodes = nodes, closed = closed });
        FindObjectOfType<SoulFishWaveLinker>()?.BakePositionsOnce();
    }

    public static void UnregisterZone(List<Vector3> nodes)
    {
        int idx = activeZones.FindIndex(e => e.nodes == nodes);
        if (idx < 0) return;
        activeZones.RemoveAt(idx);
        FindObjectOfType<SoulFishWaveLinker>()?.BakePositionsOnce();
    }

    static void PackZone(List<Vector3> nodes, bool closed, List<Vector4> output)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (output.Count >= MAX_POINTS) break;
            bool isLast = (i == nodes.Count - 1);
            // closed loop: last real node connects back to first
            float w = (!isLast || closed) ? 2f : 1f;
            output.Add(new Vector4(nodes[i].x, nodes[i].y, nodes[i].z, w));
        }
        // For closed loops, append first node as endpoint so the closing segment renders
        if (closed && nodes.Count > 0 && output.Count < MAX_POINTS)
            output.Add(new Vector4(nodes[0].x, nodes[0].y, nodes[0].z, 1f));
    }

    public static IReadOnlyList<Transform> ActiveFish => activeFish;

    void OnDrawGizmos()
    {
        var packed = new List<Vector4>();
        foreach (var entry in activeZones)
            PackZone(entry.nodes, entry.closed, packed);
        foreach (var fish in activeFish)
        {
            if (fish == null || packed.Count >= MAX_POINTS) continue;
            packed.Add(new Vector4(fish.position.x, fish.position.y, fish.position.z, 1f));
        }

        for (int i = 0; i < packed.Count; i++)
        {
            Vector3 pos = new Vector3(packed[i].x, packed[i].y, packed[i].z);
            bool connects = packed[i].w > 1.5f && i < packed.Count - 1;

            Gizmos.color = connects ? Color.cyan : Color.yellow;
            Gizmos.DrawWireSphere(pos, waveController != null ? waveController.soulFishRadius : 1f);

            if (connects)
            {
                Vector3 next = new Vector3(packed[i + 1].x, packed[i + 1].y, packed[i + 1].z);
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(pos, next);
            }

            UnityEditor.Handles.Label(pos + Vector3.up * 0.5f, $"[{i}] w={packed[i].w}");
        }
    }

    public void LogBakedState()
    {
        var packed = new List<Vector4>();
        foreach (var entry in activeZones)
            PackZone(entry.nodes, entry.closed, packed);

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
            PackZone(entry.nodes, entry.closed, packedPoints);
        }

        foreach (var fish in activeFish)
        {
            if (packedPoints.Count >= MAX_POINTS) break;
            if (fish == null) continue;
            packedPoints.Add(new Vector4(fish.position.x, fish.position.y, fish.position.z, 1f));
        }

        int count = packedPoints.Count;
        for (int i = 0; i < count; i++)
            mat.SetVector(PositionIDs[i], packedPoints[i]);
        for (int i = count; i < MAX_POINTS; i++)
            mat.SetVector(PositionIDs[i], (Vector4)OffMapPosition);
        mat.SetFloat(CountID, count);
    }
}