using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Pose tab — labelled bone handles in the Scene view, and left/right symmetry.
//
// Draws every bone as a labelled dot you can click, so a skeleton can be posed without hunting
// through the Hierarchy. Clicking a dot only sets the Unity selection: the normal rotate/move tools
// still do the work, and if the Animation window is recording the change is keyed as usual.
//
// Symmetry reflects a bone's transform through a plane of the RIG ROOT's own space rather than
// copying local rotations across. Copying locals looks correct until it doesn't: a Blender export
// gives left and right bones different local axes (bone roll is per bone), so the usual "negate y
// and z of the local quaternion" mirrors some chains and scrambles others. Reflecting in rig space
// is blind to bone roll and works whatever the exporter did. It does assume the REST pose is
// symmetric, which holds for a mirror-modelled character.
//
// Scale is the exception: it is copied straight across rather than reflected, because a mirrored
// scale is still a magnitude and has no direction to flip. That is exact for uniform scale. For
// NON-uniform scale it is only exact while both sides share their local axes — a bone rolled
// differently on the right will stretch along a different direction than its partner did.
[System.Serializable]
public class BonePoseTool : AnimateTool
{
    public enum MirrorAxis { X, Y, Z }

    [SerializeField] bool       showLabels = true;
    [SerializeField] float      dotSize    = 0.05f;
    [SerializeField] bool       liveSymmetry;
    [SerializeField] MirrorAxis mirrorAxis     = MirrorAxis.X;
    [SerializeField] bool       mirrorPosition = true;
    [SerializeField] bool       mirrorRotation = true;
    [SerializeField] bool       mirrorScale    = true;

    bool AnyProperty => mirrorPosition || mirrorRotation || mirrorScale;

    // Live-symmetry watch: the bone being dragged and the pose it had last time we looked.
    [System.NonSerialized] Transform  watched;
    [System.NonSerialized] Vector3    watchedPos;
    [System.NonSerialized] Quaternion watchedRot;
    [System.NonSerialized] Vector3    watchedScale;

    [System.NonSerialized] GUIStyle labelStyle;

    static readonly Color DotColor      = new Color(0.55f, 0.80f, 1f, 0.90f);
    static readonly Color SelectedColor = Color.white;
    static readonly Color PartnerColor  = new Color(1f, 0.75f, 0.20f, 1f);
    static readonly Color BoneLineColor = new Color(0.55f, 0.80f, 1f, 0.35f);

    public override string Title => "Pose";

    // ────────────────────────────────── mirror maths ──────────────────────────────────

    Vector3 MirrorVector(Vector3 v)
    {
        switch (mirrorAxis)
        {
            case MirrorAxis.X: return new Vector3(-v.x,  v.y,  v.z);
            case MirrorAxis.Y: return new Vector3( v.x, -v.y,  v.z);
            default:           return new Vector3( v.x,  v.y, -v.z);
        }
    }

    // Reflecting a rotation through a plane is the same as turning it 180° about that plane's
    // normal — the reflection's sign cancels in the sandwich M·R·M, leaving a proper rotation.
    // For a quaternion that comes out as a pair of sign flips on the components off the axis.
    Quaternion MirrorQuat(Quaternion q)
    {
        switch (mirrorAxis)
        {
            case MirrorAxis.X: return new Quaternion( q.x, -q.y, -q.z, q.w);
            case MirrorAxis.Y: return new Quaternion(-q.x,  q.y, -q.z, q.w);
            default:           return new Quaternion(-q.x, -q.y,  q.z, q.w);
        }
    }

    // What one bone contributes to its partner. Position and rotation are held in rig space so the
    // reflection is a plain sign flip; scale stays local, having no direction to reflect.
    struct Pose
    {
        public Vector3    pos;
        public Quaternion rot;
        public Vector3    scale;
    }

    static Pose ReadPose(RigContext rig, Transform b) => new Pose
    {
        pos   = rig.root.InverseTransformPoint(b.position),
        rot   = Quaternion.Inverse(rig.root.rotation) * b.rotation,
        scale = b.localScale,
    };

    void WriteMirrored(RigContext rig, Transform dst, Pose p)
    {
        if (mirrorPosition) dst.position   = rig.root.TransformPoint(MirrorVector(p.pos));
        if (mirrorRotation) dst.rotation   = rig.root.rotation * MirrorQuat(p.rot);
        if (mirrorScale)    dst.localScale = p.scale;
    }

    void MirrorOne(RigContext rig, Transform src, string undoLabel)
    {
        if (src == null || !AnyProperty) return;
        if (!rig.partners.TryGetValue(src, out var dst) || dst == null) return;

        Undo.RecordObject(dst, undoLabel);
        WriteMirrored(rig, dst, ReadPose(rig, src));
    }

