using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class HierarchyShortcutsWindow : EditorWindow
{
    private List<GameObject> shortcuts = new List<GameObject>();

    [MenuItem("Tools/Hierarchy Shortcuts")]
    static void Open()
    {
        GetWindow<HierarchyShortcutsWindow>("Hierarchy Shortcuts");
    }

    void OnGUI()
    {
        GUILayout.Label("Pinned Objects", EditorStyles.boldLabel);

        for (int i = 0; i < shortcuts.Count; i++)
        {
            if (shortcuts[i] == null)
                continue;

            if (GUILayout.Button(shortcuts[i].name))
            {
                Selection.activeObject = shortcuts[i];
                EditorGUIUtility.PingObject(shortcuts[i]);
            }
        }

        GUILayout.Space(10);
        GUILayout.Label("Drag Gamen objects here:", EditorStyles.miniLabel);

        Rect dropArea = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "Drop GameObjects");

        HandleDragAndDrop(dropArea);
    }

    void HandleDragAndDrop(Rect area)
    {
        Event evt = Event.current;
        if (!area.Contains(evt.mousePosition)) return;

        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (Object obj in DragAndDrop.objectReferences)
                {
                    if (obj is GameObject go && !shortcuts.Contains(go))
                        shortcuts.Add(go);
                }
            }
            evt.Use();
        }
    }
}
