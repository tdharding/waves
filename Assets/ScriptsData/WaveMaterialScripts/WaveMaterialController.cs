using UnityEngine;
using System.Collections;

public class WaveMaterialController : MonoBehaviour
{
    [System.Serializable]
    public struct WaveState
    {
        public float Frequency;
        public float Speed;
        public float RippleDepth;
        public float Smoothness;
        public float Transparency;
        public float Strength;

        public float TroughRingWave1Brightness;
        public float PeakRingWave2Brightness;

        public float TwirlBaseStrength;
        public float TwirlSlopeBoost;
        public float TwirlScale;
        public float DepthTwirlStrength;
        public float DepthFadeStrength;
        public float DepthFadeLine;
        public float WhirlpoolTwirlStrength;
        public float WhirlpoolAreaTwirlStrength;
        public float SoulFishTwirlStrength;
        public float SoulFishMaskStrength;
        public float SoulFishRadius;
        public float SoulFishBrightness1;
        // Soul-fish zone edge fringe. Rides through presets like every other wave value.
        // Legacy presets that predate these fields deserialize them as 0 — a zero noise
        // scale is never a real configuration, so it is treated as "unset" and back-filled
        // from the controller's inspector defaults (see NormalizeSoulFishEdgeNoise).
        public float SoulFishFadeStart;
        public float SoulFishEdgeNoiseStrength;
        public float SoulFishEdgeNoiseScale;
        public float SoulFishEdgeNoiseScale2;
        public float SoulFishEdgeNoiseSpeed;
        // Static width taper along the zone path — artistic channel shaping, not animation.
        // Strength 0 = constant width, so legacy presets deserializing to 0 are already correct.
        public float SoulFishTaperStrength;
        public float SoulFishTaperScale;
        public Vector2 ZoneTiling;
        public float ZoneScrollSpeed;
        public float ZoneNoiseStrength;
        public Texture2D ZoneTexture;

        public float WhirlpoolTaper;
        public float WhirlpoolDarkRadiusMult;
        public float WhirlpoolDarkStrength;
        public float WhirlpoolFalloffPower;

        // Rings around rocks. Bands travelling outward from every rock near the boat, on top of
        // the map-wide bands from _WaveCenter. These ride through presets like every other wave
        // value — tuning them live and never saving them is exactly how the soul-fish edge noise
        // came to vanish on the next preset load. Frequency 0 marks a preset that predates them
        // (a zero band count is never a real configuration), so NormalizeRockRings back-fills.
        public float RockRingStrength;
        public float RockRingFrequency;
        public float RockRingSpeed;
        public float RockRingSpread;
        public float RockRingFalloffPower;
        public float RockRingLobeStrength;
        // Rectify: 0 = the full sine, paired rings either side of neutral; +/-1 = single rings, the
        // sign picking which half survives (which depends on whether the graph reads a larger value
        // as brighter or darker). 0 is a real setting, so this can't be back-filled by its own
        // value — it rides on the Frequency guard like the rest.
        public float RockRingRectify;
        // Gradient-noise distortion. Strength 0 is a real setting (clean circles), but a zero scale
        // is not — the noise would degenerate to a constant — so NormalizeRockRings guards on scale.
        public float RockRingDistortStrength;
        public float RockRingDistortScale;
        // How much wider a band is by the end of its life. 1 = constant width; a preset predating
        // this deserializes as 0, which would mean "narrower than nothing" — so it rides the same
        // sentinel as the distortion it arrived beside, and the shader clamps it to 1 regardless.
        public float RockRingWidthMultiplier;
        // The LOD range is applied on the CPU by RockRingManager, not by the shader — the fade is
        // the same for every pixel of a rock, so working it out per pixel would be waste.
        public float RockRingLodRadius;
        public float RockRingLodFeather;

        public float WaveStepRate;
        public Color BaseColor;
        public Vector3 LightDirection;

        public float FoamDitherSize;
        public float FoamDistanceDepth;
        public float FoamDepthFade;
        public Color FoamColor;
        public float FoamLine;
        public float DepthFade;

        public Color DepthColour;
        public float DistanceDepth;

        public Texture2D NormalTexture;
        public Vector4 WaveCenter;
    }

    [Header("Soul Fish Mask")]
    public float soulFishRadius = 2f;
    public float soulFishStrength = 1f;

    [Header("Soul Fish Edge Noise")]
    [Tooltip("Fraction of the radius where the fade band begins. Below this the zone is solid.")]
    [Range(0f, 1f)] public float soulFishFadeStart = 0.35f;
    [Tooltip("How hard the animated noise erodes/extends the fade edge. 0 = clean edge.")]
    public float soulFishEdgeNoiseStrength = 0.25f;
    [Tooltip("World-space frequency of the large base octave (used at the inner fade).")]
    public float soulFishEdgeNoiseScale = 1f;
    [Tooltip("World-space frequency of the fine octave (used at the outer edge).")]
    public float soulFishEdgeNoiseScale2 = 4f;
    [Tooltip("Scroll speed of the animated edge fringe.")]
    public float soulFishEdgeNoiseSpeed = 0.2f;

    [Header("Soul Fish Zone Taper")]
    [Tooltip("Static width taper along the zone path — the channel narrows and opens over its course. " +
             "0 = constant width, 0.5 = pinches to roughly half at its narrowest. Not animated.")]
    [Range(0f, 1f)] public float soulFishTaperStrength = 0f;
    [Tooltip("Pinches per world unit along the path. Low = long lazy tapers, high = a beaded channel.")]
    public float soulFishTaperScale = 0.15f;

