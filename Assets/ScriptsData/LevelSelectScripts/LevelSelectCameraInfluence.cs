using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// A node's Camera Influence: a camera set by hand at a node, and a box across the river there.
/// While the boat is in the box the follow camera (<see cref="LevelSelectCameraController"/>) is
/// drawn towards this camera, and the boat stays the player's the whole time.
///
/// Influence: the follow camera moves only on the amounts turned up — Height swings it up or down
/// to this camera's height, Across swings it to this camera's side of the river, Distance takes it
/// to this camera's distance from the boat, Aim turns it towards this camera's facing, Lens takes
/// on its field of view. With Distance at 0 it keeps its own distance from the boat. It comes in
/// across the Fade at either end of the box.
/// Cut: the view cuts to this camera on entering the box and back on leaving, like a CCTV camera,
/// either fixed or turning to keep the boat in frame — or, with Blend on Transition, the view moves
/// there and back over Transition Time.
///
/// Either mode can also put its own Fog Distance on the world and focus the follow camera's depth of
/// field on a View Target while the boat is in the box — Influence brings both in across the Fade.
///
/// Anchoring with Q cuts back to the ordinary camera; setting off again cuts back in.
///
/// Built by the Level Select Designer (the node's Camera Influence toggle). The box is written
/// here by the designer; the camera itself is this object's transform and Cinemachine lens, set
/// by hand. The Cinemachine camera is kept switched off so the brain never takes it live — the
/// follow camera reads it instead.
/// </summary>
public class LevelSelectCameraInfluence : MonoBehaviour
{
    public enum Mode { Influence, Cut }
    public enum LookAt { FollowBoat, Fixed }
    public enum Blend { Cut, Transition }

    [Tooltip("The designer node this belongs to. Written by the designer.")]
    public string nodeId;

    [Tooltip("Influence: the follow camera is drawn towards this one by the amounts below, keeping its " +
             "distance from the boat. Cut: the view cuts to this camera while the boat is in the box.")]
    public Mode mode = Mode.Influence;

    [Header("Box (written by the designer)")]
    public Vector3    boxCentre;
    public Quaternion boxRotation = Quaternion.identity;
    [Tooltip("Across the river, up, and along the river.")]
    public Vector3    boxSize = Vector3.one;
    [Tooltip("Influence: how far in from either end of the box the pull takes to come all the way in.")]
    [Min(0f)] public float fade = 1f;

    [Header("Influence")]
    [Range(0f, 1f)] public float height = 1f;
    [Range(0f, 1f)] public float across;
    [Range(0f, 1f)] public float aim;
    [Range(0f, 1f)] public float lens;
    [Range(0f, 1f)] public float distance;

    [Header("Cut")]
    public LookAt lookAt = LookAt.FollowBoat;
    [Tooltip("Cut: straight to this camera on entering the box and straight back on leaving. Transition: " +
             "the view moves there and back over Transition Time.")]
    public Blend blend = Blend.Cut;
    [Tooltip("Transition: how long the move to this camera, and back, takes.")]
    [Min(0f)] public float transitionTime = 1f;

    [Header("Fog and Focus")]
    [Tooltip("In the box the fog is full at Fog Distance from the boat, instead of the world's White Fade " +
             "Radius. Influence: it comes in across the Fade with the pull. Written by the designer.")]
    public bool overrideFogDistance;
    [Min(0f)] public float fogDistance;

    [Tooltip("In the box the follow camera's depth of field focuses on View Target instead of its own focus. " +
             "Influence: it comes in across the Fade with the pull. Written by the designer.")]
    public bool focusOnViewTarget;
    [Tooltip("What the depth of field focuses on in the box. Set in the designer's node row.")]
    public Transform viewTarget;

    private CinemachineCamera _cam;

    private static readonly List<LevelSelectCameraInfluence> s_all = new List<LevelSelectCameraInfluence>();

    /// <summary>Every influence switched on in the scene.</summary>
    public static IReadOnlyList<LevelSelectCameraInfluence> All => s_all;

    /// <summary>This camera's vertical field of view — its Cinemachine lens.</summary>
    public float FieldOfView
    {
        get
        {
            if (_cam == null) _cam = GetComponent<CinemachineCamera>();
            return _cam != null ? _cam.Lens.FieldOfView : 60f;
        }
    }

    /// <summary>What the depth of field focuses on in the box, if anything.</summary>
    public Transform FocusTarget => focusOnViewTarget ? viewTarget : null;

    /// <summary>The box's line across the river, flat.</summary>
    public Vector3 AcrossAxis => boxRotation * Vector3.right;

    private void Awake()
    {
        // The follow camera reads this one; the brain must never take it live.
        _cam = GetComponent<CinemachineCamera>();
        if (_cam != null) _cam.enabled = false;
    }

    private void OnEnable()  { if (!s_all.Contains(this)) s_all.Add(this); }
    private void OnDisable() { s_all.Remove(this); }

    /// <summary>
    /// How much this pulls with the boat at <paramref name="boat"/>: 0 outside the box. Inside, Cut
    /// is all or nothing; Influence comes in across the Fade at each end of the box.
    /// </summary>
    public float Weight(Vector3 boat)
    {
        Vector3 local = Quaternion.Inverse(boxRotation) * (boat - boxCentre);
        Vector3 half  = boxSize * 0.5f;
        if (Mathf.Abs(local.x) > half.x || Mathf.Abs(local.y) > half.y || Mathf.Abs(local.z) > half.z)
            return 0f;

        if (mode == Mode.Cut || fade <= 0f) return 1f;
        float fromEnd = half.z - Mathf.Abs(local.z);
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fromEnd / fade));
    }

    /// <summary>The strongest influence over the boat, if any.</summary>
    public static LevelSelectCameraInfluence Strongest(Vector3 boat, out float weight)
    {
        LevelSelectCameraInfluence best = null;
        weight = 0f;
        for (int i = 0; i < s_all.Count; i++)
        {
            var influence = s_all[i];
            if (influence == null) continue;
            float w = influence.Weight(boat);
            if (w <= weight) continue;
            best   = influence;
            weight = w;
        }
        return best;
    }
}
