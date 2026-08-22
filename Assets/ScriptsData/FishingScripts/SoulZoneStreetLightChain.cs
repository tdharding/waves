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

    // The fish orbit radius (as a fraction of each light's painted pool) lives on
    // SoulFishController so it's one knob for the whole level — see SwimRadiusFactor there.
    // Changing it rebuilds this spline live; _builtRadiusFactor tracks what we last built with.
    float _builtRadiusFactor = -1f;

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

    // ── Debug surface (SoulFishDebugTracer) ──────────────
    public int LitCount   => _litCount;
    public int LightCount => _lights != null ? _lights.Count : 0;
    public bool IsRevealing => _revealing;
    public IReadOnlyList<SplineAnimate> DebugFish => _fish;
    public void DebugWindow(out float start, out float end)
    {
        int f = FrontierIndex;
        start = (_circleStartNorm != null && f < _circleStartNorm.Length) ? _circleStartNorm[f] : 0f;
        end   = (_circleEndNorm   != null && f < _circleEndNorm.Length)   ? _circleEndNorm[f]   : 0f;
    }

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
        var round = new List<bool>();   // true = circle knot (smooth), false = corridor knot (linear)
        _circleStartNorm = new float[_lights.Count];
        _circleEndNorm   = new float[_lights.Count];
        var csIdx = new int[_lights.Count];
        var ceIdx = new int[_lights.Count];

        void Add(Vector3 p, bool isCircle)
        {
            cum.Add(knots.Count == 0 ? 0f : cum[cum.Count - 1] + Vector3.Distance(knots[knots.Count - 1], p));
            knots.Add(p);
            round.Add(isCircle);
        }

        const float TAU = Mathf.PI * 2f;
        int n = _knotCount;

        for (int k = 0; k < _lights.Count; k++)
        {
            // Orbit inside the painted pool rather than on its rim, so the shoal reads as held by
            // the light's zone instead of skating around its edge.
            Vector3 center = _localPath[_lightDense[k]];
            float   r      = Mathf.Max(_poolRadii[k] * SoulFishController.SwimRadiusFactor, 0.05f);

            // Corridor INTO this light — deliberately stopping one node short of the light's own
            // node. Running all the way to the centre and then starting the ring at a fixed angle
            // is what produced the spike from the middle out to the rim; fish should meet the orbit
            // where the path crosses it.
            int from = (k == 0) ? 0 : _lightDense[k - 1] + 1;
            int to   = _lightDense[k] - 1;
            for (int i = from; i <= to; i++) Add(_localPath[i], false);

            // Direction of travel arriving at the light, so the entry point is the ring crossing on
            // the side the fish actually come from.
            Vector3 inDir;
            if (knots.Count > 0) inDir = center - knots[knots.Count - 1];
            else if (_localPath.Length > _lightDense[k] + 1) inDir = _localPath[_lightDense[k] + 1] - center;
            else inDir = Vector3.forward;
            inDir.y = 0f;
            if (inDir.sqrMagnitude < 1e-8f) inDir = Vector3.forward;
            inDir.Normalize();

            Vector3 entryOff = -inDir * r;                              // near side of the ring
            float   entryAng = Mathf.Atan2(entryOff.z, entryOff.x);

            // The circle starts AT that crossing and sweeps a full turn back to it. Start and end
            // are therefore the same position, so the gate's wrap is a zero-distance hop and the
            // orbit reads as one continuous loop.
            csIdx[k] = knots.Count;
            Add(center + entryOff, true);
            for (int a = 1; a <= n; a++)
            {
                float ang = entryAng + (a / (float)n) * TAU;
                Add(center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r, true);
            }
            ceIdx[k] = knots.Count - 1;

            // Once the frontier moves on, fish carry on around the ring to the crossing that faces
            // the next light and leave from there — again meeting the corridor on the rim, not the
            // centre. The following light's block adds the corridor itself.
            if (k < _lights.Count - 1)
            {
                int nextIdx = Mathf.Min(_lightDense[k] + 1, _localPath.Length - 1);
                Vector3 outDir = _localPath[nextIdx] - center;
                outDir.y = 0f;
                if (outDir.sqrMagnitude < 1e-8f) outDir = inDir;
                outDir.Normalize();

                float exitAng = Mathf.Atan2(outDir.z, outDir.x);
                float delta   = Mathf.Repeat(exitAng - entryAng, TAU);   // keep turning the same way
                int   arcN    = Mathf.Max(1, Mathf.CeilToInt(n * delta / TAU));
                for (int a = 1; a <= arcN; a++)
                {
                    float ang = entryAng + delta * a / arcN;
                    Add(center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r, true);
                }
            }
        }

        float total = Mathf.Max(cum[cum.Count - 1], 1e-4f);
        for (int k = 0; k < _lights.Count; k++)
        {
            _circleStartNorm[k] = cum[csIdx[k]] / total;
            _circleEndNorm[k]   = cum[ceIdx[k]] / total;
        }

        // Corridor knots are LINEAR: the mask paints straight bands between the dense path points
        // (distToSegment in SoulFishWaveMask.hlsl), so smoothing here would bow the swim path
        // outside the painted zone — worst at corners. Curvature belongs to the authored path and
        // already arrives baked into _localPath, so linear tangents reproduce it exactly.
        // Circle knots keep AutoSmooth so a light's ring stays round with few knots.
        var spline = _splineContainer.Spline;
        spline.Clear();
        for (int i = 0; i < knots.Count; i++)
            spline.Add(new BezierKnot((float3)knots[i]),
                       round[i] ? TangentMode.AutoSmooth : TangentMode.Linear);
        spline.Closed = false;

        _builtRadiusFactor = SoulFishController.SwimRadiusFactor;

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
        // Live retune: rebuilding is safe because the route is rebuilt in the SAME container-local
        // space it was baked in, so the fish stay attached to the level.
        if (_localPath != null && !Mathf.Approximately(_builtRadiusFactor, SoulFishController.SwimRadiusFactor))
            BuildSwimSpline();

        if (!_fishCached) { CacheFish(); return; }

        int frontier = Mathf.Clamp(_litCount - 1, 0, _lights.Count - 1);

        // Fish "arrive" once they first reach the frontier circle; only then are they held in it.
        // Reset on a frontier change so a newly opened light lets them swim on.
        if (frontier != _gateFrontier) { _arrived.Clear(); _gateFrontier = frontier; }

        float cs = _circleStartNorm[frontier];
        // The last light's circle ends exactly at 1.0 — the end of the spline. SplineAnimate's own
        // Loop wraps 1.0 -> 0 inside its update, which would fling the fish back to the lead-in
        // before this gate ever saw them. Wrapping fractionally early keeps ownership here.
        float ce = Mathf.Min(_circleEndNorm[frontier], 0.999f);
        float span = Mathf.Max(ce - cs, 1e-4f);

        foreach (var sa in _fish)
        {
            if (sa == null) continue;
            float t = sa.NormalizedTime;

            if (!_arrived.Contains(sa))
            {
                if (t >= cs) _arrived.Add(sa);   // reached the circle for the first time
                else continue;                    // still swimming in — leave it alone
            }

            if (t >= ce)
            {
                float nt = cs + Mathf.Repeat(t - ce, span);
                sa.NormalizedTime = nt;
                if (!_gateFiredLogged)
                {
                    _gateFiredLogged = true;
                    Debug.Log($"[StreetLightChain] Gate firing on '{name}' — wrapped a fish from {t:F3} back to {nt:F3} " +
                              $"(frontier #{frontier + 1}, window [{cs:F3}..{ce:F3}]).");
                }
            }
            else if (t < cs)
            {
                // Arrived but now behind the circle: SplineAnimate looped past the spline end.
                // Pull it back into the circle rather than let it re-swim the whole river.
                sa.NormalizedTime = cs;
            }
        }

        // Spread the shoal across the corridor instead of single file down its centre.
        SoulFishController.ApplyLateralSpread(_fish, _pathRadius);
    }
    bool _gateFiredLogged;
    int  _gateFrontier = -1;
    readonly HashSet<SplineAnimate> _arrived = new HashSet<SplineAnimate>();

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
