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
/// designer draws — so the fish swim exactly the band you authored.
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

    [Header("Appearance")]
    [Tooltip("The fish image stamped on the water. Fed to _TravelSoulFishTex.")]
    [SerializeField] private Texture2D fishTexture;

    [Tooltip("World-space width/height of one fish.")]
    [SerializeField] private float fishSize = 0.6f;

    [Tooltip("Master multiplier on the drawn result. 0 hides them without stopping their travel.")]
    [Range(0f, 1f)]
    [SerializeField] private float strength = 1f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    // ── Shader globals ───────────────────────────────────
    private static readonly int PositionsID = Shader.PropertyToID("_TravelSoulFishPositions");
    private static readonly int CountID     = Shader.PropertyToID("_TravelSoulFishCount");
    private static readonly int SizeID      = Shader.PropertyToID("_TravelSoulFishSize");
    private static readonly int StrengthID  = Shader.PropertyToID("_TravelSoulFishStrength");
    private static readonly int TexID       = Shader.PropertyToID("_TravelSoulFishTex");

    private static readonly Vector4[] PositionBuffer = new Vector4[MaxDrawnFish];

    // ── Cached route shapes ──────────────────────────────
    // Rebuilt only when the asset changes, since a route's shape is authored, not simulated.
    private class RouteShape
    {
        public List<Vector3> polyline = new List<Vector3>();   // world positions, in travel order
        public List<int>     pointIndex = new List<int>();     // polyline index of each authored point
        public List<float>   cumArc   = new List<float>();     // arc length to each polyline vertex
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

        BuildRouteShapes();
        SeedOriginPools();
        ReleaseOriginPools();
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

        AdvanceTravellingSouls();
        PublishGlobals();
    }

    // ─────────────────────────────────────────────
    // ROUTE SHAPES
    // ─────────────────────────────────────────────

    private void BuildRouteShapes()
    {
        _shapes.Clear();

        foreach (var route in designerData.soulRoutes)
        {
            if (route == null || string.IsNullOrEmpty(route.routeId)) continue;

            var ids   = designerData.BuildSoulRouteNodeIds(route);
            var shape = new RouteShape();

            foreach (string id in ids)
                shape.polyline.Add(designerData.NodeWorldPosition(id));

            // Where each authored point lands in the polyline — souls travel point to point, and
            // the nodes between are just the shape of the river along the way.
            int cursor = string.IsNullOrEmpty(route.originNodeId) ? 0 : 1;
            for (int i = 0; i < route.points.Count; i++)
            {
                if (i > 0)
                {
                    var path = designerData.FindPathContaining(route.points[i - 1].nodeId,
                                                               route.points[i].nodeId);
                    if (path != null)
                    {
                        int from = path.nodeIds.IndexOf(route.points[i - 1].nodeId);
                        int to   = path.nodeIds.IndexOf(route.points[i].nodeId);
                        if (from >= 0 && to >= 0 && from != to)
                            cursor += Mathf.Abs(to - from) - 1;
                    }
                }

                shape.pointIndex.Add(Mathf.Min(cursor, shape.polyline.Count - 1));
                cursor++;
            }

            float arc = 0f;
            shape.cumArc.Add(0f);
            for (int i = 1; i < shape.polyline.Count; i++)
            {
                arc += Vector3.Distance(shape.polyline[i - 1], shape.polyline[i]);
                shape.cumArc.Add(arc);
            }

            _shapes[route.routeId] = shape;

            if (verboseLogging)
                Debug.Log($"[TravelFish] Route '{route.displayName}' — {shape.polyline.Count} vertices, " +
                          $"{shape.pointIndex.Count} points, {arc:F1}m long.");
        }

        Debug.Log($"[TravelFish] Built {_shapes.Count} route shape(s).");
    }

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
            if (route?.souls == null) continue;

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
    /// Empties every route's origin pool into the water the moment the map loads, so the souls
    /// authored onto a route set off along it straight away.
    ///
    /// The route is opened to its last point here too. Nothing else opens one yet, and a soul
    /// never swims past the open end — so without this the pool would empty and the fish would
    /// stop dead at the first point. When openness becomes real player progress, that line is the
    /// one to delete.
    /// </summary>
    private void ReleaseOriginPools()
    {
        int released = 0;

        foreach (var route in designerData.soulRoutes)
        {
            if (route?.souls == null) continue;
            if (string.IsNullOrEmpty(route.routeId) || route.points.Count == 0) continue;

            SoulJourneyData.OpenRouteTo(route.routeId, route.points.Count - 1);

            foreach (var soul in route.souls)
            {
                if (soul == null || soul.soulDataIdentity <= 0) continue;

                // Only the pool. A soul already travelling, arrived or caught keeps what it is
                // doing, so re-running this on every load cannot drag one backwards.
                if (SoulJourneyData.Where(soul.soulDataIdentity).place != SoulPlace.OriginPool) continue;

                SoulJourneyData.Release(soul.soulDataIdentity, route.routeId, 0);
                released++;
            }
        }

        if (released > 0)
            Debug.Log($"[TravelFish] Released {released} soul(s) from their origin pools onto the river.");
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
    // MOVEMENT
    // ─────────────────────────────────────────────

    private void AdvanceTravellingSouls()
    {
        float step = swimSpeed * Time.deltaTime;
        if (step <= 0f) return;

        foreach (var entry in SoulJourneyData.Travelling())
        {
            if (string.IsNullOrEmpty(entry.routeId)) continue;              // still unresolved
            if (!_shapes.TryGetValue(entry.routeId, out var shape)) continue;

            var route = designerData.soulRoutes.Find(r => r.routeId == entry.routeId);
            if (route == null || entry.legIndex < 0 || entry.legIndex >= route.points.Count) continue;

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

            if (progress >= 1f)
            {
                var arrivedAt = route.points[entry.legIndex];

                // An arena point is a destination, not a waypoint: the soul joins that level.
                if (arrivedAt.kind == LevelSelectDesignerData.SoulRoutePoint.PointKind.Arena)
                {
                    var arena = designerData.arenas.Find(a => a.nodeId == arrivedAt.nodeId);
                    if (arena?.gridData != null)
                    {
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
    // PUBLISHING
    // ─────────────────────────────────────────────

    private void PublishGlobals()
    {
        var travelling = SoulJourneyData.Travelling();
        int count = 0;

        for (int i = 0; i < travelling.Count && count < MaxDrawnFish; i++)
        {
            if (!TryWorldPose(travelling[i], out Vector3 pos, out float heading)) continue;

            // .x/.z world position, .y heading in radians, .w per-fish size (0 = use the global).
            PositionBuffer[count] = new Vector4(pos.x, heading, pos.z, 0f);
            count++;
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

        LogPositions(travelling, count);
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
            Debug.LogWarning("[TravelFish] Nothing is on the river, so nothing will draw — every origin pool is " +
                             "empty and no soul has departed a level.", this);
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
