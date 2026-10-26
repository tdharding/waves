using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Casts a ray from the camera to the boat and fades out whatever gets in the way.
///
/// Every renderer the ray passes through is pushed a 0..1 blocking amount as _OccluderFade through a
/// MaterialPropertyBlock — per renderer, so it works on objects sharing one material. The amount
/// ramps in and out rather than snapping, so a rock passing through the view fades instead of popping.
/// OccluderFade.hlsl turns that amount into an alpha; how far it fades is the shared _OccluderFadedAlpha
/// global pushed from here.
///
/// The river the boat is floating on never fades — every run and pool carrying its name is left out.
/// A placed piece carrying an <see cref="OcclusionFade"/> follows that instead, so things standing
/// on the river can still fade while the boat sails it.
///
/// A piece set to Screen Circle (<see cref="OcclusionStyle.ScreenCircle"/>) is flagged the same
/// way, but only a soft circle round the boat on screen fades — _OccluderCircle is pushed 1 on its
/// renderers, and the circle's centre (_OccluderCircleCentre, the boat projected onto the screen)
/// is built per camera in beginCameraRendering, after the camera rig has moved.
/// </summary>
public class CameraOccluderFader : MonoBehaviour
{
    // Blockers considered in one cast. More than this in a single line of sight is already a mess.
    const int MaxHits = 32;

    [Header("Ray")]
    [Tooltip("Where the ray starts — drag the Cinemachine camera or the main camera. Left blank, " +
             "CameraController's active camera is used, then Camera.main.")]
    [SerializeField] private Transform viewSource;
    [Tooltip("What the ray aims at. Left blank, the boat is taken from CameraController, then LevelDataController.")]
    [SerializeField] private Transform boatTarget;
    [Tooltip("Which layers can block the view. Defaults to Everything — narrow this to your rocks, " +
             "spikes and walls, or the water plane and arena floor will register as blockers.")]
    [SerializeField] private LayerMask occluderLayers = ~0;

    [Tooltip("Thickness of the ray. 0 = a thin line; wider also catches objects clipping the edge of the view.")]
    [SerializeField] private float rayRadius = 0.4f;

    [Tooltip("Stops the ray short of the boat so the boat's own colliders never count as blockers.")]
    [SerializeField] private float targetPadding = 0.6f;

    [Header("Fade")]
    [Tooltip("Alpha a fully blocking object is left at. 0.25 = fades to 25%.")]
    [Range(0f, 1f)] [SerializeField] private float fadedAlpha = 0.25f;
    [Tooltip("How quickly an object fades out once it blocks the view. Higher = snappier.")]
    [SerializeField] private float fadeInSpeed = 8f;
    [Tooltip("How quickly it comes back once it is clear. Higher = snappier.")]
    [SerializeField] private float fadeOutSpeed = 4f;

    [Header("Screen Circle")]
    [Tooltip("Radius of the faded circle round the boat on pieces set to Screen Circle, as a share " +
             "of the screen's height. 0.2 = a fifth of the screen's height.")]
    [Range(0f, 1f)] [SerializeField] private float circleRadius = 0.2f;
    [Tooltip("How soft the circle's edge is, as a share of the screen's height — the edge fades " +
             "from solid to faded across this width, inside the radius.")]
    [Range(0f, 1f)] [SerializeField] private float circleFeather = 0.08f;

    [Header("Debug")]
    [Tooltip("Draws the camera-to-boat ray in the scene view — red while something is blocking it.")]
    [SerializeField] private bool drawRay = false;

    static readonly int FadeId        = Shader.PropertyToID("_OccluderFade");
    static readonly int FadedAlphaId  = Shader.PropertyToID("_OccluderFadedAlpha");
    static readonly int CircleId       = Shader.PropertyToID("_OccluderCircle");
    static readonly int CircleCentreId = Shader.PropertyToID("_OccluderCircleCentre");
    static readonly int CircleRadiusId = Shader.PropertyToID("_OccluderCircleRadius");
    static readonly int CircleFeatherId = Shader.PropertyToID("_OccluderCircleFeather");

    // Off every screen, so a circle has nothing to fade while the boat is behind the camera.
    static readonly Vector4 CircleNowhere = new Vector4(-100f, -100f, 0f, 0f);

    readonly RaycastHit[] _hits = new RaycastHit[MaxHits];

    // Renderers currently mid-fade, and how far each one is through it.
    readonly List<Renderer> _tracked = new List<Renderer>();
    readonly Dictionary<Renderer, float> _fades = new Dictionary<Renderer, float>();
    readonly HashSet<Renderer> _blocking = new HashSet<Renderer>();

