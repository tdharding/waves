using UnityEngine;

/// <summary>
/// The shaft of light under a street light: a cone mesh built in code, apex at this object and
/// opening downward. Lives in the street light's hierarchy, positioned at the bulb.
///
/// It is a real object in the world rather than a screen-space trick, so it holds its shape as the
/// camera moves, and anything that needs to know where the light falls can just read it —
/// StreetLightParticles takes one of these as a drop-in and fills it with quads.
///
/// The mesh is a bare cone wall with no caps: UVs run 0 at the apex to 1 at the base so the shader
/// can fade it down the shaft, and normals point outward so the shader can soften the silhouette.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class StreetLightCone : MonoBehaviour
{
    [Header("Shape")]
    [Tooltip("How far the cone reaches, measured from this object down to where it lands, before " +
             "the two offsets below trim it. StreetLightController rewrites this on spawn when it " +
             "sits the base on the water.")]
    [SerializeField] private float height = 6f;

    [Tooltip("Starts the shaft this far below the object, so it can begin under the bulb housing " +
             "rather than inside it. Negative starts it higher.")]
    [SerializeField] private float topOffset = 0f;

    [Tooltip("Carries the shaft this far past where it lands. A little below the water hides the " +
             "end of it in the surface; negative stops it short, above the water.")]
    [SerializeField] private float bottomOffset = 0f;

    [Tooltip("Radius of the circle of light where the cone lands.")]
    [SerializeField] private float baseRadius = 3f;

    [Tooltip("Radius at the bulb end. 0 gives a sharp point; a little width reads better where the " +
             "shaft meets the lamp.")]
    [SerializeField] private float apexRadius = 0.15f;

    [Tooltip("Segments around the cone. Higher is rounder; 24 is plenty at this size.")]
    [Range(6, 64)]
    [SerializeField] private int radialSegments = 24;

    [Header("Look")]
    [Tooltip("Material for the shaft. Assigned to this object's MeshRenderer.")]
    [SerializeField] private Material coneMaterial;

    // ---- Geometry, for anything that needs to know where the light falls ----------------------

    /// <summary>Direction the shaft travels — this object's down.</summary>
    public Vector3 Axis => -transform.up;

    /// <summary>Tip of the cone: this object, pushed down by the top offset.</summary>
    public Vector3 Apex => transform.position + Axis * topOffset;

    /// <summary>Centre of the circle the cone lands on.</summary>
    public Vector3 BaseCentre => Apex + Axis * Height;

    /// <summary>Length of the shaft itself, i.e. what is left of the height after both offsets.</summary>
    public float Height     => Mathf.Max(height - topOffset + bottomOffset, 0.001f);
    public float BaseRadius => Mathf.Max(baseRadius, 0.001f);
    public float ApexRadius => Mathf.Max(apexRadius, 0f);

    /// <summary>
    /// Sits the base of the shaft on a world height — the waterline, in practice. Solves for the
    /// height that lands it there with the offsets applied, so moving either offset afterwards
    /// still leaves the shaft ending where it was told to.
    /// </summary>
    public void SetBaseAtHeight(float worldY)
    {
        // Only meaningful for a shaft with some downward travel; a horizontal one has no answer.
        float drop = -Axis.y;
        if (drop < 0.001f) return;

        // transform.y - topOffset*drop + (height - topOffset + bottomOffset)*drop == worldY
        float apexY = transform.position.y - topOffset * drop;
        height = topOffset - bottomOffset + (apexY - worldY) / drop;
        Rebuild();
    }

    /// <summary>Half-angle of the spread, in degrees — what a cone-shaped particle emitter wants.</summary>
    public float HalfAngleDegrees => Mathf.Atan2(BaseRadius - ApexRadius, Height) * Mathf.Rad2Deg;

    /// <summary>Radius of the shaft a fraction t of the way down it, t running 0 at the apex to 1.</summary>
    public float RadiusAt(float t) => Mathf.Lerp(ApexRadius, BaseRadius, Mathf.Clamp01(t));

    /// <summary>
    /// Called by StreetLightController — the shaft only shows while the lamp is lit. The renderer is
    /// switched rather than the GameObject, so the mesh stays built and the geometry stays readable
    /// by the particle cloud whether or not the light is on.
    /// </summary>
    public void SetShowing(bool showing)
    {
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null) meshRenderer.enabled = showing;
    }

    // ---- Mesh ---------------------------------------------------------------------------------

    private Mesh mesh;
    private MeshRenderer meshRenderer;
    private bool rebuildQueued;

    private void Awake() => Rebuild();

    private void OnEnable()
    {
        if (mesh == null) Rebuild();
    }

    private void Update()
    {
        // Edit-mode only: field changes queue a rebuild rather than doing it inside OnValidate,
        // where creating meshes is not allowed.
        if (rebuildQueued)
        {
            rebuildQueued = false;
            Rebuild();
        }
    }

    [ContextMenu("Rebuild Cone")]
    public void Rebuild()
    {
        int   seg = Mathf.Clamp(radialSegments, 6, 64);
        float h   = Height;
        float rb  = BaseRadius;
        float ra  = ApexRadius;

        // One ring at each end, so the wall is a quad strip. Duplicating the seam vertex keeps the
        // U coordinate continuous rather than wrapping back to 0 across the last quad.
        int ringVerts = seg + 1;
        var verts = new Vector3[ringVerts * 2];
        var norms = new Vector3[ringVerts * 2];
        var uvs   = new Vector2[ringVerts * 2];
        var tris  = new int[seg * 6];

        float top = topOffset;   // the apex sits this far down the object's own axis

        for (int i = 0; i <= seg; i++)
        {
            float u   = i / (float)seg;
            float ang = u * Mathf.PI * 2f;
            float cos = Mathf.Cos(ang);
            float sin = Mathf.Sin(ang);

            // Built with the apex at the origin and the shaft running down local -Y, so the object's
            // own rotation aims it and the transform is the single source of where it points.
            verts[i]             = new Vector3(cos * ra, -top,     sin * ra);
            verts[i + ringVerts] = new Vector3(cos * rb, -top - h, sin * rb);

            // Perpendicular to the slant and lying in the plane through the axis: the sideways run
            // is h and the outward run is (rb - ra), so swapping them gives the outward normal.
            Vector3 n = new Vector3(cos * h, rb - ra, sin * h).normalized;
            norms[i]             = n;
            norms[i + ringVerts] = n;

            // V runs 0 at the bulb to 1 where it lands, which is what the shader fades along.
            uvs[i]             = new Vector2(u, 0f);
            uvs[i + ringVerts] = new Vector2(u, 1f);
        }

        for (int i = 0; i < seg; i++)
        {
            int a = i, b = i + 1, c = i + ringVerts, d = i + 1 + ringVerts;
            int t = i * 6;
            tris[t]     = a; tris[t + 1] = c; tris[t + 2] = b;
            tris[t + 3] = b; tris[t + 4] = c; tris[t + 5] = d;
        }

        if (mesh == null)
        {
            mesh = new Mesh { name = "StreetLightCone" };
            mesh.MarkDynamic();
        }
        mesh.Clear();
        mesh.vertices  = verts;
        mesh.normals   = norms;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;

        meshRenderer = GetComponent<MeshRenderer>();
        if (coneMaterial != null) meshRenderer.sharedMaterial = coneMaterial;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows    = false;

        PushShapeToMaterial();
    }

    /// <summary>
    /// Tells the shader how far the wall leans out per unit of length, so it can rebuild the
    /// surface direction per pixel from the UVs instead of reading the mesh's own normals. That is
    /// what keeps the shading smooth on a low-poly cone. Sent through a property block, so every
    /// lamp can share one material and still be shaded for its own shape.
    /// </summary>
    private void PushShapeToMaterial()
    {
        if (meshRenderer == null) return;

        _pushedApex = Apex;

        block ??= new MaterialPropertyBlock();
        meshRenderer.GetPropertyBlock(block);
        block.SetFloat(ConeSlopeId, (BaseRadius - ApexRadius) / Height);
        block.SetVector(ConeApexId, Apex);
        meshRenderer.SetPropertyBlock(block);
    }

    // The apex moves with the lamp and with the top offset, so it is re-sent whenever it no longer
    // matches what the shader was given — the mask would otherwise be centred where the lamp was.
    private void LateUpdate()
    {
        if (meshRenderer == null || !meshRenderer.enabled) return;
        if (Apex != _pushedApex) PushShapeToMaterial();
    }

    private Vector3 _pushedApex = new Vector3(float.NaN, float.NaN, float.NaN);

    static readonly int ConeSlopeId = Shader.PropertyToID("_ConeSlope");
    static readonly int ConeApexId  = Shader.PropertyToID("_ConeApexWS");
    static MaterialPropertyBlock block;

