using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// The obstacle's end of the deal — put one on anything that should push fog away and does not
/// already throw rock rings. Blocks, spline walls, the boat.
///
/// Rocks need nothing: <see cref="FogRockRepeller"/> adopts anything already exposing IRockRing,
/// so a level's spikes push fog around without being touched.
/// </summary>
[DisallowMultipleComponent]
public class FogRepellerSource : MonoBehaviour, IFogRepeller
{
    [Tooltip("The obstacle's own radius at the waterline.")]
    [SerializeField] float radius = 1f;

    [Tooltip("Clear water kept beyond that radius. Around 0.55 reads right against the sketches. " +
             "Note it does not simply add a gap — the mass is stretched around a bigger circle and " +
             "thins, so raise body thickness alongside it.")]
    [FormerlySerializedAs("standoff")]
    [SerializeField] float clearRadius = 0.55f;

    [Tooltip("1 pins fog exactly on the clear radius. Lower lets it press in and recover, which " +
             "suits something that moves — the boat wants roughly 0.6.")]
    [Range(0f, 1f)] [SerializeField] float strength = 1f;

    [Tooltip("Lift the measuring point off the transform, for a wall whose pivot is not at its " +
             "centre.")]
    [SerializeField] Vector3 offset;

    public Vector3 RepelCentre   => transform.position + offset;
    public float   RepelRadius   => radius;
    public float   RepelClearRadius => clearRadius;
    public float   RepelStrength => strength;
    public bool    RepelActive   => isActiveAndEnabled && radius + clearRadius > 0.0001f;

    /// <summary>For anything building these at spawn rather than authoring them in a prefab.</summary>
    public void Configure(float waterlineRadius, float clearDistance, float pushStrength)
    {
        radius   = Mathf.Max(waterlineRadius, 0f);
        clearRadius = Mathf.Max(clearDistance, 0f);
        strength = Mathf.Clamp01(pushStrength);
    }

    void OnEnable()  => FogFieldManager.Register(this);
    void OnDisable() => FogFieldManager.Unregister(this);

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 c = RepelCentre; c.y += 0.02f;
        // Solid discs: the filled area is the exclusion, and the ring between them is the clear radius.
        UnityEditor.Handles.color = new Color(0.95f, 0.55f, 0.35f, 0.16f);
        UnityEditor.Handles.DrawSolidDisc(c, Vector3.up, radius + clearRadius);
        UnityEditor.Handles.color = new Color(0.95f, 0.4f, 0.25f, 0.22f);
        UnityEditor.Handles.DrawSolidDisc(c, Vector3.up, radius);
    }
#endif
}

/// <summary>
/// Wraps a rock that already tells the water where it stands, so fog gets its obstacles for free.
///
/// A spike's waterline centre and radius are exactly what the fog needs, and ProceduralSpike
/// already publishes both through IRockRing for the ring bands. Adopting that means a level full
/// of rocks pushes fog around with nothing tagged, wired, or added to any prefab.
///
/// Not a MonoBehaviour — <see cref="FogFieldManager"/> makes these itself when it finds rocks.
/// </summary>
public class FogRockRepeller : IFogRepeller
{
    readonly IRockRing _rock;
    readonly float _clearRadius;
    readonly float _strength;

    public FogRockRepeller(IRockRing rock, float clearRadius, float strength)
    {
        _rock = rock;
        _clearRadius = clearRadius;
        _strength = strength;
    }

    public IRockRing Rock => _rock;

    public Vector3 RepelCentre   => _rock != null ? _rock.RingCentre : Vector3.zero;
    public float   RepelRadius   => _rock != null ? _rock.RingRadius : 0f;
    public float   RepelClearRadius => _clearRadius;
    public float   RepelStrength => _strength;

    // A rock that has stopped throwing rings — destroyed, or disabled — has also stopped being an
    // obstacle, so the two go quiet together rather than fog piling against an invisible wall.
    public bool RepelActive
    {
        get
        {
            if (_rock == null) return false;
            var mb = _rock as MonoBehaviour;
            if (mb == null) return _rock.RingActive;
            return mb != null && mb.isActiveAndEnabled && _rock.RingActive;
        }
    }
}

/// <summary>
/// The boat, as an obstacle. Driven entirely from the FogMap, so an arena decides how the fog
/// behaves around the hull and nothing has to be authored on the boat prefab.
///
/// The boat used to carry a <see cref="FogRepellerSource"/> like a wall does. That put a weather
/// decision on a vehicle: every arena got the same hull clearance, and changing it meant editing
/// the boat. The manager skips a FogRepellerSource found on the boat so the two cannot both apply
/// and push twice.
///
/// Not a MonoBehaviour — <see cref="FogFieldManager"/> owns one and keeps it pointed at the boat.
/// </summary>
public class FogBoatRepeller : IFogRepeller
{
    public Vector3 Centre;
    public float   Radius;
    public float   ClearRadius;
    public float   Strength;

    public Vector3 RepelCentre   => Centre;
    public float   RepelRadius   => Radius;
    public float   RepelClearRadius => ClearRadius;
    public float   RepelStrength => Strength;

    // No map means no fog at all, so the boat has nothing to push and Strength sits at zero.
    public bool RepelActive => Strength > 0.0001f && Radius + ClearRadius > 0.0001f;
}
