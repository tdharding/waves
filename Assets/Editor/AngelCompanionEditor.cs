using UnityEditor;
using UnityEngine;

// The Scene view handle for the angel's face-the-boat bone: an arrow out of the bone along the
// side that turns to face the boat, with a dot on its tip. Drag the dot round the bone's own Y
// axis to set which way its front points.
[CustomEditor(typeof(AngelCompanion))]
public class AngelCompanionEditor : Editor
{
    static readonly Color ArrowColour = new Color(1f, 0.75f, 0.2f, 1f);

    public override void OnInspectorGUI() => DrawDefaultInspector();

    void OnSceneGUI()
    {
        var angel = (AngelCompanion)target;
        Transform bone = angel.FaceBoatBone;
        if (bone == null) return;

        Quaternion rest  = angel.FaceBoatBoneRestRotation;
        Vector3    axis  = rest * Vector3.up;
        Vector3    front = angel.FaceBoatBoneFront(rest);
        Vector3    root  = bone.position;
        float      size  = HandleUtility.GetHandleSize(root) * 1.2f;
        Vector3    tip   = root + front * size;

        Handles.color = ArrowColour;
        Handles.DrawLine(root, tip, 2f);
        if (Event.current.type == EventType.Repaint)
            Handles.ConeHandleCap(0, tip, Quaternion.LookRotation(front, axis), size * 0.12f, EventType.Repaint);

        // The dot follows the mouse across the plane the bone turns in, so however the view is
        // angled the drag only ever sets the one angle.
        EditorGUI.BeginChangeCheck();
        Vector3 dot = tip + front * size * 0.15f;
        Handles.FreeMoveHandle(dot, size * 0.08f, Vector3.zero, Handles.SphereHandleCap);
        if (!EditorGUI.EndChangeCheck()) return;

        var  plane = new Plane(axis, root);
        Ray  ray   = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        if (!plane.Raycast(ray, out float hit)) return;

        Vector3 local = Quaternion.Inverse(rest) * (ray.GetPoint(hit) - root);
        if (new Vector2(local.x, local.z).sqrMagnitude < 1e-8f) return;

        serializedObject.Update();
        serializedObject.FindProperty("faceBoatBoneForward").floatValue =
            Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        serializedObject.ApplyModifiedProperties();
    }
}
