using UnityEngine;
using UnityEngine.VFX;

public class BadGuySnakeMovement : MonoBehaviour
{
    [Header("VFX")]
    public VisualEffect snakeVFX;

    // Add this private field at the top
private SnakeState previousState;

    [Header("Head Reference")]
    public Transform headVisual;

    [Header("Boat Target")]
    public Transform boat;
    public float followSpeed = 3f;

    [Header("Detection")]
    [Tooltip("How far the snake can see")]
    public float detectionRange = 10f;
    [Tooltip("Field of view angle in degrees")]
    public float fieldOfView = 90f;
    [Tooltip("Layer mask for line of sight check — should block vision")]
    public LayerMask sightBlockLayer;

    public bool IsChasing => state == SnakeState.Chasing;

    [Header("Steering")]
    public LayerMask obstacleLayer;
    public float lookAheadDistance = 0.07f;
    public float castRadius = 0.3f;
    public int fanRayCount = 12;
    public float fanAngle = 360f;
    public float avoidanceWeightMin = 1f;
    public float avoidanceWeightMax = 8f;

    [Header("Body Straightening")]
    public float straightenStrengthP1 = 0.4f;
    public float straightenStrengthP2 = 0.5f;
    public float straightenStrengthP3 = 0f;

    [Header("Body Follow")]
    public float bodyFollowSpeed = 5f;
    public float spacing = 0.15f;

    [Header("Body Point Avoidance")]
    public float bodyAvoidanceRadius = 0.5f;
    public float bodyAvoidanceStrength = 1f;

    [Header("Bezier Control Points")]
    public Transform p0;
    public Transform p1;
    public Transform p2;
    public Transform p3;

    [Header("Water")]
    public Transform waterTransform;
    public float heightMultiplier = 1f;
    public float extraYOffset = 0f;

    private float spacing01;
    private float spacing12;
    private float spacing23;

    private Material waveMaterial;

    private Vector3 currentHeading = Vector3.forward;

    // ── Detection State ──
    private enum SnakeState { Idle, Chasing, SearchingLastKnown }
    private SnakeState state = SnakeState.Idle;
    private Vector3 lastKnownBoatPos;
    private bool hasLastKnownPos = false;

    // ── Sonar reference ──
    private SonarSystemController sonarController;

    void Awake()
    {
        ResolveSceneReferences();
    }

    void Start()
    {
        BakeSpacings();

        sonarController = FindObjectOfType<SonarSystemController>();

        if (waterTransform != null)
        {
            var renderer = waterTransform.GetComponent<MeshRenderer>();
            if (renderer != null)
                waveMaterial = renderer.sharedMaterial;
        }
    }

    void BakeSpacings()
    {
        spacing01 = (p0 != null && p1 != null) ? Vector3.Distance(p0.position, p1.position) : spacing;
        spacing12 = (p1 != null && p2 != null) ? Vector3.Distance(p1.position, p2.position) : spacing;
        spacing23 = (p2 != null && p3 != null) ? Vector3.Distance(p2.position, p3.position) : spacing;
    }

    void ResolveSceneReferences()
    {
        if (LevelDataController.Instance == null) return;

        if (boat == null)
            boat = LevelDataController.Instance.GetBoatRoot();

        if (waterTransform == null)
            waterTransform = LevelDataController.Instance.GetWaveTransform();
    }

    void Update()
    {
        if (waveMaterial == null) return;

        UpdateDetectionState();

// Then in Update() after UpdateDetectionState()
if (state != previousState)
{
    switch (state)
    {
        case SnakeState.Chasing:
            Debug.Log("[Snake] Following boat");
            break;
        case SnakeState.SearchingLastKnown:
            Debug.Log("[Snake] Moving to last known position");
            break;
        case SnakeState.Idle:
            Debug.Log("[Snake] Not following");
            break;
    }
    previousState = state;
}

        MoveHeadTowardBoat();
        FollowChain();
        PushBodyFromWalls();

        UpdatePoint(p0, "Position1");
        UpdatePoint(p1, "Position2");
        UpdatePoint(p2, "Position3");
        UpdatePoint(p3, "Position4");
    }

    // =====================================================
    // DETECTION
    // =====================================================

    void UpdateDetectionState()
    {
        if (boat == null) return;

        // ── Sonar override — always chase ──
        if (sonarController != null && sonarController.IsSonarActive)
        {
            lastKnownBoatPos = boat.position;
            hasLastKnownPos = true;
            state = SnakeState.Chasing;
            return;
        }

        bool canSee = CanSeeBoat();

        if (canSee)
        {
            lastKnownBoatPos = boat.position;
            hasLastKnownPos = true;
            state = SnakeState.Chasing;
        }
        else if (hasLastKnownPos && state == SnakeState.Chasing)
        {
            // Just lost sight — go to last known
            state = SnakeState.SearchingLastKnown;
        }
        else if (state == SnakeState.SearchingLastKnown)
        {
            // Check if we've reached the last known position
            float distToLastKnown = Vector3.Distance(
                new Vector3(p0.position.x, 0f, p0.position.z),
                new Vector3(lastKnownBoatPos.x, 0f, lastKnownBoatPos.z)
            );

            if (distToLastKnown < 0.5f)
                state = SnakeState.Idle;
        }
    }

