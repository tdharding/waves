using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The world's made-up light — the one the river runs, the landscape hills and the spikes are all
/// turned against. Where this object stands IS the light's position, and its Strength is the one
/// strength all three share, so there is nowhere else for them to disagree.
///
/// Spawned from the Light (shared) foldout of the Run Shading Tuner or the Landscape Tuner, which
/// both edit this object. Nothing here pushes: <see cref="LevelSelectDesignerData.ApplyAesthetics"/>
/// calls <see cref="Push"/> every frame, alongside the rest of the world's look.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class LevelSelectLight : MonoBehaviour
{
    // The default is the landscape preset's strength when the two were merged, so a freshly
    // spawned light lights the hills exactly as they were.
    [Tooltip("How hard the river runs, the landscape and the spikes are turned against this " +
             "light. 0 leaves them flat and unlit; 1 is the full turn, darkest on faces backing " +
             "away from it; past 1 the lit result is multiplied, up to 10x brighter. There is no " +
             "falloff with distance — where the object stands says which way the light comes " +
             "from, this says how much of it there is.")]
    [Range(0f, 10f)] public float strength = 2.02f;

    private static readonly List<LevelSelectLight> Lights = new List<LevelSelectLight>();

    private static readonly int PositionId = Shader.PropertyToID("_LevelSelectLightPosition");
    private static readonly int StrengthId = Shader.PropertyToID("_LevelSelectLightStrength");

    /// <summary>The light in the open scene, or null if none has been spawned.</summary>
    public static LevelSelectLight Current => Lights.Count > 0 ? Lights[0] : null;

    private void OnEnable()
    {
        if (!Lights.Contains(this)) Lights.Add(this);
        if (Lights.Count > 1)
            Debug.LogWarning($"[LevelSelectLight] More than one light in the scene — " +
                             $"'{Lights[0].name}' wins, '{name}' is ignored.", this);
    }

    private void OnDisable()
    {
        Lights.Remove(this);
    }

    /// <summary>
    /// Publishes the light. With no light in the scene the strength goes to 0 — everything flat
    /// and unlit — rather than falling back to some remembered position, so a missing light shows.
    /// </summary>
    public static void Push()
    {
        var light = Current;
        if (light == null)
        {
            Shader.SetGlobalFloat(StrengthId, 0f);
            return;
        }

        Shader.SetGlobalVector(PositionId, light.transform.position);
        Shader.SetGlobalFloat(StrengthId, light.strength);
    }
}
