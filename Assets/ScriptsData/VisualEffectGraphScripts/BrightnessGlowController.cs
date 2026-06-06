using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BrightnessGlowController : MonoBehaviour
{
    [System.Serializable]
    public class GlowPoint
    {
        public Transform target;
        [Range(0f, 0.5f)] public float radius = 0.15f;
        [Range(0f, 0.15f)] public float softness = 0.05f;
        [Range(0f, 1f)] public float weight = 1f;
    }

    [Header("Brightness")]
    [SerializeField] Material glowMaterial;
    [SerializeField, Range(1f, 5f)] float brightnessIntensity = 1.5f;

    [Header("Burst Settings (UI / Soul Added)")]
    [SerializeField, Range(0f, 0.5f)] float burstRadius = 0.2f;
    [SerializeField, Range(0f, 0.15f)] float burstSoftness = 0.07f;
    [SerializeField, Range(0.1f, 5f)] float burstDuration = 1.5f;

    [Header("Capture Burst Settings (Fish Caught)")]
    [SerializeField] Vector2 captureRadiusRange = new Vector2(0.05f, 0.3f); // X = start, Y = end
    [SerializeField, Range(0f, 0.15f)] float captureSoftness = 0.08f;
    [SerializeField, Range(0.1f, 5f)] float captureDuration = 2f;
    [SerializeField, Range(1f, 10f)] float captureIntensity = 3f;

    [Header("Persistent Points")]
    [SerializeField] List<GlowPoint> persistentPoints = new();

    // Triggered glow (fish catch, events etc.)
    class TriggerEntry
    {
        public Vector3 worldPos;
        public Vector2 viewportPos;
        public bool    isViewportSpace;
        public float radiusStart;
        public float radiusEnd;
        public float softness;
        public float intensity;
        public float duration;
        public float elapsed;
    }

    static readonly List<TriggerEntry> triggers = new();
    static BrightnessGlowController instance;

    static readonly int GlowPointsPID          = Shader.PropertyToID("_GlowPoints");
    static readonly int GlowPointCountPID      = Shader.PropertyToID("_GlowPointCount");
    static readonly int BrightnessIntensityPID = Shader.PropertyToID("_BrightnessIntensity");

    readonly Vector4[] pointBuffer = new Vector4[8];

    void Awake() => instance = this;

    // Call from anywhere — e.g. on fish catch, orb collect, etc.
    public static void TriggerGlow(Vector3 worldPos, float duration, float radius = 0.2f, float softness = 0.07f)
    {
        triggers.Add(new TriggerEntry
        {
            worldPos    = worldPos,
            radiusStart = radius,
            radiusEnd   = 0f,
            softness    = softness,
            duration    = duration,
            elapsed     = 0f
        });
    }

    // Convenience overload for a Transform
    public static void TriggerGlow(Transform t, float duration, float radius = 0.2f, float softness = 0.07f)
        => TriggerGlow(t.position, duration, radius, softness);

    // World-space capture burst — uses Capture Burst Settings from inspector
    public static void TriggerCaptureGlow(Transform t)
    {
        Vector2 range  = instance != null ? instance.captureRadiusRange : new Vector2(0.05f, 0.3f);
        float softness = instance != null ? instance.captureSoftness    : 0.08f;
        float duration = instance != null ? instance.captureDuration    : 2f;
        float intensity = instance != null ? instance.captureIntensity : 3f;
        triggers.Add(new TriggerEntry
        {
            worldPos    = t.position,
            radiusStart = range.x,
            radiusEnd   = range.y,
            softness    = softness,
            intensity   = intensity,
            duration    = duration,
            elapsed     = 0f
        });
    }

    // For UI canvas elements (Screen Space Overlay) — uses inspector burst settings
    public static void TriggerGlowAtUI(RectTransform rt)
    {
        float r        = instance != null ? instance.burstRadius   : 0.2f;
        float softness = instance != null ? instance.burstSoftness : 0.07f;
        float duration = instance != null ? instance.burstDuration : 1.5f;

        Vector2 screenPos = rt.position;
        Vector2 vp = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);
        triggers.Add(new TriggerEntry
        {
            isViewportSpace = true,
            viewportPos     = vp,
            radiusStart     = r,
            radiusEnd       = 0f,
            softness        = softness,
            duration        = duration,
            elapsed         = 0f
        });
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;

        // Age and cull trigger entries
        for (int i = triggers.Count - 1; i >= 0; i--)
        {
            triggers[i].elapsed += Time.deltaTime;
            if (triggers[i].elapsed >= triggers[i].duration)
                triggers.RemoveAt(i);
        }

        int count = 0;

        foreach (var p in persistentPoints)
        {
            if (count >= 8) break;
            if (p.target == null || p.weight <= 0f) continue;

            Vector3 vp = cam.WorldToViewportPoint(p.target.position);
            if (vp.z <= 0f) continue;

            pointBuffer[count++] = new Vector4(vp.x, vp.y, p.radius * p.weight, p.softness);
        }

        foreach (var t in triggers)
        {
            if (count >= 8) break;

            Vector2 uv;
            if (t.isViewportSpace)
            {
                uv = t.viewportPos;
            }
            else
            {
                Vector3 vp = cam.WorldToViewportPoint(t.worldPos);
                if (vp.z <= 0f) continue;
                uv = vp;
            }

            float norm = Mathf.Clamp01(t.elapsed / t.duration);
            float r    = Mathf.Lerp(t.radiusStart, t.radiusEnd, norm);
            pointBuffer[count++] = new Vector4(uv.x, uv.y, r, t.softness);
        }

        Shader.SetGlobalVectorArray(GlowPointsPID, pointBuffer);
        Shader.SetGlobalFloat(GlowPointCountPID, count);

        if (glowMaterial != null)
        {
            float matIntensity = brightnessIntensity;
            foreach (var t in triggers)
            {
                float norm   = Mathf.Clamp01(t.elapsed / t.duration);
                float tValue = Mathf.Lerp(t.intensity, brightnessIntensity, norm);
                matIntensity = Mathf.Max(matIntensity, tValue);
            }
            glowMaterial.SetFloat(BrightnessIntensityPID, matIntensity);
        }
    }

    void OnDisable()
    {
        // Zero out when controller is off so effect disappears cleanly
        System.Array.Clear(pointBuffer, 0, pointBuffer.Length);
        Shader.SetGlobalVectorArray(GlowPointsPID, pointBuffer);
        Shader.SetGlobalFloat(GlowPointCountPID, 0f);
    }
}
