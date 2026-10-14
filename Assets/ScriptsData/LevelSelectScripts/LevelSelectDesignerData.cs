using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "LevelSelectDesignerData", menuName = "Level Select/Designer Data")]
public class LevelSelectDesignerData : ScriptableObject
{
    public enum NodeType { Waypoint, JunctionSplit, ArenaEnd, ShopEnd }
    public enum SegmentType { MainRiver, PrimaryBranch, Secondary, Tertiary }

    /// <summary>
    /// What a path's river run is built as. A Structural River Run is the stone run with its flat
    /// rim. A Landscape Natural River Run is the same section wearing the landscape's material,
    /// with a rim that can be rounded and outer walls that can be left off, so the ground runs
    /// onto it.
    /// </summary>
    public enum RunType { StructuralRiverRun, LandscapeNaturalRiverRun }

    /// <summary>The four sides of a pool a river can come in on. North is +Z, east is +X.</summary>
    public enum Compass { North, East, South, West }

    /// <summary>
    /// A node a pool places on one of its compass points, so the river meeting it arrives square
    /// on to the rim and at the pool's own height. It is locked to the pool: it moves with it
    /// and cannot be dragged on its own.
    /// </summary>
    [Serializable]
    public class PoolLeadIn
    {
        public string  nodeId;
        public Compass compass;
    }

    [Serializable]
    public class LandscapeHillPoint
    {
        public string  id;
        public Vector2 positionXZ;
        public float   scale  = 5f;
        public float   height = 1f;

        [Tooltip("1 = smooth rounded hill. Lower flattens the top and steepens the sides; 0 is a " +
                 "plateau with near-vertical cliffs.")]
        [Range(0f, 1f)] public float smoothness = 1f;

        [Tooltip("Rocky noise across this hill's shape. 0 = none.")]
        [Range(0f, 1f)] public float noise = 0f;

        [Tooltip("Fog zone sheets bend over the ground as if this point weren't there. The " +
                 "landscape itself is unchanged.")]
        public bool omitFromFog;
    }

    [Serializable]
    public class DesignerNode
    {
        public string id;
        public Vector3 worldPosition;
        public NodeType type;
        // In-Between: the designer places this node itself, evenly between the nearest ordinary
        // nodes either side of it on its river (height too), so it can't be moved by hand.
        public bool inBetween;
        // Audio: the boat sailing into the box across the river here (built on Generate, sized
        // off the river) crossfades whatever is playing (the wind, or another node's music) into
        // musicClip. The wind never comes back.
        public bool      musicTrigger;
        public AudioClip musicClip;
        public bool      musicLooped = true;
        [Min(0f)] public float musicCrossfade = 4f;
        // Cinematic: the boat sailing into the same kind of box plays cinematicPreset through the
        // scene's CameraSequence (once per save unless the preset says otherwise).
        public bool                 cinematicTrigger;
        public CameraSequencePreset cinematicPreset;
        // How it starts: sailing into the box, or pressing the interact key in reach of the node
        // (the same prompt and reach as every other interact point).
        public CinematicStart cinematicStart  = CinematicStart.SailInto;
        public string         cinematicPrompt = "Look";
        [Min(0f)] public float cinematicReach = 3f;
        // Pin Cameras: the preset's cameras, orbit points and end cameras move with this node.
        // cinematicPinnedAt is where the node stood when they were last moved — the next move
        // shifts them by the difference.
        public bool    cinematicPinCameras;
        public Vector3 cinematicPinnedAt;
    }

    public enum CinematicStart { SailInto, PressKey }

    // A shot camera (a camera sequence preset's, known by its CameraSequenceLink id) clamped to
    // something on the map. One way only: when the target moves the camera moves the same amount
    // (position only); moving the camera leaves the target alone, and its new spot is simply its
    // new offset. targetAt is where the target stood when the camera last moved with it.
    [Serializable]
    public class CameraClamp
    {
        public string      cameraLink;
        public bool        on;
        public ClampTarget target;
        public string      targetId;          // node, wall, outpost, structure or spike id
        public int         targetIndex = -1;  // the wall node, for a wall tower
        public Vector3     targetAt;
    }

    public enum ClampTarget { Node, WallTower, Outpost, Structure, Spike }

    [Serializable]
    public class DesignerPath
    {
        public string pathId;
        [Tooltip("System-assigned and unique — the designer generates it and it can't be typed.")]
        public string segmentId;
        [Tooltip("The path's name in the world's lore. Free text, may repeat; shown beside the ID.")]
        public string loreName;
        public List<string> nodeIds = new();
        [FormerlySerializedAs("isTopPath")]    public bool isLeftPath;
        [FormerlySerializedAs("isBottomPath")] public bool isRightPath;
        public SegmentType segmentType;
        public string riverName;
        public bool leadsToArena;
        public bool arenaIsAtEnd;
        public bool tJunctionBidirectional;
        public string arenaGridDataGuid;
        public Color editorColor = Color.cyan;
        [Tooltip("Multiplier on Bezier tangent length. 1 = default smoothness, lower = tighter curves, higher = more sweeping.")]
        public float curveStrength = 1f;
        [Tooltip("Knots inserted per segment between nodes. Higher = spline follows path more closely.")]
        public int curveSubdivisions = 2;

        [Tooltip("Lets this path's river run shape the landscape around it.")]
        public bool landscapeInfluence;

        [Tooltip("Brings the landscape up to the rim top along the run's outer edge.")]
        public bool landscapeToOuterRim = true;

        [Tooltip("How far out past the run's outer edge the landscape is shaped, before it eases " +
                 "back to its own hills.")]
        [Min(0f)] public float influenceDistance = 10f;

        [Tooltip("How far the landscape is drawn up toward the rim top: 1 meets it, 0.5 goes " +
                 "halfway, 0 leaves the hills as they are.")]
        [Range(0f, 1f)] public float influenceStrength = 1f;

        [Tooltip("Nodes on this path where the influence is cancelled: the stretch of run nearest " +
                 "each one is cut only, never lifted.")]
        public List<string> influenceCancelledNodes = new();

        [Tooltip("What this path's river run is built as: the stone Structural River Run, or a " +
                 "Landscape Natural River Run in the landscape's material.")]
        public RunType runType = RunType.StructuralRiverRun;

        [Tooltip("Landscape Natural River Run only. 0 keeps the rim flat with square corners, 1 " +
                 "rounds it fully. With the outer walls off only the channel side rounds, so the " +
                 "outer edge stays level with the ground.")]
        [Range(0f, 1f)] public float rimRounding;

        [Tooltip("Landscape Natural River Run only. Off leaves the run without outer walls, so " +
                 "the landscape runs straight onto its rim. Needs Landscape Influence and " +
                 "Landscape to Outer Rim on.")]
        public bool outerWalls = true;
    }

    [Serializable]
    public class DesignerJunction
    {
        public string junctionId;
        public string nodeId;
        public List<string> pathIds = new();
        public string riverPathId;   // path the junction was clicked on (mid-stream)
        public string branchPathId;  // path extruded from the junction node (branch)

        [Tooltip("The lead-in nodes this junction placed beside it — one either side on the " +
                 "river running through, one on the branch — all at the junction's height.")]
        public List<JunctionLeadIn> leadIns = new();
    }

    /// <summary>
    /// A node a junction places next to it on each river that meets it, at the junction's own
    /// height: the two on the river running through stand on one straight line across the
    /// junction, the branch's stands straight out along the way the branch leaves. Locked to the
    /// junction, the same way a pool's lead-in is.
    /// </summary>
    [Serializable]
    public class JunctionLeadIn
    {
        public string nodeId;
    }

    /// <summary>
    /// A node where one river was split in two: the river before it ends there and the river
    /// after it starts there. Not a T — neither carries on through — so the two runs are left
    /// open where they meet and turned to one heading, and the water carries straight on.
    /// </summary>
    [Serializable]
    public class DesignerRiverJoin
    {
        public string nodeId;
    }

    [Serializable]
    public class DesignerShop
    {
        public string nodeId;
        public string pathId;
        public float pathT;
        public GameObject shopPrefabOverride;
        public GameObject[] shopItems = new GameObject[4];
    }

    [Serializable]
    public class DesignerObstacle
    {
        public string obstacleId;
        public string pathId;
        public float pathT;
        public int soulSlotCount;
        public bool hasVideoOrb = true;
        public GameObject obstaclePrefab;
    }

    [Serializable]
    public class DesignerArenaEntrance
    {
        public string nodeId;
        public int    entranceIndex;

        [Tooltip("The side of the arena this entrance sits on, once its arena's entrances are " +
                 "on compass points.")]
        public Compass compass;

        [Tooltip("Tick to give the river arriving at this entrance its own overlap. Left off, " +
                 "it follows River Overlap in Procedural Generation.")]
        public bool overrideRunOverlap;

        [Tooltip("How far this entrance's own river pushes into the arena, used when Override " +
                 "Run Overlap is on.")]
        [Min(0f)] public float runOverlap;

        [Tooltip("Tick to stand the entrance prefab in its own place inside the archway. Left " +
                 "off, it follows Entrance Alignment in Procedural Generation.")]
        public bool overrideAlignment;

        [Tooltip("Where the entrance prefab stands inside this door's archway, used when " +
                 "Override Alignment is on.")]
        public ArenaEntranceAlignment alignment = new ArenaEntranceAlignment();

        [Tooltip("How far out past this door the boat appears when the player leaves the level " +
                 "by it, facing away from the door.")]
        [Min(0f)] public float boatSpawnDistance = 3f;
    }

    [Serializable]
    public class DesignerArena
    {
        public string     nodeId;
        public GridData   gridData;
        public int        entranceIndex;
        public GameObject arenaPrefabOverride;

        [Tooltip("World-space radius of the arena — read from LevelSelectArenaRadiusGizmo on the prefab. " +
                 "Controls ring display and entrance node placement in the designer.")]
        public float arenaRadius = 10f;

        [Tooltip("Tick to give this arena its own wall shape. Left off, it takes the default " +
                 "in Procedural Generation.")]
        public bool overrideWall;

        [Tooltip("Stand an archway over every one of this arena's entrances.")]
        public bool archwayOnEntrances;

        [Tooltip("Tick to give this arena its own archway shape. Left off, it takes the default " +
                 "in Procedural Generation.")]
        public bool overrideArchway;

        [Tooltip("This arena's own archway shape, used when Override Archway is on.")]
        public ArenaArchwayProfile archwayProfile = new ArenaArchwayProfile();

        [Tooltip("Tick to give this arena's doors their own shape. Left off, they take the " +
                 "default in Procedural Generation.")]
        public bool overrideDoor;

        [Tooltip("This arena's own door shape, used when Override Door is on.")]
        public ArenaDoorProfile doorProfile = new ArenaDoorProfile();

        [Tooltip("The door preset last loaded onto this arena or saved from it. Its settings " +
                 "are copied, not shared, so editing them here does not touch the preset.")]
        public ArenaDoorPreset doorPreset;

        [Tooltip("This arena's own wall shape, used when Override Wall is on. Its radius is the " +
                 "arena boundary, and is what the ring display and entrance nodes follow.")]
        public ArenaWallProfile wallProfile = new ArenaWallProfile();

        [Tooltip("Tick to give the river arriving at this arena's primary entrance its own " +
                 "overlap. Left off, it follows River Overlap in Procedural Generation.")]
        public bool overrideRunOverlap;

        [Tooltip("How far the primary entrance's own river pushes into the arena, used when " +
                 "Override Run Overlap is on.")]
        [Min(0f)] public float runOverlap;

        [Tooltip("Tick to stand this arena's primary entrance prefab in its own place inside " +
                 "the archway. Left off, it follows Entrance Alignment in Procedural Generation.")]
        public bool overrideEntranceAlignment;

        [Tooltip("Where the primary entrance prefab stands inside its archway, used when " +
                 "Override Entrance Alignment is on.")]
        public ArenaEntranceAlignment entranceAlignment = new ArenaEntranceAlignment();

