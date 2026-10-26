using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Moves souls along their authored routes across the level-select map and publishes them to the
/// water shader.
///
/// It owns NO state of its own. Where each soul is lives in SoulJourneyData; this reads that,
/// advances it, and each frame turns whatever is currently on the river into the globals that
/// TravellingSoulFish.hlsl draws. That is what makes the shader's 32-fish ceiling safe: it caps
/// what is DRAWN, never what exists.
///
/// The globals are re-pushed every frame on purpose. They are bare $Globals declared in the .hlsl,
/// so they live nowhere on disk — reimporting a shader (saving any shadergraph, or any file that
/// triggers a reload) wipes them with no silent fallback. Pushing every frame is what makes it
/// heal itself on the next one, the same reasoning as the soul-fish masks and instanced lights.
///
/// Route shape comes from LevelSelectDesignerData.BuildSoulRouteNodeIds — the same walk the
/// designer draws — traced down the middle of the generated rivers by SoulRouteCurve, so the fish
/// swim the band you authored, centred in the water and round the pools.
///
/// It also draws the SOUL ZONE along each route (TravellingSoulZone.hlsl): once a route's souls
/// are released the zone draws out from the origin at the water preset's Draw Speed, and the fish
/// follow behind its front, never passing it.
/// </summary>
public class TravellingSoulFishController : MonoBehaviour
{
    // Matches TRAVELFISH_MAX in TravellingSoulFish.hlsl.
    private const int MaxDrawnFish = 32;

    [Header("Routes")]
    [Tooltip("The designer asset holding the authored soul routes. Same asset the Level Select Designer edits.")]
    [SerializeField] private LevelSelectDesignerData designerData;

    [Header("Movement")]
    [Tooltip("World units per second souls travel along a route.")]
    [SerializeField] private float swimSpeed = 1.5f;

    [Tooltip("Closest a soul swims behind the one ahead of it on the same route, world units. Souls " +
             "released together all start on the same spot; this is what lets them stream out one " +
             "after another instead of travelling as one fish.")]
    [SerializeField] private float soulSpacing = 0.5f;

    [Tooltip("World units per second the soul zone draws out along a route after its souls are " +
             "released. The fish follow behind its front and never pass it.")]
    [Min(0.01f)]
    [SerializeField] private float zoneDrawSpeed = 1.2f;

    /// <summary>The zone's draw speed, for the Level Select Designer's route preview.</summary>
    public float ZoneDrawSpeed => zoneDrawSpeed;

    [Header("Appearance")]
    [Tooltip("The fish image stamped on the water. Fed to _TravelSoulFishTex.")]
    [SerializeField] private Texture2D fishTexture;

    [Tooltip("World-space width/height of one fish.")]
    [SerializeField] private float fishSize = 0.6f;

    [Tooltip("Master multiplier on the drawn result. 0 hides them without stopping their travel.")]
    [Range(0f, 1f)]
    [SerializeField] private float strength = 1f;

    [Header("In The Soul Juice Tank")]
    [Tooltip("World units per second souls swim round the bowl of the soul juice tank they are waiting in.")]
    [SerializeField] private float bowlSwimSpeed = 0.25f;

    [Tooltip("World-space width/height of one fish in a tower's bowl — the bowl is far smaller " +
             "than the river. 0 = the same size as on the river.")]
    [SerializeField] private float bowlFishSize = 0.15f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    // ── Shader globals ───────────────────────────────────
    private static readonly int PositionsID = Shader.PropertyToID("_TravelSoulFishPositions");
    private static readonly int CountID     = Shader.PropertyToID("_TravelSoulFishCount");
    private static readonly int SizeID      = Shader.PropertyToID("_TravelSoulFishSize");
    private static readonly int StrengthID  = Shader.PropertyToID("_TravelSoulFishStrength");
    private static readonly int TexID       = Shader.PropertyToID("_TravelSoulFishTex");

    private static readonly Vector4[] PositionBuffer = new Vector4[MaxDrawnFish];

    // Matches TRAVELZONE_MAX_POINTS in TravellingSoulZone.hlsl. Always pushed at full length —
    // a global array locks its size on the first push.
    private const int MaxZonePoints = 128;

    // Step, world units, when walking a route looking for the entry trigger a fish swims in by.
    private const float MinZoneSpacing = 0.25f;

    // Closest the zone's points ever keep to the route's line, world units: the line is thinned
    // to points no further off it than this, doubled until every route fits in MaxZonePoints.
    private const float MinZoneTolerance = 0.01f;

    private static readonly int ZonePositionsID = Shader.PropertyToID("_TravelSoulZonePositions");
    private static readonly int ZoneCountID     = Shader.PropertyToID("_TravelSoulZoneCount");

    private static readonly Vector4[] ZoneBuffer = new Vector4[MaxZonePoints];

    // How far along each route its zone has drawn out, world units. Not saved: on load a route
    // that is already open is drawn out to its open end at once, and only what opens while the
    // map is up draws out as you watch.
    private readonly Dictionary<string, float> _zoneHeads = new Dictionary<string, float>();
    private float _zoneTolerance = MinZoneTolerance;
    private bool  _zoneOverflowWarned;

    // The Corner Smoothing the zones were last packed with — repacked when the tuner changes it.
    private float _packedCornerSmoothing = -1f;

    // World units between the evenly resampled points the corners are eased over.
    private const float CornerStep = 0.1f;

    // ── Cached route shapes ──────────────────────────────
    // Rebuilt only when the asset changes, since a route's shape is authored, not simulated.
    private class RouteShape
    {
        public List<Vector3> polyline = new List<Vector3>();   // world positions, in travel order
        public List<int>     pointIndex = new List<int>();     // polyline index of each authored point
        public List<float>   cumArc   = new List<float>();     // arc length to each polyline vertex

        // The line the zone is drawn along: the polyline evenly resampled with its corners eased
        // by the tuner's Corner Smoothing, each point keeping the arc length it had on the polyline.
        public List<Vector3> zoneLine    = new List<Vector3>();
        public List<float>   zoneLineArc = new List<float>();

        // That line thinned to the zone's packing, with the arc length to each point.
        public List<Vector3> zonePoints = new List<Vector3>();
        public List<float>   zoneArc    = new List<float>();
        public float         bandWidth;

        public float Length => cumArc.Count > 0 ? cumArc[cumArc.Count - 1] : 0f;
    }

    private readonly Dictionary<string, RouteShape> _shapes = new Dictionary<string, RouteShape>();

    // Positions are logged on a timer, not every frame — a 1 Hz trace is enough to watch a fish
    // move and does not bury everything else in the console.
    private float _nextPositionLog;

    // ─────────────────────────────────────────────
    // UNITY
    // ─────────────────────────────────────────────

    private void Start()
    {
        if (designerData == null)
        {
            Debug.LogWarning("[TravelFish] No designerData assigned — no routes to swim.", this);
            return;
        }

        // Bowls and Soul Fish Areas first: their zone points are what the routes are packed around.
        FindTowerBowls();
        FindSoulFishAreas();
        BuildRouteShapes();
        FindEntryTriggers();
        SeedOriginPools();
        ReportHeldOrigins();
        InitBowlStates();

        // Before departures are resolved, so a stretch those open now draws out as you watch.
        InitZoneHeads();
        ResolveDepartures();

        ReportStartingPoints();
    }

    private void OnDisable()
    {
        // Advance() deliberately does not write per frame; commit what it left behind.
        SoulJourneyData.Flush();
    }

    private void LateUpdate()
    {
        if (designerData == null) return;

        AdvanceBowls();
        AdvanceZoneHeads();
        AdvanceTravellingSouls();
        PublishGlobals();
        PublishZone();
        SoulPathGlow.SetFronts(_zoneHeads);
    }

    // ─────────────────────────────────────────────
    // ROUTE SHAPES
    // ─────────────────────────────────────────────

