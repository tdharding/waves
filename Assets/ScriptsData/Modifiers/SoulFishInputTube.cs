using UnityEngine;
using UnityEngine.Splines;
using System.Collections;
using System.Collections.Generic;

public class SoulFishInputTube : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SplineContainer localSpline;
    [SerializeField] private LevelWaveModifierControllerTypeB targetModifier;
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

    private void Awake()
    {
        if (localSpline == null) localSpline = GetComponent<SplineContainer>();
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

    private void InitializeSystem()
    {
        if (targetModifier == null || targetModifier.pipeConnector == null || localSpline == null || isInitialized) return;

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

        // 3. Generate joining knots from the exit point to pipeConnector
        Vector3 lastKnotWorld = localSpline.transform.TransformPoint(
            (Vector3)localSpline.Spline[localSpline.Spline.Count - 1].Position);
        Vector3 connectorWorld = targetModifier.pipeConnector.position;

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

        // Tracking transform registered with FishingController — drives _HooverFishPoints via its LateUpdate
        GameObject tracker = new GameObject("TubeDeliveryTracker");
        tracker.transform.SetParent(transform);
        if (fishingController != null)
            fishingController.RegisterTubeDelivery(tracker.transform);

        GameObject fish = null;
        if (fishPrefab != null)
            fish = Instantiate(fishPrefab, transform);

        float distance = 0f;
        float totalLength = combinedSpline.CalculateLength();

        while (distance < totalLength)
        {
            distance += travelSpeed * Time.deltaTime;
            float t = Mathf.Clamp01(distance / totalLength);

            Vector3 worldPos    = combinedSpline.transform.TransformPoint((Vector3)combinedSpline.EvaluatePosition(t));
            Vector3 worldTangent = combinedSpline.transform.TransformDirection((Vector3)combinedSpline.EvaluateTangent(t));

            tracker.transform.position = worldPos;

            if (fish != null)
            {
                fish.transform.position = worldPos;
                if (worldTangent != Vector3.zero)
                    fish.transform.rotation = Quaternion.LookRotation(worldTangent);
            }

            yield return null;
        }

        if (fishingController != null)
            fishingController.UnregisterTubeDelivery(tracker.transform);
        Destroy(tracker);

        if (fish != null)
            Destroy(fish);

        if (targetModifier != null)
            targetModifier.OnSoulArrived(identity);

        IsBusy = false;
    }
}
