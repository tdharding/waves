using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// A boat-centred fog simulation, authored per level as a weather feature. NOT a map of a level.
///
/// An authored RECIPE - which blobs, how many, how far apart - is distributed into an arrangement
/// that repeats outward from wherever the boat is, so open water is never empty, and the wind
/// carries it past you. Referenced from GridData beside sonarGridType, which is what leaves room
/// for triggers to swap or vary a map by location later.
///
/// Placing every mass by hand came first and was replaced. Hand-placing says where one particular
/// mass goes, when what a weather feature wants said is how thick the fog is and how far apart it
/// clumps. The map is STILL the only authority on where fog is; it now exercises that authority
/// through a distribution rather than through a list of points.
///
/// THIS IS THE ONLY THING THAT DECIDES WHERE FOG IS. Rocks, the boat and lit street lights push
/// fog aside; none of them place it.
///
/// What this deliberately gives up: fog does not belong to particular places on a level, and
/// cannot — once masses drift on a wind they cannot also stay where they were put. Weather, not
/// terrain.
///
/// Generated positions are normalised 0..1 across one repetition, so a map survives being
/// rescaled, and are deterministic from the seed - the same map always lays out the same way.
/// </summary>
[CreateAssetMenu(fileName = "FogMap", menuName = "Waves/Fog Arena Map")]
public class FogMap : ScriptableObject
{
    [Header("Blob")]
    [Tooltip("What every mass on this arena is made of. One set of properties, not a list: a " +
             "list bought variety that the per-mass jitter and the size range already provide, " +
             "at the cost of never being able to see what you were editing.")]
    public FogProperties properties = FogProperties.Default;

    [Header("Distribution")]
    [Tooltip("How many masses to keep alive around the boat. With Spacing this IS the density: " +
             "how many, and how close they may get. Capped by Blob Budget, which is the " +
             "performance ceiling rather than an artistic choice.")]
    [Range(1, 80)] public int blobCount = 12;

    [Tooltip("Closest two masses may be born to each other, IN WORLD UNITS. It used to be a " +
             "fraction of the tile and was measured between slots on a lattice rather than " +
             "between masses, which made it near enough inert. Now it is what it says: a mass is " +
             "not placed within this of a living one. Set it wider than the water can hold and " +
             "the count simply falls short.")]
    [Range(0.05f, 8f)] public float spacing = 1f;


    [Header("Scale")]
    [Tooltip("Shortest spine a mass is grown at, IN WORLD UNITS. This was a fraction of a tile " +
             "while an arrangement repeated across one; nothing tiles any more, so a mass is " +
             "simply the size it is. Read it against Spacing — sizes near the spacing give a " +
             "continuous bank, sizes well under it give separate puffs.")]
    [Range(0.05f, 20f)] public float blobScaleMin = 1.2f;

    [Tooltip("Longest spine a mass is grown at, in world units.")]
    [Range(0.05f, 20f)] public float blobScaleMax = 2f;

    public Vector2 blobScale
    {
        get => new Vector2(blobScaleMin, blobScaleMax);
        set { blobScaleMin = value.x; blobScaleMax = value.y; }
    }

    /// <summary>Spine length in world units, low and high, at this map's tile size.</summary>
    /// <summary>
    /// Spine length in world units, low to high. Straight through now: these ARE world units, and
    /// the tile they used to be fractions of no longer exists.
    /// </summary>
    public Vector2 WorldBlobScale => blobScale;

    [Tooltip("Overall opacity of the whole fog sheet. Separate from Interior Fill, which says " +
             "how solid a mass reads from its middle to its edge — this turns all of it up and " +
             "down together.")]
    [Range(0f, 1f)] public float fogOpacity = 1f;

    [Header("Boat Mask")]
    [Tooltip("How far from the boat fog is drawn at all. Beyond this a mass paints nothing, so a " +
             "map can allocate fog across open water and only the near ones cost anything.")]
    public float maskRadius = 24f;

    [Tooltip("Fraction of that radius a mass stays at full strength before thinning. 0.75 makes " +
             "the outer quarter a fade band, so fog thins away as you sail off rather than " +
             "vanishing at a line.")]
    [Range(0f, 1f)] public float maskFeather = 0.75f;




