using UnityEngine;

/// <summary>
/// What one fog mass is made of, as plain numbers.
///
/// This replaces the PrimaryFogShape asset. That was one hand-authored skeleton per file — a spine
/// curve and an explicit list of limbs, each with its own position, side, length, angle and droop.
/// It gave exact control over a silhouette nobody could see in isolation anyway: masses are blurred
/// and thresholded, jittered ±15% on limb length and ±14° on angle at spawn, and shoved about by
/// rocks. The precision was spent on a shape that never reached the water intact.
///
/// So limbs are GENERATED from a handful of properties now. Position spreads evenly along the
/// spine, side alternates with a bias, and length, angle and droop are shared with per-limb spread.
/// The character that mattered — limbs leaving near perpendicular and staying roughly parallel,
/// leaning to one side rather than strictly alternating — is exactly what LimbAngle near zero and
/// SideBias say, so it survives the move. What is lost is the ability to place one specific limb
/// somewhere specific, which was never visible.
///
/// Everything is NORMALISED: the spine is one unit long and limb lengths are fractions of it, so
/// one set of properties works at any blob size. World size arrives at spawn.
/// </summary>
[System.Serializable]
public struct FogProperties
{
    // ── Spine ────────────────────────────────────────────────────────────────
    [Tooltip("Sideways lean a third of the way along the body, as a fraction of body length. " +
             "0 is a straight body. Give this and Bend 2 OPPOSITE signs for an S; the same " +
             "sign bows it one way.")]
    [Range(-0.4f, 0.4f)] public float spineBend1;

    [Tooltip("Sideways lean two thirds along. See Bend 1 — the two together are the whole shape " +
             "of the body's curve.")]
    [Range(-0.4f, 0.4f)] public float spineBend2;

    [Tooltip("How hard the body resists being bent by a rock. 0 drapes around obstacles and can " +
             "fold into a knot; 1 shoulders past them almost unchanged.")]
    [Range(0f, 1f)] public float spineStiffness;

    // ── Limbs ────────────────────────────────────────────────────────────────
    [Tooltip("The single dial: how limbed this mass is. It scales BOTH the limb count and their " +
             "length, so 0 is a bare sausage and 1 is the full count at full length. Reach " +
             "for this first; the two properties below are what it scales.")]
    [Range(0f, 1f)] public float limbification;

    [Tooltip("How many limbs at full Limbification. Halve Limbification and you get half of these.")]
    [Range(0, 16)] public int limbCount;

    [Tooltip("How far a limb reaches, AS A FRACTION OF BODY LENGTH. Read it against Spine " +
             "Thickness: a limb only reads as a limb if it reaches several times further than " +
             "the body is thick. At thickness 0.1, limbs under about 0.4 are swallowed by the " +
             "body and the mass comes out round.")]
    [Range(0f, 2f)] public float limbLength;

    [Tooltip("How much limb lengths differ FROM EACH OTHER on one mass, as a fraction. Fixed per " +
             "limb, so a mass has a consistent build. The variation BETWEEN masses is rolled " +
             "separately at spawn and is always there.")]
    [Range(0f, 1f)] public float limbLengthSpread;

    [Tooltip("Degrees off perpendicular. Near 0 is drifting fog: limbs leave the body at right " +
             "angles and stay roughly parallel to each other. Push it far and the mass reads as " +
             "a starfish.")]
    [Range(-70f, 70f)] public float limbAngle;

    [Tooltip("How much a limb curls as it goes, as a fraction of its own length. 0 is a straight " +
             "spike, higher is a finger. Small values do a lot.")]
    [Range(0f, 1f)] public float limbDroop;

    [Tooltip("Which side limbs favour. 0 strictly alternates left and right, which reads as a fish " +
             "bone; 1 puts every limb on one side. The middle is where fog stops looking " +
             "symmetrical.")]
    [Range(0f, 1f)] public float sideBias;

    // ── Body ─────────────────────────────────────────────────────────────────
    [Tooltip("How fat the body is at its tail, as a fraction of body length. This is the number " +
             "Limb Length has to beat for limbs to show.")]
    [Range(0.005f, 0.5f)] public float spineThicknessRoot;

    [Tooltip("How fat the body is at its head. Differ from the root to taper the mass.")]
    [Range(0.005f, 0.5f)] public float spineThicknessTip;

    [Tooltip("How fat a limb is where it meets the body, as a fraction of body length.")]
    [Range(0.005f, 0.5f)] public float limbThicknessRoot;

    [Tooltip("How fat a limb is at its far end. Taper it to nothing and the threshold deletes the " +
             "limb outright — thickness and the material threshold are a coupled pair.")]
    [Range(0.005f, 0.5f)] public float limbThicknessTip;

    [Tooltip("SHADING ONLY, never the outline. How domed the mass is at the body: 1 is a full " +
             "sphere, lower is a flat dome. Dragging this changes the lighting and leaves the " +
             "silhouette exactly where it was, which is not a bug.")]
    [Range(0f, 1f)] public float squashRoot;

    [Tooltip("Shading only. How domed the mass is at the limb tips. Flatter than the root lets limbs " +
             "lie on the water while the body rises.")]
    [Range(0f, 1f)] public float squashTip;

    [Tooltip("How far each BaseDot is drawn out along its chain. 1 is round; higher makes them " +
             "ellipses lying along the limb. This is why a mass needs about 15 dots instead " +
             "of 80, so lowering it costs performance for very little.")]
    [Range(1f, 6f)] public float ellipseStretch;

