using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

public class SoulShoalController : MonoBehaviour
{
    [Header("Refs")]
    public GameObject boat;
    public SplineContainer splineContainer;
    public FishingController fishingController;

    [Header("Spawning")]
    public GameObject fishMeshPrefab;
    public int soulCount = 1;

    [Header("Distances")]
    public float fishingDistance = 8f;

    public bool IsActive  { get; private set; }
    public bool CanFish   { get; private set; }

    private readonly List<Transform> fishList = new List<Transform>();
    private List<Vector3> _nodePositions = new List<Vector3>();
    private bool _wasActive;

    // Called by LevelSpawner before Start() to pass zone node world positions
    public void InitZone(List<Vector3> nodePositions)
    {
        _nodePositions = nodePositions;
    }

    // Called by LevelSpawner to set per-fish soul identity before spawning
    // zoneIndex and soulIndex used to build unique linkIDs
    public void SpawnFish(List<GridData.SoulZone> zones, int zoneIndex, string levelID)
    {
        if (fishMeshPrefab == null || splineContainer == null) return;

        var zone = zones[zoneIndex];

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

            GameObject fish = Instantiate(fishMeshPrefab, splineContainer.transform);

            // Assign spline
            var splineAnimate = fish.GetComponent<SplineAnimate>();
            if (splineAnimate != null)
                splineAnimate.Container = splineContainer;

            // Wire fishing behaviour
            var fishingBehaviour = fish.GetComponent<FishFishingBehaviour>();
            if (fishingBehaviour != null)
                fishingBehaviour.fishing = fishingController;

            // Per-fish identity label
            var label = fish.GetComponent<LinkIdentityLabel>() ?? fish.AddComponent<LinkIdentityLabel>();
            label.SetLabel(linkID, "SoulFish");
            label.soulDataIdentity = soulData.soulDataIdentity;

            // Per-fish proximity audio
            fish.GetComponent<SoulFishProximityAudio>()?.Init(boat != null ? boat.transform : null);

            fishList.Add(fish.transform);

            Debug.Log($"[SoulShoalController] Spawned fish — zone {zoneIndex}, soul {i}, linkID {linkID}.");
        }
    }

    // ---------------------------------------------------------
    void Update()
    {
        if (boat == null) return;

        int alive = 0;
        for (int i = 0; i < fishList.Count; i++)
            if (fishList[i] != null) alive++;

        bool shoalEmpty = alive == 0;
        _wasActive = IsActive;
        IsActive   = !shoalEmpty;

        float d = (_nodePositions.Count > 0)
            ? ClosestDistance(boat.transform.position, _nodePositions)
            : Vector3.Distance(boat.transform.position, transform.position);

        CanFish = !shoalEmpty && d <= fishingDistance;

        if (_wasActive && !IsActive)
            OnShoalDepleted();
    }

    // ---------------------------------------------------------
    void OnShoalDepleted()
    {
        // Stub — wired up in the map-link review pass.
        // Per-fish wave/map unregistration is already handled by SoulFishWaveReference.OnDisable().
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
