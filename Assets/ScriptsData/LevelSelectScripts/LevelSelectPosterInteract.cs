using UnityEngine;

/// <summary>
/// The interaction a display point carries: one poster from the map's
/// <see cref="LevelSelectPosterLibrary"/>, held on screen until the key is pressed again.
///
/// The name is kept rather than the art itself, so the library is the one place a poster is
/// swapped or renamed — see <see cref="LevelSelectPosterLibrary"/>. Both fields are written by
/// the Level Select Designer when the display is placed.
/// </summary>
public class LevelSelectPosterInteract : LevelSelectInteractAction
{
    [Tooltip("The map's poster library. Written by the designer when this display is placed.")]
    public LevelSelectPosterLibrary library;

    [Tooltip("Which poster in the library this display shows, by name.")]
    public string posterName;

    /// <summary>The art this display would show, or null when the name finds nothing.</summary>
    public Sprite Poster => library != null ? library.Find(posterName) : null;

    public override bool Begin()
    {
        var poster = Poster;
        if (poster == null)
        {
            Debug.LogWarning($"[LevelSelectPosterInteract] '{name}' has no poster to show — " +
                             $"library {(library == null ? "not set" : library.name)}, " +
                             $"name '{posterName}'.", this);
            return false;
        }

        var overlay = LevelSelectPosterOverlayUI.Instance;
        if (overlay == null)
        {
            Debug.LogWarning("[LevelSelectPosterInteract] No LevelSelectPosterOverlayUI in the " +
                             "scene — there is nowhere to put the poster.", this);
            return false;
        }

        overlay.Show(poster);
        return true;
    }

    // One press put it up, the next takes it down. Nothing else to watch.
    public override bool Step(bool advancePressed) => !advancePressed;

    public override void End() => LevelSelectPosterOverlayUI.Instance?.Hide();
}
