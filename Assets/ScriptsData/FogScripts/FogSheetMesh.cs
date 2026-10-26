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

    [Tooltip("A round sheet, Size across, instead of a square. Its subdivisions are rings out from " +
             "the centre. The level select designer sets it on round fog zone sheets.")]
    [SerializeField] bool circular;

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

    [Header("Baked")]
    [Tooltip("A mesh baked in the editor and shown instead of the flat one this builds. The level " +
             "select designer sets it on fog zone sheets, shaped by the Fog Map's Sheets " +
             "settings. Leave empty for the plain flat sheet.")]
    [SerializeField] Mesh bakedMesh;



    // Every sheet in the scene, so the fog manager can find the one the boat is under without a
    // scene search each frame — that is where the repellers' height window is measured from.
    static readonly System.Collections.Generic.List<FogSheetMesh> s_sheets =
        new System.Collections.Generic.List<FogSheetMesh>();
    public static System.Collections.Generic.IReadOnlyList<FogSheetMesh> All => s_sheets;

    MeshRenderer _renderer;

    /// <summary>
    /// Drawing right now. A fog zone hides its sheet by switching the renderer off rather than the
    /// object, so being enabled is not enough to say it is the plane being looked at.
    /// </summary>
    public bool Shown
    {
        get
        {
            if (!isActiveAndEnabled) return false;
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            return _renderer != null && _renderer.enabled;
        }
    }

    public Bounds WorldBounds
    {
        get
        {
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            return _renderer != null ? _renderer.bounds : new Bounds(transform.position, Vector3.zero);
        }
    }

    void OnEnable()
    {
        if (!s_sheets.Contains(this)) s_sheets.Add(this);
        Generate();
        ApplyTransform();
    }

    void OnDisable()
    {
        s_sheets.Remove(this);
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

        if (Application.isPlaying) SyncOcclusion();
    }

    // ── Occlusion ────────────────────────────────────────────────────────────
    // The Fog Map's Screen Circle Occlusion. The sheet gets an OcclusionFade set to Screen Circle and
    // a camera-only collider over its mesh, so CameraOccluderFader thins the fog in a circle round
    // the boat while the sheet is between the camera and the boat. Built in play only — the setting
    // lives on the map, so nothing is saved into the scene.
    OcclusionFade _occlusion;
    MeshCollider  _occlusionCollider;

    void SyncOcclusion()
    {
        bool on = FogFieldManager.ScreenCircleOcclusion && Shown;

        if (_occlusion == null)
        {
            if (!on) return;

            _occlusion = GetComponent<OcclusionFade>();
            if (_occlusion == null) _occlusion = gameObject.AddComponent<OcclusionFade>();
            _occlusion.style = OcclusionStyle.ScreenCircle;

            // Ignore Raycast, colliding with no layer: only the fader's ray (mask Everything) finds it.
            var child = new GameObject(OcclusionFade.ColliderChild);
            child.transform.SetParent(transform, false);
            int layer = LayerMask.NameToLayer("Ignore Raycast");
            child.layer = layer >= 0 ? layer : 2;

            _occlusionCollider = child.AddComponent<MeshCollider>();
            _occlusionCollider.convex        = false;
            _occlusionCollider.isTrigger     = false;
            _occlusionCollider.excludeLayers = ~0;
        }

        _occlusion.fades = on;
        if (_occlusionCollider == null) return;

        _occlusionCollider.enabled = on;
        var mesh = GetComponent<MeshFilter>().sharedMesh;
        if (on && _occlusionCollider.sharedMesh != mesh) _occlusionCollider.sharedMesh = mesh;
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

    // Cached for the same reason the wave plane is: this runs every LateUpdate, and finding the
    // manager by type is a whole-scene scan. Re-found only once it has actually gone.
    FogFieldManager _mgr;

    float ResolvedSize()
    {
        if (!matchFieldCoverage) return Mathf.Max(size, 0.01f);

        if (_mgr == null) _mgr = FindAnyObjectByType<FogFieldManager>();
        var mgr = _mgr;
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

        // A baked sheet keeps its baked shape. Building the flat one here would flatten it again
        // every time the sheet was enabled or the scene loaded.
        GetComponent<MeshFilter>().sharedMesh = bakedMesh != null ? bakedMesh
                                              : circular ? BuildCircleMesh(subs, _builtSize)
                                              : BuildMesh(subs, _builtSize);
        CacheLifts();

        // The sheet is transparent and lies flat on the water; casting or receiving shadows from
        // it is never wanted and costs a pass.
        var mr = GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

#if UNITY_EDITOR
        EnsureFogSortingLayer();
#endif
        mr.sortingLayerName = FogSortingLayer;
        mr.sortingOrder     = 0;
    }

    // Fog draws over every river. River water sits in Sorting Groups on Default (orders 0, 1, 2…
    // by branch depth) and pool water on Top — SetWaterSorting in the level select designer — and
    // the fog and the water are both transparent in the same queue, so the sorting layer is what
    // decides which is on top. This one goes after both.
    public const string FogSortingLayer = "Fog";
    static readonly string[] s_waterSortingLayers = { "Default", "Top" };

#if UNITY_EDITOR
    /// <summary>
    /// Adds the Fog sorting layer, last in the list, when it is missing, and says so if it has
    /// been dragged below a water layer.
    /// </summary>
    static void EnsureFogSortingLayer()
    {
        var tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManager == null || tagManager.Length == 0) return;

        var so     = new SerializedObject(tagManager[0]);
        var layers = so.FindProperty("m_SortingLayers");

        int fogIndex = -1;
        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == FogSortingLayer)
                fogIndex = i;

        if (fogIndex < 0)
        {
            layers.InsertArrayElementAtIndex(layers.arraySize);
            var added = layers.GetArrayElementAtIndex(layers.arraySize - 1);
            added.FindPropertyRelative("name").stringValue = FogSortingLayer;
            added.FindPropertyRelative("uniqueID").intValue = (int)(System.DateTime.Now.Ticks & 0x7fffffff);
            added.FindPropertyRelative("locked").boolValue  = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[Fog] Added the '{FogSortingLayer}' sorting layer, above the river water layers.");
            return;
        }

        foreach (var water in s_waterSortingLayers)
            if (SortingLayer.GetLayerValueFromName(FogSortingLayer) <= SortingLayer.GetLayerValueFromName(water))
                Debug.LogError($"[Fog] The '{FogSortingLayer}' sorting layer is below '{water}', so river " +
                               "water draws over the fog — move it to the bottom of the list in " +
                               "Project Settings > Tags and Layers.");
    }
