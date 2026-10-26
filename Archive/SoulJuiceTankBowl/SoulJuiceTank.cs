using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A soul juice tank: a small pool held up on a column, where a soul route's souls begin. Stands
/// in the middle of a pool's island, and is the only thing a soul route may start from.
///
///      _______________________     <- rim top (bowl radius, rim width); water inside
///     |  \_______________/    |    <- band (straight outer wall), basin (basin depth) inside
///      \_                   _/     <- curved underside (underside depth), closing onto the column
///        ‾‾‾‾‾‾‾|     |‾‾‾‾‾‾
///               |     |            <- column (column radius, column height)
///              _|_____|_           <- second base (base 2 radius, base 2 height), 0 height = none
///             /_________\          <- ramp (base radius in to base 2 radius), 0 height = none
///            |___________|         <- base (base radius, base height), standing at y = 0
///
/// Built as two pieces: the body (base tiers and column) and the bowl, each carrying the river
/// run stone shading. The bowl is shaded off its OWN rim, so the waterline gradient sits at the
/// water inside it rather than down at the river. The water is a third piece, laid by the
/// designer with the same builder a pool's water uses.
/// </summary>
[Serializable]
public class SoulJuiceTank
{
    [Header("Base")]
    [Tooltip("Radius of the round base the column stands on.")]
    [Min(0.001f)] public float baseRadius = 0.16f;

    [Tooltip("Height of the round base. 0 = no base, and the column stands on the ground.")]
    [Min(0f)] public float baseHeight = 0.08f;

    [Tooltip("Height of the ramp on top of the base, sloping in from the base's radius to the " +
             "second base's radius. 0 = no ramp.")]
    [Min(0f)] public float rampHeight = 0f;

    [Tooltip("Radius of the second base, on top of the ramp — and the radius the ramp slopes in to.")]
    [Min(0.001f)] public float base2Radius = 0.12f;

    [Tooltip("Height of the second base, on top of the ramp. 0 = no second base.")]
    [Min(0f)] public float base2Height = 0f;

    [Header("Column")]
    [Tooltip("Radius of the column holding the bowl up.")]
    [Min(0.001f)] public float columnRadius = 0.1f;

    [Tooltip("Height of the column, from the top of the base tiers up to where the bowl's " +
             "underside closes onto it.")]
    [Min(0f)] public float columnHeight = 0.8f;

    [Header("Bowl")]
    [Tooltip("Radius of the bowl, out to its outer wall.")]
    [Min(0.01f)] public float bowlRadius = 0.6f;

    [Tooltip("Width of the flat rim round the top of the bowl.")]
    [Min(0.005f)] public float rimWidth = 0.08f;

    [Tooltip("Height of the straight outer wall below the rim, before the underside starts to " +
             "curve in. 0 = the curve starts right at the rim.")]
    [Min(0f)] public float bandHeight = 0.06f;

    [Tooltip("How far the curved underside drops, from the bottom of the band down to where it " +
             "closes onto the column.")]
    [Min(0.001f)] public float undersideDepth = 0.25f;

    [Tooltip("How deep the basin inside the rim is at its middle. Kept above the underside, so " +
             "it never breaks through the bottom.")]
    [Min(0.001f)] public float basinDepth = 0.18f;

    [Tooltip("How far below the rim the water in the bowl sits. Must be less than the basin depth " +
             "for there to be any water.")]
    [Min(0f)] public float waterDrop = 0.05f;

    [Tooltip("Radius of the hole in the middle of the basin. Only seen once the water has drained " +
             "down past it. 0 = no hole.")]
    [Min(0f)] public float holeRadius = 0f;

    // The thinnest the bowl may be at its middle, between the basin and the underside.
    const float MinFloor = 0.01f;

    // Round the column and the bowl. The base tiers are the lollipop's own and keep its count;
    // nothing is shared across that join, so the two need not agree.
    const int BowlSides      = 48;
    const int UndersideRings = 10;
    const int BasinRings     = 8;

    // The basin is swept down to this angle and the grid fills the rest — so nothing meets at
    // one point in the middle.
    const float BasinSweep = 60f * Mathf.Deg2Rad;

    // ── Heights, measured up from the tower's base ──

    public float TiersHeight => Mathf.Max(0f, baseHeight) + Mathf.Max(0f, rampHeight) + Mathf.Max(0f, base2Height);

