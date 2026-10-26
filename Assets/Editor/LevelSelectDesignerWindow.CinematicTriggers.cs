using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Cinematic boxes — a trigger box across the river at every node with Cinematic on (see
/// DrawNodeCinematic). The boat sailing into one plays that node's camera sequence preset
/// (<see cref="LevelSelectCinematicTrigger"/>).
///
/// The same box as the Audio one (<see cref="TryMusicTriggerBox"/>), built on Generate and whenever
/// a node's Cinematic settings change — the preset is written onto the box. A node set to Press E
/// gets an interact point at the box's middle instead (<see cref="LevelSelectCinematicInteract"/>).
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const string CinematicTriggersParent = "CINEMATICTRIGGERS";

    private static readonly GUIContent[] StartLabels = { new GUIContent("Sail Into Box"), new GUIContent("Press E") };

    /// <summary>Clears the cinematic boxes and builds one for each node with Cinematic on.</summary>
    private void RebuildCinematicTriggers()
    {
        if (_data == null) return;

        var parent = FindParent(CinematicTriggersParent);
        if (parent != null)
            foreach (var child in parent.transform.Cast<Transform>().ToList())
                Undo.DestroyObjectImmediate(child.gameObject);

        var nodes = _data.nodes.Where(n => n != null && n.cinematicTrigger).ToList();
        if (nodes.Count == 0) return;

        if (parent == null) parent = FindOrCreateParent(CinematicTriggersParent);

        // Out of the way of mouse picks, as the music boxes are.
        int layer = LayerMask.NameToLayer("Ignore Raycast");

        int built = 0;
        foreach (var node in nodes)
        {
            if (node.cinematicPreset == null)
            {
                Debug.LogWarning($"[LevelSelectDesigner] Node {node.id} has Cinematic on but no preset — no box built for it.");
                continue;
            }
            if (!TryMusicTriggerBox(node.id, out Vector3 centre, out Quaternion facing, out Vector3 size))
            {
                Debug.LogWarning($"[LevelSelectDesigner] Node {node.id} has Cinematic on but is not on a " +
                                 "river — no cinematic box built for it.");
                continue;
            }

            var go = new GameObject($"CinematicTrigger_{SanitiseAssetName(node.id)}");
            Undo.RegisterCreatedObjectUndo(go, "Cinematic Trigger");
            go.transform.SetParent(parent.transform, false);
            go.transform.SetPositionAndRotation(centre, facing);
            if (layer >= 0) go.layer = layer;

            if (node.cinematicStart == LevelSelectDesignerData.CinematicStart.PressKey)
            {
                // Press E: an interact point at the middle of the box, no collider.
                var point = go.AddComponent<LevelSelectInteractPoint>();
                point.prompt = node.cinematicPrompt;
                point.radius = node.cinematicReach;

                var interact = go.AddComponent<LevelSelectCinematicInteract>();
                interact.nodeId = node.id;
                interact.preset = node.cinematicPreset;
            }
            else
            {
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size      = size;

                var trigger = go.AddComponent<LevelSelectCinematicTrigger>();
                trigger.nodeId = node.id;
                trigger.preset = node.cinematicPreset;
            }
            built++;
        }

        Debug.Log($"[LevelSelectDesigner] Built {built} cinematic box(es) under {CinematicTriggersParent}.");
    }

    /// <summary>Writes a Press E node's prompt and reach onto its point without rebuilding it.</summary>
    private void UpdateCinematicPoint(LevelSelectDesignerData.DesignerNode node)
    {
        var parent = FindParent(CinematicTriggersParent);
        var go     = parent != null ? parent.transform.Find($"CinematicTrigger_{SanitiseAssetName(node.id)}") : null;
        if (go == null || !go.TryGetComponent<LevelSelectInteractPoint>(out var point)) return;

        Undo.RecordObject(point, "Node Cinematic");
        point.prompt = node.cinematicPrompt;
        point.radius = node.cinematicReach;
        EditorUtility.SetDirty(point);
    }

    /// <summary>
    /// Moves the cameras of every node with Pin Cameras on by however far the node has moved since
    /// they last moved (node position − cinematicPinnedAt), and the preset's saved copies with them,
    /// so Load and the storyboard's unsaved-changes check agree with the scene. Called from MarkDirty,
    /// inside the same undo step as the node move; a move that skipped MarkDirty is caught up next time.
    /// </summary>
    private void SyncPinnedCinematicCameras()
    {
        if (_data == null) return;

        foreach (var node in _data.nodes)
        {
            if (node == null || !node.cinematicTrigger || !node.cinematicPinCameras) continue;
            var preset = node.cinematicPreset;
            if (preset == null) continue;

            Vector3 delta = node.worldPosition - node.cinematicPinnedAt;
            if (delta.sqrMagnitude < 1e-10f) continue;

            Undo.RecordObject(_data, "Move Node");
            node.cinematicPinnedAt = node.worldPosition;
            ShiftPinnedCameras(preset, delta);
        }

        // A soul juice tank's cinematic pins to its pool the same way.
        foreach (var pool in TowerCinematicPools())
        {
            if (!pool.towerCinematicPinCameras) continue;
            var preset = pool.towerCinematicPreset;
            if (preset == null) continue;

            Vector3 at    = PoolNodePosition(pool);
            Vector3 delta = at - pool.towerCinematicPinnedAt;
            if (delta.sqrMagnitude < 1e-10f) continue;

            Undo.RecordObject(_data, "Move Node");
            pool.towerCinematicPinnedAt = at;
            ShiftPinnedCameras(preset, delta);
        }
    }

    // Moves a pinned preset's scene objects and its saved copies by delta.
    private void ShiftPinnedCameras(CameraSequencePreset preset, Vector3 delta)
    {
        // The scene objects. One parented under another already moves with it — skip it.
        var moving = new List<Transform>();
        foreach (var stored in preset.storedObjects)
        {
            var link = CameraSequenceLink.Find(stored.linkId);
            if (link != null && !moving.Contains(link.transform)) moving.Add(link.transform);
        }
        foreach (var t in moving)
        {
            if (moving.Any(other => other != t && t.IsChildOf(other))) continue;
            // A camera with its own clamp follows that instead.
            var ownLink = t.GetComponent<CameraSequenceLink>();
            if (ownLink != null && IsCameraClamped(ownLink.id)) continue;
            Undo.RecordObject(t, "Move Node");
            t.position += PinDelta(ownLink != null ? ownLink.id : null, delta);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }

        // The preset's copies are world positions — every one moves.
        Undo.RecordObject(preset, "Move Node");
        foreach (var stored in preset.storedObjects)
            if (!IsCameraClamped(stored.linkId)) stored.position += PinDelta(stored.linkId, delta);
        EditorUtility.SetDirty(preset);
    }

    // A View Target holds the height of its shot's camera (and that shot's Orbit Point / End Camera) — the pin moves it across the map only.
    private static Vector3 PinDelta(string linkId, Vector3 delta)
        => CameraSequenceViewTargets.HeightHeld(linkId) ? new Vector3(delta.x, 0f, delta.z) : delta;

    // The boat reaching this node plays its camera sequence preset.
    private void DrawNodeCinematic(string nodeId)
    {
        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node == null) return;

        EditorGUILayout.Space(6);
        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.ToggleLeft(new GUIContent(
            node.cinematicStart == LevelSelectDesignerData.CinematicStart.PressKey ? "Interact trigger cinematic" : "Cinematic",
            "The boat sailing into the box across the river here plays this node's camera sequence. " +
            "The box is built and sized off the river automatically, like the Audio one."),
            node.cinematicTrigger, EditorStyles.boldLabel);

        var  preset    = node.cinematicPreset;
        var  start     = node.cinematicStart;
        var  prompt    = node.cinematicPrompt;
        var  reach     = node.cinematicReach;
        bool pin       = node.cinematicPinCameras;
        bool newPreset = false;
        if (on)
        {
            EditorGUI.indentLevel++;
            newPreset = DrawCinematicPresetFields("node", "No preset — this node does nothing.", ref preset, ref pin);

            start = (LevelSelectDesignerData.CinematicStart)EditorGUILayout.Popup(new GUIContent("Trigger",
                "Sail Into Box: plays as the boat crosses the box across the river.\n" +
                "Press E: a prompt comes up in reach of the node, and the interact key plays it."),
                (int)start, StartLabels);
            if (start == LevelSelectDesignerData.CinematicStart.PressKey)
            {
                EditorGUI.indentLevel++;
                prompt = EditorGUILayout.TextField(new GUIContent("Prompt",
                    "What the prompt reads. The key is added by the prompt itself, so this is just the doing."), prompt);
                reach = EditorGUILayout.Slider(new GUIContent("Reach",
                    "How near the boat has to be, measured flat across the water from the middle of the river here."),
                    reach, 0f, InteractMaxRadius);
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }
        bool changed = EditorGUI.EndChangeCheck();

        if (newPreset)
        {
            var created = CameraSequenceWindow.NewPresetFromDesigner();
            if (created != null) preset = created;
            changed = true;
        }
        if (!changed) return;

        Undo.RecordObject(_data, "Node Cinematic");
        bool rebuild = on != node.cinematicTrigger || preset != node.cinematicPreset ||
                       start != node.cinematicStart;
        // Pinning starts from where the node stands now — on switching it on, or on a new preset.
        if (rebuild || pin != node.cinematicPinCameras)
            node.cinematicPinnedAt = node.worldPosition;
        node.cinematicTrigger    = on;
        node.cinematicPreset     = preset;
        node.cinematicStart      = start;
        node.cinematicPrompt     = prompt;
        node.cinematicReach      = reach;
        node.cinematicPinCameras = pin;
        MarkDirty();
        // The preset is written onto the box, so a new one means a new box. Prompt and reach are
        // only written onto the point already there, so typing and dragging do not rebuild.
        if (rebuild) RebuildCinematicTriggers();
        else         UpdateCinematicPoint(node);
        Repaint();
        // The storyboard's file panel ran mid-draw — start this window's layout over.
        if (newPreset) GUIUtility.ExitGUI();
    }

    /// <summary>
    /// The preset row, Open in Camera Sequence and Pin Cameras — the same for every cinematic the designer
    /// places, a node's or a soul juice tank's. Hands back whether New Preset was pressed.
    /// </summary>
    private bool DrawCinematicPresetFields(string owner, string noPresetNote,
                                           ref CameraSequencePreset preset, ref bool pin)
    {
        bool newPreset;
        using (new EditorGUILayout.HorizontalScope())
        {
            var picked = CameraSequencePresetIO.DrawPicker(new GUIContent("Preset",
                $"The camera sequence this {owner} plays. Presets are kept in " + CameraSequencePresetIO.Folder + "."), preset);
            preset = (CameraSequencePreset)EditorGUILayout.ObjectField(
                picked, typeof(CameraSequencePreset), false, GUILayout.Width(120f));
            newPreset = GUILayout.Button(new GUIContent("New Preset",
                $"A new, empty preset for this {owner}, opened in the Camera Sequence storyboard."), GUILayout.Width(80f));
        }
        if (preset != null) DrawOpenInStoryboardButton(preset);
        if (preset == null && !newPreset)
            EditorGUILayout.LabelField(noPresetNote, EditorStyles.miniLabel);

        pin = EditorGUILayout.Toggle(new GUIContent("Pin Cameras",
            $"The preset's cameras, orbit points and end cameras move with this {owner}: drag it and " +
            "they go along with it, keeping where they stand around it. The preset's saved copies move too, " +
            "so Load puts them back in the new place."), pin);
        return newPreset;
    }

    // ─────────────────────────────────────────────
    // SOUL JUICE TANK
    // ─────────────────────────────────────────────

    /// <summary>A pool whose soul juice tank plays a cinematic as its souls are let out.</summary>
    private static bool HasTowerCinematic(LevelSelectDesignerData.DesignerPool pool)
        => pool != null && pool.hasTower && pool.towerCinematic &&
           pool.towerKind == LevelSelectDesignerData.TowerKind.SoulJuiceTank;

    private IEnumerable<LevelSelectDesignerData.DesignerPool> TowerCinematicPools()
        => _data.pools.Where(HasTowerCinematic);

    private Vector3 PoolNodePosition(LevelSelectDesignerData.DesignerPool pool)
        => _data.nodes.Find(n => n != null && n.id == pool.nodeId)?.worldPosition ?? Vector3.zero;

    /// <summary>
    /// The soul juice tank's Cinematic: the same Preset, Open and Pin Cameras as a node's. Its trigger is
    /// always the tower's own Press E — the press that lets the souls out plays it too — so the prompt and
    /// reach shown here are that release point's, written straight onto the generated tower.
    /// </summary>
    private void DrawTowerCinematic(LevelSelectDesignerData.DesignerPool pool)
    {
        EditorGUILayout.Space(6);
        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.ToggleLeft(new GUIContent("Interact trigger cinematic",
            "Pressing E at the tower lets its souls out AND plays this camera sequence, so the cameras " +
            "watch them set off. A Play Once preset that has played just lets the souls out."),
            pool.towerCinematic, EditorStyles.boldLabel);

        var  preset    = pool.towerCinematicPreset;
        bool pin       = pool.towerCinematicPinCameras;
        bool newPreset = false;
        var  point     = FindPoolTower(pool)?.GetComponent<LevelSelectInteractPoint>();
        string prompt  = point != null ? point.prompt : null;
        float  reach   = point != null ? point.radius : 0f;
        if (on)
        {
            EditorGUI.indentLevel++;
            newPreset = DrawCinematicPresetFields("tower", "No preset — pressing E only lets the souls out.",
                                                  ref preset, ref pin);

            EditorGUILayout.LabelField(new GUIContent("Trigger", "Always the tower's own Press E — the same " +
                "press that lets the souls out."), new GUIContent("Press E (releases the souls)"));
            EditorGUI.indentLevel++;
            if (point != null)
            {
                prompt = EditorGUILayout.TextField(new GUIContent("Prompt",
                    "What the tower's prompt reads. The key is added by the prompt itself, so this is just the doing."), prompt);
                reach = EditorGUILayout.Slider(new GUIContent("Reach",
                    "How near the boat has to be, measured flat across the water from the middle of the tower."),
                    reach, 0f, InteractMaxRadius);
            }
            else
            {
                EditorGUILayout.LabelField("Prompt and reach come up once the tower is generated.", EditorStyles.miniLabel);
            }
            EditorGUI.indentLevel--;

            // The node can carry its own Press E cinematic — two prompts would come up at one tower.
            var node = _data.nodes.Find(n => n != null && n.id == pool.nodeId);
            if (node != null && node.cinematicTrigger && node.cinematicStart == LevelSelectDesignerData.CinematicStart.PressKey)
                EditorGUILayout.HelpBox("This pool's node has its own Press E cinematic too — two prompts will come " +
                                        "up here. Switch one of them off.", MessageType.Warning);
            EditorGUI.indentLevel--;
        }
        bool changed = EditorGUI.EndChangeCheck();

        if (newPreset)
        {
            var created = CameraSequenceWindow.NewPresetFromDesigner();
            if (created != null) preset = created;
            changed = true;
        }
        if (!changed) return;

        Undo.RecordObject(_data, "Tower Cinematic");
        // Pinning starts from where the pool stands now — on switching it on, or on a new preset.
        if (on != pool.towerCinematic || preset != pool.towerCinematicPreset || pin != pool.towerCinematicPinCameras)
            pool.towerCinematicPinnedAt = PoolNodePosition(pool);
        pool.towerCinematic           = on;
        pool.towerCinematicPreset     = preset;
        pool.towerCinematicPinCameras = pin;
        MarkDirty();

        // Written onto what is already generated — no rebuild of the tower's meshes.
        ApplyTowerCinematic(pool);
        if (point != null && (prompt != point.prompt || !Mathf.Approximately(reach, point.radius)))
        {
            Undo.RecordObject(point, "Tower Cinematic");
            point.prompt = prompt;
            point.radius = reach;
            EditorUtility.SetDirty(point);
        }
        Repaint();
        // The storyboard's file panel ran mid-draw — start this window's layout over.
        if (newPreset) GUIUtility.ExitGUI();
    }

    /// <summary>Writes the pool's cinematic onto its generated tower's release, or clears it.</summary>
    private void ApplyTowerCinematic(LevelSelectDesignerData.DesignerPool pool)
    {
        var release = FindPoolTower(pool)?.GetComponent<SoulJuiceTankRelease>();
        if (release == null) return;

        var wanted = HasTowerCinematic(pool) ? pool.towerCinematicPreset : null;
        if (release.cinematic == wanted) return;

        Undo.RecordObject(release, "Tower Cinematic");
        release.cinematic = wanted;
        EditorUtility.SetDirty(release);
    }

    // Opens the Camera Sequence storyboard on this preset (loads it there if it isn't the active one).
    private void DrawOpenInStoryboardButton(CameraSequencePreset preset)
    {
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(EditorGUI.indentLevel * 15f);
            if (GUILayout.Button(new GUIContent("Open in Camera Sequence",
                    "Opens the Camera Sequence storyboard on this preset. If another preset is active there it loads " +
                    "this one, asking first if the storyboard has unsaved changes.")))
            {
                CameraSequenceWindow.OpenPresetFromDesigner(preset);
                // Dialogs may have run mid-draw — start this window's layout over.
                GUIUtility.ExitGUI();
            }
        }
    }
}

#endif
