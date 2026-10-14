using UnityEngine;

/// <summary>
/// The interaction a souls pool tower carries: press E beside it and the souls circling in its
/// bowl are let out onto the routes that begin there, and swim off along them.
///
/// Sits beside a <see cref="LevelSelectInteractPoint"/> and the tower's
/// <see cref="SoulsPoolTowerBowl"/>, which says which pool the tower stands in. Only offered
/// while there are souls waiting — an empty bowl has nothing to release.
///
/// With a <see cref="cinematic"/> set (the pool's Cinematic in the Level Select Designer), the same
/// press also plays that camera sequence, so the cameras watch the souls set off. A Play Once
/// preset that has already played is skipped and the souls are just released.
/// </summary>
public class SoulsPoolTowerRelease : LevelSelectInteractAction
{
    [Tooltip("The camera sequence played as the souls are let out. Written by the designer from the pool's Cinematic.")]
    public CameraSequencePreset cinematic;

    private SoulsPoolTowerBowl _bowl;
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
            if (_bowl == null) _bowl = GetComponent<SoulsPoolTowerBowl>();
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
            Debug.LogWarning("[SoulsPoolTowerRelease] No TravellingSoulFishController in the scene — " +
                             "nothing to release the souls onto.", this);
            return false;
        }
        if (string.IsNullOrEmpty(PoolNodeId))
        {
            Debug.LogWarning($"[SoulsPoolTowerRelease] '{name}' has no SoulsPoolTowerBowl beside it saying " +
                             "which pool it stands in — Generate in the Level Select Designer.", this);
            return false;
        }

        if (Controller.ReleaseSoulsPool(PoolNodeId) <= 0) return false;

        PlayCinematic();
        return true;
    }

    private void PlayCinematic()
    {
        if (cinematic == null) return;

        var sequence = Sequence;
        if (sequence == null)
        {
            Debug.LogWarning($"[SoulsPoolTowerRelease] '{name}' has cinematic '{cinematic.name}', but there is " +
                             "no CameraSequence in the scene.", this);
            return;
        }
        if (cinematic.playOnce && GameProgressData.HasSequencePlayed(sequence.PresetKey(cinematic)))
        {
            Debug.Log($"[SoulsPoolTowerRelease] Souls released at '{name}' — '{cinematic.name}' has already " +
                      "played on this save, so no cinematic.");
            return;
        }

        Debug.Log($"[SoulsPoolTowerRelease] Souls released at '{name}' — playing '{cinematic.name}'.");
        sequence.PlayPreset(cinematic);
        _playing = sequence.IsPlaying;
    }

    // Done the moment it is pressed — or, with a cinematic, held until the sequence takes the boat;
    // the sequence hands everything back itself.
    public override bool Step(bool advancePressed) => _playing && Sequence != null && Sequence.IsPlaying;

    public override void End() => _playing = false;
}
