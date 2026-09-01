using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// One BaseDot, ready to be painted. This is the ONLY thing in the fog that moves — the grid it
/// lands on never does.
/// </summary>
public struct FogDot
{
    public Vector2 Position;   // world XZ
    public Vector2 Axis;       // unit direction along its chain — which way the ellipse lies
    public float   Radius;     // world units, across the chain
    public float   Stretch;    // 1 is round; higher draws it out along Axis
    public float   Height;     // sphere-cap height, after squash and height undulation
    public float   Strength;   // 0..1, so dots melt in rather than popping
    public float   BlobId;     // 0..1, so grain and undulation can be sampled in blob space
}

/// <summary>
/// One fog mass: a FogSpine and the FogLimbs hanging off it, both made of BaseDots.
///
/// A plain class, not a MonoBehaviour — every blob on the level is pooled and driven by
/// <see cref="FogFieldManager"/>. There is deliberately no GameObject and no component per dot:
/// several hundred dots move every frame, and the whole cost argument for this system rests on
/// that being a loop over a preallocated array with nothing allocating inside it.
///
/// Order of business each frame, which matters:
///   1. drift and breathe, giving the resting skeleton for this moment
///   2. let repellers shove that skeleton aside
///   3. relax the bend so it curves rather than kinks
///   4. lay dots along wherever the skeleton ended up
///
/// Repelling the SKELETON rather than the fog is what keeps the outline continuous as a blob
/// wraps a rock. Nothing is ever masked or cut out; the mass is redirected, so it reads as fog
/// flowing round an obstacle instead of a hole punched in a cloud.
/// </summary>
public class FogBlob
{
    // Enough for a long spine and six limbs at the coarsest useful spacing. A ceiling, not a
    // cost — the loops run to the counts actually laid down, which with elliptical dots is
    // usually 15-30.
    public const int MAX_DOTS = 96;

    const int SPINE_SAMPLES = 26;
    const int LIMB_SAMPLES  = 15;
    const int RELAX_PASSES  = 3;

    // ── Identity and placement ───────────────────────────────────────────────
    public FogProperties Shape { get; private set; }
    public Vector2 Centre;                 // world XZ
    public float   Scale;                  // world length of the spine
    public float   Rotation;               // radians; which way the mass lies
    public bool    Alive { get; private set; }
    public float   Id    { get; private set; }   // 0..1, written into the grid's blob-id channel

    // ── Life ─────────────────────────────────────────────────────────────────
    // No melt in or melt out. A mass is born out beyond the mask where it draws nothing at all,
    // and the mask feather alone fades it up as it comes in and down as it leaves - so it is
    // already invisible by the time it is dropped. A second fade on top of that was doing the
    // same job twice, and the two could disagree: a mass could be melting IN while the mask was
    // fading it OUT, which is why fog seemed to appear from nowhere and then think better of it.
    float _age;

    // ── Per-blob jitter, so one preset is not ten identical stamps ───────────
    float   _seed;
    float[] _limbLengthMul = new float[0];
    float[] _limbAngleOff  = new float[0];

    // ── Scratch, reused every frame ──────────────────────────────────────────
    readonly Vector2[] _chain = new Vector2[Mathf.Max(SPINE_SAMPLES, LIMB_SAMPLES)];
    readonly Vector2[] _spine = new Vector2[SPINE_SAMPLES];

    public readonly FogDot[] Dots = new FogDot[MAX_DOTS];
    public int DotCount { get; private set; }

    /// <summary>Where the mass currently sits and how far it reaches, for the manager's culling.</summary>
    public float ReachRadius { get; private set; }

    /// <summary>
    /// Which allocation on the arena map this mass fills. The manager uses it to keep two masses
    /// off one spot, and to free the slot again once this one has drifted clear of the field.
    /// </summary>


