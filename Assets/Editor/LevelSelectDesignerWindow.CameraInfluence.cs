using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Camera Influence — per node (under Cinematic in the node row). Switching it on spawns a camera,
/// CameraInfluence_&lt;node&gt; under CAMERAINFLUENCES, standing where the follow camera would be
/// behind the boat at the node; Tweak in Cinemachine View opens it there to set by hand. A box
/// across the river at the node (the Audio box, run out to Length along the river) is where it
/// takes hold: Influence draws the follow camera towards it, Cut puts the view on it. See
/// <see cref="LevelSelectCameraInfluence"/> for the runtime.
///
/// The camera is pinned to its node, and the box and settings are written onto it from MarkDirty
/// and Generate. CAMERAINFLUENCES is never cleared — the cameras are set by hand — so switching the
/// toggle off only switches the camera off; it is removed when its node is.
///
/// While the node is selected with the toggle on, the box is drawn in the Scene view, in the Fog
/// Map's gizmo style.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const string CameraInfluencesParent = "CAMERAINFLUENCES";

    private static readonly GUIContent[] InfluenceModeLabels = { new GUIContent("Influence"), new GUIContent("Cut") };
    private static readonly GUIContent[] InfluenceLookLabels = { new GUIContent("Follow Boat"), new GUIContent("Fixed") };
    private static readonly GUIContent[] InfluenceBlendLabels = { new GUIContent("Cut"), new GUIContent("Transition") };

    private static string InfluenceCameraName(string nodeId) => $"CameraInfluence_{SanitiseAssetName(nodeId)}";

    /// <summary>The node's influence camera, switched on or not.</summary>
    private static LevelSelectCameraInfluence FindInfluenceCamera(string nodeId)
    {
        var parent = FindParent(CameraInfluencesParent);
        var t      = parent != null ? parent.transform.Find(InfluenceCameraName(nodeId)) : null;
        return t != null ? t.GetComponent<LevelSelectCameraInfluence>() : null;
    }

    // The box across the river at the node, run out to the node's Length along it, its Box Width
    // across it (centred on the river) and stood up to its Box Height from the same bottom (below
    // the water), then lowered by its Box Drop and moved by its Offset Across / Along.
    private bool TryInfluenceBox(LevelSelectDesignerData.DesignerNode node,
                                 out Vector3 centre, out Quaternion facing, out Vector3 size)
    {
        if (!TryMusicTriggerBox(node.id, out centre, out facing, out size)) return false;
        size.z = Mathf.Max(node.cameraInfluenceLength, 0.01f);
        if (node.cameraInfluenceBoxWidth > 0f) size.x = node.cameraInfluenceBoxWidth;
        if (node.cameraInfluenceBoxHeight > 0f)
        {
            float bottom = centre.y - size.y * 0.5f;
            size.y   = node.cameraInfluenceBoxHeight;
            centre.y = bottom + size.y * 0.5f;
        }
        centre.y -= node.cameraInfluenceBoxDrop;
        centre   += facing * new Vector3(node.cameraInfluenceBoxOffsetAcross, 0f, node.cameraInfluenceBoxOffsetAlong);
        return true;
    }

    /// <summary>
    /// A new camera for the node, standing where the follow camera would sit behind the boat at the
    /// node (looking down the river) with its lens. Kept switched off — the follow camera reads it.
    /// Length and Fade start from the river's width here.
    /// </summary>
    private LevelSelectCameraInfluence SpawnInfluenceCamera(LevelSelectDesignerData.DesignerNode node)
    {
        if (!TryMusicTriggerBox(node.id, out Vector3 centre, out Quaternion facing, out Vector3 box))
        {
            Debug.LogWarning($"[LevelSelectDesigner] Node {node.id} is not on a river — no Camera Influence camera made for it.");
            return null;
        }

        node.cameraInfluenceLength = box.x;
        node.cameraInfluenceFade   = box.x * 0.25f;
        node.cameraInfluenceBoxHeight = box.y;
        node.cameraInfluenceBoxWidth  = box.x;

        var follow = Object.FindAnyObjectByType<LevelSelectCameraController>();
        float distance = follow != null ? follow.followDistance : 8f;
        float height   = follow != null ? follow.followHeight   : 3f;
        float lift     = follow != null ? follow.followTargetVerticalOffset : 0f;

        // The boat sits on the water, WaterSurfaceDrop below the node on the rim top.
        Vector3 boat   = new Vector3(node.worldPosition.x, node.worldPosition.y - _data.WaterSurfaceDrop, node.worldPosition.z);
        Vector3 lookAt = boat + Vector3.up * lift;
        Vector3 down   = facing * Vector3.forward;
        Vector3 at     = lookAt - down * distance + Vector3.up * height;

        var parent = FindOrCreateParent(CameraInfluencesParent);
        var go     = new GameObject(InfluenceCameraName(node.id));
        Undo.RegisterCreatedObjectUndo(go, "Camera Influence");
        go.transform.SetParent(parent.transform, false);
        go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(lookAt - at, Vector3.up));

        var cam = go.AddComponent<CinemachineCamera>();
        if (follow != null && follow.cam != null) cam.Lens = follow.cam.Lens;
        if (follow != null && follow.defaultVerticalFOV > 0f) cam.Lens.FieldOfView = follow.defaultVerticalFOV;
        cam.enabled = false;

        var influence = go.AddComponent<LevelSelectCameraInfluence>();
        influence.nodeId = node.id;
        node.cameraInfluencePinnedAt = node.worldPosition;
        return influence;
    }

    /// <summary>
    /// Every node's influence camera brought up to date: pinned to its node, its box and settings
    /// written on, switched on or off with the toggle, and removed with its node. Called from
    /// MarkDirty, in the same undo step as the edit, and from Generate (spawnMissing: a node with the
    /// toggle on whose camera has gone gets a fresh one).
    /// </summary>
    private void SyncCameraInfluences(bool spawnMissing = false)
    {
        if (_data == null) return;

        var parent = FindParent(CameraInfluencesParent);
        if (parent != null)
            foreach (var orphan in parent.GetComponentsInChildren<LevelSelectCameraInfluence>(true)
                         .Where(c => !_data.nodes.Exists(n => n != null && n.id == c.nodeId)).ToList())
                Undo.DestroyObjectImmediate(orphan.gameObject);

        foreach (var node in _data.nodes)
        {
            if (node == null) continue;
            var influence = FindInfluenceCamera(node.id);
            if (influence == null && node.cameraInfluence && spawnMissing)
            {
                Undo.RecordObject(_data, "Camera Influence");
                influence = SpawnInfluenceCamera(node);
            }
            if (influence == null) continue;

            // Pinned: the node moved, the camera goes with it.
            Vector3 delta = node.worldPosition - node.cameraInfluencePinnedAt;
            if (delta.sqrMagnitude > 1e-10f)
            {
                Undo.RecordObject(_data, "Move Node");
                node.cameraInfluencePinnedAt = node.worldPosition;
                Undo.RecordObject(influence.transform, "Move Node");
                influence.transform.position += delta;
            }

            if (influence.gameObject.activeSelf != node.cameraInfluence)
            {
                Undo.RecordObject(influence.gameObject, "Camera Influence");
                influence.gameObject.SetActive(node.cameraInfluence);
            }

            WriteInfluenceSettings(node, influence);
        }
    }

    // The node's box and settings onto its camera — only when something differs, so MarkDirty
    // doesn't fill the undo step with nothing.
    private void WriteInfluenceSettings(LevelSelectDesignerData.DesignerNode node, LevelSelectCameraInfluence influence)
    {
        Vector3    centre = influence.boxCentre;
        Quaternion facing = influence.boxRotation;
        Vector3    size   = influence.boxSize;
        if (!TryInfluenceBox(node, out centre, out facing, out size))
            size = Vector3.zero;   // off every river: a box nothing is ever in

        bool same = influence.nodeId == node.id &&
                    influence.boxCentre == centre && influence.boxRotation == facing && influence.boxSize == size &&
                    influence.mode == node.cameraInfluenceMode &&
                    Mathf.Approximately(influence.fade,   node.cameraInfluenceFade)   &&
                    Mathf.Approximately(influence.height, node.cameraInfluenceHeight) &&
                    Mathf.Approximately(influence.across, node.cameraInfluenceAcross) &&
                    Mathf.Approximately(influence.aim,    node.cameraInfluenceAim)    &&
                    Mathf.Approximately(influence.lens,   node.cameraInfluenceLens)   &&
                    Mathf.Approximately(influence.distance, node.cameraInfluenceDistance) &&
                    influence.lookAt == node.cameraInfluenceLookAt &&
                    influence.blend  == node.cameraInfluenceBlend &&
                    Mathf.Approximately(influence.transitionTime, node.cameraInfluenceTransitionTime) &&
                    influence.overrideFogDistance == node.cameraInfluenceOverrideFog &&
                    Mathf.Approximately(influence.fogDistance, node.cameraInfluenceFogDistance) &&
                    influence.focusOnViewTarget == node.cameraInfluenceFocusOnViewTarget;
        if (same) return;

        Undo.RecordObject(influence, "Camera Influence");
        influence.nodeId      = node.id;
        influence.boxCentre   = centre;
        influence.boxRotation = facing;
        influence.boxSize     = size;
        influence.mode        = node.cameraInfluenceMode;
        influence.fade        = node.cameraInfluenceFade;
        influence.height      = node.cameraInfluenceHeight;
        influence.across      = node.cameraInfluenceAcross;
        influence.aim         = node.cameraInfluenceAim;
        influence.lens        = node.cameraInfluenceLens;
        influence.distance    = node.cameraInfluenceDistance;
        influence.lookAt      = node.cameraInfluenceLookAt;
        influence.blend       = node.cameraInfluenceBlend;
        influence.transitionTime = node.cameraInfluenceTransitionTime;
        influence.overrideFogDistance = node.cameraInfluenceOverrideFog;
        influence.fogDistance         = node.cameraInfluenceFogDistance;
        influence.focusOnViewTarget   = node.cameraInfluenceFocusOnViewTarget;
        EditorUtility.SetDirty(influence);
    }

    // The node row's Camera Influence: the toggle, the mode and its settings, and the camera's tweak button.
    private void DrawNodeCameraInfluence(string nodeId)
    {
        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node == null) return;

        EditorGUILayout.Space(6);
        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.ToggleLeft(new GUIContent("Camera Influence",
            "A camera set at this node, and a box across the river here. While the boat is in the box the " +
            "follow camera is drawn towards this one (Influence) or the view cuts to it (Cut). The boat " +
            "stays yours throughout; anchoring with Q cuts back to the ordinary camera."),
            node.cameraInfluence, EditorStyles.boldLabel);

        var   mode   = node.cameraInfluenceMode;
        float length = node.cameraInfluenceLength;
        float boxH   = node.cameraInfluenceBoxHeight;
        float boxW   = node.cameraInfluenceBoxWidth;
        float boxDrop = node.cameraInfluenceBoxDrop;
        float offAcross = node.cameraInfluenceBoxOffsetAcross;
        float offAlong  = node.cameraInfluenceBoxOffsetAlong;
        float fade   = node.cameraInfluenceFade;
        float height = node.cameraInfluenceHeight;
        float across = node.cameraInfluenceAcross;
        float aim    = node.cameraInfluenceAim;
        float lens   = node.cameraInfluenceLens;
        float dist   = node.cameraInfluenceDistance;
        var   look   = node.cameraInfluenceLookAt;
        var   blend  = node.cameraInfluenceBlend;
        float blendTime = node.cameraInfluenceTransitionTime;
        bool  fogOn  = node.cameraInfluenceOverrideFog;
        float fogDist = node.cameraInfluenceFogDistance;
        bool  focusOn = node.cameraInfluenceFocusOnViewTarget;
        if (on)
        {
            EditorGUI.indentLevel++;
            mode = (LevelSelectCameraInfluence.Mode)EditorGUILayout.Popup(new GUIContent("Mode",
                "Influence: the follow camera is drawn towards this node's camera by the amounts below, and only " +
                "by those.\nCut: the view cuts to this node's camera in the box and back out of it."),
                (int)mode, InfluenceModeLabels);
            length = Mathf.Max(0.01f, EditorGUILayout.FloatField(new GUIContent("Length",
                "How far the box runs along the river."), length));
            // 0 (a node from before Box Height / Box Width) shows the river's own until it is set.
            bool onRiver = TryMusicTriggerBox(node.id, out _, out _, out Vector3 riverBox);
            float shownW = boxW > 0f ? boxW : onRiver ? riverBox.x : 0f;
            float newW = EditorGUILayout.FloatField(new GUIContent("Box Width",
                "How wide the box is across the river, centred on it."), shownW);
            if (!Mathf.Approximately(newW, shownW)) boxW = Mathf.Max(0.01f, newW);
            float shownH = boxH > 0f ? boxH : onRiver ? riverBox.y : 0f;
            float newH = EditorGUILayout.FloatField(new GUIContent("Box Height",
                "How tall the box stands, up from its bottom below the water."), shownH);
            if (!Mathf.Approximately(newH, shownH)) boxH = Mathf.Max(0.01f, newH);
            boxDrop = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Box Drop",
                "How far the whole box is lowered, keeping its height."), boxDrop));
            offAcross = EditorGUILayout.FloatField(new GUIContent("Offset Across",
                "How far the whole box is moved across the river — right looking down it, or left when negative."), offAcross);
            offAlong = EditorGUILayout.FloatField(new GUIContent("Offset Along",
                "How far the whole box is moved along the river — down it, or up it when negative."), offAlong);

            if (mode == LevelSelectCameraInfluence.Mode.Influence)
            {
                fade = EditorGUILayout.Slider(new GUIContent("InfluenceStrengthInBox",
                    "How far in from either end of the box the pull takes to come all the way in, by where the " +
                    "boat is (not by time). Shaded in the scene view: the deeper the colour, the stronger the pull."),
                    fade, 0f, Mathf.Max(length * 0.5f, 0.01f));
                height = EditorGUILayout.Slider(new GUIContent("Height",
                    "How far the camera swings up or down towards this camera's height, at the same distance from the boat."),
                    height, 0f, 1f);
                across = EditorGUILayout.Slider(new GUIContent("Across",
                    "How far the camera swings across the river towards this camera's side, at the same distance from the boat."),
                    across, 0f, 1f);
                dist = EditorGUILayout.Slider(new GUIContent("Distance",
                    "How far the camera's distance from the boat goes towards this camera's. At 0 it keeps its own distance."),
                    dist, 0f, 1f);
                aim =EditorGUILayout.Slider(new GUIContent("Aim",
                    "How far the camera turns towards this camera's facing."), aim, 0f, 1f);
                lens = EditorGUILayout.Slider(new GUIContent("Lens",
                    "How far the field of view goes towards this camera's."), lens, 0f, 1f);
            }
            else
            {
                look = (LevelSelectCameraInfluence.LookAt)EditorGUILayout.Popup(new GUIContent("Look At",
                    "Follow Boat: the camera stays where it stands and turns to keep the boat in frame.\n" +
                    "Fixed: it looks exactly as set."), (int)look, InfluenceLookLabels);
                blend = (LevelSelectCameraInfluence.Blend)EditorGUILayout.Popup(new GUIContent("Blend",
                    "Cut: straight to this camera on entering the box and straight back on leaving.\n" +
                    "Transition: the view moves there and back over Transition Time."), (int)blend, InfluenceBlendLabels);
                if (blend == LevelSelectCameraInfluence.Blend.Transition)
                    blendTime = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Transition Time",
                        "How long, in seconds, the move to this camera and back takes."), blendTime));
            }

            var camera = FindInfluenceCamera(node.id);
            DrawInfluenceFogAndFocus(ref fogOn, ref fogDist, ref focusOn, camera, null);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                using (new EditorGUI.DisabledScope(camera == null))
                    if (GUILayout.Button(new GUIContent("Tweak in Cinemachine View",
                            "Selects this node's camera and shows its view, with its settings, in the Cinemachine View window.")))
                    {
                        Selection.activeGameObject = camera.gameObject;
                        CinemachineViewWindow.Watch(camera.GetComponent<CinemachineCamera>());
                        GUIUtility.ExitGUI();
                    }
            }
            EditorGUI.indentLevel--;
        }
        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(_data, "Node Camera Influence");
        node.cameraInfluence       = on;
        node.cameraInfluenceMode   = mode;
        node.cameraInfluenceLength = length;
        node.cameraInfluenceBoxHeight = boxH;
        node.cameraInfluenceBoxWidth  = boxW;
        node.cameraInfluenceBoxDrop   = boxDrop;
        node.cameraInfluenceBoxOffsetAcross = offAcross;
        node.cameraInfluenceBoxOffsetAlong  = offAlong;
        node.cameraInfluenceFade   = Mathf.Min(fade, length * 0.5f);
        node.cameraInfluenceHeight = height;
        node.cameraInfluenceAcross = across;
        node.cameraInfluenceAim    = aim;
        node.cameraInfluenceLens   = lens;
        node.cameraInfluenceDistance = dist;
        node.cameraInfluenceLookAt = look;
        node.cameraInfluenceBlend  = blend;
        node.cameraInfluenceTransitionTime = blendTime;
        node.cameraInfluenceOverrideFog      = fogOn;
        node.cameraInfluenceFogDistance       = fogDist;
        node.cameraInfluenceFocusOnViewTarget = focusOn;
        if (on && FindInfluenceCamera(node.id) == null) SpawnInfluenceCamera(node);
        MarkDirty();   // writes it all onto the camera
        SceneView.RepaintAll();
        Repaint();
    }

    // Fog Distance Override and Focus On View Target: in the node row, and at the top of the Cm View's panel
    // when it shows the node's camera. setViewTarget is the Cm View's Set View Target (click it in the view);
    // null in the node row. The View Target is the camera's own, written straight onto it.
    private static void DrawInfluenceFogAndFocus(ref bool fogOn, ref float fogDist, ref bool focusOn,
                                                 LevelSelectCameraInfluence camera, System.Action setViewTarget)
    {
        bool fogWasOn = fogOn;
        fogOn = EditorGUILayout.Toggle(new GUIContent("Fog Distance Override",
            "On: in the box the fog is full at Fog Distance from the boat, instead of the world's White Fade " +
            "Radius. Influence brings it in across InfluenceStrengthInBox. Shown in the Cm View."), fogOn);
        if (fogOn && !fogWasOn && fogDist <= 0f) fogDist = Shader.GetGlobalFloat("_WhiteFadeRadius");
        if (fogOn)
        {
            EditorGUI.indentLevel++;
            fogDist = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Fog Distance",
                "Metres from the boat to where the fog is full, in the box."), fogDist));
            EditorGUI.indentLevel--;
        }

        focusOn = EditorGUILayout.Toggle(new GUIContent("Focus On View Target",
            "On: in the box the camera's depth of field focuses on View Target, instead of its own focus. " +
            "Influence brings it in across InfluenceStrengthInBox. Shown in the Cm View."), focusOn);
        if (!focusOn || camera == null) return;

        EditorGUI.indentLevel++;
        EditorGUI.BeginChangeCheck();
        var target = (Transform)EditorGUILayout.ObjectField(new GUIContent("View Target",
            "What the depth of field focuses on while the boat is in the box." +
            (setViewTarget != null ? " Pick it by clicking it in the view (Set View Target)." : "")),
            camera.viewTarget, typeof(Transform), true);
        if (EditorGUI.EndChangeCheck()) SetInfluenceViewTarget(camera, target);

        if (setViewTarget != null)
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                    if (GUILayout.Button(new GUIContent("Set View Target",
                            "Then click what the depth of field should focus on, in the view on the left. Esc cancels.")))
                        setViewTarget();
            }

        if (camera.viewTarget == null)
            EditorGUILayout.HelpBox("No View Target yet — the camera keeps its own focus.", MessageType.None);
        EditorGUI.indentLevel--;
    }

    /// <summary>The camera's View Target, with undo.</summary>
    public static void SetInfluenceViewTarget(LevelSelectCameraInfluence camera, Transform target)
    {
        if (camera == null) return;
        Undo.RecordObject(camera, "Camera Influence View Target");
        camera.viewTarget = target;
        EditorUtility.SetDirty(camera);
    }

    /// <summary>"node 3 of River A" for the node a Camera Influence camera belongs to, with the designer open.</summary>
    public static string CameraInfluenceNodeLabel(LevelSelectCameraInfluence camera)
    {
        var w    = FindOpen();
        var path = w != null && w._data != null && camera != null
            ? w._data.paths.Find(p => p != null && p.nodeIds.Contains(camera.nodeId)) : null;
        if (path == null) return camera != null ? camera.name : string.Empty;
        string river = string.IsNullOrEmpty(path.riverName) ? "its river" : path.riverName;
        return $"node {path.nodeIds.IndexOf(camera.nodeId)} of {river}";
    }

    /// <summary>
    /// The Cm View's panel, for a node's Camera Influence camera: the node's Fog Distance Override and
    /// Focus On View Target, edited as in the node row. Needs the designer open on the level; false when
    /// it isn't, or the camera's node is gone.
    /// </summary>
    public static bool DrawCameraInfluenceInCmView(LevelSelectCameraInfluence camera, System.Action setViewTarget)
    {
        var w = FindOpen();
        if (w == null || w._data == null || camera == null) return false;
        var node = w._data.nodes.Find(n => n != null && n.id == camera.nodeId);
        if (node == null) return false;

        EditorGUILayout.LabelField($"Camera Influence · {CameraInfluenceNodeLabel(camera)}", EditorStyles.boldLabel);

        bool  fogOn   = node.cameraInfluenceOverrideFog;
        float fogDist = node.cameraInfluenceFogDistance;
        bool  focusOn = node.cameraInfluenceFocusOnViewTarget;
        EditorGUI.BeginChangeCheck();
        DrawInfluenceFogAndFocus(ref fogOn, ref fogDist, ref focusOn, camera, setViewTarget);
        if (!EditorGUI.EndChangeCheck()) return true;

        Undo.RecordObject(w._data, "Node Camera Influence");
        node.cameraInfluenceOverrideFog       = fogOn;
        node.cameraInfluenceFogDistance       = fogDist;
        node.cameraInfluenceFocusOnViewTarget = focusOn;
        w.MarkDirty();   // writes it onto the camera
        w.Repaint();
        return true;
    }

    // ─────────────────────────────────────────────
    // SCENE VIEW
    // ─────────────────────────────────────────────

    private void CameraInfluenceSceneEnable()  => SceneView.duringSceneGui += DrawCameraInfluenceInScene;
    private void CameraInfluenceSceneDisable() => SceneView.duringSceneGui -= DrawCameraInfluenceInScene;

    private static readonly Color InfluenceGizmoColour = new Color(0.65f, 0.75f, 0.9f);
    private static GUIStyle s_influenceLabelStyle;

    /// <summary>
    /// The selected node's Camera Influence box, drawn as the Fog Map draws its areas: a fill, an
    /// outline and a small label. The fill is sliced along the river and each slice shaded by the pull
    /// there (InfluenceStrengthInBox), faint at the box's ends and deepest where the pull is all the way
    /// in. In Influence mode the lines where the pull is all the way in are drawn across the box too.
    /// </summary>
    private void DrawCameraInfluenceInScene(SceneView view)
    {
        if (Event.current.type != EventType.Repaint || _data == null || string.IsNullOrEmpty(_selectedNodeId)) return;
        var node = _data.nodes.Find(n => n != null && n.id == _selectedNodeId);
        if (node == null || !node.cameraInfluence) return;
        if (!TryInfluenceBox(node, out Vector3 centre, out Quaternion facing, out Vector3 size)) return;

        Vector3 h = size * 0.5f;
        Vector3 P(float x, float y, float z) => centre + facing * new Vector3(h.x * x, h.y * y, h.z * z);

        Color edge = InfluenceGizmoColour; edge.a = 0.6f;

        // The pull at a point along the box (-1 one end, 1 the other), as LevelSelectCameraInfluence.Weight has it.
        bool  cut   = node.cameraInfluenceMode == LevelSelectCameraInfluence.Mode.Cut;
        float fadeN = cut || node.cameraInfluenceFade <= 0f ? 0f : Mathf.Min(node.cameraInfluenceFade / h.z, 1f);
        float Strength(float z) => fadeN <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - Mathf.Abs(z)) / fadeN));
        Color Shade(float strength) { Color c = InfluenceGizmoColour; c.a = Mathf.Lerp(0.02f, 0.24f, strength); return c; }

        // Slices: several across each end's ramp, one through the middle where the pull is all the way in.
        const int RampSlices = 12;
        var cuts = new List<float>();
        if (fadeN <= 0f) { cuts.Add(-1f); cuts.Add(1f); }
        else
        {
            for (int i = 0; i <= RampSlices; i++) cuts.Add(-1f + fadeN * i / RampSlices);
            for (int i = 0; i <= RampSlices; i++)
            {
                float z = 1f - fadeN + fadeN * i / RampSlices;
                if (z > cuts[cuts.Count - 1] + 1e-5f) cuts.Add(z);
            }
        }

        var prevColour = Handles.color;
        using (new Handles.DrawingScope())
        {
            // The four long faces, slice by slice, shaded by the pull in each.
            for (int i = 0; i < cuts.Count - 1; i++)
            {
                float a = cuts[i], b = cuts[i + 1];
                Color s = Shade(Strength((a + b) * 0.5f));
                Handles.DrawSolidRectangleWithOutline(new[] { P(-1, -1, a), P(-1, -1, b), P( 1, -1, b), P( 1, -1, a) }, s, Color.clear);
                Handles.DrawSolidRectangleWithOutline(new[] { P(-1,  1, a), P(-1,  1, b), P( 1,  1, b), P( 1,  1, a) }, s, Color.clear);
                Handles.DrawSolidRectangleWithOutline(new[] { P(-1, -1, a), P(-1,  1, a), P(-1,  1, b), P(-1, -1, b) }, s, Color.clear);
                Handles.DrawSolidRectangleWithOutline(new[] { P( 1, -1, a), P( 1,  1, a), P( 1,  1, b), P( 1, -1, b) }, s, Color.clear);
            }

            // The two ends, shaded by the pull there, and the outline round all six faces.
            Color endFill = Shade(Strength(1f));
            Handles.DrawSolidRectangleWithOutline(new[] { P(-1, -1, -1), P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1) }, Color.clear, edge);
            Handles.DrawSolidRectangleWithOutline(new[] { P(-1,  1, -1), P(-1,  1, 1), P(1,  1, 1), P(1,  1, -1) }, Color.clear, edge);
            Handles.DrawSolidRectangleWithOutline(new[] { P(-1, -1, -1), P(-1,  1, -1), P(1,  1, -1), P(1, -1, -1) }, endFill, edge);
            Handles.DrawSolidRectangleWithOutline(new[] { P(-1, -1,  1), P(-1,  1,  1), P(1,  1,  1), P(1, -1,  1) }, endFill, edge);
            Handles.DrawSolidRectangleWithOutline(new[] { P(-1, -1, -1), P(-1,  1, -1), P(-1, 1,  1), P(-1, -1, 1) }, Color.clear, edge);
            Handles.DrawSolidRectangleWithOutline(new[] { P( 1, -1, -1), P( 1,  1, -1), P( 1, 1,  1), P( 1, -1, 1) }, Color.clear, edge);

            // Where InfluenceStrengthInBox is done and the pull is all the way in.
            if (node.cameraInfluenceMode == LevelSelectCameraInfluence.Mode.Influence &&
                node.cameraInfluenceFade > 0f && node.cameraInfluenceFade < h.z)
            {
                float z = 1f - node.cameraInfluenceFade / h.z;
                Color inner = InfluenceGizmoColour; inner.a = 0.35f;
                Handles.color = inner;
                foreach (float s in new[] { z, -z })
                    Handles.DrawAAPolyLine(2f, P(-1, -1, s), P(-1, 1, s), P(1, 1, s), P(1, -1, s), P(-1, -1, s));
            }

            s_influenceLabelStyle ??= new GUIStyle(EditorStyles.miniLabel);
            s_influenceLabelStyle.normal.textColor = Color.white;
            string label = node.cameraInfluenceMode == LevelSelectCameraInfluence.Mode.Cut
                ? $"camera influence · cut  {size.z:0.##} u"
                : $"camera influence  {size.z:0.##} u  InfluenceStrengthInBox {node.cameraInfluenceFade:0.##}";
            Handles.Label(P(-1, 1, 1), label, s_influenceLabelStyle);
        }
        Handles.color = prevColour;
    }
}

#endif
