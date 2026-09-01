using UnityEngine;

// Feeds BoatWakeBands.hlsl, and the boat's disturbance term in WaveBands.hlsl. Bare $Globals, so
// every value is re-pushed each frame through BOTH Material.Set* and Shader.SetGlobal* — a shader
// reimport wipes the globals, and re-pushing is what makes that self-heal on the next frame. Same
// reasoning as WaveMaterialController's SetGlobalsBackedFloat for the wave bands and rock rings.
//
// The fields are grouped on purpose:
//   MASK SHAPE          — where the boat's own wake is; the silhouette it occupies.
//   WAVE PATTERN        — what is drawn inside that silhouette.
//   WAVE BAND DISRUPTION— how the boat roughs up the map-wide WaveBands lines, as a rock does.
// Tuning one group never changes another, so the shape can be settled before the lines are dressed.
//
// Deliberately NOT routed through WaveState/presets: these follow the boat, not the level.
public class BoatWakeBandsController : MonoBehaviour
{
    [Header("References")]
    public Material waterMaterial;
    public Transform boat;

    [Tooltip("Read for Speed01, which drives the min/max pairs below. Without it the wake sits at its minimum.")]
    public BoatMovement boatMovement;

    // ─────────────────────────────────────────────────────────────────────────
    [Header("Mask Shape")]

    [Tooltip("Slides the cap's front edge along the boat's forward axis. Positive is toward the bow.")]
    public float capOffset = 0.2f;

    [Tooltip("How wide the flat front of the cap is. Wide reads as a shallow curve hugging the hull; narrow tightens toward a semicircle; 0 is a single point.")]
    public float capWidth = 0.3f;

    [Tooltip("Holds the innermost line clear of the hull.")]
    public float beam = 0.1f;

    [Tooltip("The span the family fills, measured outward from the beam.")]
    public float wakeWidth = 0.4f;

    [Tooltip("How much wider the family gets per unit travelled back.")]
    public float flare = 0.26f;

    [Tooltip("How far behind the boat the wake reaches when barely moving.")]
    public float minLength = 0.8f;

    [Tooltip("How far behind the boat the wake reaches at full speed.")]
    public float maxLength = 3f;

    [Tooltip("How far AHEAD of the cap the family persists before fading. Must be positive — a negative value collapses the fade range and cuts the cap off entirely.")]
    public float noseFade = 0.3f;

    [Tooltip("Where along the length the family starts fading, as a fraction of it.")]
    [Range(0f, 1f)] public float tailFade = 0.45f;

    // ─────────────────────────────────────────────────────────────────────────
    [Header("Wave Pattern")]

    [Tooltip("Peak whiteness of a line. 0 removes the effect entirely.")]
    public float strength = 0.43f;

    [Tooltip("How many lines fill the family.")]
    [Range(1f, 8f)] public float count = 3f;

    [Tooltip("Fraction of its cycle a line fills.")]
    [Range(0.01f, 0.98f)] public float lineWidth = 0.35f;

    [Tooltip("0 = hard-edged line; 1 = falls away from its centre.")]
    [Range(0f, 1f)] public float softness = 0.6f;

    [Tooltip("How far the lines are pushed off true, in band-widths.")]
    public float distortStrength = 1f;

    [Tooltip("How fine the distortion field is. Wants to be several times finer than the wake is long, or the whole wake sits inside one noise cell and it becomes a constant sideways shift instead of a wobble.")]
    public float distortScale = 8f;

    [Tooltip("How fast the family drifts outward from the hull when barely moving.")]
    public float minPhaseSpeed = 0.05f;

    [Tooltip("How fast the family drifts outward from the hull at full speed.")]
    public float maxPhaseSpeed = 0.2f;

    // ─────────────────────────────────────────────────────────────────────────
    [Header("Turning")]

    [Tooltip("How far the wake bends into the arc the boat is turning through. 0 leaves it straight behind. Negative flips the direction of the bend.")]
    public float turnStrength = 1f;

    [Tooltip("How quickly the bend follows the boat. Low is smooth and laggy, high snaps to the current turn. Purely smoothing — it does not change how hard the wake bends.")]
    [Range(0.01f, 20f)] public float turnResponse = 4f;

    [Tooltip("Upper limit on the bend, in radians per world unit behind, applied before Turn Strength. Stops a spin-on-the-spot or a single bad frame from coiling the wake up.")]
    public float turnClamp = 1.5f;

    // ─────────────────────────────────────────────────────────────────────────
    // How the boat roughs up the map-wide WaveBands lines, the same way a rock does.
    [Header("Wave Band Disruption")]

    [Tooltip("How much the WaveBands lines wobble at the boat, in band-widths. 0 skips it entirely.")]
    public float waveBandDistort = 0.5f;

    [Tooltip("How far that disturbance reaches, in world units.")]
    public float waveBandReach = 2f;

    [Tooltip("0 = the wobble eases away invisibly; 1 = it holds, then stops on a visible crease.")]
    [Range(0f, 1f)] public float waveBandBevel = 0f;

    [Tooltip("How fine the boat's disturbance is. Entirely the boat's own — independent of the level's wave band meander scale.")]
    public float waveBandScale = 3f;

    float _phase;

