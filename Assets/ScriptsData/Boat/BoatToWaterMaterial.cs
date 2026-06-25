using UnityEngine;

public class BoatToWaterMaterial : MonoBehaviour
{
    [Header("References")]
    public Material waterMaterial;
    public Transform boat;
    public BoatVisual boatVisual;
    public BoatMovement boatMovement;
    public Transform waterTransform;

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

    [Header("Arena Boat Mask (Distance-Based)")]
    [Tooltip("Fallbacks if no BaselineMarker is found in the scene.")]
    public float fallbackFadeStart = 10f;
    public float fallbackFadeEnd   = 18f;

    [Header("Boat Wake")]
    public float wakeRadius      = 1.5f;
    public float wakeLength      = 1.0f;
    public float ringFrequency   = 8f;
    public float ringScrollSpeed = 3f;
    public float ringFalloff     = 1.5f;
    public float ringAmplitude   = 0.05f;
    public float idleAmplitude   = 0.01f;

    [Header("Deprecated Axis (No longer used for strength)")]
    public Vector3 maskAxis = Vector3.forward;
    public float maskFrontOffset = 0f;
    public float maskBackOffset  = 0f;

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

        float speed01 = boatMovement != null ? boatMovement.Speed01 : 0f;
        if (waterTransform != null)
        {
            Vector3 localBoatPos = waterTransform.InverseTransformPoint(boat.position);
            waterMaterial.SetVector("_BoatCenter", new Vector4(localBoatPos.x, 0f, -localBoatPos.y, 0f));

            // Convert boat forward direction to wave mesh object space (direction only, no position offset)
            Vector3 localForward = waterTransform.InverseTransformDirection(boat.forward);
            waterMaterial.SetVector("_BoatForwardLocal", new Vector4(localForward.x, -localForward.y, 0f, 0f));
        }
        waterMaterial.SetFloat("_BoatSpeed01",      speed01);
        waterMaterial.SetFloat("_WakeRadius",       wakeRadius);
        waterMaterial.SetFloat("_WakeLength",       wakeLength);
        waterMaterial.SetFloat("_RingFrequency",    ringFrequency);
        waterMaterial.SetFloat("_RingScrollSpeed",  ringScrollSpeed);
        waterMaterial.SetFloat("_RingFalloff",      ringFalloff);
        waterMaterial.SetFloat("_RingAmplitude",    ringAmplitude);
        waterMaterial.SetFloat("_IdleAmplitude",    idleAmplitude);

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

        // Try to get thresholds from the BaselineMarker
        float startDist = fallbackFadeStart;
        float endDist   = fallbackFadeEnd;

        var spawner = Object.FindFirstObjectByType<LevelSpawner>();
        var marker  = spawner != null ? spawner.GetBaselineMarker() : null;

        if (marker != null)
        {
            startDist = marker.MaskFadeStartRadius;
            endDist   = marker.MaskFadeEndRadius;
        }

        Vector3 centre = ldc.GetArenaCentre();
        // Calculate the boat's distance from the arena center (on the horizontal plane)
        Vector3 boatPosXZ = new Vector3(boat.position.x, 0, boat.position.z);
        Vector3 centreXZ = new Vector3(centre.x, 0, centre.z);
        float boatDist = Vector3.Distance(boatPosXZ, centreXZ);

        // Strength increases as the boat moves away from the center towards the walls.
        float t = Mathf.InverseLerp(startDist, endDist, boatDist);
        float strength = Mathf.SmoothStep(0f, 1f, t);

        Shader.SetGlobalFloat("_ArenaBoatMaskStrength", strength);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var ldc = LevelDataController.Instance;
        Vector3 centre = ldc != null ? ldc.GetArenaCentre() : transform.position;

        var spawner = Object.FindFirstObjectByType<LevelSpawner>();
        var marker  = spawner != null ? spawner.GetBaselineMarker() : null;

        float startDist = marker != null ? marker.MaskFadeStartRadius : fallbackFadeStart;
        float endDist   = marker != null ? marker.MaskFadeEndRadius   : fallbackFadeEnd;
        
        // Draw the concentric fade rings
        UnityEditor.Handles.color = new Color(1f, 0f, 1f, 0.4f); // Magenta = Start
        UnityEditor.Handles.DrawWireDisc(centre, Vector3.up, startDist);
        UnityEditor.Handles.Label(centre + Vector3.forward * startDist, "Mask Fade Start");

        UnityEditor.Handles.color = new Color(0f, 1f, 1f, 0.4f); // Cyan = End
        UnityEditor.Handles.DrawWireDisc(centre, Vector3.up, endDist);
        UnityEditor.Handles.Label(centre + Vector3.forward * endDist, "Mask Full Strength");

        if (boat != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(centre, new Vector3(boat.position.x, centre.y, boat.position.z));
        }
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
