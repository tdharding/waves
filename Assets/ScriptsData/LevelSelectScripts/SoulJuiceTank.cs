using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// A soul juice tank: a lollipop tower with a hub on top of its orb, where an intake pipe comes up
/// to a hub on a nearby pipe, and a hub on the orb's side, where a tap comes out and turns down.
/// Stands in the middle of a pool's island, and is the only thing a soul route may start from.
///
///               ||               <- intake (intake thickness), up to the pipe's hub ring
///              _||_              <- top hub (top hub radius, top hub thickness)
///             /    \ _
///            | orb  |_|__        <- side hub (around, radius, thickness), tap reaching out
///             \____/     |       <- and dropping down (tap thickness, reach out, drop)
///               |
///               |                <- stem (stem radius, stem height)
///             _|_|_              <- second base, upper ramp, base, lower ramp — a lollipop's own
///
/// Until 2026-10-26 the tank was a bowl of soul juice on a column; that is now the Soul Fish Bowl
/// (<see cref="SoulFishBowl"/>), and the old column fields carry over onto the stem.
///
/// Built as two pieces: the body (the lollipop, both hubs and the tap) and the intake, which is
/// laid separately because it reaches out to a pipe and is rebuilt whenever that pipe is.
/// </summary>
[Serializable]
public class SoulJuiceTank
{
    [Header("Lollipop")]
    [Tooltip("Radius of the cylinder stem.")]
    [FormerlySerializedAs("columnRadius")]
    [Min(0.001f)] public float stemRadius = 0.1f;

    [Tooltip("Height of the stem, from the top of the base tiers up to the orb.")]
    [FormerlySerializedAs("columnHeight")]
    [Min(0f)] public float stemHeight = 0.8f;

    [Tooltip("Radius of the orb on top of the stem.")]
    [Min(0.001f)] public float orbRadius = 0.35f;

    [Tooltip("Radius of the round base the stem stands on.")]
    [Min(0.001f)] public float baseRadius = 0.16f;

    [Tooltip("Height of the round base. 0 = no base.")]
    [Min(0f)] public float baseHeight = 0.08f;

    [Tooltip("Height of the lower ramp flaring out under the base. 0 = none.")]
    [Min(0f)] public float lowerRampHeight = 0f;

    [Tooltip("Radius the lower ramp flares out to at its foot.")]
    [Min(0.001f)] public float lowerRampRadius = 0.22f;

    [Tooltip("Height of the upper ramp on top of the base, sloping in to the second base's radius. 0 = none.")]
    [Min(0f)] public float rampHeight = 0f;

    [Tooltip("Radius of the second base, on top of the upper ramp — and the radius the ramp slopes in to.")]
    [Min(0.001f)] public float base2Radius = 0.12f;

    [Tooltip("Height of the second base. 0 = none.")]
    [Min(0f)] public float base2Height = 0f;

    [Header("Top Hub")]
    [Tooltip("Radius of the round hub on top of the orb.")]
    [Min(0.001f)] public float topHubRadius = 0.09f;

    [Tooltip("How far the top hub stands up past the orb.")]
    [Min(0f)] public float topHubThickness = 0.05f;

    [Header("Side Hub")]
    [Tooltip("Where the side hub sits round the orb, in degrees.")]
    public float sideHubAround = 0f;

    [Tooltip("Radius of the round hub on the side of the orb.")]
    [Min(0.001f)] public float sideHubRadius = 0.08f;

    [Tooltip("How far the side hub stands out past the orb.")]
    [Min(0f)] public float sideHubThickness = 0.05f;

    [Header("Tap")]
    [Tooltip("The tap's thickness, across the outside. Its bend is rounded to match it.")]
    [Min(0.002f)] public float tapThickness = 0.08f;

    [Tooltip("How far the tap reaches out past the side hub before it turns down.")]
    [Min(0f)] public float tapReach = 0.15f;

    [Tooltip("How far the tap drops after it turns down.")]
    [Min(0f)] public float tapDrop = 0.15f;

    [Header("Intake")]
    [Tooltip("The intake's thickness, across the outside. Its bend is rounded to match it.")]
    [Min(0.002f)] public float intakeThickness = 0.1f;

    // How much of the tap's thickness is its wall — the rest is the open mouth.
    const float TapWall = 0.25f;

    // ── Heights, measured up from the tower's base ──

