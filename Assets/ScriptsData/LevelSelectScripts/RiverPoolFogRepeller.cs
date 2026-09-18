using UnityEngine;

/// <summary>
/// Holds fog off a generated pool, so fog crossing the level select world has to get past the
/// basin instead of sliding straight over it.
///
/// Purely a look, like <see cref="RiverRunFogRepellers"/>: nothing is cleared and nothing is
/// scored.
///
/// ONE CIRCLE, not a chain. A pool already is a circle, which is the one shape the fog system
/// understands, so it spends a single obstacle slot however big it is.
///
/// The circle's own radius is the pool's, read live off <see cref="RiverPoolMesh"/> so a pool
/// rebuilt to a new size moves the fog with it. Every other number lives on the FogMap, like the
/// rocks, the lamps and the runs: the pool owns none of them.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RiverPoolMesh))]
public class RiverPoolFogRepeller : MonoBehaviour, IFogRepeller
{
    RiverPoolMesh _pool;

    public Vector3 RepelCentre      => transform.position;
    public float   RepelRadius      => _pool != null ? _pool.poolRadius : 0f;
    public float   RepelClearRadius => FogFieldManager.PoolRepelRadius;
    public float   RepelStrength    => FogFieldManager.PoolRepelStrength;
    public float   MaskClearRadius  => FogFieldManager.PoolMaskRadius;
    public float   MaskFeather      => FogFieldManager.PoolMaskFeather;

    public bool    RepelActive      => FogFieldManager.PoolsRepel && isActiveAndEnabled
                                    && RepelRadius + RepelClearRadius > 0.0001f;

    void OnEnable()
    {
        _pool = GetComponent<RiverPoolMesh>();
        FogFieldManager.Register(this);
    }

    void OnDisable() => FogFieldManager.Unregister(this);

#if UNITY_EDITOR
    // Drawn at the pool's own height, for lining the circle up against the basin it covers.
    void OnDrawGizmosSelected()
    {
        var pool = GetComponent<RiverPoolMesh>();
        if (pool == null) return;

        float radius = pool.poolRadius + FogFieldManager.PoolRepelRadius;
        bool  on     = FogFieldManager.PoolsRepel;

        UnityEditor.Handles.color = on ? new Color(0.95f, 0.55f, 0.35f, 0.14f)
                                       : new Color(0.55f, 0.55f, 0.58f, 0.10f);
        UnityEditor.Handles.DrawSolidDisc(transform.position, Vector3.up, radius);
        UnityEditor.Handles.color = on ? new Color(0.95f, 0.45f, 0.30f, 0.5f)
                                       : new Color(0.55f, 0.55f, 0.58f, 0.4f);
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, radius);

        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(transform.position, on ? "fog pool" : "fog pool (pools repel off)");
    }
#endif
}
