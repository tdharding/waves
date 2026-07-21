// WindowTiling.hlsl
// Custom Function node name: WindowTiling
// ─────────────────────────────────────────────────────────────────────────────
// Typographic "window" tiling for procedural cube buildings.
//
// Each side face of a ProceduralBoxMesh carries UV2 (channel UV1 in Shader Graph):
//   xy = face-local coords in WORLD units (x from the face's left edge as seen
//        from outside, y from the waterline), zw = (face width, height above water).
// Top/bottom faces carry zeros (w = 0) and are skipped.
//
// The function fits whole window cells to the face like typesetting (never
// stretched: as many glyphs as fit at CellSize plus Spacing between them,
// leftover centred as margins; rows anchored to the waterline by default).
// Each cell hash-picks a variant column
// from an atlas whose ROWS are roles in facade order (PNG top → bottom):
//   0 TL corner · 1 top edge · 2 TR corner · 3 left edge · 4 interior
//   5 right edge · 6 BL corner · 7 bottom/waterline row · 8 BR corner
// Blank atlas tiles simply show bare rock (natural density variation).
//
// Windows light INDIVIDUALLY from proximity to soul fish: world-space light
// points published by WindowLightManager.cs (live fish + boat weighted by souls
// on board). Per-window hash jitters the response threshold and flicker phase.
//
// Inputs (Custom Function node, in order)
//   Atlas             Texture2D     — _WindowAtlas
//   PointSampler      SamplerState  — Sampler State node (Point, Clamp)
//   UV2               Vector4       — UV node, Channel = UV1
//   PositionWS        Vector3       — Position node (World)
//   NormalWS          Vector3       — Normal Vector node (World)
//   Time              Float         — Time node (Time output)
//   CellSize          Float         — _WindowCellSize (world size of the glyph itself)
//   AtlasGrid         Vector2       — _WindowAtlasGrid (variant columns, role rows) = (8, 9)
//   EmissionColor     Vector4/Color — _WindowEmissionColor (HDR, rgb used)
//   EmissionIntensity Float         — _WindowEmissionIntensity
//   LightParams       Vector4       — _WindowLightParams:  x height falloff — world height above
//                                     the waterline at which windows stop responding (0 = off;
//                                     the light RADIUS lives on WindowLightManager as the
//                                     _WindowLightRadius global), y falloff exp,
//                                     z lit threshold, w threshold jitter (0..1)
//   FlickerParams     Vector4       — _WindowFlickerParams: x flicker amount, y flicker speed,
//                                     z BASELINE lightness every window sits at with no fish
//                                     nearby (0 = dark, ~0.15 = faintly visible),
//                                     w per-window variation of that baseline (0..1)
//   StyleParams       Vector4       — _WindowStyleParams:  x unlit darken, y anchor-bottom (0/1),
//                                     z debug mode (0 off / 1 checkerboard / 2 role tint),
//                                     w density (0..1)
//   Spacing           Vector2       — _WindowSpacing: world-unit gap between windows
//                                     (x horizontal, y vertical). 0 = glyphs touch, as before.
//                                     Glyphs never shrink — only the gaps grow, so raising
//                                     this fits fewer windows per face.
// Outputs
//   Emission          Vector3       — ADD into the fragment's Emission
//   AlbedoMul         Float         — MULTIPLY into Base Color (dark recessed panes when unlit)
// ─────────────────────────────────────────────────────────────────────────────

#ifndef WINDOW_TILING_INCLUDED
#define WINDOW_TILING_INCLUDED

// Must match WindowLightManager.MaxLights. SetGlobalVectorArray locks the array
// length on first publish — never change one without the other.
#define WINDOW_MAX_LIGHTS 16
float4 _WindowFishPoints[WINDOW_MAX_LIGHTS]; // xyz = world position, w = intensity weight
float  _WindowFishCount;

// Light radius in world units. A bare $Globals uniform of the same class as _SoulFishRadius
// in SoulFishWaveMask.hlsl, published every frame by WindowLightManager through BOTH routes
// (material.SetFloat + Shader.SetGlobalFloat) — see SetGlobalsBackedFloat there and in
// WaveMaterialController. Deliberately has NO material-property fallback: a silent fallback
// would mask a failed global and leave the component's slider looking inert, which is the
// exact confusion the soul-fish edge-noise bug produced.
float _WindowLightRadius;

float WindowTiling_Hash(float3 q, float3 k)
{
    return frac(sin(dot(q, k)) * 43758.5453);
}

