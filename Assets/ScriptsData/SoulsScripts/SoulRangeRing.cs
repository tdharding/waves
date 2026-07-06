using UnityEngine;

/// <summary>
/// Draws a ring of particles around the boat at interactionRadius.
/// Attach to the boat. Assign a Material on the ParticleSystem Renderer.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class SoulRangeRing : MonoBehaviour
{
    public static SoulRangeRing Instance { get; private set; }

    [Header("Range")]
    public float interactionRadius = 8f;

    [Header("Dot Ring")]
    [SerializeField] private int dotCount = 48;
    [SerializeField] private float dotSize = 0.35f;
    [SerializeField] private Color dotColor = new Color(0.4f, 0.9f, 1f, 0.7f);
    [SerializeField] private float heightOffset = 0.05f;
    [SerializeField] private float rotationSpeed = 0.3f;

    private ParticleSystem ps;
    private ParticleSystem.Particle[] particles;
    private bool isShowing;
    private float angleOffset;

    private void Awake()
    {
        Instance = this;
        ps = GetComponent<ParticleSystem>();

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

        particles = new ParticleSystem.Particle[dotCount];
    }

    private void OnEnable()
    {
        SoulPickupEvents.OnSoulPickedUp += Show;
        SoulPickupEvents.OnSoulReleased += Hide;
    }

    private void OnDisable()
    {
        SoulPickupEvents.OnSoulPickedUp -= Show;
        SoulPickupEvents.OnSoulReleased -= Hide;
    }

    private void Update()
    {
        if (!isShowing) return;

        angleOffset += rotationSpeed * Time.deltaTime;
        UpdateParticlePositions();
    }

    private void Show()
    {
        isShowing = true;
        UpdateParticlePositions();
    }

    private void Hide()
    {
        isShowing = false;
        ps.Clear();
    }

    private void UpdateParticlePositions()
    {
        for (int i = 0; i < dotCount; i++)
        {
            float angle = angleOffset + i / (float)dotCount * Mathf.PI * 2f;
            particles[i].position          = transform.position + new Vector3(
                                                 Mathf.Cos(angle) * interactionRadius,
                                                 heightOffset,
                                                 Mathf.Sin(angle) * interactionRadius);
            particles[i].startSize         = dotSize;
            particles[i].startColor        = dotColor;
            particles[i].remainingLifetime = 99999f;
            particles[i].velocity          = Vector3.zero;
        }

        ps.SetParticles(particles, dotCount);
    }
}
