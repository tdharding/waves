using UnityEngine;
using System.Collections.Generic;

public class UIMapController : MonoBehaviour
{
    public static UIMapController Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    // The fade is a shader GLOBAL, so it would otherwise stay stuck on if this controller
    // stops running mid-sonar (scene unload, HUD disabled) and leave the map invisible.
    private void OnDisable()
    {
        _sonarFade = 0f;
        Shader.SetGlobalFloat(_MapUIFadeOutId, 0f);
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
    [Tooltip("Fallback marker for any prefab placement that has no MapMarkerDescriptor (or leaves its marker unset). This is the 'big spike'.")]
    public GameObject defaultMazeMarkerPrefab;
    [Tooltip("If true, also draw tier (upper-layer) prefab placements on the map, not just the base layer.")]
    public bool includeTierPlacements = true;

    [Tooltip("LEGACY — tag-based marker sets. No longer used for drawing (kept only so existing scene data isn't lost). Superseded by MapMarkerDescriptor + defaultMazeMarkerPrefab.")]
    public List<MazeWallMarkerSet> mazeWallSets = new List<MazeWallMarkerSet>();
    [Tooltip("LEGACY — see mazeWallSets.")]
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

    [Header("Procedural Map Drawing (spline walls + cube buildings)")]
    [Tooltip("Shared UI material (Waves/MapProcedural) applied to every procedurally-drawn map element.")]
    [SerializeField] private Material  mapMarkerMaterial;
    [Tooltip("Parent for generated spline-wall lines + node circles. Falls back to mazeWallMarkerParent if unset.")]
    [SerializeField] private Transform splineWallMapParent;
    [Tooltip("Parent for generated cube-building quads. Falls back to mazeWallMarkerParent if unset.")]
    [SerializeField] private Transform cubeBuildingMapParent;
    [Tooltip("World-unit width of the spline-wall line ribbon, for a path of reference thickness. Thicker/thinner paths scale from this. Tune per map scale.")]
    [SerializeField] private float splineLineWidth   = 0.02f;
    [Tooltip("The SplineWallPath thickness that draws at exactly splineLineWidth / nodeCircleRadius. Paths thicker than this widen proportionally; thinner ones narrow. 0.09 = the historic default wall thickness.")]
    [SerializeField] private float splineReferenceThickness = 0.09f;
    [Tooltip("Curved-segment sampling density. Higher = smoother lines that hug the map warp.")]
    [SerializeField] private int   splineSampleSteps = 30;
    [Tooltip("World-unit radius of the (slightly larger) circle drawn at each spline node.")]
    [SerializeField] private float nodeCircleRadius  = 0.03f;
    [Tooltip("Triangle-fan resolution of each node circle.")]
    [SerializeField] private int   nodeCircleSegments = 18;
    [Tooltip("Base half-size of a procedural maze icon (the size an average-footprint object gets). Multiplied by marker/placement scale.")]
    [SerializeField] private float mapIconSize        = 0.04f;
    [Tooltip("Aligner ScaleRadius that counts as 'normal' size (→ base icon size). Larger footprints scale the icon up; smaller ones never shrink below the base.")]
    [SerializeField] private float mapIconReferenceRadius = 3f;
    [Tooltip("Shape parameters for the procedural icons (edit via the Map Icon Library window). Null = built-in defaults.")]
    [SerializeField] private MapIconLibrary iconLibrary;

    // Resolved icon shape params (assigned library or built-in defaults).
    private MapIconLibrary Lib => iconLibrary != null ? iconLibrary : MapIconLibrary.Default;

    [Header("Street Light Markers")]
    [Tooltip("Parent for street-light map icons. Falls back to mazeWallMarkerParent if unset.")]
    [SerializeField] private Transform streetLightMapParent;
    [Tooltip("Base half-size of a street-light map icon.")]
    [SerializeField] private float streetLightIconSize  = 0.05f;

    [Header("Map Height Cue (taller objects lift + draw on top)")]
    [Tooltip("Map-plane 'up' lift per world-unit of object height (from a prefab's PrefabBaselineAlignment map top marker).")]
    [SerializeField] private float mapHeightLift      = 0.01f;
    [Tooltip("Offset along the map normal per world-unit of height, so taller icons sort in front. Flip the sign if they sort behind.")]
    [SerializeField] private float mapHeightDepthBias = 0.002f;
    [Tooltip("Tiny lift along the map-plane normal to keep generated elements off the surface (avoids z-fighting).")]
    [SerializeField] private float mapMarkerLift     = 0.005f;

    [Header("Map Mask (radial clip for procedural elements)")]
    [Tooltip("Fade/clip procedural map elements beyond a world radius from the map centre.")]
    [SerializeField] private bool  enableMapMask  = true;
    [Tooltip("World-unit radius from the map centre beyond which map elements are hidden.")]
    [SerializeField] private float mapMaskRadius  = 0.5f;
    [Tooltip("Soft-edge width (world units) at the mask boundary.")]
    [SerializeField] private float mapMaskFeather = 0.03f;

    [Header("Sonar Fade (hand the map over to the sonar map)")]
    [Tooltip("Fade the procedurally-drawn map imagery out while sonar mode is active.")]
    [SerializeField] private bool  fadeInSonarMode = true;
    [Tooltip("Source of the 0..1 sonar sweep amount. Left unset, the controller finds it in the scene.")]
    [SerializeField] private SonarSystemController sonarSystem;
    [Tooltip("Alpha the procedural map drops to at full sonar. 0 = fully hidden.")]
    [SerializeField, Range(0f, 1f)] private float sonarFadeAlpha = 0f;
    [Tooltip("How fast the fade eases toward the sonar amount (per second). 0 = track the sonar sweep exactly.")]
    [SerializeField] private float sonarFadeSpeed = 8f;

    [Header("Map Zoom (scales imagery with gameplay camera zoom)")]
    [Tooltip("Assign to enable zoom. All procedural map imagery is parented here and this transform is scaled/re-centred each frame. Its parent should be unscaled/unrotated, and it must sit at identity when the map is built.")]
    [SerializeField] private Transform mapContentRoot;
    [Tooltip("Source of the 0..1 zoom amount (the boat's camera zoom).")]
    [SerializeField] private BoatCameraZoom cameraZoom;
    [Tooltip("Enable scaling the map imagery with camera zoom.")]
    [SerializeField] private bool  enableMapZoom  = true;
    [Tooltip("Content scale at fully zoomed OUT (camera at maxFOV).")]
    [SerializeField] private float mapZoomMinScale = 1f;
    [Tooltip("Content scale at fully zoomed IN (camera at minFOV).")]
    [SerializeField] private float mapZoomMaxScale = 3f;
    [Tooltip("How fast the map scale/recentre eases toward the target (per second). 0 = instant.")]
    [SerializeField] private float mapZoomLerpSpeed = 10f;

    // Cached zoom state, refreshed each LateUpdate and reused by dynamic markers.
    // All in the content root's PARENT-local space, so the map frame's own transform
    // (offset/scale/rotation, e.g. MapRefPlane) is respected.
    private bool    _zoomActive;
    private float   _zoomScale = 1f;
    private Vector3 _zoomBoatLocal;
    private Vector3 _zoomPivotLocal;   // where the boat lands: natural spot when out, map centre when in

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

    private readonly Dictionary<EntityId, GameObject> _soulMarkers  = new Dictionary<EntityId, GameObject>();
    private readonly Dictionary<EntityId, Vector3>    _pendingSouls = new Dictionary<EntityId, Vector3>();
    // World source position per soul marker, so it can be re-projected each frame under zoom.
    private readonly Dictionary<EntityId, Vector3>    _soulWorld    = new Dictionary<EntityId, Vector3>();

    private readonly List<GameObject> _mazeWallMapInstances     = new List<GameObject>();
    private readonly List<GameObject> _splineWallMapInstances   = new List<GameObject>();
    private readonly List<GameObject> _cubeBuildingMapInstances = new List<GameObject>();
    // Spline LineRenderers — tracked so their (world-space) width can be scaled with zoom.
    private readonly List<LineRenderer> _splineLines            = new List<LineRenderer>();
    // Live street-light markers, keyed by their controller so state (lit/unlit) can be pushed each frame.
    private readonly Dictionary<StreetLightController, MapStreetLightMarker> _streetLightMarkers
        = new Dictionary<StreetLightController, MapStreetLightMarker>();
    // Every street-light-source marker GameObject (incl. non-stateful shapes), for rebuild cleanup.
    private readonly List<GameObject> _streetLightGOs = new List<GameObject>();
    // Per-line unzoomed width (splineLineWidth × the path's thickness factor), index-matched
    // to _splineLines so zoom scaling doesn't flatten every path back to one width.
    private readonly List<float>        _splineLineBaseWidths   = new List<float>();

    // The post-spawn offset + Y-180 transform applied to spawnParent's children.
    // Data-reconstructed map positions run through this before WorldToMap so they match
    // the real (flipped) geometry — the boat pointer needs none of this since it reads a
    // live world position. Set by LevelDataController from LevelSpawner.SpawnFinalizeMatrix.
    private Matrix4x4 _spawnFinalize = Matrix4x4.identity;

    public void SetSpawnFinalizeMatrix(Matrix4x4 m) => _spawnFinalize = m;

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
    UpdateMapMask();
    UpdateSonarFade();

    if (!MapProjection.IsReady) return;

    Transform boat = LevelDataController.Instance?.GetBoatRoot();

    UpdateMapZoom(boat);
    UpdateSplineLineWidths();
    UpdateStreetLightMarkers();

    // Driven from here, not from the linker's own LateUpdate: the soul-fish mask has to be baked
    // with THIS frame's zoom state, and script execution order wouldn't otherwise guarantee that.
    SoulFishMapLinker.Instance?.BakePositionsOnce();

    if (boat != null)
        UpdateBoatPointer(boat);

    UpdateSnakeMarker();

    if (_pendingSouls.Count > 0)
    {
        foreach (var kvp in _pendingSouls)
            SpawnDroppedSoulMarkerNow(kvp.Key, kvp.Value);
        _pendingSouls.Clear();
    }

    UpdateDroppedSoulMarkers();
}

void UpdateBoatPointer(Transform boat)
{
    if (!pointer) return;

    // When zoom is active this resolves to the map centre (boat-centred minimap).
    pointer.position = MapContentPoint(boat.position);

    float boatY = boat.eulerAngles.y;
    pointer.rotation = Quaternion.Euler(0f, boatY + 90f, 0f);
}
    // ─────────────────────────────────────────
    // MAZE WALLS (data-driven from GridData placements — no tags, no scene lookup)
    // ─────────────────────────────────────────

