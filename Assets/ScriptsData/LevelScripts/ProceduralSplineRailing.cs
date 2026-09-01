using System.Collections.Generic;
using UnityEngine;

// Generates a "railing" spline-wall — the low-poly alternative to ProceduralSplineWall's solid box.
// LevelSpawner uses this prefab in two ways:
//   • TILES: walked along the path every tileSpacing world-units, rotated to the tangent, Build()
//     called per tile. A tile = a round rail tube (extruded along the path) + sparse round posts.
//   • NODES: instantiated once at each control node, BuildNode() called. A node = a round post
//     capped with a sphere sitting on the rail (the "node point"). The railing renders its own
//     nodes, so unlike the wall it needs no separate node-marker prefab.
//
// Rail / post / node thickness all come from the Grid Designer path's Thick value (one variable),
// so the rail is circular. Height (rail level / post tops) and Drop (how far posts go below water)
// also come from the path. Only postSpacing, roundSides and the node sphere size are prefab
// settings — tweak them and the mesh live-rebuilds in the editor (OnValidate).
//
// The built mesh is applied to two child renderers (visible surface + warning-line collision
// overlay) plus the visible child's MeshCollider, mirroring ProceduralSplineWall.
[DisallowMultipleComponent]
public class ProceduralSplineRailing : MonoBehaviour
{
    [Header("Posts")]
    [Tooltip("Target distance between posts (world units). Each segment between two nodes is split " +
             "into equal bays as close to this as possible, so posts stay evenly spaced and always " +
             "line up with the nodes.")]
    [SerializeField] float postSpacing = 0.9f;

    [Header("Roundness")]
    [Tooltip("Sides on the round rail, posts & node caps. More = smoother but higher poly. 8 reads as round.")]
    [Range(3, 24)]
    [SerializeField] int roundSides = 8;

    [Header("Child object names (auto-resolved by name if unassigned)")]
    [SerializeField] string visibleChildName      = "MeshVisible";
    [SerializeField] string warningLinesChildName = "WarningCollisionLines";

    [Header("Optional explicit references (override name lookup)")]
    [SerializeField] GameObject visibleChild;
    [SerializeField] GameObject warningLinesChild;

    [Header("Editor preview dimensions (world units)")]
    [SerializeField] float previewLength    = 0.5f;
    [SerializeField] float previewHeight    = 1f;
    [SerializeField] float previewDepth     = 7f;
    [SerializeField] float previewThickness = 0.09f;

    // Cached last-build args so an editor tweak (postSpacing / roundSides / sphere) rebuilds the same
    // piece in place via OnValidate. _mode records whether this instance was built as a tile or node.
    enum BuildMode { None, Tile, Node }
    BuildMode _mode = BuildMode.None;
    float _length, _tileSpacing, _height, _depth, _dist, _thickness, _segStart, _segLen;

    // length            = extent along the path (≈ tileSpacing, with LevelSpawner's overlap).
    // tileSpacing       = the un-overlapped step between tiles; defines this tile's "owned" span for
    //                     deciding which posts belong to it (so overlap doesn't duplicate posts).
    // heightAboveWater  = rail centre / post tops, measured from the prefab origin (on the waterline).
    // depthBelowWater   = how far posts drop beneath the waterline.
    // distanceAlongPath = this tile's centre distance from the path start (world units); posts are
    //                     phased against it and the rail texture flows continuously across tiles.
    // thickness         = rail & post diameter (the path's Thick value).
    // segmentStartDist  = this tile's segment's start distance from the path start (world units).
    // segmentLength     = this tile's segment's arc length (world units). Posts split the segment into
    //                     equal bays (~postSpacing) so they stay evenly spaced and land on the nodes.
    public void Build(float length, float tileSpacing, float heightAboveWater, float depthBelowWater, float distanceAlongPath, float thickness, float segmentStartDist, float segmentLength)
    {
        _mode = BuildMode.Tile;
        _length = length; _tileSpacing = tileSpacing; _height = heightAboveWater;
        _depth = depthBelowWater; _dist = distanceAlongPath; _thickness = thickness;
        _segStart = segmentStartDist; _segLen = segmentLength;

        Mesh mesh = BuildTileMesh(length, tileSpacing, heightAboveWater, depthBelowWater, distanceAlongPath, thickness, segmentStartDist, segmentLength);
        mesh.name = "ProceduralSplineRailing";
        ApplyMesh(ResolveChild(visibleChild, visibleChildName), mesh, assignCollider: true);
        ApplyMesh(ResolveChild(warningLinesChild, warningLinesChildName), mesh, assignCollider: false);
    }

