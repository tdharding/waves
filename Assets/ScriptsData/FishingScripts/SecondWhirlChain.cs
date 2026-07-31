using UnityEngine;

// Runs after SoulWhirlDirection (50) so the mouth it follows is already length-adjusted this frame.
[DefaultExecutionOrder(60)]
public class SecondWhirlChain : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private SoulWhirlDirection firstWhirl;

    [Header("Bone Chain")]
    [Tooltip("4 bones ordered from closest to the whirl mouth to furthest away")]
    [SerializeField] private Transform[] boneChain;

    public enum OffsetSpace
    {
        TubeFrame,  // Y = back down the tube, X = world-up flattened against the tube, Z = lean axis
        MouthBone,  // the mouth bone's own axes — rolls with the whirl and the boat
        Boat,       // this component's transform, i.e. boat-local
        World       // unrotated world axes
    }

    [Header("Chain Settings")]
    [Tooltip("Nudges where this chain attaches to the first whirl's mouth. " +
             "Purely visual — the catch envelope is unaffected.")]
    [SerializeField] private Vector3 mouthAttachOffset = Vector3.zero;

    [Tooltip("Which axes mouthAttachOffset is measured along. TubeFrame stays gravity-aligned; " +
             "MouthBone sticks to the mesh as the whirl rolls — use that for fixing a seam.")]
    [SerializeField] private OffsetSpace mouthAttachOffsetSpace = OffsetSpace.TubeFrame;

    [SerializeField] private float segmentLength      = 0.4f;
    [SerializeField] private float turnLeanMultiplier = 1f;
    [SerializeField] private float turnSmoothSpeed    = 5f;

    [Header("Material Fade")]
    [SerializeField] private SkinnedMeshRenderer targetRenderer;
    [SerializeField] private float fadeSpeed = 4f;
    [SerializeField] private float maxAlpha  = 1f;

    [Header("Debug")]
    [SerializeField] private bool startActive = false;

    private static readonly int TotalAlphaProp = Shader.PropertyToID("_TotalAlpha");

    private float                _currentAlpha;
    private float                _targetAlpha;
    private MaterialPropertyBlock _mpb;

    private float _smoothedTurnRate;

    void Awake()
    {
        _mpb = new MaterialPropertyBlock();
    }

    void Start()
    {
        if (startActive)
            SetActive(true);
    }

    void Update()
    {
        _currentAlpha = Mathf.MoveTowards(_currentAlpha, _targetAlpha, fadeSpeed * Time.deltaTime);
        if (targetRenderer)
        {
            _mpb.SetFloat(TotalAlphaProp, _currentAlpha);
            targetRenderer.SetPropertyBlock(_mpb);
        }
    }

    void LateUpdate()
    {
        if (!firstWhirl || boneChain == null || boneChain.Length < 1) return;

        Vector3 mouthPos = firstWhirl.MouthPosition;
        Vector3 axis     = firstWhirl.MouthAxis;

        float turnTarget = Input.GetKey(KeyCode.RightArrow) ? 1f : Input.GetKey(KeyCode.LeftArrow) ? -1f : 0f;
        _smoothedTurnRate = Mathf.Lerp(_smoothedTurnRate, turnTarget, Time.deltaTime * turnSmoothSpeed);

        // Bone 0 — explicit frame: -Y out of mouth, +X toward world up, Z as lean axis
        Vector3 xDir = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
        if (xDir.sqrMagnitude < 0.001f)
            xDir = Vector3.ProjectOnPlane(Vector3.forward, axis).normalized;
        Vector3 yDir = -axis;
        Vector3 zDir = Vector3.Cross(xDir, yDir);

        Matrix4x4 m = Matrix4x4.identity;
        m.SetColumn(0, new Vector4(xDir.x, xDir.y, xDir.z, 0));
        m.SetColumn(1, new Vector4(yDir.x, yDir.y, yDir.z, 0));
        m.SetColumn(2, new Vector4(zDir.x, zDir.y, zDir.z, 0));

        boneChain[0].position = AttachPoint(mouthPos, xDir, yDir, zDir);
        boneChain[0].rotation = m.rotation;

        for (int i = 1; i < boneChain.Length && i < 4; i++)
        {
            if (boneChain[i] == null || boneChain[i - 1] == null) continue;

            float xLean = -_smoothedTurnRate * turnLeanMultiplier * i;
            boneChain[i].rotation = boneChain[0].rotation * Quaternion.Euler(xLean, 0, 0);
            boneChain[i].position = boneChain[i - 1].position + boneChain[i].rotation * (-Vector3.up) * segmentLength;
        }
    }

    public void SetActive(bool active)
    {
        _targetAlpha = active ? maxAlpha : 0f;

        if (active && firstWhirl)
            SnapBonesToMouth();
    }

    // Applies mouthAttachOffset in whichever space mouthAttachOffsetSpace selects.
    // xDir/yDir/zDir are the tube frame the caller has already built.
    Vector3 AttachPoint(Vector3 mouthPos, Vector3 xDir, Vector3 yDir, Vector3 zDir)
    {
        if (mouthAttachOffset == Vector3.zero) return mouthPos;

        switch (mouthAttachOffsetSpace)
        {
            case OffsetSpace.MouthBone:
                Transform mouthBone = firstWhirl.MouthBoneTransform;
                if (mouthBone != null)
                    return mouthPos + mouthBone.rotation * mouthAttachOffset;
                break;

            case OffsetSpace.Boat:
                return mouthPos + transform.TransformDirection(mouthAttachOffset);

            case OffsetSpace.World:
                return mouthPos + mouthAttachOffset;
        }

        // TubeFrame (and the fallback if the mouth bone is missing)
        return mouthPos
             + xDir * mouthAttachOffset.x
             + yDir * mouthAttachOffset.y
             + zDir * mouthAttachOffset.z;
    }

    void SnapBonesToMouth()
    {
        Vector3    mouthPos  = firstWhirl.MouthPosition;
        Vector3    axis      = firstWhirl.MouthAxis;
        Quaternion targetRot = axis.sqrMagnitude > 0.0001f
            ? Quaternion.FromToRotation(-Vector3.up, axis)
            : Quaternion.identity;

        // Same frame LateUpdate builds, so the snap lands where the chain will settle — no pop.
        Vector3 xDir = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
        if (xDir.sqrMagnitude < 0.001f)
            xDir = Vector3.ProjectOnPlane(Vector3.forward, axis).normalized;
        Vector3 yDir = -axis;
        Vector3 zDir = Vector3.Cross(xDir, yDir);

        Vector3 attach = AttachPoint(mouthPos, xDir, yDir, zDir);

        for (int i = 0; i < boneChain.Length; i++)
        {
            if (boneChain[i] == null) continue;
            boneChain[i].rotation = targetRot;
            boneChain[i].position = attach + targetRot * (-Vector3.up) * (segmentLength * i);
        }
    }
}
