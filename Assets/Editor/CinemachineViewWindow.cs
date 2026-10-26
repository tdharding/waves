using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// A dockable window that keeps showing what one Cinemachine camera sees - in edit mode and in play
// mode, whichever camera is live. It never touches the game's own camera: a hidden throwaway camera
// is parked on the chosen vcam's state each repaint and rendered into a texture of its own.
// Beside the view sits a panel with the watched camera's own inspectors (its Transform and every
// Cinemachine component on it), so its settings can be adjusted while watching the result.
// Opened from a Camera Sequence storyboard thumbnail, the panel starts with that shot's card, and its
// Set View Target picks the shot's View Target by clicking it in the view.
public class CinemachineViewWindow : EditorWindow
{
    // Which camera is being watched has to outlive a domain reload, entering play mode (the scene is
    // reloaded, so the object reference dies) and quitting Unity, so it is stored as a global id
    // rather than only as a serialized reference.
    const string WatchedIdKey = "Waves.CinemachineView.WatchedId";
    const string SplitKey     = "Waves.CinemachineView.Split";
    const string ViewMoveKey  = "Waves.CinemachineView.ViewMove";
    const string ViewMoveCopyKey   = "Waves.CinemachineView.ViewMoveCopy";
    const string ViewMoveCopiedKey = "Waves.CinemachineView.ViewMoveCopied";
    const float  PanelStartWidth = 320f;

    [SerializeField] CinemachineVirtualCameraBase watched;

    CinemachinePreviewRig rig;
    RenderTexture         target;
    GUIStyle              messageStyle;

    // The view is drawn in IMGUI, the panel in UI Toolkit: Cinemachine's own editors are UI Toolkit
    // editors, and drawn through IMGUI they fall back to bare default fields.
    IMGUIContainer viewArea;
    ScrollView     panel;

    // What the panel was built for, so it can tell when it no longer matches the camera.
    CinemachineVirtualCameraBase panelFor;
    readonly List<Component>     panelComponents = new();
    readonly List<FloatField>    viewMoveFields  = new();

    // The Camera Sequence shot the window was opened for, shown at the top of the panel. Cleared when
    // another camera is watched.
    [SerializeField] CameraSequence shotSequence;
    [SerializeField] int            shotIndex = -1;
    IMGUIContainer shotArea;
    double         nextShotRepaint;
    // Set View Target: the next click in the view picks the shot's View Target.
    bool pickingTarget;

    [MenuItem("Tools/Camera/Cinemachine View", false, 701)]
    static void Open()
    {
        var window = GetWindow<CinemachineViewWindow>("Cm View");
        window.minSize = new Vector2(180f, 140f);
    }

    // Opens the window (or brings it forward) watching the given camera.
    public static void Watch(CinemachineVirtualCameraBase cam)
    {
        var window = GetWindow<CinemachineViewWindow>("Cm View");
        window.minSize = new Vector2(180f, 140f);
        window.SetWatched(cam);
    }

    // Opens the window watching a Camera Sequence shot's camera, with the shot's card at the top of the panel.
    public static void WatchShot(CameraSequence sequence, int index)
    {
        if (sequence == null || index < 0 || index >= sequence.shots.Count) return;
        var window = GetWindow<CinemachineViewWindow>("Cm View");
        window.minSize = new Vector2(180f, 140f);
        window.shotSequence  = sequence;
        window.shotIndex     = index;
        window.pickingTarget = false;
        window.SetWatched(sequence.shots[index].camera);
        window.shotArea?.MarkDirtyRepaint();
    }

    void OnEnable()
    {
        titleContent = new GUIContent("Cm View");
        if (watched == null) RestoreWatched();

        EditorApplication.update               += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorSceneManager.sceneOpened         += OnSceneOpened;
        ObjectChangeEvents.changesPublished    += OnObjectChanges;
        Undo.undoRedoPerformed                 += OnUndoRedo;
    }

