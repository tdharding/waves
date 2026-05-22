using UnityEngine;

public class SonarCompassDetection : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Boat transform used for forward direction")]
    public Transform boatTransform;

    [Tooltip("Renderer whose material alpha will be modified")]
    public Renderer targetRenderer;

    [Header("Facing")]
    [Range(-1f, 1f)]
    public float facingDotThreshold = 0.6f;

    [Header("Proximity")]
    [Tooltip("Maximum distance at which fish can trigger alpha")]
    public float detectionRadius = 10f;

    [Header("Fade")]
    public float fadeSpeed = 2f;

    private Material runtimeMat;
    private float currentAlpha;

    void Awake()
    {
        if (targetRenderer != null)
            runtimeMat = targetRenderer.material;

        currentAlpha = 0f;

        if (runtimeMat != null)
            runtimeMat.SetFloat("_AlphaValue", 0f);
    }

    void Update()
    {
        if (boatTransform == null || runtimeMat == null)
            return;

        bool facingAnyFish = IsFacingAnyFish();

        float targetAlpha = facingAnyFish ? 1f : 0f;
        currentAlpha = Mathf.MoveTowards(
            currentAlpha,
            targetAlpha,
            fadeSpeed * Time.deltaTime
        );

        runtimeMat.SetFloat("_AlphaValue", currentAlpha);
    }

    bool IsFacingAnyFish()
    {
        var fish = SoulFishMapLinker.ActiveFish;
        if (fish == null || fish.Count == 0) return false;

        Vector3 boatForward = boatTransform.forward;
        Vector3 boatPos     = boatTransform.position;
        float   radiusSq    = detectionRadius * detectionRadius;

        for (int i = 0; i < fish.Count; i++)
        {
            if (fish[i] == null) continue;

            Vector3 toFish = fish[i].position - boatPos;
            if (toFish.sqrMagnitude > radiusSq) continue;

            if (Vector3.Dot(boatForward, toFish.normalized) > facingDotThreshold)
                return true;
        }

        return false;
    }
}
