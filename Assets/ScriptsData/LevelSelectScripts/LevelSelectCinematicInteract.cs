using UnityEngine;

/// <summary>
/// The interaction a node with Cinematic set to Press E carries: pressing the interact key in
/// reach plays the node's camera sequence preset through the scene's <see cref="CameraSequence"/>.
///
/// The sequence takes the boat and the camera itself, so this only starts it — the
/// <see cref="LevelSelectInteractor"/> lets go as soon as the sequence freezes the boat.
/// Built by the Level Select Designer beside a <see cref="LevelSelectInteractPoint"/>, never by hand.
/// </summary>
public class LevelSelectCinematicInteract : LevelSelectInteractAction
{
    [Tooltip("The designer node this belongs to. Written by the designer.")]
    public string nodeId;

    [Tooltip("The camera sequence this plays. Written by the designer from the node's Cinematic.")]
    public CameraSequencePreset preset;

    private static CameraSequence s_sequence;

    private static CameraSequence Sequence
    {
        get
        {
            if (s_sequence == null) s_sequence = FindAnyObjectByType<CameraSequence>();
            return s_sequence;
        }
    }

    /// <summary>
    /// Not offered while another sequence is running, or once a Play Once preset has played on
    /// this save — the prompt would come up and nothing would happen.
    /// </summary>
    public override bool IsAvailable
    {
        get
        {
            var sequence = Sequence;
            if (preset == null || sequence == null || sequence.IsPlaying) return false;
            return !(preset.playOnce && GameProgressData.HasSequencePlayed(sequence.PresetKey(preset)));
        }
    }

    // The sequence runs over time on screen, so the clock must keep going.
    public override bool PausesTime => false;

    public override bool Begin()
    {
        var sequence = Sequence;
        if (preset == null || sequence == null)
        {
            Debug.LogWarning($"[LevelSelectCinematic] E pressed at node {nodeId}, but " +
                             (preset == null ? "it has no preset." : "there is no CameraSequence in the scene."), this);
            return false;
        }

        Debug.Log($"[LevelSelectCinematic] E pressed at node {nodeId} — playing '{preset.name}'.");
        sequence.PlayPreset(preset);
        return sequence.IsPlaying;
    }

    // Held only until the sequence takes the boat; it hands everything back itself.
    public override bool Step(bool advancePressed) => Sequence != null && Sequence.IsPlaying;

    public override void End() { }
}
