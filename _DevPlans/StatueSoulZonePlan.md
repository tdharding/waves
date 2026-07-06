# Statue-Linked Soul Fish Zone

## Goal
Placing a statue in the Grid Designer auto-creates a circular soul-fish-zone route
around it. Fish swim the authored ring but **can't be caught until the statue is
destroyed**. Replaces the old statue "lure attraction" behaviour. Existing standalone
soul-zone tool is untouched.

## Two radii (don't conflate)
- **Ring radius** — size of the circular route around the statue. Authored in designer.
- **Scatter** (`SoulZone.radius`) — how far fish wander off the route line (band width).
  Lower it for a tighter swim area. Statue zones default to a small value.

## Data (GridData)
- `PrefabPlacement.statueId` (int) — auto-assigned when a statue is placed.
- `SoulZone.statueGuarded` (bool)
- `SoulZone.linkedStatueId` (int)
- `SoulZone.ringRadius` (float) — node circle regenerated from this.
- `SoulZone.radius` (existing) — reused as scatter; small default for statue zones.

## Designer (GridDesignerWindow)
- On statue placement: assign `statueId`; auto-add a `SoulZone` with N nodes on a
  circle of `ringRadius` (closed loop), `statueGuarded = true`, `linkedStatueId` set.
- Editable: ring radius, scatter, node count. Move statue -> regenerate node circle.
  Delete statue -> remove linked zone.
- Draw the ring + a label/line: "Guarded by statue #N".

## Spawn (LevelSpawner)
- Stamp `statueId` onto the spawned `StatueBehaviour`; build an id -> StatueBehaviour map.
- For each guarded zone, hand the matching statue instance to its `SoulShoalController`.
  (Closed-loop spline already built by existing zone path.)

## Runtime (catch gate)
- Real capture gate is per-fish: `FishFishingBehaviour.IsEligibleForAttraction()`.
  Added `_guardStatue` ref + `SetGuardStatue()`; while non-null the whirl can't attract
  the fish. Statue destroyed -> ref goes Unity-null -> catchable. [DONE]
- Also gate `SoulShoalController.CanFish` (prompt/UI) so the "can fish here" cue only
  shows once the statue is gone: add `_linkedStatue`, `&& _linkedStatue == null`. [TODO]

## Progress — feature complete (pending Unity compile/playtest)
- [DONE] Step 1: GridData fields (PrefabPlacement.statueId; SoulZone.statueGuarded/
  linkedStatueId/ringRadius; radius default 3->0.5).
- [DONE] Step 2: tore out statue attraction (LureAttractable, StatueBehaviour slimmed to
  statueId + IsDestroyed/MarkDestroyed, FishFishingBehaviour gate `_guardStatue`).
- [DONE] Step 3: GridDesignerWindow — TryCreateStatueZone on placement, RemoveGuardedZones
  ForCell on erase/overwrite, Ring Radius + Scatter sliders + guarded HelpBox, purple ring
  drawn at ringRadius.
- [DONE] Step 4: LevelSpawner stamps statueId + id->statue map + GenerateRingKnots for
  guarded zones + SetGuardStatue on shoal & each fish; SoulShoalController.CanFish gate.

## Key decisions made during build
- Ring stored as ONE node (statue cell) + ringRadius; the 8-point ring is generated at
  spawn in world space (GenerateRingKnots). Grid cells are too coarse for a ~1.5u circle.
- Catch gate opens via StatueBehaviour.IsDestroyed, set by StatueDestruction.Trigger (was
  only disabling the component — would never open). Opens immediately on break, not after
  the 5s cleanup delay.

## Wave/map mask traces the ring [DONE]
- For guarded zones LevelSpawner rebuilds nodeRegPositions from the generated ring knots
  (post-rotation) and registers closed=true, so SoulFishWaveLinker + SoulFishMapLinker +
  boat-distance all follow the ring, not just the centre.
- Caveat: SoulFishWaveLinker.MAX_POINTS=20 is a global budget across all zones+fish; a ring
  is knotCount points (default 8). Many zones at once can exceed it (pre-existing limit).

## Removals (statue no longer attracts fish)
- `LureAttractable`: delete `State.StatueAttracted` branch, `ActiveStatues` scan,
  `_targetStatue`, statue-based `IsCatchable`. Lure/whirl attraction stays.
- `StatueBehaviour`: drop `attractionRadius`, `orbitRadius`, `orbitSpeed`,
  `moveTowardSpeed`, `returnSpeed`, `returnThreshold` and the attraction gizmos.
  Keep `statueId`; destruction stays in `StatueDestruction`.

## Defaults
- Ring radius: **1.5** (new slider, small range e.g. 0.5-10).
- Scatter (`radius`): **0.5** (existing "Radius" slider, GridDesignerWindow.cs:1722).
- Node count: **8** (closed loop).
- Also lower `GridData.SoulZone.radius` field default 3 -> 0.5 (current 3 is stale;
  real levels use 0.5-1.1). Only affects newly created zones.

## Notes
- The designer "Radius" slider == fish scatter/swim-band width (SoulZone.radius),
  fed to LevelSpawner knot generation as `scatter`. Statue zone needs a *second*
  slider for ring radius since the two are distinct.
