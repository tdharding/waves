using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Runtime owner of a street-light-gated soul zone. Added to the zone's shoal container by
/// LevelSpawner when the authored zone carries street lights.
///
/// The swim spline is built ONCE and never mutated: lead-in from the start node, then a circular
/// loop baked in at each light, joined by the authored corridors —
///   start → circle(light1) → corridor → circle(light2) → corridor → … → circle(lastLight).
/// The fish ride it with their own SplineAnimate speed like any other zone; nothing drags them.
///
/// Gating is a moving wrap point, not a moving spline. Each frame (LateUpdate, after SplineAnimate
/// has advanced) any fish that passes the END of the frontier light's circle wraps back to the
/// START of that same circle — so the shoal genuinely swims in circles around the frontier light
/// at its own pace. Lighting the next light advances the wrap point; the fish then spill out of the
/// circle, swim the freshly drawn corridor, and settle circling the new light.
///
/// Spaces — the lesson from the teleport bug: the swim spline is CONTAINER-LOCAL, baked from the
/// world path ONCE at Init while the container still sits in its spawn-time frame (the same moment
/// LevelSpawner.InjectSplineKnots relies on). It is never rebuilt, so the maze's intro rise/rotation
/// can't strand the fish. The MASK lists (revealed path + pools) stay in reg space (final world
/// positions) where the linkers pack them.
/// </summary>
public class SoulZoneStreetLightChain : MonoBehaviour
{
    [Tooltip("World units per second the path draws on the material when a light is lit. Fish swim at their OWN SplineAnimate speed, independent of this.")]
    public float revealSpeed = 2.5f;

    [Tooltip("Seconds the newly lit light's pool takes to bloom to full radius before the path draws.")]
    public float poolOpenSeconds = 0.75f;

    // ── wired by Init ────────────────────────────────────
    List<Vector3> _regPath;               // dense path, final/mask space
    List<Vector3> _worldPath;             // dense path, world space (for re-baking local after a move)
    List<int>     _lightDense;            // dense-path index per light, path order
    List<float>   _poolRadii;
    List<StreetLightController> _lights;  // path order; [0] starts lit. May be null at [0] for a
                                          // fish-bowl source pool (no lamp — the bowl is the source).
    float _pathRadius;                    // corridor mask radius (zone.radius)
    int   _knotCount;                     // knots per light circle
    SplineContainer _splineContainer;

    // ── state ────────────────────────────────────────────
    float[]   _cumArc;                    // cumulative arc length over the dense mask path
    Vector3[] _localPath;                 // dense path in container-local space (baked at Init)
    float[]   _circleStartNorm;           // per-light: normalized spline time at the circle's first knot
    float[]   _circleEndNorm;             // per-light: normalized spline time at the circle's last knot
    readonly List<Vector3> _revealedReg = new List<Vector3>();   // THE registered mask path list
    readonly List<List<Vector3>> _poolEntries = new List<List<Vector3>>();
    readonly List<SplineAnimate> _fish = new List<SplineAnimate>();
    bool _fishCached;
    int  _litCount;    // frontier light index = _litCount - 1
    bool _revealing;

    public List<Vector3> RevealedRegPath => _revealedReg;

    // ── Read-only view for adjoining tributaries ─────────
    // A fish-bowl tributary that has joined this river needs to know how far the river is open
    // so its shoal can swim on to the frontier light and circle there.
    public IReadOnlyList<Vector3> RegPath => _regPath;
    public int   FrontierIndex  => Mathf.Clamp(_litCount - 1, 0, _lights.Count - 1);
    public int   FrontierDense  => _lightDense[FrontierIndex];
    public float FrontierRadius => _poolRadii[FrontierIndex];

