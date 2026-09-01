using UnityEditor;
using UnityEngine;

// Animate Suite — Tools ▸ Waves ▸ Animate Suite
//
// One window holding the rigging and animation helpers as tabs. They share a rig root and a clip
// through RigContext, so picking the angel once sets every tab up: the bone dots the Pose tab draws
// are the same bones the Easing tab reads curves for, addressed by the same transform paths.
//
// Replaces the standalone Bone Pose Helper window, which is now the Pose tab.
public class AnimateSuiteWindow : EditorWindow
{
    [SerializeField] RigContext   rig    = new RigContext();
    [SerializeField] int          tab;
    [SerializeField] BonePoseTool pose   = new BonePoseTool();
    [SerializeField] EasingTool   easing = new EasingTool();
    [SerializeField] LooperTool   looper = new LooperTool();

    AnimateTool[] tools;
    Vector2       scroll;

    AnimateTool[] Tools => tools ??= new AnimateTool[] { pose, easing, looper };
    AnimateTool   Active => Tools[Mathf.Clamp(tab, 0, Tools.Length - 1)];

    [MenuItem("Tools/Waves/Animate Suite")]
    static void Open() => GetWindow<AnimateSuiteWindow>("Animate");

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update += OnUpdate;
        if (rig.root == null) rig.RootFromSelection();
        rig.Refresh();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.update -= OnUpdate;
    }

    // Tabs name the selected bone in their buttons, so the window has to redraw on selection.
    void OnSelectionChange() => Repaint();

    // Every tool updates, not just the visible one: live symmetry and the loop link are meant to
    // keep working while you sit on another tab.
    void OnUpdate()
    {
        foreach (var t in Tools) t.OnUpdate(rig);
    }

    void OnSceneGUI(SceneView sv)
    {
        if (rig.root == null) return;
        if (rig.bones.Count == 0) rig.Refresh();

        // The bone dots draw on every tab, because clicking them is how the clip tabs pick which
        // bone to work on. Only the active tab gets to draw anything on top of them.
        pose.OnSceneGUI(rig, sv);
        if (Active != pose) Active.OnSceneGUI(rig, sv);
    }

    void OnGUI()
    {
        DrawRigHeader();

        var titles = new string[Tools.Length];
        for (int i = 0; i < Tools.Length; i++) titles[i] = Tools[i].Title;
        tab = GUILayout.Toolbar(Mathf.Clamp(tab, 0, Tools.Length - 1), titles);

        EditorGUILayout.Space();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (rig.root == null)
            EditorGUILayout.HelpBox("Pick the rig root — the object with the Animator on it. Its own " +
                                    "axes define the mirror plane, and clip paths are relative to it.",
                                    MessageType.Info);
        else
            Active.OnGUI(rig);
        EditorGUILayout.EndScrollView();
    }

    void DrawRigHeader()
    {
        EditorGUI.BeginChangeCheck();
        rig.root = (Transform)EditorGUILayout.ObjectField("Rig Root", rig.root, typeof(Transform), true);
        if (EditorGUI.EndChangeCheck()) rig.Refresh();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("From Selection")) { rig.RootFromSelection(); rig.Refresh(); }
            if (GUILayout.Button("Refresh"))        { rig.Refresh(); }
        }

        if (rig.root != null)
            EditorGUILayout.LabelField($"{rig.bones.Count} bones, {rig.PairCount} pairs",
                                       EditorStyles.miniLabel);

        EditorGUILayout.Space();
    }
}
