using UnityEngine;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
public class BoatStartupCoordinator : MonoBehaviour
{
    public BoatMovement boatMovement;
    public BoatColliderFollower colliderFollower;

    private CharacterController controller;
    private bool hasStarted = false;

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        // Hard disable everything immediately
        controller.enabled = false;

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

        // Enable CharacterController
        controller.enabled = true;

        // Wait one physics step for capsule sync
        yield return new WaitForFixedUpdate();

        // Enable movement
        if (boatMovement != null)
            boatMovement.controlsEnabled = true;

        // Enable collider follower LAST
        if (colliderFollower != null)
            colliderFollower.enabled = true;
    }
}