    /// <summary>
    /// Distance fade, 1 near the boat easing to 0 at the far edge of the field's range. Worked out
    /// by the manager rather than here, because it is the same answer for every dot in this mass —
    /// exactly the reasoning RockRingManager uses for its own ring fade.
    ///
    /// Multiplied into every dot's strength, so a far mass paints faintly rather than being
    /// switched off: it thins away as you sail from it and thickens as you return, instead of
    /// popping at a boundary.
    /// </summary>
    public float LodFade { get; set; } = 1f;


    // ────────────────────────────────────────────────────────────────────────
    public void Spawn(FogProperties shape, Vector2 centre, float scale, float rotation,
                      float id, int seed)
    {
        Shape    = shape;
        Centre   = centre;
        Scale    = Mathf.Max(scale, 0.01f);
        Rotation = rotation;
        Id       = id;
        _age     = 0f;
        Alive    = true;

        var rng = new System.Random(seed);
        _seed = (float)rng.NextDouble() * 1000f;

        int n = shape.EffectiveLimbCount;
        if (_limbLengthMul.Length != n)
        {
            _limbLengthMul = new float[n];
            _limbAngleOff  = new float[n];
        }
        for (int i = 0; i < n; i++)
        {
            // +-15% on length and a few degrees on angle. Enough that ten presets never read as
            // ten repeated shapes; little enough that the authored silhouette survives.
            _limbLengthMul[i] = 1f + ((float)rng.NextDouble() - 0.5f) * 0.30f;
            _limbAngleOff[i]  = ((float)rng.NextDouble() - 0.5f) * 14f;
        }
    }

    public void Kill()
    {
        Alive = false;
        DotCount = 0;
    }

    /// <summary>
    /// Sit this frame out: lay no dots, but KEEP DRIFTING AND AGEING.
    ///
    /// Skipping the whole simulation was a deadlock. A mass outside the mask froze exactly where
    /// it was, so it never drifted far enough from its allocation to hand the slot back, so no
    /// replacement ever formed there — fog appeared once and then never again. Only the expensive
    /// part is skipped now: no skeleton, no repelling, no dots. Moving is two adds.
    /// </summary>
    public void SkipFrame(float dt, Vector2 wind)
    {
        DotCount = 0;
        if (!Alive) return;

        _age += dt;
        Centre += wind * dt;
    }

    // ────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Advance one frame and lay this blob's dots down. <paramref name="settings"/> carries the
    /// dials that live on the field rather than the preset — wind, wander, and how hard things
    /// push fog away.
    /// </summary>
    public void Simulate(float dt, Vector2 wind, in FogFieldSettings settings,
                         List<IFogRepeller> repellers)
    {
        if (!Alive) { DotCount = 0; return; }

        _age += dt;

        // Drift moves the whole mass on the wind — the map's wind, handed in by the manager.
        // There is deliberately no second wind here: two of them pointed different ways and the
        // arrangement travelled one way while every mass inside it travelled another.
        Centre += wind * dt;


        DotCount = 0;
        float maxReach = 0f;

        // ── Spine ────────────────────────────────────────────────────────────
        for (int i = 0; i < SPINE_SAMPLES; i++)
        {
            float t = i / (float)(SPINE_SAMPLES - 1);
            Vector2 local = Shape.SpineAt(t) * Scale;
            _spine[i] = Centre + Rotate(local, Rotation);
        }

        Deform(_spine, SPINE_SAMPLES, repellers, settings, Shape.spineStiffness);
        LayChain(_spine, SPINE_SAMPLES, isLimb: false, ref maxReach);

        // ── Limbs ────────────────────────────────────────────────────────────
        int limbCount = Shape.EffectiveLimbCount;
        for (int L = 0; L < limbCount; L++)
        {
            float specAlong  = Shape.LimbAlong(L);
            float specSide   = Shape.LimbSide(L);
            float specLength = Shape.LimbLengthOf(L);
            float specAngle  = Shape.limbAngle;
            float specDroop  = Shape.limbDroop;

            // No breathing. Limbs held a fixed length now — the wander already moves the whole
            // skeleton, and a second oscillation on top of it read as the mass pulsing rather
            // than drifting.
            float length = specLength * _limbLengthMul[L] * Scale;
            if (length <= 0.0001f) continue;

            // Limbs leave the spine near perpendicular and stay roughly parallel to each other.
            // That regularity is what makes a blob read as drifting fog rather than a splat, so
            // the authored angle is an offset from perpendicular, never a free direction.
            Vector2 rootLocal = Shape.SpineAt(specAlong) * Scale;
            Vector2 root      = Centre + Rotate(rootLocal, Rotation);
            Vector2 dir       = Rotate(Shape.SpineDirectionAt(specAlong), Rotation);
            Vector2 perp      = new Vector2(dir.y, -dir.x) * Mathf.Sign(specSide);
            perp = Rotate(perp, (specAngle + _limbAngleOff[L]) * Mathf.Deg2Rad);

            for (int i = 0; i < LIMB_SAMPLES; i++)
            {
                float u = i / (float)(LIMB_SAMPLES - 1);
                // Curl: the limb leans along the spine as it travels, which is the difference
                // between a finger and a spike.
                Vector2 p = root + perp * (length * u) + dir * (specDroop * length * u * u);
                _chain[i] = p;
            }

            Deform(_chain, LIMB_SAMPLES, repellers, settings, Shape.spineStiffness);
            LayChain(_chain, LIMB_SAMPLES, isLimb: true, ref maxReach);
        }

        ReachRadius = maxReach;
    }