    private void BuildRouteShapes()
    {
        _shapes.Clear();

        // The rivers as generated — every run's curve, sampled once, and the pools they meet.
        var runs  = SoulRouteCurve.SampleRuns(FindObjectsOfType<RiverRunMesh>());
        var pools = FindObjectsOfType<RiverPoolMesh>();

        foreach (var route in designerData.soulRoutes)
        {
            if (route == null || string.IsNullOrEmpty(route.routeId)) continue;

            var pointIds = new List<int>();
            var ids      = designerData.BuildSoulRouteNodeIds(route, pointIds);
            var problems = new List<string>();
            var shape    = new RouteShape { bandWidth = Mathf.Max(route.bandWidth, 0.0001f) };

            shape.polyline = SoulRouteCurve.Build(designerData, ids, runs, pools,
                                                  out List<int> nodeVertex, problems);

            // Where each authored point lands in the line — souls travel point to point, and the
            // river between is just the shape of the way. BuildSoulRouteNodeIds says where each
            // point sits in the ids; nodeVertex turns that into the line.
            foreach (int idAt in pointIds)
            {
                int idIndex = Mathf.Min(idAt, nodeVertex.Count - 1);
                shape.pointIndex.Add(idIndex >= 0 ? nodeVertex[idIndex] : 0);
            }

            float arc = 0f;
            shape.cumArc.Add(0f);
            for (int i = 1; i < shape.polyline.Count; i++)
            {
                arc += Vector3.Distance(shape.polyline[i - 1], shape.polyline[i]);
                shape.cumArc.Add(arc);
            }

            _shapes[route.routeId] = shape;

            foreach (string problem in problems)
                Debug.LogWarning($"[TravelFish] Route '{route.displayName}': {problem}", this);

            if (verboseLogging)
                Debug.Log($"[TravelFish] Route '{route.displayName}' — {shape.polyline.Count} vertices, " +
                          $"{shape.pointIndex.Count} points, {arc:F1}m long.");
        }

        PackZones(ZoneLook().cornerSmoothing);

        Debug.Log($"[TravelFish] Built {_shapes.Count} route shape(s) from {runs.Count} generated run(s).");
    }

    /// <summary>
    /// Thins every route's line to the points the zone shader takes: close together where the line
    /// bends (round a pool's ring, a tight river bend), far apart down the straights. One tolerance
    /// for all of them, as tight as lets every route drawn out in full still fit the shader's point
    /// limit — so a zone never changes shape as more routes open. The front of a zone drawing out
    /// is one extra point per route, which is what the two spare per route are for; each tower
    /// bowl's disc takes one more, and so does each open-bowl Soul Fish Area's; one with an island
    /// takes its whole ring.
    /// </summary>
    private void PackZones(float cornerSmoothing)
    {
        _packedCornerSmoothing = cornerSmoothing;
        foreach (var shape in _shapes.Values) EaseCorners(shape, cornerSmoothing);

        float total = 0f;
        foreach (var shape in _shapes.Values) total += shape.Length;

        int areaPoints = 0;
        foreach (var area in _areas) areaPoints += area.ZonePointCount;

        int budget = Mathf.Max(1, MaxZonePoints - 2 * _shapes.Count - _bowls.Count - areaPoints);
        int used   = 0;
        for (_zoneTolerance = MinZoneTolerance; ; _zoneTolerance *= 2f)
        {
            used = 0;
            foreach (var shape in _shapes.Values)
            {
                SoulRouteCurve.Simplify(shape.zoneLine, shape.zoneLineArc, _zoneTolerance,
                                        shape.zonePoints, shape.zoneArc);
                used += shape.zonePoints.Count;
            }
            if (used <= budget || _zoneTolerance > total) break;
        }

        Debug.Log($"[TravelFish] Soul zones packed into {used} of {budget} points over {total:F1}m of route, " +
                  $"never more than {_zoneTolerance * 100f:0.#}cm off the line, corners eased over " +
                  $"{cornerSmoothing:0.##}m.");
    }

    /// <summary>
    /// The line a route's zone is drawn along: its polyline resampled evenly, every point moved to
    /// the average of the line within <paramref name="radius"/> of it along the route — nearer
    /// points counting more — so a sharp turn becomes a smooth curve while a straight stays where
    /// it is. Near either end the reach shrinks to the distance left, so the ends never move. Each
    /// point keeps its arc length on the polyline, so the zone's front, the fish and the glow still
    /// agree on how far along the route anything is.
    /// </summary>
    private static void EaseCorners(RouteShape shape, float radius)
    {
        shape.zoneLine.Clear();
        shape.zoneLineArc.Clear();
        if (shape.polyline.Count < 2 || radius <= 0f || shape.Length <= 0f)
        {
            shape.zoneLine.AddRange(shape.polyline);
            shape.zoneLineArc.AddRange(shape.cumArc);
            return;
        }

        var even    = new List<Vector3>();
        var evenArc = new List<float>();
        SoulRouteCurve.Resample(shape.polyline, shape.cumArc, CornerStep, even, evenArc);

        int   n      = even.Count;
        float length = evenArc[n - 1];
        float step   = length / Mathf.Max(1, n - 1);
        for (int i = 0; i < n; i++)
        {
            float s     = evenArc[i];
            int   reach = Mathf.FloorToInt(Mathf.Min(radius, Mathf.Min(s, length - s)) / step);

            Vector3 sum   = Vector3.zero;
            float   total = 0f;
            for (int j = -reach; j <= reach; j++)
            {
                float w = reach + 1 - Mathf.Abs(j);
                sum   += even[i + j] * w;
                total += w;
            }
            shape.zoneLine.Add(sum / total);
            shape.zoneLineArc.Add(s);
        }
    }

    private RiverSoulZoneSettings ZoneLook()
        => RiverSoulZoneSettings.Resolve(designerData.waterPreset != null ? designerData.waterPreset.soulZone : null);

    // ─────────────────────────────────────────────
    // ORIGIN POOLS
    // ─────────────────────────────────────────────

    /// <summary>
    /// Gives every soul authored into a route's origin pool a starting place, the first time it is
    /// ever seen. Only souls with NO record are touched — one that has already set off, been caught
    /// or arrived somewhere keeps whatever it is doing, so this is safe to run on every load.
    /// </summary>
    private void SeedOriginPools()
    {
        int seeded = 0;

        foreach (var route in designerData.soulRoutes)
        {
            if (route?.souls == null || !RouteCarriesFish(route)) continue;

            foreach (var soul in route.souls)
            {
                if (soul == null || soul.soulDataIdentity <= 0) continue;
                if (SoulJourneyData.HasRecord(soul.soulDataIdentity)) continue;

                SoulJourneyData.PlaceInOriginPool(soul.soulDataIdentity, route.routeId);
                seeded++;
            }
        }

        if (seeded > 0)
            Debug.Log($"[TravelFish] Seeded {seeded} soul(s) into their origin pools.");
    }

    /// <summary>
    /// A soul juice tank carries no fish: its route is only the soul juice path, drawn out when the
    /// tank is released. Fish come from elsewhere and join it.
    /// </summary>
    private bool RouteCarriesFish(LevelSelectDesignerData.SoulRoute route)
        => route != null && !designerData.RouteStartsAtSoulJuiceTank(route);

    /// <summary>
    /// A soul juice tank's route stays closed until E at the tank (<see cref="ReleaseTower"/>).
    /// A route that does not begin at a tank can't start at all — said once here, so a route that
    /// has gone quiet is explained.
    /// </summary>
    private void ReportHeldOrigins()
    {
        foreach (var route in designerData.soulRoutes)
        {
            if (route == null || string.IsNullOrEmpty(route.routeId)) continue;

            int waiting = HeldSouls(route).Count;

            if (designerData.RouteStartsAtSoulJuiceTank(route))
            {
                bool open = SoulJourneyData.OpenLegOf(route.routeId) >= 0;
                if (!_bowls.ContainsKey(route.originNodeId))
                    Debug.LogWarning($"[TravelFish] Route '{route.displayName}' starts at a soul juice tank, but no " +
                                     "generated tank for it is in the scene — Generate in the Level Select " +
                                     "Designer.", this);
                else
                    Debug.Log($"[TravelFish] Route '{route.displayName}': soul juice path " +
                              (open ? "already released." : "waiting in its tank."));
            }
            else if (waiting > 0)
            {
                Debug.LogWarning($"[TravelFish] Route '{route.displayName}' doesn't begin at a soul juice tank, so " +
                                 $"it can't start — its {waiting} soul(s) stay where they are. Pick an origin " +
                                 "tower for it in the Level Select Designer.", this);
            }
        }
    }

    /// <summary>
    /// Opens a soul juice tank's route to its last point, so its band draws out from the tank.
    /// Called once the tank has drained and the pool round it has filled. True when it opened.
    ///
    /// Opened all the way: nothing else opens a route yet. When openness becomes real player
    /// progress, that line is the one to change.
    /// </summary>
    public bool ReleaseTower(string routeId)
    {
        var route = designerData != null ? designerData.soulRoutes.Find(r => r != null && r.routeId == routeId) : null;
        if (route == null)
        {
            Debug.LogWarning($"[TravelFish] ReleaseTower: no route '{routeId}'.", this);
            return false;
        }
        if (!designerData.RouteStartsAtSoulJuiceTank(route))
        {
            Debug.LogWarning($"[TravelFish] ReleaseTower: route '{route.displayName}' doesn't begin at a soul " +
                             "juice tank — nothing to release.", this);
            return false;
        }
        if (route.points.Count == 0)
        {
            Debug.LogWarning($"[TravelFish] ReleaseTower: route '{route.displayName}' has no destinations to draw to.", this);
            return false;
        }

        SoulJourneyData.OpenRouteTo(route.routeId, route.points.Count - 1);
        Debug.Log($"[TravelFish] Soul juice path '{route.displayName}' released from its tank.");
        return true;
    }

