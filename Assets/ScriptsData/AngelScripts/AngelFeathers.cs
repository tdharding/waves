using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Feathers laid over the angel's wings, skinned to the wing bones she already has, so every
// animation she plays moves them with no extra work.
//
// One feather shape is generated: a pointed base, a pointed top, and a number of points down each
// side, widest at a chosen point along its length. Copies are laid along each wing bone (Wing1 to
// Wing5), each bone's zone setting how many it holds and how big they are. The whole
// lot is baked into ONE mesh on this object's SkinnedMeshRenderer — one draw call for both wings.
//
// Only the LEFT wing is laid out; the right is its mirror image, bound to the matching R bones.
//
// Layout happens in the wings' REST pose, read from the bind poses of the angel's own wing mesh —
// never from wherever the bones happen to be — so the feathers sit the same whether she was
// generated perched, flying, or mid-landing.
//
// Each feather is rigid and follows the bone it sits on, blending towards the neighbouring bone as
// it nears a joint, so the row stays unbroken as the wing folds.
//
// Colour is a gradient, top (the feather's point) to bottom (where it meets the wing), with a grain
// scattering it up and down the feather. Both are painted into a small generated texture, shown
// through a generated material on the chosen shader (URP Unlit by default). The texture holds a few
// strips of different grain, handed out between feathers so neighbours don't match. The gradient is
// also written as vertex colour, for anyone swapping in a vertex-colour material instead.
//
// The mesh, texture and material are rebuilt whenever this is enabled or a value changes, and are
// never saved to disk.
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(SkinnedMeshRenderer))]
public class AngelFeathers : MonoBehaviour
{
    [Serializable]
    public class BoneZone
    {
        [Tooltip("The wing bone this zone covers. Filled in for you.")]
        public string bone;

        [Tooltip("Multiplies the length and width of the feathers on this bone.")]
        [Min(0f)] public float sizeFactor = 1f;

        [Tooltip("How many feathers sit on this bone.")]
        [Min(0)] public int feathers = 6;

        [Tooltip("Fans this bone's feathers out, each turning about its own base: the first is " +
                 "turned back by half this many degrees, the last forward by half, the rest spread between.")]
        [Range(-180f, 180f)] public float fan = 0f;

        [Tooltip("Slides this bone's feathers along the bone, in bone lengths: positive towards the " +
                 "wing tip, negative towards the shoulder. 0 = spread evenly along the bone.")]
        [Range(-1f, 1f)] public float offset = 0f;
    }

    [Header("Wing")]
    [Tooltip("The angel's mesh that is skinned to the wing bones — its bind poses give the wings' " +
             "rest pose. Found automatically if left empty.")]
    [SerializeField] SkinnedMeshRenderer wingMesh;

    [Tooltip("The first bone of the left wing (L.Wing1.bone). The chain is followed down from here. " +
             "Found by name if left empty.")]
    [SerializeField] Transform leftWingBone;

    [Tooltip("The first bone of the right wing (R.Wing1.bone). Found by name if left empty.")]
    [SerializeField] Transform rightWingBone;

    [Header("Feather shape")]
    [Tooltip("Feather length, as a fraction of the whole wing's length.")]
    [Min(0f)] [SerializeField] float length = 0.25f;

    [Tooltip("How many points down each side between the base and the top, the widest point " +
             "among them. 3 gives the drawn shape.")]
    [Range(1, 12)] [SerializeField] int pointsPerSide = 3;

    [Header("Widest point")]
    [Tooltip("Feather width at its widest point, as a fraction of the whole wing's length.")]
    [Min(0f)] [SerializeField] float width = 0.06f;

    [Tooltip("Moves the widest point up or down the feather: 0 = halfway, positive towards the " +
             "top, negative towards the base.")]
    [Range(-0.45f, 0.45f)] [SerializeField] float widestOffset = 0f;


