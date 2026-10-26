using UnityEngine;

/// <summary>
/// The interaction a rim node screen carries: press the key and its poster, from the map's
/// <see cref="LevelSelectPosterLibrary"/>, fades in on the screen itself while the view cuts to the
/// screen's camera, if it has one. The key again takes the picture down and cuts straight back.
///
/// The picture is a quad of its own just in front of the screen's glow, hidden until the key is
/// pressed; the art is put onto it here, through a property block, so the material is shared and
/// left untouched. The fade runs on unscaled time — the map's clock is stopped while it is up.
///
/// The name is kept rather than the art, as a poster display does — see
/// <see cref="LevelSelectPosterInteract"/>. Every field is written by the Level Select Designer
/// when the screen is placed.
/// </summary>
public class LevelSelectScreenInteract : LevelSelectInteractAction
{
    [Tooltip("The map's poster library. Written by the designer when this screen is placed.")]
    public LevelSelectPosterLibrary library;

    [Tooltip("Which poster in the library this screen shows, by name.")]
    public string posterName;

    [Tooltip("The quad the picture is shown on, just in front of the screen.")]
    public Renderer picture;

    [Tooltip("How long the picture takes to fade in, in seconds.")]
    [Min(0f)] public float fadeTime = 0.5f;

    [Tooltip("The camera the view cuts to while the picture is up. None: the view stays where it is.")]
    public LevelSelectScreenCamera screenCamera;

    private static readonly int BaseMapId   = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private MaterialPropertyBlock _block;
    private float                 _shown;   // how far faded in, 0–1

    /// <summary>The art this screen would show, or null when the name finds nothing.</summary>
    public Sprite Poster => library != null ? library.Find(posterName) : null;

    private void Awake()
    {
        if (picture != null) picture.enabled = false;
    }

    public override bool Begin()
    {
        var poster = Poster;
        if (poster == null)
        {
            Debug.LogWarning($"[LevelSelectScreenInteract] '{name}' has no poster to show — " +
                             $"library {(library == null ? "not set" : library.name)}, " +
                             $"name '{posterName}'.", this);
            return false;
        }

        if (picture == null)
        {
            Debug.LogWarning($"[LevelSelectScreenInteract] '{name}' has no picture quad — " +
                             "Generate the river again.", this);
            return false;
        }

        // The sprite's own part of its texture, so a packed or cut-out sprite shows just itself.
        var  texture = poster.texture;
        Rect rect    = poster.textureRect;

        _block ??= new MaterialPropertyBlock();
        picture.GetPropertyBlock(_block);
        _block.SetTexture(BaseMapId, texture);
        _block.SetVector(BaseMapStId, new Vector4(rect.width / texture.width, rect.height / texture.height,
                                                  rect.x / texture.width,     rect.y / texture.height));

        _shown = fadeTime > 0f ? 0f : 1f;
        ApplyFade();
        picture.enabled = true;

        if (screenCamera != null && screenCamera.isActiveAndEnabled) screenCamera.Look(transform);
        return true;
    }

    // One press put it up, the next takes it down; in between, the picture fades in.
    public override bool Step(bool advancePressed)
    {
        if (advancePressed) return false;

        if (_shown < 1f)
        {
            _shown = fadeTime > 0f ? Mathf.MoveTowards(_shown, 1f, Time.unscaledDeltaTime / fadeTime) : 1f;
            ApplyFade();
        }
        return true;
    }

    // Down at once, and the view straight back.
    public override void End()
    {
        if (picture != null) picture.enabled = false;
        if (screenCamera != null) screenCamera.Release();
    }

    private void OnDisable() => End();

    private void ApplyFade()
    {
        _block.SetColor(BaseColorId, new Color(1f, 1f, 1f, _shown));
        picture.SetPropertyBlock(_block);
    }
}
