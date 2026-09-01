using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

class GridSnapshot
{
    public int[] squareGrid;
    public int[] circleGrid;
    public List<GridData.ArenaEntrance>         entrances;
    public List<int>                            orbIndices;
    public List<GridData.SoulSpawnPoint>        soulSpawnPoints;
    public List<int>                            waterLevelModifierIndices;
    public List<int>                            waveModifierIndices;
    public List<int[]>                          tierCells;
    public List<GridData.PrefabPlacement>       prefabPlacements;
    public List<List<GridData.PrefabPlacement>> tierPrefabPlacements;
    public List<GridData.LinkedPrefabPair>      linkedPairs;
    public List<GridData.ArenaWaterModifier>    arenaWaterModifiers; // set after construction
    // Also set after construction (see PushUndoSnapshot) — these GridData collections must be
    // captured here or the UNDO button won't restore them. Any NEW GridData collection a designer
    // feature adds needs a field + Copy* helper here, capture in PushUndoSnapshot, and restore in
    // UndoLastAction. (Saving is handled separately — Save As clones the whole asset.)
    public List<GridData.SoulZone>       soulZones;
    public List<GridData.WhirlpoolPoint> whirlpools;
    public List<GridData.SplineWallPath> splineWallPaths;
    public List<GridData.CubeBuilding>   cubeBuildings;
    public List<GridData.ProceduralSpike> proceduralSpikes;
    public List<UnityEngine.Vector2>     orbPositions;   // free-positioned orbs (set after construction)

    public GridSnapshot(int[] square, int[] circle,
                        List<GridData.ArenaEntrance> ents,
                        List<int> orbs, List<GridData.SoulSpawnPoint> souls,
                        List<int> waterMods, List<int> waveMods,
                        List<GridData.GridTier> tiers,
                        List<GridData.PrefabPlacement> basePrefabs,
                        List<GridData.LinkedPrefabPair> links)
    {
        squareGrid = (int[])square.Clone();
        circleGrid = (int[])circle.Clone();

        entrances = new List<GridData.ArenaEntrance>();
        if (ents != null)
            foreach (var e in ents)
                entrances.Add(new GridData.ArenaEntrance
                {
                    id = e.id, prefab = e.prefab, soulPrefab = e.soulPrefab,
                    perimeterAngle = e.perimeterAngle, tierSlot = e.tierSlot,
                    spawnRadius = e.spawnRadius,
                    isLocked = e.isLocked, lockHubPrefab = e.lockHubPrefab,
                    lockHubAngle = e.lockHubAngle,
                    tubeSubdivisions = e.tubeSubdivisions,
                    tubePath = e.tubePath != null
                        ? new List<UnityEngine.Vector2Int>(e.tubePath)
                        : new List<UnityEngine.Vector2Int>()
                });

        orbIndices = new List<int>(orbs);
        soulSpawnPoints = new List<GridData.SoulSpawnPoint>();
        foreach (var s in souls)
            soulSpawnPoints.Add(new GridData.SoulSpawnPoint { cellIndex = s.cellIndex, soulData = s.soulData });
        waterLevelModifierIndices = new List<int>(waterMods);
        waveModifierIndices       = new List<int>(waveMods);

        tierCells            = new List<int[]>();
        tierPrefabPlacements = new List<List<GridData.PrefabPlacement>>();
        if (tiers != null)
            foreach (var t in tiers)
            {
                tierCells.Add(t.cells != null ? (int[])t.cells.Clone() : new int[GridData.CellCount]);
                tierPrefabPlacements.Add(CopyPlacements(t.prefabPlacements));
            }

        prefabPlacements = CopyPlacements(basePrefabs);
        linkedPairs = new List<GridData.LinkedPrefabPair>();
        if (links != null)
            foreach (var lp in links)
                linkedPairs.Add(new GridData.LinkedPrefabPair
                {
                    modifierCellIndex  = lp.modifierCellIndex,
                    modifierTierIndex  = lp.modifierTierIndex,
                    inputTubeCellIndex = lp.inputTubeCellIndex,
                    inputTubeTierIndex = lp.inputTubeTierIndex,
                    tubeSubdivisions   = lp.tubeSubdivisions,
                    // Deep-copy the node list so undo snapshots are independent of live edits.
                    tubePath = lp.tubePath != null
                        ? new List<UnityEngine.Vector2Int>(lp.tubePath)
                        : null
                });
    }

    public static List<GridData.ArenaWaterModifier> CopyWaterMods(List<GridData.ArenaWaterModifier> src)
    {
        var copy = new List<GridData.ArenaWaterModifier>();
        if (src == null) return copy;
        foreach (var wm in src)
            copy.Add(new GridData.ArenaWaterModifier
            {
                id               = wm.id,
                prefab           = wm.prefab,
                perimeterAngle   = wm.perimeterAngle,
                tierSlot         = wm.tierSlot,
                spawnRadius      = wm.spawnRadius,
                tubeSubdivisions = wm.tubeSubdivisions,
                tubePath = wm.tubePath != null
                    ? new List<UnityEngine.Vector2Int>(wm.tubePath)
                    : new List<UnityEngine.Vector2Int>()
            });
        return copy;
    }

    public static List<GridData.PrefabPlacement> CopyPlacements(List<GridData.PrefabPlacement> src)
    {
        var copy = new List<GridData.PrefabPlacement>();
        if (src == null) return copy;
        foreach (var p in src)
            copy.Add(new GridData.PrefabPlacement
            {
                cellIndex                = p.cellIndex,
                position                 = p.position,
                freePlaced               = p.freePlaced,
                prefab                   = p.prefab,
                isCircle                 = p.isCircle,
                isWorldSpaceProp         = p.isWorldSpaceProp,
                scale                    = p.scale,
                rotationOffset           = p.rotationOffset,
                spikePreset              = p.spikePreset,
                statueId                 = p.statueId,
                overrideModifierSettings = p.overrideModifierSettings,
                speedBoost               = p.speedBoost,
                frequencyBoost           = p.frequencyBoost,
                rippleDepthBoost         = p.rippleDepthBoost
            });
        return copy;
    }

    // Deep-copy of the soul zones (incl. fish-bowl tributaries + street lights) so an UNDO restores
    // the full zone state independently of the live data. souls are asset refs (shallow is correct).
    public static List<GridData.SoulZone> CopySoulZones(List<GridData.SoulZone> src)
    {
        var copy = new List<GridData.SoulZone>();
        if (src == null) return copy;
        foreach (var z in src)
        {
            if (z == null) { copy.Add(null); continue; }
            var c = new GridData.SoulZone
            {
                zoneRole           = z.zoneRole,
                zoneId             = z.zoneId,
                adjoinZoneId       = z.adjoinZoneId,
                adjoinNodeIndex    = z.adjoinNodeIndex,
                entryEntranceIndex = z.entryEntranceIndex,
                exitEntranceIndex  = z.exitEntranceIndex,
                externalSourceKey  = z.externalSourceKey,
                externalTargetKey  = z.externalTargetKey,
                nodes              = z.nodes != null ? new List<int>(z.nodes) : new List<int>(),
                nodePositions      = z.nodePositions != null ? new List<Vector2>(z.nodePositions) : new List<Vector2>(),
                closedLoop         = z.closedLoop,
                segmentCurved      = z.segmentCurved != null ? new List<bool>(z.segmentCurved) : new List<bool>(),
                radius             = z.radius,
                pathWidth          = z.pathWidth,
                knotCount          = z.knotCount,
                curveResolution    = z.curveResolution,
                souls              = z.souls != null ? new List<SoulData>(z.souls) : new List<SoulData>(),
                statueGuarded      = z.statueGuarded,
                linkedStatueId     = z.linkedStatueId,
                ringRadius         = z.ringRadius,
                towerGuarded       = z.towerGuarded,
            };
            c.streetLights = new List<GridData.SoulZone.StreetLight>();
            if (z.streetLights != null)
                foreach (var sl in z.streetLights)
                    c.streetLights.Add(sl == null ? null
                        : new GridData.SoulZone.StreetLight { nodeIndex = sl.nodeIndex, poolRadius = sl.poolRadius });
            copy.Add(c);
        }
        return copy;
    }

    public static List<GridData.WhirlpoolPoint> CopyWhirlpools(List<GridData.WhirlpoolPoint> src)
    {
        var copy = new List<GridData.WhirlpoolPoint>();
        if (src == null) return copy;
        foreach (var w in src)
            copy.Add(w == null ? null : new GridData.WhirlpoolPoint { cellIndex = w.cellIndex, radius = w.radius });
        return copy;
    }

    public static List<GridData.SplineWallPath> CopySplineWalls(List<GridData.SplineWallPath> src)
    {
        var copy = new List<GridData.SplineWallPath>();
        if (src == null) return copy;
        foreach (var p in src)
        {
            if (p == null) { copy.Add(null); continue; }
            copy.Add(new GridData.SplineWallPath
            {
                nodes                      = p.nodes != null ? new List<Vector2>(p.nodes) : new List<Vector2>(),
                isClosed                   = p.isClosed,
                segmentCurved              = p.segmentCurved != null ? new List<bool>(p.segmentCurved) : new List<bool>(),
                segmentGap                 = p.segmentGap != null ? new List<bool>(p.segmentGap) : new List<bool>(),
                segmentDestructible        = p.segmentDestructible != null ? new List<bool>(p.segmentDestructible) : new List<bool>(),
                tileSpacing                = p.tileSpacing,
                prefabOverride             = p.prefabOverride,
                destructiblePrefabOverride = p.destructiblePrefabOverride,
                wallHeight                 = p.wallHeight,
                depthBelowWater            = p.depthBelowWater,
                wallThickness              = p.wallThickness,
                nodeHeights                = p.nodeHeights != null ? new List<float>(p.nodeHeights) : new List<float>(),
                nodeSizeScale              = p.nodeSizeScale,
                pathScale                  = p.pathScale,
            });
        }
        return copy;
    }

    public static List<GridData.CubeBuilding> CopyCubeBuildings(List<GridData.CubeBuilding> src)
    {
        var copy = new List<GridData.CubeBuilding>();
        if (src == null) return copy;
        foreach (var b in src)
            copy.Add(b == null ? null : new GridData.CubeBuilding
            {
                center           = b.center,
                width            = b.width,
                length           = b.length,
                heightAboveWater = b.heightAboveWater,
                depthBelowWater  = b.depthBelowWater,
                steppedTop       = b.steppedTop,
            });
        return copy;
    }

    public static List<GridData.ProceduralSpike> CopySpikes(List<GridData.ProceduralSpike> src)
    {
        var copy = new List<GridData.ProceduralSpike>();
        if (src == null) return copy;
        foreach (var s in src)
            copy.Add(s == null ? null : new GridData.ProceduralSpike
            {
                center          = s.center,
                preset          = s.preset,
                scale           = s.scale,
                climbable       = s.climbable,
                angelPerchPoint = s.angelPerchPoint,
                angelPerchRadius   = s.angelPerchRadius,
                angelLandingCurveSize = s.angelLandingCurveSize,
                angelTalkRadius    = s.angelTalkRadius,
                angelPriorityPerch = s.angelPriorityPerch,
                angelTalkEnabled   = s.angelTalkEnabled,
                angelTalkText      = s.angelTalkText,
            });
        return copy;
    }
}

public class GridDesignerWindow : EditorWindow
{
    const int GridSize      = GridData.GridSize;
    const int CellCount     = GridData.CellCount;
    const int CellSize      = 18;
    const int GridPixelSize = GridSize * CellSize;
    const string GridDataFolder = "Assets/Resources/Levels";

    int[] squareGrid = new int[CellCount];
    int[] circleGrid = new int[CellCount];

    List<Color>  slotColors = new List<Color>();
    List<string> slotNotes  = new List<string>();

    int  activeSlot;
    bool drawCircle;
    bool drawOrb;
    bool drawSoul;
    bool drawSoulArea;
    bool drawSelect;
    bool drawWaterLevelModifier;

    // Soul zone drawing state
    int  _activeSoulZoneIndex = -1;
    readonly List<Vector2> _drawingNodes = new List<Vector2>(); // normalized positions being drawn

    // Street-light lamp markers, collected while zones draw and flushed on top afterwards so no
    // zone band can cover another zone's lamp number. (pixel centre, radius, 1-based order number)
    readonly List<(Vector2 pos, float radius, int number)> _lampMarkers = new List<(Vector2, float, int)>();
    int _drawingFirstCell = -1; // cell of the first drawn node, for close-loop detection
    bool _isDrawingSoulArea;

    // Sub-zone junction drawing: extend a tributary's path out from its radius; drop the final
    // node on a Main-Path node to create the junction. -1 = not drawing.
    int _subZoneDrawIdx = -1;

    // Duplicate-carry: press D over a selected prefab, block or spike to clone it; the copy
    // follows the cursor (and stays selected) until a left-click drops it. Escape cancels +
    // removes the copy. Which list the copy went into decides how it's moved and un-made.
    enum CarryKind { Prefab, Cube, Spike }
    bool      _carryDuplicate;
    CarryKind _carryKind = CarryKind.Prefab;

    // Select tool state
    enum SelectionType
    {
        None,
        SoulZoneNode,
        PrefabPlacement,
        Whirlpool,
        Orb,
        WaterModifier,
        WaveModifier,
        GridSlot,
        SplineWallNode   // index = path index, subIndex = node index within path
    }

    struct SelectionInfo
    {
        public SelectionType type;
        public int           cellIndex;
        public int           tierIndex; // -1 for base
        public int           index;     // index in list (e.g. soul zone index, whirlpool index)
        public int           subIndex;  // e.g. node index within soul zone
        public int           value;     // for GridSlot (slot id)
        public bool          isCircle;  // for GridSlot/Prefab
    }

    SelectionInfo _currentSelection;
    int  _selectedZoneIndex = -1;
    int  _selectedNodeIndex = -1;
    bool _isDraggingNode    = false;
    int  _dragCurrentCell   = -1;
    bool _dragUndoPushed    = false; // one undo snapshot per drag, pushed on first move

    // Bridge mode state
    bool        _isBridgeMode        = false;
    int         _bridgeEndZoneIndex  = -1;
    int         _bridgeEndNodeIndex  = -1;
    readonly List<int> _bridgeNodes  = new List<int>();

    static readonly Color[] ZonePalette =
    {
        new Color(1.0f, 0.55f, 0.0f),
        new Color(0.2f, 0.8f, 1.0f),
        new Color(0.6f, 1.0f, 0.3f),
        new Color(1.0f, 0.3f, 0.7f),
        new Color(0.9f, 0.9f, 0.2f),
        new Color(0.5f, 0.3f, 1.0f),
    };

    // Sub-zones (bowl/statue tributaries) render with the same visual as main paths but in a
    // single, distinct teal so they read as tributaries at a glance.
    static readonly Color SubZoneColor = new Color(0.13f, 0.85f, 0.78f);

    // Colour for a zone's designer visual, chosen by role (main path vs sub-zone).
    Color SoulZoneColor(GridData.SoulZone zone, int zi)
    {
        Color c = (zone != null && zone.zoneRole == GridData.SoulZone.ZoneRole.SubZone)
            ? SubZoneColor
            : ZonePalette[Mathf.Max(0, zi) % ZonePalette.Length];
        c.a = 1f;
        return c;
    }
    bool drawWaveModifier;
    int        activeTierIndex  = -1; // -1 = base layer
    List<bool> tierVisible      = new List<bool>();
    bool       baseLayerVisible = true;

    float[] cachedTierYOffsets; // pulled from LevelSpawner in scene

    // ── Direct Prefab Library ──
    enum PrefabLibraryTab { MazePieces, SetPieces, Statues, Modifiers, BadGuys }
    PrefabLibraryTab              _prefabLibTab       = PrefabLibraryTab.MazePieces;
    string                        prefabFolderPath    = "Assets/Prefab/MazePieces";
    string                        iconsFolderPath     = "";
    List<GameObject>              scannedPrefabs      = new List<GameObject>();
    // Prefab library for the spline-wall Type dropdown — every prefab in this folder is an option.
    const string                  SplineWallPrefabFolder = "Assets/Prefab/SplineWallPrefabs";
    // NonSerialized so it resets to null across domain reloads — an EditorWindow deserializes a
    // plain List field as an empty (non-null) list, which would defeat a null-guarded cache.
    [System.NonSerialized] List<GameObject> _splineWallPrefabOptions;
    Dictionary<string, Texture2D> prefabIcons         = new Dictionary<string, Texture2D>();
    // Caches the PrefabBaselineAlignment component per prefab asset so the scale-radius
    // overlay does not run GetComponentInChildren every repaint.
    Dictionary<GameObject, PrefabBaselineAlignment> _baselineAlignCache = new Dictionary<GameObject, PrefabBaselineAlignment>();
    int                           selectedPrefabIndex = -1;
    Vector2                       prefabScrollPos;
    List<GameObject>              scannedSetPiecesLib = new List<GameObject>();
    int                           selectedSetPieceIndex = -1;
    Vector2                       setpieceScrollPos;
    const string StatuesPrefabsFolder   = "Assets/Prefab/StatuesPrefabs";
    List<GameObject>              scannedStatuesLib   = new List<GameObject>();
    int                           selectedStatueIndex = -1;
    Vector2                       statueScrollPos;
    const string ModifiersPrefabsFolder = "Assets/Prefab/ModifierPrefabs";
    List<GameObject>              scannedModifiersLib = new List<GameObject>();
    int                           selectedModifierIndex = -1;
    Vector2                       modifierScrollPos;
    const string BadGuysPrefabsFolder   = "Assets/Prefab/BadGuys";
    List<GameObject>              scannedBadGuysLib   = new List<GameObject>();
    int                           selectedBadGuyIndex = -1;
    Vector2                       badGuyScrollPos;

    // The creepy guy carries his own big spike, so on the grid he is drawn as one, tagged "Creep".
    const string CreepIconSource = "BigSpike";
    const string CreepAffix      = "Creeper";
    Dictionary<GameObject, bool> _creepPrefabCache = new Dictionary<GameObject, bool>();

    // ── Creeper hop routes ──
    const float CreeperRouteWidth    = 5f;   // thick lines only
    const float CreeperLampDotRadius = 4f;

    bool showEnemies       = true;
    bool showCreeperRoutes = true;
    bool showPerchPoints   = false;   // show every angel perch's ranges, any tool

    Dictionary<GameObject, bool>      _climbingRockCache = new Dictionary<GameObject, bool>();
    Dictionary<GameObject, bool>      _badGuyPrefabCache = new Dictionary<GameObject, bool>();
    bool                              _showBadGuys       = false;   // placed-bad-guy list, collapsed by default
    List<GridData.PrefabPlacement>    _allPlacements     = new List<GridData.PrefabPlacement>();
    List<GridData.PrefabPlacement>    _climbingRocks     = new List<GridData.PrefabPlacement>();
    List<Vector2>                     _rockPixels        = new List<Vector2>();
    List<bool>                        _rockReached       = new List<bool>();
    Queue<int>                        _rockQueue         = new Queue<int>();
    List<Vector2>                     _lampPixels        = new List<Vector2>();
    List<float>                       _lampRadii         = new List<float>();
    bool                          drawDirectPrefab    = false;
    GameObject                    _activePlacementPrefab;
    bool                          _activePlacementIsWorldSpaceProp = false;
    bool                          showPrefabLibrary   = true;
    bool isDragging;
    int  lastDraggedCellIndex = -1;

    // TypeB Modifier + Input Tube two-click placement
    bool _isWaitingForTubePlacement = false;
    int  _pendingModifierCellIndex  = -1;
    int  _pendingModifierTierIndex  = -1;

    // Spline wall mode
    bool _drawSplineWall      = false;
    bool _showSplineWalls     = true;
    int  _activeSplinePathIdx = 0;
    int  _dragSplinePathIdx   = -1;
    int  _dragSplineNodeIdx   = -1;

    // ── Cube building mode ──
    bool    _drawCubeBuilding   = false;
    bool    _showCubeBuildings  = false;  // foldout collapsed by default
    int     _activeCubeIndex    = -1;   // selected/active block, edited in the panel
    bool    _isDraggingCubeBox  = false; // click-drag rubber-band creating a new block
    Vector2 _cubeDragStartNorm  = Vector2.zero;
    Vector2 _cubeDragCurrentNorm = Vector2.zero;
    int     _dragCubeCenterIndex = -1;  // block being dragged (grabbed anywhere inside its footprint)
    Vector2 _cubeDragOffsetNorm  = Vector2.zero; // centre − grab point, so the block doesn't jump to the cursor

    // ── Procedural spike mode ──
    bool    _drawSpike           = false;
    bool    _showSpikes          = false;  // foldout collapsed by default
    int     _activeSpikeIndex    = -1;   // selected/active spike, edited in the panel
    int     _activeOrbIndex      = -1;   // selected/active free orb (Select tool)
    int     _dragOrbIndex        = -1;   // orb being dragged
    bool    _isDraggingSpike     = false; // click-drag from centre outward, sizing a new spike
    Vector2 _spikeDragStartNorm  = Vector2.zero;
    Vector2 _spikeDragCurrentNorm = Vector2.zero;
    int     _dragSpikeCenterIndex = -1;  // spike being dragged by its centre
    Vector2 _spikeDragOffsetNorm  = Vector2.zero; // centre − grab point, so the spike doesn't jump to the cursor

    // ── Tube path modes ──
    int _tubePlacingEntranceIndex = -1; // -1 = not in placement mode
    int _tubeDrawEntranceIndex    = -1; // -1 = not in edit/drag mode
    int _dragTubeNodeIndex        = -1; // index within tubePath being dragged
    int _selectedTubeNodeIndex    = -1; // highlighted node

    // ── Wave-modifier tube path edit mode (mirrors the lock tube, keyed on linkedPairs) ──
    int _wmTubeDrawPairIdx = -1; // index into loadedData.linkedPairs currently editing (-1 = off)
    int _wmDragTubeNodeIdx = -1; // middle node index being dragged
    int _wmSelTubeNodeIdx  = -1; // highlighted node

    // ── Water-level exit-pipe tube path modes (mirrors the entrance/lock tube, keyed on arenaWaterModifiers) ──
    int _pipeTubePlacingIndex = -1; // index into arenaWaterModifiers being placed (-1 = off)
    int _pipeTubeDrawIndex    = -1; // index into arenaWaterModifiers being edited (-1 = off)

    // ── Grid navigation ──
    float   _gridZoom      = 1f;
    Vector2 _gridPanOffset = Vector2.zero;
    bool    _isPanningGrid = false;
    bool    _spacePanHeld  = false; // Space held over the grid → drag pans like middle mouse
    bool    _gridViewInit  = false; // one-time centring once the viewport size is known

    float EffCell       => CellSize * _gridZoom;
    float ZoomedGridSize => GridPixelSize * _gridZoom;

    // ── Grid display settings (persisted via EditorPrefs) ──
    float _gridLineOpacity    = 1f;
    float _backdropBrightness = 0.08f;
    // Radius of the white opaque selection dot, as a fraction of a grid cell. Drives the generic
    // prefab/node marker AND the procedural-spike/block selection marker, so one setting styles all.
    float _selectionCircleFactor = 0.32f;
    // Gradient resolution (columns) the procedural spikes are drawn at in the designer — higher is
    // smoother, at a little more draw cost.
    int   _spikeDisplayResolution = 8;
    // Radius of the orb cell marker, as a fraction of a grid cell.
    float _orbCircleFactor = 0.35f;
    // When true, a newly drawn object snaps to the centre of the cell under the pointer; when false
    // (default) it drops at the exact pointer position (free placement). Applies to every drawn
    // instance — prefab-library drops, soul-zone nodes, etc.
    bool  _clampToCellWhenDrawing = false;
    // The pointer position (normalised grid space) captured on the click/drag that placed a prefab,
    // used as the drop point when cell clamping is off.
    Vector2 _drawPointerNorm;
    // Per-element colour + fill/outline appearance for every overlay marker (JSON in EditorPrefs).
    GridDesignerStyle _style = new GridDesignerStyle();
    const string PrefKeyGridOpacity     = "GridDesigner_GridLineOpacity";
    const string PrefKeyBackdropBright  = "GridDesigner_BackdropBrightness";
    const string PrefKeySelectionCircle = "GridDesigner_SelectionCircleFactor";
    const string PrefKeySpikeResolution = "GridDesigner_SpikeResolution";
    const string PrefKeyOrbSize         = "GridDesigner_OrbSize";
    const string PrefKeyClampToCell     = "GridDesigner_ClampToCellWhenDrawing";
    const string PrefKeyStyle           = "GridDesigner_Style";

    Stack<GridSnapshot> undoStack = new Stack<GridSnapshot>();
    const int MaxUndoSteps = 50;

    // ── Section foldouts ──
    bool _showLevelIdentity  = true;
    bool _showCamera         = true;
    bool _showWavePresets    = true;
    bool _showSonarGrid      = true;
    bool _showFog = true;
    bool _showEnemy          = true;
    bool _showAngel          = true;
    bool _showTimeTrial      = false;
    bool _showPrefabs        = false;
    bool _showStartRitual    = false;
    bool _showSoulSpawns     = true;
    bool _showWhirlpools     = true;
    bool _showTypeBSettings  = true;
    bool drawWhirlpool       = false;

    // Soul dropdown state (cached per-load)
    SoulData[]   _allSoulData;
    string[]     _soulDropdownLabels;
    GUIStyle     _greyLabelStyle;

    // ── Debug Console ──
    List<string> _debugLog        = new List<string>();
    Vector2      _debugLogScroll;
    Vector2      _rightPanelScroll;
    const int    DebugLogMaxLines  = 200;

    // Set piece picker state
    const string SetPiecesFolder = "Assets/Prefab/SetPieces";
    GameObject[] _scannedSetPieces;
    string[]     _setPieceNames;

    GridData loadedData;

    List<GridData>  discoveredGrids     = new List<GridData>();
    string[]        discoveredGridNames = new string[0];
    int             selectedDiscoveredGridIndex;

    [MenuItem("Tools/Waves/Grid Designer #4")]
    static void Open() => GetWindow<GridDesignerWindow>("Grid Designer");

    void OnEnable()
    {
        wantsMouseMove = true;          // needed so a carried duplicate follows the cursor
        drawSelect = true;              // default tool on open is Select, not the eraser
        activeSlot = -1;
        RefreshDiscoveredGrids();
        prefabFolderPath    = EditorPrefs.GetString("GridDesigner_PrefabFolder", "Assets/Prefab/MazePieces");
        iconsFolderPath     = EditorPrefs.GetString("GridDesigner_IconsFolder",  "");
        _gridLineOpacity        = EditorPrefs.GetFloat(PrefKeyGridOpacity,     1f);
        _backdropBrightness     = EditorPrefs.GetFloat(PrefKeyBackdropBright,  0.08f);
        _selectionCircleFactor  = EditorPrefs.GetFloat(PrefKeySelectionCircle, 0.32f);
        _spikeDisplayResolution = EditorPrefs.GetInt(PrefKeySpikeResolution,   8);
        _orbCircleFactor        = EditorPrefs.GetFloat(PrefKeyOrbSize,         0.35f);
        _clampToCellWhenDrawing = EditorPrefs.GetBool(PrefKeyClampToCell,      false);
        string styleJson = EditorPrefs.GetString(PrefKeyStyle, "");
        if (!string.IsNullOrEmpty(styleJson))
            try { JsonUtility.FromJsonOverwrite(styleJson, _style); } catch { /* keep defaults on bad data */ }
        ScanPrefabFolder();
        ScanSetPiecesLib();
        ScanStatuesLib();
        ScanModifiersLib();
        ScanBadGuysLib();
        LoadPanelWidth();
    }

    void GridLog(string msg)
    {
        _debugLog.Add(msg);
        if (_debugLog.Count > DebugLogMaxLines) _debugLog.RemoveAt(0);
    }

    void PushUndoSnapshot()
    {
        List<int> orbs      = loadedData?.orbCellIndices                 ?? new List<int>();
        List<int> waterMods = loadedData?.waterLevelModifierCellIndices  ?? new List<int>();
        List<int> waveMods  = loadedData?.waveModifierCellIndices        ?? new List<int>();
        List<GridData.SoulSpawnPoint> souls     = loadedData?.soulSpawnPoints ?? new List<GridData.SoulSpawnPoint>();
        List<GridData.ArenaEntrance>  entrances = loadedData?.entrances      ?? new List<GridData.ArenaEntrance>();
        var snap = new GridSnapshot(squareGrid, circleGrid, entrances, orbs, souls, waterMods, waveMods, loadedData?.tiers, loadedData?.prefabPlacements, loadedData?.linkedPairs);
        snap.arenaWaterModifiers = GridSnapshot.CopyWaterMods(loadedData?.arenaWaterModifiers);
        snap.soulZones           = GridSnapshot.CopySoulZones(loadedData?.soulZones);
        snap.whirlpools          = GridSnapshot.CopyWhirlpools(loadedData?.whirlpools);
        snap.splineWallPaths     = GridSnapshot.CopySplineWalls(loadedData?.splineWallPaths);
        snap.cubeBuildings       = GridSnapshot.CopyCubeBuildings(loadedData?.cubeBuildings);
        snap.proceduralSpikes    = GridSnapshot.CopySpikes(loadedData?.proceduralSpikes);
        snap.orbPositions        = loadedData?.orbPositions != null
                                   ? new List<Vector2>(loadedData.orbPositions) : new List<Vector2>();
        undoStack.Push(snap);
        if (undoStack.Count > MaxUndoSteps) undoStack.TrimExcess();
    }

    void UndoLastAction()
    {
        if (undoStack.Count == 0) return;
        GridSnapshot snapshot = undoStack.Pop();
        squareGrid = (int[])snapshot.squareGrid.Clone();
        circleGrid = (int[])snapshot.circleGrid.Clone();
        if (loadedData != null)
        {
            loadedData.entrances      = snapshot.entrances ?? new List<GridData.ArenaEntrance>();
            loadedData.arenaWaterModifiers = GridSnapshot.CopyWaterMods(snapshot.arenaWaterModifiers);
            loadedData.orbCellIndices = new List<int>(snapshot.orbIndices);
            loadedData.orbPositions   = snapshot.orbPositions != null
                                        ? new List<Vector2>(snapshot.orbPositions) : new List<Vector2>();
            loadedData.waterLevelModifierCellIndices = new List<int>(snapshot.waterLevelModifierIndices);
            loadedData.waveModifierCellIndices       = new List<int>(snapshot.waveModifierIndices);
            
            loadedData.linkedPairs = snapshot.linkedPairs != null 
                ? new List<GridData.LinkedPrefabPair>(snapshot.linkedPairs) 
                : new List<GridData.LinkedPrefabPair>();

            if (loadedData.tiers != null && snapshot.tierCells != null)
                for (int i = 0; i < Mathf.Min(loadedData.tiers.Count, snapshot.tierCells.Count); i++)
                {
                    loadedData.tiers[i].cells = (int[])snapshot.tierCells[i].Clone();
                    if (snapshot.tierPrefabPlacements != null && i < snapshot.tierPrefabPlacements.Count)
                        loadedData.tiers[i].prefabPlacements = GridSnapshot.CopyPlacements(snapshot.tierPrefabPlacements[i]);
                }

            loadedData.prefabPlacements = GridSnapshot.CopyPlacements(snapshot.prefabPlacements);
            loadedData.soulSpawnPoints = new List<GridData.SoulSpawnPoint>();
            foreach (var s in snapshot.soulSpawnPoints)
                loadedData.soulSpawnPoints.Add(new GridData.SoulSpawnPoint { cellIndex = s.cellIndex, soulData = s.soulData });

            // Soul zones (incl. fish-bowl tributaries + street lights), whirlpools, spline walls and
            // cube buildings — restored here so the UNDO button covers them, not just Unity's Ctrl+Z.
            if (snapshot.soulZones      != null) loadedData.soulZones       = GridSnapshot.CopySoulZones(snapshot.soulZones);
            if (snapshot.whirlpools     != null) loadedData.whirlpools      = GridSnapshot.CopyWhirlpools(snapshot.whirlpools);
            if (snapshot.splineWallPaths!= null) loadedData.splineWallPaths = GridSnapshot.CopySplineWalls(snapshot.splineWallPaths);
            if (snapshot.cubeBuildings  != null) loadedData.cubeBuildings   = GridSnapshot.CopyCubeBuildings(snapshot.cubeBuildings);
            if (snapshot.proceduralSpikes != null) loadedData.proceduralSpikes = GridSnapshot.CopySpikes(snapshot.proceduralSpikes);
        }
        Repaint();
    }

    void OnGUI()
    {
        // Draw-mode hotkeys are handled first, before any panel/control can intercept the
        // key, so Enter/Escape/Delete always act on the centre draw window.
        HandleModeHotkeys(Event.current);

        DrawToolbar();

        EditorGUILayout.BeginHorizontal();
        DrawLeftPanel();
        DrawPanelResizeHandle();
        DrawRightPanel();
        EditorGUILayout.EndHorizontal();
    }

    // ── Centralised draw-mode hotkeys ───────────────────────────────────────────
    // Single source of truth for Enter/Escape/Delete while a draw mode is active.
    // Runs first in OnGUI so nothing downstream can steal the key. Bails while a panel
    // text field is being edited — and clicking the grid clears that focus (see DrawGrid),
    // so interacting with the centre window makes it own all key commands.
    // Modes are mutually exclusive; the first matching arm handles and consumes the event.
    void HandleModeHotkeys(Event e)
    {
        if (loadedData == null || e.type != EventType.KeyDown) return;
        if (EditorGUIUtility.editingTextField) return;

        bool enter  = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
        bool escape = e.keyCode == KeyCode.Escape;
        bool delete = e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace;
        if (!enter && !escape && !delete) return;

        // Carried duplicate — Escape cancels and removes the copy.
        if (_carryDuplicate && escape)
        {
            if (_carryKind == CarryKind.Cube)
            {
                if (loadedData.cubeBuildings != null && _activeCubeIndex >= 0 && _activeCubeIndex < loadedData.cubeBuildings.Count)
                    loadedData.cubeBuildings.RemoveAt(_activeCubeIndex);
                _activeCubeIndex = -1;
            }
            else if (_carryKind == CarryKind.Spike)
            {
                if (loadedData.proceduralSpikes != null && _activeSpikeIndex >= 0 && _activeSpikeIndex < loadedData.proceduralSpikes.Count)
                    loadedData.proceduralSpikes.RemoveAt(_activeSpikeIndex);
                _activeSpikeIndex = -1;
            }
            else if (_currentSelection.type == SelectionType.PrefabPlacement)
            {
                var list = _currentSelection.tierIndex == -1 ? loadedData.prefabPlacements
                         : (loadedData.tiers != null && _currentSelection.tierIndex >= 0 && _currentSelection.tierIndex < loadedData.tiers.Count
                            ? loadedData.tiers[_currentSelection.tierIndex].prefabPlacements : null);
                if (list != null && _currentSelection.index >= 0 && _currentSelection.index < list.Count)
                    list.RemoveAt(_currentSelection.index);
                ClearSelectState();
            }
            _carryDuplicate = false;
            EditorUtility.SetDirty(loadedData);
            e.Use(); Repaint();
            return;
        }

        // Soul area draw — Enter commits the node path, Escape cancels.
        if (_isDrawingSoulArea)
        {
            if (enter)
            {
                if (_activeSoulZoneIndex >= 0 && _activeSoulZoneIndex < loadedData.soulZones.Count)
                    CommitDrawingNodes(loadedData.soulZones[_activeSoulZoneIndex]);
                e.Use(); Repaint();
            }
            else if (escape) { CancelDrawingNodes(); e.Use(); Repaint(); }
            return;
        }

        // Tube placement (waiting to click a cell) — Escape cancels.
        if (_isWaitingForTubePlacement)
        {
            if (escape)
            {
                _isWaitingForTubePlacement = false;
                _pendingModifierCellIndex = -1;
                GridLog("Cancelled tube placement.");
                e.Use(); Repaint();
            }
            return;
        }

        // Lock tube place/edit modes — Enter or Escape exits.
        if (_tubePlacingEntranceIndex >= 0 || _tubeDrawEntranceIndex >= 0)
        {
            if (enter || escape)
            {
                _tubePlacingEntranceIndex = -1;
                _tubeDrawEntranceIndex    = -1;
                _dragTubeNodeIndex        = -1;
                _selectedTubeNodeIndex    = -1;
                e.Use(); Repaint();
            }
            return;
        }

        // Wave-modifier tube edit — Enter or Escape exits.
        if (_wmTubeDrawPairIdx >= 0)
        {
            if (enter || escape)
            {
                _wmTubeDrawPairIdx = -1;
                _wmDragTubeNodeIdx = -1;
                _wmSelTubeNodeIdx  = -1;
                e.Use(); Repaint();
            }
            return;
        }

        // Sub-zone junction drawing — Enter or Escape finishes (keeps whatever was placed).
        if (_subZoneDrawIdx >= 0)
        {
            if (enter || escape) { _subZoneDrawIdx = -1; e.Use(); Repaint(); }
            return;
        }

        // Spline wall mode — Escape exits, Delete removes the last node on the active path.
        if (_drawSplineWall)
        {
            if (escape) { _drawSplineWall = false; e.Use(); Repaint(); }
            else if (delete && loadedData.splineWallPaths != null
                     && _activeSplinePathIdx < loadedData.splineWallPaths.Count)
            {
                var activePath = loadedData.splineWallPaths[_activeSplinePathIdx];
                if (activePath.nodes != null && activePath.nodes.Count > 0)
                {
                    Undo.RecordObject(loadedData, "Delete Spline Wall Node");
                    int lastIdx = activePath.nodes.Count - 1;
                    activePath.nodes.RemoveAt(lastIdx);
                    if (activePath.segmentCurved != null && lastIdx < activePath.segmentCurved.Count)
                        activePath.segmentCurved.RemoveAt(lastIdx);
                    if (activePath.segmentGap != null && lastIdx < activePath.segmentGap.Count)
                        activePath.segmentGap.RemoveAt(lastIdx);
                    if (activePath.segmentDestructible != null && lastIdx < activePath.segmentDestructible.Count)
                        activePath.segmentDestructible.RemoveAt(lastIdx);
                    EditorUtility.SetDirty(loadedData);
                    e.Use(); Repaint();
                }
            }
            return;
        }

        // Cube building mode — Escape exits, Delete removes the active block.
        if (_drawCubeBuilding)
        {
            if (escape) { _activeCubeIndex = -1; _drawCubeBuilding = false; _drawSpike = false; e.Use(); Repaint(); }
            else if (delete && loadedData.cubeBuildings != null
                     && _activeCubeIndex >= 0 && _activeCubeIndex < loadedData.cubeBuildings.Count)
            {
                Undo.RecordObject(loadedData, "Delete Cube Building");
                loadedData.cubeBuildings.RemoveAt(_activeCubeIndex);
                _activeCubeIndex = -1;
                EditorUtility.SetDirty(loadedData);
                e.Use(); Repaint();
            }
            return;
        }

        // Spike mode — Escape exits, Delete removes the active spike.
        if (_drawSpike)
        {
            if (escape) { _activeSpikeIndex = -1; _drawSpike = false; e.Use(); Repaint(); }
            else if (delete && loadedData.proceduralSpikes != null
                     && _activeSpikeIndex >= 0 && _activeSpikeIndex < loadedData.proceduralSpikes.Count)
            {
                Undo.RecordObject(loadedData, "Delete Spike");
                loadedData.proceduralSpikes.RemoveAt(_activeSpikeIndex);
                _activeSpikeIndex = -1;
                EditorUtility.SetDirty(loadedData);
                e.Use(); Repaint();
            }
            return;
        }

        // Select tool — Escape deselects, Delete removes the selected item.
        if (drawSelect)
        {
            if (escape) { ClearSelectState(); e.Use(); Repaint(); return; }

            // A spike picked with the Select tool is tracked by _activeSpikeIndex, not as a
            // SelectionType, so Delete it here too (otherwise the selection-type switch below
            // would ignore it and the spike couldn't be deleted without switching tools). Gated on
            // no other selection being active, so a selected prefab/node's Delete isn't stolen by a
            // stale active spike — the same rule the spike selection marker uses.
            if (delete && _currentSelection.type == SelectionType.None
                && _activeSpikeIndex >= 0 && loadedData.proceduralSpikes != null
                && _activeSpikeIndex < loadedData.proceduralSpikes.Count)
            {
                Undo.RecordObject(loadedData, "Delete Spike");
                loadedData.proceduralSpikes.RemoveAt(_activeSpikeIndex);
                _activeSpikeIndex = -1;
                EditorUtility.SetDirty(loadedData);
                e.Use(); Repaint(); return;
            }

            // A free orb picked with the Select tool is tracked by _activeOrbIndex — same rule.
            if (delete && _currentSelection.type == SelectionType.None
                && _activeOrbIndex >= 0 && loadedData.orbPositions != null
                && _activeOrbIndex < loadedData.orbPositions.Count)
            {
                Undo.RecordObject(loadedData, "Delete Orb");
                loadedData.orbPositions.RemoveAt(_activeOrbIndex);
                _activeOrbIndex = -1;
                EditorUtility.SetDirty(loadedData);
                e.Use(); Repaint(); return;
            }

            if (delete && _currentSelection.type != SelectionType.None)
            {
                Undo.RecordObject(loadedData, "Delete Selection");
                PushUndoSnapshot();

                switch (_currentSelection.type)
                {
                    case SelectionType.SoulZoneNode:
                        var zone = loadedData.soulZones[_currentSelection.index];
                        if (zone.nodePositions != null && _currentSelection.subIndex < zone.nodePositions.Count)
                        {
                            zone.nodePositions.RemoveAt(_currentSelection.subIndex);
                            SoulZoneNodeDeleted(zone, _currentSelection.subIndex);
                        }
                        break;
                    case SelectionType.PrefabPlacement:
                        var placements = _currentSelection.tierIndex == -1 ? loadedData.prefabPlacements : loadedData.tiers[_currentSelection.tierIndex].prefabPlacements;
                        var removedPlacement = _currentSelection.index < placements.Count ? placements[_currentSelection.index] : null;
                        placements.RemoveAt(_currentSelection.index);
                        // Also drop any statue/tower guarded soul zone linked to this placement.
                        if (removedPlacement != null && removedPlacement.statueId != 0 && loadedData.soulZones != null)
                            loadedData.soulZones.RemoveAll(z =>
                                (z.statueGuarded || z.towerGuarded) && z.linkedStatueId == removedPlacement.statueId);
                        break;
                    case SelectionType.Whirlpool:
                        loadedData.whirlpools.RemoveAt(_currentSelection.index);
                        break;
                    case SelectionType.Orb:
                        loadedData.orbCellIndices.Remove(_currentSelection.cellIndex);
                        break;
                    case SelectionType.WaterModifier:
                        var waterMods = _currentSelection.tierIndex == -1 ? loadedData.waterLevelModifierCellIndices : loadedData.tiers[_currentSelection.tierIndex].waterLevelModifierCellIndices;
                        waterMods.Remove(_currentSelection.cellIndex);
                        break;
                    case SelectionType.WaveModifier:
                        var waveMods = _currentSelection.tierIndex == -1 ? loadedData.waveModifierCellIndices : loadedData.tiers[_currentSelection.tierIndex].waveModifierCellIndices;
                        waveMods.Remove(_currentSelection.cellIndex);
                        break;
                    case SelectionType.GridSlot:
                        if (_currentSelection.tierIndex >= 0) loadedData.tiers[_currentSelection.tierIndex].cells[_currentSelection.cellIndex] = 0;
                        else if (_currentSelection.isCircle) circleGrid[_currentSelection.cellIndex] = 0;
                        else squareGrid[_currentSelection.cellIndex] = 0;
                        break;
                    case SelectionType.SplineWallNode:
                        if (loadedData.splineWallPaths != null
                            && _currentSelection.index < loadedData.splineWallPaths.Count)
                        {
                            var wp = loadedData.splineWallPaths[_currentSelection.index];
                            int delIdx = _currentSelection.subIndex;
                            if (wp.nodes != null && delIdx < wp.nodes.Count)
                            {
                                wp.nodes.RemoveAt(delIdx);
                                if (wp.segmentCurved != null && delIdx < wp.segmentCurved.Count)
                                    wp.segmentCurved.RemoveAt(delIdx);
                                if (wp.segmentGap != null && delIdx < wp.segmentGap.Count)
                                    wp.segmentGap.RemoveAt(delIdx);
                                if (wp.segmentDestructible != null && delIdx < wp.segmentDestructible.Count)
                                    wp.segmentDestructible.RemoveAt(delIdx);
                            }
                        }
                        break;
                }

                ClearSelectState();
                EditorUtility.SetDirty(loadedData);
                e.Use(); Repaint();
            }
            return;
        }
    }

    void DrawToolbar()
    {
        // ── Row 1: Level identity bar ────────────────────────────────────────
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        string levelLabel = loadedData != null
            ? $"  ▣  {(string.IsNullOrEmpty(loadedData.displayName) ? loadedData.name : loadedData.displayName)}  [{loadedData.levelID}]"
            : "  ▣  No level loaded";

        GUIStyle levelStyle = new GUIStyle(EditorStyles.boldLabel)
            { normal = { textColor = loadedData != null ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.5f, 0.5f) } };
        GUILayout.Label(levelLabel, levelStyle);

        GUILayout.FlexibleSpace();

        // Load button
        if (GUILayout.Button("Load…", EditorStyles.toolbarButton, GUILayout.Width(52)))
        {
            string path = EditorUtility.OpenFilePanel("Select GridData", "Assets", "asset");
            if (!string.IsNullOrEmpty(path))
            {
                path = FileUtil.GetProjectRelativePath(path);
                var data = AssetDatabase.LoadAssetAtPath<GridData>(path);
                if (data != null) LoadGrid(data);
            }
        }

        if (loadedData != null && GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(42)))
            SaveGridInPlace();

        EditorGUILayout.EndHorizontal();
    }

    void DrawToolButtons()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        SetToolbarButton("⊕ Select",  drawSelect,    new Color(0.4f,0.8f,1f),  () => { activeSlot = -1; _drawSplineWall = false; _drawCubeBuilding = false; _drawSpike = false; drawSelect = true; drawSoulArea = drawSoul = drawCircle = drawOrb = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = false; });
        SetToolbarButton("★ Soul",    drawSoulArea,  Color.yellow,             () => { activeSlot = -1; _drawSplineWall = false; _drawCubeBuilding = false; _drawSpike = false; drawSoulArea = true; drawSelect = drawSoul = drawCircle = drawOrb = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = false; ClearSelectState(); LogSelection(_currentSelection); });
        SetToolbarButton("◎ Orb",     drawOrb,       Color.white,              () => { activeSlot = -1; _drawSplineWall = false; _drawCubeBuilding = false; _drawSpike = false; drawOrb = true; drawCircle = drawSoul = drawSoulArea = drawWaterLevelModifier = drawWaveModifier = drawWhirlpool = false; ClearSelectState(); });
        SetToolbarButton("〇 Whirl",  drawWhirlpool, new Color(0.7f,0.4f,1f), () => { activeSlot = -1; _drawSplineWall = false; _drawCubeBuilding = false; _drawSpike = false; drawWhirlpool = true; drawCircle = drawOrb = drawSoul = drawSoulArea = drawWaterLevelModifier = drawWaveModifier = false; ClearSelectState(); });
        SetToolbarButton("✕ Eraser", activeSlot == 0, new Color(1f,0.5f,0.5f), () => { activeSlot = 0; _drawSplineWall = false; _drawCubeBuilding = false; _drawSpike = false; drawCircle = drawOrb = drawSoul = drawSoulArea = drawWaterLevelModifier = drawWaveModifier = drawWhirlpool = drawDirectPrefab = drawSelect = false; ClearSelectState(); LogSelection(_currentSelection); _isWaitingForTubePlacement = false; });
        SetToolbarButton("≋ Walls",  _drawSplineWall, new Color(1f,0.7f,0.2f), () => { activeSlot = -1; _drawSplineWall = true; _drawCubeBuilding = false; _drawSpike = false; drawSelect = drawSoulArea = drawSoul = drawCircle = drawOrb = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = drawDirectPrefab = false; ClearSelectState(); _isWaitingForTubePlacement = false; });
        SetToolbarButton("▦ Blocks", _drawCubeBuilding, new Color(0.55f,0.55f,0.6f), () => { activeSlot = -1; _drawCubeBuilding = true; _drawSpike = false; _drawSplineWall = false; drawSelect = drawSoulArea = drawSoul = drawCircle = drawOrb = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = drawDirectPrefab = false; ClearSelectState(); _isWaitingForTubePlacement = false; });
        SetToolbarButton("▲ Spikes", _drawSpike, new Color(0.65f,0.55f,0.85f), () => { activeSlot = -1; _drawSpike = true; _drawCubeBuilding = false; _drawSplineWall = false; drawSelect = drawSoulArea = drawSoul = drawCircle = drawOrb = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = drawDirectPrefab = false; ClearSelectState(); _isWaitingForTubePlacement = false; });

        EditorGUILayout.EndHorizontal();

        // Status hints
        GUIStyle hint = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        if (_drawSplineWall)
        {
            hint.normal.textColor = new Color(1f, 0.7f, 0.2f);
            GUILayout.Label("Left-click: place node  |  Drag node: move  |  Right-click node: delete  |  Esc: deselect path", hint);
        }
        else if (_drawCubeBuilding)
        {
            hint.normal.textColor = new Color(0.75f, 0.75f, 0.8f);
            GUILayout.Label("Drag out a box to create  |  Click a box to select  |  Drag centre node to move  |  Edit W/L/H in the Cube Buildings panel  |  Del: remove  |  Esc: exit", hint);
        }
        else if (_drawSpike)
        {
            hint.normal.textColor = new Color(0.75f, 0.65f, 0.95f);
            GUILayout.Label("Drag out from a point to size the rock  |  Click a spike to select  |  Drag centre to move  |  Right-click a rock to toggle CLIMBABLE (green dot)  |  D: duplicate (click to drop)  |  Pick its shape preset in the Spikes panel  |  Del: remove  |  Esc: exit", hint);
        }
        else if (_isDrawingSoulArea || drawSelect || _isWaitingForTubePlacement)
        {
            if (_isWaitingForTubePlacement)
            {
                hint.normal.textColor = Color.cyan;
                GUILayout.Label("Click to place the SoulFishInputTube (link to modifier)", hint);
            }
            else if (_isDrawingSoulArea)
            {
                hint.normal.textColor = Color.yellow;
                GUILayout.Label("Click cells — click first to close loop — Enter to finish — Esc to cancel", hint);
            }
            else if (_selectedZoneIndex >= 0)
            {
                hint.normal.textColor = new Color(0.4f, 0.8f, 1f);
                GUILayout.Label($"Zone {_selectedZoneIndex}  Node {_selectedNodeIndex + 1} — drag to move — Shift+click to bridge — Esc to deselect", hint);
            }
            else
            {
                hint.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
                GUILayout.Label("Click to select — drag to move — click empty to deselect — Del to delete", hint);
            }
        }
    }

    void SetToolbarButton(string label, bool active, Color activeColor, System.Action onClick)
    {
        GUI.backgroundColor = active ? activeColor : Color.white;
        if (GUILayout.Toggle(active, label, EditorStyles.toolbarButton) != active)
            onClick();
        GUI.backgroundColor = Color.white;
    }

    Vector2 _leftPanelScroll;

    float   _leftPanelWidth    = 280f;
    bool    _isResizingPanel   = false;
    float   _rightPanelWidth   = 240f;
    bool    _isResizingRightPanel = false;
    const float PanelMinWidth  = 180f;
    const float PanelMaxWidth  = 600f;
    const float HandleWidth    = 5f;

    void LoadPanelWidth()
    {
        _leftPanelWidth  = EditorPrefs.GetFloat("GridDesigner_LeftPanelWidth",  280f);
        _rightPanelWidth = EditorPrefs.GetFloat("GridDesigner_RightPanelWidth", 240f);
    }

    void SavePanelWidth()
    {
        EditorPrefs.SetFloat("GridDesigner_LeftPanelWidth", _leftPanelWidth);
    }

    void SaveRightPanelWidth()
    {
        EditorPrefs.SetFloat("GridDesigner_RightPanelWidth", _rightPanelWidth);
    }

    void DrawPanelResizeHandle()
    {
        // Reserve a thin vertical strip between the two panels
        Rect handleRect = GUILayoutUtility.GetRect(HandleWidth, HandleWidth,
            GUILayout.Width(HandleWidth), GUILayout.ExpandHeight(true));

        // Tint the handle on hover/drag so it's discoverable
        bool hovering = handleRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(handleRect,
            _isResizingPanel ? new Color(0.4f, 0.7f, 1f, 0.8f) :
            hovering         ? new Color(0.6f, 0.6f, 0.6f, 0.5f) :
                               new Color(0.3f, 0.3f, 0.3f, 0.4f));

        EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);

        switch (Event.current.type)
        {
            case EventType.MouseDown:
                if (handleRect.Contains(Event.current.mousePosition))
                {
                    _isResizingPanel = true;
                    Event.current.Use();
                }
                break;

            case EventType.MouseDrag:
                if (_isResizingPanel)
                {
                    _leftPanelWidth += Event.current.delta.x;
                    _leftPanelWidth  = Mathf.Clamp(_leftPanelWidth, PanelMinWidth, PanelMaxWidth);
                    SavePanelWidth();
                    Repaint();
                    Event.current.Use();
                }
                break;

            case EventType.MouseUp:
                if (_isResizingPanel)
                {
                    _isResizingPanel = false;
                    Event.current.Use();
                }
                break;
        }
    }

    void DrawRightPanelResizeHandle()
    {
        Rect handleRect = GUILayoutUtility.GetRect(HandleWidth, HandleWidth,
            GUILayout.Width(HandleWidth), GUILayout.ExpandHeight(true));

        bool hovering = handleRect.Contains(Event.current.mousePosition);
        EditorGUI.DrawRect(handleRect,
            _isResizingRightPanel ? new Color(0.4f, 0.7f, 1f, 0.8f) :
            hovering              ? new Color(0.6f, 0.6f, 0.6f, 0.5f) :
                                    new Color(0.3f, 0.3f, 0.3f, 0.4f));

        EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal);

        switch (Event.current.type)
        {
            case EventType.MouseDown:
                if (handleRect.Contains(Event.current.mousePosition))
                {
                    _isResizingRightPanel = true;
                    Event.current.Use();
                }
                break;

            case EventType.MouseDrag:
                if (_isResizingRightPanel)
                {
                    _rightPanelWidth -= Event.current.delta.x;
                    _rightPanelWidth  = Mathf.Clamp(_rightPanelWidth, PanelMinWidth, PanelMaxWidth);
                    SaveRightPanelWidth();
                    Repaint();
                    Event.current.Use();
                }
                break;

            case EventType.MouseUp:
                if (_isResizingRightPanel)
                {
                    _isResizingRightPanel = false;
                    Event.current.Use();
                }
                break;
        }
    }


    void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(_leftPanelWidth));
        EnsureSlotCapacity(GetMaxSlotUsed());

        // ── Level file operations — FIXED header, pinned above the scroll so it never scrolls away ──
        EditorGUILayout.LabelField("Existing Levels", EditorStyles.boldLabel);
        if (discoveredGrids.Count > 0)
        {
            selectedDiscoveredGridIndex = EditorGUILayout.Popup(
                selectedDiscoveredGridIndex, discoveredGridNames);
            if (GUILayout.Button("LOAD SELECTED"))
                LoadGrid(discoveredGrids[selectedDiscoveredGridIndex]);
        }
        else
        {
            EditorGUILayout.LabelField("No GridData assets found.");
        }
        if (GUILayout.Button("Refresh Level List")) RefreshDiscoveredGrids();
        EditorGUILayout.Space();
        DrawButtons();
        EditorGUILayout.Space();

        // ── Everything below scrolls ──
        _leftPanelScroll = EditorGUILayout.BeginScrollView(_leftPanelScroll,
            GUILayout.Width(_leftPanelWidth), GUILayout.ExpandHeight(true));

        if (loadedData != null) DrawLevelIdentitySection();

        EditorGUILayout.LabelField("Tiers", EditorStyles.boldLabel);

        if (loadedData != null)
        {
            if (loadedData.tiers == null) loadedData.tiers = new List<GridData.GridTier>();

            RefreshCachedTierOffsets();

            EditorGUILayout.BeginHorizontal();
            baseLayerVisible = EditorGUILayout.Toggle(baseLayerVisible, GUILayout.Width(16));
            GUI.backgroundColor = activeTierIndex == -1 ? Color.cyan : Color.white;
            if (GUILayout.Button("Base Layer")) activeTierIndex = -1;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            while (tierVisible.Count < loadedData.tiers.Count) tierVisible.Add(true);
            while (tierVisible.Count > loadedData.tiers.Count) tierVisible.RemoveAt(tierVisible.Count - 1);

            int tierToRemove = -1;
            for (int i = 0; i < loadedData.tiers.Count; i++)
            {
                var tier = loadedData.tiers[i];
                EditorGUILayout.BeginHorizontal();

                tierVisible[i] = EditorGUILayout.Toggle(tierVisible[i], GUILayout.Width(16));

                GUI.backgroundColor = activeTierIndex == i ? Color.cyan : Color.white;
                if (GUILayout.Button($"T{i + 1}", GUILayout.Width(28))) activeTierIndex = i;
                GUI.backgroundColor = Color.white;

                tier.name = EditorGUILayout.TextField(tier.name, GUILayout.Width(60));

                if (cachedTierYOffsets != null && cachedTierYOffsets.Length > 0)
                {
                    string[] slotLabels = new string[cachedTierYOffsets.Length];
                    for (int s = 0; s < cachedTierYOffsets.Length; s++)
                        slotLabels[s] = $"{WaterLevelModifier.FloorLabel(s, cachedTierYOffsets)} ({cachedTierYOffsets[s]:+0.##;-0.##;0})";
                    int newSlot = EditorGUILayout.Popup(
                        Mathf.Clamp(tier.yOffsetSlot, 0, cachedTierYOffsets.Length - 1),
                        slotLabels, GUILayout.Width(90));
                    if (newSlot != tier.yOffsetSlot) { tier.yOffsetSlot = newSlot; EditorUtility.SetDirty(loadedData); }
                }
                else
                {
                    GUI.enabled = false;
                    EditorGUILayout.FloatField(tier.yOffset, GUILayout.Width(52));
                    GUI.enabled = true;
                }

                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("✕", GUILayout.Width(24))) tierToRemove = i;
                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndHorizontal();
            }

            if (tierToRemove >= 0)
            {
                PushUndoSnapshot();
                loadedData.tiers.RemoveAt(tierToRemove);
                tierVisible.RemoveAt(tierToRemove);
                if (activeTierIndex == tierToRemove) activeTierIndex = -1;
                else if (activeTierIndex > tierToRemove) activeTierIndex--;
                EditorUtility.SetDirty(loadedData);
            }

            if (GUILayout.Button("+ Add Tier"))
            {
                PushUndoSnapshot();
                loadedData.tiers.Add(new GridData.GridTier
                {
                    name    = $"Tier {loadedData.tiers.Count + 1}",
                    yOffset = (loadedData.tiers.Count + 1) * 5f,
                    cells   = new int[GridData.CellCount]
                });
                tierVisible.Add(true);
                EditorUtility.SetDirty(loadedData);
            }

            if (activeTierIndex >= 0 && activeTierIndex < loadedData.tiers.Count)
                EditorGUILayout.HelpBox($"Drawing into: {loadedData.tiers[activeTierIndex].name}", MessageType.None);
            else
                EditorGUILayout.HelpBox("Drawing into: Base Layer", MessageType.None);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Arena", EditorStyles.boldLabel);

        if (loadedData != null)
        {
            // The arena is authored here now. ArenaProfile is gone: it only ever existed to
            // pick between a few fixed sizes and the wall prefab, tiling and marker scales each
            // size needed. ArenaWallsGenerator builds any size, so the level owns these directly
            // and the walls prefab is a single slot on LevelSpawner.
            EditorGUI.BeginChangeCheck();

            float newRadius = EditorGUILayout.FloatField(
                new GUIContent("Arena Radius", "World-units from the arena centre to the inside face of the wall. " +
                                               "The arena walls generator builds the boundary to this, and the wave " +
                                               "and sonar masks cover the water inside it."),
                loadedData.arenaRadius);

            float newWaterY = EditorGUILayout.FloatField(
                new GUIContent("Waterline Y", "Absolute world Y of the waterline the walls rise from. " +
                                              "Also the base height every tier-aligned prefab is placed against."),
                loadedData.waterlineY);

            Vector2 newCentre = EditorGUILayout.Vector2Field(
                new GUIContent("Centre Offset", "XZ offset of the arena centre from world origin. X = world X, Y = world Z."),
                loadedData.arenaCentreOffset);

            Vector2 newTiling = EditorGUILayout.Vector2Field(
                new GUIContent("Map Grid Tiling", "Tiling of the map grid material. Match to the arena size."),
                loadedData.mapGridTiling);

            float newMarkerScale = EditorGUILayout.FloatField(
                new GUIContent("Map Marker Scale", "Scale applied to all maze wall map markers on this level."),
                loadedData.mazeWallMarkerScale);

            float newCoverage = EditorGUILayout.FloatField(
                new GUIContent("Wave Plane Coverage", "How much larger the wave plane is than the arena diameter (e.g. 1.5)."),
                loadedData.wavePlaneCoverageMultiplier);

            GameObject newEntrance = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Entrance Override", "When set, overrides the prefab on every arena entrance in this level."),
                loadedData.entrancePrefabOverride, typeof(GameObject), false);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Edit Arena");
                loadedData.arenaRadius                = Mathf.Max(0f, newRadius);
                loadedData.waterlineY                 = newWaterY;
                loadedData.arenaCentreOffset          = newCentre;
                loadedData.mapGridTiling              = newTiling;
                loadedData.mazeWallMarkerScale        = newMarkerScale;
                loadedData.wavePlaneCoverageMultiplier = newCoverage;
                loadedData.entrancePrefabOverride     = newEntrance;
                EditorUtility.SetDirty(loadedData);
            }

            // World size. Everything else in this tool is normalised to the grid, which is fine
            // until a system that works in absolute units — fog, spike sizes, block depths —
            // needs measuring against the level, and there is nothing on screen to measure with.
            if (loadedData.WorldArenaWidth > 0f)
            {
                float aWidth = loadedData.WorldArenaWidth;
                float aCell  = aWidth / GridData.GridSize;
                EditorGUILayout.LabelField(
                    $"{aWidth:0.##} u across   ·   radius {aWidth * 0.5f:0.##} u" +
                    $"   ·   1 cell = {aCell:0.###} u",
                    EditorStyles.miniLabel);
            }
            else
            {
                // Worth saying: several tools silently fall back to 12 units at radius 0,
                // so a level without one is not neutral, it is quietly wrong.
                EditorGUILayout.HelpBox(
                    "Arena Radius is 0, so world sizes fall back to 12 units and the level spawns " +
                    "no boundary. Anything measured in world units here will be wrong.", MessageType.Warning);
            }

            EditorGUILayout.Space();
            DrawPortalList();

            EditorGUILayout.Space();
            DrawWaterModifierList();
        }
        else
        {
            EditorGUILayout.HelpBox("Load a grid to set arena size.", MessageType.None);
        }

        EditorGUILayout.Space();

        int slotToRemove = -1;

        for (int i = 1; i <= slotColors.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            slotColors[i - 1] = EditorGUILayout.ColorField(slotColors[i - 1], GUILayout.Width(40));
            slotNotes[i]      = EditorGUILayout.TextField(slotNotes[i]);

            if (GUILayout.Button("■", GUILayout.Width(30)))
            { activeSlot = i; drawCircle = drawOrb = drawSoul = false; }

            if (GUILayout.Button("●", GUILayout.Width(30)))
            { activeSlot = i; drawCircle = true; drawOrb = drawSoul = false; }

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(24)))
                slotToRemove = i;
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        // Defer removal to avoid modifying list mid-loop
        if (slotToRemove > 0)
        {
            bool inUse = IsSlotInUse(slotToRemove);
            if (!inUse || EditorUtility.DisplayDialog(
                "Clear Slot",
                $"Slot {slotToRemove} is painted on cells. Clear all and remove?",
                "Clear All", "Cancel"))
            {
                // Zero out all cells using this slot before RemoveSlot remaps
                for (int ci = 0; ci < CellCount; ci++)
                {
                    if (squareGrid[ci] == slotToRemove) squareGrid[ci] = 0;
                    if (circleGrid[ci] == slotToRemove) circleGrid[ci] = 0;
                }
                if (loadedData?.tiers != null)
                    foreach (var tier in loadedData.tiers)
                        if (tier.cells != null)
                            for (int ci = 0; ci < tier.cells.Length; ci++)
                                if (tier.cells[ci] == slotToRemove) tier.cells[ci] = 0;

                RemoveSlot(slotToRemove);
            }
        }


        EditorGUILayout.Space();

        // ── Level Data Sections ──────────────────────────────────────────
        if (loadedData != null)
        {
            DrawCameraSection();
            DrawWavePresetsSection();
            DrawSonarGridSection();
            DrawFogSection();
            // Enemy section removed here — it was inert and now lives on the right panel.
            // Angel section moved to the right panel (above Enemies).
            DrawTimeTrialSection();
            DrawPrefabsSection();
            DrawStartRitualSection();
            DrawWhirlpoolsSection();
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    void ApplyToolToCell(int index)
    {
        if (activeSlot == 0)
        {
            squareGrid[index] = 0;
            circleGrid[index] = 0;
            if (loadedData != null)
            {
                loadedData.orbCellIndices?.Remove(index);
                loadedData.orbPositions?.RemoveAll(p => GridData.NormalizedToCell(p) == index); // free orbs in this cell
                loadedData.waterLevelModifierCellIndices?.Remove(index);
                loadedData.waveModifierCellIndices?.Remove(index);
                loadedData.whirlpools?.RemoveAll(w => w.cellIndex == index);
                loadedData.soulSpawnPoints?.RemoveAll(s => s.cellIndex == index);
                RemoveGuardedZonesForCell(index);
                loadedData.prefabPlacements?.RemoveAll(p => p.cellIndex == index);
                if (loadedData.tiers != null)
                    foreach (var tier in loadedData.tiers)
                    {
                        if (tier.cells != null && index < tier.cells.Length) tier.cells[index] = 0;
                        tier.waterLevelModifierCellIndices?.Remove(index);
                        tier.waveModifierCellIndices?.Remove(index);
                        tier.prefabPlacements?.RemoveAll(p => p.cellIndex == index);
                    }
            }
            return;
        }

        if (drawSoulArea && loadedData != null)
        {
            EnsureSoulZones();
            if (_activeSoulZoneIndex < 0 || _activeSoulZoneIndex >= loadedData.soulZones.Count)
            {
                // No zone selected — create a new one and start drawing
                PushUndoSnapshot();
                var newZone = new GridData.SoulZone();
                loadedData.soulZones.Add(newZone);
                _activeSoulZoneIndex = loadedData.soulZones.Count - 1;
                _drawingNodes.Clear();
                _drawingFirstCell  = -1;
                _isDrawingSoulArea = true;
            }

            if (!_isDrawingSoulArea) return;

            var zone = loadedData.soulZones[_activeSoulZoneIndex];

            // Close loop: 3+ nodes and user clicks the first node's cell again
            if (_drawingNodes.Count >= 3 && index == _drawingFirstCell)
            {
                zone.closedLoop = true;
                CommitDrawingNodes(zone);
                return;
            }

            if (_drawingNodes.Count == 0) _drawingFirstCell = index;
            // Snap the node to the cell centre when clamping is on, else drop it at the exact pointer.
            _drawingNodes.Add(_clampToCellWhenDrawing
                              ? GridData.SoulZone.CellToNormalized(index)
                              : _drawPointerNorm);
            EditorUtility.SetDirty(loadedData);
            Repaint();
            return;
        }

        if (drawOrb && loadedData != null)
        {
            // Orbs are always free-positioned — dropped at the exact pointer, never snapped to the
            // cell centre (they ignore the Clamp-to-cell toggle). A small min-gap stops a drag from
            // spraying a dense stream of orbs.
            if (loadedData.orbPositions == null) loadedData.orbPositions = new List<Vector2>();
            Vector2 op = _drawPointerNorm;
            float minGap = 0.5f / GridData.GridSize;
            bool near = false;
            foreach (var e in loadedData.orbPositions)
                if (Vector2.Distance(e, op) < minGap) { near = true; break; }
            if (!near) loadedData.orbPositions.Add(op);
            return;
        }

        if (drawWaterLevelModifier && loadedData != null)
        {
            var wlList = GetActiveTierWaterModifiers();
            if (wlList.Contains(index)) wlList.Remove(index); else wlList.Add(index);
            return;
        }

        if (drawWaveModifier && loadedData != null)
        {
            var wvList = GetActiveTierWaveModifiers();
            if (wvList.Contains(index)) wvList.Remove(index); else wvList.Add(index);
            return;
        }

        if (drawWhirlpool && loadedData != null)
        {
            if (loadedData.whirlpools == null) loadedData.whirlpools = new List<GridData.WhirlpoolPoint>();
            int existing = loadedData.whirlpools.FindIndex(w => w.cellIndex == index);
            if (existing >= 0) loadedData.whirlpools.RemoveAt(existing);
            else loadedData.whirlpools.Add(new GridData.WhirlpoolPoint { cellIndex = index });
            return;
        }

        if (drawDirectPrefab && _activePlacementPrefab != null && loadedData != null)
        {
            if (_isWaitingForTubePlacement)
            {
                // Second click: Place the tube and link it
                var tubePrefab = scannedModifiersLib.Find(p => p != null && p.name == "SoulFishInputTube");
                if (tubePrefab == null)
                {
                    GridLog("[ERR] Could not find 'SoulFishInputTube' in modifiers library.");
                    _isWaitingForTubePlacement = false;
                    return;
                }

                var placements = GetActivePrefabPlacements();
                placements.RemoveAll(p => p.cellIndex == index);
                placements.Add(new GridData.PrefabPlacement
                {
                    cellIndex = index,
                    position = GridData.SoulZone.CellToNormalized(index),
                    freePlaced = true,
                    prefab = tubePrefab,
                    isCircle = drawCircle,
                    isWorldSpaceProp = _activePlacementIsWorldSpaceProp,
                });

                if (loadedData.linkedPairs == null) loadedData.linkedPairs = new List<GridData.LinkedPrefabPair>();
                loadedData.linkedPairs.Add(new GridData.LinkedPrefabPair
                {
                    modifierCellIndex = _pendingModifierCellIndex,
                    modifierTierIndex = _pendingModifierTierIndex,
                    inputTubeCellIndex = index,
                    inputTubeTierIndex = activeTierIndex,
                    tubeSubdivisions   = 3,
                });

                GridLog($"Linked modifier at {_pendingModifierCellIndex} (T:{_pendingModifierTierIndex}) to tube at {index} (T:{activeTierIndex})");
                _isWaitingForTubePlacement = false;
                _pendingModifierCellIndex = -1;
                _pendingModifierTierIndex = -1;
                return;
            }

            // A creeper with no rock of its own is ALLOCATED to a spike rather than placed freely.
            // It snaps onto the rock it is given to, so which spike he belongs to is unambiguous
            // both here and at runtime, where he adopts the nearest climbing area.
            if (IsCreepPlacement(_activePlacementPrefab) && !IsClimbingRock(_activePlacementPrefab))
            {
                Vector2 clickNorm = GridData.SoulZone.CellToNormalized(index);

                // He can be allocated to a climbing-rock PREFAB or to a climbable PROCEDURAL SPIKE —
                // both grow a climbing area at spawn, so he adopts whichever he is snapped onto. Take
                // whichever is nearer to where you clicked.
                var rockHost  = FindClimbingRockNear(index);
                int spikeIdx  = FindClimbableSpikeNear(clickNorm);
                bool haveRock  = rockHost != null;
                bool haveSpike = spikeIdx >= 0;

                if (!haveRock && !haveSpike)
                {
                    GridLog("[Creeper] Can only be allocated to a climbable rock/spike — click on one " +
                            "(right-click a spike in ▲ Spikes to make it climbable).");
                    return;
                }
                if (haveRock && haveSpike)
                {
                    float dRock  = Vector2.Distance(rockHost.position, clickNorm);
                    float dSpike = Vector2.Distance(loadedData.proceduralSpikes[spikeIdx].center, clickNorm);
                    if (dSpike <= dRock) haveRock = false; else haveSpike = false;
                }

                Vector2 snapPos; int hostCell; string hostName;
                if (haveRock)
                {
                    rockHost.EnsureFreePosition();
                    snapPos = rockHost.position; hostCell = rockHost.cellIndex; hostName = rockHost.prefab.name;
                }
                else
                {
                    var s = loadedData.proceduralSpikes[spikeIdx];
                    snapPos = s.center; hostCell = GridData.NormalizedToCell(s.center); hostName = $"spike {spikeIdx + 1}";
                }

                var creeperList = GetActivePrefabPlacements();
                // Clear any creeper already on this host, but NOT the host itself.
                creeperList.RemoveAll(p => p?.prefab != null
                                           && IsCreepPlacement(p.prefab) && !IsClimbingRock(p.prefab)
                                           && p.cellIndex == hostCell);

                creeperList.Add(new GridData.PrefabPlacement
                {
                    cellIndex        = hostCell,
                    position         = snapPos,     // snapped onto the rock/spike
                    freePlaced       = true,
                    prefab           = _activePlacementPrefab,
                    isCircle         = false,
                    isWorldSpaceProp = _activePlacementIsWorldSpaceProp,
                });

                GridLog($"[Creeper] Allocated to '{hostName}' at cell {hostCell}.");
                EditorUtility.SetDirty(loadedData);
                return;
            }

            // First click (or normal placement)
            var placementsBase = GetActivePrefabPlacements();
            RemoveGuardedZonesForCell(index); // clean up a prior statue's zone at this cell
            placementsBase.RemoveAll(p => p.cellIndex == index);
            var placement = new GridData.PrefabPlacement
            {
                cellIndex           = index,
                // Snap to the cell centre when clamping is on; otherwise drop at the exact pointer.
                position            = _clampToCellWhenDrawing
                                      ? GridData.SoulZone.CellToNormalized(index)
                                      : _drawPointerNorm,
                freePlaced          = true,
                prefab              = _activePlacementPrefab,
                isCircle            = drawCircle,
                isWorldSpaceProp    = _activePlacementIsWorldSpaceProp,
            };
            placementsBase.Add(placement);
            TryCreateStatueZone(placement);
            TryCreateTowerZone(placement);

            if (_activePlacementPrefab.name == "TypeBWaveModifier")
            {
                _isWaitingForTubePlacement = true;
                _pendingModifierCellIndex = index;
                _pendingModifierTierIndex = activeTierIndex;
                GridLog("Modifier placed. Now place the Input Tube.");
            }
            return;
        }

        if (activeTierIndex >= 0 && loadedData?.tiers != null && activeTierIndex < loadedData.tiers.Count)
        {
            var tier = loadedData.tiers[activeTierIndex];
            if (tier.cells == null) tier.cells = new int[GridData.CellCount];
            tier.cells[index] = activeSlot;
        }
        else if (drawCircle) circleGrid[index] = activeSlot;
        else                 squareGrid[index] = activeSlot;
    }

    void ScanPrefabFolder()
    {
        scannedPrefabs.Clear();
        prefabIcons.Clear();

        if (string.IsNullOrEmpty(prefabFolderPath)) return;

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabFolderPath });
        foreach (string guid in guids)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) scannedPrefabs.Add(go);
        }
        scannedPrefabs.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        ScanIcons();
    }

    void ScanIcons()
    {
        prefabIcons.Clear();

        // Search folders: explicit icons folder first, then prefab folder itself
        var searchFolders = new List<string>();
        if (!string.IsNullOrEmpty(iconsFolderPath) && AssetDatabase.IsValidFolder(iconsFolderPath))
            searchFolders.Add(iconsFolderPath);
        if (!string.IsNullOrEmpty(prefabFolderPath) && AssetDatabase.IsValidFolder(prefabFolderPath))
            searchFolders.Add(prefabFolderPath);
        if (searchFolders.Count == 0) return;

        string[] texGuids = AssetDatabase.FindAssets("t:Texture2D", searchFolders.ToArray());
        var texByName = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);
        foreach (string guid in texGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                texByName[tex.name] = tex;
        }

        // Match icons to prefabs by name (exact, then strip common suffixes)
        foreach (var prefab in scannedPrefabs)
        {
            if (texByName.TryGetValue(prefab.name, out var icon))
            {
                prefabIcons[prefab.name] = icon;
                continue;
            }
            // Try stripping trailing digits / underscores for variant prefabs
            string stripped = prefab.name.TrimEnd('0','1','2','3','4','5','6','7','8','9','_');
            if (texByName.TryGetValue(stripped, out icon))
                prefabIcons[prefab.name] = icon;
        }
    }

    // Returns the prefab's PrefabBaselineAlignment (cached), or null if it has none.
    PrefabBaselineAlignment GetBaselineAlign(GameObject prefab)
    {
        if (prefab == null) return null;
        if (!_baselineAlignCache.TryGetValue(prefab, out var align))
        {
            align = prefab.GetComponentInChildren<PrefabBaselineAlignment>(true);
            _baselineAlignCache[prefab] = align;
        }
        return align;
    }

    Color GetPrefabColor(GameObject prefab)
    {
        int idx = scannedPrefabs.IndexOf(prefab);
        if (idx < 0) return Color.gray;
        return Color.HSVToRGB((idx * 0.618f) % 1f, 0.75f, 0.95f);
    }

    // Every prefab across the library tabs with the icon it draws on the grid (texture, or null → the
    // fallback round swatch). Used by the settings window's icon reference/colour list.
    public List<(string name, Texture2D icon)> GetPrefabLibraryIcons()
    {
        var result = new List<(string, Texture2D)>();
        var seen   = new HashSet<string>();
        void AddAll(List<GameObject> list)
        {
            if (list == null) return;
            foreach (var go in list)
            {
                if (go == null || !seen.Add(go.name)) continue;
                prefabIcons.TryGetValue(go.name, out var icon);
                result.Add((go.name, icon));
            }
        }
        AddAll(scannedPrefabs);
        AddAll(scannedSetPiecesLib);
        AddAll(scannedStatuesLib);
        AddAll(scannedModifiersLib);
        AddAll(scannedBadGuysLib);
        return result;
    }

    // The override for a prefab's fallback icon, or null. create=true adds one seeded from the default.
    public PrefabIconOverride GetIconOverride(string prefabName, bool create)
    {
        if (_style.iconOverrides == null) _style.iconOverrides = new List<PrefabIconOverride>();
        foreach (var o in _style.iconOverrides)
            if (o != null && o.prefabName == prefabName) return o;
        if (!create) return null;
        var n = new PrefabIconOverride { prefabName = prefabName, color = _style.defaultIconColor };
        _style.iconOverrides.Add(n);
        return n;
    }

    // Effective fallback-icon colour: the prefab's colour override if set, else the global default.
    public Color IconColorFor(GameObject prefab)
    {
        var o = prefab != null ? GetIconOverride(prefab.name, false) : null;
        return (o != null && o.overrideColor) ? o.color : _style.defaultIconColor;
    }

    // Effective overlay label: the prefab's text override if set, else its auto 2-letter abbreviation.
    string IconLabelFor(GameObject prefab)
    {
        if (prefab == null) return "";
        var o = GetIconOverride(prefab.name, false);
        if (o != null && !string.IsNullOrEmpty(o.label)) return o.label;
        return prefab.name.Substring(0, Mathf.Min(2, prefab.name.Length));
    }

    void RefreshCachedTierOffsets()
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:TierConfig");
        if (guids.Length > 0)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<TierConfig>(path);
            cachedTierYOffsets = config?.offsets;
        }
        else
        {
            cachedTierYOffsets = null;
        }
    }

    List<GridData.PrefabPlacement> GetActivePrefabPlacements()
    {
        if (activeTierIndex >= 0 && loadedData?.tiers != null && activeTierIndex < loadedData.tiers.Count)
        {
            var t = loadedData.tiers[activeTierIndex];
            if (t.prefabPlacements == null) t.prefabPlacements = new List<GridData.PrefabPlacement>();
            return t.prefabPlacements;
        }
        if (loadedData.prefabPlacements == null) loadedData.prefabPlacements = new List<GridData.PrefabPlacement>();
        return loadedData.prefabPlacements;
    }

    // ── Statue-guarded soul zones ────────────────────────────
    const int StatueRingNodeCount = 8;

    // If the placed prefab is a statue (has a StatueBehaviour), give the placement a
    // unique id and auto-create a guarded multi-node ring zone linked to it.
    void TryCreateStatueZone(GridData.PrefabPlacement placement)
    {
        if (placement?.prefab == null || loadedData == null) return;
        if (placement.prefab.GetComponentInChildren<StatueBehaviour>(true) == null) return;

        EnsureSoulZones();
        if (placement.statueId == 0) placement.statueId = NextStatueId();

        // Don't duplicate if a zone already links to this statue
        if (loadedData.soulZones.Exists(z => z.statueGuarded && z.linkedStatueId == placement.statueId))
            return;

        var zone = new GridData.SoulZone
        {
            zoneRole       = GridData.SoulZone.ZoneRole.SubZone, // statue sources are tributaries
            statueGuarded  = true,
            linkedStatueId = placement.statueId,
            ringRadius     = 0.08f,
            radius         = 0.5f,
            knotCount      = 8,
        };
        BuildRing(zone, GridData.SoulZone.CellToNormalized(placement.cellIndex), zone.ringRadius, StatueRingNodeCount);
        loadedData.soulZones.Add(zone);
        GridLog($"Auto-created guarded soul ring for statue #{placement.statueId} at cell {placement.cellIndex}. Assign souls to it in the Soul Zones panel.");
        EditorUtility.SetDirty(loadedData);
    }

    // If the placed prefab is a fish-bowl tower (has a FishBowlTowerController), give the placement a
    // unique id and auto-create a tower-guarded ring zone linked to it. The shoal container spawns
    // aloft in the bowl and drops to this ring when the tower is destroyed.
    void TryCreateTowerZone(GridData.PrefabPlacement placement)
    {
        if (placement?.prefab == null || loadedData == null) return;
        if (placement.prefab.GetComponentInChildren<FishBowlTowerController>(true) == null) return;

        EnsureSoulZones();
        if (placement.statueId == 0) placement.statueId = NextStatueId();

        // Don't duplicate if a zone already links to this tower
        if (loadedData.soulZones.Exists(z => z.towerGuarded && z.linkedStatueId == placement.statueId))
            return;

        // Bowl height + swim radius live on the tower prefab's FishBowlTowerController — not here.
        var zone = new GridData.SoulZone
        {
            zoneRole       = GridData.SoulZone.ZoneRole.SubZone, // fish-bowl sources are tributaries
            towerGuarded   = true,
            linkedStatueId = placement.statueId,
            knotCount      = 8,
            closedLoop     = false,
        };
        // A single anchor node co-located with the tower — the container spawns above this point.
        // No authored ring is drawn: fish are contained within the bowl radius, not along a path.
        zone.nodePositions = new List<Vector2> { GridData.SoulZone.CellToNormalized(placement.cellIndex) };
        loadedData.soulZones.Add(zone);
        GridLog($"Auto-created fish-bowl tower zone #{placement.statueId} at cell {placement.cellIndex}. Assign souls to it in the Soul Zones panel.");
        EditorUtility.SetDirty(loadedData);
    }

    // Fills a zone with an evenly-spaced closed ring of `count` nodes around `center`.
    static void BuildRing(GridData.SoulZone zone, Vector2 center, float radiusNorm, int count)
    {
        zone.nodePositions = new List<Vector2>(count);
        zone.segmentCurved = new List<bool>(count);
        for (int i = 0; i < count; i++)
        {
            float ang = (i / (float)count) * Mathf.PI * 2f;
            zone.nodePositions.Add(center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radiusNorm);
            zone.segmentCurved.Add(true);   // rings sample as smooth circles, not polygons
        }
        zone.closedLoop = true;
    }

    // ── Soul zone segment-curve flag upkeep ──────────────────────
    // Mirrors the spline wall editor's list alignment: segment i = node[i] → node[i+1].
    // Zones with no flags yet (authored pre-curves) stay flagless — missing entries read
    // as straight, so their shape never changes behind the designer's back.

    void SoulZoneNodeInserted(GridData.SoulZone zone, int newNodeIdx)
    {
        // Tributaries adjoined to THIS zone reference the junction by node index — shift them up so
        // the junction stays on the SAME logical node when a node is inserted before it.
        if (loadedData?.soulZones != null && zone.zoneId != 0)
            foreach (var t in loadedData.soulZones)
                if (t != zone && t.zoneRole == GridData.SoulZone.ZoneRole.SubZone
                    && t.adjoinZoneId == zone.zoneId && t.adjoinNodeIndex >= newNodeIdx)
                    t.adjoinNodeIndex++;

        // New node inherits its neighbour's curvature so inserting doesn't kink the path.
        if (zone.nodeTension != null && zone.nodeTension.Count > 0)
        {
            int inheritFrom = Mathf.Clamp(newNodeIdx - 1, 0, zone.nodeTension.Count - 1);
            int insertAt    = Mathf.Clamp(newNodeIdx, 0, zone.nodeTension.Count);
            zone.nodeTension.Insert(insertAt, zone.nodeTension[inheritFrom]);
        }

        if (zone.segmentCurved != null && zone.segmentCurved.Count > 0)
        {
            int inheritFrom = Mathf.Clamp(newNodeIdx - 1, 0, zone.segmentCurved.Count - 1);
            int insertAt    = Mathf.Clamp(newNodeIdx, 0, zone.segmentCurved.Count);
            zone.segmentCurved.Insert(insertAt, zone.segmentCurved[inheritFrom]);
        }

        // Street lights on/after the insertion point shift up with their nodes.
        if (zone.streetLights != null)
            foreach (var l in zone.streetLights)
                if (l != null && l.nodeIndex >= newNodeIdx) l.nodeIndex++;
    }

    void SoulZoneNodeDeleted(GridData.SoulZone zone, int nodeIdx)
    {
        // Tributaries adjoined to THIS zone: shift the junction down so it stays on the SAME logical
        // node when an earlier node is removed. (Deleting the junction node itself leaves the index
        // pointing at the next node, or out of range — SyncSubZoneJunctions guards that.)
        if (loadedData?.soulZones != null && zone.zoneId != 0)
            foreach (var t in loadedData.soulZones)
                if (t != zone && t.zoneRole == GridData.SoulZone.ZoneRole.SubZone
                    && t.adjoinZoneId == zone.zoneId && t.adjoinNodeIndex > nodeIdx)
                    t.adjoinNodeIndex--;

        if (zone.nodeTension != null && nodeIdx < zone.nodeTension.Count)
            zone.nodeTension.RemoveAt(nodeIdx);

        if (zone.segmentCurved != null && zone.segmentCurved.Count > 0)
            zone.segmentCurved.RemoveAt(Mathf.Min(nodeIdx, zone.segmentCurved.Count - 1));

        // A light standing on the deleted node goes with it; lights beyond shift down.
        if (zone.streetLights != null)
        {
            zone.streetLights.RemoveAll(l => l == null || l.nodeIndex == nodeIdx);
            foreach (var l in zone.streetLights)
                if (l.nodeIndex > nodeIdx) l.nodeIndex--;
        }
    }

    void SetAllSoulZoneSegmentsCurved(GridData.SoulZone zone, bool curved)
    {
        Undo.RecordObject(loadedData, curved ? "Curve Soul Zone Path" : "Straighten Soul Zone Path");
        SetAllNodeTension(zone, curved ? 0.5f : 0f);
        EditorUtility.SetDirty(loadedData);
        Repaint();
    }

    // ── Per-node curvature ───────────────────────────────
    // nodeTension is the authoritative curve control (GridData.SoulZone.SamplePath reads it).
    // The legacy segmentCurved list is kept in step so anything still reading it stays sane.

    static void EnsureNodeTension(GridData.SoulZone zone)
    {
        int n = zone.nodePositions?.Count ?? 0;
        if (zone.nodeTension == null) zone.nodeTension = new List<float>();
        // Seed from whatever the legacy flags implied, so existing zones keep their exact shape.
        while (zone.nodeTension.Count < n)
            zone.nodeTension.Add(zone.NodeTension(zone.nodeTension.Count));
        while (zone.nodeTension.Count > n)
            zone.nodeTension.RemoveAt(zone.nodeTension.Count - 1);
    }

    static void SetNodeTension(GridData.SoulZone zone, int nodeIdx, float tension)
    {
        EnsureNodeTension(zone);
        if (nodeIdx < 0 || nodeIdx >= zone.nodeTension.Count) return;
        zone.nodeTension[nodeIdx] = Mathf.Clamp01(tension);
        SyncLegacyCurvedFlags(zone);
    }

    static void SetAllNodeTension(GridData.SoulZone zone, float tension)
    {
        EnsureNodeTension(zone);
        for (int i = 0; i < zone.nodeTension.Count; i++) zone.nodeTension[i] = Mathf.Clamp01(tension);
        SyncLegacyCurvedFlags(zone);
    }

    // A segment counts as "curved" for the legacy flag when either end has any tension.
    static void SyncLegacyCurvedFlags(GridData.SoulZone zone)
    {
        int segCount = zone.SegmentCount();
        if (zone.segmentCurved == null) zone.segmentCurved = new List<bool>();
        while (zone.segmentCurved.Count < segCount) zone.segmentCurved.Add(false);
        for (int s = 0; s < segCount; s++) zone.segmentCurved[s] = !zone.SegmentIsStraight(s);
    }

    static Vector2 Centroid(List<Vector2> pts)
    {
        if (pts == null || pts.Count == 0) return Vector2.zero;
        Vector2 s = Vector2.zero;
        foreach (var p in pts) s += p;
        return s / pts.Count;
    }

    // Position for a node inserted next to `idx` in direction `dir` (+1 after, -1 before):
    // midpoint to the neighbour, wrapping on closed loops, else a small offset off the end.
    static Vector2 InsertedNodePos(GridData.SoulZone zone, int idx, int dir)
    {
        var pts = zone.nodePositions;
        Vector2 a = pts[idx];
        int nbr = idx + dir;
        if (nbr >= 0 && nbr < pts.Count) return (a + pts[nbr]) * 0.5f;
        if (zone.closedLoop && pts.Count > 1)
            return (a + pts[(nbr + pts.Count) % pts.Count]) * 0.5f;
        return a + new Vector2(0.02f * dir, 0f);
    }

    // Smallest unused positive statue id across base + tier placements.
    int NextStatueId()
    {
        int max = 0;
        if (loadedData.prefabPlacements != null)
            foreach (var p in loadedData.prefabPlacements) max = Mathf.Max(max, p.statueId);
        if (loadedData.tiers != null)
            foreach (var t in loadedData.tiers)
                if (t.prefabPlacements != null)
                    foreach (var p in t.prefabPlacements) max = Mathf.Max(max, p.statueId);
        return max + 1;
    }

    // Assigns a stable, level-unique zoneId to any soul zone that lacks one (0). Junctions and
    // (later) cross-level routing reference zones by this id rather than fragile list indices.
    void EnsureZoneIds()
    {
        if (loadedData?.soulZones == null) return;
        int maxId = 0;
        foreach (var z in loadedData.soulZones) maxId = Mathf.Max(maxId, z.zoneId);
        bool changed = false;
        foreach (var z in loadedData.soulZones)
            if (z.zoneId == 0) { z.zoneId = ++maxId; changed = true; }
        if (changed) EditorUtility.SetDirty(loadedData);
    }

    // Keeps each adjoined tributary's final node glued to the main-river node it joins, so the
    // junction follows when the main path is edited (mirrors WMSyncTubeEndpoints for tube ends).
    // Normalized-grid position of an arena entrance. Entrances are placed by perimeter angle, so
    // this mirrors SpawnPortalPrefab's world formula in the designer's -0.5..0.5 space: normalized
    // 0.5 IS the arena radius (the grid spans -r..+r), and spawnRadius pulls the door inward.
    // Y is flipped relative to the pixel drawing in DrawPortalOverlay because normalized +y is north.
    Vector2 EntranceNormalizedPos(GridData.ArenaEntrance ent)
    {
        float rad = ent.perimeterAngle * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));

        float rWorld = loadedData != null ? loadedData.WorldArenaRadius : 0f;
        float frac = rWorld > 0.0001f ? (rWorld - ent.spawnRadius) / (2f * rWorld) : 0.5f;
        return dir * frac;
    }

    // Pins a zone's first/last node onto its chosen entrances, so a path runs door-to-door and the
    // ends follow if an entrance angle is edited. Returns true when something moved.
    bool SyncZoneEntrances(GridData.SoulZone z)
    {
        if (!z.attachToEntrances || z.nodePositions == null || z.nodePositions.Count == 0) return false;
        if (loadedData.entrances == null || loadedData.entrances.Count == 0) return false;

        bool changed = false;

        if (z.entryEntranceIndex >= 0 && z.entryEntranceIndex < loadedData.entrances.Count)
        {
            Vector2 target = EntranceNormalizedPos(loadedData.entrances[z.entryEntranceIndex]);
            if (z.nodePositions[0] != target) { z.nodePositions[0] = target; changed = true; }
        }

        // Needs at least two nodes for the exit to be a different node from the entry.
        if (z.exitEntranceIndex >= 0 && z.exitEntranceIndex < loadedData.entrances.Count
            && z.nodePositions.Count >= 2)
        {
            Vector2 target = EntranceNormalizedPos(loadedData.entrances[z.exitEntranceIndex]);
            int last = z.nodePositions.Count - 1;
            if (z.nodePositions[last] != target) { z.nodePositions[last] = target; changed = true; }
        }

        return changed;
    }

    void SyncSubZoneJunctions()
    {
        if (loadedData?.soulZones == null) return;
        bool changed = false;

        // Entrance pinning applies to any zone that opts in, so it runs before the SubZone filter.
        foreach (var z in loadedData.soulZones)
            if (SyncZoneEntrances(z)) changed = true;

        foreach (var z in loadedData.soulZones)
        {
            if (z.zoneRole != GridData.SoulZone.ZoneRole.SubZone) continue;
            if (z.nodePositions == null || z.nodePositions.Count == 0) continue;

            // Lock a fish-bowl tributary's source node (node 0) to its tower placement, so the
            // pool follows when the prefab is moved.
            if (z.towerGuarded && z.linkedStatueId != 0)
            {
                var pp = FindPlacementByStatueId(z.linkedStatueId);
                if (pp != null)
                {
                    pp.EnsureFreePosition();
                    Vector2 anchor = pp.position; // track the bowl prefab's exact free position
                    if (z.nodePositions[0] != anchor) { z.nodePositions[0] = anchor; changed = true; }
                }
            }

            // Pin the adjoining node (last) to the main-river node it joins. If the tributary is
            // adjoined but has no path yet (e.g. the junction was set via the dropdown, not drawn),
            // CREATE the join node so a bowl→junction path exists and actually draws.
            if (z.adjoinZoneId != 0)
            {
                var main = loadedData.soulZones.Find(m =>
                    m.zoneId == z.adjoinZoneId && m.zoneRole == GridData.SoulZone.ZoneRole.MainPath);
                if (main?.nodePositions != null
                    && z.adjoinNodeIndex >= 0 && z.adjoinNodeIndex < main.nodePositions.Count)
                {
                    Vector2 target = main.nodePositions[z.adjoinNodeIndex];
                    if (z.nodePositions.Count < 2)
                    {
                        z.nodePositions.Add(target);   // straight bowl→junction path; draw/edit adds waypoints
                        changed = true;
                    }
                    else
                    {
                        int last = z.nodePositions.Count - 1;
                        if (z.nodePositions[last] != target)
                        {
                            z.nodePositions[last] = target;
                            changed = true;
                        }
                    }
                }
            }
        }
        if (changed) EditorUtility.SetDirty(loadedData);
    }

    // Finds the prefab placement carrying a given statueId (base layer + tiers), or null.
    GridData.PrefabPlacement FindPlacementByStatueId(int statueId)
    {
        if (loadedData == null || statueId == 0) return null;
        if (loadedData.prefabPlacements != null)
            foreach (var p in loadedData.prefabPlacements) if (p != null && p.statueId == statueId) return p;
        if (loadedData.tiers != null)
            foreach (var t in loadedData.tiers)
                if (t.prefabPlacements != null)
                    foreach (var p in t.prefabPlacements) if (p != null && p.statueId == statueId) return p;
        return null;
    }

    // Removes any statue-guarded zone whose statue is being erased at this cell.
    void RemoveGuardedZonesForCell(int index)
    {
        if (loadedData?.soulZones == null) return;
        var ids = new HashSet<int>();
        if (loadedData.prefabPlacements != null)
            foreach (var p in loadedData.prefabPlacements)
                if (p.cellIndex == index && p.statueId != 0) ids.Add(p.statueId);
        if (loadedData.tiers != null)
            foreach (var t in loadedData.tiers)
                if (t.prefabPlacements != null)
                    foreach (var p in t.prefabPlacements)
                        if (p.cellIndex == index && p.statueId != 0) ids.Add(p.statueId);
        if (ids.Count > 0)
            loadedData.soulZones.RemoveAll(z =>
                (z.statueGuarded || z.towerGuarded) && ids.Contains(z.linkedStatueId));
    }

    // Cell index (base or tier) of the placement carrying this guard id, or -1 if none.
    int FindPlacementCellForStatueId(int statueId)
    {
        if (statueId == 0 || loadedData == null) return -1;
        if (loadedData.prefabPlacements != null)
            foreach (var p in loadedData.prefabPlacements)
                if (p.statueId == statueId) return p.cellIndex;
        if (loadedData.tiers != null)
            foreach (var t in loadedData.tiers)
                if (t.prefabPlacements != null)
                    foreach (var p in t.prefabPlacements)
                        if (p.statueId == statueId) return p.cellIndex;
        return -1;
    }

    // Tower zones only need a single anchor node at their tower cell (the swim area + height come
    // from the tower prefab). Collapses any legacy multi-node ring created by earlier versions.
    void NormalizeTowerZones()
    {
        if (loadedData?.soulZones == null) return;
        bool changed = false;
        foreach (var z in loadedData.soulZones)
        {
            if (!z.towerGuarded) continue;
            int cell = FindPlacementCellForStatueId(z.linkedStatueId);
            Vector2 anchor = cell >= 0
                ? GridData.SoulZone.CellToNormalized(cell)
                : (z.nodePositions != null && z.nodePositions.Count > 0 ? Centroid(z.nodePositions) : Vector2.zero);

            if (z.nodePositions == null || z.nodePositions.Count != 1 || z.nodePositions[0] != anchor)
            {
                z.nodePositions = new List<Vector2> { anchor };
                changed = true;
            }
        }
        if (changed) EditorUtility.SetDirty(loadedData);
    }

    List<int> GetActiveTierWaterModifiers()
    {
        if (activeTierIndex >= 0 && loadedData?.tiers != null && activeTierIndex < loadedData.tiers.Count)
        {
            var t = loadedData.tiers[activeTierIndex];
            if (t.waterLevelModifierCellIndices == null) t.waterLevelModifierCellIndices = new List<int>();
            return t.waterLevelModifierCellIndices;
        }
        if (loadedData.waterLevelModifierCellIndices == null) loadedData.waterLevelModifierCellIndices = new List<int>();
        return loadedData.waterLevelModifierCellIndices;
    }

    List<int> GetActiveTierWaveModifiers()
    {
        if (activeTierIndex >= 0 && loadedData?.tiers != null && activeTierIndex < loadedData.tiers.Count)
        {
            var t = loadedData.tiers[activeTierIndex];
            if (t.waveModifierCellIndices == null) t.waveModifierCellIndices = new List<int>();
            return t.waveModifierCellIndices;
        }
        if (loadedData.waveModifierCellIndices == null) loadedData.waveModifierCellIndices = new List<int>();
        return loadedData.waveModifierCellIndices;
    }

    // ─────────────────────────────────────────────
    // LEVEL DATA SECTIONS
    // ─────────────────────────────────────────────

    void DrawLevelIdentitySection()
    {
        _showLevelIdentity = EditorGUILayout.Foldout(_showLevelIdentity, "Level Identity", true, EditorStyles.foldoutHeader);
        if (!_showLevelIdentity) return;

        GUI.enabled = false;
        EditorGUILayout.ObjectField("Asset", loadedData, typeof(GridData), false);
        GUI.enabled = true;

        EditorGUI.BeginChangeCheck();
        string newID   = EditorGUILayout.TextField("Level ID",     loadedData.levelID);
        string newName = EditorGUILayout.TextField("Display Name", loadedData.displayName);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Edit Level Identity");
            loadedData.levelID     = newID;
            loadedData.displayName = newName;
            EditorUtility.SetDirty(loadedData);
        }
    }

    void DrawCameraSection()
    {
        _showCamera = EditorGUILayout.Foldout(_showCamera, "Camera", true, EditorStyles.foldoutHeader);
        if (!_showCamera) return;

        EditorGUI.BeginChangeCheck();
        var newProfile = (CameraProfile)EditorGUILayout.ObjectField(
            "Camera Profile", loadedData.cameraProfile, typeof(CameraProfile), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Set Camera Profile");
            loadedData.cameraProfile = newProfile;
            EditorUtility.SetDirty(loadedData);
        }
    }

    void DrawWavePresetsSection()
    {
        _showWavePresets = EditorGUILayout.Foldout(_showWavePresets, "Wave Presets", true, EditorStyles.foldoutHeader);
        if (!_showWavePresets) return;

        EditorGUI.BeginChangeCheck();
        var newGameplay = (WavePreset)EditorGUILayout.ObjectField(
            "Gameplay Preset", loadedData.gameplayWavePreset, typeof(WavePreset), false);
        DrawWavePresetInfo(loadedData.gameplayWavePreset);
        var newGong     = (WavePreset)EditorGUILayout.ObjectField(
            "Gong Preset",     loadedData.gongWavePreset,     typeof(WavePreset), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Set Wave Presets");
            loadedData.gameplayWavePreset = newGameplay;
            loadedData.gongWavePreset     = newGong;
            EditorUtility.SetDirty(loadedData);
        }
    }

    // Small read-only summary of a wave preset's primary values, shown under its field.
    void DrawWavePresetInfo(WavePreset preset)
    {
        if (preset == null) return;
        var s = preset.state;
        EditorGUILayout.LabelField(
            $"Freq {s.Frequency:0.##}   Speed {s.Speed:0.##}   Ripple {s.RippleDepth:0.##}",
            EditorStyles.miniLabel);
    }

    void DrawSonarGridSection()
    {
        _showSonarGrid = EditorGUILayout.Foldout(_showSonarGrid, "Sonar Grid", true, EditorStyles.foldoutHeader);
        if (!_showSonarGrid) return;

        EditorGUI.BeginChangeCheck();
        var newType = (SonarGridType)EditorGUILayout.ObjectField(
            "Grid Type", loadedData.sonarGridType, typeof(SonarGridType), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Set Sonar Grid Type");
            loadedData.sonarGridType = newType;
            EditorUtility.SetDirty(loadedData);
        }

        if (loadedData.sonarGridType != null)
        {
            var st = loadedData.sonarGridType;
            EditorGUILayout.LabelField(
                $"{st.columns}×{st.rows} tiles   {st.levels} levels   mat: {(st.planeMaterial != null ? st.planeMaterial.name : "none")}",
                EditorStyles.miniLabel);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "No sonar grid type — level keeps the scene's default sonar formation.", MessageType.None);
        }

        if (GUILayout.Button(loadedData.sonarGridType != null
                ? "Edit in Sonar Grid Editor" : "Open Sonar Grid Editor…"))
            SonarGridEditorWindow.OpenWith(loadedData.sonarGridType);
    }

    void DrawFogSection()
    {
        _showFog = EditorGUILayout.Foldout(_showFog, "Fog", true, EditorStyles.foldoutHeader);
        if (!_showFog) return;

        // Arena size itself is shown up in the Arena section, where the profile that produces it
        // lives. Here it is only needed as the thing fog is compared against.
        float arenaW = SpikeArenaWidth();
        float cellW  = arenaW / GridData.GridSize;

        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.Toggle("Fog Enabled", loadedData.fogEnabled);
        var newMap = (FogMap)EditorGUILayout.ObjectField(
            "Fog Map", loadedData.fogMap, typeof(FogMap), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Set Fog");
            loadedData.fogEnabled = on;
            loadedData.fogMap = newMap;
            EditorUtility.SetDirty(loadedData);
        }

        if (loadedData.fogEnabled && loadedData.fogMap == null)
        {
            // Deliberately an error rather than a quiet fallback. Fog appearing where nobody put it
            // is worse than no fog, because it looks like the map is working when it is not.
            EditorGUILayout.HelpBox(
                "Fog is on but no fog map is assigned, so this level gets no fog. There is no " +
                "fallback — the map is the only thing that decides where fog sits.",
                MessageType.Warning);
        }
        else if (loadedData.fogMap != null)
        {
            var m = loadedData.fogMap;
            EditorGUILayout.LabelField(
                $"{m.blobCount} masses   {m.properties.EffectiveLimbCount} limbs" +
                (loadedData.fogEnabled ? "" : "   (fog off — map ignored)"),
                EditorStyles.miniLabel);

            // Mass size against the grid, so the two can be reconciled by eye.
            var ws = m.WorldBlobScale;
            EditorGUILayout.LabelField(
                $"Masses {ws.x:0.##}–{ws.y:0.##} u   ·   mask {m.maskRadius:0.##} u" +
                $"  =  {m.maskRadius / Mathf.Max(cellW, 0.001f):0.#} cells",
                EditorStyles.miniLabel);
        }

        if (GUILayout.Button(loadedData.fogMap != null ? "Edit in Fog Map" : "Open Fog Map…"))
        {
            var w = EditorWindow.GetWindow<FogMapWindow>("Fog Map");
            w.Show();
        }
    }

    void DrawEnemySection()
    {
        _showEnemy = EditorGUILayout.Foldout(_showEnemy, "Enemy", true, EditorStyles.foldoutHeader);
        if (!_showEnemy) return;

        EditorGUI.BeginChangeCheck();
        var newProfile = (EnemyProfile)EditorGUILayout.ObjectField(
            "Enemy Profile", loadedData.enemyProfile, typeof(EnemyProfile), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Set Enemy Profile");
            loadedData.enemyProfile = newProfile;
            EditorUtility.SetDirty(loadedData);
        }
        if (loadedData.enemyProfile != null)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField($"Prefab: {loadedData.enemyProfile.prefab?.name ?? "(none)"}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Spawn on visit: {loadedData.enemyProfile.spawnOnVisit}",
                EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }
    }

    // Whether the angel flies this level. Where she LANDS is authored per rock in the ▲ Spikes
    // tool, so this section also counts those up — a level that has her but nowhere to perch is a
    // level where she never comes down, and that is worth saying at the point you tick the box.
    void DrawAngelSection()
    {
        _showAngel = EditorGUILayout.Foldout(_showAngel, "Angel", true, EditorStyles.foldoutHeader);
        if (!_showAngel) return;

        EditorGUI.BeginChangeCheck();
        bool present = EditorGUILayout.Toggle(
            new GUIContent("Angel present", "Spawn the angel on this level, to fly above the boat and " +
                                            "land on the rocks marked as angel perch points."),
            loadedData.angelPresent);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Set Angel Present");
            loadedData.angelPresent = present;
            EditorUtility.SetDirty(loadedData);
        }

        if (!loadedData.angelPresent) return;

        showPerchPoints = EditorGUILayout.ToggleLeft(
            new GUIContent("Show perch points",
                           "Draw the perch range (and the talk range, where Talk is on) on every marked rock, " +
                           "in any tool — so you can read her landing spots at a glance."),
            showPerchPoints);

        int perches = 0;
        int talk    = 0;
        if (loadedData.proceduralSpikes != null)
            foreach (var s in loadedData.proceduralSpikes)
                if (s != null && s.angelPerchPoint)
                {
                    perches++;
                    if (s.angelTalkEnabled) talk++;
                }

        if (perches == 0)
        {
            EditorGUILayout.HelpBox(
                "No rocks are marked as angel perch points, so she will fly the whole level and never " +
                "come down. Mark one in the ▲ Spikes tool.", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField(
            $"{perches} perch point{(perches == 1 ? "" : "s")} · {talk} with talk",
            EditorStyles.miniLabel);

        // Per-rock perch list — edit each perch's ranges, Talk toggle and dialogue here, without
        // hunting for the rock in the ▲ Spikes tool. "Rock N" selects it on the grid too.
        for (int i = 0; i < loadedData.proceduralSpikes.Count; i++)
        {
            var s = loadedData.proceduralSpikes[i];
            if (s == null || !s.angelPerchPoint) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool sel = _activeSpikeIndex == i;
                GUI.backgroundColor = sel ? new Color(0.8f, 0.72f, 1f) : Color.white;
                if (GUILayout.Button($"Rock {i + 1}", GUILayout.Width(70)))
                {
                    ClearSelectState();
                    _activeSpikeIndex = i;
                    _drawSpike = true; _drawCubeBuilding = _drawSplineWall = false;
                    drawSelect = drawSoulArea = drawSoul = drawCircle = drawOrb = drawWhirlpool
                               = drawWaterLevelModifier = drawWaveModifier = drawDirectPrefab = false;
                    Repaint();
                }
                GUI.backgroundColor = Color.white;
                GUILayout.Label(s.angelTalkEnabled ? "· talk" : "· silent", EditorStyles.miniLabel);
            }

            EditorGUI.BeginChangeCheck();
            float pr = EditorGUILayout.FloatField(
                new GUIContent("Perch range (m)", "Sail inside this and she comes down onto this rock."),
                s.angelPerchRadius);
            bool tk = EditorGUILayout.Toggle(
                new GUIContent("Talk", "Arm the talk camera + dialogue on this perch. Off = she just perches."),
                s.angelTalkEnabled);
            float tr = s.angelTalkRadius;
            string tt = s.angelTalkText;
            if (tk)
            {
                EditorGUI.indentLevel++;
                tr = EditorGUILayout.FloatField(
                    new GUIContent("Talk range (m)", "Sail inside this, with her perched, to talk. Kept inside the perch range."),
                    tr);
                EditorGUILayout.LabelField(new GUIContent("What she says",
                    "Shown in the dialogue box (AngelDialogueUI). Use / to split into separate lines."));
                tt = EditorGUILayout.TextArea(tt ?? "", GUILayout.MinHeight(40));
                EditorGUI.indentLevel--;
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Edit Perch");
                s.angelPerchRadius = Mathf.Max(0f, pr);
                s.angelTalkEnabled = tk;
                s.angelTalkRadius  = Mathf.Clamp(tr, 0f, s.angelPerchRadius);
                s.angelTalkText    = tt;
                EditorUtility.SetDirty(loadedData);
                Repaint();
            }

            EditorGUILayout.EndVertical();
        }
    }

    // Mirrors AngelPerchPoint.SplitTalkLines so the panel previews exactly what will play. Kept in
    // step by hand — the runtime one is in the other assembly.
    static List<string> SplitAngelTalkLines(string say)
    {
        var kept = new List<string>();
        if (string.IsNullOrWhiteSpace(say)) return kept;

        foreach (var piece in say.Split('/'))
        {
            string line = piece.Trim();
            if (line.Length > 0) kept.Add(line);
        }
        return kept;
    }

    void DrawTimeTrialSection()
    {
        _showTimeTrial = EditorGUILayout.Foldout(_showTimeTrial, "Time Trial", true, EditorStyles.foldoutHeader);
        if (!_showTimeTrial) return;

        EditorGUI.BeginChangeCheck();
        bool  newIsTimeTrial      = EditorGUILayout.Toggle("Is Time Trial",   loadedData.isTimeTrial);
        float newTimeLimitSeconds = loadedData.isTimeTrial
            ? EditorGUILayout.FloatField("Time Limit (s)", loadedData.timeLimitSeconds)
            : loadedData.timeLimitSeconds;
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Edit Time Trial");
            loadedData.isTimeTrial      = newIsTimeTrial;
            loadedData.timeLimitSeconds = newTimeLimitSeconds;
            EditorUtility.SetDirty(loadedData);
        }
    }

    void DrawPrefabsSection()
    {
        _showPrefabs = EditorGUILayout.Foldout(_showPrefabs, "Prefabs", true, EditorStyles.foldoutHeader);
        if (!_showPrefabs) return;

        EditorGUILayout.Space(2);

        // Sculpture Set Piece — ObjectField + dropdown picker from Assets/Prefab/SetPieces
        EditorGUILayout.LabelField("Sculpture Set Piece", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginChangeCheck();
        var newSetPiece = (GameObject)EditorGUILayout.ObjectField(
            loadedData.sculptureSetPiecePrefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Set Sculpture Set Piece");
            loadedData.sculptureSetPiecePrefab = newSetPiece;
            EditorUtility.SetDirty(loadedData);
        }

        if (GUILayout.Button("↺", GUILayout.Width(24)))
            ScanSetPieces();

        EditorGUILayout.EndHorizontal();

        EnsureSetPieceCache();
        if (_setPieceNames != null && _setPieceNames.Length > 0)
        {
            int currentIdx = 0;
            if (loadedData.sculptureSetPiecePrefab != null)
            {
                for (int i = 1; i < _scannedSetPieces.Length + 1; i++)
                    if (_scannedSetPieces[i - 1] == loadedData.sculptureSetPiecePrefab)
                    { currentIdx = i; break; }
            }

            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup(currentIdx, _setPieceNames);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Set Sculpture Set Piece");
                loadedData.sculptureSetPiecePrefab = newIdx == 0 ? null : _scannedSetPieces[newIdx - 1];
                EditorUtility.SetDirty(loadedData);
            }
        }
        else
        {
            EditorGUILayout.LabelField($"  (no prefabs in {SetPiecesFolder})", EditorStyles.miniLabel);
        }
    }

    void EnsureSetPieceCache()
    {
        if (_scannedSetPieces != null) return;
        ScanSetPieces();
    }

    void ScanSetPieces()
    {
        var list  = new List<GameObject>();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { SetPiecesFolder });
        foreach (string guid in guids)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) list.Add(go);
        }
        list.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        _scannedSetPieces = list.ToArray();

        _setPieceNames = new string[_scannedSetPieces.Length + 1];
        _setPieceNames[0] = "(none)";
        for (int i = 0; i < _scannedSetPieces.Length; i++)
            _setPieceNames[i + 1] = _scannedSetPieces[i].name;
    }

    void ScanSetPiecesLib()
    {
        scannedSetPiecesLib.Clear();
        selectedSetPieceIndex = -1;
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { SetPiecesFolder });
        foreach (string guid in guids)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) scannedSetPiecesLib.Add(go);
        }
        scannedSetPiecesLib.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
    }

    void ScanStatuesLib()
    {
        scannedStatuesLib.Clear();
        selectedStatueIndex = -1;
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { StatuesPrefabsFolder });
        foreach (string guid in guids)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) scannedStatuesLib.Add(go);
        }
        scannedStatuesLib.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
    }

    void ScanModifiersLib()
    {
        scannedModifiersLib.Clear();
        selectedModifierIndex = -1;
        if (!AssetDatabase.IsValidFolder(ModifiersPrefabsFolder)) return;
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { ModifiersPrefabsFolder });
        foreach (string guid in guids)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) scannedModifiersLib.Add(go);
        }
        scannedModifiersLib.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
    }

    // Enemies placed by hand on the grid (creepy guy ships with his own rock, so he is a
    // single placement). Recurses subfolders, so BadGuys/CreepGuy/ is picked up too.
    void ScanBadGuysLib()
    {
        scannedBadGuysLib.Clear();
        selectedBadGuyIndex = -1;
        if (!AssetDatabase.IsValidFolder(BadGuysPrefabsFolder)) return;
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { BadGuysPrefabsFolder });
        foreach (string guid in guids)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) scannedBadGuysLib.Add(go);
        }
        scannedBadGuysLib.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
    }

    void DrawStartRitualSection()
    {
        _showStartRitual = EditorGUILayout.Foldout(_showStartRitual, "Start Ritual", true, EditorStyles.foldoutHeader);
        if (!_showStartRitual) return;

        EditorGUI.BeginChangeCheck();
        var newRitual = (LevelStartRitual)EditorGUILayout.ObjectField(
            "Start Ritual", loadedData.startRitual, typeof(LevelStartRitual), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Set Start Ritual");
            loadedData.startRitual = newRitual;
            EditorUtility.SetDirty(loadedData);
        }
    }

    void ClearSelectState()
    {
        _selectedZoneIndex = -1;
        _selectedNodeIndex = -1;
        _isDraggingNode    = false;
        _dragCurrentCell   = -1;
        _dragUndoPushed    = false;
        _currentSelection  = new SelectionInfo { type = SelectionType.None };
        // Every selection channel clears together, so switching tools (Walls, Blocks, etc.) fully
        // deselects whatever was picked — a spike/block/orb no longer lingers as "selected".
        _activeOrbIndex    = -1;
        _activeSpikeIndex  = -1;
        _activeCubeIndex   = -1;
        CancelBridge();
    }

    // ── UI display settings, exposed for the separate Grid Designer Settings window ──
    // Each setter clamps, persists to EditorPrefs and repaints, so edits from the settings window
    // apply live to the open designer and survive a restart. No explicit Save needed.
    public float GridLineOpacity
    {
        get => _gridLineOpacity;
        set { _gridLineOpacity = Mathf.Clamp01(value); EditorPrefs.SetFloat(PrefKeyGridOpacity, _gridLineOpacity); Repaint(); }
    }
    public float BackdropBrightness
    {
        get => _backdropBrightness;
        set { _backdropBrightness = Mathf.Clamp01(value); EditorPrefs.SetFloat(PrefKeyBackdropBright, _backdropBrightness); Repaint(); }
    }
    public float SelectionCircleFactor
    {
        get => _selectionCircleFactor;
        set { _selectionCircleFactor = Mathf.Clamp(value, 0.05f, 1f); EditorPrefs.SetFloat(PrefKeySelectionCircle, _selectionCircleFactor); Repaint(); }
    }
    public int SpikeDisplayResolution
    {
        get => _spikeDisplayResolution;
        set { _spikeDisplayResolution = Mathf.Clamp(value, 3, 32); EditorPrefs.SetInt(PrefKeySpikeResolution, _spikeDisplayResolution); Repaint(); }
    }
    public bool ClampToCellWhenDrawing
    {
        get => _clampToCellWhenDrawing;
        set { _clampToCellWhenDrawing = value; EditorPrefs.SetBool(PrefKeyClampToCell, _clampToCellWhenDrawing); Repaint(); }
    }
    public float OrbCircleSize
    {
        get => _orbCircleFactor;
        set { _orbCircleFactor = Mathf.Clamp(value, 0.05f, 1f); EditorPrefs.SetFloat(PrefKeyOrbSize, _orbCircleFactor); Repaint(); }
    }

    // The overlay appearance settings, edited by the settings window. Call SaveStyle() after mutating.
    public GridDesignerStyle Style => _style;
    public void SaveStyle()
    {
        EditorPrefs.SetString(PrefKeyStyle, JsonUtility.ToJson(_style));
        Repaint();
    }

    // Draws a marker disc for `st` — filled or an outline ring per its mode — at `center`/`radius`.
    // `alphaMul` dims it for base/tier visibility. The single choke point every themeable disc/ring
    // marker goes through, so colour and fill/outline are honoured everywhere.
    void DrawMarker(Vector2 center, float radius, GridMarkerStyle st, float alphaMul = 1f)
    {
        if (st == null) return;
        Color col = st.color; col.a *= alphaMul;
        Handles.color = col;
        if (st.outline) Handles.DrawWireDisc(center, Vector3.forward, radius, Mathf.Max(1f, st.width));
        else            Handles.DrawSolidDisc(center, Vector3.forward, radius);
    }

    void CancelBridge()
    {
        _isBridgeMode       = false;
        _bridgeEndZoneIndex = -1;
        _bridgeEndNodeIndex = -1;
        _bridgeNodes.Clear();
    }

    void ConnectNodes(int zoneIdx, int nodeA, int nodeB)
    {
        if (zoneIdx < 0 || zoneIdx >= loadedData.soulZones.Count) return;
        var zone = loadedData.soulZones[zoneIdx];
        if (zone.nodes == null || zone.nodes.Count < 2) return;

        int first = 0;
        int last  = zone.nodes.Count - 1;

        bool isFirstAndLast = (nodeA == first && nodeB == last)
                           || (nodeA == last  && nodeB == first);

        Undo.RecordObject(loadedData, "Connect Soul Zone Nodes");

        if (isFirstAndLast)
        {
            // Close the loop — append first cell at end (standard closed loop pattern)
            // Remove existing closing node if already there to avoid double
            if (zone.nodes[last] == zone.nodes[first])
                zone.nodes.RemoveAt(last);
            zone.nodes.Add(zone.nodes[0]);
        }
        else
        {
            // General case: remove nodes strictly between the two indices
            int startIdx    = Mathf.Min(nodeA, nodeB);
            int endIdx      = Mathf.Max(nodeA, nodeB);
            int removeCount = endIdx - startIdx - 1;
            if (removeCount > 0)
            {
                zone.nodes.RemoveRange(startIdx + 1, removeCount);
                _selectedNodeIndex = startIdx;
            }
        }

        EditorUtility.SetDirty(loadedData);
        Repaint();
    }

    bool FindNodeAtCell(int cellIndex, out int zoneIdx, out int nodeIdx)
    {
        if (loadedData?.soulZones == null) { zoneIdx = -1; nodeIdx = -1; return false; }
        for (int zi = 0; zi < loadedData.soulZones.Count; zi++)
        {
            var zone = loadedData.soulZones[zi];
            if (zone.nodes == null) continue;
            for (int ni = 0; ni < zone.nodes.Count; ni++)
            {
                if (zone.nodes[ni] == cellIndex) { zoneIdx = zi; nodeIdx = ni; return true; }
            }
        }
        zoneIdx = -1; nodeIdx = -1; return false;
    }

    SelectionInfo FindAnythingAtCell(int cellIndex)
    {
        SelectionInfo info = new SelectionInfo { type = SelectionType.None, cellIndex = cellIndex, tierIndex = activeTierIndex };

        // 1. Soul Zone Nodes (High priority as they are small dots)
        if (FindNodeAtCell(cellIndex, out int zi, out int ni))
        {
            info.type = SelectionType.SoulZoneNode;
            info.index = zi;
            info.subIndex = ni;
            return info;
        }

        // 2. Prefab Placements
        if (activeTierIndex >= 0 && loadedData.tiers != null && activeTierIndex < loadedData.tiers.Count)
        {
            var tier = loadedData.tiers[activeTierIndex];
            int pIdx = tier.prefabPlacements?.FindIndex(p => p.cellIndex == cellIndex) ?? -1;
            if (pIdx >= 0)
            {
                info.type = SelectionType.PrefabPlacement;
                info.index = pIdx;
                return info;
            }
        }
        else
        {
            int pIdx = loadedData.prefabPlacements?.FindIndex(p => p.cellIndex == cellIndex) ?? -1;
            if (pIdx >= 0)
            {
                info.type = SelectionType.PrefabPlacement;
                info.tierIndex = -1;
                info.index = pIdx;
                return info;
            }
        }

        // 3. Whirlpools
        int wIdx = loadedData.whirlpools?.FindIndex(w => w.cellIndex == cellIndex) ?? -1;
        if (wIdx >= 0)
        {
            info.type = SelectionType.Whirlpool;
            info.index = wIdx;
            return info;
        }

        // 4. Modifiers
        var waterMods = GetActiveTierWaterModifiers();
        if (waterMods.Contains(cellIndex))
        {
            info.type = SelectionType.WaterModifier;
            return info;
        }

        var waveMods = GetActiveTierWaveModifiers();
        if (waveMods.Contains(cellIndex))
        {
            info.type = SelectionType.WaveModifier;
            return info;
        }

        // 5. Orbs (Base layer only)
        if (activeTierIndex == -1 && loadedData.orbCellIndices != null && loadedData.orbCellIndices.Contains(cellIndex))
        {
            info.type = SelectionType.Orb;
            return info;
        }

        // 6. Grid Slots
        if (activeTierIndex >= 0 && loadedData.tiers != null && activeTierIndex < loadedData.tiers.Count)
        {
            var tier = loadedData.tiers[activeTierIndex];
            if (tier.cells != null && tier.cells[cellIndex] > 0)
            {
                info.type = SelectionType.GridSlot;
                info.value = tier.cells[cellIndex];
                return info;
            }
        }
        else
        {
            if (circleGrid[cellIndex] > 0)
            {
                info.type = SelectionType.GridSlot;
                info.isCircle = true;
                info.value = circleGrid[cellIndex];
                return info;
            }
            if (squareGrid[cellIndex] > 0)
            {
                info.type = SelectionType.GridSlot;
                info.isCircle = false;
                info.value = squareGrid[cellIndex];
                return info;
            }
        }

        return new SelectionInfo { type = SelectionType.None };
    }

    void LogSelection(SelectionInfo info)
    {
        if (info.type == SelectionType.None)
        {
            GridLog("Deselected.");
            return;
        }

        string tierStr = info.tierIndex == -1 ? "Base" : $"T{info.tierIndex + 1}";
        string loc = $"at cell {info.cellIndex} ({tierStr})";

        switch (info.type)
        {
            case SelectionType.SoulZoneNode:
                GridLog($"Selected: Soul Zone {info.index} Node {info.subIndex + 1} {loc}");
                break;
            case SelectionType.PrefabPlacement:
                var placements = info.tierIndex == -1 ? loadedData.prefabPlacements : loadedData.tiers[info.tierIndex].prefabPlacements;
                string pName = (info.index >= 0 && info.index < placements.Count && placements[info.index].prefab != null) 
                    ? placements[info.index].prefab.name : "Prefab";
                GridLog($"Selected: {pName} {loc}");
                break;
            case SelectionType.Whirlpool:
                GridLog($"Selected: Whirlpool {info.index + 1} {loc}");
                break;
            case SelectionType.Orb:
                GridLog($"Selected: Orb {loc}");
                break;
            case SelectionType.WaterModifier:
                GridLog($"Selected: Water Modifier {loc}");
                break;
            case SelectionType.WaveModifier:
                GridLog($"Selected: Wave Modifier {loc}");
                break;
            case SelectionType.GridSlot:
                string note = (info.value >= 0 && info.value < slotNotes.Count) ? slotNotes[info.value] : "";
                string shape = info.isCircle ? "Circle" : "Square";
                GridLog($"Selected: Slot {info.value} ({shape}) {loc} {(string.IsNullOrEmpty(note) ? "" : " - " + note)}");
                break;
        }
    }

    void MoveSelection(SelectionInfo info, int newCellIndex, bool pushUndo = true)
    {
        if (info.type == SelectionType.None || info.cellIndex == newCellIndex) return;

        Undo.RecordObject(loadedData, "Move Selection");
        if (pushUndo) PushUndoSnapshot();

        // Suppress the per-cell log while dragging (pushUndo == false) to avoid console spam.
        if (pushUndo)
        {
            string tierStr = info.tierIndex == -1 ? "Base" : $"T{info.tierIndex + 1}";
            GridLog($"Moved selection from {info.cellIndex} to {newCellIndex} ({tierStr})");
        }

        switch (info.type)
        {
            case SelectionType.SoulZoneNode:
                var zone = loadedData.soulZones[info.index];
                zone.nodes[info.subIndex] = newCellIndex;
                _selectedNodeIndex = info.subIndex;
                _selectedZoneIndex = info.index;
                break;

            case SelectionType.PrefabPlacement:
                List<GridData.PrefabPlacement> placements = info.tierIndex == -1 
                    ? loadedData.prefabPlacements 
                    : loadedData.tiers[info.tierIndex].prefabPlacements;
                placements[info.index].cellIndex = newCellIndex;
                break;

            case SelectionType.Whirlpool:
                loadedData.whirlpools[info.index].cellIndex = newCellIndex;
                break;

            case SelectionType.Orb:
                loadedData.orbCellIndices.Remove(info.cellIndex);
                if (!loadedData.orbCellIndices.Contains(newCellIndex))
                    loadedData.orbCellIndices.Add(newCellIndex);
                break;

            case SelectionType.WaterModifier:
                var waterMods = info.tierIndex == -1 
                    ? loadedData.waterLevelModifierCellIndices 
                    : loadedData.tiers[info.tierIndex].waterLevelModifierCellIndices;
                waterMods.Remove(info.cellIndex);
                if (!waterMods.Contains(newCellIndex)) waterMods.Add(newCellIndex);
                break;

            case SelectionType.WaveModifier:
                var waveMods = info.tierIndex == -1 
                    ? loadedData.waveModifierCellIndices 
                    : loadedData.tiers[info.tierIndex].waveModifierCellIndices;
                waveMods.Remove(info.cellIndex);
                if (!waveMods.Contains(newCellIndex)) waveMods.Add(newCellIndex);
                break;

            case SelectionType.GridSlot:
                if (info.tierIndex >= 0)
                {
                    var tier = loadedData.tiers[info.tierIndex];
                    tier.cells[info.cellIndex] = 0;
                    tier.cells[newCellIndex] = info.value;
                }
                else if (info.isCircle)
                {
                    circleGrid[info.cellIndex] = 0;
                    circleGrid[newCellIndex] = info.value;
                }
                else
                {
                    squareGrid[info.cellIndex] = 0;
                    squareGrid[newCellIndex] = info.value;
                }
                break;
        }

        _currentSelection.cellIndex = newCellIndex;
        EditorUtility.SetDirty(loadedData);
        Repaint();
    }

    void CommitBridge()
    {
        if (!_isBridgeMode || _bridgeNodes.Count == 0) { CancelBridge(); return; }
        if (_selectedZoneIndex < 0 || _bridgeEndZoneIndex != _selectedZoneIndex) { CancelBridge(); return; }

        var zone = loadedData.soulZones[_selectedZoneIndex];
        int startIdx = _selectedNodeIndex;
        int endIdx   = _bridgeEndNodeIndex;

        if (startIdx > endIdx) { int tmp = startIdx; startIdx = endIdx; endIdx = tmp; }

        // Replace nodes between startIdx and endIdx with bridge cells
        Undo.RecordObject(loadedData, "Bridge Soul Zone Nodes");
        zone.nodes.RemoveRange(startIdx + 1, endIdx - startIdx - 1);
        zone.nodes.InsertRange(startIdx + 1, _bridgeNodes);
        EditorUtility.SetDirty(loadedData);
        CancelBridge();
        Repaint();
    }

    void EnsureSoulZones()
    {
        if (loadedData.soulZones == null)
            loadedData.soulZones = new List<GridData.SoulZone>();

        // Legacy migration: convert old soulSpawnPoints → soulZones on first access
        if (loadedData.soulSpawnPoints != null && loadedData.soulSpawnPoints.Count > 0
            && loadedData.soulZones.Count == 0)
        {
            foreach (var sp in loadedData.soulSpawnPoints)
            {
                loadedData.soulZones.Add(new GridData.SoulZone
                {
                    nodes  = new List<int> { sp.cellIndex },
                    souls  = sp.soulData != null ? new List<SoulData> { sp.soulData } : new List<SoulData>()
                });
            }
            loadedData.soulSpawnPoints.Clear();
            EditorUtility.SetDirty(loadedData);
            Debug.Log($"[GridDesigner] Migrated {loadedData.soulZones.Count} legacy soul spawn point(s) to soulZones.");
        }
    }

    void CommitDrawingNodes(GridData.SoulZone zone)
    {
        if (_drawingNodes.Count > 0)
        {
            zone.nodePositions = new List<Vector2>(_drawingNodes);
            // Freshly drawn paths default to curved; the zone's Curved Path toggle straightens.
            // (Filling nodeCount entries covers open and closed paths — a trailing spare on open
            // paths is never read.)
            zone.segmentCurved = new List<bool>(zone.nodePositions.Count);
            for (int i = 0; i < zone.nodePositions.Count; i++) zone.segmentCurved.Add(true);
            zone.nodeTension = new List<float>(zone.nodePositions.Count);
            for (int i = 0; i < zone.nodePositions.Count; i++) zone.nodeTension.Add(0.5f);
            // A redraw invalidates node indices — street lights must be re-placed.
            zone.streetLights?.Clear();
            EditorUtility.SetDirty(loadedData);
        }
        _drawingNodes.Clear();
        _drawingFirstCell  = -1;
        _isDrawingSoulArea = false;
        Repaint();
    }

    void CancelDrawingNodes()
    {
        // If zone was just created and has no nodes yet, remove it
        if (_activeSoulZoneIndex >= 0 && _activeSoulZoneIndex < loadedData.soulZones.Count)
        {
            var zone = loadedData.soulZones[_activeSoulZoneIndex];
            if (zone.nodePositions == null || zone.nodePositions.Count == 0)
            {
                loadedData.soulZones.RemoveAt(_activeSoulZoneIndex);
                EditorUtility.SetDirty(loadedData);
            }
        }
        _drawingNodes.Clear();
        _isDrawingSoulArea = false;
        _activeSoulZoneIndex = -1;
    }

    void DrawSoulZonesSection()
    {
        EnsureSoulZones();
        EnsureZoneIds();
        EnsureSoulDataCache();

        int zoneCount = loadedData.soulZones.Count;
        _showSoulSpawns = EditorGUILayout.Foldout(_showSoulSpawns,
            $"Soul Zones ({zoneCount})", true, EditorStyles.foldoutHeader);
        if (!_showSoulSpawns) return;

        if (GUILayout.Button("+ Soul Area"))
        {
            PushUndoSnapshot();
            var newZone = new GridData.SoulZone();
            loadedData.soulZones.Add(newZone);
            _activeSoulZoneIndex = loadedData.soulZones.Count - 1;
            _drawingNodes.Clear();
            _isDrawingSoulArea = true;
            drawSoulArea       = true;
            drawSoul = drawCircle = drawOrb = drawWhirlpool = drawSelect = false;
            ClearSelectState();
            EditorUtility.SetDirty(loadedData);
        }

        if (GUILayout.Button("Auto-Assign Unallocated Souls"))
            AutoAssignSouls();

        if (_isDrawingSoulArea)
        {
            EditorGUILayout.HelpBox(
                "Click cells to place nodes — click first node to close loop — Enter to finish open path — Esc to cancel",
                MessageType.Info);
        }

        EditorGUILayout.Space(4);

        int toDelete = -1;
        for (int zi = 0; zi < loadedData.soulZones.Count; zi++)
        {
            var zone = loadedData.soulZones[zi];
            Color zc = SoulZoneColor(zone, zi);

            bool isSelected = _activeSoulZoneIndex == zi;
            GUI.backgroundColor = isSelected ? zc : Color.white;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;

            // The first zone drawn is the Main Zone and can never be a sub-zone.
            bool isMainZone = zi == 0;

            // Name: "Main Zone", "Zone N", or "SubZone N" with a source tag in brackets.
            string roleLabel;
            if (!isMainZone && zone.zoneRole == GridData.SoulZone.ZoneRole.SubZone)
            {
                int subOrd = 0;
                for (int k = 0; k <= zi; k++)
                    if (k != 0 && loadedData.soulZones[k].zoneRole == GridData.SoulZone.ZoneRole.SubZone) subOrd++;
                string tag = zone.towerGuarded  ? " (Fish Bowl Sub-Zone)"
                           : zone.statueGuarded ? " (Statue Sub-Zone)"
                           : "";
                roleLabel = $"SubZone {subOrd}{tag}";
            }
            else
            {
                roleLabel = isMainZone ? "Main Zone" : $"Zone {zi}";
            }

            // Zone header row
            EditorGUILayout.BeginHorizontal();
            Color prev = GUI.contentColor;
            GUI.contentColor = zc;
            EditorGUILayout.LabelField($"● {roleLabel}", EditorStyles.boldLabel);
            GUI.contentColor = prev;

            if (GUILayout.Button("Select", GUILayout.Width(52)))
            {
                _activeSoulZoneIndex = zi;
                _isDrawingSoulArea   = false;
            }

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(22))) toDelete = zi;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            string closedLabel = zone.closedLoop ? "● CLOSED" : "○ OPEN";
            EditorGUILayout.LabelField($"{zone.nodePositions?.Count ?? 0} node(s)   {zone.souls?.Count ?? 0} soul(s)   {closedLabel}", EditorStyles.miniLabel);

            if (isSelected)
            {
                // Role — Main Path vs Sub-Zone (tributary). The Main Zone (first zone) is locked to
                // MainPath and can't be a sub-zone.
                if (isMainZone)
                {
                    if (zone.zoneRole != GridData.SoulZone.ZoneRole.MainPath)
                    { zone.zoneRole = GridData.SoulZone.ZoneRole.MainPath; EditorUtility.SetDirty(loadedData); }
                    EditorGUILayout.LabelField("Main Zone — the level's soul-fish path (can't be a sub-zone).", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    bool isSub = EditorGUILayout.ToggleLeft(
                        new GUIContent("Sub-Zone (tributary)", "A bowl/statue tributary that adjoins the main path. Rendered in teal; merges onto the main path when unlocked."),
                        zone.zoneRole == GridData.SoulZone.ZoneRole.SubZone);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(loadedData, "Set Soul Zone Role");
                        zone.zoneRole = isSub ? GridData.SoulZone.ZoneRole.SubZone : GridData.SoulZone.ZoneRole.MainPath;
                        EditorUtility.SetDirty(loadedData);
                        Repaint();
                    }
                }

                EditorGUILayout.LabelField($"Zone id: {zone.zoneId}", EditorStyles.miniLabel);

                // Junction — where a Sub-Zone adjoins a Main Path (which path + which node).
                if (zone.zoneRole == GridData.SoulZone.ZoneRole.SubZone)
                {
                    var mainIds    = new List<int> { 0 };
                    var mainLabels = new List<string> { "(none)" };
                    for (int mi = 0; mi < loadedData.soulZones.Count; mi++)
                    {
                        var mz = loadedData.soulZones[mi];
                        if (mz.zoneRole == GridData.SoulZone.ZoneRole.MainPath)
                        {
                            mainIds.Add(mz.zoneId);
                            mainLabels.Add($"Zone {mi} (id {mz.zoneId})");
                        }
                    }
                    int curSel = Mathf.Max(0, mainIds.IndexOf(zone.adjoinZoneId));

                    EditorGUI.BeginChangeCheck();
                    int newSel  = EditorGUILayout.Popup(new GUIContent("Adjoins Main Path", "The main path this tributary merges onto when unlocked."), curSel, mainLabels.ToArray());
                    int newNode = EditorGUILayout.IntField(new GUIContent("Adjoin Node", "Node index on the main path where this tributary joins (normally a street-light node). -1 = unset."), zone.adjoinNodeIndex);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(loadedData, "Set Sub-Zone Junction");
                        zone.adjoinZoneId    = mainIds[Mathf.Clamp(newSel, 0, mainIds.Count - 1)];
                        zone.adjoinNodeIndex = newNode;
                        EditorUtility.SetDirty(loadedData);
                    }

                    // Junction + gate status. Mirrors LevelSpawner's bowlTributaryChain test so the
                    // designer tells the truth about what will actually happen at runtime — a
                    // tributary is gated by ITS OWN lights, never by the main path's.
                    {
                        var mainZ = loadedData.soulZones.Find(m =>
                            m.zoneId == zone.adjoinZoneId && m.zoneRole == GridData.SoulZone.ZoneRole.MainPath);
                        bool joined = zone.adjoinZoneId != 0 && mainZ?.nodePositions != null
                                      && zone.adjoinNodeIndex >= 0
                                      && zone.adjoinNodeIndex < mainZ.nodePositions.Count;

                        int  nodeCount = zone.nodePositions?.Count ?? 0;
                        bool hasPath   = nodeCount >= 2;
                        bool loopBad   = zone.closedLoop && nodeCount >= 3;

                        // The join opens when the river FLOWS PAST the junction node — so the
                        // junction needs no lamp of its own; the main path just needs lights (a
                        // frontier that advances). Report which lamp will carry the river past it.
                        var mainLights = joined ? mainZ.StreetLightsInOrder() : null;
                        int passLamp = -1;   // 1-based number of the first lamp at/after the junction
                        if (mainLights != null)
                            for (int mi = 0; mi < mainLights.Count; mi++)
                                if (mainLights[mi] != null && mainLights[mi].nodeIndex >= zone.adjoinNodeIndex)
                                { passLamp = mi + 1; break; }

                        bool mainHasLights = mainLights != null && mainLights.Count > 0;

                        string msg;
                        MessageType mt;
                        if (!joined)
                        {
                            msg = "NOT CONNECTED — draw the path and finish on a Main-Path node to snap the junction.";
                            mt  = MessageType.Warning;
                        }
                        else if (!hasPath || !mainHasLights || loopBad)
                        {
                            msg = $"Connected to node {zone.adjoinNodeIndex} — but WILL NOT JOIN at runtime:";
                            if (!hasPath) msg += "\n• needs 2+ nodes (path from bowl to river)";
                            if (!mainHasLights)
                                msg += "\n• the main path has NO street lights, so its river never flows — "
                                     + "add at least one so it has an advancing frontier.";
                            if (loopBad) msg += "\n• closed loop — a tributary must be an open path";
                            mt = MessageType.Error;
                        }
                        else
                        {
                            string passBy = passLamp > 0
                                ? $"street light #{passLamp} is lit"
                                : "the river reaches its final light";
                            msg = $"CONNECTED to node {zone.adjoinNodeIndex}. The joining line draws when BOTH:"
                                + $"\n  1. the fish-bowl tower is toppled and its pool reaches full size, and"
                                + $"\n  2. the river has flowed past this node — i.e. {passBy}."
                                + $"\nThe shoal then swims up the river to the newest lit light.";
                            mt = MessageType.Info;
                        }

                        EditorGUILayout.HelpBox(msg, mt);
                    }

                    // Draw the tributary path out from the radius; drop the final node on a
                    // Main-Path node to snap the junction.
                    EditorGUILayout.BeginHorizontal();
                    bool drawingThis = _subZoneDrawIdx == zi;
                    GUI.backgroundColor = drawingThis ? new Color(0.5f, 0.8f, 1f) : Color.white;
                    if (GUILayout.Button(drawingThis ? "● Drawing… (click a Main-Path node to finish)" : "Draw Nodes"))
                    {
                        if (drawingThis) _subZoneDrawIdx = -1;
                        else
                        {
                            _subZoneDrawIdx    = zi;
                            _activeSoulZoneIndex = zi;
                            _isDrawingSoulArea = false;
                            // Take over the grid so no other tool (esp. Select) intercepts clicks.
                            drawSelect = drawSoulArea = drawSoul = drawCircle = drawOrb = drawWhirlpool = false;
                            drawWaterLevelModifier = drawWaveModifier = drawDirectPrefab = _drawSplineWall = _drawCubeBuilding = false; _drawSpike = false;
                            activeSlot = -1;
                            ClearSelectState();
                        }
                    }
                    GUI.backgroundColor = Color.white;
                    // Clear the drawn path (keep node 0, the radius anchor).
                    if (zone.nodePositions != null && zone.nodePositions.Count > 1)
                    {
                        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                        if (GUILayout.Button("Clear Path", GUILayout.Width(78)))
                        {
                            Undo.RecordObject(loadedData, "Clear Sub-Zone Path");
                            zone.nodePositions.RemoveRange(1, zone.nodePositions.Count - 1);
                            zone.adjoinZoneId = 0; zone.adjoinNodeIndex = -1;
                            if (_subZoneDrawIdx == zi) _subZoneDrawIdx = -1;
                            EditorUtility.SetDirty(loadedData);
                        }
                        GUI.backgroundColor = Color.white;
                    }
                    EditorGUILayout.EndHorizontal();

                    // Adjoining node is pinned to the main river. Separate to free it (deletes the join node).
                    if (zone.adjoinZoneId != 0)
                    {
                        if (GUILayout.Button(new GUIContent("Separate from Main Path", "Unlinks the junction and deletes the joining node, freeing the tributary end.")))
                        {
                            Undo.RecordObject(loadedData, "Separate Sub-Zone Junction");
                            if (zone.nodePositions != null && zone.nodePositions.Count > 1)
                                zone.nodePositions.RemoveAt(zone.nodePositions.Count - 1);
                            zone.adjoinZoneId    = 0;
                            zone.adjoinNodeIndex = -1;
                            EditorUtility.SetDirty(loadedData);
                        }
                    }
                }

                if (zone.statueGuarded)
                    EditorGUILayout.HelpBox(
                        $"Guarded by statue #{zone.linkedStatueId} — fish here can't be caught until the statue is destroyed.",
                        MessageType.Info);
                else if (zone.towerGuarded)
                    EditorGUILayout.HelpBox(
                        $"Fish-bowl tower #{zone.linkedStatueId} — fish swim in the bowl aloft and can't be caught until the tower is smashed and the bowl drops into the water.",
                        MessageType.Info);

                // Tower zones take their swim area + height from the tower prefab, so no radius/knot
                // sliders here — just souls. Everything else authors radius/knots as normal.
                if (zone.towerGuarded)
                {
                    // Fish-bowl sub-zone has its own pool radius (same visual as a street light).
                    EditorGUI.BeginChangeCheck();
                    float newBowlRadius = EditorGUILayout.Slider(
                        new GUIContent("Radius", "World-unit radius of the fish-bowl sub-zone's pool, drawn teal in the grid — same as a street-light pool."),
                        zone.radius, 0.1f, 5f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(loadedData, "Edit Fish-Bowl Sub-Zone Radius");
                        zone.radius = newBowlRadius;
                        EditorUtility.SetDirty(loadedData);
                        Repaint();
                    }

                    // Tributary path thickness (band half-width) — separate from the source pool.
                    EditorGUI.BeginChangeCheck();
                    float newPathW = EditorGUILayout.Slider(
                        new GUIContent("Path Width", "Thickness of the connecting river to the junction (world units). Small = thin river; separate from the source pool radius."),
                        zone.pathWidth, 0f, 2f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(loadedData, "Edit Tributary Path Width");
                        zone.pathWidth = newPathW;
                        EditorUtility.SetDirty(loadedData);
                        Repaint();
                    }

                    EditorGUILayout.LabelField("Bowl aloft height is set on the tower prefab (FishBowlTowerController).", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    string scatterLabel = zone.statueGuarded ? "Scatter" : "Radius";
                    float newRadius = EditorGUILayout.Slider(scatterLabel, zone.radius, 0.1f, 5f);
                    int   newKnots  = EditorGUILayout.IntSlider("Knot Count", zone.knotCount, 3, 100);
                    int   newRes    = EditorGUILayout.IntSlider(
                        new GUIContent("Curve Resolution", "Max samples per curved segment at runtime. Higher = smoother curves on the water, but the mask's 40-point budget is shared across all zones + fish."),
                        zone.EffectiveCurveResolution, 2, 16);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(loadedData, "Edit Soul Zone");
                        zone.radius          = newRadius;
                        zone.knotCount       = newKnots;
                        zone.curveResolution = newRes;
                        EditorUtility.SetDirty(loadedData);
                    }
                }

                if (zone.statueGuarded)
                {
                    EditorGUI.BeginChangeCheck();
                    float newRing = EditorGUILayout.Slider("Ring Radius", zone.ringRadius, 0.02f, 0.4f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        // Regenerate the ring around its current centre (nodes stay editable after)
                        Undo.RecordObject(loadedData, "Edit Guard Ring");
                        zone.ringRadius = newRing;
                        BuildRing(zone, Centroid(zone.nodePositions), newRing, StatueRingNodeCount);
                        EditorUtility.SetDirty(loadedData);
                    }
                }
                else if (!zone.towerGuarded)
                {
                    EditorGUI.BeginChangeCheck();
                    bool newClosed = EditorGUILayout.Toggle("Closed Loop", zone.closedLoop);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(loadedData, "Toggle Closed Loop");
                        zone.closedLoop = newClosed;
                        EditorUtility.SetDirty(loadedData);
                    }

                    // Door-to-door: pin the path's ends to arena entrances, so the river of souls
                    // arrives through one door and leaves by another. The pinned nodes track their
                    // entrance, so moving an entrance angle drags the path end with it.
                    EditorGUI.BeginChangeCheck();
                    bool newAttach = EditorGUILayout.Toggle(
                        new GUIContent("Attach To Entrances",
                                       "Clamp the first and last nodes onto chosen arena entrances."),
                        zone.attachToEntrances);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(loadedData, "Toggle Attach To Entrances");
                        zone.attachToEntrances = newAttach;
                        EditorUtility.SetDirty(loadedData);
                    }

                    if (zone.attachToEntrances)
                    {
                        int entCount = loadedData.entrances?.Count ?? 0;
                        if (entCount == 0)
                        {
                            EditorGUILayout.HelpBox("This level has no entrances yet — add one in the Entrances section.",
                                                    MessageType.Warning);
                        }
                        else
                        {
                            var labels = new string[entCount + 1];
                            labels[0] = "(none)";
                            for (int ei = 0; ei < entCount; ei++)
                            {
                                var e = loadedData.entrances[ei];
                                labels[ei + 1] = $"{ei}: {e.id}  ({e.perimeterAngle:0.#}°)";
                            }

                            EditorGUI.BeginChangeCheck();
                            int entrySel = Mathf.Clamp(zone.entryEntranceIndex + 1, 0, entCount);
                            int exitSel  = Mathf.Clamp(zone.exitEntranceIndex  + 1, 0, entCount);
                            entrySel = EditorGUILayout.Popup(
                                new GUIContent("  From Entrance", "Entrance the path's FIRST node clamps to."),
                                entrySel, labels);
                            exitSel = EditorGUILayout.Popup(
                                new GUIContent("  To Entrance", "Entrance the path's LAST node clamps to."),
                                exitSel, labels);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(loadedData, "Set Zone Entrances");
                                zone.entryEntranceIndex = entrySel - 1;
                                zone.exitEntranceIndex  = exitSel  - 1;
                                EditorUtility.SetDirty(loadedData);
                            }

                            if (zone.entryEntranceIndex >= 0 && zone.entryEntranceIndex == zone.exitEntranceIndex)
                                EditorGUILayout.HelpBox("Both ends are pinned to the same entrance — pick two different doors.",
                                                        MessageType.Warning);
                            else if (zone.nodePositions != null && zone.nodePositions.Count < 2
                                     && zone.exitEntranceIndex >= 0)
                                EditorGUILayout.HelpBox("Needs at least 2 nodes before the exit end can be pinned.",
                                                        MessageType.Info);
                        }
                    }
                }

                // Whole-path curve toggle (per-segment control lives in the selected-node box).
                // Shown for statue rings and tributaries too — anything with an authored path.
                if (zone.SegmentCount() > 0)
                {
                    int segTotal = zone.SegmentCount();
                    bool allCurved = true;
                    for (int s = 0; s < segTotal; s++)
                        if (!zone.IsSegmentCurved(s)) { allCurved = false; break; }

                    EditorGUI.BeginChangeCheck();
                    bool newCurvedAll = EditorGUILayout.Toggle("Curved Path", allCurved);
                    if (EditorGUI.EndChangeCheck())
                        SetAllSoulZoneSegmentsCurved(zone, newCurvedAll);
                }

                // Souls list
                EditorGUILayout.LabelField("Souls", EditorStyles.boldLabel);
                if (zone.souls == null) zone.souls = new List<SoulData>();
                int soulToRemove = -1;
                for (int si = 0; si < zone.souls.Count; si++)
                {
                    EditorGUILayout.BeginHorizontal();
                    int currentIdx = -1;
                    if (_allSoulData != null && zone.souls[si] != null)
                        for (int s = 0; s < _allSoulData.Length; s++)
                            if (_allSoulData[s] == zone.souls[si]) { currentIdx = s; break; }

                    int popupSel = currentIdx + 1;
                    EditorGUI.BeginChangeCheck();
                    popupSel = EditorGUILayout.Popup(popupSel, _soulDropdownLabels);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(loadedData, "Assign Soul to Zone");
                        if (zone.souls[si] != null)
                        {
                            Undo.RecordObject(zone.souls[si], "Unallocate Soul");
                            zone.souls[si].allocated          = false;
                            zone.souls[si].allocatedToLevelID = "";
                            EditorUtility.SetDirty(zone.souls[si]);
                        }
                        SoulData newSoul = popupSel > 0 ? _allSoulData[popupSel - 1] : null;
                        zone.souls[si] = newSoul;
                        if (newSoul != null)
                        {
                            Undo.RecordObject(newSoul, "Allocate Soul");
                            newSoul.allocated          = true;
                            newSoul.allocatedToLevelID = loadedData.levelID;
                            if (loadedData.gameplayWavePreset != null)
                                newSoul.associatedWavePreset = loadedData.gameplayWavePreset;
                            EditorUtility.SetDirty(newSoul);
                        }
                        EditorUtility.SetDirty(loadedData);
                        _allSoulData = null;
                    }

                    GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("✕", GUILayout.Width(22))) soulToRemove = si;
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                }

                if (soulToRemove >= 0)
                {
                    Undo.RecordObject(loadedData, "Remove Soul from Zone");
                    zone.souls.RemoveAt(soulToRemove);
                    EditorUtility.SetDirty(loadedData);
                }

                if (GUILayout.Button("+ Add Soul"))
                {
                    Undo.RecordObject(loadedData, "Add Soul to Zone");
                    if (zone.souls == null) zone.souls = new List<SoulData>();
                    zone.souls.Add(null);
                    EditorUtility.SetDirty(loadedData);
                }

                // Fish-bowl tributaries now have an authored path, so they get the node-editing UI
                // (select a node → curve toggle / street light). Main paths and statues keep it too.
                if (!zone.towerGuarded || zone.zoneRole == GridData.SoulZone.ZoneRole.SubZone)
                {
                EditorGUILayout.Space(2);
                int nodeCount = zone.nodePositions?.Count ?? 0;
                GUI.contentColor = zone.closedLoop ? Color.green : Color.yellow;
                EditorGUILayout.LabelField(
                    zone.closedLoop ? $"Nodes: {nodeCount}  ● CLOSED LOOP" : $"Nodes: {nodeCount}  ○ OPEN PATH",
                    EditorStyles.miniLabel);
                GUI.contentColor = Color.white;

                // Selected node controls
                if (_selectedZoneIndex == zi && _selectedNodeIndex >= 0
                    && zone.nodePositions != null && _selectedNodeIndex < zone.nodePositions.Count)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField($"Selected: Node {_selectedNodeIndex + 1} of {zone.nodePositions.Count}", EditorStyles.miniLabel);
                    EditorGUILayout.BeginHorizontal();

                    if (GUILayout.Button("Insert Before", GUILayout.Height(20)))
                    {
                        Undo.RecordObject(loadedData, "Insert Node Before");
                        zone.nodePositions.Insert(_selectedNodeIndex, InsertedNodePos(zone, _selectedNodeIndex, -1));
                        SoulZoneNodeInserted(zone, _selectedNodeIndex);
                        EditorUtility.SetDirty(loadedData);
                    }
                    if (GUILayout.Button("Insert After", GUILayout.Height(20)))
                    {
                        Undo.RecordObject(loadedData, "Insert Node After");
                        int insertIdx = _selectedNodeIndex + 1;
                        zone.nodePositions.Insert(insertIdx, InsertedNodePos(zone, _selectedNodeIndex, +1));
                        SoulZoneNodeInserted(zone, insertIdx);
                        _selectedNodeIndex = insertIdx;
                        EditorUtility.SetDirty(loadedData);
                    }
                    GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("Delete", GUILayout.Width(52), GUILayout.Height(20)))
                    {
                        Undo.RecordObject(loadedData, "Delete Node");
                        zone.nodePositions.RemoveAt(_selectedNodeIndex);
                        SoulZoneNodeDeleted(zone, _selectedNodeIndex);
                        _selectedNodeIndex = Mathf.Clamp(_selectedNodeIndex - 1, -1, zone.nodePositions.Count - 1);
                        EditorUtility.SetDirty(loadedData);
                    }
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();

                    // Curvature AT this node: 0 = sharp corner, 0.5 = natural, 1 = very round.
                    // The same value drives the painted mask and the fish's swim spline, because
                    // both sample GridData.SoulZone.SamplePath.
                    {
                        float curTension = zone.NodeTension(_selectedNodeIndex);
                        EditorGUI.BeginChangeCheck();
                        float newTension = EditorGUILayout.Slider(
                            new GUIContent("Node Curve", "Curvature at this node. 0 = sharp corner, " +
                                           "0.5 = natural smoothing, 1 = very round. Drives the mask and the fish path alike."),
                            curTension, 0f, 1f);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(loadedData, "Edit Node Curve");
                            SetNodeTension(zone, _selectedNodeIndex, newTension);
                            EditorUtility.SetDirty(loadedData);
                        }

                        EditorGUILayout.BeginHorizontal();
                        if (GUILayout.Button(new GUIContent("Sharp", "Tension 0 — hard corner"), EditorStyles.miniButtonLeft))
                        { Undo.RecordObject(loadedData, "Sharpen Node"); SetNodeTension(zone, _selectedNodeIndex, 0f); EditorUtility.SetDirty(loadedData); }
                        if (GUILayout.Button(new GUIContent("Natural", "Tension 0.5 — the classic curve"), EditorStyles.miniButtonMid))
                        { Undo.RecordObject(loadedData, "Smooth Node"); SetNodeTension(zone, _selectedNodeIndex, 0.5f); EditorUtility.SetDirty(loadedData); }
                        if (GUILayout.Button(new GUIContent("Round", "Tension 1 — wide sweeping bend"), EditorStyles.miniButtonRight))
                        { Undo.RecordObject(loadedData, "Round Node"); SetNodeTension(zone, _selectedNodeIndex, 1f); EditorUtility.SetDirty(loadedData); }
                        EditorGUILayout.EndHorizontal();
                    }

                    // Street light on this node — gates zone progression. Lights are numbered
                    // in path order; only #1 starts lit, feeding a caught fish to the next one
                    // draws the zone onward (SoulZoneStreetLightChain).
                    var lightHere = zone.StreetLightAtNode(_selectedNodeIndex);
                    bool isLight = lightHere != null;
                    bool newIsLight = GUILayout.Toggle(isLight,
                        new GUIContent(isLight ? "★ StreetLight" : "☆ StreetLight",
                                       "Place/remove a street light on this node — gates the zone's fish progression"),
                        EditorStyles.miniButton);
                    if (newIsLight != isLight)
                    {
                        Undo.RecordObject(loadedData, newIsLight ? "Add Street Light" : "Remove Street Light");
                        if (zone.streetLights == null) zone.streetLights = new List<GridData.SoulZone.StreetLight>();
                        if (newIsLight)
                            zone.streetLights.Add(new GridData.SoulZone.StreetLight
                            {
                                nodeIndex  = _selectedNodeIndex,
                                poolRadius = Mathf.Max(zone.radius, 0.5f)
                            });
                        else
                            zone.streetLights.RemoveAll(l => l == null || l.nodeIndex == _selectedNodeIndex);
                        lightHere = zone.StreetLightAtNode(_selectedNodeIndex);
                        EditorUtility.SetDirty(loadedData);
                    }
                    if (lightHere != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        float newPool = EditorGUILayout.Slider("Pool Radius", lightHere.poolRadius, 0.1f, 6f);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(loadedData, "Edit Street Light Pool Radius");
                            lightHere.poolRadius = newPool;
                            EditorUtility.SetDirty(loadedData);
                        }
                    }
                    EditorGUILayout.EndVertical();
                }
                // Redraw/Add Nodes uses the main soul-area draw — not for fish bowls, which have
                // their own "Draw Nodes" junction mode in the sub-zone panel above.
                if (!zone.towerGuarded)
                {
                    bool hasNodes = zone.nodePositions != null && zone.nodePositions.Count > 0;
                    string drawBtnLabel = hasNodes ? "Redraw Nodes" : "Add Nodes";
                    if (GUILayout.Button(drawBtnLabel))
                    {
                        // Redrawing an authored zone is destructive on commit (path, curve flags and
                        // street lights are replaced), so confirm first and snapshot for undo.
                        // Esc while drawing still cancels safely without touching the zone.
                        int lightCount = zone.streetLights?.Count ?? 0;
                        bool proceed = !hasNodes || EditorUtility.DisplayDialog(
                            "Redraw Zone Nodes?",
                            $"This will replace Zone {zi}'s path ({zone.nodePositions.Count} node(s))" +
                            (lightCount > 0 ? $" and remove its {lightCount} street light(s)" : "") +
                            " when you finish drawing.\n\nEsc while drawing cancels safely.",
                            "Redraw", "Cancel");
                        if (proceed)
                        {
                            if (hasNodes) PushUndoSnapshot();
                            _activeSoulZoneIndex = zi;
                            _drawingNodes.Clear();
                            _isDrawingSoulArea   = true;
                            drawSoulArea         = true;
                            activeSlot           = -1;
                            drawSoul = drawCircle = drawOrb = drawSelect = false;
                            ClearSelectState();
                        }
                    }
                }
                } // end node-editing section
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        if (toDelete >= 0)
        {
            PushUndoSnapshot();
            loadedData.soulZones.RemoveAt(toDelete);
            if (_activeSoulZoneIndex == toDelete) _activeSoulZoneIndex = -1;
            else if (_activeSoulZoneIndex > toDelete) _activeSoulZoneIndex--;
            EditorUtility.SetDirty(loadedData);
        }
    }

    void DrawWhirlpoolsSection()
    {
        if (loadedData.whirlpools == null) loadedData.whirlpools = new List<GridData.WhirlpoolPoint>();

        _showWhirlpools = EditorGUILayout.Foldout(_showWhirlpools,
            $"Whirlpools ({loadedData.whirlpools.Count})", true, EditorStyles.foldoutHeader);
        if (!_showWhirlpools) return;

        EditorGUI.BeginChangeCheck();
        float newDepth = EditorGUILayout.Slider("Global Depth", loadedData.whirlpoolDepth, 0f, 20f);
        float newSwirl = EditorGUILayout.Slider("Global Swirl", loadedData.whirlpoolSwirl, 0f, 10f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Edit Whirlpool Settings");
            loadedData.whirlpoolDepth = newDepth;
            loadedData.whirlpoolSwirl = newSwirl;
            EditorUtility.SetDirty(loadedData);
        }

        EditorGUILayout.Space(2);

        if (loadedData.whirlpools.Count == 0)
        {
            EditorGUILayout.LabelField("  (click 〇 Whirlpool tool then paint on the grid)", EditorStyles.miniLabel);
        }

        int toRemove = -1;
        for (int i = 0; i < loadedData.whirlpools.Count; i++)
        {
            var wp = loadedData.whirlpools[i];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"#{i + 1}  Cell {wp.cellIndex}", GUILayout.Width(90));
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(22))) toRemove = i;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            float newRadius = EditorGUILayout.Slider("Radius", wp.radius, 0.1f, 30f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Edit Whirlpool Radius");
                wp.radius = newRadius;
                loadedData.whirlpools[i] = wp;
                EditorUtility.SetDirty(loadedData);
            }
            EditorGUILayout.EndVertical();
        }

        if (toRemove >= 0)
        {
            Undo.RecordObject(loadedData, "Remove Whirlpool");
            loadedData.whirlpools.RemoveAt(toRemove);
            EditorUtility.SetDirty(loadedData);
        }
    }

    void AutoAssignSouls()
    {
        EnsureSoulDataCache();
        if (_allSoulData == null || loadedData == null) return;

        // Build queue of unallocated souls
        var unallocated = new Queue<SoulData>();
        foreach (var s in _allSoulData)
            if (s != null && !s.allocated)
                unallocated.Enqueue(s);

        if (unallocated.Count == 0)
        {
            EditorUtility.DisplayDialog("Auto-Assign Souls", "No unallocated souls available.", "OK");
            return;
        }

        int assigned = 0;
        Undo.RecordObject(loadedData, "Auto-Assign Souls");

        foreach (var zone in loadedData.soulZones)
        {
            if (zone.souls == null) zone.souls = new List<SoulData>();
            for (int i = 0; i < zone.souls.Count; i++)
            {
                if (zone.souls[i] != null) continue; // already assigned
                if (unallocated.Count == 0) break;

                SoulData soul = unallocated.Dequeue();
                Undo.RecordObject(soul, "Allocate Soul");
                soul.allocated          = true;
                soul.allocatedToLevelID = loadedData.levelID;
                if (loadedData.gameplayWavePreset != null)
                    soul.associatedWavePreset = loadedData.gameplayWavePreset;
                EditorUtility.SetDirty(soul);
                zone.souls[i] = soul;
                assigned++;
            }
        }

        EditorUtility.SetDirty(loadedData);
        _allSoulData = null; // refresh dropdown labels
        Debug.Log($"[GridDesigner] Auto-assigned {assigned} soul(s). {unallocated.Count} unallocated soul(s) remaining.");
    }

    void EnsureSoulDataCache()
    {
        if (_allSoulData != null) return;

        string[] guids = AssetDatabase.FindAssets("t:SoulData");
        var list = new List<SoulData>();
        foreach (string g in guids)
        {
            var s = AssetDatabase.LoadAssetAtPath<SoulData>(AssetDatabase.GUIDToAssetPath(g));
            if (s != null) list.Add(s);
        }
        list.Sort((a, b) => a.soulDataIdentity.CompareTo(b.soulDataIdentity));
        _allSoulData = list.ToArray();

        // Build dropdown labels
        _soulDropdownLabels = new string[_allSoulData.Length + 1];
        _soulDropdownLabels[0] = "(none)";
        for (int i = 0; i < _allSoulData.Length; i++)
        {
            var soul = _allSoulData[i];
            string label = $"{soul.soulDataIdentity}: {soul.name}";
            if (soul.allocated && soul.allocatedToLevelID != loadedData.levelID)
                label += $"  (→ {soul.allocatedToLevelID})";
            _soulDropdownLabels[i + 1] = label;
        }
    }

    void RandomiseSoulWavePresets()
    {
        string[] guids = AssetDatabase.FindAssets("t:SoulData");
        string[] waveGuids = AssetDatabase.FindAssets("t:WavePreset");

        if (waveGuids.Length == 0)
        {
            Debug.LogWarning("[GridDesigner] No WavePreset assets found.");
            return;
        }

        var wavePresets = new List<WavePreset>();
        foreach (string g in waveGuids)
        {
            var wp = AssetDatabase.LoadAssetAtPath<WavePreset>(AssetDatabase.GUIDToAssetPath(g));
            if (wp != null) wavePresets.Add(wp);
        }

        int assigned = 0;
        foreach (string g in guids)
        {
            var soul = AssetDatabase.LoadAssetAtPath<SoulData>(AssetDatabase.GUIDToAssetPath(g));
            if (soul == null) continue;

            Undo.RecordObject(soul, "Randomise Soul Wave Presets");
            soul.associatedWavePreset = wavePresets[UnityEngine.Random.Range(0, wavePresets.Count)];
            EditorUtility.SetDirty(soul);
            assigned++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[GridDesigner] Randomised wave presets for {assigned} souls.");
    }

    // ─────────────────────────────────────────────
    // PORTAL LIST  (Arena Entrances)
    // ─────────────────────────────────────────────

    void DrawPortalList()
    {
        if (loadedData.entrances == null) loadedData.entrances = new List<GridData.ArenaEntrance>();

        string[] tierLabels = BuildTierLabels();

        // ── Entrances ──
        EditorGUILayout.LabelField("Entrances", EditorStyles.boldLabel);

        int entToRemove = -1;
        for (int i = 0; i < loadedData.entrances.Count; i++)
        {
            var ent = loadedData.entrances[i];
            bool wasLocked = ent.isLocked;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Row 1: ID / Angle / remove button
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("ID", GUILayout.Width(18));
            string newEntID    = EditorGUILayout.TextField(ent.id, GUILayout.Width(78));
            
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 36f;
            float newEntAngle = EditorGUILayout.FloatField("Angle", ent.perimeterAngle, GUILayout.Width(74));
            EditorGUIUtility.labelWidth = prevLabelWidth;

            EditorGUILayout.LabelField("°", GUILayout.Width(10));
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("↺", GUILayout.Width(22)))
            {
                Undo.RecordObject(loadedData, "Reset Entrance");
                loadedData.entrances[i] = new GridData.ArenaEntrance
                {
                    id             = ent.id,
                    perimeterAngle = ent.perimeterAngle,
                };
                EditorUtility.SetDirty(loadedData);
            }
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(22))) entToRemove = i;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // Row 2: Tier
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Tier", GUILayout.Width(28));
            int entTierPopup   = EditorGUILayout.Popup(Mathf.Clamp(ent.tierSlot + 1, 0, tierLabels.Length - 1), tierLabels, GUILayout.Width(72));
            int newEntTierSlot = entTierPopup - 1;
            EditorGUILayout.EndHorizontal();

            // Row 3: Locked toggle + hub angle
            EditorGUILayout.BeginHorizontal();
            bool newIsLocked = EditorGUILayout.ToggleLeft("Locked", ent.isLocked, GUILayout.Width(62));
            float newHubAngle = ent.lockHubAngle;
            GameObject newHubPrefab = ent.lockHubPrefab;
            if (newIsLocked)
            {
                float prevLW2 = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 46f;
                newHubAngle = EditorGUILayout.FloatField("Hub °", ent.lockHubAngle, GUILayout.Width(88));
                EditorGUIUtility.labelWidth = prevLW2;
            }
            EditorGUILayout.EndHorizontal();

            if (newIsLocked)
            {
                // Row 4: Hub prefab
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Hub Prefab", GUILayout.Width(70));
                newHubPrefab = (GameObject)EditorGUILayout.ObjectField(ent.lockHubPrefab, typeof(GameObject), false);
                EditorGUILayout.EndHorizontal();

                // Row 5: Subdivisions + Regenerate
                if (ent.tubePath == null) ent.tubePath = new List<UnityEngine.Vector2Int>();
                EditorGUILayout.BeginHorizontal();
                float prevLW3 = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 72f;
                int newSubs = EditorGUILayout.IntField("Subdivisions", ent.tubeSubdivisions, GUILayout.Width(120));
                EditorGUIUtility.labelWidth = prevLW3;
                if (newSubs != ent.tubeSubdivisions)
                {
                    Undo.RecordObject(loadedData, "Set Tube Subdivisions");
                    ent.tubeSubdivisions = Mathf.Max(0, newSubs);
                    EditorUtility.SetDirty(loadedData);
                }
                GUI.enabled = ent.tubePath.Count >= 2;
                if (GUILayout.Button("+", GUILayout.Width(22)))
                {
                    Undo.RecordObject(loadedData, "Subdivide Tube Path");
                    var old = ent.tubePath;
                    var subdivided = new List<UnityEngine.Vector2Int>();
                    for (int si = 0; si < old.Count - 1; si++)
                    {
                        subdivided.Add(old[si]);
                        var mid = new UnityEngine.Vector2Int(
                            Mathf.RoundToInt((old[si].x + old[si + 1].x) * 0.5f),
                            Mathf.RoundToInt((old[si].y + old[si + 1].y) * 0.5f));
                        subdivided.Add(mid);
                    }
                    subdivided.Add(old[old.Count - 1]);
                    ent.tubePath = subdivided;
                    EditorUtility.SetDirty(loadedData);
                }
                if (GUILayout.Button("Regenerate", GUILayout.Width(78)))
                    GenerateTubePath(i);
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                // Row 6: Place / Edit / Clear
                bool isPlacingThis = _tubePlacingEntranceIndex == i;
                bool isEditingThis = _tubeDrawEntranceIndex == i;
                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = isPlacingThis ? new Color(0.4f, 1f, 0.6f) : Color.white;
                if (GUILayout.Button(isPlacingThis ? "● Placing…" : "Place Input Tube", GUILayout.Width(110)))
                {
                    if (isPlacingThis)
                    {
                        _tubePlacingEntranceIndex = -1;
                    }
                    else
                    {
                        _tubePlacingEntranceIndex = i;
                        _tubeDrawEntranceIndex    = -1;
                        _pipeTubePlacingIndex     = -1;
                        _pipeTubeDrawIndex        = -1;
                        _dragTubeNodeIndex        = -1;
                        _selectedTubeNodeIndex    = -1;
                    }
                }
                GUI.backgroundColor = Color.white;
                if (ent.tubePath.Count > 0)
                {
                    GUI.backgroundColor = isEditingThis ? new Color(0.5f, 0.8f, 1f) : Color.white;
                    if (GUILayout.Button(isEditingThis ? "● Editing" : "Edit Nodes", GUILayout.Width(76)))
                    {
                        if (isEditingThis)
                        {
                            _tubeDrawEntranceIndex = -1;
                            _dragTubeNodeIndex     = -1;
                            _selectedTubeNodeIndex = -1;
                        }
                        else
                        {
                            _tubeDrawEntranceIndex    = i;
                            _tubePlacingEntranceIndex = -1;
                            _pipeTubePlacingIndex     = -1;
                            _pipeTubeDrawIndex        = -1;
                        }
                    }
                    GUI.backgroundColor = Color.white;
                }
                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    Undo.RecordObject(loadedData, "Clear Tube Path");
                    ent.tubePath           = new List<UnityEngine.Vector2Int>();
                    _tubePlacingEntranceIndex = -1;
                    _tubeDrawEntranceIndex    = -1;
                    _dragTubeNodeIndex        = -1;
                    _selectedTubeNodeIndex    = -1;
                    EditorUtility.SetDirty(loadedData);
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.LabelField($"{ent.tubePath.Count} nodes", GUILayout.Width(52));
                EditorGUILayout.EndHorizontal();

                // ── Knot breakdown ──
                if (ent.tubePath != null && ent.tubePath.Count >= 2)
                {
                    int tubeKnots = 0;
                    int hubKnots  = 0;
                    const int bridgeKnots = 3; // SoulFishInputTube.joinKnotCount default

                    var tubePrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/ModifierPrefabs/SoulFishInputTube.prefab");
                    if (tubePrefabAsset != null)
                    {
                        var sc = tubePrefabAsset.GetComponentInChildren<UnityEngine.Splines.SplineContainer>();
                        if (sc != null) tubeKnots = sc.Spline.Count;
                    }

                    if (ent.lockHubPrefab != null)
                    {
                        var hubPrefabPath = AssetDatabase.GetAssetPath(ent.lockHubPrefab);
                        var hubAsset      = AssetDatabase.LoadAssetAtPath<GameObject>(hubPrefabPath);
                        if (hubAsset != null)
                        {
                            var sc = hubAsset.GetComponentInChildren<UnityEngine.Splines.SplineContainer>();
                            if (sc != null) hubKnots = sc.Spline.Count;
                        }
                    }

                    int waypointKnots = Mathf.Max(0, ent.tubePath.Count - 2);
                    int total         = tubeKnots + waypointKnots + bridgeKnots + hubKnots;

                    EditorGUILayout.LabelField(
                        $"Knots — tube:{tubeKnots}  path:{waypointKnots}  bridge:{bridgeKnots}  hub:{hubKnots}  = {total}",
                        EditorStyles.miniLabel);
                }
            }
            else
            {
                if (_tubeDrawEntranceIndex    == i) _tubeDrawEntranceIndex    = -1;
                if (_tubePlacingEntranceIndex == i) _tubePlacingEntranceIndex = -1;
            }

            EditorGUILayout.EndVertical();

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Edit Entrance");
                ent.id             = newEntID;
                ent.perimeterAngle = newEntAngle;
                ent.tierSlot       = newEntTierSlot;
                ent.isLocked       = newIsLocked;
                ent.lockHubAngle   = newHubAngle;
                ent.lockHubPrefab  = newHubPrefab;

                // When first locking an entrance, default hub angle to the door's own angle
                if (newIsLocked && !wasLocked)
                    ent.lockHubAngle = ent.perimeterAngle;

                // Auto-assign default lock hub prefab when locking an entrance
                if (newIsLocked && ent.lockHubPrefab == null)
                    ent.lockHubPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Prefab/LevelPrefabs/DoorLockHub.prefab");

                EditorUtility.SetDirty(loadedData);
                Repaint();
            }
        }

        if (entToRemove >= 0)
        {
            Undo.RecordObject(loadedData, "Remove Entrance");
            loadedData.entrances.RemoveAt(entToRemove);
            if (_tubeDrawEntranceIndex    == entToRemove) { _tubeDrawEntranceIndex    = -1; _dragTubeNodeIndex = -1; _selectedTubeNodeIndex = -1; }
            else if (_tubeDrawEntranceIndex    > entToRemove) _tubeDrawEntranceIndex--;
            if (_tubePlacingEntranceIndex == entToRemove)   _tubePlacingEntranceIndex = -1;
            else if (_tubePlacingEntranceIndex > entToRemove) _tubePlacingEntranceIndex--;
            EditorUtility.SetDirty(loadedData);
        }

        if (GUILayout.Button("+ Add Entrance"))
        {
            Undo.RecordObject(loadedData, "Add Entrance");
            loadedData.entrances.Add(new GridData.ArenaEntrance
                { id = $"entrance_{loadedData.entrances.Count}" });
            EditorUtility.SetDirty(loadedData);
        }

    }

    // ─────────────────────────────────────────────
    // WATER LEVEL MODIFIERS  (perimeter exit pipes)
    // Mirrors DrawPortalList: each entry is placed by angle around the arena and
    // fed by an optional soul input tube authored on the grid (same as the lock tube).
    // ─────────────────────────────────────────────

    void DrawWaterModifierList()
    {
        if (loadedData.arenaWaterModifiers == null)
            loadedData.arenaWaterModifiers = new List<GridData.ArenaWaterModifier>();

        string[] tierLabels = BuildTierLabels();

        EditorGUILayout.LabelField("Water Level Modifiers", EditorStyles.boldLabel);

        int toRemove = -1;
        for (int i = 0; i < loadedData.arenaWaterModifiers.Count; i++)
        {
            var wm = loadedData.arenaWaterModifiers[i];
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Row 1: ID / Angle / remove
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("ID", GUILayout.Width(18));
            string newID = EditorGUILayout.TextField(wm.id, GUILayout.Width(78));

            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 36f;
            float newAngle = EditorGUILayout.FloatField("Angle", wm.perimeterAngle, GUILayout.Width(74));
            EditorGUIUtility.labelWidth = prevLabelWidth;
            EditorGUILayout.LabelField("°", GUILayout.Width(10));

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(22))) toRemove = i;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // Row 2: Tier + spawn radius
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Tier", GUILayout.Width(28));
            int tierPopup   = EditorGUILayout.Popup(Mathf.Clamp(wm.tierSlot + 1, 0, tierLabels.Length - 1), tierLabels, GUILayout.Width(72));
            int newTierSlot = tierPopup - 1;
            float prevLW = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 74f;
            float newSpawnRadius = EditorGUILayout.FloatField("Inward", wm.spawnRadius, GUILayout.Width(120));
            EditorGUIUtility.labelWidth = prevLW;
            EditorGUILayout.EndHorizontal();

            // Row 3: Prefab
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Prefab", GUILayout.Width(50));
            GameObject newPrefab = (GameObject)EditorGUILayout.ObjectField(wm.prefab, typeof(GameObject), false);
            EditorGUILayout.EndHorizontal();

            // Reference: the level this pipe lowers water to (read off the prefab controller)
            if (wm.prefab != null)
            {
                var ctrl = wm.prefab.GetComponentInChildren<WaterLevelExitPipeController>();
                if (ctrl != null)
                    EditorGUILayout.LabelField($"Lowers water to y = {ctrl.TargetWaterLevelY:0.##}", EditorStyles.miniLabel);
                else
                    EditorGUILayout.LabelField("Prefab has no WaterLevelExitPipeController", EditorStyles.miniLabel);
            }

            // ── Input tube sub-UI (mirrors the entrance lock tube block) ──
            if (wm.tubePath == null) wm.tubePath = new List<UnityEngine.Vector2Int>();

            EditorGUILayout.BeginHorizontal();
            float prevLW2 = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 72f;
            int newSubs = EditorGUILayout.IntField("Subdivisions", wm.tubeSubdivisions, GUILayout.Width(120));
            EditorGUIUtility.labelWidth = prevLW2;
            if (newSubs != wm.tubeSubdivisions)
            {
                Undo.RecordObject(loadedData, "Set Tube Subdivisions");
                wm.tubeSubdivisions = Mathf.Max(0, newSubs);
                EditorUtility.SetDirty(loadedData);
            }
            GUI.enabled = wm.tubePath.Count >= 2;
            if (GUILayout.Button("+", GUILayout.Width(22)))
            {
                Undo.RecordObject(loadedData, "Subdivide Tube Path");
                var old = wm.tubePath;
                var subdivided = new List<UnityEngine.Vector2Int>();
                for (int si = 0; si < old.Count - 1; si++)
                {
                    subdivided.Add(old[si]);
                    subdivided.Add(new UnityEngine.Vector2Int(
                        Mathf.RoundToInt((old[si].x + old[si + 1].x) * 0.5f),
                        Mathf.RoundToInt((old[si].y + old[si + 1].y) * 0.5f)));
                }
                subdivided.Add(old[old.Count - 1]);
                wm.tubePath = subdivided;
                EditorUtility.SetDirty(loadedData);
            }
            if (GUILayout.Button("Regenerate", GUILayout.Width(78)))
                GeneratePipeTubePath(i);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            // Place / Edit / Clear
            bool isPlacing = _pipeTubePlacingIndex == i;
            bool isEditing = _pipeTubeDrawIndex == i;
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = isPlacing ? new Color(0.4f, 1f, 0.6f) : Color.white;
            if (GUILayout.Button(isPlacing ? "● Placing…" : "Place Input Tube", GUILayout.Width(110)))
            {
                if (isPlacing) _pipeTubePlacingIndex = -1;
                else
                {
                    _pipeTubePlacingIndex = i;
                    _pipeTubeDrawIndex    = -1;
                    _tubePlacingEntranceIndex = -1;
                    _tubeDrawEntranceIndex    = -1;
                    _dragTubeNodeIndex        = -1;
                    _selectedTubeNodeIndex    = -1;
                }
            }
            GUI.backgroundColor = Color.white;
            if (wm.tubePath.Count > 0)
            {
                GUI.backgroundColor = isEditing ? new Color(0.5f, 0.8f, 1f) : Color.white;
                if (GUILayout.Button(isEditing ? "● Editing" : "Edit Nodes", GUILayout.Width(76)))
                {
                    if (isEditing) { _pipeTubeDrawIndex = -1; _dragTubeNodeIndex = -1; _selectedTubeNodeIndex = -1; }
                    else
                    {
                        _pipeTubeDrawIndex    = i;
                        _pipeTubePlacingIndex = -1;
                        _tubePlacingEntranceIndex = -1;
                        _tubeDrawEntranceIndex    = -1;
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                Undo.RecordObject(loadedData, "Clear Tube Path");
                wm.tubePath = new List<UnityEngine.Vector2Int>();
                if (_pipeTubePlacingIndex == i) _pipeTubePlacingIndex = -1;
                if (_pipeTubeDrawIndex == i)    _pipeTubeDrawIndex    = -1;
                _dragTubeNodeIndex     = -1;
                _selectedTubeNodeIndex = -1;
                EditorUtility.SetDirty(loadedData);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.LabelField($"{wm.tubePath.Count} nodes", GUILayout.Width(52));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Edit Water Modifier");
                wm.id             = newID;
                wm.perimeterAngle = newAngle;
                wm.tierSlot       = newTierSlot;
                wm.spawnRadius    = newSpawnRadius;
                wm.prefab         = newPrefab;
                EditorUtility.SetDirty(loadedData);
                Repaint();
            }
        }

        if (toRemove >= 0)
        {
            Undo.RecordObject(loadedData, "Remove Water Modifier");
            loadedData.arenaWaterModifiers.RemoveAt(toRemove);
            if (_pipeTubePlacingIndex == toRemove) _pipeTubePlacingIndex = -1;
            else if (_pipeTubePlacingIndex > toRemove) _pipeTubePlacingIndex--;
            if (_pipeTubeDrawIndex == toRemove) { _pipeTubeDrawIndex = -1; _dragTubeNodeIndex = -1; _selectedTubeNodeIndex = -1; }
            else if (_pipeTubeDrawIndex > toRemove) _pipeTubeDrawIndex--;
            EditorUtility.SetDirty(loadedData);
        }

        if (GUILayout.Button("+ Add Water Modifier"))
        {
            Undo.RecordObject(loadedData, "Add Water Modifier");
            var defPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefab/ModifierPrefabs/WaterLevelModifierExitPipe.prefab");
            loadedData.arenaWaterModifiers.Add(new GridData.ArenaWaterModifier
            {
                id     = $"watermod_{loadedData.arenaWaterModifiers.Count}",
                prefab = defPrefab
            });
            EditorUtility.SetDirty(loadedData);
        }
    }

    string[] BuildTierLabels()
    {
        int count = loadedData?.tiers?.Count ?? 0;
        var labels = new string[count + 1];
        labels[0] = "Base";
        for (int i = 0; i < count; i++)
            labels[i + 1] = $"T{i + 1} {loadedData.tiers[i].name}";
        return labels;
    }

    void DrawRightPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        // ── Tools bar — runs along the top of the centre draw window ──
        DrawToolButtons();

        // ── Scale controls for a selected prefab — appears directly under the tools ──
        DrawSelectedPrefabScaleSection();
        DrawSelectedPlacementRotationSection();
        DrawSelectedPlacementSpikeSection();
        DrawSelectedSpikeSection();

        // ── Grid display controls ──
        // All display sliders now live in the dockable Grid Designer Settings window — dock it beside
        // the designer and tune while watching the grid update live.
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("⚙ Settings",
                                            "Open the Grid Designer Settings window (display, drawing " +
                                            "and appearance settings)."),
                             GUILayout.Width(110)))
            GridDesignerSettingsWindow.ShowFor(this);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        DrawGrid();
        EditorGUILayout.EndVertical();
        DrawRightPanelResizeHandle();
        DrawDebugConsole();
    }

    void DrawPrefabLibrarySection()
    {
        showPrefabLibrary = EditorGUILayout.Foldout(showPrefabLibrary, "Prefab Library", true, EditorStyles.foldoutHeader);
        if (!showPrefabLibrary) return;

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Toggle(_prefabLibTab == PrefabLibraryTab.MazePieces, "Mazepieces", EditorStyles.miniButtonLeft))
            _prefabLibTab = PrefabLibraryTab.MazePieces;
        if (GUILayout.Toggle(_prefabLibTab == PrefabLibraryTab.SetPieces,  "Setpieces",  EditorStyles.miniButtonMid))
            _prefabLibTab = PrefabLibraryTab.SetPieces;
        if (GUILayout.Toggle(_prefabLibTab == PrefabLibraryTab.Statues,    "Statues",    EditorStyles.miniButtonMid))
            _prefabLibTab = PrefabLibraryTab.Statues;
        if (GUILayout.Toggle(_prefabLibTab == PrefabLibraryTab.Modifiers,  "Modifiers",  EditorStyles.miniButtonMid))
            _prefabLibTab = PrefabLibraryTab.Modifiers;
        if (GUILayout.Toggle(_prefabLibTab == PrefabLibraryTab.BadGuys,    "BadGuys",    EditorStyles.miniButtonRight))
            _prefabLibTab = PrefabLibraryTab.BadGuys;
        EditorGUILayout.EndHorizontal();

        if (_prefabLibTab == PrefabLibraryTab.MazePieces)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Folder", GUILayout.Width(42));
            string newPath = EditorGUILayout.TextField(prefabFolderPath);
            if (newPath != prefabFolderPath) { prefabFolderPath = newPath; EditorPrefs.SetString("GridDesigner_PrefabFolder", prefabFolderPath); }
            if (GUILayout.Button("…", GUILayout.Width(22)))
            {
                string sel = EditorUtility.OpenFolderPanel("Select MazePieces Folder", prefabFolderPath, "");
                if (!string.IsNullOrEmpty(sel)) { prefabFolderPath = FileUtil.GetProjectRelativePath(sel); EditorPrefs.SetString("GridDesigner_PrefabFolder", prefabFolderPath); ScanPrefabFolder(); }
            }
            if (GUILayout.Button("↺", GUILayout.Width(22))) ScanPrefabFolder();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Icons", GUILayout.Width(42));
            string newIcons = EditorGUILayout.TextField(iconsFolderPath);
            if (newIcons != iconsFolderPath) { iconsFolderPath = newIcons; EditorPrefs.SetString("GridDesigner_IconsFolder", iconsFolderPath); }
            if (GUILayout.Button("…", GUILayout.Width(22)))
            {
                string sel = EditorUtility.OpenFolderPanel("Select Icons Folder", iconsFolderPath, "");
                if (!string.IsNullOrEmpty(sel)) { iconsFolderPath = FileUtil.GetProjectRelativePath(sel); EditorPrefs.SetString("GridDesigner_IconsFolder", iconsFolderPath); ScanIcons(); }
            }
            EditorGUILayout.EndHorizontal();

            if (scannedPrefabs.Count == 0)
            {
                EditorGUILayout.LabelField("  (no prefabs found)", EditorStyles.miniLabel);
            }
            else
            {
                const int IconSize = 32;
                prefabScrollPos = EditorGUILayout.BeginScrollView(prefabScrollPos, GUILayout.Height(160));
                for (int i = 0; i < scannedPrefabs.Count; i++)
                {
                    bool isSelected = drawDirectPrefab && _prefabLibTab == PrefabLibraryTab.MazePieces && selectedPrefabIndex == i;
                    GUI.backgroundColor = isSelected ? Color.yellow : Color.white;
                    prefabIcons.TryGetValue(scannedPrefabs[i].name, out Texture2D icon);
                    var content = icon != null
                        ? new GUIContent(" " + scannedPrefabs[i].name, icon)
                        : new GUIContent(scannedPrefabs[i].name);
                    float btnHeight = icon != null ? IconSize + 4 : EditorGUIUtility.singleLineHeight + 2;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(content, GUILayout.Height(btnHeight)))
                    {
                        selectedPrefabIndex   = i;
                        selectedSetPieceIndex = -1;
                        selectedStatueIndex   = -1;
                        selectedModifierIndex = -1;
                        selectedBadGuyIndex   = -1;
                        _activePlacementPrefab = scannedPrefabs[i];
                        _activePlacementIsWorldSpaceProp = false;
                        drawDirectPrefab = true;
                        activeSlot = -1;
                        _drawSplineWall = false;
                        _drawCubeBuilding = false; _drawSpike = false;
                        drawCircle = drawOrb = drawSoul = drawSoulArea = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = false;
                        drawSelect = false;
                        ClearSelectState();
                        LogSelection(_currentSelection);
                    }
                    GUI.backgroundColor = Color.white;
                    if (GUILayout.Button("⊙", GUILayout.Width(22), GUILayout.Height(btnHeight)))
                        EditorGUIUtility.PingObject(scannedPrefabs[i]);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
        }
        else if (_prefabLibTab == PrefabLibraryTab.SetPieces)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Folder", GUILayout.Width(42));
            EditorGUILayout.LabelField(SetPiecesFolder, EditorStyles.miniLabel);
            if (GUILayout.Button("↺", GUILayout.Width(22))) ScanSetPiecesLib();
            EditorGUILayout.EndHorizontal();

            if (scannedSetPiecesLib.Count == 0)
            {
                EditorGUILayout.LabelField("  (no prefabs found)", EditorStyles.miniLabel);
            }
            else
            {
                setpieceScrollPos = EditorGUILayout.BeginScrollView(setpieceScrollPos, GUILayout.Height(160));
                for (int i = 0; i < scannedSetPiecesLib.Count; i++)
                {
                    bool isSelected = drawDirectPrefab && _prefabLibTab == PrefabLibraryTab.SetPieces && selectedSetPieceIndex == i;
                    GUI.backgroundColor = isSelected ? Color.yellow : Color.white;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(scannedSetPiecesLib[i].name))
                    {
                        selectedSetPieceIndex  = i;
                        selectedPrefabIndex    = -1;
                        selectedStatueIndex    = -1;
                        selectedModifierIndex  = -1;
                        selectedBadGuyIndex    = -1;
                        _activePlacementPrefab = scannedSetPiecesLib[i];
                        _activePlacementIsWorldSpaceProp = false;
                        drawDirectPrefab = true;
                        activeSlot = -1;
                        _drawSplineWall = false;
                        _drawCubeBuilding = false; _drawSpike = false;
                        drawCircle = drawOrb = drawSoul = drawSoulArea = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = false;
                        drawSelect = false;
                        ClearSelectState();
                        LogSelection(_currentSelection);
                    }
                    GUI.backgroundColor = Color.white;
                    if (GUILayout.Button("⊙", GUILayout.Width(22)))
                        EditorGUIUtility.PingObject(scannedSetPiecesLib[i]);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
        }
        else if (_prefabLibTab == PrefabLibraryTab.Statues)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Folder", GUILayout.Width(42));
            EditorGUILayout.LabelField(StatuesPrefabsFolder, EditorStyles.miniLabel);
            if (GUILayout.Button("↺", GUILayout.Width(22))) ScanStatuesLib();
            EditorGUILayout.EndHorizontal();

            if (scannedStatuesLib.Count == 0)
            {
                EditorGUILayout.LabelField("  (no prefabs found)", EditorStyles.miniLabel);
            }
            else
            {
                statueScrollPos = EditorGUILayout.BeginScrollView(statueScrollPos, GUILayout.Height(160));
                for (int i = 0; i < scannedStatuesLib.Count; i++)
                {
                    bool isSelected = drawDirectPrefab && _prefabLibTab == PrefabLibraryTab.Statues && selectedStatueIndex == i;
                    GUI.backgroundColor = isSelected ? Color.yellow : Color.white;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(scannedStatuesLib[i].name))
                    {
                        selectedStatueIndex    = i;
                        selectedPrefabIndex    = -1;
                        selectedSetPieceIndex  = -1;
                        selectedModifierIndex  = -1;
                        selectedBadGuyIndex    = -1;
                        _activePlacementPrefab = scannedStatuesLib[i];
                        _activePlacementIsWorldSpaceProp = true;
                        drawDirectPrefab = true;
                        activeSlot = -1;
                        _drawSplineWall = false;
                        _drawCubeBuilding = false; _drawSpike = false;
                        drawCircle = drawOrb = drawSoul = drawSoulArea = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = false;
                        drawSelect = false;
                        ClearSelectState();
                        LogSelection(_currentSelection);
                    }
                    GUI.backgroundColor = Color.white;
                    if (GUILayout.Button("⊙", GUILayout.Width(22)))
                        EditorGUIUtility.PingObject(scannedStatuesLib[i]);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
        }
        else if (_prefabLibTab == PrefabLibraryTab.Modifiers)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Folder", GUILayout.Width(42));
            EditorGUILayout.LabelField(ModifiersPrefabsFolder, EditorStyles.miniLabel);
            if (GUILayout.Button("↺", GUILayout.Width(22))) ScanModifiersLib();
            EditorGUILayout.EndHorizontal();

            if (scannedModifiersLib.Count == 0)
            {
                EditorGUILayout.LabelField("  (no prefabs found)", EditorStyles.miniLabel);
            }
            else
            {
                modifierScrollPos = EditorGUILayout.BeginScrollView(modifierScrollPos, GUILayout.Height(160));
                for (int i = 0; i < scannedModifiersLib.Count; i++)
                {
                    bool isSelected = drawDirectPrefab && _prefabLibTab == PrefabLibraryTab.Modifiers && selectedModifierIndex == i;
                    GUI.backgroundColor = isSelected ? Color.yellow : Color.white;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(scannedModifiersLib[i].name))
                    {
                        selectedModifierIndex  = i;
                        selectedPrefabIndex    = -1;
                        selectedSetPieceIndex  = -1;
                        selectedStatueIndex    = -1;
                        selectedBadGuyIndex    = -1;
                        _activePlacementPrefab = scannedModifiersLib[i];
                        _activePlacementIsWorldSpaceProp = false;
                        drawDirectPrefab = true;
                        activeSlot = -1;
                        _drawSplineWall = false;
                        _drawCubeBuilding = false; _drawSpike = false;
                        drawCircle = drawOrb = drawSoul = drawSoulArea = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = false;
                        drawSelect = false;
                        ClearSelectState();
                        LogSelection(_currentSelection);
                    }
                    GUI.backgroundColor = Color.white;
                    if (GUILayout.Button("⊙", GUILayout.Width(22)))
                        EditorGUIUtility.PingObject(scannedModifiersLib[i]);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
        }
        else // BadGuys tab
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Folder", GUILayout.Width(42));
            EditorGUILayout.LabelField(BadGuysPrefabsFolder, EditorStyles.miniLabel);
            if (GUILayout.Button("↺", GUILayout.Width(22))) ScanBadGuysLib();
            EditorGUILayout.EndHorizontal();

            if (scannedBadGuysLib.Count == 0)
            {
                EditorGUILayout.LabelField("  (no prefabs found)", EditorStyles.miniLabel);
            }
            else
            {
                badGuyScrollPos = EditorGUILayout.BeginScrollView(badGuyScrollPos, GUILayout.Height(160));
                for (int i = 0; i < scannedBadGuysLib.Count; i++)
                {
                    bool isSelected = drawDirectPrefab && _prefabLibTab == PrefabLibraryTab.BadGuys && selectedBadGuyIndex == i;
                    GUI.backgroundColor = isSelected ? Color.yellow : Color.white;
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(scannedBadGuysLib[i].name))
                    {
                        selectedBadGuyIndex    = i;
                        selectedPrefabIndex    = -1;
                        selectedSetPieceIndex  = -1;
                        selectedStatueIndex    = -1;
                        selectedModifierIndex  = -1;
                        _activePlacementPrefab = scannedBadGuysLib[i];
                        _activePlacementIsWorldSpaceProp = false;
                        drawDirectPrefab = true;
                        activeSlot = -1;
                        _drawSplineWall = false;
                        _drawCubeBuilding = false; _drawSpike = false;
                        drawCircle = drawOrb = drawSoul = drawSoulArea = drawWhirlpool = drawWaterLevelModifier = drawWaveModifier = false;
                        drawSelect = false;
                        ClearSelectState();
                        LogSelection(_currentSelection);
                    }
                    GUI.backgroundColor = Color.white;
                    if (GUILayout.Button("⊙", GUILayout.Width(22)))
                        EditorGUIUtility.PingObject(scannedBadGuysLib[i]);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
        }

        if (drawDirectPrefab && _activePlacementPrefab != null)
            EditorGUILayout.HelpBox($"Placing: {_activePlacementPrefab.name}", MessageType.None);
    }

    // Only appears once a creeper is actually placed — with no creeper there are no routes to show.
    void DrawEnemiesSection()
    {
        CollectAllPlacements(_allPlacements);
        float hopDistance = PlacedCreeperHopDistance(_allPlacements);
        if (hopDistance <= 0f) return;

        showEnemies = EditorGUILayout.Foldout(showEnemies, "Enemies", true, EditorStyles.foldoutHeader);
        if (!showEnemies) return;

        showCreeperRoutes = EditorGUILayout.ToggleLeft(
            new GUIContent("Creeper hop routes",
                           "Green lines between the rocks he can reach, spreading out from where he is placed. " +
                           "Amber dots mark rocks inside a street light's radius — the ones he is driven off."),
            showCreeperRoutes);

        int creepers = CountPlacedCreepers(_allPlacements);
        EditorGUILayout.LabelField(
            creepers > 1
                ? $"  {creepers} creepers placed · hop distance {hopDistance:0.##} (widest of them)"
                : $"  hop distance {hopDistance:0.##} (from the placed creeper)",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(4);
    }

    // A flat list of every bad-guy prefab placed on the grid — base layer plus every tier — mirroring
    // the Spikes list. Each row selects that exact placement with the Select tool, or removes it.
    // "Bad guys" are prefabs from the BadGuys library folder (see IsBadGuyPrefab). Collapsed by default.
    void DrawBadGuysSection()
    {
        _showBadGuys = EditorGUILayout.Foldout(_showBadGuys, "Bad Guys", true, EditorStyles.foldoutHeader);
        if (!_showBadGuys) return;

        // Gather placements with their home list (base = tier -1) and index, so a row can select or
        // remove the exact one. Rebuilt each repaint — cheap, and always in step with edits.
        var rows = new List<(int tier, int idx, GridData.PrefabPlacement pp)>();
        if (loadedData.prefabPlacements != null)
            for (int i = 0; i < loadedData.prefabPlacements.Count; i++)
            {
                var pp = loadedData.prefabPlacements[i];
                if (pp?.prefab != null && IsBadGuyPrefab(pp.prefab)) rows.Add((-1, i, pp));
            }
        if (loadedData.tiers != null)
            for (int t = 0; t < loadedData.tiers.Count; t++)
            {
                var tl = loadedData.tiers[t].prefabPlacements;
                if (tl == null) continue;
                for (int i = 0; i < tl.Count; i++)
                {
                    var pp = tl[i];
                    if (pp?.prefab != null && IsBadGuyPrefab(pp.prefab)) rows.Add((t, i, pp));
                }
            }

        if (rows.Count == 0)
        {
            EditorGUILayout.LabelField("  None placed. Drop one from the BadGuys library.", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);
            return;
        }

        // Removal is deferred so the list isn't mutated mid-draw.
        int removeTier = -2, removeIdx = -1;

        int shown = 0;
        foreach (var row in rows)
        {
            var  pp    = row.pp;
            bool isSel = _currentSelection.type == SelectionType.PrefabPlacement
                         && _currentSelection.tierIndex == row.tier
                         && _currentSelection.index == row.idx;

            EditorGUILayout.BeginHorizontal();

            string where = row.tier == -1 ? "" : $"  (tier {row.tier + 1})";
            GUI.backgroundColor = isSel ? new Color(1f, 0.7f, 0.4f) : Color.white;
            if (GUILayout.Button(new GUIContent($"{++shown}. {pp.prefab.name}{where}",
                                                "Select this bad guy on the grid (switches to the Select tool)."),
                                 EditorStyles.miniButton))
            {
                activeSlot = -1;
                _drawSplineWall = _drawCubeBuilding = _drawSpike = false;
                drawSelect = true;
                drawSoulArea = drawSoul = drawCircle = drawOrb = drawWhirlpool
                             = drawWaterLevelModifier = drawWaveModifier = drawDirectPrefab = false;
                _currentSelection = new SelectionInfo
                {
                    type      = SelectionType.PrefabPlacement,
                    tierIndex = row.tier,
                    index     = row.idx,
                    cellIndex = pp.cellIndex,
                };
                LogSelection(_currentSelection);
                Repaint();
            }
            GUI.backgroundColor = Color.white;

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button(new GUIContent("✕", "Remove this bad guy from the grid."), GUILayout.Width(24)))
            {
                removeTier = row.tier; removeIdx = row.idx;
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        if (removeIdx >= 0)
        {
            var list = removeTier == -1 ? loadedData.prefabPlacements
                     : (loadedData.tiers != null && removeTier >= 0 && removeTier < loadedData.tiers.Count
                        ? loadedData.tiers[removeTier].prefabPlacements : null);
            if (list != null && removeIdx < list.Count)
            {
                Undo.RecordObject(loadedData, "Delete Bad Guy");
                var removed = list[removeIdx];
                list.RemoveAt(removeIdx);
                // Drop any statue/tower-guarded zone linked to this placement (mirrors the delete-key path).
                if (removed != null && removed.statueId != 0 && loadedData.soulZones != null)
                    loadedData.soulZones.RemoveAll(z =>
                        (z.statueGuarded || z.towerGuarded) && z.linkedStatueId == removed.statueId);
                if (_currentSelection.type == SelectionType.PrefabPlacement
                    && _currentSelection.tierIndex == removeTier && _currentSelection.index == removeIdx)
                    ClearSelectState();
                EditorUtility.SetDirty(loadedData);
                Repaint();
            }
        }

        EditorGUILayout.Space(4);
    }

    void DrawDebugConsole()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(_rightPanelWidth), GUILayout.ExpandHeight(true));
        _rightPanelScroll = EditorGUILayout.BeginScrollView(_rightPanelScroll, GUILayout.ExpandHeight(true));

        // Constrain the content to the panel width minus the vertical scrollbar, so rows (and their
        // rightmost controls) always fit inside the visible panel instead of being clipped by the edge.
        EditorGUILayout.BeginVertical(GUILayout.Width(Mathf.Max(60f, _rightPanelWidth - 18f)));

        DrawPrefabLibrarySection();
        if (loadedData != null) DrawAngelSection();
        if (loadedData != null) DrawEnemiesSection();
        if (loadedData != null) DrawBadGuysSection();
        if (loadedData != null) DrawSplineWallsSection();
        if (loadedData != null) DrawCubeBuildingsSection();
        if (loadedData != null) DrawSpikesSection();
        if (loadedData != null) DrawSoulZonesSection();

        if (loadedData != null && loadedData.linkedPairs != null && loadedData.linkedPairs.Count > 0)
        {
            bool hasBroken = false;
            foreach (var pair in loadedData.linkedPairs)
            {
                if (!CellContainsPrefab(pair.modifierTierIndex, pair.modifierCellIndex, "TypeBWaveModifier") ||
                    !CellContainsPrefab(pair.inputTubeTierIndex, pair.inputTubeCellIndex, "SoulFishInputTube"))
                {
                    hasBroken = true;
                    break;
                }
            }

            if (hasBroken)
            {
                EditorGUILayout.HelpBox("Broken modifier links detected!", MessageType.Warning);
                if (GUILayout.Button("Clean Broken Links", EditorStyles.miniButton))
                {
                    PushUndoSnapshot();
                    loadedData.linkedPairs.RemoveAll(p =>
                        !CellContainsPrefab(p.modifierTierIndex, p.modifierCellIndex, "TypeBWaveModifier") ||
                        !CellContainsPrefab(p.inputTubeTierIndex, p.inputTubeCellIndex, "SoulFishInputTube")
                    );
                    EditorUtility.SetDirty(loadedData);
                    Repaint();
                }
            }
            else
            {
                EditorGUILayout.LabelField($"Active Modifier Links: {loadedData.linkedPairs.Count}", EditorStyles.miniLabel);
            }
        }

        DrawTypeBModifierSettingsSection();
        DrawSelectedTowerZoneInfo();

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUILayout.LabelField("Grid Debug", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
        if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(42)))
            _debugLog.Clear();
        EditorGUILayout.EndHorizontal();

        _debugLogScroll = EditorGUILayout.BeginScrollView(_debugLogScroll, GUILayout.Height(140));

        GUIStyle warnStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        warnStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
        GUIStyle errStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        errStyle.normal.textColor = new Color(1f, 0.4f, 0.4f);
        GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        infoStyle.normal.textColor = new Color(0.72f, 0.72f, 0.72f);

        foreach (string line in _debugLog)
        {
            GUIStyle s = line.StartsWith("[WARN]") ? warnStyle
                       : line.StartsWith("[ERR]")  ? errStyle
                       : infoStyle;
            GUILayout.Label(line, s);
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();   // width-constrained content
        EditorGUILayout.EndScrollView(); // right panel scroll
        EditorGUILayout.EndVertical();
    }

    // Per-modifier controls for TypeB wave modifiers placed in the grid. Only
    // shown when at least one TypeB modifier is present. Values entered here
    // override the prefab's default speed / frequency / ripple-depth boosts.
    void DrawTypeBModifierSettingsSection()
    {
        if (loadedData == null) return;

        var typeBPlacements = new List<(int tierIndex, GridData.PrefabPlacement placement)>();
        CollectTypeBPlacements(-1, loadedData.prefabPlacements, typeBPlacements);
        if (loadedData.tiers != null)
        {
            for (int ti = 0; ti < loadedData.tiers.Count; ti++)
                CollectTypeBPlacements(ti, loadedData.tiers[ti].prefabPlacements, typeBPlacements);
        }

        if (typeBPlacements.Count == 0) return;

        EditorGUILayout.Space(4);
        _showTypeBSettings = EditorGUILayout.Foldout(_showTypeBSettings,
            $"Wave Modifier Settings ({typeBPlacements.Count})", true, EditorStyles.foldoutHeader);
        if (!_showTypeBSettings) return;

        float prevLW = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 110f;

        foreach (var (tierIndex, pp) in typeBPlacements)
        {
            var defaults = pp.prefab.GetComponent<LevelWaveModifierControllerTypeB>();
            string tierLabel = tierIndex == -1 ? "Base" : $"Tier {tierIndex}";

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"TypeB · {tierLabel} · Cell {pp.cellIndex}", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            bool ov = EditorGUILayout.ToggleLeft("Override defaults", pp.overrideModifierSettings);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Toggle Wave Modifier Override");
                pp.overrideModifierSettings = ov;
                // Seed the custom values from the prefab defaults the moment
                // override is switched on, so editing starts from a sensible base.
                if (ov && defaults != null)
                {
                    pp.speedBoost       = defaults.speedBoost;
                    pp.frequencyBoost   = defaults.frequencyBoost;
                    pp.rippleDepthBoost = defaults.rippleDepthBoost;
                }
                EditorUtility.SetDirty(loadedData);
            }

            using (new EditorGUI.DisabledScope(!pp.overrideModifierSettings))
            {
                float dispSpeed  = pp.overrideModifierSettings ? pp.speedBoost       : (defaults != null ? defaults.speedBoost       : 0f);
                float dispFreq   = pp.overrideModifierSettings ? pp.frequencyBoost   : (defaults != null ? defaults.frequencyBoost   : 0f);
                float dispRipple = pp.overrideModifierSettings ? pp.rippleDepthBoost : (defaults != null ? defaults.rippleDepthBoost : 0f);

                EditorGUI.BeginChangeCheck();
                float newSpeed  = EditorGUILayout.FloatField("Speed Boost",        dispSpeed);
                float newFreq   = EditorGUILayout.FloatField("Frequency Boost",    dispFreq);
                float newRipple = EditorGUILayout.FloatField("Ripple Depth Boost", dispRipple);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(loadedData, "Edit Wave Modifier Settings");
                    pp.speedBoost       = newSpeed;
                    pp.frequencyBoost   = newFreq;
                    pp.rippleDepthBoost = newRipple;
                    EditorUtility.SetDirty(loadedData);
                }
            }

            if (!pp.overrideModifierSettings)
                EditorGUILayout.LabelField("Using prefab defaults", EditorStyles.miniLabel);

            // ── Input tube node path (mirrors the lock connection) ──────────────
            int pairIdx = FindModifierPairIndex(tierIndex, pp.cellIndex);
            if (pairIdx >= 0)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Input Tube Path", EditorStyles.miniBoldLabel);

                var pair      = loadedData.linkedPairs[pairIdx];
                int nodeCount = pair.tubePath?.Count ?? 0;

                EditorGUILayout.BeginHorizontal();
                float prevLW2 = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 76f;
                EditorGUI.BeginChangeCheck();
                int dispSubs = pair.tubeSubdivisions > 0 ? pair.tubeSubdivisions : 3;
                int newSubs  = EditorGUILayout.IntField("Subdivisions", dispSubs, GUILayout.Width(122));
                EditorGUIUtility.labelWidth = prevLW2;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(loadedData, "Set Modifier Tube Subdivisions");
                    pair.tubeSubdivisions = Mathf.Max(0, newSubs);
                    loadedData.linkedPairs[pairIdx] = pair;
                    EditorUtility.SetDirty(loadedData);
                }
                using (new EditorGUI.DisabledScope(nodeCount < 2))
                    if (GUILayout.Button("+", GUILayout.Width(22)))
                        WMSubdivideTubePath(pairIdx);
                if (GUILayout.Button(nodeCount >= 2 ? "Regenerate" : "Generate", GUILayout.Width(84)))
                    WMGenerateTubePath(pairIdx);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                bool isEditingThis = _wmTubeDrawPairIdx == pairIdx;
                using (new EditorGUI.DisabledScope(nodeCount < 2))
                {
                    GUI.backgroundColor = isEditingThis ? new Color(0.5f, 0.8f, 1f) : Color.white;
                    if (GUILayout.Button(isEditingThis ? "● Editing" : "Edit Nodes", GUILayout.Width(90)))
                    {
                        if (isEditingThis)
                        {
                            _wmTubeDrawPairIdx = -1; _wmDragTubeNodeIdx = -1; _wmSelTubeNodeIdx = -1;
                        }
                        else
                        {
                            _wmTubeDrawPairIdx = pairIdx; _wmDragTubeNodeIdx = -1; _wmSelTubeNodeIdx = -1;
                            _tubeDrawEntranceIndex = -1; _tubePlacingEntranceIndex = -1;
                        }
                    }
                    GUI.backgroundColor = Color.white;
                }
                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                using (new EditorGUI.DisabledScope(nodeCount == 0))
                {
                    if (GUILayout.Button("✕", GUILayout.Width(24)))
                    {
                        Undo.RecordObject(loadedData, "Clear Modifier Tube Path");
                        pair.tubePath?.Clear();
                        loadedData.linkedPairs[pairIdx] = pair;
                        if (_wmTubeDrawPairIdx == pairIdx)
                        { _wmTubeDrawPairIdx = -1; _wmDragTubeNodeIdx = -1; _wmSelTubeNodeIdx = -1; }
                        EditorUtility.SetDirty(loadedData);
                    }
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.LabelField($"{nodeCount} node(s)", GUILayout.Width(64));
                EditorGUILayout.EndHorizontal();

                if (isEditingThis)
                    EditorGUILayout.HelpBox(
                        "Drag the middle nodes on the grid. The end nodes stay locked to the tube and modifier.",
                        MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("No linked input tube for this modifier.", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        EditorGUIUtility.labelWidth = prevLW;
    }

    // Scale slider for the currently-selected prefab placement. Only shown when the
    // selected prefab has a PrefabBaselineAlignment scale radius enabled.
    /// <summary>
    /// Shape picker for a spike rock the selected prefab carries with it — the creepy guy brings
    /// his own starting rock, so he arrives standing on something rather than needing a separate
    /// spike placed under him. Only appears when the prefab actually has a ProceduralSpike on it,
    /// so it stays out of the way for every other placement.
    /// </summary>
    void DrawSelectedPlacementSpikeSection()
    {
        if (loadedData == null || _currentSelection.type != SelectionType.PrefabPlacement) return;

        List<GridData.PrefabPlacement> placements =
            _currentSelection.tierIndex == -1
                ? loadedData.prefabPlacements
                : (loadedData.tiers != null && _currentSelection.tierIndex < loadedData.tiers.Count
                    ? loadedData.tiers[_currentSelection.tierIndex].prefabPlacements
                    : null);

        if (placements == null || _currentSelection.index < 0 || _currentSelection.index >= placements.Count) return;

        var pp = placements[_currentSelection.index];
        if (pp?.prefab == null) return;
        if (pp.prefab.GetComponentInChildren<ProceduralSpike>(true) == null) return;

        RefreshSpikePresets();

        float prevLW = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 160f;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"Rock carried by {pp.prefab.name}", EditorStyles.miniBoldLabel);

        int cur  = System.Array.IndexOf(_spikePresets, pp.spikePreset);
        int pick = EditorGUILayout.Popup(
            new GUIContent("Shape preset", "Authored in the Spike Studio. This rock is always built " +
                                           "climbable, since it's the one he stands on."),
            cur + 1, _spikePresetNames);

        SpikeShapePreset chosen = pick <= 0 ? null : _spikePresets[pick - 1];
        if (chosen != pp.spikePreset)
        {
            Undo.RecordObject(loadedData, "Set Carried Spike Preset");
            pp.spikePreset = chosen;
            EditorUtility.SetDirty(loadedData);
            Repaint();
        }

        EditorGUILayout.LabelField(
            "Built at the preset's own size — the placement's Scale already sizes the whole prefab.",
            EditorStyles.miniLabel);

        if (_spikePresets.Length == 0)
            EditorGUILayout.HelpBox($"No presets in {SpikeShapePreset.AssetFolder} yet — the rock will " +
                                    "use the default shape. Save one from the Spike Studio.", MessageType.Info);

        EditorGUILayout.EndVertical();
        EditorGUIUtility.labelWidth = prevLW;
    }

    void DrawSelectedPrefabScaleSection()
    {
        if (loadedData == null || _currentSelection.type != SelectionType.PrefabPlacement) return;

        List<GridData.PrefabPlacement> placements =
            _currentSelection.tierIndex == -1
                ? loadedData.prefabPlacements
                : (loadedData.tiers != null && _currentSelection.tierIndex < loadedData.tiers.Count
                    ? loadedData.tiers[_currentSelection.tierIndex].prefabPlacements
                    : null);

        if (placements == null || _currentSelection.index < 0 || _currentSelection.index >= placements.Count) return;

        var pp = placements[_currentSelection.index];
        if (pp?.prefab == null) return;

        var align = GetBaselineAlign(pp.prefab);
        if (align == null || !align.UseScaleRadius) return;

        float prevLW = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 160f;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        float cur = pp.scale > 0f ? pp.scale : 1f;
        EditorGUI.BeginChangeCheck();
        float ns = LowEndSlider(
            new GUIContent($"Scale ({pp.prefab.name})",
                           "Weighted hard toward the low end for fine-tuning small props; type an exact value in the box."),
            cur, 0.05f, 5f, power: 5f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Scale Prefab Placement");
            pp.scale = ns;
            EditorUtility.SetDirty(loadedData);
            Repaint();
        }

        EditorGUILayout.LabelField($"Footprint ≈ {align.ScaleRadius * cur:0.##} world units", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        EditorGUIUtility.labelWidth = prevLW;
    }

    // Quick Size + shape-preset controls for a selected procedural spike, shown under the tools like
    // the prefab scale section — so a selected spike can be resized and restyled without opening the
    // Spikes panel.
    void DrawSelectedSpikeSection()
    {
        if (loadedData?.proceduralSpikes == null
            || _activeSpikeIndex < 0 || _activeSpikeIndex >= loadedData.proceduralSpikes.Count) return;
        var s = loadedData.proceduralSpikes[_activeSpikeIndex];
        if (s == null) return;

        RefreshSpikePresets();

        float prevLW = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 160f;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Shape preset for the selected rock (authored in the Spike Studio).
        EditorGUI.BeginChangeCheck();
        int cur  = System.Array.IndexOf(_spikePresets, s.preset);
        int pick = EditorGUILayout.Popup(
            new GUIContent($"Spike {_activeSpikeIndex + 1} shape",
                           "Which authored shape this rock wears. Editing the preset restyles every rock using it."),
            cur + 1, _spikePresetNames);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Set Spike Preset");
            s.preset = pick <= 0 ? null : _spikePresets[pick - 1];
            EditorUtility.SetDirty(loadedData);
            Repaint();
        }

        EditorGUI.BeginChangeCheck();
        float ns = LowEndSlider(
            new GUIContent("Size",
                           "Weighted hard toward the low end for fine-tuning small rocks; type an exact value in the box."),
            s.EffectiveScale, 0.05f, 6f, power: 5f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Scale Spike");
            s.scale = Mathf.Max(0.01f, ns);
            EditorUtility.SetDirty(loadedData);
            Repaint();
        }

        EditorGUILayout.EndVertical();
        EditorGUIUtility.labelWidth = prevLW;
    }

    // Facing (yaw offset) for a selected placement whose prefab overrides forward. Drives both the
    // forward arrow on the icon and the spawned instance's rotation.
    void DrawSelectedPlacementRotationSection()
    {
        var pp = GetSelectedPlacement();
        if (pp?.prefab == null) return;
        var align = GetBaselineAlign(pp.prefab);
        if (align == null || !align.UseForwardOverride) return;

        float prevLW = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 160f;

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUI.BeginChangeCheck();
        float nr = EditorGUILayout.Slider(
            new GUIContent($"Facing ({pp.prefab.name})",
                           "Yaw offset (degrees) applied at spawn and shown by the forward arrow on the icon."),
            pp.rotationOffset, 0f, 360f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(loadedData, "Rotate Prefab Placement");
            pp.rotationOffset = Mathf.Repeat(nr, 360f);
            EditorUtility.SetDirty(loadedData);
            Repaint();
        }

        EditorGUILayout.EndVertical();
        EditorGUIUtility.labelWidth = prevLW;
    }

    // Returns the currently-selected prefab placement, or null if the selection isn't a placement.
    GridData.PrefabPlacement GetSelectedPlacement()
    {
        if (loadedData == null || _currentSelection.type != SelectionType.PrefabPlacement) return null;
        var placements = _currentSelection.tierIndex == -1
            ? loadedData.prefabPlacements
            : (loadedData.tiers != null && _currentSelection.tierIndex < loadedData.tiers.Count
                ? loadedData.tiers[_currentSelection.tierIndex].prefabPlacements : null);
        if (placements == null || _currentSelection.index < 0 || _currentSelection.index >= placements.Count) return null;
        return placements[_currentSelection.index];
    }

    // Finds the tower-guarded soul zone linked to a placement (by shared guard id), plus its index.
    GridData.SoulZone FindTowerZoneForPlacement(GridData.PrefabPlacement pp, out int zoneIndex)
    {
        zoneIndex = -1;
        if (pp == null || pp.statueId == 0 || loadedData?.soulZones == null) return null;
        for (int zi = 0; zi < loadedData.soulZones.Count; zi++)
        {
            var z = loadedData.soulZones[zi];
            if (z.towerGuarded && z.linkedStatueId == pp.statueId) { zoneIndex = zi; return z; }
        }
        return null;
    }

    // True when the given tower zone belongs to the currently-selected tower placement.
    // Used to highlight the bowl footprint in the grid only while its tower is selected.
    bool IsSelectedTowerZone(GridData.SoulZone zone)
    {
        if (zone == null || !zone.towerGuarded || zone.linkedStatueId == 0) return false;
        var pp = GetSelectedPlacement();
        return pp != null && pp.statueId == zone.linkedStatueId;
    }

    // Info box under the Scale section: when a fish-bowl tower is selected, shows which soul-fish
    // zone it owns and a shortcut to select it in the Soul Zones panel.
    void DrawSelectedTowerZoneInfo()
    {
        var pp = GetSelectedPlacement();
        if (pp?.prefab == null) return;
        var ctrl = pp.prefab.GetComponentInChildren<FishBowlTowerController>(true);
        if (ctrl == null) return;

        var zone = FindTowerZoneForPlacement(pp, out int zi);

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"Fish Bowl · {pp.prefab.name}", EditorStyles.boldLabel);

        // Bowl comes from the prefab controller: fish spawn at the bowlCenter transform, contained
        // within bowlRadius. No height stored on the zone.
        string bowlPos = ctrl.bowlCenter != null ? "bowlCenter transform" : "tower root (no bowlCenter assigned)";
        EditorGUILayout.LabelField($"Bowl Radius: {ctrl.bowlRadius:0.##}   ·   Spawns at: {bowlPos}", EditorStyles.miniLabel);

        if (zone == null)
        {
            EditorGUILayout.HelpBox("No linked soul-fish zone found for this tower.", MessageType.Warning);
            EditorGUILayout.EndVertical();
            return;
        }

        int soulCount = zone.souls?.Count ?? 0;
        EditorGUILayout.LabelField($"Soul Fish Zone: #{zi}  (id {zone.linkedStatueId})   ·   Souls: {soulCount}", EditorStyles.miniLabel);

        if (GUILayout.Button("Select Zone in Soul Zones panel", EditorStyles.miniButton))
        {
            _activeSoulZoneIndex = zi;
            Repaint();
        }

        EditorGUILayout.EndVertical();
    }

    void CollectTypeBPlacements(int tierIndex, List<GridData.PrefabPlacement> placements,
        List<(int, GridData.PrefabPlacement)> results)
    {
        if (placements == null) return;
        foreach (var pp in placements)
        {
            if (pp?.prefab == null) continue;
            if (pp.prefab.GetComponent<LevelWaveModifierControllerTypeB>() != null)
                results.Add((tierIndex, pp));
        }
    }

    void DrawGrid()
    {
        // Viewport fills the central column between the side panels; GridPixelSize is
        // only the minimum size. The grid content is panned/zoomed inside it.
        Rect viewRect = GUILayoutUtility.GetRect(GridPixelSize, GridPixelSize,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        Event e = Event.current;
        bool mouseInView = viewRect.Contains(e.mousePosition);

        // Clicking anywhere in the centre window claims keyboard focus away from any panel
        // field, so draw-mode hotkeys (Enter to finish a node path, etc.) always land in
        // HandleModeHotkeys instead of a stale field. This is the "interacting with the
        // centre window makes it own key commands" guarantee.
        if (e.type == EventType.MouseDown && mouseInView)
        {
            GUIUtility.keyboardControl        = 0;
            EditorGUIUtility.editingTextField = false;
        }

        // While a panel text/number field is being edited, ignore keyboard events in the
        // centre window so Delete/Escape/Backspace/etc. act on the field, not the grid
        // (selection deletes, mode exits, node removal). Drawing only happens on Repaint
        // events, so bailing on key events here costs no visuals.
        if (EditorGUIUtility.editingTextField &&
            (e.type == EventType.KeyDown || e.type == EventType.KeyUp))
            return;

        // D over the draw window duplicates the selected prefab/block; the copy then follows the
        // cursor (carried) until a left-click drops it. Soul zones + walls are not duplicated.
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.D && mouseInView && !_carryDuplicate)
            TryStartDuplicateCarry(e);

        // Fit + centre the grid within the viewport the first time we know its real size.
        if (!_gridViewInit && e.type != EventType.Layout && viewRect.width > 1f && viewRect.height > 1f)
        {
            float fit = Mathf.Min(viewRect.width, viewRect.height) / GridPixelSize * 0.95f;
            _gridZoom = Mathf.Clamp(fit, 0.2f, 16f);
            _gridPanOffset = new Vector2(
                (viewRect.width  - ZoomedGridSize) * 0.5f,
                (viewRect.height - ZoomedGridSize) * 0.5f);
            _gridViewInit = true;
        }

        // ── Navigation (handled in absolute window space, before the clip) ──
        // Space held over the grid arms a pan gesture; left-drag then pans like MMB.
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Space && mouseInView && !_spacePanHeld)
        {
            _spacePanHeld = true;
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.KeyUp && e.keyCode == KeyCode.Space)
        {
            _spacePanHeld = false;
            if (!_isPanningGrid) e.Use();
            Repaint();
        }

        // While armed, show the pan cursor over the whole viewport.
        if (_spacePanHeld || _isPanningGrid)
            EditorGUIUtility.AddCursorRect(viewRect, MouseCursor.Pan);

        if (mouseInView)
        {
            if (e.type == EventType.ScrollWheel)
            {
                float prevZoom = _gridZoom;
                // Proportional (multiplicative) step so zooming stays smooth all the way in, and a
                // much higher cap for fine-tuning map content up close.
                _gridZoom = Mathf.Clamp(_gridZoom * (1f - e.delta.y * 0.05f), 0.2f, 16f);
                // Zoom around mouse position
                Vector2 mouseLocal = e.mousePosition - viewRect.position - _gridPanOffset;
                _gridPanOffset    -= mouseLocal * (_gridZoom / prevZoom - 1f);
                e.Use();
                Repaint();
            }
            // Middle mouse, or Space+left mouse, begins a pan.
            if (e.type == EventType.MouseDown && (e.button == 2 || (e.button == 0 && _spacePanHeld)))
            {
                _isPanningGrid = true;
                e.Use();
            }
        }
        if (e.type == EventType.MouseDrag && _isPanningGrid)
        {
            _gridPanOffset += e.delta;
            e.Use();
            Repaint();
        }
        if (e.type == EventType.MouseUp && _isPanningGrid)
        {
            _isPanningGrid = false;
            e.Use();
        }

        // ── Navigation scrollbars ──
        // Once zoomed in far enough that the grid overflows the viewport, show real scrollbars on
        // the right/bottom edges so you can slide around without the pan gesture. The pan offset is
        // the grid's top-left relative to the viewport, so the scroll value is its negation. The bar
        // only writes back the offset while it's actually being dragged (BeginChangeCheck), so free
        // panning with Space/middle-mouse still keeps its margins — the bar just reflects the offset.
        const float ScrollbarThickness = 14f;
        bool needH = ZoomedGridSize > viewRect.width  + 1f;
        bool needV = ZoomedGridSize > viewRect.height + 1f;
        if (needH || needV)
        {
            // Reserve the perpendicular strip so the two bars don't fight over the corner.
            float barViewW = viewRect.width  - (needV ? ScrollbarThickness : 0f);
            float barViewH = viewRect.height - (needH ? ScrollbarThickness : 0f);

            if (needH)
            {
                var hRect = new Rect(viewRect.x, viewRect.yMax - ScrollbarThickness, barViewW, ScrollbarThickness);
                float max = Mathf.Max(0f, ZoomedGridSize - barViewW);
                float cur = Mathf.Clamp(-_gridPanOffset.x, 0f, max);
                EditorGUI.BeginChangeCheck();
                float val = GUI.HorizontalScrollbar(hRect, cur, barViewW, 0f, ZoomedGridSize);
                if (EditorGUI.EndChangeCheck()) { _gridPanOffset.x = -val; Repaint(); }
            }
            if (needV)
            {
                var vRect = new Rect(viewRect.xMax - ScrollbarThickness, viewRect.y, ScrollbarThickness, barViewH);
                float max = Mathf.Max(0f, ZoomedGridSize - barViewH);
                float cur = Mathf.Clamp(-_gridPanOffset.y, 0f, max);
                EditorGUI.BeginChangeCheck();
                float val = GUI.VerticalScrollbar(vRect, cur, barViewH, 0f, ZoomedGridSize);
                if (EditorGUI.EndChangeCheck()) { _gridPanOffset.y = -val; Repaint(); }
            }
        }

        // Clip everything below to the viewport so the panned/zoomed grid and its
        // overlays never bleed over the side panels. Inside the clip, coordinates
        // are relative to the viewport's top-left, so the draw rect drops viewRect.position.
        // The clip stops short of the scrollbar strips (drawn just above) so the grid never
        // paints over them — the bars stay visible on top. Same top-left, so input coords are
        // unchanged; only the drawn area is trimmed by the bar thickness on the used edges.
        float clipW = viewRect.width  - (needV ? ScrollbarThickness : 0f);
        float clipH = viewRect.height - (needH ? ScrollbarThickness : 0f);
        GUI.BeginClip(new Rect(viewRect.x, viewRect.y, clipW, clipH));
        Rect localView = new Rect(0f, 0f, clipW, clipH);
        try
        {

        // Draw rect — panned and zoomed, may extend beyond viewport (clip-local space)
        Rect rect = new Rect(
            _gridPanOffset.x,
            _gridPanOffset.y,
            ZoomedGridSize, ZoomedGridSize);

        // A carried duplicate follows the cursor and swallows input until it's dropped.
        if (_carryDuplicate) HandleDuplicateCarry(rect, e);

        Handles.BeginGUI();
        Handles.color = new Color(1f, 1f, 1f, _backdropBrightness);
        Handles.DrawSolidDisc(rect.center, Vector3.forward, ZoomedGridSize * 0.5f);
        Handles.EndGUI();

        // Cube-building foundations — drawn first so blocks sit on the bottom layer,
        // beneath cells, prefabs, spline walls and every other overlay. Their selection
        // nodes + numbers are drawn later (on top) so they stay visible and pickable.
        DrawCubeBuildingFoundations(rect);

        // Selected prefab footprint — opaque fill drawn before the cell loop so the
        // prefab icon (and any icons within the footprint) render on top of it.
        DrawSelectedPrefabScaleFill(rect, GetPixelsPerWorldUnit());

        // Migrate legacy cell-index soul zones to free positions once, before any input.
        if (loadedData?.soulZones != null)
            foreach (var z in loadedData.soulZones) z.MigrateNodesIfNeeded();

        // Spline wall input — handled at rect level before cell loop so nodes are free-floating
        if (_drawSplineWall && loadedData != null)
            HandleSplineWallInput(rect, e);

        // Cube building input — click-drag to create, centre-node drag to move (pixel-based, before cell loop)
        if (_drawCubeBuilding && loadedData != null)
            HandleCubeBuildingInput(rect, e);

        // Spike input — drag out from a point to size a new rock, centre drag to move
        if (_drawSpike && loadedData != null)
            HandleSpikeInput(rect, e);

        // Sub-zone junction drawing runs FIRST so its clicks (incl. snapping onto a main-path node)
        // are never intercepted by the Select node-pickers below.
        if (_subZoneDrawIdx >= 0 && loadedData != null)
            HandleSubZoneJunctionInput(rect, e);

        // Select tool — spline wall node picking (pixel-based, before cell loop)
        if (drawSelect && loadedData != null)
            HandleSelectSplineWallInput(rect, e);

        // Select tool — soul zone node picking + free drag (pixel-based, before cell loop)
        if (drawSelect && loadedData != null)
            HandleSelectSoulNodeInput(rect, e);

        // Select tool — cube building node picking + free drag (after node pickers so precise picks win)
        if (drawSelect && loadedData != null)
            HandleSelectCubeBuildingInput(rect, e);

        // Select tool — spike picking + free drag (same ordering rule as the blocks above)
        if (drawSelect && loadedData != null)
            HandleSelectSpikeInput(rect, e);

        // Select tool — free orb picking + drag
        if (drawSelect && loadedData != null)
            HandleSelectOrbInput(rect, e);

        // Select tool — free prefab-placement picking + drag (placements are position-based now)
        if (drawSelect && loadedData != null)
            HandleSelectPrefabInput(rect, e);

        // Tube path placement mode — mouse preview + click to place
        if (_tubePlacingEntranceIndex >= 0 && loadedData != null)
            HandleTubePlacementInput(rect, e);

        // Tube path edit/drag mode
        if (_tubeDrawEntranceIndex >= 0 && loadedData != null)
            HandleTubePathInput(rect, e);

        // Wave-modifier tube path edit/drag mode
        if (_wmTubeDrawPairIdx >= 0 && loadedData != null)
            HandleWMTubePathInput(rect, e);

        // Water-level exit-pipe tube path placement + edit modes
        if (_pipeTubePlacingIndex >= 0 && loadedData != null)
            HandlePipeTubePlacementInput(rect, e);
        if (_pipeTubeDrawIndex >= 0 && loadedData != null)
            HandlePipeTubePathInput(rect, e);

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                int  index = y * GridSize + x;
                Rect cell  = new Rect(rect.x + x * EffCell, rect.y + y * EffCell, EffCell, EffCell);
                if (!localView.Overlaps(cell)) continue; // skip cells outside viewport

                // Base layer alpha: full when active or no tiers exist, faint when a tier is active
                float baseAlpha = (!baseLayerVisible) ? 0f :
                                  (activeTierIndex < 0) ? 1f : 0.12f;

                if (baseAlpha > 0f)
                {
                    int sq = squareGrid[index];
                    if (sq > 0 && sq <= slotColors.Count)
                    {
                        Color c = slotColors[sq - 1]; c.a = baseAlpha;
                        EditorGUI.DrawRect(cell, c);
                    }

                    int circ = circleGrid[index];
                    if (circ > 0 && circ <= slotColors.Count)
                    {
                        Color c = slotColors[circ - 1]; c.a = baseAlpha;
                        Handles.color = c;
                        Handles.DrawSolidDisc(cell.center, Vector3.forward, EffCell * 0.3f);
                    }

                    // Soul-zone node markers are free-positioned now — drawn at rect level
                    // (see the soul-zone drawing block after the cell loop), not per-cell.

                    // Orbs are free-positioned now — drawn at rect level after the cell loop, not per-cell.

                    // Water Level Modifier — the labelled cell disc takes its colour from the setting.
                    if (loadedData?.waterLevelModifierCellIndices != null && loadedData.waterLevelModifierCellIndices.Contains(index))
                    {
                        DrawMarker(cell.center, EffCell * 0.38f, _style.waterModifier, baseAlpha);
                        Handles.color = new Color(1f, 1f, 1f, baseAlpha);
                        Handles.Label(cell.center - new Vector2(4f, 6f), "W");
                    }

                    // Wave Modifier — cell disc colour from the setting.
                    if (loadedData?.waveModifierCellIndices != null && loadedData.waveModifierCellIndices.Contains(index))
                    {
                        DrawMarker(cell.center, EffCell * 0.38f, _style.waveModifier, baseAlpha);
                        Handles.color = new Color(0f, 0f, 0f, baseAlpha);
                        Handles.Label(cell.center - new Vector2(4f, 6f), "~");
                    }

                    // Whirlpool — cell disc colour from the setting (the radius ring uses it too).
                    if (loadedData?.whirlpools != null && loadedData.whirlpools.Exists(w => w.cellIndex == index))
                    {
                        DrawMarker(cell.center, EffCell * 0.38f, _style.whirlpool, baseAlpha);
                        Handles.color = new Color(1f, 1f, 1f, baseAlpha);
                        Handles.Label(cell.center - new Vector2(4f, 6f), "〇");
                    }

                    // Direct prefab placements (base layer) are drawn in a dedicated overlay pass
                    // AFTER the soul zones (see DrawPrefabPlacementIcons), so the icons sit on top.
                }

                // Tiers — inset squares + modifiers, active = full opacity, others = faint if visible
                if (loadedData?.tiers != null)
                {
                    for (int ti = 0; ti < loadedData.tiers.Count; ti++)
                    {
                        bool visible = ti < tierVisible.Count && tierVisible[ti];
                        if (!visible) continue;

                        var  tier     = loadedData.tiers[ti];
                        bool isActive = activeTierIndex == ti;
                        float a       = isActive ? 1f : 0.12f;

                        // Prefab cells
                        if (tier.cells != null && index < tier.cells.Length)
                        {
                            int tc = tier.cells[index];
                            if (tc > 0 && tc <= slotColors.Count)
                            {
                                Color c = slotColors[tc - 1]; c.a = a;
                                float inset = 2f;
                                EditorGUI.DrawRect(new Rect(cell.x + inset, cell.y + inset,
                                    EffCell - inset * 2, EffCell - inset * 2), c);
                                if (isActive)
                                {
                                    Handles.color = Color.white;
                                    Handles.Label(cell.center - new Vector2(4f, 6f), $"{ti + 1}");
                                }
                            }
                        }

                        // Water Level Modifier — colour from the setting (dimmed for inactive tiers).
                        if (tier.waterLevelModifierCellIndices != null && tier.waterLevelModifierCellIndices.Contains(index))
                        {
                            DrawMarker(cell.center, EffCell * 0.38f, _style.waterModifier, a);
                            Handles.color = new Color(1f, 1f, 1f, a);
                            Handles.Label(cell.center - new Vector2(4f, 6f), "W");
                        }

                        // Wave Modifier — colour from the setting (dimmed for inactive tiers).
                        if (tier.waveModifierCellIndices != null && tier.waveModifierCellIndices.Contains(index))
                        {
                            DrawMarker(cell.center, EffCell * 0.38f, _style.waveModifier, a);
                            Handles.color = new Color(0f, 0f, 0f, a);
                            Handles.Label(cell.center - new Vector2(4f, 6f), "~");
                        }

                        // Tier prefab placements are drawn in the overlay pass (on top of zones) too.
                    }
                }

                bool mouseOver = cell.Contains(e.mousePosition);

                // ── Select tool handling ──────────────────────────────────
                if (drawSelect && loadedData != null)
                {
                    if (e.type == EventType.MouseDown && mouseOver)
                    {
                        var clicked = FindAnythingAtCell(index);

                        if (clicked.type != SelectionType.None)
                        {
                            // Shift+click another node in same zone → connect directly
                            if (e.shift && clicked.type == SelectionType.SoulZoneNode &&
                                _currentSelection.type == SelectionType.SoulZoneNode &&
                                clicked.index == _currentSelection.index && clicked.subIndex != _currentSelection.subIndex)
                            {
                                ConnectNodes(clicked.index, _currentSelection.subIndex, clicked.subIndex);
                            }
                            else
                            {
                                // Select and immediately arm a drag-to-move for any
                                // movable item. Move only commits once the pointer
                                // is dragged onto a different cell.
                                _currentSelection    = clicked;
                                _selectedZoneIndex   = (clicked.type == SelectionType.SoulZoneNode) ? clicked.index : -1;
                                _selectedNodeIndex   = (clicked.type == SelectionType.SoulZoneNode) ? clicked.subIndex : -1;
                                _activeSoulZoneIndex = (clicked.type == SelectionType.SoulZoneNode) ? clicked.index : _activeSoulZoneIndex;
                                LogSelection(_currentSelection);

                                _isDraggingNode  = true;
                                _dragCurrentCell = index;
                                _dragUndoPushed  = false;
                            }
                        }
                        else
                        {
                            // Click empty cell — clear selection (no second-click move)
                            ClearSelectState();
                        }
                        e.Use();
                        Repaint();
                    }
                    else if (e.type == EventType.MouseDrag && _isDraggingNode && mouseOver && index != _dragCurrentCell
                             && _currentSelection.type != SelectionType.None)
                    {
                        // Push a single undo snapshot for the whole drag, on first move.
                        if (!_dragUndoPushed) { PushUndoSnapshot(); _dragUndoPushed = true; }
                        MoveSelection(_currentSelection, index, pushUndo: false);
                        _dragCurrentCell = index;
                        e.Use();
                        Repaint();
                    }
                    else if (e.type == EventType.MouseUp && _isDraggingNode)
                    {
                        _isDraggingNode  = false;
                        _dragCurrentCell = -1;
                        _dragUndoPushed  = false;
                        e.Use();
                    }
                }
                // ── Paint tool handling ───────────────────────────────────
                else if (!_drawSplineWall)
                {
                if (e.type == EventType.MouseDown && mouseOver)
                {
                    PushUndoSnapshot();
                    isDragging = true;
                    _drawPointerNorm = PixelToWorldXZ(rect, e.mousePosition); // exact drop point if unclamped
                    ApplyToolToCell(index);
                    lastDraggedCellIndex = index;
                    e.Use();
                    Repaint();
                }
                else if (e.type == EventType.MouseDrag && isDragging && mouseOver)
                {
                    if (index != lastDraggedCellIndex && !drawSoul && !drawSoulArea)
                    {
                        _drawPointerNorm = PixelToWorldXZ(rect, e.mousePosition);
                        ApplyToolToCell(index);
                        lastDraggedCellIndex = index;
                        Repaint();
                    }
                }
                else if (e.type == EventType.MouseUp)
                {
                    isDragging = false; lastDraggedCellIndex = -1;
                }
                }

                if (_gridLineOpacity > 0f)
                {
                    // Grid line colour from the appearance setting, faded by the Grid lines opacity.
                    Color gridLineCol = _style.gridLineColor;
                    gridLineCol.a *= _gridLineOpacity;
                    // Line thickness tracks zoom so the grid reads consistently at any scale.
                    float lw = Mathf.Max(1f, Mathf.Round(_gridZoom));
                    EditorGUI.DrawRect(new Rect(cell.x, cell.y, EffCell, lw), gridLineCol);
                    EditorGUI.DrawRect(new Rect(cell.x, cell.y, lw, EffCell), gridLineCol);
                }
            }
        }

        // Grid Overlays (drawn after all cells so lines sit on top)
        if (loadedData != null)
        {
            Handles.BeginGUI();

            float pxPerUnit = GetPixelsPerWorldUnit();

            // Orbs — free-positioned. Size from the Orb size setting; colour/fill from the Orb appearance.
            // The selected orb wears the shared white selection circle (when no other selection owns it).
            float orbAlpha = (!baseLayerVisible) ? 0f : (activeTierIndex < 0) ? 1f : 0.35f;
            if (orbAlpha > 0f && loadedData.orbPositions != null)
                for (int oi = 0; oi < loadedData.orbPositions.Count; oi++)
                {
                    Vector2 opx = WorldXZToPixel(rect, loadedData.orbPositions[oi]);
                    if (oi == _activeOrbIndex && drawSelect && _currentSelection.type == SelectionType.None)
                        DrawMarker(opx, Mathf.Max(EffCell * _selectionCircleFactor, 3f), _style.selection);
                    DrawMarker(opx, EffCell * _orbCircleFactor, _style.orb, orbAlpha);
                }

            // Whirlpool radii
            if (pxPerUnit > 0f && loadedData.whirlpools != null)
            {
                foreach (var wp in loadedData.whirlpools)
                {
                    float radiusPx = wp.radius * pxPerUnit;
                    DrawMarker(CellCenter(rect, wp.cellIndex), radiusPx, _style.whirlpool);
                }
            }

            // Prefab scale-radius footprint rings (base + visible tiers)
            if (pxPerUnit > 0f)
            {
                DrawPrefabScaleRings(rect, loadedData.prefabPlacements, -1, pxPerUnit);
                if (loadedData.tiers != null)
                    for (int ti = 0; ti < loadedData.tiers.Count; ti++)
                        if (ti < tierVisible.Count && tierVisible[ti])
                            DrawPrefabScaleRings(rect, loadedData.tiers[ti].prefabPlacements, ti, pxPerUnit);
            }

            // Soul fish zones — all drawing lives here in one place.
            if (loadedData.soulZones != null && pxPerUnit > 0f)
            {
                SyncSubZoneJunctions(); // keep adjoined tributary ends pinned to their main-river node
                for (int zi = 0; zi < loadedData.soulZones.Count; zi++)
                {
                    var zone = loadedData.soulZones[zi];
                    zone.MigrateNodesIfNeeded();                     // legacy data upgrade
                    var pts = zone.nodePositions;
                    if (pts == null || pts.Count == 0) continue;

                    // Fish-bowl sub-zones now render like any zone: their anchor node draws a teal
                    // radius pool (same visual as a street light), and any drawn path/junction
                    // extends from it. (The bowl's aloft height still lives on the tower prefab.)

                    Color lc  = SoulZoneColor(zone, zi);                // main = palette, sub-zone = distinct
                    float rpx = Mathf.Max(zone.radius * pxPerUnit, 1f); // node marker radius

                    // Sub-zone tributaries (fish bowls / manual sub-zones) draw a pool at the source
                    // and a thin path to the junction. Statue sub-zones stay rings; main paths stay
                    // full radius-width bands. Shared with the in-progress preview for consistency.
                    if (zone.zoneRole == GridData.SoulZone.ZoneRole.SubZone && !zone.statueGuarded)
                        DrawSubZoneTributary(rect, zone, lc, pxPerUnit);
                    else
                        DrawSoulZoneShape(rect, zone, lc, pxPerUnit, drawArrows: true);

                    // Street light lamps are collected here and drawn AFTER every zone band, so a
                    // later zone (or a tributary) can never paint over another zone's lamp numbers.
                    if (zone.streetLights != null && zone.streetLights.Count > 0)
                    {
                        var orderedLights = zone.StreetLightsInOrder();
                        for (int li = 0; li < orderedLights.Count; li++)
                        {
                            var sl = orderedLights[li];
                            if (sl == null || sl.nodeIndex < 0 || sl.nodeIndex >= pts.Count) continue;
                            _lampMarkers.Add((WorldXZToPixel(rect, pts[sl.nodeIndex]),
                                              Mathf.Max(rpx * 0.55f, 5f), li + 1));
                        }
                    }
                }

                // Lamps on top of every band drawn above.
                if (_lampMarkers.Count > 0)
                {
                    var lampLabel = new GUIStyle(EditorStyles.miniBoldLabel);
                    lampLabel.normal.textColor = Color.black;
                    foreach (var (p, r, num) in _lampMarkers)
                    {
                        Handles.color = Color.black;
                        Handles.DrawSolidDisc(p, Vector3.forward, r + 1.5f);   // outline so it reads on any band
                        Handles.color = new Color(1f, 0.95f, 0.5f, 1f);
                        Handles.DrawSolidDisc(p, Vector3.forward, r);
                        Handles.Label(p + new Vector2(-3f, -7f), num.ToString(), lampLabel);
                    }
                    _lampMarkers.Clear();
                }
            }

            // Creeper hop routes — under the icons, so the rocks sit on top of their own lines.
            DrawCreeperHopRoutes(rect, pxPerUnit);

            // Prefab icons — drawn here (after the soul zones/tributaries) so they sit ON TOP of
            // the zone bands + pools instead of being hidden beneath them.
            DrawPrefabPlacementIcons(rect);

            // Lamp markers on climbing rocks — above the icons they belong to.
            DrawCreeperLampMarkers(rect, pxPerUnit);

            // Linked Prefab Pairs
            if (loadedData.linkedPairs != null)
            {
                foreach (var pair in loadedData.linkedPairs)
                {
                    bool modOk = CellContainsPrefab(pair.modifierTierIndex, pair.modifierCellIndex, "TypeBWaveModifier");
                    bool tubeOk = CellContainsPrefab(pair.inputTubeTierIndex, pair.inputTubeCellIndex, "SoulFishInputTube");

                    Vector2 a = CellCenter(rect, pair.modifierCellIndex);
                    Vector2 b = CellCenter(rect, pair.inputTubeCellIndex);

                    if (modOk && tubeOk)
                    {
                        // The green node path now shows the connection, so only fall back to a
                        // straight link line when no path has been generated yet.
                        bool hasNodePath = pair.tubePath != null && pair.tubePath.Count >= 2;
                        if (!hasNodePath)
                        {
                            Handles.color = new Color(0.4f, 1f, 1f, 0.8f);
                            Handles.DrawLine(a, b, 2.5f);
                            Handles.DrawSolidDisc(b, Vector3.forward, 3.5f);
                        }
                    }
                    else
                    {
                        Handles.color = new Color(1f, 0.3f, 0.3f, 0.9f);
                        Handles.DrawLine(a, b, 1.5f);
                        Handles.Label((a + b) * 0.5f, "BROKEN LINK");
                    }
                }
            }

            // Selection marker (single white dot)
            if (_currentSelection.type != SelectionType.None)
            {
                Vector2 center;
                if (_currentSelection.type == SelectionType.SplineWallNode
                    && loadedData?.splineWallPaths != null
                    && _currentSelection.index < loadedData.splineWallPaths.Count)
                {
                    var wPath = loadedData.splineWallPaths[_currentSelection.index];
                    center = (wPath.nodes != null && _currentSelection.subIndex < wPath.nodes.Count)
                        ? WorldXZToPixel(rect, wPath.nodes[_currentSelection.subIndex])
                        : rect.center;
                }
                else if (_currentSelection.type == SelectionType.SoulZoneNode
                    && loadedData?.soulZones != null
                    && _currentSelection.index < loadedData.soulZones.Count)
                {
                    var pts = loadedData.soulZones[_currentSelection.index].nodePositions;
                    center = (pts != null && _currentSelection.subIndex < pts.Count)
                        ? WorldXZToPixel(rect, pts[_currentSelection.subIndex])
                        : rect.center;
                }
                else if (_currentSelection.type == SelectionType.PrefabPlacement)
                {
                    var list = _currentSelection.tierIndex == -1 ? loadedData.prefabPlacements
                             : (loadedData.tiers != null && _currentSelection.tierIndex >= 0 && _currentSelection.tierIndex < loadedData.tiers.Count
                                ? loadedData.tiers[_currentSelection.tierIndex].prefabPlacements : null);
                    center = (list != null && _currentSelection.index >= 0 && _currentSelection.index < list.Count)
                        ? PlacementPixel(rect, list[_currentSelection.index])
                        : CellCenter(rect, _currentSelection.cellIndex);
                }
                else
                {
                    center = CellCenter(rect, _currentSelection.cellIndex);
                }

                // Single dot at the centre of whatever is selected. Colour + fill/outline come from
                // the Selection appearance setting; size from the Selection circle size setting.
                DrawMarker(center, EffCell * _selectionCircleFactor, _style.selection);
            }

            // Bridge mode removed (incompatible with free nodes).

            // In-progress drawing preview — rendered with the same visual as a committed zone
            // (orange band + node markers) so drawing matches the applied result.
            if (_isDrawingSoulArea && _drawingNodes.Count >= 1)
            {
                float ppu = GetPixelsPerWorldUnit();
                if (ppu > 0f)
                {
                    bool activeValid = _activeSoulZoneIndex >= 0 && _activeSoulZoneIndex < loadedData.soulZones.Count;
                    float drawRadius = activeValid ? loadedData.soulZones[_activeSoulZoneIndex].radius : 1f;
                    var previewZone = new GridData.SoulZone
                    {
                        nodePositions = _drawingNodes,
                        closedLoop    = false,
                        radius        = drawRadius,
                        zoneRole      = activeValid ? loadedData.soulZones[_activeSoulZoneIndex].zoneRole
                                                    : GridData.SoulZone.ZoneRole.MainPath,
                    };
                    Color plc = SoulZoneColor(previewZone, _activeSoulZoneIndex);
                    DrawSoulZoneShape(rect, previewZone, plc, ppu, drawArrows: true);
                }
            }

            // Entrance + lock hub markers on the arena circumference
            DrawEntranceOverlay(rect);

            // Tube path overlays — one colour per entrance, active entrance highlighted
            DrawTubePathOverlay(rect);

            // Wave-modifier tube path overlays — draws the node path between a modifier and its tube
            DrawWMTubePathOverlay(rect);

            // Sub-zone junction rubber-band while drawing
            DrawSubZoneJunctionPreview(rect);

            // Water-level exit-pipe perimeter markers + tube path overlays
            DrawWaterModifierOverlay(rect);
            DrawPipeTubePathOverlay(rect);

            // Spline wall overlay — drawn on top of all other overlays
            DrawSplineWallOverlay(rect);

            // Cube building overlay — dark grey footprint boxes with centre nodes
            DrawCubeBuildingOverlay(rect);

            // Spike overlay — the four radii as concentric rings, plus centre nodes
            DrawSpikeOverlay(rect);

            Handles.EndGUI();
        }

        // Portal perimeter overlay
        if (loadedData != null)
            DrawPortalOverlay(rect);

        }
        finally
        {
            GUI.EndClip();
        }
    }

    bool CellContainsPrefab(int tierIndex, int cellIndex, string prefabName)
    {
        if (loadedData == null) return false;
        List<GridData.PrefabPlacement> placements;
        if (tierIndex == -1) placements = loadedData.prefabPlacements;
        else if (loadedData.tiers != null && tierIndex >= 0 && tierIndex < loadedData.tiers.Count) 
            placements = loadedData.tiers[tierIndex].prefabPlacements;
        else return false;

        if (placements == null) return false;
        return placements.Exists(p => p.cellIndex == cellIndex && p.prefab != null && p.prefab.name == prefabName);
    }

    Vector2 CellCenter(Rect gridRect, int cellIndex)
    {
        int x = cellIndex % GridSize;
        int y = cellIndex / GridSize;
        return new Vector2(gridRect.x + x * EffCell + EffCell * 0.5f,
                           gridRect.y + y * EffCell + EffCell * 0.5f);
    }

    // Derives how many grid pixels equal one world unit, using the arena profile's
    // reference plane prefab (or baseline radius) as the authority on real-world arena dimensions.
    float GetPixelsPerWorldUnit()
    {
        var level = loadedData;
        if (level == null) return -1f;

        float worldWidth = level.WorldArenaWidth;

        if (worldWidth <= 0f) return -1f;

        return (EffCell * GridSize) / worldWidth;
    }

    // Icon draw rect for a placement, scaled by its stored scale so the designer image
    // grows/shrinks with the prefab. When the prefab has a scale radius the icon is sized
    // to the footprint-ring diameter (so the image and ring stay linked); otherwise it
    // falls back to the cell size times the scale. Centred on the cell.
    Rect ScaledIconRect(Rect cell, GridData.PrefabPlacement pp, float pxPerUnit)
        => ScaledIconRect(cell.center, pp, pxPerUnit);

    // Icon draw rect centred on an arbitrary pixel point (the placement's free position).
    Rect ScaledIconRect(Vector2 centerPx, GridData.PrefabPlacement pp, float pxPerUnit)
    {
        float s    = pp.scale > 0f ? pp.scale : 1f;
        float size = EffCell * s;

        var  align       = pp.prefab != null ? GetBaselineAlign(pp.prefab) : null;
        bool scaleRadius = align != null && align.UseScaleRadius && align.ScaleRadius > 0f && pxPerUnit > 0f;
        if (scaleRadius)
            size = align.ScaleRadius * s * pxPerUnit * 2f; // match footprint ring diameter

        // Scale-radius icons ARE the scale indicator, so let them track the true footprint down to a
        // tiny minimum; plain marker icons keep a larger floor so they stay visible/clickable.
        size = Mathf.Max(size, scaleRadius ? 4f : EffCell * 0.5f);
        return new Rect(centerPx.x - size * 0.5f, centerPx.y - size * 0.5f, size, size);
    }

    // Pixel position of a placement's free position on the grid canvas.
    Vector2 PlacementPixel(Rect rect, GridData.PrefabPlacement pp)
    {
        pp.EnsureFreePosition();
        return WorldXZToPixel(rect, pp.position);
    }

    // Draws every prefab-placement icon at its free position. Called from the overlays pass AFTER
    // the soul zones/tributaries so the icons sit on top of them. Robust to overlaps (per-placement,
    // not per-cell). Alpha follows the base/tier visibility the cell loop used.
    void DrawPrefabPlacementIcons(Rect rect)
    {
        if (loadedData == null) return;
        float ppu = GetPixelsPerWorldUnit();

        float baseAlpha = (!baseLayerVisible) ? 0f : (activeTierIndex < 0) ? 1f : 0.12f;
        if (baseAlpha > 0f && loadedData.prefabPlacements != null)
            foreach (var pp in loadedData.prefabPlacements)
                DrawOnePlacementIcon(rect, pp, ppu, baseAlpha);

        if (loadedData.tiers != null)
            for (int ti = 0; ti < loadedData.tiers.Count; ti++)
            {
                if (!(ti < tierVisible.Count && tierVisible[ti])) continue;
                float a = activeTierIndex == ti ? 1f : 0.12f;
                var list = loadedData.tiers[ti].prefabPlacements;
                if (list != null)
                    foreach (var pp in list)
                        DrawOnePlacementIcon(rect, pp, ppu, a);
            }
    }

    void DrawOnePlacementIcon(Rect rect, GridData.PrefabPlacement pp, float ppu, float alpha)
    {
        if (pp?.prefab == null) return;
        Rect ir = ScaledIconRect(PlacementPixel(rect, pp), pp, ppu);

        bool isCreep = IsCreepPlacement(pp.prefab);

        // A creeper allocated to a spike has no rock of its own and sits exactly on top of one, so
        // it draws as black text centred directly over that rock rather than as a second overlapping
        // icon. Handles.color doesn't tint label text — the colour must come from the style.
        if (isCreep && !IsClimbingRock(pp.prefab))
        {
            var creepStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
            };
            creepStyle.normal.textColor = new Color(0f, 0f, 0f, alpha);
            Vector2 sz = creepStyle.CalcSize(new GUIContent(CreepAffix));
            Vector2 at = new Vector2(ir.center.x - sz.x * 0.5f, ir.center.y - sz.y * 0.5f);
            Handles.Label(at, CreepAffix, creepStyle);
            return;
        }

        // Creepers don't read as a spike on the grid — use their own icon if any, else the plain
        // fallback swatch. (No BigSpike icon borrow.)
        Texture2D icon = prefabIcons.TryGetValue(pp.prefab.name, out var i) ? i : null;

        if (icon != null)
        {
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(ir, icon, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }
        else
        {
            // Round swatch instead of a square. The icon rect is already sized to the prefab's scale
            // footprint when its baseline alignment uses a scale radius (see ScaledIconRect), so the
            // disc doubles as the scale indicator (dual purpose) with no separate ring needed. Colour
            // is the prefab's override or the global default; the overlay label and its size come from
            // the settings too.
            Color c = IconColorFor(pp.prefab); c.a = alpha;
            float radius = Mathf.Max(Mathf.Min(ir.width, ir.height) * 0.5f, 2f);

            Handles.color = c;
            Handles.DrawSolidDisc(ir.center, Vector3.forward, radius);

            string label = IconLabelFor(pp.prefab);
            if (!string.IsNullOrEmpty(label))
            {
                // Text scales with the on-screen circle so it fits big icons (and grows with zoom),
                // using iconTextSize as the size at a reference radius, capped at iconTextSizeMax.
                const float refRadiusPx = 40f;
                int fontSize = Mathf.Clamp(
                    Mathf.RoundToInt(_style.iconTextSize * radius / refRadiusPx),
                    4, Mathf.Max(4, Mathf.RoundToInt(_style.iconTextSizeMax)));

                var lblStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize  = fontSize,
                    wordWrap  = false,
                };
                lblStyle.normal.textColor = new Color(0f, 0f, 0f, alpha);
                var content = new GUIContent(label);
                Vector2 lsz = lblStyle.CalcSize(content);
                // Rect centred exactly on the circle centre; MiddleCenter then centres the text within
                // it, so a long label lands on the circle centre regardless of the measured width.
                var lr = new Rect(ir.center.x - lsz.x * 0.5f, ir.center.y - lsz.y * 0.5f, lsz.x, lsz.y);
                GUI.Label(lr, content, lblStyle);
            }
        }

        // Forward-direction arrow on the icon's circumference, for prefabs whose baseline alignment
        // overrides forward. Points along LocalForward projected to the grid (XZ). Size + colour global.
        var fAlign = GetBaselineAlign(pp.prefab);
        if (fAlign != null && fAlign.UseForwardOverride)
        {
            Vector3 lf  = fAlign.LocalForward;
            Vector2 fwd = new Vector2(lf.x, lf.z);
            if (fwd.sqrMagnitude > 1e-6f)
            {
                fwd.Normalize();
                // Apply the placement's yaw offset (clockwise in grid XZ, matching the world-up yaw
                // used at spawn) so the arrow shows the direction the prefab will actually face.
                float rad = pp.rotationOffset * Mathf.Deg2Rad;
                float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
                fwd = new Vector2(fwd.x * cs + fwd.y * sn, -fwd.x * sn + fwd.y * cs);
                Vector2 pd    = new Vector2(fwd.x, -fwd.y);        // world XZ → pixels (y inverted)
                Vector2 perp  = new Vector2(-pd.y, pd.x);
                float   aSc   = Mathf.Max(0.1f, _style.forwardArrowScale);
                float   baseR = Mathf.Max(Mathf.Min(ir.width, ir.height) * 0.5f, 6f);
                Vector2 baseC = ir.center + pd * baseR;
                Color   ac    = _style.forwardArrowColor; ac.a *= alpha;
                Handles.color = ac;
                Handles.DrawAAConvexPolygon(new Vector3[]
                {
                    (Vector3)(baseC + pd   * baseR * 0.55f * aSc),  // tip (outward)
                    (Vector3)(baseC + perp * baseR * 0.40f * aSc),
                    (Vector3)(baseC - perp * baseR * 0.40f * aSc),
                });
            }
        }

        if (isCreep)
        {
            Handles.color = new Color(0f, 0f, 0f, alpha);
            Handles.Label(new Vector2(ir.center.x - 16f, ir.yMax - 3f), CreepAffix);
        }
    }

    // Press D over a selected prefab/block: clone it, select the copy, and carry it (below).
    // Soul zones and walls are deliberately not duplicated.
    void TryStartDuplicateCarry(Event e)
    {
        if (loadedData == null) return;

        if (_currentSelection.type == SelectionType.PrefabPlacement)
        {
            var list = _currentSelection.tierIndex == -1 ? loadedData.prefabPlacements
                     : (loadedData.tiers != null && _currentSelection.tierIndex >= 0 && _currentSelection.tierIndex < loadedData.tiers.Count
                        ? loadedData.tiers[_currentSelection.tierIndex].prefabPlacements : null);
            if (list != null && _currentSelection.index >= 0 && _currentSelection.index < list.Count)
            {
                PushUndoSnapshot();
                var src = list[_currentSelection.index];
                var dup = new GridData.PrefabPlacement
                {
                    cellIndex        = src.cellIndex,
                    position         = src.position,
                    freePlaced       = true,
                    prefab           = src.prefab,
                    isCircle         = src.isCircle,
                    isWorldSpaceProp = src.isWorldSpaceProp,
                    scale            = src.scale,
                    rotationOffset   = src.rotationOffset,
                    spikePreset      = src.spikePreset,
                    statueId         = 0,   // a duplicate is not the same statue/tower — no zone link
                    overrideModifierSettings = src.overrideModifierSettings,
                    speedBoost       = src.speedBoost,
                    frequencyBoost   = src.frequencyBoost,
                    rippleDepthBoost = src.rippleDepthBoost,
                };
                list.Add(dup);
                _currentSelection = new SelectionInfo
                {
                    type      = SelectionType.PrefabPlacement,
                    index     = list.Count - 1,
                    tierIndex = _currentSelection.tierIndex,
                    cellIndex = dup.cellIndex,
                };
                _carryDuplicate = true; _carryKind = CarryKind.Prefab;
                EditorUtility.SetDirty(loadedData);
                e.Use(); Repaint();
            }
            return;
        }

        // Both indices survive a tool change, so whichever you touched last would otherwise win.
        // Hold the ▲ Spikes tool and D means the spike; otherwise it means the block.
        if (_drawSpike)
        {
            if (TryDuplicateSpike(e)) return;
            TryDuplicateCube(e);
        }
        else
        {
            if (TryDuplicateCube(e)) return;
            TryDuplicateSpike(e);
        }
    }

    bool TryDuplicateCube(Event e)
    {
        if (_activeCubeIndex < 0 || loadedData.cubeBuildings == null ||
            _activeCubeIndex >= loadedData.cubeBuildings.Count) return false;

        PushUndoSnapshot();
        var src = loadedData.cubeBuildings[_activeCubeIndex];
        loadedData.cubeBuildings.Add(new GridData.CubeBuilding
        {
            center           = src.center,
            width            = src.width,
            length           = src.length,
            heightAboveWater = src.heightAboveWater,
            depthBelowWater  = src.depthBelowWater,
            steppedTop       = src.steppedTop,
        });
        _activeCubeIndex = loadedData.cubeBuildings.Count - 1;
        _carryDuplicate  = true; _carryKind = CarryKind.Cube;
        EditorUtility.SetDirty(loadedData);
        e.Use(); Repaint();
        return true;
    }

    // A spike carries its whole placement across — same shape preset, same size, same climbable
    // and perch flags — so duplicating is how you lay out a run of matching rocks.
    bool TryDuplicateSpike(Event e)
    {
        if (_activeSpikeIndex < 0 || loadedData.proceduralSpikes == null ||
            _activeSpikeIndex >= loadedData.proceduralSpikes.Count) return false;

        PushUndoSnapshot();
        var src = loadedData.proceduralSpikes[_activeSpikeIndex];
        loadedData.proceduralSpikes.Add(new GridData.ProceduralSpike
        {
            center          = src.center,
            preset          = src.preset,
            scale           = src.scale,
            climbable       = src.climbable,
            angelPerchPoint = src.angelPerchPoint,
            angelPerchRadius   = src.angelPerchRadius,
            angelLandingCurveSize = src.angelLandingCurveSize,
            angelTalkRadius    = src.angelTalkRadius,
            angelPriorityPerch = src.angelPriorityPerch,
            angelTalkEnabled   = src.angelTalkEnabled,
            angelTalkText      = src.angelTalkText,
        });
        _activeSpikeIndex = loadedData.proceduralSpikes.Count - 1;
        _carryDuplicate   = true; _carryKind = CarryKind.Spike;
        EditorUtility.SetDirty(loadedData);
        e.Use(); Repaint();
        return true;
    }

    // While carrying a duplicate: it tracks the cursor; left-click drops it, Escape cancels (handled
    // in HandleModeHotkeys and removes the copy).
    void HandleDuplicateCarry(Rect rect, Event e)
    {
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            _carryDuplicate = false; // drop where it is
            e.Use(); Repaint();
            return;
        }
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
        {
            if (rect.Contains(e.mousePosition))
            {
                Vector2 norm = PixelToWorldXZ(rect, e.mousePosition);
                if (_carryKind == CarryKind.Cube)
                {
                    if (loadedData.cubeBuildings != null && _activeCubeIndex >= 0 && _activeCubeIndex < loadedData.cubeBuildings.Count)
                        loadedData.cubeBuildings[_activeCubeIndex].center =
                            new Vector2(Mathf.Clamp(norm.x, -0.5f, 0.5f), Mathf.Clamp(norm.y, -0.5f, 0.5f));
                }
                else if (_carryKind == CarryKind.Spike)
                {
                    if (loadedData.proceduralSpikes != null && _activeSpikeIndex >= 0 && _activeSpikeIndex < loadedData.proceduralSpikes.Count)
                        loadedData.proceduralSpikes[_activeSpikeIndex].center =
                            new Vector2(Mathf.Clamp(norm.x, -0.5f, 0.5f), Mathf.Clamp(norm.y, -0.5f, 0.5f));
                }
                else if (_currentSelection.type == SelectionType.PrefabPlacement)
                {
                    var list = _currentSelection.tierIndex == -1 ? loadedData.prefabPlacements
                             : (loadedData.tiers != null && _currentSelection.tierIndex >= 0 && _currentSelection.tierIndex < loadedData.tiers.Count
                                ? loadedData.tiers[_currentSelection.tierIndex].prefabPlacements : null);
                    if (list != null && _currentSelection.index >= 0 && _currentSelection.index < list.Count)
                    {
                        var pp = list[_currentSelection.index];
                        pp.position   = norm;
                        pp.freePlaced = true;
                        pp.SyncCellIndex();
                        _currentSelection.cellIndex = pp.cellIndex;
                    }
                }
                EditorUtility.SetDirty(loadedData);
            }
            e.Use(); Repaint();
        }
    }

    // Cached per prefab asset — DrawOnePlacementIcon runs for every placement on every repaint,
    // so this must not call GetComponentInChildren each time.
    bool IsCreepPlacement(GameObject prefab)
    {
        if (prefab == null) return false;
        if (_creepPrefabCache.TryGetValue(prefab, out bool cached)) return cached;

        bool isCreep = prefab.GetComponentInChildren<CreepyGuyController>(true) != null;
        _creepPrefabCache[prefab] = isCreep;
        return isCreep;
    }

    // Opaque footprint fill for the currently selected prefab placement. Called before
    // the cell loop so the prefab icon renders on top of it. Radius logic mirrors
    // DrawPrefabScaleRings so the fill and the outline ring stay aligned.
    void DrawSelectedPrefabScaleFill(Rect rect, float pxPerUnit)
    {
        if (pxPerUnit <= 0f || loadedData == null) return;
        if (_currentSelection.type != SelectionType.PrefabPlacement) return;

        int tierIndex = _currentSelection.tierIndex;
        List<GridData.PrefabPlacement> placements =
            tierIndex == -1 ? loadedData.prefabPlacements
            : (loadedData.tiers != null && tierIndex >= 0 && tierIndex < loadedData.tiers.Count
                ? loadedData.tiers[tierIndex].prefabPlacements : null);
        if (placements == null) return;

        var pp = placements.Find(p => p.cellIndex == _currentSelection.cellIndex);
        if (pp?.prefab == null) return;

        var align = GetBaselineAlign(pp.prefab);
        if (align == null || !align.UseScaleRadius) return;

        float s        = pp.scale > 0f ? pp.scale : 1f;
        float radiusPx = align.ScaleRadius * s * pxPerUnit;
        if (radiusPx <= 0f) return;

        Handles.BeginGUI();
        Handles.color = new Color(1f, 0.55f, 0.1f, 1f); // opaque footprint fill
        Handles.DrawSolidDisc(PlacementPixel(rect, pp), Vector3.forward, radiusPx);
        Handles.EndGUI();
    }

    // Draws a world-proportional footprint ring for every placement whose prefab has
    // a PrefabBaselineAlignment scale radius enabled. The ring grows with the
    // placement's stored scale so the designer preview matches the spawned size.
    // ─────────────────────────────────────────────
    // CREEPER — HOP ROUTES AND LAMP MARKERS
    // ─────────────────────────────────────────────

    // Every placement across the base layer and all tiers, since rocks may be placed on either.
    void CollectAllPlacements(List<GridData.PrefabPlacement> into)
    {
        into.Clear();
        if (loadedData == null) return;
        if (loadedData.prefabPlacements != null) into.AddRange(loadedData.prefabPlacements);
        if (loadedData.tiers != null)
            foreach (var t in loadedData.tiers)
                if (t?.prefabPlacements != null) into.AddRange(t.prefabPlacements);
    }

    bool IsClimbingRock(GameObject prefab)
    {
        if (prefab == null) return false;
        if (_climbingRockCache.TryGetValue(prefab, out bool cached)) return cached;

        bool isRock = prefab.GetComponentInChildren<CreepClimbingArea>(true) != null;
        _climbingRockCache[prefab] = isRock;
        return isRock;
    }

    // A placement is a "bad guy" when its prefab lives in the BadGuys library folder — the same
    // set the BadGuys library tab offers. Path-based so any prefab dropped in that folder counts,
    // without needing a shared marker component.
    bool IsBadGuyPrefab(GameObject prefab)
    {
        if (prefab == null) return false;
        if (_badGuyPrefabCache.TryGetValue(prefab, out bool cached)) return cached;

        string path = AssetDatabase.GetAssetPath(prefab);
        bool isBad = !string.IsNullOrEmpty(path)
                     && path.StartsWith(BadGuysPrefabsFolder + "/", System.StringComparison.OrdinalIgnoreCase);
        _badGuyPrefabCache[prefab] = isBad;
        return isBad;
    }

    // Reach read straight off the placed creeper prefabs, so the routes drawn here are the ones
    // they will actually be able to take. With several placed, the widest reach is used — the
    // routes then show everywhere any of them could go.
    float PlacedCreeperHopDistance(List<GridData.PrefabPlacement> all)
    {
        float widest = -1f;
        foreach (var pp in all)
        {
            if (pp?.prefab == null || !IsCreepPlacement(pp.prefab)) continue;
            var ctrl = pp.prefab.GetComponentInChildren<CreepyGuyController>(true);
            if (ctrl != null) widest = Mathf.Max(widest, ctrl.MaxHopDistance);
        }
        return widest;
    }

    // The spike nearest the clicked cell, within a couple of cells so a near miss still lands on
    // the rock you meant rather than silently doing nothing.
    GridData.PrefabPlacement FindClimbingRockNear(int cellIndex)
    {
        Vector2 target = GridData.SoulZone.CellToNormalized(cellIndex);
        float   maxDist = 2f / GridData.GridSize;

        CollectAllPlacements(_allPlacements);

        GridData.PrefabPlacement best = null;
        float bestDist = maxDist;

        foreach (var pp in _allPlacements)
        {
            if (pp?.prefab == null || !IsClimbingRock(pp.prefab)) continue;
            pp.EnsureFreePosition();
            float d = Vector2.Distance(pp.position, target);
            if (d < bestDist) { bestDist = d; best = pp; }
        }
        return best;
    }

    // Nearest CLIMBABLE procedural spike whose footprint the click lands on (with a couple of cells
    // of edge forgiveness), or -1. Lets a rockless creeper be allocated to a drawn spike, not only to
    // a climbing-rock prefab. clickNorm is the click in normalized grid space (-0.5..0.5).
    int FindClimbableSpikeNear(Vector2 clickNorm)
    {
        if (loadedData?.proceduralSpikes == null) return -1;
        float arena = Mathf.Max(0.0001f, SpikeArenaWidth());
        float slack = 2f / GridData.GridSize;      // same edge forgiveness as the rock search
        int best = -1; float bestDist = float.MaxValue;
        for (int i = 0; i < loadedData.proceduralSpikes.Count; i++)
        {
            var s = loadedData.proceduralSpikes[i];
            if (s == null || !s.climbable) continue;
            float radN = (s.Config.radiusWaterline * s.EffectiveScale) / arena;   // footprint in normalized units
            float d    = Vector2.Distance(clickNorm, s.center);
            if (d <= radN + slack && d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    int CountPlacedCreepers(List<GridData.PrefabPlacement> all)
    {
        int n = 0;
        foreach (var pp in all)
            if (pp?.prefab != null && IsCreepPlacement(pp.prefab)) n++;
        return n;
    }

    /// <summary>
    /// Thick green lines between climbing rocks he can actually get to, spreading outward from
    /// wherever the creeper is placed. A rock beyond his reach simply gets no line — move it
    /// closer and the line appears. Nothing is drawn at all when no creeper is placed.
    /// </summary>
    void DrawCreeperHopRoutes(Rect rect, float pxPerUnit)
    {
        if (!showCreeperRoutes || loadedData == null || pxPerUnit <= 0f) return;

        CollectAllPlacements(_allPlacements);
        float hopDistance = PlacedCreeperHopDistance(_allPlacements);
        if (hopDistance <= 0f) return;                       // no creeper placed — draw nothing

        _climbingRocks.Clear();
        foreach (var pp in _allPlacements)
            if (pp?.prefab != null && IsClimbingRock(pp.prefab)) _climbingRocks.Add(pp);

        float reachPx = hopDistance * pxPerUnit;

        // Spread outward one hop at a time from EVERY placed creeper, so a rock only earns a line
        // if some creeper has a chain of hops that actually reaches it.
        _rockPixels.Clear();
        foreach (var pp in _climbingRocks) _rockPixels.Add(PlacementPixel(rect, pp));

        // Spikes drawn with the ▲ Spikes tool and flagged climbable are rocks too — they get their
        // climbing rings fitted at spawn, so he can hop to AND from them just the same. Appended after
        // the placed rocks; both rock prefabs and climbable spikes host a creeper and seed the spread.
        if (loadedData.proceduralSpikes != null)
            foreach (var s in loadedData.proceduralSpikes)
                if (s != null && s.climbable) _rockPixels.Add(WorldXZToPixel(rect, s.center));

        _rockReached.Clear();
        for (int i = 0; i < _rockPixels.Count; i++) _rockReached.Add(false);
        _rockQueue.Clear();

        // Seed from the rock each creeper is standing on. One that carries its own spike IS that
        // rock; one allocated to a spike is snapped onto it — so both resolve by position.
        bool anySeed = false;
        foreach (var pp in _allPlacements)
        {
            if (pp?.prefab == null || !IsCreepPlacement(pp.prefab)) continue;
            pp.EnsureFreePosition();
            Vector2 creepPx = PlacementPixel(rect, pp);

            // The creeper is snapped onto its host — a climbing-rock prefab OR a climbable spike — so
            // the nearest marker pixel IS its host. Search all of them, not just the rock prefabs, or a
            // creeper allocated to a spike would never seed the spread.
            int   host     = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _rockPixels.Count; i++)
            {
                float d = Vector2.Distance(_rockPixels[i], creepPx);
                if (d < bestDist) { bestDist = d; host = i; }
            }

            if (host < 0 || _rockReached[host]) continue;
            _rockReached[host] = true;
            _rockQueue.Enqueue(host);
            anySeed = true;
        }
        if (!anySeed) return;                                // no creeper is standing on a rock

        // Pass one: work out which rocks a creeper can get to at all, without drawing. Marking a
        // rock as reached the moment it gets a line would draw a single chain and hide every other
        // hop he could make from it.
        while (_rockQueue.Count > 0)
        {
            int from = _rockQueue.Dequeue();
            for (int to = 0; to < _rockPixels.Count; to++)
            {
                if (_rockReached[to] || to == from) continue;
                if (Vector2.Distance(_rockPixels[from], _rockPixels[to]) > reachPx) continue;

                _rockReached[to] = true;
                _rockQueue.Enqueue(to);
            }
        }

        // Pass two: every hop that is actually available between reachable rocks, so the picture
        // matches what he can really choose from rather than one route through them.
        Handles.color = new Color(0.25f, 0.95f, 0.35f, 0.95f);

        for (int a = 0; a < _rockPixels.Count; a++)
        {
            if (!_rockReached[a]) continue;
            for (int b = a + 1; b < _rockPixels.Count; b++)
            {
                if (!_rockReached[b]) continue;
                if (Vector2.Distance(_rockPixels[a], _rockPixels[b]) > reachPx) continue;

                Handles.DrawAAPolyLine(CreeperRouteWidth, _rockPixels[a], _rockPixels[b]);
            }
        }
    }

    /// <summary>
    /// A small opaque circle above every climbing rock that falls inside a street light's radius —
    /// the rocks he will be driven off once that lamp is lit.
    /// </summary>
    void DrawCreeperLampMarkers(Rect rect, float pxPerUnit)
    {
        if (!showCreeperRoutes || loadedData == null || pxPerUnit <= 0f) return;

        CollectAllPlacements(_allPlacements);
        if (PlacedCreeperHopDistance(_allPlacements) <= 0f) return;

        // Lamps first — position and radius both come off the placed street light prefabs.
        _lampPixels.Clear();
        _lampRadii.Clear();
        foreach (var pp in _allPlacements)
        {
            if (pp?.prefab == null) continue;
            var lamp = pp.prefab.GetComponentInChildren<StreetLightController>(true);
            if (lamp == null) continue;
            _lampPixels.Add(PlacementPixel(rect, pp));
            _lampRadii.Add(lamp.InstLightRadius * pxPerUnit);
        }
        if (_lampPixels.Count == 0) return;

        foreach (var pp in _allPlacements)
        {
            if (pp?.prefab == null || !IsClimbingRock(pp.prefab)) continue;

            Vector2 rockPx = PlacementPixel(rect, pp);
            bool    lit    = false;
            for (int i = 0; i < _lampPixels.Count && !lit; i++)
                lit = Vector2.Distance(rockPx, _lampPixels[i]) <= _lampRadii[i];

            if (!lit) continue;

            Rect    ir  = ScaledIconRect(rockPx, pp, pxPerUnit);
            Vector2 dot = new Vector2(ir.center.x, ir.yMin - CreeperLampDotRadius - 2f);

            Handles.color = Color.black;
            Handles.DrawSolidDisc(dot, Vector3.forward, CreeperLampDotRadius + 1.5f);
            Handles.color = new Color(1f, 0.65f, 0.15f, 1f);
            Handles.DrawSolidDisc(dot, Vector3.forward, CreeperLampDotRadius);
        }
    }

    void DrawPrefabScaleRings(Rect rect, List<GridData.PrefabPlacement> placements, int tierIndex, float pxPerUnit)
    {
        if (placements == null) return;
        bool activeLayer = tierIndex == activeTierIndex;
        foreach (var pp in placements)
        {
            if (pp?.prefab == null) continue;
            var align = GetBaselineAlign(pp.prefab);
            if (align == null || !align.UseScaleRadius) continue;

            float s        = pp.scale > 0f ? pp.scale : 1f;
            float radiusPx = align.ScaleRadius * s * pxPerUnit;
            if (radiusPx <= 0f) continue;

            // Colour + fill/outline from the Prefab ring appearance setting; inactive layers dim.
            DrawMarker(PlacementPixel(rect, pp), radiusPx, _style.prefabRing, activeLayer ? 1f : 0.3f);
        }
    }

    void DrawPortalOverlay(Rect gridRect)
{
        float ringRadius   = ZoomedGridSize * 0.5f + 14f;
        const float arrowLen     = 10f;
        const float dotRadius    = 5f;
        Vector2     centre       = gridRect.center;

        float pxPerUnit = GetPixelsPerWorldUnit();

        Handles.BeginGUI();

        if (loadedData.entrances != null)
        {
            foreach (var ent in loadedData.entrances)
            {
                // angle: 0° = up (+Z = up in 2D), clockwise
                float rad = ent.perimeterAngle * Mathf.Deg2Rad;
                Vector2 dir    = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
                Vector2 tip    = centre + dir * ringRadius;
                Vector2 inward = centre + dir * (ringRadius - arrowLen);

                // Green dot + inward arrow
                Handles.color = Color.green;
                Handles.DrawSolidDisc(tip, Vector3.forward, dotRadius);
                Handles.DrawLine(tip, inward);

                // Arrowhead
                Vector2 perp  = new Vector2(-dir.y, dir.x);
                Vector2 ahead = inward - dir * (arrowLen * 0.4f);
                Handles.DrawLine(inward, ahead + perp * 4f);
                Handles.DrawLine(inward, ahead - perp * 4f);

                // Label
                Handles.color = Color.white;
                Handles.Label(tip + dir * 4f + new Vector2(0f, -7f), ent.id);
            }
        }

        Handles.EndGUI();
    }

    void DrawButtons()
    {
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
        if (GUILayout.Button("CLEAR ALL") && EditorUtility.DisplayDialog(
            "Clear All",
            "This will erase all cells, orbs, souls, modifiers, whirlpools, and prefab placements on every layer. This can be undone.\n\nAre you sure?",
            "Clear All", "Cancel"))
        {
            PushUndoSnapshot();
            System.Array.Clear(squareGrid, 0, CellCount);
            System.Array.Clear(circleGrid, 0, CellCount);
            if (loadedData != null)
            {
                loadedData.orbCellIndices?.Clear();
                loadedData.orbPositions?.Clear();
                loadedData.soulSpawnPoints?.Clear();
                loadedData.soulZones?.Clear();
                loadedData.waterLevelModifierCellIndices?.Clear();
                loadedData.waveModifierCellIndices?.Clear();
                loadedData.whirlpools?.Clear();
                loadedData.prefabPlacements?.Clear();
                if (loadedData.tiers != null)
                    foreach (var tier in loadedData.tiers)
                    {
                        if (tier.cells != null) System.Array.Clear(tier.cells, 0, tier.cells.Length);
                        tier.waterLevelModifierCellIndices?.Clear();
                        tier.waveModifierCellIndices?.Clear();
                        tier.prefabPlacements?.Clear();
                    }
            }
            _activeSoulZoneIndex = -1;
            _isDrawingSoulArea   = false;
            _drawingNodes.Clear();
            RebuildSlotsFromGrid();
            EditorUtility.SetDirty(loadedData);
            Repaint();
        }
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("CLEAR ORBS") && loadedData != null)
        { PushUndoSnapshot(); loadedData.orbCellIndices?.Clear(); loadedData.orbPositions?.Clear(); Repaint(); }

        if (GUILayout.Button("CLEAR SOULS") && loadedData != null)
        { PushUndoSnapshot(); loadedData.soulSpawnPoints?.Clear(); loadedData.soulZones?.Clear(); _activeSoulZoneIndex = -1; Repaint(); }

        if (GUILayout.Button("UNDO")) UndoLastAction();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("NEW"))     CreateNewGrid();
        if (GUILayout.Button("SAVE"))    SaveGridInPlace();
        if (GUILayout.Button("SAVE AS")) SaveGrid();
        if (GUILayout.Button("LOAD"))    LoadGrid();
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginDisabledGroup(loadedData == null);
        GUI.backgroundColor = new Color(0.4f, 1f, 0.5f);
        if (GUILayout.Button("Test Level"))
            GameTesterTool.LaunchScene("Waves1", loadedData, null);
        if (GUILayout.Button("Test Level (Fresh Save)"))
        {
            GameProgressData.ClearUnlocks();
            GameTesterTool.LaunchScene("Waves1", loadedData, null);
        }
        GUI.backgroundColor = Color.white;
        EditorGUI.EndDisabledGroup();
    }

    void CreateNewGrid()
    {
        if (loadedData != null || GetMaxSlotUsed() > 0)
        {
            if (!EditorUtility.DisplayDialog("New Grid", "Discard current grid and start fresh?", "Yes", "No"))
                return;
        }

        string path = EditorUtility.SaveFilePanelInProject("Create New Grid", "NewGridData", "asset", "Select location for the new GridData asset.", GridDataFolder);
        if (string.IsNullOrEmpty(path)) return;

        GridData data = ScriptableObject.CreateInstance<GridData>();
        
        // Initialize with default empty state
        data.cells        = new int[GridData.CellCount];
        data.overlayCells = new int[GridData.CellCount];
        data.slotNotes    = new List<string> { string.Empty };
        data.slotColors   = new List<Color>();
        data.levelID      = "new_level";
        data.displayName  = "New Level";

        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();

        GridLog($"Created and loaded new grid asset at {path}");
        LoadGrid(data);
        RefreshDiscoveredGrids();
    }

    void SaveGrid()
    {
        string path = EditorUtility.SaveFilePanelInProject("Save Grid", "GridData", "asset", "", GridDataFolder);
        if (string.IsNullOrEmpty(path)) return;

        // ── SAVE CONTRACT ────────────────────────────────────────────────────────────
        // "Save As" clones the ENTIRE loaded GridData so every serialized field carries over
        // automatically — soul zones (incl. fish-bowl tributaries + street lights), whirlpools,
        // spline walls, cube buildings, modifiers, linked pairs, profiles, etc. Do NOT go back to
        // copying fields one-by-one: any new GridData field would then be silently dropped here.
        // Only the working-state the designer holds in local arrays (cells/overlay/slot notes+colours)
        // is written on top of the clone. New designer features that live on GridData need no change;
        // features that add NEW local working arrays must also be written here (and into
        // SaveGridInPlace + the undo GridSnapshot).
        GridData data = loadedData != null
            ? Object.Instantiate(loadedData)
            : ScriptableObject.CreateInstance<GridData>();

        data.cells        = (int[])squareGrid.Clone();
        data.overlayCells = (int[])circleGrid.Clone();
        data.slotNotes    = new List<string>(slotNotes);
        data.slotColors   = new List<Color>(slotColors);

        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();
        RefreshDiscoveredGrids();
    }

    void SaveGridInPlace()
    {
        if (loadedData == null) { Debug.LogWarning("[GridDesigner] No grid loaded. Use Save As."); return; }
        // Save In Place: the designer edits most collections (soul zones, whirlpools, spline walls,
        // cube buildings, entrances, modifiers, links…) DIRECTLY on loadedData, so SetDirty +
        // SaveAssets persists them all. Only the working-state kept in local arrays
        // (cells/overlay/slot notes+colours) is written back here. A new designer feature that lives
        // on GridData needs nothing here; a new LOCAL working array must be copied back here too.
        Undo.RecordObject(loadedData, "Save Grid");
        loadedData.cells        = (int[])squareGrid.Clone();
        loadedData.overlayCells = (int[])circleGrid.Clone();
        loadedData.slotNotes    = new List<string>(slotNotes);
        loadedData.slotColors   = new List<Color>(slotColors);
        EditorUtility.SetDirty(loadedData);
        AssetDatabase.SaveAssets();
        Debug.Log($"[GridDesigner] Saved to {AssetDatabase.GetAssetPath(loadedData)}");
    }

    void LoadGrid()
    {
        string path = EditorUtility.OpenFilePanel("Load Grid", GridDataFolder, "asset");
        if (string.IsNullOrEmpty(path)) return;
        path = FileUtil.GetProjectRelativePath(path);
        GridData data = AssetDatabase.LoadAssetAtPath<GridData>(path);
        if (!data) return;
        LoadGrid(data);
    }

    void LoadGrid(GridData data)
    {
        loadedData = data;
        if (loadedData.orbCellIndices  == null) loadedData.orbCellIndices  = new List<int>();
        if (loadedData.orbPositions    == null) loadedData.orbPositions    = new List<Vector2>();
        if (loadedData.soulSpawnPoints == null) loadedData.soulSpawnPoints = new List<GridData.SoulSpawnPoint>();
        if (loadedData.soulZones       == null) loadedData.soulZones       = new List<GridData.SoulZone>();
        _allSoulData = null; // force re-scan on next draw
        _activeSoulZoneIndex = -1;
        _isDrawingSoulArea   = false;
        _drawingNodes.Clear();
        EnsureSoulZones(); // run legacy migration if needed
        NormalizeTowerZones(); // collapse tower zones to a single node at their tower cell

        squareGrid = (int[])loadedData.cells.Clone();
        circleGrid = loadedData.overlayCells != null
            ? (int[])loadedData.overlayCells.Clone() : new int[CellCount];

        RebuildSlotsFromGrid();

        if (loadedData.slotNotes != null)
            for (int i = 1; i < Mathf.Min(slotNotes.Count, loadedData.slotNotes.Count); i++)
                slotNotes[i] = loadedData.slotNotes[i];

        if (loadedData.slotColors != null)
            for (int i = 0; i < Mathf.Min(slotColors.Count, loadedData.slotColors.Count); i++)
                slotColors[i] = loadedData.slotColors[i];

        // Normalise legacy placements: the scale field did not exist previously,
        // so deserialisation leaves it at 0. Treat that as the default of 1.
        NormalizePlacementScales(loadedData.prefabPlacements);
        if (loadedData.tiers != null)
            foreach (var tier in loadedData.tiers)
                NormalizePlacementScales(tier.prefabPlacements);

        // Untether legacy grid-bound placements to free positions (cell centre). Idempotent.
        loadedData.MigratePlacementPositions();
        loadedData.MigrateOrbPositions();   // fold legacy cell-indexed orbs into free positions

        _baselineAlignCache.Clear();

        // Default tool on open is Select (not the eraser).
        drawSelect = true;
        activeSlot = -1;
        drawCircle = drawSoul = drawSoulArea = drawOrb = drawWhirlpool = false;
        drawWaterLevelModifier = drawWaveModifier = drawDirectPrefab = _drawSplineWall = _drawCubeBuilding = false; _drawSpike = false;
        _isDrawingSoulArea = false; _drawingNodes.Clear();
        ClearSelectState();
    }

    static void NormalizePlacementScales(List<GridData.PrefabPlacement> placements)
    {
        if (placements == null) return;
        foreach (var p in placements)
            if (p != null && p.scale <= 0f) p.scale = 1f;
    }

    void RefreshDiscoveredGrids()
    {
        discoveredGrids.Clear();
        string[] guids = AssetDatabase.FindAssets("t:GridData", new[] { GridDataFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GridData d  = AssetDatabase.LoadAssetAtPath<GridData>(path);
            if (d != null) discoveredGrids.Add(d);
        }
        discoveredGridNames = new string[discoveredGrids.Count];
        for (int i = 0; i < discoveredGrids.Count; i++)
        {
            var d = discoveredGrids[i];
            discoveredGridNames[i] = string.IsNullOrEmpty(d.displayName) ? d.name : d.displayName;
        }
        selectedDiscoveredGridIndex = Mathf.Clamp(selectedDiscoveredGridIndex, 0,
            Mathf.Max(0, discoveredGrids.Count - 1));
    }

    int GetMaxSlotUsed()
    {
        int max = 0;
        for (int i = 0; i < CellCount; i++)
        {
            max = Mathf.Max(max, squareGrid[i]);
            max = Mathf.Max(max, circleGrid[i]);
        }
        return max;
    }

    void RebuildSlotsFromGrid()
    {
        int required = GetMaxSlotUsed();
        slotColors.Clear(); slotNotes.Clear(); slotNotes.Add(string.Empty);
        for (int i = 0; i < required; i++)
        {
            slotColors.Add(Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.6f, 1f));
            slotNotes.Add(string.Empty);
        }
    }

    void EnsureSlotCapacity(int required)
    {
        while (slotColors.Count < required)
            slotColors.Add(Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.6f, 1f));
        while (slotNotes.Count < required + 1)
            slotNotes.Add(string.Empty);
    }

    // ─────────────────────────────────────────────
    // SLOT REMOVAL
    // ─────────────────────────────────────────────

    /// <summary>
    /// Returns true if any cell in either grid is currently using this slot number.
    /// Prevents removal of slots that are still painted on the grid.
    /// </summary>
    bool IsSlotInUse(int slot)
    {
        for (int i = 0; i < CellCount; i++)
            if (squareGrid[i] == slot || circleGrid[i] == slot)
                return true;
        return false;
    }

    /// <summary>
    /// Removes a slot and remaps all higher slot numbers down by one
    /// so the grid data stays consistent.
    /// </summary>
    void RemoveSlot(int slot)
    {
        PushUndoSnapshot();

        // Remove from lists (slot is 1-based, lists are 0-based)
        slotColors.RemoveAt(slot - 1);
        slotNotes.RemoveAt(slot);   // slotNotes has a leading empty entry at index 0

        // Remap grid values: anything above the removed slot shifts down by 1
        for (int i = 0; i < CellCount; i++)
        {
            if (squareGrid[i] > slot) squareGrid[i]--;
            if (circleGrid[i] > slot) circleGrid[i]--;
        }

        // If the active slot was the removed one, reset to eraser
        if (activeSlot == slot)
            activeSlot = 0;
        else if (activeSlot > slot)
            activeSlot--;

        Repaint();
    }

    // ─────────────────────────────────────────────────────────────
    // SPLINE WALL EDITOR
    // ─────────────────────────────────────────────────────────────

    // Sets every segment on a spline wall path to curved (true) or straight (false),
    // overriding the individual per-segment toggles. Undoable.
    void SetAllSplineSegmentsCurved(GridData.SplineWallPath path, bool curved)
    {
        if (path?.nodes == null || path.nodes.Count < 2) return;

        Undo.RecordObject(loadedData, curved ? "Curve All Spline Segments" : "Straighten All Spline Segments");
        if (path.segmentCurved == null) path.segmentCurved = new List<bool>();

        // Segment count: one per gap between nodes, plus the closing segment on a loop.
        int segCount = path.isClosed ? path.nodes.Count : path.nodes.Count - 1;
        while (path.segmentCurved.Count < segCount) path.segmentCurved.Add(curved);
        for (int i = 0; i < path.segmentCurved.Count; i++) path.segmentCurved[i] = curved;

        EditorUtility.SetDirty(loadedData);
        Repaint();
    }

    // Resizes a whole spline wall path to `newScale` by scaling every node around the path
    // centroid (relative to the previously-applied scale). Baked into node positions — the
    // stored pathScale is just the reference for the next relative resize. Undo handled by caller.
    void ScaleSplineWallPath(GridData.SplineWallPath path, float newScale)
    {
        if (path?.nodes == null || path.nodes.Count == 0) return;

        newScale     = Mathf.Max(0.05f, newScale);
        float oldScale = path.pathScale > 0f ? path.pathScale : 1f;
        float factor   = newScale / oldScale;
        path.pathScale = newScale;
        if (Mathf.Approximately(factor, 1f)) return;

        Vector2 centroid = Vector2.zero;
        foreach (var n in path.nodes) centroid += n;
        centroid /= path.nodes.Count;

        for (int i = 0; i < path.nodes.Count; i++)
            path.nodes[i] = centroid + (path.nodes[i] - centroid) * factor;
    }

    void DrawSplineWallsSection()
    {
        if (loadedData == null) return;

        _showSplineWalls = EditorGUILayout.Foldout(_showSplineWalls, "Spline Walls", true, EditorStyles.foldoutHeader);
        if (!_showSplineWalls) return;

        if (loadedData.splineWallPaths == null) loadedData.splineWallPaths = new List<GridData.SplineWallPath>();

        int toRemove = -1;
        for (int pi = 0; pi < loadedData.splineWallPaths.Count; pi++)
        {
            var  path     = loadedData.splineWallPaths[pi];
            bool selectedNodeOnThisPath = _currentSelection.type == SelectionType.SplineWallNode
                                       && _currentSelection.index == pi;
            bool isActive = ((_drawSplineWall && pi == _activeSplinePathIdx) || selectedNodeOnThisPath);

            // Row 1: name + delete
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = isActive ? GetSplineWallColor(pi) : Color.white;
            if (GUILayout.Button($"SplineWall{pi + 1}"))
            {
                _activeSplinePathIdx = pi;
                _drawSplineWall      = true;
            }
            GUI.backgroundColor = Color.white;
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(28))) toRemove = pi;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // Row 2: toggles + spacing + scale
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            bool  newClosed  = GUILayout.Toggle(path.isClosed, "Loop",  GUILayout.Width(46));
            GUILayout.Label(new GUIContent("Spacing", "Tile spacing — distance between each wall piece along the path"), GUILayout.Width(52));
            float newSpacing = EditorGUILayout.FloatField(path.tileSpacing, GUILayout.Width(50));
            float curScale   = path.pathScale > 0f ? path.pathScale : 1f;
            GUILayout.Label(new GUIContent("Scale", "Resize the whole path around its centre"), GUILayout.Width(42));
            float newScale   = EditorGUILayout.FloatField(curScale, GUILayout.Width(50));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(loadedData, "Edit Spline Wall Path");
                path.isClosed    = newClosed;
                path.tileSpacing = Mathf.Max(0.05f, newSpacing);
                if (!Mathf.Approximately(newScale, curScale))
                    ScaleSplineWallPath(path, newScale);
                EditorUtility.SetDirty(loadedData);
            }
            EditorGUILayout.EndHorizontal();

            if (isActive)
            {
                EditorGUI.indentLevel++;

                // Type — dropdown of every prefab in Assets/Prefab/SplineWallPrefabs
                // (e.g. ProceduralSplineWall, ProceduralSplineRailing).
                var splineOptions = GetSplineWallPrefabOptions();
                if (splineOptions.Count > 0)
                {
                    int  curIdx = splineOptions.IndexOf(path.prefabOverride);
                    var  labels = new List<string>();
                    foreach (var p in splineOptions) labels.Add(p.name);
                    // Keep an out-of-folder override visible so switching away isn't accidental.
                    if (curIdx < 0)
                    {
                        labels.Add(path.prefabOverride != null ? path.prefabOverride.name + " (custom)" : "(none)");
                        curIdx = labels.Count - 1;
                    }

                    EditorGUI.BeginChangeCheck();
                    int newIdx = EditorGUILayout.Popup("Type", curIdx, labels.ToArray());
                    if (EditorGUI.EndChangeCheck() && newIdx >= 0 && newIdx < splineOptions.Count)
                    {
                        Undo.RecordObject(loadedData, "Set Spline Wall Type");
                        path.prefabOverride = splineOptions[newIdx];
                        EditorUtility.SetDirty(loadedData);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("No prefabs found in " + SplineWallPrefabFolder + ".", MessageType.Warning);
                }

                // Destructible prefab — used for segments flagged destructible (the "D" toggle below)
                EditorGUI.BeginChangeCheck();
                var newDestructiblePrefab = (GameObject)EditorGUILayout.ObjectField(
                    "Destructible Prefab", path.destructiblePrefabOverride, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(loadedData, "Set Spline Wall Destructible Prefab");
                    path.destructiblePrefabOverride = newDestructiblePrefab;
                    EditorUtility.SetDirty(loadedData);
                }

                // Procedural wall dimensions (world units) — used when the prefab has a
                // ProceduralSplineWall component. Height is the whole-path default; the
                // per-node "H" fields below override it. Drop is applied to the whole path.
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                GUILayout.Label(new GUIContent("Height", "Wall height above water (world units). Whole-path default — per-node H below overrides it."), GUILayout.Width(48));
                float newWallHeight = EditorGUILayout.FloatField(path.wallHeight);
                GUILayout.Label(new GUIContent("Drop", "How far the wall drops below the waterline so it looks bottomless (world units). Whole path."), GUILayout.Width(38));
                float newDrop = EditorGUILayout.FloatField(path.depthBelowWater);
                GUILayout.Label(new GUIContent("Thick", "Wall thickness across the path (world units). Whole path; node columns are this × Node× thick. Overrides the prefab's own thickness."), GUILayout.Width(38));
                float newThickness = EditorGUILayout.FloatField(path.wallThickness);
                GUILayout.Label(new GUIContent("Node×", "Node column height & thickness = wall value × this (1.1 = 10% bigger). Overrides the node prefab's own scale."), GUILayout.Width(42));
                float newNodeScale = EditorGUILayout.FloatField(path.nodeSizeScale);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(loadedData, "Edit Spline Wall Dimensions");
                    path.wallHeight      = Mathf.Max(0.01f, newWallHeight);
                    path.depthBelowWater = Mathf.Max(0f, newDrop);
                    path.wallThickness   = Mathf.Max(0.001f, newThickness);
                    path.nodeSizeScale   = Mathf.Max(0.01f, newNodeScale);
                    EditorUtility.SetDirty(loadedData);
                }
                EditorGUILayout.EndHorizontal();

                // Node list
                int nodeCount = path.nodes?.Count ?? 0;
                EditorGUILayout.LabelField($"Nodes  ({nodeCount})", EditorStyles.miniBoldLabel);

                // Bulk straighten / curve — overrides every per-segment curve toggle on this path.
                if (nodeCount >= 2)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(new GUIContent("Straighten All", "Make every segment on this path straight (overrides individual toggles — undoable)"), EditorStyles.miniButtonLeft))
                        SetAllSplineSegmentsCurved(path, false);
                    if (GUILayout.Button(new GUIContent("Curve All", "Make every segment on this path curved (overrides individual toggles — undoable)"), EditorStyles.miniButtonRight))
                        SetAllSplineSegmentsCurved(path, true);
                    EditorGUILayout.EndHorizontal();
                }

                if (path.nodes != null)
                {
                    int insertAfter = -1;

                    for (int ni = 0; ni < path.nodes.Count; ni++)
                    {
                        EditorGUILayout.BeginHorizontal();

                        bool isSelectedNode = _currentSelection.type == SelectionType.SplineWallNode
                                           && _currentSelection.index == pi
                                           && _currentSelection.subIndex == ni;

                        GUI.backgroundColor = isSelectedNode ? new Color(1f, 0.7f, 0.2f) : Color.white;
                        if (GUILayout.Button($"{ni}", GUILayout.Width(22)))
                        {
                            _currentSelection = new SelectionInfo
                            {
                                type      = SelectionType.SplineWallNode,
                                index     = pi,
                                subIndex  = ni,
                                cellIndex = -1,
                            };
                            Repaint();
                        }
                        GUI.backgroundColor = Color.white;

                        // Node positions are edited by dragging in the draw window, not here.
                        GUILayout.FlexibleSpace();

                        // Per-node height override (world units); 0 = use the path Height above.
                        EditorGUI.BeginChangeCheck();
                        GUILayout.Label(new GUIContent("H", "Per-node wall height (world units). 0 = use the path Height."), GUILayout.Width(12));
                        float curH = path.NodeHeight(ni);
                        float newH = EditorGUILayout.FloatField(curH, GUILayout.Width(40));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(loadedData, "Edit Spline Wall Node Height");
                            if (path.nodeHeights == null) path.nodeHeights = new List<float>();
                            while (path.nodeHeights.Count <= ni) path.nodeHeights.Add(0f); // 0 = use path default
                            path.nodeHeights[ni] = Mathf.Max(0f, newH);
                            EditorUtility.SetDirty(loadedData);
                        }

                        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                        if (GUILayout.Button("✕", GUILayout.Width(20)))
                        {
                            Undo.RecordObject(loadedData, "Delete Spline Wall Node");
                            path.nodes.RemoveAt(ni);
                            if (path.segmentCurved != null && ni < path.segmentCurved.Count)
                                path.segmentCurved.RemoveAt(ni);
                            if (path.segmentGap != null && ni < path.segmentGap.Count)
                                path.segmentGap.RemoveAt(ni);
                            if (path.segmentDestructible != null && ni < path.segmentDestructible.Count)
                                path.segmentDestructible.RemoveAt(ni);
                            if (path.nodeHeights != null && ni < path.nodeHeights.Count)
                                path.nodeHeights.RemoveAt(ni);
                            EditorUtility.SetDirty(loadedData);
                            EditorGUILayout.EndHorizontal();
                            break;
                        }
                        GUI.backgroundColor = Color.white;

                        EditorGUILayout.EndHorizontal();

                        // "+" insert and curve toggle between this node and the next
                        bool hasNext = ni < path.nodes.Count - 1;
                        bool isLoop  = path.isClosed && ni == path.nodes.Count - 1;
                        if (hasNext || isLoop)
                        {
                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Space(26);
                            GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
                            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(22)))
                                insertAfter = ni;
                            GUI.backgroundColor = Color.white;

                            // Per-segment gap flag (space = no wall between these nodes)
                            if (path.segmentGap == null) path.segmentGap = new List<bool>();
                            while (path.segmentGap.Count <= ni) path.segmentGap.Add(false);
                            bool segGap = path.segmentGap[ni];

                            // Per-segment curve toggle (disabled while the segment is a gap)
                            if (path.segmentCurved == null) path.segmentCurved = new List<bool>();
                            while (path.segmentCurved.Count <= ni) path.segmentCurved.Add(true);
                            bool segCurved = path.segmentCurved[ni];
                            using (new EditorGUI.DisabledScope(segGap))
                            {
                                bool newSegCurved = GUILayout.Toggle(segCurved, new GUIContent(segCurved ? "~" : "—", segCurved ? "Curved segment" : "Straight segment"), EditorStyles.miniButton, GUILayout.Width(22));
                                if (newSegCurved != segCurved)
                                {
                                    Undo.RecordObject(loadedData, "Toggle Segment Curve");
                                    path.segmentCurved[ni] = newSegCurved;
                                    EditorUtility.SetDirty(loadedData);
                                }
                            }

                            GUI.backgroundColor = segGap ? new Color(0.4f, 0.7f, 1f) : Color.white;
                            bool newSegGap = GUILayout.Toggle(segGap, new GUIContent("··", segGap ? "Gap (no wall) — click for wall" : "Wall — click for gap (no wall)"), EditorStyles.miniButton, GUILayout.Width(22));
                            GUI.backgroundColor = Color.white;
                            if (newSegGap != segGap)
                            {
                                Undo.RecordObject(loadedData, "Toggle Segment Gap");
                                path.segmentGap[ni] = newSegGap;
                                EditorUtility.SetDirty(loadedData);
                            }

                            // Per-segment destructible flag (uses the path's Destructible Prefab; disabled while a gap)
                            if (path.segmentDestructible == null) path.segmentDestructible = new List<bool>();
                            while (path.segmentDestructible.Count <= ni) path.segmentDestructible.Add(false);
                            bool segDestr = path.segmentDestructible[ni];
                            using (new EditorGUI.DisabledScope(segGap))
                            {
                                GUI.backgroundColor = segDestr ? new Color(1f, 0.5f, 0.2f) : Color.white;
                                bool newSegDestr = GUILayout.Toggle(segDestr, new GUIContent("D", segDestr ? "Destructible wall — click for normal wall" : "Normal wall — click for destructible"), EditorStyles.miniButton, GUILayout.Width(22));
                                GUI.backgroundColor = Color.white;
                                if (newSegDestr != segDestr)
                                {
                                    Undo.RecordObject(loadedData, "Toggle Segment Destructible");
                                    path.segmentDestructible[ni] = newSegDestr;
                                    EditorUtility.SetDirty(loadedData);
                                }
                            }

                            EditorGUILayout.EndHorizontal();
                        }
                    }

                    if (insertAfter >= 0)
                    {
                        Undo.RecordObject(loadedData, "Insert Spline Wall Node");
                        int     nextIdx  = (insertAfter + 1) % path.nodes.Count;
                        Vector2 midpoint = (path.nodes[insertAfter] + path.nodes[nextIdx]) * 0.5f;
                        path.nodes.Insert(insertAfter + 1, midpoint);
                        if (path.segmentCurved == null) path.segmentCurved = new List<bool>();
                        while (path.segmentCurved.Count <= insertAfter) path.segmentCurved.Add(true);
                        bool inheritedCurve = path.segmentCurved[insertAfter];
                        path.segmentCurved.Insert(insertAfter + 1, inheritedCurve);
                        // New segment inherits the gap state of the split segment; original becomes a wall again.
                        if (path.segmentGap == null) path.segmentGap = new List<bool>();
                        while (path.segmentGap.Count <= insertAfter) path.segmentGap.Add(false);
                        bool inheritedGap = path.segmentGap[insertAfter];
                        path.segmentGap.Insert(insertAfter + 1, inheritedGap);
                        // New segment inherits the destructible state of the split segment.
                        if (path.segmentDestructible == null) path.segmentDestructible = new List<bool>();
                        while (path.segmentDestructible.Count <= insertAfter) path.segmentDestructible.Add(false);
                        bool inheritedDestr = path.segmentDestructible[insertAfter];
                        path.segmentDestructible.Insert(insertAfter + 1, inheritedDestr);
                        // Keep per-node height overrides index-aligned; new node uses the path default (0).
                        if (path.nodeHeights != null && insertAfter + 1 <= path.nodeHeights.Count)
                            path.nodeHeights.Insert(insertAfter + 1, 0f);
                        EditorUtility.SetDirty(loadedData);
                    }
                }

                EditorGUI.indentLevel--;
            }
        }

        if (toRemove >= 0)
        {
            Undo.RecordObject(loadedData, "Remove Spline Wall Path");
            loadedData.splineWallPaths.RemoveAt(toRemove);
            _activeSplinePathIdx = Mathf.Clamp(_activeSplinePathIdx, 0, Mathf.Max(0, loadedData.splineWallPaths.Count - 1));
            EditorUtility.SetDirty(loadedData);
        }

        if (GUILayout.Button("+ Add Spline Wall Path"))
        {
            Undo.RecordObject(loadedData, "Add Spline Wall Path");
            loadedData.splineWallPaths.Add(new GridData.SplineWallPath
            {
                prefabOverride = GetDefaultSplineWallPrefab()
            });
            _activeSplinePathIdx = loadedData.splineWallPaths.Count - 1;
            _drawSplineWall      = true;
            EditorUtility.SetDirty(loadedData);
        }
    }

    void HandleSelectSplineWallInput(Rect rect, Event e)
    {
        if (loadedData?.splineWallPaths == null || loadedData.splineWallPaths.Count == 0) return;

        const float pickRadius = 9f;

        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            // Check proximity to any spline wall node
            int   hitPath = -1, hitNode = -1;
            float best    = pickRadius;
            for (int pi = 0; pi < loadedData.splineWallPaths.Count; pi++)
            {
                var path = loadedData.splineWallPaths[pi];
                if (path.nodes == null) continue;
                for (int ni = 0; ni < path.nodes.Count; ni++)
                {
                    float d = Vector2.Distance(WorldXZToPixel(rect, path.nodes[ni]), e.mousePosition);
                    if (d < best) { best = d; hitPath = pi; hitNode = ni; }
                }
            }

            if (hitPath >= 0)
            {
                _currentSelection = new SelectionInfo
                {
                    type     = SelectionType.SplineWallNode,
                    index    = hitPath,
                    subIndex = hitNode,
                    cellIndex = -1,
                };
                _isDraggingNode = true;
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseDrag && e.button == 0
                 && _isDraggingNode && _currentSelection.type == SelectionType.SplineWallNode)
        {
            int pi = _currentSelection.index;
            int ni = _currentSelection.subIndex;
            if (loadedData.splineWallPaths != null && pi < loadedData.splineWallPaths.Count)
            {
                var path = loadedData.splineWallPaths[pi];
                if (path.nodes != null && ni < path.nodes.Count)
                {
                    Undo.RecordObject(loadedData, "Move Spline Wall Node");
                    path.nodes[ni] = PixelToWorldXZ(rect, e.mousePosition);
                    EditorUtility.SetDirty(loadedData);
                }
            }
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseUp && e.button == 0
                 && _currentSelection.type == SelectionType.SplineWallNode)
        {
            _isDraggingNode = false;
            e.Use();
        }
    }

    // Select-tool free move for soul zone nodes — pick by pixel proximity, drag anywhere.
    void HandleSelectSoulNodeInput(Rect rect, Event e)
    {
        if (loadedData?.soulZones == null || loadedData.soulZones.Count == 0) return;

        const float pickRadius = 10f;

        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            int   hitZone = -1, hitNode = -1;
            float best    = pickRadius;
            for (int zi = 0; zi < loadedData.soulZones.Count; zi++)
            {
                var z = loadedData.soulZones[zi];
                var pts = z.nodePositions;
                if (pts == null) continue;
                // Fish-bowl tributaries: node 0 is the pool anchor (locked to the bowl); its path
                // nodes are selectable/adjustable like the main river.
                int startNi = z.towerGuarded ? 1 : 0;
                for (int ni = startNi; ni < pts.Count; ni++)
                {
                    float d = Vector2.Distance(WorldXZToPixel(rect, pts[ni]), e.mousePosition);
                    if (d < best) { best = d; hitZone = zi; hitNode = ni; }
                }
            }

            if (hitZone >= 0)
            {
                _currentSelection = new SelectionInfo
                {
                    type      = SelectionType.SoulZoneNode,
                    index     = hitZone,
                    subIndex  = hitNode,
                    cellIndex = -1,
                };
                _selectedZoneIndex   = hitZone;
                _selectedNodeIndex   = hitNode;
                _activeSoulZoneIndex = hitZone;
                _isDraggingNode      = true;
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseDrag && e.button == 0
                 && _isDraggingNode && _currentSelection.type == SelectionType.SoulZoneNode)
        {
            int zi = _currentSelection.index;
            int ni = _currentSelection.subIndex;
            if (zi < loadedData.soulZones.Count)
            {
                var z   = loadedData.soulZones[zi];
                var pts = z.nodePositions;
                // The adjoining node is pinned to its main-river node — use "Separate" to free it.
                bool pinned = z.zoneRole == GridData.SoulZone.ZoneRole.SubZone
                           && z.adjoinZoneId != 0 && pts != null && ni == pts.Count - 1;

                // Path ends clamped to entrances are owned by those doors — dragging them would
                // just be undone by SyncZoneEntrances on the next repaint, so block it outright.
                if (z.attachToEntrances && pts != null)
                {
                    if (ni == 0 && z.entryEntranceIndex >= 0) pinned = true;
                    if (ni == pts.Count - 1 && z.exitEntranceIndex >= 0 && pts.Count >= 2) pinned = true;
                }
                if (pts != null && ni < pts.Count && !pinned)
                {
                    Undo.RecordObject(loadedData, "Move Soul Zone Node");
                    pts[ni] = PixelToWorldXZ(rect, e.mousePosition);
                    EditorUtility.SetDirty(loadedData);
                }
            }
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseUp && e.button == 0
                 && _currentSelection.type == SelectionType.SoulZoneNode)
        {
            _isDraggingNode = false;
            e.Use();
        }
    }

    // Position-based prefab-placement picking + free drag (placements are no longer grid-bound).
    // Runs before the cell loop so it takes precedence near a prefab; other cell-based selections
    // (grid slots, orbs, modifiers) still work when no prefab is under the cursor.
    void HandleSelectPrefabInput(Rect rect, Event e)
    {
        if (loadedData?.prefabPlacements == null) return;
        float pick = Mathf.Max(EffCell * 0.5f, 10f);

        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            int   bestTier = -1, bestIdx = -1;
            float best     = pick;

            for (int i = 0; i < loadedData.prefabPlacements.Count; i++)
            {
                var pp = loadedData.prefabPlacements[i];
                if (pp == null) continue;
                float d = Vector2.Distance(PlacementPixel(rect, pp), e.mousePosition);
                if (d < best) { best = d; bestTier = -1; bestIdx = i; }
            }
            if (loadedData.tiers != null)
                for (int ti = 0; ti < loadedData.tiers.Count; ti++)
                {
                    if (ti < tierVisible.Count && !tierVisible[ti]) continue;
                    var list = loadedData.tiers[ti].prefabPlacements;
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var pp = list[i]; if (pp == null) continue;
                        float d = Vector2.Distance(PlacementPixel(rect, pp), e.mousePosition);
                        if (d < best) { best = d; bestTier = ti; bestIdx = i; }
                    }
                }

            if (bestIdx >= 0)
            {
                var list = bestTier == -1 ? loadedData.prefabPlacements : loadedData.tiers[bestTier].prefabPlacements;
                _currentSelection = new SelectionInfo
                {
                    type      = SelectionType.PrefabPlacement,
                    index     = bestIdx,
                    tierIndex = bestTier,
                    cellIndex = list[bestIdx].cellIndex,
                };
                _selectedZoneIndex = -1;
                _selectedNodeIndex = -1;
                _isDraggingNode    = true;
                _dragUndoPushed    = false;
                LogSelection(_currentSelection);
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && _isDraggingNode
                 && _currentSelection.type == SelectionType.PrefabPlacement)
        {
            var list = _currentSelection.tierIndex == -1 ? loadedData.prefabPlacements
                     : (loadedData.tiers != null && _currentSelection.tierIndex < loadedData.tiers.Count
                        ? loadedData.tiers[_currentSelection.tierIndex].prefabPlacements : null);
            if (list != null && _currentSelection.index < list.Count)
            {
                var pp = list[_currentSelection.index];
                if (!_dragUndoPushed) { PushUndoSnapshot(); _dragUndoPushed = true; }
                Undo.RecordObject(loadedData, "Move Prefab Placement");
                int oldCell = pp.cellIndex;
                pp.position   = PixelToWorldXZ(rect, e.mousePosition);
                pp.freePlaced = true;
                pp.SyncCellIndex();
                int newCell = pp.cellIndex;
                // Keep cell-keyed modifier↔tube links pointing at this placement as it moves.
                if (newCell != oldCell && loadedData.linkedPairs != null)
                    for (int li = 0; li < loadedData.linkedPairs.Count; li++)
                    {
                        var lp = loadedData.linkedPairs[li];
                        bool ch = false;
                        if (lp.modifierTierIndex  == _currentSelection.tierIndex && lp.modifierCellIndex  == oldCell) { lp.modifierCellIndex  = newCell; ch = true; }
                        if (lp.inputTubeTierIndex == _currentSelection.tierIndex && lp.inputTubeCellIndex == oldCell) { lp.inputTubeCellIndex = newCell; ch = true; }
                        if (ch) loadedData.linkedPairs[li] = lp;
                    }
                _currentSelection.cellIndex = pp.cellIndex;
                EditorUtility.SetDirty(loadedData);
            }
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseUp && e.button == 0
                 && _currentSelection.type == SelectionType.PrefabPlacement)
        {
            _isDraggingNode = false;
            _dragUndoPushed = false;
            e.Use();
        }
    }

    // Converts a perimeter angle (degrees, 0=forward/+Z, clockwise) to a pixel position on the canvas circumference
    Vector2 AngleToCircumferencePixel(Rect gridRect, float angleDeg, float radiusScale = 1f)
    {
        float rad    = angleDeg * Mathf.Deg2Rad;
        float radius = ZoomedGridSize * 0.5f * radiusScale;
        return new Vector2(gridRect.center.x + Mathf.Sin(rad) * radius,
                           gridRect.center.y - Mathf.Cos(rad) * radius);
    }

    void DrawEntranceOverlay(Rect rect)
    {
        if (loadedData?.entrances == null) return;

        foreach (var ent in loadedData.entrances)
        {
            if (!ent.isLocked) continue;

            Vector2 lockPt = AngleToCircumferencePixel(rect, ent.lockHubAngle, 1f);
            Handles.color = Color.black;
            Handles.DrawSolidDisc(lockPt, Vector3.forward, 6f);
            Handles.color = new Color(1f, 0.85f, 0.1f);
            Handles.DrawSolidDisc(lockPt, Vector3.forward, 3.5f);
            Handles.Label(lockPt + new Vector2(5f, -8f), "[L]");
        }
    }

    // Returns the pixel-center of a grid cell
    Vector2 CellToGridPixel(Rect rect, int cx, int cy) =>
        new Vector2(rect.x + cx * EffCell + EffCell * 0.5f,
                    rect.y + cy * EffCell + EffCell * 0.5f);

    // Converts an angle+radius on the arena circumference to the nearest clamped grid cell
    UnityEngine.Vector2Int HubAngleToGridCell(Rect rect, float angleDeg)
    {
        Vector2 px = AngleToCircumferencePixel(rect, angleDeg, 1f);
        int cx = Mathf.Clamp(Mathf.FloorToInt((px.x - rect.x) / EffCell), 0, GridSize - 1);
        int cy = Mathf.Clamp(Mathf.FloorToInt((px.y - rect.y) / EffCell), 0, GridSize - 1);
        return new UnityEngine.Vector2Int(cx, cy);
    }

    void HandleTubePlacementInput(Rect rect, Event e)
    {
        // Repaint every frame so the preview follows the mouse
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            Repaint();

        if (!rect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            int mx = Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.x - rect.x) / EffCell), 0, GridSize - 1);
            int my = Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.y - rect.y) / EffCell), 0, GridSize - 1);

            var ent = loadedData.entrances[_tubePlacingEntranceIndex];
            var tubeCell = new UnityEngine.Vector2Int(mx, my);
            var hubCell  = HubAngleToGridCell(rect, ent.lockHubAngle);

            Undo.RecordObject(loadedData, "Place Input Tube");
            ent.tubePath = new List<UnityEngine.Vector2Int>();

            // Path: input tube → intermediate nodes → hub
            ent.tubePath.Add(tubeCell);
            int total = ent.tubeSubdivisions + 2;
            for (int si = 1; si < total - 1; si++)
            {
                float t  = (float)si / (total - 1);
                int   cx = Mathf.RoundToInt(Mathf.Lerp(tubeCell.x, hubCell.x, t));
                int   cy = Mathf.RoundToInt(Mathf.Lerp(tubeCell.y, hubCell.y, t));
                ent.tubePath.Add(new UnityEngine.Vector2Int(cx, cy));
            }
            ent.tubePath.Add(hubCell);

            _tubePlacingEntranceIndex = -1;
            _selectedTubeNodeIndex    = -1;
            EditorUtility.SetDirty(loadedData);
            e.Use();
            Repaint();
        }
    }

    void GenerateTubePath(int entranceIndex)
    {
        var ent = loadedData.entrances[entranceIndex];
        if (ent.tubePath == null || ent.tubePath.Count < 2) return;

        Undo.RecordObject(loadedData, "Generate Tube Path");
        var first = ent.tubePath[0];
        var last  = ent.tubePath[ent.tubePath.Count - 1];

        ent.tubePath.Clear();
        ent.tubePath.Add(first);

        int total = ent.tubeSubdivisions + 2; // first + subdivisions + last
        for (int si = 1; si < total - 1; si++)
        {
            float t  = (float)si / (total - 1);
            int   cx = Mathf.RoundToInt(Mathf.Lerp(first.x, last.x, t));
            int   cy = Mathf.RoundToInt(Mathf.Lerp(first.y, last.y, t));
            ent.tubePath.Add(new UnityEngine.Vector2Int(cx, cy));
        }

        ent.tubePath.Add(last);
        _selectedTubeNodeIndex = -1;
        EditorUtility.SetDirty(loadedData);
        Repaint();
    }

    void HandleTubePathInput(Rect rect, Event e)
    {
        if (!rect.Contains(e.mousePosition)) return;

        int x = Mathf.FloorToInt((e.mousePosition.x - rect.x) / EffCell);
        int y = Mathf.FloorToInt((e.mousePosition.y - rect.y) / EffCell);
        if (x < 0 || x >= GridSize || y < 0 || y >= GridSize) return;

        var entrance = loadedData.entrances[_tubeDrawEntranceIndex];
        if (entrance.tubePath == null) entrance.tubePath = new List<UnityEngine.Vector2Int>();

        var cell = new UnityEngine.Vector2Int(x, y);

        int lockedIdx = entrance.tubePath.Count - 1; // last node is locked to hub position

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            int hitNode = entrance.tubePath.IndexOf(cell);
            if (hitNode >= 0 && hitNode != lockedIdx)
            {
                _dragTubeNodeIndex     = hitNode;
                _selectedTubeNodeIndex = hitNode;
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && _dragTubeNodeIndex >= 0)
        {
            if (_dragTubeNodeIndex < entrance.tubePath.Count && entrance.tubePath[_dragTubeNodeIndex] != cell)
            {
                Undo.RecordObject(loadedData, "Move Tube Waypoint");
                entrance.tubePath[_dragTubeNodeIndex] = cell;
                _selectedTubeNodeIndex = _dragTubeNodeIndex;
                EditorUtility.SetDirty(loadedData);
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            _dragTubeNodeIndex = -1;
            e.Use();
        }
    }

    void DrawTubePathOverlay(Rect rect)
    {
        if (loadedData?.entrances == null) return;

        Color[] tubePalette =
        {
            new Color(1f,   0.5f, 0f,   1f),
            new Color(0.2f, 0.8f, 1f,   1f),
            new Color(0.8f, 0.3f, 1f,   1f),
            new Color(0.3f, 1f,   0.4f, 1f),
        };

        // ── Placement preview ──────────────────────────────────────────
        if (_tubePlacingEntranceIndex >= 0 && _tubePlacingEntranceIndex < loadedData.entrances.Count
            && rect.Contains(Event.current.mousePosition))
        {
            var  ent  = loadedData.entrances[_tubePlacingEntranceIndex];
            Color pc  = tubePalette[_tubePlacingEntranceIndex % tubePalette.Length];

            // Hub position on canvas
            Vector2 hubPx = AngleToCircumferencePixel(rect, ent.lockHubAngle, 1f);

            // Mouse position snapped to cell center
            int mx = Mathf.Clamp(Mathf.FloorToInt((Event.current.mousePosition.x - rect.x) / EffCell), 0, GridSize - 1);
            int my = Mathf.Clamp(Mathf.FloorToInt((Event.current.mousePosition.y - rect.y) / EffCell), 0, GridSize - 1);
            Vector2 tubePx = CellToGridPixel(rect, mx, my);

            // Draw preview line hub → tube
            Handles.color = new Color(pc.r, pc.g, pc.b, 0.5f);
            Handles.DrawDottedLine(hubPx, tubePx, 4f);

            // Intermediate node previews
            int total = ent.tubeSubdivisions + 2;
            for (int si = 1; si < total - 1; si++)
            {
                float t = (float)si / (total - 1);
                Vector2 np = Vector2.Lerp(tubePx, hubPx, t);
                Handles.color = new Color(pc.r, pc.g, pc.b, 0.7f);
                Handles.DrawSolidDisc(np, Vector3.forward, EffCell * 0.22f);
            }

            // Input tube dot at cursor (bright)
            Handles.color = pc;
            Handles.DrawSolidDisc(tubePx, Vector3.forward, EffCell * 0.35f);
            Handles.color = Color.white;
            Handles.DrawWireDisc(tubePx, Vector3.forward, EffCell * 0.45f, 2f);
            Handles.Label(tubePx + new Vector2(6f, -8f), "Input Tube");
        }

        // ── Sync last node of each path to current hub position ────────
        foreach (var ent in loadedData.entrances)
        {
            if (!ent.isLocked || ent.tubePath == null || ent.tubePath.Count < 2) continue;
            var hubCell = HubAngleToGridCell(rect, ent.lockHubAngle);
            if (ent.tubePath[ent.tubePath.Count - 1] != hubCell)
            {
                ent.tubePath[ent.tubePath.Count - 1] = hubCell;
                EditorUtility.SetDirty(loadedData);
            }
        }

        // ── Placed paths ───────────────────────────────────────────────
        for (int ei = 0; ei < loadedData.entrances.Count; ei++)
        {
            var ent = loadedData.entrances[ei];
            if (!ent.isLocked || ent.tubePath == null || ent.tubePath.Count == 0) continue;

            bool active = _tubeDrawEntranceIndex == ei;
            Color c = tubePalette[ei % tubePalette.Length];
            c.a = active ? 1f : 0.55f;
            Handles.color = c;

            // Connecting lines
            for (int pi = 0; pi < ent.tubePath.Count - 1; pi++)
            {
                Vector2 a = CellToGridPixel(rect, ent.tubePath[pi].x,     ent.tubePath[pi].y);
                Vector2 b = CellToGridPixel(rect, ent.tubePath[pi + 1].x, ent.tubePath[pi + 1].y);
                Handles.DrawLine(a, b, active ? 2.5f : 1.5f);
            }

            // Node dots + labels
            for (int pi = 0; pi < ent.tubePath.Count; pi++)
            {
                Vector2 center = CellToGridPixel(rect, ent.tubePath[pi].x, ent.tubePath[pi].y);
                float radius   = pi == 0 ? EffCell * 0.35f : EffCell * 0.25f; // input tube slightly larger
                Handles.color = c;
                Handles.DrawSolidDisc(center, Vector3.forward, radius);

                if (active && pi == _selectedTubeNodeIndex)
                {
                    Handles.color = Color.white;
                    Handles.DrawWireDisc(center, Vector3.forward, radius + EffCell * 0.1f, 2f);
                }

                Handles.color = Color.black;
                Handles.Label(center - new Vector2(3f, 6f), pi.ToString());
            }

            // Label first node as input tube
            if (ent.tubePath.Count > 0)
            {
                Vector2 labelPos = CellToGridPixel(rect, ent.tubePath[0].x, ent.tubePath[0].y);
                Handles.color = Color.white;
                Handles.Label(labelPos + new Vector2(6f, -8f), $"[Tube] {ent.id}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Water-level exit-pipe tube path — mirrors the entrance/lock tube, keyed on
    // arenaWaterModifiers. node[0] is the input-tube cell; the last node tracks the
    // pipe's perimeter cell (derived from perimeterAngle); middle nodes are draggable.
    // ─────────────────────────────────────────────────────────────────────────

    void HandlePipeTubePlacementInput(Rect rect, Event e)
    {
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) Repaint();
        if (!rect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            int mx = Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.x - rect.x) / EffCell), 0, GridSize - 1);
            int my = Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.y - rect.y) / EffCell), 0, GridSize - 1);

            var wm       = loadedData.arenaWaterModifiers[_pipeTubePlacingIndex];
            var tubeCell = new UnityEngine.Vector2Int(mx, my);
            var pipeCell = HubAngleToGridCell(rect, wm.perimeterAngle);

            Undo.RecordObject(loadedData, "Place Input Tube");
            wm.tubePath = new List<UnityEngine.Vector2Int> { tubeCell };
            int total = wm.tubeSubdivisions + 2;
            for (int si = 1; si < total - 1; si++)
            {
                float t  = (float)si / (total - 1);
                int   cx = Mathf.RoundToInt(Mathf.Lerp(tubeCell.x, pipeCell.x, t));
                int   cy = Mathf.RoundToInt(Mathf.Lerp(tubeCell.y, pipeCell.y, t));
                wm.tubePath.Add(new UnityEngine.Vector2Int(cx, cy));
            }
            wm.tubePath.Add(pipeCell);

            _pipeTubePlacingIndex  = -1;
            _selectedTubeNodeIndex = -1;
            EditorUtility.SetDirty(loadedData);
            e.Use();
            Repaint();
        }
    }

    void HandlePipeTubePathInput(Rect rect, Event e)
    {
        if (!rect.Contains(e.mousePosition)) return;

        int x = Mathf.FloorToInt((e.mousePosition.x - rect.x) / EffCell);
        int y = Mathf.FloorToInt((e.mousePosition.y - rect.y) / EffCell);
        if (x < 0 || x >= GridSize || y < 0 || y >= GridSize) return;

        var wm = loadedData.arenaWaterModifiers[_pipeTubeDrawIndex];
        if (wm.tubePath == null) wm.tubePath = new List<UnityEngine.Vector2Int>();

        var cell      = new UnityEngine.Vector2Int(x, y);
        int lockedIdx = wm.tubePath.Count - 1; // last node is locked to the pipe cell

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            int hitNode = wm.tubePath.IndexOf(cell);
            if (hitNode >= 0 && hitNode != lockedIdx)
            {
                _dragTubeNodeIndex     = hitNode;
                _selectedTubeNodeIndex = hitNode;
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && _dragTubeNodeIndex >= 0)
        {
            if (_dragTubeNodeIndex < wm.tubePath.Count && wm.tubePath[_dragTubeNodeIndex] != cell)
            {
                Undo.RecordObject(loadedData, "Move Tube Waypoint");
                wm.tubePath[_dragTubeNodeIndex] = cell;
                _selectedTubeNodeIndex = _dragTubeNodeIndex;
                EditorUtility.SetDirty(loadedData);
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            _dragTubeNodeIndex = -1;
            e.Use();
        }
    }

    void GeneratePipeTubePath(int idx)
    {
        var wm = loadedData.arenaWaterModifiers[idx];
        if (wm.tubePath == null || wm.tubePath.Count < 2) return;

        Undo.RecordObject(loadedData, "Generate Tube Path");
        var first = wm.tubePath[0];
        var last  = wm.tubePath[wm.tubePath.Count - 1];

        wm.tubePath.Clear();
        wm.tubePath.Add(first);
        int total = wm.tubeSubdivisions + 2;
        for (int si = 1; si < total - 1; si++)
        {
            float t  = (float)si / (total - 1);
            int   cx = Mathf.RoundToInt(Mathf.Lerp(first.x, last.x, t));
            int   cy = Mathf.RoundToInt(Mathf.Lerp(first.y, last.y, t));
            wm.tubePath.Add(new UnityEngine.Vector2Int(cx, cy));
        }
        wm.tubePath.Add(last);
        _selectedTubeNodeIndex = -1;
        EditorUtility.SetDirty(loadedData);
        Repaint();
    }

    void DrawWaterModifierOverlay(Rect rect)
    {
        if (loadedData?.arenaWaterModifiers == null) return;

        foreach (var wm in loadedData.arenaWaterModifiers)
        {
            Vector2 px = AngleToCircumferencePixel(rect, wm.perimeterAngle, 1f);
            Handles.color = Color.black;
            Handles.DrawSolidDisc(px, Vector3.forward, 6f);
            Handles.color = new Color(0.2f, 0.8f, 1f);
            Handles.DrawSolidDisc(px, Vector3.forward, 3.5f);
            Handles.Label(px + new Vector2(5f, -8f), "[W]");
        }
    }

    void DrawPipeTubePathOverlay(Rect rect)
    {
        if (loadedData?.arenaWaterModifiers == null) return;

        Color[] pal =
        {
            new Color(0.2f, 0.8f, 1f,   1f),
            new Color(1f,   0.6f, 0.1f, 1f),
            new Color(0.7f, 0.3f, 1f,   1f),
            new Color(0.3f, 1f,   0.5f, 1f),
        };

        // ── Placement preview ──
        if (_pipeTubePlacingIndex >= 0 && _pipeTubePlacingIndex < loadedData.arenaWaterModifiers.Count
            && rect.Contains(Event.current.mousePosition))
        {
            var  wm = loadedData.arenaWaterModifiers[_pipeTubePlacingIndex];
            Color pc = pal[_pipeTubePlacingIndex % pal.Length];
            Vector2 pipePx = AngleToCircumferencePixel(rect, wm.perimeterAngle, 1f);

            int mx = Mathf.Clamp(Mathf.FloorToInt((Event.current.mousePosition.x - rect.x) / EffCell), 0, GridSize - 1);
            int my = Mathf.Clamp(Mathf.FloorToInt((Event.current.mousePosition.y - rect.y) / EffCell), 0, GridSize - 1);
            Vector2 tubePx = CellToGridPixel(rect, mx, my);

            Handles.color = new Color(pc.r, pc.g, pc.b, 0.5f);
            Handles.DrawDottedLine(pipePx, tubePx, 4f);

            int total = wm.tubeSubdivisions + 2;
            for (int si = 1; si < total - 1; si++)
            {
                float t = (float)si / (total - 1);
                Vector2 np = Vector2.Lerp(tubePx, pipePx, t);
                Handles.color = new Color(pc.r, pc.g, pc.b, 0.7f);
                Handles.DrawSolidDisc(np, Vector3.forward, EffCell * 0.22f);
            }

            Handles.color = pc;
            Handles.DrawSolidDisc(tubePx, Vector3.forward, EffCell * 0.35f);
            Handles.color = Color.white;
            Handles.DrawWireDisc(tubePx, Vector3.forward, EffCell * 0.45f, 2f);
            Handles.Label(tubePx + new Vector2(6f, -8f), "Input Tube");
        }

        // ── Sync last node of each path to current pipe cell ──
        foreach (var wm in loadedData.arenaWaterModifiers)
        {
            if (wm.tubePath == null || wm.tubePath.Count < 2) continue;
            var pipeCell = HubAngleToGridCell(rect, wm.perimeterAngle);
            if (wm.tubePath[wm.tubePath.Count - 1] != pipeCell)
            {
                wm.tubePath[wm.tubePath.Count - 1] = pipeCell;
                EditorUtility.SetDirty(loadedData);
            }
        }

        // ── Placed paths ──
        for (int wi = 0; wi < loadedData.arenaWaterModifiers.Count; wi++)
        {
            var wm = loadedData.arenaWaterModifiers[wi];
            if (wm.tubePath == null || wm.tubePath.Count == 0) continue;

            bool active = _pipeTubeDrawIndex == wi;
            Color c = pal[wi % pal.Length];
            c.a = active ? 1f : 0.55f;
            Handles.color = c;

            for (int pi = 0; pi < wm.tubePath.Count - 1; pi++)
            {
                Vector2 a = CellToGridPixel(rect, wm.tubePath[pi].x,     wm.tubePath[pi].y);
                Vector2 b = CellToGridPixel(rect, wm.tubePath[pi + 1].x, wm.tubePath[pi + 1].y);
                Handles.DrawLine(a, b, active ? 2.5f : 1.5f);
            }

            for (int pi = 0; pi < wm.tubePath.Count; pi++)
            {
                Vector2 center = CellToGridPixel(rect, wm.tubePath[pi].x, wm.tubePath[pi].y);
                float radius   = pi == 0 ? EffCell * 0.35f : EffCell * 0.25f;
                Handles.color = c;
                Handles.DrawSolidDisc(center, Vector3.forward, radius);

                if (active && pi == _selectedTubeNodeIndex)
                {
                    Handles.color = Color.white;
                    Handles.DrawWireDisc(center, Vector3.forward, radius + EffCell * 0.1f, 2f);
                }

                Handles.color = Color.black;
                Handles.Label(center - new Vector2(3f, 6f), pi.ToString());
            }

            Vector2 labelPos = CellToGridPixel(rect, wm.tubePath[0].x, wm.tubePath[0].y);
            Handles.color = Color.white;
            Handles.Label(labelPos + new Vector2(6f, -8f), $"[Tube] {wm.id}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Wave-modifier tube path — mirrors the lock tube system, keyed on linkedPairs.
    // node[0] tracks the input-tube cell, the last node tracks the modifier cell,
    // and the middle nodes are freely draggable in "Edit Nodes" mode.
    // ─────────────────────────────────────────────────────────────────────────

    UnityEngine.Vector2Int CellIndexToColRow(int cellIndex) =>
        new UnityEngine.Vector2Int(cellIndex % GridSize, cellIndex / GridSize);

    // Returns the linkedPairs index whose modifier matches this placement, or -1.
    int FindModifierPairIndex(int modTierIndex, int modCellIndex)
    {
        if (loadedData?.linkedPairs == null) return -1;
        for (int i = 0; i < loadedData.linkedPairs.Count; i++)
        {
            var p = loadedData.linkedPairs[i];
            if (p.modifierTierIndex == modTierIndex && p.modifierCellIndex == modCellIndex)
                return i;
        }
        return -1;
    }

    // Keeps the first/last nodes glued to the current tube/modifier cells so the path
    // follows the placements (same idea as the lock forcing its last node onto the hub).
    void WMSyncTubeEndpoints(int pairIdx)
    {
        var pair = loadedData.linkedPairs[pairIdx];
        if (pair.tubePath == null || pair.tubePath.Count < 2) return;

        var tubeCR = CellIndexToColRow(pair.inputTubeCellIndex);
        var modCR  = CellIndexToColRow(pair.modifierCellIndex);
        int last   = pair.tubePath.Count - 1;
        bool changed = false;

        if (pair.tubePath[0] != tubeCR)   { pair.tubePath[0] = tubeCR; changed = true; }
        if (pair.tubePath[last] != modCR) { pair.tubePath[last] = modCR; changed = true; }
        if (changed) EditorUtility.SetDirty(loadedData);
    }

    // Straight line of `tubeSubdivisions` intermediate nodes between the tube and modifier.
    void WMGenerateTubePath(int pairIdx)
    {
        var pair   = loadedData.linkedPairs[pairIdx];
        var tubeCR = CellIndexToColRow(pair.inputTubeCellIndex);
        var modCR  = CellIndexToColRow(pair.modifierCellIndex);
        int subs   = pair.tubeSubdivisions > 0 ? pair.tubeSubdivisions : 3; // 0/unset → sensible default

        Undo.RecordObject(loadedData, "Generate Modifier Tube Path");
        if (pair.tubePath == null) pair.tubePath = new List<UnityEngine.Vector2Int>();
        var path = pair.tubePath;
        path.Clear();
        path.Add(tubeCR);
        int total = subs + 2; // first + subdivisions + last
        for (int si = 1; si < total - 1; si++)
        {
            float t  = (float)si / (total - 1);
            int   cx = Mathf.RoundToInt(Mathf.Lerp(tubeCR.x, modCR.x, t));
            int   cy = Mathf.RoundToInt(Mathf.Lerp(tubeCR.y, modCR.y, t));
            path.Add(new UnityEngine.Vector2Int(cx, cy));
        }
        path.Add(modCR);

        loadedData.linkedPairs[pairIdx] = pair; // write back (covers first-time list creation)
        _wmSelTubeNodeIdx = -1;
        EditorUtility.SetDirty(loadedData);
        Repaint();
    }

    // Inserts a midpoint node between every existing pair of nodes (same as the lock's "+").
    void WMSubdivideTubePath(int pairIdx)
    {
        var pair = loadedData.linkedPairs[pairIdx];
        if (pair.tubePath == null || pair.tubePath.Count < 2) return;

        Undo.RecordObject(loadedData, "Subdivide Modifier Tube Path");
        var old        = pair.tubePath;
        var subdivided = new List<UnityEngine.Vector2Int>();
        for (int si = 0; si < old.Count - 1; si++)
        {
            subdivided.Add(old[si]);
            subdivided.Add(new UnityEngine.Vector2Int(
                Mathf.RoundToInt((old[si].x + old[si + 1].x) * 0.5f),
                Mathf.RoundToInt((old[si].y + old[si + 1].y) * 0.5f)));
        }
        subdivided.Add(old[old.Count - 1]);

        pair.tubePath = subdivided;
        loadedData.linkedPairs[pairIdx] = pair;
        _wmSelTubeNodeIdx = -1;
        EditorUtility.SetDirty(loadedData);
        Repaint();
    }

    void HandleWMTubePathInput(Rect rect, Event e)
    {
        if (_wmTubeDrawPairIdx < 0 || _wmTubeDrawPairIdx >= loadedData.linkedPairs.Count) return;
        if (!rect.Contains(e.mousePosition)) return;

        int x = Mathf.FloorToInt((e.mousePosition.x - rect.x) / EffCell);
        int y = Mathf.FloorToInt((e.mousePosition.y - rect.y) / EffCell);
        if (x < 0 || x >= GridSize || y < 0 || y >= GridSize) return;

        var pair = loadedData.linkedPairs[_wmTubeDrawPairIdx];
        if (pair.tubePath == null || pair.tubePath.Count < 2) return;

        var cell    = new UnityEngine.Vector2Int(x, y);
        int lastIdx = pair.tubePath.Count - 1;

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            int hitNode = pair.tubePath.IndexOf(cell);
            // Node 0 is the input-tube end (draggable — moves the tube prefab with it).
            // Middle nodes are waypoints. The last node is pinned to the modifier.
            if (hitNode >= 0 && hitNode < lastIdx)
            {
                _wmDragTubeNodeIdx = hitNode;
                _wmSelTubeNodeIdx  = hitNode;
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 &&
                 _wmDragTubeNodeIdx >= 0 && _wmDragTubeNodeIdx < lastIdx)
        {
            if (_wmDragTubeNodeIdx == 0)
            {
                // Dragging the tube end relocates the input-tube placement + the link so the
                // tube and its spline endpoint travel together without breaking the connection.
                if (pair.tubePath[0] != cell && MoveWMInputTube(_wmTubeDrawPairIdx, cell))
                {
                    _wmSelTubeNodeIdx = 0;
                    e.Use();
                    Repaint();
                }
            }
            else if (_wmDragTubeNodeIdx < pair.tubePath.Count && pair.tubePath[_wmDragTubeNodeIdx] != cell)
            {
                Undo.RecordObject(loadedData, "Move Modifier Tube Waypoint");
                pair.tubePath[_wmDragTubeNodeIdx] = cell;
                _wmSelTubeNodeIdx = _wmDragTubeNodeIdx;
                EditorUtility.SetDirty(loadedData);
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            _wmDragTubeNodeIdx = -1;
            e.Use();
        }
    }

    // Relocates the input-tube placement (and updates the link + path endpoint) to a new cell,
    // so the input tube and its spline node move together. Returns false if the move is blocked
    // (no change, or another placement already occupies the target cell). Undo-recorded on success.
    bool MoveWMInputTube(int pairIdx, UnityEngine.Vector2Int newCell)
    {
        var pair = loadedData.linkedPairs[pairIdx];
        int newCellIndex = newCell.y * GridSize + newCell.x;
        if (newCellIndex == pair.inputTubeCellIndex) return false;

        var placements = pair.inputTubeTierIndex == -1
            ? loadedData.prefabPlacements
            : (loadedData.tiers != null && pair.inputTubeTierIndex >= 0 && pair.inputTubeTierIndex < loadedData.tiers.Count
                ? loadedData.tiers[pair.inputTubeTierIndex].prefabPlacements : null);

        // Don't stomp another placement (e.g. the modifier) already on the target cell.
        if (placements != null && placements.Exists(p => p.cellIndex == newCellIndex)) return false;

        Undo.RecordObject(loadedData, "Move Input Tube");
        var tubePlacement = placements?.Find(p => p.cellIndex == pair.inputTubeCellIndex);
        if (tubePlacement != null) tubePlacement.cellIndex = newCellIndex;

        pair.inputTubeCellIndex = newCellIndex;
        if (pair.tubePath != null && pair.tubePath.Count > 0) pair.tubePath[0] = newCell;
        loadedData.linkedPairs[pairIdx] = pair;
        EditorUtility.SetDirty(loadedData);
        return true;
    }

    void DrawWMTubePathOverlay(Rect rect)
    {
        if (loadedData?.linkedPairs == null) return;

        Color baseCol = new Color(0.2f, 1f, 0.55f, 1f); // green — distinct from the lock's orange

        for (int i = 0; i < loadedData.linkedPairs.Count; i++)
        {
            WMSyncTubeEndpoints(i);

            var pair = loadedData.linkedPairs[i];
            if (pair.tubePath == null || pair.tubePath.Count < 2) continue;

            bool  active = _wmTubeDrawPairIdx == i;
            Color c      = baseCol; c.a = active ? 1f : 0.5f;

            Handles.color = c;
            for (int pi = 0; pi < pair.tubePath.Count - 1; pi++)
            {
                Vector2 a = CellToGridPixel(rect, pair.tubePath[pi].x,     pair.tubePath[pi].y);
                Vector2 b = CellToGridPixel(rect, pair.tubePath[pi + 1].x, pair.tubePath[pi + 1].y);
                Handles.DrawLine(a, b, active ? 2.5f : 1.5f);
            }

            for (int pi = 0; pi < pair.tubePath.Count; pi++)
            {
                Vector2 center   = CellToGridPixel(rect, pair.tubePath[pi].x, pair.tubePath[pi].y);
                bool    endpoint = pi == 0 || pi == pair.tubePath.Count - 1;
                float   radius   = endpoint ? EffCell * 0.32f : EffCell * 0.22f;

                Handles.color = c;
                Handles.DrawSolidDisc(center, Vector3.forward, radius);

                if (active && pi == _wmSelTubeNodeIdx)
                {
                    Handles.color = Color.white;
                    Handles.DrawWireDisc(center, Vector3.forward, radius + EffCell * 0.1f, 2f);
                }

                Handles.color = Color.black;
                Handles.Label(center - new Vector2(3f, 6f), pi.ToString());
            }
        }
    }

    void HandleSplineWallInput(Rect rect, Event e)
    {
        if (loadedData.splineWallPaths == null)
            loadedData.splineWallPaths = new List<GridData.SplineWallPath>();

        // Ensure at least one path exists and active index is valid
        if (loadedData.splineWallPaths.Count == 0)
        {
            loadedData.splineWallPaths.Add(new GridData.SplineWallPath
            {
                prefabOverride = GetDefaultSplineWallPrefab()
            });
            _activeSplinePathIdx = 0;
            EditorUtility.SetDirty(loadedData);
        }
        _activeSplinePathIdx = Mathf.Clamp(_activeSplinePathIdx, 0, loadedData.splineWallPaths.Count - 1);

        const float pickRadius = 9f;

        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            // Check if near an existing node on any path
            int hitPath = -1, hitNode = -1;
            float bestDist = pickRadius;
            for (int pi = 0; pi < loadedData.splineWallPaths.Count; pi++)
            {
                var path = loadedData.splineWallPaths[pi];
                if (path.nodes == null) continue;
                for (int ni = 0; ni < path.nodes.Count; ni++)
                {
                    float d = Vector2.Distance(WorldXZToPixel(rect, path.nodes[ni]), e.mousePosition);
                    if (d < bestDist) { bestDist = d; hitPath = pi; hitNode = ni; }
                }
            }

            if (hitPath >= 0)
            {
                // Start dragging existing node
                _activeSplinePathIdx = hitPath;
                _dragSplinePathIdx   = hitPath;
                _dragSplineNodeIdx   = hitNode;
            }
            else
            {
                // Add node to active path
                Undo.RecordObject(loadedData, "Add Spline Wall Node");
                var activePath = loadedData.splineWallPaths[_activeSplinePathIdx];
                if (activePath.nodes == null) activePath.nodes = new List<Vector2>();
                activePath.nodes.Add(PixelToWorldXZ(rect, e.mousePosition));
                EditorUtility.SetDirty(loadedData);
            }
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && _dragSplineNodeIdx >= 0)
        {
            if (_dragSplinePathIdx >= 0 && _dragSplinePathIdx < loadedData.splineWallPaths.Count)
            {
                var path = loadedData.splineWallPaths[_dragSplinePathIdx];
                if (path.nodes != null && _dragSplineNodeIdx < path.nodes.Count)
                {
                    Undo.RecordObject(loadedData, "Move Spline Wall Node");
                    path.nodes[_dragSplineNodeIdx] = PixelToWorldXZ(rect, e.mousePosition);
                    EditorUtility.SetDirty(loadedData);
                }
            }
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            if (_dragSplineNodeIdx >= 0)
            {
                _dragSplinePathIdx = -1;
                _dragSplineNodeIdx = -1;
                e.Use();
            }
        }
        else if (e.type == EventType.MouseDown && e.button == 1 && rect.Contains(e.mousePosition))
        {
            // Right-click: delete nearest node
            int hitPath = -1, hitNode = -1;
            float bestDist = pickRadius;
            for (int pi = 0; pi < loadedData.splineWallPaths.Count; pi++)
            {
                var path = loadedData.splineWallPaths[pi];
                if (path.nodes == null) continue;
                for (int ni = 0; ni < path.nodes.Count; ni++)
                {
                    float d = Vector2.Distance(WorldXZToPixel(rect, path.nodes[ni]), e.mousePosition);
                    if (d < bestDist) { bestDist = d; hitPath = pi; hitNode = ni; }
                }
            }
            if (hitPath >= 0)
            {
                Undo.RecordObject(loadedData, "Delete Spline Wall Node");
                var rp = loadedData.splineWallPaths[hitPath];
                rp.nodes.RemoveAt(hitNode);
                if (rp.nodeHeights != null && hitNode < rp.nodeHeights.Count)
                    rp.nodeHeights.RemoveAt(hitNode);
                EditorUtility.SetDirty(loadedData);
                e.Use();
                Repaint();
            }
        }
    }

    void DrawSplineWallOverlay(Rect rect)
    {
        if (loadedData?.splineWallPaths == null || loadedData.splineWallPaths.Count == 0) return;

        for (int pi = 0; pi < loadedData.splineWallPaths.Count; pi++)
        {
            var  path     = loadedData.splineWallPaths[pi];
            if (path?.nodes == null || path.nodes.Count == 0) continue;

            bool  isActive = (_drawSplineWall || drawSelect) && pi == _activeSplinePathIdx;
            Color col      = GetSplineWallColor(pi);
            col.a = isActive ? 0.95f : 0.45f;

            int n        = path.nodes.Count;
            int segCount = path.isClosed ? n : n - 1;

            // Draw spline curve or straight segments — black outline pass then white fill pass
            if (n >= 2)
            {
                const int samplesPerSeg = 16;
                float outlineW = isActive ? 7f : 5f;
                float fillW    = isActive ? 4f : 2.5f;

                // Build one polyline per (non-gap) segment so gaps leave a visible break,
                // then draw all outlines, then all fills. Destructible segments fill orange.
                var segPolys = new List<Vector3[]>();
                var segDestr = new List<bool>();
                for (int seg = 0; seg < segCount; seg++)
                {
                    if (path.IsSegmentGap(seg)) continue;

                    bool curved = path.IsSegmentCurved(seg);
                    int  i2     = path.isClosed ? (seg + 1) % n : seg + 1;
                    var  line   = new List<Vector3>();

                    Vector2 start = curved
                        ? WorldXZToPixel(rect, SplineWallSample(path.nodes, seg, 0f, path.isClosed))
                        : WorldXZToPixel(rect, path.nodes[seg]);
                    line.Add((Vector3)start);

                    if (curved)
                    {
                        for (int s = 1; s <= samplesPerSeg; s++)
                        {
                            Vector2 pt = WorldXZToPixel(rect, SplineWallSample(path.nodes, seg, (float)s / samplesPerSeg, path.isClosed));
                            line.Add((Vector3)pt);
                        }
                    }
                    else
                    {
                        line.Add((Vector3)WorldXZToPixel(rect, path.nodes[i2]));
                    }

                    segPolys.Add(line.ToArray());
                    segDestr.Add(path.IsSegmentDestructible(seg));
                }

                Handles.color = Color.black;
                foreach (var poly in segPolys)
                    if (poly.Length >= 2) Handles.DrawAAPolyLine(outlineW, poly);
                var destrFill = new Color(1f, 0.5f, 0.2f);
                for (int p = 0; p < segPolys.Count; p++)
                {
                    if (segPolys[p].Length < 2) continue;
                    Handles.color = segDestr[p] ? destrFill : Color.white;
                    Handles.DrawAAPolyLine(fillW, segPolys[p]);
                }
            }

            // Draw nodes
            for (int ni = 0; ni < n; ni++)
            {
                Vector2 px          = WorldXZToPixel(rect, path.nodes[ni]);
                bool    isDragNode  = pi == _dragSplinePathIdx && ni == _dragSplineNodeIdx;
                float   r           = isActive ? 5f : 3.5f;

                float nodeOuter = isActive ? 8f : 6f;
                float nodeInner = isActive ? 5.5f : 4f;
                Handles.color = Color.black;
                Handles.DrawSolidDisc(px, Vector3.forward, nodeOuter);
                Handles.color = isDragNode ? new Color(1f, 0.4f, 0f) : Color.white;
                Handles.DrawSolidDisc(px, Vector3.forward, nodeInner);

                if (ni == 0)
                {
                    Handles.color = Color.black;
                    Handles.DrawWireDisc(px, Vector3.forward, nodeOuter + 2.5f, 2f);
                }

                if (isActive)
                {
                    // Label in the opposite colour so it reads on both white and black paths
                    Color labelCol = (col == Color.white) ? Color.black : Color.white;
                    GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = labelCol } };
                    Handles.Label(px + new Vector2(6f, -8f), ni.ToString(), labelStyle);
                }
            }
        }
    }

    // Catmull-Rom sample shared by editor overlay and (via LevelSpawner) runtime
    static Vector2 SplineWallSample(List<Vector2> pts, int seg, float t, bool closed)
    {
        int n  = pts.Count;
        int i0 = closed ? (seg - 1 + n) % n : Mathf.Max(seg - 1, 0);
        int i1 = seg;
        int i2 = closed ? (seg + 1) % n : Mathf.Min(seg + 1, n - 1);
        int i3 = closed ? (seg + 2) % n : Mathf.Min(seg + 2, n - 1);
        float t2 = t * t, t3 = t2 * t;
        Vector2 p0 = pts[i0], p1 = pts[i1], p2 = pts[i2], p3 = pts[i3];
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    // Soul zone path sampled to canvas pixels, curved segments via the same Catmull-Rom the
    // walls (and LevelSpawner's runtime densification) use. Closed loops end back at node 0.
    // Snaps a pixel position to the nearest Main-Path node (within a small radius). Returns its
    // zoneId, node index and normalized position — the junction endpoint for a sub-zone path.
    bool TrySnapToMainNode(Rect rect, Vector2 mousePx, out int zoneId, out int nodeIdx, out Vector2 pos)
    {
        zoneId = 0; nodeIdx = -1; pos = Vector2.zero;
        if (loadedData?.soulZones == null) return false;

        float best = Mathf.Max(EffCell * 0.6f, 12f); // snap radius (px)
        for (int zi = 0; zi < loadedData.soulZones.Count; zi++)
        {
            var mz = loadedData.soulZones[zi];
            if (mz.zoneRole != GridData.SoulZone.ZoneRole.MainPath || mz.nodePositions == null) continue;
            for (int ni = 0; ni < mz.nodePositions.Count; ni++)
            {
                float d = Vector2.Distance(WorldXZToPixel(rect, mz.nodePositions[ni]), mousePx);
                if (d < best) { best = d; zoneId = mz.zoneId; nodeIdx = ni; pos = mz.nodePositions[ni]; }
            }
        }
        return nodeIdx >= 0;
    }

    // Sub-zone junction drawing: click empty grid to add a path node extending from the radius;
    // click a Main-Path node to snap the junction and finish.
    void HandleSubZoneJunctionInput(Rect rect, Event e)
    {
        if (_subZoneDrawIdx < 0 || _subZoneDrawIdx >= loadedData.soulZones.Count) { _subZoneDrawIdx = -1; return; }
        var zone = loadedData.soulZones[_subZoneDrawIdx];
        if (zone.zoneRole != GridData.SoulZone.ZoneRole.SubZone) { _subZoneDrawIdx = -1; return; }

        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) Repaint();
        if (!(e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))) return;

        if (zone.nodePositions == null) zone.nodePositions = new List<Vector2>();
        if (zone.nodePositions.Count == 0) zone.nodePositions.Add(Vector2.zero); // ensure a radius anchor

        if (TrySnapToMainNode(rect, e.mousePosition, out int snapZoneId, out int snapNodeIdx, out Vector2 snapPos))
        {
            Undo.RecordObject(loadedData, "Connect Sub-Zone Junction");
            zone.nodePositions.Add(snapPos);          // final node coincides with the main-path node
            zone.adjoinZoneId    = snapZoneId;
            zone.adjoinNodeIndex = snapNodeIdx;
            _subZoneDrawIdx      = -1;                 // done
            GridLog($"Sub-zone junction: adjoined main path id {snapZoneId} at node {snapNodeIdx}.");
            EditorUtility.SetDirty(loadedData);
            e.Use(); Repaint();
        }
        else
        {
            Undo.RecordObject(loadedData, "Add Sub-Zone Path Node");
            zone.nodePositions.Add(PixelToWorldXZ(rect, e.mousePosition)); // normalized grid position
            EditorUtility.SetDirty(loadedData);
            e.Use(); Repaint();
        }
    }

    // Live preview while drawing a sub-zone junction: a teal rubber-band from the last node to the
    // cursor, snapping (white ring) onto a Main-Path node when hovered.
    void DrawSubZoneJunctionPreview(Rect rect)
    {
        if (_subZoneDrawIdx < 0 || _subZoneDrawIdx >= loadedData.soulZones.Count) return;
        var zone = loadedData.soulZones[_subZoneDrawIdx];
        var pts  = zone.nodePositions;
        if (pts == null || pts.Count == 0) return;

        Vector2 mouse = Event.current.mousePosition;
        Vector2 last  = WorldXZToPixel(rect, pts[pts.Count - 1]);
        bool snapping = TrySnapToMainNode(rect, mouse, out _, out _, out Vector2 snapPos);
        Vector2 end   = snapping ? WorldXZToPixel(rect, snapPos) : mouse;

        Handles.color = snapping ? Color.white : new Color(SubZoneColor.r, SubZoneColor.g, SubZoneColor.b, 0.85f);
        Handles.DrawLine(last, end, 2f);
        if (snapping) Handles.DrawWireDisc(end, Vector3.forward, EffCell * 0.5f, 2f);
    }

    // Draws a Sub-Zone tributary: the radius pool ONLY around the source (node 0), then a THIN
    // connecting path out to the junction (unlike a main path, which is a full radius-width band).
    void DrawSubZoneTributary(Rect rect, GridData.SoulZone zone, Color lc, float pxPerUnit)
    {
        var pts = zone.nodePositions;
        if (pts == null || pts.Count == 0) return;

        // Source pool: one opaque circle (street-light simplicity), nothing else.
        float poolPx = Mathf.Max(zone.radius * pxPerUnit, 3f);
        Vector2 src  = WorldXZToPixel(rect, pts[0]);
        Handles.color = lc;
        Handles.DrawSolidDisc(src, Vector3.forward, poolPx);

        // "FB" tag, bold, above-centre of the bowl pool.
        if (zone.towerGuarded)
        {
            var fb = new GUIStyle(EditorStyles.boldLabel)
            { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 12, normal = { textColor = Color.black } };
            Vector2 sz = fb.CalcSize(new GUIContent("FB"));
            Handles.Label(new Vector2(src.x - sz.x * 0.5f, src.y - poolPx - sz.y), "FB", fb);
        }

        // Connecting path (curved where authored). Draws as a band of pathWidth, or a thin line
        // when pathWidth is ~0.
        if (pts.Count >= 2)
        {
            var   pixelPath = BuildZonePixelPath(rect, zone);
            float bandPx    = zone.pathWidth * pxPerUnit;
            Handles.color = lc;
            if (bandPx >= 1.5f)
            {
                for (int i = 0; i < pixelPath.Count - 1; i++)
                {
                    Vector2 a = pixelPath[i], b = pixelPath[i + 1];
                    Vector2 d = b - a;
                    if (d.sqrMagnitude < 0.0001f) continue;
                    d.Normalize();
                    Vector2 n = new Vector2(-d.y, d.x) * bandPx;
                    Handles.DrawAAConvexPolygon((Vector3)(a + n), (Vector3)(b + n), (Vector3)(b - n), (Vector3)(a - n));
                    if (i > 0) Handles.DrawSolidDisc(a, Vector3.forward, bandPx);
                }
            }
            else
            {
                for (int i = 0; i < pixelPath.Count - 1; i++)
                    Handles.DrawAAPolyLine(3f, (Vector3)pixelPath[i], (Vector3)pixelPath[i + 1]);
            }

            // Street-light pools on the tributary (gates), under the node markers.
            if (zone.streetLights != null)
                foreach (var sl in zone.streetLights)
                {
                    if (sl == null || sl.nodeIndex < 0 || sl.nodeIndex >= pts.Count) continue;
                    Handles.color = lc;
                    Handles.DrawSolidDisc(WorldXZToPixel(rect, pts[sl.nodeIndex]), Vector3.forward,
                                          Mathf.Max(sl.poolRadius * pxPerUnit, 5f));
                }

            // Flow arrows riding the path — same as the main river, showing the fish direction.
            DrawZoneFlowArrows(pixelPath, Mathf.Max(bandPx, 6f));

            // Small dots at each authored waypoint (node 0 is the pool centre).
            for (int ni = 1; ni < pts.Count; ni++)
            {
                Vector2 p = WorldXZToPixel(rect, pts[ni]);
                Handles.color = lc;
                Handles.DrawSolidDisc(p, Vector3.forward, 4.5f);
                Handles.color = Color.black;
                Handles.DrawSolidDisc(p, Vector3.forward, 2.25f);
            }
        }
    }

    // Draws a soul zone's core visual — the orange band, street-light pools, node markers and
    // flow arrows. Shared by committed zones and the in-progress drawing preview so drawing a
    // zone looks identical to the applied result.
    void DrawSoulZoneShape(Rect rect, GridData.SoulZone zone, Color lc, float pxPerUnit, bool drawArrows)
    {
        var pts = zone.nodePositions;
        if (pts == null || pts.Count == 0) return;

        float rpx       = Mathf.Max(zone.radius * pxPerUnit, 1f);
        var   pixelPath = BuildZonePixelPath(rect, zone);

        // Band — filled quads along the sampled path, discs at interior joints keep it gapless.
        Handles.color = lc;
        for (int si = 0; si < pixelPath.Count - 1; si++)
        {
            Vector2 a = pixelPath[si];
            Vector2 b = pixelPath[si + 1];
            Vector2 d = b - a;
            if (d.sqrMagnitude < 0.0001f) continue;
            d.Normalize();
            Vector2 n = new Vector2(-d.y, d.x) * rpx; // half-width = node radius
            Handles.DrawAAConvexPolygon((Vector3)(a + n), (Vector3)(b + n), (Vector3)(b - n), (Vector3)(a - n));
            if (si > 0) Handles.DrawSolidDisc(a, Vector3.forward, rpx);
        }

        // Street-light pools — part of the footprint, under the node markers.
        if (zone.streetLights != null)
            foreach (var slPool in zone.streetLights)
            {
                if (slPool == null || slPool.nodeIndex < 0 || slPool.nodeIndex >= pts.Count) continue;
                Handles.color = lc;
                Handles.DrawSolidDisc(WorldXZToPixel(rect, pts[slPool.nodeIndex]), Vector3.forward,
                                      Mathf.Max(slPool.poolRadius * pxPerUnit, 5f));
            }

        // Orange circle + black dot at each node.
        float dotR = Mathf.Max(rpx * 0.45f, 3f);
        for (int ni = 0; ni < pts.Count; ni++)
        {
            Vector2 p = WorldXZToPixel(rect, pts[ni]);
            Handles.color = lc;
            Handles.DrawSolidDisc(p, Vector3.forward, rpx);
            Handles.color = Color.black;
            Handles.DrawSolidDisc(p, Vector3.forward, dotR);
        }

        if (drawArrows) DrawZoneFlowArrows(pixelPath, rpx);
    }

    List<Vector2> BuildZonePixelPath(Rect rect, GridData.SoulZone zone)
    {
        var pts  = zone.nodePositions;
        var path = new List<Vector2>();
        int n = pts?.Count ?? 0;
        if (n == 0) return path;

        bool closed   = zone.closedLoop && n >= 3;
        int  segCount = zone.SegmentCount();

        // Sampled through GridData.SoulZone.SamplePath — the exact call LevelSpawner densifies
        // with — so what you draw here is the curve the mask paints AND the one the fish swim.
        // Preview uses more samples than the runtime budget purely for a smooth on-screen line;
        // the underlying curve is identical.
        path.Add(WorldXZToPixel(rect, pts[0]));
        for (int seg = 0; seg < segCount; seg++)
        {
            int i2 = (seg + 1) % n;
            if (n >= 3 && !zone.SegmentIsStraight(seg))
            {
                const int samplesPerSeg = 16;
                for (int s = 1; s <= samplesPerSeg; s++)
                    path.Add(WorldXZToPixel(rect, zone.SamplePath(seg, (float)s / samplesPerSeg)));
            }
            else
            {
                path.Add(WorldXZToPixel(rect, pts[i2]));
            }
        }
        return path;
    }

    // Chevrons spaced along the sampled band, pointing in node order — the flow direction the
    // runtime path UV scrolls along. Drawn on top of the band and node markers.
    static void DrawZoneFlowArrows(List<Vector2> pixelPath, float rpx)
    {
        if (pixelPath == null || pixelPath.Count < 2) return;

        float spacing = Mathf.Max(rpx * 3f, 28f);
        float size    = Mathf.Clamp(rpx * 0.8f, 5f, 16f);
        Handles.color = new Color(0f, 0f, 0f, 0.6f);

        float next        = spacing * 0.5f;  // start half an interval in so path ends stay clean
        float accumulated = 0f;
        for (int i = 0; i < pixelPath.Count - 1; i++)
        {
            Vector2 a   = pixelPath[i];
            Vector2 b   = pixelPath[i + 1];
            float   len = Vector2.Distance(a, b);
            if (len < 0.0001f) continue;
            Vector2 dir = (b - a) / len;

            while (next <= accumulated + len)
            {
                Vector2 p     = a + dir * (next - accumulated);
                Vector2 perp  = new Vector2(-dir.y, dir.x);
                Vector2 tip   = p + dir * size;
                Vector2 backL = p - dir * size * 0.4f + perp * size * 0.7f;
                Vector2 backR = p - dir * size * 0.4f - perp * size * 0.7f;
                Handles.DrawAAConvexPolygon((Vector3)tip, (Vector3)backL, (Vector3)backR);
                next += spacing;
            }
            accumulated += len;
        }
    }

    GameObject GetDefaultSplineWallPrefab()
    {
        // Default to the procedural wall from the spline-wall prefab folder, else the first option.
        var opts = GetSplineWallPrefabOptions();
        foreach (var p in opts)
            if (p != null && p.name == "ProceduralSplineWall") return p;
        return opts.Count > 0 ? opts[0] : null;
    }

    // Every prefab under SplineWallPrefabFolder, cached (sorted by name) for the Type dropdown.
    List<GameObject> GetSplineWallPrefabOptions()
    {
        // Rebuild when null (fresh window / after reload) or empty (folder was empty last time, or a
        // prefab has since been added) — the scan is cheap for this small folder.
        if (_splineWallPrefabOptions == null || _splineWallPrefabOptions.Count == 0)
        {
            _splineWallPrefabOptions = new List<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { SplineWallPrefabFolder }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go == null) continue;
                // The folder also holds node-marker prefabs (ProceduralSplineNode) — those are wall
                // node posts, not selectable wall types, so keep them out of the Type dropdown.
                if (go.GetComponent<ProceduralSplineNode>() != null) continue;
                _splineWallPrefabOptions.Add(go);
            }
            _splineWallPrefabOptions.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        }
        return _splineWallPrefabOptions;
    }

    Color GetSplineWallColor(int pathIdx)
    {
        // Alternate black/white so multiple paths remain distinguishable without colour
        return (pathIdx % 2 == 0) ? Color.white : Color.black;
    }

    // ── Cube buildings ─────────────────────────────────────────
    // Footprint stored normalized to the arena (-0.5..0.5), same space as spline-wall nodes.
    // Click-drag creates a block; dragging a block's centre node moves it; the right-hand
    // panel edits width/length/height/depth after the fact.
    void HandleCubeBuildingInput(Rect rect, Event e)
    {
        if (loadedData.cubeBuildings == null)
            loadedData.cubeBuildings = new List<GridData.CubeBuilding>();

        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            int hit = PickCubeBuilding(rect, e.mousePosition);
            if (hit >= 0)
            {
                // Click anywhere inside a block → select it and begin moving it.
                BeginCubeMove(rect, hit, e.mousePosition);
            }
            else
            {
                // Empty space → rubber-band a new block.
                _isDraggingCubeBox   = true;
                _cubeDragStartNorm   = PixelToWorldXZ(rect, e.mousePosition);
                _cubeDragCurrentNorm = _cubeDragStartNorm;
            }
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0)
        {
            if (DragCubeMove(rect, e.mousePosition)) { e.Use(); Repaint(); }
            else if (_isDraggingCubeBox)
            {
                _cubeDragCurrentNorm = PixelToWorldXZ(rect, e.mousePosition);
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            if (_dragCubeCenterIndex >= 0)
            {
                _dragCubeCenterIndex = -1;
                e.Use();
            }
            else if (_isDraggingCubeBox)
            {
                _isDraggingCubeBox = false;
                Vector2 a = _cubeDragStartNorm, b = _cubeDragCurrentNorm;
                float w = Mathf.Abs(b.x - a.x);
                float l = Mathf.Abs(b.y - a.y);
                if (w > 0.005f && l > 0.005f) // ignore stray clicks
                {
                    Undo.RecordObject(loadedData, "Add Cube Building");
                    loadedData.cubeBuildings.Add(new GridData.CubeBuilding
                    {
                        center = (a + b) * 0.5f,
                        width  = w,
                        length = l,
                    });
                    _activeCubeIndex = loadedData.cubeBuildings.Count - 1;
                    EditorUtility.SetDirty(loadedData);
                }
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseDown && e.button == 1 && rect.Contains(e.mousePosition))
        {
            int hit = PickCubeBuilding(rect, e.mousePosition);
            if (hit >= 0)
            {
                Undo.RecordObject(loadedData, "Delete Cube Building");
                loadedData.cubeBuildings.RemoveAt(hit);
                if (_activeCubeIndex == hit) _activeCubeIndex = -1;
                else if (_activeCubeIndex > hit) _activeCubeIndex--;
                EditorUtility.SetDirty(loadedData);
                e.Use();
                Repaint();
            }
        }
    }

    // Cube selection for the ⊕ Select tool: click inside a block to select + move it.
    // Runs after the spline/soul node pickers so precise node picks still win.
    void HandleSelectCubeBuildingInput(Rect rect, Event e)
    {
        if (loadedData?.cubeBuildings == null || loadedData.cubeBuildings.Count == 0) return;

        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            int hit = PickCubeBuilding(rect, e.mousePosition);
            if (hit >= 0)
            {
                // Single-selection: clear any prefab/node selection so only the block's marker shows
                // (BeginCubeMove clears the active spike). Matches the spike select path.
                ClearSelectState();
                BeginCubeMove(rect, hit, e.mousePosition);
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseDrag && e.button == 0)
        {
            if (DragCubeMove(rect, e.mousePosition)) { e.Use(); Repaint(); }
        }
        else if (e.type == EventType.MouseUp && e.button == 0 && _dragCubeCenterIndex >= 0)
        {
            _dragCubeCenterIndex = -1;
            e.Use();
        }
    }

    // Pixel radius around a block's centre node that counts as a hit. Large so the node
    // is an easy target even though it's drawn small.
    const float CubeNodePickRadius = 16f;

    // Block whose centre node is nearest the pixel (within CubeNodePickRadius), or -1.
    int PickCubeBuilding(Rect rect, Vector2 mouse)
    {
        if (loadedData?.cubeBuildings == null) return -1;
        int hit = -1; float best = CubeNodePickRadius;
        for (int i = 0; i < loadedData.cubeBuildings.Count; i++)
        {
            float d = Vector2.Distance(WorldXZToPixel(rect, loadedData.cubeBuildings[i].center), mouse);
            if (d < best) { best = d; hit = i; }
        }
        return hit;
    }

    void BeginCubeMove(Rect rect, int index, Vector2 mouse)
    {
        _activeSpikeIndex    = -1;   // block, spike and orb selection are mutually exclusive
        _activeOrbIndex      = -1;
        _activeCubeIndex     = index;
        _dragCubeCenterIndex = index;
        _isDraggingCubeBox   = false;
        // Offset keeps the grab point fixed under the cursor instead of snapping centre to it.
        _cubeDragOffsetNorm  = loadedData.cubeBuildings[index].center - PixelToWorldXZ(rect, mouse);
        Undo.RecordObject(loadedData, "Move Cube Building");
    }

    bool DragCubeMove(Rect rect, Vector2 mouse)
    {
        if (_dragCubeCenterIndex < 0 || _dragCubeCenterIndex >= loadedData.cubeBuildings.Count)
            return false;
        loadedData.cubeBuildings[_dragCubeCenterIndex].center =
            PixelToWorldXZ(rect, mouse) + _cubeDragOffsetNorm;
        EditorUtility.SetDirty(loadedData);
        return true;
    }

    // Bottom-layer pass: the block fills + outlines only. Drawn before the cell loop so
    // blocks act as a foundation beneath prefabs, spline walls and every other overlay.
    void DrawCubeBuildingFoundations(Rect rect)
    {
        if (loadedData?.cubeBuildings == null) return;

        float gridPx = EffCell * GridSize;
        Handles.BeginGUI();
        for (int i = 0; i < loadedData.cubeBuildings.Count; i++)
        {
            var  b      = loadedData.cubeBuildings[i];
            bool active = (_drawCubeBuilding || drawSelect) && i == _activeCubeIndex;

            float hwPx = b.width  * 0.5f * gridPx;
            float hlPx = b.length * 0.5f * gridPx;
            Vector2 c  = WorldXZToPixel(rect, b.center);
            Rect r     = new Rect(c.x - hwPx, c.y - hlPx, hwPx * 2f, hlPx * 2f);

            EditorGUI.DrawRect(r, active ? new Color(0.32f, 0.34f, 0.40f, 0.85f)
                                         : new Color(0.20f, 0.20f, 0.23f, 0.80f));
            DrawRectOutline(r, active ? new Color(0.5f, 0.8f, 1f, 0.95f)
                                      : new Color(0f, 0f, 0f, 0.6f));
        }
        Handles.EndGUI();
    }

    // Top-layer pass: the selection node + block number, plus the in-progress rubber-band.
    // Kept on top of everything so nodes stay visible and pickable and numbers stay readable.
    void DrawCubeBuildingOverlay(Rect rect)
    {
        if (loadedData == null) return;

        // In-progress rubber-band while dragging out a new block.
        if (_drawCubeBuilding && _isDraggingCubeBox)
        {
            Vector2 p0 = WorldXZToPixel(rect, _cubeDragStartNorm);
            Vector2 p1 = WorldXZToPixel(rect, _cubeDragCurrentNorm);
            Rect rb = Rect.MinMaxRect(Mathf.Min(p0.x, p1.x), Mathf.Min(p0.y, p1.y),
                                      Mathf.Max(p0.x, p1.x), Mathf.Max(p0.y, p1.y));
            EditorGUI.DrawRect(rb, new Color(0.3f, 0.3f, 0.34f, 0.55f));
            DrawRectOutline(rb, new Color(0.85f, 0.85f, 0.9f, 0.9f));
        }

        if (loadedData.cubeBuildings == null) return;

        GUIStyle blockNumStyle = new GUIStyle(EditorStyles.boldLabel)
        { alignment = TextAnchor.MiddleCenter, fontSize = 12, normal = { textColor = Color.black } };
        for (int i = 0; i < loadedData.cubeBuildings.Count; i++)
        {
            var  b      = loadedData.cubeBuildings[i];
            bool active = (_drawCubeBuilding || drawSelect) && i == _activeCubeIndex;

            Vector2 c  = WorldXZToPixel(rect, b.center);

            // No default circle on blocks anymore: only the SELECTED block wears a marker — the shared
            // white select circle (sized by the UI setting, floored so the number still fits) — and it's
            // suppressed while a prefab/node selection owns the marker so the two never draw at once.
            bool selected = active && _currentSelection.type == SelectionType.None;
            if (selected)
            {
                float nr = Mathf.Max(EffCell * _selectionCircleFactor, 13f);
                DrawMarker(c, nr, _style.selection);
                if (!_style.selection.outline)   // crisp black edge behind the number when filled
                {
                    Handles.color = Color.black;
                    Handles.DrawWireDisc((Vector3)c, Vector3.forward, nr);
                }
            }

            // Block number (matches the "Block N" entries in the Cube Buildings panel). White so it
            // reads on the dark block with no disc behind it; black on the light selection circle.
            string  numLabel = (i + 1).ToString();
            blockNumStyle.normal.textColor = selected ? Color.black : Color.white;
            Vector2 numSize  = blockNumStyle.CalcSize(new GUIContent(numLabel));
            Handles.Label(new Vector2(c.x - numSize.x * 0.5f, c.y - numSize.y * 0.5f), numLabel, blockNumStyle);
        }
    }

    void DrawRectOutline(Rect r, Color col)
    {
        Handles.color = col;
        Vector3[] pts =
        {
            new Vector3(r.xMin, r.yMin), new Vector3(r.xMax, r.yMin),
            new Vector3(r.xMax, r.yMax), new Vector3(r.xMin, r.yMax),
            new Vector3(r.xMin, r.yMin),
        };
        Handles.DrawAAPolyLine(2f, pts);
    }

    void DrawCubeBuildingsSection()
    {
        EditorGUILayout.Space();
        _showCubeBuildings = EditorGUILayout.Foldout(_showCubeBuildings, "Cube Buildings", true, EditorStyles.foldoutHeader);
        if (!_showCubeBuildings) return;

        if (loadedData.cubeBuildings == null)
            loadedData.cubeBuildings = new List<GridData.CubeBuilding>();

        EditorGUILayout.HelpBox("Pick the ▦ Blocks tool, then drag out a box on the grid. Footprint is normalized to the arena width; height/depth are world units.", MessageType.None);

        float aw = loadedData.WorldArenaWidth;

        int toRemove = -1;
        for (int i = 0; i < loadedData.cubeBuildings.Count; i++)
        {
            var  b        = loadedData.cubeBuildings[i];
            bool isActive = i == _activeCubeIndex;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header row: select button, compact info (when collapsed), delete.
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = isActive ? new Color(0.5f, 0.8f, 1f) : Color.white;
            if (GUILayout.Button($"Block {i + 1}", GUILayout.Width(72)))
            {
                // Toggle selection: click again to collapse back to the info line.
                if (isActive) { _activeCubeIndex = -1; }
                else          { _activeCubeIndex = i; _drawCubeBuilding = true; } // overlay highlights it
            }
            GUI.backgroundColor = Color.white;

            if (!isActive)
            {
                string info = aw > 0f
                    ? $"{b.width * aw:0.##}×{b.length * aw:0.##} m · H {b.heightAboveWater:0.##} · D {b.depthBelowWater:0.##}"
                    : $"{b.width:0.###}×{b.length:0.###} · H {b.heightAboveWater:0.##} · D {b.depthBelowWater:0.##}";
                EditorGUILayout.LabelField(info, EditorStyles.miniLabel);
            }
            else
            {
                GUILayout.FlexibleSpace();
            }

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(24))) toRemove = i;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // Adjustable settings only for the selected block.
            if (isActive)
            {
                EditorGUI.BeginChangeCheck();
                float width  = EditorGUILayout.Slider("Width (norm)",  b.width,  0.005f, 1f);
                float length = EditorGUILayout.Slider("Length (norm)", b.length, 0.005f, 1f);
                float height = EditorGUILayout.FloatField("Height (above water)", b.heightAboveWater);
                float depth  = EditorGUILayout.FloatField("Depth (below water)",  b.depthBelowWater);
                Vector2 center = EditorGUILayout.Vector2Field("Centre X/Z (norm)", b.center);
                bool stepped = EditorGUILayout.Toggle(
                    new GUIContent("Stepped top", "Build as a stepped-rooftop building using a random preset from Resources/Buildings at spawn."),
                    b.steppedTop);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(loadedData, "Edit Cube Building");
                    b.width            = Mathf.Max(0.001f, width);
                    b.length           = Mathf.Max(0.001f, length);
                    b.heightAboveWater = height;
                    b.depthBelowWater  = Mathf.Max(0f, depth);
                    b.center           = new Vector2(Mathf.Clamp(center.x, -0.5f, 0.5f),
                                                     Mathf.Clamp(center.y, -0.5f, 0.5f));
                    b.steppedTop       = stepped;
                    EditorUtility.SetDirty(loadedData);
                    Repaint();
                }

                if (aw > 0f)
                    EditorGUILayout.LabelField($"≈ {b.width * aw:0.##} × {b.length * aw:0.##} m footprint · {b.heightAboveWater:0.##} m tall", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        if (toRemove >= 0)
        {
            Undo.RecordObject(loadedData, "Delete Cube Building");
            loadedData.cubeBuildings.RemoveAt(toRemove);
            if (_activeCubeIndex == toRemove) _activeCubeIndex = -1;
            else if (_activeCubeIndex > toRemove) _activeCubeIndex--;
            EditorUtility.SetDirty(loadedData);
        }

        if (GUILayout.Button("+ Add Block (centred)"))
        {
            Undo.RecordObject(loadedData, "Add Cube Building");
            loadedData.cubeBuildings.Add(new GridData.CubeBuilding());
            _activeCubeIndex  = loadedData.cubeBuildings.Count - 1;
            _drawCubeBuilding = true;
            EditorUtility.SetDirty(loadedData);
        }
    }

    // ─────────────────────────────────────────────
    // PROCEDURAL SPIKES
    // Rocks stored the same way as blocks — centre normalized to the arena, radii as a
    // fraction of the arena width — so they move, scale and save exactly like blocks do.
    // Drag out from a point to set how wide the rock sits in the water; the other three
    // radii come in proportioned to it and are shaped afterwards in the panel.
    // ─────────────────────────────────────────────

    // Pixel radius around a spike's centre node that counts as a hit.
    const float SpikeNodePickRadius = 14f;


    // Shape presets on offer, rescanned from Resources/Spikes each time the panel draws so a
    // preset saved in the Spike Studio shows up without reopening this window.
    SpikeShapePreset[] _spikePresets     = new SpikeShapePreset[0];
    string[]           _spikePresetNames = { "Default shape" };

    void RefreshSpikePresets()
    {
        var guids = AssetDatabase.FindAssets("t:SpikeShapePreset", new[] { SpikeShapePreset.AssetFolder });
        var found = new List<SpikeShapePreset>(guids.Length);
        foreach (var g in guids)
        {
            var p = AssetDatabase.LoadAssetAtPath<SpikeShapePreset>(AssetDatabase.GUIDToAssetPath(g));
            if (p != null) found.Add(p);
        }
        found.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));

        _spikePresets     = found.ToArray();
        _spikePresetNames = new string[found.Count + 1];
        _spikePresetNames[0] = "Default shape";
        for (int i = 0; i < found.Count; i++) _spikePresetNames[i + 1] = found[i].name;
    }

    // First preset in the folder, so a rock dropped on the grid wears a real shape rather than
    // the built-in default. Null when the folder is empty, which the profile handles.
    SpikeShapePreset FirstSpikePreset()
    {
        if (_spikePresets.Length == 0) RefreshSpikePresets();
        return _spikePresets.Length > 0 ? _spikePresets[0] : null;
    }

    // Arena width used to turn normalized grid positions into the world units the shapes live in.
    float SpikeArenaWidth() =>
        loadedData != null && loadedData.WorldArenaWidth > 0f
            ? loadedData.WorldArenaWidth : 12f;


    void HandleSpikeInput(Rect rect, Event e)
    {
        if (loadedData.proceduralSpikes == null)
            loadedData.proceduralSpikes = new List<GridData.ProceduralSpike>();

        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            int hit = PickSpike(rect, e.mousePosition);
            if (hit >= 0)
            {
                BeginSpikeMove(rect, hit, e.mousePosition);
            }
            else
            {
                // Empty water → drag outward from here to size a new rock.
                _isDraggingSpike     = true;
                _spikeDragStartNorm  = PixelToWorldXZ(rect, e.mousePosition);
                _spikeDragCurrentNorm = _spikeDragStartNorm;
            }
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0)
        {
            if (DragSpikeMove(rect, e.mousePosition)) { e.Use(); Repaint(); }
            else if (_isDraggingSpike)
            {
                _spikeDragCurrentNorm = PixelToWorldXZ(rect, e.mousePosition);
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseUp && e.button == 0)
        {
            if (_dragSpikeCenterIndex >= 0)
            {
                _dragSpikeCenterIndex = -1;
                e.Use();
            }
            else if (_isDraggingSpike)
            {
                _isDraggingSpike = false;
                float r = Vector2.Distance(_spikeDragStartNorm, _spikeDragCurrentNorm);
                if (r > 0.004f) // ignore stray clicks
                {
                    Undo.RecordObject(loadedData, "Add Spike");
                    var preset = FirstSpikePreset();
                    loadedData.proceduralSpikes.Add(new GridData.ProceduralSpike
                    {
                        center = _spikeDragStartNorm,
                        preset = preset,
                        // How far you dragged is the rock's radius at the water; the preset
                        // supplies the shape, so the drag becomes its size multiplier.
                        scale  = SpikeScaleForWaterRadius(preset, r * SpikeArenaWidth()),
                    });
                    _activeSpikeIndex = loadedData.proceduralSpikes.Count - 1;
                    EditorUtility.SetDirty(loadedData);
                }
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseDown && e.button == 1 && rect.Contains(e.mousePosition))
        {
            // Right-click a rock toggles whether the creepy guy can climb it (Del still removes it).
            int hit = PickSpike(rect, e.mousePosition);
            if (hit >= 0 && loadedData.proceduralSpikes[hit] != null)
            {
                Undo.RecordObject(loadedData, "Toggle Spike Climbable");
                loadedData.proceduralSpikes[hit].climbable = !loadedData.proceduralSpikes[hit].climbable;
                _activeSpikeIndex = hit;
                EditorUtility.SetDirty(loadedData);
                e.Use();
                Repaint();
            }
        }
    }

    // Spike selection for the ⊕ Select tool, matching the block equivalent.
    void HandleSelectSpikeInput(Rect rect, Event e)
    {
        if (loadedData?.proceduralSpikes == null || loadedData.proceduralSpikes.Count == 0) return;

        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            int hit = PickSpike(rect, e.mousePosition);
            if (hit >= 0)
            {
                // Selecting a spike is a single-selection: clear any prefab/node selection so only
                // the spike's marker shows (and Delete targets the spike, not a stale selection).
                ClearSelectState();
                BeginSpikeMove(rect, hit, e.mousePosition);
                e.Use();
                Repaint();
            }
        }
        else if (e.type == EventType.MouseDrag && e.button == 0)
        {
            if (DragSpikeMove(rect, e.mousePosition)) { e.Use(); Repaint(); }
        }
        else if (e.type == EventType.MouseUp && e.button == 0 && _dragSpikeCenterIndex >= 0)
        {
            _dragSpikeCenterIndex = -1;
            e.Use();
        }
    }

    // Free-orb selection + drag for the ⊕ Select tool.
    void HandleSelectOrbInput(Rect rect, Event e)
    {
        if (loadedData?.orbPositions == null || loadedData.orbPositions.Count == 0) return;

        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            int hit = PickOrb(rect, e.mousePosition);
            if (hit >= 0)
            {
                ClearSelectState();                          // single-selection
                _activeSpikeIndex = -1; _activeCubeIndex = -1;
                _activeOrbIndex = hit; _dragOrbIndex = hit;
                Undo.RecordObject(loadedData, "Move Orb");
                e.Use(); Repaint();
            }
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && _dragOrbIndex >= 0
                 && _dragOrbIndex < loadedData.orbPositions.Count)
        {
            loadedData.orbPositions[_dragOrbIndex] = PixelToWorldXZ(rect, e.mousePosition);
            EditorUtility.SetDirty(loadedData);
            e.Use(); Repaint();
        }
        else if (e.type == EventType.MouseUp && e.button == 0 && _dragOrbIndex >= 0)
        {
            _dragOrbIndex = -1;
            e.Use();
        }
    }

    // Orb nearest the pixel within a pick radius, or -1.
    int PickOrb(Rect rect, Vector2 mouse)
    {
        if (loadedData?.orbPositions == null) return -1;
        float best = Mathf.Max(EffCell * 0.5f, 10f);
        int hit = -1;
        for (int i = 0; i < loadedData.orbPositions.Count; i++)
        {
            float d = Vector2.Distance(WorldXZToPixel(rect, loadedData.orbPositions[i]), mouse);
            if (d < best) { best = d; hit = i; }
        }
        return hit;
    }

    // Spike whose centre node is nearest the pixel (within SpikeNodePickRadius), or -1.
    int PickSpike(Rect rect, Vector2 mouse)
    {
        if (loadedData?.proceduralSpikes == null) return -1;
        int hit = -1; float best = SpikeNodePickRadius;
        for (int i = 0; i < loadedData.proceduralSpikes.Count; i++)
        {
            var s = loadedData.proceduralSpikes[i];
            if (s == null) continue;
            float d = Vector2.Distance(WorldXZToPixel(rect, s.center), mouse);
            if (d < best) { best = d; hit = i; }
        }
        return hit;
    }

    // Scale that makes a preset sit `worldRadius` wide at the waterline.
    static float SpikeScaleForWaterRadius(SpikeShapePreset preset, float worldRadius)
    {
        var   cfg  = preset != null ? preset.config : new SpikeShapeConfig();
        float baseR = Mathf.Max(0.0001f, cfg.radiusWaterline);
        return Mathf.Clamp(worldRadius / baseR, 0.05f, 20f);
    }

    void BeginSpikeMove(Rect rect, int index, Vector2 mouse)
    {
        _activeCubeIndex      = -1;   // spike, block and orb selection are mutually exclusive
        _activeOrbIndex       = -1;
        _activeSpikeIndex     = index;
        _dragSpikeCenterIndex = index;
        _isDraggingSpike      = false;
        _spikeDragOffsetNorm  = loadedData.proceduralSpikes[index].center - PixelToWorldXZ(rect, mouse);
        Undo.RecordObject(loadedData, "Move Spike");
    }

    bool DragSpikeMove(Rect rect, Vector2 mouse)
    {
        if (_dragSpikeCenterIndex < 0 || _dragSpikeCenterIndex >= loadedData.proceduralSpikes.Count)
            return false;
        loadedData.proceduralSpikes[_dragSpikeCenterIndex].center =
            PixelToWorldXZ(rect, mouse) + _spikeDragOffsetNorm;
        EditorUtility.SetDirty(loadedData);
        return true;
    }

    // Top-layer pass, in three sweeps so they stack properly across neighbouring rocks: the
    // circle of water each spike occupies, then every spike's side-on shape, then the centre
    // nodes and numbers. Grid Designer only — the Map UI draws the shape alone.
    void DrawSpikeOverlay(Rect rect)
    {
        if (loadedData == null) return;

        float gridPx = EffCell * GridSize;

        // In-progress drag while sizing a new rock — the shape it will wear, growing under the
        // cursor, so you're placing the actual rock rather than an abstract radius.
        if (_drawSpike && _isDraggingSpike)
        {
            Vector2 c    = WorldXZToPixel(rect, _spikeDragStartNorm);
            float   norm = Vector2.Distance(_spikeDragStartNorm, _spikeDragCurrentNorm);
            var     pre  = FirstSpikePreset();
            var     pcfg = pre != null ? pre.config : new SpikeShapeConfig();
            float   psc  = SpikeScaleForWaterRadius(pre, norm * SpikeArenaWidth());

            SpikeSilhouetteGUI.Draw(pcfg, psc, c, gridPx / SpikeArenaWidth(),
                                    belowDepth: pcfg.heightAboveWater * psc * 0.3f,
                                    columns: _spikeDisplayResolution, steps: _spikeDisplayResolution);
        }

        if (loadedData.proceduralSpikes == null) return;

        // Silhouettes are drawn at true world scale, so a tall rock reads as tall against a
        // squat one, and two rocks placed close show whether the boat can get between them.
        float pxPerUnit = gridPx / SpikeArenaWidth();

        GUIStyle numStyle = new GUIStyle(EditorStyles.boldLabel)
        { alignment = TextAnchor.MiddleCenter, fontSize = 10 };

        for (int i = 0; i < loadedData.proceduralSpikes.Count; i++)
        {
            var s = loadedData.proceduralSpikes[i];
            if (s == null) continue;
            bool active = (_drawSpike || drawSelect) && i == _activeSpikeIndex;

            Vector2 c = WorldXZToPixel(rect, s.center);

            // The rock drawn as its actual shape, standing on its position with the waterline at
            // the node — the same read as the Map UI's spike icon, and the profile you authored.
            // No climbable tint: which rocks the creepy guy can use is already said by the green
            // hop-route lines, and saying it twice just costs the spikes their own look.
            var   cfg = s.Config;
            float sc  = s.EffectiveScale;

            // Every rock gets enough of a stub to sink into the water instead of stopping at a
            // hard line; the one being edited gets more of it. Never the full depth, or a field
            // of spikes reads as a field of their underwater halves.
            float above = cfg.heightAboveWater * sc;
            SpikeSilhouetteGUI.Draw(cfg, sc, c, pxPerUnit,
                                    belowDepth: Mathf.Min(cfg.depthBelowWater * sc,
                                                          above * (active ? 0.40f : 0.18f)),
                                    columns: _spikeDisplayResolution, steps: _spikeDisplayResolution);

            // No handle disc is drawn — the rock is its own handle. Picking and dragging still
            // work off SpikeNodePickRadius around this point, which was never the drawn size
            // anyway, so nothing about grabbing them changes.
            //
            // The number sits just under the waterline, on the dark sunken stub, with a shadow
            // behind it so it holds up over the pale part of a rock and over bare grid alike.
            // Selection shows in the number's colour now the disc has gone.
            // Selection marker — the SAME white opaque circle used for prefab/node selection, sized
            // by the shared UI setting (Settings ▸ Selection circle size). Shown for the active spike
            // whether it was picked with the ▲ Spikes or ⊕ Select tool, but suppressed while a
            // prefab/node selection owns the marker so the two never draw at once. Drawn first, so the
            // green climbable dot and the number still read on top of it.
            if (active && (_drawSpike || drawSelect) && _currentSelection.type == SelectionType.None)
                DrawMarker(c, Mathf.Max(EffCell * _selectionCircleFactor, 3f), _style.selection);

            // Only in the ▲ Spikes tool: mark the climbable rocks (so you can see which the creepy
            // guy can use) with a dot on centre (colour + fill/outline from the Climbable spike
            // appearance setting), mark the angel's perch rocks on their tips, and show the spike
            // numbers. All three are authoring aids, off otherwise.
            // The green climbable dot is part of the same "where can the creeper go" picture as the
            // hop routes, so the Creeper hop routes toggle hides it too.
            // Perch ranges as translucent washes, centred on the rock's position (where the boat
            // crosses them). Shown for EVERY perch when "Show perch points" is on, or — in the Spikes
            // tool — for the selected perch (or all, per the appearance setting). The talk ring only
            // shows when Talk is enabled on that perch.
            if (s.angelPerchPoint
                && (showPerchPoints || (_drawSpike && (active || !_style.angelRadiiOnSelectedOnly))))
            {
                float op = Mathf.Clamp01(_style.angelRadiiOpacity);
                DrawMarker(c, s.angelPerchRadius * pxPerUnit, _style.angelPerchRadius, op);
                if (s.angelTalkEnabled)
                    DrawMarker(c, s.angelTalkRadius * pxPerUnit, _style.angelTalkRadius, op);
            }

            if (_drawSpike)
            {
                if (s.climbable && showCreeperRoutes)
                    DrawMarker(c, Mathf.Max(EffCell * 0.18f, 5f), _style.spikeClimbable);

                // Perch rocks are marked on the TIP of the drawn silhouette rather than on centre,
                // because the tip is literally where the angel puts her feet — and it keeps the mark
                // clear of the climbable dot on a rock that is both. A talk-enabled perch gets its own
                // colour, since a place you can meet AND talk to her is the one that matters most.
                if (s.angelPerchPoint)
                    DrawAngelLandingCurve(s, c, pxPerUnit);

                    DrawMarker(new Vector2(c.x, c.y - above * pxPerUnit),
                               Mathf.Max(EffCell * 0.15f, 4f),
                               s.angelTalkEnabled ? _style.spikeAngelPriorityPerch : _style.spikeAngelPerch);

                string  numLabel = (i + 1).ToString();
                Vector2 numSize  = numStyle.CalcSize(new GUIContent(numLabel));
                Vector2 numAt    = new Vector2(c.x - numSize.x * 0.5f, c.y + 3f);

                numStyle.normal.textColor = new Color(0f, 0f, 0f, 0.65f);
                Handles.Label(numAt + Vector2.one, numLabel, numStyle);

                numStyle.normal.textColor = active ? new Color(1f, 0.95f, 0.5f) : new Color(0.92f, 0.92f, 0.95f);
                Handles.Label(numAt, numLabel, numStyle);
            }
        }
    }

    // The path she flies in along, drawn so it can be checked against the walls and blocks around
    // the rock before she ever flies it.
    //
    // She always arrives facing the boat, so the curve ROTATES to wherever you sail in from — there
    // is no one true path to draw. It is drawn here as if she came in facing the middle of the
    // arena, which is where the boat mostly is. Read it as the shape and the reach rather than as a
    // fixed route: the far end swings round the rock as the approach changes.
    void DrawAngelLandingCurve(GridData.ProceduralSpike s, Vector2 centrePx, float pxPerUnit)
    {
        float r = s.angelLandingCurveSize;
        if (r <= 0.001f) return;                     // straight in — nothing to draw

        // Heading at touchdown: toward the middle of the arena.
        Vector2 h = -s.center;
        if (h.sqrMagnitude < 1e-6f) h = Vector2.up;
        h.Normalize();

        // Same construction the angel uses: the circle sits one radius to the side of the rock,
        // and the curve is swept back a quarter turn from the touchdown point to find its start.
        Vector2 n         = new Vector2(h.y, -h.x);
        Vector2 arcCentre = n * r;                   // metres, relative to the rock
        float   endAngle  = Mathf.Atan2(-arcCentre.y, -arcCentre.x);
        float   entry     = endAngle + 90f * Mathf.Deg2Rad;

        const int steps = 24;
        var pts = new Vector3[steps + 1];
        for (int k = 0; k <= steps; k++)
        {
            float a  = Mathf.Lerp(entry, endAngle, k / (float)steps);
            var   pm = arcCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;   // metres from rock

            // Metres to pixels. The canvas puts +Z up the screen, so the vertical flips.
            pts[k] = new Vector3(centrePx.x + pm.x * pxPerUnit,
                                 centrePx.y - pm.y * pxPerUnit, 0f);
        }

        var st = _style.angelLandingCurve;
        Handles.color = st.color;
        Handles.DrawAAPolyLine(Mathf.Max(0.5f, st.width), pts);

        // A dot where the curve begins, so which end is the approach is never in doubt.
        DrawMarker(new Vector2(pts[0].x, pts[0].y), Mathf.Max(st.width * 1.2f, 3f), st);
    }

    // A slider whose travel is weighted toward the low end (power > 1), so small values are easy to
    // dial in while a wide top range stays reachable, paired with a box for typing an exact value.
    // The handle position is power-mapped but the number shown is the real value.
    public static float LowEndSlider(GUIContent label, float value, float min, float max, float power)
    {
        value = Mathf.Clamp(value, min, max);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(label);
            // Position along the bar: the value's fraction of the range, un-powered so equal drags
            // near the bottom cover smaller value steps than near the top.
            float t = Mathf.Pow(Mathf.InverseLerp(min, max, value), 1f / Mathf.Max(0.01f, power));
            t = GUILayout.HorizontalSlider(t, 0f, 1f);
            value = Mathf.Lerp(min, max, Mathf.Pow(Mathf.Clamp01(t), power));
            value = EditorGUILayout.FloatField(value, GUILayout.Width(52));
        }
        return Mathf.Clamp(value, min, max);
    }

    void DrawSpikesSection()
    {
        EditorGUILayout.Space();
        _showSpikes = EditorGUILayout.Foldout(_showSpikes, "Spikes", true, EditorStyles.foldoutHeader);
        if (!_showSpikes) return;

        if (loadedData.proceduralSpikes == null)
            loadedData.proceduralSpikes = new List<GridData.ProceduralSpike>();

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.HelpBox("Pick the ▲ Spikes tool, then drag out from a point on the grid — the drag " +
                                    "sets how big the rock is, the preset sets what shape it is. " +
                                    "Climbable fits the creepy guy's rings to that shape at spawn.", MessageType.None);
            if (GUILayout.Button("Spike\nStudio…", GUILayout.Width(64), GUILayout.Height(38)))
                EditorApplication.ExecuteMenuItem("Tools/Waves/Spike Studio");
        }

        RefreshSpikePresets();
        if (_spikePresets.Length == 0)
            EditorGUILayout.HelpBox($"No shape presets found in {SpikeShapePreset.AssetFolder}. " +
                                    "Spikes will use the default shape until you save one from the Spike Studio.",
                                    MessageType.Warning);

        int toRemove = -1;

        // Draw order: the selected spike first (pinned to the top of the list, expanded), then the
        // rest in index order. Only the display order changes — indices/removal are unaffected.
        var spikeOrder = new List<int>();
        if (_activeSpikeIndex >= 0 && _activeSpikeIndex < loadedData.proceduralSpikes.Count)
            spikeOrder.Add(_activeSpikeIndex);
        for (int k = 0; k < loadedData.proceduralSpikes.Count; k++)
            if (k != _activeSpikeIndex) spikeOrder.Add(k);

        foreach (int i in spikeOrder)
        {
            var  s        = loadedData.proceduralSpikes[i];
            if (s == null) continue;
            bool isActive = i == _activeSpikeIndex;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = isActive ? new Color(0.8f, 0.72f, 1f) : Color.white;
            if (GUILayout.Button($"Spike {i + 1}", GUILayout.Width(72)))
            {
                if (isActive) { _activeSpikeIndex = -1; }
                else          { _activeSpikeIndex = i; _drawSpike = true; }
            }
            GUI.backgroundColor = Color.white;

            if (!isActive)
            {
                var   cfg = s.Config;
                float sc  = s.EffectiveScale;
                string climb = s.climbable ? " · climbable" : "";
                string perch = s.angelPerchPoint
                    ? (s.angelPriorityPerch ? " · angel perch (priority)" : " · angel perch")
                    : "";
                string name  = s.preset != null ? s.preset.name : "default shape";
                EditorGUILayout.LabelField(
                    $"{name} · ⌀{cfg.radiusWaterline * 2f * sc:0.##} m · {cfg.heightAboveWater * sc:0.##} m tall{climb}{perch}",
                    EditorStyles.miniLabel);
            }
            else
            {
                GUILayout.FlexibleSpace();
            }

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("✕", GUILayout.Width(24))) toRemove = i;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            if (isActive)
            {
                // No shape diagram here — shaping a rock is the Spike Studio's job, and this
                // panel is only about which shape stands where.
                EditorGUI.BeginChangeCheck();

                // Which shape it wears. Listed from Resources/Spikes so saving one in the Spike
                // Studio makes it placeable here immediately, with no wiring.
                int cur = System.Array.IndexOf(_spikePresets, s.preset);
                int pick = EditorGUILayout.Popup(
                    new GUIContent("Shape preset", "Authored in the Spike Studio. Editing the preset restyles " +
                                                   "every rock using it."),
                    cur + 1, _spikePresetNames);
                SpikeShapePreset preset = pick <= 0 ? null : _spikePresets[pick - 1];

                float scale = LowEndSlider(
                    new GUIContent("Size", "Multiplier on the whole preset, so one shape furnishes boulders and " +
                                           "pebbles alike. 1 = the preset's own size. The slider is weighted hard " +
                                           "toward the low end for fine-tuning small rocks; type an exact value in the box."),
                    s.EffectiveScale, 0.05f, 6f, power: 5f);

                bool climbable = EditorGUILayout.Toggle(
                    new GUIContent("Climbable", "Fit the creepy guy's rings to this rock at spawn so he can surface on it, climb it and leap from it."),
                    s.climbable);

                bool angelPerch = EditorGUILayout.Toggle(
                    new GUIContent("Angel perch point", "Mark this rock's tip as a spot the angel can swoop down from her flight and land on. Off = she flies straight over it."),
                    s.angelPerchPoint);

                // The ranges, priority flag and talk settings only mean anything on a rock she can use.
                float  angelPerchRadius = s.angelPerchRadius;
                float  angelCurveSize   = s.angelLandingCurveSize;
                float  angelTalkRadius  = s.angelTalkRadius;
                bool   angelPriority    = s.angelPriorityPerch;
                bool   angelTalk        = s.angelTalkEnabled;
                string angelTalkText    = s.angelTalkText;
                if (angelPerch)
                {
                    EditorGUI.indentLevel++;

                    angelPriority = EditorGUILayout.Toggle(
                        new GUIContent("Priority perch", "She always comes down here the moment the boat enters the " +
                                                         "perch range — a place you can rely on meeting her. Off makes it " +
                                                         "a rock she is only WATCHING: she settles here now and then, when " +
                                                         "she happens to be looking for somewhere to land."),
                        angelPriority);

                    angelPerchRadius = EditorGUILayout.FloatField(
                        new GUIContent("Perch range (m)", "Sail inside this and she comes down onto this rock; sail back " +
                                                          "out of it and she leaves."),
                        angelPerchRadius);

                    angelCurveSize = EditorGUILayout.FloatField(
                        new GUIContent("Landing curve (m)", "Size of the curve she lands along, drawn on the canvas so you " +
                                                            "can keep it off the walls and blocks around this rock. She flies " +
                                                            "to where the curve begins, behind the rock, then rides it round " +
                                                            "and touches down facing the boat. 0 = straight at the rock."),
                        angelCurveSize);

                    // Talk is the decisive per-perch feature: off, she just perches; on, the talk
                    // camera + dialogue arm inside the talk range, and she says the line below.
                    angelTalk = EditorGUILayout.Toggle(
                        new GUIContent("Talk (AngelTalk)", "Arm the talk camera + dialogue on this perch. Off = she " +
                                                           "just perches here and the talk key does nothing."),
                        angelTalk);

                    if (angelTalk)
                    {
                        EditorGUI.indentLevel++;
                        angelTalkRadius = EditorGUILayout.FloatField(
                            new GUIContent("Talk range (m)", "Sail inside this, with her perched here, and the talk key " +
                                                             "starts a conversation. Kept inside the perch range."),
                            angelTalkRadius);

                        EditorGUILayout.LabelField(new GUIContent("What she says",
                            "Shown in the level's dialogue box when you talk to her here. Separate lines with " +
                            "a slash — she shows one at a time and the talk key steps through them, ending the " +
                            "conversation after the last. Blank = the camera still cuts to her and she talks, " +
                            "but no text comes up."));
                        angelTalkText = EditorGUILayout.TextArea(angelTalkText ?? "", GUILayout.MinHeight(42));

                        // What that string actually becomes, so the slashes can be checked without
                        // entering play mode. There is no character limit in the dialogue box — a
                        // line longer than the text object simply overflows it — so seeing the
                        // lengths here is the only warning you get.
                        var talkLines = SplitAngelTalkLines(angelTalkText);
                        if (talkLines.Count == 0)
                            EditorGUILayout.LabelField("No text — she talks, but silently.", EditorStyles.miniLabel);
                        else
                        {
                            EditorGUILayout.LabelField(
                                $"{talkLines.Count} line{(talkLines.Count == 1 ? "" : "s")}, one press each",
                                EditorStyles.miniLabel);
                            EditorGUI.indentLevel++;
                            for (int li = 0; li < talkLines.Count; li++)
                                EditorGUILayout.LabelField($"{li + 1}. {talkLines[li]}  ({talkLines[li].Length})",
                                                           EditorStyles.miniLabel);
                            EditorGUI.indentLevel--;
                        }
                        EditorGUI.indentLevel--;
                    }

                    EditorGUI.indentLevel--;
                }

                Vector2 center = EditorGUILayout.Vector2Field("Centre X/Z (norm)", s.center);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(loadedData, "Edit Spike");
                    s.preset    = preset;
                    s.scale     = Mathf.Max(0.01f, scale);
                    s.climbable = climbable;
                    s.angelPerchPoint    = angelPerch;
                    s.angelPriorityPerch = angelPriority;
                    s.angelPerchRadius   = Mathf.Max(0f, angelPerchRadius);
                    s.angelLandingCurveSize = Mathf.Max(0f, angelCurveSize);
                    // Clamped, not just warned about: a talk range reaching past the range that
                    // brought her here would arm the key for a boat she is already leaving.
                    s.angelTalkRadius    = Mathf.Clamp(angelTalkRadius, 0f, s.angelPerchRadius);
                    s.angelTalkEnabled   = angelTalk;
                    s.angelTalkText      = angelTalkText;
                    s.center    = new Vector2(Mathf.Clamp(center.x, -0.5f, 0.5f),
                                              Mathf.Clamp(center.y, -0.5f, 0.5f));
                    EditorUtility.SetDirty(loadedData);
                    Repaint();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(s.preset == null))
                        if (GUILayout.Button("Select preset asset"))
                            EditorGUIUtility.PingObject(s.preset);
                    if (GUILayout.Button("Edit in Spike Studio"))
                        EditorApplication.ExecuteMenuItem("Tools/Waves/Spike Studio");
                }
            }

            EditorGUILayout.EndVertical();
        }

        if (toRemove >= 0)
        {
            Undo.RecordObject(loadedData, "Delete Spike");
            loadedData.proceduralSpikes.RemoveAt(toRemove);
            if (_activeSpikeIndex == toRemove) _activeSpikeIndex = -1;
            else if (_activeSpikeIndex > toRemove) _activeSpikeIndex--;
            EditorUtility.SetDirty(loadedData);
        }

        if (GUILayout.Button("+ Add Spike (centred)"))
        {
            Undo.RecordObject(loadedData, "Add Spike");
            loadedData.proceduralSpikes.Add(new GridData.ProceduralSpike { preset = FirstSpikePreset() });
            _activeSpikeIndex = loadedData.proceduralSpikes.Count - 1;
            _drawSpike        = true;
            EditorUtility.SetDirty(loadedData);
        }
    }

    // Nodes are stored in normalised grid space (-0.5..0.5), independent of arena size.
    // Multiply by arenaWorldWidth at runtime to get world positions.
    Vector2 WorldXZToPixel(Rect gridRect, Vector2 normXZ)
    {
        float gridPx = EffCell * GridSize;
        return new Vector2(gridRect.center.x + normXZ.x * gridPx,
                           gridRect.center.y - normXZ.y * gridPx);
    }

    Vector2 PixelToWorldXZ(Rect gridRect, Vector2 pixel)
    {
        float gridPx = EffCell * GridSize;
        return new Vector2( (pixel.x - gridRect.center.x) / gridPx,
                           -(pixel.y - gridRect.center.y) / gridPx);
    }
}