    /// <summary>
    /// Draws a marker for every prefab placement in the active GridData (base layer, and
    /// tiers when includeTierPlacements is set). The marker is chosen from the prefab's own
    /// MapMarkerDescriptor; prefabs without one — or with the marker left unset — fall back
    /// to defaultMazeMarkerPrefab (the "big spike"). Positions mirror LevelSpawner's
    /// CellToWorldPos exactly, then project through MapProjection, so markers land on the
    /// spawned geometry regardless of map warp.
    /// </summary>
    public void BuildMazeWallMap()
    {
        ClearInstances(_mazeWallMapInstances);

        if (!MapProjection.IsReady || activeGridData == null) return;

        float   arenaWidth = _activeArenaProfile != null ? _activeArenaProfile.WorldArenaWidth : 12f;
        Vector3 normal     = MapPlaneNormal();

        DrawPlacements(activeGridData.prefabPlacements, arenaWidth, normal);

        if (includeTierPlacements && activeGridData.tiers != null)
            foreach (var tier in activeGridData.tiers)
                DrawPlacements(tier?.prefabPlacements, arenaWidth, normal);
    }

    private void DrawPlacements(List<GridData.PrefabPlacement> placements, float arenaWidth, Vector3 normal)
    {
        if (placements == null) return;

        foreach (var pp in placements)
        {
            if (pp?.prefab == null) continue;

            // The descriptor lives on the prefab asset — read it directly, no instantiation.
            var desc = pp.prefab.GetComponentInChildren<MapMarkerDescriptor>(true);
            if (desc != null && !desc.drawOnMap) continue;

            // Scale comes from the profile marker scale and the Grid Designer placement scale only —
            // deliberately NOT from the descriptor (footprint sizing below handles per-object size).
            float placementScale = pp.scale > 0f ? pp.scale : 1f;
            float scale          = markerScale * placementScale;
            Vector3 pos          = CellToMap(pp.cellIndex, arenaWidth, normal);

            // Height cue: taller objects lift along the map's up axis and nudge toward the
            // camera so they overlap (draw on top of) shorter ones. Height scales with the placement.
            var   align  = pp.prefab.GetComponentInChildren<PrefabBaselineAlignment>(true);
            float height = (align != null ? align.PrefabTopHeight : 0f) * placementScale;
            if (height > 0f)
                pos += MapUp(normal) * (mapHeightLift * height) + normal * (mapHeightDepthBias * height);

            // Resolution order: explicit prefab override → default prefab (no descriptor) → procedural icon.
            GameObject prefabOverride = desc != null ? desc.mapMarkerPrefab
                                                     : defaultMazeMarkerPrefab;

            GameObject marker;
            if (prefabOverride != null)
            {
                marker = Instantiate(prefabOverride, ContentParent(mazeWallMarkerParent));
                marker.transform.position      = pos;
                marker.transform.localRotation = Quaternion.Euler(0f, markerYRotationOffset, 0f);
                marker.transform.localScale    = marker.transform.localScale * scale;
            }
            else
            {
                if (mapMarkerMaterial == null) continue;   // procedural icons need the shared material
                MapIcon icon = desc != null ? desc.icon : MapIcon.BigSpike;
                marker = BuildIconMarker(icon, pos, normal, mapIconSize * scale * IconFootprint(align),
                                         ContentParent(mazeWallMarkerParent));
            }

            ApplyDescriptorSorting(marker, desc);
            _mazeWallMapInstances.Add(marker);
        }
    }

