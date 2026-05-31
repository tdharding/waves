using UnityEngine;
using System;
using System.Collections.Generic;

public class LureController : MonoBehaviour
{
    [Header("Lures")]
    [Tooltip("Prefab instantiated onto the boat when stocking or purchasing lures.")]
    public GameObject lurePrefab;
    [Tooltip("Transform on the boat under which lure instances are parented.")]
    public Transform boatLureParent;

    [Header("Timing")]
    public float duration               = 10f;

    [Header("Sink")]
    public float sinkSpeed              = 2f;
    public float sinkDistance           = 4f;
    public float finalSinkDistance      = 4f;

    [Header("Attraction")]
    public float attractionRadius       = 5f;

    [Header("Fish Orbit")]
    public float orbitRadius            = 1.2f;
    public float orbitSpeed             = 1.5f;
    public float moveTowardSpeed        = 3f;

    [Header("Fish Return")]
    public float returnSpeed            = 2f;
    public float returnThreshold        = 0.3f;

    [Header("Debug")]
    public bool showGizmoPreview = true;

    public event Action OnLuresExhausted;

    private readonly List<LureBehaviour> _boatLures = new List<LureBehaviour>();
    private int _nextLureIndex;
    private int _activeLureCount;
    private bool _loadedLureVisible;

    public bool HasLureAvailable => _nextLureIndex < _boatLures.Count;

    public void InitStock(int amount)
    {
        foreach (var l in _boatLures)
            if (l != null) Destroy(l.gameObject);
        _boatLures.Clear();

        for (int i = 0; i < amount; i++)
            SpawnLureOnBoat();

        _nextLureIndex   = 0;
        _activeLureCount = 0;
    }

    public void AddLure()
    {
        SpawnLureOnBoat();
        ApplyLoadedLureVisibility();
        Debug.Log($"[LureController] Lure added via shop. Total available: {_boatLures.Count - _nextLureIndex}");
    }

    private void SpawnLureOnBoat()
    {
        if (lurePrefab == null)
        {
            Debug.LogWarning("[LureController] lurePrefab not assigned — cannot stock lure.");
            return;
        }

        Transform parent = boatLureParent != null ? boatLureParent : transform;
        GameObject go    = Instantiate(lurePrefab, parent.position, parent.rotation, parent);
        var behaviour    = go.GetComponent<LureBehaviour>();
        if (behaviour == null)
            behaviour = go.AddComponent<LureBehaviour>();

        _boatLures.Add(behaviour);
    }

    public void SetLoadedLureVisible(bool visible)
    {
        _loadedLureVisible = visible;
        ApplyLoadedLureVisibility();
    }

    private void ApplyLoadedLureVisibility()
    {
        for (int i = _nextLureIndex; i < _boatLures.Count; i++)
        {
            var lure = _boatLures[i];
            if (lure != null)
                lure.gameObject.SetActive(_loadedLureVisible && i == _nextLureIndex);
        }
    }

    public void DropLure()
    {
        if (_nextLureIndex >= _boatLures.Count)
        {
            Debug.LogWarning("[LureController] DropLure blocked — no lures remaining.");
            return;
        }

        LureBehaviour lure = _boatLures[_nextLureIndex];
        _nextLureIndex++;

        if (lure == null)
        {
            Debug.LogWarning("[LureController] Lure slot was null — skipping.");
            return;
        }

        lure.duration          = duration;
        lure.sinkSpeed         = sinkSpeed;
        lure.sinkDistance      = sinkDistance;
        lure.finalSinkDistance = finalSinkDistance;
        lure.attractionRadius  = attractionRadius;
        lure.orbitRadius       = orbitRadius;
        lure.orbitSpeed        = orbitSpeed;
        lure.moveTowardSpeed   = moveTowardSpeed;
        lure.returnSpeed       = returnSpeed;
        lure.returnThreshold   = returnThreshold;
        lure.OnLureExpired    += OnLureExpired;

        lure.transform.SetParent(null);
        lure.transform.rotation = Quaternion.identity;
        lure.Launch();

        _activeLureCount++;
        int remaining = _boatLures.Count - _nextLureIndex;
        Debug.Log($"[LureController] Lure dropped. Remaining: {remaining}. Active in water: {_activeLureCount}");

        ApplyLoadedLureVisibility();

        if (_nextLureIndex >= _boatLures.Count)
        {
            Debug.Log("[LureController] All lures dropped — lure tool exhausted.");
            OnLuresExhausted?.Invoke();
        }
    }

    void OnLureExpired()
    {
        _activeLureCount = Mathf.Max(0, _activeLureCount - 1);
        Debug.Log($"[LureController] Lure expired. Still active in water: {_activeLureCount}");
    }


#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showGizmoPreview) return;

        if (Application.isPlaying)
        {
            for (int i = 0; i < _boatLures.Count; i++)
            {
                if (_boatLures[i] != null)
                    DrawLureGizmo(_boatLures[i].transform.position, i);
            }
        }
        else if (boatLureParent != null)
        {
            DrawLureGizmo(boatLureParent.position, 0);
        }
    }

    void DrawLureGizmo(Vector3 pos, int index)
    {
        UnityEditor.Handles.color = new Color(1f, 0.6f, 0f, 0.6f);
        UnityEditor.Handles.DrawWireDisc(pos, Vector3.up, attractionRadius);

        Vector3 dropTip    = pos + Vector3.down * 0.6f;
        Vector3 arrowLeft  = dropTip + Vector3.up * 0.15f + Vector3.right *  0.15f;
        Vector3 arrowRight = dropTip + Vector3.up * 0.15f + Vector3.right * -0.15f;
        UnityEditor.Handles.color = new Color(0f, 1f, 0.5f, 0.9f);
        UnityEditor.Handles.DrawLine(pos, dropTip);
        UnityEditor.Handles.DrawLine(dropTip, arrowLeft);
        UnityEditor.Handles.DrawLine(dropTip, arrowRight);

        Vector3 sinkEnd = pos + Vector3.down * sinkDistance;
        UnityEditor.Handles.color = new Color(1f, 0.4f, 0f, 0.9f);
        UnityEditor.Handles.DrawLine(pos, sinkEnd);
        UnityEditor.Handles.DrawWireDisc(sinkEnd, Vector3.up, orbitRadius);
        UnityEditor.Handles.Label(sinkEnd + Vector3.down * 0.1f, $"[{index}] depth ({sinkDistance}m)");

        Vector3 finalSinkEnd = sinkEnd + Vector3.down * finalSinkDistance;
        UnityEditor.Handles.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        UnityEditor.Handles.DrawLine(sinkEnd, finalSinkEnd);
        UnityEditor.Handles.DrawWireDisc(finalSinkEnd, Vector3.up, 0.15f);
    }
#endif
}