        [Tooltip("How far out past the primary door the boat appears when the player leaves the " +
                 "level by it, facing away from the door.")]
        [Min(0f)] public float boatSpawnDistance = 3f;

        // One entry per additional entrance beyond the primary (nodeId above)
        public List<DesignerArenaEntrance> secondaryEntrances = new List<DesignerArenaEntrance>();

        [Tooltip("On once Refresh Lead-ins has put this arena's entrances on compass points. " +
                 "From then on the entrances are locked to those points.")]
        public bool compassEntrances;

        [Tooltip("The side of the arena the primary entrance sits on, when Compass Entrances is on.")]
        public Compass compass = Compass.South;

        [Tooltip("The lead-in nodes this arena placed, one per entrance a river arrives at.")]
        public List<ArenaLeadIn> leadIns = new();
    }

    /// <summary>
    /// A node an arena places straight out from one of its entrances, so the river arrives square
    /// on to it. Locked to the arena, the same way a pool's lead-in is.
    /// </summary>
    [Serializable]
    public class ArenaLeadIn
    {
        public string nodeId;
        public string entranceNodeId;
    }

    /// <summary>
    /// A circular basin on the river, built by revolving the same cross-section a run is swept
    /// along. <see cref="islandRadius"/> is what separates the shapes the designer draws: 0 for
    /// an open pool, anything larger for a pool with a centre — and a pool that several rivers
    /// meet is a roundabout. Any path starting, ending or passing through <see cref="nodeId"/>
    /// meets it — a path passing through is split there, in on one side and out on the other.
    /// </summary>
    [Serializable]
    public class DesignerPool
    {
        public string nodeId;

        [Tooltip("Tick to give this pool its own shape. Left off, it takes the default in " +
                 "Procedural Generation.")]
        //
        // Defaults ON, and deliberately: every pool authored before there WAS a default carries
        // its own radii, and the flag is absent from those saved files. Unity runs the field
        // initialiser before applying what the file holds, so a pool that predates this keeps
        // the shape it was drawn with instead of silently snapping to the default. New pools
        // are created with it off (see the designer), so the default is what they follow.
        public bool overrideShape = true;

        [Tooltip("Radius of the water — the channel edge. The rim goes on outside this by the " +
                 "river profile's rim width.")]
        [Min(0.05f)] public float poolRadius = 3f;

        [Tooltip("Radius of the plinth in the middle, flush with the rim. 0 leaves an open bowl.")]
        [Min(0f)] public float islandRadius = 1f;

        [Tooltip("How far the floor drops below the rim at its deepest — the middle of a bowl, " +
                 "or the centre of the ring channel. 0 takes the river's own channel depth.")]
        [Min(0f)] public float floorDepth = 0f;

        public Color editorColor = new Color(0.35f, 0.75f, 1f);

        [Tooltip("The lead-in nodes this pool placed on its compass points, one per river.")]
        public List<PoolLeadIn> leadIns = new();

        [Tooltip("Stand a tower in the middle of the island. Only built when the pool has an " +
                 "island — never in an open pool.")]
        public bool hasTower;

        [Tooltip("The tower in the middle of the island — stem and orb sizes.")]
        public LollipopTower tower = new LollipopTower { stemRadius = 0.05f, stemHeight = 0.8f, orbRadius = 0.25f,
                                                     baseRadius = 0.12f, baseHeight = 0.08f };

        [Tooltip("The tower preset this pool's tower is built from — edit it with Edit in the designer, " +
                 "and this pool's tower changes with it.")]
        public LollipopTowerPreset towerPreset;

        [Tooltip("Which tower stands in the middle of the island: a lollipop, or a turret.")]
        public TowerKind towerKind = TowerKind.Lollipop;

        [Tooltip("The turret in the middle of the island, when Kind is Turret.")]
        public TurretTower turret = new TurretTower
        {
            body = new LollipopTower { stemRadius = 0.05f, stemHeight = 0.8f, orbRadius = 0.25f,
                                       baseRadius = 0.12f, baseHeight = 0.08f },
            noseLength = 0.3f, noseOuterRadius = 0.075f, noseInnerRadius = 0.045f,
        };

        [Tooltip("The turret preset this pool's turret is built from — edit it with Edit in the designer, " +
                 "and this pool's turret changes with it.")]
        public TurretTowerPreset turretPreset;

        [Tooltip("Scales the whole tower where it stands here. Kept with this placement, not " +
                 "saved into the tower preset.")]
        [Min(0.001f)] public float towerScale = 1f;

        [Tooltip("Whether the angel lands on top of the tower on this island.")]
        public DesignerPerch towerPerch = new DesignerPerch();

        [Tooltip("The souls pool tower in the middle of the island, when Kind is Souls Pool — " +
                 "where a soul route's souls begin.")]
        public SoulsPoolTower soulsPool = new SoulsPoolTower();

        [Tooltip("The souls pool tower preset this pool's tower is built from — edit it with Edit in the designer, " +
                 "and this pool's tower changes with it.")]
        public SoulsPoolTowerPreset soulsPoolPreset;

        [Tooltip("Pressing E at the souls pool tower also plays a camera sequence, as the souls are let out.")]
        public bool towerCinematic;

        [Tooltip("The camera sequence the souls pool tower plays when its souls are let out.")]
        public CameraSequencePreset towerCinematicPreset;

        // Same as a node's Pin Cameras: the preset's cameras move with this pool. towerCinematicPinnedAt
        // is where the pool's node stood when they were last moved.
        public bool    towerCinematicPinCameras;
        public Vector3 towerCinematicPinnedAt;
    }

    /// <summary>
    /// A pool's island has to be wider than this for a tower to stand in its middle. 0 — any
    /// island will do (the user lifted the old 0.5 limit, 2026-09-28); an open pool has none.
    /// </summary>
    public const float PoolTowerMinIsland = 0f;

    /// <summary>
    /// The pool on <paramref name="nodeId"/> when it really stands a souls pool tower — ticked,
    /// of that kind, and on an island wide enough for it to be built. Null otherwise. The one
    /// answer the designer and the runtime both ask, so a route can't start from a tower the
    /// scene never built.
    /// </summary>
    public DesignerPool SoulsTowerPoolAt(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return null;

        var pool = pools.Find(p => p != null && p.nodeId == nodeId);
        if (pool == null || !pool.hasTower || pool.towerKind != TowerKind.SoulsPool || pool.soulsPool == null)
            return null;

        return PoolShapeFor(pool).islandRadius > PoolTowerMinIsland ? pool : null;
    }

    /// <summary>True when the route begins at a souls pool tower — the only place a route may start.</summary>
    public bool RouteStartsAtSoulsTower(SoulRoute route)
        => route != null && SoulsTowerPoolAt(route.originNodeId) != null;

    // Both stands a block on each bank, each with its own settings (see DesignerOutpost.bothLeft).
    public enum OutpostSide { Left, Right, Both }

    // Which way an outpost's slope runs along the walkway from its opening, looking down the
    // path the way it was drawn.
    public enum OutpostSlopeWay { Downstream, Upstream }

    /// <summary>
    /// A square block standing beside a river run — an installation outpost. It is placed at a
    /// point along a path and stands on one bank, butted against the run's outer edge, with a
    /// wall around the top.
    ///
    ///          |<---- width (along the river) ---->|
    ///           _____                         _____    <- wall top (height + wall height)
    ///          |     |_______________________|     |   <- floor    (height)
    ///   run    |                                   |
    ///  ________|                                   |   <- rim top  (0)
    ///          |<- depth (away from the river) ->  |
    ///
    /// The bottom carries on down with every other generated piece, so it is not authored here.
    /// </summary>
    [Serializable]
    public class DesignerOutpost
    {
        public string outpostId;
        public string pathId;
        public float  pathT;

        [Tooltip("Which bank it stands on, looking down the path the way it was drawn — or Both, " +
                 "a block on each bank with its own settings.")]
        public OutpostSide side = OutpostSide.Right;

        [Tooltip("How far it runs along the river.")]
        [Min(0.01f)] public float width = 1.2f;

        [Tooltip("How far it reaches away from the river, measured out from the run's outer edge.")]
        [Min(0.01f)] public float depth = 0.8f;

        [Tooltip("How far its floor stands above the rim top of the river run.")]
        [Min(0f)] public float height = 0.6f;

        [Tooltip("Thickness of the wall around the top.")]
        [Min(0f)] public float wallThickness = 0.05f;

        [Tooltip("How far the wall around the top stands above the floor.")]
        [Min(0f)] public float wallHeight = 0.1f;

        [Tooltip("The lollipop tower standing on the floor — stem and orb sizes.")]
        public LollipopTower tower = new LollipopTower();

        [Tooltip("Moves the tower's base across the floor from its centre: x along the river, " +
                 "y away from it.")]
        public Vector2 towerOffset = Vector2.zero;

        [Tooltip("A round column on the back corner down the path the way it was drawn, centred " +
                 "in the wall's corner and running all the way down.")]
        public bool columnDownstream = false;

        [Tooltip("Stands a lollipop tower on the downstream column's top.")]
        public bool towerOnDownstream = true;

        [Tooltip("A round column on the back corner up the path the way it was drawn, centred " +
                 "in the wall's corner and running all the way down.")]
        public bool columnUpstream = false;

        [Tooltip("Stands a lollipop tower on the upstream column's top.")]
        public bool towerOnUpstream = true;

        [Tooltip("How far the columns' tops stand above the floor.")]
        [Min(0f)] public float columnHeight = 0.2f;

        [Tooltip("Radius of the columns, centred in the wall's corner.")]
        [Min(0.001f)] public float columnRadius = 0.1f;

        /// <summary>
        /// Whether any column carries a lollipop. When none does, the one tower stands on the floor
        /// off its offset.
        /// </summary>
        public bool TowerOnAColumn => (columnDownstream && towerOnDownstream) ||
                                      (columnUpstream   && towerOnUpstream);

        // The opening and its slope stay with the placement, not the preset: they are only built
        // where a river walkway runs along this bank.

        [Tooltip("A gap in the wall facing the river, from the floor up to the wall's top. Only " +
                 "built where a river walkway runs along this bank.")]
        public bool opening = false;

        [Tooltip("How wide the gap in the wall is, along the river.")]
        [Min(0.01f)] public float openingWidth = 0.3f;

        [Tooltip("Moves the gap along the wall from its middle — down the path the way it was " +
                 "drawn when positive. Kept clear of the wall's corners.")]
        public float openingOffset = 0f;

        [Tooltip("A slope on the walkway from the opening down to the walkway's top: flat in " +
                 "front of the opening, then straight down along the walkway.")]
        public bool slope = false;

        [Tooltip("Which way the slope runs along the walkway from the opening, looking down the " +
                 "path the way it was drawn.")]
        public OutpostSlopeWay slopeWay = OutpostSlopeWay.Downstream;

        [Tooltip("How long the flat part is, measured from the far edge of the opening — never " +
                 "shorter than the opening is wide.")]
        [Min(0f)] public float landingLength = 0.3f;

        [Tooltip("How long the slope is, from the end of the flat part down to the walkway's top.")]
        [Min(0.01f)] public float slopeLength = 1f;

        [Tooltip("How far the slope reaches out from the outpost's face, toward the river — its " +
                 "own width, not the walkway's.")]
        [Min(0.01f)] public float slopeWidth = 0.2f;

        [Tooltip("The preset this outpost is built from — edit it with Edit in the designer, " +
                 "and this outpost changes with it.")]
        public InstallationOutpostPreset preset;

        [Tooltip("The tower preset this outpost's tower is built from — edit it with Edit in the designer, " +
                 "and this outpost's tower changes with it.")]
        public LollipopTowerPreset towerPreset;

        [Tooltip("Which tower stands on the outpost — on the floor or its columns: a lollipop, or a turret.")]
        public TowerKind towerKind = TowerKind.Lollipop;

        [Tooltip("The turret standing on the outpost, when Kind is Turret.")]
        public TurretTower turret = new TurretTower();