    [Header("Placement")]
    [Tooltip("Angle between each feather and its bone. 0 = pointing along the bone towards the " +
             "wing tip; positive swings it back towards the trailing edge.")]
    [Range(-180f, 180f)] [SerializeField] float angle = 30f;

    [Tooltip("How far the feathers sit above the wing, as a fraction of the whole wing's length.")]
    [SerializeField] float liftOffWing = 0.01f;

    [Tooltip("Extra lift for each bone nearer the shoulder, so Wing1's feathers sit on top of " +
             "Wing2's, Wing2's on top of Wing3's, and so on. A fraction of the whole wing's length.")]
    [Min(0f)] [SerializeField] float boneStepLift = 0.005f;

    [Header("Colour")]
    [Tooltip("Colour at the feather's point.")]
    [SerializeField] Color topColour = Color.white;

    [Tooltip("Colour where the feather meets the wing.")]
    [SerializeField] Color bottomColour = new Color(0.25f, 0.25f, 0.28f);

    [Tooltip("How far down the feather, from the top, the change between the two colours runs. " +
             "1 = the whole feather; 0 = a hard switch at the very top.")]
    [Range(0f, 1f)] [SerializeField] float gradientLength = 0.5f;

    [Header("Grain")]
    [Tooltip("How far the grain scatters the gradient up and down the feather, as a fraction of " +
             "feather length. 0 = a clean gradient.")]
    [Range(0f, 0.5f)] [SerializeField] float grainAmount = 0.1f;

    [Tooltip("Size of one grain, as a fraction of feather length.")]
    [Range(0.005f, 0.2f)] [SerializeField] float grainSize = 0.02f;

    [Tooltip("Shader for the generated feather material; it must take a _BaseMap. URP Unlit is " +
             "filled in for you.")]
    [SerializeField] Shader shader;

    [Header("Bone zones")]
    [Tooltip("One per wing bone, Wing1 at the shoulder. Both wings share these — the right is a mirror of the left.")]
    [SerializeField] BoneZone[] zones = new BoneZone[0];

    // Within a bone, each feather is lifted a hair above the next one out, so overlapping feathers
    // never fight for the same depth and the shoulder end stays on top. A fraction of feather length.
    const float LayerStep = 0.002f;

    // Strips of differently seeded grain side by side in the texture, handed out between feathers.
    const int GrainStrips = 4;

    const string DefaultShader = "Universal Render Pipeline/Unlit";

    Mesh _mesh;
    Texture2D _texture;
    Material _material;

    void OnEnable() => Build();

    void OnDestroy()
    {
        Release(_mesh);
        Release(_texture);
        Release(_material);
    }

    static void Release(UnityEngine.Object generated)
    {
        if (generated == null) return;
        if (Application.isPlaying) Destroy(generated);
        else DestroyImmediate(generated);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (EditorUtility.IsPersistent(this)) return;
        if (shader == null) shader = Shader.Find(DefaultShader);
        SyncZones();
        EditorApplication.delayCall -= DelayedBuild;
        EditorApplication.delayCall += DelayedBuild;
    }

    void DelayedBuild()
    {
        if (this == null || !isActiveAndEnabled) return;
        Build();
    }
#endif

