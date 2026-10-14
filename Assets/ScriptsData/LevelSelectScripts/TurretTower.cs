using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A turret tower: a lollipop tower whose orb is a head that turns, with a hollow nose poking out
/// of it, angled upward. Stands anywhere a lollipop tower can — outposts, wall nodes, pool
/// islands and rim nodes.
///
///              __====   <- nose (outer radius, inner radius = the hollow, length past the orb)
///          ___/         <- tilted up by the rest pitch, turning about the orb's centre
///         /   \         <- head (the body's orb)
///         \___/
///           |           <- body: base tiers and stem, as a lollipop tower's
///          _|_
///
/// Built as three pieces so they can move apart: the body (base and stem) stands still, the head
/// turns left and right about the orb's centre, and the nose — a child of the head, pivoting on
/// the same centre — tilts up and down. <see cref="TurretTowerController"/> does the turning.
/// </summary>
[Serializable]
public class TurretTower
{
    [Tooltip("Base tiers, stem and orb — the same sizes a lollipop tower has. The orb is the head.")]
    public LollipopTower body = new LollipopTower();

    [Header("Nose")]
    [Tooltip("How far the nose reaches out past the orb's surface.")]
    [Min(0f)] public float noseLength = 0.12f;

    [Tooltip("Radius of the outside of the nose.")]
    [Min(0.001f)] public float noseOuterRadius = 0.03f;

    [Tooltip("Radius of the hollow down the middle of the nose. 0 = solid.")]
    [Min(0f)] public float noseInnerRadius = 0.018f;

    [Tooltip("How far the nose is tilted up from level, in degrees, as it is built — and where it " +
             "starts before the animation moves it.")]
    public float restPitch = 20f;

    // How the turrets move is set for all of them at once on the scene's TurretTowerAnimation.

    /// <summary>The very top of the orb, measured up from the tower's base.</summary>
    public float TopY => (body ?? new LollipopTower()).TopY;

    /// <summary>The orb's centre, measured up from the tower's base — where the head and nose turn.</summary>
    public float OrbCentreY => (body ?? new LollipopTower()).OrbCentreY;

    public TurretTower Clone()
    {
        var copy = (TurretTower)MemberwiseClone();
        copy.body = (body ?? new LollipopTower()).Clone();
        return copy;
    }

    /// <summary>
    /// A copy with every size multiplied by <paramref name="scale"/> — the placement's own scale.
    /// The rest pitch is left as it is. 0 or below counts as 1.
    /// </summary>
    public TurretTower Scaled(float scale)
    {
        var copy = Clone();
        copy.body = (body ?? new LollipopTower()).Scaled(scale);
        if (scale <= 0f || Mathf.Approximately(scale, 1f)) return copy;

        copy.noseLength      *= scale;
        copy.noseOuterRadius *= scale;
        copy.noseInnerRadius *= scale;
        return copy;
    }

    const int NoseSides = 24;

    /// <summary>The base tiers and stem, base at the origin — everything that stands still.</summary>
    public Mesh BuildBody(RiverRunShadingSettings shading = null)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();
        var kinds = new List<float>();
        var parts = new List<int>();

        (body ?? new LollipopTower()).Append(verts, norms, uvs, tris, Vector3.zero, kinds, shading, parts,
                                             withOrb: false);