    /// <summary>Where the column meets the bowl's underside.</summary>
    public float ColumnTopY => TiersHeight + Mathf.Max(0f, columnHeight);

    /// <summary>The top of the bowl's rim — the top of the whole tower.</summary>
    public float RimTopY => ColumnTopY + Mathf.Max(0.001f, undersideDepth) + Mathf.Max(0f, bandHeight);

    public float TopY => RimTopY;

    /// <summary>Radius of the basin — the inside edge of the rim.</summary>
    public float InnerRadius => Mathf.Max(0.005f, Mathf.Max(0.01f, bowlRadius) - Mathf.Max(0.005f, rimWidth));

    /// <summary>The basin depth as built: never deep enough to break through the underside.</summary>
    public float BasinDepth
        => Mathf.Clamp(basinDepth, 0.001f, Mathf.Max(0.001f, RimTopY - ColumnTopY - MinFloor));

    /// <summary>The water's surface, measured up from the tower's base.</summary>
    public float WaterY => RimTopY - Mathf.Max(0f, waterDrop);

    /// <summary>
    /// Radius of the water's edge in the basin — where the basin's half-ellipse comes up through
    /// the surface. The same answer a pool's water gives for its own bowl, so the fish swim
    /// inside what is really wet. 0 when the water is below the basin's floor.
    /// </summary>
    public float WaterRadius
    {
        get
        {
            float depth = BasinDepth;
            float sunk  = Mathf.Clamp01(Mathf.Max(0f, waterDrop) / depth);
            return InnerRadius * Mathf.Sqrt(Mathf.Max(0f, 1f - sunk * sunk));
        }
    }

    /// <summary>The hole as built: never wider than most of the basin. 0 = none.</summary>
    public float HoleRadius => Mathf.Clamp(holeRadius, 0f, InnerRadius * 0.9f);

    /// <summary>
    /// How far below the rim the water is when the bowl has drained — down to the hole's edge, or
    /// the basin's floor when there is no hole.
    /// </summary>
    public float EmptyDrop
        => HoleRadius > 0f ? BasinDepth * Mathf.Sin(HoleSweep) : BasinDepth;

    // How far round the basin's curve the hole's edge sits.
    float HoleSweep => Mathf.Acos(Mathf.Clamp01(HoleRadius / InnerRadius));

    /// <summary>Height of the bowl's underside at <paramref name="r"/> from the middle, up from the base.</summary>
    float UndersideY(float r)
    {
        float R  = Mathf.Max(0.01f, bowlRadius);
        float cr = Mathf.Min(Mathf.Max(0.001f, columnRadius), R * 0.98f);
        if (r <= cr) return ColumnTopY;
        float sin = Mathf.Clamp01((r - cr) / (R - cr));
        return ColumnTopY + Mathf.Max(0.001f, undersideDepth) * (1f - Mathf.Sqrt(1f - sin * sin));
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

        copy.baseRadius     *= s;
        copy.baseHeight     *= s;
        copy.rampHeight     *= s;
        copy.base2Radius    *= s;
        copy.base2Height    *= s;
        copy.columnRadius   *= s;
        copy.columnHeight   *= s;
        copy.bowlRadius     *= s;
        copy.rimWidth       *= s;
        copy.bandHeight     *= s;
        copy.undersideDepth *= s;
        copy.basinDepth     *= s;
        copy.waterDrop      *= s;
        copy.holeRadius     *= s;
        return copy;
    }

    // ─────────────────────────────────────────────
    // BODY — base tiers and column
    // ─────────────────────────────────────────────

    /// <summary>
    /// The base tiers and the column, base at the origin, in the river run stone shading. The
    /// tiers are a lollipop tower's own, so they take the same part seams and stone colours.
    /// </summary>
    public Mesh BuildBody(RiverRunShadingSettings shading = null)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();
        var kinds = new List<float>();
        var parts = new List<int>();

        // The tiers alone: no stem, no orb.
        var tiers = new LollipopTower
        {
            baseRadius  = baseRadius,  baseHeight  = baseHeight,
            rampHeight  = rampHeight,
            base2Radius = base2Radius, base2Height = base2Height,
            stemRadius  = columnRadius, stemHeight = 0f,
        };
        // The tiers take the Soul Juice Tanks choices, not the lollipop towers'.
        tiers.Append(verts, norms, uvs, tris, Vector3.zero, kinds,
                     RiverRunShadingSettings.ForPoolTower(shading), parts, withOrb: false,
                     upperRampLabel: "Ramp");

