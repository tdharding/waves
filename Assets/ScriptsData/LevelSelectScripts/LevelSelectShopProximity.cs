using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Handles proximity-based camera switching for shops.
/// Activates the shop's local Cinemachine camera when the boat enters the trigger.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LevelSelectShopProximity : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Cinemachine camera associated with this shop.")]
    public CinemachineCamera shopCamera;
    
    [Tooltip("Tag used to identify the boat.")]
    public string boatTag = "BoatPrefab";

    [Header("Priority Settings")]
    [Tooltip("Priority when the boat is inside the proximity zone.")]
    public int activePriority = 20;
    
    [Tooltip("Priority when the boat is outside.")]
    public int inactivePriority = 0;

    private LevelSelectCameraController _mainController;

    private void Awake()
    {
        // Ensure the collider is set to trigger
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            col.isTrigger = true;
        }

        // Auto-find shop camera if not assigned
        if (shopCamera == null)
        {
            shopCamera = GetComponentInChildren<CinemachineCamera>();
        }

        if (shopCamera != null)
        {
            var p = shopCamera.Priority;
            p.Value = inactivePriority;
            shopCamera.Priority = p;
        }

        // Find the main camera controller
        _mainController = Object.FindFirstObjectByType<LevelSelectCameraController>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(boatTag)) return;

        if (shopCamera != null)
        {
            var p = shopCamera.Priority;
            p.Value = activePriority;
            shopCamera.Priority = p;
        }

        if (_mainController != null)
        {
            _mainController.IsControlEnabled = false;
        }

        if (OrbCollectCounter.Instance != null)
        {
            OrbCollectCounter.Instance.SetForceVisible(true);
        }
        }

        private void OnTriggerExit(Collider other)
        {
        if (!other.CompareTag(boatTag)) return;

        if (shopCamera != null)
        {
            var p = shopCamera.Priority;
            p.Value = inactivePriority;
            shopCamera.Priority = p;
        }

        if (_mainController != null)
        {
            _mainController.IsControlEnabled = true;
        }

        if (OrbCollectCounter.Instance != null)
        {
            OrbCollectCounter.Instance.SetForceVisible(false);
        }
        }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(0.7f, 0.3f, 1f, 0.15f);
        if (col is SphereCollider sc)
        {
            Gizmos.DrawSphere(transform.position, sc.radius * transform.lossyScale.x);
        }
        else if (col is BoxCollider bc)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(bc.center, bc.size);
        }
    }
#endif
}
