using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Moves a camera sequence between the scene and a <see cref="CameraSequencePreset"/>.
///
/// Save (<see cref="Capture"/>): every setting, each shot's camera and orbit point as a link ID, and
/// a full copy of each linked object — every component's serialized fields, with references kept by
/// stable ID.
///
/// Load (<see cref="Deploy"/> then <see cref="CameraSequencePreset.ApplyTo"/>): each linked object is
/// put back as it was saved; one that has gone missing is built again from the copy.
/// </summary>
public static class CameraSequencePresetIO
{
    public const string Folder = "Assets/ScriptsData/DataScripts/CameraSequencePresets";

    // Never copied: the transform is stored on its own (its JSON holds the parent), the link is the ID itself.
    private static readonly HashSet<Type> Skipped = new HashSet<Type> { typeof(Transform), typeof(CameraSequenceLink) };

    // ── Save ────────────────────────────────────────────────────────────
    /// <summary>
    /// Writes <paramref name="sequence"/> into <paramref name="into"/>. With <paramref name="assignLinks"/>
    /// off nothing in the scene is touched (an object with no link yet gets an empty ID) — that is how
    /// the storyboard compares the scene with the preset.
    /// </summary>
    public static void Capture(CameraSequence sequence, CameraSequencePreset into, bool assignLinks)
    {
        into.shots = new List<CameraSequencePreset.PresetShot>();
        into.storedObjects = new List<CameraSequencePreset.StoredObject>();
        var stored = new HashSet<GameObject>();

        foreach (var shot in sequence.shots)
        {
            var copy = CameraSequence.CopyShot(shot);
            copy.camera     = null;
            copy.orbitPoint = null;
            copy.endCamera  = null;
            copy.viewTarget = null;
            var ps = new CameraSequencePreset.PresetShot
            {
                cameraLink     = LinkId(shot.camera != null ? shot.camera.gameObject : null, assignLinks),
                orbitPointLink = LinkId(shot.orbitPoint != null ? shot.orbitPoint.gameObject : null, assignLinks),
                endCameraLink  = LinkId(shot.endCamera != null ? shot.endCamera.gameObject : null, assignLinks),
                // Linked only, like the characters — never stored, so Load never moves or rebuilds it.
                viewTargetLink = LinkId(shot.viewTarget != null ? shot.viewTarget.gameObject : null, assignLinks),
                shot           = copy,
            };
            // Characters are linked only — never stored, so Load never moves or rebuilds them.
            foreach (var ca in copy.characterAnimations)
            {
                ps.characterLinks.Add(LinkId(ca.character != null ? ca.character.gameObject : null, assignLinks));
                ca.character = null;
            }
            into.shots.Add(ps);

            if (shot.camera != null) Store(into, shot.camera.gameObject, ps.cameraLink, stored);
            if (shot.orbitPoint != null) Store(into, shot.orbitPoint.gameObject, ps.orbitPointLink, stored);
            if (shot.endCamera != null) Store(into, shot.endCamera.gameObject, ps.endCameraLink, stored);
        }

        into.useAudio      = sequence.useAudio;
        into.sequenceAudio = sequence.sequenceAudio;
        into.audioFadeOut  = sequence.audioFadeOut;
        into.endBlend      = CameraSequence.DeepCopy(sequence.endBlend);
        into.playOnce      = sequence.playOnce;
    }

    private static string LinkId(GameObject go, bool assign)
    {
        if (go == null) return string.Empty;
        var link = go.GetComponent<CameraSequenceLink>();
        if (link == null)
        {
            if (!assign) return string.Empty;
            link = Undo.AddComponent<CameraSequenceLink>(go);
        }
        if (!assign) return link.id ?? string.Empty;

        // A duplicated camera carries its original's ID — the copy gets one of its own.
        var holder = CameraSequenceLink.Find(link.id);
        if (string.IsNullOrEmpty(link.id) || (holder != null && holder != link))
        {
            Undo.RecordObject(link, "Link Camera Sequence Object");
            link.id = Guid.NewGuid().ToString("N");
            PrefabUtility.RecordPrefabInstancePropertyModifications(link);
        }
        return link.id;
    }

