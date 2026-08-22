using UnityEngine;
using UnityEditor;

/// <summary>
/// Inspector for SoulFishController: the tunable fields as normal, plus a live read-only readout
/// of what's actually in the level (fish, shoals, street lights, mask budget).
/// </summary>
[CustomEditor(typeof(SoulFishController))]
public class SoulFishControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var c = (SoulFishController)target;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Level Readout", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.IntField(new GUIContent("Fish In Level", "Live soul fish across every shoal."), c.FishInLevel);
            EditorGUILayout.IntField(new GUIContent("Catchable Now", "Fish in shoals whose CanFish is currently true."), c.CatchableFish);
            EditorGUILayout.IntField(new GUIContent("Shoals", "Registered SoulShoalControllers."), c.ShoalCount);
            EditorGUILayout.IntField(new GUIContent("Street Lights Lit", "Lit lamps / total lamps in the level."), c.LitStreetLights);
            EditorGUILayout.IntField(new GUIContent("Street Lights Total"), c.StreetLights);
            EditorGUILayout.IntField(new GUIContent("Mask Zones", "Zone entries registered with SoulFishWaveLinker."), c.MaskZones);
            EditorGUILayout.IntField(new GUIContent("Mask Points (of 40)", "Packed points used of the shared shader budget."), c.MaskPackedPts);
            EditorGUILayout.IntField(new GUIContent("Surface Sprites (of 24)", "Fish sprites published to the water shader this frame."), c.SpritesDrawn);
            EditorGUILayout.EndVertical();
        }

        if (c.MaskPackedPts >= 40)
            EditorGUILayout.HelpBox("Mask point budget is full — extra zones/fish are being dropped from the wave mask.",
                                    MessageType.Warning);

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Readout populates in play mode.", MessageType.Info);
        else
            Repaint();   // keep the numbers live while playing
    }
}
