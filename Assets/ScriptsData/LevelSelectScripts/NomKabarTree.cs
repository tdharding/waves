using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One arm of a NomKabarTree: a tube reaching out of the TreeNodeHub on its own heading, bending
/// up, and ending on a round TreeNodeEnd that points up with a slight lean.
///
/// Seen side on, in the arm's own upright plane, the arm is two straight legs and the bend
/// between them. The upper leg runs into the end node along the lean; the lower leg leaves the
/// hub's centre turned the Bend down from it — so 90° on an upright end leaves the hub level,
/// 45° leaves it climbing, 0° is one straight arm. Bend Area is how much of the two legs the bend
/// takes up: small is a tight elbow, 1 is one long sweep from hub to end.
/// </summary>
[Serializable]
public class NomKabarTreeArm
{
    [Tooltip("Which way the arm reaches out of the hub, seen from above, in degrees. 0 = +Z (north), " +
             "90 = +X (east).")]
    public float angle;

    [Tooltip("Radius of the arm's tube.")]
    [Min(0.001f)] public float radius = 0.04f;

    [Tooltip("How far out from the hub's centre the TreeNodeEnd's centre is, level.")]
    [Min(0f)] public float reach = 0.35f;

    [Tooltip("How far above the hub's centre the TreeNodeEnd's centre is.")]
    public float rise = 0.5f;

    [Tooltip("TreeArmBend — how far the arm turns, in degrees, from where it leaves the hub to where it " +
             "points at its end. 90 on an upright end leaves the hub level; 0 is a straight arm.")]
    [Range(0f, 170f)] public float bend = 80f;

    [Tooltip("TreeArmBendArea — how much of the arm the bend is spread over. Small is a tight elbow, " +
             "1 is one long sweep from the hub to the end.")]
    [Range(0.05f, 1f)] public float bendArea = 0.6f;

    [Tooltip("TreeArmEndLean — how far the end of the arm leans outward from upright, in degrees.")]
    [Range(-45f, 45f)] public float endLean = 10f;

    [Tooltip("Size of the TreeNodeEnd on this arm, as a multiple of the arm's radius.")]
    [Min(1f)] public float endScale = 2.5f;

    public NomKabarTreeArm Clone() => (NomKabarTreeArm)MemberwiseClone();

    /// <summary>The TreeNodeEnd's radius.</summary>
    public float EndRadius => Mathf.Max(0.001f, radius) * Mathf.Max(1f, endScale);
}

/// <summary>
/// A NomKabarTree — a natural piece of the landscape: a bell-shaped TreeBase, a TreeStem rising
/// from its neck to a rounded TreeNodeHub, and 1–5 TreeArms out of the hub, each bending up to a
/// round TreeNodeEnd.
///
///      O     O    O      <- TreeNodeEnd on every arm (End Scale × arm radius)
///       \    |   /
///        \___|__/        <- TreeArms, each on its own Angle round the hub
///           (_)          <- TreeNodeHub (hub radius, squashed by Hub Squash)
///            |           <- TreeStem (stem radius, stem height)
///          _/ \_
///        /_______\       <- TreeBase: a bell from Foot Radius in to the stem over Base Height
///
/// Nothing meets with a seam. The bell and the stem are one revolved outline, the bell's neck
/// running straight on into the stem. Where the stem and each arm meet the hub, and each arm meets
/// its end node, the tube flares out onto the ball (Stem Join, Hub Join, End Join): its last ring
/// lies on the ball's surface and takes the ball's own normals, so shape and shading carry on.
///
/// Built standing at the origin, base on y = 0, in the landscape's material — the designer writes
/// the landscape's noise into UV1 where it stands. It holds all five arms; Arm Count says how many
/// are built, so lowering it and raising it again gives the same arms back.
/// </summary>
[Serializable]
public class NomKabarTree
{
    public const int MaxArms = 5;

    [Header("TreeBase")]
    [Tooltip("Radius of the bell's foot on the ground.")]
    [Min(0.001f)] public float baseFootRadius = 0.24f;

    [Tooltip("Height of the bell, from the ground to where it has narrowed into the stem.")]
    [Min(0f)] public float baseHeight = 0.2f;

    [Tooltip("How high the foot rises before the bell turns in to its neck — low is a flat spread " +
             "foot, high a full rounded shoulder.")]
    [Range(0f, 1f)] public float baseShoulder = 0.52f;

    [Header("TreeStem")]
    [Tooltip("Radius of the stem.")]
    [Min(0.001f)] public float stemRadius = 0.06f;

    [Tooltip("Height of the stem, from the bell's neck up to where it meets the hub.")]
    [Min(0f)] public float stemHeight = 0.25f;