        [Tooltip("The turret preset this outpost's turret is built from — edit it with Edit in the designer, " +
                 "and this outpost's turret changes with it.")]
        public TurretTowerPreset turretPreset;

        [Tooltip("Scales the whole tower where it stands here. Kept with this placement, not " +
                 "saved into the tower preset.")]
        [Min(0.001f)] public float towerScale = 1f;

        [Tooltip("Whether the angel lands on top of this outpost's tower.")]
        public DesignerPerch towerPerch = new DesignerPerch();

        [Tooltip("How the Left bank's block is built when Side is Both — its own preset and " +
                 "settings. The settings above are then the Right bank's. Where it stands (path, " +
                 "along path, side) is always the outpost's own, never this one's.")]
        [SerializeReference] public DesignerOutpost bothLeft;

        /// <summary>
        /// The settings one bank's block is built from: the Left bank's own when Side is Both,
        /// otherwise this outpost's.
        /// </summary>
        public DesignerOutpost OnBank(OutpostSide bank)
            => side == OutpostSide.Both && bank == OutpostSide.Left && bothLeft != null ? bothLeft : this;

        public DesignerOutpost Clone()
        {
            var copy = (DesignerOutpost)MemberwiseClone();
            copy.tower      = tower?.Clone();
            copy.turret     = turret?.Clone();
            copy.towerPerch = towerPerch?.Clone();
            copy.bothLeft   = bothLeft?.Clone();
            return copy;
        }
    }

    public enum StructureShape { Block, RoundColumn }
    public enum StructureTop   { None, Lollipop, Turret, Outpost }

    /// <summary>
    /// A freestanding structure: a block or a round column dropped anywhere on the map, standing
    /// on the landscape under its centre (riding the hills, as a spike does), with something on
    /// top — nothing, a lollipop tower, a turret, or an outpost (its walls round the top, square on
    /// a block and a ring on a round column, with its columns and towers).
    ///
    /// The outpost top keeps a whole <see cref="DesignerOutpost"/>, so its presets and columns
    /// work as an outpost's do; its width, depth and height are always the structure's
    /// own, written onto it before it is used. It is never in <see cref="outposts"/>.
    /// </summary>
    [Serializable]
    public class DesignerStructure
    {
        public string structureId;

        [Tooltip("Where its centre stands on the map.")]
        public Vector2 positionXZ;

        [Tooltip("A rectangular block, or a round column.")]
        public StructureShape shape = StructureShape.Block;

        [Tooltip("The block's size along its own x.")]
        [Min(0.01f)] public float width = 0.8f;

        [Tooltip("The block's size along its own z.")]
        [Min(0.01f)] public float depth = 0.8f;

        [Tooltip("The round column's radius.")]
        [Min(0.01f)] public float radius = 0.4f;

        [Tooltip("How far its top stands above the landscape under its centre.")]
        [Min(0f)] public float height = 0.6f;

        [Tooltip("Which way it is turned, in degrees, clockwise seen from above.")]
        public float turn = 0f;

        [Tooltip("What stands on top.")]
        public StructureTop top = StructureTop.None;

        [Tooltip("The lollipop tower on top, when Top is Lollipop.")]
        public LollipopTower tower = new LollipopTower();

        [Tooltip("The tower preset this tower is built from — edit it with Edit in the designer, " +
                 "and this tower changes with it.")]
        public LollipopTowerPreset towerPreset;

        [Tooltip("The turret on top, when Top is Turret.")]
        public TurretTower turret = new TurretTower();

        [Tooltip("The turret preset this turret is built from — edit it with Edit in the designer, " +
                 "and this turret changes with it.")]
        public TurretTowerPreset turretPreset;

        [Tooltip("Scales the whole tower where it stands here. Not saved into the tower preset.")]
        [Min(0.001f)] public float towerScale = 1f;

        [Tooltip("Somewhere the angel can land — the top of the tower, or the structure's top.")]
        public DesignerPerch perch = new DesignerPerch();

        [Tooltip("The outpost on top, when Top is Outpost — walls, columns and tower.")]
        public DesignerOutpost outpost = new DesignerOutpost();

        public DesignerStructure Clone()
        {
            var copy = (DesignerStructure)MemberwiseClone();
            copy.tower   = tower?.Clone();
            copy.turret  = turret?.Clone();
            copy.perch   = perch?.Clone();
            copy.outpost = outpost?.Clone();
            return copy;
        }
    }

    /// <summary>
    /// What happens when the boat stands near something the designer places and the player
    /// presses the interact key: a prompt comes up, and pressing it shows a poster.
    ///
    /// Held apart from the thing it is on, so a rim node display, an outpost's characters and
    /// whatever comes later all carry the same block and read the same way in the panel.
    /// </summary>
    [Serializable]
    public class DesignerInteract
    {
        [Tooltip("Whether this one can be interacted with at all.")]
        public bool enabled = false;

        [Tooltip("What the prompt reads. The key is added by the prompt itself, so this is " +
                 "just the doing — \"Look at the poster\".")]
        public string prompt = "Look";

        [Tooltip("How near the boat has to be, measured flat across the water.")]
        [Min(0f)] public float radius = 3f;

        [Tooltip("Which poster in the map's poster library it shows, by name.")]
        public string posterName;

        public DesignerInteract Clone() => (DesignerInteract)MemberwiseClone();
    }

    /// <summary>
    /// Somewhere the angel can land, hung on whatever was just generated. She holds a flight
    /// above the boat and comes down onto the nearest of these that the BOAT has sailed into —
    /// so the radii here are measured from the thing she lands on, not from her.
    ///
    /// The same fields the Grid Designer puts on a marked spike, so a perch reads the same
    /// wherever it is authored; the tip height is not among them, because the thing she stands
    /// on knows its own — a tower hands over the top of its orb, a marker its own height.
    /// </summary>
    [Serializable]
    public class DesignerPerch
    {
        [Tooltip("Whether the angel can land here at all.")]
        public bool enabled = false;

        [Tooltip("Sail inside this and she comes down here; back outside it and she leaves.")]
        [Min(0f)] public float perchRadius = 12f;

        [Tooltip("Always come down the moment the boat arrives, rather than only when she " +
                 "happens to be looking for somewhere to land.")]
        public bool priority = false;

        [Tooltip("She starts the map stood here. Only one perch should have this — the first " +
                 "one found wins. She holds it however far off the boat is, and only joins the " +
                 "flight once you have sailed inside its radius.")]
        public bool startPerch = false;

        [Tooltip("Radius of the curve she lands along. 0 = straight at it.")]
        [Min(0f)] public float landingCurveSize = 2f;

        [Tooltip("Tick to let her be talked to here. The prompt and the key are the map's own, " +
                 "the same ones a poster uses.")]
        public bool talkEnabled = false;

        [Tooltip("How near the boat has to be to talk to her here, measured flat. Kept inside " +
                 "the perch radius — a talk range reaching further would arm the key for a boat " +
                 "she is already leaving.")]
        [Min(0f)] public float talkRadius = 4f;

        [Tooltip("What the prompt reads. The key is added by the prompt itself.")]
        public string talkPrompt = "Talk";

        [Tooltip("What she says here. Split on \"/\" into one line per press.")]
        [TextArea(1, 4)] public string talkText = "";

        [Tooltip("How far her talk camera sits from her while she talks here, along its own view. " +
                 "0 = the distance authored on the angel prefab's camera.")]
        [Min(0f)] public float talkCameraDistance = 0f;

        public DesignerPerch Clone() => (DesignerPerch)MemberwiseClone();
    }

    /// <summary>
    /// A perch standing on nothing the designer generated — dropped where you like, at whatever
    /// height you like. This is what covers everything that has no perch block of its own: park
    /// one on top of a pipe, a wall or a hill and she lands there just the same.
    /// </summary>
    [Serializable]
    public class DesignerPerchMarker
    {
        public string perchId;

        [Tooltip("Where it stands on the map.")]
        public Vector2 positionXZ;

        [Tooltip("How far her feet stand above the water here.")]
        public float height = 0.5f;

        [Tooltip("The landing itself — radii, priority, and what she says.")]
        public DesignerPerch perch = new DesignerPerch { enabled = true };

        public DesignerPerchMarker Clone()
        {
            var copy = (DesignerPerchMarker)MemberwiseClone();
            copy.perch = perch?.Clone();
            return copy;
        }
    }

    /// <summary>
    /// A procedural spike standing in the landscape — the same generated rock the Grid Designer
    /// puts in a level (shape from a Spike Studio preset, size by a multiplier), but here it is
    /// part of the land rather than a rock in water: the preset's surface line (its y = 0) sits
    /// on the landscape baseline, and everything the preset puts below it is buried.
    ///
    /// Shaded by the landscape (Level Select Landscape Tuner > Spikes), scenery only — no boat
    /// collider.
    /// </summary>
    [Serializable]
    public class DesignerSpike
    {
        public string spikeId;

        [Tooltip("Where it stands on the map.")]
        public Vector2 positionXZ;

        [Tooltip("Shape this rock wears, authored in the Spike Studio. Presets live in " +
                 "Resources/Spikes. Unset falls back to the default shape.")]
        public SpikeShapePreset preset;

        [Tooltip("Size multiplier on the whole preset. 1 = the preset's own size.")]
        [Min(0.01f)] public float scale = 1f;

        [Tooltip("Somewhere the angel can land — on the tip of this rock.")]
        public DesignerPerch perch = new DesignerPerch();

        public DesignerSpike Clone()
        {
            var copy = (DesignerSpike)MemberwiseClone();
            copy.perch = perch?.Clone();
            return copy;
        }
    }

    /// <summary>
    /// One fog zone: a square fog sheet at its own height, and a separately authored box that
    /// says where the boat has to be for the sheet to show. The box is XZ on the map; it is built
    /// as a trigger BoxCollider and the boat is tested against it in X and Z only.
    /// </summary>
    [System.Serializable]
    public class DesignerFogZone
    {
        public string zoneId;

        [Tooltip("Centre of the fog sheet on the map, world X and Z.")]
        public Vector2 centreXZ;

        [Tooltip("Width of the square fog sheet, in world units.")]
        [Min(0.1f)] public float size = 10f;

        [Tooltip("How far this sheet hovers above the landscape surface.")]
        public float height = 2f;

        [Tooltip("How far in from the sheet's edge the fog fades to nothing, in world units. " +
                 "0 = a hard edge.")]
        [Min(0f)] public float edgeFade = 1f;

        [Tooltip("How many squares this sheet is divided into along a side. Only matters once " +
                 "Landscape Influence is above 0 — a flat sheet looks the same at any count.")]
        [Range(1, 64)] public int subdivisions = 6;

        [Tooltip("How much this sheet follows the landscape under it. 0 = flat at its height; " +
                 "1 = its height above the ground at every point, rising and falling with the hills.")]
        [Range(0f, 1f)] public float landscapeInfluence = 1f;

        [Tooltip("Mesh smoothing passes. Each pass moves every point of the sheet towards the " +
                 "average of its neighbours, rounding off the creases between the squares. 0 = none.")]
        [Range(0, 20)] public int smoothing = 1;

        [Tooltip("Centre of the zone's bounds on the map, world X and Z. The boat inside these " +
                 "bounds shows the sheet.")]
        public Vector2 boundsCentreXZ;

        [Tooltip("Width (X) and depth (Z) of the zone's bounds, in world units.")]
        public Vector2 boundsSizeXZ = new Vector2(10f, 10f);

        public DesignerFogZone Clone() => (DesignerFogZone)MemberwiseClone();
    }

    public enum WalkwaySide { Left, Right, Both }

    /// <summary>
    /// A river walkway set on a node: an extra rim along the outside of the run, from this node
    /// to the next node of the river that also has one, on the same side. Where an outpost
    /// stands beside it the outpost moves out past it and the walkway runs straight along its
    /// face. A stretch is built from the settings of the node it starts at.
    /// </summary>
    [Serializable]
    public class DesignerWalkway
    {
        public string nodeId;

