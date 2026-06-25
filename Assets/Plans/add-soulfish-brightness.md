# Project Overview
- Game Title: Waves
- High-Level Concept: A water-themed game with procedural wave effects and "Soul Fish" interactions.
- Players: Single player (implied by the nature of the tools).
- Target Platform: PC (StandaloneWindows64).
- Render Pipeline: Custom PC_RPAsset (likely URP or HDRP based on Shader Graph usage).

# Game Mechanics
## Core Gameplay Loop
The player interacts with waves and "Soul Fish". The wave appearance is controlled by `WavePreset` assets and a `WaveMaterialController`.

## Controls and Input Methods
The Wave Effects Tuner is an editor tool used to tweak wave and soul fish parameters in real-time and save them to presets.

# UI
- **Wave Effects Tuner (Editor Window)**: A custom editor window for tuning wave parameters.
- **Soul Fish Settings**: A section within the tuner for soul fish related parameters.

# Key Asset & Context
- `Assets/ScriptsData/WaveMaterialScripts/WaveMaterialController.cs`: Manages the runtime state of the wave material.
- `Assets/Editor/WaveEffectsLiveTuner.cs`: Editor tool for live tuning.
- `Assets/ScriptsData/DataScripts/WavePreset/WavePreset.cs`: ScriptableObject that stores `WaveState`.
- `Assets/TextureMatShader/MainWave/Circular Wave Displacement.shadergraph`: The shader using `_SoulFishBrightness1`.

# Implementation Steps

## Step 1: Update WaveMaterialController.cs
- **Description**: Add `SoulFishBrightness1` to the `WaveState` struct and handle its lifecycle (damping, application to material, and retrieval from material).
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: No

## Step 2: Update WaveEffectsLiveTuner.cs
- **Description**: Add the `soulFishBrightness1` field to the editor window. Update the UI to show it in the "Peaks & Troughs" section under "Soul Fish Zone". Ensure it is correctly loaded from and saved to `WavePreset`.
- **Assigned role**: developer
- **Dependencies**: Step 1
- **Parallelizable**: No

# Verification & Testing
1. **Editor UI Check**: Open the "Wave Effects Tuner" (Tools/Waves/Wave Effects Tuner #3) and verify that "Brightness 1" appears under "Soul Fish Zone".
2. **Material Sync**: Change the "Brightness 1" value in the tuner and verify that the `_SoulFishBrightness1` property on the assigned wave material updates.
3. **Save/Load Test**: 
    - Change "Brightness 1" to a specific value (e.g., 2.5).
    - Save to the active preset.
    - Change the value to something else (e.g., 1.0).
    - Load from the preset and verify it returns to 2.5.
4. **Runtime Transition**: Enter Play Mode and verify that the value smoothly transitions when switching presets (if applicable).