    [Header("Rock Rings")]
    [Tooltip("How far the bands push brightness either side of neutral. 0 = the effect is gone.")]
    public float rockRingStrength = 0.25f;
    [Tooltip("Bands between a rock's edge and the outer limit. Counted per rock rather than per " +
             "world unit, so boulders and pebbles show the same number of rings at their own size.")]
    public float rockRingFrequency = 3f;
    [Tooltip("How fast the bands travel outward. Accumulated on the CPU, so changing this mid-level " +
             "slides them instead of teleporting them.")]
    public float rockRingSpeed = 1.5f;
    [Tooltip("How far the bands reach past the rock, as a multiple of its waterline radius.")]
    public float rockRingSpread = 3f;
    [Tooltip("How hard the bands fade out toward that limit. Higher pulls them in tight to the rock.")]
    public float rockRingFalloffPower = 1.5f;
    [Tooltip("How much the rock's own carved flutes shape the ring. 0 = plain circles; 1 = its real " +
             "cross-section; above that exaggerates it.")]
    public float rockRingLobeStrength = 1f;
    [Tooltip("Flattens one half of each band, leaving single rings on water that keeps its own " +
             "brightness between them. 0 = the full paired wave. The SIGN picks which half survives: " +
             "use +1 where a larger value brightens the water, -1 where it darkens it.")]
    [Range(-1f, 1f)] public float rockRingRectify = 1f;
    [Tooltip("How far a gradient-noise field pushes the bands off true, as a fraction of the spread. " +
             "0 = clean circles.")]
    public float rockRingDistortStrength = 0.08f;
    [Tooltip("World-space frequency of that field. Low = long lazy wanders, high = a ragged edge.")]
    public float rockRingDistortScale = 0.35f;
    [Tooltip("How much wider a band has grown by the end of its life — from the rock (1x) out to " +
             "where it fades. 1 = every band the same width all the way; 3 = roughly three times as " +
             "wide by the time it goes, the way a ripple loses its edge as it spreads.")]
    public float rockRingWidthMultiplier = 1f;
    [Tooltip("Rocks further than this from the boat throw no bands at all.")]
    public float rockRingLodRadius = 18f;
    [Tooltip("Fraction of that distance a rock stays at full strength before fading out.")]
    [Range(0f, 1f)] public float rockRingLodFeather = 0.75f;

    [Header("Wave Material (Shared Instance)")]
    public Material waveMaterial;

    [Header("Water Surface Broadcast")]
    [Tooltip("Water plane transform. Publishes the wave surface as shader globals so other " +
             "materials (walls, rocks) can compute the underwater waterline. Defaults to this " +
             "GameObject's transform if left unset. Should match SonarController's waterTransform.")]
    public Transform waterPlaneTransform;

    // Globals consumed by WaterSurfaceHeight.hlsl. Distinct names from the sonar/water props
    // so this broadcast is fully self-contained and cannot collide with presets or the tuner.
    static readonly int SurfOriginID = Shader.PropertyToID("_WaterSurfaceOrigin");
    static readonly int SurfScaleID  = Shader.PropertyToID("_WaterSurfaceScale");
    static readonly int SurfFreqID   = Shader.PropertyToID("_WaterSurfaceFreq");
    static readonly int SurfRippleID = Shader.PropertyToID("_WaterSurfaceRipple");
    static readonly int SurfStepID   = Shader.PropertyToID("_WaterSurfaceStepRate");

    [Header("Transition Settings")]
    public float generalSmoothTime = 0.5f;

    [Header("Map UI Wave Renderer")]
    public Renderer mapWaveRenderer;

    [Header("State Tracking")]
    [SerializeField] private WaveState targetGlobalState;
    [SerializeField] private WaveState currentGlobalState;

    private WaveState _baselineState; // Tracks the moving baseline (presets)
    private float _modifierIntensity = 0f;
    private float _modIntensityVel;
    private float _speedVel;
    private float _freqVel;
    private float _rippleVel;
    private WaveState _generalVel; // Struct to track velocities for secondary properties

    private float _accumulatedPhase = 0f;
    private float _rockRingPhase = 0f;
    private const float TWO_PI = 6.283185f;

    private bool isModifierActive = false;
    private Vector4 modWaveCenter;
    // Persistent wave center, registered by a modifier at spawn so the wave already
    // emanates from it before it is activated. Independent of the boost-active state.
    private Vector4 persistentWaveCenter;
    private bool hasPersistentWaveCenter = false;
    private float modFreqBoost;
    private float modSpeedBoost;
    private float modRippleBoost;

    [Header("Debug")]
    [SerializeField] private bool logTransitions = false;

    private void Awake()
    {
        currentGlobalState = GetCurrentStateFromMaterial();
        targetGlobalState = currentGlobalState;
        _baselineState = currentGlobalState;

        if (waterPlaneTransform == null) waterPlaneTransform = transform;
    }