    [Header("TreeNodeHub")]
    [Tooltip("Radius of the hub the arms reach out of, across.")]
    [Min(0.001f)] public float hubRadius = 0.1f;

    [Tooltip("Height of the hub as a share of its width. 1 is round; less is a squatter, rounder-edged lump.")]
    [Range(0.2f, 1.5f)] public float hubSquash = 0.8f;

    [Header("Joins")]
    [Tooltip("How far up the stem the TreeBase's bell carries on narrowing before it is the stem — a " +
             "longer, softer neck. 0 = the bell meets the stem at Base Height.")]
    [Min(0f)] public float baseJoin = 0f;

    [Tooltip("How far the stem flares out where it meets the TreeNodeHub — out over the hub and back down " +
             "the stem. 0 = no flare.")]
    [Min(0f)] public float stemJoin = 0.04f;

    [Tooltip("How far every arm flares out where it leaves the TreeNodeHub. 0 = no flare.")]
    [Min(0f)] public float hubJoin = 0.03f;

    [Tooltip("How far every arm flares out where it meets its TreeNodeEnd. 0 = no flare.")]
    [Min(0f)] public float endJoin = 0.03f;

    [Header("TreeArms")]
    [Tooltip("How many arms the tree has, 1 to 5.")]
    [Range(1, MaxArms)] public int armCount = 3;

    [Tooltip("Every arm the tree can have — the first Arm Count of them are built.")]
    public List<NomKabarTreeArm> arms = new();

    /// <summary>
    /// The tree as it comes with nothing to start from: three arms a third of a turn apart, and two
    /// more ready for when Arm Count is raised.
    /// </summary>
    public static NomKabarTree CodeDefault()
    {
        var tree = new NomKabarTree();
        tree.EnsureArms();
        return tree;
    }

    // Headings for the arms a fresh tree starts with: the first three evenly round, the other two
    // in two of the gaps.
    static readonly float[] DefaultAngles = { 0f, 120f, 240f, 60f, 180f };

    /// <summary>Fills the arm list up to <see cref="MaxArms"/>, each new one at its default heading.</summary>
    public void EnsureArms()
    {
        arms ??= new List<NomKabarTreeArm>();
        while (arms.Count < MaxArms)
            arms.Add(new NomKabarTreeArm { angle = DefaultAngles[arms.Count] });
        for (int i = 0; i < arms.Count; i++)
            arms[i] ??= new NomKabarTreeArm { angle = DefaultAngles[Mathf.Min(i, MaxArms - 1)] };
    }

    /// <summary>True when the tree has never been given arms — a placement not yet filled in.</summary>
    public bool IsUnset => arms == null || arms.Count == 0;

    /// <summary>How many arms are built.</summary>
    public int BuiltArms => Mathf.Clamp(armCount, 1, Mathf.Min(MaxArms, arms?.Count ?? 0));

    public NomKabarTree Clone()
    {
        var copy = (NomKabarTree)MemberwiseClone();
        copy.arms = new List<NomKabarTreeArm>();
        if (arms != null)
            foreach (var arm in arms) copy.arms.Add(arm?.Clone());
        return copy;
    }

    /// <summary>
    /// A copy with every size multiplied by <paramref name="scale"/> — the placement's own scale,
    /// never saved into a preset. Angles and shares stay as they are. 0 or below counts as 1.
    /// </summary>
    public NomKabarTree Scaled(float scale)
    {
        var copy = Clone();
        if (scale <= 0f || Mathf.Approximately(scale, 1f)) return copy;
        float s = scale;

        copy.baseFootRadius *= s;
        copy.baseHeight     *= s;
        copy.stemRadius     *= s;
        copy.stemHeight     *= s;
        copy.hubRadius      *= s;
        copy.baseJoin       *= s;
        copy.stemJoin       *= s;
        copy.hubJoin        *= s;
        copy.endJoin        *= s;
        foreach (var arm in copy.arms)
        {
            if (arm == null) continue;
            arm.radius *= s;
            arm.reach  *= s;
            arm.rise   *= s;
        }
        return copy;
    }

    // ── Where things are, measured up from the base ──

    float HubHalfHeight => Mathf.Max(0.001f, hubRadius) * Mathf.Max(0.05f, hubSquash);

    /// <summary>
    /// The hub's centre above the base: the bell, the stem, and then the hub seated so the stem's
    /// top edge lies on its surface — the stem runs its full height into it with no ledge.
    /// </summary>
    public float HubCentreY
    {
        get
        {
            float a = Mathf.Max(0.001f, hubRadius);
            float r = Mathf.Min(Mathf.Max(0.001f, stemRadius), a);
            float seat = HubHalfHeight * Mathf.Sqrt(Mathf.Max(0f, 1f - r * r / (a * a)));
            return Mathf.Max(0f, baseHeight) + Mathf.Max(0f, stemHeight) + seat;
        }
    }

