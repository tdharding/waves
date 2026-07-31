// Custom Function node for SuckingVFX ShaderGraph.
// Scrolls a greyscale wavy-line texture up the trunk, fades it at both ends,
// and applies a total-alpha multiplier driven by SecondWhirlChain.cs.

void SuckingVFX_float(
    float2            UV,
    UnityTexture2D    WavyLineTex,
    UnitySamplerState Sampler,
    float             ScrollSpeed,
    float             TilingX,
    float             TilingY,
    float             MouthFadeLength,
    float             TailFadeLength,
    float             TotalAlpha,
    out float         FinalAlpha
)
{
    // Tile the UV and scroll upward along Y over time
    float2 tiledUV  = float2(UV.x * TilingX, UV.y * TilingY + _Time.y * ScrollSpeed);

    // Sample red channel — white areas opaque, dark lines transparent
    float texMask   = SAMPLE_TEXTURE2D(WavyLineTex.tex, Sampler.samplerstate, tiledUV).r;

    // Fade to transparent at the mouth (UV.y == 0), fully opaque past MouthFadeLength
    float s = smoothstep(0.0, MouthFadeLength, UV.y);
    float mouthFade = s * s * s;

    // Mirror fade at the far end (UV.y == 1), opaque before TailFadeLength from that end
    float t = smoothstep(0.0, TailFadeLength, 1.0 - UV.y);
    float tailFade = t * t * t;

    FinalAlpha = texMask * mouthFade * tailFade * TotalAlpha;
}

void SuckingVFX_half(
    half2             UV,
    UnityTexture2D    WavyLineTex,
    UnitySamplerState Sampler,
    half              ScrollSpeed,
    half              TilingX,
    half              TilingY,
    half              MouthFadeLength,
    half              TailFadeLength,
    half              TotalAlpha,
    out half          FinalAlpha
)
{
    half2 tiledUV  = half2(UV.x * TilingX, UV.y * TilingY + _Time.y * ScrollSpeed);
    half texMask   = SAMPLE_TEXTURE2D(WavyLineTex.tex, Sampler.samplerstate, tiledUV).r;
    half sh = smoothstep(0.0h, MouthFadeLength, UV.y);
    half mouthFade = sh * sh * sh;

    half th = smoothstep(0.0h, TailFadeLength, 1.0h - UV.y);
    half tailFade = th * th * th;

    FinalAlpha = texMask * mouthFade * tailFade * TotalAlpha;
}