    /// <summary>The lollipop these sizes stand for: no Destruction, nothing inside.</summary>
    public LollipopTower Lollipop() => new LollipopTower
    {
        stemRadius      = stemRadius,      stemHeight      = stemHeight,
        orbRadius       = orbRadius,
        baseRadius      = baseRadius,      baseHeight      = baseHeight,
        lowerRampHeight = lowerRampHeight, lowerRampRadius = lowerRampRadius,
        rampHeight      = rampHeight,
        base2Radius     = base2Radius,     base2Height     = base2Height,
        destruction     = false,           innerLollipop   = false,
    };

    /// <summary>The orb's centre, up from the base.</summary>
    public float OrbCentreY => Lollipop().OrbCentreY;

    /// <summary>The middle of the top hub's flat face, in the tower's own space — where the intake starts.</summary>
    public Vector3 TopHubFace => Vector3.up * (OrbCentreY + Mathf.Max(0.001f, orbRadius) + Mathf.Max(0f, topHubThickness));

    /// <summary>The very top: the top hub's face.</summary>
    public float TopY => TopHubFace.y;

    /// <summary>Which way the side hub faces, flat.</summary>
    public Vector3 SideDir
    {
        get
        {
            float a = sideHubAround * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }
    }

    public SoulJuiceTank Clone() => (SoulJuiceTank)MemberwiseClone();

    /// <summary>
    /// A copy with every size multiplied by <paramref name="scale"/> — the placement's own scale.
    /// 0 or below counts as 1.
    /// </summary>
    public SoulJuiceTank Scaled(float scale)
    {
        var copy = Clone();
        if (scale <= 0f || Mathf.Approximately(scale, 1f)) return copy;
        float s = scale;

        copy.stemRadius       *= s;
        copy.stemHeight       *= s;
        copy.orbRadius        *= s;
        copy.baseRadius       *= s;
        copy.baseHeight       *= s;
        copy.lowerRampHeight  *= s;
        copy.lowerRampRadius  *= s;
        copy.rampHeight       *= s;
        copy.base2Radius      *= s;
        copy.base2Height      *= s;
        copy.topHubRadius     *= s;
        copy.topHubThickness  *= s;
        copy.sideHubRadius    *= s;
        copy.sideHubThickness *= s;
        copy.tapThickness     *= s;
        copy.tapReach         *= s;
        copy.tapDrop          *= s;
        copy.intakeThickness  *= s;
        return copy;
    }

    // ─────────────────────────────────────────────
    // BODY — lollipop, hubs and tap
    // ─────────────────────────────────────────────

    /// <summary>
    /// The lollipop, the two hubs and the tap, base at the origin, in the river run stone shading.
    /// The lollipop takes the Soul Juice Tanks parts in the lollipop tower's places.
    /// </summary>
    public Mesh BuildBody(RiverRunShadingSettings shading = null)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();
        var kinds = new List<float>();
        var parts = new List<int>();
        StoneFaceKind Choice(Func<RiverRunShadingSettings, StoneFaceKind> pick) =>
            shading != null ? pick(shading) : StoneFaceKind.Auto;

        Lollipop().Append(verts, norms, uvs, tris, Vector3.zero, kinds,
                          RiverRunShadingSettings.ForPoolTower(shading), parts);

        float   R      = Mathf.Max(0.001f, orbRadius);
        Vector3 centre = Vector3.up * OrbCentreY;

        // Top hub, straight up out of the orb.
        AppendHub(verts, norms, tris, centre, Vector3.up, R, topHubRadius, topHubThickness,
                  kinds, Choice(s => s.poolTowerTopHub));
        Pad(uvs, verts);
        StoneFaces.Part(parts, tris, 5);
        PartLabels.Mark("Top Hub", verts, tris);

        // Side hub, out of the orb's side.
        Vector3 side = SideDir;
        AppendHub(verts, norms, tris, centre, side, R, sideHubRadius, sideHubThickness,
                  kinds, Choice(s => s.poolTowerSideHub));
        Pad(uvs, verts);
        StoneFaces.Part(parts, tris, 6);
        PartLabels.Mark("Side Hub", verts, tris);

        // Tap: from inside the side hub straight out, round one bend, then down to its open mouth.
        float   hubOut = R + Mathf.Max(0f, sideHubThickness);
        float   tapR   = Mathf.Max(0.001f, tapThickness * 0.5f);
        Vector3 start  = centre + side * HubFoot(R, sideHubRadius);
        Vector3 corner = centre + side * (hubOut + Mathf.Max(0f, tapReach));
        Vector3 mouth  = corner + Vector3.down * Mathf.Max(0f, tapDrop);
        var tapLine = PipeMesh.CentreLine(new List<Vector3> { start, corner, mouth }, tapR * 2f);
        if (tapLine.Count >= 2)
        {
            PipeMesh.AppendTube(verts, norms, tris, tapLine, tapR, tapR * (1f - TapWall),
                                kinds, Choice(s => s.poolTowerTap), Choice(s => s.poolTowerTap), PipeMesh.HubSides);
            Pad(uvs, verts);
            StoneFaces.Part(parts, tris, 7);
            PartLabels.Mark("Tap", verts, tris);
        }

