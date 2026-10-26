using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The world's made-up light — the one the river runs, the landscape hills and the spikes are all
/// turned against. Where this object stands IS the light's position, and its Strength is the one
/// strength all three share, so there is nowhere else for them to disagree.
///
/// Spawned from the Level Select Lighting window, which edits this object and holds every bake.
/// Nothing here pushes: <see cref="LevelSelectDesignerData.ApplyAesthetics"/>
/// calls <see cref="Push"/> every frame, alongside the rest of the world's look.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class LevelSelectLight : MonoBehaviour
{
    // The default is the landscape preset's strength when the two were merged, so a freshly
    // spawned light lights the hills exactly as they were.
    [Tooltip("How hard the river runs, the landscape and the spikes are turned against this " +
             "light. 0 leaves them flat and unlit; 1 is the full turn, darkest on faces backing " +
             "away from it; past 1 the lit result is multiplied, up to 10x brighter. There is no " +
             "falloff with distance — where the object stands says which way the light comes " +
             "from, this says how much of it there is.")]
    [Range(0f, 10f)] public float strength = 2.02f;

    // The light the boat carries, added over this one round wherever the boat is — on the river
    // runs, the landscape hills and the river water (LevelSelectBoatLight.hlsl). Not the spikes
    // or the trees.
    [Header("Boat Light")]
    [Tooltip("How far the boat's light reaches at full brightness, in metres from the boat.")]
    [Min(0f)] public float boatLightRadius = 1f;

    [Tooltip("How many metres past the Radius the boat's light takes to fade to nothing.")]
    [Min(0f)] public float boatLightSoftness = 0.5f;

    [Tooltip("How much the boat's light brightens everything inside it. 0 leaves the pool of " +
             "light out.")]
    [Range(0f, 4f)] public float boatLightStrength = 0.3f;

    [Tooltip("How much more the faces turned towards the boat brighten, inside the same Radius. " +
             "Not on the river water, which faces straight up and so would face the boat the " +
             "same everywhere. 0 leaves it out.")]
    [Range(0f, 4f)] public float boatLightFacing = 0.3f;

    [Tooltip("The boat light's colour.")]
    public Color boatLightColour = Color.white;

    [Header("Shadows")]
    [Tooltip("How dark the shadows are. 0 leaves them out; 1 is black.")]
    [Range(0f, 1f)] public float shadowDarkness = 0.5f;

    [Tooltip("How soft the shadows' edges are, in texels of a 2048 shadow map — the same width " +
             "whatever Shadow Map Size is baked. 0 is a hard edge; " +
             "the edge widens the farther the shadow falls from the light.")]
    [Range(0f, 8f)] public float shadowEdgeSoftness = 1f;

    [Tooltip("Texels along each side of the baked shadow map. Bigger is sharper — and slower to " +
             "bake: 4096 is 64 MB, 8192 is 256 MB. Edge Softness stays the same width at any " +
             "size. Baked — press Bake Shadows after changing it.")]
    public int shadowMapSize = 2048;

    // Written by Bake Shadows in the Lighting window — the distance from the light to the
    // world in every direction, and where the light stood when it was baked. The shadows are cast
    // from there, so moving the light leaves them where they were until the next bake.
    [HideInInspector] public Texture2D shadowMap;
    [HideInInspector] public Vector3   shadowBakedFrom;

    [Header("River Glow")]
    [Tooltip("How brightly the river water lights the walls, rims, banks, hills and fog around " +
             "it. 0 leaves the glow out. Live — no need to bake again.")]
    [Range(0f, 10f)] public float glowStrength = 1f;

    [Tooltip("How far the river's glow reaches from the water, in metres. Baked — press Bake " +
             "River Glow after changing it.")]
    [Range(0.5f, 20f)] public float glowReach = 3f;

    [Tooltip("How many times closer together the river glow's baked points are than the usual " +
             "(1). 2 is twice the detail and eight times the points. Baked — press Bake River " +
             "Glow after changing it.")]
    [Range(1f, 2f)] public float riverGlowResolution = 1f;

    // Written by Bake River Glow in the Lighting window — the glow in bricks around the
    // water, the grid saying where each near brick is, that grid's corner (xyz) and its point
    // spacing (w), the same two for the far points (null / w 0 when the reach is short enough to
    // need none), and the Glow Reach and Resolution it was baked with.
    [HideInInspector] public Texture3D riverGlowAtlas;
    [HideInInspector] public Texture3D riverGlowBricks;
    [HideInInspector] public Vector4   riverGlowOrigin;
    [HideInInspector] public Texture3D riverGlowFarBricks;
    [HideInInspector] public Vector4   riverGlowFarOrigin;
    [HideInInspector] public float     riverGlowBakedReach;
    [HideInInspector] public float     riverGlowBakedResolution = 1f;

    [Header("Soul Path Glow")]
    [Tooltip("How many times closer together the soul path glow's baked points are than the " +
             "usual (1). 2 is twice the detail and eight times the points. Baked — press Bake " +
             "Soul Path Glow after changing it.")]
    [Range(1f, 2f)] public float soulGlowResolution = 1f;

    // Written by Bake Soul Path Glow in the Lighting window — the soul routes' glow in
    // bricks around their bands (xyz the way it comes from, w how much), beside it which route
    // lights each point and how far along that route the light comes from, the brick grid, its
    // corner (xyz) and point spacing (w), the reach and Resolution it was baked with, and the
    // routes by slot: the shader finds each route's band front at its slot. Strength and Reveal
    // Softness are live, on the river water preset.
    [HideInInspector] public Texture3D    soulGlowAtlas;
    [HideInInspector] public Texture3D    soulGlowRoutes;
    [HideInInspector] public Texture3D    soulGlowBricks;
    [HideInInspector] public Vector4      soulGlowOrigin;
    [HideInInspector] public float        soulGlowBakedReach;
    [HideInInspector] public float        soulGlowBakedResolution = 1f;
    [HideInInspector] public List<string> soulGlowRouteIds = new List<string>();

    private static readonly List<LevelSelectLight> Lights = new List<LevelSelectLight>();

    private static readonly int PositionId     = Shader.PropertyToID("_LevelSelectLightPosition");
    private static readonly int StrengthId     = Shader.PropertyToID("_LevelSelectLightStrength");
    private static readonly int BoatLightId    = Shader.PropertyToID("_LevelSelectBoatLight");
    private static readonly int BoatColourId   = Shader.PropertyToID("_LevelSelectBoatLightColour");
    private static readonly int ShadowMapId    = Shader.PropertyToID("_LevelSelectShadowMap");
    private static readonly int ShadowFromId   = Shader.PropertyToID("_LevelSelectShadowFrom");
    private static readonly int ShadowParamsId = Shader.PropertyToID("_LevelSelectShadowParams");
    private static readonly int GlowAtlasId    = Shader.PropertyToID("_RiverGlowAtlas");
    private static readonly int GlowBricksId   = Shader.PropertyToID("_RiverGlowBricks");
    private static readonly int GlowOriginId   = Shader.PropertyToID("_RiverGlowOrigin");
    private static readonly int GlowGridId     = Shader.PropertyToID("_RiverGlowGrid");
    private static readonly int GlowAtlasSizeId = Shader.PropertyToID("_RiverGlowAtlasSize");
    private static readonly int GlowFarBricksId = Shader.PropertyToID("_RiverGlowFarBricks");
    private static readonly int GlowFarOriginId = Shader.PropertyToID("_RiverGlowFarOrigin");
    private static readonly int GlowFarGridId   = Shader.PropertyToID("_RiverGlowFarGrid");
    private static readonly int SoulAtlasId     = Shader.PropertyToID("_SoulGlowAtlas");
    private static readonly int SoulRoutesId    = Shader.PropertyToID("_SoulGlowRoutes");
    private static readonly int SoulBricksId    = Shader.PropertyToID("_SoulGlowBricks");
    private static readonly int SoulOriginId    = Shader.PropertyToID("_SoulGlowOrigin");
    private static readonly int SoulGridId      = Shader.PropertyToID("_SoulGlowGrid");
    private static readonly int SoulAtlasSizeId = Shader.PropertyToID("_SoulGlowAtlasSize");

    // Bound while no glow is baked, so the shader always has a 3D texture to read.
    private static Texture3D _noGlow;

    // The light that has already said it has no shadows baked, so it is said once, not every frame.
    private static LevelSelectLight _warnedNoShadows;

    /// <summary>The light in the open scene, or null if none has been spawned.</summary>
    public static LevelSelectLight Current => Lights.Count > 0 ? Lights[0] : null;

    private void OnEnable()
    {
        if (!Lights.Contains(this)) Lights.Add(this);
        if (Lights.Count > 1)
            Debug.LogWarning($"[LevelSelectLight] More than one light in the scene — " +
                             $"'{Lights[0].name}' wins, '{name}' is ignored.", this);
    }

    private void OnDisable()
    {
        Lights.Remove(this);

        // Shader globals outlive the scene, and the fog sheets are drawn in the Grid Designer's
        // scenes too, where nothing pushes this light — so the last one going takes its light and
        // its shadows with it rather than leaving them on whatever scene loads next.
        if (Lights.Count == 0) Push();
    }

    /// <summary>
    /// Publishes the light. With no light in the scene the strength goes to 0 — everything flat
    /// and unlit — rather than falling back to some remembered position, so a missing light shows.
    /// </summary>
    public static void Push()
    {
        var light = Current;
        if (light == null)
        {
            Shader.SetGlobalFloat(StrengthId, 0f);
            Shader.SetGlobalVector(BoatLightId, Vector4.zero);
            Shader.SetGlobalVector(ShadowFromId, Vector4.zero);
            PushNoGlow();
            PushNoSoulGlow();
            SoulPathGlow.PushFronts(null);
            return;
        }

        Shader.SetGlobalVector(PositionId, light.transform.position);
        Shader.SetGlobalFloat(StrengthId, light.strength);
        Shader.SetGlobalVector(BoatLightId, new Vector4(light.boatLightRadius, light.boatLightSoftness,
                                                        light.boatLightStrength, light.boatLightFacing));
        Shader.SetGlobalColor(BoatColourId, light.boatLightColour);
        PushShadows(light);
        PushGlow(light);
        PushSoulGlow(light);
        SoulPathGlow.PushFronts(light);
    }

    /// <summary>
    /// The baked soul path glow, laid out like the river glow's near points: the bricks, which
    /// route lights each point and from how far along it, the grid saying where each brick is, the
    /// grid's corner and point spacing (w — 0 means none baked), its size, and the atlas's size.
    /// </summary>
    private static void PushSoulGlow(LevelSelectLight light)
    {
        var atlas  = light.soulGlowAtlas;
        var routes = light.soulGlowRoutes;
        var bricks = light.soulGlowBricks;
        if (atlas == null || routes == null || bricks == null || light.soulGlowOrigin.w <= 0f)
        {
            PushNoSoulGlow();
            return;
        }

        Shader.SetGlobalTexture(SoulAtlasId, atlas);
        Shader.SetGlobalTexture(SoulRoutesId, routes);
        Shader.SetGlobalTexture(SoulBricksId, bricks);
        Shader.SetGlobalVector(SoulOriginId, light.soulGlowOrigin);
        Shader.SetGlobalVector(SoulGridId, new Vector4(bricks.width, bricks.height, bricks.depth, 0f));
        Shader.SetGlobalVector(SoulAtlasSizeId, new Vector4(atlas.width, atlas.height, atlas.depth, 0f));
    }

    private static void PushNoSoulGlow()
    {
        var none = NoGlowTexture();
        Shader.SetGlobalTexture(SoulAtlasId, none);
        Shader.SetGlobalTexture(SoulRoutesId, none);
        Shader.SetGlobalTexture(SoulBricksId, none);
        Shader.SetGlobalVector(SoulOriginId, Vector4.zero);
        Shader.SetGlobalVector(SoulGridId, Vector4.zero);
        Shader.SetGlobalVector(SoulAtlasSizeId, Vector4.one);
    }

    /// <summary>
    /// The baked river glow: the bricks and the grid saying where each one is, the grid's corner
    /// and point spacing (w — 0 means no glow, so the RiverGlow subgraph leaves everything as it
    /// is), the grid's size with the Glow Strength in w, and the atlas's size.
    /// </summary>
    private static void PushGlow(LevelSelectLight light)
    {
        var atlas  = light.riverGlowAtlas;
        var bricks = light.riverGlowBricks;
        if (atlas == null || bricks == null || light.riverGlowOrigin.w <= 0f)
        {
            PushNoGlow();
            return;
        }

        Shader.SetGlobalTexture(GlowAtlasId, atlas);
        Shader.SetGlobalTexture(GlowBricksId, bricks);
        Shader.SetGlobalVector(GlowOriginId, light.riverGlowOrigin);
        Shader.SetGlobalVector(GlowGridId,
            new Vector4(bricks.width, bricks.height, bricks.depth, light.glowStrength));
        Shader.SetGlobalVector(GlowAtlasSizeId, new Vector4(atlas.width, atlas.height, atlas.depth, 0f));

        // The far points, when the reach was long enough to need them.
        var far = light.riverGlowFarBricks;
        if (far != null && light.riverGlowFarOrigin.w > 0f)
        {
            Shader.SetGlobalTexture(GlowFarBricksId, far);
            Shader.SetGlobalVector(GlowFarOriginId, light.riverGlowFarOrigin);
            Shader.SetGlobalVector(GlowFarGridId, new Vector4(far.width, far.height, far.depth, 0f));
        }
        else
        {
            Shader.SetGlobalTexture(GlowFarBricksId, NoGlowTexture());
            Shader.SetGlobalVector(GlowFarOriginId, Vector4.zero);
            Shader.SetGlobalVector(GlowFarGridId, Vector4.zero);
        }
    }

    private static Texture3D NoGlowTexture()
    {
        if (_noGlow == null)
        {
            _noGlow = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false)
                      { name = "NoRiverGlow", hideFlags = HideFlags.HideAndDontSave };
            _noGlow.SetPixels32(new[] { new Color32(0, 0, 0, 0) });
            _noGlow.Apply(false, true);
        }
        return _noGlow;
    }

    private static void PushNoGlow()
    {
        var none = NoGlowTexture();
        Shader.SetGlobalTexture(GlowAtlasId, none);
        Shader.SetGlobalTexture(GlowBricksId, none);
        Shader.SetGlobalTexture(GlowFarBricksId, none);
        Shader.SetGlobalVector(GlowOriginId, Vector4.zero);
        Shader.SetGlobalVector(GlowGridId, Vector4.zero);
        Shader.SetGlobalVector(GlowFarOriginId, Vector4.zero);
        Shader.SetGlobalVector(GlowFarGridId, Vector4.zero);
        Shader.SetGlobalVector(GlowAtlasSizeId, Vector4.one);
    }

    /// <summary>
    /// The baked shadow map, where it was baked from and its size (w — 0 means no map, so the
    /// LevelSelectShadow subgraph leaves everything lit), and the Darkness and Edge Softness.
    /// </summary>
    private static void PushShadows(LevelSelectLight light)
    {
        var map = light.shadowMap;
        if (map == null)
        {
            if (_warnedNoShadows != light)
            {
                _warnedNoShadows = light;
                Debug.LogWarning($"[LevelSelectLight] '{light.name}' has no shadows baked, so there are " +
                                 "none — press Bake Shadows in the Lighting window.", light);
            }
            Shader.SetGlobalVector(ShadowFromId, Vector4.zero);
            Shader.SetGlobalTexture(ShadowMapId, Texture2D.whiteTexture);
        }
        else
        {
            Vector3 from = light.shadowBakedFrom;
            Shader.SetGlobalTexture(ShadowMapId, map);
            Shader.SetGlobalVector(ShadowFromId, new Vector4(from.x, from.y, from.z, map.width));
        }

        Shader.SetGlobalVector(ShadowParamsId,
            new Vector4(light.shadowDarkness, light.shadowEdgeSoftness, 0f, 0f));
    }
}
