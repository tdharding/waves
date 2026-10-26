using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A lollipop tower: a cylinder stem with an orb on top. Stands on installation outposts, and
/// optionally in the middle of a pool's island.
///
///        ___
///       /   \     <- orb (orb radius)
///       \___/
///         |
///         |       <- stem (stem radius, stem height)
///        _|_
///       |___|     <- second base (base 2 radius, base 2 height), 0 height = none
///      /_____\    <- upper ramp (base radius in to base 2 radius, ramp height), 0 height = none
///     |_______|   <- base (base radius, base height)
///    /_________\  <- lower ramp (lower ramp radius in to base radius, lower ramp height),
///                    standing at y = 0; 0 height = none
///
/// The orb is seated where the stem's top edge meets its surface, so the stem runs its full
/// height into the orb with no ledge and no gap.
/// </summary>
[Serializable]
public class LollipopTower
{
    [Tooltip("Radius of the cylinder stem.")]
    [Min(0.001f)] public float stemRadius = 0.02f;

    [Tooltip("Height of the cylinder stem, from its base up to where it meets the orb.")]
    [Min(0f)] public float stemHeight = 0.35f;

    [Tooltip("Radius of the orb on top.")]
    [Min(0.001f)] public float orbRadius = 0.1f;

    [Tooltip("Radius of the round base the stem stands on.")]
    [Min(0.001f)] public float baseRadius = 0.05f;

    [Tooltip("Height of the round base. 0 = no base, and the stem stands on the ground.")]
    [Min(0f)] public float baseHeight = 0.03f;

    [Tooltip("Height of the lower ramp under the base, flaring out from the base's radius to the " +
             "lower ramp radius on the ground. 0 = no lower ramp.")]
    [Min(0f)] public float lowerRampHeight = 0f;

    [Tooltip("Radius of the lower ramp's foot on the ground — the radius it flares out to from the base.")]
    [Min(0.001f)] public float lowerRampRadius = 0.08f;

    [Tooltip("Height of the upper ramp on top of the base, sloping in from the base's radius to the " +
             "second base's radius. 0 = no upper ramp.")]
    [Min(0f)] public float rampHeight = 0f;

    [Tooltip("Radius of the second base, on top of the upper ramp — and the radius the upper ramp slopes in to.")]
    [Min(0.001f)] public float base2Radius = 0.03f;

    [Tooltip("Height of the second base, on top of the ramp. 0 = no second base.")]
    [Min(0f)] public float base2Height = 0f;

    [Tooltip("Breaks a hole into the orb. The orb becomes a hollow shell, seen inside through the hole.")]
    public bool destruction = false;

    [Tooltip("Where the hole sits round the orb, in degrees.")]
    public float holeAround = 0f;

    [Tooltip("How far the hole sits above (+) or below (−) the orb's middle, in degrees. 90 = right on top.")]
    [Range(-90f, 90f)] public float holeUpDown = 20f;

    [Tooltip("How big the hole is: degrees from its middle out to its edge, before the edge's rounded shapes.")]
    [Range(1f, 150f)] public float holeSize = 35f;

    [Tooltip("How many rounded shapes run round the hole's edge. 0 = a plain round hole.")]
    [Range(0, 16)] public int holeEdgeComplexity = 5;

    [Tooltip("How far the edge's rounded shapes reach out past the hole, as a share of Hole Size.")]
    [Range(0f, 1f)] public float holeEdgeDepth = 0.35f;

    [Tooltip("How much the edge's rounded shapes differ from each other in length, width and spacing. 0 = all alike.")]
    [Range(0f, 1f)] public float holeEdgeUnevenness = 0.6f;

    [Tooltip("The shape of each rounded shape on the edge: low = broad gentle waves, high = full rounded tips.")]
    [Range(0f, 1f)] public float holeEdgeRoundness = 0.5f;

    [Tooltip("How thick the orb's shell is — the band you see round the hole's edge.")]
    [Min(0.001f)] public float holeEdgeThickness = 0.015f;

    [Tooltip("Stands a small lollipop inside the broken orb, on the lowest point of its floor — only with Destruction on.")]
    public bool innerLollipop = false;

