using UnityEngine;

// CPU implementation of WavesAndWhirlpools.hlsl for use by game objects.
// Matches the shader exactly: stepped XZ distance, same sine formula, same whirlpool falloff.
public static class WaveUtils
{
    static readonly int FreqID  = Shader.PropertyToID("_Frequency");
    static readonly int SpeedID = Shader.PropertyToID("_Speed");
    static readonly int StepID  = Shader.PropertyToID("_WaveStepRate");
    static readonly int DepthID = Shader.PropertyToID("_RippleDepth");

    public struct WaveParams
    {
        public float   frequency;
        public float   speed;
        public float   stepRate;
        public float   ripple;
        public float   meshScale;
        public Vector3 origin;
    }

    public static WaveParams ReadParams(Transform waterTransform, Material mat)
    {
        return new WaveParams
        {
            frequency = mat.GetFloat(FreqID),
            speed     = mat.GetFloat(SpeedID),
            stepRate  = mat.GetFloat(StepID),
            ripple    = mat.GetFloat(DepthID),
            meshScale = waterTransform.localScale.x,
            origin    = waterTransform.position,
        };
    }

    // World-space Y height offset from the wave at worldPos.
    // Uses stepped XZ distance to match the HLSL vertex shader.
    public static float SampleWave(Vector3 worldPos, WaveParams p, float multiplier = 1f)
    {
        float dx      = (worldPos.x - p.origin.x) / p.meshScale;
        float dz      = (worldPos.z - p.origin.z) / p.meshScale;
        float dist    = Mathf.Sqrt(dx * dx + dz * dz);
        float stepped = Mathf.Floor(dist * p.stepRate) / Mathf.Max(p.stepRate, 0.0001f);
        return -Mathf.Sin(stepped * p.frequency - Time.time * p.speed) * p.ripple * p.meshScale * multiplier;
    }

    // Smooth version — no stepping, for boat visual positioning.
    public static float SampleWaveSmooth(Vector3 worldPos, WaveParams p, float multiplier = 1f)
    {
        float dx   = (worldPos.x - p.origin.x) / p.meshScale;
        float dz   = (worldPos.z - p.origin.z) / p.meshScale;
        float dist = Mathf.Sqrt(dx * dx + dz * dz);
        return -Mathf.Sin(dist * p.frequency - Time.time * p.speed) * p.ripple * p.meshScale * multiplier;
    }

    // World-space Y depression from whirlpools at worldPos.
    // Matches the smooth-step falloff in WavesAndWhirlpools.hlsl.
    public static float SampleWhirlpoolDepth(Vector3 worldPos, WaveParams p)
    {
        if (WhirlpoolManager.Instance == null) return 0f;
        return WhirlpoolManager.Instance.SampleDepthAt(worldPos, p.meshScale);
    }

    // Combined wave height minus whirlpool depression — use this for final Y positioning.
    public static float SampleHeight(Vector3 worldPos, WaveParams p, float multiplier = 1f)
    {
        return SampleWave(worldPos, p, multiplier) - SampleWhirlpoolDepth(worldPos, p);
    }
}
