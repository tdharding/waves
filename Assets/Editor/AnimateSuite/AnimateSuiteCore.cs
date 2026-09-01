using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// What every Animate Suite tab shares: which rig is being worked on, its bones, their left/right
// pairing, and the clip the clip-editing tabs act on. The window owns one of these and hands it to
// each tool, so picking the angel once sets every tab up and a tab never asks for the rig again.
[System.Serializable]
public class RigContext
{
    public Transform     root;
    public AnimationClip clip;

    [System.NonSerialized] public List<Transform>                  bones    = new List<Transform>();
    [System.NonSerialized] public HashSet<Transform>               boneSet  = new HashSet<Transform>();
    [System.NonSerialized] public Dictionary<Transform, Transform> partners = new Dictionary<Transform, Transform>();

    public int PairCount => partners.Count / 2;

    // Every descendant of the root that is not a mesh. Renderers are skipped because a skinned
    // model sits inside the rig as a sibling of the armature (AngelMesh1) and is not a bone.
    public void Refresh()
    {
        bones.Clear(); boneSet.Clear(); partners.Clear();
        if (root == null) return;

        // Case-insensitive so a rig that mixes L.hip.bone with L.Shoulder.Bone still pairs up.
        var byName = new Dictionary<string, Transform>(System.StringComparer.OrdinalIgnoreCase);

        // GetComponentsInChildren returns parents before children, which mirroring relies on.
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root || t.GetComponent<Renderer>() != null) continue;
            bones.Add(t);
            boneSet.Add(t);
            byName[t.name] = t;
        }

        foreach (var b in bones)
            if (TryPartner(b.name, out _, out string pn) && byName.TryGetValue(pn, out var p) && p != b)
                partners[b] = p;
    }

    public void RootFromSelection()
    {
        var t = Selection.activeTransform;
        if (t == null) return;

        // Prefer the object holding the Animator — that is the rig root, the space the mirror plane
        // sits in, AND the object clip paths are relative to. One field, three jobs.
        var animator = t.GetComponentInParent<Animator>();
        root = animator != null ? animator.transform : t;
    }

    public List<Transform> SelectedBones()
    {
        var result = new List<Transform>();
        foreach (var t in Selection.transforms)
            if (boneSet.Contains(t)) result.Add(t);
        return result;
    }

    // ────────────────────────────── left / right naming ──────────────────────────────

    public enum Side { None, Left, Right }

    static readonly char[] Seps = { '.', '_', '-' };

    // Recognises a leading marker (L.hip.bone, R_hand), a trailing one (hand.L, foot_r) and the
    // words Left/Right anywhere in the name (LeftArm, arm_right), preserving the original casing.
    public static bool TryPartner(string n, out Side side, out string partner)
    {
        side = Side.None; partner = null;
        if (string.IsNullOrEmpty(n) || n.Length < 2) return false;

        if (System.Array.IndexOf(Seps, n[1]) >= 0)
        {
            if (n[0] == 'L' || n[0] == 'l') { side = Side.Left;  partner = (n[0] == 'L' ? 'R' : 'r') + n.Substring(1); return true; }
            if (n[0] == 'R' || n[0] == 'r') { side = Side.Right; partner = (n[0] == 'R' ? 'L' : 'l') + n.Substring(1); return true; }
        }

        int last = n.Length - 1;
        if (System.Array.IndexOf(Seps, n[last - 1]) >= 0)
        {
            if (n[last] == 'L' || n[last] == 'l') { side = Side.Left;  partner = n.Substring(0, last) + (n[last] == 'L' ? 'R' : 'r'); return true; }
            if (n[last] == 'R' || n[last] == 'r') { side = Side.Right; partner = n.Substring(0, last) + (n[last] == 'R' ? 'L' : 'l'); return true; }
        }

        int i = n.IndexOf("left", System.StringComparison.OrdinalIgnoreCase);
        if (i >= 0) { side = Side.Left;  partner = n.Substring(0, i) + MatchCase("right", n.Substring(i, 4)) + n.Substring(i + 4); return true; }

        i = n.IndexOf("right", System.StringComparison.OrdinalIgnoreCase);
        if (i >= 0) { side = Side.Right; partner = n.Substring(0, i) + MatchCase("left", n.Substring(i, 5)) + n.Substring(i + 5); return true; }

        return false;
    }

    static string MatchCase(string word, string sample)
    {
        if (sample == sample.ToUpperInvariant()) return word.ToUpperInvariant();
        if (char.IsUpper(sample[0]))             return char.ToUpperInvariant(word[0]) + word.Substring(1);
        return word;
    }

    // ────────────────────────────── clip access ──────────────────────────────

    // A clip that came in with a model file is locked. Say so rather than letting a tab write into
    // it and appear to do nothing.
    public bool ClipEditable => clip != null && (clip.hideFlags & HideFlags.NotEditable) == 0;

    public string PathOf(Transform bone) =>
        bone == null || root == null ? null : AnimationUtility.CalculateTransformPath(bone, root);

    public List<EditorCurveBinding> BindingsFor(Transform bone)
    {
        var result = new List<EditorCurveBinding>();
        string path = clip == null ? null : PathOf(bone);
        if (path == null) return result;

        foreach (var b in AnimationUtility.GetCurveBindings(clip))
            if (b.path == path) result.Add(b);
        return result;
    }

    // Both clip tabs need the same field and the same locked-clip warning, and they share the clip
    // itself, so it is drawn here rather than twice over.
    public bool DrawClipField()
    {
        clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", clip, typeof(AnimationClip), false);

        if (clip == null)
        {
            EditorGUILayout.HelpBox("Pick the clip to edit.", MessageType.Info);
            return false;
        }

        if (!ClipEditable)
        {
            EditorGUILayout.HelpBox($"{clip.name} is imported from a model file and cannot be edited. " +
                                    "Duplicate it into the project (Ctrl+D) and use the copy.",
                                    MessageType.Warning);
            return false;
        }

        return true;
    }

    public static int CountKeys(AnimationClip clip)
    {
        int n = 0;
        foreach (var b in AnimationUtility.GetCurveBindings(clip))
        {
            var c = AnimationUtility.GetEditorCurve(clip, b);
            if (c != null) n = Mathf.Max(n, c.length);
        }
        return n;
    }
}

// One tab of the suite. Tools are plain serializable classes rather than windows of their own, so
// the hub can hold them side by side and hand them all the same rig.
public abstract class AnimateTool
{
    public abstract string Title { get; }
    public abstract void   OnGUI(RigContext rig);

    public virtual void OnSceneGUI(RigContext rig, SceneView sv) { }
    public virtual void OnUpdate(RigContext rig)                 { }
}