    [Tooltip("The lollipop tower preset the inner lollipop is built from — linked, so editing it changes " +
             "every inner lollipop using it. Always built whole: its own Destruction and Inner Lollipop are ignored.")]
    public LollipopTowerPreset innerLollipopPreset;

    [Tooltip("How tall the inner lollipop stands, as a share of the space inside the orb above where it stands. " +
             "1 = its orb touches the top of the hollow.")]
    [Range(0.05f, 1f)] public float innerLollipopSize = 0.6f;

    /// <summary>
    /// The very top of the orb, measured up from the tower's base. The angel lands here, so it is
    /// worked out the same way the mesh is built: the tiers stacked bottom up, then the stem, then
    /// the orb seated where the stem's top edge meets its surface.
    /// </summary>
    public float TopY
    {
        get
        {
            float r = Mathf.Max(0.001f, stemRadius);
            float R = Mathf.Max(0.001f, orbRadius);

            float tiers = Mathf.Max(0f, lowerRampHeight) + Mathf.Max(0f, baseHeight) + Mathf.Max(0f, rampHeight) + Mathf.Max(0f, base2Height);
            float seat  = Mathf.Sqrt(Mathf.Max(0f, R * R - r * r));

            return tiers + Mathf.Max(0f, stemHeight) + seat + R;
        }
    }

    /// <summary>
    /// How far the tower reaches out at its foot — the lower ramp's radius when there is one and it
    /// is wider, otherwise the base's.
    /// </summary>
    public float FootRadius
        => lowerRampHeight > 0f ? Mathf.Max(baseRadius, lowerRampRadius) : baseRadius;

    public LollipopTower Clone() => (LollipopTower)MemberwiseClone();

    /// <summary>
    /// A copy with every size multiplied by <paramref name="scale"/> — the placement's own scale,
    /// which is kept on the placement and never saved into a preset. 0 or below counts as 1, so
    /// placements saved before the scale existed keep their size.
    /// </summary>
    public LollipopTower Scaled(float scale)
    {
        var copy = Clone();
        if (scale <= 0f || Mathf.Approximately(scale, 1f)) return copy;
        float s = scale;

        copy.stemRadius  *= s;
        copy.stemHeight  *= s;
        copy.orbRadius   *= s;
        copy.lowerRampHeight *= s;
        copy.lowerRampRadius *= s;
        copy.baseRadius  *= s;
        copy.baseHeight  *= s;
        copy.rampHeight  *= s;
        copy.base2Radius *= s;
        copy.base2Height *= s;
        copy.holeEdgeThickness *= s;
        return copy;
    }

    const int Sides = 24;
    const int OrbRings = 16;

    /// <summary>
    /// The tower on its own, base at the origin, carrying the river run stone shading. Each part
    /// takes the stone colour <paramref name="shading"/> gives it; null leaves them worked out.
    /// <paramref name="rimTopY"/> is the rivers' rim top above the base — 0 for a tower standing
    /// on a rim, the water's depth for one standing on the water.
    /// </summary>
    public Mesh Build(RiverRunShadingSettings shading = null, float rimTopY = 0f)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();
        var kinds = new List<float>();
        var parts = new List<int>();

        Append(verts, norms, uvs, tris, Vector3.zero, kinds, shading, parts);

        var mesh = new Mesh { name = "LollipopTower" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        // River run stone shading, read off its base, with each part's colour as the settings say.
        return RiverMeshBuilder.ShadeAsStone(mesh, rimTopY, 0f, kinds, parts,
                                             designation: RiverRunShadingSettings.TowerAs(shading));
    }