    void OnDisable()
    {
        EditorApplication.update               -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorSceneManager.sceneOpened         -= OnSceneOpened;
        ObjectChangeEvents.changesPublished    -= OnObjectChanges;
        Undo.undoRedoPerformed                 -= OnUndoRedo;

        ReleaseRig();
    }

    // Driving the repaint off the editor tick is what makes the view live rather than redrawing only
    // when the mouse crosses the window. The editor ticks in play mode too, so one path covers both.
    void Tick()
    {
        if (panel != null && PanelIsStale()) RebuildPanel();

        // The shot's card catches up a few times a second (the View Target Height is relearned off the
        // editor tick, after the camera moves), not every frame. So does an influence camera's section, after
        // edits in the designer's node row.
        if (shotArea != null && (shotSequence != null || CameraInfluencePreview.InfluenceOn(watched) != null) &&
            EditorApplication.timeSinceStartup >= nextShotRepaint)
        {
            nextShotRepaint = EditorApplication.timeSinceStartup + 0.25;
            shotArea.MarkDirtyRepaint();
        }

        if (watched == null) return;
        if (viewArea != null) viewArea.MarkDirtyRepaint();
        else                  Repaint();
    }

    // Components added, removed or reordered on the camera (including through the panel's own
    // title bars) change what the panel should hold.
    void OnObjectChanges(ref ObjectChangeEventStream stream)
    {
        shotArea?.MarkDirtyRepaint();
        if (panel == null || watched == null) return;
        if (!panelComponents.SequenceEqual(PanelComponentsOf(watched))) RebuildPanel();
    }

