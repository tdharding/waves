using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

/// <summary>
/// A timed play mode performance test that cues each step on screen and logs what every frame
/// cost, tagged with the step it belongs to.
///
/// Press Start, enter play mode, follow the cues over the Game view. Writes three files to Logs/:
///   PlayTest_[stamp].csv          one line a second - frame rate, where the frame time went, renders
///   PlayTest_[stamp]_gpu.csv      one line a second - Unity's CPU and graphics chip use, from Windows
///   PlayTest_[stamp]_summary.txt  averages per step, and the slowest scripts per step
///
/// Entering play mode reloads the domain, so the run state lives in SessionState and the driver is
/// static; the window is only the buttons.
/// </summary>
[InitializeOnLoad]
static class PlayTestRunner
{
    // ── The steps ────────────────────────────────────────────────────────────
    struct Step { public string Name, Cue; public int Seconds; public Type Disable; }

    static readonly Step[] StandardSteps =
    {
        new Step { Name = "1 Sailing, Scene view visible", Seconds = 60,
                   Cue  = "Step 1 of 3 - Sail around. Leave the Scene view visible." },
        new Step { Name = "2 Sailing, Scene view hidden",  Seconds = 60,
                   Cue  = "Step 2 of 3 - Hide the Scene view (click the Game tab, or Shift+Space over Game). Keep sailing." },
        new Step { Name = "3 Sitting still",               Seconds = 30,
                   Cue  = "Step 3 of 3 - Stop the boat. Hands off." },
    };

    // The steps this run is using: the fixed list above, or the hunt's, built once play has settled.
    static List<Step> Steps = new();

    const int TransitionSeconds = 5;   // warning before each step; logged, but left out of the summary
    const int HuntSeconds       = 15;

    /// <summary>
    /// LateUpdate hunt: sit still while each script with a LateUpdate is switched off in turn, so
    /// the one whose absence takes the LateUpdate time away names itself. Switched off in play
    /// mode only, and switched back on after its step — exiting play mode would revert it anyway.
    /// </summary>
    public static bool HuntMode
    {
        get => SessionState.GetBool("PlayTest.Hunt", false);
        set => SessionState.SetBool("PlayTest.Hunt", value);
    }

    static readonly Dictionary<Type, List<Behaviour>> _huntTargets = new();
    static readonly List<Behaviour> _disabled = new();
    static int _appliedStep = -1;
    const string KeyArmed  = "PlayTest.Armed";
    const string KeySettle = "PlayTest.Settle";

    public static int SettleSeconds
    {
        get => SessionState.GetInt(KeySettle, 10);
        set => SessionState.SetInt(KeySettle, Mathf.Clamp(value, 3, 120));
    }

    public static bool Armed   => SessionState.GetBool(KeyArmed, false);
    public static bool Running => _running;
    public static string CurrentCue { get; private set; } = "";

    // Kept in SessionState: leaving play mode reloads the domain straight after the summary is written.
    public static string LastSummaryPath
    {
        get => SessionState.GetString("PlayTest.LastSummary", "");
        private set => SessionState.SetString("PlayTest.LastSummary", value);
    }

    // ── Run state (rebuilt after the play mode domain reload) ───────────────
    static bool _running, _recordersStarted;
    static double _runStart, _nextSample;
    static int _lastFrameCount;
    static string _stamp, _csvPath, _gpuPath;
    static Process _gpuProcess;

    struct Rec { public string Label; public ProfilerRecorder R; public bool Script, Fog; }
    static readonly List<Rec> _recs = new();

    static readonly Dictionary<string, int> _renders = new();
    static readonly Dictionary<string, int> _stepOfTime = new();

    class StepTotals
    {
        public int Seconds, Frames;
        public double FrameMs;
        public long FogNear, FogDots, FogBlobs;                            // summed over seconds
        public readonly Dictionary<string, double> Ms = new();       // label -> summed ms over frames
        public readonly Dictionary<string, double> Renders = new();  // camera kind -> renders summed over seconds
    }
    static readonly Dictionary<int, StepTotals> _totals = new();

