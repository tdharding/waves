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
        // A zone is either the level's main soul-fish path (door A → door B) or a Sub-Zone —
        // a bowl/statue-owned tributary that adjoins a main path and merges in when unlocked.
        // Named for forward use in level-select / cross-level routing. Default keeps every
        // existing zone as a MainPath (no behaviour change).
        public enum ZoneRole { MainPath, SubZone }

        [Tooltip("MainPath = the level's soul-fish highway. SubZone = a bowl/statue tributary that adjoins a main path.")]
        public ZoneRole zoneRole = ZoneRole.MainPath;

        // ── Stable identity & junction (single-level now; the keys carry to cross-level later) ──
        [Tooltip("Stable unique id within this level. Assigned by the designer; referenced by junctions and (later) cross-level routing. 0 = unassigned (auto-filled on load).")]
        public int zoneId = 0;

        [Tooltip("SubZone only: the zoneId of the MainPath this tributary adjoins. 0 = not linked.")]
        public int adjoinZoneId = 0;

        [Tooltip("SubZone only: the node index on the adjoined MainPath where this tributary merges (normally a street-light node).")]
        public int adjoinNodeIndex = -1;

        [Tooltip("Pin this path's first and last nodes to arena entrances, so the river of souls runs " +
                 "door-to-door. The pinned nodes follow their entrance if its angle changes.")]
        public bool attachToEntrances = false;

        [Tooltip("MainPath only: entrance index (into GridData.entrances) that is door A / the path's start. -1 = unset.")]
        public int entryEntranceIndex = -1;

        [Tooltip("MainPath only: entrance index (into GridData.entrances) that is door B / the path's exit. -1 = unset.")]
        public int exitEntranceIndex = -1;

        [Tooltip("Reserved for cross-level routing (level-select). A stable key identifying where this path's souls come from. Unused in single-level play.")]
        public string externalSourceKey = "";

        [Tooltip("Reserved for cross-level routing (level-select). A stable key identifying where this path's souls continue to. Unused in single-level play.")]
        public string externalTargetKey = "";

        [Tooltip("LEGACY grid cell indices. Migrated into nodePositions on load; kept only for back-compat.")]
        public List<int> nodes = new List<int>();

        [Tooltip("Free node positions in normalized grid space (-0.5..0.5 from centre), same space as SplineWallPath. 1 node = circular area, 2+ = path.")]
        public List<Vector2> nodePositions = new List<Vector2>();

        [Tooltip("True = the path closes back on itself into a loop.")]
        public bool closedLoop = false;

        [Tooltip("LEGACY per-segment curve flags: index i = segment node[i]→node[i+1]. Superseded by " +
                 "nodeTension, which still falls back to these so old zones keep their exact shape.")]
        public List<bool> segmentCurved = new List<bool>();

        [Tooltip("Per-node curvature, 0..1. 0 = sharp corner, 0.5 = natural smoothing, 1 = very round. " +
                 "Missing entries fall back to the legacy segmentCurved flags.")]
        public List<float> nodeTension = new List<float>();

        [Tooltip("Scatter/path radius for spline knot generation and wave mask (fish swim-band width). For a SubZone tributary this is the SOURCE pool radius (around the bowl).")]
        public float radius = 0.5f;

        [Tooltip("SubZone tributary only: half-width (world units) of the connecting path band from the source to the junction, separate from the source pool radius. Small = thin river.")]
        public float pathWidth = 0.15f;

        [Tooltip("Number of spline knots to generate for fish swim path.")]
        public int knotCount = 8;

        [Tooltip("Max samples per curved segment when the path is densified at runtime. Higher = smoother curves on the water, but the wave mask's 40-point budget is SHARED across all zones + fish on the level, so lavish zones leave less for others. 0/legacy = default 6.")]
        public int curveResolution = 6;

        // Guards legacy zones (field absent → deserializes to 0) and any 0 back to the default.
        public int EffectiveCurveResolution => curveResolution > 0 ? Mathf.Clamp(curveResolution, 1, 16) : 6;

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

        // ── Street lights ────────────────────────────────────
        // A street light stands on a path node and gates zone progression: the zone is
        // revealed only up to the last lit light, ending in a circular fish pool around it.
        // Feeding a caught soul into the NEXT light (in path order) draws the zone onward.

        [System.Serializable]
        public class StreetLight
        {
            [Tooltip("Index into nodePositions where this light stands.")]
            public int nodeIndex;

            [Tooltip("World-unit radius of the circular fish pool (mask + swim loop) around this light.")]
            public float poolRadius = 1f;
        }

        public List<StreetLight> streetLights = new List<StreetLight>();

        // Lights in path order (ascending node index) — the numbering shown in the designer
        // and the order they must be fed in. Light #1 is lit from the start.
        public List<StreetLight> StreetLightsInOrder()
        {
            var sorted = streetLights != null ? new List<StreetLight>(streetLights) : new List<StreetLight>();
            sorted.Sort((a, b) => a.nodeIndex.CompareTo(b.nodeIndex));
            return sorted;
        }

        public StreetLight StreetLightAtNode(int nodeIdx) =>
            streetLights?.Find(l => l != null && l.nodeIndex == nodeIdx);

        // Unlike SplineWallPath (missing = curved), zones default missing entries to straight —
        // every zone authored before curve support must keep its rectilinear shape.
        public bool IsSegmentCurved(int seg) =>
            segmentCurved != null && seg >= 0 && seg < segmentCurved.Count && segmentCurved[seg];

        // ─────────────────────────────────────────────
        // PATH CURVATURE — the single source of truth
        //
        // Both the Grid Designer preview and the runtime densification sample through here, so the
        // painted mask and the fish's swim spline are the same curve by construction. Anything added
        // to this sampler shows up in both automatically.
        //
        // The curve is a Cardinal/Hermite spline whose tangent at each node is scaled by that node's
        // tension. Tension 0 at both ends of a segment yields tangents of zero, which collapses the
        // Hermite exactly to a straight line — so the old binary curved/straight system is just the
        // special case tension ∈ {0, 0.5}.
        // ─────────────────────────────────────────────

        /// <summary>Curvature at a node: 0 = sharp corner, 0.5 = natural, 1 = very round.</summary>
        public float NodeTension(int i)
        {
            if (nodeTension != null && i >= 0 && i < nodeTension.Count)
                return Mathf.Clamp01(nodeTension[i]);

            // Legacy fallback: a node is smoothed if either segment touching it was flagged curved,
            // at the 0.5 that reproduces the old Catmull-Rom exactly.
            bool inCurved  = IsSegmentCurved(i - 1);
            bool outCurved = IsSegmentCurved(i);
            return (inCurved || outCurved) ? 0.5f : 0f;
        }

        /// <summary>Position along segment `seg` (node[seg] → node[seg+1]) at local t (0..1).</summary>
        public Vector2 SamplePath(int seg, float t)
        {
            var pts = nodePositions;
            int n = pts?.Count ?? 0;
            if (n == 0) return Vector2.zero;
            if (n == 1) return pts[0];

            bool closed = closedLoop && n >= 3;
            int i1 = Mathf.Clamp(seg, 0, n - 1);
            int i2 = closed ? (seg + 1) % n : Mathf.Min(seg + 1, n - 1);
            int i0 = closed ? (seg - 1 + n) % n : Mathf.Max(seg - 1, 0);
            int i3 = closed ? (seg + 2) % n : Mathf.Min(seg + 2, n - 1);

            Vector2 p1 = pts[i1], p2 = pts[i2];
            Vector2 m1 = NodeTension(i1) * (pts[i2] - pts[i0]);
            Vector2 m2 = NodeTension(i2) * (pts[i3] - pts[i1]);

            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p1
                 + (t3 - 2f * t2 + t)       * m1
                 + (-2f * t3 + 3f * t2)     * p2
                 + (t3 - t2)                * m2;
        }

        /// <summary>True when a segment is dead straight, so it needs no subdivision.</summary>
        public bool SegmentIsStraight(int seg)
        {
            int n = nodePositions?.Count ?? 0;
            if (n < 2) return true;
            bool closed = closedLoop && n >= 3;
            int i1 = Mathf.Clamp(seg, 0, n - 1);
            int i2 = closed ? (seg + 1) % n : Mathf.Min(seg + 1, n - 1);
            return NodeTension(i1) <= 0.001f && NodeTension(i2) <= 0.001f;
        }

        /// <summary>
        /// How many samples this segment needs. Straight segments take one; curved ones scale with
        /// length and curvature, capped by the zone's curveResolution.
        /// </summary>
        public int SegmentSubdivisions(int seg)
        {
            if (SegmentIsStraight(seg)) return 1;

            int n = nodePositions.Count;
            bool closed = closedLoop && n >= 3;
            int i2 = closed ? (seg + 1) % n : Mathf.Min(seg + 1, n - 1);

            float len  = Vector2.Distance(nodePositions[Mathf.Clamp(seg, 0, n - 1)], nodePositions[i2]);
            float curl = Mathf.Max(NodeTension(Mathf.Clamp(seg, 0, n - 1)), NodeTension(i2));
            int   want = Mathf.CeilToInt(len * 40f * Mathf.Max(curl, 0.25f) * 2f);
            return Mathf.Clamp(want, 2, EffectiveCurveResolution);
        }

        // Segment count of the node path: one per gap between nodes, plus the closing segment on a loop.
        public int SegmentCount()
        {
            int n = nodePositions?.Count ?? 0;
            if (n < 2) return 0;
            return (closedLoop && n >= 3) ? n : n - 1;
        }

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
    // ARENA WATER MODIFIERS
    // Wall-mounted water-level exit pipes placed around the perimeter by angle
    // (same scheme as entrances/locks). Each is fed by a SoulFishInputTube whose
    // path is authored on the grid; when a soul arrives the pipe lowers the water.
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class ArenaWaterModifier
    {
        [Tooltip("Display label only.")]
        public string id = "watermod_0";

        [Tooltip("Exit-pipe prefab (WaterLevelModifierExitPipe) with a PrefabBaselineAlignment forward override.")]
        public GameObject prefab;

        [Tooltip("Degrees Y around the arena centre. 0 = forward (+Z), increases clockwise.")]
        public float perimeterAngle;

        [Tooltip("-1 = base layer. 0+ = index into GridData.tiers for Y height.")]
        public int tierSlot = -1;

        [Tooltip("Inward offset from the arena perimeter, in world units.")]
        public float spawnRadius = 0f;

        [Tooltip("Grid-cell waypoints (col,row) for the soul tube. node[0] = input-tube spawn cell, " +
                 "last node = the pipe's perimeter cell; middle nodes are draggable. Fewer than 2 = no tube.")]
        public List<UnityEngine.Vector2Int> tubePath = new List<UnityEngine.Vector2Int>();

        [Tooltip("Number of intermediate nodes auto-generated between the input tube and the pipe.")]
        public int tubeSubdivisions = 3;
    }

    public List<ArenaWaterModifier> arenaWaterModifiers = new List<ArenaWaterModifier>();

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
        public int        cellIndex;       // LEGACY/derived: kept in sync with `position` (nearest cell)
                                           // for back-compat consumers (links, map). `position` is authority.
        public GameObject prefab;
        public bool       isCircle;        // true = soul/overlay plane
        public bool       isWorldSpaceProp; // true = statue/world prop — skips grid rotation, uses baseline height

        [Tooltip("Free normalized position (-0.5..0.5 from arena centre), same space as spline/soul nodes. " +
                 "Authority for spawn + designer once freePlaced is set; cellIndex is kept in sync (nearest cell).")]
        public UnityEngine.Vector2 position;

        [Tooltip("False for legacy grid-bound placements; migrated to a free position (cell centre) on load.")]
        public bool freePlaced;

        // Migrate a legacy grid-bound placement to a free position at its cell centre. Idempotent.
        public void EnsureFreePosition()
        {
            if (freePlaced) return;
            position   = SoulZone.CellToNormalized(cellIndex);
            freePlaced = true;
        }

        // Keep the legacy cellIndex pointing at the nearest cell to the free position.
        public void SyncCellIndex() => cellIndex = NormalizedToCell(position);

        // Non-zero when this placement is a statue that owns a guarded soul-fish zone.
        // Stamped onto the spawned StatueBehaviour and matched by SoulZone.linkedStatueId.
        public int        statueId;

        [Tooltip("Shape for a spike rock carried by this prefab — the creepy guy brings his own " +
                 "starting rock, for one. Only used when the prefab has a ProceduralSpike on it. " +
                 "Unset falls back to the default shape so the rock still stands up.")]
        public SpikeShapePreset spikePreset;

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

    // Nearest grid cell to a normalized position (inverse of SoulZone.CellToNormalized).
    public static int NormalizedToCell(UnityEngine.Vector2 norm)
    {
        int col = Mathf.Clamp(Mathf.RoundToInt((norm.x + 0.5f) * GridSize - 0.5f), 0, GridSize - 1);
        int row = Mathf.Clamp(Mathf.RoundToInt((0.5f - norm.y) * GridSize - 0.5f), 0, GridSize - 1);
        return row * GridSize + col;
    }

    // One-time migration of legacy grid-bound placements (base + tiers) to free positions.
    // Safe to call every load — idempotent per placement via freePlaced.
    public void MigratePlacementPositions()
    {
        if (prefabPlacements != null)
            foreach (var p in prefabPlacements) p?.EnsureFreePosition();
        if (tiers != null)
            foreach (var t in tiers)
                if (t?.prefabPlacements != null)
                    foreach (var p in t.prefabPlacements) p?.EnsureFreePosition();
    }

    [System.Serializable]
    public struct LinkedPrefabPair
    {
        public int modifierCellIndex;
        public int modifierTierIndex; // -1 = base
        public int inputTubeCellIndex;
        public int inputTubeTierIndex; // -1 = base

        [Tooltip("Grid-cell waypoints (col,row) for the soul tube path from the input tube to the modifier. " +
                 "node[0] tracks the input-tube cell, the last node tracks the modifier cell, middle nodes are draggable. " +
                 "Empty/fewer than 2 = straight auto-join (legacy behaviour).")]
        public List<UnityEngine.Vector2Int> tubePath;

        [Tooltip("Number of intermediate nodes generated between the input tube and the modifier.")]
        public int tubeSubdivisions;
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
        public float         tileSpacing         = 0.09f;
        public GameObject    prefabOverride;             // null = use LevelSpawner.splineWallPrefab
        public GameObject    destructiblePrefabOverride; // prefab (with DestructibleWall) used for segments flagged destructible

        // Procedural wall dimensions (world units). Used when the tile prefab has a
        // ProceduralSplineWall component; ignored by legacy mesh prefabs.
        [Tooltip("Wall height above the waterline (world units). Per-path default; individual nodes can override via nodeHeights.")]
        public float         wallHeight          = 1f;
        [Tooltip("World-units the wall drops below the waterline so it appears bottomless. Whole-path setting.")]
        public float         depthBelowWater     = 7f;
        [Tooltip("Wall thickness across the path (world units). Whole-path setting; node columns are this × nodeSizeScale thick.")]
        public float         wallThickness       = 0.09f;
        [Tooltip("Optional per-node height overrides (world units). Entry <= 0 or missing uses wallHeight.")]
        public List<float>   nodeHeights         = new List<float>();
        [Tooltip("Node column size relative to the wall: node column height & thickness = wall value × this. 1.1 = 10% bigger. Designer is authoritative — overrides the node prefab's own scale.")]
        public float         nodeSizeScale       = 1.1f;
        [Tooltip("Overall scale of the whole path. Editing it in the Grid Designer resizes every node around the path centre (baked into node positions).")]
        public float         pathScale           = 1f;

        public bool IsSegmentCurved(int seg) =>
            segmentCurved != null && seg < segmentCurved.Count ? segmentCurved[seg] : true;

        public bool IsSegmentGap(int seg) =>
            segmentGap != null && seg < segmentGap.Count && segmentGap[seg];

        public bool IsSegmentDestructible(int seg) =>
            segmentDestructible != null && seg < segmentDestructible.Count && segmentDestructible[seg];

        // Effective height at node i: its override if positive, otherwise the path default.
        public float NodeHeight(int i)
        {
            float h = (nodeHeights != null && i >= 0 && i < nodeHeights.Count) ? nodeHeights[i] : 0f;
            return h > 0f ? h : wallHeight;
        }

        // Wall height interpolated along segment `seg` (node[seg]→node[seg+1]) at local t (0..1).
        public float HeightAt(int seg, float t)
        {
            int n = nodes != null ? nodes.Count : 0;
            if (n == 0) return wallHeight;
            int i0 = Mathf.Clamp(seg, 0, n - 1);
            int i1 = isClosed ? (seg + 1) % n : Mathf.Min(seg + 1, n - 1);
            return Mathf.Lerp(NodeHeight(i0), NodeHeight(i1), Mathf.Clamp01(t));
        }
    }

    public List<SplineWallPath> splineWallPaths = new List<SplineWallPath>();

    // ─────────────────────────────────────────────
    // CUBE BUILDINGS
    // Procedurally-generated rectangular blocks drawn in the Grid Designer.
    // Footprint is stored normalized to the arena (same -0.5..0.5 space as
    // SplineWallPath nodes); multiply by WorldArenaWidth at spawn for world size.
    // The block's top face sits `heightAboveWater` above the waterline and its
    // bottom face drops `depthBelowWater` beneath it so it appears bottomless.
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class CubeBuilding
    {
        [Tooltip("Footprint centre in normalized grid space (-0.5..0.5 from centre), same space as SplineWallPath nodes.")]
        public Vector2 center = Vector2.zero;

        [Tooltip("Footprint width (X) as a fraction of the arena width. Multiplied by WorldArenaWidth at spawn.")]
        public float width = 0.1f;

        [Tooltip("Footprint length (Z) as a fraction of the arena width. Multiplied by WorldArenaWidth at spawn.")]
        public float length = 0.1f;

        [Tooltip("World-units the top surface stands above the waterline.")]
        public float heightAboveWater = 2f;

        [Tooltip("World-units the bottom face drops below the waterline so the block appears bottomless.")]
        public float depthBelowWater = 5f;

        [Tooltip("Build this block as a stepped-rooftop building using a random preset from Resources/Buildings at spawn (instead of a plain box).")]
        public bool steppedTop = false;
    }

    public List<CubeBuilding> cubeBuildings = new List<CubeBuilding>();

    // ─────────────────────────────────────────────
    // PROCEDURAL SPIKES
    // Rocks placed in the Grid Designer and built as a mesh at spawn, instead of being
    // dropped in as a fixed prefab. This is only WHERE a rock stands and which shape it
    // wears — the shape itself lives in a SpikeShapePreset, authored in the Spike Studio
    // and shared between every rock that uses it, so restyling a preset restyles the field.
    //
    // A spike flagged climbable fits the creepy guy's three rings to its own silhouette at
    // spawn, so placing the rock is all it takes to make it climbable.
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class ProceduralSpike
    {
        [Tooltip("Centre in normalized grid space (-0.5..0.5 from centre), same space as CubeBuilding.center.")]
        public Vector2 center = Vector2.zero;

        [Tooltip("Shape this rock wears, authored in the Spike Studio. Presets live in Resources/Spikes. " +
                 "Unset falls back to the default shape so the rock still stands up.")]
        public SpikeShapePreset preset;

        [Tooltip("Size multiplier on the whole preset, so one shape can furnish boulders and pebbles alike. 1 = the preset's own size.")]
        public float scale = 1f;

        [Tooltip("Fit the creepy guy's climbing rings to this rock at spawn, making it one he can surface on, climb and leap from. Off = scenery he ignores.")]
        public bool climbable = false;

        /// <summary>The shape this rock wears, falling back to the defaults when no preset is set.</summary>
        public SpikeShapeConfig Config => preset != null ? preset.config : DefaultShape;

        static readonly SpikeShapeConfig DefaultShape = new SpikeShapeConfig();

        /// <summary>Effective scale — treats a legacy/unset 0 as 1, matching PrefabPlacement.scale.</summary>
        public float EffectiveScale => scale > 0.0001f ? scale : 1f;
    }

    public List<ProceduralSpike> proceduralSpikes = new List<ProceduralSpike>();
}