using UnityEngine;
using System.Collections.Generic;

public class UIMapController : MonoBehaviour
{
    public static UIMapController Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }
    [Header("Map Surface")]
    [SerializeField] private Transform mapSurface;
    public Transform MapSurface => mapSurface;

    [Header("Map Corner Reference")]
    [SerializeField] private MapCornerReference cornerReference;
    public Transform MapRefPlane => cornerReference.transform;

    [Header("Map Orientation")]
    [Tooltip("Y rotation applied to the MapRefPlane at runtime. 180 = compensate for flipped quad.")]
    [SerializeField] private float mapRefPlaneYRotation = 180f;
    [Tooltip("Corrective local Y rotation applied to every spawned marker. Use to counteract mapRefPlaneYRotation visually (e.g. if plane is -220, try 40).")]
    [SerializeField] private float markerYRotationOffset = 0f;

    [Header("Pointer")]
    public Transform pointerParent;
    public Transform pointer;
    public float pointerY = 0f;

    [Header("Maze Wall Markers")]
    public Transform mazeWallMarkerParent;
    public List<MazeWallMarkerSet> mazeWallSets = new List<MazeWallMarkerSet>();
    public List<string> excludedTags = new List<string>();

    [Header("Finish Marker")]
    public GameObject finishMarkerPrefab;
    public Transform finishMarkerParent;

    [Header("Entrance Marker")]
    public GameObject entranceMarkerPrefab;
    public Transform entranceMarkerParent;

    [Header("Snake Marker")]
    public GameObject snakeMarkerPrefab;
    public Transform snakeMarkerParent;

    [Header("Dropped Soul Markers")]
    [SerializeField] private GameObject droppedSoulMarkerPrefab;
    [SerializeField] private Transform  droppedSoulMarkerParent;

    [System.Serializable]
    public struct MazeWallMarkerSet
    {
        public string tag;
        public GameObject markerPrefab;
    }

    private float markerScale = 1f;

    // Runtime
    private ArenaProfile _activeArenaProfile;
    private readonly List<GameObject> _exitMarkerInstances     = new List<GameObject>();
    private readonly List<GameObject> _entranceMarkerInstances = new List<GameObject>();
    private GameObject snakeMarkerInstance;
    private BadGuySnakeMovement snakeMovement;
    private GridData activeGridData;

    private readonly Dictionary<int, GameObject> _soulMarkers  = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, Vector3>    _pendingSouls = new Dictionary<int, Vector3>();

    // ─────────────────────────────────────────
    // INITIALISE
    // ─────────────────────────────────────────

public void InitialiseMapProjection(Bounds arenaBounds, GridData gridData, ArenaProfile profile = null)
{
    if (cornerReference == null)
    {
        Debug.LogWarning("[UIMapController] No MapCornerReference assigned.");
        return;
    }

    activeGridData        = gridData;
    _activeArenaProfile   = profile;
    markerScale = profile != null ? profile.mazeWallMarkerScale : 1f;
      Debug.Log($"[UIMapController] InitialiseMapProjection — profile: {(profile != null ? profile.name : "NULL")}, markerScale: {markerScale}");
    MapProjection.Initialise(cornerReference, arenaBounds);
}

public void ApplyRefPlaneRotation()
{
    if (cornerReference == null) return;
    Vector3 angles = cornerReference.transform.localEulerAngles;
    angles.y = mapRefPlaneYRotation;
    cornerReference.transform.localEulerAngles = angles;
}

    // ─────────────────────────────────────────
    // UPDATE
    // ─────────────────────────────────────────

   void LateUpdate()
{
    if (!MapProjection.IsReady) return;

    Transform boat = LevelDataController.Instance?.GetBoatRoot();
    if (boat != null)
        UpdateBoatPointer(boat);

    UpdateSnakeMarker();

    if (_pendingSouls.Count > 0)
    {
        foreach (var kvp in _pendingSouls)
            SpawnDroppedSoulMarkerNow(kvp.Key, kvp.Value);
        _pendingSouls.Clear();
    }
}

void UpdateBoatPointer(Transform boat)
{
    if (!pointer) return;

    pointer.position = MapProjection.WorldToMap(boat.position);

    float boatY = boat.eulerAngles.y;
    pointer.rotation = Quaternion.Euler(0f, boatY + 90f, 0f);
}
    // ─────────────────────────────────────────
    // MAZE WALLS
    // ─────────────────────────────────────────

