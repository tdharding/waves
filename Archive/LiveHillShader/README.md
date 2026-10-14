# Live hill shader (retired 2026-10-01)

The level select landscape as it was before baking: flat tile meshes, raised by
`CalculateHills.hlsl` on the GPU every frame, and cut round the river runs per pixel by the
`RiverEdgeCut` subgraph with alpha clipping.

**Why it went:** the hill graph fed the node's Normal and Noise into the colour stage, so the whole
calculation — 100 hills three times over, plus four passes over 256 river edge points — ran for
every pixel of the landscape, in every view, plus the per-pixel cut. After Landscape Influence
added the river passes (27/09), the GPU began timing out (D3D11 device removed) from 28/09.

**What replaced it:** `LandscapeTool` bakes each tile into a saved mesh (in
`Assets/Prefab/LevelSelectPrefabs/Generated`) whenever a hill, a setting or a river edge changes:
height, normal, the noise lean in UV1, and the ground trimmed exactly at each run's edge. The hill
graph now just draws the mesh. The maths lives only in `LandscapeHillHeight.cs`.

Files here are copies of the last live versions:

- `CalculateHills.hlsl` — the vertex/fragment hill maths and the cut.
- `RiverEdgeCut.shadersubgraph` — the per-pixel cut (Alpha).
- `LevelSelectHillGenerator1.shadergraph` — the graph wired to the above.
- `LandscapeTool.cs`, `LandscapeTileMesh.cs`, `LandscapeHillHeight.cs` — the C# side
  (property-block arrays, flat tiles, the CPU mirror of the shader).

Not compiled from here — outside Assets on purpose.
