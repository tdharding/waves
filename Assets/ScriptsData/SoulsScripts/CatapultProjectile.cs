using System.Collections;
using UnityEngine;

/// <summary>
/// Ballistic arc launched by the catapult tool.
/// All settings are assigned by CatapultController after instantiation.
/// </summary>
public class CatapultProjectile : MonoBehaviour
{
    [HideInInspector] public float arcDuration;
    [HideInInspector] public float arcPeakHeight;
    [HideInInspector] public float obstacleCheckRadius;
    [HideInInspector] public string[] collidableTags;
    [HideInInspector] public float extraYOffset;
    [HideInInspector] public float heightMultiplier;
    [HideInInspector] public GameObject droppedSoulPrefab;
    [HideInInspector] public GameObject explosionVFXPrefab;
    [HideInInspector] public GameObject splashPrefab;

    private int _soulID;
    private int _linkID;
    private Vector3 _launchPos;
    private Vector3 _landingXZ;
    private Transform _waterTransform;
    private Material  _waterMat;

    public void Launch(Vector3 startPos, Vector3 direction, float distance, int soulID, int linkID)
    {
        _soulID    = soulID;
        _linkID    = linkID;
        _launchPos = startPos;

        Vector3 landingCandidate = startPos + direction.normalized * distance;
        _landingXZ = new Vector3(landingCandidate.x, 0f, landingCandidate.z);

        if (!CacheWaterData())
        {
            Debug.LogWarning("[CatapultProjectile] No water data — destroying.");
            Destroy(gameObject);
            return;
        }

        StartCoroutine(ArcRoutine());
    }

    private bool CacheWaterData()
    {
        if (LevelDataController.Instance == null) return false;
        _waterTransform = LevelDataController.Instance.GetWaveTransform();
        if (_waterTransform == null) return false;
        var mr = _waterTransform.GetComponent<MeshRenderer>();
        if (mr == null) return false;
        _waterMat = mr.sharedMaterial;
        return true;
    }

    private float SampleWaveY(Vector3 worldXZPos)
    {
        var p = WaveUtils.ReadParams(_waterTransform, _waterMat);
        return p.origin.y + WaveUtils.SampleHeight(worldXZPos, p, heightMultiplier) + extraYOffset;
    }

    private IEnumerator ArcRoutine()
    {
        float t = 0f;

        while (t < arcDuration)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / arcDuration);

            float   currentLandingY = SampleWaveY(new Vector3(_landingXZ.x, 0f, _landingXZ.z));
            Vector3 landingPos      = new Vector3(_landingXZ.x, currentLandingY, _landingXZ.z);

            Vector3 prevPos = transform.position;

            Vector3 flatPos = Vector3.Lerp(_launchPos, landingPos, n);
            float   arcY    = Mathf.Sin(n * Mathf.PI) * arcPeakHeight;
            Vector3 newPos  = new Vector3(flatPos.x, flatPos.y + arcY, flatPos.z);

            transform.position = newPos;

            // Face forward along the arc trajectory
            Vector3 travelDir = newPos - prevPos;
            if (travelDir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(travelDir);

            if (HitsObstacle(transform.position))
            {
                OnObstacleHit();
                yield break;
            }

            yield return null;
        }

        OnLand();
    }

    private bool HitsObstacle(Vector3 pos)
    {
        Collider[] hits = Physics.OverlapSphere(pos, obstacleCheckRadius);
        foreach (Collider col in hits)
            foreach (string tag in collidableTags)
                if (col.CompareTag(tag)) return true;
        return false;
    }

    private void OnObstacleHit()
    {
        if (explosionVFXPrefab != null)
            Instantiate(explosionVFXPrefab, transform.position, Quaternion.identity);
        Destroy(gameObject);
    }

    private void OnLand()
    {
        float   finalY     = SampleWaveY(new Vector3(_landingXZ.x, 0f, _landingXZ.z));
        Vector3 landingPos = new Vector3(_landingXZ.x, finalY, _landingXZ.z);

        if (splashPrefab != null)
            Instantiate(splashPrefab, landingPos, Quaternion.identity);

        if (droppedSoulPrefab != null)
        {
            GameObject droppedObj = Instantiate(droppedSoulPrefab, landingPos, Quaternion.identity);
            DroppedSoul ds        = droppedObj.GetComponent<DroppedSoul>();
            if (ds != null)
                ds.ConfigureDroppedSoul(_soulID, _linkID);
        }

        Destroy(gameObject);
    }
}
