using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Attach to the fog sheet GameObject. Builds the plane the fog draws on, in the manner of
// WaveMeshGenerator — a quad in local XY, Z-up local convention, so it sits flat when the object
// is rotated the way the wave plane is.
//
// Two jobs beyond making a mesh, both of which were manual scene work before:
//
//   IT SIZES ITSELF FROM THE ARENA, and it never moves. Fog is painted into a small window that
//   travels with the boat, but the surface that window is displayed on is this one static square
//   over the whole arena. Two separate things: the sheet moving is what desynced boat movement,
//   the window moving costs nothing and is what keeps the fog detailed.
//
//   IT SETS ITS OWN HEIGHT. The waterline sits at a known Y and the sheet belongs just above it.
//   The vertical scale in this game is small — the waterline gradient rises 0.07 — so the offset
//   that reads as "resting on the water" is a couple of hundredths, not a couple of tenths, and
//   guessing it by dragging in the scene view is fiddly.
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class FogSheetMesh : MonoBehaviour
{
    [Header("Size")]
    [Tooltip("Take the size from the FogFieldManager's arena width rather than the value below. " +
             "The sheet has to cover the whole arena, because it is static geometry and the fog " +
             "window travels across it with the boat.")]
    [SerializeField] bool matchFieldCoverage = true;


    [Tooltip("World size when not matching the field.")]
    [SerializeField] float size = 60f;

    [Tooltip("Mesh subdivisions. One quad is enough — the fog is drawn entirely in the fragment " +
             "shader and nothing here is displaced — so this only matters if you later want to " +
             "displace the sheet vertically.")]
    [Range(1, 64)] [SerializeField] int subdivisions = 1;

    [Header("Height")]
    [Tooltip("The wave plane this sheet sits above. Found automatically from its WaveMeshGenerator " +
             "if left empty.")]
    [SerializeField] Transform wavePlane;

    [Tooltip("Fall back to this world Y when there is no wave plane to measure from — a studio " +
             "scene with a stand-in, for instance.")]
    [SerializeField] float waterlineY = 0f;

    [Tooltip("How far above the water the sheet sits. Small: the vertical scale in this game is " +
             "tiny, and any higher and fog visibly floats off the water rather than lying on it.")]
    [SerializeField] float heightOffset = 0.03f;

    [Tooltip("Also take the sheet's XZ from the wave plane. The arena is centred by its profile's " +
             "centre offset, so a sheet left on the origin sits off-centre on any level that uses " +
             "one.")]
    [SerializeField] bool matchWavePlaneCentre = true;



    void OnEnable()
    {
        Generate();
        ApplyTransform();
    }

    float _builtSize = -1f;

    /// <summary>The size the mesh was last built at, so the manager can say when it is short.</summary>
    public float BuiltSize => _builtSize;

    void LateUpdate()
    {
        // Coverage is derived from the map's mask radius, so it CHANGES LIVE. The mesh was only
        // ever built in OnEnable and OnValidate, so raising the mask in play grew the painted
        // field while the sheet stayed its old size — and the sheet's own edge became a hard
        // straight cut across the fog, looking exactly like the grid running out.
        float want = ResolvedSize();
        if (Mathf.Abs(want - _builtSize) > 0.01f) Generate();

        ApplyTransform();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        EditorApplication.delayCall += () =>
        {
            if (this == null || gameObject == null) return;
            Generate();
            ApplyTransform();
        };
    }
#endif

    float ResolvedSize()
    {
        if (!matchFieldCoverage) return Mathf.Max(size, 0.01f);

        var mgr = FindAnyObjectByType<FogFieldManager>();
        if (mgr == null) return Mathf.Max(size, 0.01f);

        // The ARENA, not the painted window. These are deliberately different sizes: the sheet is
        // static geometry covering everything, and the window is a small travelling patch of
        // texture inside it. Sizing the sheet to the window would shrink it to a few units and
        // make it follow the boat again — which is the movement-sync problem the static sheet
        // exists to avoid.
        float fromArena = mgr.SheetSize;
        return fromArena > 0.01f ? fromArena : Mathf.Max(size, 0.01f);
    }

    [ContextMenu("Generate Fog Sheet")]
    public void Generate()
    {
        int subs = Mathf.Max(1, subdivisions);
        _builtSize = ResolvedSize();
        GetComponent<MeshFilter>().sharedMesh = BuildMesh(subs, _builtSize);

        // The sheet is transparent and lies flat on the water; casting or receiving shadows from
        // it is never wanted and costs a pass.
        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    /// <summary>
    /// The wave plane, which is where the water actually is. Cached, and re-found when it goes
    /// stale — levels rebuild their arena, so a reference caught at edit time does not survive.
    /// </summary>
    Transform ResolveWavePlane()
    {
        if (wavePlane != null) return wavePlane;
        var gen = FindAnyObjectByType<WaveMeshGenerator>();
        if (gen != null) wavePlane = gen.transform;
        return wavePlane;
    }

    void ApplyTransform()
    {
        // Flat, Z-up local, matching how the wave plane is oriented.
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        Vector3 p = transform.position;

        // Measured from the WAVE PLANE, not from world zero. The plane's height and centre are set
        // at runtime from the arena profile, so a sheet pinned to an absolute Y sits at the right
        // height in the studio scene and floats or sinks on any real level — and being a
        // transparent sheet, it does that silently rather than looking obviously wrong.
        Transform water = ResolveWavePlane();
        if (water != null)
        {
            p.y = water.position.y + heightOffset;
            if (matchWavePlaneCentre)
            {
                p.x = water.position.x;
                p.z = water.position.z;
            }
        }
        else
        {
            p.y = waterlineY + heightOffset;
        }

        transform.position = p;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // The sheet's own edge. Two different boxes can cut fog off — this one and the painted
        // grid window — and they look identical on screen. Drawing both is the only way to tell
        // which one you are hitting.
        float s = _builtSize > 0f ? _builtSize : ResolvedSize();
        Vector3 c = transform.position;
        var corners = new Vector3[]
        {
            c + new Vector3(-s * 0.5f, 0f, -s * 0.5f),
            c + new Vector3(-s * 0.5f, 0f,  s * 0.5f),
            c + new Vector3( s * 0.5f, 0f,  s * 0.5f),
            c + new Vector3( s * 0.5f, 0f, -s * 0.5f),
        };
        UnityEditor.Handles.color = new Color(0.5f, 1f, 0.6f, 0.9f);
        UnityEditor.Handles.DrawSolidRectangleWithOutline(corners,
            new Color(0.5f, 1f, 0.6f, 0.03f), new Color(0.5f, 1f, 0.6f, 0.7f));
        UnityEditor.Handles.Label(corners[1], $"fog sheet {s:0.#} u");
    }
#endif

    static Mesh BuildMesh(int subs, float size)
    {
        int rows = subs + 1;
        var verts = new Vector3[rows * rows];
        var uvs   = new Vector2[rows * rows];
        var tris  = new int[subs * subs * 6];

        float step = size / subs;
        float half = size * 0.5f;

        for (int y = 0; y < rows; y++)
        for (int x = 0; x < rows; x++)
        {
            int i = y * rows + x;
            verts[i] = new Vector3(x * step - half, y * step - half, 0f);
            uvs[i]   = new Vector2(x / (float)subs, y / (float)subs);
        }

        int t = 0;
        for (int y = 0; y < subs; y++)
        for (int x = 0; x < subs; x++)
        {
            int i = y * rows + x;
            tris[t++] = i;            tris[t++] = i + rows;     tris[t++] = i + 1;
            tris[t++] = i + 1;        tris[t++] = i + rows;     tris[t++] = i + rows + 1;
        }

        var mesh = new Mesh { name = "FogSheet" };
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(FogSheetMesh))]
public class FogSheetMeshEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var sheet = (FogSheetMesh)target;
        if (sheet.GetComponent<MeshRenderer>().sharedMaterial == null)
            EditorGUILayout.HelpBox(
                "No material — nothing will draw. Assign FogSheetGraph.mat from " +
                "Assets/ScriptsData/FogScripts.", MessageType.Warning);

        if (Object.FindAnyObjectByType<FogFieldManager>() == null)
            EditorGUILayout.HelpBox(
                "No FogFieldManager in this scene, so nothing is painting a field for this sheet " +
                "to show.", MessageType.Warning);

        if (GUILayout.Button("Regenerate")) sheet.Generate();
    }
}
#endif
