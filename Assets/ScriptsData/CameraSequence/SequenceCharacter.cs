using UnityEngine;

/// <summary>
/// Marks an object as a character for camera sequences. The Camera Sequence storyboard lists every
/// character its shot cameras can see, and a shot can cross-fade a character's Animator into one of
/// its states as the shot starts.
///
/// Only objects carrying this are listed — nothing is guessed from tags or Animators.
/// </summary>
[DisallowMultipleComponent]
public class SequenceCharacter : MonoBehaviour
{
    [Tooltip("The name the storyboard lists the character by. Empty = the object's name.")]
    public string displayName;

    [Tooltip("The Animator a shot plays states on. Found on this object or its children when empty.")]
    [SerializeField] private Animator animator;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    /// <summary>The character's Animator — the one set here, else the first on it or its children. Null when it has none.</summary>
    public Animator Animator => animator != null ? animator : GetComponentInChildren<Animator>(true);
}
