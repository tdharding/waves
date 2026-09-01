using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Looper tab — ties the last keyframe of a clip to the first, and stretches the timeline.
//
// Keep Linked is the point of it: with it on, whatever you key on the first frame is copied onto
// the last frame as you work, so a cycle stays joined up while you are still changing the pose it
// starts and ends on. It only travels one way, first to last, exactly as a loop needs.
//
// Like the Easing tab this edits the CLIP ASSET, not the scene.
[System.Serializable]
public class LooperTool : AnimateTool
{
    [SerializeField] bool linkLive;
    [SerializeField] int  targetFrames = 24;

    [System.NonSerialized] List<float> firstKeys;
    [System.NonSerialized] double      nextPoll;

    public override string Title => "Looper";

    public override void OnGUI(RigContext rig)
    {
        EditorGUILayout.HelpBox("Ties the end of the clip to its start. Edits the clip asset, not the scene.",
                                MessageType.None);

        if (!rig.DrawClipField()) return;

        int frames = FrameCount(rig.clip);
        EditorGUILayout.LabelField($"{frames} frames at {rig.clip.frameRate:0} fps · " +
                                   $"{RigContext.CountKeys(rig.clip)} keys",
                                   EditorStyles.miniLabel);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Loop Ends", EditorStyles.boldLabel);

        linkLive = EditorGUILayout.Toggle("Keep Linked", linkLive);
        if (linkLive)
            EditorGUILayout.HelpBox("Whatever you key on the first frame is copied to the last as you work.",
                                    MessageType.None);

        if (GUILayout.Button("Link Ends Now"))
        {
            int n = LinkEnds(rig.clip);
            firstKeys = FirstKeySignature(rig.clip);
            Debug.Log($"[Animate Suite] Looped {n} curves in {rig.clip.name}.");
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Timeline Length", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Scales every key time so the whole animation plays over a new " +
                                   "number of frames. Key count and curve shapes are untouched — " +
                                   "only when things happen changes.", EditorStyles.wordWrappedMiniLabel);

        targetFrames = Mathf.Max(1, EditorGUILayout.IntField("Frames", targetFrames));

        using (new EditorGUI.DisabledScope(frames <= 0 || targetFrames == frames))
        {
            if (GUILayout.Button($"Stretch {frames} → {targetFrames} frames"))
            {
                int n = Stretch(rig.clip, targetFrames);
                Debug.Log($"[Animate Suite] Stretched {n} curves in {rig.clip.name} to {targetFrames} frames.");
            }
        }
    }

    // ────────────────────────────────── live link ──────────────────────────────────

    // Polls the clip rather than hooking the Animation window, whose recording state is internal.
    // Ten times a second is plenty for keeping up with hand-keying and keeps the cost off the
    // editor loop on a clip with a lot of curves.
    public override void OnUpdate(RigContext rig)
    {
        if (!linkLive || !rig.ClipEditable) { firstKeys = null; return; }

        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 0.1;

        var now = FirstKeySignature(rig.clip);
        if (firstKeys != null && Same(firstKeys, now)) return;

        // Writing the last keys leaves the first ones alone, so the signature settles and this does
        // not chase itself.
        if (firstKeys != null) LinkEnds(rig.clip);
        firstKeys = now;
    }

    static List<float> FirstKeySignature(AnimationClip clip)
    {
        var values = new List<float>();
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length < 2) continue;

            Keyframe k = curve[0];
            values.Add(k.value);
            values.Add(k.inTangent);
            values.Add(k.outTangent);
        }
        return values;
    }

    static bool Same(List<float> a, List<float> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (Mathf.Abs(a[i] - b[i]) > 1e-6f) return false;
        return true;
    }

    // ────────────────────────────────── operations ──────────────────────────────────

    // Copies the first key onto the last of every curve, keeping the last key where it is in time.
    //
    // The slope travels too, and specifically the FIRST key's OUT tangent becomes the LAST key's IN
    // tangent. That is the continuity the wrap actually needs: playback arrives at the last key on
    // its in-tangent, then restarts leaving the first key on its out-tangent. Match values only and
    // the pose is right but the motion visibly kinks at the join.
    public static int LinkEnds(AnimationClip clip)
    {
        int changed = 0;
        Undo.RecordObject(clip, "Loop Ends");

        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length < 2) continue;

            int      lastIndex = curve.length - 1;
            Keyframe first     = curve[0];
            Keyframe last      = curve[lastIndex];

            AnimationUtility.SetKeyLeftTangentMode(curve, lastIndex, AnimationUtility.TangentMode.Free);
            last = curve[lastIndex];   // re-read: setting the mode rewrites the keyframe

            last.value        = first.value;
            last.inTangent    = first.outTangent;
            last.inWeight     = first.outWeight;
            last.outTangent   = first.outTangent;
            last.outWeight    = first.outWeight;
            last.weightedMode = first.weightedMode;

            curve.MoveKey(lastIndex, last);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            changed++;
        }

        EditorUtility.SetDirty(clip);
        return changed;
    }

    // Scales every key time so the clip spans the given number of frames. The clip length follows
    // its last key, so it retimes itself.
    public static int Stretch(AnimationClip clip, int frames)
    {
        if (frames < 1 || clip.length <= 0f) return 0;

        float newLength = frames / Mathf.Max(1f, clip.frameRate);
        float scale     = newLength / clip.length;
        if (Mathf.Approximately(scale, 1f)) return 0;

        int changed = 0;
        Undo.RecordObject(clip, "Stretch Timeline");

        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length == 0) continue;

            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].time *= scale;

                // Tangents are value per second, so stretching time flattens them by the same
                // factor and the motion keeps its shape. Weights are fractions of their own
                // segment and stay as they are.
                keys[i].inTangent  /= scale;
                keys[i].outTangent /= scale;
            }

            curve.keys = keys;
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            changed++;
        }

        EditorUtility.SetDirty(clip);
        return changed;
    }

    static int FrameCount(AnimationClip clip) =>
        Mathf.RoundToInt(clip.length * Mathf.Max(1f, clip.frameRate));
}
