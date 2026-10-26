using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Keeps Camera Sequence shot cameras at their View Target's height in edit mode, so the scene, the storyboard's
/// thumbnails and the presets' saved copies show what the shot will play.
///
/// Per camera, the FIRST shot in the scene's sequence with a View Target holds it (the same rule Orbit start uses):
/// the target moves up or down → the camera moves the same amount, and that shot's Orbit Point / End Camera with it.
/// The camera moved up or down by hand → every shot on it with a View Target learns its new View Target Height.
/// Height only — Pin Cameras and Camera Clamps still move it across the map, and leave its height alone.
///
/// In play mode the sequence does this itself as each shot starts (<see cref="CameraSequence.ViewTargetLift"/>).
/// </summary>
[InitializeOnLoad]
public static class CameraSequenceViewTargets
{
    private const float  Eps          = 1e-4f;
    private const double TickSeconds  = 0.1;
    private const double ScanSeconds  = 1.0;

    // Per camera: its height and its holding target's height when last seen, to tell which of them moved.
    private struct Seen
    {
        public Transform target;
        public float     cameraY, targetY;
    }
    private static readonly Dictionary<CinemachineCamera, Seen> s_Seen = new Dictionary<CinemachineCamera, Seen>();
    private static double s_NextTick;

    // Link IDs of every camera, Orbit Point and End Camera whose height a View Target holds — in the scene's
    // sequences and in every preset. Re-read at most once a second.
    private static readonly HashSet<string> s_Held = new HashSet<string>();
    private static double s_NextScan;

    static CameraSequenceViewTargets()
    {
        EditorApplication.update               += Tick;
        EditorApplication.playModeStateChanged += _ => Forget();
        EditorSceneManager.sceneOpened         += (_, _) => Forget();
        // An undo puts cameras and heights back together — take them as they are, nothing to learn.
        Undo.undoRedoPerformed                 += Reseen;
    }

    /// <summary>Forget what was last seen, so every held camera is put at its target's height next tick (after a preset Load).</summary>
    public static void Forget() => s_Seen.Clear();

    private static void Reseen()
    {
        var keys = new List<CinemachineCamera>(s_Seen.Keys);
        foreach (var cam in keys)
        {
            var seen = s_Seen[cam];
            if (cam == null || seen.target == null) { s_Seen.Remove(cam); continue; }
            seen.cameraY = cam.transform.position.y;
            seen.targetY = seen.target.position.y;
            s_Seen[cam]  = seen;
        }
    }

