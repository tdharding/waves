using UnityEngine;

/// <summary>
/// Attach to a visual child inside the junction prefab to apply a local
/// rotation offset independently of the SplineInstantiate alignment.
/// Works in both Edit and Play mode so the preview updates live.
/// </summary>
[ExecuteAlways]
public class SplineJunctionAligner : MonoBehaviour
{
    [SerializeField] private Vector3 _rotationOffset;

    void OnEnable()  => Apply();
    void OnDisable() => Apply();

#if UNITY_EDITOR
    void OnValidate() => Apply();
#endif

    private void Apply()
    {
        transform.localRotation = Quaternion.Euler(_rotationOffset);
    }
}