    [ContextMenu("Rebuild Feathers")]
    public void Build()
    {
#if UNITY_EDITOR
        if (EditorUtility.IsPersistent(this)) return;
#endif
        var renderer = GetComponent<SkinnedMeshRenderer>();
        Transform root = FindRoot();

        if (!FindChain(leftWingBone != null ? leftWingBone : FindDeep(root, "L.Wing1.bone"), out var left, out var leftEnd) ||
            !FindChain(rightWingBone != null ? rightWingBone : FindDeep(root, "R.Wing1.bone"), out var right, out _) ||
            left.Count != right.Count)
        {
            Debug.LogWarning($"{name}: AngelFeathers needs two wing bone chains of the same length under {root.name}.", this);
            renderer.sharedMesh = null;
            return;
        }

        SyncZones(left.Count);

        var reference = wingMesh != null ? wingMesh : FindWingMesh(root, left[0]);
        int n = left.Count;

        var restL = new Matrix4x4[n];
        var restR = new Matrix4x4[n];
        for (int i = 0; i < n; i++)
        {
            restL[i] = RestPose(left[i], reference);
            restR[i] = RestPose(right[i], reference);
        }

        // Points along the left wing: each bone's head, then the tip of the last.
        var points = new Vector3[n + 1];
        for (int i = 0; i < n; i++) points[i] = restL[i].GetColumn(3);
        if (leftEnd != null)
            points[n] = restL[n - 1].MultiplyPoint3x4(leftEnd.localPosition);
        else
            points[n] = n > 1 ? points[n - 1] + (points[n - 1] - points[n - 2]) : points[0] + Vector3.right;

        float wingLength = 0f;
        for (int i = 0; i < n; i++) wingLength += Vector3.Distance(points[i], points[i + 1]);

        // Her up and back, in this object's space. The wings rest flat, so up is the top of the wing.
        Vector3 up = transform.InverseTransformDirection(root.up).normalized;
        Vector3 back = transform.InverseTransformDirection(-root.forward).normalized;

        // The mirror between the wings, taken from every pair of matching bones — not just Wing1,
        // because both Wing1s start at the same point in the middle of her chest.
        Vector3 spread = Vector3.zero, middle = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            Vector3 l = restL[i].GetColumn(3), r = restR[i].GetColumn(3);
            spread += r - l;
            middle += (l + r) * 0.5f;
        }
        Vector3 mirrorNormal = spread.normalized;
        Vector3 mirrorPoint = middle / n;

        var rows = FeatherRows();

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var colours = new List<Color>();
        var uvs = new List<Vector2>();
        var weights = new List<BoneWeight>();
        var triangles = new List<int>();

        float featherLength = length * wingLength;
        float featherWidth = width * wingLength;

        for (int i = 0; i < n; i++)
        {
            var zone = zones[i];
            int count = zone.feathers;
            if (count <= 0) continue;

            Vector3 along = (points[i + 1] - points[i]).normalized;
            Vector3 normal = up - Vector3.Dot(up, along) * along;
            normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.Cross(along, back).normalized;
            Vector3 trailing = Vector3.Cross(normal, along).normalized;
            if (Vector3.Dot(trailing, back) < 0f) trailing = -trailing;

            float size = zone.sizeFactor;
            for (int k = 0; k < count; k++)
            {
                float t = (k + 0.5f) / count + zone.offset;

                float fanStep = count > 1 ? (float)k / (count - 1) : 0.5f;
                float a = (angle + Mathf.Lerp(-0.5f, 0.5f, fanStep) * zone.fan) * Mathf.Deg2Rad;
                Vector3 direction = Mathf.Cos(a) * along + Mathf.Sin(a) * trailing;
                Vector3 across = Vector3.Cross(normal, direction).normalized;

                float lift = (liftOffWing + (n - 1 - i) * boneStepLift) * wingLength
                           + (count - 1 - k) * LayerStep * featherLength * size;
                Vector3 origin = Vector3.LerpUnclamped(points[i], points[i + 1], t) + normal * lift;

                // Blend towards the neighbouring bone as the feather nears a joint.
                int other = i;
                float blend = 0f;
                // An offset can slide a feather past the joint, where it belongs wholly to the next bone.
                if (t < 0.5f && i > 0) { other = i - 1; blend = Mathf.Min(0.5f - t, 1f); }
                else if (t > 0.5f && i < n - 1) { other = i + 1; blend = Mathf.Min(t - 0.5f, 1f); }

                int strip = (int)(Hash(i, k) * GrainStrips) % GrainStrips;

                AddFeather(rows, origin, direction, across, normal, featherLength * size, featherWidth * size,
                           i, other, blend, false, mirrorPoint, mirrorNormal, strip,
                           vertices, normals, colours, uvs, weights, triangles);
                AddFeather(rows, origin, direction, across, normal, featherLength * size, featherWidth * size,
                           n + i, n + other, blend, true, mirrorPoint, mirrorNormal, (strip + GrainStrips / 2) % GrainStrips,
                           vertices, normals, colours, uvs, weights, triangles);
            }
        }

