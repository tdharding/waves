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
    }

    [Serializable]
    public class DesignerArena
    {
        public string     nodeId;
        public GridData   gridData;
        public int        entranceIndex;
        public GameObject arenaPrefabOverride;

        // One entry per additional entrance beyond the primary (nodeId above)
        public List<DesignerArenaEntrance> secondaryEntrances = new List<DesignerArenaEntrance>();
    }

    public List<LandscapeHillPoint> hillPoints = new();

    public List<DesignerNode>     nodes     = new();
    public List<DesignerPath>     paths     = new();
    public List<DesignerJunction> junctions = new();
    public List<DesignerShop>     shops     = new();
    public List<DesignerObstacle> obstacles = new();
    public List<DesignerArena>    arenas    = new();

    // Tool settings
    public GameObject junctionScriptObject;     // LevelSelectJunctionScriptObject — placed under RIVERJUNCTIONS, designer fills fields manually
    public GameObject junctionPrefab;           // visual fallback for gap SplineInstantiate
    public GameObject junctionRightFacingPrefab; // visual: branch exits right of travel direction
    public GameObject junctionLeftFacingPrefab;  // visual: branch exits left  of travel direction
    public GameObject arenaPrefab;
    public GameObject arenaEntrancePrefab; // LEVELSELECTARENAENTRANCE — spawned per entrance
    public GameObject obstaclePrefab;
    public GameObject shopPrefab;
    public GameObject riverBlockPrefab;
    public float      junctionGapPadding      = 0f;
    public float      splineInstantiateSpacing = 0.15f;
    public int        curveSubdivisions        = 5;
    public float      branchStartOffset        = 0.5f;
    public float      arenaHeadOffset          = 5f;
    public float      shopHeadOffset           = 5f;

    // Landscape tile settings
    public GameObject landscapeTilePrefab;
    public float landscapeTileSize = 10f;
    public int landscapeTilesX = 10;
    public int landscapeTilesZ = 10;
    public Vector2 landscapeOffset = Vector2.zero;
    public float landscapeWorldY = 0f;

    // Scene-level references (persistent — survive Respawn)
    public LandscapeTool       landscapeTool;
    public SplinePathStitcher  boatPathManager;

    // Extended scene object references — managed by Scene Deploy panel
    public LevelSelectDataController   dataController;
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

    // Music
    public AudioClip musicIntro;
    public AudioClip musicLoop;

    // Scene-level music reference
    public LevelSelectMusicController musicController;

    // Canvas state
public float canvasOriginX      = 0f;
    public float canvasOriginZ      = 0f;
    public float canvasUnitsPerPixel = 0.1f;
    public float canvasWorldY        = 0f;
}
