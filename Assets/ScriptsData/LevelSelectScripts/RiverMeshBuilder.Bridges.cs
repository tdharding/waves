using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// River bridges: a flat deck straight across a river run at a designer node, with a matching
/// flat ramp at each end. Seen looking down the river:
///
///                        ___________________ deck top
///                  ramp /|                 |\ ramp
///                     /  |_________________|  \
///                   /    |    over water   |    \
///   walkways ... __/_____|_   (the run)   _|_____\__ ... walkways
///                  |  rim|                 |rim  |
///
/// The deck spans the channel's inner width; each ramp starts at the channel's edge with its top
/// at the deck's and runs straight out, away from the river, down to the rim's height a set
/// length out — over the rim and any walkways beside it. Its far end and the part beyond the
/// run's outer edge reach down to the same drop as the run, so it stands on a walkway of any
/// height; over the rim it sits on the rim top.
///
/// One closed piece per bridge: the cross-section above, swept the deck's width along the river.
/// Built into the same piece as the walkways, in the river material.
/// </summary>
public static partial class RiverMeshBuilder
{
    /// <summary>
    /// One bridge across a run: the designer node it stands at, in the run's own local space,
    /// and its sizes.
    /// </summary>
    public struct Bridge
    {
        public Vector3 at;
        public float   deckHeight;
        public float   deckWidth;
        public float   deckThickness;
        public float   rampLength;

        /// <summary>How far along the run from its node it stands — down the run when positive.</summary>
        public float   offsetAlong;
    }

    // One corner of the cross-section: across from the run's centre (+ its right), and height.
    // Which part and stone kind the face running on from it to the next corner takes.
    private struct BridgeCorner
    {
        public Vector2 p;
        public int     part;
        public System.Func<RiverRunShadingSettings, StoneFaceKind> kind;

        public BridgeCorner(float x, float y, int part,
                            System.Func<RiverRunShadingSettings, StoneFaceKind> kind)
        {
            p = new Vector2(x, y); this.part = part; this.kind = kind;
        }
    }

