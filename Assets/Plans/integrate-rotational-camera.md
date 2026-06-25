# Project Overview
- Game Title: Waves
- High-Level Concept: A boat-based wave-riding game with fishing and combat elements.
- Players: Single player
- Target Platform: PC (Standalone Windows)
- Render Pipeline: Custom RP (PC_RPAsset)

# Game Mechanics
## Core Gameplay Loop
The player navigates a boat on a dynamic wave surface, using tools like a sonar, catapult, and fishing lure to interact with the environment and collect souls.
## Controls and Input Methods
- Boat Movement: Arrow keys (Tank controls: Rotate + Forward/Backward).
- Tools: Tab for ability wheel, Space for tools.
- Camera: Currently fixed or orbital around a center point.
- New Feature: Manual orbital camera (Middle/Right mouse to rotate, Scroll to zoom) centered on the boat.

# UI
- Boat HUD: Shows screen-space elements tracking the boat.
- Ability Wheel: Used to select tools.

# Key Asset & Context
- `CameraController.cs`: Manages the arena cameras.
- `LevelSelectCameraController.cs`: Reference for the manual orbit logic.
- `BoatToWaterMaterial.cs`: Handles water shaders and the "Arena Boat Mask" system.
- `BoatCameraZoom.cs`: Handles FOV-based zooming on the boat.

# Implementation Steps
## Phase 1: Camera Controller Refinement
1. **Initialize Orbit State in `CameraController.SetTargets`**:
   - Update `SetTargets(Transform newCenter, Transform newBoat)` to calculate initial `_manualYaw`, `_manualPitch`, and `_manualDistance`.
   - The initialization should derive from the current transform of `boatFollowCam` relative to `newBoat`, similar to `LevelSelectCameraController.SetFollowTarget`.
   - Description: Add initialization logic to `SetTargets`.
   - Assigned role: developer
   - Dependencies: None

2. **Move Virtual Camera instead of Main Camera**:
   - Update `LateUpdate` to apply manual orbit positioning/rotation to `boatFollowCam.transform` instead of `_mainCamera.transform`.
   - Remove the logic that disables `CinemachineBrain`.
   - Update `SetManualOrbit` to keep `boatFollowCam` active and ensure it has the necessary priority to be the active camera.
   - Description: Shift manual control from Main Camera to Virtual Camera.
   - Assigned role: developer
   - Dependencies: Step 1

## Phase 2: Dynamic System Integration
3. **Investigate and Integrate Camera-Boat Dynamics**:
   - Continue searching for any "dynamics" based on camera-to-boat position (e.g., proximity-based fading, audio adjustments, or shader effects).
   - Ensure these systems are correctly updated to use the rotating camera's position/direction instead of a fixed one.
   - Description: Identify and update relative dynamics.
   - Assigned role: developer
   - Dependencies: Phase 1

## Phase 3: Integration and Polish
4. **Wire Camera in `Waves1` scene**:
   - Ensure the `CameraController` in `Waves1` is configured to allow the new manual orbit mode.
   - Verify `BoatCameraZoom` correctly assigns the active camera.
   - Assigned role: developer
   - Dependencies: Phase 1

# Verification & Testing
- **Manual Orbit Test**: Run the game in `Waves1` and verify that Right-Click or Middle-Click allows rotating the camera around the boat. Verify scroll wheel zooms.
- **Mask Alignment Test**: Move the boat to the edges of the arena where the "Arena Boat Mask" is visible. Rotate the camera and verify that the mask "follows" the view (i.e., the masked area is always relative to the camera's perspective).
- **Movement Test**: Ensure boat movement (Arrow keys) remains functional and intuitive while the camera is rotating.
