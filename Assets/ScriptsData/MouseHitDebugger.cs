using System.Linq;
using UnityEngine;

/// <summary>
/// Debug utility. Drop on any GameObject in the scene.
/// Every left-click logs every collider the ray hits, ordered by distance.
/// Also logs which camera is being used.
/// Remove from scene when debugging is done.
/// </summary>
public class MouseHitDebugger : MonoBehaviour
{
    [SerializeField] private bool active = true;

    private void Update()
    {
        if (!active) return;
        if (!Input.GetMouseButtonDown(0)) return;

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[MouseHitDebugger] Camera.main is NULL — no camera tagged 'MainCamera'.");
            return;
        }

        Debug.Log($"[MouseHitDebugger] Camera: '{cam.name}' tag='{cam.tag}'");

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        if (hits.Length == 0)
        {
            Debug.Log("[MouseHitDebugger] No hits.");
            return;
        }

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit h = hits[i];
            string layer      = LayerMask.LayerToName(h.collider.gameObject.layer);
            string components = string.Join(", ", h.collider.GetComponents<Component>()
                                                             .Select(c => c.GetType().Name));
            Debug.Log($"[MouseHitDebugger] Hit [{i}] '{h.collider.gameObject.name}' " +
                      $"layer='{layer}' tag='{h.collider.tag}' dist={h.distance:F2}  " +
                      $"components=[{components}]");
        }
    }
}
