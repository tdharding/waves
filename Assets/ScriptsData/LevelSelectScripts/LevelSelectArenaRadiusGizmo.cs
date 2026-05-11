using UnityEngine;

public class LevelSelectArenaRadiusGizmo : MonoBehaviour
{
    public float radius      = 10f;
    public float northOffset = 0f;   // degrees — rotate the N marker around the ring
    public Color gizmoColor  = new Color(0f, 1f, 1f, 0.4f);

    [TextArea(2, 8)]
    public string debugAngles = "Select in scene to refresh";

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        UnityEditor.Handles.color = gizmoColor;
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, radius);

        // North marker (rotatable)
        float   nRad     = northOffset * Mathf.Deg2Rad;
        Vector3 nDir     = new Vector3(Mathf.Sin(nRad), 0f, Mathf.Cos(nRad));
        Vector3 northPos = transform.position + nDir * radius;
        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.DrawLine(transform.position + nDir * (radius - 1f), northPos, 3f);
        UnityEditor.Handles.Label(northPos + Vector3.up * 0.5f, "N");

        // Line from arena centre to each entrance direction hint
        var hints = GetComponentsInChildren<LevelSelectArenaEntranceDirectionHint>();
        foreach (var hint in hints)
        {
            Vector3 dir      = new Vector3(hint.transform.forward.x, 0f, hint.transform.forward.z).normalized;
            Vector3 ringPos  = transform.position + dir * radius;
            UnityEditor.Handles.color = new Color(1f, 0.5f, 0.1f, 0.9f);
            UnityEditor.Handles.DrawLine(transform.position, ringPos, 2f);
            UnityEditor.Handles.DrawWireDisc(ringPos, Vector3.up, 0.3f);
        }

        // Debug arrow: centre → each LevelSelectEnter trigger
        var root   = transform.parent != null ? transform.parent : transform;
        var enters = root.GetComponentsInChildren<LevelSelectEnter>();
        var sb     = new System.Text.StringBuilder();
        foreach (var enter in enters)
        {
            Vector3 toEnt = enter.transform.position - transform.position;
            Vector3 tip   = enter.transform.position;
            Vector3 back  = tip - toEnt.normalized * 0.8f;
            Vector3 perp  = Vector3.Cross(toEnt.normalized, Vector3.up) * 0.4f;
            float   angle = Mathf.Atan2(toEnt.x, toEnt.z) * Mathf.Rad2Deg;
            string  entranceName = enter.gameObject.name;
            var     t = enter.transform;
            while (t != null && !t.name.StartsWith("Entrance_")) t = t.parent;
            if (t != null) entranceName = t.name;
            UnityEditor.Handles.color = Color.cyan;
            UnityEditor.Handles.DrawLine(transform.position, tip, 3f);
            UnityEditor.Handles.DrawLine(tip, back + perp, 2f);
            UnityEditor.Handles.DrawLine(tip, back - perp, 2f);
            UnityEditor.Handles.Label(tip + Vector3.up * 0.5f, $"{entranceName}  {angle:F1}°");
            sb.AppendLine($"{entranceName}: {angle:F1}°");
        }
        debugAngles = sb.Length > 0 ? sb.ToString() : "No entrances found";
    }
#endif
}
