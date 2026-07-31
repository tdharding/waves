using System.Collections.Generic;
using UnityEngine;

public class SonarUIMapController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SonarSystemController sonarSystem;
    [SerializeField] private Transform sonarBoat; // SoulBoat — set at runtime via SetSoulBoat()
    [SerializeField] private Renderer sonarMapRenderer;


    [Header("Map Dimensions (LOCAL units)")]
    [SerializeField] private float sonarMapWidth = 1.2f;
    [SerializeField] private float sonarMapHeight = 1.2f;

    [Header("Sonar Detection")]
    [SerializeField] private float sonarWorldRadius = 10f; // Larger than whirl radius

    [Header("Fish Markers")]
    [SerializeField] private GameObject sonarFishMarkerPrefab;
    [SerializeField] private Transform markerParent;
    [SerializeField] private float markerY = 0f;


    [Header("Pointer Fade")]
    [SerializeField] private Renderer pointerRenderer;
    [SerializeField] private string pointerAlphaProperty = "_Alpha";
    private Material pointerMaterial;

    [Header("Map Rotation")]
    [Tooltip("Base rotation applied to fish marker positions and the map material. 180 = compensate for flipped quad. Try -200 to align with camera angle.")]
    [SerializeField] private float mapRotationDegrees = -200f;
    [Tooltip("Shader property name for map material rotation (leave blank if unused).")]
    [SerializeField] private string materialRotationProperty = "_Rotation";
    [Tooltip("Spin the map with the boat's heading so boat-forward always reads as 'up' on the sonar map. Off = fixed north-up.")]
    [SerializeField] private bool  rotateWithBoatForward = true;
    [Tooltip("Flip if the map spins the wrong way relative to the boat.")]
    [SerializeField] private bool  invertBoatRotation = false;
    [Tooltip("How fast the map eases toward the boat's heading (per second). 0 = snap, which can read jittery.")]
    [SerializeField] private float boatRotationLerpSpeed = 8f;

    [Header("Debug")]
    [SerializeField] private bool drawDebugRadius = true;
    [SerializeField] private Color debugRadiusColor = Color.cyan;




    private Dictionary<Transform, GameObject> activeMarkers =
        new Dictionary<Transform, GameObject>();

    private Material runtimeMaterial;

    // Live map angle — mapRotationDegrees on its own when fixed, plus the boat's heading when
    // rotateWithBoatForward is on. Smoothed with LerpAngle so crossing 0/360 doesn't whip round.
    private float currentRotationDegrees;
    private bool  rotationInitialised;

    // --------------------------------------------------

    private bool TryResolveSonarBoat()
    {
        return sonarBoat != null;
    }


    void Awake()
    {
        currentRotationDegrees = mapRotationDegrees;

        if (sonarMapRenderer != null)
        {
            runtimeMaterial = sonarMapRenderer.material;
            SetOverlayAlpha(0f);
            ApplyMaterialRotation();
        }

        if (pointerRenderer != null)
        {
            pointerMaterial = pointerRenderer.material;
        }
    }

    void LateUpdate()
{
    if (!sonarSystem || runtimeMaterial == null)
        return;

    if (!TryResolveSonarBoat())
        return;

    UpdateMapRotation();

    float normalized = sonarSystem.CurrentNormalizedRadius;

    SetOverlayAlpha(normalized);
    SetPointerAlpha(1f - normalized);


    if (normalized > 0.001f)
        UpdateFishMarkers();
    else
        ClearAllMarkers();
}

    // --------------------------------------------------

    // Boat-forward-up: rotating the world-space fish offsets by MINUS the boat's yaw parks
    // whatever the boat is pointing at on a fixed axis of the map. mapRotationDegrees stays
    // as the base offset that aligns the quad/camera, so tuning it still works unchanged.
    float TargetRotationDegrees()
    {
        if (!rotateWithBoatForward || sonarBoat == null)
            return mapRotationDegrees;

        float yaw = sonarBoat.eulerAngles.y;
        return mapRotationDegrees + (invertBoatRotation ? yaw : -yaw);
    }

    void UpdateMapRotation()
    {
        float target = TargetRotationDegrees();

        if (!rotationInitialised || boatRotationLerpSpeed <= 0f)
        {
            currentRotationDegrees = target;
            rotationInitialised    = true;
        }
        else
        {
            currentRotationDegrees = Mathf.LerpAngle(
                currentRotationDegrees, target,
                1f - Mathf.Exp(-boatRotationLerpSpeed * Time.deltaTime));
        }

        // Material has to follow every frame now, not just once in Awake.
        ApplyMaterialRotation();
    }

void SetPointerAlpha(float value)
{
    if (pointerMaterial == null)
        return;

    pointerMaterial.SetFloat(pointerAlphaProperty, value);
}


    // --------------------------------------------------

    void UpdateFishMarkers()
    {
        IReadOnlyList<Transform> fishList =
            SoulFishMapLinker.ActiveFish;

        // ADD / UPDATE
        for (int i = 0; i < fishList.Count; i++)
        {
            Transform fish = fishList[i];
            if (!fish)
                continue;

            Vector3 delta = fish.position - sonarBoat.position;
            delta.y = 0f;

            float distance = delta.magnitude;

            if (distance > sonarWorldRadius)
            {
                RemoveMarker(fish);
                continue;
            }

            if (!activeMarkers.ContainsKey(fish))
            {
                Transform parent = markerParent != null
                    ? markerParent
                    : transform;

                GameObject marker =
                    Instantiate(sonarFishMarkerPrefab, parent);

                activeMarkers.Add(fish, marker);
            }

            UpdateMarkerPosition(fish, delta);
        }

        // CLEANUP NULL / DESTROYED FISH
        var toRemove = new List<Transform>();

        foreach (var kvp in activeMarkers)
        {
            if (kvp.Key == null)
                toRemove.Add(kvp.Key);
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            RemoveMarker(toRemove[i]);
        }
    }

    // --------------------------------------------------

    void UpdateMarkerPosition(Transform fish, Vector3 delta)
    {
        if (!activeMarkers.TryGetValue(fish, out GameObject marker))
            return;

        Vector2 planar = new Vector2(delta.x, delta.z);
        Vector2 normalized = planar / sonarWorldRadius;

        float rad = currentRotationDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);

        float localX = (normalized.x * cos - normalized.y * sin) * (sonarMapWidth  * 0.5f);
        float localZ = (normalized.x * sin + normalized.y * cos) * (sonarMapHeight * 0.5f);

        marker.transform.localPosition = new Vector3(localX, markerY, localZ);
    }

    // --------------------------------------------------

    void RemoveMarker(Transform fish)
    {
        if (!activeMarkers.TryGetValue(fish, out GameObject marker))
            return;

        if (marker != null)
            Destroy(marker);

        activeMarkers.Remove(fish);
    }

    void ClearAllMarkers()
    {
        foreach (var kvp in activeMarkers)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }

        activeMarkers.Clear();
    }

    void SetOverlayAlpha(float value)
    {
        runtimeMaterial.SetFloat("_Alpha", value);
    }

    void ApplyMaterialRotation()
    {
        if (runtimeMaterial == null || string.IsNullOrEmpty(materialRotationProperty)) return;
        if (runtimeMaterial.HasProperty(materialRotationProperty))
            runtimeMaterial.SetFloat(materialRotationProperty, currentRotationDegrees);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawDebugRadius || sonarBoat == null) return;

        Gizmos.color = debugRadiusColor;
        Gizmos.DrawWireSphere(sonarBoat.position, sonarWorldRadius);
    }



}
