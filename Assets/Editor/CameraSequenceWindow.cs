using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Storyboard for a CameraSequence in the open scene: shots in order, each a scene camera with
/// its length, its blend in, and the dialogue lines it plays. The strip at the top shows every
/// shot's share of the running time.
///
/// Presets: the scene's sequence is the working copy. The preset row saves it to a
/// <see cref="CameraSequencePreset"/> (settings, camera links and a full copy of each camera) and loads
/// one back, putting its cameras back as saved. Loading another preset or closing the window with
/// unsaved changes asks to save first.
/// </summary>
public class CameraSequenceWindow : EditorWindow
{
    private CameraSequence _sequence;
    private Vector2 _scroll;

    // Each shot's camera view, drawn through the Cinemachine View window's renderer.
    private const float PreviewWidth = 180f;
    // After a change the previews keep redrawing this long, so a camera the brain moves a frame
    // later still lands. Otherwise they sit still — no constant redraw heating the editor.
    private const double SettleSeconds = 0.5;
    private CinemachinePreviewRig _previewRig;
    private readonly List<RenderTexture> _previews = new List<RenderTexture>();
    // Animate 2 Shot's end camera views, beside the shot's own.
    private readonly List<RenderTexture> _endPreviews = new List<RenderTexture>();
    private double _redrawUntil;

    // A shot's movement played in edit mode, watched in the Cinemachine View window. The camera is
    // put back where it was placed when the preview ends, is stopped, or the window closes.
    private CinemachineCamera   _previewCam;
    private CameraSequence.Shot _previewShot;
    private Pose                _previewFrom;    // where the camera is placed — put back here
    private Pose                _previewStartPose;   // where the movement starts: raised to the View Target's height
    private float               _previewLift;
    private Vector3             _previewCentre;
    private double              _previewStart;

    // Cameras mid-preview in any storyboard, so the View Target follow leaves them alone.
    private static readonly HashSet<CinemachineCamera> s_Previewing = new HashSet<CinemachineCamera>();

    /// <summary>True while a storyboard is playing a preview on this camera — it isn't where it is placed.</summary>
    public static bool IsPreviewing(CinemachineCamera cam) => cam != null && s_Previewing.Contains(cam);

    // Set while a shot card is drawn in the Cm View: its Set View Target button.
    private System.Action _cmViewSetTarget;

    // The Cm View card's Volume foldout: the shot's own profile, drawn by Unity's Volume Profile editor.
    private const string VolumeOpenKey = "CameraSequenceWindow_VolumeOpen";
    private Editor _volumeEditor;

    // Whether the storyboard (or its cameras) differs from its preset — checked off the update loop.
    private const double DirtyCheckSeconds = 0.5;
    private bool   _dirty;
    private double _nextDirtyCheck;

    // The scene's Sequence Characters, found once per redraw for every shot card to check against its view.
    private SequenceCharacter[] _characters = new SequenceCharacter[0];

    // Ping from the Level Select Designer: the shots using this camera flash, and the first is scrolled to.
    private const double PingSeconds = 1.5;
    private string    _pingCamera;   // the camera's CameraSequenceLink id
    private double    _pingStart;
    private bool      _pingScroll;
    private float     _pingCardY = -1f;

    private static readonly Color[] StripColours =
    {
        new Color(0.30f, 0.50f, 0.75f), new Color(0.35f, 0.62f, 0.45f),
        new Color(0.72f, 0.55f, 0.30f), new Color(0.60f, 0.40f, 0.65f),
    };

    [MenuItem("Tools/Camera/Camera Sequence", false, 700)]
    public static void Open() => GetWindow<CameraSequenceWindow>("Camera Sequence");

    public static void Open(CameraSequence sequence)
    {
        var w = GetWindow<CameraSequenceWindow>("Camera Sequence");
        w._sequence = sequence;
    }

    private void OnEnable()
    {
        EditorApplication.update            += Tick;
        ObjectChangeEvents.changesPublished += OnObjectChanges;
        Undo.undoRedoPerformed              += RedrawPreviews;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        EditorApplication.update            -= Tick;
        ObjectChangeEvents.changesPublished -= OnObjectChanges;
        Undo.undoRedoPerformed              -= RedrawPreviews;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;

        StopPreview();
        if (_volumeEditor != null) DestroyImmediate(_volumeEditor);
        _volumeEditor = null;
        _previewRig?.Dispose();
        _previewRig = null;
        for (int i = 0; i < _previews.Count; i++)
        {
            var rt = _previews[i];
            CinemachinePreviewRig.ReleaseTarget(ref rt);
        }
        _previews.Clear();
        for (int i = 0; i < _endPreviews.Count; i++)
        {
            var rt = _endPreviews[i];
            CinemachinePreviewRig.ReleaseTarget(ref rt);
        }
        _endPreviews.Clear();
    }

    private void OnHierarchyChange() => RedrawPreviews();
    private void OnSelectionChange() => Repaint();
    private void OnFocus()           => RedrawPreviews();

    // A camera moved, a lens changed, anything edited — the previews may no longer match.
    private void OnObjectChanges(ref ObjectChangeEventStream stream) => RedrawPreviews();

    private void RedrawPreviews()
    {
        _redrawUntil = EditorApplication.timeSinceStartup + SettleSeconds;
        Repaint();
    }

    private void Tick()
    {
        CheckDirty();
        if (_previewCam != null) TickPreview();
        if (EditorApplication.timeSinceStartup < _redrawUntil) Repaint();
        if (_pingCamera != null)
        {
            Repaint();
            if (!_pingScroll && EditorApplication.timeSinceStartup - _pingStart > PingSeconds) _pingCamera = null;
        }
    }

