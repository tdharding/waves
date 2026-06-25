using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Attach to the wave plane GameObject.
// Generates a quad mesh in local XY space (Z-up local convention).
// Transform scale handles world coverage — size and subdivisions control mesh dimensions.
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
public class WaveMeshGenerator : MonoBehaviour
{
    [SerializeField] float size         = 1f;
    [SerializeField] int   subdivisions = 10;

#if UNITY_EDITOR
    void OnValidate()
    {
        EditorApplication.delayCall += () =>
        {
            if (this != null && gameObject != null)
                Generate();
        };
    }
#endif

    public void UpdateMeshSize(float newSize)
    {
        if (Mathf.Approximately(size, newSize)) return;
        size = newSize;
        Generate();
    }

    [ContextMenu("Generate Wave Mesh")]
    public void Generate()
    {
        float s    = Mathf.Max(0.01f, size);
        int   subs = Mathf.Max(1, subdivisions);
        GetComponent<MeshFilter>().sharedMesh = BuildMesh(subs, s);
    }

    static Mesh BuildMesh(int subs, float size)
    {
        int   rows  = subs + 1;
        var   verts = new Vector3[rows * rows];
        var   uvs   = new Vector2[rows * rows];
        var   tris  = new int[subs * subs * 6];
        float cell  = size / subs;
        float half  = size * 0.5f;

        for (int j = 0; j < rows; j++)
            for (int i = 0; i < rows; i++)
            {
                int idx    = j * rows + i;
                verts[idx] = new Vector3(i * cell - half, j * cell - half, 0f);
                uvs[idx]   = new Vector2((float)i / subs, (float)j / subs);
            }

        int t = 0;
        for (int j = 0; j < subs; j++)
            for (int i = 0; i < subs; i++)
            {
                int tl = j * rows + i, tr = tl + 1;
                int bl = (j + 1) * rows + i, br = bl + 1;
                // Reversed winding so normals point local +Z → world +Y after X:-90 rotation
                tris[t++] = tl; tris[t++] = tr; tris[t++] = bl;
                tris[t++] = tr; tris[t++] = br; tris[t++] = bl;
            }

        var mesh = new Mesh { name = "WavePlane" };
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(WaveMeshGenerator))]
public class WaveMeshGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var gen  = (WaveMeshGenerator)target;
        int subs = Mathf.Max(1, serializedObject.FindProperty("subdivisions").intValue);
        int verts = (subs + 1) * (subs + 1);
        int quads = subs * subs;
        int tris  = quads * 2;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"Vertices   {verts:N0}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Quads      {quads:N0}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Triangles  {tris:N0}", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(2);
        if (GUILayout.Button("Generate Wave Mesh", GUILayout.Height(26)))
            gen.Generate();
    }
}
#endif
