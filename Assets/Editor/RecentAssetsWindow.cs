using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps a running history of recently selected/opened prefabs, materials and
/// scene GameObjects so you can jump back to them without hunting the project.
/// History is recorded even when the window is closed and persists across sessions.
/// </summary>
[InitializeOnLoad]
public class RecentAssetsWindow : EditorWindow
{
    // -------------------------------------------------------------------------
    // Data
    // -------------------------------------------------------------------------

    enum Kind { Asset, SceneObject }

    [Serializable]
    class Entry
    {
        public Kind   kind;
        public string id;        // GUID for assets, GlobalObjectId string for scene objects
        public string name;
        public string typeName;  // "Prefab", "Material", "GameObject", ...
        public bool   pinned;
        public long   ticks;

        [NonSerialized] public UnityEngine.Object cached;
        [NonSerialized] public bool resolved;
    }

    const string PrefsKey    = "Waves.RecentAssets.History";
    const string FieldSep    = "␟";  // unit separator, won't appear in names
    const string RecordSep   = "␞";  // record separator
    const int    MaxEntries  = 60;

    static List<Entry> history = new List<Entry>();
    static bool        loaded;

    // Filters
    bool showPrefabs   = true;
    bool showMaterials = true;
    bool showScene     = true;
    bool showOther     = true;
    string search      = "";
    Vector2 scroll;

    // -------------------------------------------------------------------------
    // Recording (runs regardless of whether the window is open)
    // -------------------------------------------------------------------------

    static RecentAssetsWindow()
    {
        Selection.selectionChanged += OnSelectionChanged;
    }

    static void OnSelectionChanged()
    {
        Load();

        foreach (var obj in Selection.objects)
        {
            if (obj == null) continue;
            Record(obj);
        }

        Save();
        RepaintAll();
    }

    static void Record(UnityEngine.Object obj)
    {
        string path = AssetDatabase.GetAssetPath(obj);
        Entry e;

        if (!string.IsNullOrEmpty(path))
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return;

            e = new Entry
            {
                kind     = Kind.Asset,
                id       = guid,
                name     = obj.name,
                typeName = DescribeAsset(obj, path)
            };
        }
        else
        {
            var go = obj as GameObject;
            if (go == null) return;                       // ignore raw components / transient objects

            var gid = GlobalObjectId.GetGlobalObjectIdSlow(go);
            if (gid.identifierType == 0) return;           // not addressable (e.g. preview scene object)

            e = new Entry
            {
                kind     = Kind.SceneObject,
                id       = gid.ToString(),
                name     = go.name,
                typeName = "GameObject"
            };
        }

        e.ticks  = DateTime.Now.Ticks;
        e.cached = obj;
        e.resolved = true;

        int existing = history.FindIndex(x => x.kind == e.kind && x.id == e.id);
        if (existing >= 0)
        {
            e.pinned = history[existing].pinned;
            history.RemoveAt(existing);
        }

        history.Insert(0, e);