    private void Update()
    {
        if (!waveMaterial) return;

        // 1. Smoothly transition the modifier intensity factor (0 to 1)
        float targetInt = isModifierActive ? 1.0f : 0.0f;
        _modifierIntensity = Mathf.SmoothDamp(_modifierIntensity, targetInt, ref _modIntensityVel, generalSmoothTime);

        // 2. Smoothly transition the baseline state towards the target preset
        _baselineState = SmoothDampSecondaryProperties(_baselineState, targetGlobalState, ref _generalVel, generalSmoothTime);
        _baselineState.Speed     = Mathf.SmoothDamp(_baselineState.Speed,     targetGlobalState.Speed,     ref _speedVel,  generalSmoothTime);
        _baselineState.Frequency = Mathf.SmoothDamp(_baselineState.Frequency, targetGlobalState.Frequency, ref _freqVel,  generalSmoothTime);
        _baselineState.RippleDepth = Mathf.SmoothDamp(_baselineState.RippleDepth, targetGlobalState.RippleDepth, ref _rippleVel, generalSmoothTime);

        // 3. Proportional Scaling: Result = Baseline + (Boost * Intensity)
        currentGlobalState = _baselineState;
        currentGlobalState.Speed       += modSpeedBoost  * _modifierIntensity;
        currentGlobalState.Frequency   += modFreqBoost   * _modifierIntensity;
        currentGlobalState.RippleDepth += modRippleBoost * _modifierIntensity;
        
        // 4. Update WaveCenter (Instant swap when active). When a modifier has registered
        //    a persistent center at spawn, use it while idle instead of the baseline center.
        currentGlobalState.WaveCenter = isModifierActive ? modWaveCenter
                                      : hasPersistentWaveCenter ? persistentWaveCenter
                                      : _baselineState.WaveCenter;

        // 5. Accumulate phase for smooth vertex animation
        _accumulatedPhase += currentGlobalState.Speed * Time.deltaTime;
        if (_accumulatedPhase > TWO_PI) _accumulatedPhase -= TWO_PI;
        else if (_accumulatedPhase < 0) _accumulatedPhase += TWO_PI;

        // Same treatment for the rock rings, and for the same reason: accumulating on the CPU is
        // what lets the speed change mid-level without the bands jumping.
        _rockRingPhase += currentGlobalState.RockRingSpeed * Time.deltaTime;
        if (_rockRingPhase > TWO_PI) _rockRingPhase -= TWO_PI;
        else if (_rockRingPhase < 0) _rockRingPhase += TWO_PI;

        ApplyCombinedState();
    }

