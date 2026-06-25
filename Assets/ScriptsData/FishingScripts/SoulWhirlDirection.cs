using System.Collections;
using UnityEngine;

public class SoulWhirlDirection : MonoBehaviour
{
    [Header("Tube Path")]
    [Tooltip("Armature bones ordered from the bone nearest the tube opening to the tube exit")]
    public Transform[] boneChain;

    [Tooltip("Distance along tube axis from boneChain[0] to the actual mesh opening")]
    public float mouthOffset = 0.3f;

    [Header("Suction Zone")]
    [Tooltip("Radius of the inner gizmo circle at the tube mouth")]
    public float mouthRadius = 0.2f;

    [Tooltip("How far the outer suction circle sits beyond the mouth")]
    public float suctionReach = 1.0f;

    [Tooltip("Radius of the outer suction circle — defines the catch envelope")]
    public float suctionRadius = 0.8f;

    [Header("Travel")]
    [Tooltip("Speed fish move along the bone chain after being caught")]
    public float travelSpeed = 1.5f;

    [Tooltip("How many times faster than travelSpeed fish exit the tube when the whirl is released mid-travel")]
    public float reverseSpeedMultiplier = 2f;


    // --------------------------------------------------
    // PUBLIC API — used by FishFishingBehaviour
    // --------------------------------------------------

    public Vector3 MouthPosition => ComputeMouthPosition();
    public Vector3 MouthAxis     => MouthBone.up;
    public Transform[] GetPath()  => boneChain;
    public float EntryRadius      => mouthRadius;

 public bool IsInSector(Vector3 worldPosition)
{
    if (boneChain == null || boneChain.Length < 1) return false;

    // Vector from the mouth to the fish
    Vector3 toFish = worldPosition - MouthPosition;
    
    // Project the fish position onto the mouth's forward axis
    // MouthAxis points "out" from the tube towards the water
    float distAlongAxis = Vector3.Dot(toFish, MouthAxis);

    // 1. Is the fish in front of the mouth and within the suction reach?
    if (distAlongAxis < 0 || distAlongAxis > suctionReach) return false;

    // 2. Calculate the funnel radius at this specific distance along the axis
    float t = distAlongAxis / suctionReach;
    float currentRadiusAtDist = Mathf.Lerp(mouthRadius, suctionRadius, t);

    // 3. Is the fish within that radius (distance from the center axis)?
    Vector3 projectionOnAxis = MouthPosition + MouthAxis * distAlongAxis;
    float distFromAxis = Vector3.Distance(worldPosition, projectionOnAxis);

    return distFromAxis <= currentRadiusAtDist;
}
    // --------------------------------------------------
    // UNITY
    // --------------------------------------------------

    [Header("Whirl Shader")]
    [SerializeField] Material sonarGridMaterial;

    static readonly int WhirlMouthPosID = Shader.PropertyToID("_MouthWorldPos");
    static readonly int PinchStrengthID = Shader.PropertyToID("_PinchStrength");
    static readonly int WhirlRadiusID   = Shader.PropertyToID("_WhirlRadius");

    private bool  _fishingActive     = false;
    private float _currentShaderT    = 0f;
    private float _pinchStrengthMax  = 1.5f;
    private float _whirlRadiusMax    = 6f;
    private float _transitionSpeed   = 3f;

    public void SetFishingActive(bool active, float pinchMin, float pinchMax, float radiusMin, float radiusMax, float speed)
    {
        _fishingActive    = active;
        _pinchStrengthMax = pinchMax;
        _whirlRadiusMax   = radiusMax;
        _transitionSpeed  = speed;
    }

    void LateUpdate()
    {
        if (boneChain == null || boneChain.Length < 2 || sonarGridMaterial == null) return;

        float targetT = _fishingActive ? 1f : 0f;
        _currentShaderT = Mathf.MoveTowards(_currentShaderT, targetT, _transitionSpeed * Time.deltaTime);

        sonarGridMaterial.SetVector(WhirlMouthPosID, MouthPosition);
        sonarGridMaterial.SetFloat(PinchStrengthID,  Mathf.Lerp(0f, _pinchStrengthMax, _currentShaderT));
        sonarGridMaterial.SetFloat(WhirlRadiusID,    Mathf.Lerp(0f, _whirlRadiusMax,   _currentShaderT));
    }

    // --------------------------------------------------
    // INTERNAL
    // --------------------------------------------------

    Transform MouthBone => boneChain[boneChain.Length - 1];
    Vector3 TubeAxis    => MouthBone.up;

    Vector3 ComputeMouthPosition()
    {
        if (boneChain == null || boneChain.Length < 1) return transform.position;
        return MouthBone.position + TubeAxis * mouthOffset;
    }

#if UNITY_EDITOR
    // --------------------------------------------------
    // GIZMO
    // --------------------------------------------------

