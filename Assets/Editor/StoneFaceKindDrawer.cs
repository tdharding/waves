using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws a <see cref="StoneFaceKind"/> as a dropdown of the stone types the settings beside it
/// actually hold — Auto, then A, B, C, D and any added after — rather than as the enum, which only
/// names the first four. A part still pointing at a stone type that has since been removed says
/// so, and draws as A until it is given another. With <see cref="StoneTypeOnlyAttribute"/> there
/// is no Auto.
/// </summary>
[CustomPropertyDrawer(typeof(StoneFaceKind))]
[CustomPropertyDrawer(typeof(StoneTypeOnlyAttribute))]
public class StoneFaceKindDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        bool withAuto = fieldInfo == null ||
                        !fieldInfo.IsDefined(typeof(StoneTypeOnlyAttribute), false);

        int count = StoneTypeCount(property);
        int value = property.intValue;
        int first = withAuto ? 0 : 1;

        // One option per stone type, plus Auto at the front, plus the value itself on the end when
        // it names a stone type that is no longer there.
        bool missing = value > count || value < first;
        int  options = count + 1 - first + (missing ? 1 : 0);
        var  labels  = new GUIContent[options];
        var  values  = new int[options];

        int o = 0;
        for (int v = first; v <= count; v++, o++)
        {
            values[o] = v;
            labels[o] = new GUIContent(v == 0
                ? "Auto"
                : "Stone " + RiverRunShadingSettings.StoneLetter(v - 1));
        }

        if (missing)
        {
            values[o] = value;
            labels[o] = new GUIContent(value <= 0
                ? "Auto (not allowed here — draws as before)"
                : "Stone " + RiverRunShadingSettings.StoneLetter(value - 1) + " (removed — draws as A)");
        }

        int index = System.Array.IndexOf(values, value);

        label = EditorGUI.BeginProperty(position, label, property);
        EditorGUI.BeginChangeCheck();
        int picked = EditorGUI.Popup(position, label, index, labels);
        if (EditorGUI.EndChangeCheck() && picked >= 0) property.intValue = values[picked];
        EditorGUI.EndProperty();
    }

    /// <summary>
    /// How many stone types the settings this field belongs to hold — read off the list beside it,
    /// so the dropdown follows the tuner's Add and Remove as they happen.
    /// </summary>
    private static int StoneTypeCount(SerializedProperty property)
    {
        string path   = property.propertyPath;
        int    dot    = path.LastIndexOf('.');
        string parent = dot >= 0 ? path.Substring(0, dot) + "." : string.Empty;

        var list = property.serializedObject.FindProperty(
            parent + nameof(RiverRunShadingSettings.stoneTypes));

        int held = list != null && list.isArray ? list.arraySize : 0;
        return Mathf.Max(held, RiverRunShadingSettings.FixedStoneTypes);
    }
}
