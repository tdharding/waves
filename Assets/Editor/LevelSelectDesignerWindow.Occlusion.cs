using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// The Occlusion toggle every placed piece of geometry carries — spikes, structures, outposts,
/// rim nodes, walkways, bridges, archways, pool towers, pipes and walls (rivers have their own
/// rule: the one the boat is on never fades).
///
/// Ticked, the piece gets an <see cref="OcclusionFade"/> and, where it has no collider of its
/// own, a camera-only collider under each of its meshes — so <see cref="CameraOccluderFader"/>
/// fades it when it blocks the view of the boat, even while the boat sails the river it stands
/// on. Walls and pipes collide already; for them the toggle only says whether they fade.
///
/// Rim nodes, walkways, bridges and archways are built as one mesh per run, which fades as a
/// whole — so the ticked ones are built as meshes of their own beside it instead
/// (<see cref="RefreshOcclusionPieces"/>), each fading on its own.
///
/// The toggle is a dropdown — Off, Whole Object, or Screen Circle, which fades only a soft circle
/// round the boat on screen (for big walls and set pieces). It is kept as the piece's
/// <c>occlusion</c> bool plus its <c>occlusionStyle</c>.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private static readonly GUIContent OcclusionLabel = new GUIContent("Occlusion",
        "Fades it when it stands between the camera and the boat — even on the river the boat " +
        "is sailing. Whole Object fades all of it; Screen Circle fades only a soft circle round " +
        "the boat on screen, sized on the CameraOccluderFader.");

    private static readonly GUIContent[] OcclusionChoices =
    {
        new GUIContent("Off"), new GUIContent("Whole Object"), new GUIContent("Screen Circle"),
    };

    /// <summary>The Occlusion dropdown in a piece's panel. True when it was just changed.</summary>
    private bool OcclusionToggle(ref bool occlusion, ref OcclusionStyle style)
    {
        bool          on = occlusion;
        OcclusionStyle s = style;
        OcclusionField(ref on, ref s);
        if (on == occlusion && s == style) return false;

        Undo.RecordObject(_data, "Change Occlusion");
        occlusion = on;
        style     = s;
        MarkDirty();
        return true;
    }

    /// <summary>
    /// The Occlusion dropdown — Off, Whole Object or Screen Circle — read from and written back
    /// into a piece's two fields. Off keeps the style, so turning it back on picks up where it was.
    /// </summary>
    private static void OcclusionField(ref bool occlusion, ref OcclusionStyle style)
    {
        int was = !occlusion ? 0 : style == OcclusionStyle.ScreenCircle ? 2 : 1;
        int now = EditorGUILayout.Popup(OcclusionLabel, was, OcclusionChoices);
        if (now == was) return;

        occlusion = now != 0;
        if (now != 0) style = now == 2 ? OcclusionStyle.ScreenCircle : OcclusionStyle.WholeObject;
    }

    /// <summary>
    /// Writes a piece's Occlusion setting onto its object. Ticked, every mesh under it without a
    /// collider of its own is given one only the camera's ray finds — on Ignore Raycast, so
    /// default raycasts pass it by, and colliding with no layer, so nothing bumps into it.
    /// <paramref name="addColliders"/> is false for a piece whose colliders are already
    /// children of their own (a pipe's rods).
    /// </summary>
    private static void ApplyOcclusion(GameObject go, bool on, OcclusionStyle style,
                                       bool addColliders = true)
    {
        if (go == null) return;

        // Camera-only colliders from the last build — laid again below when it is ticked.
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            if (t != null && t != go.transform && t.name == OcclusionFade.ColliderChild)
                Undo.DestroyObjectImmediate(t.gameObject);

        var setting = go.GetComponent<OcclusionFade>();
        if (setting == null) setting = Undo.AddComponent<OcclusionFade>(go);
        setting.fades = on;
        setting.style = style;
        EditorUtility.SetDirty(setting);

        if (!on || !addColliders) return;

        int layer = LayerMask.NameToLayer("Ignore Raycast");
        foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null) continue;

            // Wall chunks collide as they are drawn — the camera finds them already.
            if (filter.GetComponent<Collider>() != null) continue;

            var child = new GameObject(OcclusionFade.ColliderChild);
            Undo.RegisterCreatedObjectUndo(child, "Occlusion Collider");
            child.transform.SetParent(filter.transform, false);
            child.layer = layer >= 0 ? layer : 2;

            var col = child.AddComponent<MeshCollider>();
            col.sharedMesh    = filter.sharedMesh;
            col.convex        = false;
            col.isTrigger     = false;
            col.excludeLayers = ~0;
            EditorUtility.SetDirty(col);
        }
    }

    /// <summary>
    /// The pieces of a run's shared mesh that are ticked for Occlusion, each built as its own
    /// object under the run so it fades on its own. Every child starting with
    /// <paramref name="prefix"/> that is not in <paramref name="pieces"/> — unticked since, or
    /// gone — is taken away with its mesh asset.
    /// </summary>
    private void RefreshOcclusionPieces(RiverRunMesh record, string prefix,
                                        List<(string name, Mesh built, OcclusionStyle style)> pieces)
    {
        var keep = new HashSet<string>();

        foreach (var (name, built, style) in pieces)
        {
            if (built == null || built.vertexCount == 0)
            {
                if (built != null) Object.DestroyImmediate(built);
                continue;
            }

            var mesh = SaveGeneratedMesh(name, built);
            if (mesh == null) continue;
            keep.Add(name);

            GameObject go;
            var existing = record.transform.Find(name);
            if (existing != null)
            {
                go = existing.gameObject;
                var filter = go.GetComponent<MeshFilter>();
                if (filter == null) filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                EditorUtility.SetDirty(filter);

                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _data.riverMaterial;
                EditorUtility.SetDirty(renderer);
            }
            else
            {
                go = NewMeshChild(record.gameObject, name, mesh);
            }

            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = Vector3.one;

            ApplyOcclusion(go, true, style);
        }

        for (int i = record.transform.childCount - 1; i >= 0; i--)
        {
            var child = record.transform.GetChild(i);
            if (!child.name.StartsWith(prefix) || keep.Contains(child.name)) continue;

            string asset = $"{GeneratedDir}/{child.name}.asset";
            Undo.DestroyObjectImmediate(child.gameObject);
            if (AssetDatabase.LoadAssetAtPath<Mesh>(asset) != null) AssetDatabase.DeleteAsset(asset);
        }
    }

    /// <summary>The name a ticked piece of a run's shared mesh is built under.</summary>
    private static string OcclusionPieceName(string shared, string piece) =>
        $"{shared}{OcclusionPiecePrefix}{piece}";

    private const string OcclusionPiecePrefix = "_Occ_";
}

#endif
