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

    // Set by LevelSpawner for a statue-guarded zone. While the statue is alive the zone
    // is not fishable; when destroyed the reference goes (Unity-)null and CanFish reopens.
    private StatueBehaviour _guardStatue;
    public void SetGuardStatue(StatueBehaviour statue) => _guardStatue = statue;

    // ── Fish-bowl tower mode ─────────────────────────────
    // When _bowlMode is true this container is the "bowl" atop a FishBowlTower: it spawns aloft,
    // fish swim their ring in the air (uncatchable, no water-snap), and it physics-drops when the
    // tower is smashed. Fishing + water-snap reopen the instant it lands (_bowlLanded).
    private bool      _bowlMode;
    private bool      _bowlReleased;
    private bool      _bowlLanded;
    private float     _bowlTargetY;
    private Rigidbody _bowlBody;

    public bool IsBowlLanded => _bowlLanded;

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
    // FISH-BOWL TOWER MODE
    // ---------------------------------------------------------

    // Called by LevelSpawner for a tower zone, after SpawnFish. Puts the container in bowl mode:
    // a kinematic Rigidbody holds it aloft, and every spawned fish is suppressed (uncatchable and
    // not water-snapped) until the container drops and lands at targetWaterY.
    public void InitBowl(float targetWaterY)
    {
        _bowlMode    = true;
        _bowlLanded  = false;
        _bowlTargetY = targetWaterY;

        _bowlBody = GetComponent<Rigidbody>();
        if (_bowlBody == null) _bowlBody = gameObject.AddComponent<Rigidbody>();
        _bowlBody.isKinematic  = true;
        _bowlBody.useGravity   = false;
        _bowlBody.interpolation = RigidbodyInterpolation.Interpolate;

        foreach (var f in _fishList)
        {
            var beh = f != null ? f.GetComponent<FishFishingBehaviour>() : null;
            if (beh != null) beh.BowlSuppressed = true;
        }

        Debug.Log($"[SoulShoalController] Bowl armed: '{gameObject.name}' aloft at Y={transform.position.y:F2}, target water Y={targetWaterY:F2}.");
    }

    // Called by FishBowlTowerController when the tower is smashed — cuts the bowl loose to fall.
    public void ReleaseBowl()
    {
        if (!_bowlMode || _bowlReleased) return;
        _bowlReleased = true;

        if (_bowlBody != null)
        {
            _bowlBody.isKinematic = false;
            _bowlBody.useGravity  = true;
        }
        Debug.Log($"[SoulShoalController] Bowl released — dropping '{gameObject.name}' from Y={transform.position.y:F2}.");
    }

    // Freezes the container on its ring and reopens fishing + water-snap.
    void LandBowl()
    {
        _bowlLanded = true;

        if (_bowlBody != null)
        {
            _bowlBody.isKinematic = true;
            _bowlBody.useGravity  = false;
        }

        Vector3 p = transform.position;
        p.y = _bowlTargetY;
        transform.position = p;

        foreach (var f in _fishList)
        {
            var beh = f != null ? f.GetComponent<FishFishingBehaviour>() : null;
            if (beh != null) beh.BowlSuppressed = false;
        }

        Debug.Log($"[SoulShoalController] Bowl landed at Y={_bowlTargetY:F2} — fish now catchable: '{gameObject.name}'.");
    }

    // ---------------------------------------------------------
    void Start()
    {
        // Resolve boat from fishing controller (spawned after fish, so deferred to Start)
        if (_boat == null && fishingController != null && fishingController.boatTransform != null)
            _boat = fishingController.boatTransform;

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

        // Bowl mode: once released, detect touchdown on the ring's water plane.
        if (_bowlMode && _bowlReleased && !_bowlLanded && transform.position.y <= _bowlTargetY)
            LandBowl();

        // Refresh boat ref lazily if it wasn't ready at Start
        if (_boat == null && fishingController != null && fishingController.boatTransform != null)
            _boat = fishingController.boatTransform;

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

            // Not fishable while the guarding statue is alive and unbroken (null = already gone)
            bool guardOpen = _guardStatue == null || _guardStatue.IsDestroyed;
            // A bowl-mode container is not fishable until it has dropped and landed on its ring.
            bool bowlOpen  = !_bowlMode || _bowlLanded;
            CanFish = IsActive && d <= fishingDistance && guardOpen && bowlOpen;
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