void WindowTiling_float(
    UnityTexture2D    Atlas,
    UnitySamplerState PointSampler,
    float4 UV2,
    float3 PositionWS,
    float3 NormalWS,
    float  Time,
    float  CellSize,
    float2 AtlasGrid,
    float4 EmissionColor,
    float  EmissionIntensity,
    float4 LightParams,
    float4 FlickerParams,
    float4 StyleParams,
    float2 Spacing,
    out float3 Emission,
    out float  AlbedoMul)
{
    Emission  = float3(0.0, 0.0, 0.0);
    AlbedoMul = 1.0;

    // Top/bottom faces (zeroed UV2) and anything below the waterline: no windows.
    float faceW = UV2.z;
    float faceH = UV2.w;
    if (faceH <= 0.0001 || UV2.y < 0.0) return;

    // ── Typographic fit: whole glyphs, gaps between them ─────────────────────
    // Glyphs stay CellSize across regardless of Spacing (like letter-spacing:
    // the letters don't stretch, the gaps between them grow). n glyphs need
    // n*glyph + (n-1)*gap — no trailing gap outside the outermost windows.
    float  glyph = max(CellSize, 0.05);
    float2 gap   = max(Spacing, 0.0);
    float2 pitch = glyph + gap;

    float cols = floor((faceW + gap.x) / pitch.x);
    float rows = floor((faceH + gap.y) / pitch.y);
    if (cols < 1.0 || rows < 1.0) return;   // face too small for even one window

    float usedW = cols * pitch.x - gap.x;
    float usedH = rows * pitch.y - gap.y;

    float marginX = (faceW - usedW) * 0.5;                  // centred horizontally
    float yStart  = StyleParams.y >= 0.5 ? 0.0              // anchored to waterline
                                         : (faceH - usedH) * 0.5; // or centred vertically

    float x = UV2.x - marginX;
    float y = UV2.y - yStart;
    if (x < 0.0 || x >= usedW) return;   // in a horizontal margin
    if (y < 0.0 || y >= usedH) return;   // below band / above last row

    float col = floor(x / pitch.x);
    float row = floor(y / pitch.y);

    // Position within this cell's pitch; past the glyph width we're in the gap.
    float2 local = float2(x - col * pitch.x, y - row * pitch.y);
    if (local.x >= glyph || local.y >= glyph) return;

    float2 inCell = clamp(local / glyph, 0.001, 0.999);

    // ── Role classification (corners > bottom > top > left > right > interior)
    // Degenerate rules: a 1-column face has no left/right roles; a 1-row face is
    // all bottom row (waterline wins over top).
    bool isL = (col == 0.0)        && (cols > 1.0);
    bool isR = (col == cols - 1.0) && (cols > 1.0);
    bool isB = (row == 0.0);
    bool isT = (row == rows - 1.0) && (rows > 1.0);

    float pngRow = 4.0;                       // interior
    if      (isT && isL) pngRow = 0.0;        // TL corner
    else if (isT && isR) pngRow = 2.0;        // TR corner
    else if (isB && isL) pngRow = 6.0;        // BL corner
    else if (isB && isR) pngRow = 8.0;        // BR corner
    else if (isB)        pngRow = 7.0;        // bottom / waterline row
    else if (isT)        pngRow = 1.0;        // top edge
    else if (isL)        pngRow = 3.0;        // left edge
    else if (isR)        pngRow = 5.0;        // right edge

    // ── Window center in world space (fish distance + per-window seed) ───────
    // cross(normal, up) is the direction of increasing UV2.x on all four side
    // faces (verified against ProceduralBoxMesh's a=BL,b=BR,c=TR,d=TL winding),
    // and stays correct under the spawnParent's Y rotation.
    float3 up        = float3(0.0, 1.0, 0.0);
    float3 tangentWS = normalize(cross(NormalWS, up));
    float  cellCenterU = marginX + col * pitch.x + glyph * 0.5;
    float  cellCenterV = yStart  + row * pitch.y + glyph * 0.5;
    float3 centerWS = PositionWS
                    + tangentWS * (cellCenterU - UV2.x)
                    + up        * (cellCenterV - UV2.y);

    // Per-window hashes from the quantized center (stable: blocks never move).
    float3 q  = round(centerWS * 8.0) / 8.0;
    float  h1 = WindowTiling_Hash(q, float3(127.1, 311.7,  74.7)); // variant column
    float  h2 = WindowTiling_Hash(q, float3(269.5, 183.3, 246.1)); // threshold jitter
    float  h3 = WindowTiling_Hash(q, float3(113.5, 271.9, 124.6)); // flicker phase/rate
    float  h4 = WindowTiling_Hash(q, float3(419.2, 371.9,  97.3)); // density + ambient pick

    // ── Debug modes (before density cull so every cell shows) ────────────────
    if (StyleParams.z >= 0.5 && StyleParams.z < 1.5)
    {
        // Checkerboard: validates cell fit, margins and the waterline anchor.
        float shade = fmod(col + row, 2.0) < 0.5 ? 0.5 : 1.0;
        Emission = float3(shade, shade, shade);
        return;
    }
    if (StyleParams.z >= 1.5)
    {
        // Role tint: corners warm/pink family, top green, bottom yellow,
        // left blue, right cyan, interior grey.
        float3 tint = float3(0.5, 0.5, 0.5);
        if      (pngRow == 0.0) tint = float3(1.0, 0.0, 0.0);
        else if (pngRow == 2.0) tint = float3(1.0, 0.0, 1.0);
        else if (pngRow == 6.0) tint = float3(1.0, 0.4, 0.0);
        else if (pngRow == 8.0) tint = float3(0.6, 0.0, 1.0);
        else if (pngRow == 7.0) tint = float3(1.0, 1.0, 0.0);
        else if (pngRow == 1.0) tint = float3(0.0, 1.0, 0.0);
        else if (pngRow == 3.0) tint = float3(0.0, 0.2, 1.0);
        else if (pngRow == 5.0) tint = float3(0.0, 1.0, 1.0);
        Emission = tint;
        return;
    }

    // ── Density cull: some cells simply have no window ───────────────────────
    if (h4 > StyleParams.w) return;

    // ── Atlas sample ─────────────────────────────────────────────────────────
    float2 grid    = max(AtlasGrid, 1.0);
    float  variant = min(floor(h1 * grid.x), grid.x - 1.0);
    float  vRow    = (grid.y - 1.0) - pngRow;   // PNG rows count from the top; UV v from the bottom
    float2 atlasUV = float2((variant + inCell.x) / grid.x,
                            (vRow    + inCell.y) / grid.y);
    float  mask = SAMPLE_TEXTURE2D_LOD(Atlas.tex, PointSampler.samplerstate, atlasUV, 0.0).r;
    if (mask <= 0.001) return;                  // blank tile / empty pixel

    // ── Lit factor from fish/boat proximity ──────────────────────────────────
    // Radius is the global (WindowLightManager), NOT LightParams.x — single source of truth.
    float radius  = max(_WindowLightRadius, 0.001);
    float falloff = max(LightParams.y, 0.001);
    // True 3D distance from each light's real world position, so the radius governs how far
    // light CLIMBS a facade as well as how far it spreads: a window 8 units up drops out of
    // the pool just like one 8 units along. Fish sit below the water, so the storeys nearest
    // the waterline respond first. (WindowLightManager clamps published lights to the water
    // surface — fish beyond activeDistance of the boat are still up on their spawn spline,
    // which is authored ABOVE the water, and those were lighting the top storeys.)
    //
    // LightParams.x scales the vertical component only, to shape the pool:
    //   1 = a true sphere · >1 flattens it so light hugs the water · <1 lets it climb higher.
    float vScale = LightParams.x > 0.0 ? LightParams.x : 1.0;

    float signal = 0.0;
    int count = clamp((int)_WindowFishCount, 0, WINDOW_MAX_LIGHTS);
    for (int i = 0; i < count; i++)
    {
        float4 P   = _WindowFishPoints[i];
        float  dXZ = distance(P.xz, centerWS.xz);
        float  dY  = (centerWS.y - P.y) * vScale;
        float  d   = sqrt(dXZ * dXZ + dY * dY);
        signal += P.w * pow(saturate(1.0 - d / radius), falloff);
    }

    // Per-window jittered threshold so neighbours don't all switch at once.
    float thresh = max(LightParams.z, 0.001) * lerp(1.0 - LightParams.w, 1.0 + LightParams.w, h2);
    float lit    = smoothstep(thresh * 0.7, thresh * 1.3, signal);

    // Subtle per-window flicker (phase and rate both hash-varied).
    lit *= 1.0 + FlickerParams.x * sin(Time * FlickerParams.y * (0.7 + 0.6 * h3) + h3 * 6.2831);

    // Baseline lightness: every window sits at this fraction of full emission even with no
    // fish nearby, so the window grid stays readable; fish proximity then drives it up to
    // full, and the gap between the two IS the glow. FlickerParams.w varies the baseline
    // per window so the unlit facade isn't a uniform dim grid.
    float h5       = frac(h4 * 7.13 + 0.37);
    float baseline = FlickerParams.z * lerp(1.0 - FlickerParams.w, 1.0 + FlickerParams.w, h5);
    lit = saturate(max(lit, baseline));

    // ── Outputs ──────────────────────────────────────────────────────────────
    Emission  = EmissionColor.rgb * EmissionIntensity * mask * lit;
    AlbedoMul = 1.0 - mask * StyleParams.x * (1.0 - lit);   // unlit panes read as dark recesses
}

void WindowTiling_half(
    UnityTexture2D    Atlas,
    UnitySamplerState PointSampler,
    half4 UV2,
    half3 PositionWS,
    half3 NormalWS,
    half  Time,
    half  CellSize,
    half2 AtlasGrid,
    half4 EmissionColor,
    half  EmissionIntensity,
    half4 LightParams,
    half4 FlickerParams,
    half4 StyleParams,
    half2 Spacing,
    out half3 Emission,
    out half  AlbedoMul)
{
    float3 e; float a;
    WindowTiling_float(Atlas, PointSampler,
        (float4)UV2, (float3)PositionWS, (float3)NormalWS, (float)Time,
        (float)CellSize, (float2)AtlasGrid, (float4)EmissionColor, (float)EmissionIntensity,
        (float4)LightParams, (float4)FlickerParams, (float4)StyleParams, (float2)Spacing, e, a);
    Emission  = (half3)e;
    AlbedoMul = (half)a;
}

#endif