        var mesh = ToMesh("SoulJuiceTankBody", verts, norms, uvs, tris);
        return RiverMeshBuilder.ShadeAsStone(mesh, 0f, 0f, kinds, parts,
                                             designation: StoneDesignation.SoulJuiceTanks);
    }

    // ─────────────────────────────────────────────
    // INTAKE
    // ─────────────────────────────────────────────

    /// <summary>
    /// The intake, in the tower's own space: straight up from inside the top hub, round one bend,
    /// and straight into a pipe hub's ring along the ring's own line. <paramref name="ringFace"/>
    /// is the middle of the ring's flat face and <paramref name="ringDir"/> the way the ring
    /// stands out, both in the tower's space. The bend sits where the ring's line passes over the
    /// top hub; nothing is held in place, so a hub not lined up with the tank leaves the intake
    /// leaning to reach it.
    /// </summary>
    public Mesh BuildIntake(Vector3 ringFace, Vector3 ringDir, float ringThickness,
                            RiverRunShadingSettings shading = null)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris  = new List<int>();
        var kinds = new List<float>();
        var parts = new List<int>();

        float   r   = Mathf.Max(0.001f, intakeThickness * 0.5f);
        Vector3 dir = ringDir.sqrMagnitude > 1e-8f ? ringDir.normalized : Vector3.down;

        // Both ends sink into what they meet, so neither shows a seam of light at the join.
        Vector3 from = TopHubFace + Vector3.down * Mathf.Max(0.002f, topHubThickness * 0.5f);
        Vector3 to   = ringFace   - dir * Mathf.Max(0.002f, ringThickness * 0.5f);

        // The bend: the point on the ring's line most nearly over the top hub, seen from above.
        Vector2 flat  = new Vector2(dir.x, dir.z);
        float   along = flat.sqrMagnitude > 1e-6f
            ? Vector2.Dot(new Vector2(from.x - ringFace.x, from.z - ringFace.z), flat) / flat.sqrMagnitude
            : 0f;
        Vector3 corner = ringFace + dir * along;

        var line = PipeMesh.CentreLine(new List<Vector3> { from, corner, to }, r * 2f);
        if (line.Count >= 2)
        {
            var kind = shading != null ? shading.poolTowerIntake : StoneFaceKind.Auto;
            PipeMesh.AppendTube(verts, norms, tris, line, r, 0f, kinds, kind, kind, PipeMesh.HubSides);
            StoneFaces.Part(parts, tris, 0);
            PartLabels.Mark("Intake", verts, tris);
        }

        var uvs  = new List<Vector2>();
        Pad(uvs, verts);
        var mesh = ToMesh("SoulJuiceTankIntake", verts, norms, uvs, tris);
        return RiverMeshBuilder.ShadeAsStone(mesh, 0f, 0f, kinds, parts,
                                             designation: StoneDesignation.SoulJuiceTanks);
    }

    // ─────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────

    // A round hub out of the orb along `dir`: from just under the orb's surface at the hub's edge,
    // so no gap shows round its foot, out to `thickness` past the orb, with a flat face.
    static void AppendHub(List<Vector3> verts, List<Vector3> norms, List<int> tris,
                          Vector3 centre, Vector3 dir, float orbR, float radius, float thickness,
                          List<float> kinds, StoneFaceKind kind)
    {
        float r = Mathf.Max(0.001f, radius);
        var line = new List<Vector3>
        {
            centre + dir * HubFoot(orbR, r),
            centre + dir * (orbR + Mathf.Max(0.0005f, thickness)),
        };
        PipeMesh.AppendTube(verts, norms, tris, line, r, 0f, kinds, kind, kind, PipeMesh.HubSides);
    }

    // How far out from the orb's centre a hub of this radius starts: under the orb's faceted
    // surface where the hub's edge meets it.
    static float HubFoot(float orbR, float radius)
        => Mathf.Sqrt(Mathf.Max(0f, orbR * orbR - radius * radius)) * 0.95f;

    // The pipe builder lays no UVs; the stone pass replaces them anyway, but the lists must match.
    static void Pad(List<Vector2> uvs, List<Vector3> verts)
    {
        while (uvs.Count < verts.Count) uvs.Add(Vector2.zero);
    }

    static Mesh ToMesh(string name, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris)
    {
        var mesh = new Mesh { name = name };
        if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
