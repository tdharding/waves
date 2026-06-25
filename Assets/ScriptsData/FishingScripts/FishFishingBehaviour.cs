using UnityEngine;
using UnityEngine.Splines;
using System.Collections;

[RequireComponent(typeof(SplineAnimate))]
public class FishFishingBehaviour : MonoBehaviour
{
    [Header("Spline")]
    public SplineAnimate splineAnimate;

    [Header("Attraction")]
    public float basePullSpeed    = 0f;
    public float maxPullSpeed     = 0.8f;
    public float pullAcceleration = 0.1f;

    [Header("Attraction Glow")]
    public float attractionMaterialFadeSpeed = 2f;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock glowPropertyBlock;
    private Color glowBaseColor;
    private int glowMaterialIndex = -1;
    private float currentGlowAlpha;

    [Header("Return")]
    public float returnSnapTime = 0.25f;
    public float reattachDistance = 0.05f;

    [Header("Scale While Attracted")]
    public float minScale = 0.5f;

    [Header("Wave Height")]
    public float extraYOffset     = -0.5f;
    public float heightMultiplier = 1f;
    public float activeDistance   = 25f;

    [HideInInspector] public FishingController fishing;
    private Transform whirlTarget;
    private SoulWhirlDirection whirlDirection;

    public bool IsBeingAttracted  => fishingActive && !_travelingTube && IsEligibleForAttraction();
    public bool IsTravelingTube   => _travelingTube;

    private bool fishingActive;
    private bool _travelingTube;
    private bool _tubeReleased;
    private bool hasCachedSplinePos;

    private float currentPullSpeed;
    private Vector3 cachedSplinePosition;
    private Vector3 cachedDefaultScale;

    private LinkIdentityLabel identity;
    private LureAttractable   _lureAttractable;

    private Transform _waterTransform;
    private Transform _boatRoot;
    private Material  _waterMat;

