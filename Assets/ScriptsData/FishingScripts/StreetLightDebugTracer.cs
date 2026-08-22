using UnityEngine;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// Diagnostic tracer for street lights. Auto-creates itself at play start — no scene setup — and
/// reports every lamp in the level: whether it is lit, what lit it, whether it is wired into its
/// zone's chain, what its visuals and particle cloud are doing, and whether the map icon agrees.
///
/// Output is three kinds of block, all prefixed [LampTrace] so the Console can be filtered:
///   STARTUP    — printed once shortly after load, which is the lamp that begins the level lit
///   TRANSITION — printed the instant any lamp's state changes, i.e. when one is fed a soul
///   SNAPSHOT   — printed every `snapshotInterval` seconds, if that is above 0
///
/// Every block ends with a PROBLEMS list naming anything that would stop a lamp working, so an
/// unlit lamp says why rather than leaving it to guesswork.
///
/// Turn it off with the Enabled toggle on the auto-created "StreetLightDebugTracer" object, or set
/// StreetLightDebugTracer.AutoCreate = false before play. Delete this file to remove it entirely.
/// </summary>
public class StreetLightDebugTracer : MonoBehaviour
{
    /// <summary>Set false (e.g. from another script) to stop the tracer auto-creating.</summary>
    public static bool AutoCreate = true;

    [Tooltip("Master switch — untick to silence all tracing without removing the object.")]
    public bool enabledTracing = true;

    [Tooltip("Seconds to wait before the STARTUP block, so the level has finished spawning and the " +
             "chain has lit lamp #1. Raise it if the report lands before the lamps exist.")]
    public float startupDelay = 1f;

    [Tooltip("Seconds between periodic SNAPSHOT blocks. 0 = startup and transitions only, which is " +
             "usually what you want since lamps only change when one is fed.")]
    public float snapshotInterval = 0f;

    [Tooltip("Key that dumps a full report on demand.")]
    public KeyCode dumpKey = KeyCode.F9;

    // The layer the soul-drop raycast searches; a bowl collider on any other layer can never be hit.
    const string DropLayerName = "Interaction";

    float _startupAt = -1f;
    bool  _startupDone;
    float _nextSnapshot;

