using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The list of changes shown in a tuner's "Overwrite the preset?" warning.
///
/// It does not guess from the tuner's own fields. It saves into a scratch copy of the preset with
/// the very same write the Save button makes, then lists every setting where the copy and the
/// preset on disk disagree — so a setting Save leaves alone is never listed, and one it changes
/// is never missed.
/// </summary>
public static class TunerSaveChanges
{
    private const int MaxListed = 30;

    /// <summary>
    /// The text for the save warning: each setting <paramref name="write"/> would change on
    /// <paramref name="preset"/>, as "Setting: on disk → tuner".
    /// </summary>
    public static string Describe<T>(T preset, System.Action<T> write) where T : ScriptableObject
    {
        var changes = List(preset, write);

        if (changes.Count == 0)
            return "No changes — the preset already holds these values.";

        var sb = new StringBuilder(changes.Count == 1 ? "1 change:" : $"{changes.Count} changes:");
        for (int i = 0; i < changes.Count && i < MaxListed; i++)
            sb.Append("\n•  ").Append(changes[i]);
        if (changes.Count > MaxListed)
            sb.Append($"\n…and {changes.Count - MaxListed} more");
        return sb.ToString();
    }

    /// <summary>Each setting <paramref name="write"/> would change on <paramref name="preset"/>.</summary>
    public static List<string> List<T>(T preset, System.Action<T> write) where T : ScriptableObject
    {
        var changes = new List<string>();
        if (preset == null) return changes;

        var scratch = Object.Instantiate(preset);
        scratch.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            write(scratch);
            var saved = new SerializedObject(preset);
            var after = new SerializedObject(scratch);
            Walk(saved, after, true,  changes);   // changed, and gone from the tuner's side
            Walk(after, saved, false, changes);   // only what the tuner has that the preset doesn't
        }
        finally
        {
            Object.DestroyImmediate(scratch);
        }
        return changes;
    }

    // ─────────────────────────────────────────────────────────────

    private static void Walk(SerializedObject from, SerializedObject to, bool fromIsSaved,
                             List<string> changes)
    {
        var it = from.GetIterator();
        if (!it.NextVisible(true)) return;

        do
        {
            bool enter = false;
            string path = it.propertyPath;

            if (path != "m_Script" && path != "m_Name" &&
                it.propertyType != SerializedPropertyType.ArraySize)
            {
                var other = to.FindProperty(path);

                if (other == null)
                {
                    changes.Add(Label(from, path) + (fromIsSaved ? " — removed" : " — added"));
                }
                else if (IsList(it))
                {
                    if (fromIsSaved && it.arraySize != other.arraySize)
                        changes.Add($"{Label(from, path)}: {it.arraySize} → {other.arraySize} entries");
                    enter = true;
                }
                else if (!IsValue(it))
                {
                    enter = true;
                }
                else if (fromIsSaved && !SerializedProperty.DataEquals(it, other))
                {
                    changes.Add(Line(Label(from, path), it, other));
                }
            }

            if (!it.NextVisible(enter)) break;
        }
        while (true);
    }

    private static bool IsList(SerializedProperty p) =>
        p.isArray && p.propertyType == SerializedPropertyType.Generic;

    private static bool IsValue(SerializedProperty p) =>
        p.propertyType != SerializedPropertyType.Generic &&
        p.propertyType != SerializedPropertyType.ManagedReference;

    /// <summary>The setting's name as the inspector shows it, its parents first: "Stone A › Colour".</summary>
    private static string Label(SerializedObject so, string path)
    {
        var names = new List<string>();
        string cur = "";
        foreach (var part in path.Split('.'))
        {
            cur = cur.Length == 0 ? part : cur + "." + part;
            if (part == "Array") continue;
            var p = so.FindProperty(cur);
            names.Add(p != null ? p.displayName : part);
        }
        return string.Join(" › ", names);
    }

    private static string Line(string label, SerializedProperty saved, SerializedProperty now)
    {
        string a = Show(saved, "G4"), b = Show(now, "G4");
        if (a == null || b == null) return label + ": edited";
        if (a == b) { a = Show(saved, "R"); b = Show(now, "R"); }
        return $"{label}: {a} → {b}";
    }

    /// <summary>The value as text, or null for one that does not read as a single value (a curve, a gradient).</summary>
    private static string Show(SerializedProperty p, string number)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Float:
                return p.numericType == SerializedPropertyNumericType.Double
                    ? p.doubleValue.ToString(number)
                    : p.floatValue.ToString(number);
            case SerializedPropertyType.Integer:
            case SerializedPropertyType.LayerMask:
            case SerializedPropertyType.Character:
                return p.longValue.ToString();
            case SerializedPropertyType.Boolean:
                return p.boolValue ? "On" : "Off";
            case SerializedPropertyType.String:
                return "\"" + p.stringValue + "\"";
            case SerializedPropertyType.Enum:
                return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length
                    ? p.enumDisplayNames[p.enumValueIndex]
                    : p.intValue.ToString();
            case SerializedPropertyType.ObjectReference:
                return p.objectReferenceValue != null ? p.objectReferenceValue.name : "None";
            case SerializedPropertyType.Color:
                return Show(p.colorValue);
            case SerializedPropertyType.Vector2:    return p.vector2Value.ToString();
            case SerializedPropertyType.Vector3:    return p.vector3Value.ToString();
            case SerializedPropertyType.Vector4:    return p.vector4Value.ToString();
            case SerializedPropertyType.Vector2Int: return p.vector2IntValue.ToString();
            case SerializedPropertyType.Vector3Int: return p.vector3IntValue.ToString();
            case SerializedPropertyType.Rect:       return p.rectValue.ToString();
            default:
                return null;
        }
    }

    // Greys read as one number; anything with a tint reads as its channels.
    private static string Show(Color c)
    {
        string alpha = Mathf.Approximately(c.a, 1f) ? "" : $", alpha {c.a:0.##}";
        if (Mathf.Approximately(c.r, c.g) && Mathf.Approximately(c.g, c.b))
            return $"grey {c.r:0.###}{alpha}";
        return $"({c.r:0.###}, {c.g:0.###}, {c.b:0.###}){alpha}";
    }
}