    // Never carry a moved camera into play mode.
    private void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode) StopPreview();
    }

    // ── Presets ─────────────────────────────────────────────────────────
    private void CheckDirty()
    {
        if (EditorApplication.timeSinceStartup < _nextDirtyCheck) return;
        _nextDirtyCheck = EditorApplication.timeSinceStartup + DirtyCheckSeconds;

        // A camera mid-preview isn't where it was placed — that's not a change, so keep the last answer.
        bool dirty = _previewCam != null
            ? _dirty
            : !Application.isPlaying && _sequence != null && _sequence.preset != null
              && CameraSequencePresetIO.Differs(_sequence, _sequence.preset);

        if (dirty != _dirty)
        {
            _dirty = dirty;
            Repaint();
        }
        hasUnsavedChanges  = _dirty;
        saveChangesMessage = _sequence != null && _sequence.preset != null
            ? $"The storyboard has changes not saved to the preset '{_sequence.preset.name}'. Save them?"
            : string.Empty;
    }

    // Unity's own prompt when the window closes with unsaved changes.
    public override void SaveChanges()
    {
        if (_sequence != null && _sequence.preset != null) SaveToPreset(false);
        base.SaveChanges();
    }

    private void DrawPresetRow()
    {
        var current = _sequence.preset;
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var picked = CameraSequencePresetIO.DrawPicker(new GUIContent("Active Preset",
                    "The camera sequence presets kept in " + CameraSequencePresetIO.Folder + ". Picking one loads it."), current);
                var chosen = (CameraSequencePreset)EditorGUILayout.ObjectField(
                    picked, typeof(CameraSequencePreset), false, GUILayout.Width(150f));
                if (chosen != current)
                {
                    if (ConfirmLeave())
                    {
                        if (chosen != null) LoadFromPreset(chosen);
                        else SetPreset(null);
                    }
                    GUIUtility.ExitGUI();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(current == null))
                {
                    if (GUILayout.Button(new GUIContent("Load", "Fills the storyboard from the preset and puts its cameras back " +
                                                               "as they were saved. Any that are missing are built again.")))
                    {
                        if (ConfirmLeave()) LoadFromPreset(current);
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button(new GUIContent("Save", "Writes the storyboard and its cameras as they are now into the preset.")))
                    {
                        SaveToPreset(true);
                        GUIUtility.ExitGUI();
                    }
                }
                if (GUILayout.Button(new GUIContent("Save as New", "Saves the storyboard as it stands to a new preset.")))
                {
                    SaveAsNewPreset();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(new GUIContent("New Preset", "A new, empty preset and a fresh storyboard for it.")))
                {
                    NewPreset();
                    GUIUtility.ExitGUI();
                }
            }
        }

        if (current == null)
        {
            EditorGUILayout.HelpBox("No preset — the storyboard lives in the scene only. Save as New to keep it in a preset, " +
                                    "or pick one to load it.", MessageType.Info);
        }
        else if (_dirty)
        {
            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 0.6f, 0.3f) } };
            EditorGUILayout.LabelField($"Unsaved changes — Save to keep them in '{current.name}'.", style);
        }
    }

    /// <summary>Before the storyboard is replaced: asks to save unsaved changes. False = cancelled.</summary>
    private bool ConfirmLeave()
    {
        var p = _sequence.preset;
        if (p == null)
        {
            // A storyboard in no preset yet would be lost outright.
            if (_sequence.shots.Count == 0) return true;
            int pick = EditorUtility.DisplayDialogComplex("Storyboard not in a preset",
                "The storyboard isn't saved to any preset. Save it as a new one first?", "Save as New", "Cancel", "Don't Save");
            if (pick == 1) return false;
            if (pick == 0)
            {
                SaveAsNewPreset();
                if (_sequence.preset == null) return false;   // file panel cancelled
            }
            return true;
        }
        if (!CameraSequencePresetIO.Differs(_sequence, p)) return true;

        int choice = EditorUtility.DisplayDialogComplex("Unsaved storyboard",
            $"The storyboard has changes not saved to the preset '{p.name}'.", "Save", "Cancel", "Don't Save");
        if (choice == 1) return false;
        if (choice == 0) SaveToPreset(false);
        return true;
    }

    private void LoadFromPreset(CameraSequencePreset p)
    {
        StopPreview();
        int rebuilt = CameraSequencePresetIO.Deploy(p, () => PresetParent(p));

        Undo.RecordObject(_sequence, "Load Camera Sequence Preset");
        p.ApplyTo(_sequence);
        _sequence.preset = p;
        MarkSequenceChanged();
        // The cameras went back to where they were saved; their View Targets may have moved since.
        CameraSequenceViewTargets.Forget();
        Debug.Log($"[CameraSequenceWindow] Loaded '{p.name}': {p.shots.Count} shots, {p.storedObjects.Count} linked objects" +
                  (rebuilt > 0 ? $", {rebuilt} built again." : "."));
    }

    private void SaveToPreset(bool confirm)
    {
        var p = _sequence.preset;
        if (p == null) return;
        if (confirm && !EditorUtility.DisplayDialog("Save Camera Sequence",
                $"Overwrite '{p.name}' with the storyboard and its cameras as they are now?", "Save", "Cancel"))
            return;

        Undo.RecordObject(p, "Save Camera Sequence Preset");
        CameraSequencePresetIO.Capture(_sequence, p, true);
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssetIfDirty(p);
        // The links it may just have added live in the scene.
        MarkSequenceChanged();
        Debug.Log($"[CameraSequenceWindow] Saved to '{p.name}': {p.shots.Count} shots, {p.storedObjects.Count} linked objects.");
    }

    private void SaveAsNewPreset()
    {
        string path = EditorUtility.SaveFilePanelInProject("Save Camera Sequence Preset", "NewCameraSequencePreset",
            "asset", "Choose save location", CameraSequencePresetIO.EnsureFolder());
        if (string.IsNullOrEmpty(path)) return;

        var p = CreateInstance<CameraSequencePreset>();
        CameraSequencePresetIO.Capture(_sequence, p, true);
        AssetDatabase.CreateAsset(p, path);
        AssetDatabase.SaveAssets();
        SetPreset(p);
        Debug.Log($"[CameraSequenceWindow] Saved new preset at {path}: {p.shots.Count} shots.");
    }

    /// <summary>A new, empty preset with the storyboard cleared for it. Null when cancelled.</summary>
    private CameraSequencePreset NewPreset()
    {
        if (!ConfirmLeave()) return null;
        string path = EditorUtility.SaveFilePanelInProject("New Camera Sequence Preset", "NewCameraSequencePreset",
            "asset", "Choose save location", CameraSequencePresetIO.EnsureFolder());
        if (string.IsNullOrEmpty(path)) return null;

        StopPreview();
        var p = CreateInstance<CameraSequencePreset>();
        AssetDatabase.CreateAsset(p, path);
        AssetDatabase.SaveAssets();

        // A fresh preset holds the defaults — loading it clears the storyboard to them.
        Undo.RecordObject(_sequence, "New Camera Sequence Preset");
        p.ApplyTo(_sequence);
        _sequence.preset = p;
        MarkSequenceChanged();
        Debug.Log($"[CameraSequenceWindow] New preset at {path} — fresh storyboard.");
        return p;
    }

    /// <summary>
    /// For the Level Select Designer's node Cinematic: opens the storyboard on a new, empty preset.
    /// Null when cancelled.
    /// </summary>
    public static CameraSequencePreset NewPresetFromDesigner()
    {
        var w = GetWindow<CameraSequenceWindow>("Camera Sequence");
        if (w._sequence == null) w._sequence = FindAnyObjectByType<CameraSequence>();
        if (w._sequence == null) w._sequence = Create();
        var p = w.NewPreset();
        w.Repaint();
        return p;
    }

    /// <summary>
    /// For the Level Select Designer's node Cinematic: opens the storyboard on this preset, loading it
    /// unless it is already the active one (asks first if the storyboard has unsaved changes).
    /// </summary>
    public static void OpenPresetFromDesigner(CameraSequencePreset p)
    {
        var w = GetWindow<CameraSequenceWindow>("Camera Sequence");
        if (w._sequence == null) w._sequence = FindAnyObjectByType<CameraSequence>();
        if (w._sequence == null) w._sequence = Create();
        if (p != null && w._sequence.preset != p && w.ConfirmLeave()) w.LoadFromPreset(p);
        w.Focus();
        w.Repaint();
    }

    /// <summary>
    /// For the Level Select Designer's shot camera Ping: opens the storyboard on this preset (as
    /// <see cref="OpenPresetFromDesigner"/>), scrolls to the first shot using the camera and flashes
    /// every shot that uses it.
    /// </summary>
    public static void PingCameraFromDesigner(CameraSequencePreset p, string cameraLink)
    {
        OpenPresetFromDesigner(p);
        var w = GetWindow<CameraSequenceWindow>("Camera Sequence");
        w._pingCamera = cameraLink;
        w._pingStart  = EditorApplication.timeSinceStartup;
        w._pingScroll = true;
        w._pingCardY  = -1f;
        w.Repaint();
    }

    private void SetPreset(CameraSequencePreset p)
    {
        Undo.RecordObject(_sequence, "Set Camera Sequence Preset");
        _sequence.preset = p;
        MarkSequenceChanged();
    }

    private void MarkSequenceChanged()
    {
        EditorUtility.SetDirty(_sequence);
        PrefabUtility.RecordPrefabInstancePropertyModifications(_sequence);
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(_sequence.gameObject.scene);
        _nextDirtyCheck = 0;
        RedrawPreviews();
    }

    // New cameras and orbit points go in a child of the sequence named after the preset, so each
    // preset's own sit together. With no preset they go straight under the sequence.
    private Transform ShotParent() => _sequence.preset != null ? PresetParent(_sequence.preset) : _sequence.transform;

    private Transform PresetParent(CameraSequencePreset p)
    {
        var existing = _sequence.transform.Find(p.name);
        if (existing != null) return existing;
        var go = new GameObject(p.name);
        Undo.RegisterCreatedObjectUndo(go, "Camera Sequence Preset Parent");
        Undo.SetTransformParent(go.transform, _sequence.transform, "Camera Sequence Preset Parent");
        go.transform.localPosition = Vector3.zero;
        return go.transform;
    }

    // ── Animation preview ───────────────────────────────────────────────
    private void StartPreview(CameraSequence.Shot shot)
    {
        StopPreview();
        if (shot?.camera == null) return;

        Transform t   = shot.camera.transform;
        _previewCam    = shot.camera;
        _previewShot   = shot;
        _previewFrom   = new Pose(t.position, t.rotation);
        // As in play: the camera first stands at this shot's height over its View Target.
        _previewLift      = CameraSequence.ViewTargetLift(shot, t.position);
        _previewStartPose = new Pose(t.position + Vector3.up * _previewLift, t.rotation);
        _previewCentre    = CameraSequence.OrbitCentre(shot, _previewStartPose, _previewLift);
        _previewStart  = EditorApplication.timeSinceStartup;
        s_Previewing.Add(shot.camera);
        CinemachineViewWindow.Watch(shot.camera);
        Debug.Log($"[CameraSequenceWindow] Preview '{shot.camera.name}': {shot.animation} over {shot.animationTime:F1}s {shot.easing}" +
                  (Mathf.Abs(_previewLift) > 1e-4f ? $", raised {_previewLift:+0.00;-0.00} to its View Target height." : "."));
    }

    private void TickPreview()
    {
        float elapsed = (float)(EditorApplication.timeSinceStartup - _previewStart);
        var pose = CameraSequence.PoseAt(_previewShot, _previewStartPose, _previewCentre, elapsed, _previewLift);
        _previewCam.transform.SetPositionAndRotation(pose.position, pose.rotation);
        // Keeps the brain updating the camera's state in edit mode, so the Cm View follows it.
        EditorApplication.QueuePlayerLoopUpdate();
        RedrawPreviews();
        if (elapsed >= _previewShot.animationTime) StopPreview();
    }

    private void StopPreview()
    {
        if (_previewCam != null)
        {
            _previewCam.transform.SetPositionAndRotation(_previewFrom.position, _previewFrom.rotation);
            EditorApplication.QueuePlayerLoopUpdate();
            RedrawPreviews();
            s_Previewing.Remove(_previewCam);
        }
        _previewCam  = null;
        _previewShot = null;
    }

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

        DrawPresetRow();

        var so = new SerializedObject(_sequence);
        so.Update();
        var shots = so.FindProperty("shots");

        DrawStrip(shots);
        DrawAddRow(shots);

        EditorGUILayout.PropertyField(so.FindProperty("endBlend"), new GUIContent("End Blend",
            "How the view gets from the last shot back to the follow camera."));
        var useAudio = so.FindProperty("useAudio");
        EditorGUILayout.PropertyField(useAudio, new GUIContent("Audio",
            "On: the sequence plays its own audio and holds the level select music until the hand back. " +
            "Off: no sequence or shot audio, and the level music carries on untouched."));
        using (new EditorGUI.DisabledScope(!useAudio.boolValue))
        {
            EditorGUILayout.PropertyField(so.FindProperty("sequenceAudio"), new GUIContent("Sequence Audio",
                "Optional. Plays from the first shot until the hand back. The level select music is held while the sequence plays."));
            EditorGUILayout.PropertyField(so.FindProperty("audioFadeOut"), new GUIContent("Audio Fade Out",
                "Seconds the sequence's sound fades out at the hand back, while the level select music fades in (its Fade In After Sequence). 0 = cut."));
        }
        EditorGUILayout.PropertyField(so.FindProperty("playOnce"), new GUIContent("Play Once",
            "On: plays once per save — never again until New Game. Off: plays every time it is triggered. Saved with the preset."));
        DrawPlayOnStart(so);

        EditorGUILayout.Space(6);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        var timing = DialogueTiming.Read();
        _characters = FindObjectsByType<SequenceCharacter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        bool repaint = Event.current.type == EventType.Repaint;
        if (repaint) _pingCardY = -1f;
        for (int i = 0; i < shots.arraySize; i++)
        {
            if (!DrawShot(shots, i, timing)) break;   // the list changed under us — redraw next frame
        }
        EditorGUILayout.EndScrollView();
        if (repaint && _pingCamera != null && _pingScroll)
        {
            _pingScroll = false;
            if (_pingCardY < 0f) _pingCamera = null;   // no shot here uses it (the preset didn't load)
            else
            {
                _scroll.y  = Mathf.Max(0f, _pingCardY - 4f);
                _pingStart = EditorApplication.timeSinceStartup;   // flash from when it comes into view
            }
        }

        so.ApplyModifiedProperties();
        if (Event.current.type == EventType.Layout)
        {
            AimOrbitCameras();
            EnsureShotVolumes();
        }
    }

    // An Orbit shot starts with its camera at Start Degrees round the Orbit Point (at its own distance
    // and height), facing the point. Placed here in edit mode so the thumbnail, the Cm View and the
    // blend in all show that start; the end is that start turned round by Degrees Moved.
    private void AimOrbitCameras()
    {
        if (Application.isPlaying) return;
        // A camera used by more than one Orbit shot is placed by its first — otherwise they'd fight.
        var placed = new HashSet<CinemachineCamera>();
        foreach (var shot in _sequence.shots)
        {
            if (shot.camera == null || !shot.animated || shot.orbitPoint == null ||
                shot.animation != CameraSequence.ShotAnimation.Orbit || shot.camera == _previewCam) continue;
            if (!placed.Add(shot.camera)) continue;

            Transform t = shot.camera.transform;
            Vector3 centre = shot.orbitPoint.position;
            Vector3 position = CameraSequence.OrbitStartPosition(centre, t.position, shot.orbitStartDegrees);
            var aim = CameraSequence.OrbitLook(position, centre, t.rotation);
            if ((position - t.position).sqrMagnitude < 1e-10f && Quaternion.Angle(t.rotation, aim) < 0.01f) continue;

            Undo.RecordObject(t, "Place Orbit Camera");
            t.SetPositionAndRotation(position, aim);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            EditorApplication.QueuePlayerLoopUpdate();
            RedrawPreviews();
        }
    }

    // A newly set Orbit Point takes Start Degrees from where the camera already stands, so it doesn't jump.
    private static void SeedStartDegrees(SerializedProperty shot, CinemachineCamera cam)
    {
        var point = shot.FindPropertyRelative("orbitPoint").objectReferenceValue as Transform;
        if (point == null || cam == null) return;
        shot.FindPropertyRelative("orbitStartDegrees").floatValue =
            Mathf.Round(CameraSequence.OrbitAngle(point.position, cam.transform.position));
    }

    // Play On Start belongs to the scene's sequence, not to any preset.
    private void DrawPlayOnStart(SerializedObject so)
    {
        var playOnStart = so.FindProperty("playOnStart");
        var startPreset = so.FindProperty("startPreset");
        EditorGUILayout.PropertyField(playOnStart, new GUIContent("Play On Start",
            "On: the scene plays the Start Preset when it starts. Not saved with a preset — it belongs to the scene."));
        using (new EditorGUI.DisabledScope(!playOnStart.boolValue))
        using (new EditorGUI.IndentLevelScope())
            EditorGUILayout.PropertyField(startPreset, new GUIContent("Start Preset",
                "The preset Play On Start plays. Empty = the storyboard as it stands in the scene."));
        if (playOnStart.boolValue && startPreset.objectReferenceValue == null && _sequence.preset != null)
            EditorGUILayout.LabelField("No Start Preset — the scene starts with whatever the storyboard holds.",
                                       EditorStyles.miniLabel);
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
            if (GUILayout.Button("Add Empty Shot", GUILayout.Width(110)))
                AddShot(shots, CreateShotCamera(shots.arraySize + 1));
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

    // A new Cinemachine camera for an empty shot — under the sequence, framed where the Scene view is looking.
    private CinemachineCamera CreateShotCamera(int shotNumber)
    {
        var go = new GameObject($"Shot {shotNumber} Camera");
        Undo.RegisterCreatedObjectUndo(go, "Add Shot Camera");
        Undo.SetTransformParent(go.transform, ShotParent(), "Add Shot Camera");

        var view = SceneView.lastActiveSceneView;
        if (view != null && view.camera != null)
            go.transform.SetPositionAndRotation(view.camera.transform.position, view.camera.transform.rotation);
        else
            go.transform.localPosition = Vector3.zero;

        return go.AddComponent<CinemachineCamera>();
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
            shot.FindPropertyRelative("zoomDistance").floatValue  = 1f;
            shot.FindPropertyRelative("animationTime").floatValue = 3f;
            shot.FindPropertyRelative("orbitDegrees").floatValue  = 90f;
        }
        // Inserting copies the shot before it — keep its length and blend, not its lines, audio or animation.
        shot.FindPropertyRelative("lines").ClearArray();
        shot.FindPropertyRelative("audio").objectReferenceValue = null;
        shot.FindPropertyRelative("animated").boolValue = false;
        shot.FindPropertyRelative("orbitPoint").objectReferenceValue = null;
        shot.FindPropertyRelative("endCamera").objectReferenceValue = null;
        shot.FindPropertyRelative("viewTarget").objectReferenceValue = null;
        shot.FindPropertyRelative("viewTargetPath").stringValue = string.Empty;
        shot.FindPropertyRelative("characterAnimations").ClearArray();
        // Its own Volume, made fresh from the Level Select profile — never the shot before's.
        shot.FindPropertyRelative("volumeProfile").objectReferenceValue = null;
    }

    // ── One shot card ───────────────────────────────────────────────────
    // inCmView: drawn in the Cm View's panel beside the camera's own view — no reorder/remove buttons
    // and no thumbnail, and the View Target gets its Set View Target button.
    private bool DrawShot(SerializedProperty shots, int i, DialogueTiming timing, bool inCmView = false)
    {
        var shot     = shots.GetArrayElementAtIndex(i);
        var camProp  = shot.FindPropertyRelative("camera");
        var duration = shot.FindPropertyRelative("duration");
        var lines    = shot.FindPropertyRelative("lines");
        var anyKey   = shot.FindPropertyRelative("pressAnyKey");
        var fades    = shot.FindPropertyRelative("textFades");
        var cam      = camProp.objectReferenceValue as CinemachineCamera;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var swatch = GUILayoutUtility.GetRect(6, 18, GUILayout.Width(6));
                EditorGUI.DrawRect(swatch, StripColours[i % StripColours.Length]);
                string length = anyKey.boolValue ? "until a key" : $"{duration.floatValue:F1}s";
                EditorGUILayout.LabelField($"Shot {i + 1}  ·  {(cam != null ? cam.name : "no camera")}  ·  {length}",
                                           EditorStyles.boldLabel);

                if (!inCmView)
                {
                    using (new EditorGUI.DisabledScope(i == 0))
                        if (GUILayout.Button("▲", GUILayout.Width(24))) { shots.MoveArrayElement(i, i - 1); return false; }
                    using (new EditorGUI.DisabledScope(i == shots.arraySize - 1))
                        if (GUILayout.Button("▼", GUILayout.Width(24))) { shots.MoveArrayElement(i, i + 1); return false; }
                    if (GUILayout.Button("✕", GUILayout.Width(24))) { shots.DeleteArrayElementAtIndex(i); return false; }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
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
                    using (new EditorGUI.DisabledScope(anyKey.boolValue))
                        EditorGUILayout.PropertyField(duration, new GUIContent("Length (s)",
                            "Seconds this shot is on screen. Not used when Press Any Key is on."));
                    EditorGUILayout.PropertyField(shot.FindPropertyRelative("blendIn"), new GUIContent("Blend In"));
                    EditorGUILayout.PropertyField(anyKey, new GUIContent("Press Any Key",
                        "On: the shot holds until the player presses any key, then moves on — or, when its last line fades out, " +
                        "as soon as that line has faded. Off: it moves on after its length."));
                    using (new EditorGUI.DisabledScope(!_sequence.useAudio))
                        EditorGUILayout.PropertyField(shot.FindPropertyRelative("audio"), new GUIContent("Audio",
                            "Optional. Plays when the shot starts and stops when it ends. Not used when the sequence's Audio is off."));
                    EditorGUILayout.PropertyField(shot.FindPropertyRelative("fogCentre"), new GUIContent("Fog Centre",
                        "Where the fog's clear middle sits for this shot — set as the shot starts, and held still while the camera " +
                        "animates. View: the spot the camera looks at. Boat: on the boat. Camera Position: on the water under the " +
                        "camera."));
                    DrawFogDistance(shot);
                    DrawViewTarget(shot, cam, inCmView);
                    DrawAnimation(shot, i, cam);
                    if (inCmView) DrawVolume(shot);
                }
                if (!inCmView) DrawPreviews(shot, i, cam);
            }

            EditorGUILayout.Space(2);
            DrawCharacters(shot, cam);

            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Dialogue", EditorStyles.miniBoldLabel, GUILayout.Width(60));
                EditorGUIUtility.labelWidth = 70;
                EditorGUILayout.PropertyField(fades, new GUIContent("Text Fades",
                    "On: this shot's lines fade in and out (the black panel fades only on the first and last shots). Off: nothing fades — each line " +
                    "swaps straight to the next after its hold, and the last one stays up until the next shot."));
                EditorGUIUtility.labelWidth = 0;
            }
            for (int l = 0; l < lines.arraySize; l++)
            {
                var line = lines.GetArrayElementAtIndex(l);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var text = line.FindPropertyRelative("text");
                    text.stringValue = EditorGUILayout.TextArea(text.stringValue, GUILayout.MinHeight(34));
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(96)))
                    {
                        var lineKey = line.FindPropertyRelative("pressAnyKey");
                        EditorGUIUtility.labelWidth = 34;
                        using (new EditorGUI.DisabledScope(lineKey.boolValue))
                            EditorGUILayout.PropertyField(line.FindPropertyRelative("hold"), new GUIContent("Hold",
                                "Seconds the line holds fully shown. Not used when Key is on."));
                        EditorGUILayout.PropertyField(lineKey, new GUIContent("Key",
                            "On: the line holds until the player presses any key. Off: it holds for its Hold."));
                        EditorGUIUtility.labelWidth = 0;
                        using (new EditorGUI.DisabledScope(!fades.boolValue))
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            DrawLineFade(line.FindPropertyRelative("fadeIn"), "In",
                                "On: the line fades in. Off: it appears at once. Only used when the shot's Text Fades is on.");
                            DrawLineFade(line.FindPropertyRelative("fadeOut"), "Out",
                                "On: the line fades out. Off: it vanishes at once. Only used when the shot's Text Fades is on.");
                        }
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
                    if (lines.arraySize == 1)
                    {
                        // A fresh element copies nothing to copy from — give it the class defaults.
                        added.FindPropertyRelative("hold").floatValue   = 2f;
                        added.FindPropertyRelative("fadeIn").boolValue  = true;
                        added.FindPropertyRelative("fadeOut").boolValue = true;
                    }
                }
                if (lines.arraySize > 0)
                {
                    // The panel rises with the first lines of the run and lowers after the last shot's.
                    bool raisesPanel = true;
                    for (int prev = 0; prev < i; prev++)
                        if (shots.GetArrayElementAtIndex(prev).FindPropertyRelative("lines").arraySize > 0) raisesPanel = false;
                    bool lowersPanel = i == shots.arraySize - 1;
                    DrawDialogueLength(lines, duration.floatValue, anyKey.boolValue, fades.boolValue,
                                       raisesPanel, lowersPanel, timing);
                }
            }
        }
        if (_pingCamera != null && cam != null && cam.TryGetComponent(out CameraSequenceLink link) && link.id == _pingCamera)
            DrawPing(GUILayoutUtility.GetLastRect());
        return true;
    }

    // A pinged shot card: an outline fading out over PingSeconds. Notes the first one for the scroll.
    private void DrawPing(Rect card)
    {
        if (Event.current.type != EventType.Repaint) return;
        if (_pingCardY < 0f) _pingCardY = card.y;

        float t = (float)((EditorApplication.timeSinceStartup - _pingStart) / PingSeconds);
        if (t >= 1f) return;
        var c = new Color(1f, 0.8f, 0.2f, 1f - t * t);
        const float k = 2f;
        EditorGUI.DrawRect(new Rect(card.x, card.y, card.width, k), c);
        EditorGUI.DrawRect(new Rect(card.x, card.yMax - k, card.width, k), c);
        EditorGUI.DrawRect(new Rect(card.x, card.y, k, card.height), c);
        EditorGUI.DrawRect(new Rect(card.xMax - k, card.y, k, card.height), c);
    }

    // The shot's own fog distance, in place of the world's White Fade Radius. Ticking it starts from the
    // Radius the world is showing.
    private static void DrawFogDistance(SerializedProperty shot)
    {
        var on       = shot.FindPropertyRelative("overrideFogDistance");
        var distance = shot.FindPropertyRelative("fogDistance");

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(on, new GUIContent("Fog Distance Override",
            "On: for this shot the fog is full at Fog Distance from its centre, instead of the world's White Fade Radius. " +
            "Shown live in the Cm View."));
        if (EditorGUI.EndChangeCheck() && on.boolValue && distance.floatValue <= 0f)
            distance.floatValue = Shader.GetGlobalFloat("_WhiteFadeRadius");

        if (!on.boolValue) return;
        using (new EditorGUI.IndentLevelScope())
            EditorGUILayout.PropertyField(distance, new GUIContent("Fog Distance",
                "Metres from the fog's centre to where it is full, for this shot."));
    }

    // What the camera keeps its height to. Picked by clicking it in the Cm View; the height is learned from
    // where the camera stands, and moving the camera up or down changes it.
    private void DrawViewTarget(SerializedProperty shot, CinemachineCamera cam, bool inCmView)
    {
        var target = shot.FindPropertyRelative("viewTarget");
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(target, new GUIContent("View Target",
            "Optional. What the camera keeps its height to: as the shot starts the camera stands its height above it, " +
            "and its whole movement goes with it. Pick it by clicking it in the Cm View (Set View Target)."));
        if (EditorGUI.EndChangeCheck()) CameraSequenceViewTargets.LearnHeight(shot);

        if (inCmView && _cmViewSetTarget != null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                using (new EditorGUI.DisabledScope(cam == null || Application.isPlaying))
                    if (GUILayout.Button(new GUIContent("Set View Target",
                            "Then click what the camera should keep its height to, in the view on the left. Esc cancels.")))
                        _cmViewSetTarget();
            }
        }

        if (target.objectReferenceValue != null && cam != null)
        {
            using (new EditorGUI.IndentLevelScope())
                EditorGUILayout.LabelField($"Camera keeps {shot.FindPropertyRelative("viewTargetHeight").floatValue:F2} above it",
                                           EditorStyles.miniLabel);
        }

        var focus = shot.FindPropertyRelative("focusViewTarget");
        using (new EditorGUI.DisabledScope(target.objectReferenceValue == null))
            EditorGUILayout.PropertyField(focus, new GUIContent("Focus On View Target",
                "On: as the shot starts its Volume's Depth of Field Focus Distance is set to how far the camera stands " +
                "from the View Target — once, so it stays put while the camera animates. Off: the Volume's own Focus Distance."));
    }

    // In the Cm View only: the shot's own Volume, its overrides edited in place — no other shot shares it.
    private void DrawVolume(SerializedProperty shot)
    {
        var profile = shot.FindPropertyRelative("volumeProfile").objectReferenceValue as UnityEngine.Rendering.VolumeProfile;

        EditorGUILayout.Space(2);
        bool open = SessionState.GetBool(VolumeOpenKey, true);
        bool now  = EditorGUILayout.Foldout(open, new GUIContent("Volume",
            "This shot's own post processing — colour, depth of field and the rest. The camera wears it while the shot " +
            "is on; no other shot shares it."), true);
        if (now != open) SessionState.SetBool(VolumeOpenKey, now);
        if (!now) return;

        using (new EditorGUI.IndentLevelScope())
        {
            if (profile == null)
            {
                EditorGUILayout.LabelField(Application.isPlaying
                    ? "No Volume of its own yet — the storyboard makes one out of play mode."
                    : "No camera yet — the shot's Volume is made once it has one.", EditorStyles.miniLabel);
                return;
            }
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(new GUIContent("Profile", "The shot's own copy of the Level Select profile."),
                                            profile, typeof(UnityEngine.Rendering.VolumeProfile), false);
        }

        Editor.CreateCachedEditor(profile, null, ref _volumeEditor);
        _volumeEditor.OnInspectorGUI();
    }

    // postprocessing/LevelSelectVolumeProfile — what every shot camera's Volume Settings and every shot's own Volume start from.
    private const string LevelSelectProfileGuid = "9eafecbf7740f374b941e60136993d6f";

    // Each shot's own Volume is kept here.
    private const string ShotVolumesFolder = CameraSequencePresetIO.Folder + "/ShotVolumes";

    /// <summary>
    /// Every shot camera has Cinemachine Volume Settings (on the Level Select profile, Focus Tracking off), and every
    /// shot its own Volume — a copy of the Level Select profile in <see cref="ShotVolumesFolder"/>.
    /// </summary>
    private void EnsureShotVolumes()
    {
        if (Application.isPlaying) return;
        string levelSelectPath = AssetDatabase.GUIDToAssetPath(LevelSelectProfileGuid);
        var levelSelect = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(levelSelectPath);
        if (levelSelect == null) return;

        for (int i = 0; i < _sequence.shots.Count; i++)
        {
            var shot = _sequence.shots[i];
            if (shot.camera == null) continue;

            if (!shot.camera.TryGetComponent(out CinemachineVolumeSettings settings))
            {
                settings = Undo.AddComponent<CinemachineVolumeSettings>(shot.camera.gameObject);
                settings.Profile       = levelSelect;
                settings.FocusTracking = CinemachineVolumeSettings.FocusTrackingMode.None;
                EditorUtility.SetDirty(settings);
                PrefabUtility.RecordPrefabInstancePropertyModifications(settings);
                Debug.Log($"[CameraSequenceWindow] Gave '{shot.camera.name}' Cinemachine Volume Settings ({levelSelect.name}).");
            }

            if (shot.volumeProfile != null) continue;
            string owner = _sequence.preset != null ? _sequence.preset.name : _sequence.name;
            string path  = AssetDatabase.GenerateUniqueAssetPath(
                $"{LevelSelectRiverPresetLibrary.EnsureFolder(ShotVolumesFolder)}/{owner} Shot {i + 1} Volume.asset");
            if (!AssetDatabase.CopyAsset(levelSelectPath, path))
            {
                Debug.LogWarning($"[CameraSequenceWindow] Couldn't copy {levelSelect.name} to {path} for shot {i + 1}.");
                continue;
            }
            Undo.RecordObject(_sequence, "Shot Volume");
            shot.volumeProfile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(path);
            MarkSequenceChanged();
            Debug.Log($"[CameraSequenceWindow] Shot {i + 1}: its own Volume at {path}.");
        }
    }

    /// <summary>
    /// For the Cm View: draws shot <paramref name="index"/>'s card (as in the storyboard, less the reorder buttons and
    /// thumbnail) through the open storyboard showing <paramref name="sequence"/>. False when no storyboard shows it.
    /// </summary>
    public static bool DrawShotInCmView(CameraSequence sequence, int index, System.Action setViewTarget)
    {
        if (sequence == null) return false;
        var w = Resources.FindObjectsOfTypeAll<CameraSequenceWindow>().FirstOrDefault(x => x._sequence == sequence);
        if (w == null) return false;

        var so = new SerializedObject(sequence);
        so.Update();
        var shots = so.FindProperty("shots");
        if (index < 0 || index >= shots.arraySize) return false;

        w._characters      = FindObjectsByType<SequenceCharacter>(FindObjectsInactive.Exclude);
        w._cmViewSetTarget = setViewTarget;
        try
        {
            w.DrawShot(shots, index, DialogueTiming.Read(), true);
        }
        finally
        {
            w._cmViewSetTarget = null;
        }
        so.ApplyModifiedProperties();
        return true;
    }

    private void DrawAnimation(SerializedProperty shot, int i, CinemachineCamera cam)
    {
        var animated = shot.FindPropertyRelative("animated");
        EditorGUILayout.PropertyField(animated, new GUIContent("Animated",
            "On: the camera moves while the shot plays. It goes back to where you placed it when the sequence ends."));
        if (!animated.boolValue) return;

        EditorGUI.indentLevel++;
        var animation = shot.FindPropertyRelative("animation");
        EditorGUILayout.PropertyField(animation, new GUIContent("Animation",
            "Zoom: the camera moves forward, the way it faces. Orbit: the camera circles its Orbit Point. " +
            "Animate 2 Shot: the camera tweens from where it stands to its End Camera (shown on the right, beside the start)."));
        if (animation.enumValueIndex == (int)CameraSequence.ShotAnimation.Orbit)
        {
            var point = shot.FindPropertyRelative("orbitPoint");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(point, new GUIContent("Orbit Point",
                    "What the camera circles, round the world up axis, looking at it the whole way. The camera is placed at Start Degrees round it, facing it."));
                bool pointSet = EditorGUI.EndChangeCheck();
                using (new EditorGUI.DisabledScope(cam == null))
                    if (GUILayout.Button(new GUIContent("Generate", "A new point where the shot's camera is looking."),
                                         GUILayout.Width(70)))
                    {
                        point.objectReferenceValue = CreateOrbitPoint(i + 1, cam);
                        pointSet = true;
                    }
                if (pointSet) SeedStartDegrees(shot, cam);
            }
            if (point.objectReferenceValue == null)
                EditorGUILayout.LabelField("No Orbit Point — the camera stays still. Press Generate.", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(shot.FindPropertyRelative("orbitStartDegrees"), new GUIContent("Start Degrees",
                "Where round the Orbit Point the camera starts. 0 = the point's south side (-Z), looking north; it goes " +
                "clockwise seen from above. The camera moves there straight away, keeping its own distance and height."));
            EditorGUILayout.PropertyField(shot.FindPropertyRelative("orbitDegrees"), new GUIContent("Degrees Moved",
                "How far round the camera goes over the Time below, from Start Degrees. Negative goes the other way."));
        }
        else if (animation.enumValueIndex == (int)CameraSequence.ShotAnimation.Animate2Shot)
        {
            var endProp = shot.FindPropertyRelative("endCamera");
            var endCam  = endProp.objectReferenceValue as CinemachineCamera;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(endProp, new GUIContent("End Camera",
                    "The second shot: where the camera ends up, and the way it faces there. Its Cinemachine Camera is " +
                    "kept switched off — it is only a mark the shot camera moves to."));
                if (endCam == null)
                {
                    using (new EditorGUI.DisabledScope(cam == null))
                        if (GUILayout.Button(new GUIContent("Generate", "A second camera, a copy of the shot's camera (same place and lens), " +
                                                                        "selected so you can move it to the end framing."),
                                             GUILayout.Width(70)))
                            endProp.objectReferenceValue = CreateEndCamera(i + 1, cam);
                }
                else if (GUILayout.Button("Select", GUILayout.Width(70)))
                {
                    Selection.activeGameObject = endCam.gameObject;
                    EditorGUIUtility.PingObject(endCam.gameObject);
                }
            }
            if (endProp.objectReferenceValue == null)
                EditorGUILayout.LabelField("No End Camera — the camera stays still. Press Generate.", EditorStyles.miniLabel);
        }
        else
        {
            EditorGUILayout.PropertyField(shot.FindPropertyRelative("zoomDistance"), new GUIContent("Distance",
                "How far the camera travels forward. The world scale is small, so small values go a long way."));
        }
        EditorGUILayout.PropertyField(shot.FindPropertyRelative("animationTime"), new GUIContent("Time (s)",
            "Seconds the movement takes, from the moment the shot starts. It holds at the end."));
        EditorGUILayout.PropertyField(shot.FindPropertyRelative("easing"), new GUIContent("Easing"));
        EditorGUI.indentLevel--;

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(EditorGUIUtility.labelWidth);
            bool playing = _previewCam != null && _previewShot == _sequence.shots[i];
            using (new EditorGUI.DisabledScope(cam == null || Application.isPlaying))
            {
                if (playing)
                {
                    float left = _previewShot.animationTime - (float)(EditorApplication.timeSinceStartup - _previewStart);
                    if (GUILayout.Button(new GUIContent($"■ Stop Preview ({Mathf.Max(0f, left):F1}s)",
                            "Stops and puts the camera back where it was placed.")))
                        StopPreview();
                }
                else if (GUILayout.Button(new GUIContent("▶ Preview in Cm View",
                             "Plays this shot's movement in the Cinemachine View window, then puts the camera back where it was placed.")))
                {
                    // Pick up any field just edited before reading the shot.
                    shot.serializedObject.ApplyModifiedProperties();
                    StartPreview(_sequence.shots[i]);
                }
            }
        }
    }

    // ── Characters ──────────────────────────────────────────────────────
    // Every Sequence Character the shot's camera can see where it is placed (and an Animate 2 Shot's End
    // Camera too), each with its Animator's states to play as the shot starts. One given an animation stays
    // listed after it leaves the view, so it never drops off unseen.
    private void DrawCharacters(SerializedProperty shot, CinemachineCamera cam)
    {
        var anims = shot.FindPropertyRelative("characterAnimations");

        var inView = new List<SequenceCharacter>();
        if (cam != null)
        {
            var views = new List<CinemachineCamera> { cam };
            if (shot.FindPropertyRelative("animated").boolValue &&
                shot.FindPropertyRelative("animation").enumValueIndex == (int)CameraSequence.ShotAnimation.Animate2Shot &&
                shot.FindPropertyRelative("endCamera").objectReferenceValue is CinemachineCamera end)
                views.Add(end);
            foreach (var c in _characters)
                if (c != null && views.Any(v => InView(v, c))) inView.Add(c);
        }

        var listed = new List<SequenceCharacter>(inView);
        for (int a = 0; a < anims.arraySize; a++)
        {
            var c = anims.GetArrayElementAtIndex(a).FindPropertyRelative("character").objectReferenceValue as SequenceCharacter;
            if (c != null && !listed.Contains(c)) listed.Add(c);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Characters", EditorStyles.miniBoldLabel, GUILayout.Width(70));
            if (listed.Count == 0)
                EditorGUILayout.LabelField(cam == null ? "No camera."
                    : "None in view. Add a Sequence Character component to a character to have it listed.", EditorStyles.miniLabel);
        }
        foreach (var c in listed)
            if (!DrawCharacterRow(anims, c, inView.Contains(c))) return;

        // An animation whose character has gone from the scene (or wasn't found when a preset loaded).
        for (int a = 0; a < anims.arraySize; a++)
        {
            var entry = anims.GetArrayElementAtIndex(a);
            if (entry.FindPropertyRelative("character").objectReferenceValue != null) continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12);
                EditorGUILayout.LabelField($"Missing character — '{entry.FindPropertyRelative("state").stringValue}' won't play.", WarningStyle);
                if (GUILayout.Button("✕", GUILayout.Width(24))) { anims.DeleteArrayElementAtIndex(a); return; }
            }
        }
    }

    // False when the list changed under us.
    private static bool DrawCharacterRow(SerializedProperty anims, SequenceCharacter c, bool inView)
    {
        int index = -1;
        for (int a = 0; a < anims.arraySize && index < 0; a++)
            if (anims.GetArrayElementAtIndex(a).FindPropertyRelative("character").objectReferenceValue == c) index = a;
        var entry = index >= 0 ? anims.GetArrayElementAtIndex(index) : null;

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(12);
            if (GUILayout.Button(new GUIContent(c.DisplayName, "Selects the character."), EditorStyles.label, GUILayout.Width(120)))
            {
                Selection.activeGameObject = c.gameObject;
                EditorGUIUtility.PingObject(c.gameObject);
            }

            var animator = c.Animator;
            var states   = StatesOf(animator);
            if (states == null)
            {
                EditorGUILayout.LabelField(animator == null ? "No Animator" : "Animator has no controller", EditorStyles.miniLabel);
                if (entry != null && GUILayout.Button(new GUIContent("✕", "Removes this character's animation from the shot."),
                                                      GUILayout.Width(24)))
                {
                    anims.DeleteArrayElementAtIndex(index);
                    return false;
                }
            }
            else
            {
                string current = entry?.FindPropertyRelative("state").stringValue;
                var options = new List<string> { "None" };
                options.AddRange(states.Select(s => s.label));
                int pick = 0;
                for (int s = 0; s < states.Count; s++) if (states[s].path == current) pick = s + 1;
                // A state the controller no longer has stays shown, so it isn't lost without a word.
                int missing = -1;
                if (!string.IsNullOrEmpty(current) && pick == 0)
                {
                    missing = options.Count;
                    options.Add($"{current} (not in the controller)");
                    pick = missing;
                }

                int chosen = EditorGUILayout.Popup(pick, options.ToArray());
                if (chosen != pick && chosen == 0)
                {
                    anims.DeleteArrayElementAtIndex(index);
                    return false;
                }
                if (chosen != pick && chosen != missing)
                {
                    if (entry == null)
                    {
                        anims.InsertArrayElementAtIndex(anims.arraySize);
                        entry = anims.GetArrayElementAtIndex(anims.arraySize - 1);
                        entry.FindPropertyRelative("character").objectReferenceValue = c;
                        entry.FindPropertyRelative("blend").floatValue = 0.25f;
                    }
                    entry.FindPropertyRelative("state").stringValue = states[chosen - 1].path;
                    entry.FindPropertyRelative("layer").intValue    = states[chosen - 1].layer;
                }
                if (entry != null)
                {
                    EditorGUIUtility.labelWidth = 36;
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("blend"), new GUIContent("Blend",
                        "Seconds the Animator cross-fades from what it was playing into this state, as the shot starts."),
                        GUILayout.Width(90));
                    EditorGUIUtility.labelWidth = 0;
                }
            }

            if (!inView)
                EditorGUILayout.LabelField(new GUIContent("out of view", "Not in the shot camera's view where it is placed. Its animation still plays."),
                                           WarningStyle, GUILayout.Width(70));
        }
        return true;
    }

    private static GUIStyle _warningStyle;
    private static GUIStyle WarningStyle => _warningStyle ??=
        new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 0.6f, 0.3f) } };

    // Whether any part of the character is inside the camera's view — posed the way the thumbnail draws it.
    private static bool InView(CinemachineCamera cam, SequenceCharacter c)
    {
        // An enabled camera's state is kept fresh by the brain; a switched-off one (an End Camera) only has its transform.
        bool live     = cam.isActiveAndEnabled;
        var  state    = cam.State;
        var  position = live ? state.GetFinalPosition()    : cam.transform.position;
        var  rotation = live ? state.GetFinalOrientation() : cam.transform.rotation;
        var  lens     = live ? state.Lens : cam.Lens;
        if (lens.FieldOfView <= 0.01f) lens = LensSettings.Default;

        float aspect = CinemachinePreviewRig.GameAspect(cam);
        float near   = Mathf.Max(0.001f, lens.NearClipPlane);
        float far    = Mathf.Max(near + 0.01f, lens.FarClipPlane);
        float size   = Mathf.Max(0.001f, lens.OrthographicSize);
        Matrix4x4 projection = lens.Orthographic
            ? Matrix4x4.Ortho(-size * aspect, size * aspect, -size, size, near, far)
            : Matrix4x4.Perspective(Mathf.Clamp(lens.FieldOfView, 1f, 179f), aspect, near, far);
        // World to camera space, which looks down -Z.
        Matrix4x4 view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
        var planes = GeometryUtility.CalculateFrustumPlanes(projection * view);

        bool any = false;
        var bounds = new Bounds();
        foreach (var r in c.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (!any) bounds = r.bounds; else bounds.Encapsulate(r.bounds);
            any = true;
        }
        if (any) return GeometryUtility.TestPlanesAABB(planes, bounds);
        // Nothing drawn — go by where it stands.
        Vector3 p = c.transform.position;
        return planes.All(plane => plane.GetSide(p));
    }

    private struct AnimatorStateEntry
    {
        public string path;    // Layer.State or Layer.SubMachine.State — what the sequence plays
        public string label;   // what the dropdown shows
        public int    layer;
    }

    // Every state in the Animator's controller (an override controller's base). Null when there is no controller.
    private static List<AnimatorStateEntry> StatesOf(Animator animator)
    {
        if (animator == null) return null;
        var rac = animator.runtimeAnimatorController;
        while (rac is AnimatorOverrideController over) rac = over.runtimeAnimatorController;
        if (!(rac is UnityEditor.Animations.AnimatorController controller)) return null;

        var list   = new List<AnimatorStateEntry>();
        var layers = controller.layers;
        for (int l = 0; l < layers.Length; l++)
        {
            // A synced layer plays another layer's states — nothing of its own to pick.
            if (layers[l].syncedLayerIndex >= 0 || layers[l].stateMachine == null) continue;
            CollectStates(layers[l].stateMachine, layers[l].name, string.Empty, l, list);
        }
        return list;
    }

    private static void CollectStates(UnityEditor.Animations.AnimatorStateMachine machine, string layerName,
                                      string within, int layer, List<AnimatorStateEntry> list)
    {
        foreach (var child in machine.states)
        {
            string name = within + child.state.name;
            list.Add(new AnimatorStateEntry
            {
                path  = layerName + "." + name,
                label = layer == 0 ? name : $"{name} ({layerName})",
                layer = layer,
            });
        }
        foreach (var sub in machine.stateMachines)
            CollectStates(sub.stateMachine, layerName, within + sub.stateMachine.name + ".", layer, list);
    }

    // An orbit point under the sequence, on whatever the camera is looking at — or the Scene view's
    // focus when the camera looks at nothing with a collider.
    private Transform CreateOrbitPoint(int shotNumber, CinemachineCamera cam)
    {
        var go = new GameObject($"Shot {shotNumber} Orbit Point");
        Undo.RegisterCreatedObjectUndo(go, "Generate Orbit Point");
        Undo.SetTransformParent(go.transform, ShotParent(), "Generate Orbit Point");

        Transform t = cam.transform;
        var view = SceneView.lastActiveSceneView;
        if (Physics.Raycast(t.position, t.forward, out var hit, Mathf.Infinity, ~0, QueryTriggerInteraction.Ignore))
            go.transform.position = hit.point;
        else if (view != null)
            go.transform.position = view.pivot;
        else
            go.transform.position = t.position + t.forward;

        EditorGUIUtility.SetIconForObject(go, EditorGUIUtility.IconContent("sv_label_1").image as Texture2D);
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
        return go.transform;
    }

    // A second camera for Animate 2 Shot — a copy of the shot's camera, its Cinemachine Camera switched
    // off so it never goes live (the brain would cut to it); the previews still draw it from its transform.
    private CinemachineCamera CreateEndCamera(int shotNumber, CinemachineCamera cam)
    {
        var go = new GameObject($"Shot {shotNumber} End Camera");
        Undo.RegisterCreatedObjectUndo(go, "Generate End Camera");
        Undo.SetTransformParent(go.transform, ShotParent(), "Generate End Camera");
        go.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);

        var end = go.AddComponent<CinemachineCamera>();
        end.Lens    = cam.Lens;
        end.enabled = false;

        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log($"[CameraSequenceWindow] Generated '{go.name}' at '{cam.name}' — move it to the end framing.");
        return end;
    }

    // The shot's view on the right of its card — for Animate 2 Shot, its start and end side by side.
    private void DrawPreviews(SerializedProperty shot, int i, CinemachineCamera cam)
    {
        bool twoShot = shot.FindPropertyRelative("animated").boolValue &&
                       shot.FindPropertyRelative("animation").enumValueIndex == (int)CameraSequence.ShotAnimation.Animate2Shot;
        if (!twoShot)
        {
            DrawPreview(_previews, i, cam, "No camera", true);
            return;
        }

        var endCam = shot.FindPropertyRelative("endCamera").objectReferenceValue as CinemachineCamera;
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(PreviewWidth * 2f + 4f)))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPreview(_previews, i, cam, "No camera", true);
                GUILayout.Space(4f);
                DrawPreview(_endPreviews, i, endCam, "No end camera");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Start", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(PreviewWidth));
                GUILayout.Space(4f);
                EditorGUILayout.LabelField("End", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(PreviewWidth));
            }
        }
    }

    // What a camera sees, the shape of the game view.
    // shotCamera: the shot's own camera, whose click opens the shot's settings beside it in the Cm View.
    private void DrawPreview(List<RenderTexture> previews, int i, CinemachineCamera cam, string emptyLabel, bool shotCamera = false)
    {
        float height = PreviewWidth / CinemachinePreviewRig.GameAspect(cam);
        Rect rect = GUILayoutUtility.GetRect(PreviewWidth, height,
                                             GUILayout.Width(PreviewWidth), GUILayout.Height(height));

        // Clicking the thumbnail opens the camera in the Cinemachine View window.
        if (cam != null)
        {
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                if (shotCamera) CinemachineViewWindow.WatchShot(_sequence, i);
                else            CinemachineViewWindow.Watch(cam);
                e.Use();
                return;
            }
        }
        if (Event.current.type != EventType.Repaint) return;

        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.35f));
        if (cam == null)
        {
            var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(rect, emptyLabel, style);
            return;
        }

        while (previews.Count <= i) previews.Add(null);
        var target = previews[i];
        CinemachinePreviewRig.EnsureTarget(ref target, rect, "Camera Sequence Shot Preview");
        previews[i] = target;

        _previewRig ??= new CinemachinePreviewRig();
        var shot    = i < _sequence.shots.Count ? _sequence.shots[i] : null;
        var focusOn = CameraSequenceFogPreview.FocusFor(shot);
        if (_previewRig.Render(cam, target, null, focusOn, null, shot?.volumeProfile))
            GUI.DrawTexture(rect, target, ScaleMode.StretchToFill, false);
    }

    private static void DrawDialogueLength(SerializedProperty lines, float shotLength, bool untilKey,
                                           bool textFades, bool raisesPanel, bool lowersPanel,
                                           DialogueTiming timing)
    {
        if (!timing.found)
        {
            EditorGUILayout.LabelField("No DialogueTextController in the scene.", EditorStyles.miniLabel);
            return;
        }

        if (!textFades)
        {
            // No fades: lines swap on their holds and the last one stays up for the rest of the shot.
            float toLast = 0f;
            int keysToLast = 0;
            for (int l = 0; l < lines.arraySize - 1; l++)
            {
                var line = lines.GetArrayElementAtIndex(l);
                if (line.FindPropertyRelative("pressAnyKey").boolValue) keysToLast++;
                else toLast += line.FindPropertyRelative("hold").floatValue;
            }

            if (keysToLast > 0)
            {
                EditorGUILayout.LabelField(untilKey
                    ? $"Last line shows after {toLast:F1}s + {Presses(keysToLast)}; the shot's key press comes after those."
                    : $"Last line shows after {toLast:F1}s + {Presses(keysToLast)}; the shot ends at {shotLength:F1}s regardless.",
                    EditorStyles.miniLabel);
                return;
            }

            var heldStyle = new GUIStyle(EditorStyles.miniLabel);
            bool cut = !untilKey && toLast > shotLength;
            if (cut) heldStyle.normal.textColor = new Color(1f, 0.6f, 0.3f);
            EditorGUILayout.LabelField(cut
                ? $"Last line shows at {toLast:F1}s — after the shot ends ({shotLength:F1}s); the next shot cuts it off."
                : $"Last line shows at {toLast:F1}s and stays up until the next shot.", heldStyle);
            return;
        }

        float spoken = timing.backgroundFade * ((raisesPanel ? 1f : 0f) + (lowersPanel ? 1f : 0f));
        int keys = 0;
        bool lastFadesOut = false;
        for (int l = 0; l < lines.arraySize; l++)
        {
            var line = lines.GetArrayElementAtIndex(l);
            bool key = line.FindPropertyRelative("pressAnyKey").boolValue;
            if (key) keys++;
            bool fadeOut = line.FindPropertyRelative("fadeOut").boolValue;
            spoken += (line.FindPropertyRelative("fadeIn").boolValue  ? timing.fadeIn  : 0f)
                    + (key ? 0f : line.FindPropertyRelative("hold").floatValue)
                    + (fadeOut ? timing.fadeOut : 0f)
                    + (l < lines.arraySize - 1 ? DialogueTiming.Gap : 0f);
            if (!string.IsNullOrWhiteSpace(line.FindPropertyRelative("text").stringValue)) lastFadesOut = fadeOut;
        }

        // A Press Any Key shot whose last line fades out moves on when that fade ends.
        if (untilKey && lastFadesOut)
        {
            EditorGUILayout.LabelField(keys > 0
                ? $"Dialogue runs {spoken:F1}s + {Presses(keys)}; the shot moves on once the last line fades out."
                : $"Dialogue runs {spoken:F1}s; the shot moves on once the last line fades out.",
                EditorStyles.miniLabel);
            return;
        }

        if (keys > 0)
        {
            EditorGUILayout.LabelField(untilKey
                ? $"Dialogue runs {spoken:F1}s + {Presses(keys)}; the shot's key press comes after those."
                : $"Dialogue runs {spoken:F1}s + {Presses(keys)}; the shot ends at {shotLength:F1}s regardless.",
                EditorStyles.miniLabel);
            return;
        }

        if (untilKey)
        {
            EditorGUILayout.LabelField($"Dialogue runs {spoken:F1}s; a key press before then cuts it off.",
                                       EditorStyles.miniLabel);
            return;
        }

        var style = new GUIStyle(EditorStyles.miniLabel);
        if (spoken > shotLength) style.normal.textColor = new Color(1f, 0.6f, 0.3f);
        EditorGUILayout.LabelField(spoken > shotLength
            ? $"Dialogue runs {spoken:F1}s — longer than the shot ({shotLength:F1}s); the next shot cuts it off."
            : $"Dialogue runs {spoken:F1}s of {shotLength:F1}s.", style);
    }

    private static void DrawLineFade(SerializedProperty prop, string label, string tooltip)
    {
        prop.boolValue = EditorGUILayout.ToggleLeft(new GUIContent(label, tooltip), prop.boolValue,
                                                    GUILayout.Width(42));
    }

    private static string Presses(int n) => n == 1 ? "1 key press" : $"{n} key presses";

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
