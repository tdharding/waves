# Fog — blob-space grain (retired 2026-09-23)

Kept verbatim, outside `Assets/`, so Unity never compiles it and the old look can be put back.
Nothing in here is live.

## What it was

The fog sheet's grain was sampled **around each mass's own centre**, so every mass carried its
texture with it as it drifted.

- `FogFieldManager.PushGlobals` pushed `_FogBlobCentres[64]` every frame, one slot per live mass.
- `FogGrain.hlsl` recovered the mass from the blob id in the field texture (G / R), looked up its
  centre, and sampled two octaves of value noise around it. It took `BlobId` and `Time` inputs.
- `FogCompose.hlsl` was `lerp(fogColour, litColour, lit) * grain` — no edge shadow.

Its weakness: where two masses overlap the blob id is a blend, so the grain snapped to whichever
mass owned more of the pixel, leaving a hard seam in the texture.

## What replaced it

- **Grain**: one octave of gradient noise in world space over the whole fog plane, drifting on the
  fog's wind (`_FogGrainDrift`, accumulated in `FogFieldManager`). The fog shape masks it.
- **Edge Shadow**: `FogCompose` darkens the base colour toward the outline (on Fill), under the
  lighting. Two settings on the Fog Map: Edge Shadow Width, Edge Shadow Strength.
- The shape, lip, height map and lighting are untouched. The blob id is still painted — undulation
  offsets by it.

## Files

| File | Live location |
|---|---|
| `FogGrain.hlsl`, `FogCompose.hlsl` | `Assets/ScriptsData/VisualEffectGraphScripts/` |
| `FogSheet.shadergraph`, `FogSheet.shader`, `FogMap.cs`, `FogFieldManager.cs`, `FogSheet 1.mat` | `Assets/ScriptsData/FogScripts/` |
| `FogMapWindow.cs` | `Assets/Editor/` |
| `FogGrain.shadersubgraph` | `Assets/TextureMatShader/ShaderGraphMaterials/` (unused wrapper) |

## To revert

Copy every file back over its live counterpart. `FogFieldManager.cs` was archived whole, but it is
a big, busy file — if it has moved on since, port back only the `_FogBlobCentres` push at the top
of `PushGlobals` (and its `BlobCentresId` / `_centreBuf` declarations) and remove `_grainDrift`.
The `edgeShadow*` fields on existing FogMap assets are simply ignored by the old `FogMap.cs`.