    private static void BuildBridge(MeshBuild b, RunGrid grid, Bridge bridge,
                                    RiverRunShadingSettings shading)
    {
        // Moved along the run's own centreline, so it follows the river round a bend; kept on
        // the run, where there is a rim for it to stand on.
        float s  = Mathf.Clamp(ArcOnCentreline(grid, bridge.at) + bridge.offsetAlong,
                               grid.arc[0], grid.arc[grid.Rings - 1]);
        var   st = StationAt(grid, s);

        Vector3 across = st.r; across.y = 0f;
        if (across.sqrMagnitude < 1e-8f) return;
        across.Normalize();
        Vector3 along = Vector3.Cross(across, Vector3.up);   // down the run

        float hw        = Mathf.Max(bridge.deckWidth, 0.01f) * 0.5f;
        float halfInner = grid.HalfInnerAtArc(s);
        float halfOuter = halfInner + grid.profile.rimWidth;
        float reach     = halfInner + Mathf.Max(bridge.rampLength, 0.01f);

        // Sat on the lower of the rim heights under it, so on a climbing river its floor never
        // stands clear of the rim — on the high side it is buried in it by the climb.
        float rimY  = Mathf.Min(st.c.y, Mathf.Min(StationAt(grid, s - hw).c.y,
                                                  StationAt(grid, s + hw).c.y));
        float topY  = st.c.y + Mathf.Max(bridge.deckHeight, 0.01f);
        float underY = Mathf.Max(topY - Mathf.Max(bridge.deckThickness, 0.005f), rimY + 0.002f);
        float baseY = rimY - grid.profile.depth;

        // Where the ramp's top crosses the run's outer edge, when it reaches past it.
        bool  pastRim = reach > halfOuter + 0.002f;
        float rampAtOuter = pastRim
                          ? Mathf.Lerp(topY, rimY, (halfOuter - halfInner) / (reach - halfInner))
                          : rimY;

        System.Func<RiverRunShadingSettings, StoneFaceKind> top     = x => x.bridgeTop;
        System.Func<RiverRunShadingSettings, StoneFaceKind> under   = x => x.bridgeUnderside;
        System.Func<RiverRunShadingSettings, StoneFaceKind> rampEnd = x => x.bridgeRampEnds;
        System.Func<RiverRunShadingSettings, StoneFaceKind> buried  = null;

        // The cross-section, anticlockwise looking down the run (x across, y up), so outward
        // from each edge is its right. Each corner carries the part of the face it starts.
        var loop = new List<BridgeCorner>();
        if (pastRim)
        {
            loop.Add(new BridgeCorner(-reach,     baseY,  WalkBuriedPart, buried));
            loop.Add(new BridgeCorner(-halfOuter, baseY,  WalkBuriedPart, buried));
            loop.Add(new BridgeCorner(-halfOuter, rimY,   WalkBuriedPart, buried));
        }
        else
        {
            loop.Add(new BridgeCorner(-reach,     rimY,   WalkBuriedPart, buried));
        }
        loop.Add(new BridgeCorner(-halfInner, rimY,   WalkWallPart,   under));
        loop.Add(new BridgeCorner(-halfInner, underY, WalkWallPart,   under));
        loop.Add(new BridgeCorner( halfInner, underY, WalkWallPart,   under));
        loop.Add(new BridgeCorner( halfInner, rimY,   WalkBuriedPart, buried));
        if (pastRim)
        {
            loop.Add(new BridgeCorner(halfOuter, rimY,  WalkBuriedPart, buried));
            loop.Add(new BridgeCorner(halfOuter, baseY, WalkBuriedPart, buried));
            loop.Add(new BridgeCorner(reach,     baseY, WalkWallPart,   rampEnd));
            loop.Add(new BridgeCorner(reach,     rimY,  WalkTopPart,    top));
            loop.Add(new BridgeCorner(halfOuter, rampAtOuter, WalkTopPart, top));
        }
        else
        {
            loop.Add(new BridgeCorner(reach,     rimY,  WalkTopPart,    top));
        }
        loop.Add(new BridgeCorner( halfInner, topY, WalkTopPart, top));
        loop.Add(new BridgeCorner(-halfInner, topY, WalkTopPart, top));
        if (pastRim)
        {
            loop.Add(new BridgeCorner(-halfOuter, rampAtOuter, WalkTopPart, top));
            loop.Add(new BridgeCorner(-reach,     rimY,        WalkWallPart, rampEnd));
        }

        Vector3 origin = new Vector3(st.c.x, 0f, st.c.z);
        Vector3 At(Vector2 p, float z) => origin + across * p.x + Vector3.up * p.y + along * z;

        float Kind(System.Func<RiverRunShadingSettings, StoneFaceKind> pick) =>
            shading != null && pick != null ? (float)pick(shading) : 0f;

        // The faces round the section, each edge swept from the upstream side to the downstream.
        for (int i = 0; i < loop.Count; i++)
        {
            var     from = loop[i];
            Vector2 p = from.p, q = loop[(i + 1) % loop.Count].p;
            Vector2 d = q - p;
            if (d.sqrMagnitude < 1e-10f) continue;

            Vector3 outward = across * d.y - Vector3.up * d.x;

            b.NextPart = from.part;
            b.NextKind = Kind(from.kind);
            b.NextLabel = from.kind == null ? null                 // buried in the run
                        : Mathf.Abs((p.x + q.x) * 0.5f) < halfInner + 1e-4f ? "Deck" : "Ramp";
            FacingQuad(b, At(p, -hw), At(q, -hw), At(q, hw), At(p, hw), outward);
        }

        // The two sides facing up and down the river, in convex pieces that share every corner
        // the faces round the section have — the deck, and each ramp in front of and beyond the
        // run's outer edge.
        var pieces = new List<Vector2[]>
        {
            new[] { new Vector2(-halfInner, underY), new Vector2(-halfInner, topY),
                    new Vector2( halfInner, topY),   new Vector2( halfInner, underY) },
        };
        foreach (float sgn in new[] { -1f, 1f })
        {
            if (pastRim)
            {
                pieces.Add(new[]
                {
                    new Vector2(sgn * halfInner, rimY),   new Vector2(sgn * halfInner, underY),
                    new Vector2(sgn * halfInner, topY),   new Vector2(sgn * halfOuter, rampAtOuter),
                    new Vector2(sgn * halfOuter, rimY),
                });
                pieces.Add(new[]
                {
                    new Vector2(sgn * halfOuter, rimY),   new Vector2(sgn * halfOuter, rampAtOuter),
                    new Vector2(sgn * reach,     rimY),   new Vector2(sgn * reach,     baseY),
                    new Vector2(sgn * halfOuter, baseY),
                });
            }
            else
            {
                pieces.Add(new[]
                {
                    new Vector2(sgn * halfInner, rimY), new Vector2(sgn * halfInner, underY),
                    new Vector2(sgn * halfInner, topY), new Vector2(sgn * reach,     rimY),
                });
            }
        }

        b.NextPart = WalkWallPart;
        b.NextKind = Kind(x => x.bridgeSides);
        foreach (var piece in pieces)
        {
            b.NextLabel = piece == pieces[0] ? "Deck" : "Ramp";
            foreach (float z in new[] { -hw, hw })
            {
                var poly = new List<Vector3>(piece.Length);
                foreach (var p in piece) poly.Add(At(p, z));
                EmitFacingConvex(b, poly, along * Mathf.Sign(z));
            }
        }

        b.NextPart = 0;
        b.NextKind = 0f;
        b.NextLabel = null;
    }
}
