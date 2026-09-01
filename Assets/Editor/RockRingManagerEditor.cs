using UnityEngine;
using UnityEditor;

/// <summary>
/// Adds a live readout to the RockRingManager inspector, so what the shader is actually being fed
/// is visible while the game runs rather than something to be inferred from the console.
///
/// Three counts, because they answer three different questions: how many rocks exist at all, how
/// many the LOD radius let through, and how many reached the shader. When the middle one exceeds
/// the last, the budget is the thing clipping them — pull the LOD radius in or raise the budget.
/// When the first one exceeds the middle, the LOD is doing its job and nothing is wrong.
/// </summary>
[CustomEditor(typeof(RockRingManager))]
public class RockRingManagerEditor : Editor
{
    // The counts change every frame; without this the inspector would only refresh on mouse-over.
    public override bool RequiresConstantRepaint() => Application.isPlaying;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Live", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter play mode to see what the rings are doing — rocks are " +
                                    "built when the level spawns.", MessageType.None);
            return;
        }

        int registered = RockRingManager.RegisteredCount;
        int inRange    = RockRingManager.InRangeCount;
        int rendering  = RockRingManager.RenderingCount;
        int budget     = RockRingManager.LastBudget;

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField(new GUIContent("Rendering",
                "Rocks whose data reached the shader this frame. This is what the per-pixel loop costs."),
                rendering);
            EditorGUILayout.IntField(new GUIContent("In LOD Range",
                "Rocks inside the LOD radius, before the budget was applied."), inRange);
            EditorGUILayout.IntField(new GUIContent("Registered",
                "Every rock on the level that can throw rings, near or far."), registered);
        }

        // Only say something when there is something to say — a quiet inspector means the LOD and
        // the budget are both comfortable.
        if (inRange > budget)
            EditorGUILayout.HelpBox(
                $"{inRange - budget} rock(s) in range are being dropped: the budget is {budget} " +
                $"of a possible {RockRingManager.MAX}. The furthest go first, so they were already " +
                $"fading. Raise Active Budget or pull the LOD radius in.", MessageType.Warning);
        else if (rendering == 0 && registered > 0)
            EditorGUILayout.HelpBox(
                "No rocks in range. Either the boat is away from them, or the LOD radius is too " +
                "tight — the radius is driven by the wave preset, not the field above.",
                MessageType.Info);
        else if (registered == 0)
            EditorGUILayout.HelpBox(
                "No rocks have registered. Rings come from procedural spikes only, so a level " +
                "without them shows nothing.", MessageType.Info);
    }
}
