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

    [Header("Occlusion")]
    [Tooltip("Which layers can hide a light. A point with one of these between it and the camera is " +
             "dropped for that frame, so a lamp behind a rock stops glowing through it. Set to Nothing " +
             "to switch the check off. Narrow it the way CameraOccluderFader's mask wants narrowing if " +
             "the water plane or arena floor starts swallowing lights.")]
    [SerializeField] LayerMask occluderLayers = ~0;

    [Tooltip("Stops the ray short of the point, so a lamp's or the boat's own colliders never hide " +
             "their own glow.")]
    [SerializeField] float occluderPadding = 0.6f;

    [Tooltip("Colliders with this tag are seen through. The arena walls are masked out in the shader " +
             "but still have colliders on Default, so without this the camera looking in over a wall " +
             "hides every light behind it at once. Blank = nothing is ignored.")]
    [SerializeField] string ignoredOccluderTag = "OuterWalls";

    [Header("Occlusion Debug")]
    [Tooltip("Logs every glow point's state twice a second: on screen or not, blocked or not, and the " +
             "name of the collider blocking it. If the log shows the points differing while the screen " +
             "shows them all flipping together, the problem is in the graph, not here.")]
    [SerializeField] bool logGlowPoints = false;

    [Tooltip("Draws each camera-to-point ray in the scene view — red where something is blocking it.")]
    [SerializeField] bool drawGlowRays = false;

    [Header("Street Lights")]
    [Tooltip("Give every lit street light a persistent glow point. The lights are found automatically " +
             "via StreetLightController.LitLights — nothing to wire, and nothing to tune on the prefab.")]
    [SerializeField] bool streetLightGlow = true;

    [Tooltip("The tight core of each lamp's glow — same four knobs as a persistent point, minus the " +
             "target (the lamps supply that).")]
    [SerializeField] GlowSettings streetLights = new();

    [Tooltip("The soft halo around each lamp: bigger and fainter than the core, sitting on the same " +
             "point. Set its Opacity to 0 to switch the halo off and go back to one point per lamp.")]
    [SerializeField] GlowSettings streetLightHalo = new() { radius = 0.22f, softness = 0.12f, opacity = 0.22f };

    // Triggered glow (fish catch, events etc.)
    class TriggerEntry
    {
        public Vector3 worldPos;
        public Vector2 viewportPos;
        public bool    isViewportSpace;

        // Followed every frame so a burst rides with the boat instead of being left behind at the
        // spot it fired from. Null for a burst fired at a bare world position; if the target is
        // destroyed mid-burst, worldPos/viewportPos hold the last place it was seen.
        public Transform follow;
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

    // Blockers considered in one cast, matching CameraOccluderFader's cap. More than this stacked in
    // a single line of sight and the nearest ones have already decided the answer.
    readonly RaycastHit[] occluderHits = new RaycastHit[16];

    readonly System.Text.StringBuilder dbg = new();
    float nextLogTime;
    bool  logThisFrame;

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

    // Convenience overload for a Transform — this one follows the transform for its whole life.
    public static void TriggerGlow(Transform t, float duration, float radius = 0.2f, float softness = 0.07f)
    {
        if (t == null) return;
        triggers.Add(new TriggerEntry
        {
            follow      = t,
            worldPos    = t.position,
            radiusStart = radius,
            radiusEnd   = 0f,
            softness    = softness,
            duration    = duration,
            elapsed     = 0f
        });
    }

    // World-space capture burst — uses Capture Burst Settings from inspector
    public static void TriggerCaptureGlow(Transform t)
    {
        Vector2 range  = instance != null ? instance.captureRadiusRange : new Vector2(0.05f, 0.3f);
        float softness = instance != null ? instance.captureSoftness    : 0.08f;
        float duration = instance != null ? instance.captureDuration    : 2f;
        float intensity = instance != null ? instance.captureIntensity : 3f;
        if (t == null) return;
        triggers.Add(new TriggerEntry
        {
            follow      = t,          // rides with the boat rather than staying where it fired
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

        if (rt == null) return;

        Vector2 screenPos = rt.position;
        Vector2 vp = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);
        triggers.Add(new TriggerEntry
        {
            isViewportSpace = true,
            follow          = rt,     // a HUD element that slides keeps its glow attached
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

        logThisFrame = logGlowPoints && Time.unscaledTime >= nextLogTime;
        if (logThisFrame)
        {
            nextLogTime = Time.unscaledTime + 0.5f;
            dbg.Clear();
        }

        // Inspector points first, then bursts, then street lights — so a fish-catch burst can never
        // be the thing squeezed out by a level full of lit lamps.
        for (int i = 0; i < persistentPoints.Count && count < MAX_POINTS; i++)
            TryAddPoint(cam, persistentPoints[i], ref count);

        for (int i = 0; i < triggers.Count && count < MAX_POINTS; i++)
        {
            var t = triggers[i];
            float norm = Mathf.Clamp01(t.elapsed / t.duration);
            float r    = Mathf.Lerp(t.radiusStart, t.radiusEnd, norm);

            // A burst's intensity rides in its own mask value instead of the shared
            // _BrightnessIntensity, so catching a fish flares that one point and leaves every other
            // light alone. The graph does Lerp(scene, scene + I, mask) = scene + mask * I, so a mask
            // of intensity/I lands exactly on the intensity asked for — including above 1, which the
            // Lerp extrapolates rather than clamps.
            float ceiling = Mathf.Max(brightnessIntensity, 0.0001f);
            float burst   = t.intensity <= 0f
                          ? 1f
                          : Mathf.Lerp(t.intensity, brightnessIntensity, norm) / ceiling;

            if (t.isViewportSpace)
            {
                // Re-read the HUD element each frame so the burst tracks it if it moves.
                if (t.follow != null)
                {
                    Vector2 sp = t.follow.position;
                    t.viewportPos = new Vector2(sp.x / Screen.width, sp.y / Screen.height);
                }
                AddSlot(t.viewportPos, r, t.softness, burst, ref count);
                continue;
            }

            if (t.follow != null) t.worldPos = t.follow.position;

            if (TryProject(cam, t.worldPos, r, out Vector2 uv))
                AddSlot(uv, r, t.softness, burst, ref count);
        }

        // Lit street lights, last. No registration step: the lamps already maintain LitLights, and
        // the glow sits on InstLightPosition — the exact point they hand InstancedLightManager, the
        // one their scene gizmo draws — so the screen bloom can never drift off the lit-from point.
        if (streetLightGlow)
        {
            var lamps = StreetLightController.LitLights;
            for (int i = 0; i < lamps.Count && count < MAX_POINTS; i++)
            {
                var lamp = lamps[i];
                if (lamp == null) continue;   // destroyed on level teardown but still listed
                AddLamp(cam, lamp, ref count);
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

        if (logThisFrame)
            Debug.Log($"[Glow] pushed _GlowPointCount {count}, _GlowOpacity {opacity:0.00}, " +
                      $"{StreetLightController.LitLights.Count} lit lamps{dbg}");

        // _BrightnessIntensity stays put at the authored value — it is the shared ceiling every point
        // scales against, not a per-event knob. Raising it for a burst was what flared every light
        // on screen at once; that ramp now lives in the burst's own mask value above.
        if (glowMaterial != null)
        {
            glowMaterial.SetFloat(BrightnessIntensityPID, brightnessIntensity);
            // Bare $Globals uniform: dual-write so a material-scoped read sees the same value.
            glowMaterial.SetFloat(GlowOpacityPID, opacity);
        }
    }

    void TryAddPoint(Camera cam, GlowPoint p, ref int count)
    {
        if (p == null || p.target == null || p.weight <= 0f || p.opacity <= 0f) return;

        float r = p.radius * p.weight;
        if (r <= 0f) return;

        TryAddWorld(cam, p.target.position, r, p.softness, p.opacity, p.target.name, ref count);
    }

    // Both of a lamp's points — the tight core and the soft halo — sit on InstLightPosition, so they
    // share one occlusion ray rather than casting the same ray twice. The core is added first, so a
    // lamp that runs into the point budget loses its halo and keeps its light.
    void AddLamp(Camera cam, StreetLightController lamp, ref int count)
    {
        Vector3 p = lamp.InstLightPosition;

        float coreR = streetLights.opacity    > 0f ? streetLights.radius    * streetLights.weight    : 0f;
        float haloR = streetLightHalo.opacity > 0f ? streetLightHalo.radius * streetLightHalo.weight : 0f;
        if (coreR <= 0f && haloR <= 0f) return;

        Vector2 coreUV  = default, haloUV = default;
        bool    coreOn  = coreR > 0f && TryProject(cam, p, coreR, out coreUV);
        bool    haloOn  = haloR > 0f && TryProject(cam, p, haloR, out haloUV);
        string  blocker = null;
        bool    blocked = (coreOn || haloOn) && IsOccluded(cam, p, out blocker);

        if (coreR > 0f)
            AddResolved(coreUV, coreOn, blocked, blocker, coreR, streetLights.softness,
                        streetLights.opacity, lamp.name, ref count);

        if (haloR > 0f && count < MAX_POINTS)
            AddResolved(haloUV, haloOn, blocked, blocker, haloR, streetLightHalo.softness,
                        streetLightHalo.opacity, lamp.name + " halo", ref count);
    }

    void TryAddWorld(Camera cam, Vector3 worldPos, float radius, float softness, float pointOpacity,
                     string label, ref int count)
    {
        string blocker  = null;
        bool   onScreen = TryProject(cam, worldPos, radius, out Vector2 uv);
        bool   blocked  = onScreen && IsOccluded(cam, worldPos, out blocker);

        AddResolved(uv, onScreen, blocked, blocker, radius, softness, pointOpacity, label, ref count);
    }

    void AddResolved(Vector2 uv, bool onScreen, bool blocked, string blocker, float radius,
                     float softness, float pointOpacity, string label, ref int count)
    {
        if (logThisFrame)
        {
            dbg.Append("\n  ").Append(label).Append(": ");
            if (!onScreen)     dbg.Append("OFF SCREEN");
            else if (blocked)  dbg.Append("blocked by '").Append(blocker).Append('\'');
            else               dbg.Append("visible → slot ").Append(count)
                                  .Append(" uv(").Append(uv.x.ToString("0.00")).Append(", ")
                                  .Append(uv.y.ToString("0.00")).Append(") r ").Append(radius.ToString("0.000"))
                                  .Append(" opacity ").Append(pointOpacity.ToString("0.00"));
        }

        if (onScreen && !blocked)
            AddSlot(uv, radius, softness, pointOpacity, ref count);
    }

    // One ray per point, and only for points that already passed the on-screen test above — a level
    // full of lamps costs at most MAX_POINTS casts a frame. Runs at render time against the physics
    // state from the last FixedUpdate, which is what the frame is drawing anyway.
    bool IsOccluded(Camera cam, Vector3 worldPos, out string blocker)
    {
        blocker = null;
        if (occluderLayers.value == 0) return false;

        Vector3 from  = cam.transform.position;
        Vector3 delta = worldPos - from;
        float   full  = delta.magnitude;
        float   dist  = full - occluderPadding;

        // Point is nearer than the padding — there is no room for anything to be in the way.
        if (full <= 0.0001f || dist <= 0.01f) return false;

        Vector3 dir = delta / full;

        // Every hit, not just the nearest: the nearest one is usually an arena wall, and stopping
        // there would report "blocked" for a wall the player is already seeing straight through.
        int hits = Physics.RaycastNonAlloc(from, dir, occluderHits, dist, occluderLayers, QueryTriggerInteraction.Ignore);
        hits = Mathf.Min(hits, occluderHits.Length);

        bool ignoreByTag = !string.IsNullOrEmpty(ignoredOccluderTag);
        bool report      = logThisFrame || drawGlowRays;
        int  seenThrough = 0;
        bool occluded    = false;

        // Scan every hit rather than stopping at the first blocker — RaycastNonAlloc does not return
        // them in distance order, so an early exit would under-report the walls we saw through.
        for (int i = 0; i < hits; i++)
        {
            Collider col = occluderHits[i].collider;
            if (col == null) continue;

            if (ignoreByTag && col.CompareTag(ignoredOccluderTag)) { seenThrough++; continue; }

            if (report && !occluded)
                blocker = $"{col.name} [{LayerMask.LayerToName(col.gameObject.layer)}] " +
                          $"at {occluderHits[i].distance:0.0}m of {dist:0.0}m";

            occluded = true;
            if (!report) break;   // nothing left to learn once we know it is blocked
        }

        if (report && occluded && seenThrough > 0)
            blocker += $" (saw through {seenThrough} '{ignoredOccluderTag}')";

        if (drawGlowRays)
            Debug.DrawLine(from, worldPos, occluded ? Color.red : Color.green);

        return occluded;
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
