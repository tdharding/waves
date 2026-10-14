using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One sky: a colour gradient from the bottom of the sky to the top, and a list of cloud images
/// scattered around it that slowly turn about the vertical. Edited live in
/// Tools > Level Design > Skybox Generator, picked per world in the Level Select Designer's
/// Aesthetics foldout, which puts <see cref="material"/> on the scene's skybox.
///
/// The material, the gradient strip and the cloud placement data are sub-assets of this preset,
/// rewritten by the generator on every change. The cloud images are packed into one atlas PNG
/// beside the preset, rebuilt only when the cloud list changes.
/// </summary>
[CreateAssetMenu(menuName = "Waves/Skybox Preset")]
public class SkyboxPreset : ScriptableObject
{
    /// <summary>The most clouds the shader loops over — the width of the placement data.</summary>
    public const int MaxClouds = 64;

    [Tooltip("The sky's colour from the bottom (left) to the top (right). The middle is the " +
             "horizon.")]
    public Gradient skyGradient = DefaultGradient();

    [Tooltip("The cloud images. Each placed cloud picks one of these at random.")]
    public List<Texture2D> clouds = new List<Texture2D>();

    [Tooltip("How see-through the clouds are. 1 = as drawn in the images, 0 = invisible.")]
    [Range(0f, 1f)]
    public float opacity = 1f;

    [Tooltip("How many clouds are scattered around the sky.")]
    [Range(0, MaxClouds)]
    public int amount = 20;

    [Tooltip("The smallest cloud, as its width in degrees of sky.")]
    public float sizeMin = 10f;

    [Tooltip("The largest cloud, as its width in degrees of sky.")]
    public float sizeMax = 25f;

    [Tooltip("How tall a band of sky the clouds fill, in degrees.")]
    public float spread = 20f;

    [Tooltip("How far above the horizon the middle of the cloud band sits, in degrees.")]
    public float height = 15f;

    [Tooltip("Which random scatter. Change it for a different arrangement of the same settings.")]
    public int seed = 1;

    [Tooltip("How fast the clouds turn around the sky, in degrees per second. Negative turns " +
             "the other way.")]
    public float panSpeed = 1f;

    // Written by the generator — not authored.
    [HideInInspector] public Material material;
    [HideInInspector] public Texture2D gradientTexture;
    [HideInInspector] public Texture2D cloudDataTexture;
    [HideInInspector] public Texture2D cloudAtlas;
    [HideInInspector] public string cloudAtlasKey;

    /// <summary>Copies the authored settings (not the generated sub-assets) from another preset.</summary>
    public void CopySettingsFrom(SkyboxPreset other)
    {
        skyGradient = new Gradient { mode = other.skyGradient.mode };
        skyGradient.SetKeys(other.skyGradient.colorKeys, other.skyGradient.alphaKeys);
        clouds    = new List<Texture2D>(other.clouds);
        opacity   = other.opacity;
        amount    = other.amount;
        sizeMin   = other.sizeMin;
        sizeMax   = other.sizeMax;
        spread    = other.spread;
        height    = other.height;
        seed      = other.seed;
        panSpeed  = other.panSpeed;
    }

    private static Gradient DefaultGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 0f),
                new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 0.5f),
                new GradientColorKey(new Color(0.4f, 0.4f, 0.4f), 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}
