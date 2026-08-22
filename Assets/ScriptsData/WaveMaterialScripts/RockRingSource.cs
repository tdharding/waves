using UnityEngine;

/// <summary>
/// The rock's end of the deal. ProceduralSpike adds one of these at spawn and hands it the
/// numbers it already worked out for the mesh, so the bands on the water are drawn from the
/// same silhouette as the rock standing in it rather than from a second set of settings.
/// </summary>
[DisallowMultipleComponent]
public class RockRingSource : MonoBehaviour, IRockRing
{
    [SerializeField] float radius;
    [SerializeField] float lobeCount;
    [SerializeField] float lobeAmplitude;
    [SerializeField] float twist;
    [SerializeField] float phaseOffset;

    public Vector3 RingCentre        => transform.position;
    public float   RingRadius        => radius;
    public float   RingLobeCount     => lobeCount;
    public float   RingLobeAmplitude => lobeAmplitude;
    public float   RingTwist         => twist;
    public float   RingPhaseOffset   => phaseOffset;
    public bool    RingActive        => isActiveAndEnabled && radius > 0.0001f;

    /// <summary>
    /// Called by ProceduralSpike once the mesh is built.
    ///   waterlineRadius — where the rock meets the water, so band 0 starts on its edge
    ///   lobeCount       — its carved flute count
    ///   lobeAmplitude   — how deep those flutes cut, as a fraction of the radius. A rock with no
    ///                     carved ridges is genuinely a circle at the waterline, so it gets 0 and
    ///                     throws plain circular bands.
    ///   twist           — the rock's own helix pitch, so the lobes keep winding as the bands spread
    /// </summary>
    public void Configure(float waterlineRadius, float lobeCount, float lobeAmplitude, float twist)
    {
        this.radius        = Mathf.Max(waterlineRadius, 0f);
        this.lobeCount     = Mathf.Max(lobeCount, 0f);
        this.lobeAmplitude = Mathf.Clamp01(lobeAmplitude);
        this.twist         = twist;

        // A stable scatter from where the rock stands, so two rocks side by side pulse out of
        // step with each other and the field never looks like one synchronised machine.
        Vector3 p = transform.position;
        this.phaseOffset = Mathf.Repeat(Mathf.Abs(Mathf.Sin(p.x * 12.9898f + p.z * 78.233f) * 43758.5453f),
                                        Mathf.PI * 2f);
    }

    void OnEnable()  => RockRingManager.Register(this);
    void OnDisable() => RockRingManager.Unregister(this);
}
