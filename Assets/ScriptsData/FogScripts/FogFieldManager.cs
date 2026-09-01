using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>
/// Runs the whole fog field: fills the masses a level's FogMap allocates, moves their
/// BaseDots, paints them onto the grid, blurs it, and hands the result to the fog sheet.
///
/// THE FOG MAP IS THE ONLY AUTHORITY ON WHERE FOG IS. There is no scatter, no spawning around
/// the boat or the lamps, and no fallback of any kind: no map means no fog. Rocks, the boat and
/// lit street lights only ever PUSH fog away; nothing gathers it. Anything that put fog on the
/// water without a designer placing it has been removed, and should not come back without being
/// asked for.
///
/// Built on the same bones as RockRingManager and InstancedLightManager — statics cleared at
/// play start regardless of the domain-reload setting, a lazy self-creating instance so no scene
/// setup is needed, and globals re-pushed every frame so a shader reimport self-heals.
///
/// THE PAINTED TEXTURE COVERS THE ARENA AND DOES NOT MOVE. Only the MASK is boat-centred — it
/// fades masses by their distance from you, which is boat-centred without anything having to
/// travel. Making the texture follow the boat as well was a design mistake: a moving window is
/// what forced texel snapping and history reprojection, and when the previous frame was blended
/// back at the same UV it dragged the fog along with the boat. A window that never moves has none
/// of those problems to solve.
///
/// IT IS NOT SCREEN RESOLUTION. Cost is flat in screen size and in how many masses exist; it
/// scales only with the arena, so a much larger arena wants a higher resolution to hold detail.
///
/// Two textures, not one, and deliberately at different resolutions:
///
///   _FogField  (derived from detail) R = density, makes the outline
///                                  G = blob id premultiplied by density, recovered as G/R, so
///                                      undulation and grain can be sampled in the blob's own
///                                      space rather than in world space where the wobble would
///                                      appear to crawl across a blob as it drifted past
///
///   _FogHeight (double the grid)    R = sphere-cap height, makes the shading
///
/// The height map wants roughly double the grid's resolution. Normals amplify whatever roughness
/// is in their source, so sharing one texture would give facetted shading at whatever resolution
/// the outline happened to need.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(50)]
public class FogFieldManager : MonoBehaviour
{
    // ── Field ────────────────────────────────────────────────────────────────
    [Header("Field")]
    // Coverage is the ARENA, because the texture covers the arena and stays put. Cull is still a
    // consequence of the mask: a mass must survive past where it stops drawing or it vanishes
    // part-way through fading.
    [Tooltip("Arena width in world units. Taken from the level's ArenaProfile when there is one, " +
             "so this is the fallback for a scene without a level — the Fog Studio scene.")]
    [SerializeField] float arenaWidth = 40f;


    // ── Cull ─────────────────────────────────────────────────────────────────
    // HOW MUCH WATER IS SIMULATED, and the only thing that decides it.
    [Header("Cull")]
    [Tooltip("Where masses are BORN, in world units. That is all it does.")]
    [SerializeField] float spawnRadius = 4.2f;

    [Tooltip("Where masses are DELETED, in world units. That is all it does.")]
    [SerializeField] float cullRadius = 4.7f;

    // ── Boat mask ────────────────────────────────────────────────────────────
    // A MATERIAL PROPERTY AND NOTHING ELSE. It is pushed to the shader as a global and read by
    // nothing on this side — not the cull, not the texture, not where masses are born. Every time
    // it has been allowed to infer something, that inference has been the bug.
    [Tooltip("Overall opacity of the whole fog sheet. Seeded from the map on level load, live " +
             "from then on.")]
    [Range(0f, 1f)] [SerializeField] float fogOpacity = 1f;

    [Header("Boat Mask")]
    [Tooltip("How far from the boat fog is DRAWN, in world units. A fade on the material. It has " +
             "no effect on the simulation whatsoever — set it beyond the cull radius and you " +
             "simply see all the fog there is, with clear water past it.")]
    [SerializeField] float maskRadius = 3.14f;

    [Tooltip("How much of the mask is fade rather than hard edge, as a fraction of the radius.")]
    [Range(0f, 1f)] [SerializeField] float maskFeather = 0.75f;

    // No map means no fog, so there is no mask to speak of. Returning a made-up radius invented a
    // number that appeared nowhere in any asset and showed up in the designer as if it were real.
    float MaskRadius => fogMap != null ? maskRadius : 0f;

    /// <summary>
    /// The cull margin a map runs at. Exposed so the designer can draw the cull ring from the map
    /// it is editing rather than from whatever manager happens to be in the open scene.
    /// </summary>
    public static float CullMarginOf(FogMap map) => 1f;   // extent is authored now, not derived

    // How much wider than the cull circle the painted window is. Fog is CLIPPED at the window
    // edge rather than faded, so the window has to finish beyond the last place a mass can exist
    // or that clip becomes a straight line across the water. Masses are already fully melted out
    // by the time they reach cull, so this only has to cover their reach.
    const float FIELD_MARGIN = 1.45f;

    /// <summary>
    /// World units the painted texture spans. THE FOG'S OWN EXTENT, NOT THE ARENA.
    ///
    /// This was briefly the arena width, and that was the mistake behind the invisible fog. The
    /// simulation only ever keeps masses within cull of the boat, so an arena-wide texture spent
    /// its resolution on empty water: at 40 units across a 512 grid, one texel is 0.078 units
    /// while a BaseDot is 0.005 to 0.13. Most dots came out smaller than a single texel, missed
    /// every pixel centre, and rasterised to nothing at all.
    ///
    /// Sizing the window to the fog instead puts a texel back at roughly 0.017 units, so the
    /// smallest dots are several texels across and actually paint.
    /// </summary>
    float coverage => Mathf.Max(cullRadius * 2f * FIELD_MARGIN, 0.01f);

    /// <summary>
    /// How wide the fog SHEET is: the arena. The sheet is geometry the boat moves over, and
    /// moving it is what broke movement sync, so it is static and covers everything. The painted
    /// window inside it is a separate, much smaller thing that does follow the boat.
    /// </summary>
    public float SheetSize => Mathf.Max(arenaWidth, 0.01f);

    /// <summary>Distance at which a mass is retired and its slot freed. Derived from the mask.</summary>
    /// <summary>The map on the water, so a tool can tell whether the one it edits is deployed.</summary>
    public FogMap ActiveMap => fogMap;

    /// <summary>How wide the painted window is. Not the sheet size — see SheetSize.</summary>
    public float Coverage => coverage;

    /// <summary>Where the painted window is centred. The boat, snapped to whole texels.</summary>
    public Vector2 FieldCentre => _fieldCentre;

    /// <summary>What the mask and cull measure from.</summary>
    public Vector2 BoatCentre => _boatCentre;


    // ── Detail ───────────────────────────────────────────────────────────────
    // RESOLUTION IS DERIVED, NOT AUTHORED, AND THIS IS A BUG FIX.
    //
    // The painted window is sized from the mask (mask -> cull -> coverage). With an absolute pixel
    // count, widening the mask spread those same pixels over more water, so every texel got bigger
    // in world terms. The blur is measured in texels and BaseDots are measured in world units, so
    // the fog itself visibly changed: blurrier, and at the extreme dots fell under a texel and
    // stopped painting at all. Mask Radius is meant to say how much water is simulated, not how
    // the fog looks — the two were tangled through the texture size.
    //
    // Holding world-units-per-texel fixed and deriving the pixel count breaks that link. Widening
    // the mask now allocates a bigger texture at the same fineness, and the fog is identical.
    [Tooltip("How fine the fog is, in WORLD UNITS PER PIXEL. Lower is crisper and costs more. " +
             "Resolution follows from this and the mask, so widening the mask no longer changes " +
             "how the fog looks.")]
    [Range(0.005f, 0.3f)] [SerializeField] float unitsPerTexel = 0.053f;

    [Tooltip("Ceiling on the derived resolution, in pixels across.")]
    [SerializeField] int maxGridResolution = 1024;

    /// <summary>Pixels across the painted field, from the detail figure and how much water it covers.</summary>
    // NOT rounded to a power of two. Rounding made the texel size jump by up to 2x as the cull
    // radius crossed a threshold, and since the blur used to be measured in texels, nudging cull
    // visibly resized every blob. Render textures have no power-of-two requirement; taking the
    // exact count means units-per-texel is what you authored at any cull radius.
    int GridResolution => Mathf.Clamp(
        Mathf.CeilToInt(coverage / Mathf.Max(unitsPerTexel, 0.001f)),
        32, Mathf.Clamp(maxGridResolution, 32, 2048));

    /// <summary>
    /// The height map, at double the grid. Normals amplify whatever roughness is in their source,
    /// so a coarse height map gives facetted shading even when the outline is perfect.
    /// </summary>
    int HeightResolution => Mathf.Clamp(GridResolution * 2, 32, 4096);

    [Tooltip("Frame-to-frame stickiness. High and the fog is thick and sluggish; low and it is " +
             "wispy and quick. Also does part of the smoothing, so a high value buys back blur.")]
    [Range(0f, 0.98f)] [SerializeField] float heaviness = 0.88f;