#if UNITY_EDITOR
    private void OnValidate()
    {
        height        = Mathf.Max(0.001f, height);
        // Between them the offsets must leave some shaft, or there is nothing to build.
        if (height - topOffset + bottomOffset < 0.001f) topOffset = height + bottomOffset - 0.001f;
        baseRadius    = Mathf.Max(0.001f, baseRadius);
        apexRadius    = Mathf.Max(0f, apexRadius);
        rebuildQueued = true;
    }

    [Header("Debug")]
    [Tooltip("Outline the shaft in the scene view, so it can be placed without the material on.")]
    [SerializeField] private bool showConeGizmo = true;

    private void OnDrawGizmosSelected()
    {
        if (!showConeGizmo) return;

        Vector3 apex = Apex;
        Vector3 down = Axis;
        Vector3 baseC = BaseCentre;
        Vector3 right = transform.right;
        Vector3 fwd   = transform.forward;

        Gizmos.color = new Color(1f, 0.9f, 0.6f, 0.9f);
        for (int i = 0; i < 4; i++)
        {
            float ang = i * Mathf.PI * 0.5f;
            Vector3 offA = (right * Mathf.Cos(ang) + fwd * Mathf.Sin(ang)) * ApexRadius;
            Vector3 offB = (right * Mathf.Cos(ang) + fwd * Mathf.Sin(ang)) * BaseRadius;
            Gizmos.DrawLine(apex + offA, baseC + offB);
        }

        UnityEditor.Handles.color = new Color(1f, 0.9f, 0.6f, 0.9f);
        UnityEditor.Handles.DrawWireDisc(baseC, down, BaseRadius);
        UnityEditor.Handles.DrawWireDisc(apex,  down, ApexRadius);
        UnityEditor.Handles.Label(baseC + Vector3.up * 0.2f,
                                  $"{name}\nheight {Height:0.##}  base {BaseRadius:0.##}");
    }
#endif
}
