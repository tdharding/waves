using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Draws a fish-bowl tributary onto the main river, then sends its shoal up it.
///
/// TWO conditions must both be met before the joining line is drawn:
///   1. the fish-bowl tower is toppled and its bowl has landed in the water, and
///   2. the street light on the MAIN river at the junction node is lit.
/// Order doesn't matter — whichever happens second triggers the draw.
///
/// Once drawn, the shoal's swim spline is rebuilt as a through-route:
///   bowl → tributary corridor → junction → up the river → circle at the frontier light.
/// Fish swim it at their own SplineAnimate speed (nothing drags them) and wrap inside the
/// frontier circle, exactly like the river's own shoal. When another river light is lit the
/// frontier advances and the route is extended so they follow on.
///
/// The gate light belongs to the river's own SoulZoneStreetLightChain — this link only WATCHES
/// its lit state, since a StreetLightController can be owned by just one chain.
///
/// Spaces: mask lists are reg space (final world positions). The swim spline is CONTAINER-LOCAL,
/// converted at build time — safe here because the route is only ever built after the bowl has
/// landed and the maze has settled, so the container is stationary from then on.
/// </summary>
public class SoulZoneTributaryLink : MonoBehaviour
{
    [Tooltip("World units per second the joining line draws from the bowl to the river.")]
    public float revealSpeed = 2.5f;

    [Tooltip("Seconds the bowl's source pool takes to bloom when the bowl lands.")]
    public float poolOpenSeconds = 0.75f;

    // ── wired by Init / SetGate ──────────────────────────
    List<Vector3> _regPath;          // dense bowl → junction, mask space
    float _sourceRadius;             // pool around the bowl
    float _corridorRadius;           // thin joining band
    SoulShoalController _shoal;
    SplineContainer _spline;
    int _knotCount;
    StreetLightController _gate;     // the river's lamp at the junction
    SoulZoneStreetLightChain _mainChain;
    int _junctionDense = -1;         // index into _mainChain.RegPath where this tributary meets it

    // ── state ────────────────────────────────────────────
    float[] _cumArc;
    readonly List<Vector3> _revealed = new List<Vector3>();  // THE registered corridor list
    readonly List<SplineAnimate> _fish = new List<SplineAnimate>();
    Vector2[] _knotOffsets;
    List<Vector3> _pool;
    bool _poolRegistered, _poolFullyOpen, _revealing, _joined;
    int   _routeFrontier = -1;       // frontier index the current route was built for
    float _circleStart, _circleEnd;  // normalized window of the frontier circle
    bool  _routeBuilt;

    public bool IsJoined => _joined;

    public void Init(List<Vector3> regPath, float sourceRadius, float corridorRadius,
                     SoulShoalController shoal, SplineContainer spline, int knotCount)
    {
        _regPath        = regPath;
        _sourceRadius   = Mathf.Max(sourceRadius, 0.05f);
        _corridorRadius = Mathf.Max(corridorRadius, 0.05f);
        _shoal          = shoal;
        _spline         = spline;
        _knotCount      = Mathf.Max(6, knotCount);

        _cumArc = new float[_regPath.Count];
        for (int i = 1; i < _regPath.Count; i++)
            _cumArc[i] = _cumArc[i - 1] + Vector3.Distance(_regPath[i - 1], _regPath[i]);

        _knotOffsets = new Vector2[_knotCount];
        for (int i = 0; i < _knotCount; i++)
            _knotOffsets[i] = UnityEngine.Random.insideUnitCircle;
    }

    /// <summary>Wired by LevelSpawner once every zone's chains exist (order-independent).</summary>
    public void SetGate(StreetLightController gate, SoulZoneStreetLightChain mainChain, int junctionDense)
    {
        _gate          = gate;   // informational only — the junction need not carry a lamp
        _mainChain     = mainChain;
        _junctionDense = junctionDense;

        if (_mainChain == null || _junctionDense < 0)
            Debug.LogWarning($"[TributaryLink] '{name}' can never open — " +
                             $"mainChain={(mainChain != null ? "ok" : "NULL (main path has no street lights)")}, " +
                             $"junctionDense={_junctionDense}.");
    }

    // Condition 2: the river's revealed path has reached (or passed) the junction node. The chain
    // reveals from its start node forward, and only advances its frontier once a reveal completes,
    // so this is true exactly when the river has actually flowed past this point. A junction that
    // sits ON a lamp node therefore opens when that lamp is lit; a mid-path junction opens when the
    // river runs beyond it.
    bool RiverHasPassedJunction =>
        _mainChain != null && _junctionDense >= 0 && _mainChain.FrontierDense >= _junctionDense;