        [Tooltip("Whether this node carries a walkway. Kept when off, so its settings come back.")]
        public bool enabled = true;

        [Tooltip("Which bank it runs along, looking down the path the way it was drawn — or both.")]
        public WalkwaySide side = WalkwaySide.Both;

        [Tooltip("How far it reaches out from the run's outer edge.")]
        [Min(0.01f)] public float width = 0.2f;

        [Tooltip("How far its top stands above the rim — below it when negative, 0 for flush.")]
        public float height = 0f;

        /// <summary>Whether it is on and runs along this side (+1 right, -1 left).</summary>
        public bool Covers(int bank) =>
            enabled && (side == WalkwaySide.Both || (bank > 0) == (side == WalkwaySide.Right));

        /// <summary>Whether a stretch starting here is built the same as one starting at <paramref name="other"/>.</summary>
        public bool SameBuild(DesignerWalkway other) =>
            other != null && Mathf.Approximately(width, other.width) &&
            Mathf.Approximately(height, other.height);
    }

    /// <summary>
    /// A bridge set on a node: a flat deck straight across the river at a height above the rim,
    /// with a matching flat ramp at each end running out from the channel's edge, away from the
    /// river and down to the rim's height — over the rim and any walkways beside it.
    /// </summary>
    [Serializable]
    public class DesignerBridge
    {
        public string nodeId;

        [Tooltip("How far the deck's top stands above the rim.")]
        [Min(0.01f)] public float deckHeight = 0.15f;

        [Tooltip("How wide the deck and its ramps are, along the river.")]
        [Min(0.01f)] public float deckWidth = 0.12f;

        [Tooltip("How thick the deck is, from its top down to its underside over the water.")]
        [Min(0.005f)] public float deckThickness = 0.02f;

        [Tooltip("How far each ramp reaches out from the channel's edge, away from the river, " +
                 "to where it meets the rim's height.")]
        [Min(0.01f)] public float rampLength = 0.4f;

        [Tooltip("Moves the bridge along the river from its node — the way the path was drawn " +
                 "when positive, back when negative.")]
        public float offsetAlong = 0f;

        [Tooltip("The preset this bridge's deck and ramp sizes come from — they change with it. " +
                 "None: the sizes are this bridge's own.")]
        public BridgePreset bridgePreset;
    }

    public enum RimNodeSide { Left, Right, Both }

    /// <summary>
    /// How a rim node's river-side end is finished: the round cap it has always had, or cut
    /// square with one flat face leaning out into the river the way the channel wall does —
    /// the far end then square too, and the top a flat rectangle.
    /// </summary>
    public enum RimNodeShape { Rounded, Slanted }

    /// <summary>
    /// A rim node: a round platform standing on a river's rim at one of its nodes — a cylinder
    /// centred on the middle of the rim, bulging out past the outer edge and into the channel,
    /// its top a little above the rim and its wall running down to the drop. One side of the
    /// river or both, the same size either side.
    ///
    /// A width pushes its circle apart into two across the rim, joined by straight sides, and an
    /// optional plinth is a smaller platform on top of that, on the river-side circle — the spot
    /// whatever stands on it stands on.
    /// </summary>
    [Serializable]
    public class DesignerRimNode
    {
        public string nodeId;

        [Tooltip("Which bank it stands on, looking down the path the way it was drawn — or both.")]
        public RimNodeSide side = RimNodeSide.Right;

        [Tooltip("Radius of the platform. Held between just over half the rim width, so it " +
                 "reaches past both of the rim's edges, and just short of the river's centreline.")]
        public float radius = 0.1f;

        [Tooltip("How far its top stands above the rim.")]
        public float height = 0.01f;

        [Tooltip("Rounded: round ends, as it always was. Slanted: square ends, the river-side one " +
                 "a flat face leaning out into the river at the channel wall's lean above the " +
                 "water, and no plinth.")]
        public RimNodeShape shape = RimNodeShape.Rounded;

        [Tooltip("How far apart the platform's two circles stand, across the rim — one pushed " +
                 "out past the run's outer edge, one in towards the river. Zero is one plain " +
                 "circle, as it always was.")]
        [Min(0f)] public float width = 0f;

        [Tooltip("An extra round plinth standing on the platform's top, centred on the " +
                 "river-side circle. Zero for none.")]
        [Min(0f)] public float plinthRadius = 0f;

        [Tooltip("How far the plinth's top stands above the platform's top.")]
        [Min(0f)] public float plinthHeight = 0.01f;

        [Tooltip("What stands on top of it, on the river-side circle — on each side when it is " +
                 "on both.")]
        public RimNodeTopper topper = RimNodeTopper.None;

        [Tooltip("The lollipop tower standing on it, when Topper is Lollipop Tower.")]
        public LollipopTower tower = new LollipopTower();

        [Tooltip("The tower preset this tower is built from — edit it with Edit in the designer, " +
                 "and this tower changes with it.")]
        public LollipopTowerPreset towerPreset;

        [Tooltip("The turret standing on it, when Topper is Turret Tower.")]
        public TurretTower turret = new TurretTower();

        [Tooltip("The turret preset this turret is built from — edit it with Edit in the designer, " +
                 "and this turret changes with it.")]
        public TurretTowerPreset turretPreset;

        [Tooltip("Scales the whole tower where it stands here. Kept with this placement, not " +
                 "saved into the tower preset.")]
        [Min(0.001f)] public float towerScale = 1f;

        [Tooltip("How tall the vert display point stands, from the node floor to the top marker " +
                 "on its prefab, when Topper is Vert Display Point.")]
        [Min(0.001f)] public float displayHeight = 0.15f;

        [Tooltip("What the display point does when the boat comes near it, when Topper is " +
                 "Vert Display Point.")]
        public DesignerInteract interact = new DesignerInteract();

        [Tooltip("What stands on the Left platform when Side is Both. The Topper, Tower and " +
                 "Display Height fields above are then the Right platform's.")]
        public RimNodeTopping bothLeft = new RimNodeTopping();

        /// <summary>
        /// What stands on one platform — +1 the right, -1 the left, looking down the path the
        /// way it was drawn. A copy: hand it back through <see cref="SetToppingOn"/>.
        /// </summary>
        public RimNodeTopping ToppingOn(int side)
        {
            if (side < 0 && this.side == RimNodeSide.Both)
                return (bothLeft ?? new RimNodeTopping()).Clone();

            return new RimNodeTopping
            {
                topper        = topper,
                tower         = tower?.Clone(),
                towerPreset   = towerPreset,
                turret        = turret?.Clone(),
                turretPreset  = turretPreset,
                towerScale    = towerScale,
                displayHeight = displayHeight,
                interact      = interact?.Clone(),
            };
        }

        public void SetToppingOn(int side, RimNodeTopping topping)
        {
            if (side < 0 && this.side == RimNodeSide.Both)
            {
                bothLeft = topping.Clone();
                return;
            }

            topper        = topping.topper;
            tower         = topping.tower?.Clone();
            towerPreset   = topping.towerPreset;
            turret        = topping.turret?.Clone();
            turretPreset  = topping.turretPreset;
            towerScale    = topping.towerScale;
            displayHeight = topping.displayHeight;
            interact      = topping.interact?.Clone();
        }
    }

    /// <summary>What can stand on top of a rim node.</summary>
    public enum RimNodeTopper { None, LollipopTower, VertDisplayPoint, TurretTower }

    /// <summary>
    /// Which tower stands where a tower can: a lollipop, or a turret that turns. SoulsPool is for
    /// pool islands only — the other places offer just the first two.
    /// </summary>
    public enum TowerKind { Lollipop, Turret, SoulsPool }

    /// <summary>What stands on one rim node platform, and its sizes.</summary>
    [Serializable]
    public class RimNodeTopping
    {
        [Tooltip("What stands on the platform, on the plinth when it has one.")]
        public RimNodeTopper topper = RimNodeTopper.None;

        [Tooltip("The lollipop tower standing on it, when Topper is Lollipop Tower.")]
        public LollipopTower tower = new LollipopTower();

        [Tooltip("The tower preset this tower is built from — edit it with Edit in the designer, " +
                 "and this tower changes with it.")]
        public LollipopTowerPreset towerPreset;

        [Tooltip("The turret standing on it, when Topper is Turret Tower.")]
        public TurretTower turret = new TurretTower();

        [Tooltip("The turret preset this turret is built from — edit it with Edit in the designer, " +
                 "and this turret changes with it.")]
        public TurretTowerPreset turretPreset;

        [Tooltip("Scales the whole tower where it stands here. Kept with this placement, not " +
                 "saved into the tower preset.")]
        [Min(0.001f)] public float towerScale = 1f;

        [Tooltip("How tall the vert display point stands, from the node floor to the top marker " +
                 "on its prefab, when Topper is Vert Display Point.")]
        [Min(0.001f)] public float displayHeight = 0.15f;

        [Tooltip("What it does when the boat comes near it.")]
        public DesignerInteract interact = new DesignerInteract();

        [Tooltip("Whether the angel lands on top of the tower standing here.")]
        public DesignerPerch perch = new DesignerPerch();

        public RimNodeTopping Clone()
        {
            var copy = (RimNodeTopping)MemberwiseClone();
            copy.tower    = tower?.Clone();
            copy.turret   = turret?.Clone();
            copy.interact = interact?.Clone();
            copy.perch    = perch?.Clone();
            return copy;
        }
    }

    /// <summary>One node of a pipe: where it is on the map, and optionally its own height.</summary>
    [Serializable]
    public class PipeNode
    {
        public Vector2 positionXZ;

        [Tooltip("Tick to give this node its own height. Left off, it takes the pipe's height.")]
        public bool overrideHeight;

        [Tooltip("How far the pipe's centre line stands above the water here, when Override " +
                 "Height is on.")]
        [Min(0f)] public float height = 0.5f;
    }

    /// <summary>A support on a pipe: which leg it is on (node i to node i + 1), and how far along it.</summary>
    [Serializable]
    public class PipeSupport
    {
        public int leg;
        [Range(0f, 1f)] public float along = 0.5f;
    }

    /// <summary>
    /// A pipe standing over the map on support stems — drawn node by node in Pipe mode, with
    /// supports placed along it. Rounded at every bend; hollow, open at both ends. Only for
    /// looks: nothing travels along it, and it has no collider.
    /// </summary>
    [Serializable]
    public class DesignerPipe
    {
        public string pipeId;
        public List<PipeNode>    nodes    = new();
        public List<PipeSupport> supports = new();

        [Tooltip("How far the pipe's centre line stands above the water, at every node that " +
                 "doesn't override it.")]
        [Min(0f)] public float height = 0.5f;

        [Tooltip("The pipe's thickness, across the outside. The bends are rounded to match it.")]
        [Min(0.01f)] public float pipeThickness = 0.2f;

        [Tooltip("Thickness of the pipe's wall — the hollow is what is left inside it.")]
        [Min(0f)] public float wallThickness = 0.03f;

        [Tooltip("Thickness of each support stem. Each ring is as long as this.")]
        [Min(0.005f)] public float supportThickness = 0.06f;

        [Tooltip("How far each ring stands out beyond the pipe.")]
        [Min(0f)] public float ringOverhang = 0.02f;

        public float HeightAt(int node) =>
            nodes[node].overrideHeight ? nodes[node].height : height;
    }

    /// <summary>
    /// One node of a wall: where it is on the map, its own height if it has one, how the wall
    /// runs on to the next node, and whether a lollipop tower stands on top of it.
    /// </summary>
    [Serializable]
    public class WallNode
    {
        public Vector2 positionXZ;

        [Tooltip("Tick to give this node its own height. Left off, it takes the wall's height.")]
        public bool overrideHeight;

        [Tooltip("How far the wall's top stands above the water here, when Override Height is on.")]
        [Min(0f)] public float height = 0.3f;

        [Tooltip("The wall from this node to the next one bends through its neighbours. Off, it " +
                 "runs straight.")]
        public bool curvedToNext = true;

