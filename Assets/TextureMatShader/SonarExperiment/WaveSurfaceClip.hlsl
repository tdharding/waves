// Wave surface clip for sonar lattice planes.
// Computes the wave height at the fragment's world XZ position and outputs a
// ClipValue that, when fed into the ShaderGraph Alpha Clip Threshold, discards
// any fragment whose world Y sits above the wave surface.
//
// WaveStepRate and WaveStrength must match the values fed into SonarWaveDistort
// so the clip undoes the vertex displacement before comparing against the surface.
// This makes the mask independent of displacement strength.
//
// Properties to expose in ShaderGraph:
//   _WaveOrigin      (Vector)  - wave plane world position (XYZ) + unused W
//   _WaveFrequency   (Float)   - wave spatial frequency
//   _WaveSpeed       (Float)   - wave animation speed
//   _WaveRipple      (Float)   - wave ripple/amplitude
//   _WaveMeshScale   (Float)   - wave plane local scale X
//   _WaveMaskBias    (Float)   - clip bias: positive = clip lower, negative = allow slightly above
//   _WaveStepRate    (Float)   - must match SonarWaveDistort WaveStepRate
//   _SonarWaveStrength (Float) - must match SonarWaveDistort WaveStrength

void WaveSurfaceClip_float(
    float3 WorldPos,
    float4 WaveOrigin,
    float  WaveFrequency,
    float  WaveSpeed,
    float  WaveRipple,
    float  WaveMeshScale,
    float  WaveMaskBias,
    float  WaveStepRate,
    float  WaveStrength,
    out float ClipValue)
{
    float safeScale = max(WaveMeshScale, 0.0001);
    float dx   = (WorldPos.x - WaveOrigin.x) / safeScale;
    float dz   = (WorldPos.z - WaveOrigin.z) / safeScale;
    float dist = sqrt(dx * dx + dz * dz);

    // Smooth wave surface height — the actual water surface.
    float waveHeight = WaveOrigin.y
                     - sin(dist * WaveFrequency - _Time.y * WaveSpeed)
                     * WaveRipple * safeScale;

    // Re-compute the stepped displacement that SonarWaveDistort applied so we
    // can recover the undisplaced Y. The clip then compares the pre-displacement
    // position against the surface, making it independent of displacement strength.
    float stepped    = floor(dist * WaveStepRate) / max(WaveStepRate, 0.0001);
    float vertDisp   = sin(stepped * WaveFrequency - _Time.y * WaveSpeed)
                     * WaveRipple * safeScale * WaveStrength;
    float undisplacedY = WorldPos.y + vertDisp;

    // Positive when fragment is below or at the surface: fragment survives clip.
    // Negative when fragment is above the surface: fragment is discarded.
    ClipValue = waveHeight - undisplacedY + WaveMaskBias;
}

