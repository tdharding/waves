# Water Overlaps (retired 2026-10-25)

The places where one river water surface was built to lie OVER another, and the settings that
went with it. `WaterOverlaps.cs.txt` holds the code verbatim as it stood before removal.

## What it was

- **Water Pool Overlap** (`LevelSelectDesignerData.waterPoolOverlap`, Aesthetics section). A pool's
  water carried on past the line the river's water ends on, over the river's own water, as a
  "lap" strip whose alpha faded to nothing at the far lip. World2 had it at 1 m.
- **River Fade To Pool Distance / Intensity** (River Tuner, `RiverEdgeRippleSettings`). How much
  of that lap the alpha gradient covered, and its falloff. Shader: the lap block at the top of
  `RiverEdgeLines` in `RiverEdgeRipples.hlsl`, reading `EdgeData.w` (lap length) and `FlowData.z`
  (lap ramp).
- **Junction overlap.** A branch's water ran back across the mouth all the way to the CENTRELINE
  of the river it leaves, drawn on top of that river's water by its Sorting Group. Not a setting:
  the lead-in was the junction collar. (`waterBranchOverlap`, a setting on top of that, had
  already gone on 2026-09-16.)

## Why it went

The user's call: river branches and pools should simply meet. The laps existed so a pattern
could alpha-blend from one surface's frame into the next. They were no longer wanted.

## What was kept

- Pools butt exactly onto the line each river's water ends on (as they did at overlap 0).
- `LevelWaterIntoPool` still holds the river water level with the pool, now over the lead
  across the rim only, so the boat does not step at the join.
- A branch's water now stops on the EDGE of the river it leaves. Each side of the ribbon is cut
  where it crosses that river's water edge, so the end lies along it however the branch leans
  (`RiverJoin.ribbon`, cut in `BuildWater`).
- Branch Mouth Fade/Inset and Pool Mouth Fade/Reach Overlap/Extent are untouched. They hand the
  lines over to the other river's Reach, which is not an overlap.
- Water Sorting Groups are untouched. With nothing lapping, they no longer decide anything.
