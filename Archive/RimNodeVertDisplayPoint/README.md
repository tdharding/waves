# Rim Node Vert Display Point (retired 2026-10-14)

**What it was:** a rim node "On Top" choice that placed `LevelSelectVertDisplayPoint.prefab` on the
platform's top, off its PrefabBaselineAlignment disc, scaled by `displayHeight` and facing the river.
It could carry the press-E poster interaction.

**Why it went:** the Screen choice replaced it. Screen is a square opening cut into the rim
node's river-side wall, with a flat glowing quad at the back facing out into the river. Later,
interacting will point a camera at the screen and show the artwork there.

**What was kept:**
- `RimNodeTopper.VertDisplayPoint` stays in the enum so saved data loads. The designer reads it
  as Screen and shows it as Screen in the dropdown.
- `displayHeight` stays on the data (no renames), unused.
- The interaction settings carry straight over to the screen.
- `PlaceOffAligner` stays in the designer as a general helper.
- The prefab is still in `Assets/Prefab/LevelSelectPrefabs/`, now unreferenced. Delete it whenever
  you like.

**Files here:**
- `LevelSelectVertDisplayPoint.prefab` — a copy of the prefab.
- `LevelSelectDesignerWindow.RimNodes.VertDisplayPoint.cs.txt` — the removed designer slices.