    // Builds the marker at a control node: a round post from -depthBelowWater up to the rail, capped
    // with a sphere resting on top of the rail. Uses the same material (one mesh on the same renderer).
    public void BuildNode(float nodeHeight, float depthBelowWater, float thickness)
    {
        _mode = BuildMode.Node;
        _height = nodeHeight; _depth = depthBelowWater; _thickness = thickness;

        Mesh mesh = BuildNodeMesh(nodeHeight, depthBelowWater, thickness);
        mesh.name = "ProceduralSplineRailingNode";
        ApplyMesh(ResolveChild(visibleChild, visibleChildName), mesh, assignCollider: true);
        ApplyMesh(ResolveChild(warningLinesChild, warningLinesChildName), mesh, assignCollider: false);
    }

    Mesh BuildTileMesh(float length, float tileSpacing, float heightAboveWater, float depthBelowWater, float distanceAlongPath, float thickness, float segmentStartDist, float segmentLength)
    {
        int   sides = Mathf.Max(3, roundSides);
        float r     = Mathf.Max(0.0001f, thickness) * 0.5f;
        var   combines = new List<CombineInstance>(2);

        // Rail: a round tube extruded along the path (Z), centred at heightAboveWater. The extrude is
        // clamped to this segment's end nodes so the overlap (and the tiling in general) never pokes
        // past a node point — so a gap, or the path end, leaves the rail terminating cleanly at the
        // node. uOffset = the clamped piece's centre keeps the texture flowing across tiles.
        float railNear = distanceAlongPath - length * 0.5f;
        float railFar  = distanceAlongPath + length * 0.5f;
        if (segmentLength > 0.0001f)
        {
            railNear = Mathf.Max(railNear, segmentStartDist);
            railFar  = Mathf.Min(railFar,  segmentStartDist + segmentLength);
        }
        if (railFar > railNear + 0.00001f)
        {
            float railLen    = railFar - railNear;
            float railCenter = (railNear + railFar) * 0.5f;
            Mesh  rail       = BuildRoundRail(r, r, railLen, sides, railCenter, heightAboveWater);
            combines.Add(new CombineInstance
            {
                mesh      = rail,
                transform = Matrix4x4.Translate(new Vector3(0f, 0f, railCenter - distanceAlongPath)),
            });
        }

        // Posts: split THIS segment into equal bays (~postSpacing) and drop a post at each interior
        // division point. The segment ends are nodes (BuildNode puts a post + sphere there), so posts
        // stay evenly spaced and always line up with the nodes — no clustering at node points.
        if (postSpacing > 0.0001f && segmentLength > 0.0001f)
        {
            int   numBays   = Mathf.Max(1, Mathf.RoundToInt(segmentLength / postSpacing));
            float bay       = segmentLength / numBays;
            float halfOwned = tileSpacing * 0.5f;
            for (int i = 1; i < numBays; i++)   // interior points only; endpoints are nodes
            {
                float postDist = segmentStartDist + i * bay;
                if (postDist < distanceAlongPath - halfOwned || postDist >= distanceAlongPath + halfOwned)
                    continue;
                float localZ = postDist - distanceAlongPath;
                Mesh post = BuildRoundPost(r, -depthBelowWater, heightAboveWater, sides);
                combines.Add(new CombineInstance
                {
                    mesh      = post,
                    transform = Matrix4x4.Translate(new Vector3(0f, 0f, localZ)),
                });
            }
        }

        return Combine(combines);
    }

    Mesh BuildNodeMesh(float nodeHeight, float depthBelowWater, float thickness)
    {
        int   sides = Mathf.Max(3, roundSides);
        float r     = Mathf.Max(0.0001f, thickness) * 0.5f;
        var   combines = new List<CombineInstance>(2);

        // Post up to the rail centre, then a sphere resting on the rail's top surface.
        Mesh post = BuildRoundPost(r, -depthBelowWater, nodeHeight, sides);
        combines.Add(new CombineInstance { mesh = post, transform = Matrix4x4.identity });

        // Sphere diameter matches the railing thickness (so sphereR == the post/rail radius).
        float sphereR = r;
        float sphereY = nodeHeight + r + sphereR;   // rail top (centre + rail radius) + sphere radius
        Mesh sphere = BuildSphere(sphereY, sphereR, sides, Mathf.Max(2, sides / 2));
        combines.Add(new CombineInstance { mesh = sphere, transform = Matrix4x4.identity });

        return Combine(combines);
    }

