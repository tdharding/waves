using UnityEditor;
using UnityEngine;

public static class WaveEditorLauncher
{
    [MenuItem("Window/Waves/Open All Wave Windows #5")]
    public static void OpenAllWindows()
    {
        // 1. Open/Focus the windows and dock them together
        // We use GetWindow<T>(string title, params Type[] desiredDockNextTo)
        // to suggest docking them into the same tab group.

        var win1 = EditorWindow.GetWindow<SaveDataEditorWindow>("Save Data Monitor");
        win1.minSize = new Vector2(380, 500);

        var win2 = EditorWindow.GetWindow<GridDesignerWindow>("Grid Designer", typeof(SaveDataEditorWindow));
        var win3 = EditorWindow.GetWindow<LevelSelectDesignerWindow>("Level Select Designer", typeof(SaveDataEditorWindow));
        var win4 = EditorWindow.GetWindow<WaveEffectsLiveTuner>("Wave Tuner", typeof(SaveDataEditorWindow));
        var win5 = EditorWindow.GetWindow<SonarGridEditorWindow>("Sonar Grid Editor", typeof(SaveDataEditorWindow));

        // Show all of them
        win1.Show();
        win2.Show();
        win3.Show();
        win4.Show();
        win5.Show();

        // Focus the first one to start the view there
        win1.Focus();
    }
}
