using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// A river node's Add ▾ dropdown: everything that can be put on the node — pool, outpost,
/// bridge, archway, shop, arena, junction — in one menu instead of a row of buttons. Whatever
/// is already there shows as its Remove item.
///
/// Add Junction makes the node a junction on the river running through it and starts drawing
/// the branch from it, as the Junction tool does: click to place the branch's nodes, Enter or
/// double-click to finish.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    /// <summary>
    /// The Add ▾ button for a node. In a node's row (<paramref name="inRow"/>) it also offers
    /// the shop and arena, and pool/outpost wait until the Nodes list has finished drawing.
    /// </summary>
    private void DrawNodeAddButton(LevelSelectDesignerData.DesignerPath path, string nodeId,
                                   bool inRow, string arenaEndNodeId = null)
    {
        var  label = new GUIContent("Add ▾", "Put something on this node.");
        Rect rect  = GUILayoutUtility.GetRect(label, EditorStyles.miniPullDown, GUILayout.Width(70f));
        if (EditorGUI.DropdownButton(rect, label, FocusType.Passive, EditorStyles.miniPullDown))
            ShowNodeAddMenu(path, nodeId, inRow, arenaEndNodeId, rect);
    }

    private void ShowNodeAddMenu(LevelSelectDesignerData.DesignerPath path, string nodeId,
                                 bool inRow, string arenaEndNodeId, Rect under)
    {
        var menu = new GenericMenu();

        void Item(string label, string whyNot, GenericMenu.MenuFunction act)
        {
            if (whyNot == null) menu.AddItem(new GUIContent(label), false, act);
            else                menu.AddDisabledItem(new GUIContent($"{label} ({whyNot.TrimEnd('.')})"));
        }

        bool hasPool = _data.PoolAt(nodeId) != null;
        Item(hasPool ? "Remove Pool" : "Add Pool", null, () =>
        {
            if (inRow) _pendingPoolToggleNodeId = nodeId;
            else       TogglePoolAt(nodeId);
            Repaint();
        });

        Item("Add Outpost", path.nodeIds.Count >= 2 ? null : "needs a river", () =>
        {
            if (inRow) _pendingOutpostNodeId = nodeId;
            else       AddOutpostAt(path, nodeId);
            Repaint();
        });

        bool hasBridge = _data.BridgeAt(nodeId) != null;
        bool canBridge = CanHoldWalkway(nodeId, out string whyBridge);
        Item(hasBridge ? "Remove Bridge" : "Add Bridge",
             hasBridge || canBridge ? null : whyBridge?.Replace("Walkways", "Bridges") ?? "not here",
             () => ToggleBridgeAt(path, nodeId));

        bool hasArchway = _data.ArchwayAt(nodeId, path.pathId) != null;
        bool canArchway = CanHoldArchway(path, nodeId, out string whyArchway);
        Item(hasArchway ? "Remove Archway" : "Add Archway",
             hasArchway || canArchway ? null : whyArchway ?? "not here",
             () => ToggleArchwayAt(path, nodeId));

        if (inRow)
        {
            // A node has one type, so a shop can't go on an arena or junction node.
            var  node    = _data.nodes.Find(n => n.id == nodeId);
            bool hasShop = node != null && node.type == LevelSelectDesignerData.NodeType.ShopEnd;
            bool canShop = node != null && node.type == LevelSelectDesignerData.NodeType.Waypoint;
            Item(hasShop ? "Remove Shop" : "Add Shop",
                 hasShop || canShop ? null : "arena or junction node",
                 () => ToggleShopAt(nodeId));

            // The arena only ever stands on the end node the river runs into.
            if (nodeId == arenaEndNodeId && !path.leadsToArena)
                Item("Add Arena", null, () =>
                {
                    Undo.RecordObject(_data, "Add Arena");
                    SetLeadsToArena(path, true);
                });
        }

        menu.AddSeparator("");
        string pathId = path.pathId;
        Item("Add Junction", WhyCannotAddJunctionAt(path, nodeId),
             () => AddJunctionFromMenu(pathId, nodeId));

        menu.DropDown(under);
    }

    // ─────────────────────────────────────────────
    // ADD JUNCTION
    // ─────────────────────────────────────────────

    /// <summary>
    /// Why a branch can't start at this node, or null when it can: a plain node partway along
    /// one river, with nothing standing on it that a junction would cut away. Where another
    /// river already meets the node, the node's ▾ menu names that meeting instead.
    /// </summary>
    private string WhyCannotAddJunctionAt(LevelSelectDesignerData.DesignerPath path, string nodeId)
    {
        if (_data.junctions.Exists(j => j.nodeId == nodeId)) return "already a junction";
        if (AnotherRiverMeets(path, nodeId))                 return "another river meets it — use the node's ▾ menu";

        int at = path.nodeIds.IndexOf(nodeId);
        if (at <= 0 || at >= path.nodeIds.Count - 1)        return "end of a river";

        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node == null)                                    return "missing node";
        if (node.type != LevelSelectDesignerData.NodeType.Waypoint) return "arena or shop node";
        if (_data.PoolAt(nodeId) != null)                    return "pool";
        if (IsLockedNode(nodeId) || IsLeadInNode(nodeId))    return "lead-in";
        if (_data.RimNodeAt(nodeId) != null)                 return "rim node";
        if (_data.WalkwayAt(nodeId) != null)                 return "walkway";
        if (_data.BridgeAt(nodeId) != null)                  return "bridge";
        if (_data.ArchwayAt(nodeId, path.pathId) != null)    return "archway";
        return null;
    }

    private void AddJunctionFromMenu(string pathId, string nodeId)
    {
        var path = _data != null ? _data.paths.Find(p => p.pathId == pathId) : null;
        if (path == null || WhyCannotAddJunctionAt(path, nodeId) != null) return;

        Undo.RecordObject(_data, "Add Junction");
        _data.nodes.Find(n => n.id == nodeId).type = LevelSelectDesignerData.NodeType.JunctionSplit;
        _data.junctions.Add(new LevelSelectDesignerData.DesignerJunction
        {
            junctionId  = System.Guid.NewGuid().ToString(),
            nodeId      = nodeId,
            pathIds     = new List<string> { pathId },
            riverPathId = pathId
        });

        // Straight into drawing the branch off it, as the Junction tool does after its click.
        _pendingMode = null;
        SetMode(DesignerMode.Junction);
        _drawingNodeIds.Clear();
        _drawingNodeIds.Add(nodeId);
        _isDrawing = true;

        Debug.Log($"[LevelSelectDesigner] Junction added on '{path.segmentId}' — click on the map " +
                  "to draw the branch, Enter or double-click to finish.");
        MarkDirty();
        Repaint();
    }
}

#endif
