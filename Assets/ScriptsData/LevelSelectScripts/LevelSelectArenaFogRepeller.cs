using UnityEngine;

/// <summary>
/// Holds fog off a level select arena, so fog crossing the world has to get past the arena
/// instead of sliding straight through its wall.
///
/// Purely a look, like <see cref="RiverRunFogRepellers"/>: nothing is cleared and nothing is
/// scored.
///
/// ONE CIRCLE, not a chain. An arena already is a circle, which is the one shape the fog system
/// understands, so it spends a single obstacle slot however big it is.
///
/// The circle's own radius is the wall's OUTER face, read live off
/// <see cref="LevelSelectArenaWallMesh"/> so a wall rebuilt to a new shape moves the fog with it.
/// Every other number lives on the FogMap, like the rocks, the lamps and the runs: the arena owns
/// none of them.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LevelSelectArenaWallMesh))]
public class LevelSelectArenaFogRepeller : MonoBehaviour, IFogRepeller
{
    LevelSelectArenaWallMesh _wall;

    public Vector3 RepelCentre      => transform.position;
    public float   RepelRadius      => _wall != null && _wall.profile != null ? _wall.profile.OuterRadius : 0f;
    public float   RepelClearRadius => FogFieldManager.ArenaRepelRadius;
    public float   RepelStrength    => FogFieldManager.ArenaRepelStrength;
    public float   MaskClearRadius  => FogFieldManager.ArenaMaskRadius;
    public float   MaskFeather      => FogFieldManager.ArenaMaskFeather;

    public bool    RepelActive      => FogFieldManager.ArenasRepel && isActiveAndEnabled
                                    && RepelRadius + RepelClearRadius > 0.0001f;

    void OnEnable()
    {
        _wall = GetComponent<LevelSelectArenaWallMesh>();
        FogFieldManager.Register(this);
    }

    void OnDisable() => FogFieldManager.Unregister(this);

#if UNITY_EDITOR
    // Drawn at the wall's own height, for lining the circle up against the arena it covers.
    void OnDrawGizmosSelected()
    {
        var wall = GetComponent<LevelSelectArenaWallMesh>();
        if (wall == null || wall.profile == null) return;

        float radius = wall.profile.OuterRadius + FogFieldManager.ArenaRepelRadius;
        bool  on     = FogFieldManager.ArenasRepel;

        UnityEditor.Handles.color = on ? new Color(0.95f, 0.55f, 0.35f, 0.14f)
                                       : new Color(0.55f, 0.55f, 0.58f, 0.10f);
        UnityEditor.Handles.DrawSolidDisc(transform.position, Vector3.up, radius);
        UnityEditor.Handles.color = on ? new Color(0.95f, 0.45f, 0.30f, 0.5f)
                                       : new Color(0.55f, 0.55f, 0.58f, 0.4f);
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, radius);

        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(transform.position, on ? "fog arena" : "fog arena (arenas repel off)");
    }
#endif
}