    // Renderers of pieces set to Screen Circle — faded only inside the circle round the boat.
    readonly HashSet<Renderer> _circles = new HashSet<Renderer>();

    // Collider → its renderers. Cached because the cast runs every frame.
    readonly Dictionary<Collider, Renderer[]> _rendererCache = new Dictionary<Collider, Renderer[]>();

    // Collider → the river it is part of (null for anything that is not a river piece).
    readonly Dictionary<Collider, string> _riverCache = new Dictionary<Collider, string>();

    // Collider → the Occlusion setting of the piece it is part of (null when it has none).
    readonly Dictionary<Collider, OcclusionFade> _occlusionCache = new Dictionary<Collider, OcclusionFade>();

    // The river whose water the boat is on this frame — never faded, it is what the boat sits in.
    string _boatRiver;

    int  _waterMask;
    bool _waterMaskFound;

    MaterialPropertyBlock _block;

    private void Awake()
    {
        _block = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        // The circle's centre is the boat on screen — projected once the camera has moved this
        // frame, which LateUpdate is too early for.
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    // LateUpdate — the camera rig moves in LateUpdate, so cast after it has settled for this frame.
    private void LateUpdate()
    {
        Shader.SetGlobalFloat(FadedAlphaId, fadedAlpha);
        Shader.SetGlobalFloat(CircleRadiusId, circleRadius);
        Shader.SetGlobalFloat(CircleFeatherId, circleFeather);

        // Baseline of 0 (solid) for every renderer WITHOUT a property block, so the fade can never
        // bleed onto other objects sharing the material — only the renderers written below, each of
        // which carries its own block, come out faded.
        Shader.SetGlobalFloat(FadeId, 0f);
        Shader.SetGlobalFloat(CircleId, 0f);

        _blocking.Clear();

        if (ResolveTargets())
            CollectBlockers();

        ApplyFades();
    }

    private bool ResolveTargets()
    {
        CameraController cameras = CameraController.Instance;

        if (viewSource == null && cameras != null)
            viewSource = cameras.ActiveCameraTransform;

        if (viewSource == null && Camera.main != null)
            viewSource = Camera.main.transform;

        if (boatTarget == null && cameras != null)
            boatTarget = cameras.BoatTarget;

        if (boatTarget == null && LevelDataController.Instance != null)
            boatTarget = LevelDataController.Instance.GetBoatRoot();

        return viewSource != null && boatTarget != null;
    }

    private void CollectBlockers()
    {
        Vector3 from  = viewSource.position;
        Vector3 delta = boatTarget.position - from;
        float   full  = delta.magnitude;
        float   dist  = full - targetPadding;

        if (full <= 0.0001f || dist <= 0.01f) return;

        _boatRiver = RiverUnderBoat();

        Vector3 dir = delta / full;

        int count = rayRadius > 0.001f
            ? Physics.SphereCastNonAlloc(from, rayRadius, dir, _hits, dist, occluderLayers, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(from, dir, _hits, dist, occluderLayers, QueryTriggerInteraction.Ignore);

        count = Mathf.Min(count, MaxHits);

        for (int i = 0; i < count; i++)
        {
            Collider col = _hits[i].collider;
            if (col == null) continue;

            // A piece with an Occlusion setting follows it — ticked, it fades even on the boat's
            // own river; unticked, it never does.
            OcclusionFade setting = OcclusionOf(col);
            if (setting != null)
            {
                if (!setting.fades) continue;
            }
            else if (_boatRiver != null && RiverOf(col) == _boatRiver) continue;

            bool circle = setting != null && setting.style == OcclusionStyle.ScreenCircle;

            Renderer[] renderers = GetRenderers(col);
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] == null) continue;

                _blocking.Add(renderers[r]);
                if (circle) _circles.Add(renderers[r]);
                else        _circles.Remove(renderers[r]);
            }
        }

