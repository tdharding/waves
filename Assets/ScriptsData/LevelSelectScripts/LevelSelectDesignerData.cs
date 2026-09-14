using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "LevelSelectDesignerData", menuName = "Level Select/Designer Data")]
public class LevelSelectDesignerData : ScriptableObject
{
    public enum NodeType { Waypoint, JunctionSplit, ArenaEnd, ShopEnd }
    public enum SegmentType { MainRiver, PrimaryBranch, Secondary, Tertiary }

    [Serializable]
    public class LandscapeHillPoint
    {
        public string  id;
        public Vector2 positionXZ;
        public float   scale  = 5f;
        public float   height = 1f;
    }

    [Serializable]
    public class DesignerNode
    {
        public string id;
        public Vector3 worldPosition;
        public NodeType type;
    }

    [Serializable]
    public class DesignerPath
    {
        public string pathId;
        public string segmentId;
        public List<string> nodeIds = new();
        [FormerlySerializedAs("isTopPath")]    public bool isLeftPath;
        [FormerlySerializedAs("isBottomPath")] public bool isRightPath;
        public SegmentType segmentType;
        public string riverName;
        public bool leadsToArena;
        public bool arenaIsAtEnd;
        public bool extrudeOnExit;
        public bool tJunctionBidirectional;
        public string arenaGridDataGuid;
        public Color editorColor = Color.cyan;
        [Tooltip("Multiplier on Bezier tangent length. 1 = default smoothness, lower = tighter curves, higher = more sweeping.")]
        public float curveStrength = 1f;
        [Tooltip("Knots inserted per segment between nodes. Higher = spline follows path more closely.")]
        public int curveSubdivisions = 2;
    }

    [Serializable]
    public class DesignerJunction
    {
        public string junctionId;
        public string nodeId;
        public List<string> pathIds = new();
        public string riverPathId;   // path the junction was clicked on (mid-stream)
        public string branchPathId;  // path extruded from the junction node (branch)
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

        // One entry per additional entrance beyond the primary (nodeId above)
        public List<DesignerArenaEntrance> secondaryEntrances = new List<DesignerArenaEntrance>();
    }

