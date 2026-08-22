using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Casts a ray from the camera to the boat and fades out whatever gets in the way.
///
/// Every renderer the ray passes through is pushed a 0..1 blocking amount as _OccluderFade through a
/// MaterialPropertyBlock — per renderer, so it works on objects sharing one material. The amount
/// ramps in and out rather than snapping, so a rock passing through the view fades instead of popping.
/// OccluderFade.hlsl turns that amount into an alpha; how far it fades is the shared _OccluderFadedAlpha
/// global pushed from here.
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

    [Header("Debug")]
    [Tooltip("Draws the camera-to-boat ray in the scene view — red while something is blocking it.")]
    [SerializeField] private bool drawRay = false;

    static readonly int FadeId        = Shader.PropertyToID("_OccluderFade");
    static readonly int FadedAlphaId  = Shader.PropertyToID("_OccluderFadedAlpha");

    readonly RaycastHit[] _hits = new RaycastHit[MaxHits];

    // Renderers currently mid-fade, and how far each one is through it.
    readonly List<Renderer> _tracked = new List<Renderer>();
    readonly Dictionary<Renderer, float> _fades = new Dictionary<Renderer, float>();
    readonly HashSet<Renderer> _blocking = new HashSet<Renderer>();

    // Collider → its renderers. Cached because the cast runs every frame.
    readonly Dictionary<Collider, Renderer[]> _rendererCache = new Dictionary<Collider, Renderer[]>();

    MaterialPropertyBlock _block;

    private void Awake()
    {
        _block = new MaterialPropertyBlock();
    }

    // LateUpdate — the camera rig moves in LateUpdate, so cast after it has settled for this frame.
    private void LateUpdate()
    {
        Shader.SetGlobalFloat(FadedAlphaId, fadedAlpha);

        // Baseline of 0 (solid) for every renderer WITHOUT a property block, so the fade can never
        // bleed onto other objects sharing the material — only the renderers written below, each of
        // which carries its own block, come out faded.
        Shader.SetGlobalFloat(FadeId, 0f);

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

        Vector3 dir = delta / full;

        int count = rayRadius > 0.001f
            ? Physics.SphereCastNonAlloc(from, rayRadius, dir, _hits, dist, occluderLayers, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(from, dir, _hits, dist, occluderLayers, QueryTriggerInteraction.Ignore);

        count = Mathf.Min(count, MaxHits);

        for (int i = 0; i < count; i++)
        {
            Collider col = _hits[i].collider;
            if (col == null) continue;

            Renderer[] renderers = GetRenderers(col);
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] != null)
                    _blocking.Add(renderers[r]);
            }
        }

        if (drawRay)
            Debug.DrawLine(from, from + dir * dist, count > 0 ? Color.red : Color.green);
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
                _tracked.RemoveAt(i);
                continue;
            }

            bool  blocking = _blocking.Contains(r);
            float value    = Damp(_fades[r], blocking ? 1f : 0f, blocking ? fadeInSpeed : fadeOutSpeed);

            // Fully clear again — write it back to solid and stop tracking it
            if (!blocking && value < 0.002f)
            {
                Write(r, 0f);
                _fades.Remove(r);
                _tracked.RemoveAt(i);
                continue;
            }

            _fades[r] = value;
            Write(r, value);
        }
    }

    private void Write(Renderer r, float fade)
    {
        if (_block == null) _block = new MaterialPropertyBlock();

        // Read the existing block first so any other per-renderer overrides survive
        r.GetPropertyBlock(_block);
        _block.SetFloat(FadeId, fade);
        r.SetPropertyBlock(_block);
    }

    private static float Damp(float from, float to, float speed)
    {
        if (speed <= 0f) return to;
        return Mathf.Lerp(from, to, 1f - Mathf.Exp(-speed * Time.deltaTime));
    }

    // Leave nothing half-faded behind
    private void OnDisable()
    {
        for (int i = 0; i < _tracked.Count; i++)
        {
            if (_tracked[i] != null)
                Write(_tracked[i], 0f);
        }

        _tracked.Clear();
        _fades.Clear();
        _blocking.Clear();
        _rendererCache.Clear();
    }
}
