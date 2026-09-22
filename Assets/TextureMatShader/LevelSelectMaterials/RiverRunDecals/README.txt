River Run Decals
================

Hand-drawn marks stamped over the level select's stone rivers. Drop them in here.

  Tools > Waves > Level Select Run Shading Tuner  ->  Decals


Adding one
----------

1. Drop the drawing into THIS folder — not into Generated, which is where the built sheet goes.
   A PNG (or JPG) of any size and any shape, drawn on transparency. No particular import
   settings are needed: the sheet is built from the file itself, so Read/Write Enabled, the
   compression and the rest are all left as Unity imported them.

2. In the tuner's Decals section, press Rebuild Sheet. The section says when the folder and the
   sheet have fallen out of step, and names the drawing that changed.

3. Press Save so the preset keeps it. The sheet is held on the preset like every other setting
   in that window — until it is saved, the new drawing only exists in the tuner.


What happens to the drawing
---------------------------

Every drawing is gathered onto ONE texture of equal cells, Generated/RiverRunDecalSheet.asset,
because the shader picks which decal to stamp per pixel and so has to be able to read any of
them from a single texture. Each drawing is fitted to its cell with its longest side filling it
and its shape kept, which means:

  - The LONGEST side is what the tuner's Scale is measured across, in metres. A tall drawing
    stamped at 0.06 comes out 0.06 tall; a wide one comes out 0.06 wide.

  - Drawings are ordered BY NAME, and the name order is what decides which cell each one lands
    in. Renaming a drawing moves it to a different cell, which moves every decal of that drawing
    in the world. Adding one with a name that sorts in the middle does the same. That is not a
    fault; it is worth knowing before renaming things.

  - Cells take their size from the biggest drawing in the folder, up to 256px, so one very large
    drawing makes the sheet larger for everything. Keep them roughly the size they will be seen
    at — a decal is a few centimetres across on stone this size.

The sheet is rewritten in place on every rebuild, same asset and same guid, so the preset
holding it goes on holding it.


Where the scatter is decided
----------------------------

Not here. Amount, Spacing and the two Scales are in the tuner, saved on the structure preset, and
pushed to the shader as bare $Globals every frame like all the rest of the run shading. Amount at
0 draws none of them, which is what a preset that has never heard of decals gets.

The stamping itself is in RiverRunShading.hlsl, one folder up.
