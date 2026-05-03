using UnityEngine;

public class WhirlFXController : MonoBehaviour
{
    [Header("Target Skinned Renderers")]
    [SerializeField] private SkinnedMeshRenderer[] targetRenderers;

    [Header("Shader Property")]
    [SerializeField] private string alphaProperty = "Alpha";

    [Header("Values")]
    [SerializeField] private float minAlpha = 0f;
    [SerializeField] private float maxAlpha = 1f;

    [Header("Transition")]
    [SerializeField] private float speed = 8f;

    [Header("Animation")]
    [SerializeField] private Animator netAnimator;
    private FishingController fishingController;

    private float currentAlpha;
    private float targetAlpha;

    private Material[] runtimeMaterials;

    void Awake()
    {
        currentAlpha = minAlpha;
        targetAlpha = minAlpha;

        // Cache per-renderer material instances (INTENTIONAL)
        runtimeMaterials = new Material[targetRenderers.Length];

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            if (!targetRenderers[i]) continue;

            runtimeMaterials[i] = targetRenderers[i].material;
            runtimeMaterials[i].SetFloat(alphaProperty, currentAlpha);
        }
    }

    void Update()
    {
        currentAlpha = Mathf.MoveTowards(
            currentAlpha,
            targetAlpha,
            speed * Time.deltaTime
        );

        for (int i = 0; i < runtimeMaterials.Length; i++)
        {
            if (runtimeMaterials[i])
                runtimeMaterials[i].SetFloat(alphaProperty, currentAlpha);
        }
    }

    public void SetTargetRenderers(SkinnedMeshRenderer[] renderers)
    {
        targetRenderers = renderers;

        runtimeMaterials = new Material[targetRenderers.Length];

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            if (!targetRenderers[i]) continue;

            runtimeMaterials[i] = targetRenderers[i].material;
            runtimeMaterials[i].SetFloat(alphaProperty, currentAlpha);
        }
    }

    public void SetNetAnimator(Animator animator) => netAnimator = animator;
    public void SetFishingController(FishingController controller) => fishingController = controller;

    // ----------------------------
    // DEPLOY / RETRACT API
    // ----------------------------

    public void Deploy()
    {
        netAnimator?.SetBool("DeployNet", true);
        netAnimator?.SetBool("RetractNet", false);
        IncreaseWhirl();
    }

    public void Retract()
    {
        netAnimator?.SetBool("DeployNet", false);
        netAnimator?.SetBool("RetractNet", true);
        DecreaseWhirl();
    }

    // ----------------------------
    // ANIMATION EVENT RECEIVER
    // Called by NetAnimationEventReceiver on the soul boat prefab
    // ----------------------------

    public void OnDeployNetComplete()
    {
        fishingController?.SetFishingActive(true);
    }

    // ----------------------------
    // EXPLICIT API
    // ----------------------------

    public void IncreaseWhirl()
    {
        targetAlpha = maxAlpha;
    }

    public void DecreaseWhirl()
    {
        targetAlpha = minAlpha;
    }
}