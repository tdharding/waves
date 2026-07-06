# Free-Moving Soul Fish Zone Nodes

## Goal
Soul zone nodes stop snapping to grid cells — they become free-positioned points you can
drop and drag anywhere, like SplineWallPath nodes already do. No data loss on existing levels.

## Storage (GridData.SoulZone)
- Add `List<Vector2> nodePositions` — normalized grid coords (-0.5..0.5 from centre),
  same space as SplineWallPath.nodes. This is arena-relative, so zones still scale with
  arena size.
- Add `bool closedLoop` — replaces the current "last node == first node" cell trick, which
  can't work once nodes are floats.
- Keep legacy `List<int> nodes` for MIGRATION ONLY (don't delete — old assets need it).

## Migration (non-destructive)
- One helper: if `nodePositions` is empty but `nodes` has entries, fill nodePositions from
  each cell's centre (cell index -> normalized coord) and set closedLoop from the old
  last==first test. Run lazily in the designer on load and in LevelSpawner before use.
- Existing levels keep working; first save writes the new fields.

## Designer (GridDesignerWindow) — reuse SplineWallPath pattern
- Place: click adds `PixelToWorldXZ(mouse)` to nodePositions (no cell snap).
- Drag: hit-test nodes by pixel distance (WorldXZToPixel), drag sets node = PixelToWorldXZ.
- Draw nodes/lines from nodePositions via WorldXZToPixel (not per-cell IndexOf).
- Close-loop toggle writes `closedLoop` instead of duplicating the first node.
- Retire cell-based node code: `_dragCurrentCell`, `_drawingNodes` (int), the per-cell
  node-marker loop, bridge-mode cell logic, insert/duplicate/remove by cell.

## Spawn (LevelSpawner)
- Build nodeWorldPositions from nodePositions (normalized * arena frame, matching how
  SpawnSplineWalls maps node.x*arenaWidth). Use `closedLoop` for loop detection.
- Single-node + statue-ring paths unchanged in spirit.

## Statue ring (folds in the earlier #1 ask)
- Statue zone becomes a real multi-node free zone: auto-fill nodePositions with the 8
  evenly-spaced ring points (smooth circle) + closedLoop=true, centred on the statue.
  Designer then shows the actual smooth nodes, and moving/scaling regenerates them.

## Risk / notes
- Serialized change across all levels — migration is the critical part; test on a couple of
  existing levels (StatuesLevel1, Level2GridDatav2) that they load with zones intact.
- Not renaming `nodes` (kept for migration) per the serialized-field rule.

## Status — IMPLEMENTED (pending Unity compile/playtest)
- GridData.SoulZone: nodePositions (Vector2, normalized) + closedLoop; MigrateNodesIfNeeded
  (fills from cell centres, then clears legacy `nodes`); CellToNormalized. [DONE]
- LevelSpawner: NormalizedToWorldPos; SpawnSoulFish uses nodePositions + closedLoop;
  GenerateRingKnots removed (statue is now a normal closed multi-node zone). [DONE]
- GridDesignerWindow: draw (lines+markers+scatter) from nodePositions; placement writes
  normalized; free select+drag via HandleSelectSoulNodeInput (pixel pick, PixelToWorldXZ);
  Delete key; insert/delete node on nodePositions; Closed-Loop toggle; statue ring built as
  8 free nodes via BuildRing, Ring Radius slider regenerates. [DONE]
- Statue ring = smooth multi-node free zone (folds in earlier #1). [DONE]
- Bridge mode: was already dormant (never activated); left inert. Placement still starts by
  clicking a cell (snaps once), then nodes drag freely.

## Still pending separately (from same session, not in this plan)
- Space+drag pan (#4, small), zoom-scaling bug (#3, needs repro detail).
