using System.Collections;
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
    // When _bowlMode is true this shoal lives inside a FishBowlTower's bowl (it's parented under the
    // bowl object). Fish swim uncatchable and un-water-snapped while aloft. The BOWL owns the drop
    // physics (see FishBowlTowerController); when it lands, the controller calls BeginSettle() and
    // the fish become catchable + start following the water.
    private bool _bowlMode;
    private bool _bowlLanded;

    public bool IsBowlLanded => _bowlLanded;

    // Called by LevelDataController once the soul boat is spawned
    public void SetSoulBoat(Transform boat)
    {
        _boat = boat;
        foreach (var f in _fishList)
            if (f != null) f.GetComponent<SoulFishProximityAudio>()?.Init(_boat);
    }

    private float _zoneMaskRadius;
    private bool  _maskRegistered;

    // Set by LevelSpawner for a fish-bowl tributary: SoulZoneTributaryLink owns the mask
    // (source pool on landing, corridor only once the river's gate light is lit), so the
    // bowl-landing registration below must not paint the whole path.
    [HideInInspector] public bool maskOwnedExternally;

    // Called by LevelSpawner before Start() to pass zone node world positions.
    // registerNow = false defers the wave/map mask (tower zones: the glow shouldn't paint at
    // the tower base while the shoal is still aloft in the bowl — BeginSettle registers it
    // the moment the bowl hits the water).
    public void InitZone(List<Vector3> nodePositions, float maskRadius = 0f, bool registerNow = true)
    {
        _nodePositions  = nodePositions;
        _zoneMaskRadius = maskRadius;

        if (registerNow && _nodePositions != null && _nodePositions.Count > 0)
        {
            // Dedupe by list reference keeps LevelSpawner's earlier radius-carrying
            // registration authoritative when it already ran.
            SoulFishWaveLinker.RegisterZone(_nodePositions, false, maskRadius);
            SoulFishMapLinker.RegisterZone(_nodePositions, false, maskRadius);
            _maskRegistered = true;
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

    // Called by LevelSpawner for a tower zone, after SpawnFish. Marks bowl mode and suppresses every
    // fish (uncatchable + no water-snap) while the shoal rides the bowl aloft. The bowl object owns
    // the drop physics; this shoal is just parented under it.
    public void InitBowl()
    {
        _bowlMode   = true;
        _bowlLanded = false;

        foreach (var beh in FishBehaviours())
        {
            beh.CatchSuppressed = true;   // not catchable while aloft/falling/settling
            beh.WaterSnapBlend  = 0f;     // ignore the water surface entirely while aloft
        }

        Debug.Log($"[SoulShoalController] Bowl armed: '{gameObject.name}' — fish suppressed until the bowl settles.");
    }

    // Called by FishBowlTowerController when the dropped bowl reaches the water. Ramps each fish's
    // water-snap blend 0→1 over `duration` so they ease from the surface down to their exact wave
    // depth (no pop), then makes them catchable. The shoal then behaves as a normal soul zone.
    public void BeginSettle(float duration)
    {
        if (!_bowlMode || _bowlLanded) return;
        StartCoroutine(SettleRoutine(duration));
    }

    private IEnumerator SettleRoutine(float duration)
    {
        // The bowl has hit the water: NOW paint the zone. Tower zones defer this from spawn so
        // the glow doesn't appear at the tower's base while the shoal is still aloft.
        if (!maskOwnedExternally && !_maskRegistered && _nodePositions != null && _nodePositions.Count > 0)
        {
            SoulFishWaveLinker.RegisterZone(_nodePositions, false, _zoneMaskRadius);
            SoulFishMapLinker.RegisterZone(_nodePositions, false, _zoneMaskRadius);
            _maskRegistered = true;
            Debug.Log($"[SoulShoalController] Bowl landed — soul zone mask registered for '{gameObject.name}'.");
        }

        float e = 0f;
        while (e < duration)
        {
            e += Time.deltaTime;
            float blend = duration > 0f ? Mathf.SmoothStep(0f, 1f, e / duration) : 1f;
            foreach (var beh in FishBehaviours()) beh.WaterSnapBlend = blend;
            yield return null;
        }

        foreach (var beh in FishBehaviours())
        {
            beh.WaterSnapBlend  = 1f;
            beh.CatchSuppressed = false;   // fully settled — catchable now
        }
        _bowlLanded = true;

        Debug.Log($"[SoulShoalController] Shoal settled — fish now catchable: '{gameObject.name}'.");
    }

    private IEnumerable<FishFishingBehaviour> FishBehaviours()
    {
        foreach (var f in _fishList)
        {
            var beh = f != null ? f.GetComponent<FishFishingBehaviour>() : null;
            if (beh != null) yield return beh;
        }
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
