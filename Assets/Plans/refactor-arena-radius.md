# Project Overview
- Game Title: Waves
- High-Level Concept: Refactor Arena sizing and portal placement to use `BaselineMarker.discRadius` from the outer walls prefab as the single source of truth for radius, dropped soul bounds, and grid bounds. This eliminates the need for manual `arenaRadius1`, `droppedSoulBoundsRadius`, and `arenaSizeReferencePlane` configuration.
- Players: Single player
- Target Platform: PC (StandaloneWindows64)
- Render Pipeline: PC_RPAsset (Custom/URP)

# Game Mechanics
## Core Gameplay Loop
- Players navigate an arena with a grid-based maze, collecting souls and orbs while managing waves and water levels.
- The arena size (radius) determines the play area, camera bounds, and portal positions.
- The "Circular Wave Displacement" shader uses a radius property to mask the waves.

## Controls and Input Methods
- Keyboard/Mouse (Both New and Legacy Input supported).

# UI
- Grid Designer tool (Editor) for level creation.
- Map UI for tracking souls.

# Key Asset & Context
- `BaselineMarker.cs`: Defines `discRadius` on the outer walls prefab.
- `ArenaProfile.cs`: ScriptableObject holding arena configurations.
- `LevelSpawner.cs`: Main script for instantiating the arena grid and portals.
- `LevelDataController.cs`: Central hub for runtime level state.
- `DroppedSoul.cs`: Clamped to arena bounds.
- `BoatToWaterMaterial.cs`: Handles water material masks.
- `Circular Wave Displacement.shadergraph`: Uses `_ArenaRadius1` for masking.

# Implementation Steps

## Step 1: Update `ArenaProfile.cs`
- **Description**: Add a flag to enable the new radius-driven logic and provide a helper to get the radius.
- **Files**: `Assets/ScriptsData/DataScripts/ArenaSize/ArenaProfile.cs`
- **Changes**:
    - Add `public bool useBaselineRadius = false;`.
    - Add a method `GetDiscRadius()` that finds the `BaselineMarker` component on the `outerWallsPrefab` and returns its `discRadius`.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

## Step 2: Refactor `LevelSpawner.cs`
- **Description**: Update the grid bounds calculation and portal spawning to use the `discRadius`.
- **Files**: `Assets/ScriptsData/LevelScripts/LevelSpawner.cs`
- **Changes**:
    - In `ApplyGridData`, if `useBaselineRadius` is true:
        - Get `discRadius` from the profile.
        - Construct `cachedArenaBounds` as a square box with side length `2 * discRadius` centered at (0,0,0).
        - This automatically scales the 32x32 maze to fit the arena walls.
    - In `SpawnPortalPrefab`, if `useBaselineRadius` is true:
        - Calculate the position as `centre + (DirectionFromAngle * discRadius)`.
        - This places the portal transform exactly on the arena perimeter.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: No

## Step 3: Sync Shader and Soul Bounds
- **Description**: Link the wave shader and soul clamping to the unified `discRadius`.
- **Files**: 
    - `Assets/ScriptsData/LevelScripts/LevelDataController.cs`
    - `Assets/ScriptsData/SoulsScripts/DroppedSoul.cs`
    - `Assets/ScriptsData/Boat/BoatToWaterMaterial.cs`
- **Changes**:
    - Update `LevelDataController.UpdateArenaWaveMaterial`: if `useBaselineRadius` is true, set `_ArenaRadius1` to `-discRadius`.
    - Update `DroppedSoul.Start` and `BoatToWaterMaterial.UpdateArenaMaskStrength` to use the `discRadius` from the profile for clamping/masking.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: Yes

## Step 4: Update Grid Designer Tool
- **Description**: Ensure the editor grid scale matches the new radius-based bounds.
- **Files**: `Assets/Editor/GridDesignerWindow.cs`
- **Changes**:
    - Modify the pixels-per-unit calculation to check for `BaselineMarker` on the profile's `outerWallsPrefab` when `useBaselineRadius` is true.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: Yes

# Verification & Testing
- **Portal Alignment**: Open a level with a Half Size arena and verify portals are correctly placed at the wall perimeter.
- **Soul Clamping**: Drop a soul and ensure it cannot be pushed outside the visible walls (which should now match the `discRadius`).
- **Shader Verification**: Verify that the wave mask (`_ArenaRadius1`) aligns correctly with the circular arena boundary defined by `discRadius`.
- **Editor Verification**: Open the Grid Designer and verify that the grid size changes correctly when adjusting the `discRadius` on the wall prefab.
- **Backward Compatibility**: Ensure arenas with `useBaselineRadius = false` still use the old manual properties.
