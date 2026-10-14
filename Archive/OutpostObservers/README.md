# Outpost Observers (retired 2026-10-12)

## What it was

The Level Select Designer stood "observer" figures (`ObserverManNPC.prefab`) on installation
outposts and on freestanding structures with an Outpost top:

- Each outpost (and outpost preset) had an **Observers** list: Count, then per observer
  Offset Along / Offset Away (Offset X / Z on a structure), Facing and Height.
- The canvas drew a dot with a facing tick for each one.
- Generate spawned them under an `Observers` child of the outpost block, sized by Height against
  the prefab's top marker, feet on the floor via `PrefabBaselineAlignment`, offsets clamped to
  the floor inside the walls (round floor inside a ring wall).
- Each figure's `ObserverManNPCController` was handed the floor (`SetFloor` / `SetFloorRound`)
  and wandered it at random: stand a random time, walk to a random spot on the floor, repeat.

## Why it went

The **NPC Path Tool** (Tools > Characters > NPC Path Tool) replaced it. Characters are placed by
hand and walk authored travel paths saved as `NPCTravelPath` files. The user decided the path
tool is the only thing that moves these characters, so the designer's spawning and the random
floor wander were both removed.

## What was kept

- `ObserverManNPC.prefab` and `ObserverManNPCController` (path walking only now).
- Observer figures already in scenes were left for the user to delete by hand; Generate no
  longer creates, moves or removes them.

## Files

- `LevelSelectDesignerWindow.Outposts.observers.cs.txt` — canvas dots, the Observers fields,
  floor clamping (`ObserverFloor`, `OutpostFloor`, `FloorHalfSize`) and `SpawnObservers`.
- `LevelSelectDesignerWindow.Structures.observers.cs.txt` — the structure outpost top's floor,
  canvas dots and spawning.
- `LevelSelectDesignerWindow.Towers.observers.cs.txt` — clamping after a preset change.
- `LevelSelectPresetEditorWindow.observers.cs.txt` — the preset editor's Observers section.
- `LevelSelectDesignerData.observers.cs.txt` — `OutpostObserver` and the `observers` list.
- `InstallationOutpostPreset.observers.cs.txt` — the preset's copy of the list.
- `ObserverManNPCController.before-floor-removal.cs.txt` — the whole controller as it was,
  floor wander and travel path both.
