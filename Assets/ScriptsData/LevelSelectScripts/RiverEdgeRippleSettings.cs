using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// What the water is drawn as while the debug view is on: the frame the lines were generated in,
/// rather than the lines themselves. There to answer one question — which way is this surface's
/// frame lying — so a river whose lines came out along the wrong axis can be seen rather than
/// guessed at.
///
/// Nothing shows until the subgraph's Debug and DebugMix outputs are wired into the water graph
/// (Lerp BaseColor towards Debug by DebugMix). Off for anything but tuning.
/// </summary>
public enum RiverRippleDebugView
{
    /// <summary>The water as it really looks.</summary>
    Off = 0,

    /// <summary>The coordinate the lines are cut across — stripes down a river's banks, rings
    /// round a pool.</summary>
    Bands = 1,

    /// <summary>The coordinate the lines run along, which is the way the distortion travels.
    /// Its stripes cross the bands at a right angle.</summary>
    Along = 2,

    /// <summary>How much of the surface is really there: white in the body, dark where one water
    /// laps over another and fades out.</summary>
    Fade = 3,

    /// <summary>Both coordinates at once, as a check — the squares should sit square to the
    /// river, or turn with a pool.</summary>
    Both = 4,
}

/// <summary>
/// One set of edge ripple numbers, and the only place they are written into the shaders.
///
/// Both the tuner and the designer push through <see cref="Push"/>, so there is exactly one route
/// into the globals and no second copy of the property IDs to fall out of step.
/// </summary>
[System.Serializable]
public class RiverEdgeRippleSettings
{
    [Header("Lines")]

    [Tooltip("How white a line gets at its brightest, on rivers and pools alike — the one thing " +
             "about a line the two waters still share, so they cannot drift apart in brightness. " +
             "0 turns the effect off entirely and gives the water back exactly as it was. Above " +
             "1 is real overbright, for bloom to find.")]
    [Min(0f)] public float strength = 0.6f;

    [Tooltip("How much of its cycle a RIVER line fills. The line is a window rather than a sine, " +
             "so widening one lights more of the cycle without also brightening it. A pool's " +
             "rings have their own, below.")]
    [Range(0.01f, 0.98f)] public float width = 0.35f;

    [Tooltip("0 gives a hard-edged RIVER line; 1 has it fall away from its middle. A pool's " +
             "rings have their own, below.")]
    [Range(0f, 1f)] public float softness = 0.6f;

    [Header("Rivers")]

    [Tooltip("Metres between one river line and the next.")]
    [Min(0.001f)] public float spacing = 0.09f;

    [Tooltip("How fast a river's LINES THEMSELVES travel inward from its banks, in metres per " +
             "second. Leave at 0 to hold them still and let Distortion Speed be the only movement.")]
    public float speed = 0f;

    [Tooltip("How far in from the bank the ripples carry, as a percentage of the river's width " +
             "at that spot — so a wide river's lines carry further in than a narrow branch's, each " +
             "in proportion to itself. 50 has the two banks meeting in the middle. Past this " +
             "there are none.")]
    [Range(0f, 100f)] public float reachPercent = 38f;

    [Tooltip("How the ripples end. 0 eases them away so you cannot see where they stop; 1 holds " +
             "them most of the way in and then stops them on a visible crease.")]
    [Range(0f, 1f)] public float bevel = 0f;

    [Tooltip("How far the noise pushes a line back and forth, in line-widths: 0.1 shifts a line " +
             "by a tenth of the spacing.")]
    public float distortStrength = 0.35f;

    [Tooltip("How fine the distortion's noise is. Small numbers are long slow wanders.")]
    public float distortScale = 1.5f;

    [Tooltip("How fast the distortion travels down a river's channel, in metres per second, " +
             "while the lines themselves stand still. Negative runs it upstream.")]
    [FormerlySerializedAs("flow")] public float distortionSpeed = 0.12f;

    [Tooltip("Sample the distortion in the line's OWN frame, so it travels exactly along the " +
             "lines and follows a channel round its bends. Off pins it to the world instead.")]
    public bool driftAlongLines = true;

    [Header("Rivers — second distortion")]

    [Tooltip("A SECOND wander, laid over everything above: the lines are already spaced, already " +
             "pushed about by the first noise, and this pushes what is left of them again. Its " +
             "own field, offset from the first, so the two never line up and repeat. In " +
             "line-widths, like the first. 0 leaves the water exactly as the sliders above " +
             "left it.")]
    public float secondDistortStrength = 0f;

