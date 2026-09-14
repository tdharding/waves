# LevelSelectRiverPreset — retired 2026-09-11

One ScriptableObject that carried BOTH of a level select world's river looks: `edgeRipples` (the
lines on the water) and `runShading` (the shading of the generated stone runs).

Replaced by two assets, because they are two materials on two pieces of geometry authored in two
different tuners:

- `LevelSelectRiverWaterPreset`  — `edgeRipples`, authored in the Level Select River Tuner
- `LevelSelectRiverStructurePreset` — `runShading`, authored in the Level Select Run Shading Tuner

**Why it was retired.** Each tuner remembered its own Active Preset, so the two halves of one
combined asset could be tuned on two different copies of it. That is what happened: one copy held
tuned ripples and default stone, another held tuned stone and default ripples, and the world read
only the first. The editor hid it — a tuner window with Apply Live on pushes its own numbers over
whatever the world holds, in play mode too — so the stone looked right in the editor and reverted
in a build.

The two settings classes this file also held, `RiverEdgeRippleSettings` and the
`RiverRippleDebugView` enum, were NOT retired. They moved to
`Assets/ScriptsData/LevelSelectScripts/RiverEdgeRippleSettings.cs`, alongside the matching
`RiverRunShadingSettings.cs`.

Both existing preset assets were converted in place, keeping their guids, so the designer data
reference that pointed at one of them still resolves.
