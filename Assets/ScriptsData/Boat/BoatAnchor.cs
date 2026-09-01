using UnityEngine;

// Stopping the boat where it stands. Deliberately just that — no anchor UI, no camera zoom, no
// cursor swap, no wall fade. The old BoatAnchorController did all of those together, which is why
// it could not be borrowed by anything that wanted only the stopping part.
//
// Two ways in, and they share one state:
//   the key ....... a default binding, so the boat can be stopped anywhere on the level
//   a trigger ..... SetAnchored / Toggle, called by whatever wants the boat held still
//                   (the angel's conversation is the first)
//
// Whoever anchors it should release it. A trigger that takes over mid-key-anchor can put the boat
// back exactly as it found it by reading IsAnchored first — see AngelCompanion, which restores the
// player's own anchor rather than assuming a conversation always ends under way.
[DisallowMultipleComponent]
public class BoatAnchor : MonoBehaviour
{
    /// <summary>The one in the scene. Null until one exists — callers are expected to check.</summary>
    public static BoatAnchor Instance { get; private set; }

    [Header("References")]
    [Tooltip("The boat to stop. Resolved from LevelDataController at runtime if left empty.")]
    [SerializeField] BoatMovement boat;

    [Header("Key binding")]
    [Tooltip("Lets the player drop and lift the anchor anywhere with the key below. Off = the boat " +
             "can only be stopped by a trigger, such as talking to the angel.")]
    [SerializeField] bool allowKeyToggle = true;

    [Tooltip("The key that drops and lifts it. Steering is on the arrow keys, so A is free.")]
    [SerializeField] KeyCode anchorKey = KeyCode.A;

    bool _anchored;
    bool _controlsBeforeAnchor;
    bool _keyLocked;

    /// <summary>True while the boat is being held still.</summary>
    public bool IsAnchored => _anchored;

    /// <summary>
    /// Withholds the key while something else owns the anchor — a conversation, say, which would
    /// otherwise be sailed out of mid-sentence with the camera still on the angel. Triggers can
    /// still set the state; only the player's key is held back.
    /// </summary>
    public void SetKeyLocked(bool locked) => _keyLocked = locked;

    // Resolved lazily: the boat is built with the level, after this may already be sitting in the
    // scene, so asking once in Start would find nothing.
    BoatMovement Boat
    {
        get
        {
            if (boat == null)
            {
                var ldc = LevelDataController.Instance;
                if (ldc != null) boat = ldc.GetBoatMovement();
            }
            return boat;
        }
    }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (PauseManager.IsPaused) return;
        if (allowKeyToggle && !_keyLocked && Input.GetKeyDown(anchorKey)) Toggle();
    }

    public void Toggle() => SetAnchored(!_anchored);

    public void SetAnchored(bool anchored)
    {
        if (anchored == _anchored) return;

        var b = Boat;
        if (b == null) return;

        _anchored = anchored;

        if (anchored)
        {
            // Remembered rather than assumed: the boat's controls are off during the level's own
            // opening moments too, and lifting the anchor must not hand them back early.
            _controlsBeforeAnchor = b.controlsEnabled;
            b.StopBoatMovement();
        }
        else
        {
            b.controlsEnabled = _controlsBeforeAnchor;
        }
    }
}
