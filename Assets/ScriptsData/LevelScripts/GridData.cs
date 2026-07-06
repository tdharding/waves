using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "GridData", menuName = "WaveGrid/Grid Data")]
public class GridData : ScriptableObject
{
    public const int GridSize = 32;
    public const int CellCount = GridSize * GridSize;

    // ─────────────────────────────────────────────
    // LEVEL IDENTITY
    // ─────────────────────────────────────────────

    [Header("Level Identity")]
    [Tooltip("Unique ID for this level. Used to track completion count across sessions.")]
    public string levelID;
    public string displayName;

    // ─────────────────────────────────────────────
    // CAMERA
    // ─────────────────────────────────────────────

    [Header("Camera")]
    public CameraProfile cameraProfile;

    // ─────────────────────────────────────────────
    // WAVE PRESETS
    // ─────────────────────────────────────────────

    [Header("Wave Presets")]
    public WavePreset gameplayWavePreset;
    public WavePreset gongWavePreset;

    [Tooltip("Set at runtime by ExternalWaveModifier when a soul is slotted. Overrides gameplayWavePreset. Null = no override.")]
    public WavePreset runtimeWavePresetOverride;

    // ─────────────────────────────────────────────
    // SONAR GRID
    // ─────────────────────────────────────────────

    [Header("Sonar Grid")]
    [Tooltip("Sonar grid formation loaded on level spawn. Null = keep the scene's default sonar formation.")]
    public SonarGridType sonarGridType;

    // ─────────────────────────────────────────────
    // ENEMY
    // ─────────────────────────────────────────────

    [Header("Enemy")]
    [Tooltip("Assign an EnemyProfile to spawn an enemy on this level. Null = no enemy.")]
    public EnemyProfile enemyProfile;

    // ─────────────────────────────────────────────
    // TIME TRIAL
    // ─────────────────────────────────────────────

    [Header("Time Trial")]
    public bool isTimeTrial;

    [Tooltip("Time limit in seconds (only used if isTimeTrial is true).")]
    public float timeLimitSeconds = 60f;

    // ─────────────────────────────────────────────
    // PREFABS
    // ─────────────────────────────────────────────

    [Header("Prefabs (Index = Cell Value - 1)")]
    public List<GameObject> prefabs = new List<GameObject>();

    [Header("Sculpture Set Piece")]
    public GameObject sculptureSetPiecePrefab;

    // ─────────────────────────────────────────────
    // OPTIONAL START RITUAL
    // ─────────────────────────────────────────────

    [Header("Optional Start Ritual")]
    public LevelStartRitual startRitual;

    // ─────────────────────────────────────────────
    // REALITY LAYER
    // ─────────────────────────────────────────────

    public int[] cells = new int[CellCount];

    // ─────────────────────────────────────────────
    // SOUL PLANE LAYER
    // Reserved for non-soul objects on the soul plane.
    // Souls are now defined explicitly in soulSpawnPoints.
    // ─────────────────────────────────────────────

    public int[] overlayCells = new int[CellCount];

    // ─────────────────────────────────────────────
    // SOUL SPAWN POINTS (legacy — migrated to soulZones on load)
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class SoulSpawnPoint
    {
        [Tooltip("Grid cell index (0-1023) where this soul spawns.")]
        public int cellIndex;

        [Tooltip("The soul assigned to this spawn point. Set in Grid Designer.")]
        public SoulData soulData;
    }

    public List<SoulSpawnPoint> soulSpawnPoints = new List<SoulSpawnPoint>();

