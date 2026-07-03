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
        public Vector2 ZoneTiling;
        public float ZoneScrollSpeed;
        public float ZoneNoiseStrength;
        public Texture2D ZoneTexture;

        public float WhirlpoolTaper;
        public float WhirlpoolDarkRadiusMult;
        public float WhirlpoolDarkStrength;
        public float WhirlpoolFalloffPower;

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

    [Header("Wave Material (Shared Instance)")]
    public Material waveMaterial;

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
        res.ZoneTiling.x = Mathf.SmoothDamp(current.ZoneTiling.x, target.ZoneTiling.x, ref vel.ZoneTiling.x, smoothTime);
        res.ZoneTiling.y = Mathf.SmoothDamp(current.ZoneTiling.y, target.ZoneTiling.y, ref vel.ZoneTiling.y, smoothTime);
        res.ZoneScrollSpeed = Mathf.SmoothDamp(current.ZoneScrollSpeed, target.ZoneScrollSpeed, ref vel.ZoneScrollSpeed, smoothTime);
        res.ZoneNoiseStrength = Mathf.SmoothDamp(current.ZoneNoiseStrength, target.ZoneNoiseStrength, ref vel.ZoneNoiseStrength, smoothTime);
        res.WhirlpoolTaper = Mathf.SmoothDamp(current.WhirlpoolTaper, target.WhirlpoolTaper, ref vel.WhirlpoolTaper, smoothTime);
        res.WhirlpoolDarkRadiusMult = Mathf.SmoothDamp(current.WhirlpoolDarkRadiusMult, target.WhirlpoolDarkRadiusMult, ref vel.WhirlpoolDarkRadiusMult, smoothTime);
        res.WhirlpoolDarkStrength = Mathf.SmoothDamp(current.WhirlpoolDarkStrength, target.WhirlpoolDarkStrength, ref vel.WhirlpoolDarkStrength, smoothTime);
        res.WhirlpoolFalloffPower = Mathf.SmoothDamp(current.WhirlpoolFalloffPower, target.WhirlpoolFalloffPower, ref vel.WhirlpoolFalloffPower, smoothTime);
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
        waveMaterial.SetVector("_ZoneTiling",               currentGlobalState.ZoneTiling);
        waveMaterial.SetFloat("_ZoneScrollSpeed",           currentGlobalState.ZoneScrollSpeed);
        waveMaterial.SetFloat("_ZoneNoiseStrength",         currentGlobalState.ZoneNoiseStrength);
        if (currentGlobalState.ZoneTexture) waveMaterial.SetTexture("_ZoneTexture", currentGlobalState.ZoneTexture);
        waveMaterial.SetFloat("_WhirlpoolTaper",         currentGlobalState.WhirlpoolTaper);
        waveMaterial.SetFloat("_WhirlpoolDarkRadiusMult",currentGlobalState.WhirlpoolDarkRadiusMult);
        waveMaterial.SetFloat("_WhirlpoolDarkStrength",  currentGlobalState.WhirlpoolDarkStrength);
        waveMaterial.SetFloat("_WhirlpoolFalloffPower",  currentGlobalState.WhirlpoolFalloffPower);
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

        if (mapWaveRenderer) CopyWaveValuesTo(mapWaveRenderer);
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

    public void ApplyStateInstant(WaveState state)
    {
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
        targetGlobalState = preset.state;
        WaveLightController.Instance?.ApplyPreset(preset);
        yield break;
    }

    public IEnumerator TransitionToState(WaveState targetState, float duration)
    {
        targetGlobalState = targetState;
        generalSmoothTime = duration;
        yield break;
    }

    public void ApplySoulFishMaskSettings()
    {
        if (!waveMaterial) return;
        waveMaterial.SetFloat("_SoulFishRadius",   soulFishRadius);
        waveMaterial.SetFloat("_SoulFishStrength", soulFishStrength);
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
        s.ZoneTiling         = waveMaterial.GetVector("_ZoneTiling");
        s.ZoneScrollSpeed    = waveMaterial.GetFloat("_ZoneScrollSpeed");
        s.ZoneNoiseStrength  = waveMaterial.GetFloat("_ZoneNoiseStrength");
        s.WhirlpoolTaper         = waveMaterial.GetFloat("_WhirlpoolTaper");
        s.WhirlpoolDarkRadiusMult = waveMaterial.GetFloat("_WhirlpoolDarkRadiusMult");
        s.WhirlpoolDarkStrength   = waveMaterial.GetFloat("_WhirlpoolDarkStrength");
        s.WhirlpoolFalloffPower   = waveMaterial.GetFloat("_WhirlpoolFalloffPower");
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