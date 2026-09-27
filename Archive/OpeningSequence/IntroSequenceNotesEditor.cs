using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(IntroSequenceNotes))]
public class IntroSequenceNotesEditor : Editor
{
    private ReorderableList sequenceList;

    private const float ThumbnailWidth = 180f;
    private const float ThumbnailHeight = 120f;

    private void OnEnable()
    {
        sequenceList = new ReorderableList(
            serializedObject,
            serializedObject.FindProperty("sequenceBlocks"),
            draggable: true,
            displayHeader: true,
            displayAddButton: true,
            displayRemoveButton: true
        );

        sequenceList.drawHeaderCallback = rect =>
        {
            EditorGUI.LabelField(rect, "Intro Sequence Blocks (Storyboard)");
        };

        sequenceList.elementHeightCallback = index =>
        {
            SerializedProperty element =
                sequenceList.serializedProperty.GetArrayElementAtIndex(index);

            SerializedProperty descProp =
                element.FindPropertyRelative("description");

            float textHeight =
                EditorGUIUtility.singleLineHeight +
                EditorGUI.GetPropertyHeight(descProp) + 6f;

            float imageBlockHeight =
                ThumbnailHeight +
                EditorGUIUtility.singleLineHeight + 8f;

            return Mathf.Max(textHeight, imageBlockHeight) + 12f;
        };

        sequenceList.drawElementCallback = (rect, index, active, focused) =>
        {
            SerializedProperty element =
                sequenceList.serializedProperty.GetArrayElementAtIndex(index);

            SerializedProperty thumbProp =
                element.FindPropertyRelative("thumbnail");

            SerializedProperty titleProp =
                element.FindPropertyRelative("title");

            SerializedProperty descProp =
                element.FindPropertyRelative("description");

            rect.y += 6;

            // ==================================================
            // THUMBNAIL PREVIEW
            // ==================================================
            Rect imageRect = new Rect(
                rect.x,
                rect.y,
                ThumbnailWidth,
                ThumbnailHeight
            );

            EditorGUI.DrawRect(imageRect, new Color(0.12f, 0.12f, 0.12f));

            if (thumbProp.objectReferenceValue != null)
            {
                Texture2D tex = thumbProp.objectReferenceValue as Texture2D;
                EditorGUI.DrawPreviewTexture(
                    imageRect,
                    tex,
                    null,
                    ScaleMode.ScaleToFit
                );
            }

            // ==================================================
            // THUMBNAIL PICKER (BELOW IMAGE)
            // ==================================================
            Rect pickerRect = new Rect(
                rect.x,
                imageRect.yMax + 4f,
                ThumbnailWidth,
                EditorGUIUtility.singleLineHeight
            );

            EditorGUI.PropertyField(
                pickerRect,
                thumbProp,
                GUIContent.none
            );

            // ==================================================
            // TEXT CONTENT
            // ==================================================
            float textX = rect.x + ThumbnailWidth + 12f;
            float textWidth = rect.width - ThumbnailWidth - 12f;

            // Title
            Rect titleRect = new Rect(
                textX,
                rect.y,
                textWidth,
                EditorGUIUtility.singleLineHeight
            );

            EditorGUI.PropertyField(
                titleRect,
                titleProp,
                new GUIContent($"Step {index + 1}")
            );

            // Description
            Rect descRect = new Rect(
                textX,
                titleRect.yMax + 4f,
                textWidth,
                EditorGUI.GetPropertyHeight(descProp)
            );

            EditorGUI.PropertyField(
                descRect,
                descProp,
                GUIContent.none,
                includeChildren: true
            );
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawDefaultInspector();
        GUILayout.Space(12);

        sequenceList.DoLayoutList();

        serializedObject.ApplyModifiedProperties();
    }
}