    [Header("Wind")]
    [Tooltip("Which way the fog travels, in degrees. 0 is +X, 90 is +Z. This is the wind for the " +
             "whole level: masses drift along it, and it is the direction new ones arrive from.")]
    [Range(0f, 360f)] public float windAngle = 20f;

    [Tooltip("How fast, in world units per second. Slow — a mass crossing the field in a couple of " +
             "minutes reads as weather; much faster reads as smoke.")]
    public float windSpeed = 0.4f;

    /// <summary>Wind as a world XZ vector, which is what the simulation actually drifts along.</summary>
    public Vector2 WindVector
    {
        get
        {
            float a = windAngle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * windSpeed;
        }
    }

    // ── Simulation ───────────────────────────────────────────────────────────
    // The rig's own numbers, authored here with everything else. They were manager-only, which
    // meant half the fog was a preset and half was whatever the scene's manager happened to be
    // set to — so two levels could not actually differ in how heavy or how fine their fog was.
    [Header("Simulation")]
    [Tooltip("How fine the fog is, in WORLD UNITS PER PIXEL of the painted texture. This is the " +
             "detail dial: lower is crisper and costs more. Resolution is derived from it, so " +
             "widening the Mask grows the texture instead of coarsening the fog inside it.")]
    [Range(0.005f, 0.3f)] public float unitsPerTexel = 0.053f;

    [Tooltip("Ceiling on the derived resolution, in pixels across. Detail stops improving once " +
             "this is reached — which is the honest failure, because the alternative is a mask " +
             "widened past what the machine can paint quietly costing you frames.")]
    public int maxGridResolution = 1024;


    [Tooltip("Frame-to-frame stickiness. High and the fog is thick and sluggish; low and it is " +
             "wispy and quick. Also does part of the smoothing, so a high value buys back blur.")]
    [Range(0f, 0.98f)] public float heaviness = 0.88f;

    [Tooltip("Blur width in WORLD UNITS. Wider fuses separate dots harder and rounds the outline " + "off. Independent of resolution, so nothing else can change it.")]
    [Range(0.002f, 1f)] public float blurRadius = 0.09f;

    [Tooltip("Blur width for the height map, in its own pixels. Not optional: an unblurred union " +
             "of domes shows every BaseDot as a bump, and a limb comes out corrugated however " +
             "clean its outline is.")]
    [Range(0.002f, 2f)] public float heightBlurRadius = 0.2f;

    [Tooltip("Where masses are BORN, in world units. That is all it does.")]
    public float spawnRadius = 4.2f;

    [Tooltip("Where masses are DELETED, in world units. That is all it does.")]
    public float cullRadius = 4.7f;

    // ── Pushing ──────────────────────────────────────────────────────────────
    [Header("Pushing")]
    [Tooltip("Global multiplier on every repeller's own strength. Drop it to let fog crowd in " +
             "closer to everything at once without editing each rock.")]
    [Range(0f, 1f)] public float repelStrength = 1f;


    [Tooltip("Clear water kept beyond a rock's own waterline radius.")]
    [FormerlySerializedAs("rockStandoff")]
    public float rockClearRadius = 0.34f;

    [Tooltip("How hard rocks push. Rocks are firm — fog wraps close and stays out.")]
    [Range(0f, 1f)] public float rockStrength = 1f;

    [Tooltip("Seconds between rescans for rocks. Levels spawn their spikes, so this cannot be a " +
             "one-off at startup, but it need not run often either.")]
    public float rockRescanInterval = 2f;

    [Tooltip("Fraction of a lamp's light radius that fog is held out of. Keep it well under 1: " +
             "push fog out as far as the light reaches and it never enters the region it would " +
             "have been lit in, leaving a dark hole ringed by unlit fog.")]
    [Range(0.05f, 0.8f)] public float lampClearFraction = 0.35f;

    [Tooltip("Clear water kept beyond that, on top of it.")]
    [FormerlySerializedAs("lampStandoff")]
    public float lampClearRadius = 0.34f;

    [Tooltip("How hard a lit lamp pushes fog out. Higher than a rock's — a lamp is burning fog " +
             "off, not just standing in its way.")]
    [Range(0f, 1f)] public float lampStrength = 1f;