    [Tooltip("Blur width in pixels. Wider fuses separate dots harder and rounds the outline off.")]
    [Range(0.002f, 1f)] [SerializeField] float blurRadius = 0.09f;

    [Tooltip("Blur width for the height map, in its own pixels. This one is not optional: an " +
             "unblurred union of domes shows every single BaseDot as a bump, and a limb comes out " +
             "looking like a corrugated sausage no matter how clean its outline is.")]
    [Range(0.002f, 2f)] [SerializeField] float heightBlurRadius = 0.2f;

    // ── Population ───────────────────────────────────────────────────────────
    [Header("Population")]
    [Tooltip("Off means no new masses form. Existing ones dissolve rather than blinking away. " +
             "GridData.fogEnabled is the authority at runtime and overwrites this on level load; " +
             "the value here is only what a scene with no level loaded runs at, such as the " +
             "Fog Studio scene.")]
    [SerializeField] bool fogEnabled = true;

    // ── Population ───────────────────────────────────────────────────────────
    // The live copy, same arrangement as wind, fog scale and the mask.
    //
    // Count and spacing between them ARE the density: how many masses, and how close they may
    // get. There is no third dial, and no stored arrangement behind them any more — positions are
    // thrown as masses are needed rather than looked up in a lattice.
    [Header("Population")]
    [Tooltip("How many masses to keep alive around the boat. Seeded from the map on level load " +
             "and live from then on. Capped by Blob Budget, which is the performance ceiling.")]
    [Range(1, 80)] [SerializeField] int blobCount = 12;

    [Tooltip("Closest two masses may be born to each other, IN WORLD UNITS — measured between " +
             "the masses themselves, not between points on a lattice. Widen it past what the " +
             "water can hold and the count quietly falls short.")]
    [Range(0.05f, 8f)] [SerializeField] float spacing = 1f;


    // No melt controls. A mass is born beyond the mask, where it paints nothing, and the mask
    // FEATHER is the whole of its appearance and disappearance. Nothing needs to fade itself in
    // when the thing that decides visibility is already a gradient.




    // ── Settings ─────────────────────────────────────────────────────────────
    [Header("Fog Map")]
    [Tooltip("Where masses sit, and the only thing that decides it. Taken from the level's " +
             "GridData at runtime, so this is the starting value and what the Fog Studio scene " +
             "uses. Null means NO FOG — there is no fallback.")]
    [SerializeField] FogMap fogMap;

    // ── Wind ─────────────────────────────────────────────────────────────────
    // The live copy. The map authors the starting values and hands them over on level load; after
    // that these are what the fog actually reads, so they can be dragged mid-flight. Editing the
    // MAP while playing pushes into these too, through FogMap.OnValidate — so either end works
    // and neither silently loses to the other.
    [Header("Wind")]
    [Tooltip("Which way the fog travels, in degrees. Seeded from the map on level load and live " +
             "from then on. Wind is the only motion fog has now: it carries each mass bodily and " +
             "creeps the whole tiled arrangement along with it, so the pattern travels rather " +
             "than emptying out behind itself.")]
    [Range(0f, 360f)] [SerializeField] float windAngle = 20f;

    [Tooltip("How fast, in world units a second. Small numbers go a long way at this world scale.")]
    [SerializeField] float windSpeed = 0.4f;

    /// <summary>Which way and how fast fog travels. The live value, not the map's.</summary>
    public Vector2 WindVector
    {
        get
        {
            float r = windAngle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * windSpeed;
        }
    }

    // ── Fog scale ────────────────────────────────────────────────────────────
    // The live copy, same arrangement as the wind above: the map authors the starting values, and
    // from level load these are what the fog reads.
    //
    // Still a FRACTION OF THE TILE rather than world units, because tile size is the collective
    // scale — keeping these relative means Tile Size resizes the whole pattern proportionally
    // instead of spreading masses apart while leaving them the same size. Tile size itself stays
    // on the map: it decides where masses are placed, not just how big they are, so moving it
    // live would relay the arrangement rather than resize it.
    [Header("Fog Scale")]
    [Tooltip("Shortest spine a mass is grown at, in world units. Seeded from the map on level " +
             "load and live from then on.")]
    [Range(0.05f, 20f)] [SerializeField] float blobScaleMin = 1.2f;

    [Tooltip("Longest spine a mass is grown at, in world units.")]
    [Range(0.05f, 20f)] [SerializeField] float blobScaleMax = 2f;

    /// <summary>
    /// Spine length in world units, low to high. Straight through: these ARE world units now.
    /// They used to be fractions of a tile size, which existed because an arrangement repeated
    /// across one. Nothing repeats any more, so a mass is simply the size it is.
    /// </summary>
    public Vector2 WorldBlobScale =>
        new Vector2(Mathf.Min(blobScaleMin, blobScaleMax), Mathf.Max(blobScaleMin, blobScaleMax));

    /// <summary>
    /// Copy a map's authored starting values into the live ones. Called when a map arrives, and
    /// again from the map's own OnValidate so editing it during play reaches the fog immediately.
    ///
    /// Only ever applies to the map actually on the water — editing some other asset in the
    /// project should not reach into a running level.
    /// </summary>
    public static void SyncFromMap(FogMap from)
    {
        if (_instance == null || from == null) return;
        if (_instance.fogMap != from) return;

        _instance.windAngle = from.windAngle;
        _instance.windSpeed = from.windSpeed;

        _instance.blobScaleMin = from.blobScaleMin;
        _instance.blobScaleMax = from.blobScaleMax;

        _instance.fogOpacity  = from.fogOpacity;
        _instance.maskRadius  = from.maskRadius;
        _instance.maskFeather = from.maskFeather;

        _instance.blobCount = from.blobCount;
        _instance.spacing   = from.spacing;

        _instance.unitsPerTexel     = from.unitsPerTexel;
        _instance.maxGridResolution = from.maxGridResolution;
        _instance.heaviness        = from.heaviness;
        _instance.blurRadius       = from.blurRadius;
        _instance.heightBlurRadius = from.heightBlurRadius;
        _instance.spawnRadius = from.spawnRadius;
        _instance.cullRadius  = from.cullRadius;

        _instance.settings.RepelStrength = from.repelStrength;

        _instance.rockClearRadius       = from.rockClearRadius;
        _instance.rockStrength       = from.rockStrength;
        _instance.rockRescanInterval = from.rockRescanInterval;

        _instance.lampClearFraction = from.lampClearFraction;
        _instance.lampClearRadius      = from.lampClearRadius;
        _instance.lampStrength      = from.lampStrength;
    }

    [Header("Weather")]
    [SerializeField] FogFieldSettings settings = FogFieldSettings.Default;

    // ── References ───────────────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("Paints BaseDots into the grid. Assign FogPaint.")]
    [SerializeField] Material paintMaterial;

    [Tooltip("Blurs the grid and blends it with last frame. Assign FogBlur.")]
    [SerializeField] Material blurMaterial;

    [Tooltip("Where the field centres. Falls back to _BoatWorldCenter, which every other " +
             "boat-centred effect already agrees with.")]
    [SerializeField] Transform boat;

    [Header("Rocks")]
    [Tooltip("Adopt anything already telling the water where it stands, so a level's spikes push " +
             "fog around without being tagged, wired, or given a component. Turn off only if you " +
             "want fog to ignore rocks entirely.")]
    [SerializeField] bool adoptRocks = true;

    [Tooltip("Clear radius given to adopted rocks, since IRockRing carries no fog settings of its own.")]
    [FormerlySerializedAs("rockStandoff")] 
    [SerializeField] float rockClearRadius = 0.34f;

    [Tooltip("Push strength given to adopted rocks. Rocks are firm — fog wraps close and stays out.")]
    [Range(0f, 1f)] [SerializeField] float rockStrength = 1f;

    [Tooltip("Seconds between rescans for rocks. Levels spawn their spikes, so this cannot be a " +
             "one-off at startup, but it need not run often either.")]
    [SerializeField] float rockRescanInterval = 2f;

    // ── Street lights ────────────────────────────────────────────────────────
    // Here rather than on each lamp, for the same reason the rock settings are: how fog behaves
    // is a property of the fog, not of the thing it happens to be avoiding. Tuning it per lamp
    // meant a level could quietly hold twenty different answers to the same question.
    [Header("Street Lights")]
    [Tooltip("Fraction of a lamp's light radius that fog is held out of. Keep it well under 1: " +
             "push fog out as far as the light reaches and it never enters the region it would " +
             "have been lit in, leaving a dark hole ringed by unlit fog instead of fog banked up " +
             "glowing at the edge of the lamp's reach.")]
    [Range(0.05f, 0.8f)] [SerializeField] float lampClearFraction = 0.35f;

    [Tooltip("Clear water kept beyond that, on top of it.")]
    [FormerlySerializedAs("lampStandoff")] 
    [SerializeField] float lampClearRadius = 0.34f;

    [Tooltip("How hard a lit lamp pushes fog out. Higher than a rock's — a lamp is burning fog " +
             "off, not just standing in its way.")]
    [Range(0f, 1f)] [SerializeField] float lampStrength = 1f;

