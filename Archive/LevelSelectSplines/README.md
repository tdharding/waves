# Level Select — the scene splines (retired 2026-10-02)

Kept verbatim, outside `Assets/`, so Unity never compiles it and the old system can still be
read back. Nothing in here is live.

The boat itself came off the splines on 2026-09-07 (see `Archive/LevelSelectOnRails/`). What
was left after that was the splines as an *addressing scheme* and the extruding-water system
that grew up around them. This archive is that remainder.

## What it was

- **Spline components on every generated river.** The designer built each river's curve into
  a `SplineContainer` on the run's object, split it either side of every junction mouth into
  `_A`, `_B`… segments plus short "gap" splines across the mouth, gave each a
  **`RiverSegmentID`**, and lowered them all onto the water (`DropSplinesToWater`). Pools got a
  closed ring spline (`BuildRingSpline`). See `LevelSelectDesignerWindow.splines.cs.txt`.
- **`RiverSegmentRegistry.cs`**: looked a segment up by its ID string.
- **`SplinePathSticher.cs`** (class `SplinePathStitcher`, the scene's *BoatPathManager*):
  after every Generate it copied the segments into world-space "baked" paths under itself,
  and told every arena to `AutoDetectPaths` against them.
- **`LevelSelectArenaController`** (old version: `LevelSelectArenaController.before.cs.txt`):
  each door's `PortalLink.arenaPath` was the baked path nearest its trigger. In `Awake` it
  pushed that path's segment ID and a progress (the trigger's nearest point, moved
  `returnOffsetWorld` back up the river) into `GridData.entrances[i].targetSegmentID` /
  `targetProgress`.
- **Leaving a level** (`LevelExitController`) saved that segment and progress
  (`GameProgressData.SaveBoatState`). **`LevelSelectBoatPlacement.cs`** turned it back into a
  position on load and faced the boat along the river's tangent, flipped only when the
  progress was within 0.001 of the very end.
- **`SplineRiverManager.cs`** (+ its inspector `SplineRiverManagerEditor.cs`): stitched every
  segment into a main "highway" and branches with `SplineExtrude` water that unfolded ahead of
  the boat, with barrier colliders at the extrusion front, river progress in the save
  (`riverExtrudeProgress`) and "Extrude on Exit" branches unlocked by leaving a level
  (`exitUnlockedSegments`). With the rivers generated pre-filled (`_waterPrefilled`) it hid
  its own water and spawned no barriers, so none of this was visible any more.
- **`LevelSelectSplineManager.cs`**: advanced that extrusion, called by
  `LevelSelectObstacleManager` when a river gate unlocked (with a `RiverStopPoint` child and
  a `_nextObstacle` chain the designer wired for it).
- **`EntranceSplineRunIn.cs`**, **`LevelSelectSplineInstantiate.cs`**: nothing in any scene or
  prefab used them.

## Why it went

Nothing read the splines for movement any more, and the extrusion was invisible. Their last
live job was placing the boat when it came back from a level, and one door was putting the boat
out facing the wrong way. Read from the code (not confirmed with logs): at a door on the END of
a river the boat was put down `returnOffsetWorld` short of the end, so the end-of-river flip
never applied and it faced back into the door.

## What replaced it

- **Door exits.** Each door has a *Boat spawn distance* (arena panel → Boat exit; primary on
  `DesignerArena.boatSpawnDistance`, the rest on `DesignerArenaEntrance.boatSpawnDistance`,
  default 3). The boat comes out on the line from the arena centre through the door, that far
  past the door sheet (archways) or the arena wall (no archways), facing away from the door.
  The canvas shows a dot and arrow per door. Generate writes the result onto
  `PortalLink.boatExitPosition` / `boatExitForward`; editing a distance rewrites them in the
  open scene. Code: `Assets/Editor/LevelSelectDesignerWindow.BoatExit.cs`.
- **The save names the door, not a river.** `LevelExitController` writes the pose the boat went
  in from, then `GameProgressData.SaveBoatDoor(levelID, entranceIndex)`.
  `LevelSelectDataController.RestoreBoatPosition` tries the door
  (`LevelSelectArenaController.TryFindBoatExit`), then the pose, then the start
  (`TryGetBoatStart`: the set node, or the head of the main river). `SaveBoatPose` clears the
  door, so whichever was written last wins.
- **River gates** still unlock with souls; they just no longer move an extrusion.

## What was kept

- The curve each run is swept along. It is built in memory now
  (`SplineFromPath(Transform, path)`) instead of into a `SplineContainer`, and the run's own
  copy on `RiverRunMesh` is what fog, soul routes, landscape cut, rim nodes and outposts read.
- Unity's Splines package (all the maths), the Spline Tools window (minus its
  *Add RiverSegmentID* option), the in-level spline walls.
- `BoatSplineDrop` keeps its name — it now places doors, wall shading and boat exits on the
  water — so code written against it elsewhere keeps compiling.

## Left behind in Unity

These carry components whose scripts are now missing; delete them by hand:

- Scene objects: `SplineRiverManager`, `LevelSelectSplineManager`, `BoatPathManager`,
  `RiverSegmentRegistry`, and the `RiverExtrusion` object (with `MainHighway`). Then Generate,
  which rebuilds every run without spline components and writes the door exits.
- Prefabs: `PathPrefab.prefab`, `BoatPathManager scriptobject.prefab`.
- `LevelSelectWorld2.unity` and the numbered backup scenes have the same leftovers.

A save holding only an old river + distance (no pose) now starts the boat at the start.

## GUIDs (to restore a script with its scene references intact, put its .meta back with this guid)

| Script | guid |
|---|---|
| SplineRiverManager.cs | 30e984a36b6eba549969b66e41d67d44 |
| SplineRiverManagerEditor.cs | 6fc257e3e59e5384c92f8dbcc931248e |
| LevelSelectSplineManager.cs | 921c03a1d65280f4eb916ad659ee9c73 |
| SplinePathSticher.cs | c2dfccbf9d6366647a917bdc572d6cd9 |
| RiverSegmentID.cs | bcfd573552e40744b886a2b8eb208c9b |
| RiverSegmentRegistry.cs | 7d2607ca77e80fb478985019b9aeefcb |
| EntranceSplineRunIn.cs | 86c06f4c95c1ef848a663ff61568063b |
| LevelSelectSplineInstantiate.cs | fb975d5ec8699ad449186b2ff9e62998 |
| LevelSelectBoatPlacement.cs | e8b223a478eadaa4fafee823a1e945f6 |

## Files

- Whole scripts, verbatim: the nine `.cs` files above.
- `LevelSelectDesignerWindow.splines.cs.txt` — every slice of the designer window that went.
- `RuntimeAndTools.splines.cs.txt` — slices of the data controller, exit controller, pause
  manager, save data, GridData, selection cache, boat control, designer data, BoatStart, Game
  Tester and Spline Tools.
- `LevelSelectArenaController.before.cs.txt`, `LevelSelectObstacleManager.before.cs.txt` —
  those two whole, as they were.
