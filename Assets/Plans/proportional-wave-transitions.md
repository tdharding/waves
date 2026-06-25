# Implementation Plan - Proportional Wave Modifier Transitions

This plan applies the "Tuner Logic" to the runtime wave system. It replaces independent property transitions with a unified **Modifier Intensity Factor** (0 to 1), ensuring that Frequency, Speed, and Ripple Depth always scale in perfect proportion, preventing visual jitter and desync.

## User Requirements
- Apply the successful "Tuner" transition logic to the game modifiers.
- Ensure transitions feel smooth and "physical."
- Avoid "rapidly firing" or strobe effects by keeping all properties synchronized.

## Proposed Changes

### 1. `WaveMaterialController.cs`
- **New State Field**: Add `private float _modifierIntensity` to track the current "strength" of active modifiers (0.0 = Baseline, 1.0 = Full Boost).
- **Refactor Update**:
    - Calculate a `targetIntensity` based on `isModifierActive`.
    - Use `Mathf.SmoothDamp` to transition `_modifierIntensity` over `generalSmoothTime`.
    - Calculate the core wave properties (`Speed`, `Frequency`, `RippleDepth`) by adding `(Boost * _modifierIntensity)` to the baseline values.
    - This ensures they stay in the exact same proportion during the entire ramp-up and ramp-down.
- **Cleanup**: Remove the independent `speedSmoothTime`, `frequencySmoothTime`, and `rippleSmoothTime` fields to simplify the interface.

### 2. `LevelWaveModifierControllerTypeB.cs`
- Ensure it continues to provide the boost values, but rely on the `WaveMaterialController`'s new intensity logic to handle the smoothing.

## Implementation Steps

### Step 1: Update WaveMaterialController
- **Description**: Implement the `_modifierIntensity` logic in `Update()` and `ApplyCombinedState()`.
- **Assigned role**: developer
- **Dependencies**: None

### Step 2: Cleanup and Tuning
- **Description**: Remove legacy damping fields and ensure `generalSmoothTime` provides the desired ramp-up feel (mimicking the "Ramp" setting from the tuner).
- **Assigned role**: developer
- **Dependencies**: Step 1

## Verification & Testing
- **Visual Check**: Trigger a TypeB modifier. The ripples should bloom out and the speed should ramp up in perfect synchronicity, with no flickering in colors or normals.
- **Visual Check**: Ensure the wave "Release" (when the soul leaves) is just as smooth as the "Attack."
- **Scene Sync**: Verify that the Map UI waves still sync correctly with these new proportional values.
