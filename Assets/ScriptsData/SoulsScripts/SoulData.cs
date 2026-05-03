using UnityEngine;
using UnityEngine.Video;

[CreateAssetMenu(fileName = "NewSoulData", menuName = "Souls/Soul Data")]
public class SoulData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("The unique ID for this soul (1-100). Matches the video and collection slot.")]
    public int soulDataIdentity;

    [Header("Fish Prefab")]
    [Tooltip("The soul fish prefab spawned for this soul. Set per soul identity.")]
    public GameObject fishPrefab;

    [Header("Level Binding")]
    [Tooltip("Stamped at runtime when the soul fish spawns. The levelID of the level this soul belongs to. " +
             "Used to return dropped souls to their home level on exit.")]
    public string homeLevelID;

    [Tooltip("The wave preset associated with this soul. Baked by the Randomise Soul Wave Presets tool; " +
             "overridden to the level's gameplayWavePreset when assigned in the Grid Designer.")]
    public WavePreset associatedWavePreset;

    [Header("Allocation — set by Grid Designer, do not edit manually")]
    [Tooltip("True if this soul is assigned to a spawn point in the Grid Designer.")]
    public bool allocated;

    [Tooltip("The levelID of the GridData asset this soul is assigned to.")]
    public string allocatedToLevelID;

    [Header("Content")]
    public string soulName;
    public VideoClip videoClip;
}