    [Tooltip("Per-dot size jitter, as a fraction. Breaks up the uniform tube look along a limb. It " +
             "does not touch the outline, which comes from the blurred grid either way.")]
    [Range(0f, 0.8f)] public float radiusVariation;

    // ── Height ───────────────────────────────────────────────────────────────
    [Tooltip("SHADING ONLY. Vertical noise on the mass's top surface so it rolls rather than " +
             "reading as one smooth ridge. Cannot give you a lumpy silhouette — the outline " +
             "comes from the flat grid.")]
    [Range(0f, 1f)] public float heightUndulation;

    [Tooltip("Shading only. Long slow swells along the mass versus tight close bumps.")]
    [Range(0.1f, 8f)] public float heightUndulationScale;

    public static FogProperties Default => new FogProperties
    {
        spineBend1 = 0.03f,
        spineBend2 = -0.03f,
        spineStiffness = 0.7f,

        limbification    = 1f,
        limbCount        = 3,
        limbLength       = 0.6f,
        limbLengthSpread = 0.25f,
        limbAngle        = 0f,
        limbDroop        = 0.15f,
        sideBias         = 0.5f,

        spineThicknessRoot = 0.10f,
        spineThicknessTip  = 0.10f,
        limbThicknessRoot  = 0.09f,
        limbThicknessTip   = 0.05f,
        squashRoot         = 0.55f,
        squashTip          = 0.25f,
        ellipseStretch     = 3f,
        radiusVariation    = 0.22f,

        heightUndulation      = 0.25f,
        heightUndulationScale = 2f,
    };

    // ── Derived ──────────────────────────────────────────────────────────────

    /// <summary>Limbs this mass actually grows, after Limbification.</summary>
    public int EffectiveLimbCount =>
        Mathf.Max(0, Mathf.RoundToInt(limbCount * Mathf.Clamp01(limbification)));

    /// <summary>
    /// A point on the spine in normalised blob space: the spine runs 0..1 along +Y, and the two
    /// bend values push it sideways on X.
    ///
    /// A Catmull-like blend through 0, bend1, bend2, 0 rather than a straight lerp, so the ends
    /// stay on the axis and the middle carries the lean — a straight lerp between the two bends
    /// would leave the tail and head swinging off centre.
    /// </summary>
    public Vector2 SpineAt(float t)
    {
        t = Mathf.Clamp01(t);

        // Two humps, peaking at a third and two thirds, each fading to nothing at both ends.
        float h1 = Mathf.Sin(Mathf.Clamp01(t / 0.666f) * Mathf.PI);
        float h2 = Mathf.Sin(Mathf.Clamp01((t - 0.334f) / 0.666f) * Mathf.PI);
        return new Vector2(spineBend1 * h1 + spineBend2 * h2, t - 0.5f);
    }

    /// <summary>
    /// The spine's travel direction at t, for hanging limbs off perpendicular to it. Sampled
    /// across a short step rather than differentiated so a flat spine still returns +Y instead of
    /// a zero vector.
    /// </summary>
    public Vector2 SpineDirectionAt(float t)
    {
        const float h = 0.02f;
        Vector2 d = SpineAt(Mathf.Min(t + h, 1f)) - SpineAt(Mathf.Max(t - h, 0f));
        return d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.up;
    }

    /// <summary>Where limb <paramref name="i"/> grows from, 0 tail to 1 head.</summary>
    public float LimbAlong(int i)
    {
        int n = EffectiveLimbCount;
        if (n <= 0) return 0.5f;

        // Inset from both ends: a limb growing off the very tip of the spine reads as the spine
        // forking rather than as a limb.
        return Mathf.Lerp(0.15f, 0.85f, n == 1 ? 0.5f : i / (float)(n - 1));
    }

    /// <summary>
    /// Which side limb <paramref name="i"/> leaves from. Alternates, then Side Bias pulls limbs
    /// over to the positive side — at 1 they are all there, at 0 none are moved.
    /// </summary>
    public float LimbSide(int i)
    {
        float alternating = (i & 1) == 0 ? 1f : -1f;
        return Hash01(i * 7919 + 13) < Mathf.Clamp01(sideBias) ? 1f : alternating;
    }

    /// <summary>How far limb <paramref name="i"/> reaches, as a fraction of spine length.</summary>
    public float LimbLengthOf(int i)
    {
        float spread = 1f + (Hash01(i * 6271 + 41) - 0.5f) * 2f * Mathf.Clamp01(limbLengthSpread);
        return Mathf.Max(limbLength * Mathf.Clamp01(limbification) * spread, 0f);
    }

    public float SpineThicknessAt(float u) => Mathf.Lerp(spineThicknessRoot, spineThicknessTip, u);
    public float LimbThicknessAt(float u)  => Mathf.Lerp(limbThicknessRoot,  limbThicknessTip,  u);
    public float SquashAt(float u)         => Mathf.Max(Mathf.Lerp(squashRoot, squashTip, u), 0f);

    /// <summary>How many limbs point each way, so side bias can be judged at a glance.</summary>
    public void CountSides(out int left, out int right)
    {
        left = right = 0;
        int n = EffectiveLimbCount;
        for (int i = 0; i < n; i++)
        {
            if (LimbSide(i) < 0f) left++;
            else right++;
        }
    }

    // Stable 0..1 from an integer, so a set of properties always grows the same skeleton. The
    // per-mass variation is rolled at spawn instead, which is what stops one entry reading as the
    // same stamp repeated across the water.
    static float Hash01(int v)
    {
        uint x = (uint)v * 2654435761u;
        x ^= x >> 15; x *= 2246822519u; x ^= x >> 13;
        return (x & 0xFFFFFF) / 16777215f;
    }
}
