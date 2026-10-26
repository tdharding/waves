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

    public bool    RepelActive      => Repels && isActiveAndEnabled
                                    && RepelRadius + RepelClearRadius > 0.0001f;

    /// <summary>
    /// Pools repel at all, and a pool on a Landscape Natural River Run only while the FogMap's
    /// Natural Runs Repel is on — the same rule the run chains follow, read live.
    /// </summary>
    bool Repels => FogFieldManager.PoolsRepel
                && (_pool == null || !_pool.naturalRun || FogFieldManager.NaturalRunsRepel);

    void OnEnable()
    {
        _pool = GetComponent<RiverPoolMesh>();
        FogFieldManager.Register(this);
    }

    void OnDisable() => FogFieldManager.Unregister(this);
}
