// WindowTiling.hlsl
// Custom Function node name: WindowTiling
// ─────────────────────────────────────────────────────────────────────────────
// Free-form "snake" window field for procedural cube buildings.
//
// A window is any connected run of cells drawn into a FIELD texture (dots, lines,
// corners, L/U shapes — "Snake for windows"). The field is baked either from a
// hand-drawn sheet or by WindowFieldGenerator, and carries two channels:
//   R = mask  (1 where a window cell is, 0 = bare wall)
//   G = id    (a per-window random value, identical across every cell of one
//              window, so a multi-cell window flickers / lights as ONE unit)
// The field tiles across each face at a fixed world cell size; the shader never
// needs to understand connectivity — the bake already did (flood-fill / the
// generator assigns the shared id).
//
// Each side face of a ProceduralBoxMesh carries UV2 (channel UV1 in Shader Graph):
//   xy = face-local coords in WORLD units (x from the face's left edge as seen
//        from outside, y from the waterline), zw = (face width, height above water).
// Top/bottom faces carry zeros (w = 0) and are skipped.
//
// Windows light INDIVIDUALLY from proximity to soul fish: world-space light
// points published by WindowLightManager.cs (live fish + boat weighted by souls
// on board), reach set by the _WindowLightRadius global. The baked id jitters each
// window's threshold, flicker phase and baseline so they don't move in lockstep.
//
// Inputs (Custom Function node — SAME signature as the old glyph version, so no
// node rewiring is needed; only the meaning of a few inputs changed, noted below)
//   Atlas             Texture2D     — _WindowAtlas: now the FIELD (R = mask, G = id)
//   PointSampler      SamplerState  — Sampler State node (Point, Repeat) — MUST be Point
//   UV2               Vector4       — UV node, Channel = UV1
//   PositionWS        Vector3       — Position node (World)
//   NormalWS          Vector3       — Normal Vector node (World) [unused now; kept for compat]
//   Time              Float         — Time node (Time output)
//   CellSize          Float         — _WindowCellSize: world size of one field cell
//   AtlasGrid         Vector2       — _WindowAtlasGrid: FIELD size in cells (cols, rows)
//   EmissionColor     Vector4/Color — _WindowEmissionColor (rgb used)
//   EmissionIntensity Float         — _WindowEmissionIntensity
//   LightParams       Vector4       — _WindowLightParams: x vertical pool scale (1 = sphere,
//                                     >1 hugs water, <1 climbs higher), y falloff exp,
//                                     z lit threshold, w threshold jitter (0..1)
//   FlickerParams     Vector4       — _WindowFlickerParams: x flicker amount, y flicker speed,
//                                     z BASELINE lightness with no fish nearby, w baseline variation
//   StyleParams       Vector4       — _WindowStyleParams: x unlit darken, y EDGE MARGIN (world
//                                     units kept window-free around each face's border),
//                                     z debug mode (0 off / 1 mask / 2 id), w PANE BORDER (0..~0.4)
//   Spacing           Vector2       — _WindowSpacing: UNUSED now (gaps are baked into the field).
//                                     Kept only so the node interface is unchanged.
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
    if (UV2.w <= 0.0001 || UV2.y < 0.0) return;

    // Edge margin (StyleParams.y, world units): keep windows off a border around each face
    // so they don't run into the block's corners/top/waterline. UV2.xy is face-local; zw is
    // (face width, height above water).
    float margin = StyleParams.y;
    if (margin > 0.0 &&
        (UV2.x < margin || UV2.x > UV2.z - margin ||
         UV2.y < margin || UV2.y > UV2.w - margin)) return;

    // ── Field lookup ─────────────────────────────────────────────────────────
    // Map face-local world coords to field cells: x from the left edge, y from the
    // waterline. The field repeats (frac) so buildings taller/wider than the field
    // keep getting windows. Point sampling at the cell centre keeps mask/id crisp —
    // linear filtering would bleed neighbouring window ids across boundaries.
    float  cell   = max(CellSize, 0.001);
    float2 field  = max(AtlasGrid, 1.0);
    float2 f      = float2(UV2.x, UV2.y) / cell;   // continuous cell coords
    float2 cellId = floor(f);
    float2 sub    = frac(f);                        // position within the cell (0..1)

    float2 fieldUV = frac((cellId + 0.5) / field);
    float4 samp    = SAMPLE_TEXTURE2D_LOD(Atlas.tex, PointSampler.samplerstate, fieldUV, 0.0);
    float  mask    = samp.r;
    float  id      = samp.g;
    if (mask < 0.5) return;                         // bare wall

    // ── Debug modes ────────────────────────────────────────────────────────────
    if (StyleParams.z >= 0.5 && StyleParams.z < 1.5)
    {
        Emission = float3(1.0, 1.0, 1.0);   // 1: flat mask — validates field fit / cell size
        return;
    }
    if (StyleParams.z >= 1.5)
    {
        Emission = float3(id, id, id);      // 2: per-window id — adjacent windows differ in shade
        return;
    }

    // ── Pane border: a thin dark frame around each cell so a multi-cell window
    // reads as a row/column of window panes rather than one solid slab. Border 0
    // = flat filled cells (the old look). Done in-shader so it scales with CellSize.
    float pane = 1.0;
    float b = StyleParams.w;
    if (b > 0.001)
    {
        float2 e = smoothstep(0.0, b, sub) * smoothstep(0.0, b, 1.0 - sub);
        pane = e.x * e.y;
    }
    float glass = mask * pane;   // the lit/darkenable window area

    // ── Per-window randomness from the baked id (shared across the whole window) ─
    float r1 = id;                       // threshold jitter
    float r2 = frac(id * 7.13 + 0.13);   // flicker phase / rate
    float r3 = frac(id * 3.71 + 0.57);   // baseline variation

    // ── Lit factor from fish/boat proximity ──────────────────────────────────
    // Radius is the global (WindowLightManager), NOT a material value — single source of truth.
    float radius  = max(_WindowLightRadius, 0.001);
    float falloff = max(LightParams.y, 0.001);
    // True 3D distance from each light's real world position, so the radius governs how far
    // light CLIMBS a facade as well as how far it spreads. Fish are clamped to the water
    // surface by WindowLightManager, so the storeys nearest the waterline respond first.
    // LightParams.x scales the vertical term: 1 = sphere · >1 hugs water · <1 climbs higher.
    float vScale = LightParams.x > 0.0 ? LightParams.x : 1.0;

    float signal = 0.0;
    int count = clamp((int)_WindowFishCount, 0, WINDOW_MAX_LIGHTS);
    for (int i = 0; i < count; i++)
    {
        float4 P   = _WindowFishPoints[i];
        float  dXZ = distance(P.xz, PositionWS.xz);
        float  dY  = (PositionWS.y - P.y) * vScale;
        float  d   = sqrt(dXZ * dXZ + dY * dY);
        signal += P.w * pow(saturate(1.0 - d / radius), falloff);
    }

    // Per-window jittered threshold so neighbours don't all switch at once.
    float thresh = max(LightParams.z, 0.001) * lerp(1.0 - LightParams.w, 1.0 + LightParams.w, r1);
    float lit    = smoothstep(thresh * 0.7, thresh * 1.3, signal);

    // Subtle per-window flicker (phase and rate both id-varied).
    lit *= 1.0 + FlickerParams.x * sin(Time * FlickerParams.y * (0.7 + 0.6 * r2) + r2 * 6.2831);

    // Baseline lightness: every window sits at this fraction of full even with no fish nearby,
    // so the grid stays readable; fish proximity drives it to full and the gap is the glow.
    float baseline = FlickerParams.z * lerp(1.0 - FlickerParams.w, 1.0 + FlickerParams.w, r3);
    lit = saturate(max(lit, baseline));

    // ── Outputs ──────────────────────────────────────────────────────────────
    Emission  = EmissionColor.rgb * EmissionIntensity * glass * lit;
    AlbedoMul = 1.0 - glass * StyleParams.x * (1.0 - lit);   // unlit panes read as dark recesses
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
