using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Storyboard for a CameraSequence in the open scene: shots in order, each a scene camera with
/// its length, its blend in, and the dialogue lines it plays. The strip at the top shows every
/// shot's share of the running time.
/// </summary>
public class CameraSequenceWindow : EditorWindow
{
    private CameraSequence _sequence;
    private Vector2 _scroll;

    private static readonly Color[] StripColours =
    {
        new Color(0.30f, 0.50f, 0.75f), new Color(0.35f, 0.62f, 0.45f),
        new Color(0.72f, 0.55f, 0.30f), new Color(0.60f, 0.40f, 0.65f),
    };

    [MenuItem("Tools/Waves/Camera Sequence")]
    public static void Open() => GetWindow<CameraSequenceWindow>("Camera Sequence");

    public static void Open(CameraSequence sequence)
    {
        var w = GetWindow<CameraSequenceWindow>("Camera Sequence");
        w._sequence = sequence;
    }

    private void OnHierarchyChange() => Repaint();
    private void OnSelectionChange() => Repaint();

    private void OnGUI()
    {
        if (_sequence == null) _sequence = FindAnyObjectByType<CameraSequence>();

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            _sequence = (CameraSequence)EditorGUILayout.ObjectField("Sequence", _sequence, typeof(CameraSequence), true);
            if (GUILayout.Button("New", GUILayout.Width(50))) _sequence = Create();
        }

        if (_sequence == null)
        {
            EditorGUILayout.HelpBox("No Camera Sequence in the scene. Press New to add one.", MessageType.Info);
            return;
        }

        var so = new SerializedObject(_sequence);
        so.Update();
        var shots = so.FindProperty("shots");

        DrawStrip(shots);
        DrawAddRow(shots);

        EditorGUILayout.PropertyField(so.FindProperty("endBlend"), new GUIContent("End Blend",
            "How the view gets from the last shot back to the follow camera."));
        EditorGUILayout.PropertyField(so.FindProperty("sequenceAudio"), new GUIContent("Sequence Audio",
            "Optional. Plays from the first shot until the hand back. The level select music is held while the sequence plays."));
        EditorGUILayout.PropertyField(so.FindProperty("playOnStart"));

        EditorGUILayout.Space(6);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        var timing = DialogueTiming.Read();
        for (int i = 0; i < shots.arraySize; i++)
        {
            if (!DrawShot(shots, i, timing)) break;   // the list changed under us — redraw next frame
        }
        EditorGUILayout.EndScrollView();

