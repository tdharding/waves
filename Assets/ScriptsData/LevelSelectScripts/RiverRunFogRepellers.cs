using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Strings a chain of fog repellers along an elevated river run, so fog moving across the level
/// select world has to get past the structures instead of sliding straight through them.
///
/// Purely a look. There is no mechanic here — nothing is cleared, nothing is scored, and a run
/// that repels plays exactly like a run that does not.
///
/// WHY A CHAIN OF CIRCLES rather than a run-shaped repeller: a repeller is a circle everywhere
/// in the fog system — <see cref="FogBlob"/> pushes points radially out of one and FogMask.hlsl
/// measures distance to one — so a corridor shape would mean changing both, and both are what
/// the arena scene's fog is tuned against. Overlapping circles need neither. The mask already
/// MULTIPLIES its obstacles rather than taking the nearest, precisely so overlapping ones clear
/// their shared ground completely, which is this case exactly.
///
/// HEIGHT DOES NOT COME INTO IT. The fog is a flat field and a repeller has no Y — IFogRepeller
/// says only .xz is read. A run twenty units overhead clears the ground below it because what
/// the fog sees of any obstacle is its footprint, and that is all it has ever seen.
///
/// Every number lives on the FogMap, like the rocks and the lamps: the run owns none of them.
/// Spacing is the one that matters, because each circle is one of the thirty-two obstacle slots.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RiverRunMesh))]
public class RiverRunFogRepellers : MonoBehaviour
{
    /// <summary>
    /// One circle on the chain. Not a MonoBehaviour, for the same reason FogRockRepeller is not:
    /// a run carrying twenty child objects to hold twenty floats would be twenty things to find
    /// in the hierarchy and nothing gained. Its numbers are read live off the map rather than
    /// copied in, so tuning the fog map moves the fog while you watch it.
    /// </summary>
    class FogRunRepeller : IFogRepeller
    {
        public MonoBehaviour Owner;
        public Vector3 Centre;

        public Vector3 RepelCentre     => Centre;
        public float   RepelRadius     => FogFieldManager.RunRepelRadius;

        // The circle's radius IS the whole standoff. A second distance on top would be the same
        // dial twice over, which is the trap the boat's two radii were collapsed to escape.
        public float   RepelClearRadius => 0f;

        public float   RepelStrength   => FogFieldManager.RunRepelStrength;
        public float   MaskClearRadius => FogFieldManager.RunMaskRadius;
        public float   MaskFeather     => FogFieldManager.RunMaskFeather;

        public bool    RepelActive     => FogFieldManager.RunsRepel
                                       && Owner != null && Owner.isActiveAndEnabled
                                       && FogFieldManager.RunRepelRadius > 0.0001f;
    }

    readonly List<FogRunRepeller> _chain = new List<FogRunRepeller>();

    /// <summary>What the chain standing now was strung at, so it is only rebuilt when that moves.</summary>
    float _builtAtSpacing = -1f;

    RiverRunMesh _run;

    /// <summary>How many circles this run is currently spending, for the designer to read off.</summary>
    public int ChainLength => _chain.Count;

    void OnEnable()
    {
        _run = GetComponent<RiverRunMesh>();
        Rebuild();
    }

    void OnDisable() => Clear();

    void Update()
    {
        // Spacing is the only number that changes the chain itself — the rest are read live by
        // each circle. Two float compares on a handful of runs, against the alternative of a
        // slider you have to leave play mode to see the effect of.
        float spacing = FogFieldManager.RunChainSpacing;
        if (!Mathf.Approximately(spacing, _builtAtSpacing)) Rebuild();
    }

    void Clear()
    {
        for (int i = 0; i < _chain.Count; i++) FogFieldManager.Unregister(_chain[i]);
        _chain.Clear();
        _builtAtSpacing = -1f;
    }

    /// <summary>Restring the chain. For the designer, after a river's shape has moved.</summary>
    public void Restring() => Rebuild();

    void Rebuild()
    {
        Clear();
        _builtAtSpacing = FogFieldManager.RunChainSpacing;

        _scratch.Clear();
        if (!SampleChain(_scratch)) return;

        for (int i = 0; i < _scratch.Count; i++)
        {
            var circle = new FogRunRepeller { Owner = this, Centre = _scratch[i] };
            _chain.Add(circle);
            FogFieldManager.Register(circle);
        }
    }

    static readonly List<Vector3> _scratch = new List<Vector3>();

    /// <summary>
    /// Where the circles go, in world space. Separate from registering them so the gizmo can
    /// show a chain without putting one into the manager's list — a scene view drawing itself
    /// is not a reason for fog obstacles to exist.
    ///
    /// The curve comes from the run's record rather than from any spline in the scene: the
    /// designer splits its splines at junctions for the boat's routing, so the unbroken curve
    /// the run was actually swept along only survives on <see cref="RiverRunMesh"/>.
    /// </summary>
    bool SampleChain(List<Vector3> into)
    {
        if (_run == null) _run = GetComponent<RiverRunMesh>();
        if (_run == null || _run.knots == null || _run.knots.Count < 2) return false;

        var spline = _run.ToSpline();
        if (spline.Count < 2) return false;

        float length = spline.GetLength();
        if (length <= 0.0001f) return false;

        float spacing = Mathf.Max(FogFieldManager.RunChainSpacing, 0.05f);

        // Spread evenly rather than stepping from the start and letting the tail fall short: a
        // run ending one short leaves its last stretch open, and which end that happens at
        // depends on nothing you can see. Round to the nearest whole gap and share the run out
        // between them, so both ends carry a circle and the real spacing lands near the asked-for
        // one instead of on it.
        int gaps = Mathf.Max(1, Mathf.RoundToInt(length / spacing));

        for (int i = 0; i <= gaps; i++)
        {
            float t = (float)i / gaps;
            into.Add(transform.TransformPoint((Vector3)spline.EvaluatePosition(t)));
        }
        return true;
    }

#if UNITY_EDITOR
    // Drawn at the run's own height, where the structure is, rather than down at the fog plane.
    // The manager already draws what it is clearing down there; this is for lining the chain up
    // against the thing it is meant to be covering.
    void OnDrawGizmosSelected()
    {
        var points = new List<Vector3>();
        if (!SampleChain(points)) return;

        float radius = FogFieldManager.RunRepelRadius;
        bool  on     = FogFieldManager.RunsRepel;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 c = points[i];
            UnityEditor.Handles.color = on ? new Color(0.95f, 0.55f, 0.35f, 0.14f)
                                           : new Color(0.55f, 0.55f, 0.58f, 0.10f);
            UnityEditor.Handles.DrawSolidDisc(c, Vector3.up, radius);
            UnityEditor.Handles.color = on ? new Color(0.95f, 0.45f, 0.30f, 0.5f)
                                           : new Color(0.55f, 0.55f, 0.58f, 0.4f);
            UnityEditor.Handles.DrawWireDisc(c, Vector3.up, radius);
        }

        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(points[0],
            on ? $"fog chain {points.Count}" : $"fog chain {points.Count} (runs repel off)");
    }
#endif
}
