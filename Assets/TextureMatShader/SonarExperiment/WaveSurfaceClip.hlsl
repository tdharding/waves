// Wave surface clip for sonar lattice planes.
// Computes the wave height at the fragment's world XZ position and outputs a
// ClipValue that, when fed into the ShaderGraph Alpha Clip Threshold, discards
// any fragment whose world Y sits above the wave surface.
//
// Properties to expose in ShaderGraph:
//   _WaveOrigin      (Vector)  - wave plane world position (XYZ) + unused W
//   _WaveFrequency   (Float)   - wave spatial frequency
//   _WaveSpeed       (Float)   - wave animation speed
//   _WaveRipple      (Float)   - wave ripple/amplitude
//   _WaveMeshScale   (Float)   - wave plane local scale X
//   _WaveMaskBias    (Float)   - clip bias: positive = clip lower, negative = allow slightly above

void WaveSurfaceClip_float(
    float3 WorldPos,
    float4 WaveOrigin,
    float  WaveFrequency,
    float  WaveSpeed,
    float  WaveRipple,
    float  WaveMeshScale,
    float  WaveMaskBias,
    out float ClipValue)
{
    float dx   = (WorldPos.x - WaveOrigin.x) / WaveMeshScale;
    float dz   = (WorldPos.z - WaveOrigin.z) / WaveMeshScale;
    float dist = sqrt(dx * dx + dz * dz);

    float waveHeight = WaveOrigin.y
                     - sin(dist * WaveFrequency - _Time.y * WaveSpeed)
                     * WaveRipple * WaveMeshScale;

    // Positive when fragment is below or at the surface: fragment survives clip.
    // Negative when fragment is above the surface: fragment is discarded.
    ClipValue = waveHeight - WorldPos.y + WaveMaskBias;
}

