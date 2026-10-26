using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Lollipop towers — a cylinder stem with an orb on top. Every installation outpost carries one
/// (built into the outpost's own mesh, see the Outposts partial); a pool can stand one in the
/// middle of its island, which is what this file builds.
///
/// A pool tower is its own object under the pool, in the river material, with its base on the
/// island top — flush with the rim, so at the pool's own origin. It is only built while the
/// island's radius is above <see cref="LevelSelectDesignerData.PoolTowerMinIsland"/>.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    // ─────────────────────────────────────────────
    // DELAYED REBUILD
    // ─────────────────────────────────────────────

    // How long a rebuild waits after the last value-field edit — the Rebuild Delay in Level
    // Select Settings > Editing — so dragging or scrolling a value rebuilds once when it pauses
    // rather than on every step.
    private static double TowerRebuildDelay => LevelSelectDesignerSettings.RebuildDelay;

    // Set by the tower field blocks when one of their own fields was edited this pass; taken by
    // the change check round them, which then rebuilds after the delay instead of at once.
    // Internal so the preset editor window can take back the flag its fields set.
    internal static bool s_towerFieldsEdited;

    private readonly System.Collections.Generic.Dictionary<object, System.Action> _pendingTowerRebuilds = new();
    private double _towerRebuildDueAt;
    private int    _towerRebuildUndoGroup;
    // Set when the waiting edit was typed: the rebuild holds until the field is finished with
    // (Enter, clicking away, or leaving the window), not just until the delay runs out.
    private bool   _towerRebuildWaitsForTyping;

    /// <summary>
    /// True when the change being handled came from a value field — a drag on a number or
    /// slider, typing, or the scroll wheel — rather than a toggle, dropdown, object picker or
    /// button, which change on a click's release or a menu's command. Read off the raw event,
    /// since the field has used the event by the time its change check ends.
    /// </summary>
    private static bool IsValueFieldEdit()
    {
        var e = Event.current;
        if (e == null) return false;
        switch (e.rawType)
        {
            case EventType.MouseDown:
            case EventType.MouseDrag:
            case EventType.KeyDown:
            case EventType.ScrollWheel:
                return true;
        }
        return false;
    }

    /// <summary>
    /// Rebuilds what a change check covers: after the Rebuild Delay when the edit came from a
    /// value field (or tower settings), otherwise at once. <paramref name="key"/> names what is
    /// rebuilt, so edits to the same thing in a row wait for one rebuild. Call after the data
    /// is written.
    /// </summary>
    private void RebuildAfterEdit(object key, System.Action rebuild)
    {
        bool delayed = s_towerFieldsEdited || IsValueFieldEdit();
        s_towerFieldsEdited = false;

        if (!delayed || TowerRebuildDelay <= 0.0)
        {
            _pendingTowerRebuilds.Remove(key);
            rebuild();
            return;
        }

        _pendingTowerRebuilds[key] = rebuild;
        _towerRebuildDueAt     = EditorApplication.timeSinceStartup + TowerRebuildDelay;
        _towerRebuildUndoGroup = Undo.GetCurrentGroup();
        var e = Event.current;
        if (e != null && e.rawType == EventType.KeyDown && EditorGUIUtility.editingTextField)
            _towerRebuildWaitsForTyping = true;
        EditorApplication.update -= TowerRebuildTick;
        EditorApplication.update += TowerRebuildTick;
    }

    /// <summary>The tower name for <see cref="RebuildAfterEdit"/>, kept for the callers that use it.</summary>
    private void RebuildAfterTowerEdit(object key, System.Action rebuild) => RebuildAfterEdit(key, rebuild);

    private void TowerRebuildTick()
    {
        if (EditorApplication.timeSinceStartup < _towerRebuildDueAt) return;
        // A typed value waits until it is finished with, so half-typed numbers never rebuild.
        if (_towerRebuildWaitsForTyping && EditorGUIUtility.editingTextField && focusedWindow == this)
            return;
        FlushTowerRebuilds();
    }

    /// <summary>Runs every waiting rebuild now, folded into the edit's own undo step.</summary>
    private void FlushTowerRebuilds()
    {
        EditorApplication.update -= TowerRebuildTick;
        _towerRebuildWaitsForTyping = false;
        if (_pendingTowerRebuilds.Count == 0) return;

        var due = new System.Collections.Generic.List<System.Action>(_pendingTowerRebuilds.Values);
        _pendingTowerRebuilds.Clear();
        if (_data == null) return;

        foreach (var rebuild in due) rebuild();
        Undo.CollapseUndoOperations(_towerRebuildUndoGroup);
        SceneView.RepaintAll();
        Repaint();
    }

    /// <summary>
    /// Drops waiting rebuilds — after an undo or redo the scene already matches the data it
    /// put back.
    /// </summary>
    private void DropTowerRebuilds()
    {
        EditorApplication.update -= TowerRebuildTick;
        _pendingTowerRebuilds.Clear();
        s_towerFieldsEdited = false;
        _towerRebuildWaitsForTyping = false;
    }

    // ─────────────────────────────────────────────
    // PRESET EDITS
    // ─────────────────────────────────────────────

    /// <summary>
    /// A preset was just edited in the preset editor: everything placed that uses it takes its
    /// settings again, and rebuilds after the usual tower pause — in the preset edit's own undo
    /// step. What stays with each placement (where it stands, Placement Scale, perch) is untouched.
    /// </summary>
    private void OnPresetEdited(ScriptableObject preset)
    {
        if (_data == null || preset == null) return;

        Undo.RecordObject(_data, "Edit Preset");
        var rebuilds = new System.Collections.Generic.Dictionary<object, System.Action>();

        // Outposts on the river: the outpost itself, and its Left bank when Side is Both.
        foreach (var outpost in _data.outposts)
        {
            if (outpost == null) continue;
            var o = outpost;
            if (TakePreset(o, preset) | TakePreset(o.bothLeft, preset)
                | HoldsInner(o.tower, preset) | HoldsInner(o.bothLeft?.tower, preset))
                rebuilds[o] = () => RebuildOutpost(o);
        }

        foreach (var structure in _data.structures)
        {
            if (structure == null) continue;
            var s = structure;
            bool changed = TakePreset(s.outpost, preset);
            if (changed) SyncStructureOutpost(s);
            if (preset is LollipopTowerPreset sl && s.towerPreset  == sl) { s.tower  = sl.Build(); changed = true; }
            if (preset is TurretTowerPreset   st && s.turretPreset == st) { s.turret = st.Build(); changed = true; }
            if (HoldsInner(s.tower, preset) || HoldsInner(s.outpost?.tower, preset)) changed = true;
            if (changed) rebuilds[s] = () => RebuildStructure(s);
        }

        foreach (var pool in _data.pools)
        {
            if (pool == null) continue;
            var p = pool;
            bool changed = false;
            if (preset is LollipopTowerPreset  pl && p.towerPreset     == pl) { p.tower     = pl.Build(); changed = true; }
            if (preset is TurretTowerPreset    pt && p.turretPreset    == pt) { p.turret    = pt.Build(); changed = true; }
            if (preset is SoulJuiceTankPreset ps && p.soulJuiceTankPreset == ps) { p.soulJuiceTank = ps.Build(); changed = true; }
            if (preset is SoulFishBowlPreset  pf && p.soulFishBowlPreset  == pf) { p.soulFishBowl  = pf.Build(); changed = true; }
            if (preset is NomKabarTreePreset   pn && p.nomKabarTreePreset == pn) { p.nomKabarTree = pn.Build(); changed = true; }
            if (HoldsInner(p.tower, preset)) changed = true;
            if (changed) rebuilds[p] = () => RebuildPoolTower(p);
        }

        foreach (var arena in _data.arenas)
        {
            if (arena == null) continue;
            var a = arena;
            bool changed = false;
            if (preset is LollipopTowerPreset al && a.centreTowerPreset == al) { a.centreTower = al.Build(); changed = true; }
            if (HoldsInner(a.centreTower, preset)) changed = true;
            if (changed) rebuilds[a] = () => RebuildArenaCentreTower(a);
        }

        TakeNomKabarTreePreset(preset as NomKabarTreePreset, rebuilds);

        foreach (var wall in _data.walls)
        {
            if (wall?.nodes == null) continue;
            var w = wall;
            bool changed = false;
            if (preset is WallPreset wp && w.wallPreset == wp) { wp.Build().ApplyTo(w); changed = true; }
            foreach (var node in w.nodes)
            {
                if (node == null) continue;
                if (preset is LollipopTowerPreset wl && node.towerPreset  == wl) { node.tower  = wl.Build(); changed = true; }
                if (preset is TurretTowerPreset   wt && node.turretPreset == wt) { node.turret = wt.Build(); changed = true; }
                if (HoldsInner(node.tower, preset)) changed = true;
            }
            if (changed) rebuilds[w] = () => RebuildWall(w);
        }

        foreach (var rim in _data.rimNodes)
        {
            if (rim == null) continue;
            bool changed = false;
            if (preset is LollipopTowerPreset rl)
            {
                if (rim.towerPreset == rl)                                  { rim.tower = rl.Build();          changed = true; }
                if (rim.bothLeft != null && rim.bothLeft.towerPreset == rl) { rim.bothLeft.tower = rl.Build(); changed = true; }
            }
            if (preset is TurretTowerPreset rt)
            {
                if (rim.turretPreset == rt)                                   { rim.turret = rt.Build();          changed = true; }
                if (rim.bothLeft != null && rim.bothLeft.turretPreset == rt) { rim.bothLeft.turret = rt.Build(); changed = true; }
            }
            if (HoldsInner(rim.tower, preset) || HoldsInner(rim.bothLeft?.tower, preset)) changed = true;
            string nodeId = rim.nodeId;
            if (changed) rebuilds["RimNode:" + nodeId] = () => RebuildRimNodesAt(nodeId);
        }

        if (preset is BridgePreset bp && _data.bridges != null)
            foreach (var bridge in _data.bridges)
            {
                if (bridge == null || bridge.bridgePreset != bp) continue;
                bp.Build().ApplyTo(bridge);
                var path = _data.paths.Find(p => p.nodeIds.Contains(bridge.nodeId));
                if (path == null) continue;
                string pathId = path.pathId;
                rebuilds["Bridge:" + pathId] = () => RebuildWalkwaysOnPath(pathId, false);
            }

        if (rebuilds.Count == 0) return;

        MarkDirty();
        foreach (var pair in rebuilds)
        {
            s_towerFieldsEdited = true;   // always the paused rebuild — the editor's fields are dragged
            RebuildAfterTowerEdit(pair.Key, pair.Value);
        }
        Repaint();
    }

    /// <summary>
    /// True when <paramref name="tower"/> has an inner lollipop built from
    /// <paramref name="preset"/>. The inner one is read from its preset when the tower is built, so
    /// nothing is copied — the tower only needs rebuilding.
    /// </summary>
    private static bool HoldsInner(LollipopTower tower, ScriptableObject preset)
        => preset is LollipopTowerPreset lp && tower != null && tower.destruction && tower.innerLollipop
           && tower.innerLollipopPreset == lp;

    /// <summary>
    /// Puts <paramref name="preset"/> back on one outpost's settings when they use it — the whole
    /// outpost preset, or just its lollipop or turret. True when anything changed.
    /// </summary>
    private static bool TakePreset(LevelSelectDesignerData.DesignerOutpost o, ScriptableObject preset)
    {
        if (o == null) return false;

        switch (preset)
        {
            case InstallationOutpostPreset op when o.preset == op:
                op.ApplyTo(o);
                return true;
            case LollipopTowerPreset lp when o.towerPreset == lp:
                o.tower = lp.Build();
                return true;
            case TurretTowerPreset tp when o.turretPreset == tp:
                o.turret = tp.Build();
                return true;
        }
        return false;
    }

    /// <summary>
    /// Stem and orb fields for a tower. Hands back an edited copy, so the caller writes it only
    /// inside its own change check. A turret's body leaves out Destruction — its head is its own.
    /// <paramref name="onInnerPresetCreated"/> is handed the preset the Inner Lollipop row's New
    /// makes, after this GUI pass, to put on the tower itself.
    /// </summary>
    internal static LollipopTower DrawLollipopTowerFields(LollipopTower tower, bool withDestruction = true,
                                                          System.Action<LollipopTowerPreset> onInnerPresetCreated = null)
    {
        var edited = (tower ?? new LollipopTower()).Clone();
        EditorGUI.BeginChangeCheck();

        edited.stemRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Stem Radius", "Radius of the cylinder stem."), edited.stemRadius));
        edited.stemHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Stem Height", "Height of the stem, from its base up to where it meets the orb."),
            edited.stemHeight));
        edited.orbRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Orb Radius", "Radius of the orb on top."), edited.orbRadius));
        edited.lowerRampHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Lower Ramp Height", "Height of the lower ramp under the base, flaring out from the base " +
                                                "radius to the lower ramp radius on the ground. 0 = no lower ramp."),
            edited.lowerRampHeight));
        edited.lowerRampRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Lower Ramp Radius", "Radius of the lower ramp's foot on the ground — the radius it " +
                                                "flares out to from the base."),
            edited.lowerRampRadius));
        edited.baseRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Base Radius", "Radius of the round base the stem stands on."), edited.baseRadius));
        edited.baseHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Base Height", "Height of the round base. 0 = no base, and the stem stands on the ground."),
            edited.baseHeight));
        edited.rampHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Upper Ramp Height", "Height of the upper ramp on top of the base, sloping in from the " +
                                                "base radius to the base 2 radius. 0 = no upper ramp."),
            edited.rampHeight));
        edited.base2Radius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Base 2 Radius", "Radius of the second base on top of the upper ramp — and the radius " +
                                            "the upper ramp slopes in to."),
            edited.base2Radius));
        edited.base2Height = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Base 2 Height", "Height of the second base, on top of the upper ramp. 0 = no second base."),
            edited.base2Height));

        if (withDestruction)
        {
            EditorGUILayout.Space(4);
            edited.destruction = EditorGUILayout.Toggle(
                new GUIContent("Destruction", "Breaks a hole into the orb. The orb becomes a hollow shell, " +
                                              "seen inside through the hole."),
                edited.destruction);
            using (new EditorGUI.DisabledScope(!edited.destruction))
            {
                EditorGUI.indentLevel++;
                edited.holeAround = EditorGUILayout.Slider(
                    new GUIContent("Hole Around", "Where the hole sits round the orb, in degrees."),
                    edited.holeAround, -180f, 180f);
                edited.holeUpDown = EditorGUILayout.Slider(
                    new GUIContent("Hole Up/Down", "How far the hole sits above (+) or below (−) the orb's " +
                                                   "middle, in degrees. 90 = right on top."),
                    edited.holeUpDown, -90f, 90f);
                edited.holeSize = EditorGUILayout.Slider(
                    new GUIContent("Hole Size", "How big the hole is: degrees from its middle out to its " +
                                                "edge, before the edge's rounded shapes."),
                    edited.holeSize, 1f, 150f);
                edited.holeEdgeComplexity = EditorGUILayout.IntSlider(
                    new GUIContent("Hole Edge Complexity", "How many rounded shapes run round the hole's " +
                                                           "edge. 0 = a plain round hole."),
                    edited.holeEdgeComplexity, 0, 16);
                edited.holeEdgeDepth = EditorGUILayout.Slider(
                    new GUIContent("Hole Edge Depth", "How far the edge's rounded shapes reach out past the " +
                                                      "hole, as a share of Hole Size."),
                    edited.holeEdgeDepth, 0f, 1f);
                edited.holeEdgeUnevenness = EditorGUILayout.Slider(
                    new GUIContent("Hole Edge Unevenness", "How much the edge's rounded shapes differ from " +
                                                           "each other in length, width and spacing. 0 = all alike."),
                    edited.holeEdgeUnevenness, 0f, 1f);
                edited.holeEdgeRoundness = EditorGUILayout.Slider(
                    new GUIContent("Hole Edge Roundness", "The shape of each rounded shape on the edge: low = " +
                                                          "broad gentle waves, high = full rounded tips."),
                    edited.holeEdgeRoundness, 0f, 1f);
                edited.holeEdgeThickness = Mathf.Max(0.001f, EditorGUILayout.FloatField(
                    new GUIContent("Hole Edge Thickness", "How thick the orb's shell is — the band you see " +
                                                          "round the hole's edge."),
                    edited.holeEdgeThickness));

                EditorGUILayout.Space(2);
                edited.innerLollipop = EditorGUILayout.Toggle(
                    new GUIContent("Inner Lollipop", "Stands a small lollipop inside the broken orb, on the " +
                                                     "lowest point of its floor."),
                    edited.innerLollipop);
                if (edited.innerLollipop)
                {
                    EditorGUI.indentLevel++;
                    var source = tower;
                    edited.innerLollipopPreset = LevelSelectPresetEditorWindow.DrawPresetLink(
                        new GUIContent("Inner Lollipop Preset", "The lollipop tower preset the inner lollipop is " +
                                                                "built from. Always built whole — its own " +
                                                                "Destruction and Inner Lollipop are ignored."),
                        edited.innerLollipopPreset, LollipopPresetFolder, "Lollipop Tower",
                        () =>
                        {
                            // From the inner lollipop already chosen, or else this tower, whole.
                            var made = CreateInstance<LollipopTowerPreset>();
                            var from = source?.innerLollipopPreset != null ? source.innerLollipopPreset.Build()
                                                                           : (source ?? new LollipopTower()).Clone();
                            from.destruction   = false;
                            from.innerLollipop = false;
                            made.CopyFrom(from);
                            return made;
                        },
                        made => onInnerPresetCreated?.Invoke(made));
                    edited.innerLollipopSize = EditorGUILayout.Slider(
                        new GUIContent("Inner Lollipop Size", "How tall the inner lollipop stands, as a share of " +
                                                              "the space inside the orb above where it stands. " +
                                                              "1 = its orb touches the top of the hollow."),
                        edited.innerLollipopSize, 0.05f, 1f);
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }
        }

        if (EditorGUI.EndChangeCheck()) s_towerFieldsEdited = true;
        return edited;
    }

    /// <summary>
    /// The placement's own scale for its tower — every size multiplied by it where the tower is
    /// built. It stays with the placement: presets neither save nor load it.
    /// </summary>
    private static float DrawLollipopScaleField(float scale)
    {
        EditorGUI.BeginChangeCheck();
        float edited = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Placement Scale", "Scales the whole tower where it stands here. Kept with " +
                                              "this placement, not saved into the tower preset."),
            scale));
        if (EditorGUI.EndChangeCheck()) s_towerFieldsEdited = true;
        return edited;
    }

    internal const string LollipopPresetFolder = "Assets/ScriptsData/DataScripts/LollipopTowerPresets";

    /// <summary>
    /// A tower's preset: pick one from the dropdown to put its sizes on this tower, Edit to open it
    /// in the preset editor (every tower using it changes with it), or New to make one from this
    /// tower's sizes and open that. <paramref name="setPreset"/> writes which preset the tower
    /// uses, and <paramref name="setTower"/> puts a loaded tower on it — both called inside an Undo
    /// record of the designer data. Returns true when a preset was loaded.
    /// </summary>
    private bool DrawLollipopPresetRow(LollipopTowerPreset current, LollipopTower tower,
                                       System.Action<LollipopTowerPreset> setPreset,
                                       System.Action<LollipopTower> setTower)
        => DrawTowerPresetRow(current, tower, setPreset, setTower,
                              LollipopPresetFolder, "Tower Preset", "Lollipop Tower",
                              p => p.Build(), (p, t) => p.CopyFrom(t),
                              t => (t ?? new LollipopTower()).Clone());

    internal const string TurretPresetFolder = "Assets/ScriptsData/DataScripts/TurretTowerPresets";

    /// <summary>The same preset row as a lollipop's, for a turret — its sizes.</summary>
    private bool DrawTurretPresetRow(TurretTowerPreset current, TurretTower turret,
                                     System.Action<TurretTowerPreset> setPreset,
                                     System.Action<TurretTower> setTurret)
        => DrawTowerPresetRow(current, turret, setPreset, setTurret,
                              TurretPresetFolder, "Turret Preset", "Turret Tower",
                              p => p.Build(), (p, t) => p.CopyFrom(t),
                              t => (t ?? new TurretTower()).Clone());

    // The preset row itself, for any kind of tower. `what` names the kind in dialogs and undo.
    private bool DrawTowerPresetRow<TPreset, TTower>(TPreset current, TTower tower,
                                                     System.Action<TPreset> setPreset,
                                                     System.Action<TTower> setTower,
                                                     string folder, string label, string what,
                                                     System.Func<TPreset, TTower> build,
                                                     System.Action<TPreset, TTower> copyInto,
                                                     System.Func<TTower, TTower> snapshotOf)
        where TPreset : ScriptableObject
    {
        bool loaded = false;

        var picked = LevelSelectPresetEditorWindow.DrawPresetLink(
            new GUIContent(label, $"The {what.ToLowerInvariant()} presets kept in " + folder +
                                  ". Picking one puts its settings on this tower; Edit changes them."),
            current, folder, what,
            () =>
            {
                var made = CreateInstance<TPreset>();
                copyInto(made, snapshotOf(tower));
                return made;
            },
            made =>
            {
                if (_data == null) return;
                Undo.RecordObject(_data, $"New {what} Preset");
                setPreset(made);
                MarkDirty();
                Repaint();
            });

        if (picked != current)
        {
            Undo.RecordObject(_data, $"Load {what} Preset");
            setPreset(picked);
            if (picked != null)
            {
                setTower(build(picked));
                loaded = true;
            }
            MarkDirty();
            GUI.FocusControl(null);
        }

        if (current == null && picked == null)
            EditorGUILayout.LabelField("No preset — New saves this tower's sizes as one to edit.",
                                       EditorStyles.wordWrappedMiniLabel);

        return loaded;
    }

    /// <summary>
    /// The Lollipop / Turret choice for a place a tower stands. A pool island passes
    /// <paramref name="allowSoulJuiceTank"/> to offer the Soul Juice Tank and the Soul Fish Bowl
    /// too — nowhere else can — and a natural pool's island <paramref name="allowNomKabarTree"/>
    /// for a NomKabarTree as well.
    /// </summary>
    internal static LevelSelectDesignerData.TowerKind DrawTowerKindField(LevelSelectDesignerData.TowerKind kind,
                                                                        bool allowSoulJuiceTank = false,
                                                                        bool allowNomKabarTree = false)
    {
        var label = new GUIContent("Kind", allowSoulJuiceTank
            ? "A lollipop tower, a turret whose head turns and whose nose tilts, a soul juice tank — " +
              "a lollipop with an intake and a tap, where a soul route begins — or a soul fish bowl, " +
              "a bowl of water on a column." +
              (allowNomKabarTree ? " A natural pool's island can stand a NomKabarTree instead." : "")
            : "A lollipop tower, or a turret whose head turns and whose nose tilts.");

        var kinds = new System.Collections.Generic.List<LevelSelectDesignerData.TowerKind>
            { LevelSelectDesignerData.TowerKind.Lollipop, LevelSelectDesignerData.TowerKind.Turret };
        var names = new System.Collections.Generic.List<string> { "Lollipop", "Turret" };
        if (allowSoulJuiceTank)
        {
            kinds.Add(LevelSelectDesignerData.TowerKind.SoulJuiceTank); names.Add("Soul Juice Tank");
            kinds.Add(LevelSelectDesignerData.TowerKind.SoulFishBowl);  names.Add("Soul Fish Bowl");
        }
        if (allowSoulJuiceTank && allowNomKabarTree)
        {
            kinds.Add(LevelSelectDesignerData.TowerKind.NomKabarTree);  names.Add("NomKabar Tree");
        }

        int index = Mathf.Max(0, kinds.IndexOf(kind));
        index = EditorGUILayout.Popup(label, index, names.ConvertAll(n => new GUIContent(n)).ToArray());
        return kinds[Mathf.Clamp(index, 0, kinds.Count - 1)];
    }

    internal const string SoulJuiceTankPresetFolder = "Assets/ScriptsData/DataScripts/SoulJuiceTankPresets";

    /// <summary>The same preset row as a lollipop's, for a soul juice tank — its sizes.</summary>
    private bool DrawSoulJuiceTankPresetRow(SoulJuiceTankPreset current, SoulJuiceTank tower,
                                        System.Action<SoulJuiceTankPreset> setPreset,
                                        System.Action<SoulJuiceTank> setTower)
        => DrawTowerPresetRow(current, tower, setPreset, setTower,
                              SoulJuiceTankPresetFolder, "Soul Juice Tank Preset", "Soul Juice Tank",
                              p => p.Build(), (p, t) => p.CopyFrom(t),
                              t => (t ?? new SoulJuiceTank()).Clone());

    /// <summary>
    /// A soul juice tank's fields: a lollipop's own (no Destruction), then its two hubs, the tap
    /// and the intake. Hands back an edited copy, so the caller writes it only inside its own
    /// change check.
    /// </summary>
    internal static SoulJuiceTank DrawSoulJuiceTankFields(SoulJuiceTank tower)
    {
        var edited = (tower ?? new SoulJuiceTank()).Clone();
        EditorGUI.BeginChangeCheck();

        var lolly = DrawLollipopTowerFields(edited.Lollipop(), withDestruction: false);
        edited.stemRadius      = lolly.stemRadius;      edited.stemHeight      = lolly.stemHeight;
        edited.orbRadius       = lolly.orbRadius;
        edited.baseRadius      = lolly.baseRadius;      edited.baseHeight      = lolly.baseHeight;
        edited.lowerRampHeight = lolly.lowerRampHeight; edited.lowerRampRadius = lolly.lowerRampRadius;
        edited.rampHeight      = lolly.rampHeight;
        edited.base2Radius     = lolly.base2Radius;     edited.base2Height     = lolly.base2Height;

        EditorGUILayout.LabelField("Top Hub", EditorStyles.miniBoldLabel);
        edited.topHubRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Radius", "Radius of the round hub on top of the orb, the intake comes up out of."),
            edited.topHubRadius));
        edited.topHubThickness = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Thickness", "How far the top hub stands up past the orb."), edited.topHubThickness));

        EditorGUILayout.LabelField("Side Hub", EditorStyles.miniBoldLabel);
        edited.sideHubAround = EditorGUILayout.Slider(
            new GUIContent("Around", "Where the side hub sits round the orb, in degrees."),
            edited.sideHubAround, -180f, 180f);
        edited.sideHubRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Radius", "Radius of the round hub on the side of the orb, the tap comes out of."),
            edited.sideHubRadius));
        edited.sideHubThickness = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Thickness", "How far the side hub stands out past the orb."), edited.sideHubThickness));

        EditorGUILayout.LabelField("Tap", EditorStyles.miniBoldLabel);
        edited.tapThickness = Mathf.Max(0.002f, EditorGUILayout.FloatField(
            new GUIContent("Thickness", "The tap's thickness, across the outside. Its bend is rounded to match it."),
            edited.tapThickness));
        edited.tapReach = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Reach Out", "How far the tap reaches out past the side hub before it turns down."),
            edited.tapReach));
        edited.tapDrop = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Drop", "How far the tap drops after it turns down."), edited.tapDrop));

        EditorGUILayout.LabelField("Intake", EditorStyles.miniBoldLabel);
        edited.intakeThickness = Mathf.Max(0.002f, EditorGUILayout.FloatField(
            new GUIContent("Thickness", "The intake's thickness, across the outside. Which pipe hub it goes " +
                                        "up to is picked on each pool, under Intake Hub."),
            edited.intakeThickness));

        if (EditorGUI.EndChangeCheck()) s_towerFieldsEdited = true;
        return edited;
    }

    internal const string SoulFishBowlPresetFolder = "Assets/ScriptsData/DataScripts/SoulFishBowlPresets";

    /// <summary>The same preset row as a lollipop's, for a soul fish bowl — its sizes.</summary>
    private bool DrawSoulFishBowlPresetRow(SoulFishBowlPreset current, SoulFishBowl tower,
                                           System.Action<SoulFishBowlPreset> setPreset,
                                           System.Action<SoulFishBowl> setTower)
        => DrawTowerPresetRow(current, tower, setPreset, setTower,
                              SoulFishBowlPresetFolder, "Soul Fish Bowl Preset", "Soul Fish Bowl",
                              p => p.Build(), (p, t) => p.CopyFrom(t),
                              t => (t ?? new SoulFishBowl()).Clone());

    /// <summary>
    /// A soul fish bowl's base, column and bowl fields. Hands back an edited copy, so the caller
    /// writes it only inside its own change check.
    /// </summary>
    internal static SoulFishBowl DrawSoulFishBowlFields(SoulFishBowl tower)
    {
        var edited = (tower ?? new SoulFishBowl()).Clone();
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("Base", EditorStyles.miniBoldLabel);
        edited.baseRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Base Radius", "Radius of the round base the column stands on."), edited.baseRadius));
        edited.baseHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Base Height", "Height of the round base. 0 = no base, and the column stands on the ground."),
            edited.baseHeight));
        edited.rampHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Ramp Height", "Height of the ramp on top of the base, sloping in from the base radius " +
                                          "to the base 2 radius. 0 = no ramp."), edited.rampHeight));
        edited.base2Radius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Base 2 Radius", "Radius of the second base on top of the ramp — and the radius the ramp slopes in to."),
            edited.base2Radius));
        edited.base2Height = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Base 2 Height", "Height of the second base, on top of the ramp. 0 = no second base."),
            edited.base2Height));

        EditorGUILayout.LabelField("Column", EditorStyles.miniBoldLabel);
        edited.columnRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Column Radius", "Radius of the column holding the bowl up."), edited.columnRadius));
        edited.columnHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Column Height", "Height of the column, from the top of the base up to where the " +
                                            "bowl's underside closes onto it."), edited.columnHeight));

        EditorGUILayout.LabelField("Bowl", EditorStyles.miniBoldLabel);
        edited.bowlRadius = Mathf.Max(0.01f, EditorGUILayout.FloatField(
            new GUIContent("Bowl Radius", "Radius of the bowl, out to its outer wall."), edited.bowlRadius));
        edited.rimWidth = Mathf.Max(0.005f, EditorGUILayout.FloatField(
            new GUIContent("Rim Width", "Width of the flat rim round the top of the bowl."), edited.rimWidth));
        edited.bandHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Band Height", "Height of the straight outer wall below the rim, before the underside " +
                                          "curves in. 0 = the curve starts right at the rim."), edited.bandHeight));
        edited.undersideDepth = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Underside Depth", "How far the curved underside drops, from the band down to the column."),
            edited.undersideDepth));
        edited.basinDepth = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Basin Depth", "How deep the basin inside the rim is at its middle. Kept above the " +
                                          "underside, so it never breaks through the bottom."), edited.basinDepth));
        edited.waterDrop = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Water Drop", "How far below the rim the water in the bowl sits. Less than the " +
                                         "basin depth, or there is no water."), edited.waterDrop));
        edited.holeRadius = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Hole Radius", "Radius of the hole in the middle of the basin, seen once the water " +
                                          "has drained. 0 = no hole."), edited.holeRadius));

        if (edited.BasinDepth < edited.basinDepth - 0.0001f)
            EditorGUILayout.LabelField($"Basin held to {edited.BasinDepth:F3} so it stays above the underside.",
                                       EditorStyles.miniLabel);
        if (edited.WaterRadius <= 0.0001f)
            EditorGUILayout.LabelField("Water Drop is as deep as the basin — there is no water for the souls " +
                                       "to swim in.", EditorStyles.miniLabel);

        if (EditorGUI.EndChangeCheck()) s_towerFieldsEdited = true;
        return edited;
    }

    /// <summary>
    /// A turret's body and nose fields. Hands back an edited copy, so the caller writes
    /// it only inside its own change check.
    /// </summary>
    internal static TurretTower DrawTurretTowerFields(TurretTower turret)
    {
        var edited = (turret ?? new TurretTower()).Clone();
        EditorGUI.BeginChangeCheck();

        edited.body = DrawLollipopTowerFields(edited.body, withDestruction: false);

        EditorGUILayout.LabelField("Nose", EditorStyles.miniBoldLabel);
        edited.noseLength = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Nose Length", "How far the nose reaches out past the orb's surface."),
            edited.noseLength));
        edited.noseOuterRadius = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Nose Radius", "Radius of the outside of the nose."), edited.noseOuterRadius));
        edited.noseInnerRadius = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Hollow Radius", "Radius of the hollow down the middle of the nose. 0 = solid."),
            edited.noseInnerRadius));
        edited.restPitch = EditorGUILayout.FloatField(
            new GUIContent("Rest Pitch", "How far the nose is tilted up from level, in degrees, as it is " +
                                         "built — and where it starts before the animation moves it."),
            edited.restPitch);

        EditorGUILayout.LabelField("How every turret moves is set on the scene's Turret Tower Animation.",
                                   EditorStyles.miniLabel);

        if (EditorGUI.EndChangeCheck()) s_towerFieldsEdited = true;
        return edited;
    }

    // ── Sizes for the canvas and the perch, whichever kind stands there ──

    // A soul juice tank is sized for the canvas by its orb, a soul fish bowl by its bowl — what is
    // seen from above.
    private static float PoolTowerCanvasRadius(LevelSelectDesignerData.DesignerPool pool)
        => pool.towerKind == LevelSelectDesignerData.TowerKind.SoulJuiceTank
            ? (pool.soulJuiceTank ?? new SoulJuiceTank()).Scaled(pool.towerScale).orbRadius
         : pool.towerKind == LevelSelectDesignerData.TowerKind.SoulFishBowl
            ? (pool.soulFishBowl ?? new SoulFishBowl()).Scaled(pool.towerScale).bowlRadius
            : TowerOrbRadius(pool.towerKind, pool.tower, pool.turret, pool.towerScale);

    private static float TowerOrbRadius(LevelSelectDesignerData.TowerKind kind, LollipopTower tower,
                                        TurretTower turret, float scale)
        => kind == LevelSelectDesignerData.TowerKind.Turret
            ? (turret ?? new TurretTower()).Scaled(scale).body.orbRadius
            : (tower  ?? new LollipopTower()).Scaled(scale).orbRadius;

    private static float TowerBaseRadius(LevelSelectDesignerData.TowerKind kind, LollipopTower tower,
                                         TurretTower turret, float scale)
        => kind == LevelSelectDesignerData.TowerKind.Turret
            ? (turret ?? new TurretTower()).Scaled(scale).body.FootRadius
            : (tower  ?? new LollipopTower()).Scaled(scale).FootRadius;

    private static float TowerTopY(LevelSelectDesignerData.TowerKind kind, LollipopTower tower,
                                   TurretTower turret, float scale)
        => kind == LevelSelectDesignerData.TowerKind.Turret
            ? (turret ?? new TurretTower()).Scaled(scale).TopY
            : (tower  ?? new LollipopTower()).Scaled(scale).TopY;

    /// <summary>The tower toggle and sizes for the selected pool, in the Pools list.</summary>
    private void DrawPoolTowerFields(LevelSelectDesignerData.DesignerPool pool)
    {
        float island = _data.PoolShapeFor(pool).islandRadius;

        EditorGUI.BeginChangeCheck();
        bool hasTower = EditorGUILayout.Toggle(
            new GUIContent("Tower", "Stand a tower in the middle of the island — a lollipop or a turret."),
            pool.hasTower);

        var            kind   = pool.towerKind;
        LollipopTower  tower  = pool.tower;
        TurretTower    turret = pool.turret;
        SoulJuiceTank souls  = pool.soulJuiceTank;
        SoulFishBowl   fishBowl = pool.soulFishBowl;
        string         tankHub  = pool.tankHubId;
        NomKabarTree   nkTree = pool.nomKabarTree;
        float          nkTurn = pool.nomKabarTreeTurn;
        float          scale  = pool.towerScale;
        var            perch  = pool.towerPerch;
        if (hasTower)
        {
            if (island <= LevelSelectDesignerData.PoolTowerMinIsland)
                EditorGUILayout.LabelField(
                    "This pool has no island — a tower is only built on one.",
                    EditorStyles.miniLabel);

            // A NomKabarTree is a natural feature, so only a natural pool offers one — a pool that
            // already has one keeps showing it, so it can be changed back.
            bool natural = IsNaturalRun(PoolPath(pool));
            kind = DrawTowerKindField(pool.towerKind, allowSoulJuiceTank: true,
                                      allowNomKabarTree: natural ||
                                                         pool.towerKind == LevelSelectDesignerData.TowerKind.NomKabarTree);
            if (kind == LevelSelectDesignerData.TowerKind.NomKabarTree)
            {
                if (!natural)
                    EditorGUILayout.LabelField("This pool's river is no longer a natural run — the tree still stands.",
                                               EditorStyles.wordWrappedMiniLabel);

                // First picked, it starts as the Default NomKabarTree.
                if (nkTree == null || nkTree.IsUnset)
                {
                    Undo.RecordObject(_data, "Fill NomKabar Tree");
                    pool.nomKabarTree = DefaultNomKabarTree();
                    MarkDirty();
                }

                DrawNomKabarTreePresetRow(pool.nomKabarTreePreset, pool.nomKabarTree,
                                          p => pool.nomKabarTreePreset = p, t => pool.nomKabarTree = t);
                scale  = DrawLollipopScaleField(pool.towerScale);
                nkTurn = EditorGUILayout.Slider(
                    new GUIContent("Turn", "How far the whole tree is turned about its stem, in degrees."),
                    pool.nomKabarTreeTurn, 0f, 360f);
                nkTree = DrawPlacedNomKabarTreeFields(pool.nomKabarTree, pool.nomKabarTreePreset,
                                                      "Pool:" + pool.nodeId);
            }
            else if (kind == LevelSelectDesignerData.TowerKind.SoulJuiceTank)
            {
                DrawSoulJuiceTankPresetRow(pool.soulJuiceTankPreset, pool.soulJuiceTank,
                                       p => pool.soulJuiceTankPreset = p, t => pool.soulJuiceTank = t);
                souls = pool.soulJuiceTank;   // read back: picking a preset just loaded it
                scale = DrawLollipopScaleField(pool.towerScale);
                tankHub = DrawTankIntakeHubField(pool);
                DrawSoulJuiceTankRoutesNote(pool);
            }
            else if (kind == LevelSelectDesignerData.TowerKind.SoulFishBowl)
            {
                DrawSoulFishBowlPresetRow(pool.soulFishBowlPreset, pool.soulFishBowl,
                                          p => pool.soulFishBowlPreset = p, t => pool.soulFishBowl = t);
                fishBowl = pool.soulFishBowl;   // read back: picking a preset just loaded it
                scale    = DrawLollipopScaleField(pool.towerScale);
            }
            else if (kind == LevelSelectDesignerData.TowerKind.Turret)
            {
                DrawTurretPresetRow(pool.turretPreset, pool.turret,
                                    p => pool.turretPreset = p, t => pool.turret = t);
                turret = pool.turret;   // read back: picking a preset just loaded it
                scale  = DrawLollipopScaleField(pool.towerScale);
            }
            else
            {
                DrawLollipopPresetRow(pool.towerPreset, pool.tower,
                                      p => pool.towerPreset = p, t => pool.tower = t);
                tower = pool.tower;   // read back: picking a preset just loaded it
                scale = DrawLollipopScaleField(pool.towerScale);
            }

            // Neither has a perch: the tank never had one, and the fish bowl's top is water.
            if (kind != LevelSelectDesignerData.TowerKind.SoulJuiceTank &&
                kind != LevelSelectDesignerData.TowerKind.SoulFishBowl)
                perch = DrawPerchBlock(pool.towerPerch);
        }

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Pool Tower");
            string oldHub = pool.tankHubId;
            pool.hasTower   = hasTower;
            pool.towerKind  = kind;
            pool.tower      = tower;
            pool.turret     = turret;
            pool.soulJuiceTank  = souls;
            pool.soulFishBowl   = fishBowl;
            pool.tankHubId      = tankHub;

            // The hub let go of turns its ring back to its own side.
            if (oldHub != tankHub && FindPipeHub(oldHub, out var oldPipe, out _)) BuildPipeIfGenerated(oldPipe);
            pool.nomKabarTree     = kind == LevelSelectDesignerData.TowerKind.NomKabarTree ? nkTree : pool.nomKabarTree;
            pool.nomKabarTreeTurn = nkTurn;
            pool.towerScale = scale;
            pool.towerPerch = perch;
            MarkDirty();
            RebuildAfterTowerEdit(pool, () => RebuildPoolTower(pool));
        }

        if (pool.hasTower && OcclusionToggle(ref pool.occlusion, ref pool.occlusionStyle)) RebuildPoolTower(pool);

        // Outside the tower's change check — the cinematic is written onto the tower, never rebuilds it.
        if (pool.hasTower && pool.towerKind == LevelSelectDesignerData.TowerKind.SoulJuiceTank)
            DrawTowerCinematic(pool);
    }

    /// <summary>Which soul juice paths begin at this tank.</summary>
    private void DrawSoulJuiceTankRoutesNote(LevelSelectDesignerData.DesignerPool pool)
    {
        var starting = _data.soulRoutes.FindAll(r => r != null && r.originNodeId == pool.nodeId);
        if (starting.Count == 0)
        {
            EditorGUILayout.LabelField("No soul route starts here yet — pick this tank as a route's origin " +
                                       "in Souls mode.", EditorStyles.wordWrappedMiniLabel);
            return;
        }

        foreach (var route in starting)
            EditorGUILayout.LabelField($"Route '{route.displayName}' starts here.", EditorStyles.miniLabel);
    }

    // ─────────────────────────────────────────────
    // GENERATION
    // ─────────────────────────────────────────────

    /// <summary>
    /// Builds, rebuilds or clears one pool's tower, under the generated pool it belongs to. Does
    /// nothing when that pool has not been generated.
    /// </summary>
    private void RebuildPoolTower(LevelSelectDesignerData.DesignerPool pool)
    {
        if (pool == null || string.IsNullOrEmpty(pool.nodeId)) return;

        string poolName = PoolMeshName(pool);
        var record = FindObjectsOfType<RiverPoolMesh>().FirstOrDefault(r => r.meshAssetName == poolName);
        if (record == null) return;

        // The tower goes on the pool's own object, beside the pool mesh rather than inside it.
        Transform poolRoot = record.transform.parent != null ? record.transform.parent : record.transform;

        string    towerName = PoolTowerMeshName(pool);
        Transform existing  = poolRoot.Find(towerName);

        bool isTurret = pool.towerKind == LevelSelectDesignerData.TowerKind.Turret;
        bool isSouls  = pool.towerKind == LevelSelectDesignerData.TowerKind.SoulJuiceTank;
        bool isBowl   = pool.towerKind == LevelSelectDesignerData.TowerKind.SoulFishBowl;
        bool isTree   = pool.towerKind == LevelSelectDesignerData.TowerKind.NomKabarTree;
        bool wanted   = pool.hasTower &&
                        (isTree   ? pool.nomKabarTree != null && !pool.nomKabarTree.IsUnset :
                         isSouls  ? pool.soulJuiceTank != null :
                         isBowl   ? pool.soulFishBowl != null :
                         isTurret ? pool.turret != null : pool.tower != null) &&
                        record.islandRadius > LevelSelectDesignerData.PoolTowerMinIsland;

        // A tower of another kind is cleared rather than reused — their pieces differ. A tank
        // built before 2026-10-26 still has a bowl, so it is cleared and built again as well.
        if (existing != null &&
            (!wanted ||
             (existing.GetComponent<TurretTowerController>() != null) != isTurret ||
             (existing.GetComponent<SoulJuiceTankBowl>() != null) != isSouls ||
             (existing.Find(SoulJuiceTankBowlChild) != null) != isBowl))
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
            existing = null;
        }
        if (!wanted)
        {
            // A tank taken away lets go of its hub, so the ring goes back to its own side.
            if (FindPipeHub(pool.tankHubId, out var freed, out _)) BuildPipeIfGenerated(freed);
            return;
        }

        if (isBowl)
        {
            var bowlBuilt = PlaceSoulFishBowl(poolRoot, towerName, pool.soulFishBowl.Scaled(pool.towerScale),
                                              _data.ProfileFor(_data.PoolRiverName(pool)), existing?.gameObject);
            if (bowlBuilt == null) return;

            // The island top is flush with the rim, which is the pool's origin.
            bowlBuilt.transform.localPosition = Vector3.zero;
            bowlBuilt.transform.localRotation = Quaternion.identity;
            bowlBuilt.transform.localScale    = Vector3.one;
            ApplyOcclusion(bowlBuilt, pool.occlusion, pool.occlusionStyle);
            AssetDatabase.SaveAssets();
            return;
        }

        if (isTree)
        {
            GameObject treeGO = existing != null ? existing.gameObject : null;
            if (treeGO == null)
            {
                treeGO = new GameObject(towerName);
                Undo.RegisterCreatedObjectUndo(treeGO, "Generate Pool NomKabar Tree");
                treeGO.transform.SetParent(poolRoot, false);
            }

            // The island top is flush with the rim, which is the pool's origin. Placed before the
            // mesh is built, since the landscape noise it wears is read where it stands.
            treeGO.transform.localPosition = Vector3.zero;
            treeGO.transform.localRotation = Quaternion.Euler(0f, pool.nomKabarTreeTurn, 0f);
            treeGO.transform.localScale    = Vector3.one;

            var placedTree = PlacedNomKabarTree(pool.nomKabarTree, pool.towerScale);
            var built      = placedTree.Build();
            ApplyLandscapeNoise(built, treeGO.transform.localToWorldMatrix);
            var treeMesh = SaveGeneratedMesh(towerName, built);
            if (treeMesh == null) return;
            SetTurretPiece(treeGO, treeMesh, NomKabarTreeMaterial());

            // She lands on top of the highest TreeNodeEnd.
            ApplyPerch(treeGO, pool.towerPerch, NomKabarTreePerchTip(placedTree));
            ApplyOcclusion(treeGO, pool.occlusion, pool.occlusionStyle);
            AssetDatabase.SaveAssets();
            return;
        }

        if (isSouls)
        {
            var built = PlaceSoulJuiceTank(poolRoot, towerName, pool.nodeId, pool.soulJuiceTank.Scaled(pool.towerScale),
                                           existing?.gameObject);
            if (built == null) return;

            // The island top is flush with the rim, which is the pool's origin.
            built.transform.localPosition = Vector3.zero;
            built.transform.localRotation = Quaternion.identity;
            built.transform.localScale    = Vector3.one;

            // Its hub's ring turns to face where the tank now stands, then the intake is laid to it.
            if (FindPipeHub(pool.tankHubId, out var linked, out _)) BuildPipeIfGenerated(linked);
            RebuildTankIntake(pool);

            AddSoulJuiceTankRelease(built, record.islandRadius,
                                HasTowerCinematic(pool) ? pool.towerCinematicPreset : null);
            ApplyOcclusion(built, pool.occlusion, pool.occlusionStyle);

            AssetDatabase.SaveAssets();
            return;
        }

        if (isTurret)
        {
            var turret = pool.turret.Scaled(pool.towerScale);
            var built  = PlaceTurret(poolRoot, towerName, turret, _data.riverMaterial, existing?.gameObject);
            if (built == null) return;

            // The island top is flush with the rim, which is the pool's origin.
            built.transform.localPosition = Vector3.zero;
            built.transform.localRotation = Quaternion.identity;
            built.transform.localScale    = Vector3.one;

            ApplyPerch(built, pool.towerPerch, Vector3.up * turret.TopY);
            ApplyOcclusion(built, pool.occlusion, pool.occlusionStyle);
            AssetDatabase.SaveAssets();
            return;
        }

        var placed = pool.tower.Scaled(pool.towerScale);
        var mesh   = SaveGeneratedMesh(towerName, placed.Build(StoneShadingForBuild));
        if (mesh == null) return;

        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(towerName);
            Undo.RegisterCreatedObjectUndo(go, "Generate Pool Tower");
            go.transform.SetParent(poolRoot, false);
        }

        // The island top is flush with the rim, which is the pool's origin.
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;

        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        EditorUtility.SetDirty(filter);

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _data.riverMaterial;
        EditorUtility.SetDirty(renderer);

        // A tower in the middle of an island is about the most inviting thing on the map to land
        // on, so it carries its own perch block. The tip is the top of the orb.
        ApplyPerch(go, pool.towerPerch, Vector3.up * placed.TopY);
        ApplyOcclusion(go, pool.occlusion, pool.occlusionStyle);

        AssetDatabase.SaveAssets();
    }

    private void RebuildPoolTowers()
    {
        foreach (var pool in _data.pools) RebuildPoolTower(pool);
    }

    private static string PoolTowerMeshName(LevelSelectDesignerData.DesignerPool pool)
        => $"PoolTower_{SanitiseAssetName(pool.nodeId)}";

    // ─────────────────────────────────────────────
    // ARENA CENTRE TOWER
    // ─────────────────────────────────────────────

    private const string ArenaCentreTowerChild = "CentreTower";

    /// <summary>The Centre Tower toggle and sizes for the selected arena, under its Wall.</summary>
    private void DrawArenaCentreTowerFields(LevelSelectDesignerData.DesignerArena arena)
    {
        EditorGUI.BeginChangeCheck();
        bool hasTower = EditorGUILayout.Toggle(
            new GUIContent("Centre Tower", "Stand a lollipop tower in the middle of the arena, its base on the water."),
            arena.hasCentreTower);

        LollipopTower tower = arena.centreTower;
        float         scale = arena.centreTowerScale;
        if (hasTower)
        {
            DrawLollipopPresetRow(arena.centreTowerPreset, arena.centreTower,
                                  p => arena.centreTowerPreset = p, t => arena.centreTower = t);
            tower = arena.centreTower;   // read back: picking a preset just loaded it
            scale = DrawLollipopScaleField(arena.centreTowerScale);
        }

        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(_data, "Edit Arena Centre Tower");
        arena.hasCentreTower   = hasTower;
        arena.centreTower      = tower;
        arena.centreTowerScale = scale;
        MarkDirty();
        RebuildAfterTowerEdit(arena, () => RebuildArenaCentreTower(arena));
    }

    /// <summary>
    /// Builds, rebuilds or clears one arena's centre tower, as a child of its generated wall. The
    /// wall's origin is the arena centre on the water surface, so that is where the tower's base
    /// stands. Does nothing when the wall has not been generated.
    /// </summary>
    private void RebuildArenaCentreTower(LevelSelectDesignerData.DesignerArena arena)
    {
        if (arena == null || string.IsNullOrEmpty(arena.nodeId)) return;

        var wall = FindObjectsOfType<LevelSelectArenaWallMesh>().FirstOrDefault(w => w.nodeId == arena.nodeId);
        if (wall == null) return;

        Transform existing = wall.transform.Find(ArenaCentreTowerChild);
        if (!arena.hasCentreTower || arena.centreTower == null)
        {
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            return;
        }

        // Standing on the water, the rivers' rim top — what the waterline shading is placed off —
        // is the water's depth above its base.
        var placed = arena.centreTower.Scaled(arena.centreTowerScale);
        var mesh   = SaveGeneratedMesh(ArenaCentreTowerMeshName(arena),
                                       placed.Build(StoneShadingForBuild, BoatSplineDrop));
        if (mesh == null) return;

        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(ArenaCentreTowerChild);
            Undo.RegisterCreatedObjectUndo(go, "Generate Arena Centre Tower");
            go.transform.SetParent(wall.transform, false);
        }

        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;
        SetTurretPiece(go, mesh, _data.riverMaterial);

        AssetDatabase.SaveAssets();
    }

    private static string ArenaCentreTowerMeshName(LevelSelectDesignerData.DesignerArena arena)
        => $"ArenaCentreTower_{SanitiseAssetName(arena.nodeId)}";

    private const string TurretHeadChild = "Head";
    private const string TurretNoseChild = "Nose";

    /// <summary>
    /// Builds a turret under <paramref name="parent"/>, base at its origin: the body (base and
    /// stem) on the root, a "Head" child on the orb's centre carrying the orb, and a "Nose" child
    /// of the head on the same centre, tilted up by the rest pitch. The root carries a
    /// <see cref="TurretTowerController"/> wired to both, with the rest pitch it was built at.
    ///
    /// <paramref name="reuse"/>, when given, is a turret built before — its pieces are refilled
    /// rather than made again. Meshes are saved as <paramref name="name"/>, name_Head and
    /// name_Nose. The caller places the root.
    /// </summary>
    private GameObject PlaceTurret(Transform parent, string name, TurretTower placed, Material material,
                                   GameObject reuse = null)
    {
        var bodyMesh = SaveGeneratedMesh(name,           placed.BuildBody(StoneShadingForBuild));
        var headMesh = SaveGeneratedMesh(name + "_Head", placed.BuildHead(StoneShadingForBuild));
        var noseMesh = SaveGeneratedMesh(name + "_Nose", placed.BuildNose(StoneShadingForBuild));
        if (bodyMesh == null || headMesh == null || noseMesh == null) return null;

        GameObject go = reuse;
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Generate Turret Tower");
            go.transform.SetParent(parent, false);
        }
        SetTurretPiece(go, bodyMesh, material);

        Transform head = TurretChild(go.transform, TurretHeadChild);
        head.localPosition = Vector3.up * placed.OrbCentreY;
        head.localRotation = Quaternion.identity;
        head.localScale    = Vector3.one;
        SetTurretPiece(head.gameObject, headMesh, material);

        Transform nose = TurretChild(head, TurretNoseChild);
        nose.localPosition = Vector3.zero;
        nose.localRotation = Quaternion.Euler(-placed.restPitch, 0f, 0f);   // up is a negative turn about x
        nose.localScale    = Vector3.one;
        SetTurretPiece(nose.gameObject, noseMesh, material);

        var controller = go.GetComponent<TurretTowerController>();
        if (controller == null) controller = Undo.AddComponent<TurretTowerController>(go);
        controller.head = head;
        controller.nose = nose;
        controller.restPitch = placed.restPitch;
        EditorUtility.SetDirty(controller);

        return go;
    }

    private const string SoulJuiceTankBowlChild  = "Bowl";
    private const string SoulJuiceTankWaterChild = "Water";

    private const string SoulJuiceTankIntakeChild = "Intake";

    /// <summary>
    /// Builds a soul juice tank under <paramref name="parent"/>, base at its origin: the body
    /// (lollipop, both hubs and the tap) on the root. The intake is an "Intake" child laid by
    /// <see cref="RebuildTankIntake"/> once the root is placed, since it reaches out to a pipe.
    /// The root carries a <see cref="SoulJuiceTankBowl"/> with no water, so the runtime knows
    /// which pool the tank stands in.
    ///
    /// <paramref name="reuse"/>, when given, is one built before — its pieces are refilled. The
    /// body mesh is saved as <paramref name="name"/>. The caller places the root.
    /// </summary>
    private GameObject PlaceSoulJuiceTank(Transform parent, string name, string poolNodeId, SoulJuiceTank placed,
                                          GameObject reuse = null)
    {
        var bodyMesh = SaveGeneratedMesh(name, placed.BuildBody(StoneShadingForBuild));
        if (bodyMesh == null) return null;

        GameObject go = reuse;
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Generate Soul Juice Tank");
            go.transform.SetParent(parent, false);
        }
        SetTurretPiece(go, bodyMesh, _data.riverMaterial);

        // Which pool the tank stands in, for the runtime to read. No bowl, so no water.
        var marker = go.GetComponent<SoulJuiceTankBowl>();
        if (marker == null) marker = Undo.AddComponent<SoulJuiceTankBowl>(go);
        marker.poolNodeId  = poolNodeId;
        marker.water       = null;
        marker.waterRadius = 0f;
        marker.innerRadius = 0f;
        marker.basinDepth  = 0f;
        marker.waterDrop   = 0f;
        marker.emptyDrop   = 0f;
        EditorUtility.SetDirty(marker);

        return go;
    }

    /// <summary>
    /// Lays, refills or clears a soul juice tank's intake: from its top hub up to the ring of the
    /// pipe hub it is linked to. Needs both the tank and the pipe's hub; with no link, or a hub
    /// that is gone, the intake is taken away. Built in the tank's own space, so it moves with it.
    /// </summary>
    private void RebuildTankIntake(LevelSelectDesignerData.DesignerPool pool)
    {
        if (pool == null) return;
        var tank = FindGeneratedPoolTower(pool);
        if (tank == null || tank.GetComponent<SoulJuiceTankBowl>() == null) return;

        Transform existing = tank.Find(SoulJuiceTankIntakeChild);
        PipeMesh.Hub hub = default;
        bool wanted = pool.hasTower && pool.towerKind == LevelSelectDesignerData.TowerKind.SoulJuiceTank &&
                      pool.soulJuiceTank != null &&
                      FindPipeHub(pool.tankHubId, out var pipe, out int node) &&
                      TryPipeHubWorld(pipe, node, out hub);
        if (!wanted)
        {
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            return;
        }

        var     placed = pool.soulJuiceTank.Scaled(pool.towerScale);
        Vector3 face   = tank.InverseTransformPoint(hub.RingFace);
        Vector3 dir    = tank.InverseTransformDirection(hub.ringDir);
        var mesh = SaveGeneratedMesh(tank.name + "_Intake",
                                     placed.BuildIntake(face, dir, hub.ringThickness, StoneShadingForBuild));
        if (mesh == null) return;

        Transform intake = TurretChild(tank, SoulJuiceTankIntakeChild);
        intake.localPosition = Vector3.zero;
        intake.localRotation = Quaternion.identity;
        intake.localScale    = Vector3.one;
        SetTurretPiece(intake.gameObject, mesh, _data.riverMaterial);
    }

    /// <summary>The intake of every soul juice tank linked to a hub, laid again.</summary>
    private void RebuildTankIntakes()
    {
        foreach (var pool in _data.pools)
            if (pool != null && !string.IsNullOrEmpty(pool.tankHubId)) RebuildTankIntake(pool);
    }

    /// <summary>The generated tower standing on <paramref name="pool"/>'s island, or null.</summary>
    private Transform FindGeneratedPoolTower(LevelSelectDesignerData.DesignerPool pool)
    {
        string poolName = PoolMeshName(pool);
        var record = FindObjectsOfType<RiverPoolMesh>().FirstOrDefault(r => r.meshAssetName == poolName);
        if (record == null) return null;
        Transform poolRoot = record.transform.parent != null ? record.transform.parent : record.transform;
        return poolRoot.Find(PoolTowerMeshName(pool));
    }

    /// <summary>
    /// Builds a soul fish bowl under <paramref name="parent"/>, base at its origin: the body
    /// (base tiers and column) on the root, a "Bowl" child carrying the bowl, and a "Water" child
    /// of the bowl at the bowl's water surface. The water is a pool's own water, built by the same
    /// builder as a bowl with no island and no rivers, so it looks and moves like the rest. It
    /// sits the bowl's own Water Drop below its rim — the world's Water Level is far deeper than
    /// any bowl. This was the soul juice tank until 2026-10-26; it starts no routes, so it has no
    /// <see cref="SoulJuiceTankBowl"/> and nothing drains it yet.
    ///
    /// <paramref name="reuse"/>, when given, is one built before — its pieces are refilled. Meshes
    /// are saved as <paramref name="name"/>, name_Bowl and name_Water. The caller places the root.
    /// </summary>
    private GameObject PlaceSoulFishBowl(Transform parent, string name, SoulFishBowl placed,
                                         RiverProfile profile, GameObject reuse = null)
    {
        var bodyMesh = SaveGeneratedMesh(name,           placed.BuildBody(StoneShadingForBuild));
        var bowlMesh = SaveGeneratedMesh(name + "_Bowl", placed.BuildBowl(_data.WaterSurfaceDrop, StoneShadingForBuild));

        // Fine enough to go round even a small bowl; the pool water's own grid is laid off this.
        float drop  = Mathf.Max(0f, placed.waterDrop);
        float inner = placed.InnerRadius;
        float edge  = Mathf.Min(MeshEdge, 2f * Mathf.PI * inner / 32f);
        var waterMesh = SaveGeneratedMesh(name + "_Water",
            RiverMeshBuilder.BuildPoolWater(profile, drop, inner, 0f, edge, null, placed.BasinDepth));

        if (bodyMesh == null || bowlMesh == null || waterMesh == null) return null;

        GameObject go = reuse;
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Generate Soul Fish Bowl");
            go.transform.SetParent(parent, false);
        }
        SetTurretPiece(go, bodyMesh, _data.riverMaterial);

        // The bowl mesh is built in the tower's own space, so it sits at the root's origin.
        Transform bowl = TurretChild(go.transform, SoulJuiceTankBowlChild);
        bowl.localPosition = Vector3.zero;
        bowl.localRotation = Quaternion.identity;
        bowl.localScale    = Vector3.one;
        SetTurretPiece(bowl.gameObject, bowlMesh, _data.riverMaterial);

        // The pool water builder works down from a rim at y = 0, so the water's origin is the rim.
        Transform water = TurretChild(bowl, SoulJuiceTankWaterChild);
        water.localPosition = Vector3.up * placed.RimTopY;
        water.localRotation = Quaternion.identity;
        water.localScale    = Vector3.one;
        SetTurretPiece(water.gameObject, waterMesh, _data.waterMaterial);
        SetWaterSorting(water.gameObject, PoolWaterSortingLayer, 0);

        // Deliberately not made sampleable: the boat reads the water under it by raycast, and a
        // bowl held up over the island must never be taken for the river.
        return go;
    }

    /// <summary>
    /// Press E at the tank to release its soul juice: an interact point and a
    /// <see cref="SoulJuiceTankRelease"/> beside it. Added once — the prompt and reach are only
    /// set when the point is first made, so anything tuned on the component survives Generate.
    /// The reach starts past the island's edge, since the boat can only get as near as the water.
    /// The pool's Cinematic, if any, is written onto the release every time.
    /// </summary>
    private static void AddSoulJuiceTankRelease(GameObject tower, float islandRadius, CameraSequencePreset cinematic)
    {
        var point = tower.GetComponent<LevelSelectInteractPoint>();
        if (point == null)
        {
            point = Undo.AddComponent<LevelSelectInteractPoint>(tower);
            point.prompt = "Release the soul juice";
            point.radius = islandRadius + 3f;
            EditorUtility.SetDirty(point);
        }

        var release = tower.GetComponent<SoulJuiceTankRelease>();
        if (release == null) release = Undo.AddComponent<SoulJuiceTankRelease>(tower);
        release.cinematic = cinematic;
        EditorUtility.SetDirty(release);
    }

    /// <summary>The pool's generated tower in the scene, or null when it has not been generated.</summary>
    private static GameObject FindPoolTower(LevelSelectDesignerData.DesignerPool pool)
    {
        if (pool == null) return null;
        string poolName = PoolMeshName(pool);
        var record = FindObjectsOfType<RiverPoolMesh>().FirstOrDefault(r => r.meshAssetName == poolName);
        if (record == null) return null;
        Transform poolRoot = record.transform.parent != null ? record.transform.parent : record.transform;
        Transform tower    = poolRoot.Find(PoolTowerMeshName(pool));
        return tower != null ? tower.gameObject : null;
    }

    private static Transform TurretChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Generate Turret Tower");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static void SetTurretPiece(GameObject go, Mesh mesh, Material material)
    {
        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        EditorUtility.SetDirty(filter);

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        EditorUtility.SetDirty(renderer);
    }

    // ─────────────────────────────────────────────
    // CANVAS
    // ─────────────────────────────────────────────

    /// <summary>A solid dot the size of the orb in the middle of every pool that will get a tower.</summary>
    private void DrawPoolTowers()
    {
        if (Event.current.type != EventType.Repaint) return;

        foreach (var pool in _data.pools)
        {
            if (pool == null || !pool.hasTower) continue;
            if (_data.PoolShapeFor(pool).islandRadius <= LevelSelectDesignerData.PoolTowerMinIsland) continue;

            var node = _data.nodes.Find(n => n.id == pool.nodeId);
            if (node == null) continue;

            // The perch range washed underneath, the same as a spike's. A soul juice tank or fish bowl has no perch.
            var perch = pool.towerPerch;
            if (pool.towerKind != LevelSelectDesignerData.TowerKind.SoulJuiceTank &&
                pool.towerKind != LevelSelectDesignerData.TowerKind.SoulFishBowl &&
                perch != null && perch.enabled && perch.perchRadius > 0f &&
                LevelSelectDesignerSettings.ShowPerchRadius)
            {
                Handles.color = new Color(1f, 0.93f, 0.55f, pool.nodeId == _selectedPoolNodeId ? 0.16f : 0.08f);
                Handles.DrawSolidDisc(WorldToCanvas(node.worldPosition), Vector3.forward,
                                      perch.perchRadius * PerchCanvasScale);
            }

            // A NomKabarTree is drawn as it is seen from above, the same as a freestanding one.
            if (pool.towerKind == LevelSelectDesignerData.TowerKind.NomKabarTree)
            {
                DrawNomKabarTreeShape(node.worldPosition, PlacedNomKabarTree(pool.nomKabarTree, pool.towerScale),
                                      pool.nomKabarTreeTurn, pool.nodeId == _selectedPoolNodeId);
                continue;
            }

            float orb = PoolTowerCanvasRadius(pool);
            Handles.color = Faded(pool.nodeId == _selectedPoolNodeId ? Color.white : LevelSelectDesignerSettings.RiverColour(pool.editorColor));
            Handles.DrawSolidDisc(WorldToCanvas(node.worldPosition), Vector3.forward,
                                  Mathf.Max(2f, orb * _zoom));

            // A soul fish bowl shows its water as a dark fill inside the bowl, so it reads apart from
            // the solid towers at a glance.
            if (pool.towerKind == LevelSelectDesignerData.TowerKind.SoulFishBowl && pool.soulFishBowl != null)
            {
                var placed = pool.soulFishBowl.Scaled(pool.towerScale);
                Handles.color = Faded(new Color(0.05f, 0.1f, 0.2f, 1f));
                Handles.DrawSolidDisc(WorldToCanvas(node.worldPosition), Vector3.forward,
                                      Mathf.Max(1f, placed.WaterRadius * _zoom));
            }
        }
    }

    /// <summary>A solid dot the size of the orb in the middle of every arena with a centre tower.</summary>
    private void DrawArenaCentreTowers()
    {
        if (Event.current.type != EventType.Repaint) return;

        foreach (var arena in _data.arenas)
        {
            if (arena == null || !arena.hasCentreTower || arena.centreTower == null) continue;
            if (_data.nodes.Find(n => n.id == arena.nodeId) == null) continue;

            float orb = arena.centreTower.Scaled(arena.centreTowerScale).orbRadius;
            Handles.color = Faded(arena.nodeId == _selectedArenaNodeId ? Color.white : new Color(0.28f, 0.27f, 0.25f, 1f));
            Handles.DrawSolidDisc(WorldToCanvas(GetTrueArenaCenter(arena)), Vector3.forward,
                                  Mathf.Max(2f, orb * _zoom));
        }
    }
}

#endif