        var bones = new Transform[n * 2];
        var bindposes = new Matrix4x4[n * 2];
        for (int i = 0; i < n; i++)
        {
            bones[i] = left[i];
            bones[n + i] = right[i];
            bindposes[i] = restL[i].inverse;
            bindposes[n + i] = restR[i].inverse;
        }

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "AngelFeathers (generated)", hideFlags = HideFlags.HideAndDontSave };
        }
        _mesh.Clear();
        _mesh.indexFormat = vertices.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        _mesh.SetVertices(vertices);
        _mesh.SetNormals(normals);
        _mesh.SetColors(colours);
        _mesh.SetUVs(0, uvs);
        _mesh.boneWeights = weights.ToArray();
        _mesh.bindposes = bindposes;
        _mesh.SetTriangles(triangles, 0);
        _mesh.RecalculateBounds();

        renderer.sharedMesh = _mesh;
        renderer.bones = bones;
        renderer.rootBone = left[0].parent;
        // The wings sweep a long way from wherever the bounds were baked; let Unity follow them.
        renderer.updateWhenOffscreen = true;

        BuildMaterial(renderer);
    }

    // The gradient and its grain, painted into a texture: V runs base (0) to top (1) of a feather,
    // U holds GrainStrips side-by-side strips of grain. Each grain nudges where along the gradient
    // its texel reads from, so the grain lives where the colour is changing.
    void BuildMaterial(SkinnedMeshRenderer renderer)
    {
        if (shader == null) shader = Shader.Find(DefaultShader);
        if (shader == null)
        {
            Debug.LogWarning($"{name}: AngelFeathers has no shader for its material.", this);
            return;
        }

        int rowsHigh = Mathf.Clamp(Mathf.CeilToInt(1f / grainSize), 4, 1024);
        float aspect = length > 0f ? width / length : 1f;
        int columns = Mathf.Clamp(Mathf.RoundToInt(rowsHigh * aspect), 1, 256);
        int texWidth = columns * GrainStrips;

        if (_texture == null || _texture.width != texWidth || _texture.height != rowsHigh)
        {
            Release(_texture);
            _texture = new Texture2D(texWidth, rowsHigh, TextureFormat.RGBA32, false)
            {
                name = "AngelFeathers grain (generated)",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        var pixels = new Color[texWidth * rowsHigh];
        for (int y = 0; y < rowsHigh; y++)
        {
            float t = (y + 0.5f) / rowsHigh;
            for (int x = 0; x < texWidth; x++)
            {
                float scatter = (Hash(x + 7919, y + 104729) - 0.5f) * 2f * grainAmount;
                pixels[y * texWidth + x] = ColourAt(Mathf.Clamp01(t + scatter));
            }
        }
        _texture.SetPixels(pixels);
        _texture.Apply(false);

        if (_material == null || _material.shader != shader)
        {
            Release(_material);
            _material = new Material(shader) { name = "AngelFeathers (generated)", hideFlags = HideFlags.HideAndDontSave };
        }
        if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", _texture);
        _material.mainTexture = _texture;
        if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", Color.white);
        // Seen from above and below as the wings beat.
        if (_material.HasProperty("_Cull")) _material.SetFloat("_Cull", 0f);

        renderer.sharedMaterial = _material;
    }

    // 0..1, the same every time for the same pair.
    static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    // The feather's cross-sections from base (t = 0) to top (t = 1): the outline points, plus one
    // extra row where the colour gradient starts so the gradient comes out exact.
    List<Vector2> FeatherRows()
    {
        // The widest point is always a corner; the other points are shared between the stretch
        // below it and the stretch above it, by how long each is.
        float widest = 0.5f + widestOffset;
        int below = Mathf.RoundToInt((pointsPerSide - 1) * widest);
        int above = pointsPerSide - 1 - below;

        var outline = new List<Vector2> { new Vector2(0f, 0f) };
        for (int k = 1; k <= below; k++)
        {
            float t = widest * k / (below + 1f);
            outline.Add(new Vector2(t, Profile(t, widest)));
        }
        outline.Add(new Vector2(widest, 1f));
        for (int k = 1; k <= above; k++)
        {
            float t = widest + (1f - widest) * k / (above + 1f);
            outline.Add(new Vector2(t, Profile(t, widest)));
        }
        outline.Add(new Vector2(1f, 0f));

        float gradientStart = 1f - gradientLength;
        if (gradientStart <= 0f || gradientStart >= 1f) return outline;

        for (int k = 1; k < outline.Count; k++)
        {
            if (Mathf.Approximately(outline[k].x, gradientStart)) break;
            if (outline[k].x > gradientStart)
            {
                // Width taken off the straight edge between the two outline points, so the shape is unchanged.
                float s = Mathf.InverseLerp(outline[k - 1].x, outline[k].x, gradientStart);
                outline.Insert(k, new Vector2(gradientStart, Mathf.Lerp(outline[k - 1].y, outline[k].y, s)));
                break;
            }
        }
        return outline;
    }

    // Width at t (0..1 of full width), a smooth swell peaking at the widest point.
    static float Profile(float t, float widest)
    {
        float s = t < widest
            ? 0.5f * t / widest
            : 0.5f + 0.5f * (t - widest) / (1f - widest);
        return Mathf.Sin(Mathf.PI * s);
    }

    Color ColourAt(float t)
    {
        if (gradientLength <= 0f) return t >= 1f ? topColour : bottomColour;
        float g = Mathf.Clamp01((t - (1f - gradientLength)) / gradientLength);
        return Color.Lerp(bottomColour, topColour, g);
    }

    void AddFeather(List<Vector2> rows, Vector3 origin, Vector3 direction, Vector3 across, Vector3 normal,
                    float featherLength, float featherWidth, int bone, int otherBone, float blend,
                    bool mirror, Vector3 mirrorPoint, Vector3 mirrorNormal, int strip,
                    List<Vector3> vertices, List<Vector3> normals, List<Color> colours, List<Vector2> uvs,
                    List<BoneWeight> weights, List<int> triangles)
    {
        var weight = new BoneWeight
        {
            boneIndex0 = bone, weight0 = 1f - blend,
            boneIndex1 = otherBone, weight1 = blend,
        };

        Vector3 Place(Vector3 p) => mirror ? p - 2f * Vector3.Dot(p - mirrorPoint, mirrorNormal) * mirrorNormal : p;
        Vector3 faceNormal = mirror ? normal - 2f * Vector3.Dot(normal, mirrorNormal) * mirrorNormal : normal;

        // Each row is either a single point (the base and the top) or a left/right pair.
        var rowStart = new int[rows.Count];
        for (int r = 0; r < rows.Count; r++)
        {
            float t = rows[r].x;
            float half = rows[r].y * featherWidth * 0.5f;
            Vector3 centre = origin + direction * (featherLength * t);
            Color colour = ColourAt(t);

            // U kept just inside this feather's strip so point sampling never reads its neighbour.
            float StripU(float side) => (strip + Mathf.Lerp(0.01f, 0.99f, side)) / GrainStrips;

            rowStart[r] = vertices.Count;
            if (half <= 0f)
            {
                vertices.Add(Place(centre));
                uvs.Add(new Vector2(StripU(0.5f), t));
            }
            else
            {
                vertices.Add(Place(centre - across * half));
                vertices.Add(Place(centre + across * half));
                uvs.Add(new Vector2(StripU(0f), t));
                uvs.Add(new Vector2(StripU(1f), t));
            }
            int added = vertices.Count - rowStart[r];
            for (int v = 0; v < added; v++)
            {
                normals.Add(faceNormal);
                colours.Add(colour);
                weights.Add(weight);
            }
        }

        void Tri(int a, int b, int c)
        {
            if (mirror) { triangles.Add(a); triangles.Add(c); triangles.Add(b); }
            else        { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
        }

        for (int r = 0; r < rows.Count - 1; r++)
        {
            int a = rowStart[r], b = rowStart[r + 1];
            bool aPoint = r == 0 || rows[r].y <= 0f;
            bool bPoint = r + 1 == rows.Count - 1 || rows[r + 1].y <= 0f;

            if (aPoint && bPoint) continue;
            if (aPoint)      Tri(a, b + 1, b);
            else if (bPoint) Tri(a, a + 1, b);
            else
            {
                Tri(a, a + 1, b + 1);
                Tri(a, b + 1, b);
            }
        }
    }

    // A bone's rest pose in this object's space, from the wing mesh's bind poses. Falls back to the
    // bone's current pose if no wing mesh holds it — right only if she is stood in her rest pose.
    Matrix4x4 RestPose(Transform bone, SkinnedMeshRenderer reference)
    {
        if (reference != null && reference.sharedMesh != null)
        {
            int index = Array.IndexOf(reference.bones, bone);
            var binds = reference.sharedMesh.bindposes;
            if (index >= 0 && index < binds.Length)
                return transform.worldToLocalMatrix * reference.transform.localToWorldMatrix * binds[index].inverse;
        }
        Debug.LogWarning($"{name}: no wing mesh holds {bone.name}, so its current pose is used as the rest pose.", this);
        return transform.worldToLocalMatrix * bone.localToWorldMatrix;
    }

    Transform FindRoot()
    {
        var animator = GetComponentInParent<Animator>();
        return animator != null ? animator.transform : transform.root;
    }

    SkinnedMeshRenderer FindWingMesh(Transform root, Transform firstBone)
    {
        var own = GetComponent<SkinnedMeshRenderer>();
        foreach (var candidate in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (candidate != own && Array.IndexOf(candidate.bones, firstBone) >= 0)
                return candidate;
        return null;
    }

    // The wing's first bone, then down through each child in turn, stopping at the _end tip.
    static bool FindChain(Transform first, out List<Transform> chain, out Transform end)
    {
        chain = new List<Transform>();
        end = null;
        var bone = first;
        while (bone != null)
        {
            chain.Add(bone);
            Transform next = null;
            foreach (Transform child in bone)
            {
                if (child.name.EndsWith("_end")) end = child;
                else if (next == null) next = child;
            }
            bone = next;
        }
        return chain.Count > 0;
    }

    static Transform FindDeep(Transform parent, string boneName)
    {
        if (parent.name == boneName) return parent;
        foreach (Transform child in parent)
        {
            var found = FindDeep(child, boneName);
            if (found != null) return found;
        }
        return null;
    }

    void SyncZones()
    {
        var first = leftWingBone != null ? leftWingBone : FindDeep(FindRoot(), "L.Wing1.bone");
        if (FindChain(first, out var chain, out _)) SyncZones(chain.Count);
    }

    // One zone per wing bone, named after it; existing settings kept.
    void SyncZones(int count)
    {
        if (zones == null) zones = new BoneZone[0];
        if (zones.Length != count)
        {
            var resized = new BoneZone[count];
            for (int i = 0; i < count; i++)
                resized[i] = i < zones.Length && zones[i] != null ? zones[i] : new BoneZone();
            zones = resized;
        }
        for (int i = 0; i < count; i++)
        {
            if (zones[i] == null) zones[i] = new BoneZone();
            zones[i].bone = $"Wing{i + 1}";
        }
    }
}