        // Trim unpinned overflow from the tail.
        while (history.Count > MaxEntries)
        {
            int idx = history.FindLastIndex(x => !x.pinned);
            if (idx < 0) break;
            history.RemoveAt(idx);
        }
    }

    static string DescribeAsset(UnityEngine.Object obj, string path)
    {
        if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return "Prefab";
        if (obj is Material)  return "Material";
        if (obj is Shader)    return "Shader";
        if (obj is Texture)   return "Texture";
        if (obj is SceneAsset) return "Scene";
        return obj.GetType().Name;
    }

    // -------------------------------------------------------------------------
    // Persistence
    // -------------------------------------------------------------------------

    static void Load()
    {
        if (loaded) return;
        loaded = true;

        history.Clear();
        string raw = EditorPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(raw)) return;

        foreach (var rec in raw.Split(new[] { RecordSep }, StringSplitOptions.RemoveEmptyEntries))
        {
            var f = rec.Split(new[] { FieldSep }, StringSplitOptions.None);
            if (f.Length < 6) continue;

            Kind kind;
            if (!Enum.TryParse(f[0], out kind)) continue;

            history.Add(new Entry
            {
                kind     = kind,
                id       = f[1],
                name     = f[2],
                typeName = f[3],
                pinned   = f[4] == "1",
                ticks    = long.TryParse(f[5], out var t) ? t : 0
            });
        }
    }

    static void Save()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var e in history)
        {
            sb.Append(e.kind).Append(FieldSep)
              .Append(e.id).Append(FieldSep)
              .Append(e.name).Append(FieldSep)
              .Append(e.typeName).Append(FieldSep)
              .Append(e.pinned ? "1" : "0").Append(FieldSep)
              .Append(e.ticks).Append(RecordSep);
        }
        EditorPrefs.SetString(PrefsKey, sb.ToString());
    }

    static void RepaintAll()
    {
        foreach (var w in Resources.FindObjectsOfTypeAll<RecentAssetsWindow>())
            w.Repaint();
    }

    // -------------------------------------------------------------------------
    // Resolving
    // -------------------------------------------------------------------------

    static UnityEngine.Object Resolve(Entry e)
    {
        if (e.resolved && e.cached != null) return e.cached;

        if (e.kind == Kind.Asset)
        {
            string path = AssetDatabase.GUIDToAssetPath(e.id);
            e.cached = string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        }
        else
        {
            if (GlobalObjectId.TryParse(e.id, out var gid))
                e.cached = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
            else
                e.cached = null;
        }

        e.resolved = true;
        return e.cached;
    }

    // -------------------------------------------------------------------------
    // Window
    // -------------------------------------------------------------------------

    [MenuItem("Tools/Waves/Recent Assets")]
    public static void Open() => GetWindow<RecentAssetsWindow>("Recent");

    void OnEnable()
    {
        titleContent = new GUIContent("Recent", EditorGUIUtility.IconContent("d_UnityEditor.HistoryWindow").image);
        Load();
        // Drop resolved caches so stale references get looked up again.
        foreach (var e in history) e.resolved = false;
    }

    void OnGUI()
    {
        DrawToolbar();

        var filtered = history.Where(Passes).ToList();

        if (filtered.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "Nothing here yet. Select prefabs, materials or scene objects and they'll be listed.",
                MessageType.Info);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);

        // Pinned first, then most recent.
        foreach (var e in filtered.OrderByDescending(x => x.pinned).ThenByDescending(x => x.ticks).ToList())
            DrawRow(e);

        EditorGUILayout.EndScrollView();
    }

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        showPrefabs   = GUILayout.Toggle(showPrefabs,   "Prefabs",   EditorStyles.toolbarButton, GUILayout.Width(60));
        showMaterials = GUILayout.Toggle(showMaterials, "Materials", EditorStyles.toolbarButton, GUILayout.Width(65));
        showScene     = GUILayout.Toggle(showScene,     "Scene",     EditorStyles.toolbarButton, GUILayout.Width(50));
        showOther     = GUILayout.Toggle(showOther,     "Other",     EditorStyles.toolbarButton, GUILayout.Width(50));

        GUILayout.Space(4);
        search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(60));

        if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(45)))
        {
            history.RemoveAll(x => !x.pinned);
            Save();
        }

        EditorGUILayout.EndHorizontal();
    }

    bool Passes(Entry e)
    {
        if (!string.IsNullOrEmpty(search) &&
            e.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        switch (e.typeName)
        {
            case "Prefab":     return showPrefabs;
            case "Material":   return showMaterials;
            case "GameObject": return showScene;
            default:           return showOther;
        }
    }

    void DrawRow(Entry e)
    {
        var obj     = Resolve(e);
        bool missing = obj == null;

        var rect = EditorGUILayout.GetControlRect(false, 20f);

        if (Event.current.type == EventType.Repaint && rect.Contains(Event.current.mousePosition))
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.05f));

        // Pin toggle
        var pinRect = new Rect(rect.x, rect.y + 2, 16, 16);
        bool newPinned = GUI.Toggle(pinRect, e.pinned,
            EditorGUIUtility.IconContent(e.pinned ? "d_Favorite On Icon" : "d_Favorite"), GUIStyle.none);
        if (newPinned != e.pinned)
        {
            e.pinned = newPinned;
            Save();
        }

        // Icon + label
        var iconRect  = new Rect(rect.x + 18, rect.y + 2, 16, 16);
        var labelRect = new Rect(rect.x + 38, rect.y, rect.width - 38 - 70, rect.height);
        var typeRect  = new Rect(rect.xMax - 70, rect.y, 70, rect.height);

        if (!missing)
            GUI.DrawTexture(iconRect, AssetPreview.GetMiniThumbnail(obj), ScaleMode.ScaleToFit);

        var style = new GUIStyle(EditorStyles.label);
        if (missing) style.normal.textColor = Color.gray;

        GUI.Label(labelRect, new GUIContent(missing ? e.name + "  (missing)" : e.name), style);
        GUI.Label(typeRect, e.typeName, EditorStyles.miniLabel);

        // Interaction
        var ev = Event.current;
        if (ev.type == EventType.MouseDown && rect.Contains(ev.mousePosition) && ev.button == 0 && ev.mousePosition.x > rect.x + 16)
        {
            if (!missing)
            {
                if (ev.clickCount >= 2)
                    AssetDatabase.OpenAsset(obj);
                else
                {
                    Selection.activeObject = obj;
                    EditorGUIUtility.PingObject(obj);
                }
            }
            ev.Use();
        }

        if (ev.type == EventType.MouseDown && rect.Contains(ev.mousePosition) && ev.button == 1)
        {
            var menu = new GenericMenu();
            if (!missing)
            {
                menu.AddItem(new GUIContent("Ping"),  false, () => EditorGUIUtility.PingObject(obj));
                menu.AddItem(new GUIContent("Open"),  false, () => AssetDatabase.OpenAsset(obj));
            }
            menu.AddItem(new GUIContent(e.pinned ? "Unpin" : "Pin"), false, () => { e.pinned = !e.pinned; Save(); });
            menu.AddItem(new GUIContent("Remove"), false, () => { history.Remove(e); Save(); });
            menu.ShowAsContext();
            ev.Use();
        }
    }
}