    // Read by StreetLightController, which owns no fog numbers of its own. Defaults stand in when
    // no manager exists yet, so a lamp registering before the rig is built still behaves sanely.
    public static float LampClearFraction => _instance != null ? _instance.lampClearFraction : 0.35f;
    public static float LampClearRadius      => _instance != null ? _instance.lampClearRadius      : 0.34f;
    public static float LampStrength      => _instance != null ? _instance.lampStrength      : 1f;

    // ── Shader ids ───────────────────────────────────────────────────────────
    static readonly int FieldTexId    = Shader.PropertyToID("_FogField");
    static readonly int HeightTexId   = Shader.PropertyToID("_FogHeight");
    static readonly int OriginId      = Shader.PropertyToID("_FogFieldOrigin");   // xz world min, zw = 1/size
    static readonly int TexelId       = Shader.PropertyToID("_FogFieldTexel");
    static readonly int DotBufferId   = Shader.PropertyToID("_FogDots");
    static readonly int DotCountId    = Shader.PropertyToID("_FogDotCount");
    static readonly int BlurStepId    = Shader.PropertyToID("_FogBlurStep");
    static readonly int HeavinessId   = Shader.PropertyToID("_FogHeaviness");
    static readonly int HistoryShiftId = Shader.PropertyToID("_FogHistoryShift");
    static readonly int BlobCentresId = Shader.PropertyToID("_FogBlobCentres");
    static readonly int MaskRadiusId  = Shader.PropertyToID("_FogMaskRadius");
    static readonly int MaskFeatherId = Shader.PropertyToID("_FogMaskFeather");
    static readonly int OpacityId     = Shader.PropertyToID("_FogOpacity");
    static readonly int ObstaclesId    = Shader.PropertyToID("_FogObstacles");
    static readonly int ObstacleCountId = Shader.PropertyToID("_FogObstacleCount");

    // Must match FOG_OBSTACLE_SLOTS in FogMask.hlsl. A global array locks its size on the first
    // set, so the whole array goes out every frame even when three obstacles are near.
    public const int FOG_OBSTACLE_SLOTS = 32;
    static readonly Vector4[] _obstacleBuf = new Vector4[FOG_OBSTACLE_SLOTS];

    // Must match FOG_BLOB_SLOTS in FogGrain.hlsl. Also the wrap on blob ids, so an id is directly
    // an index into the centres array rather than needing a second lookup.
    public const int FOG_BLOB_SLOTS = 64;
    static readonly Vector4[] _centreBuf = new Vector4[FOG_BLOB_SLOTS];
    static readonly int BoatCentreId  = Shader.PropertyToID("_BoatWorldCenter");

    // ── Registration ─────────────────────────────────────────────────────────
    static readonly List<IFogRepeller> _repellers = new List<IFogRepeller>();
    static FogFieldManager _instance;

    public static int RepellerCount => _repellers.Count;
    /// <summary>
    /// The mask, for drawing only — the designer shows the fade the game actually uses rather
    /// than a number typed into the tool separately. Nothing in the simulation reads these.
    /// </summary>
    public float LodRadius  => MaskRadius;
    public float LodFeather => fogMap != null ? maskFeather : 0.75f;

    /// <summary>Where a mass is retired. Authored, and independent of the mask.</summary>
    public float CullRadius => cullRadius;

    public static int BlobCount     { get; private set; }
    public static int DotTotal      { get; private set; }

    /// <summary>
    /// Masses alive but faded to nothing by the LOD this frame. Without this the inspector cannot
    /// tell "the preset lays no dots" from "every mass is outside the mask", which look identical
    /// from a dot count of zero and have completely different fixes.
    /// </summary>
    public static int FadedCount { get; private set; }

    // ── Runtime state ────────────────────────────────────────────────────────
    readonly List<FogBlob> _blobs = new List<FogBlob>();
    readonly List<IFogRepeller> _near = new List<IFogRepeller>();
    readonly List<FogRockRepeller> _rocks = new List<FogRockRepeller>();

    // Which arena-map allocations currently hold a live mass, so two never stack on one spot and a
    // slot comes free the moment its mass dissolves.
    float _rockRescanClock;

    // One GPU-side dot. Must match FogDotGPU in FogPaint.hlsl — the two are a matched pair and
    // changing one without the other silently corrupts every dot.
    struct FogDotGPU
    {
        public Vector2 pos;
        public Vector2 axis;
        public float radius;
        public float stretch;
        public float height;
        public float strength;
        public float blobId;
    }
    const int DOT_STRIDE = 9 * 4;

    FogDotGPU[]    _dotCpu;
    GraphicsBuffer _dotGpu;

    RenderTexture _field, _fieldHistory, _scratch, _height, _heightScratch;
    CommandBuffer _cmd;
    // THREE things move together, and keeping them straight is the whole design.
    //
    //   the SHEET      geometry, static, arena-wide. Moving it desynced boat movement, so it
    //                  never moves. This is the one that must stay put.
    //   _fieldCentre   the painted window. Follows the boat, snapped to whole texels, and only a
    //                  few units across — it only has to cover where masses can exist.
    //   _boatCentre    what the mask fades from, what cull measures, and what decides which map
    //                  slots are near enough to fill.
    //
    // Making the SHEET static was right. Making the WINDOW static as well was the error: it cost
    // five times the world size for the same texture and put every BaseDot under a texel.
    Vector2 _fieldCentre;
    Vector2 _fieldCentrePrev;
    Vector2 _boatCentre;

    // Where last frame's fog sits in this frame's UV. The window moves in whole texels, so this
    // offset lands exactly on texel centres and the heaviness blend stays a memory rather than
    // becoming a smear. Not reprojecting this is what made the moving window look broken before.
    Vector2 _historyShift;
    int _nextId;