    /// <summary>
    /// What pressing E at the soul juice tank standing in <paramref name="poolNodeId"/> does:
    /// the bowl drains, the pool round the tank fills, and then every route beginning there is
    /// drawn out (<see cref="AdvanceBowls"/>). True when the release started.
    /// </summary>
    public bool ReleaseSoulJuiceTank(string poolNodeId)
    {
        if (!CanRelease(poolNodeId)) return false;

        // No tank in the scene to drain — straight out.
        if (!_bowlStates.TryGetValue(poolNodeId, out var state)) return ReleaseRoutesAt(poolNodeId) > 0;

        state.phase = BowlPhase.Draining;
        Debug.Log($"[TravelFish] Soul juice tank on '{poolNodeId}' draining — its path draws out once the " +
                  "pool round it has filled.");
        return true;
    }

    /// <summary>Whether E at the tank on <paramref name="poolNodeId"/> has anything to do.</summary>
    public bool CanRelease(string poolNodeId)
    {
        if (designerData == null || string.IsNullOrEmpty(poolNodeId)) return false;
        if (!TankHasRoutes(poolNodeId) || TowerReleased(poolNodeId)) return false;
        return !_bowlStates.TryGetValue(poolNodeId, out var state) || state.phase == BowlPhase.Holding;
    }

    /// <summary>Opens every route that begins at <paramref name="poolNodeId"/>. Returns how many opened.</summary>
    private int ReleaseRoutesAt(string poolNodeId)
    {
        int opened = 0;
        foreach (var route in designerData.soulRoutes)
            if (route != null && route.originNodeId == poolNodeId && route.points.Count > 0 &&
                SoulJourneyData.OpenLegOf(route.routeId) < 0 && ReleaseTower(route.routeId))
                opened++;

        return opened;
    }

    /// <summary>Whether a route with somewhere to go begins at the tank on <paramref name="poolNodeId"/>.</summary>
    private bool TankHasRoutes(string poolNodeId)
        => designerData.soulRoutes.Exists(r => r != null && r.originNodeId == poolNodeId && r.points.Count > 0 &&
                                               designerData.RouteStartsAtSoulJuiceTank(r));

    /// <summary>
    /// The route's souls still waiting at its origin. Only the pool: a soul already travelling,
    /// arrived or caught keeps what it is doing, so nothing here can drag one backwards. A soul
    /// juice tank's route has none — it carries no fish.
    /// </summary>
    private List<int> HeldSouls(LevelSelectDesignerData.SoulRoute route)
    {
        _held.Clear();
        if (route?.souls == null || !RouteCarriesFish(route)) return _held;

        foreach (var soul in route.souls)
        {
            if (soul == null || soul.soulDataIdentity <= 0) continue;
            if (!SoulJourneyData.HasRecord(soul.soulDataIdentity)) continue;
            if (SoulJourneyData.Where(soul.soulDataIdentity).place != SoulPlace.OriginPool) continue;
            _held.Add(soul.soulDataIdentity);
        }

        return _held;
    }

    private readonly List<int> _held = new List<int>();

    // ─────────────────────────────────────────────
    // SOUL JUICE TANKS
    // ─────────────────────────────────────────────

    // Every generated soul juice tank in the scene, by the pool node it stands on.
    private readonly Dictionary<string, SoulJuiceTankBowl> _bowls = new Dictionary<string, SoulJuiceTankBowl>();

    private void FindTowerBowls()
    {
        _bowls.Clear();
        foreach (var bowl in FindObjectsOfType<SoulJuiceTankBowl>())
            if (bowl != null && !string.IsNullOrEmpty(bowl.poolNodeId))
                _bowls[bowl.poolNodeId] = bowl;
    }

    // Releasing a tank: Holding (the full bowl of soul juice) → Draining (the juice lowers) →
    // Filling (gone down the hole; the zone grows across the pool round the tank) → Done (its
    // routes drawn out; the pool stays filled).
    private enum BowlPhase { Holding, Draining, Filling, Done }

    private class BowlState
    {
        public BowlPhase phase;
        public float     fill;       // the pool fill's radius while Filling, world units
        public float     poolRadius;   // the pool it fills, world units — looked up once
        public float     islandRadius; // the island in the middle of it, world units

        // Pool Fill Radius as a proportion of the water, from the island's edge out to the pool's.
        public float FillRadius(float proportion)
            => islandRadius + (poolRadius - islandRadius) * Mathf.Clamp01(proportion);
    }

    private readonly Dictionary<string, BowlState> _bowlStates = new Dictionary<string, BowlState>();

    /// <summary>
    /// The phase isn't saved — read off its routes instead: a tank whose routes are all open was
    /// released before, so it loads drained, with the pool round it filled.
    /// </summary>
    private void InitBowlStates()
    {
        _bowlStates.Clear();
        foreach (var pair in _bowls)
        {
            bool released = TowerReleased(pair.Key);
            _bowlStates[pair.Key] = new BowlState { phase = released ? BowlPhase.Done : BowlPhase.Holding };
            var st = _bowlStates[pair.Key];
            st.poolRadius = PoolFillRadius(pair.Value, out st.islandRadius);
            if (released && pair.Value != null) pair.Value.Empty();
        }
    }

    private bool TowerReleased(string poolNodeId)
    {
        bool any = false;
        foreach (var route in designerData.soulRoutes)
        {
            if (route == null || route.originNodeId != poolNodeId || route.points.Count == 0 ||
                !designerData.RouteStartsAtSoulJuiceTank(route)) continue;
            any = true;
            if (SoulJourneyData.OpenLegOf(route.routeId) < 0) return false;
        }
        return any;
    }

    /// <summary>Drains each released tower's bowl, then fills the pool round it, then lets its souls out.</summary>
    private void AdvanceBowls()
    {
        var look = RiverSoulZoneSettings.Resolve(designerData.waterPreset != null ? designerData.waterPreset.soulZone : null);

        foreach (var pair in _bowlStates)
        {
            var state = pair.Value;
            _bowls.TryGetValue(pair.Key, out var bowl);

            if (state.phase == BowlPhase.Draining)
            {
                if (bowl != null) bowl.Lower(Mathf.Max(look.drainSpeed, 0.001f) * Time.deltaTime);
                if (bowl == null || bowl.Drained)
                {
                    state.phase = BowlPhase.Filling;
                    state.fill  = state.islandRadius;   // nothing to fill on the island itself
                }
            }
            else if (state.phase == BowlPhase.Filling)
            {
                float target = state.FillRadius(look.poolFillRadius);
                state.fill = Mathf.MoveTowards(state.fill, target, Mathf.Max(look.fillSpeed, 0.01f) * Time.deltaTime);
                if (state.fill >= target)
                {
                    state.phase = BowlPhase.Done;
                    ReleaseRoutesAt(pair.Key);
                }
            }
        }
    }

    /// <summary>
    /// The radius of the pool the tower stands in, and of its island, world units — the generated
    /// pool nearest the tower that reaches it. The tuner's Pool Fill Radius is a proportion of
    /// the water between the two.
    /// </summary>
    private float PoolFillRadius(SoulJuiceTankBowl bowl, out float island)
    {
        island = 0f;
        if (bowl == null) return 0f;

        Vector3 c    = bowl.transform.position;
        float   best = float.MaxValue, radius = 0f;
        foreach (var pool in FindObjectsOfType<RiverPoolMesh>())
        {
            Vector3 p    = pool.transform.position;
            float   r    = pool.poolRadius * Mathf.Abs(pool.transform.lossyScale.x);
            float   dist = new Vector2(p.x - c.x, p.z - c.z).magnitude;
            if (dist <= r && dist < best)
            {
                best   = dist;
                radius = r;
                island = Mathf.Min(r, pool.islandRadius * Mathf.Abs(pool.transform.lossyScale.x));
            }
        }

        if (radius <= 0f)
            Debug.LogWarning($"[TravelFish] No generated pool round the soul juice tank on '{bowl.poolNodeId}' — " +
                             "nothing for the zone to fill.", bowl);
        return radius;
    }

    private bool SoulsInBowl(string poolNodeId)
        => !_bowlStates.TryGetValue(poolNodeId, out var state) ||
           state.phase == BowlPhase.Holding || state.phase == BowlPhase.Draining;

