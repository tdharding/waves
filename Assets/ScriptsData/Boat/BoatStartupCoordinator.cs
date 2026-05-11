using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
public class BoatStartupCoordinator : MonoBehaviour
{
    public BoatMovement boatMovement;
    public BoatColliderFollower colliderFollower;

    private Rigidbody rb;
    private bool hasStarted = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // Hard disable everything immediately
        if (boatMovement != null)
            boatMovement.controlsEnabled = false;

        if (colliderFollower != null)
            colliderFollower.enabled = false;
    }

    // CALL THIS FROM LEVEL / DATA CONTROLLER
    public void BeginStartup()
    {
        if (hasStarted)
            return;

        hasStarted = true;
        StartCoroutine(StartupRoutine());
    }

    IEnumerator StartupRoutine()
    {
        // Let transforms & visuals settle
        yield return null;

        // Ensure Rigidbody starts kinematic if we want it to wait? 
        // Actually, movement script handles controlsEnabled.

        // Wait one physics step
        yield return new WaitForFixedUpdate();

        // Enable movement
        if (boatMovement != null)
            boatMovement.controlsEnabled = true;

        // Enable collider follower LAST
        if (colliderFollower != null)
            colliderFollower.enabled = true;
    }
}