    public void Init(
        List<Vector3> regPath, List<Vector3> worldPath, List<int> lightDenseIndices,
        List<float> poolRadii, List<StreetLightController> lightsInOrder,
        float pathRadius, int knotCount, SplineContainer splineContainer)
    {
        _regPath         = regPath;
        _worldPath       = worldPath;
        _lightDense      = lightDenseIndices;
        _poolRadii       = poolRadii;
        _lights          = lightsInOrder;
        _pathRadius      = pathRadius;
        _knotCount       = Mathf.Max(6, knotCount);
        _splineContainer = splineContainer;

        _cumArc = new float[_regPath.Count];
        for (int i = 1; i < _regPath.Count; i++)
            _cumArc[i] = _cumArc[i - 1] + Vector3.Distance(_regPath[i - 1], _regPath[i]);

        // Bake the swim path container-local NOW, while the container is still in its spawn-time
        // frame — the one moment worldPath and the container transform agree.
        _localPath = new Vector3[worldPath.Count];
        for (int i = 0; i < worldPath.Count; i++)
            _localPath[i] = _splineContainer.transform.InverseTransformPoint(worldPath[i]);

        BuildSwimSpline();

        for (int i = 0; i < _lights.Count; i++)
        {
            if (_lights[i] == null) continue; // source pool (fish bowl) — no lamp to wire
            _lights[i].chain      = this;
            _lights[i].orderIndex = i;
        }

        // Initial mask state: the authored lead-in from the START node (no light needed there)
        // up to light #1, plus light #1's pool. Register the path entry FIRST so the later dedupe
        // (shoal.InitZone with the same list) keeps this radius.
        // A null light[0] is a fish-bowl source: it's logically lit (frontier 0) with just its pool
        // and an empty lead-in, so nothing draws until the first real light is fed.
        _litCount = 1;
        _lights[0]?.SetLit(true);

        for (int i = 0; i <= _lightDense[0]; i++)
            _revealedReg.Add(_regPath[i]);
        SoulFishWaveLinker.RegisterZone(_revealedReg, false, _pathRadius);
        SoulFishMapLinker.RegisterZone(_revealedReg, false, _pathRadius);

        RegisterPool(0, _poolRadii[0]);
    }

    // Builds the static swim spline: lead-in + a circle at each light + corridors between them,
    // recording each circle's normalized start/end so the gate can wrap fish inside it.
    void BuildSwimSpline()
    {
        var knots = new List<Vector3>();
        var cum   = new List<float>();
        _circleStartNorm = new float[_lights.Count];
        _circleEndNorm   = new float[_lights.Count];
        var csIdx = new int[_lights.Count];
        var ceIdx = new int[_lights.Count];

        void Add(Vector3 p)
        {
            cum.Add(knots.Count == 0 ? 0f : cum[cum.Count - 1] + Vector3.Distance(knots[knots.Count - 1], p));
            knots.Add(p);
        }

        // Lead-in: start node up to light #1.
        for (int i = 0; i <= _lightDense[0]; i++) Add(_localPath[i]);

        int n = _knotCount;
        for (int k = 0; k < _lights.Count; k++)
        {
            Vector3 center = _localPath[_lightDense[k]];
            float   r      = Mathf.Max(_poolRadii[k], 0.05f);

            csIdx[k] = knots.Count;
            for (int a = 0; a < n; a++)
            {
                float ang = (a / (float)n) * Mathf.PI * 2f;
                Add(center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r);
            }
            ceIdx[k] = knots.Count - 1;

            // Corridor to the next light.
            if (k < _lights.Count - 1)
                for (int i = _lightDense[k] + 1; i <= _lightDense[k + 1]; i++) Add(_localPath[i]);
        }

        float total = Mathf.Max(cum[cum.Count - 1], 1e-4f);
        for (int k = 0; k < _lights.Count; k++)
        {
            _circleStartNorm[k] = cum[csIdx[k]] / total;
            _circleEndNorm[k]   = cum[ceIdx[k]] / total;
        }

        var spline = _splineContainer.Spline;
        spline.Clear();
        foreach (var p in knots)
            spline.Add(new BezierKnot((float3)p), TangentMode.AutoSmooth);
        spline.Closed = false;

        var win = new System.Text.StringBuilder();
        for (int k = 0; k < _lights.Count; k++)
            win.Append($" light{k}=[{_circleStartNorm[k]:F3}..{_circleEndNorm[k]:F3}]");
        Debug.Log($"[StreetLightChain] Built swim spline on '{name}': {knots.Count} knots, " +
                  $"{_lights.Count} light(s), denseIdx=[{string.Join(",", _lightDense)}]. Circle windows:{win}");
        if (_lights.Count == 1)
            Debug.LogWarning("[StreetLightChain] Only ONE street light on this zone — there is nothing to gate past it, " +
                             "so fish correctly swim the lead-in and circle that light. Add a 2nd light to see gating.");
    }

