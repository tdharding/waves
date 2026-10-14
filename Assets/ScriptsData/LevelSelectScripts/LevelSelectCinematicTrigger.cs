using UnityEngine;

/// <summary>
/// The box across the river at a node with Cinematic on. The boat sailing into it plays the node's
/// camera sequence preset through the scene's <see cref="CameraSequence"/> — once per save unless
/// the preset's Play Once is off.
///
/// Built by the Level Select Designer on Generate (and when a node's Cinematic or its preset
/// changes), the same size and place as the Audio box — never placed or sized by hand.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class LevelSelectCinematicTrigger : MonoBehaviour
{
    [Tooltip("The designer node this box belongs to. Written by the designer.")]
    public string nodeId;

    [Tooltip("The camera sequence this box plays. Written by the designer from the node's Cinematic.")]
    public CameraSequencePreset preset;

    private static CameraSequence s_sequence;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("BoatPrefab")) return;
        if (preset == null)
        {
            Debug.LogWarning($"[LevelSelectCinematic] Boat entered node {nodeId}'s box, but it has no preset.", this);
            return;
        }

        if (s_sequence == null) s_sequence = FindAnyObjectByType<CameraSequence>();
        if (s_sequence == null)
        {
            Debug.LogWarning($"[LevelSelectCinematic] Boat entered node {nodeId}'s box, but there is no " +
                             "CameraSequence in the scene to play '{preset.name}'.", this);
            return;
        }

        Debug.Log($"[LevelSelectCinematic] Boat reached node {nodeId} — playing '{preset.name}'.");
        s_sequence.PlayPreset(preset);
    }
}
