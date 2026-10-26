using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// River bridges — a flat deck straight across the river at a node, with a matching flat ramp at
/// each end running out over the rim and walkways, down to the rim's height (see
/// <see cref="LevelSelectDesignerData.DesignerBridge"/>). Added with Add Bridge on a node; its
/// deck height, deck width, deck thickness and ramp length are set in the node's panel.
///
/// Built by <see cref="RiverMeshBuilder.BuildWalkways"/> into the same piece as the walkways
/// beside each run, in the river material, so it is rebuilt whenever they are.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // ─────────────────────────────────────────────
    // ADDING
    // ─────────────────────────────────────────────

    // Add Bridge / Remove Bridge is in a node's Add ▾ menu (NodeAddMenu.cs).
    private void ToggleBridgeAt(LevelSelectDesignerData.DesignerPath path, string nodeId)
    {
        Undo.RecordObject(_data, "Toggle Bridge");
        _data.bridges ??= new List<LevelSelectDesignerData.DesignerBridge>();

        var bridge = _data.BridgeAt(nodeId);
        if (bridge != null) _data.bridges.Remove(bridge);
        else                _data.bridges.Add(new LevelSelectDesignerData.DesignerBridge { nodeId = nodeId });

        MarkDirty();
        string pathId = path.pathId;
        RebuildAfterTowerEdit("Bridge:" + pathId, () => RebuildWalkwaysOnPath(pathId, false));
        Repaint();
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    /// <summary>The selected node's bridge — its deck and ramp sizes — when it has one.</summary>
    private void DrawSelectedBridgeProps()
    {
        if (string.IsNullOrEmpty(_selectedNodeId)) return;

        var bridge = _data.BridgeAt(_selectedNodeId);
        if (bridge == null) return;

        var path = _data.paths.Find(p => p.nodeIds.Contains(_selectedNodeId));
        if (path == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Bridge", EditorStyles.boldLabel);

        if (!CanHoldWalkway(_selectedNodeId, out string why))
        {
            EditorGUILayout.LabelField(why?.Replace("Walkways", "Bridges"), EditorStyles.miniLabel);
            return;
        }

        string pathId = path.pathId;
        DrawBridgePresetRow(bridge, pathId);

        EditorGUI.BeginChangeCheck();
        BridgeSettings sizes;
        if (bridge.bridgePreset != null)
        {
            using (new EditorGUI.DisabledScope(true))
                DrawBridgeSettingsFields(BridgeSettings.Of(bridge));
            EditorGUILayout.LabelField("Set in the preset — Edit changes every bridge using it.",
                                       EditorStyles.wordWrappedMiniLabel);
            sizes = null;
        }
        else
            sizes = DrawBridgeSettingsFields(BridgeSettings.Of(bridge));

        float offsetAlong = EditorGUILayout.FloatField(
            new GUIContent("Offset Along", "Moves the bridge along the river from its node — the " +
                                           "way the path was drawn when positive, back when negative."),
            bridge.offsetAlong);
        bool occlusion = bridge.occlusion;
        var  occlusionStyle = bridge.occlusionStyle;
        OcclusionField(ref occlusion, ref occlusionStyle);
        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(_data, "Edit Bridge");
        sizes?.ApplyTo(bridge);
        bridge.offsetAlong = offsetAlong;
        bridge.occlusion   = occlusion;
        bridge.occlusionStyle = occlusionStyle;

        MarkDirty();
        RebuildAfterTowerEdit("Bridge:" + pathId, () => RebuildWalkwaysOnPath(pathId, false));
        Repaint();
    }

    internal const string BridgePresetFolder = "Assets/ScriptsData/DataScripts/BridgePresets";

    /// <summary>
    /// The bridge's preset — pick one to put its deck and ramp sizes on this bridge, Edit to open
    /// it in the preset editor (every bridge using it changes with it), or New to save this
    /// bridge's sizes as one. Offset Along stays with the bridge.
    /// </summary>
    private void DrawBridgePresetRow(LevelSelectDesignerData.DesignerBridge bridge, string pathId)
    {
        var picked = LevelSelectPresetEditorWindow.DrawPresetLink(
            new GUIContent("Bridge Preset", "The bridge presets kept in " + BridgePresetFolder +
                                            ". Picking one puts its deck and ramp sizes on this bridge; Edit changes them."),
            bridge.bridgePreset, BridgePresetFolder, "Bridge",
            () =>
            {
                var made = CreateInstance<BridgePreset>();
                made.CopyFrom(BridgeSettings.Of(bridge));
                return made;
            },
            made =>
            {
                if (_data == null) return;
                Undo.RecordObject(_data, "New Bridge Preset");
                bridge.bridgePreset = made;
                MarkDirty();
                Repaint();
            });

        if (picked == bridge.bridgePreset) return;

        Undo.RecordObject(_data, "Load Bridge Preset");
        bridge.bridgePreset = picked;
        if (picked != null) picked.Build().ApplyTo(bridge);
        MarkDirty();
        GUI.FocusControl(null);
        RebuildAfterTowerEdit("Bridge:" + pathId, () => RebuildWalkwaysOnPath(pathId, false));
    }

    /// <summary>
    /// A bridge's deck and ramp fields. Hands back an edited copy, so the caller writes it only
    /// inside its own change check.
    /// </summary>
    internal static BridgeSettings DrawBridgeSettingsFields(BridgeSettings settings)
    {
        var edited = (settings ?? new BridgeSettings()).Clone();
        edited.deckHeight = Mathf.Max(0.01f, EditorGUILayout.FloatField(
            new GUIContent("Deck Height", "How far the deck's top stands above the rim."),
            edited.deckHeight));
        edited.deckWidth = Mathf.Max(0.01f, EditorGUILayout.FloatField(
            new GUIContent("Deck Width", "How wide the deck and its ramps are, along the river."),
            edited.deckWidth));
        edited.deckThickness = Mathf.Max(0.005f, EditorGUILayout.FloatField(
            new GUIContent("Deck Thickness", "How thick the deck is, from its top down to its " +
                                             "underside over the water."),
            edited.deckThickness));
        edited.rampLength = Mathf.Max(0.01f, EditorGUILayout.FloatField(
            new GUIContent("Ramp Length", "How far each ramp reaches out from the channel's edge, " +
                                          "away from the river, to where it meets the rim's " +
                                          "height — over the rim, River Walkway and River " +
                                          "Walkway 2."),
            edited.rampLength));
        return edited;
    }

    // ─────────────────────────────────────────────
    // CANVAS
    // ─────────────────────────────────────────────

    /// <summary>
    /// Each bridge from above: the deck across the drawn path at its node, and a ramp out from
    /// each side of it. Drawn against the drawn path, which is close to the run but not on it.
    /// </summary>
    private void DrawBridges()
    {
        if (Event.current.type != EventType.Repaint || _data.bridges == null ||
            _data.bridges.Count == 0)
            return;

        foreach (var bridge in _data.bridges)
        {
            if (bridge == null) continue;
            var path = _data.paths.Find(p => p.nodeIds.Contains(bridge.nodeId));
            if (path == null || path.nodeIds.Count < 2) continue;

            var     profile = _data.ProfileFor(path.riverName);
            int     index   = path.nodeIds.IndexOf(bridge.nodeId);
            Vector3 right   = DrawnRightAt(path, index);
            Vector3 along   = Vector3.Cross(right, Vector3.up);
            Vector3 at      = _data.NodeWorldPosition(bridge.nodeId) + along * bridge.offsetAlong;

            float hw    = Mathf.Max(bridge.deckWidth, 0.01f) * 0.5f;
            float inner = InnerWidthAtNode(profile, bridge.nodeId) * 0.5f;
            float reach = inner + Mathf.Max(bridge.rampLength, 0.01f);

            void Band(float from, float to)
            {
                Handles.DrawAAConvexPolygon(
                    (Vector3)WorldToCanvas(at + right * from - along * hw),
                    (Vector3)WorldToCanvas(at + right * to   - along * hw),
                    (Vector3)WorldToCanvas(at + right * to   + along * hw),
                    (Vector3)WorldToCanvas(at + right * from + along * hw));
            }

            bool selected = bridge.nodeId == _selectedNodeId;

            // The deck lighter than its ramps, so where the slope starts reads at a glance.
            Handles.color = Faded(selected ? new Color(0.98f, 0.98f, 0.95f, 0.9f)
                                     : new Color(0.85f, 0.85f, 0.82f, 0.8f));
            Band(-inner, inner);

            Handles.color = Faded(selected ? new Color(0.8f, 0.8f, 0.77f, 0.85f)
                                     : new Color(0.62f, 0.62f, 0.6f, 0.75f));
            Band(-reach, -inner);
            Band(inner, reach);
        }
    }

    // ─────────────────────────────────────────────
    // BUILDING
    // ─────────────────────────────────────────────

    /// <summary>
    /// The bridges across a run, as the builder takes them — in the run's own space. With
    /// <paramref name="occluded"/>, one entry per bridge: a name for each ticked for Occlusion,
    /// null for the rest.
    /// </summary>
    private List<RiverMeshBuilder.Bridge> BridgesFor(RiverRunMesh record, List<string> occluded = null,
        List<OcclusionStyle> occludedStyles = null)
    {
        var bridges = new List<RiverMeshBuilder.Bridge>();
        if (record?.pathNodes == null || record.pathNodes.Count < 2 || _data.bridges == null ||
            _data.bridges.Count == 0)
            return bridges;

        foreach (var node in record.pathNodes)
        {
            var bridge = _data.BridgeAt(node.nodeId);
            if (bridge == null || !CanHoldWalkway(node.nodeId, out _)) continue;

            bridges.Add(new RiverMeshBuilder.Bridge
            {
                at            = node.position,
                deckHeight    = bridge.deckHeight,
                deckWidth     = bridge.deckWidth,
                deckThickness = bridge.deckThickness,
                rampLength    = bridge.rampLength,
                offsetAlong   = bridge.offsetAlong,
            });
            occluded?.Add(bridge.occlusion ? $"Bridge_{SanitiseAssetName(node.nodeId)}" : null);
            occludedStyles?.Add(bridge.occlusionStyle);
        }
        return bridges;
    }
}

#endif