    static Mesh Combine(List<CombineInstance> combines)
    {
        var mesh = new Mesh();
        mesh.CombineMeshes(combines.ToArray(), mergeSubMeshes: true, useMatrices: true);
        mesh.RecalculateBounds();
        // CombineMeshes copied the geometry; the transient part meshes can go so repeated
        // (live) rebuilds don't leak them.
        foreach (var c in combines) DestroyMesh(c.mesh);
        return mesh;
    }

    // A round tube extruded straight along Z (the path tangent), centred at (0, centerY, 0), with an
    // elliptical cross-section (radii rx across X, ry up Y — equal for a circular rail). Open ends —
    // consecutive tiles overlap so the rail looks continuous. Smooth radial normals; vertex channels
    // mirror ProceduralBoxMesh (verts/normals/uv/uv2) so CombineMeshes merges cleanly; uv2 is zeroed.
    // u runs along the path, v around the tube.
    static Mesh BuildRoundRail(float rx, float ry, float length, int sides, float uOffset, float centerY)
    {
        rx = Mathf.Max(0.0001f, rx);
        ry = Mathf.Max(0.0001f, ry);
        float hl = Mathf.Max(0.0001f, length) * 0.5f;
        int   cols = sides + 1;                 // duplicate seam column so UVs don't wrap-blend
        float circumference = Mathf.PI * (rx + ry);

        var verts = new Vector3[cols * 2];
        var norms = new Vector3[cols * 2];
        var uvs   = new Vector2[cols * 2];
        var uv2s  = new Vector4[cols * 2];
        var tris  = new int[sides * 6];

        for (int i = 0; i < cols; i++)
        {
            float a = (float)i / sides * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            var   nrm = new Vector3(c / rx, s / ry, 0f).normalized;   // outward ellipse normal
            float x = c * rx, y = centerY + s * ry;
            float vAround = (float)i / sides * circumference;

            int r0 = i * 2, r1 = i * 2 + 1;      // ring at z=-hl, ring at z=+hl
            verts[r0] = new Vector3(x, y, -hl); norms[r0] = nrm; uvs[r0] = new Vector2(-hl + uOffset, vAround);
            verts[r1] = new Vector3(x, y,  hl); norms[r1] = nrm; uvs[r1] = new Vector2( hl + uOffset, vAround);
        }

        int ti = 0;
        for (int i = 0; i < sides; i++)
        {
            int r0 = i * 2, r1 = i * 2 + 1, r0n = (i + 1) * 2, r1n = (i + 1) * 2 + 1;
            // Outward winding for a Z-axis tube (matches ProceduralBoxMesh's front faces).
            tris[ti++] = r0; tris[ti++] = r1n; tris[ti++] = r1;
            tris[ti++] = r0; tris[ti++] = r0n; tris[ti++] = r1n;
        }

        return MakeMesh(verts, norms, uvs, uv2s, tris);
    }

    // A smooth-shaded open cylinder (no caps — the top meets the rail, the bottom is bottomless below
    // water), axis along Y. Radial normals; channels mirror ProceduralBoxMesh; uv2 zeroed.
    static Mesh BuildRoundPost(float radius, float yBot, float yTop, int sides)
    {
        radius = Mathf.Max(0.0001f, radius);
        int   cols = sides + 1;
        float circumference = 2f * Mathf.PI * radius;

        var verts = new Vector3[cols * 2];
        var norms = new Vector3[cols * 2];
        var uvs   = new Vector2[cols * 2];
        var uv2s  = new Vector4[cols * 2];
        var tris  = new int[sides * 6];

        for (int i = 0; i < cols; i++)
        {
            float a  = (float)i / sides * Mathf.PI * 2f;
            float cx = Mathf.Cos(a), sz = Mathf.Sin(a);
            var   nrm = new Vector3(cx, 0f, sz);
            float u  = (float)i / sides * circumference;

            int b = i * 2, t = i * 2 + 1;
            verts[b] = new Vector3(cx * radius, yBot, sz * radius); norms[b] = nrm; uvs[b] = new Vector2(u, yBot);
            verts[t] = new Vector3(cx * radius, yTop, sz * radius); norms[t] = nrm; uvs[t] = new Vector2(u, yTop);
        }

        int ti = 0;
        for (int i = 0; i < sides; i++)
        {
            int b0 = i * 2, t0 = i * 2 + 1, b1 = (i + 1) * 2, t1 = (i + 1) * 2 + 1;
            // Outward winding, matching ProceduralBoxMesh: (BL,TR,BR),(BL,TL,TR).
            tris[ti++] = b0; tris[ti++] = t1; tris[ti++] = b1;
            tris[ti++] = b0; tris[ti++] = t0; tris[ti++] = t1;
        }

        return MakeMesh(verts, norms, uvs, uv2s, tris);
    }

