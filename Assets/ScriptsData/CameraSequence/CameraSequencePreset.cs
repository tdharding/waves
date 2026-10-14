using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Everything a camera sequence is, saved as an asset: every shot with its length, blend, dialogue,
/// audio and animation, plus the sequence's audio, end blend, fog and Play Once.
///
/// Shot cameras and orbit points stay in the scene. The preset links to them by the ID on their
/// <see cref="CameraSequenceLink"/>, and keeps a full copy of each one (every component on it) so the
/// storyboard's Load can put them back — or build them again — if anything goes wrong.
///
/// The storyboard (Tools/Camera/Camera Sequence) edits the scene's <see cref="CameraSequence"/> and
/// saves it here; the scene's sequence plays a preset by loading it (<see cref="CameraSequence.PlayPreset"/>).
/// </summary>
[CreateAssetMenu(menuName = "Waves/Camera Sequence Preset", fileName = "CameraSequencePreset")]
public class CameraSequencePreset : ScriptableObject
{
    [Serializable]
    public class PresetShot
    {
        [Tooltip("Link ID of the shot's camera in the scene.")]
        public string cameraLink;
        [Tooltip("Link ID of the shot's orbit point in the scene. Empty when it has none.")]
        public string orbitPointLink;
        [Tooltip("Link ID of the shot's Animate 2 Shot end camera in the scene. Empty when it has none.")]
        public string endCameraLink;
        [Tooltip("Link IDs of the characters in the shot's Character Animations, in the same order. Characters are " +
                 "only linked — never stored or rebuilt; they belong to the scene, not the sequence.")]
        public List<string> characterLinks = new List<string>();
        [Tooltip("The shot's settings. Its Camera, Orbit Point, End Camera and characters are always empty here — they come from the links.")]
        public CameraSequence.Shot shot = new CameraSequence.Shot();
    }

    /// <summary>A scene object the preset links to, as it was when the preset was saved.</summary>
    [Serializable]
    public class StoredObject
    {
        public string     linkId;
        public string     name;
        [Tooltip("Hierarchy path of its parent when saved, from the scene root. Empty = at the root.")]
        public string     parentPath;
        public string     tag;
        public int        layer;
        public bool       active = true;
        public Vector3    position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3    localScale = Vector3.one;
        public List<StoredComponent> components = new List<StoredComponent>();
    }

    /// <summary>One component on a stored object, every serialized field of it.</summary>
    [Serializable]
    public class StoredComponent
    {
        public string type;
        [TextArea] public string json;
        [Tooltip("The component's references to other objects, by stable ID — the JSON only knows them for the editor session it was saved in.")]
        public List<StoredReference> references = new List<StoredReference>();
    }

    [Serializable]
    public class StoredReference
    {
        public string propertyPath;
        public string globalId;
        [Tooltip("Link ID of the object referenced, when it is one of this preset's linked objects.")]
        public string linkId;
    }

    public List<PresetShot> shots = new List<PresetShot>();

    public bool useAudio = true;
    public AudioClip sequenceAudio;
    [Min(0f)] public float audioFadeOut = 4f;
    public CinemachineBlendDefinition endBlend =
        new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 2f);

    public bool overrideFog;
    public WhiteFadeSettings fog = new WhiteFadeSettings();

    [Tooltip("On: plays once per save — after that it never plays again until New Game. Off: plays every time it is triggered.")]
    public bool playOnce = true;

    [Tooltip("The linked cameras and orbit points as they were when saved. The storyboard's Load puts them back from here.")]
    public List<StoredObject> storedObjects = new List<StoredObject>();

    /// <summary>
    /// Fills <paramref name="sequence"/> with this preset, finding each shot's camera and orbit point
    /// in the scene by its link. A link with nothing in the scene leaves that field empty (the shot
    /// is skipped when it plays, with a warning).
    /// </summary>
    public void ApplyTo(CameraSequence sequence)
    {
        sequence.shots = new List<CameraSequence.Shot>();
        foreach (var ps in shots)
        {
            var shot = CameraSequence.CopyShot(ps.shot);
            var camLink   = CameraSequenceLink.Find(ps.cameraLink);
            var pointLink = CameraSequenceLink.Find(ps.orbitPointLink);
            var endLink   = CameraSequenceLink.Find(ps.endCameraLink);
            shot.camera     = camLink   != null ? camLink.GetComponent<CinemachineCamera>() : null;
            shot.orbitPoint = pointLink != null ? pointLink.transform : null;
            shot.endCamera  = endLink   != null ? endLink.GetComponent<CinemachineCamera>() : null;
            for (int c = 0; c < shot.characterAnimations.Count; c++)
            {
                string id = c < ps.characterLinks.Count ? ps.characterLinks[c] : null;
                var charLink = CameraSequenceLink.Find(id);
                shot.characterAnimations[c].character = charLink != null ? charLink.GetComponent<SequenceCharacter>() : null;
                if (!string.IsNullOrEmpty(id) && shot.characterAnimations[c].character == null)
                    Debug.LogWarning($"[CameraSequencePreset] '{name}': a shot's character (link {id}) is not in the scene.");
            }
            if (!string.IsNullOrEmpty(ps.cameraLink) && shot.camera == null)
                Debug.LogWarning($"[CameraSequencePreset] '{name}': a shot's camera (link {ps.cameraLink}) is not in the scene.");
            sequence.shots.Add(shot);
        }

        sequence.useAudio      = useAudio;
        sequence.sequenceAudio = sequenceAudio;
        sequence.audioFadeOut  = audioFadeOut;
        sequence.endBlend      = CameraSequence.DeepCopy(endBlend);
        sequence.overrideFog   = overrideFog;
        sequence.fog           = CameraSequence.DeepCopy(fog);
        sequence.playOnce      = playOnce;
    }
}
