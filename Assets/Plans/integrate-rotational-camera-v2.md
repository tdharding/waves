# Project Overview
- Game Title: Waves
- High-Level Concept: Boat navigation and interaction on dynamic waves.
- Players: Single player
- Target Platform: PC

# Game Mechanics
## Core Gameplay Loop
The player navigates a boat using tank controls while interacting with various gameplay elements (souls, sonar, etc.). The camera provides a perspective on the action.
## Controls and Input Methods
- Boat: Arrow keys for rotation and movement.
- Camera: Manual orbital rotation (Right/Middle mouse) and zoom (Scroll wheel) centered on the boat.

# Key Asset & Context
- `CameraController.cs`: Drives the arena cameras.
- `BoatToWaterMaterial.cs`: Handles camera-relative arena masking.
- `LevelSelectCameraController.cs`: Inspiration for the manual orbit logic and initialization.

# Implementation Steps
## Phase 1: Camera Controller Refinement
1. **Remove Main Camera Control**:
   - Update `CameraController.cs` to move the `boatFollowCam` transform directly instead of `Camera.main.transform`.
   - Remove any logic that disables `CinemachineBrain`, allowing Cinemachine to handle the Main Camera through virtual camera blending.
   - Assigned role: developer
   - Dependencies: None

2. **Initialize Orbit State in `SetTargets`**:
   - Update `SetTargets` to initialize `_manualYaw`, `_manualPitch`, and `_manualDistance` based on the current world position of `boatFollowCam` relative to the boat.
   - This ensures a seamless transition when the boat is spawned.
   - Assigned role: developer
   - Dependencies: Step 1

3. **Improve Manual Orbit Toggle**:
   - Update `SetManualOrbit` to manage `boatFollowCam` priority and activation correctly.
   - Ensure the `BoatCameraZoom` is assigned to the correct active camera.
   - Assigned role: developer
   - Dependencies: Step 2

## Phase 2: System Validation
4. **Verify Dynamics**:
   - Ensure `BoatToWaterMaterial`'s dynamic `maskAxis` calculation remains effective with the Cinemachine-driven camera.
   - Assigned role: developer
   - Dependencies: Phase 1

# Verification & Testing
- **Initialization Test**: Start the `Waves1` level and verify the camera doesn't "snap" to a default angle but stays where it was positioned in the editor or start-up.
- **Rotation Test**: Verify manual rotation and zoom still work by affecting the Virtual Camera's transform.
- **Mask Test**: Verify the arena mask remains aligned with the view.
