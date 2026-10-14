using UnityEngine;

/// <summary>
/// Marks a scene object — a shot camera, an orbit point or a character — that a <see cref="CameraSequencePreset"/>
/// links to. A preset asset can't hold a scene object, so it holds this ID instead and finds the
/// object by it. Renaming or moving the object in the hierarchy never breaks the link.
///
/// Added by the Camera Sequence storyboard when it saves a preset — never by hand.
/// </summary>
[DisallowMultipleComponent]
public class CameraSequenceLink : MonoBehaviour
{
    [Tooltip("The ID camera sequence presets know this object by. Written by the storyboard on Save.")]
    public string id;

    /// <summary>The object in the open scenes with this link ID, switched off or not. Null when there is none.</summary>
    public static CameraSequenceLink Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var link in FindObjectsByType<CameraSequenceLink>(FindObjectsInactive.Include))
            if (link.id == id) return link;
        return null;
    }
}
