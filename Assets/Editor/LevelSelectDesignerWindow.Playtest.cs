using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Playtesting from a node of your choosing, whatever the save says.
///
/// Under PLAYTEST (Fresh Save) sits Playtest From Playtest Position, with a small square (⊙)
/// beside it: press that, then click a node, and a second boat, in the colour set in the
/// Designer Settings (Node Dots), stands there on the map. The long button is the same fresh
/// save, with the boat on that node instead of at the level's start. Only that launch, and a
/// build never sees it.
///
/// While picking, Esc cancels quietly; a click off the map or a change of tool cancels with a
/// pop-up saying so.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private bool _pickingPlaytestBoat;

    private const string SkipOpeningPref = "LevelSelectDesigner_SkipOpeningCinematic";

    private static bool SkipOpeningCinematic
    {
        get => EditorPrefs.GetBool(SkipOpeningPref, false);
        set => EditorPrefs.SetBool(SkipOpeningPref, value);
    }

    /// <summary>Under PLAYTEST (Fresh Save): launch it without the scene's Play On Start cinematic.</summary>
    private void DrawSkipOpeningToggle()
    {
        bool skip = EditorGUILayout.ToggleLeft(new GUIContent("Skip Opening Cinematic",
            "PLAYTEST (Fresh Save) starts without the scene's opening camera sequence (Play On Start). " +
            "Playtest From Playtest Position always skips it."), SkipOpeningCinematic);
        if (skip != SkipOpeningCinematic) SkipOpeningCinematic = skip;
    }

    /// <summary>
    /// The secondary row under PLAYTEST (Fresh Save): the same fresh save, but the boat starts
    /// on the playtest node rather than at the level's start. The small square beside it places
    /// the playtest boat.
    /// </summary>
    private void DrawPlaytestFromPositionButton(bool canPlaytest)
    {
        bool can = canPlaytest && _data.HasPlaytestBoatNode;
        var  tip = can
            ? "Clear saved progress and enter play mode with the boat on the playtest node"
            : "Place the playtest boat first — the square button on the right";

        EditorGUILayout.BeginHorizontal();

        var prev = GUI.backgroundColor;
        GUI.enabled         = can;
        GUI.backgroundColor = can ? LevelSelectDesignerSettings.PlaytestBoatColour : Color.gray;
        if (GUILayout.Button(new GUIContent("Playtest From Playtest Position", tip), GUILayout.Height(20)))
            PlaytestFromPlaytestPosition();
        GUI.enabled         = true;

        GUI.backgroundColor = _pickingPlaytestBoat ? LevelSelectDesignerSettings.PlaytestBoatColour : prev;
        bool pick = GUILayout.Toggle(_pickingPlaytestBoat,
            new GUIContent("⊙", "Set Playtest Position: click a node to put the playtest boat there"),
            GUI.skin.button, GUILayout.Width(20), GUILayout.Height(20));
        GUI.backgroundColor = prev;

        EditorGUILayout.EndHorizontal();

        if (pick != _pickingPlaytestBoat)
        {
            _pickingBoatStart    = false;
            _pickingPlaytestBoat = pick;
        }

        if (_pickingPlaytestBoat)
            EditorGUILayout.HelpBox("Click a node to put the playtest boat there.\n" +
                                    "Esc, another tool or a click off the map cancels.",
                                    MessageType.Info);
    }

    private void PlaytestFromPlaytestPosition()
    {
        if (_data == null || !_data.HasPlaytestBoatNode) return;

        SessionState.SetString(LevelSelectDataController.PlaytestBoatNodeKey, _data.playtestBoatNodeId);
        // Always without the opening: the boat is somewhere else, not at the start it shows.
        PlaytestLinkedScene(freshSave: true, skipOpening: true);

        // Launch called off (not in Build Settings, scene save refused) — don't leave the start
        // waiting for whatever is played next.
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            SessionState.EraseString(LevelSelectDataController.PlaytestBoatNodeKey);
    }

    /// <summary>
    /// Run first thing in OnGUI, before any panel can take the event: Esc cancels the pick, and so
    /// does a click anywhere off the map — with a pop-up, the click itself still going on to
    /// whatever it was on.
    /// </summary>
    private void WatchPlaytestPick()
    {
        if (!_pickingPlaytestBoat) return;
        Event e = Event.current;

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            _pickingPlaytestBoat = false;
            Repaint();
            e.Use();
            return;
        }

        if (e.type == EventType.MouseDown && !_playtestCanvasRect.Contains(e.mousePosition))
            CancelPlaytestPick("you clicked off the map");
    }

    // The map's real rect, from the last Repaint. _canvasRect can't be used at the top of OnGUI:
    // it holds whatever the previous event left there, and before a click that's the Layout
    // pass, where GetRect hands back a 1-pixel placeholder — every click read as off the map.
    private Rect _playtestCanvasRect;

    private void RememberPlaytestCanvasRect()
    {
        if (Event.current.type == EventType.Repaint) _playtestCanvasRect = _canvasRect;
    }

    /// <summary>Called on a change of tool. Nothing happens when no pick is under way.</summary>
    private void CancelPlaytestPick(string why)
    {
        if (!_pickingPlaytestBoat) return;
        _pickingPlaytestBoat = false;
        Repaint();

        // Not shown mid-event: a modal opened inside OnGUI breaks the layout it is drawing.
        EditorApplication.delayCall += () =>
            EditorUtility.DisplayDialog("Set Playtest Position",
                $"Playtest position not set — {why}.\n\nPress the ⊙ beside Playtest From Playtest Position again and click a node.",
                "OK");
    }

    /// <summary>Takes the canvas click while picking. True when the event was handled.</summary>
    private bool HandlePlaytestPick(Event e)
    {
        if (!_pickingPlaytestBoat) return false;
        if (e.type != EventType.MouseDown || e.button != 0) return false;

        string nodeId = FindNodeAtCanvas(e.mousePosition);
        if (nodeId == null) { e.Use(); return true; }   // missed — stay armed

        if (!_data.TryGetNodeAhead(nodeId, out _))
        {
            Debug.LogWarning($"[LevelSelectDesigner] Node '{nodeId}' is not on a river — pick a node on one.");
            e.Use();
            return true;
        }

        Undo.RecordObject(_data, "Set Playtest Position");
        _data.playtestBoatNodeId = nodeId;
        _pickingPlaytestBoat     = false;
        MarkDirty();

        Debug.Log($"[LevelSelectDesigner] Playtest position set to node '{nodeId}'.");
        Repaint();
        e.Use();
        return true;
    }

    // The second boat: where Play From Playtest Position starts it.
    private void DrawPlaytestBoat()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (!_data.TryGetBoatPoseAtNode(_data.playtestBoatNodeId, out Vector3 world, out Vector3 forward, out _)) return;

        // Label under the hull, so it still reads when the start boat stands on the same node.
        DrawBoatMarker(world, forward, LevelSelectDesignerSettings.PlaytestBoatColour, "PLAYTEST", 6f);
    }
}

#endif