        [Tooltip("Leave out the wall from this node to the next one.")]
        public bool gapToNext;

        [Tooltip("Make this node a round column: a cylinder from the wall's base up past its " +
                 "top. Anything standing on the node stands on the column's top.")]
        public bool hasColumn;

        [Tooltip("The column's radius.")]
        [Min(0.001f)] public float columnRadius = 0.08f;

        [Tooltip("How far the column's top stands above the wall's top at this node — above the " +
                 "lip's top, when the wall has a lip.")]
        [Min(0f)] public float columnHeight = 0.05f;

        [Tooltip("Stand a lollipop tower on top of the wall at this node.")]
        public bool hasTower;

        [Tooltip("The tower standing on this node — stem and orb sizes.")]
        public LollipopTower tower = new LollipopTower();

        [Tooltip("The tower preset this tower is built from — edit it with Edit in the designer, " +
                 "and this tower changes with it.")]
        public LollipopTowerPreset towerPreset;

        [Tooltip("Which tower stands on this node: a lollipop, or a turret.")]
        public TowerKind towerKind = TowerKind.Lollipop;

        [Tooltip("The turret standing on this node, when Kind is Turret.")]
        public TurretTower turret = new TurretTower();

        [Tooltip("The turret preset this turret is built from — edit it with Edit in the designer, " +
                 "and this turret changes with it.")]
        public TurretTowerPreset turretPreset;

        [Tooltip("Scales the whole tower where it stands here. Kept with this placement, not " +
                 "saved into the tower preset.")]
        [Min(0.001f)] public float towerScale = 1f;

        [Tooltip("Whether the angel lands on top of the tower on this node.")]
        public DesignerPerch towerPerch = new DesignerPerch();

        [Tooltip("Cut an archway through the wall here, centred on this node.")]
        public bool hasArchway;

        [Tooltip("How wide the archway is, measured along the wall.")]
        [Min(0f)] public float archWidth = 0.1f;

        [Tooltip("How tall the archway is, from the wall's base up to the top of its round.")]
        [Min(0f)] public float archHeight = 0.2f;

        [Tooltip("Bore a round hole straight across the wall here, centred on this node.")]
        public bool hasPorthole;

        [Tooltip("The porthole's radius.")]
        [Min(0f)] public float portholeRadius = 0.08f;

        [Tooltip("How high the porthole's centre is, from the wall's base.")]
        [Min(0f)] public float portholeHeight = 0.2f;
    }

    /// <summary>
    /// A wall standing out of the water — a plain slab drawn node by node in Wall mode, built as
    /// one mesh along its nodes, in the river material, with a collider of the same shape. Each
    /// stretch between two nodes is curved or straight, or left out as a gap.
    /// </summary>
    [Serializable]
    public class DesignerWall
    {
        public string wallId;
        public List<WallNode> nodes = new();

        [Tooltip("Join the last node back to the first.")]
        public bool closed;

        [Tooltip("How far the wall's top stands above the water, at every node that doesn't " +
                 "override it.")]
        [Min(0f)] public float height = 0.3f;

        [Tooltip("The wall's thickness, across.")]
        [Min(0.001f)] public float thickness = 0.09f;

        [Tooltip("Stand a square lip on the wall's top all the way along.")]
        public bool hasLip;

        [Tooltip("The lip's sizes — height, thickness and its ring round columns.")]
        public WallLip lip = new WallLip();

        [Tooltip("The wall preset this wall's height, thickness and lip come from — edit it with Edit " +
                 "in the designer, and this wall changes with it.")]
        public WallPreset wallPreset;

        /// <summary>How far the lip stands above the wall's top — 0 with no lip.</summary>
        public float LipHeight => hasLip && lip != null ? Mathf.Max(0f, lip.height) : 0f;

        [Tooltip("Seam lines left undrawn on this wall — edited in Tools > Level Design > Seam Clean Up Tool.")]
        public List<WallSeamSpot> removedSeams = new();

        [Tooltip("Surfaces of this wall treated as one, so their seam fades come out even — edited in " +
                 "Tools > Level Design > Seam Clean Up Tool.")]
        public List<WallFaceGroup> faceGroups = new();

        public float HeightAt(int node) =>
            nodes[node].overrideHeight ? nodes[node].height : height;
    }

    public List<LandscapeHillPoint> hillPoints = new();

    public List<DesignerNode>     nodes     = new();
    public List<DesignerPath>     paths     = new();
    public List<DesignerJunction> junctions = new();
    public List<DesignerRiverJoin> riverJoins = new();
    public List<DesignerShop>     shops     = new();
    public List<DesignerObstacle> obstacles = new();
    public List<DesignerArena>    arenas    = new();
    public List<DesignerPool>     pools     = new();
    public List<DesignerOutpost>  outposts  = new();
    public List<DesignerRimNode>  rimNodes  = new();
    public List<DesignerWalkway>  walkways  = new();
    [Tooltip("River Walkway 2 — a second walkway set on nodes, built the same way, out past " +
             "the first walkway where one runs beside it.")]
    public List<DesignerWalkway>  walkways2 = new();
    public List<DesignerBridge>   bridges   = new();
    public List<DesignerPipe>     pipes     = new();
    public List<DesignerWall>     walls     = new();

    [Tooltip("Perches standing on nothing generated — dropped where you like, for the angel to " +
             "land on whatever has no perch block of its own.")]
    public List<DesignerPerchMarker> perchMarkers = new();

    [Tooltip("Procedural spikes standing in the landscape, on its baseline.")]
    public List<DesignerSpike> spikes = new();

    [Tooltip("What the spikes are drawn with — LevelSelectSpikeMat, shaded by the landscape tuner.")]
    public Material spikeMaterial;

    [Tooltip("Freestanding structures — a block or round column dropped anywhere on the landscape, " +
             "with a lollipop, a turret or an outpost on top.")]
    public List<DesignerStructure> structures = new();

    [Tooltip("Shot cameras clamped to something on the map — picked on a camera symbol in Select mode.")]
    public List<CameraClamp> cameraClamps = new();

    // Tool settings
    // Retired with the on-rails boat. Kept so designer assets written before that still
    // load without complaint; nothing reads it, and nothing is spawned from it.
    public GameObject junctionScriptObject;
    public GameObject arenaPrefab;
    public GameObject arenaEntrancePrefab; // LEVELSELECTARENAENTRANCE — spawned per entrance
    public GameObject obstaclePrefab;
    public GameObject shopPrefab;
    // River shape — blocks and junctions are both built from these, so they always mate.
    public Material           riverMaterial;
    public Material           waterMaterial;

    [Tooltip("Material on the generated arena walls. Falls back to the river material when " +
             "left empty, so a wall is never generated unshaded.")]
    public Material           arenaWallMaterial;

    [Tooltip("Material on the door filling each archway's opening. Falls back to the wall " +
             "material when left empty.")]
    public Material           arenaDoorMaterial;

    [Tooltip("The material every arena's numeral is shown on. Each numeral gets its own variant " +
             "of it carrying that drawing, so this one holds the look and nothing else. Left " +
             "empty, the discs are left blank.")]
    public Material           arenaNumeralMaterial;

    [Tooltip("Every poster the map's display points can show. One asset, so art is swapped " +
             "or added there rather than on each display in turn.")]
    public LevelSelectPosterLibrary posterLibrary;

    [Tooltip("Water is generated as permanent mesh sitting in every river channel. Untick and " +
             "no water is generated at all.")]
    public bool               waterFilled = true;

    [Tooltip("How far the water surface sits below the rim top — the same distance on every " +
             "river, so a run that climbs or drops carries its water with it. The surface is " +
             "the run's full inner width, so its edges bury themselves in the channel wall.")]
    [Min(0f)] public float     waterLevel  = 0f;

    // ─────────────────────────────────────────────
    // WHERE TWO WATERS MEET
    //
    // Every water surface in the world is generated to BUTT onto its neighbours — a branch's
    // ribbon stops on the edge of the river it leaves, a pool's ring stops on the line each of
    // its rivers ends on. Nothing overlaps, so nothing z-fights.
    //
    // What butting cannot do is carry a pattern across the join. Each surface has its own frame,
    // and where two frames meet on a line the lines drawn in them meet on that line too. An
    // overlap lets the one on top run PAST the join and fade out over its neighbour instead,
    // which is what hides it.
    //
    // These are geometry, not look: they change the mesh, so they need a Rebuild Runs. Leave
    // them at 0 and every surface butts exactly as it did before.
    // ─────────────────────────────────────────────

    [Tooltip("How far a pool's water carries on OUT into each river that meets it, over that " +
             "river's own water, before fading out. 0 butts it onto the line the river's water " +
             "ends on as before. Geometry — needs a Rebuild Runs.")]
    [Min(0f)] public float     waterPoolOverlap = 0f;

    [Tooltip("How far every generated piece carries on below the water surface — runs, pools " +
             "and arena walls alike, so they all end at the same height and the world reads as " +
             "bottomless. One number for the lot; nothing overrides it.")]
    [Min(0f)] public float     generatedDrop = 8f;

    public RiverProfile       defaultRiverProfile = new RiverProfile();
    public List<RiverProfile> riverProfiles       = new();

    [Tooltip("Shape every pool takes unless it overrides it.")]
    public PoolShape          defaultPoolShape    = new PoolShape();

    [Tooltip("How far a pool's lead-in nodes stand out past the point its rivers are cut off " +
             "at (the rim, plus the collar). 0 puts them right on the cut.")]
    [Min(0f)] public float    poolLeadInDistance  = 0.5f;

    [Tooltip("How far an arena's lead-in nodes stand out from the entrance they lead to. " +
             "0 puts them right on the entrance.")]
    [Min(0f)] public float    arenaLeadInDistance = 0.5f;

    [Tooltip("How far a junction's lead-in nodes stand out past the junction's mouth — along " +
             "the river from the edge of the opening, and along the branch from where it is " +
             "cut off. 0 puts them right on the edge.")]
    [Min(0f)] public float    junctionLeadInDistance = 0.5f;

    [Tooltip("Shape every arena wall takes unless it overrides it.")]
    public ArenaWallProfile   defaultArenaWall    = new ArenaWallProfile();

    [Tooltip("Shape every entrance archway takes unless its arena overrides it.")]
    public ArenaArchwayProfile defaultArchway     = new ArenaArchwayProfile();

    [Tooltip("Shape every archway's door takes unless its arena overrides it.")]
    public ArenaDoorProfile    defaultDoor        = new ArenaDoorProfile();

    [Tooltip("The door preset last loaded onto the default shape or saved from it. Its settings " +
             "are copied, not shared, so editing them here does not touch the preset.")]
    public ArenaDoorPreset     defaultDoorPreset;

    [Tooltip("Where every entrance prefab stands inside its archway, unless that entrance " +
             "overrides it. Measured in the archway's own frame.")]
    public ArenaEntranceAlignment defaultEntranceAlignment = new ArenaEntranceAlignment();

    [Tooltip("How far a river pushes INTO an arena past its wall's inner face. Every run is put " +
             "on that face first — carried on when it stops short, pulled back when its arena is " +
             "wide enough to have swallowed it — so 0 leaves them all ending flush with the " +
             "inside of the wall, having filled the archway on the way through.")]
    [Min(0f)] public float arenaRunOverlap = 0f;

    public float      splineInstantiateSpacing = 0.15f;
    public float      arenaHeadOffset          = 5f;
    public float      shopHeadOffset           = 5f;

    // Landscape tile settings
    public GameObject landscapeTilePrefab;
    public float landscapeTileSize = 41.62f;
    public int landscapeTilesX = 5;
    public int landscapeTilesZ = 5;

    [Tooltip("Grid squares along each side of every landscape tile. The hills are raised in the " +
             "shader, so more squares give smoother hill shapes at the cost of more vertices.")]
    [Range(1, 100)] public int landscapeTileSubdivisions = 20;

    public Vector2 landscapeOffset = new Vector2(-97.9f, -130.9f);
    public float landscapeWorldY = -1.65f;

