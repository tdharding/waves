using UnityEngine;
using UnityEngine.Splines;

[RequireComponent(typeof(SplineContainer))]
[RequireComponent(typeof(SplineExtrude))]
public class SnakeSplineWavePrototype : MonoBehaviour
{
    [Header("Follow Settings")]
    public Transform boat;
    public float followSpeed = 6f;
    public float spacing = 1.2f;

    [Header("Spline Settings")]
    public int pointCount = 20;

    [Header("Wave Settings")]
    public Transform waterTransform;
    public float extraYOffset = 0f;
    public float heightMultiplier = 1f;

    private Material waveMaterial;
    private int freqID, speedID, depthID;

    private SplineContainer splineContainer;
    private Spline spline;
    private SplineExtrude extrude;

    void Awake()
    {
        splineContainer = GetComponent<SplineContainer>();
        extrude = GetComponent<SplineExtrude>();
    }

    void Start()
    {
        if (waterTransform == null)
            return;

        var renderer = waterTransform.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            waveMaterial = renderer.sharedMaterial;
            freqID  = Shader.PropertyToID("_Frequency");
            speedID = Shader.PropertyToID("_Speed");
            depthID = Shader.PropertyToID("_RippleDepth");
        }

        spline = new Spline();
        splineContainer.Spline = spline;

        for (int i = 0; i < pointCount; i++)
        {
            Vector3 startPos = transform.position - transform.forward * spacing * i;
            Vector3 localPos = transform.InverseTransformPoint(startPos);
            spline.Add(new BezierKnot(localPos));
        }

        spline.Closed = false;

        extrude.enabled = true;
    }
void Update()
{
    if (waveMaterial == null || spline == null || boat == null)
        return;

    // -------- HEAD --------

    Vector3 headWorld = boat.position;

    // Keep XZ following with lerp
    BezierKnot headKnot = spline[0];
    Vector3 currentHeadWorld = transform.TransformPoint(headKnot.Position);

    Vector3 targetHeadWorld = new Vector3(
        headWorld.x,
        0f,
        headWorld.z
    );

    currentHeadWorld = Vector3.Lerp(
        new Vector3(currentHeadWorld.x, 0f, currentHeadWorld.z),
        targetHeadWorld,
        Time.deltaTime * followSpeed
    );

    // Apply wave height independently
    float headWaveY = GetWaveHeightAtPosition(currentHeadWorld);
    currentHeadWorld.y = headWaveY;

    headKnot.Position = transform.InverseTransformPoint(currentHeadWorld);
    spline[0] = headKnot;


    // -------- BODY --------

    for (int i = 1; i < pointCount; i++)
    {
        BezierKnot prevKnot = spline[i - 1];
        BezierKnot currentKnot = spline[i];

        Vector3 prevWorld = transform.TransformPoint(prevKnot.Position);
        Vector3 currentWorld = transform.TransformPoint(currentKnot.Position);

        // Horizontal direction only
        Vector3 prevXZ = new Vector3(prevWorld.x, 0f, prevWorld.z);
        Vector3 currentXZ = new Vector3(currentWorld.x, 0f, currentWorld.z);

        Vector3 dir = (currentXZ - prevXZ).normalized;
        Vector3 targetXZ = prevXZ + dir * spacing;

        Vector3 newXZ = Vector3.Lerp(
            currentXZ,
            targetXZ,
            Time.deltaTime * followSpeed
        );

        // Apply wave height separately
        float waveY = GetWaveHeightAtPosition(newXZ);
        Vector3 finalWorld = new Vector3(newXZ.x, waveY, newXZ.z);

        currentKnot.Position = transform.InverseTransformPoint(finalWorld);
        spline[i] = currentKnot;
    }
}
    float GetWaveHeightAtPosition(Vector3 worldPos)
    {
        Vector3 waveCenter = waterTransform.position;

        float frequency = waveMaterial.GetFloat(freqID);
        float waveSpeed = waveMaterial.GetFloat(speedID);
        float ripple = waveMaterial.GetFloat(depthID);

        float phase = -(Time.time * waveSpeed);
        float meshScale = waterTransform.localScale.x;

        float dist = Vector3.Distance(worldPos, waveCenter) / meshScale;

        float sine = Mathf.Sin(phase + dist * frequency);
        float amplitude = ripple * meshScale;

        float height = sine * amplitude * heightMultiplier + extraYOffset;

        return waterTransform.position.y + height;
    }
}