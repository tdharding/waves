using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using System;

public class GizmoManagerWindow : EditorWindow
{
    struct GizmoEntry
    {
        public Type type;
        public bool hasDrawGizmos;
        public bool hasDrawGizmosSelected;
        public int  instanceCount;
        public bool gizmoEnabled;
        public int  classID;
    }

    // Built-in collider classIDs (stable across Unity versions: Box, Sphere, Capsule, Mesh, Terrain, Wheel)
    static readonly int[] ColliderClassIDs = { 65, 135, 136, 64, 39, 146 };

    List<GizmoEntry> entries              = new List<GizmoEntry>();
    Vector2          scroll;
    bool             annotationsAvailable;
    bool             showColliders        = true;

    // AnnotationUtility reflection handles
    MethodInfo mGetAnnotations;
    MethodInfo mSetGizmoEnabled;
    FieldInfo  fClassID;
    FieldInfo  fScriptClass;
    FieldInfo  fGizmoEnabled;

    [MenuItem("Tools/Gizmo Manager")]
    static void Open() => GetWindow<GizmoManagerWindow>("Gizmo Manager");

    void OnEnable()
    {
        InitAnnotations();
        Refresh();
    }

    // -------------------------------------------------------------------------
    // AnnotationUtility reflection setup
    // -------------------------------------------------------------------------

    void InitAnnotations()
    {
        var annType = typeof(Editor).Assembly.GetType("UnityEditor.AnnotationUtility");
        if (annType == null) return;

        mGetAnnotations  = annType.GetMethod("GetAnnotations",  BindingFlags.Static | BindingFlags.NonPublic);
        mSetGizmoEnabled = annType.GetMethod("SetGizmoEnabled", BindingFlags.Static | BindingFlags.NonPublic);

        if (mGetAnnotations == null) return;

        var arr = mGetAnnotations.Invoke(null, null) as Array;
        if (arr == null || arr.Length == 0) return;

        var elemType  = arr.GetValue(0).GetType();
        fClassID      = elemType.GetField("classID");
        fScriptClass  = elemType.GetField("scriptClass");
        fGizmoEnabled = elemType.GetField("gizmoEnabled");

        annotationsAvailable = fClassID != null && fScriptClass != null && fGizmoEnabled != null;
    }

    Dictionary<string, (int classID, bool enabled)> GetAnnotationMap()
    {
        var map = new Dictionary<string, (int, bool)>(StringComparer.Ordinal);
        if (!annotationsAvailable) return map;

        var arr = mGetAnnotations.Invoke(null, null) as Array;
        if (arr == null) return map;

        foreach (var ann in arr)
        {
            string sc = fScriptClass.GetValue(ann) as string;
            if (string.IsNullOrEmpty(sc)) continue;
            map[sc] = ((int)fClassID.GetValue(ann), (int)fGizmoEnabled.GetValue(ann) != 0);
        }

        return map;
    }

    void ApplyToggle(GizmoEntry entry, bool enabled)
    {
        if (!annotationsAvailable || mSetGizmoEnabled == null) return;
        mSetGizmoEnabled.Invoke(null, new object[] { entry.classID, entry.type.Name, enabled ? 1 : 0, false });
    }

    // -------------------------------------------------------------------------
    // Refresh
    // -------------------------------------------------------------------------

