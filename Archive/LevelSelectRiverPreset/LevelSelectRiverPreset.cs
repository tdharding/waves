using UnityEngine;

/// <summary>
/// How the level select's rivers look, for one world — one section per effect drawn on the water.
///
/// The numbers live on an asset rather than on a material because the shaders read them as bare
/// $Globals: there is no property block behind them and nothing on disk remembers them, so a
/// preset plus a per-frame push is what gives them somewhere to live. Authored in
/// Tools > Waves > Level Select River Tuner, held by the Level Select Designer, and pushed by
/// <see cref="LevelSelectDesignerData.ApplyAesthetics"/>.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Level Select River Preset")]
public class LevelSelectRiverPreset : ScriptableObject
{
    [Tooltip("The lines drawn on the water — along a river's banks, and in rings out of a " +
             "pool's middle.")]
    public RiverEdgeRippleSettings edgeRipples = new RiverEdgeRippleSettings();

    [Tooltip("How the stone itself is shaded — dark along the seams of every run, white rising " +
             "off the waterline up the inside of the channel.")]
    public RiverRunShadingSettings runShading = new RiverRunShadingSettings();
}

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
    [Tooltip("How far in from the bank the ripples carry, in metres. Past this there are none. " +
             "A branch river's water is about 1.2m across, so much over half a metre has the two " +
             "banks meeting in the middle.")]
    [Min(0f)] public float reach = 0.45f;

    [Tooltip("How the ripples end. 0 eases them away so you cannot see where they stop; 1 holds " +
             "them most of the way in and then stops them on a visible crease.")]
    [Range(0f, 1f)] public float bevel = 0f;

    [Tooltip("Metres between one line and the next.")]
    [Min(0.001f)] public float spacing = 0.09f;

    [Tooltip("How white a line gets at its brightest. 0 turns the effect off entirely and gives " +
             "the water back exactly as it was. Above 1 is real overbright, for bloom to find.")]
    [Min(0f)] public float strength = 0.6f;

    [Tooltip("How fast the LINES THEMSELVES travel, in metres per second — a river's inward from " +
             "its banks, a pool's outward from its middle. Leave at 0 to hold them still and let " +
             "Flow below be the only movement, which is what the water is meant to read as.")]
    public float speed = 0f;

    [Tooltip("How much of its cycle a line fills. The line is a window rather than a sine, so " +
             "widening one lights more of the cycle without also brightening it.")]
    [Range(0.01f, 0.98f)] public float width = 0.35f;

    [Tooltip("0 gives a hard-edged line; 1 has it fall away from its middle.")]
    [Range(0f, 1f)] public float softness = 0.6f;

    [Tooltip("How far a line swings from side to side as it runs along the bank, measured in " +
             "line-widths. Every line shares the same swing, which is what keeps them one body " +
             "of water.")]
    public float waviness = 0.25f;

    [Tooltip("How often that swing repeats along the bank.")]
    public float wavinessScale = 6f;

    [Tooltip("Irregular drift on top of the swing, in line-widths, so the family never looks " +
             "stamped. This is the layer that stops the two banks reading as a mirror.")]
    public float meander = 0.35f;

    [Tooltip("How fine that drift is. Small numbers are long slow wanders.")]
    public float meanderScale = 1.5f;

    // ─────────────────────────────────────────────────────────────
    // MOVEMENT ALONG THE LINES
    //
    // The other movement, and the one meant to carry the water: the lines stand still and the
    // wander above travels ALONG them, so the water is distorting rather than drifting sideways.
    // ─────────────────────────────────────────────────────────────

    [Tooltip("How fast the distortion travels down a river's channel, in metres per second, " +
             "while the lines themselves stand still. Negative runs it upstream.")]
    public float flow = 0.12f;

    [Tooltip("How fast the distortion travels round a pool's rings, in turns per second. Turns " +
             "rather than metres so a ring spins as one piece — measured in metres its outside " +
             "would outrun its inside and tear it apart.")]
    public float poolSpin = 0.02f;

    // ─────────────────────────────────────────────────────────────
    // THE POOL
    // ─────────────────────────────────────────────────────────────

    [Tooltip("Rings coming out of the pool's middle. Off draws lines along the pool's edges " +
             "instead, the way a river's banks are drawn — which is what a pool used to get.")]
    public bool poolRings = true;

    [Tooltip("How far in from the water's edge a pool's rings come up to full, in metres. They " +
             "fill the whole bowl rather than hugging the wall, so this is only there to stop " +
             "them ending on a hard line where the water meets the wall or the island.")]
    [Min(0.001f)] public float poolFade = 0.06f;

    // ─────────────────────────────────────────────────────────────
    // SWITCHES
    //
    // Each is a different reading of the same surface, kept in the shader rather than baked into
    // the mesh so one can be put against the other without a Rebuild Runs in between.
    // ─────────────────────────────────────────────────────────────

    [Tooltip("Sample the drift in the line's OWN frame, so it travels exactly along the lines " +
             "and follows a channel round its bends. Off pins it to the world instead, where the " +
             "field is nailed to the map and never travels — the older reading.")]
    public bool driftAlongLines = true;

    [Tooltip("Correction. Turns a river's lines a quarter turn, so they run ACROSS the channel " +
             "instead of along its banks. Here to fix a frame that came out on the wrong axis " +
             "without rebuilding every mesh in the world. Pools are unaffected.")]
    public bool flipRiverLines = false;

    [Tooltip("How an overlap fades where one water laps over another. 0 is a straight ramp; 1 " +
             "eases it at both ends so neither lip can be seen.")]
    [Range(0f, 1f)] public float overlapSoftness = 1f;

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

    private static readonly int ReachId         = Shader.PropertyToID("_RiverEdgeRippleReach");
    private static readonly int BevelId         = Shader.PropertyToID("_RiverEdgeRippleBevel");
    private static readonly int SpacingId       = Shader.PropertyToID("_RiverEdgeRippleSpacing");
    private static readonly int StrengthId      = Shader.PropertyToID("_RiverEdgeRippleStrength");
    private static readonly int SpeedId         = Shader.PropertyToID("_RiverEdgeRippleSpeed");
    private static readonly int WidthId         = Shader.PropertyToID("_RiverEdgeRippleWidth");
    private static readonly int SoftnessId      = Shader.PropertyToID("_RiverEdgeRippleSoftness");
    private static readonly int WavinessId      = Shader.PropertyToID("_RiverEdgeRippleWaviness");
    private static readonly int WavinessScaleId = Shader.PropertyToID("_RiverEdgeRippleWavinessScale");
    private static readonly int MeanderId       = Shader.PropertyToID("_RiverEdgeRippleMeander");
    private static readonly int MeanderScaleId  = Shader.PropertyToID("_RiverEdgeRippleMeanderScale");
    private static readonly int FlowId          = Shader.PropertyToID("_RiverEdgeRippleFlow");
    private static readonly int PoolSpinId      = Shader.PropertyToID("_RiverEdgeRipplePoolSpin");
    private static readonly int PoolFadeId      = Shader.PropertyToID("_RiverEdgeRipplePoolFade");
    private static readonly int PoolRingsId     = Shader.PropertyToID("_RiverEdgeRipplePoolRings");
    private static readonly int FlowSpaceId     = Shader.PropertyToID("_RiverEdgeRippleFlowSpace");
    private static readonly int FlipId          = Shader.PropertyToID("_RiverEdgeRippleFlip");
    private static readonly int OverlapSoftId   = Shader.PropertyToID("_RiverEdgeRippleOverlapSoft");
    private static readonly int DebugId         = Shader.PropertyToID("_RiverEdgeRippleDebug");

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
        Shader.SetGlobalFloat(ReachId,         reach);
        Shader.SetGlobalFloat(BevelId,         bevel);
        Shader.SetGlobalFloat(SpacingId,       spacing);
        Shader.SetGlobalFloat(StrengthId,      strength);
        Shader.SetGlobalFloat(SpeedId,         speed);
        Shader.SetGlobalFloat(WidthId,         width);
        Shader.SetGlobalFloat(SoftnessId,      softness);
        Shader.SetGlobalFloat(WavinessId,      waviness);
        Shader.SetGlobalFloat(WavinessScaleId, wavinessScale);
        Shader.SetGlobalFloat(MeanderId,       meander);
        Shader.SetGlobalFloat(MeanderScaleId,  meanderScale);

        Shader.SetGlobalFloat(FlowId,          flow);
        Shader.SetGlobalFloat(PoolSpinId,      poolSpin);
        Shader.SetGlobalFloat(PoolFadeId,      poolFade);

        // The switches go over as 1 and 0. There is no bool global to set, and a float is what
        // the shader compares against a half anyway.
        Shader.SetGlobalFloat(PoolRingsId,     poolRings       ? 1f : 0f);
        Shader.SetGlobalFloat(FlowSpaceId,     driftAlongLines ? 1f : 0f);
        Shader.SetGlobalFloat(FlipId,          flipRiverLines  ? 1f : 0f);
        Shader.SetGlobalFloat(OverlapSoftId,   overlapSoftness);
    }

    /// <summary>Field-by-field copy, so the tuner can load a preset without holding on to it.</summary>
    public void CopyFrom(RiverEdgeRippleSettings other)
    {
        if (other == null) return;

        reach         = other.reach;
        bevel         = other.bevel;
        spacing       = other.spacing;
        strength      = other.strength;
        speed         = other.speed;
        width         = other.width;
        softness      = other.softness;
        waviness      = other.waviness;
        wavinessScale = other.wavinessScale;
        meander       = other.meander;
        meanderScale  = other.meanderScale;

        flow            = other.flow;
        poolSpin        = other.poolSpin;
        poolRings       = other.poolRings;
        poolFade        = other.poolFade;
        driftAlongLines = other.driftAlongLines;
        flipRiverLines  = other.flipRiverLines;
        overlapSoftness = other.overlapSoftness;
    }
}