    // ─────────────────────────────────────────────
    // SOUL ZONES
    // Each zone is a SoulFishArea with 1+ designer-placed nodes.
    // 1 node = circular scatter area. 2+ nodes = path area.
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class SoulZone
    {
        [Tooltip("LEGACY grid cell indices. Migrated into nodePositions on load; kept only for back-compat.")]
        public List<int> nodes = new List<int>();

        [Tooltip("Free node positions in normalized grid space (-0.5..0.5 from centre), same space as SplineWallPath. 1 node = circular area, 2+ = path.")]
        public List<Vector2> nodePositions = new List<Vector2>();

        [Tooltip("True = the path closes back on itself into a loop.")]
        public bool closedLoop = false;

        [Tooltip("Scatter/path radius for spline knot generation and wave mask (fish swim-band width).")]
        public float radius = 0.5f;

        [Tooltip("Number of spline knots to generate for fish swim path.")]
        public int knotCount = 8;

        [Tooltip("Soul identities in this zone. Each entry spawns one fish mesh instance.")]
        public List<SoulData> souls = new List<SoulData>();

        // ── Statue link ──────────────────────────────────────
        [Tooltip("True when this zone is a ring auto-created around a statue. Fish can't be caught until the statue is destroyed.")]
        public bool statueGuarded = false;

        [Tooltip("Matches PrefabPlacement.statueId of the guarding statue. Only meaningful when statueGuarded is true.")]
        public int linkedStatueId = 0;

        [Tooltip("Normalized-grid radius of the circular route around the statue. Only used when statueGuarded.")]
        public float ringRadius = 0.08f;

        // ── Fish-bowl tower link ─────────────────────────────
        [Tooltip("True when this zone belongs to a FishBowlTower. The shoal container spawns aloft in " +
                 "the bowl and drops into the water when the tower is destroyed; fish become catchable on landing. " +
                 "The bowl height and swim radius are defined by the tower prefab's FishBowlTowerController — not stored here.")]
        public bool towerGuarded = false;

        // Back-compat: older assets stored grid-cell indices in `nodes`. Populate nodePositions
        // from them once so existing levels keep their zones. No-op after first migration.
        public void MigrateNodesIfNeeded()
        {
            if (nodePositions == null) nodePositions = new List<Vector2>();
            if (nodePositions.Count > 0) return;
            if (nodes == null || nodes.Count == 0) return;

            var src = new List<int>(nodes);
            // Old closed-loop convention: last node duplicated the first.
            if (src.Count >= 3 && src[src.Count - 1] == src[0]) { closedLoop = true; src.RemoveAt(src.Count - 1); }
            foreach (int cell in src)
                nodePositions.Add(CellToNormalized(cell));

            nodes.Clear(); // legacy list severed once migrated
        }

        // Grid cell index -> normalized grid coord (matches the designer's WorldXZToPixel space).
        public static Vector2 CellToNormalized(int cell)
        {
            int col = cell % GridSize;
            int row = cell / GridSize;
            return new Vector2((col + 0.5f) / GridSize - 0.5f,
                               0.5f - (row + 0.5f) / GridSize);
        }
    }

    public List<SoulZone> soulZones = new List<SoulZone>();

    public int GetTotalSoulCount()
    {
        if (soulZones == null) return 0;
        int total = 0;
        foreach (var zone in soulZones)
            total += zone.souls?.Count ?? 0;
        return total;
    }

    // ─────────────────────────────────────────────
    // ARENA PORTALS
    // Entrances placed around the perimeter by angle.
    // Each prefab has its pivot at the arena centre; rotating by
    // perimeterAngle around Y positions the door at the correct edge.
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class ArenaEntrance
    {
        [Tooltip("Display label only — not used for runtime matching (matching is index-based).")]
        public string id = "entrance_0";

        [Tooltip("Door prefab with pivot at arena centre. The radial offset is baked into the mesh.")]
        public GameObject prefab;

        [Tooltip("Optional soul-plane version of the door prefab. Null = no soul-plane door.")]
        public GameObject soulPrefab;

        [Tooltip("Degrees Y around the arena centre. 0 = forward (+Z), increases clockwise.")]
        public float perimeterAngle;

        [Tooltip("-1 = base layer. 0+ = index into GridData.tiers for Y height.")]
        public int tierSlot = -1;

        public float spawnRadius = 0f;

        [Header("Lock")]
        [Tooltip("If true, this entrance spawns locked (emission off) and requires a soul via the tube to unlock.")]
        public bool isLocked = false;

        [Tooltip("The DoorLockHub prefab to spawn at lockHubAngle when isLocked is true.")]
        public GameObject lockHubPrefab;

        [Tooltip("Perimeter angle (degrees Y) where the DoorLockHub spawns. 0 = forward (+Z).")]
        public float lockHubAngle;

        [Tooltip("Grid-cell waypoints (col, row) drawn in the Grid Designer. Converted to world positions at runtime to build the soul tube spline.")]
        public List<UnityEngine.Vector2Int> tubePath = new List<UnityEngine.Vector2Int>();

        [Tooltip("Number of intermediate nodes auto-generated between the first and last tube waypoints.")]
        public int tubeSubdivisions = 3;

        [Header("River Routing")]
        [Tooltip("Set automatically by LevelSelectArenaController at runtime. " +
                 "The RiverSegmentID.SegmentID of the level-select path that leads to this entrance.")]
        public string targetSegmentID;

        [Tooltip("Normalised spline progress (0-1) where the boat reappears on exit through this entrance.")]
        public float targetProgress;

        [Tooltip("Whether the target segment is the left-path branch.")]
        public bool targetIsLeftPath;

        [Tooltip("Whether the target segment is the right-path branch.")]
        public bool targetIsRightPath;
    }

    public List<ArenaEntrance> entrances = new List<ArenaEntrance>();

    // ─────────────────────────────────────────────
    // ORBS
    // ─────────────────────────────────────────────

    public List<int> orbCellIndices = new List<int>();

    // ─────────────────────────────────────────────
    // MODIFIERS
    // ─────────────────────────────────────────────

    public List<int> waterLevelModifierCellIndices = new List<int>();
    public List<int> waveModifierCellIndices       = new List<int>();