    static readonly (string label, string marker)[] EngineMarkers =
    {
        ("PlayerLoop",        "PlayerLoop"),
        ("Scripts Update",    "Update.ScriptRunBehaviourUpdate"),
        ("Scripts LateUpdate","PreLateUpdate.ScriptRunBehaviourLateUpdate"),
        ("Scripts FixedUpdate","FixedUpdate.ScriptRunBehaviourFixedUpdate"),
        ("Physics",           "FixedUpdate.PhysicsFixedUpdate"),
        ("Rendering",         "PostLateUpdate.FinishFrameRendering"),
        ("Waiting on GPU",    "Gfx.WaitForPresentOnGfxThread"),
        ("Editor",            "EditorLoop"),
    };

    static PlayTestRunner()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    // ── Buttons ──────────────────────────────────────────────────────────────
    public static void Arm()
    {
        SessionState.SetBool(KeyArmed, true);
        CurrentCue = "Armed - enter play mode.";
        Cue(CurrentCue);
    }

    public static void Abort()
    {
        SessionState.SetBool(KeyArmed, false);
        if (_running) Finish("aborted");
        CurrentCue = "";
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode && _running)
            Finish("play mode exited");
    }

    // ── Driver ───────────────────────────────────────────────────────────────
    static void Tick()
    {
        if (!Armed) return;

        if (!EditorApplication.isPlaying)
        {
            if (!_running) CurrentCue = "Armed - enter play mode.";
            return;
        }

        if (!_running) Begin();

        double now = EditorApplication.timeSinceStartup;
        if (now < _nextSample) return;
        _nextSample = now + 1.0;

        double t = now - _runStart;
        if (HuntMode && Steps.Count == 0 && t >= SettleSeconds) BuildHuntSteps();

        int step = StepAt(t, out int remaining, out bool transition, out bool done);

        if (done) { Finish("complete"); return; }

        // A step's switch-off happens at the start of its warning, so the frame has settled by the
        // time its own seconds are measured.
        if (t >= SettleSeconds && step != _appliedStep) ApplyStep(step);

        // Started when step 1 does, not on entering play mode: a script's timing marker only exists
        // once it has run, and the intro may not have spawned half of them yet.
        if (t >= SettleSeconds && !_recordersStarted)
        {
            StartRecorders();
            _recordersStarted = true;
        }

        if (t < SettleSeconds)
            CurrentCue = HuntMode
                ? $"Get ready - the hunt starts in {remaining}s. Stop the boat and keep your hands off for the whole test."
                : $"Get ready - the test starts in {remaining}s. Get the boat on the water.";
        else if (transition)
            CurrentCue = $"Next in {remaining}s: {Steps[step].Cue}";
        else
            CurrentCue = $"{Steps[step].Cue}   {remaining}s";
        Cue(CurrentCue);

        Sample(t < SettleSeconds ? -1 : step, transition || t < SettleSeconds);
    }

    /// <summary>Which step a moment of the run falls in, and how long is left of that part.</summary>
    static int StepAt(double t, out int remaining, out bool transition, out bool done)
    {
        done = false; transition = false;
        double at = SettleSeconds;
        if (t < at) { remaining = Mathf.CeilToInt((float)(at - t)); return 0; }

        for (int i = 0; i < Steps.Count; i++)
        {
            if (i > 0)
            {
                if (t < at + TransitionSeconds)
                {
                    transition = true;
                    remaining = Mathf.CeilToInt((float)(at + TransitionSeconds - t));
                    return i;
                }
                at += TransitionSeconds;
            }
            if (t < at + Steps[i].Seconds)
            {
                remaining = Mathf.CeilToInt((float)(at + Steps[i].Seconds - t));
                return i;
            }
            at += Steps[i].Seconds;
        }
        done = true; remaining = 0;
        return Steps.Count - 1;
    }

    static void BuildHuntSteps()
    {
        _huntTargets.Clear();
        foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (mb == null || !mb.isActiveAndEnabled || !HasLateUpdate(mb.GetType())) continue;
            if (!_huntTargets.TryGetValue(mb.GetType(), out var list)) _huntTargets[mb.GetType()] = list = new List<Behaviour>();
            list.Add(mb);
        }

        Steps.Add(new Step { Name = "Baseline, everything on", Seconds = HuntSeconds,
                             Cue = "Hunt - hands off. Baseline, everything on." });
        foreach (var type in _huntTargets.Keys.OrderBy(k => k.Name))
            Steps.Add(new Step { Name = $"{type.Name} off ({_huntTargets[type].Count})", Seconds = HuntSeconds, Disable = type,
                                 Cue = $"Hunt - hands off. {type.Name} switched off." });
        Steps.Add(new Step { Name = "Baseline again, everything on", Seconds = HuntSeconds,
                             Cue = "Hunt - hands off. Baseline again, everything back on." });
    }

    static bool HasLateUpdate(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            if (t.GetMethod("LateUpdate", flags, null, Type.EmptyTypes, null) != null) return true;
        return false;
    }

    static void ApplyStep(int step)
    {
        RestoreDisabled();
        _appliedStep = step;
        if (step < 0 || step >= Steps.Count || Steps[step].Disable == null) return;
        if (!_huntTargets.TryGetValue(Steps[step].Disable, out var list)) return;
        foreach (var b in list)
            if (b != null && b.enabled) { b.enabled = false; _disabled.Add(b); }
    }

    static void RestoreDisabled()
    {
        foreach (var b in _disabled) if (b != null) b.enabled = true;
        _disabled.Clear();
    }

    static int TotalSeconds =>SettleSeconds + Steps.Sum(s => s.Seconds) + TransitionSeconds * (Steps.Count - 1);

    static void Begin()
    {
        _running = true;
        _runStart = EditorApplication.timeSinceStartup;
        _nextSample = _runStart + 1.0;
        _lastFrameCount = Time.frameCount;
        _stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string logs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
        Directory.CreateDirectory(logs);
        _csvPath = Path.Combine(logs, $"PlayTest_{_stamp}.csv");
        _gpuPath = Path.Combine(logs, $"PlayTest_{_stamp}_gpu.csv");

        _totals.Clear(); _renders.Clear(); _stepOfTime.Clear();
        DisposeRecorders();
        _recordersStarted = false;
        Steps = HuntMode ? new List<Step>() : StandardSteps.ToList();
        _huntTargets.Clear(); _disabled.Clear(); _appliedStep = -1;

        File.WriteAllText(_csvPath,
            "time,step,fps,frameMs," + string.Join(",", EngineMarkers.Select(m => m.label)) +
            ",sceneRenders,gameRenders,previewRenders,otherRenders\n");

        RenderPipelineManager.beginCameraRendering += OnCamera;
        // The hunt's length is not known until its scripts are found; the logger is stopped at Finish.
        StartGpuLogger(HuntMode ? 900 : TotalSeconds + 15);
    }

    static void StartRecorders()
    {
        DisposeRecorders();

        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        var byName = new Dictionary<string, ProfilerRecorderHandle>();
        foreach (var h in handles)
        {
            var d = ProfilerRecorderHandle.GetDescription(h);
            if (!byName.ContainsKey(d.Name)) byName[d.Name] = h;
        }

        if (byName.TryGetValue("Main Thread", out var main))
            _recs.Add(new Rec { Label = "Main Thread", R = new ProfilerRecorder(main, 300) });

        foreach (var (label, marker) in EngineMarkers)
            if (byName.TryGetValue(marker, out var h))
                _recs.Add(new Rec { Label = label, R = new ProfilerRecorder(h, 300) });

        // Individual scripts: the editor names each callback "Type.Method() [Invoke]". Taken from
        // whatever is registered now, so it only finds scripts that have already run once.
        foreach (var kv in byName.Where(k => k.Key.EndsWith("() [Invoke]")).Take(80))
            _recs.Add(new Rec { Label = kv.Key.Replace(" [Invoke]", ""), R = new ProfilerRecorder(kv.Value, 300), Script = true });

        // The fog's own section markers (FogFieldManager), when that script is in the scene.
        foreach (var kv in byName.Where(k => k.Key.StartsWith("Fog.")))
            _recs.Add(new Rec { Label = kv.Key, R = new ProfilerRecorder(kv.Value, 300), Fog = true });

        foreach (var r in _recs) r.R.Start();
    }

    static void DisposeRecorders()
    {
        foreach (var r in _recs) r.R.Dispose();
        _recs.Clear();
    }

    static void OnCamera(ScriptableRenderContext ctx, Camera cam)
    {
        string kind = cam.cameraType switch
        {
            CameraType.SceneView => "scene",
            CameraType.Game      => "game",
            CameraType.Preview   => "preview",
            _                    => "other",
        };
        _renders.TryGetValue(kind, out int n);
        _renders[kind] = n + 1;
    }

    static double SumMs(ProfilerRecorder r, out int count)
    {
        count = r.Count;
        long sum = 0;
        for (int i = 0; i < count; i++) sum += r.GetSample(i).Value;
        return sum / 1e6;
    }

    static void Sample(int step, bool excluded)
    {
        int frames = Mathf.Max(Time.frameCount - _lastFrameCount, 1);
        _lastFrameCount = Time.frameCount;

        var ms = new Dictionary<string, double>();
        double frameMs = 0;
        foreach (var rec in _recs)
        {
            double sum = SumMs(rec.R, out int count);
            if (rec.Label == "Main Thread") frameMs = count > 0 ? sum / count : 0;
            else ms[rec.Label] = sum;
            // Reset also STOPS a recorder, so it has to be started again. The first run lost every
            // timing to this: one second of samples, then zeros for the rest of the test.
            rec.R.Reset();
            rec.R.Start();
        }

        string time = DateTime.Now.ToString("HH:mm:ss");
        string stepName = step < 0 ? "settle" : excluded ? "transition" : Steps[step].Name;
        _stepOfTime[time] = excluded || step < 0 ? -1 : step;

        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(time).Append(',').Append(stepName).Append(',')
          .Append(frames.ToString(inv)).Append(',').Append(frameMs.ToString("0.00", inv));
        foreach (var (label, _) in EngineMarkers)
            sb.Append(',').Append(ms.TryGetValue(label, out var v) ? (v / frames).ToString("0.00", inv) : "n/a");
        foreach (var kind in new[] { "scene", "game", "preview", "other" })
            sb.Append(',').Append(_renders.TryGetValue(kind, out int n) ? n : 0);
        sb.Append('\n');
        File.AppendAllText(_csvPath, sb.ToString());

        if (!excluded && step >= 0)
        {
            if (!_totals.TryGetValue(step, out var tot)) _totals[step] = tot = new StepTotals();
            tot.Seconds++;
            tot.Frames += frames;
            tot.FrameMs += frameMs * frames;
            tot.FogNear  += FogFieldManager.NearRepellerCount;
            tot.FogDots  += FogFieldManager.DotTotal;
            tot.FogBlobs += FogFieldManager.BlobCount;
            foreach (var kv in ms)
                tot.Ms[kv.Key] = (tot.Ms.TryGetValue(kv.Key, out var a) ? a : 0) + kv.Value;
            foreach (var kv in _renders)
                tot.Renders[kv.Key] = (tot.Renders.TryGetValue(kv.Key, out var a) ? a : 0) + kv.Value;
        }
        _renders.Clear();
    }

    // ── Graphics chip, from Windows ─────────────────────────────────────────
    const string GpuScript = @"param($unityPid, $out, $seconds)
