# Soul Fish Routes — travelling between levels

First draft, 2026-09-03. Souls stop belonging to one level and start making a journey across the
map: an origin pool, into a level, out through its door, along the level-select river, into the
next level.

The shader half is already built and already in the water graph — the `Travelling Soul Fish` node
in `BranchWaterShader.shadergraph`. It draws nothing until a controller publishes its globals.
This plan is the half that feeds it.

---

## 1. The journey, in one pass

```
origin pool  →  river leg  →  door A  →  LEVEL: soul zone  →  door B  →  river leg  →  next level
   (map)                                (fish catchable)                              (repeats)
```

Four moments, in order:

1. **Departure gate.** Inside a level, the zone's street-light chain reaches the entrance, and if
   that door is locked it has been unlocked. The zone is now open door-to-door.
2. **Leaving.** Its remaining fish swim to the door and despawn there, one by one.
3. **Travelling.** Back in level select, those fish are on the river, drawn by the branch-water
   shader, moving along the authored route toward the next level's door.
4. **Arriving.** Enter that level and the fish are there, swimming its path.

---

## 2. Departure gate

Two conditions, both already tracked, neither newly invented:

| Condition | Where it lives now |
|---|---|
| Every lamp lit, so the zone is drawn all the way to the door | `SoulZoneStreetLightChain` (its `_litCount`) |
| Door unlocked (only if `isLocked`) | `LockedDoorController` → `GameProgressData.IsUnlocked(SaveKey())` |

The zone knows which door it ends at: `SoulZone.attachToEntrances` + `exitEntranceIndex`
(`GridData.cs:161-167`). When both conditions hold, the chain raises a departure event once.

**Leaving behaviour.** Until now the fish have been circling the pool at the last lit lamp —
they reach the far side of that pool and loop back to its near side, so they never swim past it.
On departure that loop-back is simply switched off: the fish carry on out of the pool and down
the rest of the path to the door, at their own `SplineAnimate` speed. It is exactly what already
happens when you light the next lamp, so there is no new movement code. Each fish despawns as it
reaches the door, handing its soul to the tracker as it goes. One by one, as specified.

---

## 3. The tracker

One record per soul, keyed on `soulDataIdentity` — the only stable ID a soul has.

```
SoulPlace
  identity      int      SoulData.soulDataIdentity
  where         enum     OriginPool | InLevel | OnRiver | OnBoat
  routeId       string   which authored route it is on
  legIndex      int      which leg of that route it currently occupies
  progress      float    0-1 along that leg
```

**Why a record and not a derived value.** Today a soul's whereabouts is inferred —
`LevelSelectSoulFishDisplay.GetUncaughtCount` is `total − caught` (`:190`). A soul that has *left*
is neither caught nor present, so it keeps showing in the level it walked out of. Absence has to
be recorded, not subtracted.

**ID space.** Identity becomes the single key. `linkID` (`zoneIndex * 100 + i`) is demoted to what
it actually is — a spawn slot computed when a shoal spawns. It stops being stored as identity, so
the `* 100` collision ceiling stops being a cross-level problem.

**It does not own the boat.** `LevelSoulTracker` already exists and already owns who is aboard —
its parallel identity/linkID lists and its commit on level exit stay exactly as they are. A soul's
place simply reads `OnBoat`, and the details stay where they already work.

**Migration is additive.** `caughtSouls[].caughtLinkIDs` and the boat lists stay exactly as they
are and keep working. The new record is written alongside. Nothing renamed, no save or scene
breakage. Where a soul has no record yet, §3a says where it starts.

---

## 3a. Where souls come from

Two sources, and between them they replace per-level allocation as the thing that decides where a
soul is.

**The origin pool** is the source of record. Souls begin there and flow outward along their route,
which is what gives the map its continuity — a soul is somewhere on a journey, not a possession of
one level. In the end state the Grid Designer's per-level allocation (`SoulZone.souls`,
`SoulData.allocated`) no longer decides anything; the pool and the route do.

**Bowls and statues are the second source.** A soul found inside a level — a fish-bowl tower
toppled, a statue destroyed — is released into the stream from *that level*, and travels on toward
its destination from there rather than from the pool. Both cases already exist as zone kinds
(`towerGuarded`, `statueGuarded`) and already gate catchability, so what is new is only that
releasing them now puts a soul on a route.

**Getting there without emptying the game.** Flipping the authority over in one step would leave
every existing level with nothing in it, since the pool starts empty and the routes are not
authored yet. So the first step seeds the pool from what is already allocated: existing allocation
becomes the pool's opening contents, and from then on the pool is the authority and allocation is
read-only history. Nothing is lost, levels keep their souls, and no level ever reads `allocated`
again to decide who is present.

---

## 4. The route

Authored in the Level Select Designer. Shared, not per-soul — every fish on a route follows the
same chain, and the player advances it.

```
SoulRoute
  routeId       string
  originPool    node id + radius on the map — where this route's souls begin
  points        ordered list, in the order the fish take them:
                  Node    a point on the river (DesignerNode.id)
                  Arena   a level passed through (+ door in / door out)
  gateCondition per point, unused in v1 (see §7)
```