    void OnDrawGizmosSelected()
    {
        if (boneChain == null || boneChain.Length < 1) return;

        Vector3 mouth    = ComputeMouthPosition();
        Vector3 axis     = TubeAxis;
        Vector3 outerPos = mouth + axis * suctionReach;

        // ── suction zone (yellow) ──────────────────────────────────────────────
        Gizmos.color = Color.yellow;

        // inner disc at tube mouth
        DrawDisc(mouth, axis, mouthRadius);

        // outer disc at suction reach
        DrawDisc(outerPos, axis, suctionRadius);

        // funnel silhouette — 4 connecting lines (top/bottom/left/right)
        Vector3 perp1 = Vector3.Cross(axis, Vector3.up);
        if (perp1.sqrMagnitude < 0.001f) perp1 = Vector3.Cross(axis, Vector3.forward);
        perp1.Normalize();
        Vector3 perp2 = Vector3.Cross(axis, perp1).normalized;

        Gizmos.DrawLine(mouth + perp1 * mouthRadius,    outerPos + perp1 * suctionRadius);
        Gizmos.DrawLine(mouth - perp1 * mouthRadius,    outerPos - perp1 * suctionRadius);
        Gizmos.DrawLine(mouth + perp2 * mouthRadius,    outerPos + perp2 * suctionRadius);
        Gizmos.DrawLine(mouth - perp2 * mouthRadius,    outerPos - perp2 * suctionRadius);

        // dot at mouth
        Gizmos.DrawSphere(mouth, 0.04f);

        // ── travel path (cyan) ────────────────────────────────────────────────
        Gizmos.color = Color.cyan;

        Vector3 prev = mouth;
        foreach (Transform bone in boneChain)
        {
            if (bone == null) continue;
            Gizmos.DrawLine(prev, bone.position);
            Gizmos.DrawSphere(bone.position, 0.03f);
            prev = bone.position;
        }
    }

    static readonly int discSegments = 24;

    void DrawDisc(Vector3 center, Vector3 normal, float radius)
    {
        Vector3 perp = Vector3.Cross(normal, Vector3.up);
        if (perp.sqrMagnitude < 0.001f) perp = Vector3.Cross(normal, Vector3.forward);
        perp = perp.normalized * radius;

        Vector3 prev = center + perp;
        for (int i = 1; i <= discSegments; i++)
        {
            float   angle = i / (float)discSegments * 360f * Mathf.Deg2Rad;
            Vector3 right = Vector3.Cross(normal, perp.normalized).normalized;
            Vector3 next  = center + Mathf.Cos(angle) * perp.normalized * radius
                                   + Mathf.Sin(angle) * right * radius;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
#endif

    // --------------------------------------------------
    // COROUTINE HELPER — called by FishFishingBehaviour
    // --------------------------------------------------

    // onCaptured fires when the fish reaches the last bone (capture confirmed).
    // onReleased fires when the whirl was released mid-travel and the fish has exited the tube.
    // isReleased is polled each frame to detect a mid-travel release.
    public IEnumerator TravelAlongPath(Transform fish, System.Action onCaptured, System.Action onReleased, System.Func<bool> isReleased)
    {
        if (boneChain == null || boneChain.Length == 0)
        {
            onReleased?.Invoke();
            yield break;
        }

        Vector3 defaultScale = fish.localScale;
        int entryBoneIdx = boneChain.Length - 1;
        int currentBoneIdx = entryBoneIdx;
        bool released = false;

        // Forward travel: boneChain[entryBoneIdx] → boneChain[0]
        while (currentBoneIdx >= 0)
        {
            if (isReleased != null && isReleased()) { released = true; break; }

            Transform bone = boneChain[currentBoneIdx];
            if (bone == null) { currentBoneIdx--; continue; }

            bool isFinal = (currentBoneIdx == 0);

            while (fish != null)
            {
                if (isReleased != null && isReleased()) { released = true; break; }

                // Lock fish to the bone's local space so boat rotation causes no drag
                Vector3 localPos = bone.InverseTransformPoint(fish.position);
                yield return null;
                if (fish == null) break;
                fish.position = bone.TransformPoint(localPos);

                float dist = Vector3.Distance(fish.position, bone.position);
                if (dist <= 0.02f) break;

                fish.position = Vector3.MoveTowards(fish.position, bone.position, travelSpeed * Time.deltaTime);

                if (isFinal)
                {
                    float t = 1f - Mathf.Clamp01(dist / 0.3f);
                    fish.localScale = Vector3.Lerp(defaultScale, Vector3.zero, t);
                }
            }

            if (released) break;
            currentBoneIdx--;
        }

        if (!released)
        {
            onCaptured?.Invoke();
            yield break;
        }

        // ── Reversal ─────────────────────────────────────────────────────────
        // Fish didn't reach the capture bone; exit back toward the tube entry.
        if (fish != null)
            fish.localScale = defaultScale;

        float reverseSpeed = travelSpeed * reverseSpeedMultiplier;
        int reverseStartIdx = Mathf.Min(currentBoneIdx + 1, entryBoneIdx);

        for (int i = reverseStartIdx; i <= entryBoneIdx; i++)
        {
            Transform bone = boneChain[i];
            if (bone == null) continue;

            while (fish != null)
            {
                Vector3 localPos = bone.InverseTransformPoint(fish.position);
                yield return null;
                if (fish == null) break;
                fish.position = bone.TransformPoint(localPos);

                float dist = Vector3.Distance(fish.position, bone.position);
                if (dist <= 0.02f) break;

                fish.position = Vector3.MoveTowards(fish.position, bone.position, reverseSpeed * Time.deltaTime);
            }
        }

        onReleased?.Invoke();
    }
}