#endif

    // The baked sheet's lift at each grid point, read off its vertices once per Generate, so the
    // fog manager can ask how high the sheet is at a point without touching the mesh every frame.
    // Null for the flat sheet.
    float[] _lifts;
    int     _liftRows;
    float   _liftSize;
    float   _liftRotation;   // the turn baked into the points, degrees as a world Y rotation
    int     _liftSegments;   // round sheet: points round each ring; 0 = a square sheet

    void CacheLifts()
    {
        _lifts = null;
        _liftRotation = 0f;
        _liftSegments = 0;
        if (bakedMesh == null || !bakedMesh.isReadable) return;

        var verts = bakedMesh.vertices;

        if (circular)
        {
            // The ring count whose layout has this many points — CircleSegments grows with it,
            // so only one can match.
            int rings = 0;
            for (int r = 1; r <= 256 && rings == 0; r++)
                if (1 + r * CircleSegments(r) == verts.Length) rings = r;
            if (rings == 0) return;

            _lifts        = new float[verts.Length];
            _liftRows     = rings;
            _liftSegments = CircleSegments(rings);
            _liftSize     = ((Vector2)verts[verts.Length - 1]).magnitude * 2f;   // the outer ring's radius
            for (int i = 0; i < verts.Length; i++) _lifts[i] = -verts[i].z;
            return;
        }

        int rows  = Mathf.RoundToInt(Mathf.Sqrt(verts.Length));
        if (rows < 2 || rows * rows != verts.Length) return;

        // The first row's run from its first point to its last: its length is the sheet's size,
        // its direction (local X/Y = world X/Z) the turn BuildMesh baked in.
        Vector2 run = (Vector2)verts[rows - 1] - (Vector2)verts[0];
        _lifts        = new float[verts.Length];
        _liftRows     = rows;
        _liftSize     = run.magnitude;
        _liftRotation = Mathf.Atan2(-run.y, run.x) * Mathf.Rad2Deg;
        // Local -Z is world up, the same as BuildMesh lays it.
        for (int i = 0; i < verts.Length; i++) _lifts[i] = -verts[i].z;
    }

    /// <summary>
    /// The sheet's world height at a world XZ — the plane's height plus however far Landscape
    /// Influence bent it there. Past the sheet's edge it carries on at the edge's height. A flat
    /// sheet is the same height everywhere.
    /// </summary>
    public float SurfaceY(Vector2 xz)
    {
        Vector3 p = transform.position;
        if (_lifts == null || _liftSize <= 0f) return p.y;

        if (_liftSegments > 0) return p.y + CircleLift(new Vector2(xz.x - p.x, xz.y - p.z));

        // Into the sheet's own unturned frame first.
        Vector3 o    = Quaternion.Euler(0f, -_liftRotation, 0f) * new Vector3(xz.x - p.x, 0f, xz.y - p.z);
        int     subs = _liftRows - 1;
        float   half = _liftSize * 0.5f;
        float gx = Mathf.Clamp((o.x + half) / _liftSize * subs, 0f, subs);
        float gy = Mathf.Clamp((o.z + half) / _liftSize * subs, 0f, subs);

        int   x0 = Mathf.Min((int)gx, subs - 1), y0 = Mathf.Min((int)gy, subs - 1);
        float fx = gx - x0, fy = gy - y0;
        int   i  = y0 * _liftRows + x0;

        float lift = Mathf.Lerp(Mathf.Lerp(_lifts[i],             _lifts[i + 1],             fx),
                                Mathf.Lerp(_lifts[i + _liftRows], _lifts[i + _liftRows + 1], fx), fy);
        return p.y + lift;
    }

    // A round sheet's lift at an offset from its centre: between the two rings either side, and
    // between the two points round each; inside the first ring, towards the centre point.
    float CircleLift(Vector2 o)
    {
        int   rings = _liftRows, segs = _liftSegments;
        float gr    = Mathf.Clamp(o.magnitude / (_liftSize * 0.5f) * rings, 0f, rings);
        float ga    = Mathf.Repeat(Mathf.Atan2(o.y, o.x) / (Mathf.PI * 2f), 1f) * segs;
        int   s0    = Mathf.Min((int)ga, segs - 1), s1 = (s0 + 1) % segs;
        float fa    = ga - s0;

        float Ring(int k) => k == 0 ? _lifts[0]
            : Mathf.Lerp(_lifts[1 + (k - 1) * segs + s0], _lifts[1 + (k - 1) * segs + s1], fa);

        int k0 = Mathf.Min((int)gr, rings - 1);
        return Mathf.Lerp(Ring(k0), Ring(k0 + 1), gr - k0);
    }

    // When the last search came up empty. A scene with no wave plane at all is a real case now —
    // the level select world has none, because out there the sheet hovers over the landscape
    // rather than lying on water — and searching for one that is never going to be there is a
    // whole-scene type scan every single frame, forever.
    float _wavePlaneSearchClock;
    const float WAVE_PLANE_RESEARCH = 1f;

    /// <summary>
    /// The wave plane, which is where the water actually is. Cached, and re-found when it goes
    /// stale — levels rebuild their arena, so a reference caught at edit time does not survive.
    /// Null is a legitimate answer, so a failed search is throttled rather than repeated: the
    /// caller falls back to its own waterline Y, which is what an exterior scene wants anyway.
    /// </summary>
    Transform ResolveWavePlane()
    {
        if (wavePlane != null) return wavePlane;

        _wavePlaneSearchClock -= Application.isPlaying ? Time.deltaTime : 1f / 60f;
        if (_wavePlaneSearchClock > 0f) return null;
        _wavePlaneSearchClock = WAVE_PLANE_RESEARCH;

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
        if (circular)
        {
            UnityEditor.Handles.color = new Color(0.5f, 1f, 0.6f, 0.7f);
            UnityEditor.Handles.DrawWireDisc(c, Vector3.up, s * 0.5f);
            UnityEditor.Handles.Label(c + new Vector3(0f, 0f, s * 0.5f), $"fog sheet {s:0.#} u");
            return;
        }
        var turn = Quaternion.Euler(0f, _liftRotation, 0f);
        var corners = new Vector3[]
        {
            c + turn * new Vector3(-s * 0.5f, 0f, -s * 0.5f),
            c + turn * new Vector3(-s * 0.5f, 0f,  s * 0.5f),
            c + turn * new Vector3( s * 0.5f, 0f,  s * 0.5f),
            c + turn * new Vector3( s * 0.5f, 0f, -s * 0.5f),
        };
        UnityEditor.Handles.color = new Color(0.5f, 1f, 0.6f, 0.9f);
        UnityEditor.Handles.DrawSolidRectangleWithOutline(corners,
            new Color(0.5f, 1f, 0.6f, 0.03f), new Color(0.5f, 1f, 0.6f, 0.7f));
        UnityEditor.Handles.Label(corners[1], $"fog sheet {s:0.#} u");
    }
