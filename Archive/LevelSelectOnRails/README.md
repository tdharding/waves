# Level Select — the on-rails boat system (retired 2026-09-07)

Kept verbatim, outside `Assets/`, so Unity never compiles it and the old system can
still be read back. Nothing in here is live.

## What it was

The level select boat did not move. It was *placed*, every frame, at a point along a
spline:

- **`LevelSelectBoatControl.cs`** held a normalised `_progress` and pushed it into a
  `SplineAnimate.NormalizedTime`. Left/Right were not steering — they were *requests*
  to a junction. Up/Down flipped `_isReversed`, which spun the mesh 180° and ran the
  progress backwards. Space multiplied the speed. Speed was normalised by the spline's
  world length so every river ran at the same pace regardless of how long it was.
- **`SplineRiverJunctionNodeV2.cs`** was a trigger box sitting at each fork, holding
  the two segment IDs that met there. On entry it worked out which side the boat came
  in on, so the other side was the destination; it then watched for the matching arrow
  key and lerped the boat across to the new spline over `transitionDuration`, handing
  control back with `AttachToSegment`.
- **`SplineRiverJunctionNode.cs`** was the earlier single-file version. It was already
  unreferenced by any scene or prefab when this was archived.
- **`JunctionPromptUI.cs`** drew the ← / → prompt while the boat sat in a junction's
  trigger. The live copy of this file was left in `Assets` because it is wired into a
  canvas prefab — it is simply never called now.
- **`LevelSelectDesignerWindow.junctions.cs.txt`** is the designer's half: it spawned
  one junction script object per split, sized its trigger from the two rivers meeting
  there, and wired the baked segment IDs into it after a bake.

## Why it went

The boat could only ever be at one place on one curve, so obstacles had to be tested
as "ahead or behind along the tangent", turnings had to be asked for rather than taken,
and the geometry either side of the water was decoration the boat could never touch.

## What was kept

The splines themselves, `RiverSegmentID` and `RiverSegmentRegistry` all stay. They are
no longer how the boat moves, but they are still how a place in the world is *named* —
the map UI, the soul fish routes, and the portal exit routing (`entrance.targetSegmentID`
plus `targetProgress`) all address the world that way. The new boat resolves such a
request into a world position once, on load, and is free from then on.