    // ────────────────────────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _repellers.Clear();
        _instance = null;
        BlobCount = 0;
        DotTotal = 0;
    }

    public static void Register(IFogRepeller r)
    {
        if (r == null || _repellers.Contains(r)) return;
        _repellers.Add(r);
        EnsureInstance();
    }

    public static void Unregister(IFogRepeller r) => _repellers.Remove(r);

    /// <summary>
    /// Size the painted field to this level's arena. Called on level load: the texture covers the
    /// arena and never moves, so it has to be told how wide the arena actually is or it falls back
    /// to a guess that is right only for one level.
    /// </summary>
    public static void SetArenaWidth(float width)
    {
        if (_instance == null || width <= 0.01f) return;
        _instance.arenaWidth = width;
    }

    /// <summary>
    /// Turn the whole field on or off for this level. Off retires what is out there rather than
    /// deleting it, so a level transition dissolves the last level's fog instead of blinking it
    /// away mid-shot.
    /// </summary>
    public static void SetEnabled(bool on)
    {
        if (_instance == null) return;
        _instance.fogEnabled = on;
        // Dropped outright rather than wound down. With no melt there is nothing to wind down,
        // and this fires on level load behind a transition, so the cut is not seen.
        if (!on)
            for (int i = 0; i < _instance._blobs.Count; i++) _instance._blobs[i].Kill();
    }

    /// <summary>
    /// Hand this level's arena map to the field. Called when a level spawns, the same way the
    /// sonar grid is applied — the map is level geography, so it arrives with the level rather
    /// than with the weather.
    /// </summary>
    public static void ApplyArenaMap(FogMap map)
    {
        if (_instance == null) return;
        _instance.fogMap = map;
        map?.ApplyLook();

        // The map says how thick this arena's fog starts. After this the manager's own control is
        // free to move, which is the point of having both: authored starting point, live dial.
        SyncFromMap(map);                // the map's wind and scale become the live ones

        // Fill what is already around the boat before the first frame is drawn. Without this the
        // edge-only rule below means a level opens in clear water and stays that way until you
        // have sailed a cull radius, because masses drift far too slowly to arrive on their own.
        _instance._primePending = true;
        // Existing masses were placed under the old map, so they go rather than hang on
        // allocations that no longer exist. Dropped outright: with no melt there is nothing to
        // wind down, and a map swap is a cut, not a transition.
        for (int i = 0; i < _instance._blobs.Count; i++) _instance._blobs[i].Kill();
    }


    static void EnsureInstance()
    {
        if (_instance != null) return;
        _instance = FindAnyObjectByType<FogFieldManager>();
        // Unlike the ring manager this does NOT create itself: it needs two materials assigned,
        // and a self-made one would silently do nothing while looking present in the hierarchy.
    }

    void Awake() => _instance = this;
    void OnEnable() { _instance = this; }
    void OnDisable() { ReleaseTextures(); if (_instance == this) _instance = null; }
    void OnDestroy() { ReleaseTextures(); if (_instance == this) _instance = null; }

    // ────────────────────────────────────────────────────────────────────────
    // LateUpdate so the boat, the rocks and the street lights have all finished moving.
    void LateUpdate()
    {
        if (paintMaterial == null || blurMaterial == null) return;

        float dt = Application.isPlaying ? Time.deltaTime : 1f / 60f;
        float time = Application.isPlaying ? Time.time : (float)UnityEditor_Time();

        EnsureTextures();
        CentreField();

        RescanRocks(dt);
        GatherRepellers();
        Populate(dt);
        SimulateBlobs(dt, time);
        Paint();
        PushGlobals();

        if (_snapFramesLeft > 0) CaptureFrame();

        // Cleared every frame, not only while capturing. They used to accumulate from play start,
        // so the first captured frame reported 437 births — the whole session, read as one frame.
        _bornThisFrame = _diedThisFrame = _throwFailures = 0;
    }

    // ── Diagnostic snapshot ──────────────────────────────────────────────────
    // Three frames of everything that decides what reaches the screen, written to a file. Reading
    // fog by eye is guesswork: a mass can be alive, drifting, and laying dots, and still paint
    // nothing because its dots are smaller than a texel or the mask has already faded it out.
    // Those are different failures with the same appearance, and only the numbers separate them.
    int _snapFramesLeft;
    System.Text.StringBuilder _snap;

    // Counted per frame by Populate, so the report can say whether masses are churning.
    int _bornThisFrame, _diedThisFrame, _throwFailures;

    // ── Distribution report ──────────────────────────────────────────────────
    // Not serialised: this is a readout, and writing it into the scene file would dirty the scene
    // every time the button was pressed.
    [System.NonSerialized] public string DistributionReport = "";

    // The last few birth angles, measured against the inflow direction at the moment of birth.
    // Kept so the report can say whether the bias toward oncoming water is actually landing.
    const int BIRTH_LOG = 48;
    readonly float[] _birthOffsets = new float[BIRTH_LOG];
    int _birthWrite, _birthCount;

    void LogBirthAngle(Vector2 pos)
    {
        Vector2 fromBoat = pos - _boatCentre;
        if (fromBoat.sqrMagnitude < 1e-6f) return;

        Vector2 flow = FlowRelativeToBoat;
        float birth = Mathf.Atan2(fromBoat.y, fromBoat.x) * Mathf.Rad2Deg;

        // Zero means born straight into the oncoming water; 180 means born directly behind.
        float offset = birth;
        if (flow.sqrMagnitude > 1e-6f)
        {
            float incoming = Mathf.Atan2(-flow.y, -flow.x) * Mathf.Rad2Deg;
            offset = Mathf.DeltaAngle(incoming, birth);
        }

        _birthOffsets[_birthWrite] = Mathf.Abs(offset);
        _birthWrite = (_birthWrite + 1) % BIRTH_LOG;
        _birthCount = Mathf.Min(_birthCount + 1, BIRTH_LOG);
    }

    /// <summary>
    /// Where the masses actually are, right now. Distance alone cannot show clustering — every
    /// mass at the same radius reads identically whether they ring you evenly or sit in one heap
    /// — so this reports angle, radial bands, nearest-neighbour spacing and where births land.
    /// </summary>
    public void CaptureDistribution()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("FOG DISTRIBUTION");
        sb.AppendLine($"masses {_blobs.Count} of {blobCount} asked for");
        sb.AppendLine($"spawn {spawnRadius:0.##}   cull {cullRadius:0.##}   mask {MaskRadius:0.##}");
        sb.AppendLine($"spacing {spacing:0.##} world units");

        Vector2 flow = FlowRelativeToBoat;
        float incomingDeg = flow.sqrMagnitude > 1e-6f
            ? Mathf.Atan2(-flow.y, -flow.x) * Mathf.Rad2Deg : 0f;
        sb.AppendLine($"wind {windAngle:0}deg {windSpeed:0.###} u/s   boat {_boatVelocity.magnitude:0.###} u/s");
        sb.AppendLine(flow.sqrMagnitude > 1e-6f
            ? $"water arrives from {incomingDeg:0}deg at {flow.magnitude:0.###} u/s"
            : "no flow past the boat - births are even all round");
        sb.AppendLine();

        if (_blobs.Count == 0) { sb.AppendLine("no masses alive"); DistributionReport = sb.ToString(); return; }

        // ── per mass ─────────────────────────────────────────────────────────
        sb.AppendLine("  #    dist   angle   nearest   dots");
        float nnMin = float.MaxValue, nnSum = 0f; int nnCount = 0, tooClose = 0;

        for (int i = 0; i < _blobs.Count; i++)
        {
            Vector2 rel = _blobs[i].Centre - _boatCentre;
            float d = rel.magnitude;
            float ang = Mathf.Repeat(Mathf.Atan2(rel.y, rel.x) * Mathf.Rad2Deg, 360f);

            float nn = float.MaxValue;
            for (int j = 0; j < _blobs.Count; j++)
            {
                if (j == i) continue;
                nn = Mathf.Min(nn, (_blobs[j].Centre - _blobs[i].Centre).magnitude);
            }
            if (nn < float.MaxValue)
            {
                nnMin = Mathf.Min(nnMin, nn); nnSum += nn; nnCount++;
                if (nn < spacing - 0.001f) tooClose++;
            }

            sb.AppendLine($"  [{i,2}] {d,6:0.##}  {ang,6:0}  {(nn < float.MaxValue ? nn.ToString("0.##") : "-"),8}  {_blobs[i].DotCount,5}");
        }
        sb.AppendLine();

        // ── radial bands, by area ────────────────────────────────────────────
        // Counts alone mislead: an outer band holds more water than an inner one of the same
        // thickness, so an even field puts MORE masses in the outer bands. Density is the honest
        // number, and a flat density column is what an even distribution looks like.
        sb.AppendLine("radial bands (even distribution = flat density)");
        const int BANDS = 5;
        for (int b = 0; b < BANDS; b++)
        {
            float r0 = cullRadius * b / BANDS, r1 = cullRadius * (b + 1) / BANDS;
            int n = 0;
            for (int i = 0; i < _blobs.Count; i++)
            {
                float d = (_blobs[i].Centre - _boatCentre).magnitude;
                if (d >= r0 && d < r1) n++;
            }
            float area = Mathf.PI * (r1 * r1 - r0 * r0);
            sb.AppendLine($"  {r0,5:0.##} to {r1,5:0.##}  {n,3} masses   density {n / Mathf.Max(area, 0.001f):0.###}");
        }
        sb.AppendLine();

        // ── angular sectors ──────────────────────────────────────────────────
        sb.AppendLine("angular sectors (even distribution = equal counts)");
        const int SECTORS = 8;
        var sec = new int[SECTORS];
        for (int i = 0; i < _blobs.Count; i++)
        {
            Vector2 rel = _blobs[i].Centre - _boatCentre;
            float ang = Mathf.Repeat(Mathf.Atan2(rel.y, rel.x) * Mathf.Rad2Deg, 360f);
            sec[Mathf.Clamp((int)(ang / (360f / SECTORS)), 0, SECTORS - 1)]++;
        }
        for (int s = 0; s < SECTORS; s++)
        {
            float from = s * 360f / SECTORS;
            sb.AppendLine($"  {from,3:0} to {from + 360f / SECTORS,3:0}  {sec[s],3}  {new string('#', Mathf.Min(sec[s], 40))}");
        }
        sb.AppendLine();

        // ── spacing ──────────────────────────────────────────────────────────
        if (nnCount > 0)
        {
            sb.AppendLine($"nearest neighbour: min {nnMin:0.###}  mean {nnSum / nnCount:0.###}  " +
                          $"spacing asks for {spacing:0.###}");
            if (tooClose > 0)
                sb.AppendLine($"  {tooClose} pairs are CLOSER than spacing - they drifted together after birth");
        }
        sb.AppendLine();

        // ── births ───────────────────────────────────────────────────────────
        if (_birthCount > 0)
        {
            float sum = 0f; int front = 0;
            for (int i = 0; i < _birthCount; i++)
            {
                sum += _birthOffsets[i];
                if (_birthOffsets[i] < 90f) front++;
            }
            sb.AppendLine($"last {_birthCount} births: mean {sum / _birthCount:0} deg off the oncoming side");
            sb.AppendLine($"  {front} of {_birthCount} were on the oncoming half " +
                          "(50% means no bias, 100% means all into the flow)");
        }
        else sb.AppendLine("no births recorded yet");

        DistributionReport = sb.ToString();
        Debug.Log("[Fog] Distribution captured - see the field on the component.", this);
    }

    /// <summary>Start a three-frame capture. Called from the inspector button.</summary>
    public void CaptureSnapshot()
    {
        _snap = new System.Text.StringBuilder();
        _snapFramesLeft = 3;

        // Announced on the spot, before anything else can go wrong. The capture itself needs
        // LateUpdate to run three times, and if that is not happening the useful information is
        // precisely that — silence would otherwise look identical to a button that did nothing.
        Debug.Log("[Fog] Snapshot armed - capturing 3 frames.", this);

        if (paintMaterial == null || blurMaterial == null)
        {
            Debug.LogError("[Fog] Snapshot will NOT complete: paintMaterial or blurMaterial is " +
                           "unassigned, so LateUpdate returns before the fog runs at all. Assign " +
                           "FogPaint.mat and FogBlur.mat on this component.", this);
            _snapFramesLeft = 0;
        }

        _snap.AppendLine("FOG SNAPSHOT");
        _snap.AppendLine($"map            {(fogMap != null ? fogMap.name : "NONE - no fog")}");
        _snap.AppendLine($"fogEnabled     {fogEnabled}");
        _snap.AppendLine($"playing        {Application.isPlaying}");
        _snap.AppendLine();

        _snap.AppendLine("-- extent and detail ------------------------------------------");
        _snap.AppendLine($"spawnRadius       {spawnRadius:0.###}   (masses are BORN here)");
        _snap.AppendLine($"cullRadius        {cullRadius:0.###}   (masses are DELETED here)");
        _snap.AppendLine($"maskRadius        {MaskRadius:0.###}   (fog DRAWS inside this)");
        _snap.AppendLine($"maskFeather       {LodFeather:0.###}   -> fade runs {MaskRadius * (1f - LodFeather):0.###} to {MaskRadius:0.###}");
        _snap.AppendLine($"coverage          {coverage:0.###}  world units of painted texture");
        _snap.AppendLine($"gridResolution    {GridResolution} px    -> {coverage / GridResolution:0.#####} u/texel");
        _snap.AppendLine($"unitsPerTexel     {unitsPerTexel:0.#####}  asked for");
        _snap.AppendLine($"heightResolution  {HeightResolution} px");
        _snap.AppendLine($"blur              {blurRadius:0.###} world units (resolution-independent)");
        _snap.AppendLine($"heaviness         {heaviness:0.###}");
        _snap.AppendLine();

        _snap.AppendLine("-- population -------------------------------------------------");
        _snap.AppendLine($"blobCount {blobCount}   (masses kept alive around the boat)");
        _snap.AppendLine($"spacing   {spacing:0.###} world units between masses");
        _snap.AppendLine($"blobScale {WorldBlobScale.x:0.###} to {WorldBlobScale.y:0.###} world units (spine length)");
        _snap.AppendLine($"wind      {windAngle:0}deg at {windSpeed:0.###} u/s");
        _snap.AppendLine($"boatVel   {_boatVelocity.magnitude:0.###} u/s   " +
                         $"flow past boat {FlowRelativeToBoat.magnitude:0.###} u/s   " +
                         $"births biased {(FlowRelativeToBoat.sqrMagnitude > 1e-6f ? "toward the inflow" : "evenly (no flow)")}");
        _snap.AppendLine($"birth band {spawnRadius * BIRTH_BAND:0.##} to {spawnRadius:0.##}");
        var fp = fogMap.properties;
        _snap.AppendLine($"blob: limbification {fp.limbification:0.##}  limbCount {fp.limbCount} -> " +
                         $"{fp.EffectiveLimbCount} effective  limbLength {fp.limbLength:0.##}  " +
                         $"spine {fp.spineThicknessRoot:0.###}/{fp.spineThicknessTip:0.###}  " +
                         $"limb {fp.limbThicknessRoot:0.###}/{fp.limbThicknessTip:0.###}  " +
                         $"stretch {fp.ellipseStretch:0.#}");
        _snap.AppendLine();
        _snap.AppendLine($"repellers near {_near.Count}   rocks adopted {_rocks.Count}" +
                         (_near.Count > 30 ? "   <-- every one of these deforms every dot" : ""));
        _snap.AppendLine();

        // Written now as well as at the end. If the frames never arrive, this file still holds
        // every setting, which is most of what a diagnosis needs.
        WriteSnapshotFile("(header only - frames did not run)");
    }

    static string SnapshotPath => System.IO.Path.Combine(
        System.IO.Directory.GetParent(Application.dataPath).FullName, "FogSnapshot.txt");

    void WriteSnapshotFile(string note)
    {
        try { System.IO.File.WriteAllText(SnapshotPath, _snap.ToString() + note); }
        catch (System.Exception e) { Debug.LogError($"[Fog] Could not write snapshot: {e.Message}"); }
    }

    void CaptureFrame()
    {
        int frame = 4 - _snapFramesLeft;
        float texel = coverage / Mathf.Max(GridResolution, 1);

        _snap.AppendLine($"=== FRAME {frame} ============================================");
        _snap.AppendLine($"boat {_boatCentre.x:0.##},{_boatCentre.y:0.##}   " +
                         $"masses {_blobs.Count}   dots {DotTotal}   " +
                         $"born {_bornThisFrame}  died {_diedThisFrame}  throwFailures {_throwFailures}");

        int visible = 0, faded = 0, skipped = 0;
        float dotMin = float.MaxValue, dotMax = 0f;
        int subTexelDots = 0, totalDots = 0;

        for (int i = 0; i < _blobs.Count; i++)
        {
            var b = _blobs[i];
            float d = (b.Centre - _boatCentre).magnitude;

            // What the MATERIAL will do with it: 1 fully drawn, 0 invisible.
            // Normalise FIRST. Mathf.SmoothStep is not HLSL's smoothstep — it interpolates
            // between its first two arguments by the third, so feeding it a raw world distance
            // returns a distance, not a 0..1 blend. This line reported mask=-2.09 for every mass
            // until it was fixed. The same mistake once inverted the rock rings.
            float inner  = MaskRadius * (1f - LodFeather);
            float span   = Mathf.Max(MaskRadius - inner, 1e-4f);
            float maskAt = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - inner) / span));
            if (maskAt > 0.99f) visible++;
            else if (maskAt > 0.001f) faded++;
            else skipped++;

            for (int k = 0; k < b.DotCount; k++)
            {
                float r = b.Dots[k].Radius;
                dotMin = Mathf.Min(dotMin, r); dotMax = Mathf.Max(dotMax, r);
                if (r < texel) subTexelDots++;
                totalDots++;
            }

            _snap.AppendLine($"  [{i,2}] d={d,6:0.##}  scale={b.Scale,5:0.##}  dots={b.DotCount,3}  " +
                             $"mask={maskAt:0.00}  {(b.DotCount == 0 ? "NOT SIMULATED" : "")}");
        }

        _snap.AppendLine($"  fully drawn {visible}   partly faded {faded}   invisible {skipped}");
        if (totalDots > 0)
        {
            _snap.AppendLine($"  dot radius {dotMin:0.####} to {dotMax:0.####} world units " +
                             $"= {dotMin / texel:0.##} to {dotMax / texel:0.##} TEXELS");
            _snap.AppendLine($"  dots under one texel: {subTexelDots} of {totalDots}" +
                             (subTexelDots > 0 ? "   <-- these paint NOTHING" : ""));
        }
        else _snap.AppendLine("  no dots laid at all this frame");
        _snap.AppendLine();

        if (--_snapFramesLeft == 0) WriteSnapshot();
    }

    void WriteSnapshot()
    {
        string path = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName, "FogSnapshot.txt");
        System.IO.File.WriteAllText(path, _snap.ToString());
        Debug.Log($"[Fog] Snapshot written to {path}\n\n{_snap}");