    // ── Look ──────────────────────────────────────────────────────────────
    // Following SonarGridType, which carries its own plane material alongside its formation: the
    // preset that decides what appears also decides how it looks, so a level is one asset to open
    // rather than a hunt across a material, a manager and a wave preset.
    //
    // Pushed onto the material EVERY FRAME, like every other value on this map. They used to go
    // only at level start and on Refresh Preview, on the theory that the material was the live
    // surface while tuning — but that left the map and the material disagreeing until a button was
    // pressed, so dragging a value here appeared to do nothing. One live source is less confusing
    // than two, even if it means the material is no longer the place to tune.
    //
    // Use Pull From Material to bring numbers the other way once, when the material is ahead.

    [Header("Material")]
    [Tooltip("The fog sheet material for this level. Leave null to use whatever the sheet already " +
             "has, which is the right answer when several levels share one look.")]

    public Material fogMaterial;

    [Tooltip("Off leaves the material exactly as authored and ignores everything below — useful " +
             "while tuning the material directly.")]
    public bool overrideLook = false;

    [Header("Shape")]
    [Tooltip("How dense the field must be before fog appears. THIS is what creates the outline: " +
             "raise it and limbs are eaten from the tips inward. 0.20-0.30 keeps the blunt limb " +
             "tips the reference sketches have.")]
    [Range(0.05f, 0.9f)] public float threshold = 0.26f;

    [Tooltip("Hard ink outline versus a soft feathered fade.")]
    [Range(0.002f, 0.2f)] public float edgeSoftness = 0.03f;

    [Tooltip("How far the outline wanders off the smooth shape. Cheaper than adding limbs for the " +
             "fine waviness.")]
    [Range(0f, 0.4f)] public float undulationAmount = 0.10f;

    [Tooltip("Long slow waves versus tight ripples.")]
    [Range(0.05f, 6f)] public float undulationScale = 1.2f;

    [Header("Lip")]
    [Range(0.005f, 0.3f)] public float lipWidth = 0.05f;

    [Tooltip("How strongly the rim catches street lights.")]
    [Range(0f, 4f)] public float lipLighting = 1.6f;

    [Tooltip("Small reads as a dramatic domed lip, large as nearly flat.")]
    [Range(0.05f, 4f)] public float lipCurvature = 0.55f;

    [Tooltip("Exaggerates the relief without touching the height map itself.")]
    [Range(0.1f, 6f)] public float heightScale = 1f;

    [Header("Body")]
    public Color fogColour = new Color(0.42f, 0.50f, 0.62f, 1f);
    public Color litColour = new Color(0.88f, 0.92f, 1.00f, 1f);

    [Tooltip("Baseline shading with no lamp near. This game has no sun, so at 0 fog away from " +
             "every lit street light renders completely flat with no volume at all.")]
    [Range(0f, 1f)] public float ambient = 0.18f;

    [Range(0f, 1f)] public float interiorFill = 0.75f;

    [Tooltip("Width of the see-through band along the edge, as a fraction of the body. Small hugs the outline; large bleeds the softness inward. Not a curve over the whole mass.")]
    [Range(0.02f, 1.5f)] public float transparencyFalloff = 0.25f;

    [Header("Grain")]
    [Tooltip("How strongly the grain lightens and darkens the fog. Past 1 it starts cutting holes " +
             "rather than mottling, which is a look in itself — the shader floors it at black so " +
             "it never inverts.")]
    [Range(0f, 2f)] public float grainAmount = 0.18f;

    [Tooltip("Grain frequency, in WORLD units. This world is small, so the useful numbers are far " +
             "higher than they look — the same reason the wander scale wants a big value. Low " +
             "hundreds gives a fine tooth; under ten is broad cloudy blotching.")]
    [Range(0.5f, 400f)] public float grainScale = 12f;

    // ── Boat ─────────────────────────────────────────────────────────────────
    // Here rather than on the boat, for the same reason the rock and lamp numbers live on the fog
    // side: how fog behaves around something is a property of the WEATHER, not of the thing it is
    // avoiding. Per-arena rather than global because it genuinely differs by arena — thin haze
    // barely parts for a hull, a thick bank shoulders well clear of it.
    [Header("Boat")]
    [Tooltip("The hull's own radius at the waterline: the circle fog does not enter at all.")]
    public float boatRepelRadius = 1f;

