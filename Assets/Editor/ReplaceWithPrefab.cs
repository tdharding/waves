using UnityEditor;
using UnityEngine;

public class ReplaceWithPrefab : EditorWindow
{
    private GameObject prefab;
    private bool matchScale = true;
    private bool matchRotation = true;

    [MenuItem("Tools/Replace Selected with Prefab")]
    static void Init()
    {
        ReplaceWithPrefab window = (ReplaceWithPrefab)EditorWindow.GetWindow(typeof(ReplaceWithPrefab));
        window.titleContent = new GUIContent("Replace With Prefab");
        window.Show();
    }

    void OnGUI()
    {
        GUILayout.Label("Select Prefab to Replace With", EditorStyles.boldLabel);
        prefab = (GameObject)EditorGUILayout.ObjectField("Prefab:", prefab, typeof(GameObject), false);

        matchScale = EditorGUILayout.Toggle("Match Scale", matchScale);
        matchRotation = EditorGUILayout.Toggle("Match Rotation", matchRotation);

        if (GUILayout.Button("Replace Selected Objects") && prefab != null)
        {
            ReplaceSelectedObjects();
        }
    }

    void ReplaceSelectedObjects()
    {
        if (prefab == null)
        {
            Debug.LogError("No prefab selected!");
            return;
        }

        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects.Length == 0)
        {
            Debug.LogError("No objects selected!");
            return;
        }

        Undo.RegisterCompleteObjectUndo(this, "Replace Objects with Prefab");

        foreach (GameObject obj in selectedObjects)
        {
            Transform objTransform = obj.transform;
            Transform parentTransform = objTransform.parent;
            int siblingIndex = objTransform.GetSiblingIndex();

            GameObject newObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            if (newObject != null)
            {
                Undo.RegisterCreatedObjectUndo(newObject, "Instantiate Prefab");

                // Maintain hierarchy
                newObject.transform.SetParent(parentTransform);
                newObject.transform.SetSiblingIndex(siblingIndex);

                // Preserve position
                newObject.transform.position = objTransform.position;

                // Apply optional transformations based on checkboxes
                if (matchRotation)
                    newObject.transform.rotation = objTransform.rotation;

                if (matchScale)
                    newObject.transform.localScale = objTransform.localScale;

                Undo.DestroyObjectImmediate(obj);
            }
        }
    }
}