    /// <summary>
    /// How far the tree reaches out from its centre, seen from above — the bell's foot, or the
    /// farthest TreeNodeEnd's edge.
    /// </summary>
    public float FootprintRadius
    {
        get
        {
            float r = Mathf.Max(0.001f, baseFootRadius);
            for (int i = 0; i < BuiltArms; i++)
            {
                var arm = arms[i];
                if (arm != null) r = Mathf.Max(r, arm.reach + arm.EndRadius);
            }
            return r;
        }
    }

    /// <summary>The TreeNodeEnd's centre for arm <paramref name="i"/>, in the tree's own space.</summary>
    public Vector3 EndCentre(int i)
    {
        var arm = arms[i];
        return Vector3.up * (HubCentreY + arm.rise) + Outward(arm.angle) * arm.reach;
    }

    /// <summary>Level direction for a heading in degrees — 0 is +Z, 90 is +X.</summary>
    public static Vector3 Outward(float angle)
    {
        float a = angle * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
    }

    // ─────────────────────────────────────────────
    // MESH
    // ─────────────────────────────────────────────

    const int Sides     = 18;
    const int ArmSides  = 14;
    const int BellRings = 11;
    const int HubRings  = 11;

    /// <summary>
    /// The whole tree as one mesh, base on y = 0. The bell and stem are one revolved outline; the
    /// stem and the arms flare onto the balls they meet (the Joins), each flare's last ring lying
    /// on the ball's surface with the ball's normals. A join of 0 leaves the tube running on into
    /// the ball instead, as an overlap.
    /// </summary>
    public Mesh Build()
    {
        EnsureArms();

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();

        float hubY = HubCentreY;
        var   hub  = new Ball(Vector3.up * hubY, Mathf.Max(0.001f, hubRadius), HubHalfHeight);

        // ── TreeBase and TreeStem, one outline from the buried foot up onto the hub ──
        AppendTrunk(verts, norms, uvs, tris, hub);

        // ── TreeNodeHub ──
        AppendEllipsoid(verts, norms, uvs, tris, hub.centre, hub.across, hub.half);

        // ── TreeArms, each flaring off the hub and onto its TreeNodeEnd ──
        for (int i = 0; i < BuiltArms; i++)
        {
            var arm = arms[i];
            if (arm == null) continue;

            var end = new Ball(EndCentre(i), arm.EndRadius, arm.EndRadius);
            AppendArm(verts, norms, uvs, tris, ArmCentreline(arm, hubY), Mathf.Max(0.001f, arm.radius),
                      hub, Mathf.Max(0f, hubJoin), end, Mathf.Max(0f, endJoin));

            AppendEllipsoid(verts, norms, uvs, tris, end.centre, end.across, end.across);
        }

        var mesh = new Mesh { name = "NomKabarTree" };
        if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);