    // Adds a SortingGroup to a marker when its descriptor asks for one (so transparent icons like
    // fish bowls / street lights sort over the map surface). Shared by all marker paths.
    private void ApplyDescriptorSorting(GameObject marker, MapMarkerDescriptor desc)
    {
        if (marker == null || desc == null || !desc.addSortingGroup) return;
        var sg = marker.GetComponent<UnityEngine.Rendering.SortingGroup>();
        if (sg == null) sg = marker.AddComponent<UnityEngine.Rendering.SortingGroup>();
        sg.sortingOrder = desc.sortingOrder;
    }

    // Icon size factor from the prefab's real footprint (aligner ScaleRadius) relative to a
    // reference — bigger objects get bigger icons, nothing shrinks below the base. Shared.
    private float IconFootprint(PrefabBaselineAlignment align)
    {
        if (align == null || !align.UseScaleRadius || align.ScaleRadius <= 0f) return 1f;
        return Mathf.Max(1f, align.ScaleRadius / Mathf.Max(0.0001f, mapIconReferenceRadius));
    }

    // Grid cell index -> map position, mirroring LevelSpawner.CellToWorldPos (with its Y flip)
    // then projecting through MapProjection. Arena is centred on origin, width = WorldArenaWidth.
    private Vector3 CellToMap(int cellIndex, float arenaWidth, Vector3 normal)
    {
        int   cellX    = cellIndex % GridData.GridSize;
        int   cellY    = cellIndex / GridData.GridSize;
        int   flippedY = GridData.GridSize - 1 - cellY;

        float tile   = arenaWidth / GridData.GridSize;
        float originX = -arenaWidth * 0.5f;
        float wx = originX + cellX    * tile + tile * 0.5f;
        float wz = originX + flippedY * tile + tile * 0.5f;

        Vector3 world = _spawnFinalize.MultiplyPoint3x4(new Vector3(wx, 0f, wz));
        return MapProjection.WorldToMap(world) + normal * mapMarkerLift;
    }

    // ─────────────────────────────────────────
    // SPLINE WALLS (procedural — data-driven from GridData)
    // ─────────────────────────────────────────

    /// <summary>
    /// Draws every GridData.splineWallPaths entry onto the map as a continuous line
    /// (with a slightly larger circle at each control node). Sampling mirrors
    /// LevelSpawner.WalkSpline so the map line follows the same curve as the spawned
    /// wall; each contiguous run is projected through MapProjection so it also hugs
    /// the map's corner warp. Gap segments break the line.
    /// </summary>
    public void BuildSplineWallMap()
    {
        ClearInstances(_splineWallMapInstances);
        _splineLines.Clear();
        _splineLineBaseWidths.Clear();

        if (!MapProjection.IsReady || activeGridData?.splineWallPaths == null) return;
        if (mapMarkerMaterial == null)
        {
            Debug.LogWarning("[UIMapController] BuildSplineWallMap — mapMarkerMaterial not assigned.");
            return;
        }

        float     arenaWidth = _activeArenaProfile != null ? _activeArenaProfile.WorldArenaWidth : 12f;
        Vector3   normal     = MapPlaneNormal();
        Transform parent     = ContentParent(splineWallMapParent != null ? splineWallMapParent : mazeWallMarkerParent);

        foreach (var path in activeGridData.splineWallPaths)
        {
            if (path?.nodes == null || path.nodes.Count < 2) continue;

            // The designer's per-path thickness, expressed relative to the reference, so a
            // fatter wall reads as a fatter line (and node circle) on the map.
            float thicknessFactor = SplineThicknessFactor(path);
            float lineWidth       = splineLineWidth  * thicknessFactor;
            float circleRadius    = nodeCircleRadius * thicknessFactor;

            // Continuous runs of projected points, broken wherever a gap segment sits.
            foreach (var run in SampleSplineRuns(path, arenaWidth, normal))
            {
                if (run.Count < 2) continue;

                var go = new GameObject("SplineWallMapLine");
                go.transform.SetParent(parent, false);

                var lr = go.AddComponent<LineRenderer>();
                // In content-root (zoom) mode the line must live in the root's local space so it
                // scales/recentres with everything else; otherwise it stays in world space.
                bool rootMode = mapContentRoot != null;
                lr.useWorldSpace      = !rootMode;
                lr.sharedMaterial     = mapMarkerMaterial;
                lr.widthMultiplier    = lineWidth;
                lr.numCornerVertices  = 2;
                lr.numCapVertices     = 2;
                lr.textureMode        = LineTextureMode.Stretch;
                lr.alignment          = LineAlignment.View;
                lr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows     = false;
                lr.positionCount      = run.Count;

                if (rootMode)
                {
                    var local = new Vector3[run.Count];
                    for (int i = 0; i < run.Count; i++)
                        local[i] = go.transform.InverseTransformPoint(run[i]);
                    lr.SetPositions(local);
                }
                else
                {
                    lr.SetPositions(run.ToArray());
                }

                _splineLines.Add(lr);
                _splineLineBaseWidths.Add(lineWidth);
                _splineWallMapInstances.Add(go);
            }

            // Node circles (slightly larger than the line width reads).
            foreach (var node in path.nodes)
            {
                Vector3 p = ProjectNorm(node, arenaWidth, normal);
                _splineWallMapInstances.Add(BuildCircleMarker(p, normal, circleRadius, parent));
            }
        }
    }

