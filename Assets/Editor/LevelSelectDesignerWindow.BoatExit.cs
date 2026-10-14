using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Where the boat comes out when the player leaves a level by one of its doors: straight out
/// from the arena centre through that door, Boat Spawn Distance past it, facing away from it.
///
/// Each door carries its own distance, set in the arena's panel and shown on the canvas as a dot
/// with the way the boat will face. Generate writes the result onto that door's link on the
/// arena's LevelSelectArenaController, and LevelSelectDataController reads it back when the map
/// loads after a level. Changing a distance rewrites the links in the open scene straight away.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private static readonly Color BoatExitColour = new Color(1f, 0.93f, 0.6f);   // the BOAT marker's

    // ── Panel ──────────────────────────────────────────────────────

    /// <summary>One Boat Spawn Distance per door, primary first. Shown with or without archways.</summary>
    private void DrawBoatExitSection(LevelSelectDesignerData.DesignerArena arena)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Boat exit", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "How far out past each door the boat appears when it comes back from the level, " +
            "facing away from the door. The dot on the canvas shows where.",
            EditorStyles.wordWrappedMiniLabel);

        bool changed = DrawBoatSpawnDistance($"Entrance {arena.entranceIndex + 1}",
                                             ref arena.boatSpawnDistance);

        foreach (var entrance in arena.secondaryEntrances)
        {
            if (entrance == null) continue;
            changed |= DrawBoatSpawnDistance($"Entrance {entrance.entranceIndex + 1}",
                                             ref entrance.boatSpawnDistance);
        }

        if (changed) RefreshBoatExitsInScene(arena);
    }

    private bool DrawBoatSpawnDistance(string label, ref float value)
    {
        EditorGUI.BeginChangeCheck();
        float distance = EditorGUILayout.FloatField(
            new GUIContent($"{label}: Boat spawn distance",
                "How far out past this door the boat appears when the player leaves the level by " +
                "it. Measured from the door sheet when the arena has archways, otherwise from the " +
                "arena wall."),
            value);

        if (!EditorGUI.EndChangeCheck()) return false;

        Undo.RecordObject(_data, "Edit Boat Spawn Distance");
        value = Mathf.Max(0f, distance);
        MarkDirty();
        return true;
    }

    // ── Where it lands ─────────────────────────────────────────────

    /// <summary>
    /// The boat exit of the door standing at <paramref name="doorNodeId"/>: on the water, out past
    /// the door along the line from the arena centre through it, and the way that line runs. False
    /// when the node is not one of this arena's doors.
    /// </summary>
    private bool TryDoorBoatExit(LevelSelectDesignerData.DesignerArena arena, string doorNodeId,
                                 out Vector3 position, out Vector3 forward)
    {
        position = Vector3.zero;
        forward  = Vector3.zero;
        if (arena == null || string.IsNullOrEmpty(doorNodeId)) return false;

        var site = ArenaEntranceSites(arena).FirstOrDefault(s => s.nodeId == doorNodeId);
        Vector3 outward = site.outward;
        outward.y = 0f;
        if (outward.sqrMagnitude < 1e-6f) return false;
        forward = outward.normalized;

        // The wall's inner face, where an archway stands — and, with archways on, as far back
        // through the arch as this door's sheet.
        float door = _data.ArenaWallFor(arena).radius;
        if (arena.archwayOnEntrances)
            door += DoorAlong(arena, doorNodeId, site.entranceIndex);

        Vector3 centre = GetTrueArenaCenter(arena);
        position   = centre + forward * (door + _data.BoatSpawnDistanceFor(arena, doorNodeId));
        position.y = centre.y - BoatSplineDrop;   // on the water, as the walls and doors stand
        return true;
    }

    /// <summary>Writes a door's boat exit onto its link, or clears it when there is none.</summary>
    private void StampBoatExit(LevelSelectArenaController.PortalLink link,
                               LevelSelectDesignerData.DesignerArena arena, string doorNodeId)
    {
        if (link == null) return;

        if (TryDoorBoatExit(arena, doorNodeId, out Vector3 position, out Vector3 forward))
        {
            link.boatExitPosition = position;
            link.boatExitForward  = forward;
        }
        else
        {
            link.boatExitPosition = Vector3.zero;
            link.boatExitForward  = Vector3.zero;
            Debug.LogWarning($"[LevelSelectDesigner] No boat exit for door node '{doorNodeId}' — " +
                             "the boat will go back where it went in when leaving by that door.");
        }
    }

    /// <summary>
    /// Rewrites the boat exits on this arena's links in the open scene, so a distance changed in
    /// the panel is live without a Generate. The links are matched to doors the way Generate
    /// makes them: the primary door first, then each extra door by its entrance index.
    /// </summary>
    private void RefreshBoatExitsInScene(LevelSelectDesignerData.DesignerArena arena)
    {
        var tag  = FindObjectsOfType<LevelSelectDesignerArenaTag>().FirstOrDefault(t => t.nodeId == arena.nodeId);
        var ctrl = tag != null ? tag.GetComponentInChildren<LevelSelectArenaController>() : null;
        if (ctrl == null || ctrl.portalLinks == null) return;

        Undo.RecordObject(ctrl, "Edit Boat Spawn Distance");

        for (int i = 0; i < ctrl.portalLinks.Count; i++)
        {
            var link = ctrl.portalLinks[i];
            if (link == null) continue;

            string doorNodeId = i == 0
                ? arena.nodeId
                : arena.secondaryEntrances.FirstOrDefault(s => s != null && s.entranceIndex == link.entranceIndex)?.nodeId;
            if (doorNodeId == null) continue;

            StampBoatExit(link, arena, doorNodeId);
        }

        EditorUtility.SetDirty(ctrl);
    }

    // ── Canvas ─────────────────────────────────────────────────────

    /// <summary>
    /// A dot where the boat comes out of each door, with a short arrow the way it faces. Read from
    /// the same answer Generate writes, so the dot cannot drift from where the boat turns up.
    /// </summary>
    private void DrawBoatExits()
    {
        if (Event.current.type != EventType.Repaint) return;

        foreach (var arena in _data.arenas)
        {
            if (arena == null || string.IsNullOrEmpty(arena.nodeId)) continue;

            DrawBoatExit(arena, arena.nodeId);
            foreach (var entrance in arena.secondaryEntrances)
                if (entrance != null) DrawBoatExit(arena, entrance.nodeId);
        }
    }

    private void DrawBoatExit(LevelSelectDesignerData.DesignerArena arena, string doorNodeId)
    {
        if (!TryDoorBoatExit(arena, doorNodeId, out Vector3 world, out Vector3 forward)) return;

        Vector2 at = WorldToCanvas(world);
        if (!_canvasRect.Contains(at)) return;

        // Taken off the canvas rather than off the world, so the arrow points the way the boat
        // faces whichever way the view has the map round.
        Vector2 fwd = WorldToCanvas(world + forward) - at;
        if (fwd.sqrMagnitude < 1e-6f) return;
        fwd.Normalize();
        Vector2 side = new Vector2(-fwd.y, fwd.x);

        const float Dot   = 4f;    // fixed sizes on screen, as the node markers are
        const float Shaft = 12f;
        const float Head  = 4f;

        Vector2 tip  = at + fwd * (Dot + Shaft);
        Vector2 back = tip - fwd * Head;

        Handles.color = BoatExitColour;
        Handles.DrawSolidDisc(at, Vector3.forward, Dot);
        DrawSolidLine(2f, (Vector3)(at + fwd * Dot), (Vector3)tip);
        DrawSolidLine(2f, (Vector3)tip, (Vector3)(back + side * Head));
        DrawSolidLine(2f, (Vector3)tip, (Vector3)(back - side * Head));
    }
}

#endif
