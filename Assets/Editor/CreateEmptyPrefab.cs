using System.IO;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Adds "Prefab" to the Project window's right-click > Create menu.
//
// Unity can only make a prefab by dragging an object out of a scene, so building one starts by
// cluttering whatever scene happens to be open and then deleting the leftover. This creates the
// asset directly in the folder you right-clicked, with the same inline-rename flow as every other
// Create item, and never touches the open scene.
static class CreateEmptyPrefab
{
    // Priority 20 puts it alongside Folder at the top of the Create menu.
    [MenuItem("Assets/Create/Prefab", false, 20)]
    static void Create()
    {
        var icon = EditorGUIUtility.IconContent("Prefab Icon").image as Texture2D;

        ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
            EntityId.None,
            ScriptableObject.CreateInstance<CreateEmptyPrefabAction>(),
            "New Prefab.prefab",
            icon,
            null);
    }
}

// Unity resolves the typed name against the active folder and hands us the full path.
class CreateEmptyPrefabAction : AssetCreationEndAction
{
    public override void Action(EntityId instanceId, string pathName, string resourceFile)
    {
        string path = AssetDatabase.GenerateUniqueAssetPath(pathName);

        // The source object lives in a preview scene rather than the open one, so creating a
        // prefab never marks the user's scene dirty — otherwise every prefab made this way
        // leaves an unsaved-changes prompt behind for an object that no longer exists.
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject temp = null;
        try
        {
            temp = new GameObject(Path.GetFileNameWithoutExtension(path));
            EditorSceneManager.MoveGameObjectToScene(temp, preview);

            GameObject asset = PrefabUtility.SaveAsPrefabAsset(temp, path);
            if (asset != null) ProjectWindowUtil.ShowCreatedAsset(asset);
            else Debug.LogError($"[CreateEmptyPrefab] Could not create a prefab at '{path}'.");
        }
        finally
        {
            if (temp != null) Object.DestroyImmediate(temp);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
