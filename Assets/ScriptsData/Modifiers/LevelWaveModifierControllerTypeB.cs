using System.Collections.Generic;
using UnityEngine;

public class LevelWaveModifierControllerTypeB : MonoBehaviour
{
    [Header("Configuration")]
    public float speedBoost;
    public float frequencyBoost;
    public float rippleDepthBoost;
    public Transform pipeConnector;

    [Header("Debug Info")]
    [SerializeField] private bool showDebug = true;
    [SerializeField] private Vector3 debugWorldPosition;
    [SerializeField] private Vector4 debugWaveCenterSent;
    [SerializeField] private Vector4 debugMaterialWaveCenter;

    [Header("Animation")]
    [SerializeField] private Animator plungerAnimator;
    [SerializeField] private string animationTrigger = "Active";
    [SerializeField] private float speedIncreaseRate = 0.2f;
    [SerializeField] private float maxAnimationSpeed = 3f;

    private WaveMaterialController waveController;
    private Transform wavePlane;
    private float currentAnimSpeed = 1f;
    private bool isAnimating = false;
    private Vector4 modifierWaveCenterSent;

    private static readonly List<LevelWaveModifierControllerTypeB> allModifiers = new();

    private bool hasBaseline;
    private int currentSoulIdentity = -1;
    private SoulEnterPipeTrigger associatedTrigger;

    public void SetTrigger(SoulEnterPipeTrigger trigger) => associatedTrigger = trigger;

    public void Init(WaveMaterialController controller)
    {
        waveController = controller;
        debugWorldPosition = transform.position;
        if (plungerAnimator == null) plungerAnimator = GetComponentInChildren<Animator>();
    }

    public void OnSoulArrived(int identity)
    {
        currentSoulIdentity = identity;
        hasBaseline = true;
        LockOthers();
        ApplyWaveCenter(); 
        
        if (plungerAnimator != null)
        {
            currentAnimSpeed = 0.5f; 
            plungerAnimator.speed = currentAnimSpeed;
            plungerAnimator.SetBool(animationTrigger, true);
            isAnimating = true;
        }
        else
        {
            OnPlungeTrigger();
        }

        if (associatedTrigger != null) associatedTrigger.SetLocked(true);

        Debug.Log($"[TypeBMod] Soul {identity} arrived via tube.");
    }

    private void Awake()
    {
        allModifiers.Add(this);
        if (waveController == null)
            waveController = FindObjectOfType<WaveMaterialController>();
        if (plungerAnimator == null) plungerAnimator = GetComponentInChildren<Animator>();
        debugWorldPosition = transform.position;
    }

    private void Start()
    {
        wavePlane = LevelDataController.Instance?.GetWaveTransform();
    }

    private void Update()
    {
        if (isAnimating && plungerAnimator != null)
        {
            currentAnimSpeed = Mathf.Min(currentAnimSpeed + speedIncreaseRate * Time.deltaTime, maxAnimationSpeed);
            plungerAnimator.speed = currentAnimSpeed;
        }
    }

    private void OnDestroy()
    {
        allModifiers.Remove(this);
    }

    public void OnPlungeTrigger()
    {
        if (hasBaseline)
        {
            ApplyBoosts();
        }
    }

    public void SetLocked(bool locked)
    {
        if (associatedTrigger != null) associatedTrigger.SetLocked(locked);
    }

    private void ApplyWaveCenter()
    {
        if (!waveController) return;

        Vector3 worldPos = transform.position;
        debugWorldPosition = worldPos;

        Vector4 wc;
        if (wavePlane != null)
        {
            Vector3 local = wavePlane.InverseTransformPoint(worldPos);
            wc = new Vector4(local.x, 0f, -local.y, 0f);
        }
        else
        {
            wc = new Vector4(worldPos.x, worldPos.y, worldPos.z, 0f);
        }

        modifierWaveCenterSent = wc;
    }

    private void ApplyBoosts()
    {
        if (!waveController || !hasBaseline) return;

        waveController.SetModifierBoost(true, modifierWaveCenterSent, frequencyBoost, speedBoost, rippleDepthBoost);

        if (showDebug)
        {
            Debug.Log($"[TypeBMod] FEED BOOST: Freq={frequencyBoost:F2} | Speed={speedBoost:F2}");
            debugWaveCenterSent = modifierWaveCenterSent;
        }
    }

    private void RestoreBaseline()
    {
        if (!hasBaseline || !waveController) return;
        
        waveController.SetModifierBoost(false, Vector4.zero, 0, 0, 0);
        hasBaseline = false;
        currentSoulIdentity = -1;
    }

    private void LockOthers()
    {
        foreach (var mod in allModifiers)
            if (mod != this) mod.SetLocked(true);
        
        var typeAMods = FindObjectsByType<LevelWaveModifierControllerTypeA>(FindObjectsSortMode.None);
        foreach (var mod in typeAMods)
            mod.SetLocked(true);
    }

    private static void UnlockAll()
    {
        foreach (var mod in allModifiers)
            mod.SetLocked(false);
        
        var typeAMods = FindObjectsByType<LevelWaveModifierControllerTypeA>(FindObjectsSortMode.None);
        foreach (var mod in typeAMods)
            mod.SetLocked(false);
    }

    private void OnDrawGizmos()
    {
        if (!showDebug) return;

        Vector3 pos = transform.position;

        Gizmos.color = hasBaseline
            ? new Color(0f, 1f, 0.8f, 0.9f)
            : new Color(0.4f, 0.6f, 1f, 0.6f);

        Gizmos.DrawWireSphere(pos, 0.6f);

        float arm = 1.5f;
        Gizmos.DrawLine(pos - Vector3.right   * arm, pos + Vector3.right   * arm);
        Gizmos.DrawLine(pos - Vector3.forward * arm, pos + Vector3.forward * arm);
        Gizmos.DrawLine(pos - Vector3.up * 0.5f,    pos + Vector3.up * 0.5f);

        if (pipeConnector != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(pos, pipeConnector.position);
            Gizmos.DrawWireSphere(pipeConnector.position, 0.2f);
        }

#if UNITY_EDITOR
        string state = hasBaseline ? "ACTIVE (TypeB)" : "idle (TypeB)";
        string label = $"WaveModifier [{state}]\n" +
                       $"World pos: ({pos.x:F3}, {pos.y:F3}, {pos.z:F3})\n" +
                       $"Current Soul: {currentSoulIdentity}";
        UnityEditor.Handles.Label(pos + Vector3.up * 1.0f, label);
#endif
    }
}
