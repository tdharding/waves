using UnityEngine;

[CreateAssetMenu(fileName = "CameraProfile", menuName = "Custom/Camera Profile")]
public class CameraProfile : ScriptableObject
{
    [Header("Orbital")]
    public bool useOrbitalCam = false;
    public float orbitRadius = 15f;
    public float height = 8f;

    [Header("Lens")]
    public float fieldOfView = 60f;
    public float dutch = 0f;

    [Header("Damping")]
    public float dampingX = 1f;

    [Header("Post Processing (future use)")]
    public bool useDOF = false;
    public float focusDistance = 10f;
    public float aperture = 2.8f;
}