        if (drawRay)
            Debug.DrawLine(from, from + dir * dist, count > 0 ? Color.red : Color.green);
    }

    /// <summary>
    /// The river the boat is floating on: the water straight below it, looked for from above as
    /// the boat finds its own surface, and the run or pool that water belongs to. Null off a river.
    /// </summary>
    private string RiverUnderBoat()
    {
        if (!_waterMaskFound)
        {
            _waterMask      = LayerMask.GetMask("Water");
            _waterMaskFound = true;
        }
        if (_waterMask == 0) return null;

        Vector3 from = boatTarget.position + Vector3.up * 4f;
        if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 16f, _waterMask,
                             QueryTriggerInteraction.Collide))
            return null;

        return RiverOf(hit.collider);
    }

    /// <summary>Which river a collider is part of — its run's or pool's river name.</summary>
    private string RiverOf(Collider col)
    {
        if (_riverCache.TryGetValue(col, out string cached))
            return cached;

        string river = null;

        var run = col.GetComponentInParent<RiverRunMesh>();
        if (run != null) river = run.riverName;
        else
        {
            var pool = col.GetComponentInParent<RiverPoolMesh>();
            if (pool != null) river = pool.riverName;
        }

        if (string.IsNullOrEmpty(river)) river = null;

        _riverCache[col] = river;
        return river;
    }

    /// <summary>The Occlusion setting of the placed piece a collider belongs to — null for anything without one.</summary>
    private OcclusionFade OcclusionOf(Collider col)
    {
        if (_occlusionCache.TryGetValue(col, out OcclusionFade cached))
            return cached;

        OcclusionFade setting = col.GetComponentInParent<OcclusionFade>(true);
        _occlusionCache[col] = setting;
        return setting;
    }

    private Renderer[] GetRenderers(Collider col)
    {
        if (_rendererCache.TryGetValue(col, out Renderer[] cached))
            return cached;

        Renderer[] renderers = col.GetComponentsInChildren<Renderer>(true);

        // Collider sitting on its own — walk up to the renderer it belongs to.
        if (renderers.Length == 0)
        {
            Renderer parent = col.GetComponentInParent<Renderer>();
            renderers = parent != null ? new[] { parent } : System.Array.Empty<Renderer>();
        }

        _rendererCache[col] = renderers;
        return renderers;
    }

    private void ApplyFades()
    {
        // Anything newly blocking starts its ramp from clear
        foreach (Renderer r in _blocking)
        {
            if (!_fades.ContainsKey(r))
            {
                _fades[r] = 0f;
                _tracked.Add(r);
            }
        }

        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            Renderer r = _tracked[i];

            if (r == null)
            {
                _fades.Remove(r);
                _circles.Remove(r);
                _tracked.RemoveAt(i);
                continue;
            }

            bool  blocking = _blocking.Contains(r);
            float value    = Damp(_fades[r], blocking ? 1f : 0f, blocking ? fadeInSpeed : fadeOutSpeed);

            // Fully clear again — write it back to solid and stop tracking it
            if (!blocking && value < 0.002f)
            {
                Write(r, 0f, false);
                _fades.Remove(r);
                _circles.Remove(r);
                _tracked.RemoveAt(i);
                continue;
            }

            _fades[r] = value;
            Write(r, value, _circles.Contains(r));
        }
    }

    private void Write(Renderer r, float fade, bool circle)
    {
        if (_block == null) _block = new MaterialPropertyBlock();

        // Read the existing block first so any other per-renderer overrides survive
        r.GetPropertyBlock(_block);
        _block.SetFloat(FadeId, fade);
        _block.SetFloat(CircleId, circle ? 1f : 0f);
        r.SetPropertyBlock(_block);
    }

    private static float Damp(float from, float to, float speed)
    {
        if (speed <= 0f) return to;
        return Mathf.Lerp(from, to, 1f - Mathf.Exp(-speed * Time.deltaTime));
    }

    /// <summary>
    /// The Screen Circle's centre for the camera about to draw: the boat on that camera's screen,
    /// 0..1 across it as the shader's Screen Position reads it. Built per camera so every view
    /// draws its circle round the boat; off every screen while the boat is behind it.
    /// </summary>
    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam == null) return;

        Vector4 centre = CircleNowhere;
        if (boatTarget != null)
        {
            Vector3 vp = cam.WorldToViewportPoint(boatTarget.position);
            if (vp.z > 0f) centre = new Vector4(vp.x, vp.y, 0f, 0f);
        }

        Shader.SetGlobalVector(CircleCentreId, centre);
    }

    // Leave nothing half-faded behind
    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

        for (int i = 0; i < _tracked.Count; i++)
        {
            if (_tracked[i] != null)
                Write(_tracked[i], 0f, false);
        }

        _tracked.Clear();
        _fades.Clear();
        _blocking.Clear();
        _circles.Clear();
        _rendererCache.Clear();
        _riverCache.Clear();
        _occlusionCache.Clear();
        _boatRiver = null;
    }
}
