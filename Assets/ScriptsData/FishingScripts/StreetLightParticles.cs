using UnityEngine;

/// <summary>
/// The cloud of floating quads that drifts around a street light while it is lit.
///
/// The ParticleSystem does the work: this writes every module it needs from the fields below, then
/// starts and stops emission as the lamp lights. Nothing is placed by hand, so the particle
/// inspector's own Play button previews the cloud exactly as it will look in game — the only thing
/// left to set on the ParticleSystem is the Material on its Renderer.
///
/// Movement is three things at once: a slow counter-clockwise turn around the lamp, a wander vector
/// of each particle's own, and the Noise module curling them about. They
/// are born anywhere inside the inner sphere (particleRadius * fadeStart) and live exactly long
/// enough for that drift to carry them to particleRadius, so age stands in for distance out and the
/// colour ramp can fade them to nothing as they reach the edge.
///
/// StreetLightController turns this on and off with SetShowing as the lamp lights.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class StreetLightParticles : MonoBehaviour
{
    [Header("Cloud")]
    [Tooltip("Distance from the centre at which a particle has faded to nothing.")]
    [SerializeField] private float particleRadius = 4f;

    [Tooltip("Fraction of particleRadius the particles stay at full strength out to. Beyond it " +
             "they fade to zero at the radius, so the cloud has no visible edge.")]
    [Range(0f, 1f)]
    [SerializeField] private float fadeStart = 0.4f;

    [Tooltip("Fraction of a particle's life spent fading in, so new ones ease into the cloud rather " +
             "than appearing at full strength. 0 pops them in.")]
    [Range(0f, 0.9f)]
    [SerializeField] private float fadeIn = 0.3f;

    [Tooltip("How many quads are in the air around the lamp at once.")]
    [SerializeField] private int particleCount = 40;

    [Tooltip("Optional centre for the cloud (the bulb, usually). Falls back to this object.")]
    [SerializeField] private Transform centre;

    [Header("Particles")]
    [Tooltip("Material for the quads. Assigned to the ParticleSystem's Renderer, and handed the " +
             "centre and radius below so the shader can fade the cloud at its edge.")]
    [SerializeField] private Material particleMaterial;

    [SerializeField] private float particleSize = 0.25f;

    [Tooltip("Colour of a particle at full strength. Its alpha is scaled by the radius fade.")]
    [SerializeField] private Color particleColor = new Color(1f, 0.85f, 0.5f, 0.6f);

    [Tooltip("World units per second each particle drifts outward from the centre. Also sets how " +
             "hard the floaty wander pushes them about on the way.")]
    [SerializeField] private float driftSpeed = 0.25f;

    [Tooltip("Degrees per second the whole cloud turns around the lamp, counter-clockwise seen " +
             "from above. 0 leaves it still; a negative value turns it the other way.")]
    [SerializeField] private float orbitSpeed = 12f;

    [Tooltip("How many waves of the floaty wander span the radius. Around 1 gives long lazy curves " +
             "that still differ from particle to particle; higher gets busy and fidgety.")]
    [SerializeField] private float floatFrequency = 1.5f;

#if UNITY_EDITOR
    [Header("Debug")]
    [Tooltip("Draw the volume the particles live in: the outer sphere they fade to nothing at, and " +
             "when selected, the inner one the fade runs out from.")]
    [SerializeField] private bool showRadiusGizmo = true;
#endif

    private ParticleSystem ps;
    private bool isShowing;

    private Transform CentreTransform => centre != null ? centre : transform;

    // Born inside the inner sphere, dead at the outer one: crossing that gap at driftSpeed is what
    // makes a particle's age stand for how far out it has got.
    private float Lifetime =>
        Mathf.Max(particleRadius * (1f - fadeStart) / Mathf.Max(driftSpeed, 0.0001f), 0.05f);

    private void Awake() => EnsureInitialised();

    // Awake order between components is undefined and StreetLightController pushes its lit state
    // from its own Awake, so setup runs on first use rather than relying on ours running first.
    private void EnsureInitialised()
    {
        if (ps != null) return;
        ApplySettings();
    }

    private void ApplySettings()
    {
        ps = GetComponent<ParticleSystem>();

        var main = ps.main;
        main.loop            = true;
        main.playOnAwake     = false;   // SetShowing starts it when the lamp lights
        main.prewarm         = true;    // lighting a lamp gives a full cloud, not one that fills in
        main.startLifetime   = Lifetime;
        // A speed of its own per particle, so no two cross the cloud together.
        main.startSpeed      = new ParticleSystem.MinMaxCurve(driftSpeed * 0.5f, driftSpeed);
        main.startSize       = particleSize;
        main.startColor      = particleColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        // Headroom on the cap, so the count asked for is never clipped by emission timing.
        main.maxParticles    = Mathf.CeilToInt(particleCount * 1.1f);

        // Replacing the cloud once per lifetime is what holds particleCount of them in the air.
        var emission = ps.emission;
        emission.enabled     = true;
        emission.rateOverTime = particleCount / Mathf.Max(Lifetime, 0.0001f);

        // Born anywhere inside the inner sphere, so the cloud is a volume from the outset rather
        // than a spray coming off a point. The outward drift then carries them to the edge.
        var shape = ps.shape;
        shape.enabled        = true;
        shape.shapeType      = ParticleSystemShapeType.Sphere;
        shape.radius         = Mathf.Max(particleRadius * fadeStart, 0.01f);
        shape.radiusThickness = 1f;   // fill the volume, not just the shell

        // Where the cloud sits relative to this object, shared by the shape and the orbit below so
        // the quads circle the lamp rather than the object the component happens to be on.
        Vector3 centreOffset = centre != null ? transform.InverseTransformPoint(centre.position) : Vector3.zero;
        shape.position       = centreOffset;

        // A wander vector of its own per particle, drawn once at birth and held for its life. This
        // is what stops the cloud reading as one body of particles all going the same way.
        float wander = driftSpeed * 0.6f;
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space   = ParticleSystemSimulationSpace.World;
        velocity.x       = new ParticleSystem.MinMaxCurve(-wander, wander);
        velocity.y       = new ParticleSystem.MinMaxCurve(-wander, wander);
        velocity.z       = new ParticleSystem.MinMaxCurve(-wander, wander);

        // The slow turn around the lamp. Negated because a positive spin about Y reads clockwise
        // from above, and the offset moves the axis it turns about onto the lamp itself.
        velocity.orbitalY       = -orbitSpeed * Mathf.Deg2Rad;
        velocity.orbitalOffsetX = centreOffset.x;
        velocity.orbitalOffsetY = centreOffset.y;
        velocity.orbitalOffsetZ = centreOffset.z;

        // The floaty part: noise curls them about on the way out.
        //
        // Frequency is per world unit, so it has to be measured against the cloud or the whole thing
        // moves as a slab: at the default 0.3 one wave is over three units long, and every particle
        // in a cloud a metre across reads almost the same value off it and slides the same way.
        // Dividing by the radius makes the field fit the cloud — floatFrequency is then how many
        // waves span it, and neighbours genuinely differ.
        var noise = ps.noise;
        noise.enabled     = true;
        noise.strength    = driftSpeed;
        noise.frequency   = floatFrequency / Mathf.Max(particleRadius, 0.0001f);
        noise.scrollSpeed = driftSpeed * 0.5f;
        noise.damping     = true;

        // The radius fade, with the ramp in on the front of it: particles are born and die inside
        // the cloud, so without it each birth would appear at full strength. White colour keys
        // leave startColor alone.
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;

        // Gradient keys have to climb in time, so a low fadeStart cannot be allowed to land on or
        // before the ramp in, and a fade in of nothing still needs two keys to sit between.
        float rampIn = Mathf.Max(fadeIn, 0.0001f);
        float hold   = Mathf.Max(fadeStart, rampIn + 0.05f);

        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, rampIn),
                    new GradientAlphaKey(1f, hold),
                    new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        // Flat quads turned towards the camera. Facing points each one at the camera's position,
        // rather than View, which only lines them up with the camera plane — at the edges of a wide
        // shot that difference is the quad showing its side as the lamp passes off to one side.
        var psRenderer = GetComponent<ParticleSystemRenderer>();
        if (psRenderer != null)
        {
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            psRenderer.alignment  = ParticleSystemRenderSpace.Facing;
            if (particleMaterial != null) psRenderer.sharedMaterial = particleMaterial;
        }

        PushMaterialValues();
    }

    /// <summary>
    /// Hands the shader where the cloud is and how big it is, so it can fade the quads out at the
    /// edge. Sent through a property block rather than the material, so every lamp in the level can
    /// share one material and still fade around its own centre.
    /// </summary>
    private void PushMaterialValues()
    {
        var psRenderer = GetComponent<ParticleSystemRenderer>();
        if (psRenderer == null) return;

        _pushedCentre = CentreTransform.position;

        block ??= new MaterialPropertyBlock();
        psRenderer.GetPropertyBlock(block);
        block.SetVector(CloudCentreId,    _pushedCentre);
        block.SetFloat (CloudRadiusId,    particleRadius);
        block.SetFloat (CloudFadeStartId, fadeStart);
        psRenderer.SetPropertyBlock(block);
    }

    // The centre is a world position, and the level rig is moved after the lamps spawn, so a value
    // captured once at Awake goes stale — the shader would fade around a point the lamp has left.
    // Re-sent whenever it no longer matches, which is free on a lamp that never moves.
    private void LateUpdate()
    {
        if (!isShowing) return;
        if (CentreTransform.position != _pushedCentre) PushMaterialValues();
    }

    // Deliberately not a valid position, so the first push always happens.
    private Vector3 _pushedCentre = new Vector3(float.NaN, float.NaN, float.NaN);

    // Shader property names — these are the reference names the shader graph has to expose.
    static readonly int CloudCentreId    = Shader.PropertyToID("_CloudCentre");
    static readonly int CloudRadiusId    = Shader.PropertyToID("_CloudRadius");
    static readonly int CloudFadeStartId = Shader.PropertyToID("_CloudFadeStart");

    static MaterialPropertyBlock block;

    /// <summary>Called by StreetLightController — on while the lamp is lit.</summary>
    public void SetShowing(bool showing)
    {
        if (showing == isShowing) return;
        isShowing = showing;
        EnsureInitialised();

        if (showing) ps.Play();
        // Stop emitting rather than clearing: the quads already in the air fade out on their way to
        // the radius as they always would, instead of the cloud vanishing in a frame.
        else         ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    /// <summary>
    /// One line on what the system actually holds, for when the cloud is invisible: an alive count
    /// of 0 means it is not running, while a healthy count with nothing on screen points at the
    /// Renderer — no material assigned is the usual answer. Read by StreetLightDebugTracer.
    /// </summary>
    public string DebugSummary()
    {
        EnsureInitialised();
        var psRenderer = GetComponent<ParticleSystemRenderer>();
        string material = psRenderer == null                  ? "no renderer"
                        : psRenderer.sharedMaterial == null   ? "MATERIAL NONE — nothing will draw"
                                                              : psRenderer.sharedMaterial.name;

        // Centre vs pushed centre: if these disagree the shader is fading around the wrong point,
        // which clips the whole cloud away while the system happily reports a full particle count.
        Vector3 centreNow = CentreTransform.position;
        string  centres   = $"centre=({centreNow.x:F2},{centreNow.y:F2},{centreNow.z:F2}) " +
                            $"pushedCentre=({_pushedCentre.x:F2},{_pushedCentre.y:F2},{_pushedCentre.z:F2})" +
                            (Vector3.Distance(centreNow, _pushedCentre) > 0.01f ? " STALE" : "");

        // The lifetime ramp, read back off the system. If these alphas climb as expected and the
        // quads still draw flat, the ramp is reaching the particles and the shader is ignoring the
        // vertex colour it arrives in.
        var    col  = ps.colorOverLifetime;
        string ramp = !col.enabled ? "colourOverLife=OFF"
                    : col.color.mode != ParticleSystemGradientMode.Gradient
                        ? $"colourOverLife=on but mode={col.color.mode}, not a gradient"
                        : $"colourOverLife=on alpha@0={col.color.gradient.Evaluate(0f).a:F2} " +
                          $"@{fadeIn:F2}={col.color.gradient.Evaluate(fadeIn).a:F2} " +
                          $"@0.5={col.color.gradient.Evaluate(0.5f).a:F2} " +
                          $"@1={col.color.gradient.Evaluate(1f).a:F2} " +
                          $"startAlpha={ps.main.startColor.color.a:F2}";

        return $"showing={isShowing} playing={ps.isPlaying} emitting={ps.isEmitting} " +
               $"alive={ps.particleCount}/{particleCount} lifetime={Lifetime:F2}s " +
               $"radius={particleRadius:F2} {ramp} {centres} material={material} " +
               $"rendererEnabled={(psRenderer != null && psRenderer.enabled)} " +
               $"gameObjectActive={gameObject.activeInHierarchy}";
    }

#if UNITY_EDITOR
    // ---- Editor helpers -----------------------------------------------------------------------
    // Entries in the component's ... menu, same as the Rebuild Preview on the procedural pieces.

    /// <summary>
    /// Writes every field on this script into the ParticleSystem's modules. This runs on its own at
    /// the start of play; use it in the editor to get the settings onto the component so the
    /// particle inspector's Play button previews the real thing.
    /// </summary>
    [ContextMenu("Apply Settings To Particle System")]
    private void ApplySettingsToParticleSystem()
    {
        ps = null;                 // force the one-time setup to run again with the current fields
        EnsureInitialised();
        UnityEditor.EditorUtility.SetDirty(ps);
        Debug.Log($"[StreetLightParticles] '{name}' pushed its settings to the ParticleSystem " +
                  $"(count {particleCount}, radius {particleRadius}, lifetime {Lifetime:0.##}s, " +
                  $"rate {particleCount / Mathf.Max(Lifetime, 0.0001f):0.##}/s).", this);
    }

    [ContextMenu("Log Particle State")]
    private void LogParticleState() =>
        Debug.Log($"[StreetLightParticles] '{name}' {DebugSummary()}", this);

    private void OnValidate()
    {
        particleCount   = Mathf.Max(1, particleCount);
        particleRadius  = Mathf.Max(0.0001f, particleRadius);
        driftSpeed      = Mathf.Max(0.0001f, driftSpeed);
        floatFrequency  = Mathf.Max(0.0001f, floatFrequency);
    }

    // ---- Radius gizmo -------------------------------------------------------------------------
    // The volume the particles live in, so the cloud can be sized against the lamp and the walls
    // around it without running anything.

    static readonly Color GizmoEdge  = new Color(1f, 0.85f, 0.5f, 0.8f);   // where they vanish
    static readonly Color GizmoInner = new Color(1f, 0.85f, 0.5f, 0.3f);   // where the fade starts
    static readonly Color GizmoFill  = new Color(1f, 0.85f, 0.5f, 0.06f);

    // Always on, but only the outer edge and the centre point — a level full of lamps stays
    // readable, matching the light gizmo on StreetLightController.
    private void OnDrawGizmos()
    {
        if (!showRadiusGizmo) return;

        Vector3 origin = CentreTransform.position;

        Gizmos.color = GizmoEdge;
        Gizmos.DrawWireSphere(origin, particleRadius);
        Gizmos.DrawSphere(origin, 0.08f);
    }

    // Selected: the full shape — a solid tint for the volume, plus the inner sphere the fade runs
    // out from, so the soft edge is something you can see rather than a number.
    private void OnDrawGizmosSelected()
    {
        if (!showRadiusGizmo) return;

        Vector3 origin = CentreTransform.position;

        Gizmos.color = GizmoFill;
        Gizmos.DrawSphere(origin, particleRadius);

        Gizmos.color = GizmoInner;
        Gizmos.DrawWireSphere(origin, particleRadius * fadeStart);

        // The centre can be pulled off this object by the centre field — draw the link so the cloud
        // can never quietly sit somewhere other than where the component is.
        if (centre != null && centre != transform)
        {
            Gizmos.color = GizmoEdge;
            Gizmos.DrawLine(transform.position, origin);
            Gizmos.DrawWireSphere(transform.position, 0.07f);
        }

        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(origin + Vector3.up * (particleRadius + 0.3f),
                                  $"{name}\n{particleCount} particles, radius {particleRadius:0.##}" +
                                  $"\nfade from {particleRadius * fadeStart:0.##}");
    }
#endif
}
