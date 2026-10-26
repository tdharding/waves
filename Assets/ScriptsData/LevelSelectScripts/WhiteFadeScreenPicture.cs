using UnityEngine;

/// <summary>
/// What the white fade fades to — once plain white, now a picture pinned to the screen: a ramp
/// read from the bottom of the screen to the top, its colour what the fade goes to and its alpha
/// how strongly it goes there, with white added by a grain of gradient noise that scrolls
/// sideways — the grain only ever lightens. The ground moves through it as the camera pans. The
/// fade from the boat goes to it, and the gradient rising from the bottom adds it.
///
/// Part of the world's <see cref="LevelSelectWhiteFadePreset"/>, beside the ring numbers, and
/// tuned in the same White Fade foldout of the Run Shading or Landscape Tuner. It is the world's
/// alone: a Camera Sequence's fog overrides the rings, never the picture.
///
/// Bare $Globals read by WhiteFade.hlsl — the ramp as a small texture, the grain as one vector —
/// pushed every frame by <see cref="LevelSelectDesignerData.ApplyAesthetics"/>. Anything else
/// wearing the node gets an all white ramp, the old plain white fade, from the moment it loads.
/// </summary>
[System.Serializable]
public class WhiteFadeScreenPicture
{
    [Tooltip("The picture, read from the bottom of the screen (left) to the top (right). Colour " +
             "is what the fade goes to at that height; alpha how strongly it goes there. All " +
             "white is the old plain white fade.")]
    public Gradient ramp = White();

    [Tooltip("How big the grain is, as a share of the screen's height.")]
    [Min(0.001f)] public float grainSize = 0.05f;

    [Tooltip("How much white the grain adds to the picture at its brightest. It only ever " +
             "lightens. 0 = a clean ramp.")]
    [Min(0f)] public float grainAmount = 0.05f;

    [Tooltip("How fast the grain scrolls sideways, in screen heights a second. Minus goes left.")]
    public float scrollSpeed = 0.02f;

    /// <summary>The default ramp: all white, all the way up — the old plain white fade.</summary>
    public static Gradient White()
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    // ─────────────────────────────────────────────────────────────
    // PUSHING
    // ─────────────────────────────────────────────────────────────

    /// <summary>What a world with no white fade preset gets: plain white, no grain.</summary>
    private static readonly WhiteFadeScreenPicture None =
        new WhiteFadeScreenPicture { grainAmount = 0f, scrollSpeed = 0f };

    private static readonly int RampId  = Shader.PropertyToID("_WhiteFadeRamp");
    private static readonly int GrainId = Shader.PropertyToID("_WhiteFadeGrain");

    /// <summary>How many steps up the ramp texture.</summary>
    private const int RampWidth = 256;

    // The ramp as the shader reads it, rebuilt only when the gradient changes. Made here rather
    // than saved: it is nothing but what the gradient already says.
    private static Texture2D _rampTexture;
    private static int       _rampHash;

    /// <summary>This world's picture, or plain white without a preset.</summary>
    public static void Push(WhiteFadeScreenPicture picture)
    {
        (picture ?? None).Apply();
    }

    // The node is also worn outside the level select, where nothing calls ApplyAesthetics — give
    // it the plain white ramp from the start, so it reads what it always did rather than an
    // unset texture.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void PushDefault() => None.Apply();

    private void Apply()
    {
        Shader.SetGlobalTexture(RampId, RampTexture());
        Shader.SetGlobalVector(GrainId, new Vector4(grainSize, grainAmount, scrollSpeed, 0f));
    }

    /// <summary>
    /// The ramp, bottom of the screen at the left. sRGB like the colours picked in it, clamped so
    /// neither end wraps round into the other.
    /// </summary>
    private Texture2D RampTexture()
    {
        var g = ramp ?? White();

        int hash = 17 * 31 + (int)g.mode;
        foreach (var key in g.colorKeys)
            hash = hash * 31 + key.color.GetHashCode() * 7 + key.time.GetHashCode();
        foreach (var key in g.alphaKeys)
            hash = hash * 31 + key.alpha.GetHashCode() * 7 + key.time.GetHashCode();

        if (_rampTexture != null && hash == _rampHash) return _rampTexture;

        if (_rampTexture == null)
        {
            _rampTexture = new Texture2D(RampWidth, 1, TextureFormat.RGBA32, false, false)
            {
                name       = "WhiteFadeRamp",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags  = HideFlags.HideAndDontSave,
            };
        }

        var pixels = new Color32[RampWidth];
        for (int x = 0; x < RampWidth; x++)
            pixels[x] = g.Evaluate(x / (float)(RampWidth - 1));

        _rampTexture.SetPixels32(pixels);
        _rampTexture.Apply(false, false);
        _rampHash = hash;
        return _rampTexture;
    }
}
