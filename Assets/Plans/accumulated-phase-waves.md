# Implementation Plan - Accumulated Phase Wave Transitions

This plan fixes the "sudden" jumping/teleporting visual effect in the waves by switching from absolute-time phase calculation to accumulated phase tracking. This ensures that changes in speed or frequency result in a smooth acceleration/deceleration of the water rather than a jarring shift.

## User Requirements
- Fix the "sudden" feeling when modifiers change wave properties.
- Ensure perfectly smooth transitions for vertex displacement.

## Proposed Changes

### 1. `WavesAndWhirlpools.hlsl`
- Update the `WavesAndWhirlpools_float` function signature (or logic) to accept an accumulated phase value.
- Change the sine calculation to: `sin(stepped * Frequency - AccumulatedPhase)`.
- Note: To avoid breaking the ShaderGraph node, we will keep the `Time` and `Speed` inputs but ignore them inside the function, instead reading a new `_WavePhase` global or local input.

### 2. `WaveMaterialController.cs`
- Add a `private float accumulatedPhase` field.
- In `Update()`, increment `accumulatedPhase` using: `accumulatedPhase += currentGlobalState.Speed * Time.deltaTime;`.
- Wrap the phase using `2 * PI` (approx 6.283185f) to prevent floating-point precision issues over long play sessions.
- Update `ApplyCombinedState()` to send this phase to the shader via `waveMaterial.SetFloat("_WavePhase", accumulatedPhase)`.

### 3. `Circular Wave Displacement.shadergraph`
- Open the ShaderGraph and update the `WavesAndWhirlpools` custom function node.
- Add a new input port for `Phase`.
- Connect a new Property or Global keyword to this port.

## Implementation Steps

### Step 1: Update HLSL Logic
- **Description**: Modify `Assets/ScriptsData/VisualEffectGraphScripts/WavesAndWhirlpools.hlsl` to include a `Phase` parameter and use it in the `sin` function.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: No

### Step 2: Update WaveMaterialController
- **Description**: Implement the `accumulatedPhase` calculation and material upload.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: No

### Step 3: ShaderGraph Wiring
- **Description**: Update the `Circular Wave Displacement.shadergraph` to pass the new `_WavePhase` property into the custom function.
- **Assigned role**: developer (Manual check/Edit via tool)
- **Dependencies**: Step 1
- **Parallelizable**: No

## Verification & Testing
- **Visual Check**: Trigger a modifier. The waves should speed up smoothly with no "shuddering" or jumping.
- **Long-term Test**: Let the game run for 5-10 minutes. Ensure the waves don't stop moving or become jittery (verifying the phase wrapping logic).
- **Preset Swap**: Change wave presets. The transition should be visually seamless in terms of motion.