    private WaveState SmoothDampSecondaryProperties(WaveState current, WaveState target, ref WaveState vel, float smoothTime)
    {
        WaveState res = current;
        // Frequency, Speed, and RippleDepth are handled proportionally in Update()
        
        res.Smoothness = Mathf.SmoothDamp(current.Smoothness, target.Smoothness, ref vel.Smoothness, smoothTime);
        res.Transparency = Mathf.SmoothDamp(current.Transparency, target.Transparency, ref vel.Transparency, smoothTime);
        res.Strength = Mathf.SmoothDamp(current.Strength, target.Strength, ref vel.Strength, smoothTime);
        res.TroughRingWave1Brightness = Mathf.SmoothDamp(current.TroughRingWave1Brightness, target.TroughRingWave1Brightness, ref vel.TroughRingWave1Brightness, smoothTime);
        res.PeakRingWave2Brightness = Mathf.SmoothDamp(current.PeakRingWave2Brightness, target.PeakRingWave2Brightness, ref vel.PeakRingWave2Brightness, smoothTime);
        res.TwirlBaseStrength = Mathf.SmoothDamp(current.TwirlBaseStrength, target.TwirlBaseStrength, ref vel.TwirlBaseStrength, smoothTime);
        res.TwirlSlopeBoost = Mathf.SmoothDamp(current.TwirlSlopeBoost, target.TwirlSlopeBoost, ref vel.TwirlSlopeBoost, smoothTime);
        res.TwirlScale = Mathf.SmoothDamp(current.TwirlScale, target.TwirlScale, ref vel.TwirlScale, smoothTime);
        res.DepthTwirlStrength = Mathf.SmoothDamp(current.DepthTwirlStrength, target.DepthTwirlStrength, ref vel.DepthTwirlStrength, smoothTime);
        res.DepthFadeStrength = Mathf.SmoothDamp(current.DepthFadeStrength, target.DepthFadeStrength, ref vel.DepthFadeStrength, smoothTime);
        res.DepthFadeLine = Mathf.SmoothDamp(current.DepthFadeLine, target.DepthFadeLine, ref vel.DepthFadeLine, smoothTime);
        res.WhirlpoolTwirlStrength = Mathf.SmoothDamp(current.WhirlpoolTwirlStrength, target.WhirlpoolTwirlStrength, ref vel.WhirlpoolTwirlStrength, smoothTime);
        res.WhirlpoolAreaTwirlStrength = Mathf.SmoothDamp(current.WhirlpoolAreaTwirlStrength, target.WhirlpoolAreaTwirlStrength, ref vel.WhirlpoolAreaTwirlStrength, smoothTime);
        res.SoulFishTwirlStrength = Mathf.SmoothDamp(current.SoulFishTwirlStrength, target.SoulFishTwirlStrength, ref vel.SoulFishTwirlStrength, smoothTime);
        res.SoulFishMaskStrength = Mathf.SmoothDamp(current.SoulFishMaskStrength, target.SoulFishMaskStrength, ref vel.SoulFishMaskStrength, smoothTime);
        res.SoulFishRadius = Mathf.SmoothDamp(current.SoulFishRadius, target.SoulFishRadius, ref vel.SoulFishRadius, smoothTime);
        res.SoulFishBrightness1 = Mathf.SmoothDamp(current.SoulFishBrightness1, target.SoulFishBrightness1, ref vel.SoulFishBrightness1, smoothTime);
        res.SoulFishFadeStart = Mathf.SmoothDamp(current.SoulFishFadeStart, target.SoulFishFadeStart, ref vel.SoulFishFadeStart, smoothTime);
        res.SoulFishEdgeNoiseStrength = Mathf.SmoothDamp(current.SoulFishEdgeNoiseStrength, target.SoulFishEdgeNoiseStrength, ref vel.SoulFishEdgeNoiseStrength, smoothTime);
        res.SoulFishEdgeNoiseScale = Mathf.SmoothDamp(current.SoulFishEdgeNoiseScale, target.SoulFishEdgeNoiseScale, ref vel.SoulFishEdgeNoiseScale, smoothTime);
        res.SoulFishEdgeNoiseScale2 = Mathf.SmoothDamp(current.SoulFishEdgeNoiseScale2, target.SoulFishEdgeNoiseScale2, ref vel.SoulFishEdgeNoiseScale2, smoothTime);
        res.SoulFishTaperStrength = Mathf.SmoothDamp(current.SoulFishTaperStrength, target.SoulFishTaperStrength, ref vel.SoulFishTaperStrength, smoothTime);
        res.SoulFishTaperScale = Mathf.SmoothDamp(current.SoulFishTaperScale, target.SoulFishTaperScale, ref vel.SoulFishTaperScale, smoothTime);
        res.SoulFishEdgeNoiseSpeed = Mathf.SmoothDamp(current.SoulFishEdgeNoiseSpeed, target.SoulFishEdgeNoiseSpeed, ref vel.SoulFishEdgeNoiseSpeed, smoothTime);
        res.ZoneTiling.x = Mathf.SmoothDamp(current.ZoneTiling.x, target.ZoneTiling.x, ref vel.ZoneTiling.x, smoothTime);
        res.ZoneTiling.y = Mathf.SmoothDamp(current.ZoneTiling.y, target.ZoneTiling.y, ref vel.ZoneTiling.y, smoothTime);
        res.ZoneScrollSpeed = Mathf.SmoothDamp(current.ZoneScrollSpeed, target.ZoneScrollSpeed, ref vel.ZoneScrollSpeed, smoothTime);
        res.ZoneNoiseStrength = Mathf.SmoothDamp(current.ZoneNoiseStrength, target.ZoneNoiseStrength, ref vel.ZoneNoiseStrength, smoothTime);
        res.WhirlpoolTaper = Mathf.SmoothDamp(current.WhirlpoolTaper, target.WhirlpoolTaper, ref vel.WhirlpoolTaper, smoothTime);
        res.WhirlpoolDarkRadiusMult = Mathf.SmoothDamp(current.WhirlpoolDarkRadiusMult, target.WhirlpoolDarkRadiusMult, ref vel.WhirlpoolDarkRadiusMult, smoothTime);
        res.WhirlpoolDarkStrength = Mathf.SmoothDamp(current.WhirlpoolDarkStrength, target.WhirlpoolDarkStrength, ref vel.WhirlpoolDarkStrength, smoothTime);
        res.WhirlpoolFalloffPower = Mathf.SmoothDamp(current.WhirlpoolFalloffPower, target.WhirlpoolFalloffPower, ref vel.WhirlpoolFalloffPower, smoothTime);
        res.RockRingStrength = Mathf.SmoothDamp(current.RockRingStrength, target.RockRingStrength, ref vel.RockRingStrength, smoothTime);
        res.RockRingFrequency = Mathf.SmoothDamp(current.RockRingFrequency, target.RockRingFrequency, ref vel.RockRingFrequency, smoothTime);
        res.RockRingSpeed = Mathf.SmoothDamp(current.RockRingSpeed, target.RockRingSpeed, ref vel.RockRingSpeed, smoothTime);
        res.RockRingSpread = Mathf.SmoothDamp(current.RockRingSpread, target.RockRingSpread, ref vel.RockRingSpread, smoothTime);
        res.RockRingFalloffPower = Mathf.SmoothDamp(current.RockRingFalloffPower, target.RockRingFalloffPower, ref vel.RockRingFalloffPower, smoothTime);
        res.RockRingLobeStrength = Mathf.SmoothDamp(current.RockRingLobeStrength, target.RockRingLobeStrength, ref vel.RockRingLobeStrength, smoothTime);
        res.RockRingRectify = Mathf.SmoothDamp(current.RockRingRectify, target.RockRingRectify, ref vel.RockRingRectify, smoothTime);
        res.RockRingDistortStrength = Mathf.SmoothDamp(current.RockRingDistortStrength, target.RockRingDistortStrength, ref vel.RockRingDistortStrength, smoothTime);
        res.RockRingDistortScale = Mathf.SmoothDamp(current.RockRingDistortScale, target.RockRingDistortScale, ref vel.RockRingDistortScale, smoothTime);
        res.RockRingWidthMultiplier = Mathf.SmoothDamp(current.RockRingWidthMultiplier, target.RockRingWidthMultiplier, ref vel.RockRingWidthMultiplier, smoothTime);
        res.RockRingLodRadius = Mathf.SmoothDamp(current.RockRingLodRadius, target.RockRingLodRadius, ref vel.RockRingLodRadius, smoothTime);
        res.RockRingLodFeather = Mathf.SmoothDamp(current.RockRingLodFeather, target.RockRingLodFeather, ref vel.RockRingLodFeather, smoothTime);
        res.WaveStepRate = Mathf.SmoothDamp(current.WaveStepRate, target.WaveStepRate, ref vel.WaveStepRate, smoothTime);
        
        // Specialized LERPs for Colors/Vectors (Damping approach)
        float t = (1f / Mathf.Max(smoothTime, 0.01f)) * Time.deltaTime;
        res.BaseColor = Color.Lerp(current.BaseColor, target.BaseColor, t);
        res.LightDirection = Vector3.Lerp(current.LightDirection, target.LightDirection, t);
        res.FoamColor = Color.Lerp(current.FoamColor, target.FoamColor, t);
        res.DepthColour = Color.Lerp(current.DepthColour, target.DepthColour, t);
        res.WaveCenter = Vector4.Lerp(current.WaveCenter, target.WaveCenter, t);

        res.FoamDitherSize = Mathf.SmoothDamp(current.FoamDitherSize, target.FoamDitherSize, ref vel.FoamDitherSize, smoothTime);
        res.FoamDistanceDepth = Mathf.SmoothDamp(current.FoamDistanceDepth, target.FoamDistanceDepth, ref vel.FoamDistanceDepth, smoothTime);
        res.FoamDepthFade = Mathf.SmoothDamp(current.FoamDepthFade, target.FoamDepthFade, ref vel.FoamDepthFade, smoothTime);
        res.FoamLine = Mathf.SmoothDamp(current.FoamLine, target.FoamLine, ref vel.FoamLine, smoothTime);
        res.DepthFade = Mathf.SmoothDamp(current.DepthFade, target.DepthFade, ref vel.DepthFade, smoothTime);
        res.DistanceDepth = Mathf.SmoothDamp(current.DistanceDepth, target.DistanceDepth, ref vel.DistanceDepth, smoothTime);
        
        res.NormalTexture = target.NormalTexture;
        res.ZoneTexture = target.ZoneTexture;
        
        return res;
    }