    void Awake()
    {
        if (!splineAnimate)
            splineAnimate = GetComponent<SplineAnimate>();

        cachedDefaultScale = transform.localScale;

        identity         = GetComponentInParent<LinkIdentityLabel>();
        _lureAttractable = GetComponent<LureAttractable>();

        meshRenderer = GetComponentInChildren<MeshRenderer>();

        if (meshRenderer != null)
        {
            var sharedMaterials = meshRenderer.sharedMaterials;
            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                if (sharedMaterials[i] != null && sharedMaterials[i].name.Contains("FishGlow"))
                {
                    glowMaterialIndex = i;
                    glowPropertyBlock = new MaterialPropertyBlock();
                    glowBaseColor = sharedMaterials[i].GetColor("_BaseColor");
                    currentGlowAlpha = 0f;
                    break;
                }
            }

            if (glowMaterialIndex == -1)
                Debug.LogError("FishFishingBehaviour: No material named 'FishGlow' found.", this);
        }
    }

    void Start()
    {
        if (LevelDataController.Instance == null) return;
        _waterTransform = LevelDataController.Instance.GetWaveTransform();
        _boatRoot       = LevelDataController.Instance.GetBoatRoot();
        if (_waterTransform != null)
            _waterMat = _waterTransform.GetComponent<MeshRenderer>().sharedMaterial;
    }

    void OnEnable()
    {
        fishing = FindObjectOfType<FishingController>();
        if (fishing != null)
            fishing.RegisterFish(this);

        if (splineAnimate)
        {
            splineAnimate.enabled = true;
            splineAnimate.Play();
        }
    }

    void OnDisable()
    {
        if (fishing != null)
            fishing.UnregisterFish(this);
    }

    void UpdateGlowFade(bool attracting)
    {
        if (meshRenderer == null || glowMaterialIndex == -1) return;

        float targetAlpha = attracting ? 1f : 0f;
        currentGlowAlpha = Mathf.MoveTowards(currentGlowAlpha, targetAlpha, attractionMaterialFadeSpeed * Time.deltaTime);

        glowPropertyBlock.Clear();
        Color c = glowBaseColor;
        c.a = currentGlowAlpha;
        glowPropertyBlock.SetColor("_BaseColor", c);
        meshRenderer.SetPropertyBlock(glowPropertyBlock, glowMaterialIndex);
    }

    public void OnFishingStarted(FishingController controller)
    {
        fishing = controller;
        fishingActive = true;
        ResolveWhirlTarget();
    }

    public void OnFishingStopped()
    {
        fishingActive = false;
        if (_travelingTube)
            _tubeReleased = true;
    }

    public void TriggerReturnToSpline()
    {
        cachedSplinePosition = transform.position;
        hasCachedSplinePos   = true;
        currentPullSpeed     = 0f;
    }

    void Update()
    {
        if (_travelingTube) return;

        bool attracting = fishingActive && IsEligibleForAttraction();
        UpdateGlowFade(attracting);

        if (!attracting)
        {
            ReturnToSpline();
            return;
        }

        AttractTowardsWhirl();
    }

    bool IsEligibleForAttraction()
    {
        if (fishing == null || whirlDirection == null) return false;
        return whirlDirection.IsInSector(transform.position);
    }

    void ResolveWhirlTarget()
    {
        if (fishing == null || fishing.boatTransform == null) return;

        foreach (Transform t in fishing.boatTransform.GetComponentsInChildren<Transform>(true))
        {
            if (t.CompareTag("SoulWhirl"))
            {
                whirlTarget    = t;
                whirlDirection = t.GetComponent<SoulWhirlDirection>();
                break;
            }
        }
    }

    void AttractTowardsWhirl()
{
    if (whirlDirection == null) return;

    Vector3 target = whirlDirection.MouthPosition;

    if (!hasCachedSplinePos)
    {
        cachedSplinePosition = transform.position;
        hasCachedSplinePos   = true;
        currentPullSpeed     = basePullSpeed;
        splineAnimate.Pause();
    }

    // --- NEW DIRECTIONAL LOGIC ---
    // Calculate how well the fish is aligned with the mouth's intake direction
    Vector3 toMouth = (target - transform.position).normalized;
    // -whirlDirection.MouthAxis points "into" the tube
    float alignment = Vector3.Dot(toMouth, -whirlDirection.MouthAxis);
    // Ensure speed doesn't drop to 0, but give a 5x boost for perfect alignment
    float alignmentMultiplier = Mathf.Clamp(alignment, 0.2f, 1.0f); 

    currentPullSpeed = Mathf.Min(currentPullSpeed + pullAcceleration * Time.deltaTime, maxPullSpeed);
    
    // Apply the alignment multiplier to the movement
    transform.position = Vector3.MoveTowards(transform.position, target, currentPullSpeed * alignmentMultiplier * Time.deltaTime);
    // -----------------------------

    float dist = Vector3.Distance(transform.position, target);
    float t    = Mathf.InverseLerp(fishing.CurrentFishingRange, whirlDirection.EntryRadius, dist);
    transform.localScale = cachedDefaultScale * Mathf.Lerp(1f, minScale, t);

    if (dist <= whirlDirection.EntryRadius)
    {
        if (identity != null)
        {
            _travelingTube = true;
            _tubeReleased  = false;
            var capturedIdentity = identity;
            StartCoroutine(whirlDirection.TravelAlongPath(
                transform,
                onCaptured: () =>
                {
                    fishing.OnFishCaptured(capturedIdentity);
                    Destroy(capturedIdentity.gameObject);
                },
                onReleased: () =>
                {
                    _travelingTube = false;
                    _tubeReleased  = false;
                },
                isReleased: () => _tubeReleased
            ));
        }
        else
        {
            Debug.LogError("FishFishingBehaviour: No LinkIdentityLabel found. Cannot complete capture.", this);
            Destroy(gameObject);
        }
    }
}

    void LateUpdate()
    {
        if (_travelingTube) return;
        if (IsBeingAttracted) return;
        if (_waterTransform == null || _boatRoot == null || _waterMat == null) return;
        if ((_boatRoot.position - transform.position).sqrMagnitude > activeDistance * activeDistance) return;

        var   p      = WaveUtils.ReadParams(_waterTransform, _waterMat);
        float height = WaveUtils.SampleHeight(transform.position, p, heightMultiplier) + extraYOffset;
        Vector3 pos  = transform.position;
        pos.y        = p.origin.y + height;
        transform.position = pos;
    }

    void ReturnToSpline()
    {
        if (!hasCachedSplinePos) return;

        if (_lureAttractable != null && _lureAttractable.CurrentState == LureAttractable.State.Attracted)
        {
            hasCachedSplinePos = false;
            return;
        }

        transform.position   = Vector3.MoveTowards(transform.position, cachedSplinePosition, Time.deltaTime / returnSnapTime);
        transform.localScale = Vector3.MoveTowards(transform.localScale, cachedDefaultScale, Time.deltaTime / returnSnapTime);

        if (Vector3.Distance(transform.position, cachedSplinePosition) <= reattachDistance)
        {
            hasCachedSplinePos = false;
            splineAnimate.Play();
            transform.localScale = cachedDefaultScale;
        }
    }
}
