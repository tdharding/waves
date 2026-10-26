using System;
using UnityEditor;

/// <summary>
/// Asks a tuner to save once its preset has held unsaved changes for ten minutes.
///
/// Every few seconds it asks the tuner whether Save would change anything on the preset. The
/// clock starts on the first check that says yes and stops on any that says no, so changes that
/// get saved — or undone back to what is on disk — never ask. When the ten minutes are up the
/// tuner's own Save runs, with the reminder heading its usual overwrite warning; Cancel there
/// starts another ten minutes. It waits out play mode and compiles, then asks straight after.
/// The clock is kept in SessionState, so a script reload does not restart it.
/// </summary>
public sealed class TunerUnsavedReminder
{
    private const double Minutes    = 10;
    private const double CheckEvery = 15;   // seconds between "would Save change anything?" checks

    private readonly string _key;
    private double          _nextCheck;

    /// <param name="tuner">The window's name, so each tuner keeps its own clock.</param>
    public TunerUnsavedReminder(string tuner) => _key = "TunerUnsavedReminder_" + tuner;

    /// <summary>The line put in front of the Save warning when the reminder opens it.</summary>
    public static string Message =>
        $"These changes have not been saved for {Minutes:0} minutes. " +
        $"Cancel asks again in another {Minutes:0}.\n\n";

    /// <summary>
    /// Call from EditorApplication.update. <paramref name="unsaved"/> is true while Save would
    /// change the preset (and the preset's values were loaded); <paramref name="save"/> is the
    /// tuner's Save, given <see cref="Message"/> to put in front of its warning.
    /// </summary>
    public void Tick(Func<bool> unsaved, Action<string> save)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        double now = EditorApplication.timeSinceStartup;
        if (now < _nextCheck) return;
        _nextCheck = now + CheckEvery;

        if (!unsaved())
        {
            SessionState.EraseFloat(_key);
            return;
        }

        float since = SessionState.GetFloat(_key, -1f);
        if (since < 0f || since > now)
        {
            SessionState.SetFloat(_key, (float)now);
            return;
        }
        if (now - since < Minutes * 60) return;

        SessionState.EraseFloat(_key);
        save(Message);
    }
}