    private void ApplyCombinedState()
    {
        waveMaterial.SetFloat("_Frequency",   currentGlobalState.Frequency);
        waveMaterial.SetFloat("_Speed",       currentGlobalState.Speed);
        waveMaterial.SetFloat("_RippleDepth", currentGlobalState.RippleDepth);
        
        Shader.SetGlobalFloat("_WavePhase", _accumulatedPhase);

        // Global properties
        waveMaterial.SetFloat("_Smoothness",   currentGlobalState.Smoothness);
        waveMaterial.SetFloat("_Transparency", currentGlobalState.Transparency);
        waveMaterial.SetFloat("_Strength",     currentGlobalState.Strength);
        waveMaterial.SetFloat("_TroughRingWave1Brightness", currentGlobalState.TroughRingWave1Brightness);
        waveMaterial.SetFloat("_PeakRingWave2Brightness",   currentGlobalState.PeakRingWave2Brightness);
        waveMaterial.SetFloat("_TwirlBaseStrength",  currentGlobalState.TwirlBaseStrength);
        waveMaterial.SetFloat("_TwirlSlopeBoost",   currentGlobalState.TwirlSlopeBoost);
        waveMaterial.SetFloat("_TwirlScale",        currentGlobalState.TwirlScale);
        waveMaterial.SetFloat("_DepthTwirlStrength", currentGlobalState.DepthTwirlStrength);
        waveMaterial.SetFloat("_DepthFadeStrength",  currentGlobalState.DepthFadeStrength);
        waveMaterial.SetFloat("_DepthFadeLine",      currentGlobalState.DepthFadeLine);
        waveMaterial.SetFloat("_WhirlpoolTwirlStrength",     currentGlobalState.WhirlpoolTwirlStrength);
        waveMaterial.SetFloat("_WhirlpoolAreaTwirlStrength", currentGlobalState.WhirlpoolAreaTwirlStrength);
        waveMaterial.SetFloat("_SoulFishTwirlStrength",      currentGlobalState.SoulFishTwirlStrength);
        waveMaterial.SetFloat("_SoulFishMaskStrength",       currentGlobalState.SoulFishMaskStrength);
        waveMaterial.SetFloat("_SoulFishBrightness1",       currentGlobalState.SoulFishBrightness1);
        // Edge-fringe uniforms are bare $Globals in SoulFishWaveMask.hlsl (not blackboard
        // properties), so write them through both routes: per-material for the non-batched
        // path and globally for the SRP-batched path.
        SetGlobalsBackedFloat("_SoulFishFadeStart",         currentGlobalState.SoulFishFadeStart);
        SetGlobalsBackedFloat("_SoulFishEdgeNoiseStrength", currentGlobalState.SoulFishEdgeNoiseStrength);
        SetGlobalsBackedFloat("_SoulFishEdgeNoiseScale",    currentGlobalState.SoulFishEdgeNoiseScale);
        SetGlobalsBackedFloat("_SoulFishEdgeNoiseScale2",   currentGlobalState.SoulFishEdgeNoiseScale2);
        SetGlobalsBackedFloat("_SoulFishTaperStrength",     currentGlobalState.SoulFishTaperStrength);
        SetGlobalsBackedFloat("_SoulFishTaperScale",        currentGlobalState.SoulFishTaperScale);
        SetGlobalsBackedFloat("_SoulFishEdgeNoiseSpeed",    currentGlobalState.SoulFishEdgeNoiseSpeed);
        waveMaterial.SetVector("_ZoneTiling",               currentGlobalState.ZoneTiling);
        waveMaterial.SetFloat("_ZoneScrollSpeed",           currentGlobalState.ZoneScrollSpeed);
        waveMaterial.SetFloat("_ZoneNoiseStrength",         currentGlobalState.ZoneNoiseStrength);
        if (currentGlobalState.ZoneTexture) waveMaterial.SetTexture("_ZoneTexture", currentGlobalState.ZoneTexture);
        waveMaterial.SetFloat("_WhirlpoolTaper",         currentGlobalState.WhirlpoolTaper);
        waveMaterial.SetFloat("_WhirlpoolDarkRadiusMult",currentGlobalState.WhirlpoolDarkRadiusMult);
        waveMaterial.SetFloat("_WhirlpoolDarkStrength",  currentGlobalState.WhirlpoolDarkStrength);
        waveMaterial.SetFloat("_WhirlpoolFalloffPower",  currentGlobalState.WhirlpoolFalloffPower);
        // Rock rings. Bare $Globals in RockRings.hlsl, so both routes — per-material for the
        // non-batched path, globally for the SRP-batched one — same as the soul-fish fringe above.
        SetGlobalsBackedFloat("_RockRingStrength",     currentGlobalState.RockRingStrength);
        SetGlobalsBackedFloat("_RockRingFrequency",    currentGlobalState.RockRingFrequency);
        SetGlobalsBackedFloat("_RockRingSpread",       currentGlobalState.RockRingSpread);
        SetGlobalsBackedFloat("_RockRingFalloffPower", currentGlobalState.RockRingFalloffPower);
        SetGlobalsBackedFloat("_RockRingLobeStrength", currentGlobalState.RockRingLobeStrength);
        SetGlobalsBackedFloat("_RockRingRectify",      currentGlobalState.RockRingRectify);
        SetGlobalsBackedFloat("_RockRingDistortStrength", currentGlobalState.RockRingDistortStrength);
        SetGlobalsBackedFloat("_RockRingDistortScale",    currentGlobalState.RockRingDistortScale);
        SetGlobalsBackedFloat("_RockRingWidthMultiplier", currentGlobalState.RockRingWidthMultiplier);
        SetGlobalsBackedFloat("_RockRingPhase",        _rockRingPhase);
        // The LOD range never reaches the shader — RockRingManager applies it when it decides
        // which rocks to push at all, which is where the saving actually is.
        RockRingManager.SetLod(currentGlobalState.RockRingLodRadius, currentGlobalState.RockRingLodFeather);

        waveMaterial.SetFloat("_WaveStepRate", currentGlobalState.WaveStepRate);
        waveMaterial.SetColor("_BaseColor",    currentGlobalState.BaseColor);
        waveMaterial.SetVector("_LightDirection", currentGlobalState.LightDirection);
        waveMaterial.SetFloat("_FoamDitherSize",    currentGlobalState.FoamDitherSize);
        waveMaterial.SetFloat("_FoamDistanceDepth", currentGlobalState.FoamDistanceDepth);
        waveMaterial.SetFloat("_FoamDepthFade",     currentGlobalState.FoamDepthFade);
        waveMaterial.SetColor("_FoamColor",  currentGlobalState.FoamColor);
        waveMaterial.SetFloat("_FoamLine",  currentGlobalState.FoamLine);
        waveMaterial.SetFloat("_DepthFade", currentGlobalState.DepthFade);
        waveMaterial.SetColor("_DepthColour",   currentGlobalState.DepthColour);
        waveMaterial.SetFloat("_DistanceDepth", currentGlobalState.DistanceDepth);
        if (currentGlobalState.NormalTexture) waveMaterial.SetTexture("_NormalTexture", currentGlobalState.NormalTexture);

        waveMaterial.SetVector("_WaveCenter", currentGlobalState.WaveCenter);

        PublishSurfaceGlobals();

        if (mapWaveRenderer) CopyWaveValuesTo(mapWaveRenderer);
    }