    /// <summary>
    /// A circular basin on the river, built by revolving the same cross-section a run is swept
    /// along. <see cref="islandRadius"/> is what separates the shapes the designer draws: 0 for
    /// an open pool, anything larger for a pool with a centre — and a pool that several rivers
    /// meet is a roundabout. Any path starting or ending on <see cref="nodeId"/> arrives at it.
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
    }

    public List<LandscapeHillPoint> hillPoints = new();

    public List<DesignerNode>     nodes     = new();
    public List<DesignerPath>     paths     = new();
    public List<DesignerJunction> junctions = new();
    public List<DesignerShop>     shops     = new();
    public List<DesignerObstacle> obstacles = new();
    public List<DesignerArena>    arenas    = new();
    public List<DesignerPool>     pools     = new();

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

    [Tooltip("Water is generated as permanent mesh sitting in every river channel. Untick to " +
             "go back to the SplineExtrude water that unfolds ahead of the boat.")]
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

    [Tooltip("How far a branch's water carries on PAST the river it leaves, over that river's " +
             "own water, before fading out. 0 butts it onto the channel edge as before. " +
             "Geometry — needs a Rebuild Runs.")]
    [Min(0f)] public float     waterBranchOverlap = 0f;

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

    [Tooltip("Shape every arena wall takes unless it overrides it.")]
    public ArenaWallProfile   defaultArenaWall    = new ArenaWallProfile();

    [Tooltip("Shape every entrance archway takes unless its arena overrides it.")]
    public ArenaArchwayProfile defaultArchway     = new ArenaArchwayProfile();

    [Tooltip("Where every entrance prefab stands inside its archway, unless that entrance " +
             "overrides it. Measured in the archway's own frame.")]
    public ArenaEntranceAlignment defaultEntranceAlignment = new ArenaEntranceAlignment();

    [Tooltip("How far a river pushes INTO an arena past its wall's inner face. Every run is put " +
             "on that face first — carried on when it stops short, pulled back when its arena is " +
             "wide enough to have swallowed it — so 0 leaves them all ending flush with the " +
             "inside of the wall, having filled the archway on the way through.")]
    [Min(0f)] public float arenaRunOverlap = 0f;

    public float      junctionGapPadding      = 0f;
    public float      splineInstantiateSpacing = 0.15f;
    public int        curveSubdivisions        = 2;
    public float      arenaHeadOffset          = 5f;
    public float      shopHeadOffset           = 5f;

    // Landscape tile settings
    public GameObject landscapeTilePrefab;
    public float landscapeTileSize = 41.62f;
    public int landscapeTilesX = 5;
    public int landscapeTilesZ = 5;
    public Vector2 landscapeOffset = new Vector2(-97.9f, -130.9f);
    public float landscapeWorldY = -1.65f;

    [Tooltip("Lifts or drops the whole tile family off the World Y base. Hill points and dips " +
             "keep their heights measured from the tile base, so the landscape shape rides with " +
             "the tiles unchanged.")]
    public float landscapeHeightOffset = 0f;

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
        RiverRunShadingSettings.Push(structurePreset != null ? structurePreset.runShading : null);

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
    public SplinePathStitcher  boatPathManager;

    // Extended scene object references — managed by Scene Deploy panel
    public LevelSelectDataController   dataController;
    public FogFieldManager             fogFieldManager;
    public SplineRiverManager          riverManager;
    public RiverSegmentRegistry        segmentRegistry;
    public LevelSelectSplineManager    splineManager;
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
    public GameObject pathPrefab;
    public GameObject branchWaterExtrudePrefab;
    public GameObject barrierPrefab;
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
    public AudioClip musicIntro;
    public AudioClip musicLoop;

    // Scene-level music reference
    public LevelSelectMusicController musicController;
    public LevelSelectOpeningSequence openingSequence;

    // Linked scene
    public string targetScenePath = "";

    // Opening sequence settings
    public bool      useOpeningSequence        = false;
    public Vector3   openingSequenceStartPos   = Vector3.zero;
    public GameObject openingSequencePrefab;

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
                var path = FindPathContaining(route.points[i - 1].nodeId, nodeId);
                if (path != null)
                {
                    int from = path.nodeIds.IndexOf(route.points[i - 1].nodeId);
                    int to   = path.nodeIds.IndexOf(nodeId);
                    if (from >= 0 && to >= 0 && from != to)
                    {
                        int step = to > from ? 1 : -1;
                        for (int n = from + step; n != to; n += step)
                            ids.Add(path.nodeIds[n]);
                    }
                }
            }

            ids.Add(nodeId);
        }

        return ids;
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

    /// <summary>The pool sitting on a node, or null when it carries none.</summary>
    public DesignerPool PoolAt(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return null;
        return pools.Find(p => p != null && p.nodeId == nodeId);
    }

    /// <summary>
    /// Every path arriving at a pool, paired with the end it arrives on. A path meets a pool by
    /// starting or ending on its node — so a river can run in, and another can run out the far
    /// side, without either owning the pool.
    /// </summary>
    public List<(DesignerPath path, bool atEnd)> PathsAtPool(DesignerPool pool)
    {
        var arrivals = new List<(DesignerPath, bool)>();
        if (pool == null || string.IsNullOrEmpty(pool.nodeId)) return arrivals;

        foreach (var path in paths)
        {
            if (path.nodeIds.Count < 2) continue;
            if (path.nodeIds[path.nodeIds.Count - 1] == pool.nodeId) arrivals.Add((path, true));
            else if (path.nodeIds[0] == pool.nodeId)                 arrivals.Add((path, false));
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

        var main = MainRiver();
        if (main == null || main.nodeIds.Count < 2) return false;

        var head = nodes.Find(n => n != null && n.id == main.nodeIds[0]);
        if (head == null) return false;

        position = head.worldPosition;

        // Facing the way the river runs from here, which is the way the boat sets off.
        Vector3 dir = NodeWorldPosition(main.nodeIds[1]) - position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 1e-6f) forward = dir.normalized;

        var pool = PoolAt(main.nodeIds[0]);
        if (pool == null) return true;

        inPool = true;

        // An open bowl is started in the middle of. One with a plinth has no middle to sit in,
        // so the boat waits on the ring it would travel, at the mouth its river leaves by.
        var   shape = PoolShapeFor(pool);
        float ring  = RiverMeshBuilder.PoolChannelRadius(shape.poolRadius, shape.islandRadius);
        if (ring > 0.001f) position += forward * ring;

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
    /// Where one entrance's prefab stands inside its archway. Doors differ — one arch may be
    /// deeper than another, and one prefab may want to sit further under it — so any entrance
    /// can be given its own offset; the rest follow <see cref="defaultEntranceAlignment"/>.
    /// </summary>
    public ArenaEntranceAlignment EntranceAlignmentFor(DesignerArena arena, string entranceNodeId)
    {
        if (arena == null) return defaultEntranceAlignment;

        if (arena.nodeId == entranceNodeId)
            return (arena.overrideEntranceAlignment ? arena.entranceAlignment
                                                    : defaultEntranceAlignment) ?? defaultEntranceAlignment;

        var entrance = arena.secondaryEntrances?.Find(e => e != null && e.nodeId == entranceNodeId);
        if (entrance != null)
            return (entrance.overrideAlignment ? entrance.alignment
                                               : defaultEntranceAlignment) ?? defaultEntranceAlignment;

        return defaultEntranceAlignment;
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