    // Whole-rig mirror. Every source pose is read before anything is written, because each pair is
    // its own opposite — writing as we went would mirror bones we had already overwritten.
    void MirrorPose(RigContext rig, bool leftToRight)
    {
        if (rig.root == null || !AnyProperty) return;
        rig.Refresh();

        var source = new Dictionary<Transform, Pose>();
        foreach (var b in rig.bones) source[b] = ReadPose(rig, b);

        var sources = new List<Transform>();
        var targets = new List<Transform>();
        foreach (var b in rig.bones)
        {
            if (!rig.partners.TryGetValue(b, out var p) || p == null) continue;
            RigContext.TryPartner(b.name, out RigContext.Side side, out _);
            if (side != (leftToRight ? RigContext.Side.Left : RigContext.Side.Right)) continue;
            sources.Add(b); targets.Add(p);
        }

        if (targets.Count == 0)
        {
            Debug.LogWarning("[Animate Suite] No left/right bone pairs found under " + rig.root.name);
            return;
        }

        Undo.RecordObjects(targets.ToArray(), "Mirror Pose");
        for (int i = 0; i < targets.Count; i++)   // parents first, so a child corrects after its parent moved
            WriteMirrored(rig, targets[i], source[sources[i]]);
    }

    // ────────────────────────────────── rest pose ──────────────────────────────────

    // Where each bone sat when the mesh was skinned to it. bindposes[i] is the inverse of bone i's
    // matrix relative to the renderer, so the renderer's own matrix times that inverse puts the bone
    // back exactly where the skin expects it. That is the mesh's own idea of a default — not the
    // prefab's, not the clip's, and not affected by anything since.
    static Dictionary<Transform, Matrix4x4> BindPoses(RigContext rig)
    {
        var bind = new Dictionary<Transform, Matrix4x4>();
        if (rig.root == null) return bind;

        foreach (var smr in rig.root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null) continue;

            var poses = mesh.bindposes;
            var bound = smr.bones;

            // A rig can carry several skinned meshes over the same skeleton (body, dress, wings).
            // They agree on the bind pose, so the first one to claim a bone wins.
            int n = Mathf.Min(poses.Length, bound.Length);
            for (int i = 0; i < n; i++)
                if (bound[i] != null && !bind.ContainsKey(bound[i]))
                    bind[bound[i]] = smr.transform.localToWorldMatrix * poses[i].inverse;
        }