    // Turn measurement. Curvature is signed radians per world unit TRAVELLED, which is the arc the
    // boat is actually leaving — not its yaw rate. Dividing by distance rather than time is what
    // makes it independent of speed: the same steering at half speed leaves the same shaped arc,
    // and turning on the spot leaves no arc at all because no water was passed through.
    Vector3 _prevPos;
    Vector3 _prevFwd;
    bool    _hasPrev;
    float   _curvature;

    void LateUpdate()
    {
        if (waterMaterial == null || boat == null)
            return;

        UpdateCurvature();

        // Both min/max pairs are resolved here, from the boat speed the movement script already
        // works out. Lerping on the CPU keeps the shader unaware of speed entirely — it still gets
        // a single Length and a single accumulated phase.
        float speed01 = boatMovement != null ? Mathf.Clamp01(boatMovement.Speed01) : 0f;

        // Accumulated on the CPU rather than derived from Time * Speed, so changing the speed
        // mid-level slides the pattern instead of teleporting it.
        //
        // NOT wrapped. It used to wrap at 2pi because it indexed a band cycle, where one full cycle
        // was invisible to wrap across. It is now a distance scrolled along the wake, feeding a
        // noise field that does not repeat at any convenient interval — wrapping it would pop the
        // distortion. Accumulating unbounded is fine: at these speeds a long session reaches only a
        // few thousand, where float32 still resolves far finer than the field needs.
        _phase += Mathf.Lerp(minPhaseSpeed, maxPhaseSpeed, speed01) * Time.deltaTime;

        Vector3 p = boat.position;
        Vector3 f = boat.forward;
        SetVec("_BoatBandOrigin",   new Vector4(p.x, p.y, p.z, 0f));
        SetVec("_BoatBandForward",  new Vector4(f.x, f.y, f.z, 0f));

        // Mask shape
        Set("_BoatBandCapOffset",       capOffset);
        Set("_BoatBandCapWidth",        capWidth);
        Set("_BoatBandBeam",            beam);
        Set("_BoatBandWakeWidth",       wakeWidth);
        Set("_BoatBandFlare",           flare);
        Set("_BoatBandLength",          Mathf.Lerp(minLength, maxLength, speed01));
        Set("_BoatBandNoseFade",        noseFade);
        Set("_BoatBandTailFade",        tailFade);

        // Wave pattern
        Set("_BoatBandStrength",        strength);
        Set("_BoatBandCount",           count);
        Set("_BoatBandLineWidth",       lineWidth);
        Set("_BoatBandSoftness",        softness);
        Set("_BoatBandDistortStrength", distortStrength);
        Set("_BoatBandDistortScale",    distortScale);
        Set("_BoatBandTurn",            _curvature * turnStrength);
        Set("_BoatBandPhase",           _phase);

        // The boat's disturbance of the map-wide WaveBands family
        SetVec("_WaveBandBoatPos",      new Vector4(p.x, p.y, p.z, 0f));
        Set("_WaveBandBoatDistort",     waveBandDistort);
        Set("_WaveBandBoatReach",       waveBandReach);
        Set("_WaveBandBoatBevel",       waveBandBevel);
        Set("_WaveBandBoatScale",       waveBandScale);
    }

    void UpdateCurvature()
    {
        Vector3 pos = boat.position;
        Vector3 fwd = boat.forward;

        if (!_hasPrev)
        {
            _prevPos = pos;
            _prevFwd = fwd;
            _hasPrev = true;
            return;
        }

        // Flatten to the horizontal plane — pitch and roll on the swell are not turning.
        Vector2 f0 = new Vector2(_prevFwd.x, _prevFwd.z);
        Vector2 f1 = new Vector2(fwd.x, fwd.z);
        Vector2 d  = new Vector2(pos.x - _prevPos.x, pos.z - _prevPos.z);

        float moved = d.magnitude;

        if (f0.sqrMagnitude > 1e-8f && f1.sqrMagnitude > 1e-8f && moved > 1e-5f)
        {
            f0.Normalize();
            f1.Normalize();

            // Signed angle between the two headings. The cross term carries the direction, so a
            // left turn and a right turn come out opposite rather than both positive.
            float yaw = Mathf.Atan2(f0.x * f1.y - f0.y * f1.x, Vector2.Dot(f0, f1));

            float target = Mathf.Clamp(yaw / moved, -Mathf.Abs(turnClamp), Mathf.Abs(turnClamp));

            // Framerate-independent smoothing, so the bend does not follow every jitter of the
            // steering and does not behave differently at a different framerate.
            _curvature = Mathf.Lerp(_curvature, target,
                                    1f - Mathf.Exp(-turnResponse * Time.deltaTime));
        }
        else
        {
            // Stationary, or turning on the spot: no water passed through, so the arc decays away
            // rather than holding the last bend indefinitely.
            _curvature = Mathf.Lerp(_curvature, 0f,
                                    1f - Mathf.Exp(-turnResponse * Time.deltaTime));
        }

        _prevPos = pos;
        _prevFwd = fwd;
    }

    void Set(string name, float v)
    {
        waterMaterial.SetFloat(name, v);
        Shader.SetGlobalFloat(name, v);
    }

    void SetVec(string name, Vector4 v)
    {
        waterMaterial.SetVector(name, v);
        Shader.SetGlobalVector(name, v);
    }
}
