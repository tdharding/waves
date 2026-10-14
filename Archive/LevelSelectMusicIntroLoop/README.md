# Level Select Music Intro / Loop (retired 2026-10-02)

The level select's Setup > Audio had three clips: **Wind**, **Music Intro** and **Music Loop**.
The wind looped from the start; with no wind set, Music Intro played once then Music Loop
looped, on the controller's own AudioSource, held (paused) by camera sequences.

Replaced by a single **Starting Audio** clip (`LevelSelectDesignerData.startingAudio`,
carried over from `windLoop`). It loops from the start, plays through camera sequences like
the wind did, and nodes with Audio on crossfade out of it as before.

- `LevelSelectMusicController.cs.txt` — the controller as it was, with the Intro/Loop path
  (`Play`, `FadeIn`, `PlaySequence`, `FadeOutIntroLoop`) and the hold/release logic for it.

The `musicIntro` / `musicLoop` values left in the world data assets (OPN4) are dropped.
