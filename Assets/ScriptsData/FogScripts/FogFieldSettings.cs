using UnityEngine;

/// <summary>
/// The fog dials that belong to the level rather than to a shape. A PrimaryFogShape says what a
/// blob looks like at rest; this says how the weather treats it.
///
/// Passed to <see cref="FogBlob.Simulate"/> by reference so a per-frame loop over every blob
/// never copies it.
/// </summary>
[System.Serializable]
public struct FogFieldSettings
{

    [Header("Pushing")]
    [Tooltip("Global multiplier on every repeller's own strength. Drop it to let fog crowd in " +
             "closer to everything at once without editing each rock.")]
    [Range(0f, 1f)] public float RepelStrength;


    public static FogFieldSettings Default => new FogFieldSettings
    {
        RepelStrength = 1f,
    };
}