    /// <summary>
    /// A soul let out of a tower that has not set off yet — held at the start of its route behind
    /// the one ahead. It is drawn swimming round the pool the tower stands in, come up from the hole.
    /// </summary>
    private bool TryPoolPose(SoulJourneyEntry entry, out Vector3 position, out float heading)
    {
        position = Vector3.zero;
        heading  = 0f;

        if (entry.legIndex != 0 || entry.progress > 0f) return false;
        if (!_shapes.TryGetValue(entry.routeId ?? string.Empty, out var shape) || shape.polyline.Count == 0) return false;

        var route = designerData.soulRoutes.Find(r => r != null && r.routeId == entry.routeId);
        if (route == null || !designerData.RouteStartsAtSoulJuiceTank(route)) return false;
        if (!_bowls.TryGetValue(route.originNodeId, out var bowl) || bowl == null) return false;

        // Round the pool, on the ring the route starts from.
        Vector3 c      = bowl.transform.position;
        Vector3 start  = shape.polyline[0];
        float   radius = new Vector2(start.x - c.x, start.z - c.z).magnitude;
        if (radius < 0.01f) return false;

        var   rng   = new System.Random(entry.identity * 7919 + 17);
        float angle = (float)rng.NextDouble() * Mathf.PI * 2f + Time.time * swimSpeed / radius;

        position = new Vector3(c.x + Mathf.Cos(angle) * radius, start.y, c.z + Mathf.Sin(angle) * radius);
        heading  = Mathf.Atan2(-Mathf.Sin(angle), Mathf.Cos(angle));
        return true;
    }

    /// <summary>
    /// Where a soul waiting in a tower's bowl is now, circling. Each soul keeps its own lane — a
    /// radius and a starting point seeded off its identity — and drifts gently in and out of it,
    /// so the shoal reads as swimming round the bowl rather than riding one ring. All go the same
    /// way round.
    /// </summary>
    // Where round the bowl each waiting soul has got to, radians.
    private readonly Dictionary<int, float> _bowlAngles = new Dictionary<int, float>();

    private void BowlPose(SoulJuiceTankBowl bowl, int identity, float fishSize,
                          out Vector3 position, out float heading)
    {
        float wet = Mathf.Max(0.0001f, bowl.WorldWaterRadius);

        // Keep the whole fish inside the water: lanes run from the middle out to its edge less half a fish.
        float reach = Mathf.Max(0f, wet - fishSize * 0.5f);

        var   rng   = new System.Random(identity * 7919 + 17);
        float lane  = Mathf.Lerp(0.3f, 0.85f, (float)rng.NextDouble()) * reach;
        float start = (float)rng.NextDouble() * Mathf.PI * 2f;
        float drift = (float)rng.NextDouble() * Mathf.PI * 2f;

        float radius = Mathf.Clamp(lane + Mathf.Sin(Time.time * 0.7f + drift) * 0.12f * reach, 0.05f * reach, reach);

        // Stepped on each frame, not worked out from the clock: as the bowl drains the lanes
        // narrow, and dividing all the time so far by a shrinking lane spun the fish faster and
        // faster. This keeps them swimming at the same speed.
        float turn = bowlSwimSpeed / Mathf.Max(0.05f * wet, lane);
        if (!_bowlAngles.TryGetValue(identity, out float angle))
            angle = start + Time.time * turn;
        else
            angle += Time.deltaTime * turn;
        _bowlAngles[identity] = angle;

        Vector3 c = bowl.WaterCentre;
        position = new Vector3(c.x + Mathf.Cos(angle) * radius, c.y, c.z + Mathf.Sin(angle) * radius);

        // Moving along the circle: the tangent of increasing angle.
        heading = Mathf.Atan2(-Mathf.Sin(angle), Mathf.Cos(angle));
    }

    // ─────────────────────────────────────────────
    // SOUL FISH AREAS
    // ─────────────────────────────────────────────

    // A pool ticked Soul Fish Area in the designer: filled with soul juice, its souls circling in
    // its water. Where it stands comes off the generated pool, as a tank's pool does.
    private class SoulFishArea
    {
        public string    nodeId;
        public Vector3   centre;
        public float     poolRadius;    // world units, the water's edge
        public float     islandRadius;  // world units, 0 for an open bowl
        public float     ringRadius;    // world units, the channel ring a route rides round the island
        public float     bandWidth;     // the soul routes' Band Width, before the tuner's Width
        public List<int> souls = new List<int>();

        // With an island: the band round the channel ring, closed — its first point again at the
        // end — with the arc length to each point. Empty for an open bowl, which keeps its disc.
        public List<Vector3> ring    = new List<Vector3>();
        public List<float>   ringArc = new List<float>();

        public int ZonePointCount => ring.Count > 0 ? ring.Count : 1;
    }

    // Most sides a Soul Fish Area's ring is drawn with; fewer on a small ring.
    private const int MaxAreaRingSides = 24;

    private readonly List<SoulFishArea> _areas = new List<SoulFishArea>();

    // Where round its pool each soul has got to, radians.
    private readonly Dictionary<int, float> _areaAngles = new Dictionary<int, float>();

    private void FindSoulFishAreas()
    {
        _areas.Clear();
        if (designerData.pools == null) return;

        RiverPoolMesh[] generated = null;
        foreach (var pool in designerData.pools)
        {
            if (pool == null || !pool.soulFishArea) continue;

            var node = designerData.nodes.Find(n => n != null && n.id == pool.nodeId);
            if (node == null) continue;

            generated ??= FindObjectsOfType<RiverPoolMesh>();
            if (!NearestPool(generated, node.worldPosition, out var record))
            {
                Debug.LogWarning($"[TravelFish] Pool '{pool.nodeId}' is a Soul Fish Area, but no generated pool " +
                                 "stands there — Generate in the Level Select Designer.", this);
                continue;
            }

            float scale = Mathf.Abs(record.transform.lossyScale.x);
            var   area  = new SoulFishArea
            {
                nodeId       = pool.nodeId,
                centre       = record.transform.position,
                poolRadius   = record.poolRadius * scale,
                islandRadius = Mathf.Min(record.poolRadius, record.islandRadius) * scale,
                ringRadius   = record.ChannelRadius * scale,
                bandWidth    = SoulRouteBandWidth(),
            };
            if (area.islandRadius > 0f) BuildAreaRing(area);

            if (pool.soulFishAreaSouls != null)
                foreach (var soul in pool.soulFishAreaSouls)
                    if (soul != null && soul.soulDataIdentity > 0 && !area.souls.Contains(soul.soulDataIdentity))
                        area.souls.Add(soul.soulDataIdentity);

            _areas.Add(area);
        }

        if (_areas.Count > 0)
            Debug.Log($"[TravelFish] {_areas.Count} Soul Fish Area pool(s) found.");
    }

    // A Soul Fish Area's band is as wide as the soul routes' bands on the rivers — the widest, if
    // they differ, and a new route's when there are none.
    private float SoulRouteBandWidth()
    {
        float widest = 0f;
        foreach (var route in designerData.soulRoutes)
            if (route != null) widest = Mathf.Max(widest, route.bandWidth);
        return widest > 0f ? widest : new LevelSelectDesignerData.SoulRoute().bandWidth;
    }

