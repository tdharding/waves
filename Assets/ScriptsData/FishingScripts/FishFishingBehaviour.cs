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

    [HideInInspector] public FishingController fishing;
    private Transform whirlTarget;
    private SoulWhirlDirection whirlDirection;

    public bool IsBeingAttracted => fishingActive && hasCachedSplinePos;

    private bool fishingActive;
    private bool hasCachedSplinePos;

    private float currentPullSpeed;
    private Vector3 cachedSplinePosition;
    private Vector3 cachedDefaultScale;

    private LinkIdentityLabel identity;

    void Awake()
    {
        if (!splineAnimate)
            splineAnimate = GetComponent<SplineAnimate>();

        cachedDefaultScale = transform.localScale;
        
        // Ensure we grab the identity label from the parent (the spawned container)
        identity = GetComponentInParent<LinkIdentityLabel>();

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

    public void OnFishingStopped() => fishingActive = false;

    public void TriggerReturnToSpline()
    {
        cachedSplinePosition = transform.position;
        hasCachedSplinePos   = true;
        currentPullSpeed     = 0f;
    }

    void Update()
    {
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
        if (fishing == null || fishing.dummyBoatTarget == null) return;

        foreach (Transform t in fishing.dummyBoatTarget.GetComponentsInChildren<Transform>(true))
        {
            if (t.CompareTag("SoulWhirl"))
            {
                whirlTarget = t;
                whirlDirection = t.GetComponent<SoulWhirlDirection>();
                break;
            }
        }
    }

    void AttractTowardsWhirl()
    {
        if (whirlTarget == null) return;

        if (!hasCachedSplinePos)
        {
            cachedSplinePosition = transform.position;
            hasCachedSplinePos = true;
            currentPullSpeed = basePullSpeed;
            splineAnimate.Pause();
        }

        currentPullSpeed = Mathf.Min(currentPullSpeed + pullAcceleration * Time.deltaTime, maxPullSpeed);
        transform.position = Vector3.MoveTowards(transform.position, whirlTarget.position, currentPullSpeed * Time.deltaTime);

        float dist = Vector3.Distance(transform.position, whirlTarget.position);
        float t = Mathf.InverseLerp(fishing.CurrentFishingRange, fishing.commitDistance, dist);
        transform.localScale = cachedDefaultScale * Mathf.Lerp(1f, minScale, t);

        // --- THE CAPTURE TRIGGER ---
        if (dist <= fishing.commitDistance)
        {
            if (identity != null)
            {
                // RELAY: Hand the whole "Passport" to the FishingController
                fishing.OnFishCaptured(identity);
                
                // Cleanup the physical fish
                Destroy(identity.gameObject);
            }
            else
            {
                Debug.LogError("FishFishingBehaviour: No LinkIdentityLabel found. Cannot complete capture.", this);
                Destroy(gameObject);
            }
        }
    }

    void ReturnToSpline()
    {
        if (!hasCachedSplinePos) return;

        transform.position = Vector3.MoveTowards(transform.position, cachedSplinePosition, Time.deltaTime / returnSnapTime);
        transform.localScale = Vector3.MoveTowards(transform.localScale, cachedDefaultScale, Time.deltaTime / returnSnapTime);

        if (Vector3.Distance(transform.position, cachedSplinePosition) <= reattachDistance)
        {
            hasCachedSplinePos = false;
            splineAnimate.Play();
            transform.localScale = cachedDefaultScale;
        }
    }
}