    private static void Store(CameraSequencePreset into, GameObject go, string linkId, HashSet<GameObject> stored)
    {
        // A camera in more than one shot is stored once.
        if (!stored.Add(go)) return;

        Transform t = go.transform;
        var so = new CameraSequencePreset.StoredObject
        {
            linkId     = linkId,
            name       = go.name,
            parentPath = t.parent != null ? HierarchyPath(t.parent) : string.Empty,
            tag        = go.tag,
            layer      = go.layer,
            active     = go.activeSelf,
            position   = t.position,
            rotation   = t.rotation,
            localScale = t.localScale,
        };

        foreach (var c in go.GetComponents<Component>())
        {
            if (c == null || Skipped.Contains(c.GetType())) continue;
            so.components.Add(new CameraSequencePreset.StoredComponent
            {
                type       = c.GetType().AssemblyQualifiedName,
                json       = EditorJsonUtility.ToJson(c),
                references = References(c),
            });
        }
        into.storedObjects.Add(so);
    }

    // Every object reference the component holds, by an ID that outlives the editor session.
    private static List<CameraSequencePreset.StoredReference> References(Component c)
    {
        var list = new List<CameraSequencePreset.StoredReference>();
        var prop = new SerializedObject(c).GetIterator();
        while (prop.Next(true))
        {
            if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
            if (prop.propertyPath == "m_Script" || prop.propertyPath == "m_GameObject") continue;
            var target = prop.objectReferenceValue;
            if (target == null) continue;

            var targetGo = target as GameObject ?? (target as Component)?.gameObject;
            var link = targetGo != null ? targetGo.GetComponent<CameraSequenceLink>() : null;
            list.Add(new CameraSequencePreset.StoredReference
            {
                propertyPath = prop.propertyPath,
                globalId     = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString(),
                linkId       = link != null ? link.id : string.Empty,
            });
        }
        return list;
    }

