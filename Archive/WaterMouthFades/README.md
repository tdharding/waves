# Water Mouth Fades (retired 2026-10-25)

The settings that eased one water's ripple lines into another's Reach where they meet, plus the
UV3 "bank" data that fed them. `WaterMouthFades.cs.txt` holds the code verbatim as it stood.

## What it was

- **Branch Mouth Fade / Branch Mouth Inset** (River Tuner, Rivers). Branch water carried the
  river it leaves's two waterline distances on UV3 (`RiverJoin.BankAt`, flagged per ring pair by
  `RiverJoin.Near`). Near the junction the branch's line mask was multiplied by that river's
  Reach mask.
- **Pool Mouth Fade / Pool Mouth Reach Overlap / Pool Mouth Extent** (River Tuner, Pools). The
  pool's strips and sheet carried the facing river's banks on UV3 (`SheetBank` and the strip
  `BankAt`). The rings' mask eased to the river's Reach mask, and Extent ended the rings outright.

## Why it went

These only swapped the MASK, so a pool's rings stayed rings and a main river's bank lines ran
straight across a mouth. The user asked for proper openings and a simulated overlap instead.
UV3 on the water now carries which mouth record a face belongs to (see `RefreshWaterMouths` in the
designer and the `_RiverMouth*` globals in `RiverEdgeRipples.hlsl`). Lines follow the real
shoreline, with a gap at each mouth and its two corners, and the two patterns cross-fade over
**Join Overlap** metres.