    /// <summary>
    /// Adds the tower into a mesh being built, its base standing at <paramref name="basePos"/>.
    /// <paramref name="kinds"/>, when given, is filled to one entry per triangle in
    /// <paramref name="tris"/> — any triangles already there get Auto (worked out as usual), and the
    /// tower's own get the stone colour <paramref name="shading"/> names for their part.
    /// <paramref name="parts"/>, when given, is filled the same way with which tier each face is
    /// on — lower ramp, base, upper ramp, second base — so the stone shading draws a seam at every join between
    /// them, however steep the ramp. Faces already there, and the stem and orb, are part 0.
    /// </summary>
    public void Append(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                       Vector3 basePos, List<float> kinds = null, RiverRunShadingSettings shading = null,
                       List<int> parts = null, bool withOrb = true,
                       string upperRampLabel = "Upper Ramp", string stemLabel = "Stem",
                       string tierLabel = null, string orbLabel = "Orb")
    {
        var sideKind = shading != null ? shading.towerBaseSides : StoneFaceKind.Auto;
        var capKind  = shading != null ? shading.towerBaseTop   : StoneFaceKind.Auto;
        var stemKind = shading != null ? shading.towerStem : StoneFaceKind.Auto;
        var orbKind  = shading != null ? shading.towerOrb  : StoneFaceKind.Auto;
        var lowerRampKind = shading != null ? shading.towerLowerRamp : StoneFaceKind.Auto;
        var upperRampKind = shading != null ? shading.towerUpperRamp : StoneFaceKind.Auto;
        var lowerRampTopKind = shading != null ? shading.towerLowerRampTop    : StoneFaceKind.Auto;
        var upperRampTopKind = shading != null ? shading.towerUpperRampTop    : StoneFaceKind.Auto;
        var base2SideKind    = shading != null ? shading.towerSecondBaseSides : StoneFaceKind.Auto;
        var base2CapKind     = shading != null ? shading.towerSecondBaseTop   : StoneFaceKind.Auto;

        StoneFaces.Tag(kinds, tris, StoneFaceKind.Auto);
        StoneFaces.Part(parts, tris, 0);
        PartLabels.Mark(null, verts, tris);

        float r = Mathf.Max(0.001f, stemRadius);
        float h = Mathf.Max(0f, stemHeight);
        float R = Mathf.Max(0.001f, orbRadius);

        // ── Lower ramp, base, upper ramp and second base, bottom up — each off at zero height. A
        //    tier is capped unless the one on it covers its whole top ──
        float lh  = Mathf.Max(0f, lowerRampHeight);
        float lr  = Mathf.Max(0.001f, lowerRampRadius);
        float bh  = Mathf.Max(0f, baseHeight);
        float br  = Mathf.Max(0.001f, baseRadius);
        float rh  = Mathf.Max(0f, rampHeight);
        float b2h = Mathf.Max(0f, base2Height);
        float b2r = Mathf.Max(0.001f, base2Radius);

        // The base's top is open only when the upper ramp, or a second base at least as wide,
        // stands on it — and the lower ramp's top the same, when there is no base between.
        bool baseCapped = rh <= 0f && (b2h <= 0f || b2r < br);

        if (lh > 0f)
            AppendRing(verts, norms, uvs, tris, kinds, parts, 4, ref basePos, lr, br, lh,
                       bh <= 0f && baseCapped, lowerRampKind, lowerRampTopKind, tierLabel ?? "Lower Ramp");
        if (bh > 0f)
            AppendRing(verts, norms, uvs, tris, kinds, parts, 1, ref basePos, br, br, bh,
                       baseCapped, sideKind, capKind, tierLabel ?? "Base");
        if (rh > 0f)
            AppendRing(verts, norms, uvs, tris, kinds, parts, 2, ref basePos, br, b2r, rh,
                       b2h <= 0f, upperRampKind, upperRampTopKind, tierLabel ?? upperRampLabel);
        if (b2h > 0f)
            AppendRing(verts, norms, uvs, tris, kinds, parts, 3, ref basePos, b2r, b2r, b2h,
                       true, base2SideKind, base2CapKind, tierLabel ?? "Base 2");

        // ── Stem: smooth sides, one column doubled at the seam so u runs round unbroken ──
        if (h > 0f)
        {
            int start = verts.Count;
            for (int i = 0; i <= Sides; i++)
            {
                float   a = i * Mathf.PI * 2f / Sides;
                Vector3 n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float   u = a * r;

                verts.Add(basePos + n * r);                   norms.Add(n); uvs.Add(new Vector2(u, 0f));
                verts.Add(basePos + n * r + Vector3.up * h);  norms.Add(n); uvs.Add(new Vector2(u, h));
            }
            for (int i = 0; i < Sides; i++)
            {
                int b0 = start + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                AddTri(tris, verts, norms, b0, t0, t1);
                AddTri(tris, verts, norms, b0, t1, b1);
            }

            // A stem wider than its orb pokes out past it, so it is capped.
            if (r >= R)
            {
                int centre = verts.Count;
                verts.Add(basePos + Vector3.up * h); norms.Add(Vector3.up); uvs.Add(Vector2.zero);
                for (int i = 0; i <= Sides; i++)
                {
                    float a = i * Mathf.PI * 2f / Sides;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    verts.Add(basePos + d + Vector3.up * h); norms.Add(Vector3.up); uvs.Add(new Vector2(d.x, d.z));
                }
                for (int i = 0; i < Sides; i++)
                    AddTri(tris, verts, norms, centre, centre + 1 + i, centre + 2 + i);
            }

            StoneFaces.Tag(kinds, tris, stemKind);
            StoneFaces.Part(parts, tris, 0);
            PartLabels.Mark(stemLabel, verts, tris);
        }

        // ── Orb: seated so the stem's top edge lies on its surface. A turret builds its own,
        //    as a separate piece that turns ──
        if (!withOrb) return;

        Vector3 orbCentre = basePos + Vector3.up * (h + Mathf.Sqrt(Mathf.Max(0f, R * R - r * r)));
        if (!destruction)
        {
            AppendOrb(verts, norms, uvs, tris, orbCentre, R);

            StoneFaces.Tag(kinds, tris, orbKind);
            StoneFaces.Part(parts, tris, 0);
            PartLabels.Mark(orbLabel, verts, tris);
            return;
        }

        var insideKind = shading != null ? shading.towerOrbInside   : StoneFaceKind.D;
        var edgeKind   = shading != null ? shading.towerOrbHoleEdge : StoneFaceKind.D;
        AppendBrokenOrb(verts, norms, uvs, tris, kinds, parts, orbCentre, R, orbKind, insideKind, edgeKind);

        if (innerLollipop)
            AppendInnerLollipop(verts, norms, uvs, tris, kinds, parts, shading, orbCentre, R);
    }