        so.ApplyModifiedProperties();
    }

    // ── Running-time strip ──────────────────────────────────────────────
    private void DrawStrip(SerializedProperty shots)
    {
        float total = 0f;
        for (int i = 0; i < shots.arraySize; i++)
            total += shots.GetArrayElementAtIndex(i).FindPropertyRelative("duration").floatValue;

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"{shots.arraySize} shots · {total:F1}s", EditorStyles.boldLabel);

        Rect bar = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.2f));
        if (total <= 0f) return;

        float x = bar.x;
        for (int i = 0; i < shots.arraySize; i++)
        {
            float d = shots.GetArrayElementAtIndex(i).FindPropertyRelative("duration").floatValue;
            float w = bar.width * d / total;
            var seg = new Rect(x, bar.y, Mathf.Max(0f, w - 1f), bar.height);
            EditorGUI.DrawRect(seg, StripColours[i % StripColours.Length]);
            if (w > 14f) GUI.Label(seg, $" {i + 1}", EditorStyles.whiteMiniLabel);
            x += w;
        }
    }

    // ── Adding shots ────────────────────────────────────────────────────
    private void DrawAddRow(SerializedProperty shots)
    {
        var selected = SelectedCameras();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(selected.Count == 0))
            {
                string label = selected.Count > 1 ? $"Add {selected.Count} Selected Cameras" : "Add Selected Camera";
                if (GUILayout.Button(label))
                    foreach (var cam in selected) AddShot(shots, cam);
            }
            if (GUILayout.Button("Add Empty Shot", GUILayout.Width(110))) AddShot(shots, null);
        }
        if (selected.Count == 0)
            EditorGUILayout.LabelField("Select Cinemachine cameras in the scene, in shot order, to add them.",
                                       EditorStyles.miniLabel);
    }

    private static List<CinemachineCamera> SelectedCameras()
    {
        // Selection.objects usually follows click order, so shots come in as picked — reorder below if not.
        return Selection.objects.OfType<GameObject>()
                                .Select(g => g.GetComponent<CinemachineCamera>())
                                .Where(c => c != null)
                                .ToList();
    }

    private static void AddShot(SerializedProperty shots, CinemachineCamera cam)
    {
        int i = shots.arraySize;
        shots.InsertArrayElementAtIndex(i);
        var shot = shots.GetArrayElementAtIndex(i);
        shot.FindPropertyRelative("camera").objectReferenceValue = cam;
        if (i == 0)
        {
            // A fresh element copies nothing to copy from — give it the class defaults.
            shot.FindPropertyRelative("duration").floatValue = 3f;
            var blend = shot.FindPropertyRelative("blendIn");
            blend.FindPropertyRelative("Style").enumValueIndex = (int)CinemachineBlendDefinition.Styles.EaseInOut;
            blend.FindPropertyRelative("Time").floatValue      = 1f;
        }
        // Inserting copies the shot before it — keep its length and blend, not its lines or audio.
        shot.FindPropertyRelative("lines").ClearArray();
        shot.FindPropertyRelative("audio").objectReferenceValue = null;
    }

    // ── One shot card ───────────────────────────────────────────────────
    private bool DrawShot(SerializedProperty shots, int i, DialogueTiming timing)
    {
        var shot     = shots.GetArrayElementAtIndex(i);
        var camProp  = shot.FindPropertyRelative("camera");
        var duration = shot.FindPropertyRelative("duration");
        var lines    = shot.FindPropertyRelative("lines");
        var cam      = camProp.objectReferenceValue as CinemachineCamera;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var swatch = GUILayoutUtility.GetRect(6, 18, GUILayout.Width(6));
                EditorGUI.DrawRect(swatch, StripColours[i % StripColours.Length]);
                EditorGUILayout.LabelField($"Shot {i + 1}  ·  {(cam != null ? cam.name : "no camera")}  ·  {duration.floatValue:F1}s",
                                           EditorStyles.boldLabel);

                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUILayout.Button("▲", GUILayout.Width(24))) { shots.MoveArrayElement(i, i - 1); return false; }
                using (new EditorGUI.DisabledScope(i == shots.arraySize - 1))
                    if (GUILayout.Button("▼", GUILayout.Width(24))) { shots.MoveArrayElement(i, i + 1); return false; }
                if (GUILayout.Button("✕", GUILayout.Width(24))) { shots.DeleteArrayElementAtIndex(i); return false; }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(camProp, new GUIContent("Camera"));
                using (new EditorGUI.DisabledScope(cam == null))
                    if (GUILayout.Button("Select", GUILayout.Width(55)))
                    {
                        Selection.activeGameObject = cam.gameObject;
                        EditorGUIUtility.PingObject(cam.gameObject);
                    }
            }
            EditorGUILayout.PropertyField(duration, new GUIContent("Length (s)"));
            EditorGUILayout.PropertyField(shot.FindPropertyRelative("blendIn"), new GUIContent("Blend In"));
            EditorGUILayout.PropertyField(shot.FindPropertyRelative("audio"), new GUIContent("Audio",
                "Optional. Plays when the shot starts and stops when it ends."));

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Dialogue", EditorStyles.miniBoldLabel);
            for (int l = 0; l < lines.arraySize; l++)
            {
                var line = lines.GetArrayElementAtIndex(l);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var text = line.FindPropertyRelative("text");
                    text.stringValue = EditorGUILayout.TextArea(text.stringValue, GUILayout.MinHeight(34));
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(90)))
                    {
                        EditorGUIUtility.labelWidth = 34;
                        EditorGUILayout.PropertyField(line.FindPropertyRelative("hold"), new GUIContent("Hold"));
                        EditorGUIUtility.labelWidth = 0;
                        if (GUILayout.Button("Remove", EditorStyles.miniButton)) { lines.DeleteArrayElementAtIndex(l); return false; }
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Line", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    lines.InsertArrayElementAtIndex(lines.arraySize);
                    var added = lines.GetArrayElementAtIndex(lines.arraySize - 1);
                    added.FindPropertyRelative("text").stringValue = string.Empty;
                    if (lines.arraySize == 1) added.FindPropertyRelative("hold").floatValue = 2f;
                }
                if (lines.arraySize > 0) DrawDialogueLength(lines, duration.floatValue, timing);
            }
        }
        return true;
    }

    private static void DrawDialogueLength(SerializedProperty lines, float shotLength, DialogueTiming timing)
    {
        if (!timing.found)
        {
            EditorGUILayout.LabelField("No DialogueTextController in the scene.", EditorStyles.miniLabel);
            return;
        }

        float spoken = timing.backgroundFade * 2f;
        for (int l = 0; l < lines.arraySize; l++)
            spoken += timing.fadeIn + lines.GetArrayElementAtIndex(l).FindPropertyRelative("hold").floatValue
                    + timing.fadeOut + DialogueTiming.Gap;

        var style = new GUIStyle(EditorStyles.miniLabel);
        if (spoken > shotLength) style.normal.textColor = new Color(1f, 0.6f, 0.3f);
        EditorGUILayout.LabelField(spoken > shotLength
            ? $"Dialogue runs {spoken:F1}s — longer than the shot ({shotLength:F1}s); the next shot cuts it off."
            : $"Dialogue runs {spoken:F1}s of {shotLength:F1}s.", style);
    }

    /// <summary>The fade timings the scene's DialogueTextController will play lines with.</summary>
    private struct DialogueTiming
    {
        public const float Gap = 0.25f;   // DialogueTextController's pause between sequence lines
        public bool  found;
        public float fadeIn, fadeOut, backgroundFade;

        public static DialogueTiming Read()
        {
            var c = Object.FindAnyObjectByType<DialogueTextController>();
            if (c == null) return default;
            var so = new SerializedObject(c);
            return new DialogueTiming
            {
                found          = true,
                fadeIn         = so.FindProperty("defaultFadeIn").floatValue,
                fadeOut        = so.FindProperty("defaultFadeOut").floatValue,
                backgroundFade = so.FindProperty("backgroundFadeDuration").floatValue,
            };
        }
    }

    private static CameraSequence Create()
    {
        var go = new GameObject("CameraSequence");
        Undo.RegisterCreatedObjectUndo(go, "Create Camera Sequence");
        var seq = go.AddComponent<CameraSequence>();
        Selection.activeGameObject = go;
        return seq;
    }
}