    // The ring round the island, on the channel ring a route takes round the pool: sides enough to
    // stay within the zone's packing tolerance of the circle, closed back on its first point.
    private static void BuildAreaRing(SoulFishArea area)
    {
        area.ring.Clear();
        area.ringArc.Clear();
        float r = area.ringRadius;
        if (r <= 0f) return;

        float step  = 2f * Mathf.Acos(Mathf.Clamp(1f - MinZoneTolerance / r, -1f, 1f));
        int   sides = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI / Mathf.Max(step, 0.0001f)), 12, MaxAreaRingSides);

        for (int i = 0; i <= sides; i++)
        {
            float a = 2f * Mathf.PI * i / sides;
            area.ring.Add(new Vector3(area.centre.x + Mathf.Cos(a) * r, area.centre.y,
                                      area.centre.z + Mathf.Sin(a) * r));
            area.ringArc.Add(r * a);
        }
    }

    // The generated pool whose water reaches the point, nearest first.
    private static bool NearestPool(RiverPoolMesh[] pools, Vector3 at, out RiverPoolMesh found)
    {
        found = null;
        float best = float.MaxValue;
        foreach (var pool in pools)
        {
            if (pool == null) continue;
            Vector3 p    = pool.transform.position;
            float   r    = pool.poolRadius * Mathf.Abs(pool.transform.lossyScale.x);
            float   dist = new Vector2(p.x - at.x, p.z - at.z).magnitude;
            if (dist <= r && dist < best) { best = dist; found = pool; }
        }
        return found != null;
    }

    /// <summary>
    /// Where a soul circling in a Soul Fish Area is now. As in a tank's bowl, each keeps its own
    /// lane, seeded off its identity, drifting gently in and out of it, all going the same way
    /// round. With an island the lanes stay inside the band round the channel ring — and inside
    /// the water between the island's edge and the pool's — so none swims off the band; an open
    /// bowl's circle round its middle. <paramref name="zoneWidth"/> is the tuner's Width, which
    /// widens the band on the water.
    /// </summary>
    private void AreaPose(SoulFishArea area, int identity, float fishSize, float zoneWidth,
                          out Vector3 position, out float heading)
    {
        float half  = fishSize * 0.5f;
        float outer = Mathf.Max(0.0001f, area.poolRadius - half);
        float inner = area.islandRadius > 0f ? Mathf.Min(area.islandRadius + half, outer) : 0f;

        if (area.ring.Count > 0)
        {
            float bandHalf = Mathf.Max(0f, area.bandWidth * zoneWidth - half);
            outer = Mathf.Max(inner, Mathf.Min(outer, area.ringRadius + bandHalf));
            inner = Mathf.Min(outer, Mathf.Max(inner, area.ringRadius - bandHalf));
        }

        var   rng   = new System.Random(identity * 7919 + 17);
        float pick  = (float)rng.NextDouble();
        float start = (float)rng.NextDouble() * Mathf.PI * 2f;
        float drift = (float)rng.NextDouble() * Mathf.PI * 2f;

        float lane, sway, lo;
        if (area.islandRadius > 0f)
        {
            // In the ring: a fish wider than the ring rides its middle.
            lane = Mathf.Lerp(inner, outer, Mathf.Lerp(0.15f, 0.85f, pick));
            sway = 0.12f * (outer - inner);
            lo   = inner;
        }
        else
        {
            lane = Mathf.Lerp(0.3f, 0.85f, pick) * outer;
            sway = 0.12f * outer;
            lo   = 0.05f * outer;
        }
        float radius = Mathf.Clamp(lane + Mathf.Sin(Time.time * 0.7f + drift) * sway, lo, outer);

        // Stepped on each frame, as in the bowl, so every fish keeps the river's swim speed.
        float turn = swimSpeed / Mathf.Max(0.05f * area.poolRadius, lane);
        if (!_areaAngles.TryGetValue(identity, out float angle))
            angle = start + Time.time * turn;
        else
            angle += Time.deltaTime * turn;
        _areaAngles[identity] = angle;

        position = new Vector3(area.centre.x + Mathf.Cos(angle) * radius, area.centre.y,
                               area.centre.z + Mathf.Sin(angle) * radius);
        heading  = Mathf.Atan2(-Mathf.Sin(angle), Mathf.Cos(angle));
    }

    // ─────────────────────────────────────────────
    // RESOLVING DEPARTURES
    // ─────────────────────────────────────────────

    /// <summary>
    /// Turns "left level X by door N" into a place on a route. The arena scene cannot see the
    /// routes, so it records only the door; this is where that becomes a journey.
    /// </summary>
    private void ResolveDepartures()
    {
        var unresolved = SoulJourneyData.Unresolved();
        if (unresolved.Count == 0) return;

        int placed = 0;

        foreach (var entry in unresolved)
        {
            if (!FindRouteLeavingLevel(entry.fromLevelID, out string routeId, out int arenaPointIndex))
            {
                Debug.LogWarning($"[TravelFish] Soul #{entry.identity} left '{entry.fromLevelID}' but no " +
                                 "authored route passes through that level — it stays put until one does.");
                continue;
            }

            var route = designerData.soulRoutes.Find(r => r.routeId == routeId);
            int next  = arenaPointIndex + 1;

            if (route == null || next >= route.points.Count)
            {
                // The level is the end of this route — the soul has arrived where it was going.
                Debug.Log($"[TravelFish] Soul #{entry.identity} reached the end of route '{routeId}'.");
                continue;
            }

            // The stretch out of a level the player has just finished is open by definition.
            SoulJourneyData.OpenRouteTo(routeId, next);
            SoulJourneyData.Release(entry.identity, routeId, next);
            placed++;
        }

        if (placed > 0)
            Debug.Log($"[TravelFish] Placed {placed} departed soul(s) onto their routes.");
    }

    /// <summary>The first route with an arena point standing on <paramref name="levelID"/>.</summary>
    private bool FindRouteLeavingLevel(string levelID, out string routeId, out int pointIndex)
    {
        routeId    = null;
        pointIndex = -1;

        if (string.IsNullOrEmpty(levelID)) return false;

        foreach (var route in designerData.soulRoutes)
        {
            for (int i = 0; i < route.points.Count; i++)
            {
                var point = route.points[i];
                if (point.kind != LevelSelectDesignerData.SoulRoutePoint.PointKind.Arena) continue;

                var arena = designerData.arenas.Find(a => a.nodeId == point.nodeId);
                if (arena?.gridData == null || arena.gridData.levelID != levelID) continue;

                routeId    = route.routeId;
                pointIndex = i;
                return true;
            }
        }

        return false;
    }

    // ─────────────────────────────────────────────
    // ENTRY TRIGGERS
    // The space in front of an arena door that asks the player to go in. A soul's fish swimming
    // into it counts the soul into that level — the same place that counts the boat in.
    // ─────────────────────────────────────────────

    private class EntryTrigger
    {
        public LevelSelectEnter enter;
        public Collider[]       colliders;
    }

    // Every entry trigger in the scene, by the level it leads into.
    private readonly Dictionary<string, List<EntryTrigger>> _entryTriggers = new Dictionary<string, List<EntryTrigger>>();

    private void FindEntryTriggers()
    {
        _entryTriggers.Clear();

        foreach (var enter in FindObjectsOfType<LevelSelectEnter>())
        {
            if (enter == null || enter.gridData == null || string.IsNullOrEmpty(enter.gridData.levelID)) continue;

            var colliders = new List<Collider>();
            foreach (var c in enter.GetComponentsInChildren<Collider>(true))
                if (c.isTrigger) colliders.Add(c);

            if (colliders.Count == 0)
            {
                Debug.LogWarning($"[TravelFish] Entry trigger '{enter.name}' has no trigger collider — souls " +
                                 "can't be counted into its level there.", enter);
                continue;
            }

            if (!_entryTriggers.TryGetValue(enter.gridData.levelID, out var list))
                _entryTriggers[enter.gridData.levelID] = list = new List<EntryTrigger>();

            list.Add(new EntryTrigger { enter = enter, colliders = colliders.ToArray() });
        }

        // Said once: a level on a route with no trigger falls back to joining at the Arena point.
        foreach (var route in designerData.soulRoutes)
        {
            if (route == null) continue;
            foreach (var point in route.points)
            {
                if (point.kind != LevelSelectDesignerData.SoulRoutePoint.PointKind.Arena) continue;
                var arena = designerData.arenas.Find(a => a.nodeId == point.nodeId);
                if (arena?.gridData == null || _entryTriggers.ContainsKey(arena.gridData.levelID)) continue;

                Debug.LogWarning($"[TravelFish] Route '{route.displayName}' goes into '{arena.gridData.levelID}', " +
                                 "but no entry trigger for that level is in the scene — its souls will join at " +
                                 "the Arena point by the route's Door In instead.", this);
            }
        }

        int count = 0;
        foreach (var list in _entryTriggers.Values) count += list.Count;
        Debug.Log($"[TravelFish] Found {count} entry trigger(s) across {_entryTriggers.Count} level(s).");
    }

    /// <summary>
    /// Counts the soul into the level its route is heading for, the first time its fish is inside
    /// one of that level's entry triggers. The door is the trigger's own.
    /// </summary>
    private void CheckEntryTrigger(SoulJourneyEntry entry, LevelSelectDesignerData.SoulRoute route)
    {
        if (NextArenaPoint(route, entry.legIndex, out var arena) < 0) return;

        string levelID = arena.gridData.levelID;
        if (!TryWorldPose(entry, out Vector3 position, out _)) return;

        int door = EntryDoorAt(levelID, position);
        if (door != NoDoor)
            SoulJourneyData.EnterLevel(entry.identity, levelID, door);
    }

    private const int NoDoor = int.MinValue;

    /// <summary>The door of whichever of the level's entry triggers holds <paramref name="position"/>; NoDoor if none.</summary>
    private int EntryDoorAt(string levelID, Vector3 position)
    {
        if (!_entryTriggers.TryGetValue(levelID, out var triggers)) return NoDoor;

        foreach (var trigger in triggers)
        {
            if (trigger.enter == null) continue;

            foreach (var c in trigger.colliders)
                if (InsideFromAbove(c, position))
                    return trigger.enter.entranceIndex;
        }

        return NoDoor;
    }

    /// <summary>
    /// The level ahead of a soul on leg <paramref name="legIndex"/>: the first Arena point at or
    /// past the end of that leg. Returns its point index, or -1 when no arena with level data is ahead.
    /// </summary>
    private int NextArenaPoint(LevelSelectDesignerData.SoulRoute route, int legIndex,
                               out LevelSelectDesignerData.DesignerArena arena)
    {
        arena = null;
        for (int i = Mathf.Max(legIndex, 0); i < route.points.Count; i++)
        {
            if (route.points[i].kind != LevelSelectDesignerData.SoulRoutePoint.PointKind.Arena) continue;

            arena = designerData.arenas.Find(a => a.nodeId == route.points[i].nodeId);
            if (arena == null) continue;
            return arena.gridData != null ? i : -1;
        }

        return -1;
    }

    // ─────────────────────────────────────────────
    // ARRIVING WITH THE BOAT
    // ─────────────────────────────────────────────

    /// <summary>
    /// The boat is going into <paramref name="levelID"/>. Once a route's band has drawn all the way
    /// to that level's Arena point, every released soul still swimming towards it comes in too —
    /// no waiting for each fish to reach the arena. Souls still in a tower's bowl stay there. Each
    /// comes in by the door its fish would have swum through. Called by LevelSelectEnter just
    /// before the level loads. Returns how many came in.
    /// </summary>
    public int ArriveWithBoat(string levelID)
    {
        if (designerData == null || string.IsNullOrEmpty(levelID)) return 0;

        int arrived = 0, finished = 0;

        foreach (var entry in SoulJourneyData.Swimming())
        {
            // Already counted in by an entry trigger, just not finished swimming on the map.
            if (entry.place == SoulPlace.InLevel)
            {
                if (entry.levelID != levelID) continue;
                SoulJourneyData.FinishSwimmingIn(entry.identity);
                finished++;
                continue;
            }

            if (string.IsNullOrEmpty(entry.routeId)) continue;                 // still unresolved
            if (!_shapes.TryGetValue(entry.routeId, out var shape)) continue;

            var route = designerData.soulRoutes.Find(r => r != null && r.routeId == entry.routeId);
            if (route == null || entry.legIndex < 0) continue;

            int point = NextArenaPoint(route, entry.legIndex, out var arena);
            if (point < 0 || point >= shape.pointIndex.Count || arena.gridData.levelID != levelID) continue;

            // The band has to have reached the arena itself.
            float arenaArc = shape.cumArc[Mathf.Clamp(shape.pointIndex[point], 0, shape.cumArc.Count - 1)];
            if (!_zoneHeads.TryGetValue(entry.routeId, out float head) || head < arenaArc - 0.001f)
            {
                if (verboseLogging)
                    Debug.Log($"[TravelFish] Soul #{entry.identity} stays out of '{levelID}' — route " +
                              $"'{route.displayName}' band at {head:F1}m, arena at {arenaArc:F1}m.");
                continue;
            }

            int door = DoorOnTheWayIn(shape, RouteArc(entry), arenaArc, levelID, route.points[point].entranceIn);
            SoulJourneyData.Arrive(entry.identity, levelID, door);
            arrived++;
        }

        Debug.Log($"[TravelFish] Boat entering '{levelID}': {arrived} soul(s) still on the way came in with it" +
                  (finished > 0 ? $", {finished} already counted in stopped swimming on the map." : "."));
        return arrived;
    }

    /// <summary>
    /// The door a soul's fish would swim in by: the first of the level's entry triggers its route
    /// passes through between where it is and the Arena point. The route point's Door In if none.
    /// </summary>
    private int DoorOnTheWayIn(RouteShape shape, float fromArc, float toArc, string levelID, int fallback)
    {
        for (float arc = fromArc; ; arc += MinZoneSpacing)
        {
            float at   = Mathf.Min(arc, toArc);
            int   door = EntryDoorAt(levelID, SoulRouteCurve.PointAt(shape.polyline, shape.cumArc, at));
            if (door != NoDoor) return door;
            if (at >= toArc) break;
        }

        return fallback;
    }

    /// <summary>
    /// Whether a point lies inside a trigger seen from above. The fish swim at water level and the
    /// trigger may not reach down to it, so height is ignored — tested at the trigger's own middle.
    /// </summary>
    private static bool InsideFromAbove(Collider c, Vector3 position)
    {
        if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) return false;

        Bounds  b = c.bounds;
        Vector3 p = new Vector3(position.x, b.center.y, position.z);
        if (!b.Contains(p)) return false;

        // ClosestPoint can't take a concave mesh; its box is the best there is.
        if (c is MeshCollider mesh && !mesh.convex) return true;

        return (c.ClosestPoint(p) - p).sqrMagnitude < 0.000001f;
    }

    // ─────────────────────────────────────────────
    // MOVEMENT
    // ─────────────────────────────────────────────

    private void AdvanceTravellingSouls()
    {
        float step = swimSpeed * Time.deltaTime;
        if (step <= 0f) return;

        // Front of each route first, so every soul knows where the one ahead of it has got to.
        // Souls on the same spot (all released together) are put in order by identity. Souls
        // already counted into a level but still swimming in move with the rest.
        var travelling = SoulJourneyData.Swimming();
        travelling.Sort((a, b) =>
        {
            int byArc = RouteArc(b).CompareTo(RouteArc(a));
            return byArc != 0 ? byArc : a.identity.CompareTo(b.identity);
        });

        // Where the soul ahead on each route is, after its move this frame.
        _aheadArc.Clear();

        foreach (var entry in travelling)
        {
            if (string.IsNullOrEmpty(entry.routeId)) continue;              // still unresolved
            if (!_shapes.TryGetValue(entry.routeId, out var shape)) continue;

            bool hasAhead = _aheadArc.TryGetValue(entry.routeId, out float aheadArc);
            _aheadArc[entry.routeId] = RouteArc(entry);   // until it moves, below

            var route = designerData.soulRoutes.Find(r => r.routeId == entry.routeId);
            if (route == null || entry.legIndex < 0 || entry.legIndex >= route.points.Count) continue;

            // Into the entry trigger of the level ahead: the soul is in that level from now on,
            // though its fish swims on to the Arena point as before.
            if (entry.place == SoulPlace.OnRiver)
                CheckEntryTrigger(entry, route);

            // A soul never swims past where the route has been opened; it gathers at the last
            // open point and waits, the way fish gather at an unlit lamp inside a level.
            int openTo = SoulJourneyData.OpenLegOf(entry.routeId);
            if (entry.legIndex > openTo)
            {
                SoulJourneyData.Advance(entry.identity, 1f);
                continue;
            }

            float legLength = LegLength(shape, entry.legIndex);
            if (legLength <= 0.0001f)
            {
                SoulJourneyData.Advance(entry.identity, 1f);
                continue;
            }

            float progress = entry.progress + step / legLength;

            // Behind the front of the zone, never past it. A soul already ahead of it (placed
            // there by a departure) holds still until the zone reaches it.
            if (_zoneHeads.TryGetValue(entry.routeId, out float head) &&
                TryLegBounds(shape, entry.legIndex, out int legFrom, out _))
            {
                float allowed = (head - shape.cumArc[legFrom]) / legLength;
                progress = Mathf.Min(progress, Mathf.Max(entry.progress, allowed));
            }

            // At least Soul Spacing behind the soul ahead on this route. Same rule as the zone
            // front: held back, never pushed backwards.
            if (TryLegBounds(shape, entry.legIndex, out int from, out _))
            {
                if (hasAhead)
                {
                    float allowed = (aheadArc - soulSpacing - shape.cumArc[from]) / legLength;
                    progress = Mathf.Min(progress, Mathf.Max(entry.progress, allowed));
                }

                _aheadArc[entry.routeId] = shape.cumArc[from] + Mathf.Clamp01(progress) * legLength;
            }

            if (progress >= 1f)
            {
                var arrivedAt = route.points[entry.legIndex];

                // An arena point is a destination, not a waypoint: the fish stops being drawn here.
                if (arrivedAt.kind == LevelSelectDesignerData.SoulRoutePoint.PointKind.Arena)
                {
                    // Already counted in by the entry trigger — only the drawing ends.
                    if (entry.place == SoulPlace.InLevel)
                    {
                        SoulJourneyData.FinishSwimmingIn(entry.identity);
                        continue;
                    }

                    // Never crossed a trigger (the level has none in the scene): join it here,
                    // by the route point's Door In, the way it worked before triggers counted.
                    var arena = designerData.arenas.Find(a => a.nodeId == arrivedAt.nodeId);
                    if (arena?.gridData != null)
                    {
                        Debug.LogWarning($"[TravelFish] Soul #{entry.identity} reached '{arena.gridData.levelID}' " +
                                         "without crossing an entry trigger — joining by the route point's " +
                                         $"Door In ({arrivedAt.entranceIn}).", this);
                        SoulJourneyData.Arrive(entry.identity, arena.gridData.levelID,
                                               arrivedAt.entranceIn);
                        continue;
                    }
                }

                int next = entry.legIndex + 1;
                if (next < route.points.Count && next <= openTo)
                    SoulJourneyData.EnterLeg(entry.identity, next);
                else
                    SoulJourneyData.Advance(entry.identity, 1f);   // hold at the open end

                continue;
            }

            SoulJourneyData.Advance(entry.identity, progress);
        }
    }

    private readonly Dictionary<string, float> _aheadArc = new Dictionary<string, float>();

    /// <summary>How far along its whole route a soul is, world units. 0 if it has no shape to be on.</summary>
    private float RouteArc(SoulJourneyEntry entry)
    {
        if (!_shapes.TryGetValue(entry.routeId ?? string.Empty, out var shape)) return 0f;
        if (!TryLegBounds(shape, entry.legIndex, out int from, out int to)) return 0f;
        return shape.cumArc[from] + (shape.cumArc[to] - shape.cumArc[from]) * Mathf.Clamp01(entry.progress);
    }

    private float LegLength(RouteShape shape, int legIndex)
    {
        if (!TryLegBounds(shape, legIndex, out int from, out int to)) return 0f;
        return shape.cumArc[to] - shape.cumArc[from];
    }

    /// <summary>Polyline vertex range of the stretch that ENDS at <paramref name="legIndex"/>.</summary>
    private bool TryLegBounds(RouteShape shape, int legIndex, out int from, out int to)
    {
        from = 0;
        to   = 0;

        if (legIndex < 0 || legIndex >= shape.pointIndex.Count) return false;

        to   = shape.pointIndex[legIndex];
        from = legIndex > 0 ? shape.pointIndex[legIndex - 1] : 0;

        return to > from;
    }

    /// <summary>Where a soul is, and which way it faces, in world space.</summary>
    private bool TryWorldPose(SoulJourneyEntry entry, out Vector3 position, out float heading)
    {
        position = Vector3.zero;
        heading  = 0f;

        if (!_shapes.TryGetValue(entry.routeId ?? string.Empty, out var shape)) return false;
        if (!TryLegBounds(shape, entry.legIndex, out int from, out int to)) return false;

        float startArc = shape.cumArc[from];
        float target   = startArc + (shape.cumArc[to] - startArc) * Mathf.Clamp01(entry.progress);

        for (int i = from; i < to; i++)
        {
            if (target > shape.cumArc[i + 1] && i + 1 < to) continue;

            float span = shape.cumArc[i + 1] - shape.cumArc[i];
            float t    = span > 0.0001f ? (target - shape.cumArc[i]) / span : 0f;

            Vector3 a = shape.polyline[i];
            Vector3 b = shape.polyline[i + 1];

            position = Vector3.Lerp(a, b, Mathf.Clamp01(t));

            Vector3 dir = b - a;
            heading = Mathf.Atan2(dir.x, dir.z);   // radians, matching the .hlsl's rotation frame
            return true;
        }

        return false;
    }

    // ─────────────────────────────────────────────
    // SOUL ZONE
    // ─────────────────────────────────────────────

    /// <summary>How far along its line a route is open — the end of its last opened leg. -1 = not open.</summary>
    private float OpenEndArc(string routeId, RouteShape shape)
    {
        int openTo = SoulJourneyData.OpenLegOf(routeId);
        if (openTo < 0 || shape.pointIndex.Count == 0) return -1f;

        int vertex = shape.pointIndex[Mathf.Min(openTo, shape.pointIndex.Count - 1)];
        return shape.cumArc[Mathf.Clamp(vertex, 0, shape.cumArc.Count - 1)];
    }

    /// <summary>A route already open when the map loads is drawn out to its open end straight away.</summary>
    private void InitZoneHeads()
    {
        _zoneHeads.Clear();
        foreach (var pair in _shapes)
        {
            float end = OpenEndArc(pair.Key, pair.Value);
            if (end >= 0f) _zoneHeads[pair.Key] = end;
        }
    }

    /// <summary>Draws each open route's zone on towards its open end at Zone Draw Speed.</summary>
    private void AdvanceZoneHeads()
    {
        float step = Mathf.Max(zoneDrawSpeed, 0.01f) * Time.deltaTime;

        foreach (var pair in _shapes)
        {
            float end = OpenEndArc(pair.Key, pair.Value);
            if (end < 0f) continue;

            // Released just now: no head yet, so it starts from the origin.
            _zoneHeads.TryGetValue(pair.Key, out float head);
            _zoneHeads[pair.Key] = Mathf.MoveTowards(head, end, step);
        }
    }

    /// <summary>
    /// Packs every drawn zone — each route from its origin to its front — into the shader's one
    /// array. Pushed every frame, whole: the globals are bare and a reimport empties them.
    /// </summary>
    private void PublishZone()
    {
        int  count    = 0;
        bool overflow = false;

        // Every soul juice tank's bowl is filled with the zone: one lone point, a disc as wide as
        // the bowl's water. The shader widens every point by the tuner's Width, so it is divided
        // out here to keep the disc the bowl's own size.
        var   look  = ZoneLook();
        float width = Mathf.Max(look.width, 0.0001f);

        // Corner Smoothing is tuned live: repack the routes' lines when it changes.
        if (_shapes.Count > 0 && Mathf.Abs(look.cornerSmoothing - _packedCornerSmoothing) > 1e-4f)
            PackZones(look.cornerSmoothing);

        // Once a tower is released the same point becomes the pool fill round its base instead —
        // the bowl has drained first, so the two never show at once.
        foreach (var pair in _bowls)
        {
            var bowl = pair.Value;
            if (bowl == null) continue;
            if (count >= MaxZonePoints) { overflow = true; break; }

            Vector3 centre = bowl.WaterCentre;
            float   radius = bowl.WorldWaterRadius;
            if (_bowlStates.TryGetValue(pair.Key, out var state) &&
                (state.phase == BowlPhase.Filling || state.phase == BowlPhase.Done))
            {
                centre = bowl.transform.position;
                radius = state.phase == BowlPhase.Filling ? state.fill : state.FillRadius(look.poolFillRadius);
            }
            if (radius <= 0f) continue;

            Vector4 disc = ZonePoint(centre, radius / width, 0f);
            disc.w = -disc.w;   // alone — joined to nothing
            ZoneBuffer[count++] = disc;
        }

        // Every Soul Fish Area round an island is a band round its channel ring, as wide as a soul
        // route's; an open bowl is filled the same way as a tank's, out to its water's edge.
        foreach (var area in _areas)
        {
            if (count >= MaxZonePoints) { overflow = true; break; }
            if (area.poolRadius <= 0f) continue;

            if (area.ring.Count > 0)
            {
                if (count + area.ring.Count > MaxZonePoints) { overflow = true; break; }
                for (int i = 0; i < area.ring.Count; i++)
                    ZoneBuffer[count++] = ZonePoint(area.ring[i], area.bandWidth, area.ringArc[i]);
                ZoneBuffer[count - 1].w = -ZoneBuffer[count - 1].w;
                continue;
            }

            Vector4 disc = ZonePoint(area.centre, area.poolRadius / width, 0f);
            disc.w = -disc.w;
            ZoneBuffer[count++] = disc;
        }

        foreach (var pair in _shapes)
        {
            if (!_zoneHeads.TryGetValue(pair.Key, out float head)) continue;

            var shape = pair.Value;
            if (shape.zonePoints.Count == 0) continue;

            int routeStart = count;
            for (int i = 0; i < shape.zonePoints.Count && shape.zoneArc[i] < head; i++)
            {
                if (count >= MaxZonePoints) { overflow = true; break; }
                ZoneBuffer[count++] = ZonePoint(shape.zonePoints[i], shape.bandWidth, shape.zoneArc[i]);
            }

            // The front, exactly where it has drawn to — so it slides on rather than stepping from
            // one packed point to the next.
            if (count < MaxZonePoints)
                ZoneBuffer[count++] = ZonePoint(SoulRouteCurve.PointAt(shape.zoneLine, shape.zoneLineArc, head),
                                                shape.bandWidth, head);
            else
                overflow = true;

            // The last point of a route does not join on to the next route's first.
            if (count > routeStart)
                ZoneBuffer[count - 1].w = -ZoneBuffer[count - 1].w;
        }

        if (overflow && !_zoneOverflowWarned)
        {
            _zoneOverflowWarned = true;
            Debug.LogWarning($"[TravelFish] The soul zones need more than {MaxZonePoints} points — the rest " +
                             "are not drawn. Raise TRAVELZONE_MAX_POINTS and MaxZonePoints together.", this);
        }

        for (int i = count; i < MaxZonePoints; i++)
            ZoneBuffer[i] = Vector4.zero;

        Shader.SetGlobalVectorArray(ZonePositionsID, ZoneBuffer);
        Shader.SetGlobalFloat(ZoneCountID, count);
    }

    // .xz position, .y half-width, .w +(1 + arc length) — joined to the next point until flipped.
    private static Vector4 ZonePoint(Vector3 p, float halfWidth, float arc)
        => new Vector4(p.x, halfWidth, p.z, 1f + arc);

    // ─────────────────────────────────────────────
    // PUBLISHING
    // ─────────────────────────────────────────────

    private void PublishGlobals()
    {
        var travelling = SoulJourneyData.Swimming();
        int count = 0;

        for (int i = 0; i < travelling.Count && count < MaxDrawnFish; i++)
        {
            if (!TryPoolPose(travelling[i], out Vector3 pos, out float heading) &&
                !TryWorldPose(travelling[i], out pos, out heading)) continue;

            // .x/.z world position, .y heading in radians, .w per-fish size (0 = use the global).
            PositionBuffer[count] = new Vector4(pos.x, heading, pos.z, 0f);
            count++;
        }

        // Then the souls still waiting in a soul juice tank, circling in its bowl. They share the
        // one buffer: the bowl's water is the same water material, so it draws them the same way.
        int inBowls = 0;
        float bowlSize = bowlFishSize > 0f ? bowlFishSize : Mathf.Max(fishSize, 0.0001f);

        foreach (var route in designerData.soulRoutes)
        {
            if (count >= MaxDrawnFish) break;
            if (!designerData.RouteStartsAtSoulJuiceTank(route)) continue;
            if (!_bowls.TryGetValue(route.originNodeId, out var bowl) || bowl == null) continue;
            if (!SoulsInBowl(route.originNodeId)) continue;   // gone down the hole

            foreach (int identity in HeldSouls(route))
            {
                if (count >= MaxDrawnFish) break;

                BowlPose(bowl, identity, bowlSize, out Vector3 pos, out float heading);
                PositionBuffer[count] = new Vector4(pos.x, heading, pos.z, bowlSize);
                count++;
                inBowls++;
            }
        }

        // Then the souls circling in each Soul Fish Area — river fish, in river water. One that
        // has a journey of its own now is wherever that has it, never here as well.
        float zoneWidth = _areas.Count > 0
            ? Mathf.Max(RiverSoulZoneSettings.Resolve(designerData.waterPreset != null ? designerData.waterPreset.soulZone : null).width, 0.0001f)
            : 1f;
        foreach (var area in _areas)
        {
            if (count >= MaxDrawnFish) break;
            foreach (int identity in area.souls)
            {
                if (count >= MaxDrawnFish) break;
                if (SoulJourneyData.HasRecord(identity)) continue;

                AreaPose(area, identity, Mathf.Max(fishSize, 0.0001f), zoneWidth, out Vector3 pos, out float heading);
                PositionBuffer[count] = new Vector4(pos.x, heading, pos.z, 0f);
                count++;
                inBowls++;
            }
        }

        // Stale slots would still be sampled if the count went up again, so blank the rest.
        for (int i = count; i < MaxDrawnFish; i++)
            PositionBuffer[i] = Vector4.zero;

        Shader.SetGlobalVectorArray(PositionsID, PositionBuffer);
        Shader.SetGlobalFloat(CountID, count);
        Shader.SetGlobalFloat(SizeID, Mathf.Max(fishSize, 0.0001f));
        Shader.SetGlobalFloat(StrengthID, strength);

        if (fishTexture != null)
            Shader.SetGlobalTexture(TexID, fishTexture);

        LogPositions(travelling, count - inBowls);
    }

    // ─────────────────────────────────────────────
    // DEBUG
    // ─────────────────────────────────────────────

    /// <summary>
    /// One report at load: what the routes came out as, and where every soul actually stands. This
    /// is the first thing to read when no fish appear, because it separates the three ways nothing
    /// can draw — no soul is on the river, a soul is on the river with no route resolved, or the
    /// route resolved but has no shape to swim.
    /// </summary>
    private void ReportStartingPoints()
    {
        if (fishTexture == null)
            Debug.LogWarning("[TravelFish] No fishTexture assigned — _TravelSoulFishTex is never pushed, " +
                             "so the water samples an unbound texture and no fish can show.", this);

        // Routes first. NodeWorldPosition hands back Vector3.zero for an id it does not know, so a
        // route full of zeroes is an authoring mismatch rather than a movement problem.
        foreach (var route in designerData.soulRoutes)
        {
            if (route == null) continue;

            if (!_shapes.TryGetValue(route.routeId ?? string.Empty, out var shape) || shape.polyline.Count == 0)
            {
                Debug.LogWarning($"[TravelFish] Route '{route.displayName}' ({route.routeId}) built no shape — " +
                                 "nothing on it can move.", this);
                continue;
            }

            int zeros = 0;
            foreach (var vertex in shape.polyline)
                if (vertex == Vector3.zero) zeros++;

            int soulCount = route.souls != null ? route.souls.Count : 0;
            float length  = shape.cumArc.Count > 0 ? shape.cumArc[shape.cumArc.Count - 1] : 0f;

            // A fish stops being drawn at an arena point — it arrives in that level. Naming them
            // here saves wondering why the fish vanish part way along.
            var arenaPoints = new List<int>();
            for (int i = 0; i < route.points.Count; i++)
                if (route.points[i].kind == LevelSelectDesignerData.SoulRoutePoint.PointKind.Arena)
                    arenaPoints.Add(i);

            Debug.Log($"[TravelFish] Route '{route.displayName}' ({route.routeId}): " +
                      $"{shape.polyline.Count} vertices over {route.points.Count} points, {length:F1}m long, " +
                      $"{soulCount} authored soul(s), opened as far as leg {SoulJourneyData.OpenLegOf(route.routeId)}. " +
                      $"Starts {shape.polyline[0].ToString("F2")}, " +
                      $"ends {shape.polyline[shape.polyline.Count - 1].ToString("F2")}" +
                      (zeros > 0 ? $", {zeros} vertex(es) sitting at the world origin — unresolved node ids." : ".") +
                      (arenaPoints.Count > 0
                          ? $" Fish arrive and stop being drawn at point(s) {string.Join(", ", arenaPoints)}."
                          : " No arena points — fish swim the whole route."));
        }

        // Then every soul. Only OnRiver souls are ever drawn; one in an origin pool is authored but
        // has never been released, which is the normal state before any level has been finished.
        int pool = 0, river = 0, inLevel = 0, boat = 0;

        foreach (var entry in SaveManager.Load().soulJourneys)
        {
            switch (entry.place)
            {
                case SoulPlace.OriginPool: pool++;    break;
                case SoulPlace.OnRiver:    river++;   break;
                case SoulPlace.InLevel:    inLevel++; break;
                case SoulPlace.OnBoat:     boat++;    break;
            }
        }

        Debug.Log($"[TravelFish] Souls on record: {pool} in origin pools, {river} on the river, " +
                  $"{inLevel} in levels, {boat} on the boat.");

        foreach (var entry in SoulJourneyData.Travelling())
        {
            if (string.IsNullOrEmpty(entry.routeId))
            {
                Debug.LogWarning($"[TravelFish] Soul #{entry.identity} is on the river with NO ROUTE — it left " +
                                 $"'{entry.fromLevelID}' by door {entry.fromEntranceIndex} and no authored route " +
                                 "passes through that level, so it can never be placed or drawn.", this);
                continue;
            }

            if (TryWorldPose(entry, out Vector3 pos, out float heading))
                Debug.Log($"[TravelFish] Soul #{entry.identity} starts on route '{entry.routeId}' leg " +
                          $"{entry.legIndex} at {entry.progress:F2} — world {pos.ToString("F2")}, " +
                          $"heading {heading * Mathf.Rad2Deg:F0}°.");
            else
                Debug.LogWarning($"[TravelFish] Soul #{entry.identity} is on route '{entry.routeId}' leg " +
                                 $"{entry.legIndex} but has NO WORLD POSE — that leg has no length in the " +
                                 "route shape, so it cannot be drawn.", this);
        }

        if (river == 0)
            Debug.LogWarning("[TravelFish] Nothing is on the river yet — souls waiting in a soul juice tank are drawn in its bowl, " +
                             "and nothing on the river draws until a tower is released or a soul departs a level.", this);
    }

    /// <summary>Where each travelling soul is right now, once a second while verboseLogging is on.</summary>
    private void LogPositions(List<SoulJourneyEntry> travelling, int drawn)
    {
        if (!verboseLogging || Time.time < _nextPositionLog) return;
        _nextPositionLog = Time.time + 1f;

        if (travelling.Count == 0)
        {
            Debug.Log("[TravelFish] Nothing on the river to publish.");
            return;
        }

        var report = new System.Text.StringBuilder();
        report.Append($"[TravelFish] Publishing {drawn} of {travelling.Count} travelling soul(s) — " +
                      $"size {fishSize:F2}, strength {strength:F2}, " +
                      $"tex {(fishTexture != null ? fishTexture.name : "NONE")}.");

        foreach (var entry in travelling)
        {
            if (TryWorldPose(entry, out Vector3 pos, out float heading))
                report.Append($"\n    #{entry.identity} route '{entry.routeId}' leg {entry.legIndex} " +
                              $"at {entry.progress:F2} — world {pos.ToString("F2")}, " +
                              $"heading {heading * Mathf.Rad2Deg:F0}°");
            else
                report.Append($"\n    #{entry.identity} route '{(string.IsNullOrEmpty(entry.routeId) ? "UNRESOLVED" : entry.routeId)}' " +
                              $"leg {entry.legIndex} — NO WORLD POSE, not drawn");
        }

        Debug.Log(report.ToString());
    }
}
