using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using System.Collections;

public class SonarGridPoints : MonoBehaviour
{
    [Header("Generation Settings")]
    public int nodeCount = 300;
    public float cellSize = 1f;

    [Header("Particle Settings")]
    public float speed = 1f;

    [Header("Debug")]
    public bool drawGizmos = true;

    public Vector3[] nodes;
    public List<int>[] connections;

    List<List<Vector3>> paths = new List<List<Vector3>>();

    public VisualEffect vfx;
    GraphicsBuffer pathBuffer;

void Start()
{
    Generate();
    BuildPaths();
    StartCoroutine(SendNextFrame());
}

IEnumerator SendNextFrame()
{
    yield return null;
    SendToVFX();
}

    void Update()
{
    Debug.Log("alive: " + vfx.aliveParticleCount);
}

    public void Generate()
    {
        List<Vector3> nodeList = new List<Vector3>();
        List<List<int>> connectionList = new List<List<int>>();

        nodeList.Add(Vector3.zero);
        connectionList.Add(new List<int>());

        Vector3[] dirs = new Vector3[]
        {
            Vector3.right, Vector3.left,
            Vector3.up, Vector3.down,
            Vector3.forward, Vector3.back
        };

        for (int i = 1; i < nodeCount; i++)
        {
            int parentIndex = Random.Range(0, nodeList.Count);
            Vector3 parent = nodeList[parentIndex];

            Vector3 dir = dirs[Random.Range(0, dirs.Length)];
            Vector3 newPos = parent + dir * cellSize;

            if (nodeList.Contains(newPos)) { i--; continue; }

            int newIndex = nodeList.Count;
            nodeList.Add(newPos);
            connectionList.Add(new List<int>());
            connectionList[parentIndex].Add(newIndex);
            connectionList[newIndex].Add(parentIndex);
        }

        nodes = nodeList.ToArray();
        connections = new List<int>[connectionList.Count];
        for (int i = 0; i < connectionList.Count; i++)
            connections[i] = connectionList[i];
    }

    void BuildPaths()
    {
        paths.Clear();

        for (int i = 1; i < nodes.Length; i++)
        {
            if (connections[i].Count == 1)
            {
                List<Vector3> path = new List<Vector3>();
                int current = i;
                int previous = -1;

                while (current != 0)
                {
                    path.Add(nodes[current]);
                    int next = -1;
                    foreach (int c in connections[current])
                    {
                        if (c != previous) { next = c; break; }
                    }
                    previous = current;
                    current = next;
                }

                path.Add(nodes[0]);
                path.Reverse();
                paths.Add(path);
            }
        }

        Debug.Log("Path count: " + paths.Count);
    }

    void SendToVFX()
    {
        if (paths.Count == 0)
        {
            Debug.LogError("No paths found");
            return;
        }

        // find longest path
        int maxLength = 0;
        foreach (var path in paths)
            if (path.Count > maxLength) maxLength = path.Count;

        Debug.Log("Max path length: " + maxLength);

        // build flat padded buffer
        Vector3[] flatBuffer = new Vector3[paths.Count * maxLength];

        for (int i = 0; i < paths.Count; i++)
        {
            List<Vector3> path = paths[i];
            for (int j = 0; j < maxLength; j++)
            {
                // pad with last position if path is shorter than maxLength
                int idx = Mathf.Min(j, path.Count - 1);
                flatBuffer[i * maxLength + j] = path[idx];
            }
        }

        if (pathBuffer != null) pathBuffer.Release();
        pathBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            flatBuffer.Length,
            sizeof(float) * 3
        );
        pathBuffer.SetData(flatBuffer);

        Debug.Log("Flat buffer length: " + flatBuffer.Length);
Debug.Log("First element: " + flatBuffer[0]);

        vfx.SetGraphicsBuffer("PathBuffer", pathBuffer);
        vfx.SetInt("PathCount", paths.Count);
        vfx.SetInt("PathLength", maxLength);

vfx.Reinit();

Debug.Log("VFX PathCount: " + vfx.GetInt("PathCount"));
Debug.Log("VFX PathLength: " + vfx.GetInt("PathLength"));
Debug.Log("VFX alive: " + vfx.aliveParticleCount);
    }

    void OnDestroy()
    {
        pathBuffer?.Release();
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos || nodes == null || connections == null) return;
        Gizmos.color = Color.green;
        for (int i = 0; i < nodes.Length; i++)
            foreach (int c in connections[i])
                Gizmos.DrawLine(transform.position + nodes[i], transform.position + nodes[c]);
    }
}