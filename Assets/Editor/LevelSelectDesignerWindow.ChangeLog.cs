using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Change log — every 20 minutes the window is open, appends a section to a text file under
/// Logs/LevelSelectDesigner/ listing the named edits made since the last section and which
/// designer fields changed. Nothing is written when nothing changed. One file per session
/// (the window opening), so each file stays small.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const double ChangeLogIntervalSeconds = 20 * 60;
    private const int    ChangeLogMaxFieldLines   = 40;
    private const int    ChangeLogMaxValueLength  = 60;
    private const double ChangeLogRunGapSeconds   = 2.0; // same edit within this gap = one entry (a drag)
    private const string ChangeLogFolder          = "Logs/LevelSelectDesigner";

    // Serialized so a script reload carries the pending section over instead of losing it.
    [SerializeField] private string       _changeLogFile;
    [SerializeField] private double       _changeLogNextWrite;
    [SerializeField] private List<string> _changeLogActions   = new List<string>();
    [SerializeField] private List<int>    _changeLogCounts    = new List<int>();
    [SerializeField] private List<string> _changeLogBaseKeys  = new List<string>();
    [SerializeField] private List<string> _changeLogBaseVals  = new List<string>();
    [SerializeField] private string       _changeLogBaseAsset;

    [NonSerialized] private double _changeLogLastActionTime;

    private void ChangeLogEnable()
    {
        if (string.IsNullOrEmpty(_changeLogFile))
        {
            _changeLogFile = Path.Combine(ChangeLogFolder,
                "LevelSelectDesigner_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm") + ".txt");
            _changeLogNextWrite = EditorApplication.timeSinceStartup + ChangeLogIntervalSeconds;
            ChangeLogTakeBaseline();
        }

        EditorApplication.update += ChangeLogTick;
        EditorApplication.quitting += ChangeLogFlush;
        Undo.undoRedoEvent += ChangeLogOnUndoRedo;
    }

    private void ChangeLogDisable()
    {
        EditorApplication.update -= ChangeLogTick;
        EditorApplication.quitting -= ChangeLogFlush;
        Undo.undoRedoEvent -= ChangeLogOnUndoRedo;
    }

    // Window closed (not a script reload): write what's pending.
    private void OnDestroy() => ChangeLogFlush();

    /// <summary>Called from MarkDirty — the current undo group's name is the edit being made.</summary>
    private void ChangeLogNoteEdit()
    {
        string name = Undo.GetCurrentGroupName();
        if (string.IsNullOrEmpty(name)) name = "Edit";
        ChangeLogAddAction(name);
    }

    private void ChangeLogOnUndoRedo(in UndoRedoInfo info)
    {
        if (_data == null) return;
        ChangeLogAddAction((info.isRedo ? "Redo: " : "Undo: ") + info.undoName);
    }

    private void ChangeLogAddAction(string name)
    {
        double now = EditorApplication.timeSinceStartup;
        int last = _changeLogActions.Count - 1;
        bool sameRun = last >= 0
            && _changeLogActions[last].EndsWith("  " + name, StringComparison.Ordinal)
            && now - _changeLogLastActionTime < ChangeLogRunGapSeconds;

        if (!sameRun)
        {
            // Repeat of the previous edit after a pause: count it rather than add a line.
            if (last >= 0 && _changeLogActions[last].EndsWith("  " + name, StringComparison.Ordinal))
                _changeLogCounts[last]++;
            else
            {
                _changeLogActions.Add(DateTime.Now.ToString("HH:mm") + "  " + name);
                _changeLogCounts.Add(1);
            }
        }
        _changeLogLastActionTime = now;
    }

    private void ChangeLogTick()
    {
        if (EditorApplication.timeSinceStartup < _changeLogNextWrite) return;
        _changeLogNextWrite = EditorApplication.timeSinceStartup + ChangeLogIntervalSeconds;
        ChangeLogFlush();
    }

    private void ChangeLogFlush()
    {
        if (string.IsNullOrEmpty(_changeLogFile)) return;

        string asset = _sourceData != null ? AssetDatabase.GetAssetPath(_sourceData) : "";
        List<string> fieldLines = asset == _changeLogBaseAsset
            ? ChangeLogDiffFields()
            : new List<string> { "Switched data asset: " + _changeLogBaseAsset + " → " + asset };

        if (_changeLogActions.Count == 0 && fieldLines.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine("── " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  " + asset
            + (hasUnsavedChanges ? "  (unsaved changes)" : ""));

        if (_changeLogActions.Count > 0)
        {
            sb.AppendLine("Actions:");
            for (int i = 0; i < _changeLogActions.Count; i++)
                sb.AppendLine("  " + _changeLogActions[i]
                    + (_changeLogCounts[i] > 1 ? " ×" + _changeLogCounts[i] : ""));
        }

        if (fieldLines.Count > 0)
        {
            sb.AppendLine("Fields changed:");
            foreach (var line in fieldLines) sb.AppendLine("  " + line);
        }
        sb.AppendLine();

        try
        {
            Directory.CreateDirectory(ChangeLogFolder);
            File.AppendAllText(_changeLogFile, sb.ToString());
        }
        catch (Exception e)
        {
            Debug.LogWarning("[LevelSelectDesigner] Change log write failed: " + e.Message);
            return;
        }

        _changeLogActions.Clear();
        _changeLogCounts.Clear();
        ChangeLogTakeBaseline();
    }

    // ── Field snapshot ────────────────────────────────────────────

    private void ChangeLogTakeBaseline()
    {
        _changeLogBaseKeys.Clear();
        _changeLogBaseVals.Clear();
        _changeLogBaseAsset = _sourceData != null ? AssetDatabase.GetAssetPath(_sourceData) : "";
        foreach (var kv in ChangeLogFlatten())
        {
            _changeLogBaseKeys.Add(kv.Key);
            _changeLogBaseVals.Add(kv.Value);
        }
    }

    private List<string> ChangeLogDiffFields()
    {
        var lines = new List<string>();
        var now = ChangeLogFlatten();
        var before = new Dictionary<string, string>(_changeLogBaseKeys.Count);
        for (int i = 0; i < _changeLogBaseKeys.Count; i++)
            before[_changeLogBaseKeys[i]] = _changeLogBaseVals[i];

        // Lists that grew or shrank: report the count and skip everything inside them,
        // since every later item would otherwise read as changed.
        var resized = new List<string>();
        foreach (var kv in now)
        {
            if (!kv.Key.EndsWith(".Count", StringComparison.Ordinal)) continue;
            if (before.TryGetValue(kv.Key, out var old) && old != kv.Value)
            {
                string list = kv.Key.Substring(0, kv.Key.Length - ".Count".Length);
                resized.Add(list + "[");
                int delta = int.Parse(kv.Value) - int.Parse(old);
                lines.Add(list + ": " + old + " → " + kv.Value + " items ("
                    + (delta > 0 ? "+" : "") + delta + ")");
            }
        }

        bool InsideResized(string key)
        {
            foreach (var r in resized)
                if (key.StartsWith(r, StringComparison.Ordinal)) return true;
            return false;
        }

        int hidden = 0;
        foreach (var kv in now)
        {
            if (kv.Key.EndsWith(".Count", StringComparison.Ordinal) || InsideResized(kv.Key)) continue;
            before.TryGetValue(kv.Key, out var old);
            if (old == kv.Value) continue;
            if (lines.Count >= ChangeLogMaxFieldLines) { hidden++; continue; }
            lines.Add(kv.Key + ": " + (old ?? "(new)") + " → " + kv.Value);
        }
        if (hidden > 0) lines.Add("…and " + hidden + " more");
        return lines;
    }

    private Dictionary<string, string> ChangeLogFlatten()
    {
        var result = new Dictionary<string, string>();
        if (_data == null) return result;

        var so = new SerializedObject(_data);
        var it = so.GetIterator();
        bool enter = true;
        while (it.Next(enter))
        {
            enter = true;
            if (it.propertyPath == "m_Script") continue;

            string key = ChangeLogReadablePath(it.propertyPath);
            if (it.propertyType == SerializedPropertyType.ArraySize)
            {
                // "list.Array.size" → "list.Count"
                result[key.Replace(".Array.size", ".Count")] = it.intValue.ToString();
                continue;
            }
            if (it.propertyType == SerializedPropertyType.String && it.isArray)
            {
                result[key] = ChangeLogClip(it.stringValue);
                enter = false; // don't walk a string's characters
                continue;
            }

            string value = ChangeLogFormat(it);
            if (value != null)
            {
                result[key] = value;
                if (it.propertyType != SerializedPropertyType.Generic) enter = false;
            }
        }
        return result;
    }

    // "paths.Array.data[2].nodes.Array.data[5].position" → "paths[2].nodes[5].position"
    private static string ChangeLogReadablePath(string path) =>
        path.Replace(".Array.data[", "[");

    private static string ChangeLogFormat(SerializedProperty p)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer:   return p.longValue.ToString();
            case SerializedPropertyType.Boolean:   return p.boolValue ? "on" : "off";
            case SerializedPropertyType.Float:     return ChangeLogNum(p.doubleValue);
            case SerializedPropertyType.String:    return ChangeLogClip(p.stringValue);
            case SerializedPropertyType.Enum:
                return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length
                    ? p.enumDisplayNames[p.enumValueIndex] : p.intValue.ToString();
            case SerializedPropertyType.Color:
                var c = p.colorValue;
                return "#" + ColorUtility.ToHtmlStringRGBA(c);
            case SerializedPropertyType.ObjectReference:
                return p.objectReferenceValue != null ? p.objectReferenceValue.name : "none";
            case SerializedPropertyType.Vector2:
                return "(" + ChangeLogNum(p.vector2Value.x) + ", " + ChangeLogNum(p.vector2Value.y) + ")";
            case SerializedPropertyType.Vector3:
                var v3 = p.vector3Value;
                return "(" + ChangeLogNum(v3.x) + ", " + ChangeLogNum(v3.y) + ", " + ChangeLogNum(v3.z) + ")";
            case SerializedPropertyType.Vector4:
                var v4 = p.vector4Value;
                return "(" + ChangeLogNum(v4.x) + ", " + ChangeLogNum(v4.y) + ", "
                    + ChangeLogNum(v4.z) + ", " + ChangeLogNum(v4.w) + ")";
            case SerializedPropertyType.Vector2Int: return p.vector2IntValue.ToString();
            case SerializedPropertyType.Vector3Int: return p.vector3IntValue.ToString();
            case SerializedPropertyType.Quaternion:
                var e = p.quaternionValue.eulerAngles;
                return "(" + ChangeLogNum(e.x) + ", " + ChangeLogNum(e.y) + ", " + ChangeLogNum(e.z) + ")°";
            case SerializedPropertyType.Rect:        return p.rectValue.ToString();
            case SerializedPropertyType.Bounds:      return p.boundsValue.ToString();
            case SerializedPropertyType.LayerMask:   return p.intValue.ToString();
            case SerializedPropertyType.AnimationCurve: return p.animationCurveValue.length + " keys";
            case SerializedPropertyType.Gradient:    return "gradient";
            default: return null; // Generic containers: their children are read instead
        }
    }

    private static string ChangeLogNum(double v) =>
        v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string ChangeLogClip(string s)
    {
        if (s == null) return "\"\"";
        if (s.Length > ChangeLogMaxValueLength) s = s.Substring(0, ChangeLogMaxValueLength) + "…";
        return "\"" + s.Replace("\n", " ") + "\"";
    }
}

#endif
