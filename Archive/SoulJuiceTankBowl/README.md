# Soul Juice Tank — bowl, water and drain (archived 2026-10-26)

What it was: the Soul Juice Tank as a bowl of soul juice held up on a column in the middle of a
pool's island. Pressing E drained the bowl (the water lowered down the basin, narrowing with its
curve, down to the Hole Radius or the basin floor), then the pool round the tank filled, then the
tank's soul routes drew out.

Why it went: the user redesigned the tank (2026-10-26) as a lollipop — stem and orb — with a hub on
top feeding an intake pipe up to a hub on a nearby pipe, and a hub on the side with a tap. E now
fills the pool and opens the routes straight away; a water flow effect is planned for later. The
user asked for the bowl and drain to be kept, as they may be used for something again.

What was kept live:
- The bowl geometry lives on as the **Soul Fish Bowl** tower kind (`SoulFishBowl.cs`, its own
  presets and Designation). It starts no routes and has no E.
- `SoulJuiceTankBowl` (the marker) and the controller's Draining phase are still in the live code;
  a tank with no water skips straight past the drain.

Files:
- `SoulJuiceTank.cs` — the bowl tank's sizes and mesh builder, verbatim.
- `SoulJuiceTankBowl.cs` — the runtime marker that lowers the water, verbatim.
- `SoulJuiceTankPreset.cs` — its preset, verbatim.
- `TravellingSoulFishController.TankDrain.cs.txt` — release → drain → fill → routes, and the fish
  circling the bowl.
- `LevelSelectDesignerWindow.Towers.TankFields.cs.txt` — the designer's bowl fields.
- `LevelSelectDesignerWindow.Towers.PlaceTank.cs.txt` — how the body, bowl and water were placed.