    bool CanSeeBoat()
    {
        if (boat == null || p0 == null) return false;

        Vector3 toBoat = boat.position - p0.position;
        toBoat.y = 0f;
        float distance = toBoat.magnitude;

        // ── Range check ──
        if (distance > detectionRange) return false;

        // ── FOV check ──
        Vector3 facingDir = (headVisual != null)
            ? new Vector3(-headVisual.forward.x, 0f, -headVisual.forward.z).normalized
            : currentHeading;

        float angle = Vector3.Angle(facingDir, toBoat.normalized);
        if (angle > fieldOfView * 0.5f) return false;

        // ── Line of sight check ──
        if (Physics.Raycast(p0.position, toBoat.normalized, distance, sightBlockLayer))
            return false;

        return true;
    }

    // =====================================================
    // MOVEMENT
    // =====================================================

    void MoveHeadTowardBoat()
    {
        if (p0 == null) return;

        // ── Determine chase target based on state ──
        Vector3 chaseTarget;

        switch (state)
        {
            case SnakeState.Chasing:
                chaseTarget = boat.position;
                break;
            case SnakeState.SearchingLastKnown:
                chaseTarget = lastKnownBoatPos;
                break;
// NEW
            default:
                if (hasLastKnownPos)
                    chaseTarget = lastKnownBoatPos;
                else
                    return;
                break;
        }

        Vector3 toTarget = chaseTarget - p0.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.001f)
            toTarget.Normalize();

        // ── Fan of SphereCasts ──
        Vector3 avoidance = Vector3.zero;

        Vector3 fanForward = (headVisual != null)
            ? new Vector3(-headVisual.forward.x, 0f, -headVisual.forward.z).normalized
            : toTarget;

        for (int i = 0; i < fanRayCount; i++)
        {
            float t = fanRayCount == 1 ? 0.5f : (float)i / (fanRayCount - 1);
            float angle = Mathf.Lerp(-fanAngle * 0.5f, fanAngle * 0.5f, t);

            Vector3 rayDir = Quaternion.Euler(0f, angle, 0f) * fanForward;
            Vector3 rayOrigin = p0.position;

            if (Physics.SphereCast(rayOrigin, castRadius, rayDir,
                out RaycastHit hit, lookAheadDistance, obstacleLayer))
            {
                float proximity = 1f - (hit.distance / lookAheadDistance);
                float dynamicWeight = Mathf.Lerp(avoidanceWeightMin, avoidanceWeightMax, proximity);
                Vector3 pushDir = -rayDir;
                pushDir.y = 0f;
                avoidance += pushDir * proximity * dynamicWeight;
            }
        }

        Vector3 desiredDir = toTarget + avoidance;
        desiredDir.y = 0f;

        if (desiredDir.sqrMagnitude > 0.001f)
            desiredDir.Normalize();
        else
            desiredDir = currentHeading;

        currentHeading = Vector3.Slerp(currentHeading, desiredDir,
            Time.deltaTime * followSpeed * 2f);
        currentHeading.y = 0f;
        currentHeading.Normalize();