    // ────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Push a chain clear of every repeller, then relax it so the bend curves instead of kinking.
    /// Pushing points straight out of a circle leaves a sharp corner where the chain meets the
    /// edge, so the relax pass runs after — and pushes again, because relaxing can walk a point
    /// back inside.
    /// </summary>
    void Deform(Vector2[] chain, int count, List<IFogRepeller> repellers,
                in FogFieldSettings settings, float stiffness)
    {
        if (repellers == null || repellers.Count == 0) return;

        PushOut(chain, count, repellers, settings);

        // Stiffness resists bending, so a stiff spine keeps its shape and shoulders past a rock
        // while a slack one drapes around it.
        float relax = Mathf.Clamp01(1f - stiffness) * 0.5f + 0.25f;
        for (int pass = 0; pass < RELAX_PASSES; pass++)
        {
            Vector2 prev = chain[0];
            for (int i = 1; i < count - 1; i++)
            {
                Vector2 here = chain[i];
                Vector2 avg  = (prev + chain[i + 1]) * 0.5f;
                chain[i] = Vector2.Lerp(here, avg, relax);
                prev = here;
            }
            PushOut(chain, count, repellers, settings);
        }
    }

    void PushOut(Vector2[] chain, int count, List<IFogRepeller> repellers,
                 in FogFieldSettings settings)
    {
        for (int r = 0; r < repellers.Count; r++)
        {
            var rep = repellers[r];
            if (rep == null || !rep.RepelActive) continue;

            Vector3 c3 = rep.RepelCentre;
            Vector2 c  = new Vector2(c3.x, c3.z);

            // The obstacle's own radius plus the clearance it asks for. There used to be a third
            // global term on top, meant to compensate for dots having width — but dot radii vary
            // several-fold along a body, so one number could not do that job, and it was only ever
            // a second dial onto this same sum.
            float keep = rep.RepelRadius + rep.RepelClearRadius;
            if (keep <= 0f) continue;

            float strength = Mathf.Clamp01(rep.RepelStrength) * settings.RepelStrength;
            if (strength <= 0f) continue;

            float keepSq = keep * keep;
            for (int i = 0; i < count; i++)
            {
                Vector2 d = chain[i] - c;
                float sq = d.sqrMagnitude;
                if (sq >= keepSq || sq < 1e-10f) continue;

                float dist = Mathf.Sqrt(sq);
                Vector2 outward = d / dist;
                // Partial strength lets a moving repeller (the boat) be pressed into and recovered
                // from, rather than pinning the skeleton exactly on the ring every frame.
                chain[i] = Vector2.Lerp(chain[i], c + outward * keep, strength);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Walk a chain dropping BaseDots along it. Spacing comes from the dot's own footprint, not
    /// from a dial: dots must overlap or the limb beads into a string of sausages once the
    /// threshold sees it. Elliptical dots cover more ground each, which is why stretching them
    /// takes a blob from around eighty dots to fifteen.
    /// </summary>
    void LayChain(Vector2[] chain, int count, bool isLimb, ref float maxReach)
    {
        if (count < 2) return;

        float stretch = Mathf.Max(Shape.ellipseStretch, 1f);

        // Total length first, so u runs by distance travelled rather than by sample index — a
        // chain bent hard around a rock has its samples bunched up on the inside of the curve.
        float total = 0f;
        for (int i = 1; i < count; i++) total += (chain[i] - chain[i - 1]).magnitude;
        if (total <= 1e-5f) return;

        float travelled = 0f;
        float nextAt    = 0f;

        for (int i = 1; i < count; i++)
        {
            Vector2 a = chain[i - 1], b = chain[i];
            float seg = (b - a).magnitude;
            if (seg <= 1e-6f) continue;
            Vector2 axis = (b - a) / seg;

            while (nextAt <= travelled + seg)
            {
                if (DotCount >= MAX_DOTS) return;

                float f = (nextAt - travelled) / seg;
                Vector2 p = a + (b - a) * f;
                float u = Mathf.Clamp01(nextAt / total);

                float radius = Mathf.Max(isLimb ? Shape.LimbThicknessAt(u)
                                               : Shape.SpineThicknessAt(u), 0.0001f) * Scale;
                float squash = Shape.SquashAt(u);

                // Per-dot size jitter. Keyed off the dot's own position along the chain and this
                // blob's seed, so a given dot keeps the same size frame after frame — rolled fresh
                // each frame it would flicker rather than read as an uneven mass.
                if (Shape.radiusVariation > 0f)
                {
                    float j = Frac(Mathf.Sin((u * 371.9f + _seed + (isLimb ? 17.3f : 0f)) * 12.9898f)
                                   * 43758.5453f) - 0.5f;
                    radius *= 1f + j * 2f * Shape.radiusVariation;
                    radius = Mathf.Max(radius, 0.0001f);
                }

                // Height undulation rides on top of the authored squash curve so the top surface
                // rolls instead of reading as one uniform ridge. Sampled along the chain rather
                // than per dot, so neighbouring dots agree and it swells rather than shimmers.
                float roll = 1f;
                if (Shape.heightUndulation > 0f)
                {
                    float n = Mathf.PerlinNoise(u * Shape.heightUndulationScale + _seed,
                                                (isLimb ? 31.7f : 5.3f) + _seed * 0.37f) - 0.5f;
                    roll = 1f + n * 2f * Shape.heightUndulation;
                }

                Dots[DotCount++] = new FogDot
                {
                    Position = p,
                    Axis     = axis,
                    Radius   = radius,
                    Stretch  = stretch,
                    Height   = radius * squash * Mathf.Max(roll, 0f),
                    Strength = LodFade,
                    BlobId   = Id,
                };

                float reach = (p - Centre).magnitude + radius * stretch;
                if (reach > maxReach) maxReach = reach;

                // One radius of overlap along the chain. Any sparser and the threshold finds gaps.
                nextAt += Mathf.Max(radius * stretch * 0.55f, 0.01f);
            }
            travelled += seg;
        }
    }

    static float Frac(float v) => v - Mathf.Floor(v);

    static Vector2 Rotate(Vector2 v, float radians)
    {
        float s = Mathf.Sin(radians), c = Mathf.Cos(radians);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

}