    [Tooltip("How fine the second wander's noise is. Set it well away from the first Distort " +
             "Scale — much coarser for a slow drift under the first, much finer for a chop on " +
             "top of it.")]
    public float secondDistortScale = 6f;

    [Tooltip("How fast the second wander travels down a river's channel, in metres per second. " +
             "Negative runs it upstream — against the first, the two beat against each other " +
             "instead of travelling together.")]
    public float secondDistortionSpeed = 0f;

    [Tooltip("Where a branch's water runs back over the river it leaves, its lines take on that " +
             "river's Reach (and Bevel) — held along its banks, gone from its middle — easing over " +
             "across this many metres past that river's waterline. Needs Rebuild Runs once. 0 " +
             "leaves the branch's own lines running straight out across it.")]
    [Min(0f)] public float branchMouthFade = 0f;

    [Tooltip("How far back OUT in the branch that change-over starts, in metres. 0 starts it on " +
             "the waterline of the river it leaves. Needs Rebuild Runs once.")]
    [Min(0f)] public float branchMouthInset = 0f;

    [Header("Pools")]

    [Tooltip("Metres between one pool ring and the next.")]
    [Min(0.001f)] public float poolSpacing = 0.09f;

    [Tooltip("How much of its cycle a pool RING fills — the rings' own, separate from the river " +
             "lines' Width, so a pool can carry fatter or thinner lines than the rivers running " +
             "out of it.")]
    [Range(0.01f, 0.98f)] public float poolWidth = 0.35f;

    [Tooltip("0 gives a hard-edged ring; 1 has it fall away from its middle. The rings' own, " +
             "separate from the river lines' Softness.")]
    [Range(0f, 1f)] public float poolSoftness = 0.6f;

    [Tooltip("How far in from the pool's waterline (and its island's) the rings carry, in " +
             "metres. Past this there are none. Set it past the pool's radius to fill the pool.")]
    [Min(0f)] public float poolReach = 100f;

    [Tooltip("How fast a pool's rings travel outward from its middle, in metres per second. " +
             "Negative draws them in. 0 holds them still.")]
    public float poolSpeed = 0f;

    [Tooltip("How far the noise pushes a ring in and out, in ring-widths. The field is fixed in " +
             "the world, like the rock rings' distortion.")]
    public float poolDistortStrength = 0.35f;

    [Tooltip("How fine the pool distortion's noise is, in world space. Small numbers are long " +
             "slow wanders.")]
    public float poolDistortScale = 1.5f;

    [Tooltip("How fast a pool's distortion spins round the pool's centre, in turns per second, " +
             "while the rings themselves stand still. Negative spins it the other way.")]
    public float poolDistortionSpeed = 0.02f;

    [Tooltip("A SECOND wander on the rings, laid over the one above: the rings are already " +
             "spaced and already pushed about, and this pushes what is left of them again. Its " +
             "own field, offset from the first, so the two never line up and repeat. In " +
             "ring-widths, like the first. 0 leaves the pools exactly as the sliders above " +
             "left them.")]
    public float poolSecondDistortStrength = 0f;

    [Tooltip("How fine the second wander's noise is, in world space. Set it well away from Pool " +
             "Distort Scale — much coarser for a slow drift under the first, much finer for a " +
             "chop on top of it.")]
    public float poolSecondDistortScale = 6f;

    [Tooltip("How fast the second wander spins round the pool's centre, in turns per second. " +
             "Negative spins it the other way — against the first, the two beat against each " +
             "other instead of turning together.")]
    public float poolSecondDistortionSpeed = 0f;

    [Tooltip("Where a pool's water runs out down a river, its rings take on that river's Reach " +
             "(and Bevel) — held along the banks, gone from the middle — easing over across this " +
             "many metres past the pool's circle. Needs Rebuild Runs once. 0 leaves the rings at " +
             "full all the way down.")]
    [Min(0f)] public float poolMouthFade = 0f;

    [Tooltip("How far INSIDE the pool's circle that change-over to a river's Reach starts, in " +
             "metres. 0 starts it on the circle. Needs Rebuild Runs once.")]
    [FormerlySerializedAs("poolMouthInset")]
    [Min(0f)] public float poolMouthReachOverlap = 0f;