$cores = [Environment]::ProcessorCount
'time,cpuPct,gpuPct' | Out-File $out -Encoding ascii
$end = (Get-Date).AddSeconds([int]$seconds)
while ((Get-Date) -lt $end) {
  $p = Get-Process -Id $unityPid -ErrorAction SilentlyContinue
  if (-not $p) { break }
  $t0 = $p.TotalProcessorTime.TotalMilliseconds; $w0 = Get-Date
  $s = (Get-Counter ""\GPU Engine(pid_$($unityPid)*engtype_3D)\Utilization Percentage"" -SampleInterval 1 -MaxSamples 1 -ErrorAction SilentlyContinue).CounterSamples
  $p.Refresh()
  $cpu = ($p.TotalProcessorTime.TotalMilliseconds - $t0) / ((Get-Date) - $w0).TotalMilliseconds / $cores * 100
  $g = 0; foreach ($x in $s) { $g += $x.CookedValue }
  [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0},{1:0.0},{2:0.0}', (Get-Date -Format 'HH:mm:ss'), $cpu, $g) | Out-File $out -Append -Encoding ascii
}";

    static void StartGpuLogger(int seconds)
    {
        try
        {
            string ps1 = Path.Combine(Path.GetDirectoryName(_gpuPath), "PlayTest_gpu_logger.ps1");
            File.WriteAllText(ps1, GpuScript);
            var info = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ps1}\" " +
                $"-unityPid {Process.GetCurrentProcess().Id} -out \"{_gpuPath}\" -seconds {seconds}")
            { CreateNoWindow = true, UseShellExecute = false };
            _gpuProcess = Process.Start(info);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlayTest] Graphics chip logger did not start: {e.Message}. Frame timings still log.");
        }
    }

    static Dictionary<int, (double cpu, double gpu, int n)> ReadGpuPerStep()
    {
        var result = new Dictionary<int, (double, double, int)>();
        if (!File.Exists(_gpuPath)) return result;
        foreach (var line in File.ReadAllLines(_gpuPath).Skip(1))
        {
            var parts = line.Trim().Split(',');
            if (parts.Length < 3 || !_stepOfTime.TryGetValue(parts[0], out int step) || step < 0) continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double cpu)) continue;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double gpu)) continue;
            result.TryGetValue(step, out var acc);
            result[step] = (acc.Item1 + cpu, acc.Item2 + gpu, acc.Item3 + 1);
        }
        return result;
    }

    // ── Finish ───────────────────────────────────────────────────────────────
    static void Finish(string how)
    {
        _running = false;
        SessionState.SetBool(KeyArmed, false);
        RenderPipelineManager.beginCameraRendering -= OnCamera;
        RestoreDisabled();

        try { if (_gpuProcess != null && !_gpuProcess.HasExited) _gpuProcess.Kill(); } catch { }
        _gpuProcess = null;

        var gpu = ReadGpuPerStep();
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"PLAY MODE TEST  {_stamp}  ({how})");
        sb.AppendLine($"per-second log: {Path.GetFileName(_csvPath)}   graphics chip: {Path.GetFileName(_gpuPath)}");
        int scriptRecs = _recs.Count(r => r.Script);
        sb.AppendLine(scriptRecs > 0
            ? $"individual scripts watched: {scriptRecs}"
            : "individual scripts: none found - Unity did not expose per-script timings; use the Profiler for that");
        sb.AppendLine();

        if (HuntMode) AppendHuntTable(sb, gpu);

        for (int i = 0; i < Steps.Count; i++)
        {
            sb.AppendLine($"== {Steps[i].Name} ==");
            if (!_totals.TryGetValue(i, out var t) || t.Frames == 0) { sb.AppendLine("  not reached"); sb.AppendLine(); continue; }

            sb.AppendLine($"  {t.Seconds}s   fps {(double)t.Frames / t.Seconds:0.0}   frame {t.FrameMs / t.Frames:0.00} ms");
            if (gpu.TryGetValue(i, out var g) && g.n > 0)
                sb.AppendLine($"  Unity CPU {g.cpu / g.n:0.0}%   graphics chip {g.gpu / g.n:0.0}%");
            else
                sb.AppendLine("  graphics chip: no samples");

            sb.AppendLine("  where the frame went (ms per frame):");
            foreach (var (label, _) in EngineMarkers)
                sb.AppendLine(t.Ms.TryGetValue(label, out var v)
                    ? $"    {label,-20} {(v / t.Frames).ToString("0.00", inv),7}"
                    : $"    {label,-20}     n/a");

            sb.Append("  renders per second:");
            foreach (var kind in new[] { "game", "scene", "preview", "other" })
                sb.Append($"  {kind} {(t.Renders.TryGetValue(kind, out var r) ? r / t.Seconds : 0):0.0}");
            sb.AppendLine();

            var fogRecs = _recs.Where(r => r.Fog).OrderBy(r => r.Label).ToList();
            if (fogRecs.Count > 0)
            {
                sb.AppendLine($"  fog: {(double)t.FogBlobs / t.Seconds:0} masses   {(double)t.FogDots / t.Seconds:0} dots   " +
                              $"{(double)t.FogNear / t.Seconds:0} nearby repellers");
                sb.AppendLine("  fog breakdown (ms per frame):");
                foreach (var fr in fogRecs)
                    sb.AppendLine($"    {fr.Label,-40} {(t.Ms.TryGetValue(fr.Label, out var fv) ? fv / t.Frames : 0).ToString("0.00", inv),7}");
            }

            var top = _recs.Where(r => r.Script)
                           .Select(r => (r.Label, ms: t.Ms.TryGetValue(r.Label, out var v) ? v / t.Frames : 0))
                           .Where(x => x.ms > 0.005)
                           .OrderByDescending(x => x.ms).Take(10).ToList();
            if (top.Count > 0)
            {
                sb.AppendLine("  slowest scripts (ms per frame):");
                foreach (var (label, ms) in top) sb.AppendLine($"    {label,-55} {ms.ToString("0.000", inv),7}");
            }
            sb.AppendLine();
        }

        DisposeRecorders();

        LastSummaryPath = Path.Combine(Path.GetDirectoryName(_csvPath), $"PlayTest_{_stamp}_summary.txt");
        File.WriteAllText(LastSummaryPath, sb.ToString());
        CurrentCue = how == "complete" ? "Done - exit play mode. Summary written to Logs." : $"Stopped ({how}). Summary written to Logs.";
        Cue(CurrentCue);
        Debug.Log($"[PlayTest] {how}. Summary: {LastSummaryPath}\n\n{sb}");
    }

    /// <summary>
    /// One line per hunt step: the LateUpdate time with that script off, and how far it fell from
    /// the two baselines. The script whose line drops furthest is the one doing the work.
    /// </summary>
    static void AppendHuntTable(StringBuilder sb, Dictionary<int, (double cpu, double gpu, int n)> gpu)
    {
        double Late(int i) => _totals.TryGetValue(i, out var t) && t.Frames > 0 && t.Ms.TryGetValue("Scripts LateUpdate", out var v)
            ? v / t.Frames : double.NaN;

        var baselines = Enumerable.Range(0, Steps.Count)
                                  .Where(i => Steps[i].Disable == null && !double.IsNaN(Late(i)))
                                  .Select(Late).ToList();
        double baseline = baselines.Count > 0 ? baselines.Average() : double.NaN;

        sb.AppendLine("LATEUPDATE HUNT  (sitting still; Scripts LateUpdate ms per frame)");
        sb.AppendLine($"  baseline {baseline:0.00} ms");
        sb.AppendLine($"  {"step",-52} {"late ms",8} {"saved",8} {"fps",6} {"chip %",7}");
        for (int i = 0; i < Steps.Count; i++)
        {
            double late = Late(i);
            if (double.IsNaN(late)) { sb.AppendLine($"  {Steps[i].Name,-52} not reached"); continue; }
            var t = _totals[i];
            string saved = Steps[i].Disable == null ? "" : (baseline - late).ToString("0.00", CultureInfo.InvariantCulture);
            string chip  = gpu.TryGetValue(i, out var g) && g.n > 0 ? (g.gpu / g.n).ToString("0.0", CultureInfo.InvariantCulture) : "-";
            sb.AppendLine($"  {Steps[i].Name,-52} {late.ToString("0.00", CultureInfo.InvariantCulture),8} {saved,8} " +
                          $"{((double)t.Frames / t.Seconds).ToString("0.0", CultureInfo.InvariantCulture),6} {chip,7}");
        }
        sb.AppendLine();
    }

    // ── Cues ─────────────────────────────────────────────────────────────────
    static void Cue(string text)
    {
        var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
        var target = gameViewType != null
            ? Resources.FindObjectsOfTypeAll(gameViewType).OfType<EditorWindow>().FirstOrDefault()
            : null;
        if (target == null) target = EditorWindow.focusedWindow;
        if (target != null) target.ShowNotification(new GUIContent(text), 1.5);

        foreach (var w in Resources.FindObjectsOfTypeAll<PlayTestWindow>()) w.Repaint();
    }
}