    // Broadcasts the current wave surface as shader globals so any material (e.g. submerged
    // walls/rocks) can reconstruct the world-space waterline. Read-only mirror of the live
    // state — it never mutates presets, the material, or _WavePhase. Matches WaveUtils.SampleWave
    // exactly (same origin, mesh scale, freq, ripple, step rate), minus whirlpools.
    private void PublishSurfaceGlobals()
    {
        if (waterPlaneTransform == null) return;

        // _WaveCenter is stored in wave-plane object space (x=localX, z=-localY, -90°X plane).
        // TransformPoint returns the world-space origin; its Y is the flat base surface height.
        Vector4 wc = currentGlobalState.WaveCenter;
        Vector3 originWorld = waterPlaneTransform.TransformPoint(new Vector3(wc.x, -wc.z, 0f));

        Shader.SetGlobalVector(SurfOriginID, new Vector4(originWorld.x, originWorld.y, originWorld.z, 0f));
        Shader.SetGlobalFloat(SurfScaleID,  waterPlaneTransform.localScale.x);
        Shader.SetGlobalFloat(SurfFreqID,   currentGlobalState.Frequency);
        Shader.SetGlobalFloat(SurfRippleID, currentGlobalState.RippleDepth);
        Shader.SetGlobalFloat(SurfStepID,   currentGlobalState.WaveStepRate);
    }

    public void SetModifierBoost(bool active, Vector4 center, float freq, float speed, float ripple)
    {
        isModifierActive = active;
        modWaveCenter = center;
        modFreqBoost = freq;
        modSpeedBoost = speed;
        modRippleBoost = ripple;
    }

    // Registers a wave center that is applied immediately (at modifier spawn), independent
    // of whether the modifier has been activated. Used so the wave already originates from
    // the modifier at runtime rather than only once a soul arrives.
    public void SetModifierWaveCenter(Vector4 center)
    {
        persistentWaveCenter = center;
        hasPersistentWaveCenter = true;
    }

    private void SetGlobalsBackedFloat(string name, float value)
    {
        waveMaterial.SetFloat(name, value);
        Shader.SetGlobalFloat(name, value);
    }

