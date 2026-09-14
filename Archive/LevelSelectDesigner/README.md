# Level Select Designer — retired pieces

Kept verbatim, outside `Assets/`, so Unity never compiles it and the old code can still be
read back. Nothing in here is live.

## `DeployCinemachine.cs.txt` (removed 2026-09-08)

A Scene Deploy step that added a `CinemachineBrain` to the main camera and stood a
`LevelSelectVCam` GameObject under `LEVELSELECT_SCRIPTS` carrying a `CinemachineCamera`.

It was dead code by the time it was removed — nothing called it. No button drew it, and
`DeployAllSceneObjects` never ran it. The work it did is covered elsewhere:

- the brain is added by `EnsureCinemachineBrain`, called from `EnsureMainCamera`
- the vcam comes in on the camera prefab, and `WireAllSceneObjects` finds it with
  `camGO.GetComponentInChildren<CinemachineCamera>()` and wires it to the controller

It was taken out during the script objects lock pass, where every spawn site had to be put
behind the lock's gate — a spawn path nothing could reach was better deleted than guarded.
If a Cinemachine deploy is ever wanted again, this is the shape it had; note that it spawned
`LevelSelectVCam` under the scripts parent, which the lock now holds still.