    readonly Dictionary<Object, string> _lastState = new Dictionary<Object, string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!AutoCreate) return;
        var go = new GameObject("StreetLightDebugTracer") { hideFlags = HideFlags.DontSave };
        go.AddComponent<StreetLightDebugTracer>();
        DontDestroyOnLoad(go);
    }

    void Update()
    {
        if (!enabledTracing) return;

        if (_startupAt < 0f) _startupAt = Time.time + startupDelay;

        if (!_startupDone && Time.time >= _startupAt)
        {
            _startupDone = true;
            SeedBaseline();                      // so the startup state isn't reported twice
            Debug.Log(BuildReport("STARTUP"));
        }

        // Checked every frame so a lamp being fed is never missed between snapshots.
        if (_startupDone) CheckTransitions();

        if (snapshotInterval > 0f && Time.time >= _nextSnapshot)
        {
            _nextSnapshot = Time.time + snapshotInterval;
            if (_startupDone) Debug.Log(BuildReport("SNAPSHOT"));
        }

        if (Input.GetKeyDown(dumpKey)) Debug.Log(BuildReport("DUMP"));
    }

    [ContextMenu("Dump Now")]
    void DumpNow() => Debug.Log(BuildReport("DUMP"));

    // ── Transition detection ────────────────────────────────────────────────────────────────────

    // A compact signature per lamp; when it changes, something worth printing happened.
    string Signature(StreetLightController lamp)
    {
        var marker = Map != null ? Map.DebugMarkerFor(lamp) : null;
        var cloud  = lamp.DebugLitParticles;
        return $"{lamp.IsLit}|{lamp.OccupantSoulIdentity}|{lamp.chain != null}|" +
               $"{(lamp.chain != null && lamp.chain.CanFeed(lamp))}|" +
               $"{InstancedLightManager.IsContributing(lamp)}|" +
               $"{(marker != null ? marker.DebugLitState : null)}|" +
               $"{(cloud != null && cloud.isActiveAndEnabled)}";
    }

    void SeedBaseline()
    {
        foreach (var lamp in StreetLightController.All)
            if (lamp != null) _lastState[lamp] = Signature(lamp);
    }

    void CheckTransitions()
    {
        bool changed = false;
        foreach (var lamp in StreetLightController.All)
        {
            if (lamp == null) continue;
            string sig = Signature(lamp);
            if (_lastState.TryGetValue(lamp, out string prev) && prev == sig) continue;
            _lastState[lamp] = sig;
            if (prev != null) changed = true;   // first sighting is a baseline, not a change
        }

        if (changed) Debug.Log(BuildReport("TRANSITION"));
    }

    // ── Report ──────────────────────────────────────────────────────────────────────────────────

    static UIMapController Map => UIMapController.Instance;

    string BuildReport(string kind)
    {
        var sb       = new StringBuilder();
        var problems = new List<string>();

        var lamps = StreetLightController.All;
        int lit   = 0;
        foreach (var l in lamps) if (l != null && l.IsLit) lit++;

        sb.AppendLine($"[LampTrace] ===== {kind} @ t={Time.time:F2}s =====");
        sb.AppendLine($"[LampTrace] LAMPS {lamps.Count} total, {lit} lit " +
                      $"(LitLights list holds {StreetLightController.LitLights.Count}) " +
                      $"| map {(Map != null ? $"present, {Map.DebugStreetLightMarkerCount} icons" : "NO UIMapController")}");

        if (lamps.Count == 0)
        {
            sb.AppendLine("  no lamps registered — nothing spawned them, or they were destroyed");
            return sb.ToString();
        }

        foreach (var lamp in lamps)
        {
            if (lamp == null) continue;
            AppendLamp(sb, lamp, problems);
        }

        sb.AppendLine($"[LampTrace] PROBLEMS ({problems.Count})");
        if (problems.Count == 0) sb.AppendLine("  none — every lamp is wired and reporting consistently");
        else foreach (string p in problems) sb.AppendLine($"  • {p}");

        return sb.ToString();
    }

    void AppendLamp(StringBuilder sb, StreetLightController lamp, List<string> problems)
    {
        string id = $"'{lamp.name}' #{lamp.orderIndex + 1}";

        // ── State ──
        sb.AppendLine($"[LampTrace] LAMP {id} {(lamp.IsLit ? "LIT" : "unlit")}" +
                      $"{(lamp.orderIndex == 0 ? "  (starts the level lit)" : "")}");
        sb.AppendLine($"     soul={lamp.OccupantSoulIdentity} " +
                      $"inLitLights={StreetLightController.LitLights.Contains(lamp)} " +
                      $"pos={Fmt(lamp.transform.position)} activeInHierarchy={lamp.gameObject.activeInHierarchy}");

        // ── Chain: the only thing that lights lamp #1 and the only thing that lets the rest be fed ──
        if (lamp.chain == null)
        {
            sb.AppendLine("     chain=NONE — SetLit is never called and TryInsertSoul always rejects");
            problems.Add($"{id} has no chain wired (not spawned into a zone's street-light chain)");
        }
        else
        {
            bool canFeed = lamp.chain.CanFeed(lamp);
            sb.AppendLine($"     chain='{lamp.chain.name}' lit={lamp.chain.LitCount}/{lamp.chain.LightCount} " +
                          $"frontierIdx={lamp.chain.FrontierIndex} revealing={lamp.chain.IsRevealing} " +
                          $"canFeedThisOne={canFeed}");
            if (!lamp.IsLit && !canFeed)
                sb.AppendLine("     ↳ not the next lamp in path order — a soul dropped here will be refused");
        }

        // ── Drop target: what the soul-drag raycast needs to find ──
        var col = lamp.GetComponentInChildren<Collider>();
        if (col == null)
        {
            sb.AppendLine("     collider=NONE — a soul can never be dropped on this bowl");
            problems.Add($"{id} has no collider, so it cannot be fed");
        }
        else
        {
            string layer = LayerMask.LayerToName(col.gameObject.layer);
            sb.AppendLine($"     collider='{col.name}' layer='{layer}' enabled={col.enabled}");
            if (layer != DropLayerName)
                problems.Add($"{id} collider is on layer '{layer}', not '{DropLayerName}' — the drop raycast will miss it");
        }

        var indicator = lamp.GetComponent<SoulSlotIndicator>() ?? lamp.GetComponentInParent<SoulSlotIndicator>();
        sb.AppendLine($"     rangeIndicator={(indicator != null ? $"yes, inRange={indicator.IsInRange()}" : "none (range check skipped)")}");

        // ── Visuals ──
        sb.AppendLine($"     litVisual={Describe(lamp.DebugLitVisual)} " +
                      $"bowlFishVisual={Describe(lamp.DebugBowlFishVisual)}");
        if (lamp.DebugLitVisual == null)
            problems.Add($"{id} has no litVisual assigned — nothing on the lamp shows its lit state");
        else if (lamp.DebugLitVisual.activeSelf != lamp.IsLit)
            problems.Add($"{id} litVisual is {(lamp.DebugLitVisual.activeSelf ? "on" : "off")} " +
                         $"but the lamp is {(lamp.IsLit ? "lit" : "unlit")} — they should match");

        // ── Particle cloud ──
        var cloud = lamp.DebugLitParticles;
        if (cloud == null)
        {
            sb.AppendLine("     particles=UNASSIGNED (Lit Particles slot empty)");
            problems.Add($"{id} has no StreetLightParticles in its Lit Particles slot — no cloud will ever show");
        }
        else
        {
            sb.AppendLine($"     particles: {cloud.DebugSummary()}");
        }

        // ── Instanced light ──
        sb.AppendLine($"     instLight active={lamp.InstLightActive} " +
                      $"contributing={InstancedLightManager.IsContributing(lamp)} " +
                      $"pos={Fmt(lamp.InstLightPosition)} radius={lamp.InstLightRadius:F2}");
        if (lamp.IsLit && InstancedLightManager.HasPushData && !InstancedLightManager.IsContributing(lamp))
            problems.Add($"{id} is lit but dropped from the instanced-light buffer (over budget)");

        // ── Map icon ──
        var marker = Map != null ? Map.DebugMarkerFor(lamp) : null;
        if (Map == null)
        {
            sb.AppendLine("     map: no UIMapController in the scene");
        }
        else if (marker == null)
        {
            sb.AppendLine("     map: NO ICON for this lamp");
            problems.Add($"{id} has no map icon — BuildStreetLightMap ran before it spawned, or its " +
                         $"MapMarkerDescriptor has drawOnMap off, or its icon shape is not StreetLight");
        }
        else
        {
            bool? shown = marker.DebugLitState;
            sb.AppendLine($"     map: icon present, showing={(shown.HasValue ? shown.Value.ToString() : "never set")} " +
                          $"lampSays={lamp.IsLit}");

            // What SetLit writes, what the mesh holds, and what draws it. The lit look is nothing
            // but vertex colours, so if those are right the answer is in the material.
            sb.AppendLine($"     map mesh: {marker.DebugMeshState()}");

            var iconRenderer = marker.GetComponent<MeshRenderer>();
            sb.AppendLine($"     map draw: renderer={(iconRenderer != null ? $"enabled={iconRenderer.enabled}" : "NONE")} " +
                          $"material={(iconRenderer != null && iconRenderer.sharedMaterial != null ? iconRenderer.sharedMaterial.name : "NONE")} " +
                          $"shader={(iconRenderer != null && iconRenderer.sharedMaterial != null && iconRenderer.sharedMaterial.shader != null ? iconRenderer.sharedMaterial.shader.name : "NONE")} " +
                          $"iconActive={marker.gameObject.activeInHierarchy}");
            if (shown.HasValue && shown.Value != lamp.IsLit)
                problems.Add($"{id} MAP MISMATCH — icon shows {shown.Value}, lamp is {lamp.IsLit}");
            if (!shown.HasValue)
                problems.Add($"{id} map icon has never been given a state — UpdateStreetLightMarkers is not running");
        }
    }

    static string Describe(GameObject go) =>
        go == null ? "UNASSIGNED" : $"'{go.name}' active={go.activeSelf}";

    static string Fmt(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";
}
