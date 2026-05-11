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

    public static float SampleHeightSmooth(Vector3 worldPos, WaveParams p, float multiplier = 1f)
    {
        return SampleWaveSmooth(worldPos, p, multiplier) - SampleWhirlpoolDepth(worldPos, p);
    }

    public static Vector3 GetNormal(Vector3 worldPos, WaveParams p, float sampleOffset = 0.1f)
    {
        Vector3 p0 = worldPos;
        Vector3 p1 = worldPos + Vector3.right * sampleOffset;
        Vector3 p2 = worldPos + Vector3.forward * sampleOffset;

        p0.y = SampleHeightSmooth(p0, p);
        p1.y = SampleHeightSmooth(p1, p);
        p2.y = SampleHeightSmooth(p2, p);

        Vector3 v1 = p1 - p0;
        Vector3 v2 = p2 - p0;

        return Vector3.Cross(v2, v1).normalized;
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

    public static Vector3 GetWhirlpoolPull(Vector3 worldPos, float meshScale, float radiusMultiplier = 1f)
    {
        if (WhirlpoolManager.Instance == null) return Vector3.zero;
        return WhirlpoolManager.Instance.GetPullForceAt(worldPos, meshScale, radiusMultiplier);
    }
    }
