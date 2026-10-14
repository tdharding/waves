# WallLipPreset — retired 2026-10-12

A ScriptableObject holding only a wall lip's sizes (height, thickness), linked from a wall's
`lipPreset` field and edited in the Level Select Preset Editor. Folder was
`Assets/ScriptsData/DataScripts/WallLipPresets` (one asset, `NewWallLipPreset.asset`, kept here).

**Replaced by `WallPreset`** (`Assets/ScriptsData/LevelSelectScripts/WallPreset.cs`, folder
`Assets/ScriptsData/DataScripts/WallPresets`), which saves the whole wall as one: height,
thickness, the Lip toggle and the lip's sizes (now including its Column Ring). A wall links to
it through `DesignerWall.wallPreset`.

**Why.** The user wanted a wall saved as a whole preset, not just its lip.

Nothing was lost on retiring it: every wall kept its own copy of the lip's sizes in
`DesignerWall.lip`; only the link (`lipPreset`) was dropped.