    private static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.timeSinceStartup < s_NextTick) return;
        s_NextTick = EditorApplication.timeSinceStartup + TickSeconds;

        foreach (var sequence in Object.FindObjectsByType<CameraSequence>(FindObjectsInactive.Include))
            Sync(sequence);
    }

    private static void Sync(CameraSequence sequence)
    {
        KeepTargets(sequence);

        // Each camera's holding shot, and every shot on it with a View Target.
        var holding = new Dictionary<CinemachineCamera, CameraSequence.Shot>();
        var onCamera = new Dictionary<CinemachineCamera, List<CameraSequence.Shot>>();
        foreach (var shot in sequence.shots)
        {
            if (shot == null || shot.camera == null || shot.viewTarget == null) continue;
            if (!holding.ContainsKey(shot.camera)) holding[shot.camera] = shot;
            if (!onCamera.TryGetValue(shot.camera, out var list)) onCamera[shot.camera] = list = new List<CameraSequence.Shot>();
            list.Add(shot);
        }

        foreach (var pair in holding)
        {
            var cam  = pair.Key;
            var shot = pair.Value;
            // A camera mid-preview isn't where it is placed.
            if (CameraSequenceWindow.IsPreviewing(cam)) continue;
            // A target under the camera would chase it up forever.
            if (shot.viewTarget.IsChildOf(cam.transform)) continue;

            Transform t  = cam.transform;
            float cy = t.position.y;
            float ty = shot.viewTarget.position.y;

            if (s_Seen.TryGetValue(cam, out var seen) && seen.target == shot.viewTarget)
            {
                float camMoved    = cy - seen.cameraY;
                bool  targetMoved = Mathf.Abs(ty - seen.targetY) > Eps;
                if (Mathf.Abs(camMoved) > Eps && !targetMoved)
                {
                    // Moved by hand: the shots on it keep the camera where it now is.
                    Undo.RecordObject(sequence, "Move Camera");
                    foreach (var s in onCamera[cam]) s.viewTargetHeight += camMoved;
                    MarkChanged(sequence);
                    Debug.Log($"[CameraSequenceViewTargets] '{cam.name}' moved {camMoved:+0.00;-0.00} — now {shot.viewTargetHeight:F2} " +
                              $"above '{shot.viewTarget.name}'.");
                }
            }

            float lift = ty + shot.viewTargetHeight - cy;
            if (Mathf.Abs(lift) > Eps) Lift(shot, lift);

            s_Seen[cam] = new Seen { target = shot.viewTarget, cameraY = t.position.y, targetY = ty };
        }
    }

    // ── Keeping a rebuilt target ────────────────────────────────────────
    // Paths already warned about as not found, so a lost target is reported once, not every tick.
    private static readonly HashSet<string> s_Lost = new HashSet<string>();

    /// <summary>
    /// GENERATE takes the Level Select's pieces away and builds them again as new objects, which leaves a shot
    /// pointing at one with nothing. Each shot remembers where its target sits; when the target is gone, the
    /// object now at that place (else the one object of that name) becomes its target again — with the link ID
    /// its presets know it by.
    /// </summary>
    private static void KeepTargets(CameraSequence sequence)
    {
        for (int i = 0; i < sequence.shots.Count; i++)
        {
            var shot = sequence.shots[i];
            if (shot == null) continue;

            if (shot.viewTarget != null)
            {
                string path = CameraSequencePresetIO.HierarchyPath(shot.viewTarget);
                if (shot.viewTargetPath != path)
                {
                    shot.viewTargetPath = path;
                    MarkChanged(sequence);
                }
                continue;
            }
            if (string.IsNullOrEmpty(shot.viewTargetPath)) continue;

            var found = FindRebuilt(shot.viewTargetPath);
            if (found == null || (shot.camera != null && found.IsChildOf(shot.camera.transform)))
            {
                if (s_Lost.Add(shot.viewTargetPath))
                    Debug.LogWarning($"[CameraSequenceViewTargets] Shot {i + 1}: View Target '{shot.viewTargetPath}' is gone and " +
                                     "nothing stands in its place — set it again, or bring it back.");
                continue;
            }

            s_Lost.Remove(shot.viewTargetPath);
            Undo.RecordObject(sequence, "Find View Target");
            shot.viewTarget = found;
            MarkChanged(sequence);
            Debug.Log($"[CameraSequenceViewTargets] Shot {i + 1}: View Target was rebuilt — found it again at '{shot.viewTargetPath}'.");
            Relink(shot, found);
        }
    }

    // The object at the path; else, when the path itself changed, the one object of that name in the open scenes.
    private static Transform FindRebuilt(string path)
    {
        var at = CameraSequencePresetIO.FindPath(path);
        if (at != null) return at;

        string name = path.Substring(path.LastIndexOf('/') + 1);
        Transform only = null;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t.name != name || (t.hideFlags & HideFlags.HideInHierarchy) != 0) continue;
            if (only != null) return null;   // more than one — no telling which
            only = t;
        }
        return only;
    }

    // The presets know the target by the link ID that went with the old object — the rebuilt one takes it on.
    // A preset shot is this one when it saved the same target path, or (saved before paths were kept) the same camera.
    private static void Relink(CameraSequence.Shot shot, Transform found)
    {
        if (found.TryGetComponent(out CameraSequenceLink _)) return;
        string cameraLink = shot.camera != null && shot.camera.TryGetComponent(out CameraSequenceLink camLink) ? camLink.id : null;
        foreach (var preset in Presets())
            foreach (var ps in preset.shots)
            {
                if (ps == null || string.IsNullOrEmpty(ps.viewTargetLink)) continue;
                bool same = ps.shot.viewTargetPath == shot.viewTargetPath ||
                            (string.IsNullOrEmpty(ps.shot.viewTargetPath) && !string.IsNullOrEmpty(cameraLink) && ps.cameraLink == cameraLink);
                if (!same || CameraSequenceLink.Find(ps.viewTargetLink) != null) continue;

                var link = Undo.AddComponent<CameraSequenceLink>(found.gameObject);
                link.id = ps.viewTargetLink;
                EditorUtility.SetDirty(link);
                Debug.Log($"[CameraSequenceViewTargets] '{found.name}' takes back link {link.id} for preset '{preset.name}'.");
                return;
            }
    }

    // The holding shot's camera, and the marks its movement uses, up by lift — and their saved copies in every preset.
    private static void Lift(CameraSequence.Shot shot, float lift)
    {
        var cam = shot.camera;
        var moving = new List<Transform> { cam.transform };
        if (shot.animated && shot.animation == CameraSequence.ShotAnimation.Orbit) AddMark(moving, shot, shot.orbitPoint);
        if (shot.animated && shot.animation == CameraSequence.ShotAnimation.Animate2Shot && shot.endCamera != null)
            AddMark(moving, shot, shot.endCamera.transform);

        var links = new HashSet<string>();
        foreach (var m in moving)
        {
            Undo.RecordObject(m, "Follow View Target");
            m.position += Vector3.up * lift;
            PrefabUtility.RecordPrefabInstancePropertyModifications(m);
            if (m.TryGetComponent(out CameraSequenceLink link) && !string.IsNullOrEmpty(link.id)) links.Add(link.id);
        }

        // The presets' copies, so Load and the storyboard's unsaved-changes check agree with the scene.
        foreach (var preset in Presets())
        {
            bool touched = false;
            foreach (var stored in preset.storedObjects)
            {
                if (!links.Contains(stored.linkId)) continue;
                if (!touched) { Undo.RecordObject(preset, "Follow View Target"); touched = true; }
                stored.position += Vector3.up * lift;
            }
            if (touched) EditorUtility.SetDirty(preset);
        }

        EditorApplication.QueuePlayerLoopUpdate();
        Debug.Log($"[CameraSequenceViewTargets] '{cam.name}' moved {lift:+0.00;-0.00} to stay {shot.viewTargetHeight:F2} above " +
                  $"'{shot.viewTarget.name}'" + (moving.Count > 1 ? $", with {moving.Count - 1} mark(s) of its movement." : "."));
    }

    // A mark that is the target, sits under it or under the camera already moves — don't move it twice.
    private static void AddMark(List<Transform> moving, CameraSequence.Shot shot, Transform mark)
    {
        if (mark == null || CameraSequence.RidesWithViewTarget(shot, mark) || mark.IsChildOf(shot.camera.transform)) return;
        moving.Add(mark);
    }

    private static void MarkChanged(CameraSequence sequence)
    {
        EditorUtility.SetDirty(sequence);
        PrefabUtility.RecordPrefabInstancePropertyModifications(sequence);
        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(sequence.gameObject.scene);
    }

    // ── Setting a target ────────────────────────────────────────────────
    /// <summary>
    /// Gives shot <paramref name="index"/> of <paramref name="sequence"/> its View Target, its View Target Height
    /// taken from where the camera stands now (so nothing jumps). Null clears it. Undoable.
    /// </summary>
    public static void SetTarget(CameraSequence sequence, int index, Transform target)
    {
        if (sequence == null || index < 0 || index >= sequence.shots.Count) return;
        var shot = sequence.shots[index];
        if (target != null && shot.camera != null && target.IsChildOf(shot.camera.transform))
        {
            Debug.LogWarning($"[CameraSequenceViewTargets] '{target.name}' sits under the shot's camera — it can't be its View Target.");
            return;
        }

        Undo.RecordObject(sequence, "Set View Target");
        shot.viewTarget     = target;
        shot.viewTargetPath = target != null ? CameraSequencePresetIO.HierarchyPath(target) : null;
        if (target != null && shot.camera != null)
            shot.viewTargetHeight = shot.camera.transform.position.y - target.position.y;
        MarkChanged(sequence);
        Debug.Log(target != null
            ? $"[CameraSequenceViewTargets] Shot {index + 1}: View Target '{target.name}', camera {shot.viewTargetHeight:F2} above it."
            : $"[CameraSequenceViewTargets] Shot {index + 1}: View Target cleared.");
    }

    /// <summary>For the storyboard's View Target field: a newly set target learns its height from where the camera stands.</summary>
    public static void LearnHeight(SerializedProperty shot)
    {
        var target = shot.FindPropertyRelative("viewTarget").objectReferenceValue as Transform;
        var cam    = shot.FindPropertyRelative("camera").objectReferenceValue as CinemachineCamera;
        // Cleared by hand: nothing to find again after a GENERATE.
        var path   = shot.FindPropertyRelative("viewTargetPath");
        path.stringValue = target != null ? CameraSequencePresetIO.HierarchyPath(target) : string.Empty;
        if (target == null || cam == null) return;
        if (target.IsChildOf(cam.transform))
        {
            Debug.LogWarning($"[CameraSequenceViewTargets] '{target.name}' sits under the shot's camera — it can't be its View Target.");
            shot.FindPropertyRelative("viewTarget").objectReferenceValue = null;
            path.stringValue = string.Empty;
            return;
        }
        shot.FindPropertyRelative("viewTargetHeight").floatValue = cam.transform.position.y - target.position.y;
    }

    // ── Picking what is drawn ───────────────────────────────────────────
    /// <summary>
    /// The nearest object the ray hits as it is drawn (mesh, posed skinned mesh, else colliders), on the layers
    /// the view draws. A Sequence Character's own object stands in for any part of it. Null when nothing is hit.
    /// </summary>
    public static Transform PickDrawn(Ray ray, int cullingMask)
    {
        GameObject best = null;
        float bestDistance = Mathf.Infinity;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (!r.enabled || (r.hideFlags & HideFlags.HideInHierarchy) != 0) continue;
            if ((cullingMask & (1 << r.gameObject.layer)) == 0) continue;
            if (r.TryGetComponent(out FogSheetMesh _)) continue;
            if (!r.bounds.IntersectRay(ray, out float near) || near >= bestDistance) continue;
            if (!SceneViewNavigation.TryHitObject(ray, r.gameObject, out Vector3 point)) continue;

            float distance = Vector3.Dot(point - ray.origin, ray.direction);
            if (distance < 0f || distance >= bestDistance) continue;
            best = r.gameObject;
            bestDistance = distance;
        }
        if (best == null) return null;
        var character = best.GetComponentInParent<SequenceCharacter>();
        return character != null ? character.transform : best.transform;
    }

    // ── Held heights, for Pin Cameras and Camera Clamps ─────────────────
    /// <summary>
    /// True when a View Target holds this linked object's height — a shot camera with a View Target, or the
    /// Orbit Point / End Camera its movement uses — in the scene's sequences or any preset. Pins and clamps
    /// then move it across the map only.
    /// </summary>
    public static bool HeightHeld(string linkId)
    {
        if (string.IsNullOrEmpty(linkId)) return false;
        if (EditorApplication.timeSinceStartup >= s_NextScan) ScanHeld();
        return s_Held.Contains(linkId);
    }

    private static void ScanHeld()
    {
        s_NextScan = EditorApplication.timeSinceStartup + ScanSeconds;
        s_Held.Clear();

        foreach (var sequence in Object.FindObjectsByType<CameraSequence>(FindObjectsInactive.Include))
            foreach (var shot in sequence.shots)
            {
                if (shot == null || shot.viewTarget == null) continue;
                AddHeld(shot.camera != null ? shot.camera.gameObject : null);
                if (shot.animated && shot.animation == CameraSequence.ShotAnimation.Orbit && shot.orbitPoint != null)
                    AddHeld(shot.orbitPoint.gameObject);
                if (shot.animated && shot.animation == CameraSequence.ShotAnimation.Animate2Shot && shot.endCamera != null)
                    AddHeld(shot.endCamera.gameObject);
            }

        foreach (var preset in Presets())
            foreach (var ps in preset.shots)
            {
                if (ps == null || string.IsNullOrEmpty(ps.viewTargetLink)) continue;
                AddHeldId(ps.cameraLink);
                if (ps.shot.animated && ps.shot.animation == CameraSequence.ShotAnimation.Orbit) AddHeldId(ps.orbitPointLink);
                if (ps.shot.animated && ps.shot.animation == CameraSequence.ShotAnimation.Animate2Shot) AddHeldId(ps.endCameraLink);
            }
    }

    private static void AddHeld(GameObject go)
    {
        if (go != null && go.TryGetComponent(out CameraSequenceLink link)) AddHeldId(link.id);
    }

    private static void AddHeldId(string id)
    {
        if (!string.IsNullOrEmpty(id)) s_Held.Add(id);
    }

    private static IEnumerable<CameraSequencePreset> Presets()
    {
        if (!AssetDatabase.IsValidFolder(CameraSequencePresetIO.Folder)) yield break;
        foreach (var guid in AssetDatabase.FindAssets("t:CameraSequencePreset", new[] { CameraSequencePresetIO.Folder }))
        {
            var preset = AssetDatabase.LoadAssetAtPath<CameraSequencePreset>(AssetDatabase.GUIDToAssetPath(guid));
            if (preset != null) yield return preset;
        }
    }
}
