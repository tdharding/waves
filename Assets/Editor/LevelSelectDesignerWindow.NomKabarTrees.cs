using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// NomKabarTrees — natural features in the landscape: a bell-shaped TreeBase, a TreeStem, a
/// rounded TreeNodeHub and 1–5 TreeArms each ending on a round TreeNodeEnd (see
/// <see cref="NomKabarTree"/>).
///
/// They stand in two places. Freestanding, placed like a spike (Add ▾ > NomKabar Trees): the base
/// on the landscape surface under the tree's centre, re-seated whenever the hills change. Or in the
/// middle of a natural pool's island, as that pool's tower Kind (the Towers partial).
///
/// Being natural rather than structural they wear the landscape's material, shaded by the Level
/// Select Landscape Tuner, with the landscape's rocky noise written into UV1 where they stand —
/// the same as a natural river run. They take nothing from the Run Shading Tuner.
///
/// Every new tree starts as a copy of the "Default NomKabarTree" preset, and raising a tree's Arm
/// Count brings each new arm in from it. Linked to a preset of their own, they change with it.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const string NomKabarTreesParent = "NOMKABARTREES";

    internal const string NomKabarTreePresetFolder = "Assets/ScriptsData/DataScripts/NomKabarTreePresets";
    private  const string DefaultNomKabarTreeName  = "Default NomKabarTree";
    private  static string DefaultNomKabarTreePath => $"{NomKabarTreePresetFolder}/{DefaultNomKabarTreeName}.asset";

    private string  _selectedNomKabarTreeId;
    private bool    _isDraggingNomKabarTree;
    private Vector2 _nomKabarTreeDragOffset;

    private LevelSelectDesignerData.DesignerNomKabarTree SelectedNomKabarTree =>
        _data?.nomKabarTrees?.Find(t => t.treeId == _selectedNomKabarTreeId);

    // ─────────────────────────────────────────────
    // THE DEFAULT
    // ─────────────────────────────────────────────

    /// <summary>
    /// The Default NomKabarTree preset — what every new tree starts as. Made from the code's own
    /// defaults the first time it is asked for with <paramref name="create"/>.
    /// </summary>
    internal static NomKabarTreePreset DefaultNomKabarTreePreset(bool create)
    {
        var preset = AssetDatabase.LoadAssetAtPath<NomKabarTreePreset>(DefaultNomKabarTreePath);
        if (preset != null || !create) return preset;

        LevelSelectRiverPresetLibrary.EnsureFolder(NomKabarTreePresetFolder);
        preset = CreateInstance<NomKabarTreePreset>();
        preset.CopyFrom(NomKabarTree.CodeDefault());
        AssetDatabase.CreateAsset(preset, DefaultNomKabarTreePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[LevelSelectDesigner] Made {DefaultNomKabarTreePath} — every new NomKabarTree starts as it.");
        return preset;
    }

    /// <summary>A fresh copy of the Default NomKabarTree, made if it isn't there yet.</summary>
    internal static NomKabarTree DefaultNomKabarTree()
    {
        var preset = DefaultNomKabarTreePreset(create: true);
        return preset != null ? preset.Build() : NomKabarTree.CodeDefault();
    }

    // ─────────────────────────────────────────────
    // FIELDS — shared by the designer and the preset editor
    // ─────────────────────────────────────────────

    // Which arm dropdowns are open, by the tree they belong to and the arm's number.
    private static readonly HashSet<string> s_openNomKabarArms = new();

    /// <summary>
    /// A tree's TreeBase, TreeStem, TreeNodeHub and TreeArms fields, each arm in a dropdown of its
    /// own. <paramref name="context"/> tells one tree's dropdowns from another's. Hands back an
    /// edited copy, so the caller writes it only inside its own change check. Raising Arm Count
    /// brings each new arm in from the Default NomKabarTree.
    /// </summary>
    internal static NomKabarTree DrawNomKabarTreeFields(NomKabarTree tree, string context)
    {
        var edited = (tree ?? NomKabarTree.CodeDefault()).Clone();
        edited.EnsureArms();
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("TreeBase", EditorStyles.miniBoldLabel);
        edited.baseFootRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Foot Radius", "Radius of the bell's foot on the ground."), edited.baseFootRadius));
        edited.baseHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Height", "Height of the bell, from the ground to where it has narrowed into the stem. " +
                                     "0 = no base, and the stem stands on the ground."), edited.baseHeight));
        edited.baseShoulder = EditorGUILayout.Slider(
            new GUIContent("Shoulder", "How high the foot rises before the bell turns in to the stem — low is a " +
                                       "flat spread foot, high a full rounded shoulder."),
            edited.baseShoulder, 0f, 1f);

        EditorGUILayout.LabelField("TreeStem", EditorStyles.miniBoldLabel);
        edited.stemRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Radius", "Radius of the stem."), edited.stemRadius));
        edited.stemHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Height", "Height of the stem, from the bell's neck up to where it meets the hub."),
            edited.stemHeight));

        EditorGUILayout.LabelField("TreeNodeHub", EditorStyles.miniBoldLabel);
        edited.hubRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Radius", "Radius of the hub the arms reach out of, across."), edited.hubRadius));
        edited.hubSquash = EditorGUILayout.Slider(
            new GUIContent("Squash", "Height of the hub as a share of its width. 1 is round; less is squatter."),
            edited.hubSquash, 0.2f, 1.5f);

        EditorGUILayout.LabelField("Joins", EditorStyles.miniBoldLabel);
        edited.baseJoin = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Base Join", "How far up the stem the TreeBase's bell carries on narrowing before it " +
                                        "is the stem — a longer, softer neck. 0 = the bell meets the stem at " +
                                        "Base Height."), edited.baseJoin));
        edited.stemJoin = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Stem Join", "How far the stem flares out where it meets the TreeNodeHub — out over " +
                                        "the hub and back down the stem. 0 = no flare."), edited.stemJoin));
        edited.hubJoin = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Hub Join", "How far every arm flares out where it leaves the TreeNodeHub. 0 = no flare."),
            edited.hubJoin));
        edited.endJoin = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("End Join", "How far every arm flares out where it meets its TreeNodeEnd. 0 = no flare."),
            edited.endJoin));

        EditorGUILayout.LabelField("TreeArms", EditorStyles.miniBoldLabel);
        int count = EditorGUILayout.IntSlider(
            new GUIContent("Arm Count", "How many arms the tree has. Each arm raised in comes from the Default " +
                                        "NomKabarTree's arm of the same number."),
            Mathf.Clamp(edited.armCount, 1, NomKabarTree.MaxArms), 1, NomKabarTree.MaxArms);
        if (count > edited.armCount)
        {
            var from = DefaultNomKabarTreePreset(create: false)?.Build();
            if (from != null)
                for (int i = Mathf.Max(0, edited.armCount); i < count; i++)
                    edited.arms[i] = from.arms[i].Clone();
        }
        edited.armCount = count;

        for (int i = 0; i < edited.armCount; i++)
        {
            var    arm  = edited.arms[i];
            string key  = $"{context}:{i}";
            bool   open = s_openNomKabarArms.Contains(key);

            // Opening a dropdown is not an edit, so it never counts as a change.
            bool changed = GUI.changed;
            bool now = EditorGUILayout.Foldout(open, $"Arm {i + 1} — {arm.angle:0.#}°", true);
            GUI.changed = changed;
            if (now != open)
            {
                if (now) s_openNomKabarArms.Add(key); else s_openNomKabarArms.Remove(key);
            }
            if (!now) continue;

            EditorGUI.indentLevel++;
            arm.angle = EditorGUILayout.Slider(
                new GUIContent("Angle", "Which way the arm reaches out of the hub, seen from above. 0 = +Z (north), " +
                                        "90 = +X (east)."), arm.angle, 0f, 360f);
            arm.radius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
                new GUIContent("Radius", "Radius of the arm's tube."), arm.radius));
            arm.reach = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Reach", "How far out from the hub's centre the TreeNodeEnd's centre is, level."),
                arm.reach));
            arm.rise = EditorGUILayout.FloatField(
                new GUIContent("Rise", "How far above the hub's centre the TreeNodeEnd's centre is."), arm.rise);
            arm.bend = EditorGUILayout.Slider(
                new GUIContent("Bend", "TreeArmBend — how far the arm turns, in degrees, from leaving the hub to " +
                                       "pointing up at its end. 90 on an upright end leaves the hub level; 0 is a " +
                                       "straight arm."), arm.bend, 0f, 170f);
            arm.bendArea = EditorGUILayout.Slider(
                new GUIContent("Bend Area", "TreeArmBendArea — how much of the arm the bend is spread over. Small " +
                                            "is a tight elbow, 1 is one long sweep from the hub to the end."),
                arm.bendArea, 0.05f, 1f);
            arm.endLean = EditorGUILayout.Slider(
                new GUIContent("End Lean", "TreeArmEndLean — how far the end of the arm leans outward from " +
                                           "upright, in degrees."), arm.endLean, -45f, 45f);
            arm.endScale = Mathf.Max(1f, EditorGUILayout.FloatField(
                new GUIContent("End Scale", "Size of this arm's TreeNodeEnd, as a multiple of the arm's radius."),
                arm.endScale));
            EditorGUI.indentLevel--;
        }

        if (EditorGUI.EndChangeCheck()) s_towerFieldsEdited = true;
        return edited;
    }

    /// <summary>The same preset row as a lollipop's, for a NomKabarTree.</summary>
    private bool DrawNomKabarTreePresetRow(NomKabarTreePreset current, NomKabarTree tree,
                                           System.Action<NomKabarTreePreset> setPreset,
                                           System.Action<NomKabarTree> setTree)
        => DrawTowerPresetRow(current, tree, setPreset, setTree,
                              NomKabarTreePresetFolder, "Tree Preset", "NomKabar Tree",
                              p => p.Build(), (p, t) => p.CopyFrom(t),
                              t => (t ?? NomKabarTree.CodeDefault()).Clone());

    /// <summary>
    /// A placed tree's own settings: edited here when it has no preset. Linked to one, they live in
    /// that preset, the way a lollipop's do — its Edit is where they change, for every tree using it.
    /// </summary>
    private static NomKabarTree DrawPlacedNomKabarTreeFields(NomKabarTree tree, NomKabarTreePreset preset,
                                                             string context)
    {
        if (preset == null) return DrawNomKabarTreeFields(tree, context);

        EditorGUILayout.LabelField($"Its settings come from {preset.name} — Edit changes them, for every tree using it.",
                                   EditorStyles.wordWrappedMiniLabel);
        return tree;
    }

    // ─────────────────────────────────────────────
    // CANVAS INPUT
    // ─────────────────────────────────────────────

    private void HandleNomKabarTreeMode(Event e)
    {
        if (e.type == EventType.KeyDown &&
            (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
        {
            DeleteSelectedNomKabarTree();
            e.Use();
            return;
        }

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.D && SelectedNomKabarTree != null)
        {
            DuplicateSelectedNomKabarTree();
            e.Use();
            return;
        }

        if (HandleNomKabarTreeDrag(e)) return;

        if (e.type != EventType.MouseDown || e.button != 0) return;

        // A click on one already there picks it up rather than stacking another on it.
        if (TryPickNomKabarTree(e)) return;

        AddNomKabarTree(e.mousePosition);
        e.Use();
    }

    /// <summary>Removes the selected tree, from the data and the scene. Shared with Select mode.</summary>
    private void DeleteSelectedNomKabarTree()
    {
        var selected = SelectedNomKabarTree;
        if (selected == null) return;

        Undo.RecordObject(_data, "Delete NomKabar Tree");
        _data.nomKabarTrees.Remove(selected);
        _selectedNomKabarTreeId = null;
        _isDraggingNomKabarTree = false;
        MarkDirty();
        RemoveNomKabarTreeObject(selected.treeId);
        Repaint();
    }

    /// <summary>Copies the selected tree, stood just beside it, and selects the copy. Shared with Select mode.</summary>
    private void DuplicateSelectedNomKabarTree()
    {
        var selected = SelectedNomKabarTree;
        if (selected == null) return;

        Undo.RecordObject(_data, "Duplicate NomKabar Tree");

        var dupe = selected.Clone();
        dupe.treeId      = System.Guid.NewGuid().ToString();
        dupe.positionXZ += new Vector2(NomKabarTreeFootprint(selected) * 2f, 0f);

        // Only one perch should start her on the map — the copy doesn't take that over.
        if (dupe.perch != null) dupe.perch.startPerch = false;

        _data.nomKabarTrees.Insert(_data.nomKabarTrees.IndexOf(selected) + 1, dupe);
        _selectedNomKabarTreeId = dupe.treeId;
        _isDraggingNomKabarTree = false;
        MarkDirty();
        RebuildNomKabarTree(dupe);
        Repaint();
    }

    /// <summary>Selects the tree under the mouse and starts carrying it. Shared with Select mode.</summary>
    private bool TryPickNomKabarTree(Event e)
    {
        if (!FindNomKabarTreeAtCanvas(e.mousePosition, out string hit)) return false;

        _selectedNomKabarTreeId = hit;
        var picked = SelectedNomKabarTree;
        _nomKabarTreeDragOffset = e.mousePosition -
                                  WorldToCanvas(new Vector3(picked.positionXZ.x, 0f, picked.positionXZ.y));
        _isDraggingNomKabarTree = true;
        e.Use();
        Repaint();
        return true;
    }

    /// <summary>Moves the carried tree and lets it go. True when it took the event.</summary>
    private bool HandleNomKabarTreeDrag(Event e)
    {
        if (!_isDraggingNomKabarTree) return false;

        if (e.type == EventType.MouseDrag)
        {
            var dragged = SelectedNomKabarTree;
            if (dragged != null)
            {
                Undo.RecordObject(_data, "Move NomKabar Tree");
                dragged.positionXZ = CanvasToWorld2D(e.mousePosition - _nomKabarTreeDragOffset);
                MarkDirty();
            }
            Repaint();
            e.Use();
            return true;
        }

        if (e.type == EventType.MouseUp)
        {
            // Rebuilt once on release, not on every step: the landscape noise it wears is read
            // where it now stands.
            _isDraggingNomKabarTree = false;
            var moved = SelectedNomKabarTree;
            if (moved != null) RebuildNomKabarTree(moved);
            e.Use();
            return true;
        }

        return false;
    }

    private void AddNomKabarTree(Vector2 canvas)
    {
        Undo.RecordObject(_data, "Add NomKabar Tree");

        // A GUID, not a count: the mesh asset is named off it, and the Generated folder is shared
        // by every world.
        string id = System.Guid.NewGuid().ToString();

        var tree = new LevelSelectDesignerData.DesignerNomKabarTree
        {
            treeId     = id,
            positionXZ = CanvasToWorld2D(canvas),
            tree       = DefaultNomKabarTree(),
        };
        _data.nomKabarTrees.Add(tree);

        _selectedNomKabarTreeId = id;
        MarkDirty();
        RebuildNomKabarTree(tree);
        Repaint();
    }

    private bool FindNomKabarTreeAtCanvas(Vector2 canvas, out string treeId)
    {
        treeId = null;
        if (_data?.nomKabarTrees == null) return false;

        float best = float.MaxValue;
        foreach (var tree in _data.nomKabarTrees)
        {
            if (tree == null) continue;

            Vector2 at    = WorldToCanvas(new Vector3(tree.positionXZ.x, 0f, tree.positionXZ.y));
            float   reach = Mathf.Max(8f, NomKabarTreeFootprint(tree) * PerchCanvasScale);
            float   d     = Vector2.Distance(at, canvas);
            if (d > reach || d >= best) continue;

            best   = d;
            treeId = tree.treeId;
        }
        return treeId != null;
    }

    /// <summary>How far the tree reaches out from its centre, seen from above, in world units.</summary>
    private static float NomKabarTreeFootprint(LevelSelectDesignerData.DesignerNomKabarTree tree)
        => Mathf.Max(0.01f, PlacedNomKabarTree(tree.tree, tree.scale).FootprintRadius);

    // The tree as it is built at a placement's scale, with its arms filled in.
    private static NomKabarTree PlacedNomKabarTree(NomKabarTree tree, float scale)
    {
        var placed = (tree == null || tree.IsUnset ? NomKabarTree.CodeDefault() : tree).Scaled(scale);
        placed.EnsureArms();
        return placed;
    }

    /// <summary>
    /// Where the angel stands on a built tree, in its own space: the top of its highest
    /// TreeNodeEnd — or of the hub, should every end sit below it.
    /// </summary>
    private static Vector3 NomKabarTreePerchTip(NomKabarTree placed)
    {
        float   hubY = placed.HubCentreY;
        Vector3 tip  = Vector3.up * (hubY + Mathf.Max(0.001f, placed.hubRadius) * Mathf.Max(0.05f, placed.hubSquash));

        for (int i = 0; i < placed.BuiltArms; i++)
        {
            var arm = placed.arms[i];
            if (arm == null) continue;

            Vector3 top = placed.EndCentre(i) + Vector3.up * arm.EndRadius;
            if (top.y > tip.y) tip = top;
        }
        return tip;
    }

    // ─────────────────────────────────────────────
    // CANVAS DRAWING
    // ─────────────────────────────────────────────

    /// <summary>Every freestanding tree, seen from above.</summary>
    private void DrawNomKabarTrees()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (_data?.nomKabarTrees == null) return;

        foreach (var tree in _data.nomKabarTrees)
        {
            if (tree == null) continue;
            bool selected = tree.treeId == _selectedNomKabarTreeId;

            if (tree.perch != null && tree.perch.enabled && tree.perch.perchRadius > 0f
                && LevelSelectDesignerSettings.ShowPerchRadius)
            {
                Handles.color = new Color(1f, 0.93f, 0.55f, selected ? 0.16f : 0.08f);
                Handles.DrawSolidDisc(WorldToCanvas(new Vector3(tree.positionXZ.x, 0f, tree.positionXZ.y)),
                                      Vector3.forward, tree.perch.perchRadius * PerchCanvasScale);
            }

            DrawNomKabarTreeShape(new Vector3(tree.positionXZ.x, 0f, tree.positionXZ.y),
                                  PlacedNomKabarTree(tree.tree, tree.scale), tree.turn, selected);
        }
    }

    /// <summary>
    /// A tree seen from above, as drawn in the top-down sketch: the bell's foot as a grey disc,
    /// each arm a thick dark line out of the hub along its own path, and a dark dot for each end
    /// node — the selected one lighter.
    /// </summary>
    private void DrawNomKabarTreeShape(Vector3 centre, NomKabarTree placed, float turn, bool selected)
    {
        float     scale = PerchCanvasScale;
        Vector2   at    = WorldToCanvas(centre);
        Quaternion spin = Quaternion.Euler(0f, turn, 0f);

        Handles.color = Faded(selected ? new Color(0.82f, 0.82f, 0.82f, 1f) : new Color(0.55f, 0.55f, 0.55f, 1f));
        Handles.DrawSolidDisc(at, Vector3.forward, Mathf.Max(3f, placed.baseFootRadius * scale));

        Color dark = Faded(selected ? new Color(0.3f, 0.3f, 0.3f, 1f) : new Color(0.12f, 0.12f, 0.12f, 1f));
        Handles.color = dark;
        Handles.DrawSolidDisc(at, Vector3.forward, Mathf.Max(2f, placed.hubRadius * scale));

        float hubY = placed.HubCentreY;
        for (int i = 0; i < placed.BuiltArms; i++)
        {
            var arm = placed.arms[i];
            if (arm == null) continue;

            var line   = placed.ArmCentreline(arm, hubY);
            var points = new Vector3[line.Count];
            for (int k = 0; k < line.Count; k++)
            {
                Vector2 p = WorldToCanvas(centre + spin * line[k]);
                points[k] = new Vector3(p.x, p.y, 0f);
            }
            Handles.color = dark;
            Handles.DrawAAPolyLine(Mathf.Max(2f, arm.radius * 2f * scale), points);

            Vector2 end = WorldToCanvas(centre + spin * placed.EndCentre(i));
            Handles.DrawSolidDisc(end, Vector3.forward, Mathf.Max(2.5f, arm.EndRadius * scale));
        }
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    private Vector2 _nomKabarTreeListScroll;

    /// <summary>The freestanding trees, listed with the selected one opened up. Shown in NomKabar Trees mode.</summary>
    private void DrawNomKabarTreePanel()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("NomKabar Trees", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Click the canvas to drop one. Each stands on the landscape surface under it " +
                                   "and follows the hills as they are edited. They wear the landscape's material " +
                                   "— their look is the Level Select Landscape Tuner's.",
                                   EditorStyles.wordWrappedMiniLabel);

        // The tree every new one starts as.
        using (new EditorGUILayout.HorizontalScope())
        {
            var preset = DefaultNomKabarTreePreset(create: false);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(new GUIContent("Default", "What every new tree starts as, and where a " +
                                                                      "tree's extra arms come from when its Arm Count is raised."),
                                            preset, typeof(NomKabarTreePreset), false);
            if (GUILayout.Button(new GUIContent("Edit", "Open the Default NomKabarTree in its own window. Trees " +
                                                        "already placed keep their own settings."),
                                 EditorStyles.miniButton, GUILayout.Width(38f)))
            {
                // Made outside this GUI pass, so the asset write never lands inside the layout.
                EditorApplication.delayCall += () => LevelSelectPresetEditorWindow.Open(DefaultNomKabarTreePreset(create: true));
            }
        }

        EditorGUILayout.Space(4);

        if (_data.nomKabarTrees.Count == 0)
        {
            EditorGUILayout.LabelField("None yet.", EditorStyles.miniLabel);
            return;
        }

        _nomKabarTreeListScroll = EditorGUILayout.BeginScrollView(_nomKabarTreeListScroll, GUILayout.MaxHeight(520f));

        LevelSelectDesignerData.DesignerNomKabarTree toDelete = null;

        foreach (var tree in _data.nomKabarTrees)
        {
            if (tree == null) continue;

            bool   open  = tree.treeId == _selectedNomKabarTreeId;
            string label = NomKabarTreeLabel(tree);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(open ? $"▾ {label}" : $"▸ {label}", EditorStyles.miniButton))
                {
                    _selectedNomKabarTreeId = open ? null : tree.treeId;
                    Repaint();
                }
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22f)))
                    toDelete = tree;
            }

            if (!open) continue;

            EditorGUI.indentLevel++;
            DrawNomKabarTreeItemFields(tree);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndScrollView();

        if (toDelete != null)
        {
            Undo.RecordObject(_data, "Delete NomKabar Tree");
            _data.nomKabarTrees.Remove(toDelete);
            if (_selectedNomKabarTreeId == toDelete.treeId) _selectedNomKabarTreeId = null;
            MarkDirty();
            RemoveNomKabarTreeObject(toDelete.treeId);
            Repaint();
        }

        EditorGUILayout.Space(2);
        if (GUILayout.Button("Rebuild NomKabar Trees", EditorStyles.miniButton))
            RebuildNomKabarTrees();
    }

    /// <summary>"Tree 2 — Default NomKabarTree": its place in the list and the preset it wears.</summary>
    private string NomKabarTreeLabel(LevelSelectDesignerData.DesignerNomKabarTree tree) =>
        $"Tree {_data.nomKabarTrees.IndexOf(tree) + 1} — " +
        $"{(tree.treePreset != null ? tree.treePreset.name : "own settings")}, {tree.tree?.BuiltArms ?? 0} arms";

    /// <summary>One tree's own fields — where, its turn and scale, its preset, and the tree itself.</summary>
    private void DrawNomKabarTreeItemFields(LevelSelectDesignerData.DesignerNomKabarTree tree)
    {
        if (tree.tree == null || tree.tree.IsUnset)
        {
            Undo.RecordObject(_data, "Fill NomKabar Tree");
            tree.tree = DefaultNomKabarTree();
            MarkDirty();
        }

        EditorGUI.BeginChangeCheck();

        Vector2 at = EditorGUILayout.Vector2Field(
            new GUIContent("Position", "Where it stands on the map, in world X and Z."), tree.positionXZ);
        float turn = EditorGUILayout.Slider(
            new GUIContent("Turn", "How far the whole tree is turned about its stem, in degrees."),
            tree.turn, 0f, 360f);
        float scale = DrawLollipopScaleField(tree.scale);

        DrawNomKabarTreePresetRow(tree.treePreset, tree.tree,
                                  p => tree.treePreset = p, t => tree.tree = t);
        var built = DrawPlacedNomKabarTreeFields(tree.tree, tree.treePreset, "Tree:" + tree.treeId);
        var perch = DrawPerchBlock(tree.perch);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit NomKabar Tree");
            tree.positionXZ = at;
            tree.turn       = turn;
            tree.scale      = scale;
            tree.tree       = built;
            tree.perch      = perch;
            MarkDirty();
            RebuildAfterEdit(tree, () => RebuildNomKabarTree(tree));
        }
    }

    /// <summary>The tree picked in Select mode, shown on its own. NomKabar Trees mode lists them all instead.</summary>
    private void DrawSelectedNomKabarTreeProps()
    {
        if (_mode == DesignerMode.NomKabarTree) return;

        var tree = SelectedNomKabarTree;
        if (tree == null) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(NomKabarTreeLabel(tree), EditorStyles.boldLabel);
        DrawNomKabarTreeItemFields(tree);
    }

    // ─────────────────────────────────────────────
    // GENERATION
    // ─────────────────────────────────────────────

    private static string NomKabarTreeObjectName(string treeId) => $"NomKabarTree_{SanitiseAssetName(treeId)}";

    private GameObject FindNomKabarTreeObject(string treeId)
    {
        var parent = GameObject.Find(NomKabarTreesParent);
        if (parent == null) return null;
        var child = parent.transform.Find(NomKabarTreeObjectName(treeId));
        return child != null ? child.gameObject : null;
    }

    private void RemoveNomKabarTreeObject(string treeId)
    {
        var go = FindNomKabarTreeObject(treeId);
        if (go != null) Undo.DestroyObjectImmediate(go);
    }

    /// <summary>
    /// Builds every tree afresh. Anything under the NOMKABARTREES parent that no longer has a tree
    /// in the data is cleared, so one deleted in the panel leaves the scene too.
    /// </summary>
    private void RebuildNomKabarTrees()
    {
        var parent = FindOrCreateParent(NomKabarTreesParent);

        var wanted = new HashSet<string>(
            _data.nomKabarTrees.Where(t => t != null).Select(t => NomKabarTreeObjectName(t.treeId)));

        for (int i = parent.transform.childCount - 1; i >= 0; i--)
        {
            var child = parent.transform.GetChild(i).gameObject;
            if (!wanted.Contains(child.name)) Undo.DestroyObjectImmediate(child);
        }

        foreach (var tree in _data.nomKabarTrees)
            if (tree != null) BuildNomKabarTree(tree, parent);

        AssetDatabase.SaveAssets();
    }

    /// <summary>Rebuilds one tree — its mesh and its place.</summary>
    private void RebuildNomKabarTree(LevelSelectDesignerData.DesignerNomKabarTree tree)
    {
        if (tree == null) return;
        BuildNomKabarTree(tree, FindOrCreateParent(NomKabarTreesParent));
        AssetDatabase.SaveAssets();
    }

    private void BuildNomKabarTree(LevelSelectDesignerData.DesignerNomKabarTree tree, GameObject parent)
    {
        string name = NomKabarTreeObjectName(tree.treeId);

        var go = FindNomKabarTreeObject(tree.treeId);
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Generate NomKabar Tree");
            go.transform.SetParent(parent.transform, false);
        }

        // Placed first: the landscape noise is read where it stands.
        PlaceNomKabarTreeObject(tree, go);

        var placed = PlacedNomKabarTree(tree.tree, tree.scale);
        var built  = placed.Build();
        ApplyLandscapeNoise(built, go.transform.localToWorldMatrix);
        var mesh = SaveGeneratedMesh(name, built);
        if (mesh == null) return;

        SetTurretPiece(go, mesh, NomKabarTreeMaterial());
        ApplyPerch(go, tree.perch, NomKabarTreePerchTip(placed));
    }

    /// <summary>Stands the tree where it belongs — its base on the landscape under its centre, turned.</summary>
    private void PlaceNomKabarTreeObject(LevelSelectDesignerData.DesignerNomKabarTree tree, GameObject go = null,
                                         bool recordUndo = true)
    {
        if (go == null) go = FindNomKabarTreeObject(tree.treeId);
        if (go == null) return;

        if (recordUndo) Undo.RecordObject(go.transform, "Place NomKabar Tree");
        go.transform.localPosition = new Vector3(tree.positionXZ.x, LandscapeGroundY(tree.positionXZ), tree.positionXZ.y);
        go.transform.localRotation = Quaternion.Euler(0f, tree.turn, 0f);
        go.transform.localScale    = Vector3.one;
    }

    /// <summary>
    /// Re-seats every tree on the landscape as it now stands — called wherever the spikes are, so a
    /// tree rides the hill it stands on. Only moved, never rebuilt; no undo record, since it
    /// follows the hill edit that gets undone.
    /// </summary>
    private void PlaceAllNomKabarTrees()
    {
        if (_data?.nomKabarTrees == null || _data.nomKabarTrees.Count == 0) return;
        if (GameObject.Find(NomKabarTreesParent) == null) return;

        foreach (var tree in _data.nomKabarTrees)
            if (tree != null) PlaceNomKabarTreeObject(tree, recordUndo: false);
    }

    /// <summary>The landscape's material, or the Run Material (with a warning) when there is none.</summary>
    private Material NomKabarTreeMaterial()
    {
        var landscape = LandscapeMaterial();
        if (landscape != null) return landscape;

        Debug.LogWarning("[LevelSelectDesigner] NomKabarTree: no landscape material found (LandscapeTool's, " +
                         "or 'LevelSelectLandscape'), so it wears the Run Material.");
        return _data.riverMaterial;
    }

    /// <summary>
    /// A NomKabarTree preset was just edited: every freestanding tree using it takes its settings
    /// again. Pools are handled beside their other towers in <see cref="OnPresetEdited"/>.
    /// </summary>
    private void TakeNomKabarTreePreset(NomKabarTreePreset preset,
                                        Dictionary<object, System.Action> rebuilds)
    {
        if (preset == null || _data?.nomKabarTrees == null) return;

        foreach (var tree in _data.nomKabarTrees)
        {
            if (tree == null || tree.treePreset != preset) continue;
            var t = tree;
            t.tree = preset.Build();
            rebuilds[t] = () => RebuildNomKabarTree(t);
        }
    }
}

#endif
