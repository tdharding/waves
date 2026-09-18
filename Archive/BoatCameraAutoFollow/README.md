# Boat camera auto-follow (Waves scene)

Retired 2026-09-15.

**What it was.** `CameraController` in the Waves scene left the mouse turning the camera about the
boat at all times, and once the player stopped turning for `autoFollowDelay` seconds while the boat
was moving, it drifted the yaw round behind the boat — eased in, eased out, only past
`autoFollowMinAngle`, only above `autoFollowMinBoatSpeed`.

**Why it went.** The Waves boat camera now follows the level select camera's pattern: under way it
sits behind the boat at an authored distance and height and catches up through turns
(`followCatchUpTime`); the mouse only turns it about the boat while the boat is stopped with Q
(`BoatAnchor`). A drift that waits for the mouse to go quiet has nothing left to do.

**What was kept.** The follow vertical offset, scroll zoom, low-angle zoom, height floor, sonar
pitch lock, zoom depth of field, the free cursor key and the orbital profile camera.
`boatHeadingSource` stayed too — the follow reads the boat's heading from it.

`CameraController.cs.txt` is the whole file as it was, verbatim.