    [Tooltip("How far past the pool's waterline its rings carry on out into a river, in metres, " +
             "fading out over that distance. Separate from Reach — this ends the rings outright. " +
             "The water itself stays solid. 0 leaves them running all the way down.")]
    [Min(0f)] public float poolMouthExtent = 0f;

    [Header("River fade to pool")]

    [Tooltip("How far back from the far lip of a pool's lap the alpha fades, in metres. Held to " +
             "the lap's own length — Water Pool Overlap in the designer — so it can never outrun " +
             "the lap. Retunes without a Rebuild Runs.")]
    [FormerlySerializedAs("lapFadeDistance")]
    [Min(0f)] public float riverFadeToPoolDistance = 0.25f;

    [Tooltip("The falloff of that fade over its distance. 1 is an even ramp. Above 1 the water " +
             "stays faint further in from the lip and comes in late and hard; below 1 it comes " +
             "in quickly and eases the rest of the way.")]
    [Min(0.01f)] public float riverFadeToPoolIntensity = 1f;

    [Header("Grain")]

    [Tooltip("How far the grain pushes the water's colour lighter and darker. 0 is no grain.")]
    [Min(0f)] public float grainStrength = 0f;

    [Tooltip("Metres across one grain. Pinned to the world, so it runs straight across rivers, " +
             "pools and the joins between them, and it does not move.")]
    [Min(0.0001f)] public float grainSize = 0.01f;

    [Tooltip("How far the same grain thins the water's alpha where it runs dark. 0 leaves the " +
             "alpha alone; 1 takes the darkest of the grain fully clear. Separate from Grain " +
             "Strength, so the alpha can take the grain with or without the colour.")]
    [Range(0f, 1f)] public float grainAlpha = 0f;

    // ─────────────────────────────────────────────────────────────
    // PUSHING
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The tuner, while it is driving. Set by the Level Select River Tuner with Apply Live on,
    /// and cleared when it closes or the toggle goes off.
    ///
    /// It has to win over the designer's preset rather than sit alongside it: the aesthetics pump
    /// re-pushes that preset every editor tick, so without this a dragged slider would be dragged
    /// straight back to the saved number before the next repaint.
    /// </summary>
    public static RiverEdgeRippleSettings Live;

    /// <summary>No ripples at all — what a world with no preset assigned gets.</summary>
    private static readonly RiverEdgeRippleSettings None =
        new RiverEdgeRippleSettings { strength = 0f };

