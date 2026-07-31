using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class BrightnessGlowController : MonoBehaviour
{
    // Must equal GLOW_FX_MAX in GlowFXMask.hlsl.
    public const int MAX_POINTS = 16;

    // GlowPoint and GlowSettings deliberately duplicate their four knobs rather than sharing a base
    // class: Unity draws inherited fields ABOVE the subclass's own, which would shove Target to the
    // bottom of every element in the Persistent Points list.

    [System.Serializable]
    public class GlowPoint
    {
        public Transform target;
        [Range(0f, 0.5f)] public float radius = 0.15f;
        [Range(0f, 0.15f)] public float softness = 0.05f;

        [Tooltip("Scales this point's radius — how BIG it is.")]
        [Range(0f, 1f)] public float weight = 1f;

        [Tooltip("Scales this point's mask value — how STRONG it is. Independent of the master " +
                 "Opacity slider, which multiplies every point.")]
        [Range(0f, 1f)] public float opacity = 1f;
    }

    /// <summary>Same four knobs, for emitters that supply their own position (street lights).</summary>
    [System.Serializable]
    public class GlowSettings
    {
        [Range(0f, 0.5f)] public float radius = 0.08f;
        [Range(0f, 0.15f)] public float softness = 0.05f;

        [Tooltip("Scales the radius — how BIG each point is.")]
        [Range(0f, 1f)] public float weight = 1f;

        [Tooltip("Scales the mask value — how STRONG each point is. Independent of the master " +
                 "Opacity slider, which multiplies every point.")]
        [Range(0f, 1f)] public float opacity = 0.6f;
    }

    [Header("Brightness")]
    [SerializeField] Material glowMaterial;
    [SerializeField, Range(1f, 5f)] float brightnessIntensity = 1.5f;

    [Tooltip("Master fade for the whole glow mask. Multiplied into the mask inside GlowFXMask.hlsl, " +
             "so it scales however the graph consumes the mask (currently an Add).")]
    [SerializeField, Range(0f, 1f)] float opacity = 1f;

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

    [Header("Street Lights")]
    [Tooltip("Give every lit street light a persistent glow point. The lights are found automatically " +
             "via StreetLightController.LitLights — nothing to wire, and nothing to tune on the prefab.")]
    [SerializeField] bool streetLightGlow = true;

    [Tooltip("Shared settings for every lit street light — same four knobs as a persistent point, " +
             "minus the target (the lamps supply that).")]
    [SerializeField] GlowSettings streetLights = new();

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
    static readonly int GlowPointParamsPID     = Shader.PropertyToID("_GlowPointParams");
    static readonly int GlowPointCountPID      = Shader.PropertyToID("_GlowPointCount");
    static readonly int GlowOpacityPID         = Shader.PropertyToID("_GlowOpacity");
    static readonly int BrightnessIntensityPID = Shader.PropertyToID("_BrightnessIntensity");

    readonly Vector4[] pointBuffer = new Vector4[MAX_POINTS];   // xy = uv, z = radius, w = softness
    readonly Vector4[] paramBuffer = new Vector4[MAX_POINTS];   // x  = per-point opacity
    int lastBuiltFrame = -1;

    /// <summary>Master fade, 0-1. Settable from code for fades; mirrors the inspector slider.</summary>
    public static float Opacity
    {
        get => instance != null ? instance.opacity : 1f;
        set { if (instance != null) instance.opacity = Mathf.Clamp01(value); }
    }

    // Domain-reload-safe: statics survive play-mode exit when "Reload Domain" is off.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        triggers.Clear();
        instance = null;
    }

    void Awake()
    {
        instance = this;

        // Auto-assign the boat transform to persistentPoints[0] if it has no target set
        if (persistentPoints.Count > 0 && persistentPoints[0].target == null)
        {
            var boatGo = GameObject.Find("LevelSelectBoat");
            if (boatGo != null)
            {
                persistentPoints[0].target = boatGo.transform;
                Debug.Log("[BrightnessGlowController] Auto-assigned LevelSelectBoat to persistentPoints[0].");
            }
            else
            {
                Debug.LogWarning("[BrightnessGlowController] persistentPoints[0] has no target and 'LevelSelectBoat' not found in scene.");
            }
        }
    }

    // ============================================================
    // TRIGGERED GLOW
    // ============================================================

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

    // ============================================================
    // PUSH
    // ============================================================

    void OnEnable()
    {
        instance = this;
        // Build at render time, not in LateUpdate: these are SCREEN-space points, and the camera rig
        // (CameraController / the Cinemachine brain) also moves in LateUpdate. Projecting before the
        // camera has settled leaves the glow a frame behind the object it belongs to, which reads as
        // lag while orbiting. beginCameraRendering runs after every Update/LateUpdate and after the
        // brain, against the matrices this frame is actually rendered with, so there is no lag left.
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    void Update()
    {
        // Age and cull trigger entries once per frame — the render callback can fire more than once.
        for (int i = triggers.Count - 1; i >= 0; i--)
        {
            triggers[i].elapsed += Time.deltaTime;
            if (triggers[i].elapsed >= triggers[i].duration)
                triggers.RemoveAt(i);
        }
    }

    void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        // Scene view / reflection / preview cameras must not stomp the globals with their own
        // projection. Stacked overlay cameras would re-push the same frame, so build only once.
        if (cam == null || cam.cameraType != CameraType.Game) return;
        if (Camera.main != null && cam != Camera.main) return;
        if (Time.frameCount == lastBuiltFrame) return;
        lastBuiltFrame = Time.frameCount;

        BuildAndPush(cam);
    }

    void BuildAndPush(Camera cam)
    {
        int count = 0;

        // Inspector points first, then bursts, then street lights — so a fish-catch burst can never
        // be the thing squeezed out by a level full of lit lamps.
        for (int i = 0; i < persistentPoints.Count && count < MAX_POINTS; i++)
            TryAddPoint(cam, persistentPoints[i], ref count);

        for (int i = 0; i < triggers.Count && count < MAX_POINTS; i++)
        {
            var t = triggers[i];
            float norm = Mathf.Clamp01(t.elapsed / t.duration);
            float r    = Mathf.Lerp(t.radiusStart, t.radiusEnd, norm);

            // Bursts carry their own falloff through radius/intensity, so they ride at full opacity.
            if (t.isViewportSpace)
            {
                AddSlot(t.viewportPos, r, t.softness, 1f, ref count);
                continue;
            }

            if (TryProject(cam, t.worldPos, r, out Vector2 uv))
                AddSlot(uv, r, t.softness, 1f, ref count);
        }

        // Lit street lights, last. No registration step: the lamps already maintain LitLights, and
        // the glow sits on InstLightPosition — the exact point they hand InstancedLightManager, the
        // one their scene gizmo draws — so the screen bloom can never drift off the lit-from point.
        if (streetLightGlow && streetLights.opacity > 0f && streetLights.weight > 0f)
        {
            var lamps = StreetLightController.LitLights;
            for (int i = 0; i < lamps.Count && count < MAX_POINTS; i++)
            {
                var lamp = lamps[i];
                if (lamp == null) continue;   // destroyed on level teardown but still listed
                TryAddPointAt(cam, lamp.InstLightPosition, streetLights, ref count);
            }
        }

        for (int i = count; i < MAX_POINTS; i++)
        {
            pointBuffer[i] = Vector4.zero;
            paramBuffer[i] = Vector4.zero;
        }

        Shader.SetGlobalVectorArray(GlowPointsPID, pointBuffer);
        Shader.SetGlobalVectorArray(GlowPointParamsPID, paramBuffer);
        Shader.SetGlobalFloat(GlowPointCountPID, count);
        Shader.SetGlobalFloat(GlowOpacityPID, opacity);

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
            // Bare $Globals uniform: dual-write so a material-scoped read sees the same value.
            glowMaterial.SetFloat(GlowOpacityPID, opacity);
        }
    }

    void TryAddPoint(Camera cam, GlowPoint p, ref int count)
    {
        if (p == null || p.target == null || p.weight <= 0f || p.opacity <= 0f) return;

        float r = p.radius * p.weight;
        if (r <= 0f) return;

        if (TryProject(cam, p.target.position, r, out Vector2 uv))
            AddSlot(uv, r, p.softness, p.opacity, ref count);
    }

    void TryAddPointAt(Camera cam, Vector3 worldPos, GlowSettings s, ref int count)
    {
        float r = s.radius * s.weight;
        if (r <= 0f) return;

        if (TryProject(cam, worldPos, r, out Vector2 uv))
            AddSlot(uv, r, s.softness, s.opacity, ref count);
    }

    void AddSlot(Vector2 uv, float radius, float softness, float pointOpacity, ref int count)
    {
        pointBuffer[count] = new Vector4(uv.x, uv.y, radius, softness);
        paramBuffer[count] = new Vector4(pointOpacity, 0f, 0f, 0f);
        count++;
    }

    // Projects to viewport and rejects anything behind the camera or fully off screen, so an
    // off-screen light never eats one of the MAX_POINTS slots.
    bool TryProject(Camera cam, Vector3 worldPos, float radius, out Vector2 uv)
    {
        Vector3 vp = cam.WorldToViewportPoint(worldPos);
        uv = new Vector2(vp.x, vp.y);
        if (vp.z <= 0f) return false;

        // The shader multiplies the x delta by aspect, so the mask's x extent in UV is radius/aspect.
        float aspect = cam.aspect > 0.0001f ? cam.aspect : 1f;
        float mx = radius / aspect;
        return vp.x >= -mx && vp.x <= 1f + mx && vp.y >= -radius && vp.y <= 1f + radius;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

        // Zero out when controller is off so effect disappears cleanly
        System.Array.Clear(pointBuffer, 0, pointBuffer.Length);
        System.Array.Clear(paramBuffer, 0, paramBuffer.Length);
        Shader.SetGlobalVectorArray(GlowPointsPID, pointBuffer);
        Shader.SetGlobalVectorArray(GlowPointParamsPID, paramBuffer);
        Shader.SetGlobalFloat(GlowPointCountPID, 0f);
        Shader.SetGlobalFloat(GlowOpacityPID, 0f);
    }
}
