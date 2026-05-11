# Plan: LevelSelectDesignerWindow

## Context
The Level Select scene requires a complex hand-placed river network of source SplineContainers with RiverSegmentID metadata, visual SplineInstantiate setups, junction prefab placements with spline gaps, and arena nodes. Currently every segment is placed entirely by hand. This tool lets the designer draw spline paths in the Scene view, configure metadata per-path, and generate all scene objects (source segments, visuals, junctions, arenas) in one click — with full undo/respawn support.

---

## New Files (only)

| File | Type | Purpose |
|---|---|---|
| `Assets/ScriptsData/LevelSelectScripts/LevelSelectDesignerData.cs` | ScriptableObject | Persistent designer state |
| `Assets/ScriptsData/LevelSelectScripts/LevelSelectSplineInstantiate.cs` | MonoBehaviour | Custom instantiator with junction gap trimming |
| `Assets/Editor/LevelSelectDesignerWindow.cs` | EditorWindow | Scene-view designer tool |

**Minimal changes to 3 existing scripts** to support multiple world scenes. The designer window itself uses `SerializedObject` to set `_sourceSegments` with no further script changes.

---

## Multi-World Scene Routing (prerequisite — DONE)

All three changes are already in the codebase:

- `LevelSelectionCache.cs` — `CurrentWorldScene` property exists
- `LevelSelectDataController.Awake()` — sets `CurrentWorldScene` on startup
- `LevelExitController.ConfirmExit()` / `ForceExit()` — both use cache with `sceneToLoad` fallback

**Result:** Every world scene routes exits back to itself automatically. Existing LevelSelect and all level prefabs unchanged. `sceneToLoad` field kept as safe fallback.

---

## LevelSelectDesignerData (ScriptableObject)

Persists the entire designer state as a project asset (`.asset` file). Saved on every edit via `EditorUtility.SetDirty`.

```csharp
[Serializable] DesignerNode
  string   id              // GUID
  Vector3  worldPosition
  NodeType type            // Waypoint | JunctionSplit | ArenaEnd | ShopEnd

[Serializable] DesignerPath
  string   pathId          // GUID
  string   segmentId       // editable, auto-generated default (e.g. "Main_00")
  List<string> nodeIds     // ordered knot sequence
  bool     isTopPath, isBottomPath
  SegmentType segmentType  // MainRiver | PrimaryBranch | Secondary | Tertiary
  string   riverName       // displayed as "River Name" in UI; written to RiverSegmentID.junctionGroup on generate
  bool     leadsToArena, arenaIsAtEnd, extrudeOnExit
  string   arenaGridDataGuid
  Color    editorColor

[Serializable] DesignerJunction
  string   junctionId
  string   nodeId          // the shared JunctionSplit node
  List<string> pathIds     // which paths pass through here

[Serializable] DesignerShop       // one per ShopEnd node
  string     nodeId
  string     pathId
  float      pathT
  GameObject shopPrefabOverride   // null = use global shopPrefab
  // Orbs of Omalon currency — delivery method TBD, reserved for future implementation

[Serializable] DesignerObstacle   // one per ObstacleGate marker
  string     obstacleId           // unique string for GameProgressData.IsUnlocked()
  string     pathId
  float      pathT                // normalized position along path (0-1)
  int        soulSlotCount
  GameObject obstaclePrefab       // null = use global obstacle prefab

[Serializable] DesignerArena      // one per ArenaEnd node
  string     nodeId
  GridData   gridData             // the level assigned to this arena slot
  int        entranceIndex        // which GridData.entrances index to use
  GameObject arenaPrefabOverride  // null = use global arenaPrefab

// Tool settings (stored in data asset for portability)
  GameObject junctionPrefab
  GameObject arenaPrefab
  GameObject obstaclePrefab
  GameObject shopPrefab           // TBD delivery
  GameObject riverBlockPrefab
  float      junctionGapPadding
  float      splineInstantiateSpacing  // default 0.15

  // Scene-level references (persistent — not regenerated)
  LandscapeTool landscapeTool     // drag in from scene; survives Respawn
```

---

## LevelSelectDesignerWindow (EditorWindow)

### 3-Column Layout