    void Refresh()
    {
        entries.Clear();
        var annMap = GetAnnotationMap();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (IsSystemAssembly(assembly)) continue;

            Type[] types;
            try   { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsAbstract) continue;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public
                                         | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

                bool hasGizmos    = type.GetMethod("OnDrawGizmos",         flags) != null;
                bool hasSelected  = type.GetMethod("OnDrawGizmosSelected", flags) != null;
                if (!hasGizmos && !hasSelected) continue;

                bool enabled = true;
                int  classID = 114; // MonoBehaviour persistent type ID

                if (annMap.TryGetValue(type.Name, out var ann))
                {
                    enabled = ann.enabled;
                    classID = ann.classID;
                }

                entries.Add(new GizmoEntry
                {
                    type                  = type,
                    hasDrawGizmos         = hasGizmos,
                    hasDrawGizmosSelected = hasSelected,
                    instanceCount         = FindObjectsOfType(type).Length,
                    gizmoEnabled          = enabled,
                    classID               = classID,
                });
            }
        }

        entries.Sort((a, b) => string.Compare(a.type.Name, b.type.Name, StringComparison.Ordinal));
        showColliders = ReadColliderVisibility();
    }

    static bool IsSystemAssembly(Assembly asm)
    {
        string n = asm.GetName().Name;
        return n.StartsWith("Unity",       StringComparison.Ordinal)
            || n.StartsWith("System",      StringComparison.Ordinal)
            || n.StartsWith("Mono.",       StringComparison.Ordinal)
            || n.StartsWith("mscorlib",    StringComparison.Ordinal)
            || n.StartsWith("netstandard", StringComparison.Ordinal)
            || n.StartsWith("Microsoft.",  StringComparison.Ordinal)
            || n.StartsWith("nunit.",      StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------
    // GUI
    // -------------------------------------------------------------------------

    void OnGUI()
    {
        // Toolbar
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("Gizmo Manager", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("All On",  EditorStyles.toolbarButton, GUILayout.Width(52))) SetAll(true);
        if (GUILayout.Button("All Off", EditorStyles.toolbarButton, GUILayout.Width(52))) SetAll(false);
        GUILayout.Space(6);
        bool newShowColliders = GUILayout.Toggle(showColliders, "Colliders", EditorStyles.toolbarButton, GUILayout.Width(65));
        if (newShowColliders != showColliders)
        {
            showColliders = newShowColliders;
            SetCollidersVisible(showColliders);
        }
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(55))) Refresh();
        EditorGUILayout.EndHorizontal();

        if (!annotationsAvailable)
            EditorGUILayout.HelpBox("AnnotationUtility unavailable — toggles disabled.", MessageType.Warning);

        if (entries.Count == 0)
        {
            EditorGUILayout.HelpBox("No scripts with OnDrawGizmos or OnDrawGizmosSelected found.", MessageType.Info);
            return;
        }

        // Column headers
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("",           GUILayout.Width(20));  // toggle
        GUILayout.Label("Script",     GUILayout.Width(190));
        GUILayout.Label("Gizmos",     GUILayout.Width(52));
        GUILayout.Label("Selected",   GUILayout.Width(62));
        GUILayout.Label("In Scene",   GUILayout.Width(58));
        EditorGUILayout.EndHorizontal();

        scroll = EditorGUILayout.BeginScrollView(scroll);

        for (int i = 0; i < entries.Count; i++)
        {
            var  entry   = entries[i];
            bool inScene = entry.instanceCount > 0;

            EditorGUILayout.BeginHorizontal();

            // Visibility toggle
            EditorGUI.BeginDisabledGroup(!annotationsAvailable);
            bool newEnabled = EditorGUILayout.Toggle(entry.gizmoEnabled, GUILayout.Width(20));
            EditorGUI.EndDisabledGroup();

            if (newEnabled != entry.gizmoEnabled)
            {
                ApplyToggle(entry, newEnabled);
                entry.gizmoEnabled = newEnabled;
                entries[i] = entry;
                SceneView.RepaintAll();
            }

            // Dim row if gizmo is disabled
            if (!entry.gizmoEnabled) GUI.color = new Color(1f, 1f, 1f, 0.45f);

            if (GUILayout.Button(entry.type.Name, EditorStyles.linkLabel, GUILayout.Width(190)))
                PingScript(entry.type);

            GUILayout.Label(entry.hasDrawGizmos        ? "✓" : "–", GUILayout.Width(52));
            GUILayout.Label(entry.hasDrawGizmosSelected ? "✓" : "–", GUILayout.Width(62));

            if (inScene)
                GUI.color = new Color(0.5f, 1f, 0.5f);

            GUILayout.Label(inScene ? entry.instanceCount.ToString() : "–", GUILayout.Width(58));

            GUI.color = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        GUILayout.Space(2);
        EditorGUILayout.LabelField($"{entries.Count} script(s)", EditorStyles.centeredGreyMiniLabel);
    }

    void SetAll(bool enabled)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            ApplyToggle(e, enabled);
            e.gizmoEnabled = enabled;
            entries[i] = e;
        }
        SceneView.RepaintAll();
    }

    bool ReadColliderVisibility()
    {
        if (!annotationsAvailable) return true;
        var arr = mGetAnnotations.Invoke(null, null) as Array;
        if (arr == null) return true;
        foreach (var ann in arr)
        {
            int cid = (int)fClassID.GetValue(ann);
            if (Array.IndexOf(ColliderClassIDs, cid) >= 0)
                if ((int)fGizmoEnabled.GetValue(ann) == 0) return false;
        }
        return true;
    }

    void SetCollidersVisible(bool visible)
    {
        if (mSetGizmoEnabled == null) return;
        foreach (int id in ColliderClassIDs)
            mSetGizmoEnabled.Invoke(null, new object[] { id, "", visible ? 1 : 0, false });
        SceneView.RepaintAll();
    }

    static void PingScript(Type type)
    {
        foreach (var guid in AssetDatabase.FindAssets($"t:MonoScript {type.Name}"))
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (script != null && script.GetClass() == type)
            {
                EditorGUIUtility.PingObject(script);
                Selection.activeObject = script;
                return;
            }
        }
    }
}
