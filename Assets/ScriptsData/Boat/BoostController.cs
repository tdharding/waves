using UnityEngine;
using UnityEngine.VFX;

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

    [Header("Boost Stamina")]
    [Tooltip("Maximum continuous boost time before it runs out.")]
    public float maxBoostDuration = 2f;
    [Tooltip("Time for a fully drained boost meter to recharge to full.")]
    public float boostCooldown = 2f;

    [Header("Launch Trigger")]
    [Range(0f, 1f)]
    [Tooltip("How close to the wave peak counts as 'on the crest'. 1 = only the exact peak, 0.85 = a narrow band near the peak, lower = wider band. Direction-independent.")]
    public float crestBandThreshold = 0.85f;
    [Tooltip("How long Space must be held continuously before a crest launch can fire.")]
    public float MinimumBoostTimeToLaunch = 0.5f;
    [Tooltip("Minimum time between launches.")]
    public float launchCooldown = 1f;

    [Header("Launch Impulse")]
    [Tooltip("Multiplier on the boat's current speed, applied along the ramp direction at launch.")]
    public float launchForwardBoost = 1.3f;
    [Range(0f, 1f)]
    [Tooltip("Guarantees a minimum upward component so a shallow ramp still leaves the water.")]
    public float minLaunchUp = 0.1f;

    [Header("Airborne Physics")]
    public float airGravityMultiplier = 1.5f;
    [Range(0f, 1f)]
    public float airControlFactor = 0.5f;
    [Tooltip("Safety cap so the boat can't stay airborne indefinitely.")]
    public float maxAirTime = 2.5f;
    [Range(0f, 1f)]
    [Tooltip("Fraction of forward speed lost on landing. 0 = keep all speed, 0.3 = lose 30%.")]
    public float landingSpeedReduction = 0f;

    [Header("Boost VFX")]
    [Tooltip("Exhaust VFX Graph effects. Assign the VisualEffect components on your two booster exhausts. They play while boosting and stop when boost ends/cools down.")]
    public VisualEffect[] boostExhausts;

    [Header("Debug")]
    public bool debugLogging = true;
    [Tooltip("Draw the wave surface around the boat in the Scene view, colored by climb detection.")]
    public bool debugGizmos = true;
    public float gizmoGridExtent = 12f;
    public float gizmoGridStep = 1f;

    private Transform waterTransform;
    private Material waterMaterial;
    private float lastLaunchTime = -999f;
    private bool active;
    private bool wasBoosting;
    private float boostHeldTime;

    // Ramp anchor tracking: the most recent trough (descent->ascent inflection) during the boost.
    private Vector3 troughPos;
    private float troughHeight;
    private Vector3 prevPos;
    private float prevWaveH;
    private bool wasDescending;
    private float lastBlockLogTime = -999f;

    // Boost stamina
    private float boostCharge;
    private bool boostExhausted;

    void Start()
    {
        boostCharge = maxBoostDuration;
        waterTransform = LevelDataController.Instance?.GetWaveTransform();
        if (waterTransform != null)
        {
            var rend = waterTransform.GetComponent<Renderer>();
            if (rend != null) waterMaterial = rend.sharedMaterial;
        }

        SetBoostVFX(false); // start idle — the exhausts only play while boosting
    }

    public void SetActive(bool value)
    {
        active = value;
        if (debugLogging)
            Debug.Log($"[BoostController] Tool {(value ? "equipped" : "unequipped")}");

        if (!active && boatMovement != null)
        {
            boatMovement.SetBoosting(false);
            SetBoostVFX(false);
            wasBoosting = false;
        }
    }

    // Play/stop the exhaust VFX with the boost state.
    private void SetBoostVFX(bool on)
    {
        if (boostExhausts == null) return;
        foreach (var vfx in boostExhausts)
        {
            if (vfx == null) continue;
            if (on)
                vfx.Play();   // sends OnPlay → spawn starts
            else
                vfx.Stop();   // sends OnStop → spawn stops, existing particles finish naturally
        }
    }

    // Called every frame from BoatControlRouter while the Boost tool is equipped.
    public void Tick(bool spaceHeld)
    {
        if (!active || boatMovement == null) return;

        // Boost stamina: drain while boosting, recharge otherwise. When drained it locks out
        // (boostExhausted) until the meter fully refills — that's the boost cooldown.
        bool effectiveBoost = spaceHeld && !boostExhausted && boostCharge > 0f;
        if (effectiveBoost)
        {
            boostCharge -= Time.deltaTime;
            if (boostCharge <= 0f) { boostCharge = 0f; boostExhausted = true; }
        }
        else
        {
            boostCharge += Time.deltaTime * (maxBoostDuration / Mathf.Max(0.01f, boostCooldown));
            if (boostCharge >= maxBoostDuration) { boostCharge = maxBoostDuration; boostExhausted = false; }
        }

        bool risingEdge = effectiveBoost && !wasBoosting;
        if (effectiveBoost != wasBoosting)
        {
            SetBoostVFX(effectiveBoost);
            if (debugLogging)
                Debug.Log($"[BoostController] Boosting {(effectiveBoost ? "started" : "stopped")} (charge={boostCharge:F2}s)");
        }
        wasBoosting = effectiveBoost;

        boatMovement.SetBoosting(effectiveBoost, boostedMoveSpeed, boostAcceleration, boostDeceleration);

        // Track how long boost has been held continuously.
        boostHeldTime = effectiveBoost ? boostHeldTime + Time.deltaTime : 0f;

        if (boatMovement.IsAirborne) return;   // airborne guard: launch continues until Land()
        if (!effectiveBoost) return;
        if (waterTransform == null || waterMaterial == null) return;

        var p = WaveUtils.ReadParams(waterTransform, waterMaterial);
        Vector3 pos = boatMovement.transform.position;
        // Wave height, not transform Y — the boat's transform is pinned flat, so the real
        // climb lives in the wave field. This is what gives the ramp its vertical angle.
        float waveH = WaveUtils.SampleWaveSmooth(pos, p);

        // Update the ramp anchor: the most recent trough (where descent turns to ascent).
        if (risingEdge)
        {
            troughPos = pos; troughHeight = waveH;
            prevPos = pos;   prevWaveH = waveH;
            wasDescending = false;
        }
        else
        {
            if (waveH < prevWaveH)
                wasDescending = true;
            else if (waveH > prevWaveH && wasDescending)
            {
                troughPos = prevPos; troughHeight = prevWaveH;   // the inflection point = trough
                wasDescending = false;
            }
            prevPos = pos; prevWaveH = waveH;
        }

        // Direction-independent crest detection: are we near the top of a wave?
        float crest = CrestValue(pos, p);
        if (crest < crestBandThreshold) return;

        // On the crest holding boost — remaining gates (logged so we can see which one blocks).
        if (boostHeldTime < MinimumBoostTimeToLaunch)
        {
            if (debugLogging && Time.time - lastBlockLogTime > 0.25f)
            {
                Debug.Log($"[BoostController] On crest but BLOCKED: boostHeld {boostHeldTime:F2}s < MinimumBoostTimeToLaunch {MinimumBoostTimeToLaunch:F2}s");
                lastBlockLogTime = Time.time;
            }
            return;
        }
        if (Time.time - lastLaunchTime < launchCooldown)
        {
            if (debugLogging && Time.time - lastBlockLogTime > 0.25f)
            {
                Debug.Log($"[BoostController] On crest but BLOCKED: cooldown, {launchCooldown - (Time.time - lastLaunchTime):F2}s left");
                lastBlockLogTime = Time.time;
            }
            return;
        }

        Vector3 launchVelocity = BuildLaunchVelocity(pos, waveH, out Vector3 ramp, out Vector3 launchDir);

        if (debugLogging)
            Debug.Log($"[BoostController] LAUNCH triggered — crest={crest:F3}, boostHeld={boostHeldTime:F2}s, " +
                       $"speed={boatMovement.CurrentSpeed:F2}, ramp={ramp}, launchDir={launchDir}, vel={launchVelocity}");

        boatMovement.LaunchBoat(launchVelocity, airGravityMultiplier, airControlFactor, maxAirTime, landingSpeedReduction);
        lastLaunchTime = Time.time;
        boostHeldTime = 0f;
    }

    // Builds the launch velocity from the ramp the boat climbed (trough -> current pos/crest).
    // Horizontal from XZ travel, vertical from wave-height gain, magnitude from current speed.
    private Vector3 BuildLaunchVelocity(Vector3 pos, float waveH, out Vector3 ramp, out Vector3 launchDir)
    {
        ramp = new Vector3(pos.x - troughPos.x, waveH - troughHeight, pos.z - troughPos.z);
        Vector3 horiz = new Vector3(ramp.x, 0f, ramp.z);
        if (horiz.sqrMagnitude < 0.0001f)
            horiz = new Vector3(boatMovement.transform.forward.x, 0f, boatMovement.transform.forward.z);
        horiz.Normalize();

        launchDir = new Vector3(horiz.x, Mathf.Max(ramp.y, 0f), horiz.z);
        launchDir.Normalize();
        if (launchDir.y < minLaunchUp)   // guarantee the boat actually leaves the water
        {
            launchDir.y = minLaunchUp;
            launchDir.Normalize();
        }
        return launchDir * (boatMovement.CurrentSpeed * launchForwardBoost);
    }

    // Normalized wave height at a position: -1 at trough, +1 at crest. Direction-independent.
    private float CrestValue(Vector3 worldPos, WaveUtils.WaveParams p)
    {
        float maxAmp = Mathf.Max(1e-4f, p.ripple * p.meshScale);
        return WaveUtils.SampleWaveSmooth(worldPos, p) / maxAmp;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!debugGizmos || !Application.isPlaying) return;
        if (boatMovement == null || waterTransform == null || waterMaterial == null) return;

        var p = WaveUtils.ReadParams(waterTransform, waterMaterial);
        Vector3 boatPos = boatMovement.transform.position;
        float baseY = waterTransform.position.y;

        // Wave surface grid around the boat. Each segment is colored by crest proximity
        // at its midpoint (direction-independent): green = on the crest band (launch zone),
        // red = anywhere else.
        int steps = Mathf.Max(2, Mathf.RoundToInt(gizmoGridExtent * 2f / gizmoGridStep));
        for (int i = 0; i <= steps; i++)
        {
            float ox = -gizmoGridExtent + i * gizmoGridStep;
            Vector3 prev = Vector3.zero;
            bool hasPrev = false;
            for (int j = 0; j <= steps; j++)
            {
                float oz = -gizmoGridExtent + j * gizmoGridStep;
                Vector3 wp = new Vector3(boatPos.x + ox, 0f, boatPos.z + oz);
                wp.y = baseY + WaveUtils.SampleHeightSmooth(wp, p);

                if (hasPrev)
                {
                    Vector3 mid = (prev + wp) * 0.5f;
                    float crestMid = CrestValue(mid, p);
                    Gizmos.color = crestMid >= crestBandThreshold ? Color.green : new Color(1f, 0.25f, 0.25f);
                    Gizmos.DrawLine(prev, wp);
                }
                prev = wp;
                hasPrev = true;
            }
        }

        float crest = CrestValue(boatPos, p);
        Vector3 surface = new Vector3(boatPos.x, baseY + WaveUtils.SampleHeightSmooth(boatPos, p), boatPos.z);

        // Ramp line (magenta): from the tracked trough up to the boat — the launch angle being built.
        // Trajectory (cyan): the predicted launch arc if you hit a crest right now.
        if (wasBoosting)
        {
            Vector3 troughSurface = new Vector3(troughPos.x, baseY + troughHeight, troughPos.z);
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(troughSurface, 0.2f);
            Gizmos.DrawLine(troughSurface, surface);

            float waveHNow = WaveUtils.SampleWaveSmooth(boatPos, p);
            Vector3 vel = BuildLaunchVelocity(boatPos, waveHNow, out _, out _);
            Vector3 g = Physics.gravity * airGravityMultiplier;
            Vector3 simPos = surface;
            Gizmos.color = Color.cyan;
            const float dt = 0.05f;
            for (int s = 0; s < 80; s++)
            {
                Vector3 next = simPos + vel * dt;
                vel += g * dt;
                Gizmos.DrawLine(simPos, next);
                simPos = next;
                float surfY = baseY + WaveUtils.SampleHeightSmooth(next, p);
                if (vel.y < 0f && next.y <= surfY) break;   // predicted landing point
            }
            Gizmos.DrawWireSphere(simPos, 0.3f);
        }

        // Boat state marker: green sphere above boat when currently on the crest band.
        bool onCrest = crest >= crestBandThreshold;
        Gizmos.color = onCrest ? Color.green : Color.gray;
        Gizmos.DrawSphere(surface + Vector3.up * 3f, 0.35f);

        UnityEditor.Handles.color = boostExhausted ? Color.red : Color.white;
        UnityEditor.Handles.Label(surface + Vector3.up * 3.6f,
            $"crest={crest:F2}  boostHeld={boostHeldTime:F2}s  charge={boostCharge:F2}/{maxBoostDuration:F1}s{(boostExhausted ? "  [COOLDOWN]" : "")}");
    }
#endif
}
