using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

// Authoring window for the procedural map icons. Lists every MapIcon with a live preview and its
// shape parameters (from a MapIconLibrary asset). Edits save straight into the asset; the map picks
// them up on the next level load. Open via WaveGrid ▸ Map Icon Library, or the button on a descriptor.
public class MapIconLibraryWindow : EditorWindow
{
    const int PreviewSize = 96;

    MapIconLibrary   _lib;
    SerializedObject _so;
    Vector2          _scroll;
    readonly Dictionary<MapIcon, Texture2D> _previews = new Dictionary<MapIcon, Texture2D>();

    static readonly (MapIcon icon, string prop)[] Entries =
    {
        (MapIcon.BigSpike,    "spike"),
        (MapIcon.FishBowl,    "fishBowl"),
        (MapIcon.StreetLight, "streetLight"),
    };

    [MenuItem("WaveGrid/Map Icon Library")]
    public static void Open()
    {
        var w = GetWindow<MapIconLibraryWindow>("Map Icons");
        w.minSize = new Vector2(380, 320);
    }

    void OnEnable()
    {
        if (_lib == null) _lib = MapIconPreview.ResolveLibraryAsset();
    }

    void OnDisable()
    {
        foreach (var t in _previews.Values) if (t) DestroyImmediate(t);
        _previews.Clear();
    }

    void OnGUI()
    {
        EditorGUILayout.Space();
        var newLib = (MapIconLibrary)EditorGUILayout.ObjectField("Library Asset", _lib, typeof(MapIconLibrary), false);
        if (newLib != _lib) { _lib = newLib; _so = null; ClearPreviews(); }

        if (_lib == null)
        {
            EditorGUILayout.HelpBox("No MapIconLibrary assigned. Create one to author icon shapes (or leave unset to use built-in defaults).", MessageType.Info);
            if (GUILayout.Button("Create Map Icon Library")) CreateLibraryAsset();
            return;
        }

        if (_so == null || _so.targetObject != _lib) _so = new SerializedObject(_lib);
        _so.Update();

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.PropertyField(_so.FindProperty("fanSegments"));
        EditorGUILayout.Space();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var e in Entries) DrawIcon(e.icon, e.prop);
        EditorGUILayout.EndScrollView();

        if (EditorGUI.EndChangeCheck())
        {
            _so.ApplyModifiedProperties();
            RegenerateAll();
            EditorUtility.SetDirty(_lib);

            // Live update: rebuild the running map's icons so edits show immediately in play mode.
            if (Application.isPlaying && UIMapController.Instance != null)
                UIMapController.Instance.RebuildProceduralMarkers();
        }
        else
        {
            _so.ApplyModifiedProperties();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Edits save into the library asset. In play mode they update the map live (if the running UIMapController uses this same asset); otherwise they apply on the next level load.",
            MessageType.None);
    }

    void DrawIcon(MapIcon icon, string prop)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField(icon.ToString(), EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();

        Rect r = GUILayoutUtility.GetRect(PreviewSize, PreviewSize, GUILayout.Width(PreviewSize), GUILayout.Height(PreviewSize));
        GUI.DrawTexture(r, GetPreview(icon), ScaleMode.ScaleToFit);

        EditorGUILayout.BeginVertical();
        var group = _so.FindProperty(prop);
        if (group != null)
        {
            var end = group.GetEndProperty();
            var it  = group.Copy();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                EditorGUILayout.PropertyField(it, true);
                enter = false;
            }
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    Texture2D GetPreview(MapIcon icon)
    {
        if (!_previews.TryGetValue(icon, out var tex) || tex == null)
        {
            tex = MapIconPreview.Create(PreviewSize);
            MapIconPreview.Render(tex, icon, _lib);
            _previews[icon] = tex;
        }
        return tex;
    }

    void RegenerateAll()
    {
        foreach (var kv in _previews)
            if (kv.Value) MapIconPreview.Render(kv.Value, kv.Key, _lib);
    }

    void ClearPreviews()
    {
        foreach (var t in _previews.Values) if (t) DestroyImmediate(t);
        _previews.Clear();
    }

    void CreateLibraryAsset()
    {
        string path = EditorUtility.SaveFilePanelInProject("Create Map Icon Library", "MapIconLibrary", "asset", "");
        if (string.IsNullOrEmpty(path)) return;

        var lib = ScriptableObject.CreateInstance<MapIconLibrary>();
        AssetDatabase.CreateAsset(lib, path);
        AssetDatabase.SaveAssets();
        _lib = lib;
        _so  = null;
        ClearPreviews();
    }
}
