using UnityEngine;


public class SonarProxyBoat : MonoBehaviour
{
    [Header("Follow Settings")]
    [SerializeField] private string sourceBoatTag = "Boat";
    [SerializeField] private bool followY = false;

    [Header("Manual Offset")]
    [SerializeField] private Vector3 manualOffset;

    private Transform sourceBoat;

    private Vector3 sourceStart;
    private Vector3 proxyStart;

    private Quaternion sourceStartRot;
    private Quaternion proxyStartRot;

    private bool trackingActive = false;



    public void BeginTracking()
    {
        if (!sourceBoat)
        {
     
            GameObject boatObj = GameObject.FindGameObjectWithTag(sourceBoatTag);

            if (!boatObj)
            {
                Debug.LogError("SonarProxyBoat: No source boat found with tag " + sourceBoatTag);
                return;
            }

            sourceBoat = boatObj.transform;
        }

        sourceStart = sourceBoat.position;
        proxyStart  = transform.position;

        sourceStartRot = sourceBoat.rotation;
        proxyStartRot  = transform.rotation;

       


        trackingActive = true;
        enabled = true;
    }

    void LateUpdate()
    {

        
        if (!trackingActive || !sourceBoat)
            return;

        // ----------------------------
        // POSITION
        // ----------------------------
        Vector3 delta = sourceBoat.position - sourceStart;
        Vector3 target = proxyStart + delta + manualOffset;

        transform.position = new Vector3(
            target.x,
            followY ? target.y : proxyStart.y,
            target.z
        );

        // ----------------------------
        // ROTATION
        // ----------------------------
        Quaternion deltaRot = sourceBoat.rotation * Quaternion.Inverse(sourceStartRot);
        transform.rotation = deltaRot * proxyStartRot;
    }
}
