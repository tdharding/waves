using UnityEngine;

/// <summary>
/// Manages the boost tool on the boat.
/// Hold Space to boost. Boosting while climbing the face of a wave launches the boat into the air.
/// All boost/launch tuning lives here, not on BoatMovement, so the airborne mechanic is only
/// reachable through this tool.
/// </summary>
public class BoostController : MonoBehaviour
{
    [Header("Refs")]
    public BoatMovement boatMovement;

    [Header("Boost Speed")]
    [Tooltip("Move speed applied while holding Space with this tool equipped.")]
    public float boostedMoveSpeed = 10f;
    [Tooltip("How fast the boat accelerates toward boostedMoveSpeed. -1 = use BoatMovement's default acceleration.")]
    public float boostAcceleration = -1f;
    [Tooltip("How fast the boat decelerates back to base speed after releasing boost. -1 = use BoatMovement's default deceleration.")]
    public float boostDeceleration = -1f;

    [Header("Launch Trigger")]
    [Tooltip("How far apart the 3 normal samples are (world units). Larger = smoother, less noisy reading.")]
    public float normalSampleOffset = 1.5f;
    [Tooltip("Sine of the minimum surface angle in the boat's forward direction to count as climbing. ~0.1 = 6deg, ~0.2 = 12deg.")]
    public float launchSlopeThreshold = 0.12f;
    [Tooltip("Minimum current speed required before a launch can trigger.")]
    public float launchMinSpeed = 2f;
    [Tooltip("Minimum time between launches.")]
    public float launchCooldown = 1f;

    [Header("Launch Impulse")]
    public float launchUpForce = 6f;
    [Tooltip("Multiplier on boosted move speed applied as forward velocity at launch.")]
    public float launchForwardBoost = 1.3f;
    [Range(0f, 1f)]
    [Tooltip("How much the wave normal bends the launch direction vs. pure world-up.")]
    public float launchNormalInfluence = 0.5f;

    [Header("Airborne Physics")]
    public float airGravityMultiplier = 1.5f;
    [Range(0f, 1f)]
    public float airControlFactor = 0.5f;
    [Tooltip("Safety cap so the boat can't stay airborne indefinitely.")]
    public float maxAirTime = 2.5f;

    [Header("Debug")]
    public bool debugLogging = true;

    private Transform waterTransform;
    private Material waterMaterial;
    private float lastLaunchTime = -999f;
    private bool active;
    private bool wasBoosting;

    void Start()
    {
        waterTransform = LevelDataController.Instance?.GetWaveTransform();
        if (waterTransform != null)
        {
            var rend = waterTransform.GetComponent<Renderer>();
            if (rend != null) waterMaterial = rend.sharedMaterial;
        }
    }

    public void SetActive(bool value)
    {
        active = value;
        if (debugLogging)
            Debug.Log($"[BoostController] Tool {(value ? "equipped" : "unequipped")}");

        if (!active && boatMovement != null)
            boatMovement.SetBoosting(false);
    }

    // Called every frame from BoatControlRouter while the Boost tool is equipped.
    public void Tick(bool spaceHeld)
    {
        if (!active || boatMovement == null) return;

        if (debugLogging && spaceHeld != wasBoosting)
            Debug.Log($"[BoostController] Boosting {(spaceHeld ? "started" : "stopped")} (speed={boostedMoveSpeed})");
        wasBoosting = spaceHeld;

        boatMovement.SetBoosting(spaceHeld, boostedMoveSpeed, boostAcceleration, boostDeceleration);

        if (boatMovement.IsAirborne) return;
        if (!spaceHeld) return;
        if (Time.time - lastLaunchTime < launchCooldown) return;
        if (waterTransform == null || waterMaterial == null) return;
        if (boatMovement.CurrentSpeed < launchMinSpeed) return;

        var p = WaveUtils.ReadParams(waterTransform, waterMaterial);
        Vector3 pos = boatMovement.transform.position;
        Vector3 forward = boatMovement.transform.forward;

        // Use the wave normal sampled over a wider area for stable crest detection.
        // ascent = sin(slope angle in forward direction): positive = climbing, negative = descending.
        Vector3 normal = WaveUtils.GetNormal(pos, p, normalSampleOffset);
        float ascent = Vector3.Dot(normal, -forward);

        if (ascent < launchSlopeThreshold) return;

        Vector3 launchDir = Vector3.Slerp(Vector3.up, normal, launchNormalInfluence);

        // Scale off the boat's actual current speed, not the configured max — otherwise a boat
        // that's barely accelerated yet gets snapped straight to full boosted velocity.
        Vector3 launchVelocity = forward * (boatMovement.CurrentSpeed * launchForwardBoost)
                                  + launchDir * launchUpForce;

        if (debugLogging)
            Debug.Log($"[BoostController] LAUNCH triggered — ascent={ascent:F3}, speed={boatMovement.CurrentSpeed:F2}, " +
                       $"launchVelocity={launchVelocity}, normal={normal}");

        boatMovement.LaunchBoat(launchVelocity, airGravityMultiplier, airControlFactor, maxAirTime);
        lastLaunchTime = Time.time;
    }
}