    [Tooltip("Lifts or drops the whole tile family off the World Y base. Hill points and dips " +
             "keep their heights measured from the tile base, so the landscape shape rides with " +
             "the tiles unchanged.")]
    public float landscapeHeightOffset = 0f;

    [Tooltip("How fine the rocky noise on hills is — its frequency across the world. Only hills " +
             "with a Rocky Noise amount show it.")]
    public float landscapeNoiseScale = 0.25f;

    /// <summary>
    /// How wide the tile family is, corner to corner on its longer side. Tiles are laid from
    /// their pivot outward, so the family runs from the offset for Tiles x Tile Size, and this
    /// is what the fog sheet is sized to — one square that covers the whole landscape.
    /// </summary>
    public float LandscapeSpan => Mathf.Max(
        landscapeTileSize * Mathf.Max(landscapeTilesX, 0),
        landscapeTileSize * Mathf.Max(landscapeTilesZ, 0));

    /// <summary>
    /// The middle of the tile family, at the tile surface. Tile meshes are corner-origin, so the
    /// centre is half a family past the offset rather than the offset itself.
    /// </summary>
    public Vector3 LandscapeCentre => new Vector3(
        landscapeOffset.x + landscapeTileSize * Mathf.Max(landscapeTilesX, 0) * 0.5f,
        landscapeWorldY + landscapeHeightOffset,
        landscapeOffset.y + landscapeTileSize * Mathf.Max(landscapeTilesZ, 0) * 0.5f);

    // ─────────────────────────────────────────────
    // FOG
    // One map for the whole world, the same arrangement GridData uses inside a level: a switch
    // and a map, with no fallback behind them. The map is the only thing that decides where fog
    // sits, so fog on with no map is a world that gets no fog and says so.
    //
    // Out here the fog is decoration hovering over the landscape rather than weather lying on
    // water, so the sheet is sized and placed from the landscape tiles above, not from a wave
    // plane — there is no wave plane in a level select scene.
    // ─────────────────────────────────────────────

    [Header("Fog")]
    [Tooltip("Fog over this world at all. On requires a Fog Map — there is no fallback, so on " +
             "with no map means no fog.")]
    public bool fogEnabled = false;

    [Tooltip("What the fog is and how much of it there is, for the whole world. Authored in " +
             "Waves > Fog Arena Map, the same asset the interior levels use.")]
    public FogMap fogMap;

    [Tooltip("How far the fog sheet hovers above the landscape surface. The landscape has hills " +
             "standing off its base, so this is the one number that decides whether fog lies in " +
             "the valleys with the hills through it or floats clear over the top of everything.")]
    public float fogHeightOffset = 2f;

    [Tooltip("Single: one fog sheet over the whole landscape at Height Offset — the default. " +
             "Zones: a sheet per authored fog zone, each at its own height, fading in and out " +
             "as the boat enters and leaves the zone's bounds. Either way the fog itself is " +
             "generated once and every sheet shows the same fog.")]
    public FogPlaneMode fogPlaneMode = FogPlaneMode.Single;

    [Tooltip("Seconds a fog zone's sheet takes to fade in when the boat enters its bounds, and " +
             "out when it leaves. Shared by every zone.")]
    [Min(0f)] public float fogZoneFadeTime = 1f;

    [Tooltip("The fog zones, used when Fog Plane Mode is Zones.")]
    public List<DesignerFogZone> fogZones = new();

    // ─────────────────────────────────────────────
    // AESTHETICS
    // How the world LOOKS, as opposed to what is generated in it. These are driven into the
    // shaders as globals, not written onto any one material — every generated piece out here
    // (hills, river runs) reads the same uniform, so the look is authored once for the world.
    //
    // The shaders declare them as bare $Globals with no property block entry, so there is no
    // material value sitting behind them: until something pushes the number the shaders see 0,
    // and a material reimport puts them back to 0. That push is
    // LevelSelectDataController.Update in play mode, and LevelSelectAestheticsPump out of it.
    // ─────────────────────────────────────────────

    [Header("Aesthetics")]
    [Tooltip("Where the white gradient sits on the landscape hills and the river runs — the " +
             "_WhiteGradientPos global. Nothing else drives it, so what is set here is what the " +
             "whole world uses.")]
    public float whiteGradientPos = 0f;

    // Two presets rather than one, because the water and the stone it runs through are two
    // materials on two pieces of geometry, authored in two tuners against two different sets of
    // questions. Held together on one asset they only had somewhere to drift: each tuner
    // remembers its own active preset, so the two halves ended up tuned on two different copies
    // of the same combined asset, with the world reading one of them and a live tuner hiding the
    // difference until a build.
    //
    // FormerlySerializedAs because this field was riverPreset, holding both. A world already
    // pointed at one of those combined assets keeps that reference, and the asset it points at
    // was converted to a water preset in place.

    [Tooltip("How the WATER looks — the ripple lines along the rivers' banks and in rings out " +
             "of a pool's middle. Authored in Tools > Waves > Level Select River Tuner. No " +
             "preset means no ripples: these are bare globals, so there is nothing behind them " +
             "to fall back on.")]
    [FormerlySerializedAs("riverPreset")]
    public LevelSelectRiverWaterPreset waterPreset;

    [Tooltip("How the STRUCTURES look — the generated stone the rivers run through: the colour " +
             "of each part of a run, its grain, the dark along its seams and the white off the " +
             "waterline. Authored in Tools > Waves > Level Select Run Shading Tuner. No preset " +
             "means bare unlit stone, for the same reason as above.")]
    public LevelSelectRiverStructurePreset structurePreset;

    [Tooltip("How the LANDSCAPE hills look — three stone variants and which part of the landscape " +
             "(ground, tops, cliffs, holes) wears each. Authored in Tools > Waves > Level Select " +
             "Landscape Tuner. No preset means plain unlit stone.")]
    public LevelSelectLandscapePreset landscapePreset;

    [Tooltip("The white fade out from the boat, shared by the river runs, the landscape and the " +
             "spikes through the WhiteFade subgraph. Tuned in the White Fade foldout of the Run " +
             "Shading Tuner or the Landscape Tuner. No preset means no fade.")]
    public LevelSelectWhiteFadePreset whiteFadePreset;

    [Tooltip("This world's sky — a gradient and drifting clouds, authored in Tools > Level Design " +
             "> Skybox Generator. Picking one in the designer puts its material on the scene's " +
             "skybox (saved with the scene). None leaves the scene's skybox as it is.")]
    public SkyboxPreset skyboxPreset;

    // No longer the light: that is the scene's LevelSelectLight object now. Kept only as where
    // Spawn Light first stands the object, so a world spawns its light exactly where it was lit
    // from before.
    [HideInInspector]
    public Vector3 lightPosition = new Vector3(362.3f, 259.8f, -3.2f);

    [Tooltip("Draws the frame each water surface was generated in instead of the water itself — " +
             "which way its lines are lying, and how much of it is really there under an " +
             "overlap. A way of reading the water rather than a way it looks, so it lives here " +
             "and not on the preset. Needs the RiverEdgeRipples subgraph's Debug and DebugMix " +
             "outputs wired into the water graph. Off for anything but tuning.")]
    public RiverRippleDebugView rippleDebugView = RiverRippleDebugView.Off;

    [Tooltip("How far from the camera the river runs fade out — the _DistanceFadeRadius the run " +
             "shader reads. Unlike the gradient above this one is EXPOSED on RiverRunShader, so " +
             "the run material carries its own copy and that copy wins over a global. Written " +
             "both ways below; the number here is the authority either way.")]
    public float distanceFadeRadius = 10f;

    private static readonly int WhiteGradientPosId    = Shader.PropertyToID("_WhiteGradientPos");
    private static readonly int DistanceFadeRadiusId  = Shader.PropertyToID("_DistanceFadeRadius");

    /// <summary>
    /// Publishes this world's look to the shaders. Called every frame rather than once: these
    /// are bare globals with nothing behind them, so a set-once push is undone by the next
    /// material or shader reimport and never comes back.
    /// </summary>
    public void ApplyAesthetics()
    {
        Shader.SetGlobalFloat(WhiteGradientPosId, whiteGradientPos);

        // Dual-written, and for the opposite reason to the usual one. _DistanceFadeRadius is an
        // exposed property on RiverRunShader, so the run material holds a serialized value and a
        // material value BEATS a global — the material write is the one that lands today. The
        // global write is what carries on working if the property is ever flipped to Global on
        // the graph, at which point the material write becomes a silent no-op rather than a bug.
        Shader.SetGlobalFloat(DistanceFadeRadiusId, distanceFadeRadius);
        if (riverMaterial != null) riverMaterial.SetFloat(DistanceFadeRadiusId, distanceFadeRadius);

        // The river's own look, from this world's two presets — the water from one and the
        // stone from the other — or a tuner's numbers instead while it is driving, which is
        // decided inside each Push rather than here so both routes stay one route into the
        // globals.
        RiverEdgeRippleSettings.Push(waterPreset     != null ? waterPreset.edgeRipples    : null);
        RiverSoulZoneSettings.Push(waterPreset       != null ? waterPreset.soulZone       : null);
        RiverRunShadingSettings.Push(structurePreset != null ? structurePreset.runShading : null);
        LandscapeShadingSettings.Push(landscapePreset != null ? landscapePreset.landscapeShading : null);
        WhiteFadeSettings.Push(whiteFadePreset != null ? whiteFadePreset.whiteFade : null);

        // The one light the runs, the hills and the spikes are turned against — the scene's
        // LevelSelectLight object, its position and strength both.
        LevelSelectLight.Push();

        // Where the tile surface sits, which the landscape's Tops and Holes are measured from.
        LandscapeShadingSettings.PushBaseY(landscapeWorldY + landscapeHeightOffset);

        // Where the water lies, which is what places the waterline band on the stone. Pushed from
        // the world rather than carried on the preset: it is already authored once as Water Level,
        // and a second copy would only give the band somewhere to drift off the water.
        RiverRunShadingSettings.PushWaterDepth(WaterSurfaceDrop);

        // Separate from the preset on purpose: the debug view belongs to whoever is looking, not
        // to how the world is meant to look, so no preset can carry it on by accident. The tuner
        // overrides it while its window is open, which is decided inside PushDebug.
        RiverEdgeRippleSettings.PushDebug(rippleDebugView);
    }

    // Scene-level references (persistent — survive Respawn)
    public LandscapeTool       landscapeTool;

    // Extended scene object references — managed by Scene Deploy panel
    public LevelSelectDataController   dataController;
    public FogFieldManager             fogFieldManager;
    public LevelSelectBoatControl      boatControl;
    public LevelSelectCameraController cameraController;
    public SoulDisplaySlotManager      soulDisplaySlotManager;
    public PauseManager                pauseManager;
    public PauseMenuUI                 pauseMenuUI;
    public PortalConfirmUI             portalConfirmUI;
    public SoulsOnBoatDisplayManager   soulsOnBoatDisplayManager;
    public VideoPlayerController       videoPlayerController;

    // Script object prefabs (from Prefab/ScriptPrefabs)
    public GameObject cameraPrefab;
    public GameObject dataControllerPrefab;
    public GameObject pauseManagerScriptPrefab;
    public GameObject portalConfirmUIScriptPrefab;
    public GameObject soulsOnBoatDisplayScriptPrefab;
    public GameObject arenaSoulsWindowPrefab;
    public GameObject videoPlayerControllerPrefab;
    public GameObject boatPrefab;

    // UI Canvas prefabs
    public GameObject canvasParentPrefab;
    public GameObject pauseMenuPrefab;
    public GameObject boatHUDPrefab;
    public GameObject soulsOnBoatDisplayPrefab;
    public GameObject orbsCounterPrefab;
    public GameObject shopTooltipPrefab;