    // Legacy WavePresets predate the edge-noise fields and deserialize them as 0. A zero
    // noise scale is never a real configuration (the noise degenerates to a constant), so
    // treat it as "unset" and back-fill all five from the inspector defaults.
    private WaveState NormalizeSoulFishEdgeNoise(WaveState s)
    {
        if (s.SoulFishEdgeNoiseScale <= 0f)
        {
            s.SoulFishFadeStart         = soulFishFadeStart;
            s.SoulFishEdgeNoiseStrength = soulFishEdgeNoiseStrength;
            s.SoulFishEdgeNoiseScale    = soulFishEdgeNoiseScale;
            s.SoulFishEdgeNoiseScale2   = soulFishEdgeNoiseScale2;
            s.SoulFishEdgeNoiseSpeed    = soulFishEdgeNoiseSpeed;
        }
        // Taper scale of 0 is never a real configuration (it would flatten the noise to a constant),
        // so treat it as "preset predates the taper" and fall back to the inspector default.
        if (s.SoulFishTaperScale <= 0.0001f)
        {
            s.SoulFishTaperStrength = soulFishTaperStrength;
            s.SoulFishTaperScale    = soulFishTaperScale;
        }
        return s;
    }

    // Presets authored before the rock rings existed deserialize all eight fields as 0. A zero
    // band count is never a real configuration (there would be nothing to see), so treat it as
    // "this preset predates the rings" and back-fill the lot from the inspector defaults. Same
    // reasoning as NormalizeSoulFishEdgeNoise above.
    private WaveState NormalizeRockRings(WaveState s)
    {
        if (s.RockRingFrequency <= 0.0001f)
        {
            s.RockRingStrength     = rockRingStrength;
            s.RockRingFrequency    = rockRingFrequency;
            s.RockRingSpeed        = rockRingSpeed;
            s.RockRingSpread       = rockRingSpread;
            s.RockRingFalloffPower = rockRingFalloffPower;
            s.RockRingLobeStrength = rockRingLobeStrength;
            s.RockRingRectify      = rockRingRectify;
            s.RockRingLodRadius    = rockRingLodRadius;
            s.RockRingLodFeather   = rockRingLodFeather;
        }
        // The distortion arrived after the rings did, so a preset saved in between carries a live
        // band count but a zero noise scale — which would collapse the field to a constant. Guarded
        // on its own, exactly like SoulFishTaperScale. Strength is left alone: 0 there means clean
        // circles, which is a real choice.
        if (s.RockRingDistortScale <= 0.0001f)
        {
            s.RockRingDistortStrength = rockRingDistortStrength;
            s.RockRingDistortScale    = rockRingDistortScale;
            s.RockRingWidthMultiplier = rockRingWidthMultiplier;
        }
        return s;
    }

    public void ApplyStateInstant(WaveState state)
    {
        state = NormalizeRockRings(NormalizeSoulFishEdgeNoise(state));
        targetGlobalState = state;
        _baselineState = state;
        currentGlobalState = state;
        _modifierIntensity = 0f;
        _modIntensityVel = 0f;
        _speedVel = 0f;
        _freqVel = 0f;
        _rippleVel = 0f;
        _generalVel = new WaveState();
        ApplyCombinedState();
    }

    public void CopyWaveValuesTo(Renderer targetRenderer)
    {
        if (!targetRenderer || !waveMaterial) return;
        Material targetMat = targetRenderer.material;
        targetMat.SetFloat("_RippleDepth", waveMaterial.GetFloat("_RippleDepth"));
        targetMat.SetFloat("_Frequency",   waveMaterial.GetFloat("_Frequency"));
        targetMat.SetFloat("_WaveStepRate", waveMaterial.GetFloat("_WaveStepRate"));
        targetMat.SetFloat("_Speed",       waveMaterial.GetFloat("_Speed"));
    }

    public void SyncMapWaves()
    {
        if (mapWaveRenderer) CopyWaveValuesTo(mapWaveRenderer);
    }

    public void ApplyPresetInstant(WavePreset preset)
    {
        if (preset == null) return;
        ApplyStateInstant(preset.state);
        WaveLightController.Instance?.ApplyPreset(preset);
        LevelAudioController.Instance?.OnPresetChanged(preset);
        LevelAudioController.Instance?.PlayMusic();
    }

    public IEnumerator TransitionToPreset(WavePreset preset)
    {
        if (preset == null) yield break;
        LevelAudioController.Instance?.OnPresetChanged(preset);
        targetGlobalState = NormalizeRockRings(NormalizeSoulFishEdgeNoise(preset.state));
        WaveLightController.Instance?.ApplyPreset(preset);
        yield break;
    }

    public IEnumerator TransitionToState(WaveState targetState, float duration)
    {
        targetGlobalState = NormalizeRockRings(NormalizeSoulFishEdgeNoise(targetState));
        generalSmoothTime = duration;
        yield break;
    }

    public void ApplySoulFishMaskSettings()
    {
        if (!waveMaterial) return;
        waveMaterial.SetFloat("_SoulFishRadius",   soulFishRadius);
        waveMaterial.SetFloat("_SoulFishStrength", soulFishStrength);
        waveMaterial.SetFloat("_SoulFishFadeStart",         soulFishFadeStart);
        waveMaterial.SetFloat("_SoulFishEdgeNoiseStrength", soulFishEdgeNoiseStrength);
        waveMaterial.SetFloat("_SoulFishEdgeNoiseScale",    soulFishEdgeNoiseScale);
        waveMaterial.SetFloat("_SoulFishEdgeNoiseScale2",   soulFishEdgeNoiseScale2);
        waveMaterial.SetFloat("_SoulFishEdgeNoiseSpeed",    soulFishEdgeNoiseSpeed);
        waveMaterial.SetFloat("_SoulFishTaperStrength",     soulFishTaperStrength);
        waveMaterial.SetFloat("_SoulFishTaperScale",        soulFishTaperScale);
    }

