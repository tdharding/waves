using System;
using System.Collections.Generic;
using UnityEngine;

public class LureBehaviour : MonoBehaviour
{
    // Set by LureController at spawn time — do not edit in inspector
    [HideInInspector] public float attractionRadius  = 5f;
    [HideInInspector] public float duration          = 10f;
    [HideInInspector] public float sinkSpeed         = 2f;
    [HideInInspector] public float sinkDistance      = 4f;
    [HideInInspector] public float finalSinkDistance = 4f;

    // Fish orbit params — read by LureAttractable
    [HideInInspector] public float orbitRadius      = 1.2f;
    [HideInInspector] public float orbitSpeed       = 1.5f;
    [HideInInspector] public float moveTowardSpeed  = 3f;

    public event Action OnLureExpired;

    public static readonly List<LureBehaviour> ActiveLures = new List<LureBehaviour>();

    enum Phase { Dropping, Active, FinalSink }
    Phase   _phase;
    float   _timer;
    Vector3 _dropOrigin;
    Vector3 _activeOrigin;

    void OnEnable()
    {
        _phase      = Phase.Dropping;
        _dropOrigin = transform.position;
        _timer      = duration;
    }

    void OnDisable()
    {
        ActiveLures.Remove(this);
    }

    void Update()
    {
        switch (_phase)
        {
            case Phase.Dropping:
                transform.position += -transform.up * sinkSpeed * Time.deltaTime;
                if (Vector3.Distance(transform.position, _dropOrigin) >= sinkDistance)
                {
                    _phase       = Phase.Active;
                    _activeOrigin = transform.position;
                    ActiveLures.Add(this); // start attracting fish
                }
                break;

            case Phase.Active:
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    _phase = Phase.FinalSink;
                    ActiveLures.Remove(this); // fish return to paths
                }
                break;

            case Phase.FinalSink:
                transform.position += -transform.up * sinkSpeed * Time.deltaTime;
                if (Vector3.Distance(transform.position, _activeOrigin) >= finalSinkDistance)
                {
                    OnLureExpired?.Invoke();
                    Destroy(gameObject);
                }
                break;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        UnityEditor.Handles.color = new Color(1f, 0.6f, 0f, 0.25f);
        UnityEditor.Handles.DrawSolidDisc(transform.position, Vector3.up, attractionRadius);
        UnityEditor.Handles.color = new Color(1f, 0.6f, 0f, 0.9f);
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, attractionRadius);

        UnityEditor.Handles.color = Color.yellow;
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, 0.2f);

        if (Application.isPlaying)
        {
            string label = _phase switch
            {
                Phase.Dropping  => "Dropping...",
                Phase.Active    => $"Active  {_timer:F1}s",
                Phase.FinalSink => "Sinking...",
                _               => ""
            };
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, label);
        }
    }
#endif
}
