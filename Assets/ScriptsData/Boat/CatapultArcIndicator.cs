using UnityEngine;

/// <summary>
/// Add to the same GameObject as CatapultController (or a child).
/// Requires a ParticleSystem. Assign a soft dot material on the PS Renderer.
/// Dots travel along the catapult arc while a soul is loaded.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class CatapultArcIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CatapultController catapult;

    [Header("Dot Trail")]
    [SerializeField] private int dotCount     = 20;
    [SerializeField] private float dotSize    = 0.25f;
    [SerializeField] private float travelSpeed = 0.6f;   // full arc cycles per second
    [SerializeField] private Color dotColor   = new Color(1f, 0.85f, 0.3f, 0.85f);

    private ParticleSystem ps;
    private ParticleSystem.Particle[] particles;
    private float timeOffset;
    private bool isShowing;

    private void Awake()
    {
        if (catapult == null)
            catapult = GetComponent<CatapultController>();

        ps = GetComponent<ParticleSystem>();
        particles = new ParticleSystem.Particle[dotCount];
        ConfigureParticleSystem();
    }

    private void ConfigureParticleSystem()
    {
        var main = ps.main;
        main.loop            = false;
        main.playOnAwake     = false;
        main.maxParticles    = dotCount;
        main.startLifetime   = 99999f;
        main.startSpeed      = 0f;
        main.startSize       = dotSize;
        main.startColor      = dotColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = false;
    }

    private void Update()
    {
        if (catapult == null) return;

        bool loaded = catapult.soulSlot != null && catapult.soulSlot.IsFilled && !catapult.IsFiring;

        if (loaded)
        {
            timeOffset = (timeOffset + travelSpeed * Time.deltaTime) % 1f;
            UpdateArcParticles();
            if (!isShowing) { isShowing = true; }
        }
        else if (isShowing)
        {
            isShowing = false;
            ps.Clear();
        }
    }

    private void UpdateArcParticles()
    {
        Vector3 origin  = catapult.launchPoint != null ? catapult.launchPoint.position : catapult.transform.position;
        Vector3 fireDir = catapult.transform.TransformDirection(catapult.fireDirectionLocal.normalized);
        float   dist    = catapult.EffectiveThrowDistance();
        float   peak    = catapult.EffectiveArcPeak();
        Vector3 landing = origin + fireDir * dist;

        for (int i = 0; i < dotCount; i++)
        {
            float t = ((float)i / dotCount + timeOffset) % 1f;

            Vector3 flat = Vector3.Lerp(origin, landing, t);
            float   arcY = Mathf.Sin(t * Mathf.PI) * peak;

            particles[i].position          = new Vector3(flat.x, flat.y + arcY, flat.z);
            particles[i].startSize         = dotSize;
            particles[i].startColor        = dotColor;
            particles[i].remainingLifetime = 99999f;
            particles[i].velocity          = Vector3.zero;
        }

        ps.SetParticles(particles, dotCount);
    }
}
