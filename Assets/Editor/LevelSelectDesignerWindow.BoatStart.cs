using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

#if UNITY_EDITOR

/// <summary>
/// Setting by hand which node the boat starts at on a save that has never seen the map.
///
/// Rarely used, so it lives as one button under Setup in the right panel: press it, click a
/// node, and the boat's start moves there. Left unset, the boat starts at the head of the main
/// river as it always has.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private bool _pickingBoatStart;

    private void DrawBoatStartSection()
    {
        EditorGUILayout.Space(2);

        if (_pickingBoatStart)
        {
            EditorGUILayout.HelpBox("Click a node to start the boat there. Esc to cancel.",
                                    MessageType.Info);
            if (GUILayout.Button("Cancel")) _pickingBoatStart = false;
            return;
        }

        if (GUILayout.Button("SetBoatStartingPosition"))
            _pickingBoatStart = true;

        if (_data.HasBoatStartNode)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Boat starts at a set node", EditorStyles.miniLabel);
            if (GUILayout.Button("Back to main river", EditorStyles.miniButton, GUILayout.Width(120)))
            {
                Undo.RecordObject(_data, "Clear Boat Start");
                _data.boatStartNodeId = "";
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>Takes the canvas click while picking. True when the event was handled.</summary>
    private bool HandleBoatStartPick(Event e)
    {
        if (!_pickingBoatStart) return false;

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            _pickingBoatStart = false;
            Repaint();
            e.Use();
            return true;
        }

        if (e.type != EventType.MouseDown || e.button != 0) return false;

        string nodeId = FindNodeAtCanvas(e.mousePosition);
        if (nodeId == null) { e.Use(); return true; }   // missed — stay armed

        Undo.RecordObject(_data, "Set Boat Start");
        _data.boatStartNodeId = nodeId;
        _pickingBoatStart     = false;
        MarkDirty();

        Debug.Log($"[LevelSelectDesigner] Boat start set to node '{nodeId}'.");
        Repaint();
        e.Use();
        return true;
    }

    /// <summary>
    /// Stands the boat in the scene where a save that has never seen the map starts it — the
    /// same choice LevelSelectDataController makes at load: the set node or a pool the main
    /// river begins in, otherwise the head of the main river. Run at the end of Generate, once
    /// the rivers and their water are built. No boat in the scene, and nothing is touched.
    /// </summary>
    private void PlaceBoatAtStart()
    {
        var control = _data.boatControl != null
            ? _data.boatControl
            : FindAnyObjectByType<LevelSelectBoatControl>();

        Transform boat = control != null ? control.BoatTransform : null;
        if (boat == null)
        {
            var go = GameObject.Find("LevelSelectBoat");
            if (go != null) boat = go.transform;
        }
        if (boat == null) return;

        Vector3    position;
        Quaternion rotation;

        if (_data.TryGetBoatStart(out Vector3 startPos, out Vector3 startForward, out bool inPool) &&
            (inPool || _data.HasBoatStartNode))
        {
            position = startPos;
            rotation = Quaternion.LookRotation(startForward, Vector3.up);
        }
        else
        {
            var main = FindObjectsByType<RiverSegmentID>(FindObjectsSortMode.None)
                .FirstOrDefault(s => s.SegmentID == "MainRiver");
            if (main == null ||
                !LevelSelectBoatPlacement.TryResolve(main.GetComponent<SplineContainer>(), 0f,
                                                     out position, out rotation))
            {
                Debug.LogWarning("[LevelSelectDesigner] No start node and no MainRiver — boat left where it is.");
                return;
            }
        }

        // Settled on the water the way PlaceAt settles it at load. The water was only just
        // built, so physics is told where it is before looking for it.
        Physics.SyncTransforms();
        if (control != null && control.TrySampleSurface(position, out float y))
            position.y = y;

        Undo.RecordObject(boat, "Place Boat At Start");
        boat.SetPositionAndRotation(position, rotation);

        Debug.Log($"[LevelSelectDesigner] Boat placed at its start, {position}.");
    }
}

#endif
