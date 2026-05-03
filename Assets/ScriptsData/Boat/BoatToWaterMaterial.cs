using UnityEngine;

public class BoatToWaterMaterial : MonoBehaviour
{
    [Header("References")]
    public Material waterMaterial;
    public Transform boat;
    public BoatVisual boatVisual;
    public BoatMovement boatMovement;

    [Header("Sonar Screen Space")]
public Material rockSonarMaterial;
public string sonarCenterProperty = "_SonarCenter";
public Camera mainCamera;

    [Header("Offset")]
    [Tooltip("Forward offset of foam origin")]
    public float forwardOffset = 0.5f;

    [Header("Shader Property Names")]
    public string boatPositionProperty = "_BoatPosition";
    public string boatForwardProperty  = "_BoatForward";
    public string boatRightProperty    = "_BoatRight";

    public string foamSizeProperty     = "_BoatFoamSize";
    public string trailWidthProperty   = "_BoatTrailWidth";
    public string rippleFreqProperty   = "_BoatRippleFrequency";
    public string rippleSpeedProperty  = "_BoatRippleSpeed";

    [Header("Foam Size")]
    public float foamSizeZeroSpeed = 0.05f;
    public float foamSizeLowSpeed  = 0.15f;
    public float foamSizeMaxSpeed  = 0.30f;

    [Header("Other Water Values")]
    public float trailWidthLowSpeed  = 40f;
    public float trailWidthMaxSpeed  = 30f;

    public float rippleFreqLowSpeed  = 10f;
    public float rippleFreqMaxSpeed  = 30f;

    public float rippleSpeedLowSpeed = 0.2f;
    public float rippleSpeedMaxSpeed = 0.8f;

    [Header("Zero Speed Detection")]
    [Tooltip("Below this normalized speed, the boat is treated as stationary")]
    public float stationaryThreshold = 0.02f;

    [Header("Arena Mask Zoom")]
    public BoatCameraZoom cameraZoom;
    public Material arenaMaskMaterial;
    [Range(-1f, 2f)] public float innerRadiusZoomedIn  =  1.00f;
    [Range(-1f, 2f)] public float innerRadiusZoomedOut = -0.27f;
    [Range(-1f, 2f)] public float outerRadiusZoomedIn  =  1.30f;
    [Range(-1f, 2f)] public float outerRadiusZoomedOut =  0.03f;

    [Header("Arena Boat Mask")]
    [Tooltip("Direction from front (camera-near) to back (camera-far) of the arena.")]
    public Vector3 maskAxis = Vector3.forward;
    [Tooltip("Offset of the front point along the axis from arena centre (negative = closer to camera).")]
    public float maskFrontOffset = 0f;
    [Tooltip("Offset of the back point along the axis from arena centre (positive = further from camera).")]
    public float maskBackOffset  = 0f;
    [Tooltip("Point along the axis where the mask is at FULL strength (boat in front of this = fully masked).")]
    public float maskFadeStartOffset = 0f;
    [Tooltip("Point along the axis where the mask fades to ZERO (boat behind this = no mask).")]
    public float maskFadeEndOffset   = 10f;

    void Start()
{
    if (mainCamera == null)
        mainCamera = Camera.main;
}

    void Update()
    {
        if (waterMaterial == null || boat == null)
            return;

        // ----------------------------------
        // Spatial data
        // ----------------------------------
        Vector3 foamOrigin =
            boat.position + boat.forward * forwardOffset;

            if (rockSonarMaterial != null && mainCamera != null)
            {
                Vector3 viewportPos =
                    mainCamera.WorldToViewportPoint(boat.position);

                Vector2 screenUV =
                    new Vector2(viewportPos.x, viewportPos.y);

                rockSonarMaterial.SetVector(sonarCenterProperty, screenUV);
            }

        waterMaterial.SetVector(boatPositionProperty, foamOrigin);
        Vector3 visualPos = boatVisual != null ? boatVisual.transform.position : boat.position;
        Shader.SetGlobalVector(boatPositionProperty, visualPos);

        if (mainCamera != null)
        {
            Vector3 vp = mainCamera.WorldToViewportPoint(visualPos);
            Shader.SetGlobalVector("_BoatScreenCenter", new Vector4(vp.x, vp.y, 0f, 0f));
        }

        UpdateArenaMaskStrength();
        UpdateArenaMaskRadii();
        waterMaterial.SetVector(boatForwardProperty, boat.forward);
        waterMaterial.SetVector(boatRightProperty, boat.right);

        bool boosting = Input.GetKey(KeyCode.Space);

        bool stationary =
            boatMovement != null &&
            boatMovement.Speed01 < stationaryThreshold;

        // ----------------------------------
        // FOAM (three discrete states)
        // ----------------------------------
        if (stationary)
        {
            waterMaterial.SetFloat(
                foamSizeProperty,
                foamSizeZeroSpeed
            );
        }
        else if (boosting)
        {
            waterMaterial.SetFloat(
                foamSizeProperty,
                foamSizeMaxSpeed
            );
        }
        else
        {
            waterMaterial.SetFloat(
                foamSizeProperty,
                foamSizeLowSpeed
            );
        }

        // ----------------------------------
        // OTHER VALUES (binary boost switch)
        // ----------------------------------
        if (boosting)
        {
            ApplyMaxSpeedValues();
        }
        else
        {
            ApplyLowSpeedValues();
        }
    }

