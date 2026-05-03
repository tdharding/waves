using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Scroll-wheel zoom for the level select scene.
/// Attach to any GameObject and assign the Cinemachine camera.
/// </summary>
public class LevelSelectCameraController : MonoBehaviour
{
    [Header("Camera")]
    public CinemachineCamera cam;

    [Header("Scroll Zoom")]
    public float zoomSpeed = 10f;
    public float minFOV    = 5f;
    public float maxFOV    = 60f;
    public float defaultFOV = 30f;

    private float _currentFOV;

    private void Start()
    {
        _currentFOV = defaultFOV;
        ApplyFOV();
    }

    private void Update()
    {
        if (cam == null) return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.01f) return;

        _currentFOV = Mathf.Clamp(_currentFOV - scroll * zoomSpeed, minFOV, maxFOV);
        ApplyFOV();
    }

    private void ApplyFOV()
    {
        var lens = cam.Lens;
        lens.FieldOfView = _currentFOV;
        cam.Lens = lens;
    }
}