    // ─────────────────────────────────────────
    // CUBE BUILDINGS (procedural — data-driven from GridData)
    // ─────────────────────────────────────────

    /// <summary>
    /// Draws every GridData.cubeBuildings footprint as a filled quad. The four
    /// normalized-space corners are projected individually through MapProjection so
    /// the quad respects the same warp/rotation as the rest of the map.
    /// </summary>
    public void BuildCubeBuildingMap()
    {
        ClearInstances(_cubeBuildingMapInstances);

        if (!MapProjection.IsReady || activeGridData?.cubeBuildings == null) return;
        if (mapMarkerMaterial == null)
        {
            Debug.LogWarning("[UIMapController] BuildCubeBuildingMap — mapMarkerMaterial not assigned.");
            return;
        }

        float     arenaWidth = _activeArenaProfile != null ? _activeArenaProfile.WorldArenaWidth : 12f;
        Vector3   normal     = MapPlaneNormal();
        Transform parent     = ContentParent(cubeBuildingMapParent != null ? cubeBuildingMapParent : mazeWallMarkerParent);

        foreach (var b in activeGridData.cubeBuildings)
        {
            if (b == null) continue;

            float hw = b.width  * 0.5f;
            float hl = b.length * 0.5f;

            Vector3 p0 = ProjectNorm(new Vector2(b.center.x - hw, b.center.y - hl), arenaWidth, normal);
            Vector3 p1 = ProjectNorm(new Vector2(b.center.x + hw, b.center.y - hl), arenaWidth, normal);
            Vector3 p2 = ProjectNorm(new Vector2(b.center.x + hw, b.center.y + hl), arenaWidth, normal);
            Vector3 p3 = ProjectNorm(new Vector2(b.center.x - hw, b.center.y + hl), arenaWidth, normal);

            _cubeBuildingMapInstances.Add(BuildQuadMarker(p0, p1, p2, p3, parent));
        }
    }

    // ─────────────────────────────────────────
    // STREET LIGHTS (live markers — reflect StreetLightController.IsLit)
    // ─────────────────────────────────────────

    /// <summary>
    /// Builds a street-light icon for every spawned StreetLightController. Positions are static
    /// (lights don't move) so they're parented under the content root like other icons; only the
    /// lit/unlit visual updates each frame (UpdateStreetLightMarkers). Must run after the lights
    /// spawn and while the content root is at identity (LevelDataController handles both).
    /// </summary>
    public void BuildStreetLightMap()
    {
        ClearStreetLightMarkers();

        if (!MapProjection.IsReady || mapMarkerMaterial == null) return;

        Vector3   normal = MapPlaneNormal();
        Transform parent = ContentParent(streetLightMapParent != null ? streetLightMapParent : mazeWallMarkerParent);

        foreach (var light in StreetLightController.All)
        {
            if (light == null) continue;

            // Same descriptor conventions as every other marker (drawOnMap, footprint sizing, sorting).
            var desc  = light.GetComponentInChildren<MapMarkerDescriptor>(true);
            if (desc != null && !desc.drawOnMap) continue;
            var align = light.GetComponentInChildren<PrefabBaselineAlignment>(true);

            Vector3 pos  = MapProjection.WorldToMap(light.transform.position) + normal * mapMarkerLift;
            float   size = streetLightIconSize * markerScale * IconFootprint(align) * Lib.streetLight.sizeFactor;

            // Shape comes from the descriptor (default StreetLight); rendered through the shared entry point.
            MapIcon icon = desc != null ? desc.icon : MapIcon.StreetLight;
            var go = BuildIconMarker(icon, pos, normal, size, parent);
            _streetLightGOs.Add(go);

            ApplyDescriptorSorting(go, desc);

            // Only the StreetLight shape carries the lit/unlit state component.
            var marker = go.GetComponent<MapStreetLightMarker>();
            if (marker != null)
            {
                marker.SetLit(light.IsLit);
                _streetLightMarkers[light] = marker;
            }
        }
    }

    // Pushes each street light's lit/unlit state to its marker; culls markers for destroyed lights.
    private void UpdateStreetLightMarkers()
    {
        if (_streetLightMarkers.Count == 0) return;

        List<StreetLightController> dead = null;
        foreach (var kvp in _streetLightMarkers)
        {
            if (kvp.Key == null || kvp.Value == null)
            {
                if (kvp.Value) Destroy(kvp.Value.gameObject);
                if (dead == null) dead = new List<StreetLightController>();
                dead.Add(kvp.Key);
                continue;
            }
            kvp.Value.SetLit(kvp.Key.IsLit);
        }

        if (dead != null)
            foreach (var k in dead)
                _streetLightMarkers.Remove(k);
    }

    private void ClearStreetLightMarkers()
    {
        for (int i = 0; i < _streetLightGOs.Count; i++)
            if (_streetLightGOs[i]) Destroy(_streetLightGOs[i]);
        _streetLightGOs.Clear();
        _streetLightMarkers.Clear();
    }

    // Rebuilds the icon-based markers (maze + street lights) from current data — used by the
    // Map Icon Library window so shape/size edits show live in play mode. Resets the content root
    // to identity first (markers build in the natural frame); zoom re-applies next LateUpdate.
    public void RebuildProceduralMarkers()
    {
        if (!MapProjection.IsReady) return;
        PrepareMapContentRoot();
        BuildMazeWallMap();
        BuildStreetLightMap();
    }

    // ─────────────────────────────────────────
    // PROCEDURAL MAP HELPERS
    // ─────────────────────────────────────────