    // Re-bakes the container-local swim path from the stored world path against the container's
    // CURRENT transform, then rebuilds the swim spline. Used when the container is moved after Init
    // — e.g. a fish-bowl tributary whose shoal container drops from aloft to the water on smash.
    // The mask (_regPath, world XZ) is unaffected by a straight-down drop, so only the swim spline
    // is re-baked. Fish keep their normalized times (the spline shape is unchanged, just relocated).
    public void RebakeAfterMove()
    {
        if (_worldPath == null || _splineContainer == null) return;
        for (int i = 0; i < _worldPath.Count && i < _localPath.Length; i++)
            _localPath[i] = _splineContainer.transform.InverseTransformPoint(_worldPath[i]);
        BuildSwimSpline();
        Debug.Log($"[StreetLightChain] Re-baked swim spline on '{name}' after a container move (bowl landing).");
    }

    // Caches the fish once they've been spawned (SpawnFish runs after Init, same frame), sets them
    // looping, and files them along the lead-in so they swim in FROM THE START on load.
    //
    // SoulShoalController.SpawnFish spreads fish across the whole spline via SplineAnimate.StartOffset
    // (0→1). That offset is added on top of NormalizedTime at evaluation, so it must be zeroed here
    // or the fish start scattered past the gate instead of at the beginning. Position is then driven
    // purely by NormalizedTime.
    void CacheFish()
    {
        var found = _splineContainer.GetComponentsInChildren<SplineAnimate>(true);
        if (found.Length == 0) return;   // not spawned yet

        float cs = _circleStartNorm[0];
        for (int i = 0; i < found.Length; i++)
        {
            var sa = found[i];
            sa.Loop        = SplineAnimate.LoopMode.Loop;
            sa.StartOffset = 0f;   // neutralize the shoal's spread — we own the position here
            // Stagger along the lead-in so they stream in from the start node, not stack on it.
            float f = found.Length > 1 ? (float)i / found.Length : 0f;
            sa.NormalizedTime = Mathf.Lerp(0f, cs, f);
            _fish.Add(sa);
        }
        _fishCached = true;
        Debug.Log($"[StreetLightChain] Cached {_fish.Count} fish on '{name}'. Gate active — frontier light #1, " +
                  $"circle window [{_circleStartNorm[0]:F3}..{_circleEndNorm[0]:F3}], fish filed into lead-in [0..{cs:F3}].");
    }

    // The gate. After SplineAnimate has advanced each fish this frame, wrap any that passed the
    // frontier circle's end back to its start — so they circle the frontier light at their own
    // speed. When the frontier advances (a light is lit) the window jumps forward and the fish
    // naturally flow out of the old circle and on to the new one.
    void LateUpdate()
    {
        if (!_fishCached) { CacheFish(); return; }

        int   frontier = Mathf.Clamp(_litCount - 1, 0, _lights.Count - 1);
        float cs = _circleStartNorm[frontier];
        float ce = _circleEndNorm[frontier];

        foreach (var sa in _fish)
        {
            if (sa == null) continue;
            float t = sa.NormalizedTime;
            if (t >= ce)
            {
                float nt = cs + (t - ce);   // carry the small per-frame overflow
                if (nt < cs || nt >= ce) nt = cs;
                sa.NormalizedTime = nt;
                if (!_gateFiredLogged)
                {
                    _gateFiredLogged = true;
                    Debug.Log($"[StreetLightChain] Gate firing on '{name}' — wrapped a fish from {t:F3} back to {nt:F3} " +
                              $"(frontier #{frontier + 1}, window [{cs:F3}..{ce:F3}]).");
                }
            }
        }
    }
    bool _gateFiredLogged;

    /// <summary>Only the next unlit light accepts a soul, and never while a sequence runs.</summary>
    public bool CanFeed(StreetLightController light) =>
        !_revealing && light != null && light.orderIndex == _litCount;