        // UV2.x = 1 says "a tree" to LandscapeShading.hlsl, which the tree shares with the hills:
        // it is what puts the landscape tuner's Trees decal groups on it rather than the
        // Landscape ones. The tiles carry no UV2 and read 0.
        var tag = new List<Vector2>(verts.Count);
        for (int i = 0; i < verts.Count; i++) tag.Add(new Vector2(1f, 0f));
        mesh.SetUVs(2, tag);

        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// The points down the middle of an arm, from the hub's centre to its end node's centre. The
    /// shape is worked out to the point where the tube's rim would sit on the node's surface; from
    /// there it runs straight on into the node, so the end join always finds the node around it.
    /// </summary>
    public List<Vector3> ArmCentreline(NomKabarTreeArm arm, float hubY)
    {
        // Worked in the arm's upright plane: u out along its heading, v up.
        float lean = Mathf.Clamp(arm.endLean, -45f, 45f) * Mathf.Deg2Rad;
        float bend = Mathf.Clamp(arm.bend, 0f, 170f) * Mathf.Deg2Rad;
        float r    = Mathf.Max(0.001f, arm.radius);
        float R    = arm.EndRadius;

        var endDir   = new Vector2(Mathf.Sin(lean), Mathf.Cos(lean));
        var centre   = new Vector2(Mathf.Max(0f, arm.reach), arm.rise);
        var end      = centre - endDir * Mathf.Sqrt(Mathf.Max(0f, R * R - r * r));
        float phi    = Mathf.PI * 0.5f - lean - bend;        // the lower leg, up from level
        var startDir = new Vector2(Mathf.Cos(phi), Mathf.Sin(phi));

        var plane = new List<Vector2>();
        const int Steps = 42;

        // Where the two legs meet: start + a·startDir = end − b·endDir.
        float det = startDir.x * endDir.y - startDir.y * endDir.x;
        float a = 0f, b = 0f;
        bool  corner = Mathf.Abs(det) > 1e-4f;
        if (corner)
        {
            a = (end.x * endDir.y - end.y * endDir.x) / det;
            b = (startDir.x * end.y - startDir.y * end.x) / det;
            corner = a > 1e-4f && b > 1e-4f;
        }

        if (corner)
        {
            // Straight, the bend (a curve inside the corner), straight.
            float   t = Mathf.Clamp(arm.bendArea, 0.05f, 1f);
            Vector2 k = startDir * a;
            Vector2 p = k - startDir * (a * t);
            Vector2 q = k + endDir   * (b * t);

            plane.Add(Vector2.zero);
            if (t < 1f) plane.Add(p);
            for (int i = 1; i < Steps; i++)
            {
                float s = i / (float)Steps;
                plane.Add((1 - s) * (1 - s) * p + 2f * (1 - s) * s * k + s * s * q);
            }
            plane.Add(q);
            if (t < 1f) plane.Add(end);
        }
        else
        {
            // The end can't be reached with that bend (or there is none): a smooth sweep leaving
            // the hub the way the bend asks and arriving along the lean.
            float   len = Mathf.Max(0.001f, end.magnitude);
            Vector2 c1  = startDir * (len / 3f);
            Vector2 c2  = end - endDir * (len / 3f);
            for (int i = 0; i <= Steps; i++)
            {
                float s = i / (float)Steps, m = 1 - s;
                plane.Add(m * m * m * Vector2.zero + 3 * m * m * s * c1 + 3 * m * s * s * c2 + s * s * s * end);
            }
        }

        // On into the end node's centre, along the lean.
        if ((centre - end).sqrMagnitude > 1e-10f) plane.Add(centre);

        Vector3 outward = Outward(arm.angle);
        var line = new List<Vector3>(plane.Count);
        foreach (var pt in plane)
            line.Add(outward * pt.x + Vector3.up * (hubY + pt.y));
        return Resample(line, Mathf.Max(0.003f, r * 0.8f));
    }

    // Even spacing down a polyline, so the tube's rings come out evenly whatever the bend.
    static List<Vector3> Resample(List<Vector3> line, float step)
    {
        float total = 0f;
        for (int i = 1; i < line.Count; i++) total += Vector3.Distance(line[i - 1], line[i]);
        if (total <= 1e-5f) return line;

        int count = Mathf.Clamp(Mathf.CeilToInt(total / step), 2, 160);
        var result = new List<Vector3>(count + 1) { line[0] };

        int   seg = 1;
        float segStart = 0f;
        float segLen   = Vector3.Distance(line[0], line[1]);
        for (int i = 1; i < count; i++)
        {
            float d = total * i / count;
            while (seg < line.Count - 1 && segStart + segLen < d)
            {
                segStart += segLen;
                seg++;
                segLen = Vector3.Distance(line[seg - 1], line[seg]);
            }
            float t = segLen > 1e-6f ? (d - segStart) / segLen : 0f;
            result.Add(Vector3.Lerp(line[seg - 1], line[seg], Mathf.Clamp01(t)));
        }
        result.Add(line[line.Count - 1]);
        return result;
    }

    // A ball the stem and the arms meet: the hub, squashed upright, or an end node, round.
    readonly struct Ball
    {
        public readonly Vector3 centre;
        public readonly float   across;   // level radius
        public readonly float   half;     // half its height

        public Ball(Vector3 centre, float across, float half)
        {
            this.centre = centre;
            this.across = Mathf.Max(0.0001f, across);
            this.half   = Mathf.Max(0.0001f, half);
        }

        /// <summary>Below 0 inside the ball, above 0 outside.</summary>
        public float Inside(Vector3 p)
        {
            Vector3 d = p - centre;
            return (d.x * d.x + d.z * d.z) / (across * across) + d.y * d.y / (half * half) - 1f;
        }

        public Vector3 Normal(Vector3 p)
        {
            Vector3 d = p - centre;
            return new Vector3(d.x / (across * across), d.y / (half * half), d.z / (across * across)).normalized;
        }

        /// <summary>The point carried onto the surface, straight out from the centre.</summary>
        public Vector3 Onto(Vector3 p)
        {
            Vector3 d = p - centre;
            float u = Mathf.Sqrt((d.x * d.x + d.z * d.z) / (across * across) + d.y * d.y / (half * half));
            return u > 1e-6f ? centre + d / u : centre + Vector3.up * half;
        }
    }

