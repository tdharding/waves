using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Place on a child transform of the prefab root, at the height where the water line should meet the object.
// Move this transform up/down in the prefab to position the waterline disc on the object.
// At spawn, LevelSpawner offsets the prefab so this disc aligns with the BaselineMarker disc.
public class PrefabBaselineAlignment : MonoBehaviour
{
    [SerializeField] bool showDebug = false;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!showDebug) return;

        // Green disc — waterline: move this transform to where the water meets the object
        Handles.color = new Color(0f, 0.9f, 0.5f, 0.6f);
        Handles.DrawSolidDisc(transform.position, Vector3.up, 1.5f);
        Handles.color = new Color(0f, 0.9f, 0.5f, 0.85f);
        Handles.Label(transform.position + Vector3.up * 0.2f, "WATERLINE");
    }
#endif
}
