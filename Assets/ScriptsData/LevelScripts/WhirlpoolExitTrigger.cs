using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
public class WhirlpoolExitTrigger : MonoBehaviour
{
    [Tooltip("Trigger sphere radius in world units")]
    public float triggerRadius = 1f;
    [Tooltip("Vertical offset so the trigger aligns with the water surface")]
    public float triggerYOffset = -4f;

    private void Start()
    {
        var col = GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.center    = new Vector3(0f, triggerYOffset, 0f);
        float lossyScale = transform.lossyScale.x;
        col.radius = lossyScale > 0.0001f ? triggerRadius / lossyScale : triggerRadius;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("BoatPrefab")) return;

        var exitCtrl = FindObjectOfType<LevelExitController>();
        if (exitCtrl == null) return;

        exitCtrl.ForceExit();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.8f, 0.2f, 0.2f, 0.4f);
        Gizmos.DrawSphere(transform.position + Vector3.up * triggerYOffset, triggerRadius);
        Gizmos.color = new Color(0.8f, 0.2f, 0.2f, 1f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * triggerYOffset, triggerRadius);
    }
}