        // The column, round at the bowl's count so its top ring lies exactly on the underside's
        // first. Open at both ends: the bottom stands on the tier below, the top is inside the bowl.
        float r0 = Mathf.Max(0.001f, columnRadius);
        float y0 = TiersHeight;
        float y1 = ColumnTopY;
        if (y1 > y0)
        {
            for (int i = 0; i < BowlSides; i++)
            {
                Vector3 d0 = Dir(i), d1 = Dir(i + 1);
                Quad(verts, norms, uvs, tris,
                     d0 * r0 + Vector3.up * y0, d1 * r0 + Vector3.up * y0,
                     d1 * r0 + Vector3.up * y1, d0 * r0 + Vector3.up * y1,
                     (d0 + d1).normalized);
            }
            StoneFaces.Tag(kinds, tris, shading != null ? shading.poolTowerColumn : StoneFaceKind.Auto);
            StoneFaces.Part(parts, tris, 0);
            PartLabels.Mark("Column", verts, tris);
        }

        var mesh = ToMesh("SoulJuiceTankBody", verts, norms, uvs, tris);
        return RiverMeshBuilder.ShadeAsStone(mesh, 0f, 0f, kinds, parts,
                                             designation: StoneDesignation.SoulJuiceTanks);
    }

    // ─────────────────────────────────────────────
    // BOWL
    // ─────────────────────────────────────────────

    /// <summary>
    /// The bowl, in the tower's own space (base at the origin): the curved underside out from the
    /// column, the band, the flat rim and the basin, one profile turned round the column. Shaded
    /// as stone off its own rim, so the rim reads as rim, the basin as the inside and the rest as
    /// the outside, and the waterline gradient lands on the water in the basin.
    ///
    /// <paramref name="shadingWaterDepth"/> is the depth below a rim the stone shader draws its
    /// waterline at — the world's water drop (0 when the level isn't water filled), one number for
    /// every piece. The bowl's water sits
    /// its own drop below its rim instead, so the rim the shading is read off is moved to put
    /// that waterline on the bowl's water rather than down its side.
    /// </summary>
    public Mesh BuildBowl(float shadingWaterDepth, RiverRunShadingSettings shading = null)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();
        var kinds = new List<float>();
        StoneFaceKind Choice(System.Func<RiverRunShadingSettings, StoneFaceKind> pick) =>
            shading != null ? pick(shading) : StoneFaceKind.Auto;

        float R  = Mathf.Max(0.01f, bowlRadius);
        float cr = Mathf.Min(Mathf.Max(0.001f, columnRadius), R * 0.98f);
        float C  = ColumnTopY;
        float ud = Mathf.Max(0.001f, undersideDepth);
        float T  = RimTopY;
        float Ri = InnerRadius;
        float bd = BasinDepth;

        // The profile in (radius, height), walked from the column outwards and over the top into
        // the basin. Walked this way the solid is always on the same side, so each stretch's
        // outward normal is (dy, -dr) — down under the bowl, out on the band, up on the rim, and
        // up and in on the basin.
        var profile = new List<Vector2>();

        for (int k = 0; k <= UndersideRings; k++)
        {
            float a = 0.5f * Mathf.PI * k / UndersideRings;
            profile.Add(new Vector2(cr + (R - cr) * Mathf.Sin(a), C + ud * (1f - Mathf.Cos(a))));
        }
        if (bandHeight > 0f) profile.Add(new Vector2(R, T));
        profile.Add(new Vector2(Ri, T));
        int rimEnd = profile.Count - 1;   // the stretch ending here is the rim; before it the outside

        // With a hole the basin is swept on down to the hole's edge, then a straight wall drops
        // to a floor just above the underside.
        float hole  = HoleRadius;
        float sweep = hole > 0f ? HoleSweep : BasinSweep;
        for (int k = 1; k <= BasinRings; k++)
        {
            float a = sweep * k / BasinRings;
            profile.Add(new Vector2(Ri * Mathf.Cos(a), T - bd * Mathf.Sin(a)));
        }

        float floorY = 0f;
        if (hole > 0f)
        {
            float edgeY = T - bd * Mathf.Sin(sweep);
            floorY = Mathf.Min(UndersideY(hole) + MinFloor, edgeY - 0.001f);
            profile.Add(new Vector2(hole, floorY));
        }

        for (int s = 0; s + 1 < profile.Count; s++)
        {
            Vector2 p0 = profile[s], p1 = profile[s + 1];
            Vector2 n2 = new Vector2(p1.y - p0.y, -(p1.x - p0.x));

            for (int i = 0; i < BowlSides; i++)
            {
                Vector3 d0 = Dir(i), d1 = Dir(i + 1);
                Vector3 mid = (d0 + d1).normalized;
                Quad(verts, norms, uvs, tris,
                     d0 * p0.x + Vector3.up * p0.y, d1 * p0.x + Vector3.up * p0.y,
                     d1 * p1.x + Vector3.up * p1.y, d0 * p1.x + Vector3.up * p1.y,
                     mid * n2.x + Vector3.up * n2.y);
            }

            StoneFaces.Tag(kinds, tris, s + 1 < rimEnd  ? Choice(st => st.poolTowerBowlOutside)
                                      : s + 1 == rimEnd ? Choice(st => st.poolTowerBowlRim)
                                                        : Choice(st => st.poolTowerBowlInside));
            PartLabels.Mark(s < UndersideRings   ? "Underside"
                          : s + 1 < rimEnd       ? "Band"
                          : s + 1 == rimEnd      ? "Rim"
                          : hole > 0f && s + 2 == profile.Count ? "Hole" : "Basin", verts, tris);
        }

        // The middle of the basin: a grid inside its last ring, the inside dropped onto the
        // basin's own curve — the way a pool fills the middle of its floor. With a hole, the
        // hole's flat floor instead.
        Vector2 last = profile[profile.Count - 1];
        var ring = new Vector3[BowlSides];
        for (int i = 0; i < BowlSides; i++) ring[i] = Dir(i) * last.x + Vector3.up * last.y;

        var grid = RiverMeshBuilder.FillOutline(ring);
        if (grid == null)
        {
            for (int j = 1; j + 1 < ring.Length; j++)
                Tri(verts, norms, uvs, tris, ring[0], ring[j], ring[j + 1], Vector3.up);
        }
        else
        {
            int p = grid.Length - 1, q = grid[0].Length - 1;
            for (int i = 1; i < p; i++)
                for (int j = 1; j < q; j++)
                {
                    Vector3 v = grid[i][j];
                    float   r = Mathf.Min(new Vector2(v.x, v.z).magnitude, Ri);
                    float   y = hole > 0f ? floorY   // the hole's floor is flat
                                          : T - bd * Mathf.Sqrt(Mathf.Max(0f, 1f - (r / Ri) * (r / Ri)));
                    grid[i][j] = new Vector3(v.x, y, v.z);
                }

            for (int i = 0; i < p; i++)
                for (int j = 0; j < q; j++)
                    Quad(verts, norms, uvs, tris,
                         grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1], Vector3.up);
        }
        StoneFaces.Tag(kinds, tris, Choice(st => st.poolTowerBowlInside));
        PartLabels.Mark(hole > 0f ? "Hole" : "Basin", verts, tris);

        var mesh = ToMesh("SoulJuiceTankBowl", verts, norms, uvs, tris);
        return RiverMeshBuilder.ShadeAsStone(mesh, WaterY + Mathf.Max(0f, shadingWaterDepth), T, kinds,
                                             designation: StoneDesignation.SoulJuiceTanks);
    }

    // ─────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────

    static Vector3 Dir(int i)
    {
        float a = (i % BowlSides) * Mathf.PI * 2f / BowlSides;
        return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
    }

    // A quad a-b-c-d, wound to face along `facing`. The stone pass replaces normals and UVs, so
    // these only have to be sensible enough to wind by.
    static void Quad(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                     Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
    {
        Tri(verts, norms, uvs, tris, a, b, c, facing);
        Tri(verts, norms, uvs, tris, a, c, d, facing);
    }

    static void Tri(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                    Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
    {
        // A sliver with no area — a grid corner folded flat — is left out rather than wound at random.
        if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-16f) return;

        int s = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        norms.Add(facing); norms.Add(facing); norms.Add(facing);
        uvs.Add(Vector2.zero); uvs.Add(Vector2.zero); uvs.Add(Vector2.zero);
        LollipopTower.AddTri(tris, verts, norms, s, s + 1, s + 2);
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
