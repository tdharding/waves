using UnityEngine;

/// <summary>
/// A saved copy of the scene's <see cref="LevelSelectLight"/> — where it stands and every number
/// the Level Select Lighting window sets on it. Not the bakes: those are made from the light in
/// the scene and saved with the scene.
///
/// The scene's light is still what the world is lit by. The window's Load puts a preset's values
/// on it; Save writes the light's values back. Kept in the river presets folder with the others.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Level Select Lighting Preset")]
public class LevelSelectLightingPreset : ScriptableObject
{
    [Tooltip("Where the light stands, in world space.")]
    public Vector3 position = new Vector3(362.3f, 259.8f, -3.2f);

    [Range(0f, 10f)] public float strength = 2.02f;

    [Header("Boat Light")]
    [Min(0f)] public float boatLightRadius = 1f;
    [Min(0f)] public float boatLightSoftness = 0.5f;
    [Range(0f, 4f)] public float boatLightStrength = 0.3f;
    [Range(0f, 4f)] public float boatLightFacing = 0.3f;
    public Color boatLightColour = Color.white;

    [Header("Shadows")]
    [Range(0f, 1f)] public float shadowDarkness = 0.5f;
    [Range(0f, 8f)] public float shadowEdgeSoftness = 1f;
    public int shadowMapSize = 2048;

    [Header("River Glow")]
    [Range(0f, 10f)] public float glowStrength = 1f;
    [Range(0.5f, 20f)] public float glowReach = 3f;
    [Range(1f, 2f)] public float riverGlowResolution = 1f;

    [Header("Soul Path Glow")]
    [Range(1f, 2f)] public float soulGlowResolution = 1f;

    /// <summary>Takes every saved value off <paramref name="light"/>.</summary>
    public void CopyFrom(LevelSelectLight light)
    {
        position            = light.transform.position;
        strength            = light.strength;
        boatLightRadius     = light.boatLightRadius;
        boatLightSoftness   = light.boatLightSoftness;
        boatLightStrength   = light.boatLightStrength;
        boatLightFacing     = light.boatLightFacing;
        boatLightColour     = light.boatLightColour;
        shadowDarkness      = light.shadowDarkness;
        shadowEdgeSoftness  = light.shadowEdgeSoftness;
        shadowMapSize       = light.shadowMapSize;
        glowStrength        = light.glowStrength;
        glowReach           = light.glowReach;
        riverGlowResolution = light.riverGlowResolution;
        soulGlowResolution  = light.soulGlowResolution;
    }

    /// <summary>Puts every saved value on <paramref name="light"/>.</summary>
    public void ApplyTo(LevelSelectLight light)
    {
        light.transform.position  = position;
        light.strength            = strength;
        light.boatLightRadius     = boatLightRadius;
        light.boatLightSoftness   = boatLightSoftness;
        light.boatLightStrength   = boatLightStrength;
        light.boatLightFacing     = boatLightFacing;
        light.boatLightColour     = boatLightColour;
        light.shadowDarkness      = shadowDarkness;
        light.shadowEdgeSoftness  = shadowEdgeSoftness;
        light.shadowMapSize       = shadowMapSize;
        light.glowStrength        = glowStrength;
        light.glowReach           = glowReach;
        light.riverGlowResolution = riverGlowResolution;
        light.soulGlowResolution  = soulGlowResolution;
    }
}