    // Music
    [Tooltip("Plays from the start of the level select until the boat reaches a node with Audio on.")]
    [FormerlySerializedAs("windLoop")] public AudioClip startingAudio;
    [Tooltip("Off = the starting audio plays through once.")]
    public bool  startingAudioLooped = true;
    [Tooltip("Seconds the starting audio takes to fade in. 0 = straight in.")]
    [Min(0f)] public float startingAudioFadeIn = 0f;

    // Scene-level music reference
    public LevelSelectMusicController musicController;

    // Linked scene
    public string targetScenePath = "";

    // Where a new world's main river is seeded from (kept under its old name so saved data still loads)
    public Vector3   openingSequenceStartPos   = Vector3.zero;

    // Node the boat starts at on a save that has never seen the map. Empty → head of the main river.
    public string boatStartNodeId = "";

    // Node the boat starts at when the designer's "Play From Playtest Position" launches the
    // scene. Editor playtesting only — a build never reads it. Empty → none set.
    public string playtestBoatNodeId = "";

    // Canvas state
public float canvasOriginX      = 0f;
    public float canvasOriginZ      = 0f;
    public float canvasUnitsPerPixel = 0.1f;
    public float canvasWorldY        = 0f;


    // ─────────────────────────────────────────────
    // SOUL ROUTES
    // The journey souls make across the map: an origin pool, then an ordered chain of legs
    // alternating river stretches and levels. Shared, not per-soul — every fish on a route
    // follows the same chain, and the player opens it leg by leg.
    //
    // A leg is a REFERENCE to something the designer already holds (a path, an arena), so a
    // route adds no geometry of its own and cannot drift out of step with the river.
    // ─────────────────────────────────────────────

    [Serializable]
    public class SoulRoutePoint
    {
        public enum PointKind { Node, Arena }

        [Tooltip("Node = a point on the river. Arena = a level the souls pass through.")]
        public PointKind kind = PointKind.Node;

        [Tooltip("DesignerNode.id for a river point, or DesignerArena.nodeId for an arena.")]
        public string nodeId = "";

        [Tooltip("Arena only: index into GridData.entrances for the door souls arrive at. -1 = unset.")]
        public int entranceIn = -1;

        [Tooltip("Arena only: index into GridData.entrances for the door souls leave by. -1 = unset.")]
        public int entranceOut = -1;

        [Tooltip("Authored but unread in v1 — what the player must do to open the stretch that " +
                 "ENDS at this point. Empty means it opens as soon as the point before it does.")]
        public string gateCondition = "";
    }

    [Serializable]
    public class SoulRoute
    {
        public string routeId = "";
        public string displayName = "New Route";

        [Tooltip("The node the route's origin pool sits on — where these souls begin on the map.")]
        public string originNodeId = "";

        [Tooltip("World-unit radius of the origin pool.")]
        public float originRadius = 2f;

        [Tooltip("World-unit half-width of the band drawn along the route — the swim band, " +
                 "the same meaning as a soul zone's radius inside a level.")]
        public float bandWidth = 0.6f;

        public Color editorColor = new Color(0.45f, 0.85f, 1f);

        [Tooltip("Soul identities waiting in this route's origin pool — the souls that begin here " +
                 "and travel this route. Same authoring idea as a GridData soul zone's souls list: " +
                 "one slot per soul, filled by Auto-Assign or by hand.")]
        public List<SoulData> souls = new List<SoulData>();

        [Tooltip("The points the fish take, in order. Between two points that share a river path " +
                 "the route follows that path's own nodes, so it hugs the river rather than " +
                 "cutting across it — and it stops at a junction until the next point is chosen.")]
        public List<SoulRoutePoint> points = new List<SoulRoutePoint>();
    }

    public List<SoulRoute> soulRoutes = new List<SoulRoute>();

    /// <summary>
    /// The node ids a soul route passes through, in travel order: the origin pool, then every
    /// authored point with the river's own nodes filled in between consecutive points that share
    /// a path. This is the ONE definition of a route's shape — the designer draws it and the
    /// runtime swims it, so the two cannot disagree. Callers turn ids into positions themselves.
    /// </summary>
    public List<string> BuildSoulRouteNodeIds(SoulRoute route)
    {
        var ids = new List<string>();
        if (route == null) return ids;

        if (!string.IsNullOrEmpty(route.originNodeId))
            ids.Add(route.originNodeId);

        for (int i = 0; i < route.points.Count; i++)
        {
            string nodeId = route.points[i].nodeId;
            if (string.IsNullOrEmpty(nodeId)) continue;

            if (i > 0)
            {
                var between = NodesBetween(route.points[i - 1].nodeId, nodeId);
                if (between != null) ids.AddRange(between);
            }

            ids.Add(nodeId);
        }

        return ids;
    }

    /// <summary>
    /// The river's own nodes strictly between two nodes, in order from <paramref name="a"/> —
    /// along the path carrying both, or on through river joins when a split put them on
    /// different rivers. Null when no river links them.
    /// </summary>
    public List<string> NodesBetween(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return null;

        var path = FindPathContaining(a, b);
        if (path != null) return NodesStrictlyBetween(path, a, b);

        return NodesAcrossJoins(a, b, new HashSet<string>());
    }

    private List<string> NodesAcrossJoins(string a, string b, HashSet<string> used)
    {
        if (riverJoins == null) return null;

        foreach (var join in riverJoins)
        {
            string j = join?.nodeId;
            if (string.IsNullOrEmpty(j) || j == a || j == b || used.Contains(j)) continue;

            var first = FindPathContaining(a, j);
            if (first == null) continue;

            used.Add(j);
            var next = FindPathContaining(j, b);
            var rest = next != null && next != first
                     ? NodesStrictlyBetween(next, j, b)
                     : NodesAcrossJoins(j, b, used);
            used.Remove(j);
            if (rest == null) continue;

            var list = NodesStrictlyBetween(first, a, j);
            list.Add(j);
            list.AddRange(rest);
            return list;
        }
        return null;
    }

    private static List<string> NodesStrictlyBetween(DesignerPath path, string a, string b)
    {
        var list = new List<string>();
        int from = path.nodeIds.IndexOf(a);
        int to   = path.nodeIds.IndexOf(b);
        if (from < 0 || to < 0 || from == to) return list;

        int step = to > from ? 1 : -1;
        for (int n = from + step; n != to; n += step)
            list.Add(path.nodeIds[n]);
        return list;
    }

    /// <summary>The first river path carrying both nodes, or null when they share none.</summary>
    public DesignerPath FindPathContaining(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return null;

        foreach (var path in paths)
            if (path.nodeIds.Contains(a) && path.nodeIds.Contains(b))
                return path;

        return null;
    }

    /// <summary>World position of a node by id, or Vector3.zero when it is unknown.</summary>
    public Vector3 NodeWorldPosition(string nodeId)
    {
        var node = nodes.Find(n => n.id == nodeId);
        return node != null ? node.worldPosition : Vector3.zero;
    }