    /// <summary>
    /// The small lollipop standing inside the broken orb: <see cref="innerLollipopPreset"/>, built
    /// whole (its own Destruction and Inner Lollipop ignored, so nothing nests again), scaled to
    /// Inner Lollipop Size of the hollow's height above where it stands. It stands on the lowest
    /// point of the hollow, raised just enough that the rim of its foot sits on the curved floor
    /// all the way round.
    /// </summary>
    void AppendInnerLollipop(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                             List<float> kinds, List<int> parts, RiverRunShadingSettings shading,
                             Vector3 centre, float R)
    {
        if (innerLollipopPreset == null || innerLollipopPreset.tower == null) return;

        var inner = innerLollipopPreset.Build();
        inner.destruction   = false;
        inner.innerLollipop = false;

        float Ri     = R - Mathf.Clamp(holeEdgeThickness, 0.001f, R * 0.9f);
        float height = Mathf.Max(0.001f, inner.TopY);
        float foot   = Mathf.Max(0.001f, inner.FootRadius);
        float size   = Mathf.Clamp(innerLollipopSize, 0.05f, 1f);

        // The wider it stands, the higher its foot's rim meets the floor, and the less room above
        // it — so the scale and the lift are settled together.
        float scale = size * 2f * Ri / height, lift = 0f;
        for (int i = 0; i < 8; i++)
        {
            float f = Mathf.Min(foot * scale, Ri);
            lift  = Ri - Mathf.Sqrt(Mathf.Max(0f, Ri * Ri - f * f));
            scale = size * (2f * Ri - lift) / height;
        }

        Vector3 basePos = centre + Vector3.up * (lift - Ri);
        inner.Scaled(scale).Append(verts, norms, uvs, tris, basePos, kinds,
                                   RiverRunShadingSettings.ForInnerLollipop(shading), parts,
                                   stemLabel: "Inner Lollipop Stem", tierLabel: "Inner Lollipop Base",
                                   orbLabel: "Inner Lollipop Orb");
    }

