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
        [Tooltip("Ordered grid cell indices placed by designer. 1 node = circular area, 2+ = path.")]
        public List<int> nodes = new List<int>();

        [Tooltip("Scatter/path radius for spline knot generation and wave mask.")]
        public float radius = 3f;

        [Tooltip("Number of spline knots to generate for fish swim path.")]
        public int knotCount = 8;

        [Tooltip("Soul identities in this zone. Each entry spawns one fish mesh instance.")]
        public List<SoulData> souls = new List<SoulData>();
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
        public List<bool>    segmentCurved = new List<bool>(); // index i = curved flag for segment node[i]→node[i+1]; missing = true
        public float         tileSpacing   = 0.2f;
        public GameObject    prefabOverride; // null = use LevelSpawner.splineWallPrefab

        public bool IsSegmentCurved(int seg) =>
            segmentCurved != null && seg < segmentCurved.Count ? segmentCurved[seg] : true;
    }

    public List<SplineWallPath> splineWallPaths = new List<SplineWallPath>();
}