#endif

    /// <summary>
    /// The sheet as a grid of <paramref name="subs"/> squares a side. <paramref name="heights"/>,
    /// when given, lifts each point that many metres (world up), row by row from the -Z edge —
    /// (subs + 1)² of them. Left null the sheet is flat. <paramref name="rotation"/> turns the
    /// square round its centre, in degrees as a world Y rotation — baked into the points, since
    /// the object itself is always held flat.
    /// </summary>
    public static Mesh BuildMesh(int subs, float size, float[] heights = null, float rotation = 0f)
    {
        int rows = subs + 1;
        var verts = new Vector3[rows * rows];
        var uvs   = new Vector2[rows * rows];
        var tris  = new int[subs * subs * 6];

        float step = size / subs;
        float half = size * 0.5f;
        var   turn = Quaternion.Euler(0f, rotation, 0f);

        for (int y = 0; y < rows; y++)
        for (int x = 0; x < rows; x++)
        {
            int i = y * rows + x;
            // Local -Z is world up: the object is turned 90 degrees about X to lie flat, so local
            // X and Y are world X and Z.
            float   lift = heights != null ? heights[i] : 0f;
            Vector3 o    = turn * new Vector3(x * step - half, 0f, y * step - half);
            verts[i] = new Vector3(o.x, o.z, -lift);
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

    /// <summary>Points round each ring of a round sheet with <paramref name="rings"/> rings.</summary>
    public static int CircleSegments(int rings) => Mathf.Max(24, rings * 8);

    /// <summary>
    /// Where point <paramref name="seg"/> of ring <paramref name="ring"/> sits, as world X/Z from
    /// the centre. Ring 0 is the centre point itself; ring <paramref name="rings"/> is the rim.
    /// </summary>
    public static Vector2 CirclePoint(int ring, int seg, int rings, float size)
    {
        float r = ring / (float)rings * size * 0.5f;
        float a = seg / (float)CircleSegments(rings) * Mathf.PI * 2f;
        return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
    }

    /// <summary>
    /// A round sheet, <paramref name="size"/> across: a centre point then <paramref name="rings"/>
    /// rings of <see cref="CircleSegments"/> points out to the rim. <paramref name="heights"/>,
    /// when given, lifts each point that many metres (world up) — the centre first, then each ring
    /// from the inside out, each round from world +X. Left null the sheet is flat. UV is laid flat
    /// across it, 0..1 over the width, as on the square sheet.
    /// </summary>
    public static Mesh BuildCircleMesh(int rings, float size, float[] heights = null)
    {
        rings = Mathf.Max(1, rings);
        int segs  = CircleSegments(rings);
        int count = 1 + rings * segs;
        var verts = new Vector3[count];
        var uvs   = new Vector2[count];
        var tris  = new int[(segs + (rings - 1) * segs * 2) * 3];

        int Index(int k, int s) => k == 0 ? 0 : 1 + (k - 1) * segs + s % segs;

        for (int k = 0; k <= rings; k++)
        for (int s = 0; s < (k == 0 ? 1 : segs); s++)
        {
            int     i    = Index(k, s);
            Vector2 o    = CirclePoint(k, s, rings, size);
            float   lift = heights != null ? heights[i] : 0f;
            // Local -Z is world up, local X and Y are world X and Z, as in BuildMesh.
            verts[i] = new Vector3(o.x, o.y, -lift);
            uvs[i]   = new Vector2(o.x / size + 0.5f, o.y / size + 0.5f);
        }

        // Wound the same way round as BuildMesh's squares.
        int t = 0;
        for (int s = 0; s < segs; s++)
        {
            tris[t++] = 0;  tris[t++] = Index(1, s + 1);  tris[t++] = Index(1, s);
        }
        for (int k = 1; k < rings; k++)
        for (int s = 0; s < segs; s++)
        {
            int a = Index(k, s), b = Index(k, s + 1), c = Index(k + 1, s), d = Index(k + 1, s + 1);
            tris[t++] = a;  tris[t++] = b;  tris[t++] = c;
            tris[t++] = b;  tris[t++] = d;  tris[t++] = c;
        }

        var mesh = new Mesh { name = "FogSheetRound" };
        if (count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
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
