using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

public class SoulShoalController : MonoBehaviour
{
    [Header("Refs")]
    public SplineContainer splineContainer;
    public FishingController fishingController;

    [Header("Spawning")]
    public GameObject fishMeshPrefab;
    public int soulCount = 1;

    [Header("Distances")]
    public float fishingDistance = 8f;

    public bool IsActive  { get; private set; }
    public bool CanFish   { get; private set; }

    // Accessible by LevelSpawner to wire up reality proxies after spawning
    public IReadOnlyList<Transform> FishList => _fishList;

    private readonly List<Transform> _fishList = new List<Transform>();
    private List<Vector3> _nodePositions = new List<Vector3>();
    private Transform _boat;
    private bool _wasActive;
    private bool _fishSpawned;

    // Called by LevelDataController once the soul boat is spawned
    public void SetSoulBoat(Transform boat)
    {
        _boat = boat;
        foreach (var f in _fishList)
            if (f != null) f.GetComponent<SoulFishProximityAudio>()?.Init(_boat);
    }

    // Called by LevelSpawner before Start() to pass zone node world positions
    public void InitZone(List<Vector3> nodePositions)
    {
        _nodePositions = nodePositions;
        
        if (_nodePositions != null && _nodePositions.Count > 0)
        {
            SoulFishWaveLinker.RegisterZone(_nodePositions);
            SoulFishMapLinker.RegisterZone(_nodePositions);
        }
    }

    void OnDestroy()
    {
        if (_nodePositions != null && _nodePositions.Count > 0)
        {
            SoulFishWaveLinker.UnregisterZone(_nodePositions);
            SoulFishMapLinker.UnregisterZone(_nodePositions);
        }
    }

    // Called by LevelSpawner to instantiate fish meshes for each soul in the zone
    public void SpawnFish(List<GridData.SoulZone> zones, int zoneIndex, string levelID)
    {
        if (fishMeshPrefab == null || splineContainer == null) return;

        var zone = zones[zoneIndex];
        if (zone.souls == null) return;

        for (int i = 0; i < zone.souls.Count; i++)
        {
            var soulData = zone.souls[i];
            if (soulData == null) continue;

            int linkID = zoneIndex * 100 + i;

            if (GameProgressData.IsSoulCaught(levelID, linkID))
            {
                Debug.Log($"[SoulShoalController] Soul linkID {linkID} already caught — skipping.");
                continue;
            }

            // Deactivate prefab root before instantiating so Awake/OnEnable are deferred
            // until SplineContainer and other refs are fully assigned.
            fishMeshPrefab.SetActive(false);
            GameObject fish = Instantiate(fishMeshPrefab, splineContainer.transform);
            fishMeshPrefab.SetActive(true);

            var splineAnimate = fish.GetComponent<SplineAnimate>();
            if (splineAnimate != null)
            {
                splineAnimate.Container   = splineContainer;
                splineAnimate.StartOffset = (float)i / zone.souls.Count;
            }

            var fishingBehaviour = fish.GetComponent<FishFishingBehaviour>();
            if (fishingBehaviour != null)
                fishingBehaviour.fishing = fishingController;

            // Stamp per-fish identity so FishFishingBehaviour reads the correct soul
            var label = fish.GetComponent<LinkIdentityLabel>() ?? fish.AddComponent<LinkIdentityLabel>();
            label.SetLabel(linkID, "SoulFish");
            label.soulDataIdentity = soulData.soulDataIdentity;

            fish.SetActive(true);
            _fishList.Add(fish.transform);
        }

        _fishSpawned = true;
    }

    // ---------------------------------------------------------
    void Start()
    {
        // Resolve boat from fishing controller (spawned after fish, so deferred to Start)
        if (_boat == null && fishingController != null && fishingController.dummyBoatTarget != null)
            _boat = fishingController.dummyBoatTarget;

        // Init per-fish proximity audio now that boat is resolved
        foreach (var f in _fishList)
        {
            if (f == null) continue;
            f.GetComponent<SoulFishProximityAudio>()?.Init(_boat);
        }

        IsActive  = _fishList.Count > 0;
        _wasActive = IsActive;
    }

    // ---------------------------------------------------------
    void Update()
    {
        if (!_fishSpawned) return;

        // Refresh boat ref lazily if it wasn't ready at Start
        if (_boat == null && fishingController != null && fishingController.dummyBoatTarget != null)
            _boat = fishingController.dummyBoatTarget;

        int alive = 0;
        for (int i = 0; i < _fishList.Count; i++)
            if (_fishList[i] != null) alive++;

        _wasActive = IsActive;
        IsActive   = alive > 0;

        if (_boat != null)
        {
            float d = (_nodePositions.Count > 0)
                ? ClosestDistance(_boat.position, _nodePositions)
                : Vector3.Distance(_boat.position, transform.position);

            CanFish = IsActive && d <= fishingDistance;
        }
        else
        {
            CanFish = false;
        }

        if (_wasActive && !IsActive)
            OnShoalDepleted();
    }

    // ---------------------------------------------------------
    void OnShoalDepleted()
    {
        SoulFishWaveLinker.UnregisterZone(_nodePositions);
        Debug.Log($"[SoulShoalController] Shoal depleted: {gameObject.name}");
    }

    // ---------------------------------------------------------
    float ClosestDistance(Vector3 pos, List<Vector3> points)
    {
        float min = float.MaxValue;
        foreach (var p in points)
            min = Mathf.Min(min, Vector3.Distance(pos, p));
        return min;
    }

    // ---------------------------------------------------------
    void OnDrawGizmosSelected()
    {
        if (_nodePositions == null || _nodePositions.Count == 0)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, fishingDistance);
            return;
        }

        Gizmos.color = Color.yellow;
        for (int i = 0; i < _nodePositions.Count; i++)
        {
            Gizmos.DrawWireSphere(_nodePositions[i], fishingDistance);
            if (i < _nodePositions.Count - 1)
                Gizmos.DrawLine(_nodePositions[i], _nodePositions[i + 1]);
        }
    }
}
