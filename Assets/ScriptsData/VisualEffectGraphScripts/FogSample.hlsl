// Reads the fog field at a world position. Every other fog function starts here.
//
// FogFieldManager pushes both textures and the world mapping as bare $Globals every frame, so a
// shader reimport that wipes them self-heals on the next frame — same reasoning as the soul-fish
// masks and the rock rings.
//
// Two textures rather than one, at deliberately different resolutions:
//   _FogField   R = density, G = blob id PREMULTIPLIED by density
//   _FogHeight  R = sphere-cap height
//
// Blob id has to be recovered as G/R because the paint pass is additive: summing raw ids where
// two blobs overlap would give nonsense, whereas summing density-weighted ids and dividing gives
// a weighted average that blends sensibly exactly where two masses fuse.

#ifndef FOG_SAMPLE_INCLUDED
#define FOG_SAMPLE_INCLUDED

TEXTURE2D(_FogField);    SAMPLER(sampler_FogField);
TEXTURE2D(_FogHeight);   SAMPLER(sampler_FogHeight);

float4 _FogFieldOrigin;   // xy = world min corner, z = 1/size, w = size

// World XZ to field UV. Outside 0..1 there is simply no fog — the field is a window on the water
// near the boat, not the whole level.
float2 FogFieldUV(float3 worldPos)
{
    return (worldPos.xz - _FogFieldOrigin.xy) * _FogFieldOrigin.z;
}

void FogSample_float(
    float3 WorldPos,
    out float Density,
    out float BlobId,
    out float Height,
    out float2 FieldUV,
    out float InField)
{
    float2 uv = FogFieldUV(WorldPos);
    FieldUV = uv;

    // Hard cut at the field edge rather than clamping, or the outermost texels would smear out
    // across the rest of the level as a stripe.
    InField = (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0) ? 1.0 : 0.0;

    float4 f = SAMPLE_TEXTURE2D(_FogField, sampler_FogField, uv);
    Density = f.r * InField;
    BlobId  = f.g / max(f.r, 1e-4);
    Height  = SAMPLE_TEXTURE2D(_FogHeight, sampler_FogHeight, uv).r * InField;
}

void FogSample_half(
    half3 WorldPos,
    out half Density,
    out half BlobId,
    out half Height,
    out half2 FieldUV,
    out half InField)
{
    float d, b, h, i; float2 uv;
    FogSample_float(WorldPos, d, b, h, uv, i);
    Density = (half)d; BlobId = (half)b; Height = (half)h;
    FieldUV = (half2)uv; InField = (half)i;
}

#endif