    // Normalized grid node (-0.5..0.5) -> world (matching LevelSpawner) -> map surface,
    // lifted slightly off the surface along its normal to avoid z-fighting.
    private Vector3 ProjectNorm(Vector2 norm, float arenaWidth, Vector3 normal)
    {
        Vector3 world = new Vector3(norm.x * arenaWidth, 0f, norm.y * arenaWidth);
        world = _spawnFinalize.MultiplyPoint3x4(world);
        return MapProjection.WorldToMap(world) + normal * mapMarkerLift;
    }

    // Samples a spline path into one-or-more contiguous projected-point runs, mirroring
    // LevelSpawner.WalkSpline (Catmull-Rom on curved segments, straight lerp otherwise).
    // A gap segment ends the current run so the drawn line breaks where the wall does.
    private List<List<Vector3>> SampleSplineRuns(GridData.SplineWallPath path, float arenaWidth, Vector3 normal)
    {
        var runs = new List<List<Vector3>>();
        var pts  = path.nodes;
        int n    = pts.Count;
        int segCount = path.isClosed ? n : n - 1;

        List<Vector3> current = null;

        void Add(Vector2 p2d)
        {
            if (current == null) { current = new List<Vector3>(); runs.Add(current); }
            Vector3 p = ProjectNorm(p2d, arenaWidth, normal);
            if (current.Count == 0 || (current[current.Count - 1] - p).sqrMagnitude > 1e-8f)
                current.Add(p);
        }

        for (int seg = 0; seg < segCount; seg++)
        {
            if (path.IsSegmentGap(seg)) { current = null; continue; }

            int  i2     = path.isClosed ? (seg + 1) % n : Mathf.Min(seg + 1, n - 1);
            bool curved = path.IsSegmentCurved(seg);

            Add(curved ? SplineSample(pts, seg, 0f, path.isClosed) : pts[seg]);

            int steps = curved ? Mathf.Max(2, splineSampleSteps) : 1;
            for (int s = 1; s <= steps; s++)
            {
                float t = (float)s / steps;
                Add(curved ? SplineSample(pts, seg, t, path.isClosed)
                           : Vector2.Lerp(pts[seg], pts[i2], t));
            }
        }
        return runs;
    }

    // Catmull-Rom sampling — matches LevelSpawner so the map line traces the spawned wall.
    private static Vector2 SplineSample(List<Vector2> pts, int seg, float t, bool closed)
    {
        int n  = pts.Count;
        int i0 = closed ? (seg - 1 + n) % n : Mathf.Max(seg - 1, 0);
        int i1 = seg;
        int i2 = closed ? (seg + 1) % n : Mathf.Min(seg + 1, n - 1);
        int i3 = closed ? (seg + 2) % n : Mathf.Min(seg + 2, n - 1);

        Vector2 p0 = pts[i0], p1 = pts[i1], p2 = pts[i2], p3 = pts[i3];
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    // Map-plane normal from the four corner references (falls back to world up).
    private Vector3 MapPlaneNormal()
    {
        if (cornerReference == null ||
            cornerReference.topLeft == null || cornerReference.topRight == null ||
            cornerReference.bottomLeft == null) return Vector3.up;

        Vector3 x = cornerReference.topRight.position  - cornerReference.topLeft.position;
        Vector3 z = cornerReference.bottomLeft.position - cornerReference.topLeft.position;
        Vector3 nrm = Vector3.Cross(z, x);
        return nrm.sqrMagnitude < 1e-6f ? Vector3.up : nrm.normalized;
    }

    // Map-plane "up" direction (same basis the icon meshes use), for height-cue lift.
    private Vector3 MapUp(Vector3 normal)
    {
        Vector3 u = Vector3.Cross(normal, Vector3.up);
        if (u.sqrMagnitude < 1e-6f) u = Vector3.right;
        u.Normalize();
        return Vector3.Cross(normal, u).normalized;
    }

    // Flat triangle-fan disc lying in the map plane, built from world points converted
    // into the (already-parented) object's local space so it renders under any parent.
    private GameObject BuildCircleMarker(Vector3 center, Vector3 normal, float radius, Transform parent)
    {
        Vector3 u = Vector3.Cross(normal, Vector3.up);
        if (u.sqrMagnitude < 1e-6f) u = Vector3.right;
        u.Normalize();
        Vector3 v = Vector3.Cross(normal, u).normalized;

        var go = new GameObject("SplineNodeMapCircle");
        go.transform.SetParent(parent, false);

        int seg   = Mathf.Max(6, nodeCircleSegments);
        var verts = new Vector3[seg + 1];
        var uvs   = new Vector2[seg + 1];
        var tris  = new int[seg * 3];

        verts[0] = go.transform.InverseTransformPoint(center);
        uvs[0]   = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < seg; i++)
        {
            float a = (i / (float)seg) * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i + 1] = go.transform.InverseTransformPoint(center + (u * c + v * s) * radius);
            uvs[i + 1]   = new Vector2(0.5f + 0.5f * c, 0.5f + 0.5f * s);

            tris[i * 3 + 0] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % seg + 1;
        }

