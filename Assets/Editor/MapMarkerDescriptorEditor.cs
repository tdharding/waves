using UnityEngine;
using UnityEditor;

// Adds a live preview image of the selected procedural MapIcon to the MapMarkerDescriptor
// inspector, rasterised from the shared MapIconLibrary params (see MapIconPreview). Only shown
// when the descriptor uses a procedural icon (no prefab override).
[CustomEditor(typeof(MapMarkerDescriptor))]
public class MapMarkerDescriptorEditor : Editor
{
    const int PreviewSize = 128;

    Texture2D _tex;
    MapIcon   _cachedIcon;
    bool      _cachedValid;

    void OnDisable()
    {
        if (_tex != null) DestroyImmediate(_tex);
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        var desc = (MapMarkerDescriptor)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Map Preview", EditorStyles.boldLabel);

        if (desc.mapMarkerPrefab != null)
        {
            EditorGUILayout.HelpBox("A Map Marker Prefab is set, so it overrides the procedural icon — no preview.", MessageType.Info);
            return;
        }

        if (!_cachedValid || _cachedIcon != desc.icon || _tex == null)
        {
            if (_tex == null) _tex = MapIconPreview.Create(PreviewSize);
            MapIconPreview.Render(_tex, desc.icon, MapIconPreview.ResolveLibrary());
            _cachedIcon  = desc.icon;
            _cachedValid = true;
        }

        Rect r = GUILayoutUtility.GetRect(PreviewSize, PreviewSize, GUILayout.ExpandWidth(false));
        r.x += (EditorGUIUtility.currentViewWidth - PreviewSize) * 0.5f - 8f;
        if (_tex != null) GUI.DrawTexture(r, _tex, ScaleMode.ScaleToFit);

        EditorGUILayout.LabelField($"Icon: {desc.icon}  (street light shown lit)", EditorStyles.miniLabel);
        if (GUILayout.Button("Open Map Icon Library…")) MapIconLibraryWindow.Open();
    }
}