        Vector3 newPos = p0.position + currentHeading * followSpeed * Time.deltaTime;
        newPos.y = p0.position.y;
        p0.position = newPos;
    }

    // =====================================================
    // BODY
    // =====================================================

    void FollowChain()
    {
        Vector3 headForward = (headVisual != null)
            ? new Vector3(-headVisual.forward.x, 0f, -headVisual.forward.z).normalized
            : currentHeading;

        Follow(p1, p0, headForward, spacing01, straightenStrengthP1);
        Follow(p2, p1, headForward, spacing12, straightenStrengthP2);
        Follow(p3, p2, headForward, spacing23, straightenStrengthP3);
    }

    void Follow(Transform follower, Transform leader, Vector3 headForward, float dist, float straighten)
    {
        if (follower == null || leader == null) return;

        Vector3 leaderXZ   = new Vector3(leader.position.x,   0f, leader.position.z);
        Vector3 followerXZ = new Vector3(follower.position.x, 0f, follower.position.z);
        Vector3 dir        = leaderXZ - followerXZ;

        if (dir.sqrMagnitude < 0.0001f) return;

        dir.Normalize();
        Vector3 chasePos = leaderXZ - dir * dist;

        Vector3 headXZ = new Vector3(p0.position.x, 0f, p0.position.z);
        Vector3 straightPos = headXZ - headForward * dist;

        Vector3 targetXZ = Vector3.Lerp(chasePos, straightPos, straighten);

        follower.position = new Vector3(
            targetXZ.x,
            follower.position.y,
            targetXZ.z
        );
    }

    void PushBodyFromWalls()
    {
        PushPointFromWalls(p1);
        PushPointFromWalls(p2);
        PushPointFromWalls(p3);
    }

    void PushPointFromWalls(Transform point)
    {
        if (point == null) return;

        Collider[] hits = Physics.OverlapSphere(point.position, bodyAvoidanceRadius, obstacleLayer);

        foreach (Collider hit in hits)
        {
            Vector3 closest = hit.ClosestPoint(point.position);
            Vector3 pushDir = point.position - closest;
            pushDir.y = 0f;

            if (pushDir.sqrMagnitude < 0.0001f) continue;

            float distance = pushDir.magnitude;
            float force = (1f - (distance / bodyAvoidanceRadius)) * bodyAvoidanceStrength;

            pushDir.Normalize();
            Vector3 newPos = point.position + pushDir * force * Time.deltaTime;
            newPos.y = point.position.y;
            point.position = newPos;
        }
    }

    // =====================================================
    // VFX + WAVE
    // =====================================================

    void UpdatePoint(Transform point, string vfxName)
    {
        if (point == null) return;

        Vector3 worldPos = point.position;
        worldPos.y = GetWaveHeightAtPosition(worldPos);
        point.position = worldPos;

        Vector3 localPos = snakeVFX.transform.InverseTransformPoint(worldPos);
        snakeVFX.SetVector3(vfxName, localPos);
    }

    float GetWaveHeightAtPosition(Vector3 worldPos)
    {
        var p = WaveUtils.ReadParams(waterTransform, waveMaterial);
        return p.origin.y + WaveUtils.SampleHeight(worldPos, p, heightMultiplier) + extraYOffset;
    }

    // =====================================================
    // GIZMOS
    // =====================================================

    void OnDrawGizmos()
    {
        if (p0 == null) return;

        Vector3 fanForward = Vector3.zero;
        if (headVisual != null)
            fanForward = new Vector3(-headVisual.forward.x, 0f, -headVisual.forward.z).normalized;
        else if (Application.isPlaying)
            fanForward = currentHeading;
        else
            fanForward = -transform.forward;

        // ── Fan rays ──
        for (int i = 0; i < fanRayCount; i++)
        {
            float t = fanRayCount == 1 ? 0.5f : (float)i / (fanRayCount - 1);
            float angle = Mathf.Lerp(-fanAngle * 0.5f, fanAngle * 0.5f, t);
            Vector3 rayDir = Quaternion.Euler(0f, angle, 0f) * fanForward;

            bool didHit = Physics.SphereCast(
                p0.position, castRadius, rayDir,
                out RaycastHit hit, lookAheadDistance, obstacleLayer
            );

            Gizmos.color = didHit ? Color.red : Color.yellow;
            Vector3 rayEnd = p0.position + rayDir * (didHit ? hit.distance : lookAheadDistance);
            Gizmos.DrawLine(p0.position, rayEnd);
            Gizmos.DrawWireSphere(p0.position, castRadius);
            Gizmos.DrawWireSphere(rayEnd, castRadius);

            if (didHit)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(hit.point, hit.normal * 0.5f);
            }
        }

        // ── Detection range and FOV ──
        Gizmos.color = state == SnakeState.Chasing ? Color.red : Color.green;
        DrawFOVGizmo(fanForward);

        // ── Last known position ──
        if (hasLastKnownPos)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(lastKnownBoatPos, 0.2f);
            Gizmos.DrawLine(p0.position, lastKnownBoatPos);
        }

        // ── Body avoidance spheres ──
        DrawBodyPointGizmo(p1);
        DrawBodyPointGizmo(p2);
        DrawBodyPointGizmo(p3);
    }

    void DrawFOVGizmo(Vector3 forward)
    {
        if (forward == Vector3.zero) return;

        // Detection range circle
        Gizmos.DrawWireSphere(p0.position, detectionRange);

        // FOV arc lines
        Vector3 leftBound  = Quaternion.Euler(0f, -fieldOfView * 0.5f, 0f) * forward;
        Vector3 rightBound = Quaternion.Euler(0f,  fieldOfView * 0.5f, 0f) * forward;

        Gizmos.DrawRay(p0.position, leftBound  * detectionRange);
        Gizmos.DrawRay(p0.position, rightBound * detectionRange);
    }

    void DrawBodyPointGizmo(Transform point)
    {
        if (point == null) return;

        Collider[] hits = Physics.OverlapSphere(point.position, bodyAvoidanceRadius, obstacleLayer);

        Gizmos.color = hits.Length > 0
            ? new Color(1f, 0f, 0f, 0.6f)
            : new Color(1f, 0.5f, 0f, 0.25f);

        Gizmos.DrawWireSphere(point.position, bodyAvoidanceRadius);
    }
}