    const int HoleColumns = 64;   // round the hole's edge
    const int HoleRings   = 10;   // from the hole's edge to the far side of the orb

    /// <summary>
    /// The orb with Destruction on: a hollow shell of radius <paramref name="R"/>, its inside
    /// Hole Edge Thickness in, with one hole broken through it. The shell is laid out round the
    /// hole — rings from the hole's edge across to the far side — so its first ring IS the edge,
    /// rounded shapes and all, with nothing cut. The edge band joins the outside to the inside
    /// straight in towards the centre.
    /// </summary>
    void AppendBrokenOrb(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                         List<float> kinds, List<int> parts, Vector3 centre, float R,
                         StoneFaceKind orbKind, StoneFaceKind insideKind, StoneFaceKind edgeKind)
    {
        float Ri = R - Mathf.Clamp(holeEdgeThickness, 0.001f, R * 0.9f);

        // The hole's middle, and two directions across it to measure round its edge from.
        float   yaw   = holeAround * Mathf.Deg2Rad;
        float   pitch = Mathf.Clamp(holeUpDown, -90f, 90f) * Mathf.Deg2Rad;
        Vector3 d     = new Vector3(Mathf.Cos(pitch) * Mathf.Cos(yaw), Mathf.Sin(pitch),
                                    Mathf.Cos(pitch) * Mathf.Sin(yaw));
        Vector3 e1    = Vector3.Cross(d, Vector3.up);
        e1 = e1.sqrMagnitude > 1e-8f ? e1.normalized : Vector3.right;
        Vector3 e2    = Vector3.Cross(d, e1);

        // The edge: degrees from the hole's middle out to its edge, all the way round.
        var edge = HoleEdgeAngles(HoleColumns);

        Vector3 Dir(float theta, float phi)
            => Mathf.Cos(theta) * d + Mathf.Sin(theta) * (Mathf.Cos(phi) * e1 + Mathf.Sin(phi) * e2);

        // ── Outside, then inside: the same rings, the inside facing in ──
        AppendShell(R, 1f);
        StoneFaces.Tag(kinds, tris, orbKind);
        StoneFaces.Part(parts, tris, 0);
        PartLabels.Mark("Orb", verts, tris);

        AppendShell(Ri, -1f);
        StoneFaces.Tag(kinds, tris, insideKind);
        StoneFaces.Part(parts, tris, 0);
        PartLabels.Mark("Orb Inside", verts, tris);

        // ── Edge band: outside edge to inside edge, facing into the hole ──
        int start = verts.Count;
        for (int k = 0; k <= HoleColumns; k++)
        {
            int     kk    = k % HoleColumns;
            float   phi   = kk * Mathf.PI * 2f / HoleColumns;
            float   theta = edge[kk];
            Vector3 n     = Dir(theta, phi);

            float   phiA = (kk + HoleColumns - 1) % HoleColumns * Mathf.PI * 2f / HoleColumns;
            float   phiB = (kk + 1) % HoleColumns * Mathf.PI * 2f / HoleColumns;
            Vector3 along = Dir(edge[(kk + 1) % HoleColumns], phiB) - Dir(edge[(kk + HoleColumns - 1) % HoleColumns], phiA);
            Vector3 face  = Vector3.Cross(n, along).normalized;
            Vector3 outward = -Mathf.Sin(theta) * d + Mathf.Cos(theta) * (Mathf.Cos(phi) * e1 + Mathf.Sin(phi) * e2);
            if (Vector3.Dot(face, outward) > 0f) face = -face;   // into the hole, not into the shell

            float u = phi * R;
            verts.Add(centre + n * R);  norms.Add(face); uvs.Add(new Vector2(u, R));
            verts.Add(centre + n * Ri); norms.Add(face); uvs.Add(new Vector2(u, Ri));
        }
        for (int k = 0; k < HoleColumns; k++)
        {
            int o0 = start + k * 2, i0 = o0 + 1, o1 = o0 + 2, i1 = o0 + 3;
            AddTri(tris, verts, norms, o0, i0, i1);
            AddTri(tris, verts, norms, o0, i1, o1);
        }
        StoneFaces.Tag(kinds, tris, edgeKind);
        StoneFaces.Part(parts, tris, 0);
        PartLabels.Mark("Orb Hole Edge", verts, tris);

        // One shell at radius `radius`, its normals out (+1) or in (−1). Ring 0 lies on the hole's
        // edge, the last ring closes to a point on the far side.
        void AppendShell(float radius, float facing)
        {
            int shellStart = verts.Count;
            int cols       = HoleColumns + 1;
            for (int ring = 0; ring <= HoleRings; ring++)
            {
                float s = (float)ring / HoleRings;
                for (int k = 0; k <= HoleColumns; k++)
                {
                    float   phi   = k * Mathf.PI * 2f / HoleColumns;
                    float   from  = edge[k % HoleColumns];
                    float   theta = from + (Mathf.PI - from) * s;
                    Vector3 n     = Dir(theta, phi);

                    verts.Add(centre + n * radius);
                    norms.Add(n * facing);
                    uvs.Add(new Vector2(phi * radius, theta * radius));
                }
            }
            for (int ring = 0; ring < HoleRings; ring++)
            {
                for (int k = 0; k < HoleColumns; k++)
                {
                    int a = shellStart + ring * cols + k, b = a + 1;
                    int c = a + cols,                     e = c + 1;
                    AddTri(tris, verts, norms, a, b, c);
                    if (ring < HoleRings - 1) AddTri(tris, verts, norms, b, e, c);
                }
            }
        }
    }