    public void OnStreetLightFed(StreetLightController light)
    {
        if (light.orderIndex != _litCount)
        {
            Debug.LogError($"[StreetLightChain] Fed light #{light.orderIndex + 1} but expected #{_litCount + 1} — ignoring.");
            return;
        }
        StartCoroutine(RevealRoutine(light.orderIndex));
    }

    IEnumerator RevealRoutine(int targetLight)
    {
        _revealing = true;

        // 1. The new light's pool opens — radius blooms out around the lamp. The frontier hasn't
        //    moved yet, so the fish keep circling the previous light through this and step 2.
        var pool = RegisterPool(targetLight, 0.02f);
        float open = 0f;
        while (open < poolOpenSeconds)
        {
            open += Time.deltaTime;
            float r = Mathf.Lerp(0.02f, _poolRadii[targetLight],
                                 Mathf.SmoothStep(0f, 1f, poolOpenSeconds > 0f ? open / poolOpenSeconds : 1f));
            SoulFishWaveLinker.UpdateZoneRadius(pool, r);
            SoulFishMapLinker.UpdateZoneRadius(pool, r);
            SoulFishMapLinker.Instance?.BakePositionsOnce();
            yield return null;
        }
        SoulFishWaveLinker.UpdateZoneRadius(pool, _poolRadii[targetLight]);
        SoulFishMapLinker.UpdateZoneRadius(pool, _poolRadii[targetLight]);

        // 2. The path draws on the material from the previous light to the new one.
        float fromArc = _cumArc[_lightDense[targetLight - 1]];
        float toArc   = _cumArc[_lightDense[targetLight]];
        float arc = fromArc;
        while (arc < toArc)
        {
            arc = Mathf.Min(arc + revealSpeed * Time.deltaTime, toArc);
            RebuildRevealedPath(arc);
            SoulFishMapLinker.Instance?.BakePositionsOnce();
            yield return null;
        }
        RebuildRevealedPath(toArc);
        SoulFishMapLinker.Instance?.BakePositionsOnce();

        // 3. Open the gate — the frontier advances, so the fish spill out of the old circle and
        //    swim the freshly drawn corridor to the new light at their own speed.
        _litCount++;
        _revealing = false;
        Debug.Log($"[StreetLightChain] Sequence complete — {_litCount}/{_lights.Count} lights lit on '{name}'.");
    }

    // Revealed mask path = dense points from the path START (node 0) up to the frontier arc,
    // frontier interpolated exactly. Clear+rebuild keeps the SAME list instance — that reference
    // is what the linkers (and SoulShoalController's fishing-distance check) hold.
    void RebuildRevealedPath(float arc)
    {
        _revealedReg.Clear();
        for (int i = 0; i < _regPath.Count && _cumArc[i] <= arc; i++)
            _revealedReg.Add(_regPath[i]);
        Vector3 frontier = PointAtArc(_regPath, arc);
        if (_revealedReg.Count == 0 || (_revealedReg[_revealedReg.Count - 1] - frontier).sqrMagnitude > 1e-6f)
            _revealedReg.Add(frontier);
    }

    Vector3 PointAtArc(IReadOnlyList<Vector3> path, float arc)
    {
        if (arc <= 0f) return path[0];
        for (int i = 1; i < path.Count; i++)
        {
            if (_cumArc[i] >= arc)
            {
                float segLen = _cumArc[i] - _cumArc[i - 1];
                float t = segLen > 1e-5f ? (arc - _cumArc[i - 1]) / segLen : 1f;
                return Vector3.Lerp(path[i - 1], path[i], t);
            }
        }
        return path[path.Count - 1];
    }

    List<Vector3> RegisterPool(int lightIdx, float radius)
    {
        var pool = new List<Vector3> { _regPath[_lightDense[lightIdx]] };
        _poolEntries.Add(pool);
        SoulFishWaveLinker.RegisterZone(pool, false, radius);
        SoulFishMapLinker.RegisterZone(pool, false, radius);
        return pool;
    }

    void OnDestroy()
    {
        // The revealed-path entry is unregistered by SoulShoalController (it holds the same list);
        // the pool entries are ours to clean up.
        foreach (var pool in _poolEntries)
        {
            SoulFishWaveLinker.UnregisterZone(pool);
            SoulFishMapLinker.UnregisterZone(pool);
        }
    }
}