    public static string HierarchyPath(Transform t)
    {
        var parts = new List<string>();
        for (; t != null; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    // ── Load ────────────────────────────────────────────────────────────
    /// <summary>
    /// Puts every object the preset links to back as it was saved. One that is missing from the scene
    /// is built again — under its old parent if that is still there, else under <paramref name="fallbackParent"/>
    /// (asked for only when needed, so nothing is made when nothing is missing).
    /// Returns how many were built again.
    /// </summary>
    public static int Deploy(CameraSequencePreset preset, Func<Transform> fallbackParent)
    {
        int rebuilt = 0;
        foreach (var stored in preset.storedObjects)
        {
            if (string.IsNullOrEmpty(stored.linkId)) continue;

            var link = CameraSequenceLink.Find(stored.linkId);
            GameObject go;
            if (link == null)
            {
                go = new GameObject(stored.name);
                Undo.RegisterCreatedObjectUndo(go, "Rebuild Camera Sequence Object");
                var parent = FindPath(stored.parentPath) ?? fallbackParent?.Invoke();
                if (parent != null) Undo.SetTransformParent(go.transform, parent, "Rebuild Camera Sequence Object");
                Undo.AddComponent<CameraSequenceLink>(go).id = stored.linkId;
                rebuilt++;
                Debug.Log($"[CameraSequencePresetIO] '{preset.name}': '{stored.name}' was missing — built again " +
                          $"under '{(parent != null ? parent.name : "the scene root")}'.");
            }
            else
            {
                go = link.gameObject;
            }

            Undo.RecordObject(go, "Load Camera Sequence Preset");
            Undo.RecordObject(go.transform, "Load Camera Sequence Preset");
            go.name  = stored.name;
            go.layer = stored.layer;
            try { go.tag = stored.tag; } catch (UnityException) { /* the tag no longer exists — keep the current one */ }
            go.SetActive(stored.active);
            go.transform.SetPositionAndRotation(stored.position, stored.rotation);
            go.transform.localScale = stored.localScale;

            RestoreComponents(go, stored);
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
        }
        return rebuilt;
    }

    private static void RestoreComponents(GameObject go, CameraSequencePreset.StoredObject stored)
    {
        // Two components of one type: the second stored goes on the second on the object.
        var seen = new Dictionary<Type, int>();
        foreach (var sc in stored.components)
        {
            var type = Type.GetType(sc.type);
            if (type == null)
            {
                Debug.LogWarning($"[CameraSequencePresetIO] '{stored.name}': component type '{sc.type}' no longer exists — skipped.");
                continue;
            }
            seen.TryGetValue(type, out int nth);
            seen[type] = nth + 1;

            var existing = go.GetComponents(type);
            var c = nth < existing.Length ? existing[nth] : Undo.AddComponent(go, type);
            if (c == null) continue;

            Undo.RecordObject(c, "Load Camera Sequence Preset");
            EditorJsonUtility.FromJsonOverwrite(sc.json, c);
            RestoreReferences(c, sc.references);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        }
    }

    // The JSON's references only hold for the session it was written in — set each one again by its stable ID.
    private static void RestoreReferences(Component c, List<CameraSequencePreset.StoredReference> refs)
    {
        if (refs.Count == 0) return;
        var so = new SerializedObject(c);
        foreach (var r in refs)
        {
            var prop = so.FindProperty(r.propertyPath);
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference) continue;

            UnityEngine.Object target = null;
            if (GlobalObjectId.TryParse(r.globalId, out var gid))
                target = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
            // A linked object built again has a new global ID — find it by its link instead.
            if (target == null && !string.IsNullOrEmpty(r.linkId))
            {
                var link = CameraSequenceLink.Find(r.linkId);
                if (link != null) target = MatchType(link.gameObject, prop);
            }
            prop.objectReferenceValue = target;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // The linked object itself, or its component of the type the field takes.
    private static UnityEngine.Object MatchType(GameObject go, SerializedProperty prop)
    {
        string typeName = prop.type.StartsWith("PPtr<$") ? prop.type.Substring(6, prop.type.Length - 7) : null;
        if (typeName == null || typeName == "GameObject") return go;
        if (typeName == "Transform") return go.transform;
        return go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == typeName) ?? (UnityEngine.Object)go;
    }

    public static Transform FindPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var found = GameObject.Find("/" + path);
        if (found != null) return found.transform;
        // GameObject.Find skips switched-off objects — walk the roots for those.
        string[] parts = path.Split('/');
        for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != parts[0]) continue;
                Transform t = root.transform;
                for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
                if (t != null) return t;
            }
        }
        return null;
    }

    // ── Comparing ───────────────────────────────────────────────────────
    /// <summary>True when the scene's sequence (settings or linked cameras) no longer matches the preset.</summary>
    public static bool Differs(CameraSequence sequence, CameraSequencePreset preset)
    {
        if (sequence == null || preset == null) return false;
        var probe = ScriptableObject.CreateInstance<CameraSequencePreset>();
        try
        {
            probe.name = preset.name;
            Capture(sequence, probe, false);
            return Normalise(EditorJsonUtility.ToJson(probe)) != Normalise(EditorJsonUtility.ToJson(preset));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probe);
        }
    }

    // A scene reference in the JSON is an instance ID, new every editor session — the references
    // list carries the stable IDs, so the comparison leaves these out (escaped or not).
    private static readonly System.Text.RegularExpressions.Regex InstanceIds =
        new System.Text.RegularExpressions.Regex(@"(\\?""instanceID\\?"":\s*)-?\d+");

    private static string Normalise(string json) => InstanceIds.Replace(json, "${1}0");

    /// <summary>The camera sequence presets in <see cref="Folder"/>.</summary>
    public static CameraSequencePreset DrawPicker(GUIContent label, CameraSequencePreset current)
        => LevelSelectRiverPresetLibrary.DrawPicker(label, current, Folder);

    public static string EnsureFolder() => LevelSelectRiverPresetLibrary.EnsureFolder(Folder);
}