    // Rows down a join's flare, from the tube's ring to the ball.
    const int JoinRows = 6;

    // One arm: a round tube along its centreline, its rings carried down it without twisting
    // (parallel transport). Where it leaves the hub and where it meets its end node it stops a join's
    // length short of the ball, and a flare carries each column of the tube on and out onto the
    // ball's surface. A join of 0, or an arm too short to hold both, runs on into the ball, capped.
    static void AppendArm(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                          List<Vector3> line, float radius, Ball hub, float hubJoin, Ball end, float endJoin)
    {
        int n = line.Count;
        if (n < 2) return;
        const int sides = ArmSides;

        var tangents  = new Vector3[n];
        var normals   = new Vector3[n];
        var binormals = new Vector3[n];
        var arc       = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 t = line[Mathf.Min(i + 1, n - 1)] - line[Mathf.Max(i - 1, 0)];
            tangents[i] = t.sqrMagnitude > 1e-12f ? t.normalized : Vector3.up;
        }

        Vector3 normal = Vector3.Cross(tangents[0], Vector3.up);
        if (normal.sqrMagnitude < 1e-6f) normal = Vector3.Cross(tangents[0], Vector3.right);
        normal.Normalize();
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                // Turn the frame by the least rotation taking the last tangent onto this one.
                normal = Quaternion.FromToRotation(tangents[i - 1], tangents[i]) * normal;
                normal = Vector3.ProjectOnPlane(normal, tangents[i]).normalized;
                arc[i] = arc[i - 1] + Vector3.Distance(line[i - 1], line[i]);
            }
            normals[i]   = normal;
            binormals[i] = Vector3.Cross(tangents[i], normal);
        }

        Vector3 Dir(int i, int s)
        {
            float a = s * Mathf.PI * 2f / sides;
            return normals[i] * Mathf.Cos(a) + binormals[i] * Mathf.Sin(a);
        }

        // Where each column of the tube comes out of a ball: walking out from the start for the hub,
        // back from the end for the end node. `reach` is the arc length the whole ring is clear by.
        bool FindExits(Ball ball, bool fromStart, out Vector3[] exits, out Vector3[] exitDirs, out float reach)
        {
            exits    = new Vector3[sides];
            exitDirs = new Vector3[sides];
            reach    = fromStart ? 0f : float.MaxValue;

            for (int s = 0; s < sides; s++)
            {
                int  step  = fromStart ? 1 : -1;
                int  i     = fromStart ? 0 : n - 1;
                int  prev  = -1;
                bool found = false;
                for (; i >= 0 && i < n; prev = i, i += step)
                {
                    if (ball.Inside(line[i] + Dir(i, s) * radius) <= 0f) continue;
                    found = true;
                    break;
                }
                if (!found) return false;

                float   at;
                Vector3 point, dir;
                if (prev < 0)
                {
                    // Already outside where the line starts.
                    dir = Dir(i, s); point = line[i] + dir * radius; at = arc[i];
                }
                else
                {
                    // Between the last sample inside and the first outside.
                    float lo = 0f, hi = 1f;
                    for (int k = 0; k < 24; k++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        Vector3 d = Vector3.Lerp(Dir(prev, s), Dir(i, s), mid).normalized;
                        if (ball.Inside(Vector3.Lerp(line[prev], line[i], mid) + d * radius) <= 0f) lo = mid; else hi = mid;
                    }
                    dir   = Vector3.Lerp(Dir(prev, s), Dir(i, s), hi).normalized;
                    point = Vector3.Lerp(line[prev], line[i], hi) + dir * radius;
                    at    = Mathf.Lerp(arc[prev], arc[i], hi);
                }

                exits[s]    = point;
                exitDirs[s] = dir;
                reach = fromStart ? Mathf.Max(reach, at) : Mathf.Min(reach, at);
            }
            return true;
        }

        int first = 0, last = n - 1;
        Vector3[] hubExits = null, hubDirs = null, endExits = null, endDirs = null;

        if (hubJoin > 0f && FindExits(hub, true, out hubExits, out hubDirs, out float hubReach))
        {
            first = System.Array.FindIndex(arc, a => a >= hubReach + hubJoin);
            if (first < 0) { first = 0; hubExits = null; }
        }
        else hubExits = null;

        if (endJoin > 0f && FindExits(end, false, out endExits, out endDirs, out float endReach))
        {
            last = System.Array.FindLastIndex(arc, a => a <= endReach - endJoin);
            if (last < 0) { last = n - 1; endExits = null; }
        }
        else endExits = null;

        // No room between the two flares: the tube runs the whole way, into both balls.
        if (first >= last)
        {
            first = 0; last = n - 1;
            hubExits = endExits = null;
        }

        // ── The tube ──
        int cols  = sides + 1;
        int start = verts.Count;
        for (int i = first; i <= last; i++)
            for (int s = 0; s <= sides; s++)
            {
                Vector3 dir = Dir(i, s);
                verts.Add(line[i] + dir * radius);
                norms.Add(dir);
                uvs.Add(new Vector2(s * Mathf.PI * 2f / sides * radius, arc[i]));
            }
        for (int i = 0; i < last - first; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + i * cols + s, b = a + 1, c = a + cols, d = c + 1;
                LollipopTower.AddTri(tris, verts, norms, a, c, d);
                LollipopTower.AddTri(tris, verts, norms, a, d, b);
            }

        // ── The joins, or caps buried in the balls ──
        if (hubExits != null)
            AppendJoin(verts, norms, uvs, tris, start, sides, -tangents[first], hub, hubExits, hubDirs, hubJoin);
        else
            AppendCap(verts, norms, uvs, tris, line[first], -tangents[first], start, sides);

        int lastRing = start + (last - first) * cols;
        if (endExits != null)
            AppendJoin(verts, norms, uvs, tris, lastRing, sides, tangents[last], end, endExits, endDirs, endJoin);
        else
            AppendCap(verts, norms, uvs, tris, line[last], tangents[last], lastRing, sides);
    }

    // A flare from one of the tube's rings out onto a ball. Each column is a curve leaving the ring
    // along the tube (`along` points from the ring towards the ball) and arriving on the ball's
    // surface a join's length beyond where that column came out of it, lying along the surface. Its
    // last row takes the ball's own normals, its first the tube's, so neither edge shows.
    static void AppendJoin(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                           int ringStart, int sides, Vector3 along, Ball ball,
                           Vector3[] exits, Vector3[] exitDirs, float join)
    {
        int rows = JoinRows + 1, cols = sides + 1;
        var grid  = new Vector3[rows, cols];
        var first = new Vector3[cols];
        var final = new Vector3[cols];

        for (int s = 0; s < cols; s++)
        {
            int c = s % sides;
            Vector3 q  = verts[ringStart + s];
            Vector3 e  = exits[c];
            Vector3 ne = ball.Normal(e);

            // Away from the arm, along the ball's surface.
            Vector3 away = Vector3.ProjectOnPlane(exitDirs[c], ne);
            if (away.sqrMagnitude < 1e-8f) away = Vector3.Cross(ne, along);
            away.Normalize();

            Vector3 j  = ball.Onto(e + away * join);
            Vector3 nj = ball.Normal(j);
            Vector3 tj = Vector3.ProjectOnPlane(away, nj).normalized;

            // Handles aimed at the corner being rounded off — where the tube's side, carried on,
            // passes nearest the ball's tangent at j — so the flare bends one way only. Lines too
            // near parallel to have a corner take even handles instead.
            float   span = Vector3.Distance(q, j);
            float   kq = span * 0.45f, kj = span * 0.45f;
            float   bdot = Vector3.Dot(along, -tj), denom = 1f - bdot * bdot;
            if (denom > 1e-4f)
            {
                Vector3 r  = q - j;
                float   dd = Vector3.Dot(along, r), ee = Vector3.Dot(-tj, r);
                float   u  = (bdot * ee - dd) / denom;   // along the tube from q
                float   w  = (ee - bdot * dd) / denom;   // back along the surface from j
                if (u > 0.05f * span && w > 0.05f * span && u < 3f * span && w < 3f * span)
                {
                    kq = u * 0.55f;
                    kj = w * 0.55f;
                }
            }
            Vector3 p1 = q + along * kq, p2 = j - tj * kj;
            for (int v = 0; v < rows; v++)
            {
                float t = v / (float)JoinRows, m = 1f - t;
                grid[v, s] = m * m * m * q + 3f * m * m * t * p1 + 3f * m * t * t * p2 + t * t * t * j;
            }
            first[s] = norms[ringStart + s];
            final[s] = nj;
        }

        int start = verts.Count;
        for (int v = 0; v < rows; v++)
            for (int s = 0; s < cols; s++)
            {
                Vector3 n;
                if (v == 0)             n = first[s];
                else if (v == rows - 1) n = final[s];
                else
                {
                    // The surface's own normal, from its neighbours, turned the way the ends face.
                    int     sl    = s == 0 ? sides - 1 : s - 1;
                    int     sr    = s == sides ? 1 : s + 1;
                    Vector3 guide = Vector3.Lerp(first[s], final[s], v / (float)JoinRows).normalized;
                    n = Vector3.Cross(grid[v, sr] - grid[v, sl], grid[v + 1, s] - grid[v - 1, s]).normalized;
                    if (n.sqrMagnitude < 0.5f) n = guide;
                    else if (Vector3.Dot(n, guide) < 0f) n = -n;
                }
                verts.Add(grid[v, s]);
                norms.Add(n);
                uvs.Add(new Vector2(s, v));
            }

        for (int v = 0; v < JoinRows; v++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + v * cols + s, b = a + 1, c = a + cols, d = c + 1;
                LollipopTower.AddTri(tris, verts, norms, a, c, d);
                LollipopTower.AddTri(tris, verts, norms, a, d, b);
            }
    }

    // A flat disc closing a tube's end ring, facing out along `facing`.
    static void AppendCap(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                          Vector3 centre, Vector3 facing, int ringStart, int sides)
    {
        int c = verts.Count;
        verts.Add(centre); norms.Add(facing); uvs.Add(Vector2.zero);
        int first = verts.Count;
        for (int s = 0; s <= sides; s++)
        {
            Vector3 p = verts[ringStart + s];
            verts.Add(p); norms.Add(facing); uvs.Add(new Vector2(p.x - centre.x, p.z - centre.z));
        }
        for (int s = 0; s < sides; s++)
            LollipopTower.AddTri(tris, verts, norms, c, first + s, first + s + 1);
    }

    // The bell and the stem as one outline, revolved. From a skirt carrying the foot down into the
    // ground (so it never stands proud of a slope), the bell rises straight off the ground — the
    // edge reads as a rounded lip — and turns in, the shoulder setting how high the foot rises first,
    // arriving upright at the stem's radius so it runs on into the stem with nothing between. The
    // stem goes up and flares out onto the hub's underside (Stem Join), its last ring on the hub's
    // surface with the hub's normals.
    void AppendTrunk(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris, Ball hub)
    {
        // The flare into the stem — fitted to the drawn TreeBase shape.
        const float NeckFlare = 0.76f;

        float footR = Mathf.Max(0.001f, baseFootRadius);
        float bh    = Mathf.Max(0f, baseHeight);
        float stemR = Mathf.Max(0.001f, stemRadius);

        // Where the bell has finished narrowing: Base Height, or a Base Join further up the stem —
        // never past the bottom of the hub.
        float neckTop = bh;
        if (bh > 0f && baseJoin > 0f)
            neckTop = Mathf.Max(bh, Mathf.Min(bh + baseJoin, hub.centre.y - hub.half));

        var profile = new List<Vector2>();   // (radius, height)
        var slopes  = new List<Vector2>();   // outward normal in (radius, height)

        if (bh > 0f)
        {
            float shoulder = Mathf.Clamp01(baseShoulder);
            profile.Add(new Vector2(footR, -footR * 0.5f)); slopes.Add(new Vector2(1f, 0f));

            Vector2 p0 = new Vector2(footR, 0f), p1 = new Vector2(footR, shoulder * bh);
            Vector2 p2 = new Vector2(stemR, NeckFlare * bh), p3 = new Vector2(stemR, neckTop);
            int rings = neckTop > bh ? BellRings + 4 : BellRings;   // a longer neck gets a few more rings
            for (int i = 0; i <= rings; i++)
            {
                float t = i / (float)rings, m = 1f - t;
                Vector2 pt = m * m * m * p0 + 3 * m * m * t * p1 + 3 * m * t * t * p2 + t * t * t * p3;
                Vector2 d  = 3 * m * m * (p1 - p0) + 6 * m * t * (p2 - p1) + 3 * t * t * (p3 - p2);
                if (d.sqrMagnitude < 1e-12f) d = Vector2.up;
                profile.Add(pt);
                slopes.Add(new Vector2(d.y, -d.x).normalized);
            }
        }
        else
        {
            // No bell: the stem goes a little way into the ground instead.
            profile.Add(new Vector2(stemR, -stemR)); slopes.Add(new Vector2(1f, 0f));
            profile.Add(new Vector2(stemR, 0f));     slopes.Add(new Vector2(1f, 0f));
        }

        // Where the flare lands on the hub: a Stem Join along the hub's surface, out from where the
        // stem would meet it. θ is the angle up from the hub's bottom.
        float a = hub.across, b = hub.half, hubY = hub.centre.y;
        const float MaxTheta = 85f * Mathf.Deg2Rad;
        float theta  = Mathf.Asin(Mathf.Clamp01(stemR / a));
        bool  join   = stemJoin > 0f && stemR < a * 0.98f && theta < MaxTheta;
        if (join)
        {
            const int Steps = 96;
            float dTheta = (MaxTheta - theta) / Steps, walked = 0f;
            for (int i = 0; i < Steps && walked < stemJoin; i++)
            {
                walked += Mathf.Sqrt(Mathf.Pow(a * Mathf.Cos(theta), 2f) + Mathf.Pow(b * Mathf.Sin(theta), 2f)) * dTheta;
                theta  += dTheta;
            }
        }

        Vector2 j  = new Vector2(a * Mathf.Sin(theta), hubY - b * Mathf.Cos(theta));
        // Along the hub's surface at j, heading out towards its widest, and its normal there.
        Vector2 tj = new Vector2(a * Mathf.Cos(theta), b * Mathf.Sin(theta)).normalized;
        Vector2 nj = new Vector2(Mathf.Sin(theta) / a, -Mathf.Cos(theta) / b).normalized;

        // The corner the flare rounds off — where the stem's side meets the hub's tangent at j —
        // and the stem the same distance below it, so the curve is even both ways and bends one
        // way only.
        Vector2 corner = j + tj * ((stemR - j.x) / Mathf.Max(1e-5f, tj.x));
        Vector2 flareStart = new Vector2(stemR, Mathf.Max(neckTop, corner.y - Vector2.Distance(corner, j)));
        join &= j.y > flareStart.y + 1e-5f && corner.y >= flareStart.y - 1e-5f;

        if (join)
        {
            if (flareStart.y > profile[profile.Count - 1].y + 1e-6f) { profile.Add(flareStart); slopes.Add(new Vector2(1f, 0f)); }

            Vector2 c1 = flareStart + (corner - flareStart) * 0.55f, c2 = j + (corner - j) * 0.55f;
            for (int i = 1; i <= JoinRows; i++)
            {
                float t = i / (float)JoinRows, m = 1f - t;
                Vector2 pt = m * m * m * flareStart + 3 * m * m * t * c1 + 3 * m * t * t * c2 + t * t * t * j;
                Vector2 d  = 3 * m * m * (c1 - flareStart) + 6 * m * t * (c2 - c1) + 3 * t * t * (j - c2);
                profile.Add(pt);
                slopes.Add(i == JoinRows || d.sqrMagnitude < 1e-12f ? nj : new Vector2(d.y, -d.x).normalized);
            }
        }
        else
        {
            // No flare: the stem runs on into the hub's middle, so it never shows a gap under it.
            profile.Add(new Vector2(stemR, hubY)); slopes.Add(new Vector2(1f, 0f));
        }

        int   start = verts.Count;
        float along = 0f;
        for (int i = 0; i < profile.Count; i++)
        {
            if (i > 0) along += Vector2.Distance(profile[i - 1], profile[i]);
            for (int s = 0; s <= Sides; s++)
            {
                float   ang = s * Mathf.PI * 2f / Sides;
                Vector3 dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                verts.Add(dir * profile[i].x + Vector3.up * profile[i].y);
                norms.Add((dir * slopes[i].x + Vector3.up * slopes[i].y).normalized);
                uvs.Add(new Vector2(ang * profile[i].x, along));
            }
        }

        int cols = Sides + 1;
        for (int i = 0; i < profile.Count - 1; i++)
            for (int s = 0; s < Sides; s++)
            {
                int p = start + i * cols + s, q = p + 1, r = p + cols, t = r + 1;
                LollipopTower.AddTri(tris, verts, norms, p, r, t);
                LollipopTower.AddTri(tris, verts, norms, p, t, q);
            }
    }

    // A ball squashed upright: `across` its radius level, `half` its half height.
    static void AppendEllipsoid(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                                Vector3 centre, float across, float half)
    {
        int start = verts.Count;
        for (int ring = 0; ring <= HubRings; ring++)
        {
            float lat = Mathf.PI * ring / HubRings;   // 0 at the top
            for (int i = 0; i <= Sides; i++)
            {
                float   lon = i * Mathf.PI * 2f / Sides;
                Vector3 u   = new Vector3(Mathf.Sin(lat) * Mathf.Sin(lon), Mathf.Cos(lat),
                                          Mathf.Sin(lat) * Mathf.Cos(lon));
                verts.Add(centre + new Vector3(u.x * across, u.y * half, u.z * across));
                norms.Add(new Vector3(u.x / across, u.y / half, u.z / across).normalized);
                uvs.Add(new Vector2(lon * across, (Mathf.PI - lat) * half));
            }
        }
        int cols = Sides + 1;
        for (int ring = 0; ring < HubRings; ring++)
            for (int i = 0; i < Sides; i++)
            {
                int a = start + ring * cols + i, b = a + 1, c = a + cols, d = c + 1;
                if (ring > 0)            LollipopTower.AddTri(tris, verts, norms, a, b, d);
                if (ring < HubRings - 1) LollipopTower.AddTri(tris, verts, norms, a, d, c);
            }
    }
}
