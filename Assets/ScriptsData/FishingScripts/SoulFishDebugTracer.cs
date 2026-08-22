using UnityEngine;
using UnityEngine.Splines;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// Diagnostic tracer for the soul-fish street-light / fish-bowl systems. Auto-creates itself at
/// play start — no scene setup — and reports where every fish is, what route it is on, and every
/// handoff between systems (bowl landing, pool bloom, tributary join, route rebuild, gate wrap).
///
/// Output is two kinds of block, both prefixed [FishTrace] so you can filter the Console:
///   TRANSITION — printed the instant any tracked state changes (the interesting moments)
///   SNAPSHOT   — printed every `snapshotInterval` seconds (movement over time)
///
/// Turn it off with the Enabled toggle on the auto-created "SoulFishDebugTracer" object, or set
/// SoulFishDebugTracer.AutoCreate = false before play. Delete this file to remove it entirely.
/// </summary>
public class SoulFishDebugTracer : MonoBehaviour
{
    /// <summary>Set false (e.g. from another script) to stop the tracer auto-creating.</summary>
    public static bool AutoCreate = true;

    [Tooltip("Master switch — untick to silence all tracing without removing the object.")]
    public bool enabledTracing = true;

    [Tooltip("Seconds between periodic SNAPSHOT blocks. 0 = transitions only.")]
    public float snapshotInterval = 2f;

    [Tooltip("Include a line per fish (position, spline progress, speed). Off = per-zone summary only.")]
    public bool perFishDetail = true;

    [Tooltip("Max fish detailed per zone, so a big shoal doesn't flood the Console.")]
    public int maxFishPerZone = 6;

    float _nextSnapshot;
    readonly Dictionary<Transform, Vector3> _lastPos = new Dictionary<Transform, Vector3>();
    float _lastSampleTime;

