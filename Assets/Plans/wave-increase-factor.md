# Implementation Plan - Wave Increase Factor for Live Tuner

This plan adds a "Wave Increase Factor" to the `WaveEffectsLiveTuner` editor window. This factor acts as a master multiplier for Frequency, Speed, and Ripple Depth, allowing for simultaneous proportional adjustment of these core wave properties for debugging and tuning.

## User Requirements
- Add a "Wave Increase Factor" scale to the Wave Effects Tuner.
- This scale must modify frequency, speed, and ripple depth simultaneously.
- Useful for debugging and understanding the relationship between these values.

## Proposed Changes

### 1. `WaveEffectsLiveTuner.cs`
- **Add Fields**:
    - `[SerializeField] float waveIncreaseFactor = 1f;`
    - `[SerializeField] float baseFrequency = 1f;`
    - `[SerializeField] float baseSpeed = 1f;`
    - `[SerializeField] float baseRippleDepth = 1f;`
- **Update `LoadFromPreset`**:
    - Capture the baseline values (Frequency, Speed, Ripple Depth) from the preset and reset `waveIncreaseFactor` to 1.0.
- **Update `DrawWaveMotion`**:
    - Add a `Slider` or `FloatField` for "Wave Increase Factor" (suggested range 0.1 to 5.0).
    - When the factor changes:
        - Update `frequency`, `speed`, and `rippleDepth` by multiplying their respective base values by the factor.
    - When individual `frequency`, `speed`, or `rippleDepth` fields are modified manually:
        - Back-calculate the base values (e.g., `baseFrequency = frequency / waveIncreaseFactor`) so the factor remains active and consistent.
- **Initialization**:
    - Ensure base values are initialized in `OnEnable` or `Awake` if no preset is loaded.

## Implementation Steps

### Step 1: Add internal state fields
- **Description**: Add the factor and baseline storage fields to the `SERIALIZED STATE` section of `WaveEffectsLiveTuner.cs`.
- **Assigned role**: developer
- **Dependencies**: None

### Step 2: Update UI and Logic in `DrawWaveMotion`
- **Description**: Add the multiplier slider and implement the bi-directional update logic (Factor -> Values and Manual Value -> Base Value).
- **Assigned role**: developer
- **Dependencies**: Step 1

### Step 3: Update Preset Loading
- **Description**: Modify `LoadFromPreset` to initialize the base values so the tuner starts in a predictable state when a preset is loaded.
- **Assigned role**: developer
- **Dependencies**: Step 1

## Verification & Testing
- **Setup**: Open the Wave Effects Tuner and load a preset.
- **Simultaneous Adjustment**: Move the "Wave Increase Factor" slider. Verify that Frequency, Speed, and Ripple Depth update proportionally in their respective fields.
- **Live Preview**: Verify that the water in the scene reacts to the master multiplier if "Apply Live" is enabled.
- **Manual Override**: Change Frequency manually while the factor is at 2.0. Then change the factor to 1.0. Verify Frequency returns to the manually set value (divided by 2).
- **Persistence**: Close and re-open the window. Verify the factor and base values are preserved (since they are SerializedFields).