```
┌──────────────┬───────────────────────────┬────────────────────┐
│ Left panel   │   2D TOP-DOWN CANVAS      │  River Info Panel  │
│              │   (mouse draw)            │                    │
│ Modes:       │                           │  Rivers            │
│ [Draw]       │  ══════════════           │  ──────────────    │
│ [Select]     │       \                   │  ■ MainRiver       │
│ [Junction]   │        \── ◆ junction     │    Main_00   T     │
│ [Arena]      │             \             │    Main_01   T     │
│ [Obstacle]   │              ● arena      │                    │
│ [Shop]       │                           │  ■ NorthBranch     │
│              │  [Pan: mid-mouse]         │    Branch_00 T     │
│ ──────────── │  [Zoom: scroll]           │                    │
│ Path list    │                           │  [+ New River]     │
│ ■ Main_00    │                           │                    │
│ ■ Main_01    │                           │  Obstacles         │
│ ◆ Branch_A   │                           │  ──────────────    │
│              │                           │  ① main_gate_01    │
│ [+ New Path] │                           │  ② main_gate_02    │
│              │                           │  ③ main_gate_03    │
│ Selected     │                           │                    │
│  path props  │                           │                    │
│ ──────────── │                           │                    │
│ Scene Objs   │                           │                    │
│ Landscape [] │                           │                    │
│ ──────────── │                           │                    │
│ Prefabs      │                           │                    │
│ Junction  [] │                           │                    │
│ Arena     [] │                           │                    │
│ Obstacle  [] │                           │                    │
│ Shop      [] │                           │                    │
│ Block     [] │                           │                    │
│ ──────────── │                           │                    │
│ [Generate]   │                           │                    │
│ [Respawn]    │                           │                    │
│ [Clear]      │                           │                    │
└──────────────┴───────────────────────────┴────────────────────┘
```

### 2D Canvas → World Mapping
- Canvas pixel (px, py) → world (px * unitsPerPixel + originX, worldY, py * unitsPerPixel + originZ)
- Pan: middle-mouse drag. Zoom: scroll wheel.
- Right-click: context menu (delete node, change type)

### Modes

| Mode | Canvas interaction |
|---|---|
| **Draw** | Left-click to place knots. Double-click/Enter to end path. Snap to nearby node to connect. |
| **Select** | Click path or node to select. Drag node to reposition. |
| **Junction** | Click a node to toggle to `JunctionSplit` (diamond icon). |
| **Arena** | Click a path endpoint to mark `ArenaEnd`. Opens GridData picker. |
| **Obstacle** | Click along a path to place an `ObstacleGate` marker at that T position. |
| **Shop** | Click a path endpoint to mark `ShopEnd`. Uses Orbs of Omalon — delivery TBD. |

### Terminology
`junctionGroup` on `RiverSegmentID` is shown as **"River Name"** throughout the designer UI. Underlying field name unchanged.

### Canvas Visual Feedback

- **Paths colored by River Name group** — all segments in a group share one color
- Hover/select river in info panel → group brightens, others dim to ~30%
- Waypoint nodes: filled circle in river group color
- JunctionSplit nodes: yellow diamond (always full opacity)
- ArenaEnd nodes: icon + level name label from `gridData.levelName`
- ObstacleGate markers: orange gate icon on path line, numbered ①②③ in chain order
- ShopEnd nodes: purple/gold icon + "SHOP" label
- Selected path: white outline
- Junction gap preview: two perpendicular lines showing gap width

### River Info Panel (right column)

- List of River Name groups with color swatch + name
- Segments listed under each group (segmentId + T/B flag)
- Obstacle chain list (numbered) below rivers
- Click group header → highlights all paths in group on canvas
- Click segment row → selects that path
- Click color swatch → color picker for whole group
- Double-click name → inline rename
- [+ New River] button

### Context Panels (left panel, context-sensitive)

**When ObstacleGate selected:**
```
Obstacle Gate  #2 of 4
  ID:        [main_gate_02]
  Slots:     [3]
  Prefab:    [override    ]
  Order:     2  [ ↑ ] [ ↓ ]
  Path:      Main_01  T: 0.42
```

**When ArenaEnd selected:**
```
Arena: Rocky Waves
  GridData:  [RockyWavesGrid]
  Entrance:  [0]
  Prefab:    [override      ]
  ─────────────────
  levelID:      rocky_waves
  Soul points:  12
  Entrances:    2
```

**When ShopEnd selected:**
```
Shop Endpoint
  Prefab:    [override      ]
  Currency:  Orbs of Omalon (TBD)
```

---

## Generation Algorithm

All steps wrapped in `Undo.IncrementCurrentGroup` — single Ctrl+Z removes everything.

### Target Parents

| Content | Parent GO | Cleared? |
|---|---|---|
| Source visual segments | `MAINRIVERVISUALS` / `RIVERBRANCHES` | Yes |
| Junction prefabs | `RIVERJUNCTIONS` | Yes |
| Obstacle gates | `RIVERGATEsobstacles` | Yes |
| Baked paths | `BoatPathManager` | No (SplinePathStitcher owns this) |
| Arenas | In-place (tagged by GUID) | No |

### Steps

