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
    public float TransitionSpeed = 2f;

    [Header("Map UI Wave Renderer")]
    public Renderer mapWaveRenderer;

    [Header("State Tracking")]
    [SerializeField] private WaveState targetGlobalState;
    [SerializeField] private WaveState currentGlobalState;

    private bool isModifierActive = false;
    private Vector4 modWaveCenter;
    private float modFreqBoost;
    private float modSpeedBoost;
    private float modRippleBoost;

    [Header("Debug")]
    [SerializeField] private bool logTransitions = false;

    private void Awake()
    {
        currentGlobalState = GetCurrentStateFromMaterial();
        targetGlobalState = currentGlobalState;
    }

    private WaveState ComputeEffectiveTarget()
    {
        if (!isModifierActive) return targetGlobalState;

        WaveState boosted = targetGlobalState;
        boosted.Frequency   += modFreqBoost;
        boosted.Speed       += modSpeedBoost;
        boosted.RippleDepth += modRippleBoost;
        boosted.WaveCenter   = modWaveCenter;
        return boosted;
    }

    private void Update()
    {
        if (!waveMaterial) return;

        currentGlobalState = LerpWaveState(currentGlobalState, ComputeEffectiveTarget(), TransitionSpeed * Time.deltaTime);
        ApplyCombinedState();
    }

    private void ApplyCombinedState()
    {
        waveMaterial.SetFloat("_Frequency",   currentGlobalState.Frequency);
        waveMaterial.SetFloat("_Speed",       currentGlobalState.Speed);
        waveMaterial.SetFloat("_RippleDepth", currentGlobalState.RippleDepth);

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

    private WaveState LerpWaveState(WaveState a, WaveState b, float t)
    {
        WaveState res = new WaveState();
        res.Frequency = Mathf.MoveTowards(a.Frequency, b.Frequency, t);
        res.Speed     = Mathf.MoveTowards(a.Speed, b.Speed, t);
        res.RippleDepth = Mathf.MoveTowards(a.RippleDepth, b.RippleDepth, t);
        res.Smoothness = Mathf.MoveTowards(a.Smoothness, b.Smoothness, t);
        res.Transparency = Mathf.MoveTowards(a.Transparency, b.Transparency, t);
        res.Strength = Mathf.MoveTowards(a.Strength, b.Strength, t);
        res.TroughRingWave1Brightness = Mathf.MoveTowards(a.TroughRingWave1Brightness, b.TroughRingWave1Brightness, t);
        res.PeakRingWave2Brightness = Mathf.MoveTowards(a.PeakRingWave2Brightness, b.PeakRingWave2Brightness, t);
        res.TwirlBaseStrength = Mathf.MoveTowards(a.TwirlBaseStrength, b.TwirlBaseStrength, t);
        res.TwirlSlopeBoost = Mathf.MoveTowards(a.TwirlSlopeBoost, b.TwirlSlopeBoost, t);
        res.TwirlScale = Mathf.MoveTowards(a.TwirlScale, b.TwirlScale, t);
        res.DepthTwirlStrength = Mathf.MoveTowards(a.DepthTwirlStrength, b.DepthTwirlStrength, t);
        res.DepthFadeStrength = Mathf.MoveTowards(a.DepthFadeStrength, b.DepthFadeStrength, t);
        res.DepthFadeLine = Mathf.MoveTowards(a.DepthFadeLine, b.DepthFadeLine, t);
        res.WhirlpoolTwirlStrength = Mathf.MoveTowards(a.WhirlpoolTwirlStrength, b.WhirlpoolTwirlStrength, t);
        res.WhirlpoolAreaTwirlStrength = Mathf.MoveTowards(a.WhirlpoolAreaTwirlStrength, b.WhirlpoolAreaTwirlStrength, t);
        res.SoulFishTwirlStrength = Mathf.MoveTowards(a.SoulFishTwirlStrength, b.SoulFishTwirlStrength, t);
        res.SoulFishMaskStrength = Mathf.MoveTowards(a.SoulFishMaskStrength, b.SoulFishMaskStrength, t);
        res.SoulFishRadius = Mathf.MoveTowards(a.SoulFishRadius, b.SoulFishRadius, t);
        res.ZoneTiling = Vector2.MoveTowards(a.ZoneTiling, b.ZoneTiling, t);
        res.ZoneScrollSpeed = Mathf.MoveTowards(a.ZoneScrollSpeed, b.ZoneScrollSpeed, t);
        res.ZoneNoiseStrength = Mathf.MoveTowards(a.ZoneNoiseStrength, b.ZoneNoiseStrength, t);
        res.ZoneTexture = b.ZoneTexture;
        res.WhirlpoolTaper = Mathf.MoveTowards(a.WhirlpoolTaper, b.WhirlpoolTaper, t);
        res.WhirlpoolDarkRadiusMult = Mathf.MoveTowards(a.WhirlpoolDarkRadiusMult, b.WhirlpoolDarkRadiusMult, t);
        res.WhirlpoolDarkStrength = Mathf.MoveTowards(a.WhirlpoolDarkStrength, b.WhirlpoolDarkStrength, t);
        res.WhirlpoolFalloffPower = Mathf.MoveTowards(a.WhirlpoolFalloffPower, b.WhirlpoolFalloffPower, t);
        res.WaveStepRate = Mathf.MoveTowards(a.WaveStepRate, b.WaveStepRate, t);
        res.BaseColor = Color.Lerp(a.BaseColor, b.BaseColor, t);
        res.LightDirection = Vector3.MoveTowards(a.LightDirection, b.LightDirection, t);
        res.FoamDitherSize = Mathf.MoveTowards(a.FoamDitherSize, b.FoamDitherSize, t);
        res.FoamDistanceDepth = Mathf.MoveTowards(a.FoamDistanceDepth, b.FoamDistanceDepth, t);
        res.FoamDepthFade = Mathf.MoveTowards(a.FoamDepthFade, b.FoamDepthFade, t);
        res.FoamColor = Color.Lerp(a.FoamColor, b.FoamColor, t);
        res.FoamLine = Mathf.MoveTowards(a.FoamLine, b.FoamLine, t);
        res.DepthFade = Mathf.MoveTowards(a.DepthFade, b.DepthFade, t);
        res.DepthColour = Color.Lerp(a.DepthColour, b.DepthColour, t);
        res.DistanceDepth = Mathf.MoveTowards(a.DistanceDepth, b.DistanceDepth, t);
        res.NormalTexture = b.NormalTexture;
        res.WaveCenter = Vector4.MoveTowards(a.WaveCenter, b.WaveCenter, t);
        return res;
    }

    public void SetModifierBoost(bool active, Vector4 center, float freq, float speed, float ripple)
    {
        isModifierActive = active;
        modWaveCenter = center;
        modFreqBoost = freq;
        modSpeedBoost = speed;
        modRippleBoost = ripple;
    }

    public void ApplyStateInstant(WaveState state)
    {
        targetGlobalState = state;
        currentGlobalState = state;
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