public class PlayTestWindow : EditorWindow
{
    [MenuItem("Tools/Waves/Play Test")]
    public static void Open() => GetWindow<PlayTestWindow>("Play Test");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Play mode performance test", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(PlayTestRunner.Armed))
            PlayTestRunner.HuntMode = GUILayout.Toolbar(PlayTestRunner.HuntMode ? 1 : 0,
                                                        new[] { "Standard test", "LateUpdate hunt" }) == 1;

        EditorGUILayout.HelpBox(PlayTestRunner.HuntMode
            ? "Press Start, then enter play mode, stop the boat and keep your hands off. Every script " +
              "with a LateUpdate is switched off for 15s in turn, between two baselines, and the " +
              "summary names which one takes the LateUpdate time away. Switched back on after each " +
              "step; nothing in the scene is changed."
            : "Press Start, then enter play mode. Cues appear over the Game view: sail with the Scene " +
              "view visible (60s), sail with it hidden (60s), sit still (30s).",
            MessageType.Info);
        EditorGUILayout.HelpBox("Close Shader Graph windows and turn Apply Live off first, so they are not measured too.",
                                MessageType.None);

        using (new EditorGUI.DisabledScope(PlayTestRunner.Armed))
            PlayTestRunner.SettleSeconds = EditorGUILayout.IntField(
                new GUIContent("Seconds before step 1", "Time after entering play mode to get past the intro and onto the water."),
                PlayTestRunner.SettleSeconds);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(PlayTestRunner.Armed))
                if (GUILayout.Button("Start", GUILayout.Height(28))) PlayTestRunner.Arm();
            using (new EditorGUI.DisabledScope(!PlayTestRunner.Armed && !PlayTestRunner.Running))
                if (GUILayout.Button("Abort", GUILayout.Height(28))) PlayTestRunner.Abort();
        }

        if (!string.IsNullOrEmpty(PlayTestRunner.CurrentCue))
        {
            EditorGUILayout.Space();
            var style = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 16, fontStyle = FontStyle.Bold };
            EditorGUILayout.LabelField(PlayTestRunner.CurrentCue, style);
        }

        if (!string.IsNullOrEmpty(PlayTestRunner.LastSummaryPath))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Last summary:", EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(PlayTestRunner.LastSummaryPath, EditorStyles.miniLabel);
            if (GUILayout.Button("Show in Explorer")) EditorUtility.RevealInFinder(PlayTestRunner.LastSummaryPath);
        }
    }
}
