using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// The Interact block: what a thing the designer places does when the boat comes near it —
/// the prompt that comes up, how near is near enough, and the poster it shows.
///
/// Drawn here once and written onto the placed object by whatever put it there, so the display
/// on a rim node, an outpost's characters and anything added later all read the same way in the
/// panel and run off the same pair of runtime scripts
/// (<see cref="LevelSelectInteractPoint"/> and its <see cref="LevelSelectInteractAction"/>).
/// </summary>
public partial class LevelSelectDesignerWindow
{
    /// <summary>How far the reach slider goes — a river is a couple of units across, so a few
    /// units is already standing well off the bank.</summary>
    private const float InteractMaxRadius = 20f;

    private const string NoPosterLabel = "None";

    /// <summary>
    /// The block itself. Hands back an edited copy, written by the caller inside its own change
    /// check, the same way the topper fields are.
    /// </summary>
    private LevelSelectDesignerData.DesignerInteract DrawInteractBlock(
        LevelSelectDesignerData.DesignerInteract interact)
    {
        var edited = interact?.Clone() ?? new LevelSelectDesignerData.DesignerInteract();

        edited.enabled = EditorGUILayout.Toggle(
            new GUIContent("Interact", "Stand the boat near it and a prompt comes up; press the " +
                                       "key and it shows its poster."), edited.enabled);
        if (!edited.enabled) return edited;

        EditorGUI.indentLevel++;

        edited.prompt = EditorGUILayout.TextField(
            new GUIContent("Prompt", "What the prompt reads. The key is added by the prompt " +
                                     "itself, so this is just the doing."), edited.prompt);

        edited.radius = EditorGUILayout.Slider(
            new GUIContent("Reach", "How near the boat has to be, measured flat across the " +
                                    "water — the height it stands at makes no difference."),
            edited.radius, 0f, InteractMaxRadius);

        // The library is the map's, not this one display's — shown here because here is where
        // it is wanted, and written straight onto the data.
        EditorGUI.BeginChangeCheck();
        var library = (LevelSelectPosterLibrary)EditorGUILayout.ObjectField(
            new GUIContent("Poster Library", "Every poster the map's displays can show. One " +
                                             "asset for the whole map."),
            _data.posterLibrary, typeof(LevelSelectPosterLibrary), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Set Poster Library");
            _data.posterLibrary = library;
            MarkDirty();
        }

        edited.posterName = DrawPosterPicker(edited.posterName);

        EditorGUI.indentLevel--;
        return edited;
    }

    /// <summary>
    /// The poster dropdown: None and every name in the library. A name the library no longer
    /// has is kept and shown as missing rather than quietly swapped for another one.
    /// </summary>
    private string DrawPosterPicker(string posterName)
    {
        if (_data.posterLibrary == null)
        {
            EditorGUILayout.LabelField("Poster", "Pick a library first.", EditorStyles.miniLabel);
            return posterName;
        }

        string[] names = _data.posterLibrary.Names();
        if (names.Length == 0)
        {
            EditorGUILayout.LabelField("Poster", $"{_data.posterLibrary.name} is empty.",
                                       EditorStyles.miniLabel);
            return posterName;
        }

        bool missing = !string.IsNullOrEmpty(posterName) && System.Array.IndexOf(names, posterName) < 0;

        var labels = new GUIContent[names.Length + (missing ? 2 : 1)];
        labels[0] = new GUIContent(NoPosterLabel);
        for (int i = 0; i < names.Length; i++) labels[i + 1] = new GUIContent(names[i]);
        if (missing) labels[^1] = new GUIContent($"{posterName} (missing)");

        int current = missing ? labels.Length - 1
                    : string.IsNullOrEmpty(posterName) ? 0
                    : System.Array.IndexOf(names, posterName) + 1;

        int picked = EditorGUILayout.Popup(
            new GUIContent("Poster", "Which poster it shows. Swap the art itself inside the " +
                                     "library — this only remembers the name."),
            current, labels);

        if (picked == 0) return "";
        if (missing && picked == labels.Length - 1) return posterName;
        return names[picked - 1];
    }

    /// <summary>
    /// Puts the block onto something just placed: the point that notices the boat, and the
    /// action that shows the poster. Nothing is left behind when it is off, so turning it off
    /// and generating again takes the interaction away.
    /// </summary>
    private void ApplyInteract(GameObject placed, LevelSelectDesignerData.DesignerInteract interact)
    {
        if (placed == null) return;

        var point  = placed.GetComponent<LevelSelectInteractPoint>();
        var poster = placed.GetComponent<LevelSelectPosterInteract>();

        if (interact == null || !interact.enabled)
        {
            if (poster != null) Undo.DestroyObjectImmediate(poster);
            if (point  != null) Undo.DestroyObjectImmediate(point);
            return;
        }

        if (point  == null) point  = Undo.AddComponent<LevelSelectInteractPoint>(placed);
        if (poster == null) poster = Undo.AddComponent<LevelSelectPosterInteract>(placed);

        point.prompt = interact.prompt;
        point.radius = interact.radius;

        // The library is a real asset, so this reference survives the designer's workspace copy
        // — it is the data asset itself that must never be wired from the clone.
        poster.library    = _data.posterLibrary;
        poster.posterName = interact.posterName;

        EditorUtility.SetDirty(point);
        EditorUtility.SetDirty(poster);
    }
}

#endif
