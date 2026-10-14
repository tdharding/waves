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
        EditorApplication.update -= TowerRebuildTick;
        EditorApplication.update += TowerRebuildTick;
    }

    /// <summary>The tower name for <see cref="RebuildAfterEdit"/>, kept for the callers that use it.</summary>
    private void RebuildAfterTowerEdit(object key, System.Action rebuild) => RebuildAfterEdit(key, rebuild);

    private void TowerRebuildTick()
    {
        if (EditorApplication.timeSinceStartup < _towerRebuildDueAt) return;
        FlushTowerRebuilds();
    }

    /// <summary>Runs every waiting rebuild now, folded into the edit's own undo step.</summary>
    private void FlushTowerRebuilds()
    {
        EditorApplication.update -= TowerRebuildTick;
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
            if (TakePreset(o, preset) | TakePreset(o.bothLeft, preset))
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
            if (changed) rebuilds[s] = () => RebuildStructure(s);
        }

        foreach (var pool in _data.pools)
        {
            if (pool == null) continue;
            var p = pool;
            bool changed = false;
            if (preset is LollipopTowerPreset  pl && p.towerPreset     == pl) { p.tower     = pl.Build(); changed = true; }
            if (preset is TurretTowerPreset    pt && p.turretPreset    == pt) { p.turret    = pt.Build(); changed = true; }
            if (preset is SoulsPoolTowerPreset ps && p.soulsPoolPreset == ps) { p.soulsPool = ps.Build(); changed = true; }
            if (changed) rebuilds[p] = () => RebuildPoolTower(p);
        }

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
    /// inside its own change check.
    /// </summary>
    internal static LollipopTower DrawLollipopTowerFields(LollipopTower tower)
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
    /// <paramref name="allowSoulsPool"/> to offer the Souls Pool tower too — nowhere else can.
    /// </summary>
    internal static LevelSelectDesignerData.TowerKind DrawTowerKindField(LevelSelectDesignerData.TowerKind kind,
                                                                        bool allowSoulsPool = false)
    {
        var label = new GUIContent("Kind", allowSoulsPool
            ? "A lollipop tower, a turret whose head turns and whose nose tilts, or a souls pool — " +
              "a bowl on a column where a soul route's souls begin."
            : "A lollipop tower, or a turret whose head turns and whose nose tilts.");

        string[] names = allowSoulsPool
            ? new[] { "Lollipop", "Turret", "Souls Pool" }
            : new[] { "Lollipop", "Turret" };

        int index = Mathf.Clamp((int)kind, 0, names.Length - 1);
        return (LevelSelectDesignerData.TowerKind)EditorGUILayout.Popup(label, index,
                                                                        System.Array.ConvertAll(names, n => new GUIContent(n)));
    }

    internal const string SoulsPoolPresetFolder = "Assets/ScriptsData/DataScripts/SoulsPoolTowerPresets";

    /// <summary>The same preset row as a lollipop's, for a souls pool tower — its sizes.</summary>
    private bool DrawSoulsPoolPresetRow(SoulsPoolTowerPreset current, SoulsPoolTower tower,
                                        System.Action<SoulsPoolTowerPreset> setPreset,
                                        System.Action<SoulsPoolTower> setTower)
        => DrawTowerPresetRow(current, tower, setPreset, setTower,
                              SoulsPoolPresetFolder, "Souls Pool Preset", "Souls Pool Tower",
                              p => p.Build(), (p, t) => p.CopyFrom(t),
                              t => (t ?? new SoulsPoolTower()).Clone());

    /// <summary>
    /// A souls pool tower's base, column and bowl fields. Hands back an edited copy, so the caller
    /// writes it only inside its own change check.
    /// </summary>
    internal static SoulsPoolTower DrawSoulsPoolTowerFields(SoulsPoolTower tower)
    {
        var edited = (tower ?? new SoulsPoolTower()).Clone();
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
                                          "has drained when the souls are released. 0 = no hole."), edited.holeRadius));

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

        edited.body = DrawLollipopTowerFields(edited.body);

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

    // A souls pool tower is sized for the canvas by its bowl — what is seen from above.
    private static float PoolTowerCanvasRadius(LevelSelectDesignerData.DesignerPool pool)
        => pool.towerKind == LevelSelectDesignerData.TowerKind.SoulsPool
            ? (pool.soulsPool ?? new SoulsPoolTower()).Scaled(pool.towerScale).bowlRadius
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
        SoulsPoolTower souls  = pool.soulsPool;
        float          scale  = pool.towerScale;
        var            perch  = pool.towerPerch;
        if (hasTower)
        {
            if (island <= LevelSelectDesignerData.PoolTowerMinIsland)
                EditorGUILayout.LabelField(
                    "This pool has no island — a tower is only built on one.",
                    EditorStyles.miniLabel);

            kind = DrawTowerKindField(pool.towerKind, allowSoulsPool: true);
            if (kind == LevelSelectDesignerData.TowerKind.SoulsPool)
            {
                DrawSoulsPoolPresetRow(pool.soulsPoolPreset, pool.soulsPool,
                                       p => pool.soulsPoolPreset = p, t => pool.soulsPool = t);
                souls = pool.soulsPool;   // read back: picking a preset just loaded it
                scale = DrawLollipopScaleField(pool.towerScale);
                DrawSoulsPoolRoutesNote(pool);
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

            // The souls pool's top is water, so there is nothing on it for the angel to land on.
            if (kind != LevelSelectDesignerData.TowerKind.SoulsPool)
                perch = DrawPerchBlock(pool.towerPerch);
        }

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Pool Tower");
            pool.hasTower   = hasTower;
            pool.towerKind  = kind;
            pool.tower      = tower;
            pool.turret     = turret;
            pool.soulsPool  = souls;
            pool.towerScale = scale;
            pool.towerPerch = perch;
            MarkDirty();
            RebuildAfterTowerEdit(pool, () => RebuildPoolTower(pool));
        }

        // Outside the tower's change check — the cinematic is written onto the tower, never rebuilds it.
        if (pool.hasTower && pool.towerKind == LevelSelectDesignerData.TowerKind.SoulsPool)
            DrawTowerCinematic(pool);
    }

    /// <summary>Which soul routes begin at this souls pool tower — the souls in its bowl.</summary>
    private void DrawSoulsPoolRoutesNote(LevelSelectDesignerData.DesignerPool pool)
    {
        var starting = _data.soulRoutes.FindAll(r => r != null && r.originNodeId == pool.nodeId);
        if (starting.Count == 0)
        {
            EditorGUILayout.LabelField("No soul route starts here yet — pick this tower as a route's origin " +
                                       "in Souls mode.", EditorStyles.wordWrappedMiniLabel);
            return;
        }

        foreach (var route in starting)
            EditorGUILayout.LabelField($"Route '{route.displayName}' starts here — {CountAssigned(route)} soul(s).",
                                       EditorStyles.miniLabel);
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
        bool isSouls  = pool.towerKind == LevelSelectDesignerData.TowerKind.SoulsPool;
        bool wanted   = pool.hasTower &&
                        (isSouls ? pool.soulsPool != null : isTurret ? pool.turret != null : pool.tower != null) &&
                        record.islandRadius > LevelSelectDesignerData.PoolTowerMinIsland;

        // A tower of another kind is cleared rather than reused — their pieces differ.
        if (existing != null &&
            (!wanted ||
             (existing.GetComponent<TurretTowerController>() != null) != isTurret ||
             (existing.Find(SoulsPoolBowlChild) != null) != isSouls))
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
            existing = null;
        }
        if (!wanted) return;

        if (isSouls)
        {
            var built = PlaceSoulsPoolTower(poolRoot, towerName, pool.nodeId, pool.soulsPool.Scaled(pool.towerScale),
                                            _data.ProfileFor(_data.PoolRiverName(pool)), existing?.gameObject);
            if (built == null) return;

            // The island top is flush with the rim, which is the pool's origin.
            built.transform.localPosition = Vector3.zero;
            built.transform.localRotation = Quaternion.identity;
            built.transform.localScale    = Vector3.one;

            AddSoulsPoolRelease(built, record.islandRadius,
                                HasTowerCinematic(pool) ? pool.towerCinematicPreset : null);

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

        AssetDatabase.SaveAssets();
    }

    private void RebuildPoolTowers()
    {
        foreach (var pool in _data.pools) RebuildPoolTower(pool);
    }

    private static string PoolTowerMeshName(LevelSelectDesignerData.DesignerPool pool)
        => $"PoolTower_{SanitiseAssetName(pool.nodeId)}";

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

    private const string SoulsPoolBowlChild  = "Bowl";
    private const string SoulsPoolWaterChild = "Water";

    /// <summary>
    /// Builds a souls pool tower under <paramref name="parent"/>, base at its origin: the body
    /// (base tiers and column) on the root, a "Bowl" child carrying the bowl, and a "Water" child
    /// of the bowl at the bowl's water surface. The water is a pool's own water, built by the same
    /// builder as a bowl with no island and no rivers, so it looks and moves like the rest and the
    /// souls stamped on it draw the way they do on the river. It sits the tower's own Water Drop
    /// below the bowl's rim — the world's Water Level is far deeper than any bowl.
    ///
    /// <paramref name="reuse"/>, when given, is one built before — its pieces are refilled. Meshes
    /// are saved as <paramref name="name"/>, name_Bowl and name_Water. The caller places the root.
    /// </summary>
    private GameObject PlaceSoulsPoolTower(Transform parent, string name, string poolNodeId, SoulsPoolTower placed,
                                           RiverProfile profile, GameObject reuse = null)
    {
        var bodyMesh = SaveGeneratedMesh(name,           placed.BuildBody(StoneShadingForBuild));
        var bowlMesh = SaveGeneratedMesh(name + "_Bowl", placed.BuildBowl(_data.WaterSurfaceDrop));

        // Fine enough to go round even a small bowl; the pool water's own grid is laid off this.
        float drop  = Mathf.Max(0f, placed.waterDrop);
        float inner = placed.InnerRadius;
        float edge  = Mathf.Min(MeshEdge, 2f * Mathf.PI * inner / 32f);
        var waterMesh = SaveGeneratedMesh(name + "_Water",
            RiverMeshBuilder.BuildPoolWater(profile, drop, inner, 0f, edge, null, placed.BasinDepth, 0f));

        if (bodyMesh == null || bowlMesh == null || waterMesh == null) return null;

        GameObject go = reuse;
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Generate Souls Pool Tower");
            go.transform.SetParent(parent, false);
        }
        SetTurretPiece(go, bodyMesh, _data.riverMaterial);

        // The bowl mesh is built in the tower's own space, so it sits at the root's origin.
        Transform bowl = TurretChild(go.transform, SoulsPoolBowlChild);
        bowl.localPosition = Vector3.zero;
        bowl.localRotation = Quaternion.identity;
        bowl.localScale    = Vector3.one;
        SetTurretPiece(bowl.gameObject, bowlMesh, _data.riverMaterial);

        // The pool water builder works down from a rim at y = 0, so the water's origin is the rim.
        Transform water = TurretChild(bowl, SoulsPoolWaterChild);
        water.localPosition = Vector3.up * placed.RimTopY;
        water.localRotation = Quaternion.identity;
        water.localScale    = Vector3.one;
        SetTurretPiece(water.gameObject, waterMesh, _data.waterMaterial);
        SetWaterSorting(water.gameObject, PoolWaterSortingLayer, 0);

        // Where the souls waiting in this tower swim, for the runtime to read.
        var marker = go.GetComponent<SoulsPoolTowerBowl>();
        if (marker == null) marker = Undo.AddComponent<SoulsPoolTowerBowl>(go);
        marker.poolNodeId  = poolNodeId;
        marker.water       = water;
        marker.waterRadius = placed.WaterRadius;
        marker.innerRadius = placed.InnerRadius;
        marker.basinDepth  = placed.BasinDepth;
        marker.waterDrop   = drop;
        marker.emptyDrop   = Mathf.Max(drop, placed.EmptyDrop);
        EditorUtility.SetDirty(marker);

        // Deliberately not made sampleable: the boat reads the water under it by raycast, and a
        // bowl held up over the island must never be taken for the river.
        return go;
    }

    /// <summary>
    /// Press E at the tower to let its souls out: an interact point and a
    /// <see cref="SoulsPoolTowerRelease"/> beside it. Added once — the prompt and reach are only
    /// set when the point is first made, so anything tuned on the component survives Generate.
    /// The reach starts past the island's edge, since the boat can only get as near as the water.
    /// The pool's Cinematic, if any, is written onto the release every time.
    /// </summary>
    private static void AddSoulsPoolRelease(GameObject tower, float islandRadius, CameraSequencePreset cinematic)
    {
        var point = tower.GetComponent<LevelSelectInteractPoint>();
        if (point == null)
        {
            point = Undo.AddComponent<LevelSelectInteractPoint>(tower);
            point.prompt = "Release the souls";
            point.radius = islandRadius + 3f;
            EditorUtility.SetDirty(point);
        }

        var release = tower.GetComponent<SoulsPoolTowerRelease>();
        if (release == null) release = Undo.AddComponent<SoulsPoolTowerRelease>(tower);
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

            float orb = PoolTowerCanvasRadius(pool);
            Handles.color = pool.nodeId == _selectedPoolNodeId ? Color.white : LevelSelectDesignerSettings.RiverColour(pool.editorColor);
            Handles.DrawSolidDisc(WorldToCanvas(node.worldPosition), Vector3.forward,
                                  Mathf.Max(2f, orb * _zoom));

            // A souls pool shows its water as a dark fill inside the bowl, so it reads apart from
            // the solid towers at a glance.
            if (pool.towerKind == LevelSelectDesignerData.TowerKind.SoulsPool && pool.soulsPool != null)
            {
                var placed = pool.soulsPool.Scaled(pool.towerScale);
                Handles.color = new Color(0.05f, 0.1f, 0.2f, 1f);
                Handles.DrawSolidDisc(WorldToCanvas(node.worldPosition), Vector3.forward,
                                      Mathf.Max(1f, placed.WaterRadius * _zoom));
            }
        }
    }
}

#endif