    [Tooltip("Clear water kept beyond that radius. Note it does not simply add a gap — the mass is " +
             "stretched around a bigger circle and thins, so raise body thickness alongside it.")]
    [FormerlySerializedAs("boatRepelStandoff")]
    public float boatRepelClearRadius = 0.55f;

    [Tooltip("1 pins fog exactly on the clear radius. Lower lets it press in and recover, which " +
             "is what suits something moving — around 0.6 reads right for a boat. 0 turns the " +
             "boat's push off entirely and fog closes straight over you.")]
    [Range(0f, 1f)] public float boatRepelStrength = 0.6f;

    /// <summary>
    /// Push this map's look onto its material. Called at level start and by Refresh Preview, never
    /// per frame — the material is what you tune against, and re-pushing every frame would undo
    /// every slider you moved while looking at it.
    /// </summary>
    /// <summary>
    /// The reverse of ApplyLook: read every Look value OFF the material and into this map.
    ///
    /// For when the material is ahead of the map — which is what happens after an evening of
    /// tuning the material directly, since that used to be the only live surface. One press makes
    /// the map agree with what is on screen instead of overwriting it on the next load.
    /// </summary>
    public void PullFromMaterial()
    {
        if (fogMaterial == null) return;

        threshold           = fogMaterial.GetFloat("_Threshold");
        edgeSoftness        = fogMaterial.GetFloat("_EdgeSoftness");
        undulationAmount    = fogMaterial.GetFloat("_UndulationAmount");
        undulationScale     = fogMaterial.GetFloat("_UndulationScale");
        lipWidth            = fogMaterial.GetFloat("_LipWidth");
        lipLighting         = fogMaterial.GetFloat("_LipLight");
        lipCurvature        = fogMaterial.GetFloat("_Curvature");
        heightScale         = fogMaterial.GetFloat("_HeightScale");
        ambient             = fogMaterial.GetFloat("_Ambient");
        interiorFill        = fogMaterial.GetFloat("_Opacity");
        transparencyFalloff = fogMaterial.GetFloat("_Transparency");
        grainAmount         = fogMaterial.GetFloat("_GrainAmount");
        grainScale          = fogMaterial.GetFloat("_GrainScale");
        fogColour           = fogMaterial.GetColor("_FogColor");
        litColour           = fogMaterial.GetColor("_LightColor");
    }

    public void ApplyLook()
    {
        if (!overrideLook || fogMaterial == null) return;

        fogMaterial.SetFloat("_Threshold", threshold);
        fogMaterial.SetFloat("_EdgeSoftness", edgeSoftness);
        fogMaterial.SetFloat("_UndulationAmount", undulationAmount);
        fogMaterial.SetFloat("_UndulationScale", undulationScale);
        fogMaterial.SetFloat("_LipWidth", lipWidth);
        fogMaterial.SetFloat("_LipLight", lipLighting);
        fogMaterial.SetFloat("_Curvature", lipCurvature);
        fogMaterial.SetFloat("_HeightScale", heightScale);
        fogMaterial.SetFloat("_Ambient", ambient);
        fogMaterial.SetFloat("_Opacity", interiorFill);
        fogMaterial.SetFloat("_Transparency", transparencyFalloff);
        fogMaterial.SetFloat("_GrainAmount", grainAmount);
        fogMaterial.SetFloat("_GrainScale", grainScale);
        fogMaterial.SetColor("_FogColor", fogColour);
        fogMaterial.SetColor("_LightColor", litColour);
    }

    void OnValidate()
    {
        // No conversion here any anymore. There used to be one for the world-units-to-fraction
        // move; it divided anything above 1 by the tile, which would now silently shrink every
        // legitimate world-unit size the moment the asset was touched.
        if (blobScaleMax < blobScaleMin) blobScaleMax = blobScaleMin;

        // Wind and fog scale live on the manager once a level is running, so an edit here has to
        // be pushed or it would appear to do nothing during play. Ignored unless this is the map
        // actually on the water.
        FogFieldManager.SyncFromMap(this);

    }
}
