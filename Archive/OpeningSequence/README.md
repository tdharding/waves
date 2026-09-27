# Level Select — the old opening sequence (retired 2026-09-27)

Kept verbatim, outside `Assets/`, so Unity never compiles it. Nothing in here is live.
Replaced by `CameraSequence` (Tools/Waves/Camera Sequence), which lives in the scene.

## What it was

- **`LevelSelectOpeningSequence.cs`** — the "OPENING SEQUENCE CONTROLLER". On a save's first
  visit it froze the boat (`IntroMode` + `ControlsFrozen`), switched the follow camera off,
  hid a barrier collider and looped wind audio. Space let the boat go while a hand model
  followed it; `NotifyEndTrigger` played the hand's `PlayEnd`, faded the wind out and the
  music in, handed back to the follow camera and saved the boat pose. On a later visit, or
  with `skipIntro` on, it called `CompleteIntroSequence` straight away. With `skipIntro`, a
  first visit also put the boat at the designer's start node (or projected
  `skipIntroStartPoint` onto the nearest river) and jumped the river extrude ahead.
- **`LevelSelectIntroEndTrigger.cs`** — the trigger box that called `NotifyEndTrigger`.
- **`OpeningSequencePrefab.prefab`** — the controller + hand + trigger, deployed by the
  Level Select Designer (Core Prefabs "Opening Sequence", Deploy row, Deploy All).
- **`IntroSequenceNotes.cs` / `IntroSequenceNotesEditor.cs`** — inspector-only storyboard
  notes for the intro (used on an object in `Tutorial1.unity`).

The designer's "Opening Sequence" toggle wrote `skipIntro = !toggle`. It also snapped the
main river's first node to "River Start Pos" on every Generate, and set the camera
controller's `previewOrigin` to that position.

## Why it went

It ran after `CameraSequence` (`[DefaultExecutionOrder(100)]`). On any visit that skipped
the intro, it unfroze the boat in the first frame, so the camera sequence could not hold
the boat still.

## What replaced each piece

- Fresh-save boat placement: already done by `LevelSelectDataController.RestoreBoatPosition`
  (start node / pool / head of MainRiver). The `skipIntroStartPoint` projection was dropped.
- Extrude head start: dropped. The rivers are prefilled generated water.
- Freeze, camera hand-back and unfreeze: `CameraSequence`.
- Hand, wind, barrier and music fade: not carried over.
- `LevelSelectDesignerData.openingSequenceStartPos` stays as the seed position for a new
  world's main river. `useOpeningSequence`, `openingSequence` and `openingSequencePrefab`
  were removed.