    /// <summary>
    /// The hole's edge, in radians from its middle, at <paramref name="columns"/> even steps round
    /// it: Hole Size plus Hole Edge Complexity rounded shapes reaching out past it by up to Hole
    /// Edge Depth of Hole Size. Each shape is a smooth hump, flat where it meets the next, so the
    /// edge never comes to a point. Hole Edge Unevenness varies their length, width and spacing —
    /// the same way every time for the same settings.
    /// </summary>
    public float[] HoleEdgeAngles(int columns)
    {
        float size = Mathf.Clamp(holeSize, 1f, 150f) * Mathf.Deg2Rad;
        float most = 170f * Mathf.Deg2Rad;
        int   count  = Mathf.Max(0, holeEdgeComplexity);
        float uneven = Mathf.Clamp01(holeEdgeUnevenness);
        float power  = Mathf.Lerp(2f, 5f, Mathf.Clamp01(holeEdgeRoundness));
        float depth  = Mathf.Clamp01(holeEdgeDepth);

        var middles = new float[count];
        var halves  = new float[count];
        var lengths = new float[count];
        float spacing = count > 0 ? Mathf.PI * 2f / count : 0f;
        for (int i = 0; i < count; i++)
        {
            middles[i] = (i + (Hash(i, 1) - 0.5f) * 0.5f * uneven) * spacing;
            halves[i]  = 0.5f * spacing * (1f + uneven * (Hash(i, 2) * 0.9f - 0.45f));
            lengths[i] = 1f - uneven * 0.75f * Hash(i, 3);
        }

        var angles = new float[columns];
        for (int k = 0; k < columns; k++)
        {
            float phi  = k * Mathf.PI * 2f / columns;
            float reach = 0f;
            for (int i = 0; i < count; i++)
            {
                float gap = Mathf.Abs(Mathf.DeltaAngle(phi * Mathf.Rad2Deg, middles[i] * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                float t   = gap / halves[i];
                if (t >= 1f) continue;
                float hump = 1f - Mathf.Pow(t, power);
                reach += lengths[i] * hump * hump;
            }
            angles[k] = Mathf.Min(most, size * (1f + depth * reach));
        }
        return angles;
    }

    // A fixed number in [0, 1) for shape `i` — the same every build.
    static float Hash(int i, int salt)
    {
        float x = Mathf.Sin(i * 12.9898f + salt * 78.233f) * 43758.5453f;
        return x - Mathf.Floor(x);
    }

    /// <summary>
    /// The orb's centre, measured up from the tower's base — where the stem's top edge meets its
    /// surface. A turret's head turns about this point.
    /// </summary>
    public float OrbCentreY => TopY - Mathf.Max(0.001f, orbRadius);

    /// <summary>A sphere of radius <paramref name="R"/> round <paramref name="centre"/>.</summary>
    public static void AppendOrb(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                                 Vector3 centre, float R)
    {
        int orbStart = verts.Count;
        for (int ring = 0; ring <= OrbRings; ring++)
        {
            float lat = Mathf.PI * ring / OrbRings;           // 0 at the top, pi at the bottom
            for (int i = 0; i <= Sides; i++)
            {
                float   lon = i * Mathf.PI * 2f / Sides;
                Vector3 n   = new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat),
                                          Mathf.Sin(lat) * Mathf.Sin(lon));
                verts.Add(centre + n * R);
                norms.Add(n);
                uvs.Add(new Vector2(lon * R, (Mathf.PI - lat) * R));
            }
        }
        int cols = Sides + 1;
        for (int ring = 0; ring < OrbRings; ring++)
        {
            for (int i = 0; i < Sides; i++)
            {
                int a = orbStart + ring * cols + i, b = a + 1;
                int c = a + cols,                   d = c + 1;
                if (ring > 0)            AddTri(tris, verts, norms, a, b, d);
                if (ring < OrbRings - 1) AddTri(tris, verts, norms, a, d, c);
            }
        }
    }

