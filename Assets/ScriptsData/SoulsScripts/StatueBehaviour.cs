using System.Collections.Generic;
using UnityEngine;

public class StatueBehaviour : MonoBehaviour
{
    [Header("Attraction")]
    public float attractionRadius = 2f;
    public float orbitRadius      = 0.2f;
    public float orbitSpeed       = 0.46f;
    public float moveTowardSpeed  = 0.34f;
    public float returnSpeed      = 2f;
    public float returnThreshold  = 0.3f;

    public static readonly List<StatueBehaviour> ActiveStatues = new List<StatueBehaviour>();

    void OnEnable()  => ActiveStatues.Add(this);
    void OnDisable() => ActiveStatues.Remove(this);

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        UnityEditor.Handles.color = new Color(0.6f, 0.2f, 1f, 0.2f);
        UnityEditor.Handles.DrawSolidDisc(transform.position, Vector3.up, attractionRadius);
        UnityEditor.Handles.color = new Color(0.6f, 0.2f, 1f, 0.9f);
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, attractionRadius);
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, orbitRadius);
    }
#endif
}