    void Update()
    {
        if (_regPath == null || _regPath.Count < 2) return;

        if (!_joined)
        {
            // Condition 1: the tower is down, the bowl has settled, and its pool has bloomed to
            // full size — the source has to exist properly before it can feed anything.
            bool landed = _shoal == null || _shoal.IsBowlLanded;
            if (!landed) return;

            if (!_poolRegistered)
            {
                _pool = new List<Vector3> { _regPath[0] };
                SoulFishWaveLinker.RegisterZone(_pool, false, 0.02f);
                SoulFishMapLinker.RegisterZone(_pool, false, 0.02f);
                _poolRegistered = true;
                StartCoroutine(OpenPool());
                Debug.Log($"[TributaryLink] Bowl landed on '{name}' — source pool opening.");
            }
            if (!_poolFullyOpen) return;

            // Condition 2: the river has flowed past the junction node.
            if (!_revealing && RiverHasPassedJunction)
                StartCoroutine(DrawJoin());
            return;
        }

        // Joined: keep the shoal's route in step with how far the river has opened.
        if (_mainChain != null && _mainChain.FrontierIndex != _routeFrontier)
            BuildRoute();
    }

    IEnumerator OpenPool()
    {
        float t = 0f;
        while (t < poolOpenSeconds)
        {
            t += Time.deltaTime;
            float r = Mathf.Lerp(0.02f, _sourceRadius,
                                 Mathf.SmoothStep(0f, 1f, poolOpenSeconds > 0f ? t / poolOpenSeconds : 1f));
            SoulFishWaveLinker.UpdateZoneRadius(_pool, r);
            SoulFishMapLinker.UpdateZoneRadius(_pool, r);
            SoulFishMapLinker.Instance?.BakePositionsOnce();
            yield return null;
        }
        SoulFishWaveLinker.UpdateZoneRadius(_pool, _sourceRadius);
        SoulFishMapLinker.UpdateZoneRadius(_pool, _sourceRadius);
        _poolFullyOpen = true;
        Debug.Log($"[TributaryLink] '{name}' source pool at full radius ({_sourceRadius:F2}) — " +
                  $"waiting for the river to pass junction node (dense {_junctionDense}).");
    }

    IEnumerator DrawJoin()
    {
        _revealing = true;
        Debug.Log($"[TributaryLink] Both conditions met on '{name}' (bowl pool full + river passed the junction) — drawing the join.");

        SoulFishWaveLinker.RegisterZone(_revealed, false, _corridorRadius);
        SoulFishMapLinker.RegisterZone(_revealed, false, _corridorRadius);

        float total = _cumArc[_cumArc.Length - 1];
        float arc = 0f;
        while (arc < total)
        {
            arc = Mathf.Min(arc + revealSpeed * Time.deltaTime, total);
            Rebuild(arc);
            SoulFishMapLinker.Instance?.BakePositionsOnce();
            yield return null;
        }
        Rebuild(total);
        SoulFishMapLinker.Instance?.BakePositionsOnce();

        _revealing = false;
        _joined    = true;
        Debug.Log($"[TributaryLink] '{name}' joined the main river — shoal now free to swim up it.");

        BuildRoute();   // fish leave the bowl and head for the frontier light
    }