    // ─────────────────────────────────────────────
    // TIERS
    // Additional spawned layers at different Y offsets.
    // All tiers share the same prefab slots as the base layer.
    // ─────────────────────────────────────────────

    // Direct prefab placement — stores a prefab reference per cell
    // rather than going through the slot index system.
    [System.Serializable]
    public class PrefabPlacement
    {
        public int        cellIndex;
        public GameObject prefab;
        public bool       isCircle;        // true = soul/overlay plane
        public bool       isWorldSpaceProp; // true = statue/world prop — skips grid rotation, uses baseline height

        // Non-zero when this placement is a statue that owns a guarded soul-fish zone.
        // Stamped onto the spawned StatueBehaviour and matched by SoulZone.linkedStatueId.
        public int        statueId;

        // Uniform scale multiplier applied to the spawned instance. Driven by the
        // Grid Designer when the prefab has a PrefabBaselineAlignment scale radius.
        // 1 = prefab default. 0 (legacy/unset) is treated as 1 everywhere it is read.
        public float scale = 1f;

        // Per-modifier overrides for TypeB wave modifiers. When
        // overrideModifierSettings is false the spawned prefab keeps its own
        // default speed/frequency/ripple boost values.
        public bool  overrideModifierSettings;
        public float speedBoost;
        public float frequencyBoost;
        public float rippleDepthBoost;
    }

    public List<PrefabPlacement> prefabPlacements = new List<PrefabPlacement>();

    [System.Serializable]
    public struct LinkedPrefabPair
    {
        public int modifierCellIndex;
        public int modifierTierIndex; // -1 = base
        public int inputTubeCellIndex;
        public int inputTubeTierIndex; // -1 = base
    }

    public List<LinkedPrefabPair> linkedPairs = new List<LinkedPrefabPair>();

    [System.Serializable]
    public class GridTier
    {
        public string    name         = "Tier";
        public float     yOffset      = 5f;   // legacy fallback only
        public int       yOffsetSlot  = 0;    // index into TierConfig.offsets
        public int[]     cells        = new int[CellCount];
        public List<int> waterLevelModifierCellIndices = new List<int>();
        public List<int> waveModifierCellIndices       = new List<int>();
        public List<PrefabPlacement> prefabPlacements  = new List<PrefabPlacement>();
    }

    public List<GridTier> tiers = new List<GridTier>();

    // ─────────────────────────────────────────────
    // WHIRLPOOLS
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class WhirlpoolPoint
    {
        public int   cellIndex;
        public float radius = 5f;
    }

    public List<WhirlpoolPoint> whirlpools     = new List<WhirlpoolPoint>();
    [Range(0f, 20f)] public float whirlpoolDepth = 5f;
    [Range(0f, 10f)] public float whirlpoolSwirl = 2f;

    // ─────────────────────────────────────────────
    // EDITOR METADATA
    // ─────────────────────────────────────────────

    public List<string> slotNotes = new List<string>();
    public List<Color>  slotColors = new List<Color>();

    // ─────────────────────────────────────────────
    // ARENA
    // ─────────────────────────────────────────────

    [Tooltip("ArenaProfile asset for this level. Used directly by LevelSpawner — replaces the old arenaSize enum.")]
    public ArenaProfile arenaProfile;

    // ─────────────────────────────────────────────
    // SPLINE WALL PATHS
    // Free-floating spline paths that tile wall prefabs at runtime.
    // Nodes are world XZ positions (Vector2.x = world X, Vector2.y = world Z).
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class SplineWallPath
    {
        public List<Vector2> nodes         = new List<Vector2>();
        public bool          isClosed      = false;
        public List<bool>    segmentCurved       = new List<bool>(); // index i = curved flag for segment node[i]→node[i+1]; missing = true
        public List<bool>    segmentGap          = new List<bool>(); // index i = gap flag (no wall) for segment node[i]→node[i+1]; missing = false
        public List<bool>    segmentDestructible = new List<bool>(); // index i = use destructiblePrefabOverride for segment node[i]→node[i+1]; missing = false
        public float         tileSpacing         = 0.2f;
        public GameObject    prefabOverride;             // null = use LevelSpawner.splineWallPrefab
        public GameObject    destructiblePrefabOverride; // prefab (with DestructibleWall) used for segments flagged destructible

        public bool IsSegmentCurved(int seg) =>
            segmentCurved != null && seg < segmentCurved.Count ? segmentCurved[seg] : true;

        public bool IsSegmentGap(int seg) =>
            segmentGap != null && seg < segmentGap.Count && segmentGap[seg];

        public bool IsSegmentDestructible(int seg) =>
            segmentDestructible != null && seg < segmentDestructible.Count && segmentDestructible[seg];
    }

    public List<SplineWallPath> splineWallPaths = new List<SplineWallPath>();
}