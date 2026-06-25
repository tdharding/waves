# Implementation Plan - Independent Proportional Wave Transitions

This plan refactors `WaveMaterialController` to allow each wave property (Frequency, Speed, Ripple Depth) to transition towards its target independently using proportional damping (exponential smoothing). This ensures that large changes (like Speed) move faster to close the gap, while allowing each to have its own "snappiness."

## User Requirements
- Values should change proportionally to their own distance from target (damping).
- Frequency, Speed, and Ripple Depth should have independent calculation/tuning.
- `WaveCenter` remains instant.

## Proposed Changes

### 1. `WaveMaterialController.cs`
- Add individual damping/speed factors for the core properties:
    - `public float frequencyDamping = 5f;`
    - `public float speedDamping = 10f;` (Higher = Snappier)
    - `public float rippleDamping = 5f;`
- Remove the unified `transitionTimer` and `transitionStartState`.
- Update `Update()` to apply damping to each property individually using `Mathf.Lerp` or `Mathf.MoveTowards`.
- Ensure other `WaveState` properties still transition smoothly using a general factor.

## Implementation Steps

### Step 1: Update fields in `WaveMaterialController`
- **Description**: Remove timer-based fields. Add individual damping factors for Frequency, Speed, and Ripple.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: No

### Step 2: Refactor `Update()` logic
- **Description**: Replace the synced `transitionTimer` logic with per-property damping. 
    - `current.Speed = Mathf.Lerp(current.Speed, target.Speed, speedDamping * Time.deltaTime);`
    - Apply similar logic for Frequency and Ripple.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: No

### Step 3: Cleanup `LerpWaveState` and setters
- **Description**: Simplify `LerpWaveState` if needed or integrate the logic directly into `Update`. Remove `StartTransition()` calls.
- **Assigned role**: developer
- **Dependencies**: Step 2
- **Parallelizable**: Yes

## Verification & Testing
- **Visual Check**: Trigger a modifier. Verify that `Speed` reaches its target at its own pace (ideally snappier) while `Frequency` and `Ripple` evolve at their own rates.
- **Visual Check**: Verify that `WaveCenter` is still instant.
- **Visual Check**: Ensure the overall animation doesn't feel "dragged" by a single unified timer.