public void BuildMazeWallMap()
{
    if (!MapProjection.IsReady || activeGridData == null) return;


  Debug.Log($"[UIMapController] BuildMazeWallMap — markerScale: {markerScale}");
    foreach (var set in mazeWallSets)
    {
        if (string.IsNullOrEmpty(set.tag) || set.tag == "Untagged" || !set.markerPrefab) continue;

        GameObject[] walls = GameObject.FindGameObjectsWithTag(set.tag);

        foreach (var wall in walls)
        {
            if (excludedTags.Contains(wall.tag)) continue;

            int cellX = WorldToCellX(wall.transform.position);
            int cellZ = WorldToCellZ(wall.transform.position);

            GameObject marker = Instantiate(set.markerPrefab, mazeWallMarkerParent);
            marker.transform.position = MapProjection.GridToMap(cellX, cellZ);
            marker.transform.localRotation = Quaternion.Euler(0f, markerYRotationOffset, 0f);
            marker.transform.localScale = marker.transform.localScale * markerScale;
        }
    }
}

    // ─────────────────────────────────────────
    // EXIT MARKERS
    // ─────────────────────────────────────────

    public void UpdateExitMarkers()
    {
        // Exits no longer exist as a separate concept — all portals are entrances.
        foreach (var m in _exitMarkerInstances)
            if (m) Destroy(m);
        _exitMarkerInstances.Clear();
    }


    // ─────────────────────────────────────────
    // ENTRANCE MARKERS
    // ─────────────────────────────────────────

    public void UpdateEntranceMarkers()
    {
        foreach (var m in _entranceMarkerInstances)
            if (m) Destroy(m);
        _entranceMarkerInstances.Clear();

        if (!MapProjection.IsReady || !entranceMarkerPrefab || !entranceMarkerParent ||
            activeGridData?.entrances == null || _activeArenaProfile == null) return;

        float cx     = _activeArenaProfile.arenaCentreOffset.x;
        float cz     = _activeArenaProfile.arenaCentreOffset.y;
        float radius = Mathf.Min(MapProjection.ArenaWidth, MapProjection.ArenaHeight) * 0.5f;

        foreach (var entrance in activeGridData.entrances)
        {
            float rad = entrance.perimeterAngle * Mathf.Deg2Rad;
            Vector3 worldPos = new Vector3(
                cx + Mathf.Sin(rad) * radius,
                0f,
                cz + Mathf.Cos(rad) * radius);

            GameObject marker = Instantiate(entranceMarkerPrefab, entranceMarkerParent);
            marker.transform.position      = MapProjection.WorldToMap(worldPos);
            marker.transform.localRotation = Quaternion.Euler(0f, markerYRotationOffset, 0f);
            _entranceMarkerInstances.Add(marker);
        }
    }

    // ─────────────────────────────────────────
    // SNAKE MARKER
    // ─────────────────────────────────────────

    public void InitialiseSnakeMarker(BadGuySnakeMovement snake)
    {
        snakeMovement = snake;
    }

    public void UpdateSnakeMarker()
    {
        if (!MapProjection.IsReady || !snakeMarkerPrefab) return;

        if (snakeMovement == null || snakeMovement.p0 == null)
        {
            if (snakeMarkerInstance)
                snakeMarkerInstance.SetActive(false);
            return;
        }

        if (!snakeMarkerInstance)
        {
            Transform parent = snakeMarkerParent;
            snakeMarkerInstance = Instantiate(snakeMarkerPrefab, parent);
        }

        snakeMarkerInstance.SetActive(true);
        snakeMarkerInstance.transform.position = MapProjection.WorldToMap(snakeMovement.p0.position);
        snakeMarkerInstance.transform.localRotation = Quaternion.Euler(0f, markerYRotationOffset, 0f);
    }

    // ─────────────────────────────────────────
    // WAVE CENTER
    // ─────────────────────────────────────────

public void UpdateWaveCenter()
{
    if (!MapProjection.IsReady || _activeArenaProfile == null) return;

    Renderer mapRenderer = mapSurface?.GetComponent<Renderer>();
    if (!mapRenderer) return;

    Vector3 worldCentre = new Vector3(
        _activeArenaProfile.arenaCentreOffset.x,
        0f,
        _activeArenaProfile.arenaCentreOffset.y);

    Vector3 mapPos = MapProjection.WorldToMap(worldCentre);
    Vector3 local  = mapSurface.InverseTransformPoint(mapPos);
    mapRenderer.material.SetVector("_WaveCenter", local);
}
    // ─────────────────────────────────────────
    // DROPPED SOUL MARKERS
    // ─────────────────────────────────────────

    public void ShowDroppedSoulMarker(int key, Vector3 worldPos)
    {
        HideDroppedSoulMarker(key);

        if (!droppedSoulMarkerPrefab || !droppedSoulMarkerParent) return;

        if (MapProjection.IsReady)
            SpawnDroppedSoulMarkerNow(key, worldPos);
        else
            _pendingSouls[key] = worldPos;
    }

    public void HideDroppedSoulMarker(int key)
    {
        _pendingSouls.Remove(key);

        if (_soulMarkers.TryGetValue(key, out var marker))
        {
            if (marker) Destroy(marker);
            _soulMarkers.Remove(key);
        }
    }

    private void SpawnDroppedSoulMarkerNow(int key, Vector3 worldPos)
    {
        GameObject marker = Instantiate(droppedSoulMarkerPrefab, droppedSoulMarkerParent);
        marker.transform.position      = MapProjection.WorldToMap(worldPos);
        marker.transform.localRotation = Quaternion.Euler(0f, markerYRotationOffset, 0f);
        _soulMarkers[key] = marker;
    }

    // ─────────────────────────────────────────
    // GRID HELPERS
    // ─────────────────────────────────────────

    int WorldToCellX(Vector3 worldPos)
    {
        float nx = Mathf.Clamp01((worldPos.x - MapProjection.ArenaOrigin.x) / MapProjection.ArenaWidth);
        return Mathf.RoundToInt(nx * (GridData.GridSize - 1));
    }

    int WorldToCellZ(Vector3 worldPos)
    {
        float nz = Mathf.Clamp01((worldPos.z - MapProjection.ArenaOrigin.z) / MapProjection.ArenaHeight);
        return Mathf.RoundToInt(nz * (GridData.GridSize - 1));
    }

    // ─────────────────────────────────────────
    // REMOVED — no longer needed
    // BakeFishMarkerPositions — handled by material shader system
    // SyncFishMarkers         — handled by material shader system
    // RemoveFishMarker        — handled by material shader system
    // ─────────────────────────────────────────
}