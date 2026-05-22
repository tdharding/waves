using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Add to a statue/world-space prefab.
// At spawn, LevelSpawner aligns this prefab's forward to match the BaselineMarker's forward,
// and places it at the BaselineMarker's height.
public class PrefabBaselineAlignment : MonoBehaviour
{
#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        // Draw at baseline height when in scene; fall back to own Y in prefab editor.
        var marker = FindObjectOfType<BaselineMarker>();
        float y    = marker != null ? marker.height : transform.position.y;
        Vector3 centre = new Vector3(transform.position.x, y, transform.position.z);

        Gizmos.color = new Color(0f, 0.9f, 0.5f, 0.9f);
        BaselineMarker.DrawArrow(centre, Vector3.up, 3f);

        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
        BaselineMarker.DrawArrow(centre, transform.forward, 4f);

        Handles.color = new Color(0.3f, 0.7f, 1f, 0.8f);
        Handles.Label(centre + Vector3.up * 3.6f, "Baseline Align");
    }
#endif
}