    void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode)
        {
            ReleaseRig();          // the hidden camera does not survive the scene reload
            RestoreWatched();
        }
    }

    void OnSceneOpened(Scene scene, OpenSceneMode mode) => RestoreWatched();

    // -------------------------------------------------------------------------
    // Which camera to watch
    // -------------------------------------------------------------------------

    void SetWatched(CinemachineVirtualCameraBase cam)
    {
        // Another camera: the shot shown no longer belongs here.
        if (shotSequence != null && !ShotCameraIs(cam))
        {
            shotSequence  = null;
            shotIndex     = -1;
            pickingTarget = false;
        }
        watched = cam;

        if (cam == null)
            EditorPrefs.DeleteKey(WatchedIdKey);
        else
            EditorPrefs.SetString(WatchedIdKey, GlobalObjectId.GetGlobalObjectIdSlow(cam).ToString());

        Repaint();
    }

    void RestoreWatched()
    {
        if (watched != null) return;

        var id = EditorPrefs.GetString(WatchedIdKey, string.Empty);
        if (string.IsNullOrEmpty(id) || !GlobalObjectId.TryParse(id, out var parsed)) return;

        watched = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) as CinemachineVirtualCameraBase;
    }

    // -------------------------------------------------------------------------
    // GUI
    // -------------------------------------------------------------------------

    // The toolbar spans the top; below it the view and the panel share a splitter, the panel on the
    // right. The split width is remembered through the window's view data.
    void CreateGUI()
    {
        var root = rootVisualElement;
        root.Add(new IMGUIContainer(DrawToolbar));

        var split = new TwoPaneSplitView(1, PanelStartWidth, TwoPaneSplitViewOrientation.Horizontal)
        {
            viewDataKey = SplitKey
        };
        split.style.flexGrow = 1f;

        viewArea = new IMGUIContainer(DrawView);
        viewArea.style.flexGrow = 1f;

        panel = new ScrollView(ScrollViewMode.Vertical);

        split.Add(viewArea);
        split.Add(panel);
        root.Add(split);

        RebuildPanel();
    }

    void DrawView()
    {
        var view = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        if (watched == null)
        {
            DrawMessage(view, "Pick a Cinemachine camera to watch.");
            return;
        }

        if (EditorUtility.IsPersistent(watched))
        {
            DrawMessage(view, "That camera is on a prefab asset. Open the prefab and pick the camera "
                            + "from the prefab's own hierarchy.");
            return;
        }

        if (pickingTarget && ((shotSequence == null && CameraInfluencePreview.InfluenceOn(watched) == null) ||
                              Application.isPlaying)) pickingTarget = false;
        if (pickingTarget) HandlePick(view);

        if (Event.current.type != EventType.Repaint) return;

        if (!TryRender(view))
        {
            DrawMessage(view, "Nothing to show yet.");
            return;
        }

        GUI.DrawTexture(view, target, ScaleMode.StretchToFill, false);

        if (pickingTarget)
        {
            var banner = new Rect(view.x, view.y, view.width, 22f);
            EditorGUI.DrawRect(banner, new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(banner, "Set View Target: click it in the view  ·  Esc cancels", EditorStyles.whiteLabel);
        }
    }

    void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            EditorGUI.BeginChangeCheck();
            var picked = (CinemachineVirtualCameraBase)EditorGUILayout.ObjectField(
                watched, typeof(CinemachineVirtualCameraBase), true, GUILayout.MinWidth(80f));
            if (EditorGUI.EndChangeCheck()) SetWatched(picked);

            var shot = WornShot(out var sequence, out int index);
            if (shot != null)
                GUILayout.Label(new GUIContent($"Shot {index + 1} of {sequence.name}: {CameraSequenceFogPreview.Describe(shot)}",
                    "This camera is a shot in that Camera Sequence, so the view wears the shot's Fog Centre, Fog Distance " +
                    "and focus, as it will at runtime."),
                    EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            else if (CameraInfluencePreview.InfluenceOn(watched) is LevelSelectCameraInfluence influence)
                GUILayout.Label(new GUIContent($"Camera Influence, {LevelSelectDesignerWindow.CameraInfluenceNodeLabel(influence)}: {CameraInfluencePreview.Describe(influence)}",
                    "This is a node's Camera Influence camera, so the view wears its Fog Distance and focus as they will " +
                    "look with the boat in its box: the fog on the boat there, and the follow camera's depth of field."),
                    EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
        }
    }

    void DrawMessage(Rect rect, string text)
    {
        messageStyle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap  = true
        };
        EditorGUI.LabelField(rect, text, messageStyle);
    }

    // -------------------------------------------------------------------------
    // Panel - the watched camera's own inspectors
    // -------------------------------------------------------------------------

    bool PanelIsStale()
    {
        // Identity, not Unity's null-equality: a new camera after a scene load must count as a change.
        if (!ReferenceEquals(panelFor, watched)) return true;

        foreach (var component in panelComponents)
            if (component == null) return true;    // destroyed under the panel

        return false;
    }

    void RebuildPanel()
    {
        panel.Clear();
        panelComponents.Clear();
        viewMoveFields.Clear();
        panelFor = watched;

        if (watched == null) return;

        // Draws nothing (and takes no room) unless a shot, or a node's Camera Influence camera, is being shown.
        shotArea = new IMGUIContainer(DrawShotSection);
        panel.Add(shotArea);

        panel.Add(ViewMoveSection());

        panelComponents.AddRange(PanelComponentsOf(watched));
        foreach (var component in panelComponents)
            panel.Add(ComponentSection(component));
    }

    // -------------------------------------------------------------------------
    // Shot - the Camera Sequence shot the window was opened for
    // -------------------------------------------------------------------------

    bool ShotCameraIs(CinemachineVirtualCameraBase cam)
    {
        return shotSequence != null && cam != null && shotIndex >= 0 && shotIndex < shotSequence.shots.Count
            && shotSequence.shots[shotIndex].camera == cam;
    }

    // The shot's card, drawn by the storyboard. If the shots were reordered under it, it follows the
    // first shot using the watched camera; with none left it goes.
    void DrawShotSection()
    {
        if (DrawInfluenceSection()) return;
        if (shotSequence == null || watched == null) return;
        if (!ShotCameraIs(watched))
        {
            shotIndex = shotSequence.shots.FindIndex(s => s.camera == watched);
            if (shotIndex < 0)
            {
                shotSequence  = null;
                pickingTarget = false;
                return;
            }
        }

        EditorGUILayout.Space(2);
        if (!CameraSequenceWindow.DrawShotInCmView(shotSequence, shotIndex, StartPickingTarget))
        {
            EditorGUILayout.HelpBox($"Open the Camera Sequence storyboard on '{shotSequence.name}' to edit shot {shotIndex + 1} here.",
                                    MessageType.Info);
            pickingTarget = false;
        }
        if (pickingTarget)
            EditorGUILayout.HelpBox("Click what the camera should keep its height to, in the view. Esc cancels.", MessageType.None);
        EditorGUILayout.Space(4);
    }

    // A node's Camera Influence camera: its Fog Distance and View Target, as in the Level Select Designer's
    // node row. False when the watched camera isn't one.
    bool DrawInfluenceSection()
    {
        var influence = CameraInfluencePreview.InfluenceOn(watched);
        if (influence == null) return false;

        EditorGUILayout.Space(2);
        if (!LevelSelectDesignerWindow.DrawCameraInfluenceInCmView(influence, StartPickingTarget))
        {
            EditorGUILayout.HelpBox("Open the Level Select Designer on this level to edit this camera's fog and focus here.",
                                    MessageType.Info);
            pickingTarget = false;
        }
        if (pickingTarget)
            EditorGUILayout.HelpBox("Click what the depth of field should focus on, in the view. Esc cancels.", MessageType.None);
        EditorGUILayout.Space(4);
        return true;
    }

    void StartPickingTarget()
    {
        pickingTarget = true;
        if (viewArea != null)
        {
            viewArea.focusable = true;
            viewArea.Focus();
        }
        shotArea?.MarkDirtyRepaint();
    }

    void StopPickingTarget()
    {
        pickingTarget = false;
        shotArea?.MarkDirtyRepaint();
    }

    // Set View Target: the click goes through the view to the nearest thing drawn there.
    void HandlePick(Rect view)
    {
        var e = Event.current;
        EditorGUIUtility.AddCursorRect(view, MouseCursor.ArrowPlus);

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            StopPickingTarget();
            e.Use();
            return;
        }
        if (e.type != EventType.MouseDown || e.button != 0 || !view.Contains(e.mousePosition)) return;
        e.Use();

        var viewport = new Vector2((e.mousePosition.x - view.x) / view.width,
                                   1f - (e.mousePosition.y - view.y) / view.height);
        if (rig == null || !rig.TryViewportRay(viewport, out Ray ray, out int cullingMask)) return;

        var picked = CameraSequenceViewTargets.PickDrawn(ray, cullingMask);
        if (picked == null)
        {
            ShowNotification(new GUIContent("Nothing drawn there — try again, or Esc"));
            return;
        }
        var influence = CameraInfluencePreview.InfluenceOn(watched);
        if (influence != null) LevelSelectDesignerWindow.SetInfluenceViewTarget(influence, picked);
        else                   CameraSequenceViewTargets.SetTarget(shotSequence, shotIndex, picked);
        StopPickingTarget();
    }

    // The Transform plus every Cinemachine component on the camera: the camera itself, its
    // position/rotation components, noise and extensions.
    static List<Component> PanelComponentsOf(CinemachineVirtualCameraBase cam)
    {
        var list = new List<Component>();
        foreach (var component in cam.GetComponents<Component>())
        {
            if (component == null) continue;                                         // missing script
            if ((component.hideFlags & HideFlags.HideInInspector) != 0) continue;
            if (component is Transform || IsCinemachine(component)) list.Add(component);
        }
        return list;
    }

    static bool IsCinemachine(Component component)
    {
        var ns = component.GetType().Namespace;
        return ns != null && ns.StartsWith("Unity.Cinemachine");
    }

    // The same title bar the Inspector draws (fold, enable toggle, context menu), over the
    // component's own inspector. The fold state is shared with the Inspector window.
    static VisualElement ComponentSection(Component component)
    {
        var section   = new VisualElement();
        var inspector = new InspectorElement(component);

        section.Add(new IMGUIContainer(() =>
        {
            if (component == null) return;

            bool expanded = InternalEditorUtility.GetIsInspectorExpanded(component);
            bool picked   = EditorGUILayout.InspectorTitlebar(expanded, component, true);
            if (picked != expanded) InternalEditorUtility.SetIsInspectorExpanded(component, picked);

            inspector.style.display = picked ? DisplayStyle.Flex : DisplayStyle.None;
        }));
        section.Add(inspector);

        inspector.style.display = InternalEditorUtility.GetIsInspectorExpanded(component)
            ? DisplayStyle.Flex
            : DisplayStyle.None;

        return section;
    }

    // -------------------------------------------------------------------------
    // View Move - moving the camera along the way it is looking
    // -------------------------------------------------------------------------

    // One field per view axis: X across the view, Y up the view, Z towards what the camera points
    // at. Each field reads how far the camera has been moved along that axis since the panel was
    // built; dragging its label or typing a number moves the camera by the change. The axes are
    // re-read on every change, so after turning the camera they follow the new view.
    VisualElement ViewMoveSection()
    {
        var foldout = new Foldout { text = "View Move", viewDataKey = ViewMoveKey };
        foldout.Add(ViewMoveField("X  Right",   Vector3.right));
        foldout.Add(ViewMoveField("Y  Up",      Vector3.up));
        foldout.Add(ViewMoveField("Z  Forward", Vector3.forward));

        var buttons = new VisualElement();
        buttons.style.flexDirection = FlexDirection.Row;

        var copy = new Button(CopyViewMove) { text = "Copy",
            tooltip = "Remembers these X, Y and Z, to paste into another camera's View Move." };
        var paste = new Button(PasteViewMove) { text = "Paste",
            tooltip = "Puts the copied X, Y and Z into these fields, moving this camera along its own view to match." };
        copy.style.flexGrow  = 1f;
        paste.style.flexGrow = 1f;
        buttons.Add(copy);
        buttons.Add(paste);
        foldout.Add(buttons);
        return foldout;
    }

    // The copied View Move lasts the editor session, through domain reloads.
    void CopyViewMove()
    {
        var move = new Vector3(viewMoveFields[0].value, viewMoveFields[1].value, viewMoveFields[2].value);
        SessionState.SetVector3(ViewMoveCopyKey, move);
        SessionState.SetBool(ViewMoveCopiedKey, true);
        ShowNotification(new GUIContent($"Copied View Move  {move.x:0.00}, {move.y:0.00}, {move.z:0.00}"));
    }

    // Setting the fields moves the camera by the change, as typing the numbers in would. One undo step.
    void PasteViewMove()
    {
        if (watched == null) return;
        if (!SessionState.GetBool(ViewMoveCopiedKey, false))
        {
            ShowNotification(new GUIContent("Nothing copied yet"));
            return;
        }

        var move = SessionState.GetVector3(ViewMoveCopyKey, Vector3.zero);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        viewMoveFields[0].value = move.x;
        viewMoveFields[1].value = move.y;
        viewMoveFields[2].value = move.z;
        Undo.SetCurrentGroupName("Paste View Move");
        Undo.CollapseUndoOperations(group);
    }

    FloatField ViewMoveField(string label, Vector3 viewAxis)
    {
        var field = new FloatField(label)
        {
            tooltip = "Drag the label to move the camera along its own view. Shift = faster, Alt = slower."
        };
        field.AddToClassList(BaseField<float>.alignedFieldUssClassName);
        viewMoveFields.Add(field);

        field.RegisterValueChangedCallback(evt =>
        {
            float amount = evt.newValue - evt.previousValue;
            if (watched == null || amount == 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;

            var t = watched.transform;
            Undo.RecordObject(t, "Move Camera Along View");
            t.position += ViewRotation(watched) * viewAxis * amount;
        });

        return field;
    }

    // An undo puts the camera back without the fields knowing, so they go back to 0 rather than
    // show a distance the camera is no longer at.
    void OnUndoRedo()
    {
        foreach (var field in viewMoveFields) field.SetValueWithoutNotify(0f);
        shotArea?.MarkDirtyRepaint();
    }

    // The way the camera is looking as the view shows it - noise left out, so a shaking camera still
    // moves along steady axes. Same live/not-live rule as ApplyWatchedState.
    static Quaternion ViewRotation(CinemachineVirtualCameraBase cam)
    {
        var stage = PrefabStageUtility.GetPrefabStage(cam.gameObject);
        var scene = stage != null ? stage.scene : cam.gameObject.scene;
        bool isolated = scene.IsValid() && EditorSceneManager.IsPreviewScene(scene);
        bool live     = cam.isActiveAndEnabled && !isolated;

        return live ? cam.State.RawOrientation : cam.transform.rotation;
    }

    // -------------------------------------------------------------------------
    // Rendering
    // -------------------------------------------------------------------------

    // The Camera Sequence shot whose fog and focus the view wears: the shot the window was opened for, else
    // the first shot in the scene's sequences using the watched camera. Null when it is no shot's camera.
    CameraSequence.Shot WornShot(out CameraSequence sequence, out int index)
    {
        sequence = ShotCameraIs(watched) ? shotSequence : CameraSequenceFogPreview.SequenceFor(watched);
        index    = CameraSequenceFogPreview.ShotIndexFor(sequence, watched, sequence == shotSequence ? shotIndex : -1);
        return index >= 0 ? sequence.shots[index] : null;
    }

    string lastFocusDebug;

    bool TryRender(Rect view)
    {
        if (view.width < 4f || view.height < 4f) return false;

        CinemachinePreviewRig.EnsureTarget(ref target, view, "Cinemachine View Target");
        rig ??= new CinemachinePreviewRig();

        // A Camera Sequence shot camera shows that shot's fog and focus, as it will at runtime; so does a
        // node's Camera Influence camera, seen through the follow camera's depth of field.
        var shot      = WornShot(out var shotSeq, out var shotIdx);
        var influence = shot == null ? CameraInfluencePreview.InfluenceOn(watched) : null;
        // [CmFocusDebug] — temporary.
        string focusDebug = $"[CmFocusDebug] Cm View '{(watched != null ? watched.name : "null")}': shot {shotIdx + 1} of " +
                            $"'{(shotSeq != null ? shotSeq.name : "null")}' (opened for {shotIndex + 1}), " +
                            $"focusViewTarget={(shot != null && shot.focusViewTarget)}, " +
                            $"viewTarget={(shot != null && shot.viewTarget != null ? shot.viewTarget.name : "null")}, " +
                            $"volume={(shot != null && shot.volumeProfile != null ? shot.volumeProfile.name : "null")}, " +
                            $"influence={(influence != null)}";
        if (focusDebug != lastFocusDebug) { lastFocusDebug = focusDebug; Debug.Log(focusDebug); }
        var saved     = default(CameraSequenceFogPreview.Saved);
        try
        {
            return rig.Render(watched, target, previewCam =>
            {
                if (shot != null)           saved = CameraSequenceFogPreview.Wear(shot, previewCam);
                else if (influence != null) saved = CameraInfluencePreview.Wear(influence);
            },
            influence != null ? influence.FocusTarget : CameraSequenceFogPreview.FocusFor(shot),
            influence != null ? CameraInfluencePreview.FollowPost() : null,
            shot?.volumeProfile);
        }
        finally
        {
            CameraSequenceFogPreview.Restore(saved);
        }
    }

    // -------------------------------------------------------------------------
    // Teardown
    // -------------------------------------------------------------------------

    void ReleaseRig()
    {
        rig?.Dispose();
        rig = null;
        CinemachinePreviewRig.ReleaseTarget(ref target);
    }
}
