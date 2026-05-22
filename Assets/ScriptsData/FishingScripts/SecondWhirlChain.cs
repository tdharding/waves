using UnityEngine;

public class SecondWhirlChain : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private SoulWhirlDirection firstWhirl;

    [Header("Bone Chain")]
    [Tooltip("4 bones ordered from closest to the whirl mouth to furthest away")]
    [SerializeField] private Transform[] boneChain;

    [Header("Chain Settings")]
    [SerializeField] private float segmentLength      = 0.4f;
    [SerializeField] private float turnLeanMultiplier = 1f;    // Z lean per bone per deg/sec of turn
    [SerializeField] private float turnSmoothSpeed    = 5f;    // how quickly lean builds and decays

    [Header("Material Fade")]
    [SerializeField] private SkinnedMeshRenderer targetRenderer;
    [SerializeField] private float fadeSpeed = 4f;
    [SerializeField] private float maxAlpha  = 1f;

    [Header("Debug")]
    [SerializeField] private bool startActive = false;

    private static readonly int TotalAlphaProp = Shader.PropertyToID("_TotalAlpha");

    private float     _currentAlpha;
    private float     _targetAlpha;
    private Material  _mat;

    private Vector3   _prevAxis;
    private float     _smoothedTurnRate;

    void Awake()
    {
        if (targetRenderer)
            _mat = targetRenderer.material;
    }

    void Start()
    {
        if (firstWhirl)
            _prevAxis = firstWhirl.MouthAxis;

        if (startActive)
            SetActive(true);
    }

    void Update()
    {
        _currentAlpha = Mathf.MoveTowards(_currentAlpha, _targetAlpha, fadeSpeed * Time.deltaTime);
        if (_mat) _mat.SetFloat(TotalAlphaProp, _currentAlpha);
    }

    void LateUpdate()
    {
        if (!firstWhirl || boneChain == null || boneChain.Length < 1) return;

        Vector3 mouthPos = firstWhirl.MouthPosition;
        Vector3 axis     = firstWhirl.MouthAxis;

        // Measure horizontal turn rate (degrees/sec) from change in mouth direction
        float turnDelta = 0f;
        if (_prevAxis.sqrMagnitude > 0.0001f)
        {
            Vector3 prevFlat    = Vector3.ProjectOnPlane(_prevAxis, Vector3.up).normalized;
            Vector3 currentFlat = Vector3.ProjectOnPlane(axis,      Vector3.up).normalized;
            if (prevFlat.sqrMagnitude > 0.0001f && currentFlat.sqrMagnitude > 0.0001f)
                turnDelta = Vector3.SignedAngle(prevFlat, currentFlat, Vector3.up) / Time.deltaTime;
        }
        _prevAxis = axis;

        _smoothedTurnRate = Mathf.Lerp(_smoothedTurnRate, turnDelta, Time.deltaTime * turnSmoothSpeed);

        // Bone 0 — direct follow, no lean
        boneChain[0].position = mouthPos;
        boneChain[0].rotation = Quaternion.FromToRotation(-Vector3.up, axis);

        // Each following bone multiplies Z lean by its index
        for (int i = 1; i < boneChain.Length && i < 4; i++)
        {
            if (boneChain[i] == null || boneChain[i - 1] == null) continue;

            float zLean = -_smoothedTurnRate * turnLeanMultiplier * i;
            boneChain[i].rotation = boneChain[0].rotation * Quaternion.Euler(0, 0, zLean);
            boneChain[i].position = boneChain[i - 1].position + boneChain[i].rotation * (-Vector3.up) * segmentLength;
        }
    }

    public void SetActive(bool active)
    {
        _targetAlpha = active ? maxAlpha : 0f;

        if (active && firstWhirl)
            SnapBonesToMouth();
    }

    void SnapBonesToMouth()
    {
        Vector3    mouthPos  = firstWhirl.MouthPosition;
        Vector3    axis      = firstWhirl.MouthAxis;
        Quaternion targetRot = axis.sqrMagnitude > 0.0001f
            ? Quaternion.FromToRotation(-Vector3.up, axis)
            : Quaternion.identity;

        for (int i = 0; i < boneChain.Length; i++)
        {
            if (boneChain[i] == null) continue;
            boneChain[i].rotation = targetRot;
            boneChain[i].position = mouthPos + targetRot * (-Vector3.up) * (segmentLength * i);
        }
    }
}
