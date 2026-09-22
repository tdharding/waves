using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The poster art the map's display points can show — one asset holding every image, each
/// under a name.
///
/// A display point remembers the name, not the image, so art can be swapped, renamed in one
/// place, or added to long after the point was placed without going back to the designer and
/// re-generating the river it stands on.
/// </summary>
[CreateAssetMenu(fileName = "LevelSelectPosterLibrary", menuName = "Level Select/Poster Library")]
public class LevelSelectPosterLibrary : ScriptableObject
{
    /// <summary>One poster: the name it is picked by, and the art itself.</summary>
    [Serializable]
    public class Poster
    {
        [Tooltip("What this poster is called in the designer's Interact block. Keep it unique.")]
        public string posterName;

        [Tooltip("The art shown on the overlay.")]
        public Sprite image;
    }

    [Tooltip("Every poster on the map. The order here is the order of the designer's dropdown.")]
    public List<Poster> posters = new();

    /// <summary>The art under a name, or null when there is none — an empty name included.</summary>
    public Sprite Find(string posterName)
    {
        if (string.IsNullOrEmpty(posterName) || posters == null) return null;

        foreach (var poster in posters)
            if (poster != null && poster.posterName == posterName) return poster.image;

        return null;
    }

    /// <summary>Every name in the asset, in order, for the designer's dropdown.</summary>
    public string[] Names()
    {
        var names = new List<string>();
        if (posters == null) return names.ToArray();

        foreach (var poster in posters)
            if (poster != null && !string.IsNullOrEmpty(poster.posterName)) names.Add(poster.posterName);

        return names.ToArray();
    }
}
