using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps a running history of recently selected/opened prefabs, materials and
/// scene GameObjects so you can jump back to them without hunting the project.
/// History is recorded even when the window is closed and persists across sessions.
/// The panel is split in two: recent history on the left, a highlighted pinned
/// column on the right, ordered by how often each entry gets visited.
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
        public int    visits;    // how many times this entry has been selected

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

    // Two-column layout
    const float DividerWidth = 6f;
    const float ColumnMin    = 150f;
    const float RowHeight    = 20f;
    const float HeaderHeight = 18f;

    [SerializeField] float pinnedWidth = 220f;
    Vector2 recentScroll;
    Vector2 pinnedScroll;
    bool    draggingDivider;

    static GUIStyle labelStyle, missingStyle, countStyle;

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

        e.ticks    = DateTime.Now.Ticks;
        e.visits   = 1;
        e.cached   = obj;
        e.resolved = true;

        int existing = history.FindIndex(x => x.kind == e.kind && x.id == e.id);
        if (existing >= 0)
        {
            e.pinned = history[existing].pinned;
            e.visits = history[existing].visits + 1;
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

            int visits = 1;
            if (f.Length > 6 && int.TryParse(f[6], out var v)) visits = Mathf.Max(1, v);

            history.Add(new Entry
            {
                kind     = kind,
                id       = f[1],
                name     = f[2],
                typeName = f[3],
                pinned   = f[4] == "1",
                ticks    = long.TryParse(f[5], out var t) ? t : 0,
                visits   = visits
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
              .Append(e.ticks).Append(FieldSep)
              .Append(e.visits).Append(RecordSep);
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
        EnsureStyles();
        DrawToolbar();

        var filtered = history.Where(Passes).ToList();

        var pinned = filtered.Where(x => x.pinned)
                             .OrderByDescending(x => x.visits)
                             .ThenByDescending(x => x.ticks)
                             .ToList();

        var recent = filtered.Where(x => !x.pinned)
                             .OrderByDescending(x => x.ticks)
                             .ToList();

        var body = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (body.height < RowHeight) return;

        float minCol = Mathf.Min(ColumnMin, (body.width - DividerWidth) * 0.5f);
        pinnedWidth  = Mathf.Clamp(pinnedWidth, minCol, Mathf.Max(minCol, body.width - DividerWidth - minCol));

        var leftRect  = new Rect(body.x, body.y, body.width - pinnedWidth - DividerWidth, body.height);
        var divRect   = new Rect(leftRect.xMax, body.y, DividerWidth, body.height);
        var rightRect = new Rect(divRect.xMax, body.y, pinnedWidth, body.height);

        DrawColumn(leftRect, "Recent", recent, ref recentScroll, false,
            "Nothing here yet. Select prefabs, materials or scene objects and they'll be listed.");

        DrawDivider(divRect);

        DrawColumn(rightRect, "Pinned", pinned, ref pinnedScroll, true,
            "Right-click anything on the left and choose Add to Pinned.");
    }

    static void EnsureStyles()
    {
        if (labelStyle != null) return;

        labelStyle   = new GUIStyle(EditorStyles.label);
        missingStyle = new GUIStyle(EditorStyles.label);
        missingStyle.normal.textColor = Color.gray;
        countStyle   = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
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

    // -------------------------------------------------------------------------
    // Columns
    // -------------------------------------------------------------------------

    void DrawColumn(Rect area, string title, List<Entry> entries, ref Vector2 scrollPos,
                    bool highlight, string emptyHint)
    {
        if (highlight)
        {
            EditorGUI.DrawRect(area, new Color(0.35f, 0.55f, 0.85f, 0.07f));
            EditorGUI.DrawRect(new Rect(area.x, area.y, 2f, area.height), new Color(0.35f, 0.6f, 0.95f, 0.55f));
        }

        var headerRect = new Rect(area.x + 6, area.y + 2, area.width - 12, HeaderHeight);
        GUI.Label(headerRect, entries.Count > 0 ? title + "  (" + entries.Count + ")" : title,
                  EditorStyles.miniBoldLabel);

        var listRect = new Rect(area.x + 4, area.y + HeaderHeight + 4,
                                area.width - 8, area.height - HeaderHeight - 8);
        if (listRect.height < RowHeight) return;

        if (entries.Count == 0)
        {
            GUI.Label(new Rect(listRect.x, listRect.y, listRect.width, listRect.height),
                      emptyHint, EditorStyles.wordWrappedMiniLabel);
            return;
        }

        float contentHeight = entries.Count * RowHeight;
        bool  needsBar      = contentHeight > listRect.height;
        var   content       = new Rect(0, 0, listRect.width - (needsBar ? 16f : 0f), contentHeight);

        scrollPos = GUI.BeginScrollView(listRect, scrollPos, content);
        for (int i = 0; i < entries.Count; i++)
            DrawRow(new Rect(0, i * RowHeight, content.width, RowHeight), entries[i]);
        GUI.EndScrollView();
    }

    void DrawDivider(Rect rect)
    {
        EditorGUI.DrawRect(new Rect(rect.center.x - 0.5f, rect.y, 1f, rect.height), new Color(0f, 0f, 0f, 0.35f));
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

        var ev = Event.current;
        switch (ev.type)
        {
            case EventType.MouseDown:
                if (ev.button == 0 && rect.Contains(ev.mousePosition))
                {
                    draggingDivider = true;
                    ev.Use();
                }
                break;

            case EventType.MouseDrag:
                if (draggingDivider)
                {
                    pinnedWidth -= ev.delta.x;
                    ev.Use();
                    Repaint();
                }
                break;

            case EventType.MouseUp:
                if (draggingDivider)
                {
                    draggingDivider = false;
                    ev.Use();
                }
                break;
        }
    }

    void DrawRow(Rect rect, Entry e)
    {
        var  obj     = Resolve(e);
        bool missing = obj == null;
        var  ev      = Event.current;

        if (ev.type == EventType.Repaint && rect.Contains(ev.mousePosition))
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.05f));

        float x = rect.x;
        var pinRect  = new Rect(x, rect.y + 2, 16, 16); x += 18;
        var iconRect = new Rect(x, rect.y + 2, 16, 16); x += 20;

        bool  showCount = e.visits > 1;
        bool  showType  = rect.width > 260f;
        float tail      = (showCount ? 34f : 0f) + (showType ? 70f : 0f);

        var labelRect = new Rect(x, rect.y, Mathf.Max(20f, rect.xMax - x - tail - 4f), rect.height);

        // Pin toggle
        bool newPinned = GUI.Toggle(pinRect, e.pinned,
            EditorGUIUtility.IconContent(e.pinned ? "d_Favorite On Icon" : "d_Favorite"), GUIStyle.none);
        if (newPinned != e.pinned)
        {
            e.pinned = newPinned;
            Save();
            RepaintAll();
        }

        if (!missing)
            GUI.DrawTexture(iconRect, AssetPreview.GetMiniThumbnail(obj), ScaleMode.ScaleToFit);

        GUI.Label(labelRect, new GUIContent(missing ? e.name + "  (missing)" : e.name),
                  missing ? missingStyle : labelStyle);

        float t = rect.xMax - tail;
        if (showCount)
        {
            GUI.Label(new Rect(t, rect.y, 34, rect.height),
                      new GUIContent("×" + e.visits, "Visited " + e.visits + " times"), countStyle);
            t += 34f;
        }
        if (showType)
            GUI.Label(new Rect(t, rect.y, 70, rect.height), e.typeName, EditorStyles.miniLabel);

        // Interaction (the pin toggle owns the left-most strip)
        if (ev.type != EventType.MouseDown ||
            !rect.Contains(ev.mousePosition) ||
            ev.mousePosition.x <= pinRect.xMax)
            return;

        if (ev.button == 0)
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
        else if (ev.button == 1)
        {
            var menu = new GenericMenu();
            if (!missing)
            {
                menu.AddItem(new GUIContent("Ping"), false, () => EditorGUIUtility.PingObject(obj));
                menu.AddItem(new GUIContent("Open"), false, () => AssetDatabase.OpenAsset(obj));
                menu.AddSeparator("");
            }
            menu.AddItem(new GUIContent(e.pinned ? "Remove from Pinned" : "Add to Pinned"), false,
                         () => { e.pinned = !e.pinned; Save(); RepaintAll(); });
            menu.AddItem(new GUIContent("Remove"), false,
                         () => { history.Remove(e); Save(); RepaintAll(); });
            menu.ShowAsContext();
            ev.Use();
        }
    }
}
