using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
public class LandscapeTileMesh : MonoBehaviour
{
    [Header("Tile Dimensions")]
    public float width = 10f;
    public float depth = 10f;

    [Header("Subdivision")]
    [Range(1, 100)] public int subdivisionsX = 20;
    [Range(1, 100)] public int subdivisionsZ = 20;

    private MeshFilter _meshFilter;

    void Awake() => GenerateMesh();

#if UNITY_EDITOR
    void OnValidate()
    {
        // Defer mesh generation — Unity forbids modifying components during OnValidate
        // (e.g. while a scene or asset is loading), which causes "Unknown error" messages.
        EditorApplication.delayCall += () =>
        {
            if (this != null) GenerateMesh();
        };
    }
#endif

    public void GenerateMesh()
    {
        if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
        if (_meshFilter == null) return;

        int vx = subdivisionsX + 1;
        int vz = subdivisionsZ + 1;

        var verts = new Vector3[vx * vz];
        var uvs   = new Vector2[vx * vz];
        var tris  = new int[subdivisionsX * subdivisionsZ * 6];

        for (int z = 0; z < vz; z++)
        {
            for (int x = 0; x < vx; x++)
            {
                float u = (float)x / subdivisionsX;
                float v = (float)z / subdivisionsZ;
                verts[z * vx + x] = new Vector3(u * width, 0f, v * depth);
                uvs[z * vx + x]   = new Vector2(u, v);
            }
        }

        int t = 0;
        for (int z = 0; z < subdivisionsZ; z++)
        {
            for (int x = 0; x < subdivisionsX; x++)
            {
                int v00 = z * vx + x;
                int v10 = v00 + 1;
                int v01 = v00 + vx;
                int v11 = v01 + 1;

                tris[t++] = v00; tris[t++] = v01; tris[t++] = v10;
                tris[t++] = v10; tris[t++] = v01; tris[t++] = v11;
            }
        }

        var mesh = new Mesh { name = "LandscapeTile_Procedural" };
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.bounds = new Bounds(
            new Vector3(width * 0.5f, 0f, depth * 0.5f),
            new Vector3(width, 50f, depth));

        _meshFilter.sharedMesh = mesh;
    }
}
