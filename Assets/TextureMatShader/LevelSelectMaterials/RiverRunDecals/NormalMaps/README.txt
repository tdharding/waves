River Run Normal Map Decals
===========================

Real normal maps (the purple RGB kind) for the Normal Map Decal Groups, tiled over the level
select's stone. Drop them in here — not in the RiverRunDecals folder above, whose drawings are
gathered onto the colour sheet.

  Tools > Waves > Level Select Run Shading Tuner  ->  Decals  ->  Normal Map Decal Groups


Adding one
----------

1. Drop the normal map into THIS folder (PNG or JPG). No import settings matter: the sheet is
   built from the file itself.

2. Press Rebuild Normal Sheet. It is gathered onto Generated/RiverRunNormalDecalSheet.asset,
   which is LINEAR, so the directions read back exactly as drawn.

3. Add Normal Map Decal Group, tick the map into it, tick the group on a stone type, and Save.


What it does
------------

Nothing is painted. The normal map bends which way the stone faces, and the Level Select Light,
the boat's light and the River Glow / Soul Path Glow are all worked out from that bent direction
as the stone is drawn. So it is live: Strength, Spacing and the light all change it straight
away, and no bake has to be pressed again for it. The baked shadows are not changed by it.

  - Green is UP (OpenGL / Unity style). A map made the DirectX way, green down, lights its bumps
    from the wrong side top to bottom — flip its green channel before dropping it in.
  - Tiles are square and meet edge to edge, so the map has to tile.
  - A colour decal group Tiled at the same Spacing lines up with these tiles cell for cell.
