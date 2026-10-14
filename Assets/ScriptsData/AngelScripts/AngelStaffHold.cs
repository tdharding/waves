using UnityEngine;

// Locks the angel's staff to her hand. Goes on the staff object in the AngelPrefab hierarchy.
//
// Every frame the staff is put on the hand bone, moved by Grip Position / Grip Rotation (both in
// the hand's own space, so the grip holds however the hand turns), and given its Scale.
//
// To set the grip: place the staff in her hand by eye, then right-click this component ▸
// Capture Grip From Current Placement.
//
// Runs after everything else in LateUpdate — the Animator has posed the hand by then and
// AngelCompanion has moved her — so the staff never trails the hand by a frame.
[ExecuteAlways]
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public class AngelStaffHold : MonoBehaviour
{
    [Tooltip("The hand bone the staff is held in, e.g. Armature/.../R.hand.bone.")]
    [SerializeField] Transform hand;

    [Tooltip("Where the staff sits, in the hand bone's space.")]
    [SerializeField] Vector3 gripPosition = Vector3.zero;

    [Tooltip("How the staff is turned, in the hand bone's space (degrees).")]
    [SerializeField] Vector3 gripRotation = Vector3.zero;

    [Tooltip("The staff's scale.")]
    [SerializeField] float scale = 1f;

    void Reset()
    {
        scale = transform.localScale.x;
    }

    void LateUpdate()
    {
        transform.localScale = Vector3.one * scale;
        if (hand == null) return;
        transform.SetPositionAndRotation(
            hand.TransformPoint(gripPosition),
            hand.rotation * Quaternion.Euler(gripRotation));
    }

    [ContextMenu("Capture Grip From Current Placement")]
    void CaptureGrip()
    {
        if (hand == null)
        {
            Debug.LogWarning("AngelStaffHold: assign the hand bone first.", this);
            return;
        }
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Capture Staff Grip");
#endif
        gripPosition = hand.InverseTransformPoint(transform.position);
        gripRotation = (Quaternion.Inverse(hand.rotation) * transform.rotation).eulerAngles;
        scale = transform.localScale.x;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
