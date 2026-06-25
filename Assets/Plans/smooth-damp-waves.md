# Implementation Plan - Independent SmoothDamp Wave Transitions

## Explanation of the Change
The current wave transitions feel "sudden" because they use **Linear Interpolation (Lerp)**. In a Lerp transition, the value starts moving at its maximum speed the very instant the modifier triggers. This creates a visual "jolt."

Additionally, a logic bug in the current `Update` loop causes the specific settings for Speed, Frequency, and Ripple to be overwritten by a general setting, making the transition much faster than intended.

### Why SmoothDamp?
This plan replaces the Lerp logic with **SmoothDamp**. Think of SmoothDamp like a physical spring or a car accelerating:
1. **Ease-In**: It starts moving at 0 speed and accelerates smoothly. This removes the "snap" when the machine hits the water.
2. **Ease-Out**: It slows down naturally as it reaches the target, preventing a hard stop.
3. **Independent Momentum**: We will give Speed, Frequency, and Ripple their own "Smooth Time" variables. This allows the machine's animation pace to change quickly while letting the ripples grow more slowly and organically.

## Proposed Changes

### 1. `WaveMaterialController.cs`
- **New Fields**:
    - `public float speedSmoothTime = 0.3f;` (How many seconds to reach target Speed)
    - `public float frequencySmoothTime = 0.6f;` (How many seconds to reach target Frequency)
    - `public float rippleSmoothTime = 0.5f;` (How many seconds to reach target Ripple)
    - `public float generalSmoothTime = 0.4f;` (For secondary properties like color/foam)
- **Velocity Trackers**: Private floats (`_speedVel`, `_freqVel`, etc.) to track the "momentum" of each property between frames.
- **Update Logic**: 
    - Fix the bug where core properties were being overwritten.
    - Calculate the target state based on modifier status.
    - Apply `Mathf.SmoothDamp` to the core three values.
    - Apply general smoothing to everything else in the `WaveState` struct.

## Implementation Steps

### Step 1: Update fields and velocity tracking
- **Description**: Add the new tuning fields (Smooth Times) to the Inspector and the private velocity variables to the script.
- **Assigned role**: developer
- **Dependencies**: None

### Step 2: Fix `Update()` logic and implement SmoothDamp
- **Description**: 
    1. Update the `Update()` loop to calculate the transition for Speed, Frequency, and Ripple independently using `SmoothDamp`.
    2. Ensure the general `LerpWaveState` helper no longer overwrites these three specific values.
- **Assigned role**: developer
- **Dependencies**: Step 1

### Step 3: Cleanup and Safety
- **Description**: Ensure that `ApplyStateInstant` (used when loading levels) resets the velocities to zero, so the waves don't "wiggle" after an instant snap.
- **Assigned role**: developer
- **Dependencies**: Step 2

## Verification & Testing
- **Visual Check**: Trigger the TypeB modifier. The ripples should now "bloom" out smoothly rather than "popping" in.
- **Tuning**: You will be able to adjust the `SmoothTime` values in the Inspector while the game is running to find the exact "feel" you want.
