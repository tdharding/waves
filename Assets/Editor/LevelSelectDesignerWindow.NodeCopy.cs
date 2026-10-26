using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Copy / Paste on a node's open row — what stands on one node, put onto another in one go.
///
/// A river node carries In-Between, its shop, pool (its own lead-ins, empty soul slots), river
/// width, rim node, River Walkway and River Walkway 2, rim wall, bridge, archway, porthole, Audio,
/// Cinematic, Camera Influence (its camera too, standing the same way to the river) and the
/// outposts standing at it. A wall node carries everything in its box: height, column,
/// tower, archway and porthole. Neither carries where the node is, and a wall node keeps its own
/// Curved / Gap to the next node.
///
/// Paste makes the node match the copied one: anything the copied node didn't have is taken off.
/// The copy is taken when Copy is pressed, so editing the first node afterwards changes nothing.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // ─────────────────────────────────────────────
    // WHAT IS HELD
    // ─────────────────────────────────────────────

    private class RiverNodeCopy
    {
        public string label;
        public bool   inBetween;
        public LevelSelectDesignerData.DesignerShop    shop;
        public LevelSelectDesignerData.DesignerPool    pool;
        public LevelSelectDesignerData.DesignerRimNode rim;
        public LevelSelectDesignerData.DesignerWalkway walkway, walkway2;
        public LevelSelectDesignerData.DesignerBridge  bridge;
        public LevelSelectDesignerData.DesignerRiverPorthole porthole;
        public LevelSelectDesignerData.DesignerRiverWidth width;
        public LevelSelectDesignerData.DesignerRimWall   rimWall;
        public LevelSelectDesignerData.DesignerArchway   archway;
        public LevelSelectDesignerData.DesignerNode    sound;   // only the Audio, Cinematic and Camera Influence fields are read
        // The Camera Influence camera, measured from the node in the river's own frame there.
        public bool         hasCamera;
        public Vector3      cameraOffset;
        public Quaternion   cameraRotation;
        public LensSettings cameraLens;
        // Each outpost, with how far it stood from the node in node-to-node stretches.
        public List<(LevelSelectDesignerData.DesignerOutpost outpost, float along)> outposts = new();
    }

    private static RiverNodeCopy                    s_riverNodeCopy;
    private static LevelSelectDesignerData.WallNode s_wallNodeCopy;
    private static string                           s_wallNodeCopyLabel;

    /// <summary>A full copy — towers, perches and all — so the two nodes never share anything.</summary>
    private static T CopyOf<T>(T source) where T : class
        => (T)DeepCopy(source);

    // Values, strings and assets (presets, clips) are carried as they are; lists and nested
    // settings are copied all the way down.
    private static object DeepCopy(object source)
    {
        if (source == null) return null;
        var type = source.GetType();
        if (type.IsValueType || type == typeof(string) || source is UnityEngine.Object) return source;

        if (source is Array array)
        {
            var copiedArray = (Array)array.Clone();
            for (int i = 0; i < copiedArray.Length; i++) copiedArray.SetValue(DeepCopy(array.GetValue(i)), i);
            return copiedArray;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list       = (System.Collections.IList)source;
            var copiedList = (System.Collections.IList)Activator.CreateInstance(type, list.Count);
            foreach (var item in list) copiedList.Add(DeepCopy(item));
            return copiedList;
        }

        // Engine types (a curve, a gradient) hold native memory a field copy would share — copy
        // only our own settings classes, and carry anything else as it is.
        string ns = type.Namespace ?? string.Empty;
        if (ns.StartsWith("Unity") || ns.StartsWith("System") || type.GetConstructor(Type.EmptyTypes) == null)
            return source;

        var copy = Activator.CreateInstance(type);
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            foreach (var field in t.GetFields(System.Reflection.BindingFlags.Instance |
                                              System.Reflection.BindingFlags.Public |
                                              System.Reflection.BindingFlags.NonPublic |
                                              System.Reflection.BindingFlags.DeclaredOnly))
                field.SetValue(copy, DeepCopy(field.GetValue(source)));
        return copy;
    }

    // Only one perch should start the map with the angel stood on it.
    private static void NotStartPerch(LevelSelectDesignerData.DesignerPerch perch)
    {
        if (perch != null) perch.startPerch = false;
    }

    // ─────────────────────────────────────────────
    // BUTTONS
    // ─────────────────────────────────────────────

    /// <summary>Copy and Paste for a river node's open row.</summary>
    private void DrawRiverNodeCopyButtons(LevelSelectDesignerData.DesignerPath path, string nodeId, int index)
    {
        if (GUILayout.Button(new GUIContent("Copy", "Copy what stands on this node — In-Between, shop, pool, river width, " +
                                                    "rim node, walkways, rim wall, bridge, archway, porthole, Audio, Cinematic, " +
                                                    "Camera Influence and outposts — to paste onto another."),
                             EditorStyles.miniButtonLeft, GUILayout.Width(42)))
            CopyRiverNode(path, nodeId, index);

        using (new EditorGUI.DisabledScope(s_riverNodeCopy == null))
            if (GUILayout.Button(new GUIContent("Paste", s_riverNodeCopy == null
                                                    ? "Copy a river node first."
                                                    : $"Make this node match {s_riverNodeCopy.label}. Anything that " +
                                                      "node didn't have is taken off this one."),
                                 EditorStyles.miniButtonRight, GUILayout.Width(44)))
                _pendingRiverNodePaste = (path.pathId, nodeId);

        var node = _data.nodes.Find(n => n.id == nodeId);
        string river = string.IsNullOrEmpty(path.riverName) ? $"river {_data.paths.IndexOf(path) + 1}" : path.riverName;
        DrawNodeReferenceButton($"River node {index} of {river}", nodeId,
                                node != null ? $"({node.worldPosition.x:0.##}, {node.worldPosition.y:0.##}, {node.worldPosition.z:0.##})" : null);
    }

    /// <summary>
    /// The small square: copies a line naming this node — its place, the first part of its id
    /// (found as it is in the data asset) and where it stands — to talk about it elsewhere.
    /// </summary>
    private void DrawNodeReferenceButton(string label, string id, string position)
    {
        if (!GUILayout.Button(new GUIContent("□", "Copy a reference to this node, for talking about it."),
                              EditorStyles.miniButton, GUILayout.Width(20)))
            return;

        string reference = label;
        if (!string.IsNullOrEmpty(id)) reference += $" · ref {(id.Length > 8 ? id.Substring(0, 8) : id)}";
        if (position != null) reference += $" · at {position}";
        EditorGUIUtility.systemCopyBuffer = reference;
        _consoleStatusMsg = $"Copied: {reference}";
    }

    /// <summary>Copy and Paste for a wall node's open box.</summary>
    private void DrawWallNodeCopyButtons(LevelSelectDesignerData.DesignerWall wall, int index)
    {
        if (GUILayout.Button(new GUIContent("Copy", "Copy this node's height, column, tower, archway and " +
                                                    "porthole to paste onto another wall node."),
                             EditorStyles.miniButtonLeft, GUILayout.Width(42)))
        {
            s_wallNodeCopy      = CopyOf(wall.nodes[index]);
            s_wallNodeCopyLabel = $"Node {index + 1} of {WallLabel(wall)}";
            _consoleStatusMsg   = $"Copied {s_wallNodeCopyLabel}.";
        }

        using (new EditorGUI.DisabledScope(s_wallNodeCopy == null))
            if (GUILayout.Button(new GUIContent("Paste", s_wallNodeCopy == null
                                                    ? "Copy a wall node first."
                                                    : $"Make this node match {s_wallNodeCopyLabel} — its place " +
                                                      "and its Curved / Gap stay as they are."),
                                 EditorStyles.miniButtonRight, GUILayout.Width(44)))
                _pendingWallNodePaste = (wall.wallId, index);

        var node = wall.nodes[index];
        DrawNodeReferenceButton($"Node {index + 1} of {WallLabel(wall)}", node.nodeId,
                                $"x {node.positionXZ.x:0.##}, z {node.positionXZ.y:0.##}");
    }

    // Set by Paste on a wall node's box, carried out once the node list has finished drawing.
    private (string wallId, int index)? _pendingWallNodePaste;

    private void ApplyPendingWallNodePaste()
    {
        if (_pendingWallNodePaste == null) return;
        var (wallId, index) = _pendingWallNodePaste.Value;
        _pendingWallNodePaste = null;

        var wall = _data.walls.Find(w => w.wallId == wallId);
        if (wall != null) PasteWallNode(wall, index);
    }

    private string WallLabel(LevelSelectDesignerData.DesignerWall wall)
        => $"wall {_data.walls.IndexOf(wall) + 1}";

    // Set by Paste on a river node's row, carried out once the Nodes list has finished drawing.
    private (string pathId, string nodeId)? _pendingRiverNodePaste;

    private void ApplyPendingRiverNodePaste()
    {
        if (_pendingRiverNodePaste == null) return;
        var (pathId, nodeId) = _pendingRiverNodePaste.Value;
        _pendingRiverNodePaste = null;

        var path = _data.paths.Find(p => p.pathId == pathId);
        if (path != null && path.nodeIds.Contains(nodeId)) PasteRiverNode(path, nodeId);
    }

    // ─────────────────────────────────────────────
    // RIVER NODES
    // ─────────────────────────────────────────────

    private void CopyRiverNode(LevelSelectDesignerData.DesignerPath path, string nodeId, int index)
    {
        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node == null) return;

        var copy = new RiverNodeCopy
        {
            label    = $"node {index} of {(string.IsNullOrEmpty(path.riverName) ? "its river" : path.riverName)}",
            inBetween = node.inBetween,
            shop     = node.type != LevelSelectDesignerData.NodeType.ShopEnd ? null
                     : CopyOf(_data.shops.Find(s => s.nodeId == nodeId)) ?? new LevelSelectDesignerData.DesignerShop(),
            pool     = CopyOf(_data.PoolAt(nodeId)),
            rim      = CopyOf(_data.RimNodeAt(nodeId)),
            walkway  = CopyOf(_data.WalkwayAt(nodeId)),
            walkway2 = CopyOf(_data.Walkway2At(nodeId)),
            bridge   = CopyOf(_data.BridgeAt(nodeId)),
            porthole = CopyOf(_data.RiverPortholeAt(nodeId)),
            width    = CopyOf(_data.RiverWidthAt(nodeId)),
            rimWall  = CopyOf(_data.RimWallAt(nodeId)),
            archway  = CopyOf(_data.ArchwayAt(nodeId, path.pathId)),
            sound    = new LevelSelectDesignerData.DesignerNode
            {
                musicTrigger     = node.musicTrigger,
                musicClip        = node.musicClip,
                musicLooped      = node.musicLooped,
                musicCrossfade   = node.musicCrossfade,
                cinematicTrigger = node.cinematicTrigger,
                cinematicPreset  = node.cinematicPreset,
                cinematicStart   = node.cinematicStart,
                cinematicPrompt  = node.cinematicPrompt,
                cinematicReach   = node.cinematicReach,
                cameraInfluence          = node.cameraInfluence,
                cameraInfluenceMode      = node.cameraInfluenceMode,
                cameraInfluenceLength    = node.cameraInfluenceLength,
                cameraInfluenceBoxHeight = node.cameraInfluenceBoxHeight,
                cameraInfluenceBoxWidth  = node.cameraInfluenceBoxWidth,
                cameraInfluenceBoxDrop   = node.cameraInfluenceBoxDrop,
                cameraInfluenceBoxOffsetAcross = node.cameraInfluenceBoxOffsetAcross,
                cameraInfluenceBoxOffsetAlong  = node.cameraInfluenceBoxOffsetAlong,
                cameraInfluenceFade      = node.cameraInfluenceFade,
                cameraInfluenceHeight    = node.cameraInfluenceHeight,
                cameraInfluenceAcross    = node.cameraInfluenceAcross,
                cameraInfluenceAim       = node.cameraInfluenceAim,
                cameraInfluenceLens      = node.cameraInfluenceLens,
                cameraInfluenceDistance  = node.cameraInfluenceDistance,
                cameraInfluenceLookAt    = node.cameraInfluenceLookAt,
                cameraInfluenceBlend     = node.cameraInfluenceBlend,
                cameraInfluenceTransitionTime    = node.cameraInfluenceTransitionTime,
                cameraInfluenceOverrideFog       = node.cameraInfluenceOverrideFog,
                cameraInfluenceFogDistance       = node.cameraInfluenceFogDistance,
                cameraInfluenceFocusOnViewTarget = node.cameraInfluenceFocusOnViewTarget,
            },
        };

        var influence = FindInfluenceCamera(nodeId);
        if (influence != null)
        {
            Quaternion toRiver = Quaternion.Inverse(RiverFacingAt(nodeId));
            copy.hasCamera      = true;
            copy.cameraOffset   = toRiver * (influence.transform.position - node.worldPosition);
            copy.cameraRotation = toRiver * influence.transform.rotation;
            var cam = influence.GetComponent<CinemachineCamera>();
            if (cam != null) copy.cameraLens = cam.Lens;
        }

        int stretches = path.nodeIds.Count - 1;
        if (stretches >= 1)
            foreach (var o in _data.outposts.Where(o => o.pathId == path.pathId && OutpostNodeId(o) == nodeId))
                copy.outposts.Add((o.Clone(), Mathf.Clamp01(o.pathT) * stretches - index));

        s_riverNodeCopy   = copy;
        _consoleStatusMsg = $"Copied {copy.label}.";
    }

    private void PasteRiverNode(LevelSelectDesignerData.DesignerPath path, string nodeId)
    {
        var src  = s_riverNodeCopy;
        var node = _data.nodes.Find(n => n.id == nodeId);
        if (src == null || node == null) return;

        Undo.RecordObject(_data, "Paste Node");
        string skipped = null;
        void Skip(string what) => skipped = (skipped == null ? "" : skipped + "; ") + what;

        // This node's outposts, found before a pool's lead-ins change the river's nodes.
        var removed = _data.outposts.Where(o => o.pathId == path.pathId && OutpostNodeId(o) == nodeId).ToList();

        // Shop — only where Add Shop is offered: a plain node, or a shop already.
        _data.shops ??= new List<LevelSelectDesignerData.DesignerShop>();
        if (node.type == LevelSelectDesignerData.NodeType.Waypoint ||
            node.type == LevelSelectDesignerData.NodeType.ShopEnd)
        {
            _data.shops.RemoveAll(s => s != null && s.nodeId == nodeId);
            node.type = src.shop != null ? LevelSelectDesignerData.NodeType.ShopEnd
                                         : LevelSelectDesignerData.NodeType.Waypoint;
            if (src.shop != null)
            {
                var shop = CopyOf(src.shop);
                shop.nodeId = nodeId;
                shop.pathId = null;   // stood on its node, as Add Shop makes it
                shop.pathT  = 0f;
                _data.shops.Add(shop);
                _selectedShopNodeId = nodeId;
            }
            else if (_selectedShopNodeId == nodeId) _selectedShopNodeId = null;
        }
        else if (src.shop != null)
        {
            Skip("shop left off — arena or junction node");
        }

        // Pool, with lead-ins of its own here. A soul is claimed by one pool only, so the copied
        // pool's soul slots come empty; the pool it replaces hands its souls back.
        var oldPool = _data.PoolAt(nodeId);
        if (oldPool != null)
        {
            RemovePoolLeadIns(oldPool);
            if (oldPool.soulFishAreaSouls != null) foreach (var soul in oldPool.soulFishAreaSouls) ReleaseSoul(soul);
            _data.pools.RemoveAll(p => p != null && p.nodeId == nodeId);
            if (_selectedPoolNodeId == nodeId) _selectedPoolNodeId = null;
            _allSouls = null;
        }
        if (src.pool != null)
        {
            var pool = CopyOf(src.pool);
            pool.nodeId  = nodeId;
            pool.leadIns = new List<LevelSelectDesignerData.PoolLeadIn>();
            pool.soulFishAreaSouls = Enumerable.Repeat<SoulData>(null, src.pool.soulFishAreaSouls?.Count ?? 0).ToList();
            pool.towerCinematicPinCameras = false;
            pool.towerCinematicPinnedAt   = node.worldPosition;
            NotStartPerch(pool.towerPerch);
            _data.pools.Add(pool);
            _selectedPoolNodeId = nodeId;

            int missed = RefreshPoolLeadIns(pool);
            if (missed > 0) Skip($"{missed} river(s) left without a pool lead-in (four compass points)");
        }

        // In-Between — kept off where the node can't be one.
        string whyInBetween = WhyCannotBeInBetween(path, nodeId);
        if (!src.inBetween || whyInBetween == null)
        {
            node.inBetween = src.inBetween;
            if (node.inBetween) SyncInBetweenNodes();
        }
        else
        {
            node.inBetween = false;
            Skip("In-Between left off — " + whyInBetween);
        }

        // River width, rim node, walkways, rim wall, bridge and porthole — only where a rim node can
        // stand. Where one can't (now a pool or a shop), whatever was here is taken off.
        bool rimChanged = false, walkwaysChanged = false, portholeChanged = false, widthChanged = false;
        _data.rimNodes  ??= new List<LevelSelectDesignerData.DesignerRimNode>();
        _data.walkways  ??= new List<LevelSelectDesignerData.DesignerWalkway>();
        _data.walkways2 ??= new List<LevelSelectDesignerData.DesignerWalkway>();
        _data.bridges   ??= new List<LevelSelectDesignerData.DesignerBridge>();
        _data.riverPortholes ??= new List<LevelSelectDesignerData.DesignerRiverPorthole>();
        _data.riverWidths    ??= new List<LevelSelectDesignerData.DesignerRiverWidth>();
        _data.rimWalls       ??= new List<LevelSelectDesignerData.DesignerRimWall>();
        if (CanHoldRimNode(nodeId, out string why))
        {
            rimChanged = _data.rimNodes.RemoveAll(r => r.nodeId == nodeId) > 0 || src.rim != null;
            if (src.rim != null)
            {
                var rim = CopyOf(src.rim);
                rim.nodeId = nodeId;
                NotStartPerch(rim.bothLeft?.perch);
                _data.rimNodes.Add(rim);
            }

            walkwaysChanged |= PasteNodeEntry(_data.walkways,  src.walkway,  nodeId, w => w.nodeId, (w, id) => w.nodeId = id);
            walkwaysChanged |= PasteNodeEntry(_data.walkways2, src.walkway2, nodeId, w => w.nodeId, (w, id) => w.nodeId = id);
            walkwaysChanged |= PasteNodeEntry(_data.bridges,   src.bridge,   nodeId, b => b.nodeId, (b, id) => b.nodeId = id);
            walkwaysChanged |= PasteNodeEntry(_data.rimWalls,  src.rimWall,  nodeId, w => w.nodeId, (w, id) => w.nodeId = id);
            portholeChanged  = PasteNodeEntry(_data.riverPortholes, src.porthole, nodeId, p => p.nodeId, (p, id) => p.nodeId = id);
            widthChanged     = PasteNodeEntry(_data.riverWidths, src.width, nodeId, w => w.nodeId, (w, id) => w.nodeId = id);
        }
        else
        {
            rimChanged       = _data.rimNodes.RemoveAll(r => r.nodeId == nodeId) > 0;
            walkwaysChanged |= PasteNodeEntry(_data.walkways,  null, nodeId, w => w.nodeId, (w, id) => w.nodeId = id);
            walkwaysChanged |= PasteNodeEntry(_data.walkways2, null, nodeId, w => w.nodeId, (w, id) => w.nodeId = id);
            walkwaysChanged |= PasteNodeEntry(_data.bridges,   null, nodeId, b => b.nodeId, (b, id) => b.nodeId = id);
            walkwaysChanged |= PasteNodeEntry(_data.rimWalls,  null, nodeId, w => w.nodeId, (w, id) => w.nodeId = id);
            portholeChanged  = PasteNodeEntry(_data.riverPortholes, null, nodeId, p => p.nodeId, (p, id) => p.nodeId = id);
            widthChanged     = PasteNodeEntry(_data.riverWidths, null, nodeId, w => w.nodeId, (w, id) => w.nodeId = id);

            if (src.rim != null || src.walkway != null || src.walkway2 != null || src.bridge != null ||
                src.porthole != null || src.width != null || src.rimWall != null)
                Skip("river width, rim node, walkways, rim wall, bridge and porthole left off — " + why);
        }

        // Archway — this river's only; it can also stand on either end of the river.
        _data.archways ??= new List<LevelSelectDesignerData.DesignerArchway>();
        if (CanHoldArchway(path, nodeId, out string whyArchway))
        {
            walkwaysChanged |= _data.archways.RemoveAll(a => a != null && a.nodeId == nodeId && a.pathId == path.pathId) > 0;
            if (src.archway != null)
            {
                var archway = CopyOf(src.archway);
                archway.nodeId = nodeId;
                archway.pathId = path.pathId;
                _data.archways.Add(archway);
                walkwaysChanged = true;
            }
        }
        else
        {
            walkwaysChanged |= _data.archways.RemoveAll(a => a != null && a.nodeId == nodeId && a.pathId == path.pathId) > 0;
            if (src.archway != null) Skip("archway left off — " + whyArchway);
        }

        // Audio and Cinematic. Pin Cameras stays off: a preset's cameras follow one node only.
        bool musicChanged = node.musicTrigger != src.sound.musicTrigger;
        node.musicTrigger   = src.sound.musicTrigger;
        node.musicClip      = src.sound.musicClip;
        node.musicLooped    = src.sound.musicLooped;
        node.musicCrossfade = src.sound.musicCrossfade;

        bool cinematicChanged = node.cinematicTrigger != src.sound.cinematicTrigger ||
                                node.cinematicPreset  != src.sound.cinematicPreset  ||
                                node.cinematicStart   != src.sound.cinematicStart;
        node.cinematicTrigger    = src.sound.cinematicTrigger;
        node.cinematicPreset     = src.sound.cinematicPreset;
        node.cinematicStart      = src.sound.cinematicStart;
        node.cinematicPrompt     = src.sound.cinematicPrompt;
        node.cinematicReach      = src.sound.cinematicReach;
        node.cinematicPinCameras = false;
        node.cinematicPinnedAt   = node.worldPosition;

        // Camera Influence, and its camera stood the same way to the river here.
        node.cameraInfluence          = src.sound.cameraInfluence;
        node.cameraInfluenceMode      = src.sound.cameraInfluenceMode;
        node.cameraInfluenceLength    = src.sound.cameraInfluenceLength;
        node.cameraInfluenceBoxHeight = src.sound.cameraInfluenceBoxHeight;
        node.cameraInfluenceBoxWidth  = src.sound.cameraInfluenceBoxWidth;
        node.cameraInfluenceBoxDrop   = src.sound.cameraInfluenceBoxDrop;
        node.cameraInfluenceBoxOffsetAcross = src.sound.cameraInfluenceBoxOffsetAcross;
        node.cameraInfluenceBoxOffsetAlong  = src.sound.cameraInfluenceBoxOffsetAlong;
        node.cameraInfluenceFade      = src.sound.cameraInfluenceFade;
        node.cameraInfluenceHeight    = src.sound.cameraInfluenceHeight;
        node.cameraInfluenceAcross    = src.sound.cameraInfluenceAcross;
        node.cameraInfluenceAim       = src.sound.cameraInfluenceAim;
        node.cameraInfluenceLens      = src.sound.cameraInfluenceLens;
        node.cameraInfluenceDistance  = src.sound.cameraInfluenceDistance;
        node.cameraInfluenceLookAt    = src.sound.cameraInfluenceLookAt;
        node.cameraInfluenceBlend     = src.sound.cameraInfluenceBlend;
        node.cameraInfluenceTransitionTime    = src.sound.cameraInfluenceTransitionTime;
        node.cameraInfluenceOverrideFog       = src.sound.cameraInfluenceOverrideFog;
        node.cameraInfluenceFogDistance       = src.sound.cameraInfluenceFogDistance;
        node.cameraInfluenceFocusOnViewTarget = src.sound.cameraInfluenceFocusOnViewTarget;
        if (src.hasCamera) PasteInfluenceCamera(node, src);

        // Outposts: this node's own go, the copied ones stand the same distance from it.
        int index     = path.nodeIds.IndexOf(nodeId);
        int stretches = path.nodeIds.Count - 1;
        var added     = new List<LevelSelectDesignerData.DesignerOutpost>();
        foreach (var o in removed) _data.outposts.Remove(o);
        if (stretches >= 1)
            foreach (var (outpost, along) in src.outposts)
            {
                var o = outpost.Clone();
                o.outpostId = Guid.NewGuid().ToString();
                o.pathId    = path.pathId;
                o.pathT     = Mathf.Clamp01((index + along) / stretches);
                NotStartPerch(o.towerPerch);
                NotStartPerch(o.bothLeft?.towerPerch);
                _data.outposts.Add(o);
                added.Add(o);
            }
        if (removed.Any(o => o.outpostId == _selectedOutpostId)) _selectedOutpostId = null;

        MarkDirty();

        // Rebuild what changed in the scene.
        if (rimChanged) RebuildRimNodesAt(nodeId);
        foreach (var o in removed)
            foreach (var block in FindObjectsOfType<InstallationOutpostMesh>().Where(r => r.outpostId == o.outpostId))
                Undo.DestroyObjectImmediate(block.gameObject);
        var outpostsParent = GameObject.Find(OutpostsParent);
        if (outpostsParent != null)
            foreach (var o in added) BuildOutpostBlocks(o, outpostsParent.transform);
        if (portholeChanged || widthChanged) RebuildRunMeshes(path.riverName);   // the run and its walkways
        if (widthChanged) RebuildOutpostsOnPath(path.pathId);
        if (walkwaysChanged || removed.Count > 0 || added.Count > 0)
            RebuildWalkwaysOnPath(path.pathId, true);
        if (musicChanged)     RebuildMusicTriggers();
        if (cinematicChanged) RebuildCinematicTriggers();
        else                  UpdateCinematicPoint(node);

        // A shop, a pool and an In-Between move reach the scene on Generate, as when added by hand.
        if (src.shop != null || src.pool != null || oldPool != null || src.inBetween)
            Skip("Generate to see the shop, pool or In-Between");
        _consoleStatusMsg = skipped == null ? $"Pasted {src.label}." : $"Pasted {src.label} — {skipped}";
        Repaint();
    }

    // Which way the river runs at a node, flat — the frame a Camera Influence camera is carried in.
    private Quaternion RiverFacingAt(string nodeId)
        => TryMusicTriggerBox(nodeId, out _, out Quaternion facing, out _) ? facing : Quaternion.identity;

    // The copied camera onto the node's own (made if it has none), its settings left as pasted.
    private void PasteInfluenceCamera(LevelSelectDesignerData.DesignerNode node, RiverNodeCopy src)
    {
        var influence = FindInfluenceCamera(node.id);
        if (influence == null)
        {
            influence = SpawnInfluenceCamera(node);   // seeds Length / Fade / Box Height / Box Width — put back below
            if (influence == null) return;
            node.cameraInfluenceLength    = src.sound.cameraInfluenceLength;
            node.cameraInfluenceFade      = src.sound.cameraInfluenceFade;
            node.cameraInfluenceBoxHeight = src.sound.cameraInfluenceBoxHeight;
            node.cameraInfluenceBoxWidth  = src.sound.cameraInfluenceBoxWidth;
        }

        Quaternion facing = RiverFacingAt(node.id);
        Undo.RecordObject(influence.transform, "Paste Node");
        influence.transform.SetPositionAndRotation(node.worldPosition + facing * src.cameraOffset,
                                                   facing * src.cameraRotation);
        node.cameraInfluencePinnedAt = node.worldPosition;

        var cam = influence.GetComponent<CinemachineCamera>();
        if (cam != null)
        {
            Undo.RecordObject(cam, "Paste Node");
            cam.Lens = src.cameraLens;
        }
    }

    /// <summary>
    /// Puts a copy of <paramref name="source"/> on the node in place of whatever it had — or
    /// nothing, when the copied node had none. Hands back whether anything changed.
    /// </summary>
    private static bool PasteNodeEntry<T>(List<T> list, T source, string nodeId,
                                          Func<T, string> idOf, Action<T, string> setId) where T : class
    {
        bool changed = list.RemoveAll(e => e != null && idOf(e) == nodeId) > 0;
        if (source == null) return changed;

        var entry = CopyOf(source);
        setId(entry, nodeId);
        list.Add(entry);
        return true;
    }

    // ─────────────────────────────────────────────
    // WALL NODES
    // ─────────────────────────────────────────────

    private void PasteWallNode(LevelSelectDesignerData.DesignerWall wall, int index)
    {
        if (s_wallNodeCopy == null || index < 0 || index >= wall.nodes.Count) return;

        Undo.RecordObject(_data, "Paste Wall Node");
        var old  = wall.nodes[index];
        var node = CopyOf(s_wallNodeCopy);
        node.positionXZ   = old.positionXZ;
        node.curvedToNext = old.curvedToNext;
        node.gapToNext    = old.gapToNext;
        node.nodeId       = old.nodeId;   // still the same node, so its seam spots stay on it
        NotStartPerch(node.towerPerch);
        wall.nodes[index] = node;

        MarkDirty();
        RebuildWall(wall);
        _consoleStatusMsg = $"Pasted {s_wallNodeCopyLabel}.";
        GUI.FocusControl(null);
        Repaint();
    }
}

#endif
