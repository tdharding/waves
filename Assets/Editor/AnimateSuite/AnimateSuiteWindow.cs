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

    // The Animation window is what actually records: bone moves made here are keyed through it.
    // Held rather than searched for every frame, and re-found at most twice a second while none is open.
    AnimationWindow animWindow;
    double          nextAnimWindowSearch;
    bool            lastRecording;
    AnimationClip   lastRecordClip;

    // Set when a bone of the rig moves while nothing is recording; the warning pops once the drag
    // is let go, so it never lands mid-drag and only comes up once per move.
    bool unrecordedMove;

    static readonly Color RecordColor = new Color(1f, 0.35f, 0.35f, 1f);

    AnimateTool[] Tools => tools ??= new AnimateTool[] { pose, easing, looper };
    AnimateTool   Active => Tools[Mathf.Clamp(tab, 0, Tools.Length - 1)];

    [MenuItem("Tools/Characters/Animate Suite", false, 600)]
    static void Open() => GetWindow<AnimateSuiteWindow>("Animate");

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update += OnUpdate;
        Undo.postprocessModifications += OnModifications;
        if (rig.root == null) rig.RootFromSelection();
        rig.Refresh();
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.update -= OnUpdate;
        Undo.postprocessModifications -= OnModifications;
    }

    // Tabs name the selected bone in their buttons, so the window has to redraw on selection.
    void OnSelectionChange() => Repaint();

    // Every tool updates, not just the visible one: live symmetry and the loop link are meant to
    // keep working while you sit on another tab.
    void OnUpdate()
    {
        foreach (var t in Tools) t.OnUpdate(rig);

        // Record can be toggled, and the clip switched, from the Animation window itself — redraw
        // when either changes so the record bar never shows a stale state.
        var  aw        = FindAnimationWindow(false);
        bool recording = aw != null && aw.recording;
        var  clip      = aw != null ? aw.animationClip : null;
        if (recording != lastRecording || clip != lastRecordClip)
        {
            lastRecording  = recording;
            lastRecordClip = clip;
            Repaint();
        }

        if (unrecordedMove && GUIUtility.hotControl == 0)
        {
            unrecordedMove = false;
            ShowUnrecordedWarning();
        }
    }

    // Every bone move — scene handles, the inspector, the Pose tab's own buttons — passes through
    // here, the same hook the Animation window keys from. Nothing is changed, only noticed.
    UndoPropertyModification[] OnModifications(UndoPropertyModification[] mods)
    {
        if (unrecordedMove || rig.root == null || EditorApplication.isPlaying) return mods;

        var aw = FindAnimationWindow(true);
        if (aw != null && aw.recording) return mods;

        foreach (var m in mods)
            if (m.currentValue?.target is Transform t && rig.boneSet.Contains(t))
            {
                unrecordedMove = true;
                break;
            }
        return mods;
    }

    void ShowUnrecordedWarning()
    {
        var aw = FindAnimationWindow(true);
        if (aw != null && aw.recording) return;   // switched on while the drag was still held
        var clip = aw != null ? aw.animationClip : null;

        string msg = aw == null   ? "The Animation window isn't open, so that bone move wasn't recorded."
                   : clip == null ? "The Animation window has no clip, so that bone move wasn't recorded."
                                  : $"Record isn't enabled, so that bone move wasn't saved into {clip.name}.";
        EditorUtility.DisplayDialog("Not Recording", msg, "OK");
    }

    AnimationWindow FindAnimationWindow(bool force)
    {
        if (animWindow != null) return animWindow;
        if (!force && EditorApplication.timeSinceStartup < nextAnimWindowSearch) return null;

        nextAnimWindowSearch = EditorApplication.timeSinceStartup + 0.5;
        var found = Resources.FindObjectsOfTypeAll<AnimationWindow>();
        animWindow = found.Length > 0 ? found[0] : null;
        return animWindow;
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
        DrawRecordBar();
        EditorGUILayout.Space();
    }

    void DrawRecordBar()
    {
        var  aw        = FindAnimationWindow(true);
        bool recording = aw != null && aw.recording;
        var  clip      = aw != null ? aw.animationClip : null;

        using (new EditorGUILayout.HorizontalScope())
        {
            var prevBg = GUI.backgroundColor;
            if (recording) GUI.backgroundColor = RecordColor;
            bool want = GUILayout.Toggle(recording, recording ? "● Recording" : "● Record",
                                         "Button", GUILayout.Width(90));
            GUI.backgroundColor = prevBg;

            // Display only — the clip is whichever one the Animation window has open.
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(clip, typeof(AnimationClip), false);

            // Deferred: opening the Animation window mid-layout would break this window's GUI pass.
            if (want != recording) EditorApplication.delayCall += () => SetRecording(want);
        }

        if (aw == null)
            EditorGUILayout.HelpBox("The Animation window isn't open, so nothing is being recorded. " +
                                    "Press Record to open it.", MessageType.Error);
        else if (clip == null)
            EditorGUILayout.HelpBox("The Animation window has no clip — select the rig in the Hierarchy " +
                                    "and pick a clip there.", MessageType.Error);
        else if (!recording)
            EditorGUILayout.HelpBox($"Record isn't enabled — changes won't be saved into {clip.name}.",
                                    MessageType.Error);
    }

    void SetRecording(bool on)
    {
        var aw = FindAnimationWindow(true);
        if (aw == null)
        {
            if (!on) return;
            aw = animWindow = GetWindow<AnimationWindow>();
            Focus();   // opening it steals focus; hand it back so the record bar keeps updating
        }

        if (on && !aw.canRecord)
        {
            Debug.LogWarning("[Animate Suite] The Animation window can't record yet — select the rig " +
                             "in the Hierarchy and make sure it has a clip open.");
            return;
        }

        aw.recording = on;
        SceneView.RepaintAll();
    }
}
