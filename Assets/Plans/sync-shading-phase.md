# Implementation Plan - Synchronize Wave Shading (Peaks & Troughs)

This plan fixes the "flickering" or "strobe" effect during wave transitions. While the physical water movement was updated to use a smooth accumulated phase (`_WavePhase`), the color shading (Peaks & Troughs) is still using an old formula that "jumps" whenever the speed changes.

## The Problem
- **Physical Movement**: Uses `_WavePhase` (Smooth, calculated on CPU).
- **Color Shading**: Uses `Time * Speed` (Jarring, calculates a new position every time Speed changes).
- **Result**: During a transition, the "bright spots" of the waves teleport across the surface dozens of times per second, creating a rapidly firing visual glitch.

## Proposed Changes

### 1. `WavePeaksTroughs.hlsl`
- Update the `WavePeaksTroughs_float` function to use the global `_WavePhase` variable.
- Remove the `Time * Speed` calculation from the sine function.
- This ensures the shading perfectly "rides" the physical wave height.

## Implementation Steps

### Step 1: Update HLSL Shading Logic
- **Description**: Modify `Assets/ScriptsData/VisualEffectGraphScripts/WavePeaksTroughs.hlsl`.
- **Assigned role**: developer
- **Dependencies**: None

### Step 2: Verification
- **Description**: Play a transition in the Wave Effects Tuner. The bright and dark bands should now stay perfectly locked to the wave peaks and troughs without any flickering.
- **Assigned role**: developer
- **Dependencies**: Step 1

## Verification & Testing
- **Visual Check**: Move the "Wave Increase Factor" manually and via the "Play" button. The shading should remain stable in both cases.
- **Visual Check**: Trigger a TypeB modifier in-game. The resulting wave boost should feel smooth and powerful rather than chaotic and flickering.