    // One round tier of the base: sides running from the bottom radius to the top radius (a
    // cylinder when they match, a ramp when they don't), capped on top when nothing stands on it.
    // Moves basePos up to its top. Every face of it, cap included, is tagged as tier `part`.
    static void AppendRing(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                           List<float> kinds, List<int> parts, int part, ref Vector3 basePos, float bottomR, float topR, float height,
                           bool cap, StoneFaceKind sideKind, StoneFaceKind capKind, string label)
    {
        float slant = Mathf.Sqrt(height * height + (bottomR - topR) * (bottomR - topR));
        float midR  = (bottomR + topR) * 0.5f;

        int start = verts.Count;
        for (int i = 0; i <= Sides; i++)
        {
            float   a   = i * Mathf.PI * 2f / Sides;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 n   = (dir * height + Vector3.up * (bottomR - topR)).normalized;
            float   u   = a * midR;

            verts.Add(basePos + dir * bottomR);                      norms.Add(n); uvs.Add(new Vector2(u, 0f));
            verts.Add(basePos + dir * topR + Vector3.up * height);   norms.Add(n); uvs.Add(new Vector2(u, slant));
        }
        for (int i = 0; i < Sides; i++)
        {
            int b0 = start + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
            AddTri(tris, verts, norms, b0, t0, t1);
            AddTri(tris, verts, norms, b0, t1, b1);
        }
        StoneFaces.Tag(kinds, tris, sideKind);
        StoneFaces.Part(parts, tris, part);
        PartLabels.Mark(label, verts, tris);

        basePos += Vector3.up * height;
        if (!cap) return;

        int centre = verts.Count;
        verts.Add(basePos); norms.Add(Vector3.up); uvs.Add(Vector2.zero);
        for (int i = 0; i <= Sides; i++)
        {
            float   a = i * Mathf.PI * 2f / Sides;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * topR;
            verts.Add(basePos + d); norms.Add(Vector3.up); uvs.Add(new Vector2(d.x, d.z));
        }
        for (int i = 0; i < Sides; i++)
            AddTri(tris, verts, norms, centre, centre + 1 + i, centre + 2 + i);

        StoneFaces.Tag(kinds, tris, capKind);
        StoneFaces.Part(parts, tris, part);
        PartLabels.Mark(label, verts, tris);
    }

    // One triangle, wound so its face points the way its vertices' normals do — Unity's front
    // face is the winding whose Cross(v1-v0, v2-v0) points along the normal.
    internal static void AddTri(List<int> tris, List<Vector3> verts, List<Vector3> norms, int a, int b, int c)
    {
        Vector3 face = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
        Vector3 n    = norms[a] + norms[b] + norms[c];
        if (Vector3.Dot(face, n) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
        else                            { tris.Add(a); tris.Add(c); tris.Add(b); }
    }
}
