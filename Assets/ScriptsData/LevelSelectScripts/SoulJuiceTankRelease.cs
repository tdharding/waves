using UnityEngine;

/// <summary>
/// The interaction a soul juice tank carries: press E beside it and its bowl of soul juice drains,
/// fills the pool round it, and the soul juice paths that begin there draw out. No fish.
///
/// Sits beside a <see cref="LevelSelectInteractPoint"/> and the tank's
/// <see cref="SoulJuiceTankBowl"/>, which says which pool the tank stands in. Only offered
/// until the tank has been released, and only while a route begins there.
///
/// With a <see cref="cinematic"/> set (the pool's Cinematic in the Level Select Designer), the same
/// press also plays that camera sequence, so the cameras watch the path draw out. A Play Once
/// preset that has already played is skipped and the tank is just released.
/// </summary>
public class SoulJuiceTankRelease : LevelSelectInteractAction
{
    [Tooltip("The camera sequence played as the souls are let out. Written by the designer from the pool's Cinematic.")]
    public CameraSequencePreset cinematic;

    private SoulJuiceTankBowl _bowl;
    private TravellingSoulFishController _controller;
    private bool _playing;

    private static CameraSequence s_sequence;

    private static CameraSequence Sequence
    {
        get
        {
            if (s_sequence == null) s_sequence = FindAnyObjectByType<CameraSequence>();
            return s_sequence;
        }
    }

    private string PoolNodeId
    {
        get
        {
            if (_bowl == null) _bowl = GetComponent<SoulJuiceTankBowl>();
            return _bowl != null ? _bowl.poolNodeId : null;
        }
    }

    private TravellingSoulFishController Controller
    {
        get
        {
            if (_controller == null) _controller = FindObjectOfType<TravellingSoulFishController>();
            return _controller;
        }
    }

    public override bool IsAvailable => Controller != null && Controller.CanRelease(PoolNodeId);

    // The souls have to be seen swimming off — the clock keeps running.
    public override bool PausesTime => false;

    public override bool Begin()
    {
        _playing = false;

        if (Controller == null)
        {
            Debug.LogWarning("[SoulJuiceTankRelease] No TravellingSoulFishController in the scene — " +
                             "nothing to release the souls onto.", this);
            return false;
        }
        if (string.IsNullOrEmpty(PoolNodeId))
        {
            Debug.LogWarning($"[SoulJuiceTankRelease] '{name}' has no SoulJuiceTankBowl beside it saying " +
                             "which pool it stands in — Generate in the Level Select Designer.", this);
            return false;
        }

        if (!Controller.ReleaseSoulJuiceTank(PoolNodeId)) return false;

        PlayCinematic();
        return true;
    }

    private void PlayCinematic()
    {
        if (cinematic == null) return;

        var sequence = Sequence;
        if (sequence == null)
        {
            Debug.LogWarning($"[SoulJuiceTankRelease] '{name}' has cinematic '{cinematic.name}', but there is " +
                             "no CameraSequence in the scene.", this);
            return;
        }
        if (cinematic.playOnce && GameProgressData.HasSequencePlayed(sequence.PresetKey(cinematic)))
        {
            Debug.Log($"[SoulJuiceTankRelease] Tank released at '{name}' — '{cinematic.name}' has already " +
                      "played on this save, so no cinematic.");
            return;
        }

        Debug.Log($"[SoulJuiceTankRelease] Tank released at '{name}' — playing '{cinematic.name}'.");
        sequence.PlayPreset(cinematic);
        _playing = sequence.IsPlaying;
    }

    // Done the moment it is pressed — or, with a cinematic, held until the sequence takes the boat;
    // the sequence hands everything back itself.
    public override bool Step(bool advancePressed) => _playing && Sequence != null && Sequence.IsPlaying;

    public override void End() => _playing = false;
}
