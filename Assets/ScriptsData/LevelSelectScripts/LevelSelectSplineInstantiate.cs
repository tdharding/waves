using UnityEngine;
using UnityEngine.Splines;

[RequireComponent(typeof(SplineContainer))]
[RequireComponent(typeof(SplineInstantiate))]
public class LevelSelectSplineInstantiate : MonoBehaviour
{
    [Tooltip("Prefab to tile along the spline.")]
    public GameObject prefab;

    [Tooltip("Spacing between instances in world units.")]
    public float spacing = 0.15f;

    [Tooltip("When true, instances are evenly distributed rather than offset from start.")]
    public bool autoSpacing = true;

    private SplineInstantiate _si;

    private void Awake()
    {
        _si = GetComponent<SplineInstantiate>();
    }

    public void Refresh()
    {
        _si = GetComponent<SplineInstantiate>();
        if (_si != null)
            _si.UpdateInstances();
    }
}