    private static readonly int SpacingId             = Shader.PropertyToID("_RiverEdgeRippleSpacing");
    private static readonly int StrengthId            = Shader.PropertyToID("_RiverEdgeRippleStrength");
    private static readonly int SpeedId               = Shader.PropertyToID("_RiverEdgeRippleSpeed");
    private static readonly int WidthId               = Shader.PropertyToID("_RiverEdgeRippleWidth");
    private static readonly int SoftnessId            = Shader.PropertyToID("_RiverEdgeRippleSoftness");
    private static readonly int ReachId               = Shader.PropertyToID("_RiverEdgeRippleReach");
    private static readonly int BevelId               = Shader.PropertyToID("_RiverEdgeRippleBevel");
    private static readonly int DistortStrengthId     = Shader.PropertyToID("_RiverEdgeRippleDistortStrength");
    private static readonly int DistortScaleId        = Shader.PropertyToID("_RiverEdgeRippleDistortScale");
    private static readonly int DistortSpeedId        = Shader.PropertyToID("_RiverEdgeRippleDistortSpeed");
    private static readonly int FlowSpaceId           = Shader.PropertyToID("_RiverEdgeRippleFlowSpace");
    private static readonly int SecondDistortStrengthId = Shader.PropertyToID("_RiverEdgeRippleSecondDistortStrength");
    private static readonly int SecondDistortScaleId    = Shader.PropertyToID("_RiverEdgeRippleSecondDistortScale");
    private static readonly int SecondDistortSpeedId    = Shader.PropertyToID("_RiverEdgeRippleSecondDistortSpeed");
    private static readonly int BranchMouthFadeId     = Shader.PropertyToID("_RiverEdgeRippleBranchMouthFade");
    private static readonly int BranchMouthInsetId    = Shader.PropertyToID("_RiverEdgeRippleBranchMouthInset");
    private static readonly int PoolSpacingId         = Shader.PropertyToID("_RiverEdgeRipplePoolSpacing");
    private static readonly int PoolReachId           = Shader.PropertyToID("_RiverEdgeRipplePoolReach");
    private static readonly int PoolSpeedId           = Shader.PropertyToID("_RiverEdgeRipplePoolSpeed");
    private static readonly int PoolDistortStrengthId = Shader.PropertyToID("_RiverEdgeRipplePoolDistortStrength");
    private static readonly int PoolDistortScaleId    = Shader.PropertyToID("_RiverEdgeRipplePoolDistortScale");
    private static readonly int PoolDistortSpeedId    = Shader.PropertyToID("_RiverEdgeRipplePoolDistortSpeed");
    private static readonly int PoolWidthId           = Shader.PropertyToID("_RiverEdgeRipplePoolWidth");
    private static readonly int PoolSoftnessId        = Shader.PropertyToID("_RiverEdgeRipplePoolSoftness");
    private static readonly int PoolSecondDistortStrengthId = Shader.PropertyToID("_RiverEdgeRipplePoolSecondDistortStrength");
    private static readonly int PoolSecondDistortScaleId    = Shader.PropertyToID("_RiverEdgeRipplePoolSecondDistortScale");
    private static readonly int PoolSecondDistortSpeedId    = Shader.PropertyToID("_RiverEdgeRipplePoolSecondDistortSpeed");
    private static readonly int PoolMouthFadeId       = Shader.PropertyToID("_RiverEdgeRipplePoolMouthFade");
    private static readonly int PoolMouthReachOverlapId = Shader.PropertyToID("_RiverEdgeRipplePoolMouthReachOverlap");
    private static readonly int PoolMouthExtentId     = Shader.PropertyToID("_RiverEdgeRipplePoolMouthExtent");
    private static readonly int RiverFadeToPoolDistanceId  = Shader.PropertyToID("_RiverEdgeRippleRiverFadeToPoolDistance");
    private static readonly int RiverFadeToPoolIntensityId = Shader.PropertyToID("_RiverEdgeRippleRiverFadeToPoolIntensity");
    private static readonly int GrainStrengthId       = Shader.PropertyToID("_RiverEdgeRippleGrainStrength");
    private static readonly int GrainSizeId           = Shader.PropertyToID("_RiverEdgeRippleGrainSize");
    private static readonly int GrainAlphaId          = Shader.PropertyToID("_RiverEdgeRippleGrainAlpha");
    private static readonly int DebugId               = Shader.PropertyToID("_RiverEdgeRippleDebug");

    /// <summary>
    /// Publishes one set of ripple numbers to the shaders — the tuner's if it is driving, this
    /// world's otherwise, and none at all if the world has no preset.
    ///
    /// Called every frame rather than once. These are bare globals with nothing behind them, so a
    /// set-once push is undone by the next material or shader reimport and never comes back.
    /// </summary>
    public static void Push(RiverEdgeRippleSettings settings)
    {
        (Live ?? settings ?? None).Apply();
    }

    /// <summary>
    /// The debug view, which belongs to whoever is LOOKING rather than to a preset — it is a way
    /// of reading the water, not a way the water is meant to look, and nothing should be able to
    /// save it on by accident. The world holds one on its designer data; the tuner parks its own
    /// in <see cref="LiveDebug"/> while its window is open, and that wins the same way Live does.
    ///
    /// Kept out of <see cref="Apply"/> so there is still exactly one write of the property, with
    /// both routes going through here.
    /// </summary>
    public static RiverRippleDebugView? LiveDebug;

    public static void PushDebug(RiverRippleDebugView view)
    {
        Shader.SetGlobalFloat(DebugId, (float)(LiveDebug ?? view));
    }