    // Swim route: bowl → tributary corridor → junction → along the river → circle at the frontier
    // light. Rebuilt (extended) whenever the river's frontier advances.
    void BuildRoute()
    {
        if (_spline == null || _mainChain == null || _junctionDense < 0) return;

        var main = _mainChain.RegPath;
        if (main == null || main.Count == 0) return;

        int frontier = Mathf.Clamp(_mainChain.FrontierDense, 0, main.Count - 1);
        int junction = Mathf.Clamp(_junctionDense, 0, main.Count - 1);

        // World-space route, then one conversion to container-local at the end.
        var route = new List<Vector3>(_regPath);          // bowl → junction

        int step = frontier >= junction ? 1 : -1;         // the river may open either way from here
        for (int i = junction + step; i != frontier + step; i += step)
            route.Add(main[i]);

        // Circle at the frontier light so they gather there rather than stopping dead.
        Vector3 centre = main[frontier];
        float   r      = Mathf.Max(_mainChain.FrontierRadius, 0.05f);
        int     circleStartIdx = route.Count;
        for (int i = 0; i < _knotCount; i++)
        {
            float ang = (i / (float)_knotCount) * Mathf.PI * 2f;
            route.Add(centre + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r);
        }

        // Cumulative length → normalized window of the circle, and the old/new length ratio used
        // to keep fish where they already are when the route is extended.
        var cum = new float[route.Count];
        for (int i = 1; i < route.Count; i++)
            cum[i] = cum[i - 1] + Vector3.Distance(route[i - 1], route[i]);
        float total = Mathf.Max(cum[cum.Length - 1], 1e-4f);

        float newStart = cum[Mathf.Min(circleStartIdx, cum.Length - 1)] / total;
        float newEnd   = cum[cum.Length - 1] / total;

        // Preserve each fish's arc position across the rebuild (the route only ever extends).
        float oldTotal = _routeBuilt ? _prevTotalLen : total;
        CacheFish();
        if (_routeBuilt && oldTotal > 1e-4f)
        {
            float ratio = oldTotal / total;
            foreach (var sa in _fish)
                if (sa != null) sa.NormalizedTime = Mathf.Clamp01(sa.NormalizedTime * ratio);
        }
        else
        {
            // First build: file them along the bowl→junction leg so they stream out of the bowl.
            float leadEnd = cum[Mathf.Min(_regPath.Count - 1, cum.Length - 1)] / total;
            for (int i = 0; i < _fish.Count; i++)
            {
                if (_fish[i] == null) continue;
                _fish[i].Loop        = SplineAnimate.LoopMode.Loop;
                _fish[i].StartOffset = 0f;   // we own position via NormalizedTime (see the chain for why)
                _fish[i].NormalizedTime = _fish.Count > 1 ? leadEnd * i / _fish.Count : 0f;
            }
        }

        var spline = _spline.Spline;
        spline.Clear();
        foreach (var p in route)
            spline.Add(new BezierKnot((float3)_spline.transform.InverseTransformPoint(p)), TangentMode.AutoSmooth);
        spline.Closed = false;

        _circleStart   = newStart;
        _circleEnd     = newEnd;
        _prevTotalLen  = total;
        _routeFrontier = _mainChain.FrontierIndex;
        _routeBuilt    = true;

        Debug.Log($"[TributaryLink] '{name}' route rebuilt — junction dense {junction} → frontier dense {frontier} " +
                  $"({route.Count} knots, circle window [{newStart:F3}..{newEnd:F3}]).");
    }

    float _prevTotalLen;

    void CacheFish()
    {
        _fish.Clear();
        if (_spline == null) return;
        _fish.AddRange(_spline.GetComponentsInChildren<SplineAnimate>(true));
    }

    // Keep arrivals circling the frontier light instead of running off the end of the route.
    void LateUpdate()
    {
        if (!_routeBuilt || _circleEnd <= _circleStart) return;

        foreach (var sa in _fish)
        {
            if (sa == null) continue;
            float t = sa.NormalizedTime;
            if (t >= _circleEnd)
            {
                float nt = _circleStart + (t - _circleEnd);
                if (nt < _circleStart || nt >= _circleEnd) nt = _circleStart;
                sa.NormalizedTime = nt;
            }
        }
    }

    // Same list instance every rebuild — the linkers hold that reference.
    void Rebuild(float arc)
    {
        _revealed.Clear();
        for (int i = 0; i < _regPath.Count && _cumArc[i] <= arc; i++)
            _revealed.Add(_regPath[i]);

        Vector3 frontier = PointAtArc(arc);
        if (_revealed.Count == 0 || (_revealed[_revealed.Count - 1] - frontier).sqrMagnitude > 1e-6f)
            _revealed.Add(frontier);
    }

    Vector3 PointAtArc(float arc)
    {
        if (arc <= 0f) return _regPath[0];
        for (int i = 1; i < _regPath.Count; i++)
        {
            if (_cumArc[i] >= arc)
            {
                float segLen = _cumArc[i] - _cumArc[i - 1];
                float t = segLen > 1e-5f ? (arc - _cumArc[i - 1]) / segLen : 1f;
                return Vector3.Lerp(_regPath[i - 1], _regPath[i], t);
            }
        }
        return _regPath[_regPath.Count - 1];
    }

    void OnDestroy()
    {
        if (_pool != null)
        {
            SoulFishWaveLinker.UnregisterZone(_pool);
            SoulFishMapLinker.UnregisterZone(_pool);
        }
        SoulFishWaveLinker.UnregisterZone(_revealed);
        SoulFishMapLinker.UnregisterZone(_revealed);
    }
}