1. **Clear** — destroy children of cleared parents above
2. **Source segments** — per path: SplineContainer + BezierKnots + RiverSegmentID + SplineInstantiate (riverBlockPrefab, 0.15 spacing, Y+/Z+ axes)
3. **Junction splits** — reuse `SplineToolsWindow` gap logic: A→Junction→B containers on same GO; separately instantiate junction prefab persistently; configure `SplineRiverJunctionNodeV2` sideA/sideB segmentIDs; wire `boatControl` if found
4. **Obstacle gates** — sort by river position; instantiate prefabs; set `obstacleID`; chain `nextObstacleTransform`; wire `LevelSelectSplineManager._firstObstacleTransform` to first obstacle via SerializedObject
5. **Arena nodes** — instantiate arena prefab (or update existing via `LevelSelectDesignerArenaTag` GUID); set `LevelSelectArenaController.gridData`; orient to path tangent
6. **Shop nodes** — instantiate shop prefab at endpoint; TBD wiring
7. **Wire _sourceSegments** — SerializedObject sets `_sourceSegments` list on SplineRiverManager and SplinePathStitcher (ordered by BranchDepth)
8. **Wire SoulsOnBoatDisplayManager** — DONE. `DeploySoulsOnBoatDisplay()` finds the canvas, gets `SoulDisplaySlotManager`, stores it in `_data.soulDisplaySlotManager`, and wires `slotManager`+`iconParent` via SerializedObject. Generate also re-wires both fields.

---

## Segment ID Auto-Generation

```
Main river:   Main_00, Main_01, Main_02  (split by junctions)
Named branch: {riverName}_Left, {riverName}_Right
Unnamed:      Branch_00, Branch_01
```
User can override in left panel at any time.

---

## Scene-Level Features

### LandscapeTool
Drives world landscape mesh shader with up to 100 hill handle positions (`_HillPositions`, `_PointCount`, `_GlobalHeight`). Persistent scene object — not regenerated.

- Added to LEVELSELECTPREFABPACKAGE
- Drag instance into "Landscape Tool" slot in designer left panel
- Designer data stores the reference; Respawn skips it
- Hill handles (child GOs of `hillHandlesParent`) tweaked manually

### Shop (planned — TBD)
`ShopEnd` node type is reserved in the data model. Instantiates a shop prefab at the path endpoint on generate. Gameplay wiring (Orbs of Omalon spending, UI) implemented when delivery mechanic is decided.

### Intro Sequence (planned — TBD)
No scripts exist yet. Will be a scene-specific cinematic triggered by `LevelSelectDataController` on first visit. Reserved as a future touchpoint — no designer fields needed now.

---

## New Scene Setup (migration)

1. Place **LEVELSELECTPREFABPACKAGE** in new scene (Scripts, Audio, Canvas, Boat, Camera intact)
2. Delete children of `RIVERJUNCTIONS`, `BoatPathManager`, `RIVERGATEsobstacles`, `MAINRIVERVISUALS`, `RIVERBRANCHES`
3. Leave arena objects in place
4. Open Level Select Designer, load/create a `LevelSelectDesignerData` asset

---

## Post-Generation Workflow

1. Draw paths, mark junctions/arenas/obstacles/shop in designer canvas
2. Click **Generate**
3. Inspector → SplineRiverManager → **Stitch**
4. Inspector → SplinePathStitcher → **Bake Paths** (auto-calls AutoDetectPaths)
5. Tweak junction SplineRiverJunctionNodeV2 settings as needed
6. **Respawn** to re-generate after canvas edits

---

## Critical Files

| File | Why |
|---|---|
| `Assets/Editor/GridDesignerWindow.cs` | Panel layout, undo, EditorPrefs patterns |
| `Assets/Editor/SplineTools.cs` | Gap calc: `DoSplit()`, `MeasurePrefabXExtent()`, `SetupJunctionInstantiate()`, `BuildSplineFromPositions()` |
| `Assets/ScriptsData/LevelSelectScripts/SplineRiverManager.cs` | `_sourceSegments` field name |
| `Assets/ScriptsData/LevelSelectScripts/SplinePathSticher.cs` | `_sourceSegments` field name |
| `Assets/ScriptsData/LevelSelectScripts/SplineRiverJunctionNodeV2.cs` | JunctionSide fields: `segmentID`, `switchKey`, `autoReturn` |
| `Assets/ScriptsData/LevelSelectScripts/RiverSegmentID.cs` | All fields to set on generated components |
| `Assets/ScriptsData/LevelSelectScripts/LevelSelectObstacleManager.cs` | `obstacleID`, `nextObstacleTransform` fields |
| `Assets/ScriptsData/LevelSelectScripts/LandscapeTool.cs` | `hillHandlesParent`, `heightMultiplier` — understand before wiring reference |

---

## Verification

1. Create LevelSelectDesignerData asset
2. Draw main path + two branches, mark junction, mark obstacles, assign arenas
3. Click Generate — verify MAINRIVERVISUALS, RIVERJUNCTIONS, RIVERGATEsobstacles populated
4. Verify SplineInstantiate river blocks appear with gap at junction
5. Run Stitch + BakePaths — no console errors
6. Verify _sourceSegments on both managers populated
7. Verify obstacle chain (nextObstacleTransform) wired correctly
8. Play mode — boat travels paths, junction works, obstacle blocks progress