    private void Apply()
    {
        Shader.SetGlobalFloat(SpacingId,             spacing);
        Shader.SetGlobalFloat(StrengthId,            strength);
        Shader.SetGlobalFloat(SpeedId,               speed);
        Shader.SetGlobalFloat(WidthId,               width);
        Shader.SetGlobalFloat(SoftnessId,            softness);

        Shader.SetGlobalFloat(ReachId,               reachPercent);
        Shader.SetGlobalFloat(BevelId,               bevel);
        Shader.SetGlobalFloat(DistortStrengthId,     distortStrength);
        Shader.SetGlobalFloat(DistortScaleId,        distortScale);
        Shader.SetGlobalFloat(DistortSpeedId,        distortionSpeed);

        // The switch goes over as 1 and 0. There is no bool global to set, and a float is what
        // the shader compares against a half anyway.
        Shader.SetGlobalFloat(FlowSpaceId,           driftAlongLines ? 1f : 0f);

        Shader.SetGlobalFloat(SecondDistortStrengthId, secondDistortStrength);
        Shader.SetGlobalFloat(SecondDistortScaleId,    secondDistortScale);
        Shader.SetGlobalFloat(SecondDistortSpeedId,    secondDistortionSpeed);
        Shader.SetGlobalFloat(BranchMouthFadeId,     branchMouthFade);
        Shader.SetGlobalFloat(BranchMouthInsetId,    branchMouthInset);

        Shader.SetGlobalFloat(PoolSpacingId,         poolSpacing);
        Shader.SetGlobalFloat(PoolReachId,           poolReach);
        Shader.SetGlobalFloat(PoolSpeedId,           poolSpeed);
        Shader.SetGlobalFloat(PoolDistortStrengthId, poolDistortStrength);
        Shader.SetGlobalFloat(PoolDistortScaleId,    poolDistortScale);
        Shader.SetGlobalFloat(PoolDistortSpeedId,    poolDistortionSpeed);
        Shader.SetGlobalFloat(PoolWidthId,           poolWidth);
        Shader.SetGlobalFloat(PoolSoftnessId,        poolSoftness);
        Shader.SetGlobalFloat(PoolSecondDistortStrengthId, poolSecondDistortStrength);
        Shader.SetGlobalFloat(PoolSecondDistortScaleId,    poolSecondDistortScale);
        Shader.SetGlobalFloat(PoolSecondDistortSpeedId,    poolSecondDistortionSpeed);
        Shader.SetGlobalFloat(PoolMouthFadeId,       poolMouthFade);
        Shader.SetGlobalFloat(PoolMouthReachOverlapId, poolMouthReachOverlap);
        Shader.SetGlobalFloat(PoolMouthExtentId,     poolMouthExtent);

        Shader.SetGlobalFloat(RiverFadeToPoolDistanceId,  riverFadeToPoolDistance);
        Shader.SetGlobalFloat(RiverFadeToPoolIntensityId, riverFadeToPoolIntensity);

        Shader.SetGlobalFloat(GrainStrengthId,       grainStrength);
        Shader.SetGlobalFloat(GrainSizeId,           grainSize);
        Shader.SetGlobalFloat(GrainAlphaId,          grainAlpha);
    }

    /// <summary>Field-by-field copy, so the tuner can load a preset without holding on to it.</summary>
    public void CopyFrom(RiverEdgeRippleSettings other)
    {
        if (other == null) return;

        spacing  = other.spacing;
        strength = other.strength;
        speed    = other.speed;
        width    = other.width;
        softness = other.softness;

        reachPercent    = other.reachPercent;
        bevel           = other.bevel;
        distortStrength = other.distortStrength;
        distortScale    = other.distortScale;
        distortionSpeed = other.distortionSpeed;
        driftAlongLines = other.driftAlongLines;

        secondDistortStrength = other.secondDistortStrength;
        secondDistortScale    = other.secondDistortScale;
        secondDistortionSpeed = other.secondDistortionSpeed;
        branchMouthFade  = other.branchMouthFade;
        branchMouthInset = other.branchMouthInset;

        poolSpacing         = other.poolSpacing;
        poolReach           = other.poolReach;
        poolSpeed           = other.poolSpeed;
        poolDistortStrength = other.poolDistortStrength;
        poolDistortScale    = other.poolDistortScale;
        poolDistortionSpeed = other.poolDistortionSpeed;
        poolWidth           = other.poolWidth;
        poolSoftness        = other.poolSoftness;
        poolSecondDistortStrength = other.poolSecondDistortStrength;
        poolSecondDistortScale    = other.poolSecondDistortScale;
        poolSecondDistortionSpeed = other.poolSecondDistortionSpeed;
        poolMouthFade       = other.poolMouthFade;
        poolMouthReachOverlap = other.poolMouthReachOverlap;
        poolMouthExtent     = other.poolMouthExtent;

        riverFadeToPoolDistance  = other.riverFadeToPoolDistance;
        riverFadeToPoolIntensity = other.riverFadeToPoolIntensity;

        grainStrength = other.grainStrength;
        grainSize     = other.grainSize;
        grainAlpha    = other.grainAlpha;
    }
}