    // Tracked state, for transition detection.
    readonly Dictionary<Object, string> _lastState = new Dictionary<Object, string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!AutoCreate) return;
        var go = new GameObject("SoulFishDebugTracer") { hideFlags = HideFlags.DontSave };
        go.AddComponent<SoulFishDebugTracer>();
        DontDestroyOnLoad(go);
    }

    void Update()
    {
        if (!enabledTracing) return;

        // Transitions are checked every frame so nothing is missed between snapshots.
        CheckTransitions();

        if (snapshotInterval > 0f && Time.time >= _nextSnapshot)
        {
            _nextSnapshot = Time.time + snapshotInterval;
            Debug.Log(BuildReport("SNAPSHOT"));
            _lastSampleTime = Time.time;
        }
    }

    // A compact signature per controller; when it changes, something meaningful happened.
    void CheckTransitions()
    {
        bool changed = false;

        foreach (var shoal in FindObjectsByType<SoulShoalController>(FindObjectsSortMode.None))
        {
            string sig = $"{shoal.IsActive}|{shoal.CanFish}|{shoal.IsBowlLanded}|{shoal.FishList.Count}";
            if (Track(shoal, sig)) changed = true;
        }
        foreach (var chain in FindObjectsByType<SoulZoneStreetLightChain>(FindObjectsSortMode.None))
        {
            string sig = $"{chain.LitCount}|{chain.IsRevealing}|{chain.FrontierDense}";
            if (Track(chain, sig)) changed = true;
        }
        foreach (var link in FindObjectsByType<SoulZoneTributaryLink>(FindObjectsSortMode.None))
        {
            string sig = $"{link.BowlLanded}|{link.PoolRegistered}|{link.PoolFullyOpen}|" +
                         $"{link.RiverPassed}|{link.IsJoined}|{link.RouteFrontier}";
            if (Track(link, sig)) changed = true;
        }

        if (changed) Debug.Log(BuildReport("TRANSITION"));
    }

    bool Track(Object key, string sig)
    {
        if (_lastState.TryGetValue(key, out string prev) && prev == sig) return false;
        _lastState[key] = sig;
        return prev != null;   // first sighting isn't a transition, just a baseline
    }

    string BuildReport(string kind)
    {
        float dt = Mathf.Max(Time.time - _lastSampleTime, 1e-4f);
        var sb = new StringBuilder();
        sb.AppendLine($"[FishTrace] ===== {kind} @ t={Time.time:F2}s =====");

        // ── Shoals ──
        var shoals = FindObjectsByType<SoulShoalController>(FindObjectsSortMode.None);
        sb.AppendLine($"[FishTrace] SHOALS ({shoals.Length})");
        foreach (var s in shoals)
        {
            int alive = 0;
            foreach (var f in s.FishList) if (f != null) alive++;
            sb.AppendLine($"  '{s.name}' fish={alive}/{s.FishList.Count} active={s.IsActive} " +
                          $"canFish={s.CanFish} bowlLanded={s.IsBowlLanded} maskExternal={s.maskOwnedExternally} " +
                          $"pos={Fmt(s.transform.position)}");
        }

        // ── River chains ──
        var chains = FindObjectsByType<SoulZoneStreetLightChain>(FindObjectsSortMode.None);
        sb.AppendLine($"[FishTrace] RIVER CHAINS ({chains.Length})");
        foreach (var c in chains)
        {
            c.DebugWindow(out float ws, out float we);
            sb.AppendLine($"  '{c.name}' lit={c.LitCount}/{c.LightCount} revealing={c.IsRevealing} " +
                          $"frontierIdx={c.FrontierIndex} frontierDense={c.FrontierDense} " +
                          $"frontierR={c.FrontierRadius:F2} circleWindow=[{ws:F3}..{we:F3}] " +
                          $"pathPts={(c.RegPath != null ? c.RegPath.Count : 0)} revealedPts={c.RevealedRegPath.Count}");
            if (c.RegPath != null && c.RegPath.Count > 0)
                sb.AppendLine($"     frontierPos={Fmt(c.RegPath[Mathf.Clamp(c.FrontierDense, 0, c.RegPath.Count - 1)])}");
            AppendFish(sb, c.DebugFish, dt);
        }

        // ── Tributaries ──
        var links = FindObjectsByType<SoulZoneTributaryLink>(FindObjectsSortMode.None);
        sb.AppendLine($"[FishTrace] TRIBUTARIES ({links.Length})");
        foreach (var l in links)
        {
            l.DebugWindow(out float ws, out float we);
            sb.AppendLine($"  '{l.name}' bowlLanded={l.BowlLanded} poolReg={l.PoolRegistered} " +
                          $"poolFull={l.PoolFullyOpen} riverPassed={l.RiverPassed} joined={l.IsJoined}");
            sb.AppendLine($"     junctionDense={l.JunctionDense} mainFrontierDense={l.MainFrontierDense} " +
                          $"routeBuilt={l.RouteBuilt} routeFrontier={l.RouteFrontier} " +
                          $"circleWindow=[{ws:F3}..{we:F3}]");
            AppendFish(sb, l.DebugFish, dt);
        }

        // ── Mask registration ──
        sb.AppendLine($"[FishTrace] MASK  waveZones={SoulFishWaveLinker.ActiveZoneCount} " +
                      $"wavePacked={SoulFishWaveLinker.PackedPointCount}/40 " +
                      $"looseFish={SoulFishWaveLinker.ActiveFish.Count}");

        // ── Street lights ──
        var lamps = FindObjectsByType<StreetLightController>(FindObjectsSortMode.None);
        sb.Append($"[FishTrace] LIGHTS ({lamps.Length}) ");
        foreach (var lamp in lamps)
            sb.Append($"[#{lamp.orderIndex + 1} lit={lamp.IsLit} soul={lamp.OccupantSoulIdentity} " +
                      $"pos={Fmt(lamp.transform.position)}] ");
        sb.AppendLine();

        return sb.ToString();
    }

    void AppendFish(StringBuilder sb, IReadOnlyList<SplineAnimate> fish, float dt)
    {
        if (!perFishDetail || fish == null || fish.Count == 0)
        {
            sb.AppendLine($"     fish tracked: {(fish != null ? fish.Count : 0)}");
            return;
        }

        int shown = Mathf.Min(fish.Count, maxFishPerZone);
        for (int i = 0; i < shown; i++)
        {
            var sa = fish[i];
            if (sa == null) { sb.AppendLine($"     fish[{i}] <null>"); continue; }

            Vector3 p = sa.transform.position;
            float speed = 0f;
            if (_lastPos.TryGetValue(sa.transform, out var prev)) speed = Vector3.Distance(prev, p) / dt;
            _lastPos[sa.transform] = p;

            sb.AppendLine($"     fish[{i}] t={sa.NormalizedTime:F3} offset={sa.StartOffset:F3} " +
                          $"pos={Fmt(p)} speed={speed:F2}/s playing={sa.IsPlaying}");
        }
        if (fish.Count > shown) sb.AppendLine($"     … +{fish.Count - shown} more");
    }

    static string Fmt(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
}
