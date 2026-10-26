using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Level Select designer overlay: with it on, selecting a generated piece writes the name of
/// each of its bits — Base, Upper Ramp, Stem, Orb — on that bit in the Scene view, named as the
/// designer's shape controls name them. Switched from the Gizmo Manager's top bar.
///
/// The names are worked out when the piece is generated and kept inside its mesh asset (see
/// <see cref="PartLabels"/>), so a piece generated before the overlay existed shows nothing until
/// it is generated again. The choice lives in EditorPrefs; nothing is written to the scene.
/// </summary>
[InitializeOnLoad]
public static class LevelSelectDesignerOverlay
{
    const string ShownKey = "FogTools.GizmoManager.LevelSelectDesignerOverlay";

    // Each mesh's labels, read out of its asset once — null kept for a mesh that has none.
    static readonly Dictionary<Mesh, List<PartLabel>> _labels = new Dictionary<Mesh, List<PartLabel>>();

    // The mesh filters under the selection, found once per selection rather than every repaint.
    static readonly List<MeshFilter> _selected = new List<MeshFilter>();
    static bool _selectionRead;

    static GUIStyle _style;

    static LevelSelectDesignerOverlay()
    {
        SceneView.duringSceneGui  += Draw;
        Selection.selectionChanged += () => _selectionRead = false;
    }

    public static bool Shown
    {
        get => EditorPrefs.GetBool(ShownKey, true);
        set
        {
            EditorPrefs.SetBool(ShownKey, value);
            SceneView.RepaintAll();
        }
    }

    /// <summary>Drops what was read for <paramref name="mesh"/>, after it has been generated again.</summary>
    public static void Forget(Mesh mesh)
    {
        if (mesh != null) _labels.Remove(mesh);
        SceneView.RepaintAll();
    }

    static void Draw(SceneView view)
    {
        if (!Shown || Event.current.type != EventType.Repaint) return;

        if (!_selectionRead)
        {
            _selected.Clear();
            foreach (var go in Selection.gameObjects)
                if (go != null) _selected.AddRange(go.GetComponentsInChildren<MeshFilter>());
            _selectionRead = true;
        }
        if (_selected.Count == 0) return;

        var style = Style();
        foreach (var filter in _selected)
        {
            if (filter == null) continue;
            var labels = LabelsOf(filter.sharedMesh);
            if (labels == null) continue;

            Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
            foreach (var label in labels)
                Handles.Label(toWorld.MultiplyPoint3x4(label.position), label.name, style);
        }
    }

    static List<PartLabel> LabelsOf(Mesh mesh)
    {
        if (mesh == null) return null;
        if (_labels.TryGetValue(mesh, out var held)) return held;

        List<PartLabel> found = null;
        string path = AssetDatabase.GetAssetPath(mesh);
        if (!string.IsNullOrEmpty(path))
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is GeneratedPartLabelSet set && set.labels != null && set.labels.Count > 0)
                {
                    found = set.labels;
                    break;
                }

        _labels[mesh] = found;
        return found;
    }

    static GUIStyle Style()
    {
        if (_style != null && _style.normal.background != null) return _style;

        var back = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        back.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.7f));
        back.Apply();

        _style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 11,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(4, 4, 1, 2),
        };
        _style.normal.background = back;
        _style.normal.textColor  = Color.white;
        return _style;
    }
}
