using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Looper tab — sends a clip back to its starting pose, and stretches the timeline.
//
// Return to Start adds a key after the last one that copies the first, so a clip keyed A → B plays
// A → B → A. The trip back takes as long as the trip there. Pressed again once that return key
// exists, it refreshes the key instead of adding another — so after changing the first pose, press
// it again to carry the change to the end.
//
// Like the Easing tab this edits the CLIP ASSET, not the scene.
[System.Serializable]
public class LooperTool : AnimateTool
{
    // None keeps the slope the clip leaves its first key with, so the join back to the start is
    // seamless. The rest shape the move back instead, the same presets as the Easing tab.
    public enum ReturnEase { None, Linear, EaseIn, EaseOut, EaseInOut }

    [SerializeField] int        targetFrames = 24;
    [SerializeField] ReturnEase returnEase   = ReturnEase.None;
    [SerializeField] float      easeIntensity = 0.5f;

    public override string Title => "Looper";

    // Every key frame in the clip, listed in order, with the return key shown where it is or where
    // it will go — so what the button does can be read off before pressing it.
    static void DrawKeyList(AnimationClip clip, float fps, bool returning, float at, bool usable)
    {
        var times = KeyTimes(clip);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (!usable)
            {
                EditorGUILayout.LabelField(times.Count == 0 ? "The clip has no keys."
                                                            : "Needs at least two keys at different frames.",
                                           EditorStyles.wordWrappedMiniLabel);
                return;
            }

            int shown = returning ? times.Count - 1 : times.Count;
            for (int i = 0; i < shown; i++)
                EditorGUILayout.LabelField($"Key {i + 1}", $"frame {times[i] * fps:0}" +
                                           (i == 0 ? "   (start pose)" : ""));

            var prev = GUI.color;
            GUI.color = returning ? prev : new Color(1f, 0.85f, 0.4f, 1f);
            EditorGUILayout.LabelField("Return", $"frame {at * fps:0}   " +
                                       (returning ? "(already there — will be updated)" : "(will be added)"));
            GUI.color = prev;
        }
    }

    static List<float> KeyTimes(AnimationClip clip)
    {
        var times = new List<float>();
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null) continue;

            foreach (var k in curve.keys)
            {
                bool held = false;
                foreach (float t in times)
                    if (Mathf.Abs(t - k.time) < 1e-4f) { held = true; break; }
                if (!held) times.Add(k.time);
            }
        }
        times.Sort();
        return times;
    }

    public override void OnGUI(RigContext rig)
    {
        EditorGUILayout.HelpBox("Sends the clip back to its starting pose. Edits the clip asset, not the scene.",
                                MessageType.None);

        if (!rig.DrawClipField()) return;

        int frames = FrameCount(rig.clip);
        EditorGUILayout.LabelField($"{frames} frames at {rig.clip.frameRate:0} fps · " +
                                   $"{RigContext.CountKeys(rig.clip)} keys",
                                   EditorStyles.miniLabel);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Return to Start", EditorStyles.boldLabel);

        KeySpan(rig.clip, out float start, out float end);
        float fps       = Mathf.Max(1f, rig.clip.frameRate);
        bool  returning = EndsOnStart(rig.clip);
        float at        = returning ? end : end + (end - start);

        DrawKeyList(rig.clip, fps, returning, at, end > start);

        using (new EditorGUI.DisabledScope(end <= start))
        {
            returnEase = (ReturnEase)EditorGUILayout.EnumPopup(
                new GUIContent("Return Easing", "How the move back to the start pose speeds up and slows down."),
                returnEase);

            using (new EditorGUI.DisabledScope(returnEase == ReturnEase.None || returnEase == ReturnEase.Linear))
                easeIntensity = EditorGUILayout.Slider(
                    new GUIContent("Intensity", "0 is no easing, 0.5 the standard ease, 1 the strongest."),
                    easeIntensity, 0f, 1f);

            if (GUILayout.Button(returning ? "Update Return to Start" : "Add Return to Start"))
            {
                int n = ReturnToStart(rig.clip, returnEase, easeIntensity);
                Debug.Log($"[Animate Suite] {(returning ? "Updated" : "Added")} the return to start " +
                          $"on {n} curves in {rig.clip.name}.");
            }
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

    // ────────────────────────────────── return to start ──────────────────────────────────

    // The earliest and latest key times across the whole clip. Bones keyed on fewer frames than
    // others still return on the same frame as everything else.
    static void KeySpan(AnimationClip clip, out float start, out float end)
    {
        start = float.MaxValue; end = float.MinValue;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length == 0) continue;
            start = Mathf.Min(start, curve[0].time);
            end   = Mathf.Max(end,   curve[curve.length - 1].time);
        }
        if (start > end) start = end = 0f;
    }

    // Whether the clip's final frame already holds the first pose, on every curve keyed there. A bone
    // that never moved matches trivially, so a clip only reads as returning once the moved ones do.
    static bool EndsOnStart(AnimationClip clip)
    {
        KeySpan(clip, out float start, out float end);
        if (end <= start) return false;

        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length < 2) continue;

            Keyframe last = curve[curve.length - 1];
            if (!Mathf.Approximately(last.time, end)) return false;
            if (Mathf.Abs(last.value - curve[0].value) > 1e-4f) return false;
        }
        return true;
    }

    // Adds a key after the last one holding the first key's pose, the same gap on from the last key
    // as the last key is from the first. If the clip already ends on its first pose, that end key is
    // refreshed in place rather than a second one added.
    //
    // The slope travels too, and specifically the FIRST key's OUT tangent becomes the return key's IN
    // tangent. That is the continuity a loop needs: playback arrives at the end on its in-tangent,
    // then restarts leaving the first key on its out-tangent. Match values only and the pose is right
    // but the motion visibly kinks at the join.
    //
    // With an ease picked, the move back is then reshaped by it, which replaces that slope on the
    // way in to the return key and on the way out of the key before it. Updating re-applies whatever
    // ease and intensity are set now, so the move back can be retuned and pressed again.
    public static int ReturnToStart(AnimationClip clip, ReturnEase ease, float intensity)
    {
        KeySpan(clip, out float start, out float end);
        if (end <= start) return 0;

        bool  update = EndsOnStart(clip);
        float at     = update ? end : end + (end - start);

        int changed = 0;
        Undo.RecordObject(clip, "Return to Start");

        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length == 0) continue;

            Keyframe first = curve[0];
            int      index;

            if (update && curve.length >= 2)
                index = curve.length - 1;
            else
            {
                index = curve.AddKey(new Keyframe(at, first.value));
                if (index < 0) continue;   // a key already sits at that time

                // The old last key may be auto-smoothed; re-applying its own mode recalculates its
                // slope now that it has a key after it instead of being the end of the curve.
                if (index > 0)
                    AnimationUtility.SetKeyRightTangentMode(curve, index - 1,
                        AnimationUtility.GetKeyRightTangentMode(curve, index - 1));
            }

            AnimationUtility.SetKeyLeftTangentMode (curve, index, AnimationUtility.TangentMode.Free);
            AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Free);
            Keyframe k = curve[index];   // re-read: setting the mode rewrites the keyframe

            k.value        = first.value;
            k.inTangent    = first.outTangent;
            k.inWeight     = first.outWeight;
            k.outTangent   = first.outTangent;
            k.outWeight    = first.outWeight;
            k.weightedMode = first.weightedMode;

            curve.MoveKey(index, k);

            if (ease != ReturnEase.None && index > 0)
            {
                EasePreset(ease, intensity, out Vector2 easeIn, out Vector2 easeOut);
                EasingTool.EaseSegment(curve, index - 1, easeIn, easeOut);
            }
            else if (update && index > 0)
            {
                // Back to None after an ease: an earlier press left the key before the return eased
                // on its way out. Hand that side back to Unity's default smoothing, unweighted.
                Keyframe prev = curve[index - 1];
                prev.weightedMode &= ~WeightedMode.Out;
                curve.MoveKey(index - 1, prev);
                AnimationUtility.SetKeyRightTangentMode(curve, index - 1, AnimationUtility.TangentMode.ClampedAuto);
            }

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

    // Intensity slides the graph handles along a line: linear at 0, the Easing tab's preset at 0.5,
    // and the handles pushed right into the corners at 1 — as sharp as that ease can get while still
    // starting and ending on the right poses.
    static void EasePreset(ReturnEase ease, float intensity, out Vector2 easeIn, out Vector2 easeOut)
    {
        EasingTool.PresetLinear(out Vector2 linIn, out Vector2 linOut);
        Vector2 midIn, midOut, maxIn, maxOut;

        switch (ease)
        {
            case ReturnEase.EaseIn:
                EasingTool.PresetEaseIn(out midIn, out midOut);
                maxIn = new Vector2(1f, 0f); maxOut = new Vector2(1f, 1f);
                break;
            case ReturnEase.EaseOut:
                EasingTool.PresetEaseOut(out midIn, out midOut);
                maxIn = new Vector2(0f, 0f); maxOut = new Vector2(0f, 1f);
                break;
            case ReturnEase.EaseInOut:
                EasingTool.PresetEaseInOut(out midIn, out midOut);
                maxIn = new Vector2(1f, 0f); maxOut = new Vector2(0f, 1f);
                break;
            default:
                easeIn = linIn; easeOut = linOut;
                return;
        }

        intensity = Mathf.Clamp01(intensity);
        if (intensity <= 0.5f)
        {
            easeIn  = Vector2.Lerp(linIn,  midIn,  intensity * 2f);
            easeOut = Vector2.Lerp(linOut, midOut, intensity * 2f);
        }
        else
        {
            easeIn  = Vector2.Lerp(midIn,  maxIn,  (intensity - 0.5f) * 2f);
            easeOut = Vector2.Lerp(midOut, maxOut, (intensity - 0.5f) * 2f);
        }
    }

    static int FrameCount(AnimationClip clip) =>
        Mathf.RoundToInt(clip.length * Mathf.Max(1f, clip.frameRate));
}
