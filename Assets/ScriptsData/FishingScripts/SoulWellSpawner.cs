using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;

public class SoulWellSpawner : MonoBehaviour
{
    [Header("Spline")]
    public SplineContainer splineContainer;

    [Header("Soul Prefab")]
    public GameObject soulPrefab;

    [Header("Destination Parent")]
    public Transform destinationParent;

    [Header("Offsets (Very Subtle)")]
    public Vector3 maxPositionOffset = new Vector3(0.05f, 0.05f, 0.05f);
    public float maxRotationOffset = 3f;

    [Header("Spline Animation")]
    [Tooltip("Random start offset along the spline (0–1 range)")]
    public float maxSplineStartOffset = 0.05f;

    [Tooltip("Optional speed variation")]
    public float speedVariation = 0.1f;

    private readonly List<GameObject> spawnedSouls = new List<GameObject>();
    private int lastSoulCount = -1;

    private void Update()
    {
        int soulCount = GameProgressData.GetPermanentSouls();

        if (soulCount != lastSoulCount)
        {
            RebuildSouls(soulCount);
        }
    }

    private void RebuildSouls(int soulCount)
    {
        lastSoulCount = soulCount;
        ClearSouls();

        if (soulCount <= 0 || splineContainer == null || soulPrefab == null)
            return;

        Spline spline = splineContainer.Spline;
        Transform parent = destinationParent != null ? destinationParent : transform;

        for (int i = 0; i < soulCount; i++)
        {
            float t = soulCount == 1 ? 0.5f : (float)i / (soulCount - 1);

            Vector3 localSplinePos = spline.EvaluatePosition(t);
            Vector3 worldPos = splineContainer.transform.TransformPoint(localSplinePos);

            Vector3 tangent = ((Vector3)spline.EvaluateTangent(t)).normalized;
            Quaternion rotation = Quaternion.LookRotation(tangent, Vector3.up);

            worldPos += Random.insideUnitSphere * maxPositionOffset.magnitude;
            rotation *= Quaternion.Euler(
                Random.Range(-maxRotationOffset, maxRotationOffset),
                Random.Range(-maxRotationOffset, maxRotationOffset),
                Random.Range(-maxRotationOffset, maxRotationOffset)
            );

            GameObject soul = Instantiate(soulPrefab, worldPos, rotation, parent);
            spawnedSouls.Add(soul);

            ConfigureSplineAnimation(soul, t);
        }
    }

    private void ConfigureSplineAnimation(GameObject soul, float baseT)
    {
        SplineAnimate splineAnimate = soul.GetComponentInChildren<SplineAnimate>();
        if (splineAnimate == null)
            return;

        splineAnimate.Container = splineContainer;

        float offset = Random.Range(-maxSplineStartOffset, maxSplineStartOffset);
        splineAnimate.StartOffset = Mathf.Repeat(baseT + offset, 1f);

        if (speedVariation > 0f)
        {
            float speedMultiplier = 1f + Random.Range(-speedVariation, speedVariation);
            splineAnimate.MaxSpeed *= speedMultiplier;
        }

        splineAnimate.Play();
    }

    private void ClearSouls()
    {
        for (int i = 0; i < spawnedSouls.Count; i++)
        {
            if (spawnedSouls[i] != null)
                Destroy(spawnedSouls[i]);
        }

        spawnedSouls.Clear();
    }
}