        AssignMesh(go, verts, uvs, tris, "MapNodeCircle");
        return go;
    }

    // Filled quad from four projected world corners (converted to local space).
    private GameObject BuildQuadMarker(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Transform parent)
    {
        var go = new GameObject("CubeBuildingMapQuad");
        go.transform.SetParent(parent, false);

        var verts = new[]
        {
            go.transform.InverseTransformPoint(p0),
            go.transform.InverseTransformPoint(p1),
            go.transform.InverseTransformPoint(p2),
            go.transform.InverseTransformPoint(p3),
        };
        var uvs  = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        var tris = new[] { 0, 2, 1, 0, 3, 2 };

        AssignMesh(go, verts, uvs, tris, "MapCubeQuad");
        return go;
    }

    // ─────────────────────────────────────────
    // PROCEDURAL MAZE ICONS (shared MapProcedural material)
    // ─────────────────────────────────────────

    // Builds a flat icon mesh lying in the map plane at `center`, sized by `size` (half-extent).
    // Vertex alpha bakes any shape fade (e.g. the spike's base). UVs feed the material gradient/rock.
    private GameObject BuildIconMarker(MapIcon icon, Vector3 center, Vector3 normal, float size, Transform parent)
    {
        // StreetLight is stateful (bulb/halo toggle) so it has its own builder that also
        // attaches a MapStreetLightMarker — but it's still selected through the same desc.icon.
        if (icon == MapIcon.StreetLight)
            return BuildStreetLightIcon(center, normal, size, parent);

        Vector3 u = Vector3.Cross(normal, Vector3.up);
        if (u.sqrMagnitude < 1e-6f) u = Vector3.right;
        u.Normalize();
        Vector3 v = Vector3.Cross(normal, u).normalized;

        var go = new GameObject($"MapIcon_{icon}");
        go.transform.SetParent(parent, false);

        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var cols  = new List<Color>();
        var tris  = new List<int>();

        // Adds a world-space point (converted to local), deriving a UV from its planar offset.
        int Add(Vector3 world, Color col)
        {
            Vector3 rel = world - center;
            uvs.Add(new Vector2(0.5f + Vector3.Dot(rel, u) / (2f * size),
                                0.5f + Vector3.Dot(rel, v) / (2f * size)));
            verts.Add(go.transform.InverseTransformPoint(world));
            cols.Add(col);
            return verts.Count - 1;
        }
        void Tri(int a, int b, int c) { tris.Add(a); tris.Add(b); tris.Add(c); }

        switch (icon)
        {
            case MapIcon.FishBowl: BuildFishBowlIcon(center, u, v, size, Add, Tri); break;
            case MapIcon.BigSpike:
            default:               BuildBigSpikeIcon(center, u, v, size, Add, Tri); break;
        }

        AssignMesh(go, verts.ToArray(), uvs.ToArray(), tris.ToArray(), $"MapIcon_{icon}", cols.ToArray());
        return go;
    }

    // Triangle pointing along +v, gradient fill, bottom ~10% of the height fading to transparent.
    private void BuildBigSpikeIcon(Vector3 c, Vector3 u, Vector3 v, float s,
                                   System.Func<Vector3, Color, int> add, System.Action<int, int, int> tri)
    {
        float fadeFrac = Lib.spike.baseFadeFraction;
        float halfW    = s * Lib.spike.widthFactor;

        Vector3 apex  = c + v * s;
        Vector3 baseC = c - v * s;
        Vector3 fadeC = Vector3.Lerp(baseC, apex, fadeFrac);
        float fadeHalfW = halfW * (1f - fadeFrac);

        Color solid = Color.white;
        Color clear = new Color(1f, 1f, 1f, 0f);

        int a  = add(apex,                       solid);
        int fl = add(fadeC - u * fadeHalfW,      solid);
        int fr = add(fadeC + u * fadeHalfW,      solid);
        int bl = add(baseC - u * halfW,          clear);
        int br = add(baseC + u * halfW,          clear);

        tri(a, fr, fl);    // solid top
        tri(fl, fr, br);   // fading base band
        tri(fl, br, bl);
    }

    // Lollipop: a rectangular stick rising along +v with a filled circle (bowl) on top.
    private void BuildFishBowlIcon(Vector3 c, Vector3 u, Vector3 v, float s,
                                   System.Func<Vector3, Color, int> add, System.Action<int, int, int> tri)
    {
        Color solid = Color.white;

        float   rc    = s * Lib.fishBowl.bowlRadiusFactor;   // bowl radius
        Vector3 bowlC = c + v * (s - rc);                    // top of bowl reaches ~+v*s
        float   halfStickW = s * Lib.fishBowl.stickWidthFactor;
        Vector3 stickBot = c - v * s;

        // Stick (bottom → bowl centre so it tucks under the circle).
        int sbl = add(stickBot - u * halfStickW, solid);
        int sbr = add(stickBot + u * halfStickW, solid);
        int stl = add(bowlC    - u * halfStickW, solid);
        int str = add(bowlC    + u * halfStickW, solid);
        tri(sbl, sbr, str);
        tri(sbl, str, stl);

        // Bowl (triangle fan).
        int seg  = Mathf.Max(8, Lib.fanSegments);
        int cIdx = add(bowlC, solid);
        int prev = -1;
        for (int i = 0; i <= seg; i++)
        {
            float ang = (i / (float)seg) * Mathf.PI * 2f;
            int idx = add(bowlC + (u * Mathf.Cos(ang) + v * Mathf.Sin(ang)) * rc, solid);
            if (i > 0) tri(cIdx, prev, idx);
            prev = idx;
        }
    }

    // Street light: a lollipop whose bulb toggles black(off)/white(on), with a half-opacity
    // halo circle behind it that appears only when lit. Built as one mesh in draw order
    // halo → stick → bulb; the returned GameObject carries a MapStreetLightMarker to flip state.
    private GameObject BuildStreetLightIcon(Vector3 center, Vector3 normal, float size, Transform parent)
    {
        Vector3 u = Vector3.Cross(normal, Vector3.up);
        if (u.sqrMagnitude < 1e-6f) u = Vector3.right;
        u.Normalize();
        Vector3 v = Vector3.Cross(normal, u).normalized;

        var go = new GameObject("MapIcon_StreetLight");
        go.transform.SetParent(parent, false);

        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var cols  = new List<Color>();
        var tris  = new List<int>();

        int Add(Vector3 world, Color col)
        {
            Vector3 rel = world - center;
            uvs.Add(new Vector2(0.5f + Vector3.Dot(rel, u) / (2f * size),
                                0.5f + Vector3.Dot(rel, v) / (2f * size)));
            verts.Add(go.transform.InverseTransformPoint(world));
            cols.Add(col);
            return verts.Count - 1;
        }
        void Tri(int a, int b, int c) { tris.Add(a); tris.Add(b); tris.Add(c); }
        void Fan(Vector3 fc, float r, Color col)
        {
            int fanSeg = Mathf.Max(10, Lib.fanSegments);
            int c0 = Add(fc, col);
            int prev = -1;
            for (int i = 0; i <= fanSeg; i++)
            {
                float ang = (i / (float)fanSeg) * Mathf.PI * 2f;
                int idx = Add(fc + (u * Mathf.Cos(ang) + v * Mathf.Sin(ang)) * r, col);
                if (i > 0) Tri(c0, prev, idx);
                prev = idx;
            }
        }

        var sl = Lib.streetLight;
        Vector3 bulbC = center + v * (size * sl.bulbCenterYFactor);
        float   rb    = size * sl.bulbRadiusFactor;   // bulb radius
        float   rh    = size * sl.haloRadiusFactor;   // halo radius (behind bulb)
        float   halfStickW = size * sl.stickWidthFactor;
        Vector3 stickBot   = center - v * size;

        Color offHalo = new Color(1f, 1f, 1f, 0f);  // hidden until lit
        Color offBulb = new Color(0f, 0f, 0f, 1f);  // black until lit
        Color white   = Color.white;

        // Halo (drawn first = behind).
        int haloStart = verts.Count;
        Fan(bulbC, rh, offHalo);
        int haloCount = verts.Count - haloStart;

        // Stick.
        int sbl = Add(stickBot - u * halfStickW, white);
        int sbr = Add(stickBot + u * halfStickW, white);
        int stl = Add(bulbC    - u * halfStickW, white);
        int str = Add(bulbC    + u * halfStickW, white);
        Tri(sbl, sbr, str);
        Tri(sbl, str, stl);

        // Bulb (drawn last = front).
        int bulbStart = verts.Count;
        Fan(bulbC, rb, offBulb);
        int bulbCount = verts.Count - bulbStart;

        Color[] colArray = cols.ToArray();
        Mesh mesh = AssignMesh(go, verts.ToArray(), uvs.ToArray(), tris.ToArray(), "MapIcon_StreetLight", colArray);

        var marker = go.AddComponent<MapStreetLightMarker>();
        marker.Init(mesh, colArray, bulbStart, bulbCount, haloStart, haloCount, sl.haloAlpha);
        return go;
    }

    private Mesh AssignMesh(GameObject go, Vector3[] verts, Vector2[] uvs, int[] tris, string name, Color[] colors = null)
    {
        var mesh = new Mesh { name = name };
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;

        if (colors == null)
        {
            colors = new Color[verts.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
        }
        mesh.colors = colors;

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial    = mapMarkerMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows    = false;

        return mesh;
    }

    private void ClearInstances(List<GameObject> list)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i]) Destroy(list[i]);
        list.Clear();
    }

    // Global shader ids for the radial mask (shared MapProcedural material reads these).
    private static readonly int _MapMaskCenterId  = Shader.PropertyToID("_MapMaskCenter");
    private static readonly int _MapMaskRadiusId  = Shader.PropertyToID("_MapMaskRadius");
    private static readonly int _MapMaskFeatherId = Shader.PropertyToID("_MapMaskFeather");

    // Pushes the radial mask (centre = map surface centre, in world) to the shader globals.
    // Runs every frame so it tracks a moving/HUD-anchored map. Radius <= 0 disables the mask.
    private void UpdateMapMask()
    {
        if (!enableMapMask || cornerReference == null ||
            cornerReference.topLeft == null || cornerReference.topRight == null ||
            cornerReference.bottomLeft == null || cornerReference.bottomRight == null)
        {
            Shader.SetGlobalFloat(_MapMaskRadiusId, 0f); // disabled
            return;
        }

        Vector3 center = (cornerReference.topLeft.position + cornerReference.topRight.position +
                          cornerReference.bottomLeft.position + cornerReference.bottomRight.position) * 0.25f;

        Shader.SetGlobalVector(_MapMaskCenterId, center);
        Shader.SetGlobalFloat (_MapMaskRadiusId, mapMaskRadius);
        Shader.SetGlobalFloat (_MapMaskFeatherId, mapMaskFeather);
    }

    // ─────────────────────────────────────────
    // SONAR FADE (procedural map hands over to the sonar map)
    // ─────────────────────────────────────────

    // Fade-OUT amount pushed to the shared MapProcedural material. Inverted (0 = visible) so a
    // scene with no controller driving it still draws the map — shader globals default to 0.
    private static readonly int _MapUIFadeOutId = Shader.PropertyToID("_MapUIFadeOut");

    private float _sonarFade;          // 0 = full map, 1 = fully faded out
    private bool  _sonarSystemSearched;

    // Follows the sonar sweep (SonarSystemController.CurrentNormalizedRadius) so the map fades
    // out and back in on the same tween the sonar itself uses, eased a little on top.
    private void UpdateSonarFade()
    {
        float target = 0f;

        if (fadeInSonarMode)
        {
            if (sonarSystem == null && !_sonarSystemSearched)
            {
                _sonarSystemSearched = true;   // one lookup only — no per-frame scene search
                sonarSystem = FindObjectOfType<SonarSystemController>();
            }

            if (sonarSystem != null)
                target = Mathf.Clamp01(sonarSystem.CurrentNormalizedRadius) * (1f - sonarFadeAlpha);
        }

        _sonarFade = sonarFadeSpeed > 0f
            ? Mathf.Lerp(_sonarFade, target, 1f - Mathf.Exp(-sonarFadeSpeed * Time.deltaTime))
            : target;

        Shader.SetGlobalFloat(_MapUIFadeOutId, _sonarFade);
    }

    // ─────────────────────────────────────────
    // MAP ZOOM (content root scales/recentres with camera zoom)
    // ─────────────────────────────────────────

    // Parent for built markers: the zoom content root when assigned, else the given fallback.
    private Transform ContentParent(Transform fallback) => mapContentRoot != null ? mapContentRoot : fallback;

    // World-space centre of the map surface (average of the four corners).
    private Vector3 MapCenter()
    {
        if (cornerReference == null || cornerReference.topLeft == null || cornerReference.topRight == null ||
            cornerReference.bottomLeft == null || cornerReference.bottomRight == null)
            return mapContentRoot != null ? mapContentRoot.position : Vector3.zero;

        return (cornerReference.topLeft.position + cornerReference.topRight.position +
                cornerReference.bottomLeft.position + cornerReference.bottomRight.position) * 0.25f;
    }

    // Resets the content root to identity so markers are built in the map's natural frame.
    // Call before the Build* methods (LevelDataController does this).
    public void PrepareMapContentRoot()
    {
        _zoomActive = false;
        _zoomScale  = 1f;
        if (mapContentRoot == null) return;
        mapContentRoot.localPosition = Vector3.zero;
        mapContentRoot.localRotation = Quaternion.identity;
        mapContentRoot.localScale    = Vector3.one;
    }

    // Drives the content-root LOCAL transform from camera zoom, re-centring on the boat.
    // Everything is computed in the content root's parent-local space so the map frame's
    // own transform (MapRefPlane offset/scale/rotation) is preserved.
    private void UpdateMapZoom(Transform boat)
    {
        _zoomActive = enableMapZoom && mapContentRoot != null && MapProjection.IsReady;
        if (!_zoomActive) { _zoomScale = 1f; return; }

        Transform parent = mapContentRoot.parent;

        Vector3 centerL = ToParentLocal(parent, MapCenter());
        _zoomBoatLocal  = ToParentLocal(parent, boat != null ? MapProjection.WorldToMap(boat.position) : MapCenter());

        float z           = cameraZoom != null ? Mathf.Clamp01(cameraZoom.NormalizedZoom) : 0f;
        float targetScale = Mathf.Lerp(mapZoomMinScale, mapZoomMaxScale, z);
        _zoomScale = mapZoomLerpSpeed > 0f
            ? Mathf.Lerp(_zoomScale, targetScale, 1f - Mathf.Exp(-mapZoomLerpSpeed * Time.deltaTime))
            : targetScale;

        // Re-centre blends with zoom: zoomed out keeps the boat at its natural spot (whole arena
        // visible), zoomed in pulls the boat to the map centre. child = pivot + scale*(P - boat).
        _zoomPivotLocal = Vector3.Lerp(_zoomBoatLocal, centerL, z);

        mapContentRoot.localPosition = _zoomPivotLocal - _zoomScale * _zoomBoatLocal;
        mapContentRoot.localRotation = Quaternion.identity;
        mapContentRoot.localScale    = Vector3.one * _zoomScale;
    }

    /// <summary>
    /// Current zoom multiplier (1 when zoom is off). Anything that scales by hand rather than by
    /// riding the content-root transform — the spline line widths, the soul-fish mask radii — has
    /// to multiply by this.
    /// </summary>
    public float MapZoomScale => _zoomActive ? _zoomScale : 1f;

    /// <summary>
    /// Map-surface units per arena world unit, i.e. how much the arena shrinks to fit the map.
    /// Converts a world-space radius (the Grid Designer swim radius) into the map's own scale.
    /// Averages both axes, since a warped map can compress them differently.
    /// </summary>
    public float MapUnitsPerWorldUnit()
    {
        if (cornerReference == null || cornerReference.topLeft == null ||
            cornerReference.topRight == null || cornerReference.bottomLeft == null) return 1f;

        float w = MapProjection.ArenaWidth, h = MapProjection.ArenaHeight;
        if (w <= 0.0001f || h <= 0.0001f) return 1f;

        float sx = Vector3.Distance(cornerReference.topLeft.position, cornerReference.topRight.position)  / w;
        float sz = Vector3.Distance(cornerReference.topLeft.position, cornerReference.bottomLeft.position) / h;
        return (sx + sz) * 0.5f;
    }

    // Maps a world source to its final map position, applying zoom/recentre when active.
    // Used by dynamic markers (pointer, snake, dropped souls) not parented to the content root,
    // and by SoulFishMapLinker to bake the soul-zone mask into the same zoomed space.
    public Vector3 MapContentPoint(Vector3 worldSource)
    {
        Vector3 p = MapProjection.WorldToMap(worldSource);
        if (!_zoomActive) return p;

        Transform parent = mapContentRoot.parent;
        Vector3   pL     = ToParentLocal(parent, p);
        Vector3   outL   = _zoomPivotLocal + _zoomScale * (pL - _zoomBoatLocal);
        return parent != null ? parent.TransformPoint(outL) : outL;
    }

    private static Vector3 ToParentLocal(Transform parent, Vector3 world) =>
        parent != null ? parent.InverseTransformPoint(world) : world;

    // LineRenderer width is world-space and ignores the content-root scale, so scale it
    // manually to track zoom. (Mesh markers — node circles, quads, prefabs — scale via the
    // content-root transform already.)
    private void UpdateSplineLineWidths()
    {
        if (_splineLines.Count == 0) return;

        float zoom = _zoomActive ? _zoomScale : 1f;
        for (int i = 0; i < _splineLines.Count; i++)
        {
            if (!_splineLines[i]) continue;
            float baseW = i < _splineLineBaseWidths.Count ? _splineLineBaseWidths[i] : splineLineWidth;
            _splineLines[i].widthMultiplier = baseW * zoom;
        }
    }

    // How thick this path draws on the map, relative to the reference thickness. Guards the
    // legacy case where an old asset deserialized wallThickness as 0 (→ draw at 1×).
    private float SplineThicknessFactor(GridData.SplineWallPath path)
    {
        if (path == null || path.wallThickness <= 0f || splineReferenceThickness <= 0f) return 1f;
        return path.wallThickness / splineReferenceThickness;
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

            GameObject marker = Instantiate(entranceMarkerPrefab, ContentParent(entranceMarkerParent));
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
        snakeMarkerInstance.transform.position = MapContentPoint(snakeMovement.p0.position);
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

    public void ShowDroppedSoulMarker(EntityId key, Vector3 worldPos)
    {
        HideDroppedSoulMarker(key);

        if (!droppedSoulMarkerPrefab || !droppedSoulMarkerParent) return;

        if (MapProjection.IsReady)
            SpawnDroppedSoulMarkerNow(key, worldPos);
        else
            _pendingSouls[key] = worldPos;
    }

    public void HideDroppedSoulMarker(EntityId key)
    {
        _pendingSouls.Remove(key);
        _soulWorld.Remove(key);

        if (_soulMarkers.TryGetValue(key, out var marker))
        {
            if (marker) Destroy(marker);
            _soulMarkers.Remove(key);
        }
    }

    private void SpawnDroppedSoulMarkerNow(EntityId key, Vector3 worldPos)
    {
        GameObject marker = Instantiate(droppedSoulMarkerPrefab, droppedSoulMarkerParent);
        marker.transform.position      = MapContentPoint(worldPos);
        marker.transform.localRotation = Quaternion.Euler(0f, markerYRotationOffset, 0f);
        _soulMarkers[key] = marker;
        _soulWorld[key]   = worldPos;
    }

    // Re-projects dropped-soul markers each frame so they track the map zoom/recentre.
    private void UpdateDroppedSoulMarkers()
    {
        if (_soulMarkers.Count == 0) return;

        foreach (var kvp in _soulMarkers)
        {
            if (kvp.Value == null) continue;
            if (_soulWorld.TryGetValue(kvp.Key, out var worldPos))
                kvp.Value.transform.position = MapContentPoint(worldPos);
        }
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