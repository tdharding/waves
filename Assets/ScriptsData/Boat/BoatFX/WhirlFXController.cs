using UnityEngine;

public class WhirlFXController : MonoBehaviour
{
    [Header("Target Material")]
    [SerializeField] private Material whirlMaterial;

    [Header("Shader Property")]
    [SerializeField] private string alphaProperty = "Alpha";

    [Header("Values")]
    [SerializeField] private float minAlpha = 0f;
    [SerializeField] private float maxAlpha = 1f;

    [Header("Transition")]
    [SerializeField] private float speed = 8f;

    [Header("Animation")]
    [SerializeField] private Animator netAnimator;
    [SerializeField] private FishingController fishingController;

    private float currentAlpha;
    private float targetAlpha;
    private int _alphaPropID;

    void Awake()
    {
        currentAlpha = minAlpha;
        targetAlpha = minAlpha;
        _alphaPropID = Shader.PropertyToID(alphaProperty);

        if (whirlMaterial != null)
            whirlMaterial.SetFloat(_alphaPropID, currentAlpha);
    }

    void Update()
    {
        currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, speed * Time.deltaTime);

        if (whirlMaterial != null)
            whirlMaterial.SetFloat(_alphaPropID, currentAlpha);
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
