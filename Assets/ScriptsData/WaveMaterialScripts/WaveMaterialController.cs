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
    public float TransitionDuration = 2f;

    [Header("Wave Center")]
[SerializeField] private Transform wavePlaneTransform;

    // ─────────────────────────────────────────
    // WAVE STATES
    // ─────────────────────────────────────────

    [Header("RockyWaves1 State")]
    public WaveState RockyWaves1;

    [Header("RockyWaves2 State")]
    public WaveState RockyWaves2;

[Header("Map UI Wave Renderer")]
public Renderer mapWaveRenderer;

    // ─────────────────────────────────────────
    // INSTANT APPLY
    // ─────────────────────────────────────────
    public void ApplyStateInstant(WaveState state)
    {
        waveMaterial.SetFloat("_Frequency", state.Frequency);
        waveMaterial.SetFloat("_Speed", state.Speed);
        waveMaterial.SetFloat("_RippleDepth", state.RippleDepth);

        waveMaterial.SetFloat("_Smoothness", state.Smoothness);
        waveMaterial.SetFloat("_Transparency", state.Transparency);
        waveMaterial.SetFloat("_Strength", state.Strength);

        waveMaterial.SetFloat("_TroughRingWave1Brightness", state.TroughRingWave1Brightness);
        waveMaterial.SetFloat("_PeakRingWave2Brightness",   state.PeakRingWave2Brightness);

        waveMaterial.SetFloat("_TwirlBaseStrength", state.TwirlBaseStrength);
        waveMaterial.SetFloat("_TwirlSlopeBoost",  state.TwirlSlopeBoost);
        waveMaterial.SetFloat("_TwirlScale",       state.TwirlScale);
        waveMaterial.SetFloat("_DepthTwirlStrength", state.DepthTwirlStrength);
        waveMaterial.SetFloat("_DepthFadeStrength",  state.DepthFadeStrength);
        waveMaterial.SetFloat("_DepthFadeLine",      state.DepthFadeLine);
        waveMaterial.SetFloat("_WhirlpoolTwirlStrength",     state.WhirlpoolTwirlStrength);
        waveMaterial.SetFloat("_WhirlpoolAreaTwirlStrength", state.WhirlpoolAreaTwirlStrength);
        waveMaterial.SetFloat("_SoulFishTwirlStrength",      state.SoulFishTwirlStrength);

        waveMaterial.SetFloat("_WhirlpoolTaper",         state.WhirlpoolTaper);
        waveMaterial.SetFloat("_WhirlpoolDarkRadiusMult",state.WhirlpoolDarkRadiusMult);
        waveMaterial.SetFloat("_WhirlpoolDarkStrength",  state.WhirlpoolDarkStrength);
        waveMaterial.SetFloat("_WhirlpoolFalloffPower",  state.WhirlpoolFalloffPower);

        waveMaterial.SetFloat("_WaveStepRate", state.WaveStepRate);
        waveMaterial.SetColor("_BaseColor",    state.BaseColor);
        waveMaterial.SetVector("_LightDirection", state.LightDirection);

        waveMaterial.SetFloat("_FoamDitherSize",    state.FoamDitherSize);
        waveMaterial.SetFloat("_FoamDistanceDepth", state.FoamDistanceDepth);
        waveMaterial.SetFloat("_FoamDepthFade",     state.FoamDepthFade);
        waveMaterial.SetColor("_FoamColor",  state.FoamColor);
        waveMaterial.SetFloat("_FoamLine",  state.FoamLine);
        waveMaterial.SetFloat("_DepthFade", state.DepthFade);

        waveMaterial.SetColor("_DepthColour",   state.DepthColour);
        waveMaterial.SetFloat("_DistanceDepth", state.DistanceDepth);

        if (state.NormalTexture)
            waveMaterial.SetTexture("_NormalTexture", state.NormalTexture);

        waveMaterial.SetVector("_WaveCenter", state.WaveCenter);
    }

  



    // ─────────────────────────────────────────
    // TRANSITION (LERP OVER TIME)
    // ─────────────────────────────────────────
    public IEnumerator TransitionToState(WaveState targetState, float duration)
    {



        float timer = 0f;

        // Current values
        float curFreq      = waveMaterial.GetFloat("_Frequency");
        float curSpeed     = waveMaterial.GetFloat("_Speed");
        float curRipple    = waveMaterial.GetFloat("_RippleDepth");

        float curSmooth    = waveMaterial.GetFloat("_Smoothness");
        float curTrans     = waveMaterial.GetFloat("_Transparency");
        float curStrength  = waveMaterial.GetFloat("_Strength");

        float curTroughBright  = waveMaterial.GetFloat("_TroughRingWave1Brightness");
        float curPeakBright    = waveMaterial.GetFloat("_PeakRingWave2Brightness");

        float curTwirlBase          = waveMaterial.GetFloat("_TwirlBaseStrength");
        float curTwirlBoost         = waveMaterial.GetFloat("_TwirlSlopeBoost");
        float curTwirlScale         = waveMaterial.GetFloat("_TwirlScale");
        float curDepthTwirl         = waveMaterial.GetFloat("_DepthTwirlStrength");
        float curDepthFadeStrength  = waveMaterial.GetFloat("_DepthFadeStrength");
        float curDepthFadeLine      = waveMaterial.GetFloat("_DepthFadeLine");
        float curWhirlpoolTwirl     = waveMaterial.GetFloat("_WhirlpoolTwirlStrength");
        float curWhirlpoolAreaTwirl = waveMaterial.GetFloat("_WhirlpoolAreaTwirlStrength");
        float curSoulFishTwirl      = waveMaterial.GetFloat("_SoulFishTwirlStrength");

        float curWhirlpoolTaper     = waveMaterial.GetFloat("_WhirlpoolTaper");
        float curDarkRadiusMult     = waveMaterial.GetFloat("_WhirlpoolDarkRadiusMult");
        float curDarkStrength       = waveMaterial.GetFloat("_WhirlpoolDarkStrength");
        float curFalloffPower       = waveMaterial.GetFloat("_WhirlpoolFalloffPower");

        float curWaveStepRate       = waveMaterial.GetFloat("_WaveStepRate");
        Color curBaseColor          = waveMaterial.GetColor("_BaseColor");
        Vector3 curLightDir         = waveMaterial.GetVector("_LightDirection");

        float curFoamDitherSize     = waveMaterial.GetFloat("_FoamDitherSize");
        float curFoamDistanceDepth  = waveMaterial.GetFloat("_FoamDistanceDepth");
        float curFoamDepthFade      = waveMaterial.GetFloat("_FoamDepthFade");
        Color curFoamColor          = waveMaterial.GetColor("_FoamColor");
        float curFoamLine      = waveMaterial.GetFloat("_FoamLine");
        float curDepthFade     = waveMaterial.GetFloat("_DepthFade");

        Color curDepthColour        = waveMaterial.GetColor("_DepthColour");
        float curDistanceDepth      = waveMaterial.GetFloat("_DistanceDepth");

        // Rates
        float freqRate     = (targetState.Frequency   - curFreq)     / duration;
        float speedRate    = (targetState.Speed       - curSpeed)    / duration;
        float rippleRate   = (targetState.RippleDepth - curRipple)   / duration;

        float smoothRate   = (targetState.Smoothness  - curSmooth)   / duration;
        float transRate    = (targetState.Transparency- curTrans)    / duration;
        float strengthRate = (targetState.Strength    - curStrength) / duration;

        float troughBrightRate   = (targetState.TroughRingWave1Brightness - curTroughBright)   / duration;
        float peakBrightRate     = (targetState.PeakRingWave2Brightness   - curPeakBright)     / duration;

        float twirlBaseRate      = (targetState.TwirlBaseStrength - curTwirlBase)  / duration;
        float twirlBoostRate     = (targetState.TwirlSlopeBoost  - curTwirlBoost) / duration;
        float twirlScaleRate     = (targetState.TwirlScale       - curTwirlScale) / duration;
        float depthTwirlRate     = (targetState.DepthTwirlStrength - curDepthTwirl)     / duration;
        float depthFadeStrengthRate = (targetState.DepthFadeStrength - curDepthFadeStrength) / duration;
        float depthFadeLineRate  = (targetState.DepthFadeLine      - curDepthFadeLine)  / duration;
        float whirlpoolTwirlRate     = (targetState.WhirlpoolTwirlStrength     - curWhirlpoolTwirl)     / duration;
        float whirlpoolAreaTwirlRate = (targetState.WhirlpoolAreaTwirlStrength - curWhirlpoolAreaTwirl) / duration;
        float soulFishTwirlRate      = (targetState.SoulFishTwirlStrength      - curSoulFishTwirl)      / duration;

        float whirlpoolTaperRate  = (targetState.WhirlpoolTaper          - curWhirlpoolTaper) / duration;
        float darkRadiusMultRate  = (targetState.WhirlpoolDarkRadiusMult - curDarkRadiusMult)  / duration;
        float darkStrengthRate    = (targetState.WhirlpoolDarkStrength   - curDarkStrength)    / duration;
        float falloffPowerRate    = (targetState.WhirlpoolFalloffPower   - curFalloffPower)    / duration;

        float waveStepRate_rate  = (targetState.WaveStepRate   - curWaveStepRate)  / duration;
        float foamDitherSizeRate    = (targetState.FoamDitherSize    - curFoamDitherSize)   / duration;
        float foamDistanceDepthRate = (targetState.FoamDistanceDepth - curFoamDistanceDepth) / duration;
        float foamDepthFadeRate     = (targetState.FoamDepthFade     - curFoamDepthFade)     / duration;
        float foamLineRate      = (targetState.FoamLine  - curFoamLine)      / duration;
        float depthFadeRate     = (targetState.DepthFade - curDepthFade) / duration;
        float distanceDepthRate  = (targetState.DistanceDepth  - curDistanceDepth) / duration;

        while (timer < duration)
        {

           
            timer += Time.deltaTime;

            curFreq     += freqRate     * Time.deltaTime;
            curSpeed    += speedRate    * Time.deltaTime;
            curRipple   += rippleRate   * Time.deltaTime;

            curSmooth   += smoothRate   * Time.deltaTime;
            curTrans    += transRate    * Time.deltaTime;
            curStrength += strengthRate * Time.deltaTime;

            curTroughBright   += troughBrightRate   * Time.deltaTime;
            curPeakBright     += peakBrightRate     * Time.deltaTime;

            curTwirlBase      += twirlBaseRate  * Time.deltaTime;
            curTwirlBoost     += twirlBoostRate * Time.deltaTime;
            curTwirlScale     += twirlScaleRate * Time.deltaTime;
            curDepthTwirl     += depthTwirlRate    * Time.deltaTime;
            curDepthFadeStrength += depthFadeStrengthRate * Time.deltaTime;
            curDepthFadeLine     += depthFadeLineRate     * Time.deltaTime;
            curWhirlpoolTwirl     += whirlpoolTwirlRate     * Time.deltaTime;
            curWhirlpoolAreaTwirl += whirlpoolAreaTwirlRate * Time.deltaTime;
            curSoulFishTwirl      += soulFishTwirlRate      * Time.deltaTime;

            curWhirlpoolTaper += whirlpoolTaperRate * Time.deltaTime;
            curDarkRadiusMult += darkRadiusMultRate * Time.deltaTime;
            curDarkStrength   += darkStrengthRate   * Time.deltaTime;
            curFalloffPower   += falloffPowerRate   * Time.deltaTime;

            curWaveStepRate   += waveStepRate_rate  * Time.deltaTime;
            curFoamDitherSize    += foamDitherSizeRate    * Time.deltaTime;
            curFoamDistanceDepth += foamDistanceDepthRate * Time.deltaTime;
            curFoamDepthFade     += foamDepthFadeRate     * Time.deltaTime;
            curFoamLine      += foamLineRate      * Time.deltaTime;
            curDepthFade     += depthFadeRate     * Time.deltaTime;
            curDistanceDepth  += distanceDepthRate  * Time.deltaTime;

            float t = Mathf.Clamp01(timer / duration);
            curBaseColor   = Color.Lerp(curBaseColor,   targetState.BaseColor,   t);
            curFoamColor   = Color.Lerp(curFoamColor,   targetState.FoamColor,   t);
            curDepthColour = Color.Lerp(curDepthColour, targetState.DepthColour, t);

            waveMaterial.SetFloat("_Frequency", curFreq);
            waveMaterial.SetFloat("_Speed", curSpeed);
            waveMaterial.SetFloat("_RippleDepth", curRipple);

            waveMaterial.SetFloat("_Smoothness", curSmooth);
            waveMaterial.SetFloat("_Transparency", curTrans);
            waveMaterial.SetFloat("_Strength", curStrength);

            waveMaterial.SetFloat("_TroughRingWave1Brightness", curTroughBright);
            waveMaterial.SetFloat("_PeakRingWave2Brightness",   curPeakBright);

            waveMaterial.SetFloat("_TwirlBaseStrength", curTwirlBase);
            waveMaterial.SetFloat("_TwirlSlopeBoost",  curTwirlBoost);
            waveMaterial.SetFloat("_TwirlScale",       curTwirlScale);
            waveMaterial.SetFloat("_DepthTwirlStrength", curDepthTwirl);
            waveMaterial.SetFloat("_DepthFadeStrength",  curDepthFadeStrength);
            waveMaterial.SetFloat("_DepthFadeLine",      curDepthFadeLine);
            waveMaterial.SetFloat("_WhirlpoolTwirlStrength",     curWhirlpoolTwirl);
            waveMaterial.SetFloat("_WhirlpoolAreaTwirlStrength", curWhirlpoolAreaTwirl);
            waveMaterial.SetFloat("_SoulFishTwirlStrength",      curSoulFishTwirl);

            waveMaterial.SetFloat("_WhirlpoolTaper",          curWhirlpoolTaper);
            waveMaterial.SetFloat("_WhirlpoolDarkRadiusMult", curDarkRadiusMult);
            waveMaterial.SetFloat("_WhirlpoolDarkStrength",   curDarkStrength);
            waveMaterial.SetFloat("_WhirlpoolFalloffPower",   curFalloffPower);

            waveMaterial.SetFloat("_WaveStepRate", curWaveStepRate);
            waveMaterial.SetColor("_BaseColor",    curBaseColor);
            waveMaterial.SetVector("_LightDirection", Vector3.Lerp(curLightDir, targetState.LightDirection, Mathf.Clamp01(timer / duration)));

            waveMaterial.SetFloat("_FoamDitherSize",    curFoamDitherSize);
            waveMaterial.SetFloat("_FoamDistanceDepth", curFoamDistanceDepth);
            waveMaterial.SetFloat("_FoamDepthFade",     curFoamDepthFade);
            waveMaterial.SetColor("_FoamColor",  curFoamColor);
            waveMaterial.SetFloat("_FoamLine",  curFoamLine);
            waveMaterial.SetFloat("_DepthFade", curDepthFade);

            waveMaterial.SetColor("_DepthColour",   curDepthColour);
            waveMaterial.SetFloat("_DistanceDepth", curDistanceDepth);

             if (mapWaveRenderer)
            {
                 CopyWaveValuesTo(mapWaveRenderer);
            }


            yield return null;
        }

        // Final snap to exact target values
        ApplyStateInstant(targetState);

        if (mapWaveRenderer)
        {
             CopyWaveValuesTo(mapWaveRenderer);
        }

    }

