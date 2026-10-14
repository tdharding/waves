using UnityEngine;

/// <summary>
/// Holds fog off a spike that throws no rock rings — the level select's spikes, which stand on
/// the landscape rather than in water, so have no bands to throw.
///
/// The SAME rock as far as the fog is concerned: every number is the FogMap's Rock numbers, and
/// the Adopt Rocks switch turns these off with the rest. So a rock in a level (adopted through
/// its RockRingSource) and a rock on the level select push fog alike, from one set of numbers.
///
/// The only thing the spike owns is its radius where it meets the ground, written once at
/// generation off the profile the mesh was built from.
///
/// Goes quiet on an object that also throws rock rings — the manager already adopts that one,
/// and both together would push twice.
/// </summary>
[DisallowMultipleComponent]
public class SpikeFogRepeller : MonoBehaviour, IFogRepeller
{
    [Tooltip("The spike's radius where it meets the ground. Written by the generator.")]
    [SerializeField] float radius = 0.5f;

    RockRingSource _rings;

    public void Configure(float groundRadius) => radius = Mathf.Max(groundRadius, 0f);

    public Vector3 RepelCentre      => transform.position;
    public float   RepelRadius      => radius;
    public float   RepelClearRadius => FogFieldManager.RockRepelRadius;
    public float   RepelStrength    => FogFieldManager.RockRepelStrength;
    public float   MaskClearRadius  => FogFieldManager.RockMaskRadius;
    public float   MaskFeather      => FogFieldManager.RockMaskFeather;

    public bool    RepelActive      => FogFieldManager.AdoptRocks && isActiveAndEnabled && _rings == null
                                    && radius + RepelClearRadius > 0.0001f;

    void OnEnable()
    {
        _rings = GetComponent<RockRingSource>();
        FogFieldManager.Register(this);
    }

    void OnDisable() => FogFieldManager.Unregister(this);

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        float r  = radius + FogFieldManager.RockRepelRadius;
        bool  on = FogFieldManager.AdoptRocks && GetComponent<RockRingSource>() == null;

        UnityEditor.Handles.color = on ? new Color(0.95f, 0.55f, 0.35f, 0.14f)
                                       : new Color(0.55f, 0.55f, 0.58f, 0.10f);
        UnityEditor.Handles.DrawSolidDisc(transform.position, Vector3.up, r);
        UnityEditor.Handles.color = on ? new Color(0.95f, 0.4f, 0.25f, 0.22f)
                                       : new Color(0.55f, 0.55f, 0.58f, 0.16f);
        UnityEditor.Handles.DrawSolidDisc(transform.position, Vector3.up, radius);

        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(transform.position, on ? "fog spike" : "fog spike (off)");
    }
#endif
}