        return Finish("TurretTowerBody", verts, norms, uvs, tris, kinds, parts, 0f);
    }

    /// <summary>The orb, centred on the origin — the head, which turns about its own centre.</summary>
    public Mesh BuildHead(RiverRunShadingSettings shading = null)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();
        var kinds = new List<float>();

        LollipopTower.AppendOrb(verts, norms, uvs, tris, Vector3.zero, Mathf.Max(0.001f, body.orbRadius));
        StoneFaces.Tag(kinds, tris, shading != null ? shading.towerOrb : StoneFaceKind.Auto);

        // Its origin is the orb's centre, so the rim top the waterline is placed off is below it.
        return Finish("TurretTowerHead", verts, norms, uvs, tris, kinds, null, -OrbCentreY);
    }

    /// <summary>
    /// The nose, pointing along +z from the orb's centre at the origin — the pivot it tilts about.
    /// A tube running from inside the orb out to <see cref="noseLength"/> past its surface, open at
    /// the far end with the hollow closed off deep inside the orb, so looking down it shows a bore
    /// rather than straight through.
    /// </summary>
    public Mesh BuildNose(RiverRunShadingSettings shading = null)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var tris  = new List<int>();
        var kinds = new List<float>();

        float R  = Mathf.Max(0.001f, body.orbRadius);
        float ro = Mathf.Max(0.001f, noseOuterRadius);
        float ri = Mathf.Clamp(noseInnerRadius, 0f, ro * 0.98f);
        bool  hollow = ri > 0.0001f;

        // Starts at the orb's centre, so its back end is always buried inside the orb.
        float zBack  = 0f;
        float zFront = R + Mathf.Max(0f, noseLength);

        // ── Outside wall ──
        AppendTube(verts, norms, uvs, tris, ro, zBack, zFront, outward: true);

        // ── Front: a ring round the hollow, or a solid cap ──
        AppendDisc(verts, norms, uvs, tris, zFront, hollow ? ri : 0f, ro, Vector3.forward);

        if (hollow)
        {
            // ── The hollow: its wall facing in, and its floor deep in the orb facing out ──
            AppendTube(verts, norms, uvs, tris, ri, zBack, zFront, outward: false);
            AppendDisc(verts, norms, uvs, tris, zBack, 0f, ri, Vector3.forward);
        }

        // ── Back, in case a nose wider than its orb shows it ──
        AppendDisc(verts, norms, uvs, tris, zBack, 0f, ro, Vector3.back);

        StoneFaces.Tag(kinds, tris, shading != null ? shading.towerStem : StoneFaceKind.Auto);

        return Finish("TurretTowerNose", verts, norms, uvs, tris, kinds, null, -OrbCentreY);
    }

    static Mesh Finish(string name, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                       List<float> kinds, List<int> parts, float rimTopY)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        // River run stone shading, the same as a lollipop tower's, each part in the colour the
        // tower settings give it.
        return RiverMeshBuilder.ShadeAsStone(mesh, rimTopY, rimTopY, kinds, parts);
    }

    // A tube of radius r along z, from z0 to z1, facing out or in.
    static void AppendTube(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                           float r, float z0, float z1, bool outward)
    {
        int start = verts.Count;
        for (int i = 0; i <= NoseSides; i++)
        {
            float   a   = i * Mathf.PI * 2f / NoseSides;
            Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            Vector3 n   = outward ? dir : -dir;
            float   u   = a * r;

            verts.Add(dir * r + Vector3.forward * z0); norms.Add(n); uvs.Add(new Vector2(u, z0));
            verts.Add(dir * r + Vector3.forward * z1); norms.Add(n); uvs.Add(new Vector2(u, z1));
        }
        for (int i = 0; i < NoseSides; i++)
        {
            int b0 = start + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
            LollipopTower.AddTri(tris, verts, norms, b0, t0, t1);
            LollipopTower.AddTri(tris, verts, norms, b0, t1, b1);
        }
    }

    // A flat ring at z from radius r0 out to r1, facing `facing` — a full disc when r0 is 0.
    static void AppendDisc(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                           float z, float r0, float r1, Vector3 facing)
    {
        int start = verts.Count;
        for (int i = 0; i <= NoseSides; i++)
        {
            float   a   = i * Mathf.PI * 2f / NoseSides;
            Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);

            Vector3 inner = dir * r0 + Vector3.forward * z;
            Vector3 outer = dir * r1 + Vector3.forward * z;
            verts.Add(inner); norms.Add(facing); uvs.Add(new Vector2(inner.x, inner.y));
            verts.Add(outer); norms.Add(facing); uvs.Add(new Vector2(outer.x, outer.y));
        }
        for (int i = 0; i < NoseSides; i++)
        {
            int i0 = start + i * 2, o0 = i0 + 1, i1 = i0 + 2, o1 = i0 + 3;
            LollipopTower.AddTri(tris, verts, norms, i0, o0, o1);
            if (r0 > 0f) LollipopTower.AddTri(tris, verts, norms, i0, o1, i1);
        }
    }
}
