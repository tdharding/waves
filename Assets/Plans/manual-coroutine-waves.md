# Implementation Plan - Manual Coroutine Wave Transitions

## Explanation of the New Strategy
The previous "Sudden" and "Rapidly Firing" effects were caused by the transition logic restarting every time the modifier animation "plunged" into the water. By moving the logic to a **Manual Coroutine**, we can control the exact order of operations and ensure the transition only happens once per trigger.

### Key Visual Improvement: The "Two-Phase" Transition
Instead of changing everything at once (which causes "jitter"), this plan uses a controlled sequence:
1. **Stabilize Phase:** While we grow the new ripples (changing Frequency/Ripple), we keep the wave movement speed at its baseline level. This prevents the waves from "vibrating" or "teleporting" while their shape is evolving.
2. **Grow Shapes:** Over ~1.5 seconds, we smoothly increase the Frequency and Wave Height.
3. **Spin Up Speed:** Once the shapes are established, we smoothly ramp up the animation Speed to the final boosted value.
4. **Spam Gate:** We will ignore repeated "Activate" calls from the modifier if a transition is already in progress, fixing the "rapidly firing" resets.

## Proposed Changes

### 1. `WaveMaterialController.cs`
- **Strip Damping**: Remove `SmoothDamp` and damping variables from `Update()`.
- **Steady-State Animation**: `Update()` will only handle the steady "walking" of the wave phase using the `currentGlobalState.Speed`.
- **Manual Transition Coroutine**: Implement `ModifierTransitionSequence`.
    - It captures the visual start state.
    - It loops manually to lerp values.
    - It uses a "Spam Gate" to ensure it only runs once per modifier activation.

### 2. `LevelWaveModifierControllerTypeB.cs`
- Simplify the interaction so it doesn't spam the controller unnecessarily.

## Implementation Steps

### Step 1: Logic Cleanup
- **Description**: Remove the damping/velocity code from `WaveMaterialController`. Restore the `Update` loop to only handle steady-state phase accumulation.
- **Assigned role**: developer
- **Dependencies**: None

### Step 2: Implement "Sequence" Coroutine
- **Description**: Add the `ModifierTransitionSequence` coroutine to `WaveMaterialController`.
    - **Step A**: Smoothly transition Frequency and Ripple while keeping Speed at baseline.
    - **Step B**: Smoothly transition Speed to target.
- **Assigned role**: developer
- **Dependencies**: Step 1

### Step 3: Add the Spam Gate
- **Description**: Update `SetModifierBoost` to ignore calls if the target values haven't changed or if a transition is already active for that state.
- **Assigned role**: developer
- **Dependencies**: Step 2

## Verification & Testing
- **Visual Check**: Activate the modifier. You should see ripples "bloom" out first, followed by the machine's speed increasing, with no jarring jumps.
- **Visual Check**: Verify that `WaveCenter` snaps instantly.
- **Performance**: This approach is more efficient as the math only runs during the transition.
