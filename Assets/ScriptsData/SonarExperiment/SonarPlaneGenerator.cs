using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class SonarPlaneGenerator : MonoBehaviour
{
    public enum LayoutMode
    {
        Manual,       // planeCount and spacing set independently
        GridDriven,   // gridDensityRef drives planeCount and spacing
        CountDriven   // planeCount drives gridDensity and spacing; pushes _GridDensity to material
    }

    [Header("Mesh")]
    [SerializeField] int   subdivisions = 8;
    [SerializeField] float planeSize    = 6f;

    [Header("Layout")]
    [SerializeField] LayoutMode layoutMode     = LayoutMode.GridDriven;
    [SerializeField] int        gridDensityRef = 3;
    [SerializeField] int        planeCount     = 4;
    [SerializeField] float      spacing        = 2f;

    [Header("Material")]
    [SerializeField] Material planeMaterial;

    // ── computed layout properties ────────────────────────────────────────────
    int   Planes      => layoutMode == LayoutMode.GridDriven  ? gridDensityRef + 1 : planeCount;
    float Spacing     => layoutMode == LayoutMode.GridDriven  ? planeSize / Mathf.Max(1, gridDensityRef)
                       : layoutMode == LayoutMode.CountDriven ? planeSize / Mathf.Max(1, planeCount - 1)
                       : spacing;

    // ── static property IDs for the pulse controller to use ──────────────────
    public static readonly int PulseOriginsID         = Shader.PropertyToID("_PulseOrigins");
    public static readonly int PulseOriginCountID     = Shader.PropertyToID("_PulseOriginCount");
    public static readonly int PulseRadiusID          = Shader.PropertyToID("_PulseRadius");
    public static readonly int PulseWidthID           = Shader.PropertyToID("_PulseWidth");
    public static readonly int DisplaceStrengthID     = Shader.PropertyToID("_DisplaceStrength");
    public static readonly int DisplaceRadiusOffsetID = Shader.PropertyToID("_DisplaceRadiusOffset");

    public static readonly int GridDensityShaderID = Shader.PropertyToID("_GridDensity");
    public static readonly int GridWorldScaleID    = Shader.PropertyToID("_GridWorldScale");

    public int   GridDensity    => layoutMode == LayoutMode.CountDriven ? Mathf.Max(1, planeCount - 1) : gridDensityRef;
    public float GridWorldScale => GridDensity / Mathf.Max(0.001f, planeSize);

    readonly List<GameObject> _planes = new List<GameObject>();

    // ── lifecycle ─────────────────────────────────────────────────────────────

    void OnEnable()
    {
        if (_planes.Count == 0)
            Generate();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        subdivisions   = Mathf.Max(1, subdivisions);
        planeSize      = Mathf.Max(0.01f, planeSize);
        gridDensityRef = Mathf.Max(1, gridDensityRef);
        planeCount     = Mathf.Max(2, planeCount);
        spacing        = Mathf.Max(0.01f, spacing);

        EditorApplication.delayCall += () =>
        {
            if (this != null && gameObject != null)
                Generate();
        };
    }
#endif

    // ── context menu ──────────────────────────────────────────────────────────

    [ContextMenu("Generate Planes")]
    public void Generate()
    {
        Clear();
        SpawnHorizontalPlanes();
        SpawnVerticalPlanes();
        SyncMaterialGridDensity();
    }

    [ContextMenu("Clear Planes")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        _planes.Clear();
    }

    public Renderer[] GetRenderers()
    {
        var renderers = new Renderer[_planes.Count];
        for (int i = 0; i < _planes.Count; i++)
            renderers[i] = _planes[i].GetComponent<Renderer>();
        return renderers;
    }

    // ── plane spawning ────────────────────────────────────────────────────────

    void SpawnHorizontalPlanes()
    {
        float s = Spacing, total = (Planes - 1) * s;
        for (int i = 0; i < Planes; i++)
            Spawn($"Horizontal_{i}", new Vector3(0f, -total * 0.5f + i * s, 0f), Quaternion.identity);
    }

    void SpawnVerticalPlanes()
    {
        float s = Spacing, total = (Planes - 1) * s;
        for (int i = 0; i < Planes; i++)
            Spawn($"Vertical_{i}", new Vector3(0f, 0f, -total * 0.5f + i * s), Quaternion.Euler(90f, 0f, 0f));
    }

    void Spawn(string goName, Vector3 localPos, Quaternion localRot)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();

        mf.sharedMesh = BuildMesh(subdivisions, planeSize);

        if (planeMaterial != null)
            mr.sharedMaterial = planeMaterial;

        _planes.Add(go);
    }

    // In CountDriven mode, push the derived grid density back to the material
    // so the shader stays in sync automatically
    void SyncMaterialGridDensity()
    {
        if (layoutMode == LayoutMode.CountDriven && planeMaterial != null)
            planeMaterial.SetFloat(GridDensityShaderID, GridDensity);
    }

    // ── procedural mesh ───────────────────────────────────────────────────────

    static Mesh BuildMesh(int subs, float size)
    {
        int rows     = subs + 1;
        var verts    = new Vector3[rows * rows];
        var uvs      = new Vector2[rows * rows];
        var tris     = new int[subs * subs * 6];
        float cell   = size / subs;
        float half   = size * 0.5f;

        for (int j = 0; j < rows; j++)
            for (int i = 0; i < rows; i++)
            {
                int idx  = j * rows + i;
                verts[idx] = new Vector3(i * cell - half, 0f, j * cell - half);
                uvs[idx]   = new Vector2((float)i / subs, (float)j / subs);
            }

        int t = 0;
        for (int j = 0; j < subs; j++)
            for (int i = 0; i < subs; i++)
            {
                int tl = j * rows + i, tr = tl + 1;
                int bl = (j + 1) * rows + i, br = bl + 1;
                tris[t++] = tl; tris[t++] = bl; tris[t++] = tr;
                tris[t++] = tr; tris[t++] = bl; tris[t++] = br;
            }

        var mesh = new Mesh { name = "SonarPlane" };
        mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.15f);
        float s = Spacing, total = (Planes - 1) * s;
        for (int i = 0; i < Planes; i++)
        {
            Gizmos.DrawWireCube(transform.position + new Vector3(0f, -total * 0.5f + i * s, 0f),
                                new Vector3(planeSize, 0f, planeSize));
            Gizmos.DrawWireCube(transform.position + new Vector3(0f, 0f, -total * 0.5f + i * s),
                                new Vector3(planeSize, planeSize, 0f));
        }
    }
#endif
}
