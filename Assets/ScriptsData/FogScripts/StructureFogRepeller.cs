using UnityEngine;

/// <summary>
/// Holds fog off a level select freestanding structure, when its Fog Repeller is ticked in the
/// designer.
///
/// Pushes as a spike does — every number but the radius is the FogMap's Rock numbers, and the
/// Adopt Rocks switch turns these off with the rest (see <see cref="SpikeFogRepeller"/>).
///
/// The structure owns its radius (its own size, or the Override Radius) and where its centre sits
/// in its own frame — a block's object stands at one end, not the middle. Both are written by the
/// designer at generation.
///
/// It also owns its height, ground to top, so the Fog Map's height window tests the whole column
/// rather than only its foot (see <see cref="IFogRepellerSpan"/>).
/// </summary>
[DisallowMultipleComponent]
public class StructureFogRepeller : MonoBehaviour, IFogRepeller, IFogRepellerSpan
{
    [Tooltip("The structure's radius at the ground. Written by the designer.")]
    [SerializeField] float radius = 0.5f;

    [Tooltip("The structure's centre in its own frame. Written by the designer.")]
    [SerializeField] Vector3 localCentre;

    [Tooltip("How far its top stands above the ground under its centre — walls, columns and " +
             "towers included. Written by the designer.")]
    [SerializeField] float topHeight;

    public void Configure(float groundRadius, Vector3 centre, float top)
    {
        radius      = Mathf.Max(groundRadius, 0f);
        localCentre = centre;
        topHeight   = Mathf.Max(top, 0f);
    }

    public float   RepelBottomY     => RepelCentre.y;
    public float   RepelTopY        => RepelCentre.y + topHeight;

    public Vector3 RepelCentre      => transform.TransformPoint(localCentre);
    public float   RepelRadius      => radius;
    public float   RepelClearRadius => FogFieldManager.RockRepelRadius;
    public float   RepelStrength    => FogFieldManager.RockRepelStrength;
    public float   MaskClearRadius  => FogFieldManager.RockMaskRadius;
    public float   MaskFeather      => FogFieldManager.RockMaskFeather;

    public bool    RepelActive      => FogFieldManager.AdoptRocks && isActiveAndEnabled
                                    && radius + RepelClearRadius > 0.0001f;

    void OnEnable()  => FogFieldManager.Register(this);
    void OnDisable() => FogFieldManager.Unregister(this);
}