#if UNITY_EDITOR
        UnityEditor.EditorGUIUtility.systemCopyBuffer = _snap.ToString();
#endif
        _snap = null;
    }

    static double UnityEditor_Time()
    {
#if UNITY_EDITOR
        return UnityEditor.EditorApplication.timeSinceStartup;
#else
        return Time.timeAsDouble;
#endif
    }

    // ────────────────────────────────────────────────────────────────────────
    void EnsureTextures()
    {
        // Clamped into LOCALS, never written back into the serialised fields. Writing back ran
        // every frame, so a typed value was snapped to the nearest power of two and put straight
        // back in the inspector before the field lost focus — which read exactly like the
        // resolution being locked and refusing to take anything you entered.
        int gridPx   = GridResolution;
        int heightPx = HeightResolution;

        if (_field == null || _field.width != gridPx)
        {
            ReleaseTextures();
            _field        = NewRT(gridPx, "FogField");
            _fieldHistory = NewRT(gridPx, "FogFieldHistory");
            _scratch      = NewRT(gridPx, "FogFieldScratch");
            _height       = NewRT(heightPx, "FogHeight");
            _heightScratch = NewRT(heightPx, "FogHeightScratch");
        }
        else if (_height == null || _height.width != heightPx)
        {
            if (_height != null) _height.Release();
            if (_heightScratch != null) _heightScratch.Release();
            _height = NewRT(heightPx, "FogHeight");
            _heightScratch = NewRT(heightPx, "FogHeightScratch");
        }

        _cmd ??= new CommandBuffer { name = "Fog Field" };
    }

    static RenderTexture NewRT(int size, string name)
    {
        // Half-float: density sums well past 1 where dots overlap, and clamping that at 1 would
        // flatten exactly the difference the threshold reads.
        var rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false,
        };
        rt.Create();
        return rt;
    }

    void ReleaseTextures()
    {
        if (_field != null)        { _field.Release();        _field = null; }
        if (_fieldHistory != null) { _fieldHistory.Release(); _fieldHistory = null; }
        if (_scratch != null)      { _scratch.Release();      _scratch = null; }
        if (_height != null)       { _height.Release();       _height = null; }
        if (_heightScratch != null){ _heightScratch.Release();_heightScratch = null; }
        _dotGpu?.Release();
        _dotGpu = null;
        _dotCpu = null;   // released together, or the pair disagree about whether they exist
        _cmd?.Release();
        _cmd = null;
    }

    // ────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Centre the painted window on the boat, SNAPPED TO WHOLE TEXELS, and work out where last
    /// frame's fog has ended up relative to it.
    ///
    /// The snapping and the reprojection are one mechanism, and having only the first half of it
    /// is why a moving window looked broken before. Snapping means the window never lands
    /// half-way across a texel, so last frame's texels line up exactly with this frame's rather
    /// than being resampled slightly off every frame. The shift is what tells the heaviness pass
    /// WHERE they line up — without it the blend reads last frame at the same UV, which is a
    /// different piece of water once the window has moved, and drags the fog along behind you.
    /// </summary>
    void CentreField()
    {
        Vector3 b = ResolveBoat();
        Vector2 wasAt = _boatCentre;
        _boatCentre = new Vector2(b.x, b.z);

        // Smoothed, because a single frame's delta is noisy enough to swing the inflow direction
        // around and undo the whole point of biasing births toward it.
        if (Application.isPlaying && Time.deltaTime > 1e-5f)
            _boatVelocity = Vector2.Lerp(_boatVelocity,
                                         (_boatCentre - wasAt) / Time.deltaTime, 0.15f);

        _fieldCentrePrev = _fieldCentre;

        float texel = coverage / Mathf.Max(_field != null ? _field.width : GridResolution, 1);
        _fieldCentre = new Vector2(Mathf.Round(_boatCentre.x / texel) * texel,
                                   Mathf.Round(_boatCentre.y / texel) * texel);

        // World the window moved, expressed in UV. Sampling last frame at uv + this reads the
        // same piece of water it was painted on.
        _historyShift = (_fieldCentre - _fieldCentrePrev) / coverage;
    }

    Vector3 ResolveBoat()
    {
        if (boat != null) return boat.position;
        Vector4 g = Shader.GetGlobalVector(BoatCentreId);
        if (g.sqrMagnitude > 1e-8f) return new Vector3(g.x, g.y, g.z);
        return Vector3.zero;
    }

    /// <summary>
    /// Find rocks that already publish their waterline through IRockRing and wrap them as
    /// repellers. Levels spawn their spikes, so this cannot be a one-off at startup — but it is a
    /// scan of the scene, so it runs on an interval rather than every frame.
    /// </summary>
    void RescanRocks(float dt)
    {
        if (!adoptRocks) { _rocks.Clear(); return; }

        _rockRescanClock -= dt;
        if (_rockRescanClock > 0f && _rocks.Count > 0) return;
        _rockRescanClock = Mathf.Max(rockRescanInterval, 0.25f);

        _rocks.Clear();
        var all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] is IRockRing rock)
                _rocks.Add(new FogRockRepeller(rock, rockClearRadius, rockStrength));
        }
    }

    // The boat as an obstacle. Owned here and fed from the map every frame, so an arena decides
    // how fog behaves around the hull without anything being authored on the boat prefab.
    readonly FogBoatRepeller _boatRepel = new FogBoatRepeller();

    void GatherRepellers()
    {
        // Only what could possibly reach the grid. A level carrying forty rocks pays for the
        // handful near the boat.
        _near.Clear();
        float limit = coverage * 0.5f + 12f;
        float limitSq = limit * limit;

        // Refreshed from the map rather than cached, so editing the numbers moves the fog while
        // you watch it rather than on the next level load.
        _boatRepel.Centre   = new Vector3(_boatCentre.x, 0f, _boatCentre.y);
        _boatRepel.Radius   = fogMap != null ? Mathf.Max(fogMap.boatRepelRadius, 0f)   : 0f;
        _boatRepel.ClearRadius = fogMap != null ? Mathf.Max(fogMap.boatRepelClearRadius, 0f) : 0f;
        _boatRepel.Strength = fogMap != null ? Mathf.Clamp01(fogMap.boatRepelStrength) : 0f;
        Consider(_boatRepel, limitSq);

        for (int i = 0; i < _repellers.Count; i++) Consider(_repellers[i], limitSq);
        for (int i = 0; i < _rocks.Count; i++)     Consider(_rocks[i], limitSq);
    }

    void Consider(IFogRepeller r, float limitSq)
    {
        if (r == null || !r.RepelActive) return;

        // A FogRepellerSource left on the boat would push alongside the map-driven one above, so
        // fog would be shouldered aside twice by the same hull. The boat's numbers live on the map
        // now; a component still sitting on it is leftover, and ignoring it means finding one does
        // not have to be a bug you chase.
        if (boat != null && r is FogRepellerSource src && src != null &&
            (src.transform == boat || src.transform.IsChildOf(boat))) return;

        Vector3 c = r.RepelCentre;
        float dx = c.x - _boatCentre.x, dz = c.z - _boatCentre.y;
        if (dx * dx + dz * dz > limitSq) return;
        _near.Add(r);
    }

    // ────────────────────────────────────────────────────────────────────────
    void Populate(float dt)
    {
        // Retire anything that has wandered out of range. Its slot comes back round to wherever
        // the boat is now, which is what makes the cost flat regardless of level size.
        float cullSq = cullRadius * cullRadius;
        for (int i = _blobs.Count - 1; i >= 0; i--)
        {
            var b = _blobs[i];
            if (!b.Alive) { _blobs.RemoveAt(i); _diedThisFrame++; continue; }

            float dx = b.Centre.x - _boatCentre.x, dz = b.Centre.y - _boatCentre.y;
            // Dropped the moment it is clear of the radius. It has been invisible since it
            // crossed the mask, so there is nothing to fade and nothing to see go.
            if (dx * dx + dz * dz > cullSq) b.Kill();
        }

        // No map, no fog. Deliberately not a soft failure with a scatter behind it: fog appearing
        // where nobody placed it is worse than fog not appearing at all, because it looks like the
        // map is working when it is not.
        if (!fogEnabled) return;
        if (fogMap == null) return;
        // One number. There used to be a budget alongside this, which only ever meant that the
        // count you typed was not the count you got.
        if (_blobs.Count >= blobCount) return;

        // The first fill: masses thrown across the WHOLE disc rather than only the birth ring, so
        // a level opens at the state the system holds rather than filling in from the edge over
        // several minutes. Same throw, same spacing rule — only the inner radius differs.
        if (_primePending)
        {
            _primePending = false;
            int want = blobCount;
            for (int i = _blobs.Count; i < want; i++)
            {
                int before = _blobs.Count;
                SpawnFromMap(anywhereInRange: true);
                if (_blobs.Count == before) break;   // no room left at this spacing
            }
            return;
        }

        // Fill in one go rather than one a frame. Births are invisible — they happen beyond the
        // mask — so there is nothing to stagger, and a single birth per frame cannot keep up with
        // a population being emptied by drift and cull at speed.
        int target = blobCount;
        for (int i = _blobs.Count; i < target; i++)
        {
            int before = _blobs.Count;
            SpawnFromMap(anywhereInRange: false);
            if (_blobs.Count == before) break;   // no room at this spacing; do not spin
        }
    }

    // Set when a map arrives, cleared by the burst above.
    bool _primePending;

    /// <summary>
    /// Put one mass on the water, ALWAYS OUT OF SIGHT.
    ///
    /// A mass may only be born between the mask and the cull radius — past where fog draws, short
    /// of where it is dropped. Fog therefore never materialises in front of you: it forms in the
    /// blind ring and comes into view by moving, yours or its own. The mask is the inner edge
    /// rather than a tuned number, so this holds at any mask setting.
    ///
    /// POSITIONS ARE THROWN, NOT LOOKED UP. This used to search a lattice of stored arrangement
    /// slots across several tile repetitions and take the nearest free one — machinery inherited
    /// from hand-placed clouds, back when the map really did say where each mass went. It brought
    /// its own problems: spacing was measured between lattice points rather than between masses
    /// and so did nothing, slot keys could detach from the water when the tile drift wrapped, and
    /// "nearest free slot" is why the first fill balled up around the boat.
    ///
    /// Now a candidate point is thrown into the ring and kept if no living mass is within Spacing
    /// of it. Even coverage falls out of that on its own, and Spacing finally means the thing its
    /// name claims.
    /// </summary>
    void SpawnFromMap(bool anywhereInRange)
    {
        // On the spawn radius. Cull has nothing to do with where a mass starts — it only says
        // where one ends. The band gives the ring a little thickness so masses do not all appear
        // on one exact circle.
        float outer = Mathf.Max(spawnRadius, 0.01f);
        float inner = anywhereInRange ? 0f : outer * BIRTH_BAND;

        Vector2 pos;
        if (!ThrowPoint(inner, outer, out pos)) { _throwFailures++; return; }
        SpawnAt(pos);
    }

    // How many darts before giving up. Failing means the water is genuinely full at this spacing,
    // which is a legitimate answer — the count falling short is how too-wide spacing shows itself.
    const int SPAWN_ATTEMPTS = 24;

    // How thick the birth ring is, as a fraction of the spawn radius. Masses appear in the outer
    // tenth of it rather than on one exact circle.
    const float BIRTH_BAND = 0.9f;

    // How far the boat is travelling, smoothed. Only used to work out which way water is streaming
    // past it, which is the direction new masses should arrive from.
    Vector2 _boatVelocity;

    /// <summary>
    /// Which way masses travel RELATIVE TO THE BOAT: the wind carries them, and the boat's own
    /// motion carries the boat through them. Masses therefore stream past in this direction, and
    /// arrive from the opposite one.
    /// </summary>
    Vector2 FlowRelativeToBoat => WindVector - _boatVelocity;

    /// <summary>
    /// A free point in the ring between two radii of the boat, or false when there is no room.
    ///
    /// The radius is drawn from the square root of a uniform number, not uniformly. Uniform radius
    /// crowds points toward the middle, because a thin ring far out holds far more water than the
    /// same thickness near the centre; the square root weights by area and spreads them properly.
    /// </summary>
    bool ThrowPoint(float inner, float outer, out Vector2 point)
    {
        point = _boatCentre;
        if (outer <= 0.001f) return false;

        float innerSq = inner * inner, outerSq = outer * outer;
        float gapSq   = Mathf.Max(spacing, 0f) * Mathf.Max(spacing, 0f);

        // Which way new water is arriving from. Births are biased toward it, because a mass born
        // on the far side is already leaving: it is thrown into the outer band, which sits right
        // against the cull radius, and the very next thing it does is cross it. That produced a
        // constant churn of masses appearing and vanishing around the edge while the middle sat
        // still — half of every birth was on the outbound side and died almost immediately.
        //
        // Biased rather than restricted, so with no wind and a stationary boat this falls back to
        // an even ring instead of piling every mass on one arbitrary side.
        Vector2 flow = FlowRelativeToBoat;
        bool    directional = flow.sqrMagnitude > 1e-6f;
        Vector2 incoming = directional ? -flow.normalized : Vector2.zero;

        for (int attempt = 0; attempt < SPAWN_ATTEMPTS; attempt++)
        {
            float ang = Random.value * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

            // Accept with a probability that falls off toward the outbound side. Never zero, so a
            // mass can still appear behind you — just far less often than ahead.
            if (directional)
            {
                float facing = 0.5f + 0.5f * Vector2.Dot(dir, incoming);
                if (Random.value > facing * facing) continue;
            }

            // Area-weighted radius: a uniform one crowds points toward the inner edge, because a
            // ring of the same thickness holds more water the further out it sits.
            float r = Mathf.Sqrt(Mathf.Lerp(innerSq, outerSq, Random.value));
            Vector2 p = _boatCentre + dir * r;

            bool clear = true;
            for (int i = 0; i < _blobs.Count; i++)
            {
                Vector2 d = _blobs[i].Centre - p;
                if (d.sqrMagnitude < gapSq) { clear = false; break; }
            }
            if (!clear) continue;

            point = p;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Grow a mass at a point. The only place a FogBlob is made, so the first fill and every later
    /// birth build identical masses and can differ only in where they are put.
    /// </summary>
    void SpawnAt(Vector2 pos)
    {
        var blob = new FogBlob();
        blob.Spawn(fogMap.properties, pos,
                   Random.Range(WorldBlobScale.x, WorldBlobScale.y),
                   Random.value * Mathf.PI * 2f,
                   (_nextId++ % 64) / 64f,
                   Random.Range(int.MinValue, int.MaxValue));

        _blobs.Add(blob);
        _bornThisFrame++;
        LogBirthAngle(pos);
    }






    // ────────────────────────────────────────────────────────────────────────
    void SimulateBlobs(float dt, float time)
    {
        // One wind for the whole system, off the map. Masses drift on it and so does the tiled
        // arrangement, so the pattern and its contents travel together instead of shearing apart.
        Vector2 wind = WindVector;

        // Everything inside cull is simulated. There used to be a skip for masses the mask had
        // hidden, which meant the mask silently decided which masses had bodies at all — and a
        // mass with no dots is a mass that cannot come into view when you sail toward it. Cull is
        // the budget; the mask is a fade.
        int total = 0, faded = 0;
        for (int i = 0; i < _blobs.Count; i++)
        {
            var b = _blobs[i];

            // Full strength. What reaches the screen is decided in the shader, from one radius and
            // one feather, which is what a mask should be.
            b.LodFade = 1f;

            b.Simulate(dt, wind, in settings, _near);
            total += b.DotCount;
        }
        BlobCount = _blobs.Count;
        DotTotal = total;
        FadedCount = faded;

        int capacity = Mathf.Max(blobCount * FogBlob.MAX_DOTS, 1);
        // _dotGpu is tested too, not just the CPU array. ReleaseTextures drops the GPU buffer
        // and leaves _dotCpu alone, so after a release the length still matched, this branch was
        // skipped, and SetData below ran on a null buffer. Latent until resolution started
        // deriving from the mask — then every frame of a mask drag released and threw.
        if (_dotCpu == null || _dotCpu.Length != capacity || _dotGpu == null)
        {
            _dotCpu = new FogDotGPU[capacity];
            _dotGpu?.Release();
            _dotGpu = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, DOT_STRIDE);
        }

        int w = 0;
        for (int i = 0; i < _blobs.Count && w < capacity; i++)
        {
            var b = _blobs[i];
            for (int d = 0; d < b.DotCount && w < capacity; d++)
            {
                var s = b.Dots[d];
                _dotCpu[w++] = new FogDotGPU
                {
                    pos = s.Position, axis = s.Axis, radius = s.Radius,
                    stretch = s.Stretch, height = s.Height,
                    strength = s.Strength, blobId = s.BlobId,
                };
            }
        }
        DotTotal = w;
        if (w > 0) _dotGpu.SetData(_dotCpu, 0, 0, w);
    }

    // ────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Paint, blur, blend. Every pass here draws into a small offscreen texture — none of it is
    /// ever drawn to the screen. What the player sees is the fog sheet, a real plane in the scene
    /// carrying a material that reads these textures, so it depth-sorts against rocks like any
    /// other geometry.
    /// </summary>
    void Paint()
    {
        _cmd.Clear();

        float size = coverage;
        Vector4 origin = new Vector4(_fieldCentre.x - size * 0.5f,
                                     _fieldCentre.y - size * 0.5f,
                                     1f / size, size);

        _cmd.SetGlobalVector(OriginId, origin);
        _cmd.SetGlobalFloat(DotCountId, DotTotal);

        // ── density + blob id ────────────────────────────────────────────────
        _cmd.SetRenderTarget(_scratch);
        _cmd.ClearRenderTarget(false, true, Color.clear);
        if (DotTotal > 0)
        {
            _cmd.SetGlobalBuffer(DotBufferId, _dotGpu);
            // Six verts a dot, one instance a dot. No mesh, no per-dot object.
            _cmd.DrawProcedural(Matrix4x4.identity, paintMaterial, 0, MeshTopology.Triangles, 6, DotTotal);
        }

        // ── height ───────────────────────────────────────────────────────────
        _cmd.SetRenderTarget(_height);
        _cmd.ClearRenderTarget(false, true, Color.clear);
        if (DotTotal > 0)
        {
            _cmd.SetGlobalBuffer(DotBufferId, _dotGpu);
            _cmd.DrawProcedural(Matrix4x4.identity, paintMaterial, 1, MeshTopology.Triangles, 6, DotTotal);
        }

        // ── blur the height map ──────────────────────────────────────────────
        // Not optional. A raw union of domes reads as a corrugated sausage with every dot
        // visible along it, however clean the outline over the top of it is.
        // WORLD units, converted to UV here. Measured in texels it changed width whenever the
        // texture was resized, so the cull radius silently resized every blob.
        float hStep = heightBlurRadius / Mathf.Max(coverage, 0.001f);
        _cmd.SetGlobalVector(BlurStepId, new Vector4(hStep, 0f, 0f, 0f));
        _cmd.Blit(_height, _heightScratch, blurMaterial, 0);
        _cmd.SetGlobalVector(BlurStepId, new Vector4(0f, hStep, 0f, 0f));
        _cmd.Blit(_heightScratch, _height, blurMaterial, 0);

        // ── separable blur ───────────────────────────────────────────────────
        float texel = 1f / Mathf.Max(_field != null ? _field.width : GridResolution, 1);
        _cmd.SetGlobalFloat(TexelId, texel);

        float bStep = blurRadius / Mathf.Max(coverage, 0.001f);
        _cmd.SetGlobalVector(BlurStepId, new Vector4(bStep, 0f, 0f, 0f));
        _cmd.Blit(_scratch, _field, blurMaterial, 0);

        _cmd.SetGlobalVector(BlurStepId, new Vector4(0f, bStep, 0f, 0f));
        _cmd.Blit(_field, _scratch, blurMaterial, 0);

        // ── heaviness: blend with last frame ─────────────────────────────────
        // Does part of the smoothing on its own, which is why the blur above can stay narrow,
        // and turns any residual jitter into the slow creep that reads as heavy fog.
        _cmd.SetGlobalFloat(HeavinessId, Application.isPlaying ? heaviness : 0f);
        _cmd.SetGlobalVector(HistoryShiftId, new Vector4(_historyShift.x, _historyShift.y, 0f, 0f));
        _cmd.SetGlobalTexture("_FogHistory", _fieldHistory);
        _cmd.Blit(_scratch, _field, blurMaterial, 1);
        _cmd.CopyTexture(_field, _fieldHistory);

        Graphics.ExecuteCommandBuffer(_cmd);
    }

    void PushGlobals()
    {
        // Both the texture and its world mapping, every frame. A shader reimport that wipes these
        // self-heals on the next frame, same reasoning as the soul-fish masks.
        // Where each live mass currently sits, so the grain can be sampled in its space and travel
        // with it. A global array locks its size on first set, so the full slot count always goes
        // out even when three masses are alive.
        for (int i = 0; i < FOG_BLOB_SLOTS; i++) _centreBuf[i] = Vector4.zero;
        for (int i = 0; i < _blobs.Count; i++)
        {
            var b = _blobs[i];
            int slot = Mathf.Clamp(Mathf.RoundToInt(b.Id * FOG_BLOB_SLOTS), 0, FOG_BLOB_SLOTS - 1);
            _centreBuf[slot] = new Vector4(b.Centre.x, b.Centre.y, 0f, 0f);
        }
        Shader.SetGlobalVectorArray(BlobCentresId, _centreBuf);

        // The mask, straight to the material. Every frame, like everything else here, so a
        // shader reimport that wipes the globals self-heals on the next frame.
        Shader.SetGlobalFloat(MaskRadiusId,  MaskRadius);
        Shader.SetGlobalFloat(MaskFeatherId, LodFeather);
        Shader.SetGlobalFloat(OpacityId,     Mathf.Clamp01(fogOpacity));

        // Obstacle circles for the fragment mask. The same repellers that push the skeleton, but
        // here they cut fog exactly — which is what lets the push be gentle enough not to lurch.
        int obstacles = 0;
        for (int i = 0; i < _near.Count && obstacles < FOG_OBSTACLE_SLOTS; i++)
        {
            var r = _near[i];
            if (r == null || !r.RepelActive) continue;

            Vector3 c = r.RepelCentre;
            float keep = r.RepelRadius + r.RepelClearRadius;
            if (keep <= 0f) continue;

            // w is the softness of the cut. Scaled off the obstacle so a big rock does not get a
            // razor edge while a small one dissolves.
            _obstacleBuf[obstacles++] = new Vector4(c.x, c.z, keep, Mathf.Max(keep * 0.15f, 0.02f));
        }
        for (int i = obstacles; i < FOG_OBSTACLE_SLOTS; i++) _obstacleBuf[i] = Vector4.zero;

        Shader.SetGlobalVectorArray(ObstaclesId, _obstacleBuf);
        Shader.SetGlobalFloat(ObstacleCountId, obstacles);

        // The look, every frame. See FogMap's Look header for why this is no longer once-only.
        fogMap?.ApplyLook();

        Shader.SetGlobalTexture(FieldTexId, _field);
        Shader.SetGlobalTexture(HeightTexId, _height);
        Shader.SetGlobalVector(OriginId, new Vector4(_fieldCentre.x - coverage * 0.5f,
                                                     _fieldCentre.y - coverage * 0.5f,
                                                     1f / coverage, coverage));
    }

#if UNITY_EDITOR
    /// <summary>
    /// Where the debug rings are drawn. The fog sheet, if there is one, so the rings sit ON the
    /// fog rather than hovering above it — comparing a ring against a mass is the whole point of
    /// drawing them, and a height difference makes that a guess from any angle but straight down.
    /// </summary>
    float GizmoHeight()
    {
        var sheet = FindAnyObjectByType<FogSheetMesh>();
        if (sheet != null) return sheet.transform.position.y + 0.001f;
        return 0.02f;
    }

    void OnDrawGizmosSelected()
    {
        float y = GizmoHeight();
        Vector3 c = new Vector3(_boatCentre.x, y, _boatCentre.y);
        float mask = MaskRadius;

        // Coverage: the painted texture's world window. Fog is CLIPPED at this edge, not faded,
        // so a mass reaching it shows a hard line on screen.
        UnityEditor.Handles.color = Fade(new Color(0.65f, 0.75f, 0.9f, 0.06f));
        Vector3 fc = new Vector3(_fieldCentre.x, y, _fieldCentre.y);
        UnityEditor.Handles.DrawSolidRectangleWithOutline(new Vector3[]
        {
            fc + new Vector3(-coverage * 0.5f, 0f, -coverage * 0.5f),
            fc + new Vector3(-coverage * 0.5f, 0f,  coverage * 0.5f),
            fc + new Vector3( coverage * 0.5f, 0f,  coverage * 0.5f),
            fc + new Vector3( coverage * 0.5f, 0f, -coverage * 0.5f),
        }, Fade(new Color(0.65f, 0.75f, 0.9f, 0.04f)),
           Fade(new Color(0.65f, 0.75f, 0.9f, 0.30f)));

        // The figure that decides whether fog can exist at all. A BaseDot smaller than a texel
        // falls between pixel centres and paints NOTHING, however alive it looks in these
        // gizmos, so this wants to stay well under the smallest dot radius the map produces.
        int fieldPx = _field != null ? _field.width : GridResolution;
        UnityEditor.Handles.Label(fc + new Vector3(-coverage * 0.5f, 0f, coverage * 0.5f),
            $"grid window {coverage:0.#} u  @ {coverage / Mathf.Max(fieldPx, 1):0.####} u/texel");

        // Wire rings, not filled discs, so four nested ranges stay readable at once. The blob
        // discs below stay solid — those are areas, these are boundaries.
        // The painted grid's edge is a HARD CLIP in the shader, not a fade, so a mass reaching it
        // shows a straight line across the fog. Labelled because that line is indistinguishable
        // by eye from the fog sheet's own edge, and the two have different fixes.
        UnityEditor.Handles.Label(fc + new Vector3(-coverage * 0.5f, 0f, coverage * 0.5f),
            $"arena field {coverage:0.#} u  (static, hard clip at its edge)");

        Ring(c, cullRadius,           new Color(0.95f, 0.55f, 0.35f, 0.75f));   // retired here
        Ring(c, mask,                 new Color(0.35f, 0.72f, 1f,    0.95f));   // stops drawing
        // (1 - feather), matching FogMask.hlsl. It read mask * feather, which put the ring in the
        // wrong place at every value except 0.5.
        Ring(c, mask * (1f - LodFeather), new Color(0.35f, 0.72f, 1f, 0.45f));   // fade begins

        UnityEditor.Handles.Label(c + new Vector3(mask, 0f, 0f),
            $"mask {mask:0.##}");
        UnityEditor.Handles.Label(c + new Vector3(cullRadius, 0f, 0f),
            $"cull {cullRadius:0.##}");

        // What every repeller is actually clearing, drawn at the size the simulation uses — rock
        // radius plus its clear radius plus dot slack. Seeing these against the mask ring is the
        // fastest way to spot a repeller that clears more ground than the fog can occupy.
        for (int i = 0; i < _near.Count; i++)
        {
            var r = _near[i];
            if (r == null || !r.RepelActive) continue;
            Vector3 rc = r.RepelCentre; rc.y = y;
            float keep = r.RepelRadius + r.RepelClearRadius;

            UnityEditor.Handles.color = Fade(new Color(0.95f, 0.45f, 0.30f, 0.13f));
            UnityEditor.Handles.DrawSolidDisc(rc, Vector3.up, keep);
            Ring(rc, keep, new Color(0.95f, 0.45f, 0.30f, 0.55f));
        }

        // Live masses. Solid, because a mass is an area you are comparing against those rings.
        for (int i = 0; i < _blobs.Count; i++)
        {
            var b = _blobs[i];
            if (!b.Alive) continue;

            // Faded masses drawn hollow: alive, drifting, holding a slot, painting nothing. That
            // distinction is invisible on screen and it is exactly what goes wrong most often.
            bool painting = b.LodFade > 0.001f;
            Vector3 bc = new Vector3(b.Centre.x, y, b.Centre.y);
            float rad = Mathf.Max(b.ReachRadius, 0.1f);

            if (painting)
            {
                UnityEditor.Handles.color = Fade(new Color(1f, 0.95f, 0.75f, 0.10f + 0.25f * b.LodFade));
                UnityEditor.Handles.DrawSolidDisc(bc, Vector3.up, rad);
            }
            Ring(bc, rad, painting ? new Color(1f, 0.95f, 0.75f, 0.7f)
                                   : new Color(0.6f, 0.6f, 0.62f, 0.5f));
        }

        // Which way everything travels. Drawn from the boat so it can be read against the wind
        // arrow in the map designer without mentally rotating anything.
        if (fogMap != null)
        {
            Vector2 w = WindVector;
            if (w.sqrMagnitude > 1e-6f)
            {
                Vector3 dir = new Vector3(w.x, 0f, w.y).normalized;
                UnityEditor.Handles.color = Fade(new Color(0.7f, 0.9f, 1f, 0.8f));
                UnityEditor.Handles.DrawLine(c, c + dir * mask);
                UnityEditor.Handles.Label(c + dir * mask,
                    $"wind {windAngle:0}\u00B0  {windSpeed:0.##} u/s");
            }
        }
    }

    // Every fog gizmo goes through these two, so the Gizmo Manager's opacity slider thins the
    // whole set at once. Fog draws a lot of overlapping area and at full strength it buries the
    // thing it is meant to help you look at.
    static Color Fade(Color c)
    {
        // The pref is read directly rather than through GizmoManagerWindow: that lives in the
        // editor assembly and runtime code cannot see it. The key is the contract between them.
        c.a *= Mathf.Clamp01(UnityEditor.EditorPrefs.GetFloat("FogTools.GizmoManager.Opacity", 1f));
        return c;
    }

    static void Ring(Vector3 centre, float radius, Color colour)
    {
        if (radius <= 0.001f) return;
        UnityEditor.Handles.color = Fade(colour);
        UnityEditor.Handles.DrawWireDisc(centre, Vector3.up, radius);
    }
#endif
}
