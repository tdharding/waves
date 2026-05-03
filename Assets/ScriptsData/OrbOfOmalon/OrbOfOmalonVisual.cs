using UnityEngine;

public class OrbOfOmalonVisual : MonoBehaviour
{
    [Header("Wave Settings")]
    public float extraYOffset = 0.5f;
    public float heightMultiplier = 1f;

    [Header("LOD")]
    public float activeDistance = 25f;

    private Transform waterTransform;
    private Transform boatRoot;

    private Material mat;

    void Start()
    {
        if (LevelDataController.Instance == null)
            return;

        waterTransform = LevelDataController.Instance.GetWaveTransform();
        boatRoot = LevelDataController.Instance.GetBoatRoot();

        if (!waterTransform || !boatRoot)
            return;

        mat = waterTransform.GetComponent<MeshRenderer>().sharedMaterial;
    }

    void LateUpdate()
    {
        if (!waterTransform || !boatRoot)
            return;

        float sqrDist = (boatRoot.position - transform.position).sqrMagnitude;
        float maxDist = activeDistance * activeDistance;

        if (sqrDist > maxDist)
            return;

        ApplyWaveHeight();
    }

    void ApplyWaveHeight()
    {
        var p = WaveUtils.ReadParams(waterTransform, mat);
        float height = WaveUtils.SampleHeight(transform.position, p, heightMultiplier) + extraYOffset;

        Vector3 pos = transform.position;
        pos.y = p.origin.y + height;
        transform.position = pos;
    }
}