public void CopyWaveValuesTo(Renderer targetRenderer)
{
    if (!targetRenderer || !waveMaterial)
        return;

    Material targetMat = targetRenderer.material;

    targetMat.SetFloat("_RippleDepth", waveMaterial.GetFloat("_RippleDepth"));
    targetMat.SetFloat("_Frequency",   waveMaterial.GetFloat("_Frequency"));
    targetMat.SetFloat("_WaveStepRate", waveMaterial.GetFloat("_WaveStepRate"));
    targetMat.SetFloat("_Speed",       waveMaterial.GetFloat("_Speed"));
}

public void SyncMapWaves()
{
    if (mapWaveRenderer)
    {
        CopyWaveValuesTo(mapWaveRenderer);
    }
}

public void ApplyPresetInstant(WavePreset preset)
{
    if (preset == null) return;

    ApplyStateInstant(preset.state);
    SyncMapWaves();
    WaveLightController.Instance?.ApplyPreset(preset);
    LevelAudioController.Instance?.OnPresetChanged(preset);
    LevelAudioController.Instance?.PlayMusic();
}

public IEnumerator TransitionToPreset(WavePreset preset)
{
    if (preset == null) yield break;

    LevelAudioController.Instance?.OnPresetChanged(preset);
    yield return TransitionToState(preset.state, TransitionDuration);
    WaveLightController.Instance?.ApplyPreset(preset);
}

public void ApplySoulFishMaskSettings()
{
    if (!waveMaterial) return;
    waveMaterial.SetFloat("_SoulFishRadius",   soulFishRadius);
    waveMaterial.SetFloat("_SoulFishStrength", soulFishStrength);
}


}