**Points, not whole paths.** The first cut made a leg a whole `DesignerPath`, and it looped back:
appending a path emits its nodes from ITS node 0, which runs backwards whenever the path's stored
order does not start where the previous leg ended. Points fix that, and they make junctions fall
out for free — the route goes only where the next point says, so it stops at a junction until a
point past it is chosen. Between two consecutive points that share a path the band walks that
path's own nodes in the implied direction, so it hugs the river; points sharing no path draw a
straight link and the order list flags them.

Every reference already exists and is already stable: `DesignerPath.pathId`,
`DesignerArena.gridData`, `DesignerArenaEntrance.entranceIndex`. A route is a list of references,
not new geometry.

**How far it is open.** One integer per route in the save — the leg the route has been opened as
far as. In v1 the only thing that pushes it onward is a departure gate firing in a level (§2).
River legs open on their own behind it.

---

## 5. Designer display

Held to the same rule the Grid Designer's zone display follows: `DrawSoulZoneShape` samples
through `zone.SamplePath`, *the same call `LevelSpawner` densifies with*, so the drawing is the
curve the fish actually swim. Route drawing samples the river's own spline for the same reason.

| Grid Designer | Level Select Designer |
|---|---|
| Filled band along the path, half-width = zone radius | Filled band along the river paths the route runs |
| Black chevrons along the band, in node order | Same chevrons — the direction souls travel |
| Solid disc per street-light pool, under the node markers | Solid disc at the origin pool, and at each door |
| Solid disc + black dot per node | Solid disc + black dot where legs meet |

How far the route has opened reads the way the zone does in-level: opened legs drawn solid, the
rest dimmed back — but that is a RUNTIME reading, and it is deliberately not in the designer.
Openness lives in the save file, so at authoring time with no play session every route would draw
as barely opened, which would be misleading rather than informative. The designer draws the whole
route solid, with unselected routes sitting back at low alpha so they do not fight the river for
attention. The dimming belongs with the runtime display.

Solid fills and dots throughout — no wire circles.

---

## 6. Level select runtime

**Movement.** Fish on a river leg advance along that leg's spline. The leg's `pathId` resolves
through `RiverSegmentRegistry` to the live `RiverSegmentID`, so the position is sampled from the
same spline the river mesh was built from. Where the route has not opened any further, fish
gather at that point and wait, the way they gather at an unlit lamp inside a level.

**Rendering.** A `TravellingSoulFishController` publishes the globals every frame — deliberately
every frame, per the `.hlsl` header and the established globals pattern, because a shader reimport
wipes bare globals and there is no silent fallback:

- `_TravelSoulFishPositions[32]` — `.x/.z` world position, `.y` heading in radians, `.w` per-fish size
- `_TravelSoulFishCount`, `_TravelSoulFishSize`, `_TravelSoulFishStrength`, `_TravelSoulFishTex`

32 is the array ceiling. Where more souls are travelling than that, the nearest are published —
the controller owns no state, it is a view onto the tracker.

**Boat entry into a level** already routes through `ArenaEntrance.targetSegmentID`, filled at
runtime by `LevelSelectArenaController`. Souls arriving at a door use the same link in reverse.

---

## 7. Deliberately not in v1

Named here so the seams are visible, not so they get built:

- **Leg gates.** The `gateCondition` field is authored and stored but nothing reads it. Gating a
  river stretch later is filling in a field, not reworking the route.
- **Branching decisions.** A fork is a route with two continuations at a junction. The soul record
  already names its leg, so this costs a field when it comes.
- **Interception mid-river.** Explicitly dropped. Fish make no decisions; they advance and hold.
- **Fish visibly entering the arena.** Arrival puts souls in the destination zone. Watching them
  swim in through the door is a later refinement.

---

## 8. Linked systems — checked before building

| System | Keyed on | Verdict |
|---|---|---|
| Circular wave material (`SoulFishWaveLinker`) | zone node positions + fish `Transform`s | **Untouched.** Never reads an identity or a linkID. |
| `SoulFishLinkingController` | linkID, as a dictionary key only | **Untouched.** Per-level Transform lookup, persists nothing. |
| Boat display (`SoulsOnBoatDisplayManager`) | identity, via `GetSoulsOnBoatIdentities` | **Already correct.** |
| `LevelSoulTracker` | identity + linkID, in parallel | **Kept as-is.** Owns the boat; the journey record sits alongside. |
| `SoulShoalController.SpawnFish` | linkID via `IsSoulCaught` | Unchanged for now. Becomes a `SoulsIn(levelID)` reader when routes land. |

Found but deliberately NOT touched, being pre-existing and outside this ask: the boat has two
writers. `LevelSoulTracker` maintains its index-matched pair, while `GameProgressData.AddSoulToBoat`
(from `SoulSlot`, `GameplaySoulSlot`, `ExternalWaveModifier`) appends straight to the save with a
`-1` linkID, bypassing the session lists. They can drift.

---

## Open item

How much of §3a lands in v1. The seeding step is small and can go in from the start. Actually
*retiring* allocation — the Grid Designer no longer assigning souls to levels at all, souls only
ever arriving by route — is a bigger change to the authoring flow and probably wants its own pass
once routes are drawable and the journey has been seen working end to end.