    private WaveState GetCurrentStateFromMaterial()
    {
        WaveState s = new WaveState();
        if (!waveMaterial) return s;

        s.Frequency   = waveMaterial.GetFloat("_Frequency");
        s.Speed       = waveMaterial.GetFloat("_Speed");
        s.RippleDepth = waveMaterial.GetFloat("_RippleDepth");
        s.Smoothness   = waveMaterial.GetFloat("_Smoothness");
        s.Transparency = waveMaterial.GetFloat("_Transparency");
        s.Strength     = waveMaterial.GetFloat("_Strength");
        s.TroughRingWave1Brightness = waveMaterial.GetFloat("_TroughRingWave1Brightness");
        s.PeakRingWave2Brightness   = waveMaterial.GetFloat("_PeakRingWave2Brightness");
        s.TwirlBaseStrength  = waveMaterial.GetFloat("_TwirlBaseStrength");
        s.TwirlSlopeBoost    = waveMaterial.GetFloat("_TwirlSlopeBoost");
        s.TwirlScale         = waveMaterial.GetFloat("_TwirlScale");
        s.DepthTwirlStrength = waveMaterial.GetFloat("_DepthTwirlStrength");
        s.DepthFadeStrength  = waveMaterial.GetFloat("_DepthFadeStrength");
        s.DepthFadeLine      = waveMaterial.GetFloat("_DepthFadeLine");
        s.WhirlpoolTwirlStrength     = waveMaterial.GetFloat("_WhirlpoolTwirlStrength");
        s.WhirlpoolAreaTwirlStrength = waveMaterial.GetFloat("_WhirlpoolAreaTwirlStrength");
        s.SoulFishTwirlStrength      = waveMaterial.GetFloat("_SoulFishTwirlStrength");
        s.SoulFishMaskStrength       = waveMaterial.GetFloat("_SoulFishMaskStrength");
        s.SoulFishBrightness1        = waveMaterial.GetFloat("_SoulFishBrightness1");
        s.SoulFishRadius             = soulFishRadius;
        s.SoulFishFadeStart          = soulFishFadeStart;
        s.SoulFishEdgeNoiseStrength  = soulFishEdgeNoiseStrength;
        s.SoulFishEdgeNoiseScale     = soulFishEdgeNoiseScale;
        s.SoulFishEdgeNoiseScale2    = soulFishEdgeNoiseScale2;
        s.SoulFishEdgeNoiseSpeed     = soulFishEdgeNoiseSpeed;
        s.SoulFishTaperStrength      = soulFishTaperStrength;
        s.SoulFishTaperScale         = soulFishTaperScale;
        s.ZoneTiling         = waveMaterial.GetVector("_ZoneTiling");
        s.ZoneScrollSpeed    = waveMaterial.GetFloat("_ZoneScrollSpeed");
        s.ZoneNoiseStrength  = waveMaterial.GetFloat("_ZoneNoiseStrength");
        s.WhirlpoolTaper         = waveMaterial.GetFloat("_WhirlpoolTaper");
        s.WhirlpoolDarkRadiusMult = waveMaterial.GetFloat("_WhirlpoolDarkRadiusMult");
        s.WhirlpoolDarkStrength   = waveMaterial.GetFloat("_WhirlpoolDarkStrength");
        s.WhirlpoolFalloffPower   = waveMaterial.GetFloat("_WhirlpoolFalloffPower");
        // Seeded from the inspector, not the material: these are bare $Globals with no serialized
        // home on the material to read back.
        s.RockRingStrength     = rockRingStrength;
        s.RockRingFrequency    = rockRingFrequency;
        s.RockRingSpeed        = rockRingSpeed;
        s.RockRingSpread       = rockRingSpread;
        s.RockRingFalloffPower = rockRingFalloffPower;
        s.RockRingLobeStrength = rockRingLobeStrength;
        s.RockRingRectify      = rockRingRectify;
        s.RockRingDistortStrength = rockRingDistortStrength;
        s.RockRingDistortScale    = rockRingDistortScale;
        s.RockRingWidthMultiplier = rockRingWidthMultiplier;
        s.RockRingLodRadius    = rockRingLodRadius;
        s.RockRingLodFeather   = rockRingLodFeather;
        s.WaveStepRate   = waveMaterial.GetFloat("_WaveStepRate");
        s.BaseColor      = waveMaterial.GetColor("_BaseColor");
        s.LightDirection = waveMaterial.GetVector("_LightDirection");
        s.FoamDitherSize    = waveMaterial.GetFloat("_FoamDitherSize");
        s.FoamDistanceDepth = waveMaterial.GetFloat("_FoamDistanceDepth");
        s.FoamDepthFade     = waveMaterial.GetFloat("_FoamDepthFade");
        s.FoamColor         = waveMaterial.GetColor("_FoamColor");
        s.FoamLine          = waveMaterial.GetFloat("_FoamLine");
        s.DepthFade         = waveMaterial.GetFloat("_DepthFade");
        s.DepthColour   = waveMaterial.GetColor("_DepthColour");
        s.DistanceDepth = waveMaterial.GetFloat("_DistanceDepth");
        s.WaveCenter    = waveMaterial.GetVector("_WaveCenter");
        return s;
    }
}