    // A smooth UV sphere centred at (0, centerY, 0). Radial normals; channels mirror ProceduralBoxMesh;
    // uv2 zeroed. longitude = segments around, latitude = stacks top-to-bottom.
    static Mesh BuildSphere(float centerY, float radius, int longitude, int latitude)
    {
        longitude = Mathf.Max(3, longitude);
        latitude  = Mathf.Max(2, latitude);
        int cols = longitude + 1;
        int rows = latitude + 1;

        var verts = new Vector3[rows * cols];
        var norms = new Vector3[rows * cols];
        var uvs   = new Vector2[rows * cols];
        var uv2s  = new Vector4[rows * cols];
        var tris  = new int[latitude * longitude * 6];

        for (int lat = 0; lat < rows; lat++)
        {
            float theta = Mathf.PI * lat / latitude;  // 0 = top pole, PI = bottom pole
            float st = Mathf.Sin(theta), ct = Mathf.Cos(theta);
            for (int lon = 0; lon < cols; lon++)
            {
                float phi = 2f * Mathf.PI * lon / longitude;
                float cp = Mathf.Cos(phi), sp = Mathf.Sin(phi);
                var   nrm = new Vector3(st * cp, ct, st * sp);
                int   idx = lat * cols + lon;
                verts[idx] = new Vector3(nrm.x * radius, centerY + nrm.y * radius, nrm.z * radius);
                norms[idx] = nrm;
                uvs[idx]   = new Vector2((float)lon / longitude, 1f - (float)lat / latitude);
            }
        }

        int ti = 0;
        for (int lat = 0; lat < latitude; lat++)
        {
            for (int lon = 0; lon < longitude; lon++)
            {
                int a = lat * cols + lon;
                int b = a + 1;
                int c = (lat + 1) * cols + lon + 1;
                int d = (lat + 1) * cols + lon;
                // Outward winding: (a,b,c),(a,c,d).
                tris[ti++] = a; tris[ti++] = b; tris[ti++] = c;
                tris[ti++] = a; tris[ti++] = c; tris[ti++] = d;
            }
        }

        return MakeMesh(verts, norms, uvs, uv2s, tris);
    }

    static Mesh MakeMesh(Vector3[] verts, Vector3[] norms, Vector2[] uvs, Vector4[] uv2s, int[] tris)
    {
        var mesh = new Mesh();
        mesh.vertices  = verts;
        mesh.normals   = norms;
        mesh.uv        = uvs;
        mesh.SetUVs(1, new List<Vector4>(uv2s));
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

#if UNITY_EDITOR
    // Lets the prefab be previewed with a mesh in the editor without entering play mode.
    [ContextMenu("Rebuild Preview")]
    void RebuildPreview() =>
        Build(previewLength, Mathf.Max(0.05f, previewLength), previewHeight, previewDepth, 0f, previewThickness, 0f, previewLength);

    // Live-rebuild when a prefab field is tweaked in the inspector, reusing the last build args so a
    // spawned tile/node updates in the Game view. delayCall defers the mesh work out of OnValidate.
    void OnValidate()
    {
        if (_mode == BuildMode.None) return;
        var mode = _mode;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            if (mode == BuildMode.Tile) Build(_length, _tileSpacing, _height, _depth, _dist, _thickness, _segStart, _segLen);
            else                        BuildNode(_height, _depth, _thickness);
        };
    }
#endif

    GameObject ResolveChild(GameObject explicitRef, string childName)
    {
        if (explicitRef != null) return explicitRef;
        if (string.IsNullOrEmpty(childName)) return null;
        Transform t = transform.Find(childName);
        return t != null ? t.gameObject : null;
    }

    static void ApplyMesh(GameObject go, Mesh mesh, bool assignCollider)
    {
        if (go == null) return;

        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        if (assignCollider)
        {
            var col = go.GetComponent<MeshCollider>();
            if (col != null) col.sharedMesh = mesh;
        }
    }

    static void DestroyMesh(Mesh m)
    {
        if (m == null) return;
        if (Application.isPlaying) Destroy(m);
        else DestroyImmediate(m);
    }
}
