using UnityEngine;
using UnityEngine.Splines;
using System.Collections;
using System.Collections.Generic;

public class SoulDeliveryIdentity : MonoBehaviour
{
    public int identity;
}

public class SoulFishInputTube : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SplineContainer localSpline;
    [SerializeField] private LevelWaveModifierControllerTypeB targetModifier;
    [SerializeField] private DoorLockHubController targetLockHub;
    [SerializeField] private WaterLevelExitPipeController targetWaterExit;
    [SerializeField] private FishingController fishingController;
    [SerializeField] private GameObject fishPrefab;
    [SerializeField] private GameObject tubeExtrudePrefab;
    [SerializeField] private Material tubeMaterial;

    [Header("Settings")]
    [SerializeField] private float travelSpeed = 4f;
    [SerializeField] private float curveStrength = 1.5f;
    [SerializeField] private int joinKnotCount = 3;

    [Header("Glow Appearance")]
    [SerializeField] private float tubeGlowRadius = 0.6f;
    [SerializeField] private float tubeGlowIntensity = 2.5f;

    public bool IsBusy { get; private set; }

    private SplineContainer combinedSpline;
    private GameObject extrudedTube;
    private bool isInitialized = false;
    private List<Vector3> _waypoints;

    private void Awake()
    {
        if (localSpline == null) localSpline = GetComponent<SplineContainer>();
        TryFindFishingController();
    }

    private void Start()
    {
        // Initialization happens when targetModifier is linked via LevelSpawner
    }

    public void SetTargetModifier(LevelWaveModifierControllerTypeB modifier)
    {
        targetModifier = modifier;
        InitializeSystem();
    }

    public void SetFishingController(FishingController controller)
    {
        fishingController = controller;
    }

    private void TryFindFishingController()
    {
        if (fishingController != null) return;

        fishingController = Object.FindAnyObjectByType<FishingController>();
        if (fishingController == null && LevelDataController.Instance != null)
        {
            fishingController = LevelDataController.Instance.FishingController;
        }
    }

    public void SetTargetLockHub(DoorLockHubController hub)
    {
        targetLockHub = hub;
        InitializeSystem();
    }

    public void SetTargetWaterExit(WaterLevelExitPipeController pipe)
    {
        targetWaterExit = pipe;
        InitializeSystem();
    }

    public void SetLocalSpline(SplineContainer spline)
    {
        localSpline = spline;
    }

    public void SetWaypoints(List<Vector3> worldPositions)
    {
        _waypoints = worldPositions;
    }

    private SplineContainer GetTargetSplineReceiver()
    {
        if (targetModifier != null) return targetModifier.splineReceiver;
        if (targetLockHub != null) return targetLockHub.pipeConnector;
        if (targetWaterExit != null) return targetWaterExit.splineReceiver;
        return null;
    }

    private Transform GetTargetPipeConnector()
    {
        if (targetModifier != null) return targetModifier.pipeConnector;
        return null;
    }

    private void InitializeSystem()
    {
        bool hasTarget = targetModifier != null || targetLockHub != null || targetWaterExit != null;
        if (!hasTarget || localSpline == null || isInitialized) return;

        TryFindFishingController();

        // 1. Create the combined spline object
        GameObject combinedObj = new GameObject("MergedTubeSpline");
        combinedObj.transform.SetParent(transform, false);
        combinedSpline = combinedObj.AddComponent<SplineContainer>();
        Spline targetPath = combinedSpline.Spline;
        targetPath.Clear();

        // 2. Copy authored knots through world space so any child-transform offset on localSpline is preserved
        foreach (var knot in localSpline.Spline)
        {
            Vector3 worldPos = localSpline.transform.TransformPoint((Vector3)knot.Position);
            Vector3 localPos = combinedSpline.transform.InverseTransformPoint(worldPos);
            targetPath.Add(new BezierKnot((Unity.Mathematics.float3)localPos), TangentMode.AutoSmooth);
        }

        // 3. Insert optional intermediate waypoints (e.g. lock tube arena path)
        if (_waypoints != null && _waypoints.Count > 0)
        {
            float startY     = combinedSpline.transform.TransformPoint((Vector3)targetPath[targetPath.Count - 1].Position).y;
            float targetY    = startY - 0.5f;
            foreach (var pt in _waypoints)
            {
                Vector3 wp   = pt;
                wp.y         = targetY;
                targetPath.Add(new BezierKnot((Unity.Mathematics.float3)combinedSpline.transform.InverseTransformPoint(wp)), TangentMode.AutoSmooth);
            }
        }

        // 4. Join with target spline receiver or pipe connector
        var splineReceiver = GetTargetSplineReceiver();
        var pipeConnector  = GetTargetPipeConnector();

        if (splineReceiver != null)
        {
            // Join with the receiver spline in REVERSE order (Last Knot -> Knot 0)
            var receiverSpline = splineReceiver.Spline;
            // Use the last knot already in targetPath (may be a waypoint, not just the local spline end)
            Vector3 lastTubeKnotWorld  = combinedSpline.transform.TransformPoint((Vector3)targetPath[targetPath.Count - 1].Position);
            Vector3 receiverStartWorld = splineReceiver.transform.TransformPoint((Vector3)receiverSpline[receiverSpline.Count - 1].Position);

            int segments = joinKnotCount + 1;
            bool hasWaypoints = _waypoints != null && _waypoints.Count > 0;
            for (int i = 1; i <= joinKnotCount; i++)
            {
                float t = (float)i / segments;
                Vector3 worldPt = Vector3.Lerp(lastTubeKnotWorld, receiverStartWorld, t);
                if (!hasWaypoints)
                {
                    worldPt += Vector3.up * Mathf.Sin(t * Mathf.PI) * curveStrength;
                    worldPt.y = Mathf.Min(worldPt.y, receiverStartWorld.y);
                }
                targetPath.Add(new BezierKnot((Unity.Mathematics.float3)combinedSpline.transform.InverseTransformPoint(worldPt)), TangentMode.AutoSmooth);
            }

            for (int i = receiverSpline.Count - 1; i >= 0; i--)
            {
                Vector3 worldPos = splineReceiver.transform.TransformPoint((Vector3)receiverSpline[i].Position);
                Vector3 localPos = combinedSpline.transform.InverseTransformPoint(worldPos);
                targetPath.Add(new BezierKnot((Unity.Mathematics.float3)localPos), TangentMode.AutoSmooth);
            }
            Debug.Log($"[SoulFishInputTube] Merged with spline receiver (Reverse: {receiverSpline.Count - 1} -> 0).");
        }
        else if (pipeConnector != null)
        {
            Vector3 lastKnotWorld  = combinedSpline.transform.TransformPoint((Vector3)targetPath[targetPath.Count - 1].Position);
            Vector3 connectorWorld = pipeConnector.position;

            int segments = joinKnotCount + 1;
            for (int i = 1; i <= joinKnotCount; i++)
            {
                float t = (float)i / segments;
                Vector3 worldPt = Vector3.Lerp(lastKnotWorld, connectorWorld, t)
                                + Vector3.up * Mathf.Sin(t * Mathf.PI) * curveStrength;
                worldPt.y = Mathf.Min(worldPt.y, connectorWorld.y);
                targetPath.Add(new BezierKnot(
                    (Unity.Mathematics.float3)combinedSpline.transform.InverseTransformPoint(worldPt)),
                    TangentMode.AutoSmooth);
            }
            targetPath.Add(new BezierKnot(
                (Unity.Mathematics.float3)combinedSpline.transform.InverseTransformPoint(connectorWorld)),
                TangentMode.AutoSmooth);
        }

        // 4. Instantiate and map extrusion
        if (tubeExtrudePrefab != null)
        {
            extrudedTube = Instantiate(tubeExtrudePrefab, combinedObj.transform);
            extrudedTube.transform.localPosition = Vector3.zero;

            var extrudeComp = extrudedTube.GetComponent<SplineExtrude>();
            if (extrudeComp != null)
            {
                extrudeComp.Container = combinedSpline;
                extrudeComp.Rebuild();
            }

            if (tubeMaterial == null)
            {
                var renderer = extrudedTube.GetComponentInChildren<MeshRenderer>();
                if (renderer != null) tubeMaterial = renderer.material;
            }

            if (tubeMaterial != null)
            {
                tubeMaterial.SetFloat("_FishGlowRadius", tubeGlowRadius);
                tubeMaterial.SetFloat("_FishGlowIntensity", tubeGlowIntensity);
            }
        }

        isInitialized = true;
    }

    public void StartSoulDelivery(int identity)
    {
        if (!isInitialized || IsBusy) return;
        StartCoroutine(TravelRoutine(identity));
    }

    private IEnumerator TravelRoutine(int identity)
    {
        IsBusy = true;
        TryFindFishingController();

        GameObject fish = null;
        GameObject tracker = null;

        if (fishPrefab != null)
        {
            fish = Instantiate(fishPrefab, transform);
            
            // 1. Setup identity for the physics trigger
            var idComp = fish.GetComponent<SoulDeliveryIdentity>();
            if (idComp == null) idComp = fish.AddComponent<SoulDeliveryIdentity>();
            idComp.identity = identity;

            // 2. Setup SplineAnimate if present
            var animator = fish.GetComponent<SplineAnimate>();
            if (animator != null)
            {
                animator.Container = combinedSpline;
                animator.AnimationMethod = SplineAnimate.Method.Speed;
                animator.MaxSpeed = travelSpeed;
                animator.Loop = SplineAnimate.LoopMode.Once;
                animator.Restart(true); // Restart and play immediately
            }

            // 3. Setup tracker for Hoover FX (Shader Globals)
            tracker = new GameObject("TubeDeliveryTracker");
            tracker.transform.SetParent(fish.transform, false); // Parented to fish to follow it
            if (fishingController != null)
                fishingController.RegisterTubeDelivery(tracker.transform);

            // 4. Wait for movement to complete
            if (animator != null)
            {
                // We wait until animator finishes or a safety timeout based on length
                float estimatedTime = combinedSpline.CalculateLength() / travelSpeed;
                float elapsed = 0f;
                
                // SplineAnimate.IsPlaying stays true while active
                while (animator.IsPlaying && elapsed < estimatedTime + 1f)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
            else
            {
                // Fallback to manual movement if animator is missing from the prefab instance
                float distance = 0f;
                float totalLength = combinedSpline.CalculateLength();

                while (distance < totalLength)
                {
                    distance += travelSpeed * Time.deltaTime;
                    float t = Mathf.Clamp01(distance / totalLength);

                    Vector3 worldPos = combinedSpline.transform.TransformPoint((Vector3)combinedSpline.EvaluatePosition(t));
                    Vector3 worldTangent = combinedSpline.transform.TransformDirection((Vector3)combinedSpline.EvaluateTangent(t));

                    fish.transform.position = worldPos;
                    if (worldTangent != Vector3.zero)
                        fish.transform.rotation = Quaternion.LookRotation(worldTangent);

                    yield return null;
                }
            }
        }

        // 5. Notify lock hub / water-exit pipe on arrival (wave modifier is notified via physics trigger on the fish)
        targetLockHub?.OnSoulArrived();
        targetWaterExit?.OnSoulArrived();

        // 6. Cleanup
        if (fishingController != null && tracker != null)
            fishingController.UnregisterTubeDelivery(tracker.transform);
        
        if (tracker != null) Destroy(tracker);
        if (fish != null) Destroy(fish, 0.1f); // Short delay to ensure trigger processes

        IsBusy = false;
    }
}