    /// <summary>The river join set on a node, or null when it carries none.</summary>
    public DesignerRiverJoin RiverJoinAt(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || riverJoins == null) return null;
        return riverJoins.Find(j => j != null && j.nodeId == nodeId);
    }

    /// <summary>
    /// The two rivers meeting at a river join: the one ending on it and the one starting from
    /// it. False when the node is not a join, or no longer reads as one — a third river drawn
    /// to it, one of the two deleted or turned round — and it is then left alone.
    /// </summary>
    public bool RiverJoinPaths(string nodeId, out DesignerPath ending, out DesignerPath starting)
    {
        ending = starting = null;
        if (RiverJoinAt(nodeId) == null) return false;

        foreach (var p in paths)
        {
            if (p == null || p.nodeIds.Count < 2 || !p.nodeIds.Contains(nodeId)) continue;

            if      (p.nodeIds[p.nodeIds.Count - 1] == nodeId && ending   == null) ending   = p;
            else if (p.nodeIds[0]                   == nodeId && starting == null) starting = p;
            else return false;
        }
        return ending != null && starting != null && ending != starting;
    }

    /// <summary>The rim node set on a node, or null when it carries none.</summary>
    public DesignerRimNode RimNodeAt(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || rimNodes == null) return null;
        return rimNodes.Find(r => r != null && r.nodeId == nodeId);
    }

    /// <summary>The walkway set on a node — on or off — or null when it has never had one.</summary>
    public DesignerWalkway WalkwayAt(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || walkways == null) return null;
        return walkways.Find(w => w != null && w.nodeId == nodeId);
    }

    /// <summary>The River Walkway 2 set on a node — on or off — or null when it has never had one.</summary>
    public DesignerWalkway Walkway2At(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || walkways2 == null) return null;
        return walkways2.Find(w => w != null && w.nodeId == nodeId);
    }

    /// <summary>The bridge across the river at a node, or null when it carries none.</summary>
    public DesignerBridge BridgeAt(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || bridges == null) return null;
        return bridges.Find(b => b != null && b.nodeId == nodeId);
    }

    /// <summary>The pool sitting on a node, or null when it carries none.</summary>
    public DesignerPool PoolAt(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return null;
        return pools.Find(p => p != null && p.nodeId == nodeId);
    }

    /// <summary>The pool a node is a lead-in of, or null when it is not one.</summary>
    public DesignerPool PoolOfLeadIn(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return null;
        return pools.Find(p => p?.leadIns != null && p.leadIns.Exists(l => l.nodeId == nodeId));
    }

    /// <summary>The arena a node is a lead-in of, or null when it is not one.</summary>
    public DesignerArena ArenaOfLeadIn(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return null;
        return arenas.Find(a => a?.leadIns != null && a.leadIns.Exists(l => l.nodeId == nodeId));
    }

    /// <summary>The junction a node is a lead-in of, or null when it is not one.</summary>
    public DesignerJunction JunctionOfLeadIn(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return null;
        return junctions.Find(j => j?.leadIns != null && j.leadIns.Exists(l => l.nodeId == nodeId));
    }

    /// <summary>Flat world direction of a compass point.</summary>
    public static Vector3 CompassDirection(Compass c) => c switch
    {
        Compass.North => Vector3.forward,
        Compass.East  => Vector3.right,
        Compass.South => Vector3.back,
        _             => Vector3.left,
    };

    /// <summary>
    /// Every river meeting a pool, one entry per mouth, paired with the node it comes in from.
    /// A path meets a pool on every side of the pool's node it has a neighbour: once where it
    /// starts or ends there, twice where it is drawn straight THROUGH the pool — in on one side,
    /// out on the other. Any number of rivers can meet the same pool, and none of them owns it.
    /// </summary>
    public List<(DesignerPath path, string neighbourId)> PathsAtPool(DesignerPool pool)
    {
        var arrivals = new List<(DesignerPath, string)>();
        if (pool == null || string.IsNullOrEmpty(pool.nodeId)) return arrivals;

        foreach (var path in paths)
        {
            int n = path.nodeIds.Count;
            if (n < 2) continue;

            for (int i = 0; i < n; i++)
            {
                if (path.nodeIds[i] != pool.nodeId) continue;
                if (i > 0)     arrivals.Add((path, path.nodeIds[i - 1]));
                if (i < n - 1) arrivals.Add((path, path.nodeIds[i + 1]));
            }
        }

        return arrivals;
    }

    /// <summary>
    /// The river a pool takes its shape from: the one that runs into it. A pool is a widening
    /// of the river it sits on, so it is never authored separately — the rim width and the
    /// depths come from whichever river arrives, and the two always mate.
    /// </summary>
    public string PoolRiverName(DesignerPool pool)
    {
        var arrivals = PathsAtPool(pool);
        return arrivals.Count > 0 ? arrivals[0].path.riverName : null;
    }

    /// <summary>
    /// Shape for a pool, falling back to the default when it does not override it.
    ///
    /// The three numbers stay on the pool itself whether it overrides or not, so the ones it
    /// was last authored with are still there to come back to when the override goes back on.
    /// </summary>
    public PoolShape PoolShapeFor(DesignerPool pool)
    {
        if (pool == null) return defaultPoolShape;
        if (!pool.overrideShape) return defaultPoolShape;

        return new PoolShape
        {
            poolRadius   = pool.poolRadius,
            islandRadius = pool.islandRadius,
            floorDepth   = pool.floorDepth,
        };
    }

    /// <summary>Writes a shape back onto a pool, so an override starts from what it had.</summary>
    public static void ApplyPoolShape(DesignerPool pool, PoolShape shape)
    {
        if (pool == null || shape == null) return;
        pool.poolRadius   = shape.poolRadius;
        pool.islandRadius = shape.islandRadius;
        pool.floorDepth   = shape.floorDepth;
    }

    /// <summary>The main river — the trunk every other river hangs off.</summary>
    public DesignerPath MainRiver()
        => paths.Find(p => p != null && p.segmentType == SegmentType.MainRiver)
        ?? paths.Find(p => p != null && p.segmentId == "MainRiver");

    /// <summary>
    /// Where the boat is put down on a save that has never seen the map: the head of the main
    /// river, or the water of the pool that head sits in when it has one.
    ///
    /// The pool has to be answered for here rather than left to the river's own curve. A river
    /// meeting a pool is cut off outside that pool's rim, so the head of the curve stands out
    /// on the river with the pool still ahead of it — put down there, the boat starts beside
    /// the pool rather than on it.
    ///
    /// <paramref name="inPool"/> says which of the two came back, so a caller that only wants
    /// to know about the pool can leave the river to the spline it already reads.
    /// </summary>
    public bool TryGetBoatStart(out Vector3 position, out Vector3 forward, out bool inPool)
    {
        position = Vector3.zero;
        forward  = Vector3.forward;
        inPool   = false;

        if (!TryGetBoatStartNode(out string startId, out string aheadId)) return false;

        return TryGetBoatPose(startId, aheadId, out position, out forward, out inPool);
    }

    /// <summary>
    /// Where the boat is put down standing on <paramref name="nodeId"/>, facing the way
    /// <see cref="TryGetBoatStartNode"/> would face it there — the playtest start. False when
    /// that node no longer stands on a river.
    /// </summary>
    public bool TryGetBoatPoseAtNode(string nodeId, out Vector3 position, out Vector3 forward, out bool inPool)
    {
        position = Vector3.zero;
        forward  = Vector3.forward;
        inPool   = false;

        if (!TryGetNodeAhead(nodeId, out string aheadId)) return false;

        return TryGetBoatPose(nodeId, aheadId, out position, out forward, out inPool);
    }

    /// <summary>Whether a playtest start node has been set and still stands on a river.</summary>
    public bool HasPlaytestBoatNode => TryGetNodeAhead(playtestBoatNodeId, out _);

    // The boat on startId facing aheadId, or on the water of the pool startId sits in.
    bool TryGetBoatPose(string startId, string aheadId, out Vector3 position, out Vector3 forward, out bool inPool)
    {
        position = Vector3.zero;
        forward  = Vector3.forward;
        inPool   = false;

        var head = nodes.Find(n => n != null && n.id == startId);
        if (head == null) return false;

        position = head.worldPosition;

        // Facing the way the river runs from here, which is the way the boat sets off.
        Vector3 dir = NodeWorldPosition(aheadId) - position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 1e-6f) forward = dir.normalized;

        var pool = PoolAt(startId);
        if (pool == null) return true;

        inPool = true;

        // An open bowl is started in the middle of. One with a plinth has no middle to sit in,
        // so the boat waits on the ring it would travel, at the mouth its river leaves by.
        var   shape = PoolShapeFor(pool);
        float ring  = RiverMeshBuilder.PoolChannelRadius(shape.poolRadius, shape.islandRadius);
        if (ring > 0.001f) position += forward * ring;

        return true;
    }

    /// <summary>Whether a start node has been set by hand and still stands on a river.</summary>
    public bool HasBoatStartNode
        => !string.IsNullOrEmpty(boatStartNodeId) &&
           paths.Exists(p => p != null && p.nodeIds.Count >= 2 && p.nodeIds.Contains(boatStartNodeId));

    /// <summary>
    /// The node the boat starts on and the one it faces. The hand-set start node when there is
    /// one, otherwise the head of the main river.
    ///
    /// A hand-set node faces the next node along the first river it stands on — the main river
    /// if it is on it — or back along that river when it is the last node there.
    /// </summary>
    public bool TryGetBoatStartNode(out string startId, out string aheadId)
    {
        startId = null;
        aheadId = null;

        if (TryGetNodeAhead(boatStartNodeId, out aheadId))
        {
            startId = boatStartNodeId;
            return true;
        }

        var river = MainRiver();
        if (river == null || river.nodeIds.Count < 2) return false;

        startId = river.nodeIds[0];
        aheadId = river.nodeIds[1];
        return true;
    }

    /// <summary>
    /// The node a boat standing on <paramref name="nodeId"/> faces: the next node along the first
    /// river it stands on — the main river if it is on it — or back along that river when it is
    /// the last node there. False when the node stands on no river.
    /// </summary>
    public bool TryGetNodeAhead(string nodeId, out string aheadId)
    {
        aheadId = null;
        if (string.IsNullOrEmpty(nodeId)) return false;

        var main = MainRiver();
        var on   = main != null && main.nodeIds.Count >= 2 && main.nodeIds.Contains(nodeId)
            ? main
            : paths.Find(p => p != null && p.nodeIds.Count >= 2 && p.nodeIds.Contains(nodeId));
        if (on == null) return false;

        int i   = on.nodeIds.IndexOf(nodeId);
        aheadId = i < on.nodeIds.Count - 1 ? on.nodeIds[i + 1] : on.nodeIds[i - 1];
        return true;
    }

    /// <summary>
    /// Archway shape for an arena, falling back to the default when it does not override it.
    ///
    /// Still unresolved — the width and thickness it inherits from the river, and the depth it
    /// inherits from the wall, are settled by <see cref="ArenaArchwayProfile.Resolve"/> at the
    /// point of building, where the river arriving at that particular entrance is known.
    /// </summary>
    public ArenaArchwayProfile ArchwayFor(DesignerArena arena)
    {
        if (arena == null) return defaultArchway;
        return (arena.overrideArchway ? arena.archwayProfile : defaultArchway) ?? defaultArchway;
    }

    /// <summary>
    /// Door shape for an arena, falling back to the default when it does not override it.
    ///
    /// Still unresolved — the height and base width it inherits from the arch it stands in are
    /// settled by <see cref="ArenaDoorProfile.Resolve"/> at the point of building, where the
    /// arch at that particular entrance is known.
    /// </summary>
    public ArenaDoorProfile DoorFor(DesignerArena arena)
    {
        if (arena == null) return defaultDoor;
        return (arena.overrideDoor ? arena.doorProfile : defaultDoor) ?? defaultDoor;
    }

    /// <summary>
    /// How far the water surface sits below the rim top. 0 when no water is generated, so the
    /// rim top is the surface and everything is measured from there either way.
    /// </summary>
    public float WaterSurfaceDrop => waterFilled ? waterLevel : 0f;

    /// <summary>
    /// Total height of a run, rim top down to its underside. A run is built from its rim top and
    /// a wall from the water surface, so the run takes the water level on top of
    /// <see cref="generatedDrop"/> and the two undersides land at the same height.
    /// </summary>
    public float RunDepth => Mathf.Max(0.001f, generatedDrop + WaterSurfaceDrop);

    /// <summary>
    /// The wall shape an arena is authored against, exactly as written — no radius settled.
    /// A radius of 0 here means the arena was never given one, which is what tells the designer
    /// to keep whatever size the arena already carries.
    ///
    /// The drop is not authored here at all: it is stamped from <see cref="generatedDrop"/>, so
    /// a wall ends at the same height as every other generated piece.
    /// </summary>
    public ArenaWallProfile ArenaWallShapeOf(DesignerArena arena)
    {
        var shape = (arena == null || !arena.overrideWall ? defaultArenaWall : arena.wallProfile)
                 ?? defaultArenaWall;
        shape.drop = generatedDrop;
        return shape;
    }

    /// <summary>
    /// Wall shape for an arena, falling back to the default when it does not override it.
    ///
    /// A radius of 0 means "the radius this arena already carries" — arenas were sized before
    /// the wall had a shape of its own, and that size is still what the entrance nodes orbit.
    /// </summary>
    public ArenaWallProfile ArenaWallFor(DesignerArena arena)
    {
        var shape = ArenaWallShapeOf(arena);
        if (shape == null) return defaultArenaWall;

        if (shape.radius > 0.05f) return shape;

        var settled = shape.Clone();
        settled.radius = arena.arenaRadius > 0.05f ? arena.arenaRadius : 10f;
        return settled;
    }

    /// <summary>
    /// The arena an entrance node belongs to — its primary node or any of the secondary
    /// entrances orbiting it — or null when the node is not an arena entrance at all.
    /// </summary>
    public DesignerArena ArenaAtEntrance(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return null;
        return arenas.Find(a => a != null &&
            (a.nodeId == nodeId ||
             (a.secondaryEntrances != null &&
              a.secondaryEntrances.Exists(e => e != null && e.nodeId == nodeId))));
    }

    /// <summary>
    /// How far the river arriving at one entrance pushes into the arena past the wall's inner
    /// face. How much is wanted varies from arena to arena and from one side of an arena to
    /// another, so any entrance can be given its own number; the rest follow
    /// <see cref="arenaRunOverlap"/>.
    /// </summary>
    public float RunOverlapFor(DesignerArena arena, string entranceNodeId)
    {
        if (arena == null) return arenaRunOverlap;

        if (arena.nodeId == entranceNodeId)
            return arena.overrideRunOverlap ? arena.runOverlap : arenaRunOverlap;

        var entrance = arena.secondaryEntrances?.Find(e => e != null && e.nodeId == entranceNodeId);
        if (entrance != null)
            return entrance.overrideRunOverlap ? entrance.runOverlap : arenaRunOverlap;

        return arenaRunOverlap;
    }

    /// <summary>
    /// Where one entrance's door stands inside its archway. Every entrance carries its own;
    /// <see cref="defaultEntranceAlignment"/> is only a fallback for one that has none.
    /// The override flags are no longer read, and are kept only so saved data loads.
    /// </summary>
    public ArenaEntranceAlignment EntranceAlignmentFor(DesignerArena arena, string entranceNodeId)
    {
        if (arena == null) return defaultEntranceAlignment;

        if (arena.nodeId == entranceNodeId)
            return arena.entranceAlignment ?? defaultEntranceAlignment;

        var entrance = arena.secondaryEntrances?.Find(e => e != null && e.nodeId == entranceNodeId);
        if (entrance != null)
            return entrance.alignment ?? defaultEntranceAlignment;

        return defaultEntranceAlignment;
    }

    /// <summary>
    /// How far out past one door the boat appears when the player leaves the level by it. Every
    /// door carries its own — the primary on the arena, the rest on their entrance.
    /// </summary>
    public float BoatSpawnDistanceFor(DesignerArena arena, string entranceNodeId)
    {
        if (arena == null) return 0f;
        if (arena.nodeId == entranceNodeId) return Mathf.Max(0f, arena.boatSpawnDistance);

        var entrance = arena.secondaryEntrances?.Find(e => e != null && e.nodeId == entranceNodeId);
        return entrance != null ? Mathf.Max(0f, entrance.boatSpawnDistance) : 0f;
    }

    /// <summary>
    /// Shape for a named river, falling back to the default when it has no entry of its own.
    ///
    /// The depth is not authored per river: it is stamped from <see cref="RunDepth"/>, so every
    /// run and every pool ends at the same height as the arena walls.
    /// </summary>
    public RiverProfile ProfileFor(string riverName)
    {
        if (defaultRiverProfile == null) defaultRiverProfile = new RiverProfile();

        var profile = defaultRiverProfile;
        if (!string.IsNullOrEmpty(riverName))
        {
            var match = riverProfiles.Find(p => p != null && p.riverName == riverName);
            if (match != null) profile = match;
        }

        profile.depth = RunDepth;
        return profile;
    }
}