        return bind;
    }

    // Restores the bone's bind-time LOCAL transform, worked out against its parent's bind matrix
    // rather than wherever the parent happens to be now. Reset a forearm on a raised arm and it
    // straightens against the arm as posed, instead of tearing off to where the mesh was skinned.
    //
    // Leaf bones (the *_end transforms Blender exports) carry no skin weights and so have no bind
    // pose at all. Leaving them alone is correct: they ride whatever their parent does.
    //
    // Exact unless a PARENT bone carries non-uniform scale in its bind pose, which puts shear in the
    // local matrix that position/rotation/scale cannot express — a limit of Transform itself, not of
    // the arithmetic here. Armature bones export with unit scale, so this does not arise in practice.
    void ResetToBind(RigContext rig, List<Transform> selection)
    {
        var bind = BindPoses(rig);
        if (bind.Count == 0)
        {
            Debug.LogWarning($"[Animate Suite] No skinned mesh under {rig.root.name}, " +
                             "so there is no bind pose to reset to.");
            return;
        }

        var resettable = new List<Transform>();
        foreach (var b in selection)
            if (bind.ContainsKey(b)) resettable.Add(b);

        if (resettable.Count == 0)
        {
            Debug.LogWarning("[Animate Suite] None of the selected bones are skinned to the mesh.");
            return;
        }

        Undo.RecordObjects(resettable.ToArray(), "Reset To Mesh Default");

        foreach (var b in resettable)
        {
            // A bone whose parent is unskinned (a hip hanging off the Armature) falls back to where
            // that parent is now, which is the same thing unless the Armature itself was moved.
            Matrix4x4 parentWorld =
                b.parent == null                            ? Matrix4x4.identity :
                bind.TryGetValue(b.parent, out var parentBind) ? parentBind
                                                              : b.parent.localToWorldMatrix;

            Matrix4x4 local = parentWorld.inverse * bind[b];

            b.localPosition = local.GetPosition();
            b.localRotation = local.rotation;
            b.localScale    = local.lossyScale;
        }
    }

    // ────────────────────────────────── live symmetry ──────────────────────────────────

    // Polls the selected bone rather than hooking the handles, so it mirrors whatever moved it:
    // the rotate tool, the Inspector, or a curve dragged in the Animation window.
    public override void OnUpdate(RigContext rig)
    {
        if (!liveSymmetry || rig.root == null || !AnyProperty) { watched = null; return; }

        var t = Selection.activeTransform;
        if (t == null || !rig.boneSet.Contains(t)) { watched = null; return; }

        if (t != watched) { Watch(t); return; }

        // Watches all three properties whatever is being mirrored, so nudging a bone by any means
        // re-applies the enabled ones.
        if ((t.localPosition - watchedPos).sqrMagnitude   < 1e-12f &&
            (t.localScale    - watchedScale).sqrMagnitude < 1e-12f &&
            Quaternion.Angle(t.localRotation, watchedRot) < 1e-4f) return;

        MirrorOne(rig, t, "Mirror Bone");
        Watch(t);
        SceneView.RepaintAll();
    }

    void Watch(Transform t)
    {
        watched      = t;
        watchedPos   = t.localPosition;
        watchedRot   = t.localRotation;
        watchedScale = t.localScale;
    }

    // ────────────────────────────────── scene view ──────────────────────────────────

    public override void OnSceneGUI(RigContext rig, SceneView sv)
    {
        if (labelStyle == null)
            labelStyle = new GUIStyle(EditorStyles.whiteMiniLabel) { fontSize = 10 };

        Transform selected = Selection.activeTransform;
        Transform selectedPartner = null;
        if (selected != null) rig.partners.TryGetValue(selected, out selectedPartner);

        // Bones sit inside the mesh, so draw on top or half the rig is unclickable.
        var prevZ = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

        Handles.color = BoneLineColor;
        foreach (var b in rig.bones)
        {
            if (b == null) continue;
            for (int c = 0; c < b.childCount; c++)
            {
                var child = b.GetChild(c);
                if (rig.boneSet.Contains(child)) Handles.DrawAAPolyLine(2f, b.position, child.position);
            }
        }

        foreach (var b in rig.bones)
        {
            if (b == null) continue;

            float size = HandleUtility.GetHandleSize(b.position) * dotSize;

            Handles.color = b == selected        ? SelectedColor
                          : b == selectedPartner ? PartnerColor
                                                 : DotColor;

            if (Handles.Button(b.position, Quaternion.identity, size, size * 1.4f, Handles.DotHandleCap))
                Selection.activeTransform = b;

            if (showLabels)
            {
                labelStyle.normal.textColor = b == selectedPartner ? PartnerColor : Color.white;
                Handles.Label(b.position + Vector3.up * size * 1.5f, b.name, labelStyle);
            }
        }

        Handles.zTest = prevZ;
    }

    // ────────────────────────────────── window ──────────────────────────────────

    public override void OnGUI(RigContext rig)
    {
        EditorGUILayout.LabelField("Handles", EditorStyles.boldLabel);
        showLabels = EditorGUILayout.Toggle("Show Labels", showLabels);
        dotSize    = EditorGUILayout.Slider("Dot Size", dotSize, 0.01f, 0.2f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Symmetry", EditorStyles.boldLabel);
        mirrorAxis   = (MirrorAxis)EditorGUILayout.EnumPopup("Mirror Axis", mirrorAxis);
        liveSymmetry = EditorGUILayout.Toggle("Live Symmetry", liveSymmetry);
        if (liveSymmetry)
            EditorGUILayout.HelpBox("Moving a bone writes the mirrored pose to its opposite number.",
                                    MessageType.None);

        // Which properties travel across. A property left off is not touched on the partner at all,
        // so rotation-only mirroring leaves hand-placed bone offsets and scales alone.
        EditorGUILayout.LabelField("Mirror Properties", EditorStyles.miniBoldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            mirrorPosition = EditorGUILayout.Toggle("Position", mirrorPosition);
            mirrorRotation = EditorGUILayout.Toggle("Rotation", mirrorRotation);
            mirrorScale    = EditorGUILayout.Toggle("Scale",    mirrorScale);
        }

        if (!AnyProperty)
            EditorGUILayout.HelpBox("No properties selected — mirroring will do nothing.",
                                    MessageType.Warning);

        EditorGUILayout.Space();

        var  sel       = Selection.activeTransform;
        bool selIsBone = sel != null && rig.partners.ContainsKey(sel);
        using (new EditorGUI.DisabledScope(!selIsBone || !AnyProperty))
        {
            if (GUILayout.Button(selIsBone ? $"Mirror {sel.name} → {rig.partners[sel].name}"
                                           : "Mirror Selected Bone"))
                MirrorOne(rig, sel, "Mirror Bone");
        }

        using (new EditorGUI.DisabledScope(!AnyProperty))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Mirror Pose L → R")) MirrorPose(rig, true);
            if (GUILayout.Button("Mirror Pose R → L")) MirrorPose(rig, false);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rest Pose", EditorStyles.boldLabel);

        var picked = rig.SelectedBones();
        using (new EditorGUI.DisabledScope(picked.Count == 0))
        {
            string what = picked.Count == 0 ? "Selected Bone"
                        : picked.Count == 1 ? picked[0].name
                                            : $"{picked.Count} Bones";

            if (GUILayout.Button($"Reset {what} to Mesh Default"))
                ResetToBind(rig, picked);
        }
    }
}
