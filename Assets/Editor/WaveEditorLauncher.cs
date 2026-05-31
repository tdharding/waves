using UnityEditor;
using UnityEngine;

public static class WaveEditorLauncher
{
    [MenuItem("Tools/Waves/Open All Wave Windows #5")]
    public static void OpenAllWindows()
    {
        // 1. Open/Focus the windows and dock them together
        // We use GetWindow<T>(string title, params Type[] desiredDockNextTo)
        // to suggest docking them into the same tab group.

        var win1 = EditorWindow.GetWindow<GameTesterTool>("GameTesterTool");
        win1.minSize = new Vector2(380, 500);

        var win2 = EditorWindow.GetWindow<GridDesignerWindow>("Grid Designer", typeof(GameTesterTool));
        var win3 = EditorWindow.GetWindow<LevelSelectDesignerWindow>("Level Select Designer", typeof(GameTesterTool));
        var win4 = EditorWindow.GetWindow<WaveEffectsLiveTuner>("Wave Tuner", typeof(GameTesterTool));
        var win5 = EditorWindow.GetWindow<SonarGridEditorWindow>("Sonar Grid Editor", typeof(GameTesterTool));

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