    void UpdateArenaMaskRadii()
    {
        if (cameraZoom == null || cameraZoom.cam == null || arenaMaskMaterial == null) return;

        float fov = cameraZoom.cam.Lens.FieldOfView;
        float t   = Mathf.InverseLerp(cameraZoom.minFOV, cameraZoom.maxFOV, fov); // 0 = zoomed in, 1 = zoomed out

        arenaMaskMaterial.SetFloat("_ArenaBoatMaskRadius",      Mathf.Lerp(innerRadiusZoomedIn,  innerRadiusZoomedOut, t));
        arenaMaskMaterial.SetFloat("_ArenaBoatMaskOuterRadius", Mathf.Lerp(outerRadiusZoomedIn,  outerRadiusZoomedOut, t));
    }

    void UpdateArenaMaskStrength()
    {
        if (boat == null) return;

        var ldc = LevelDataController.Instance;
        if (ldc == null) return;

        float radius = ldc.GetArenaProfile()?.droppedSoulBoundsRadius ?? 20f;
        Vector3 centre = ldc.GetArenaCentre();
        Vector3 axisDir = maskAxis.normalized;

        Vector3 fadeStartPt = centre + axisDir * maskFadeStartOffset;
        Vector3 fadeEndPt   = centre + axisDir * maskFadeEndOffset;

        Vector3 fadeAxis   = fadeEndPt - fadeStartPt;
        float   fadeLength = fadeAxis.magnitude;
        if (fadeLength < 0.001f) return;

        float t = Mathf.Clamp01(Vector3.Dot(boat.position - fadeStartPt, fadeAxis / fadeLength) / fadeLength);
        float strength = 1f - Mathf.SmoothStep(0f, 1f, t);

        Shader.SetGlobalFloat("_ArenaBoatMaskStrength", strength);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var ldc = LevelDataController.Instance;
        float radius  = ldc != null ? (ldc.GetArenaProfile()?.droppedSoulBoundsRadius ?? 20f) : 20f;
        Vector3 centre = ldc != null ? ldc.GetArenaCentre() : transform.position;
        Vector3 axisDir = maskAxis.normalized;

        Vector3 frontPt = centre + axisDir * (-radius + maskFrontOffset);
        Vector3 backPt  = centre + axisDir * ( radius + maskBackOffset);

        // Front dot (green = full mask)
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(frontPt, 0.4f);

        // Back dot (red = no mask)
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(backPt, 0.4f);

        // Axis line
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(frontPt, backPt);

        // Midpoint dividing line (perpendicular to axis, horizontal)
        Vector3 mid  = (frontPt + backPt) * 0.5f;
        Vector3 perp = Vector3.Cross(axisDir, Vector3.up).normalized * radius;
        Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
        Gizmos.DrawLine(mid - perp, mid + perp);

        // Fade zone — cyan = full strength, magenta = zero strength
        Vector3 fadeStartPt = centre + axisDir * maskFadeStartOffset;
        Vector3 fadeEndPt   = centre + axisDir * maskFadeEndOffset;

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(fadeStartPt, 0.4f);
        Gizmos.DrawLine(fadeStartPt - perp, fadeStartPt + perp);

        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(fadeEndPt, 0.4f);
        Gizmos.DrawLine(fadeEndPt - perp, fadeEndPt + perp);
    }
#endif

    void ApplyLowSpeedValues()
    {
        waterMaterial.SetFloat(trailWidthProperty, trailWidthLowSpeed);
        waterMaterial.SetFloat(rippleFreqProperty, rippleFreqLowSpeed);
        waterMaterial.SetFloat(rippleSpeedProperty, rippleSpeedLowSpeed);
    }

    void ApplyMaxSpeedValues()
    {
        waterMaterial.SetFloat(trailWidthProperty, trailWidthMaxSpeed);
        waterMaterial.SetFloat(rippleFreqProperty, rippleFreqMaxSpeed);
        waterMaterial.SetFloat(rippleSpeedProperty, rippleSpeedMaxSpeed);
    }
}
