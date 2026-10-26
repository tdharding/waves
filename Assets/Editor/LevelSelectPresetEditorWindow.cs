using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Where a Level Select Designer preset is edited — an installation outpost, a lollipop tower, a
/// turret or a soul juice tank. The designer's preset rows only pick a preset (Edit / New open
/// this); every one of its settings lives here.
///
/// Editing a preset changes every placed thing that uses it: each edit is handed to the open
/// designer through <see cref="PresetEdited"/>, which puts the preset's settings back on them and
/// rebuilds them after the usual tower pause. One window per preset, so an outpost preset and
/// the tower preset it carries can be open side by side.
/// </summary>
public class LevelSelectPresetEditorWindow : EditorWindow
{
    /// <summary>Raised after every edit made here, with the preset that changed.</summary>
    public static event System.Action<ScriptableObject> PresetEdited;

    [SerializeField] private ScriptableObject _preset;
    private Vector2 _scroll;

    /// <summary>Opens the window editing <paramref name="preset"/>, or brings forward the one already on it.</summary>
    public static void Open(ScriptableObject preset)
    {
        if (preset == null) return;

        var window = Resources.FindObjectsOfTypeAll<LevelSelectPresetEditorWindow>()
                              .FirstOrDefault(w => w._preset == preset);
        if (window == null)
        {
            window = CreateInstance<LevelSelectPresetEditorWindow>();
            window._preset = preset;
            window.minSize = new Vector2(300f, 260f);
        }
        window.titleContent = new GUIContent(preset.name);
        window.Show();
        window.Focus();
    }

    private void OnEnable()  => Undo.undoRedoPerformed += Repaint;
    private void OnDisable() => Undo.undoRedoPerformed -= Repaint;

    private void OnGUI()
    {
        if (_preset == null)
        {
            EditorGUILayout.HelpBox("The preset this window was editing is gone.", MessageType.Info);
            return;
        }
        titleContent = new GUIContent(_preset.name);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"{KindName(_preset)} Preset", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField(_preset, typeof(ScriptableObject), false);

        if (!Resources.FindObjectsOfTypeAll<LevelSelectDesignerWindow>().Any())
            EditorGUILayout.HelpBox("The Level Select Designer isn't open — edits are saved to the preset, but " +
                                    "the placed ones using it only change while the designer is open.",
                                    MessageType.Info);

        EditorGUILayout.Space(4);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        switch (_preset)
        {
            case InstallationOutpostPreset p: DrawOutpost(p);   break;
            case LollipopTowerPreset p:       DrawLollipop(p);  break;
            case TurretTowerPreset p:         DrawTurret(p);    break;
            case SoulJuiceTankPreset p:      DrawSoulJuiceTank(p); break;
            case SoulFishBowlPreset p:       DrawSoulFishBowl(p); break;
            case NomKabarTreePreset p:        DrawNomKabarTree(p); break;
            case WallPreset p:                DrawWall(p);      break;
            case BridgePreset p:              DrawBridge(p);    break;
        }

        EditorGUILayout.EndScrollView();
    }

    private static string KindName(ScriptableObject preset) => preset switch
    {
        InstallationOutpostPreset => "Installation Outpost",
        LollipopTowerPreset       => "Lollipop Tower",
        TurretTowerPreset         => "Turret Tower",
        SoulJuiceTankPreset      => "Soul Juice Tank",
        SoulFishBowlPreset       => "Soul Fish Bowl",
        NomKabarTreePreset        => "NomKabar Tree",
        WallPreset                => "Wall",
        BridgePreset              => "Bridge",
        _                         => preset.GetType().Name,
    };

    // Writes an edit into the preset, saves it, and hands it to the designer.
    private void Commit(string undoName, System.Action write)
    {
        Undo.RecordObject(_preset, undoName);
        write();
        EditorUtility.SetDirty(_preset);
        AssetDatabase.SaveAssetIfDirty(_preset);
        PassTowerToOutpostPresets(_preset);
        PresetEdited?.Invoke(_preset);
    }

    // An outpost preset keeps its own copy of its tower preset's sizes, so a tower preset edit
    // is copied into every outpost preset that carries it — whether the designer is open or not.
    private static void PassTowerToOutpostPresets(ScriptableObject edited)
    {
        if (edited is not LollipopTowerPreset && edited is not TurretTowerPreset) return;

        foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(InstallationOutpostPreset)))
        {
            var outpost = AssetDatabase.LoadAssetAtPath<InstallationOutpostPreset>(AssetDatabase.GUIDToAssetPath(guid));
            if (outpost == null) continue;

            if (edited is LollipopTowerPreset lollipop && outpost.towerPreset == lollipop)
            {
                Undo.RecordObject(outpost, "Edit Lollipop Tower Preset");
                outpost.tower = lollipop.Build();
            }
            else if (edited is TurretTowerPreset turret && outpost.turretPreset == turret)
            {
                Undo.RecordObject(outpost, "Edit Turret Tower Preset");
                outpost.turret = turret.Build();
            }
            else continue;

            EditorUtility.SetDirty(outpost);
            AssetDatabase.SaveAssetIfDirty(outpost);
        }
    }

    // The field helpers mark a tower edit for the designer's own change checks; nothing here
    // needs it, so it is taken back after each pass.
    private static void ClearDesignerTowerFlag() => LevelSelectDesignerWindow.s_towerFieldsEdited = false;

    // ─────────────────────────────────────────────
    // TOWERS
    // ─────────────────────────────────────────────

    private void DrawLollipop(LollipopTowerPreset p)
    {
        EditorGUI.BeginChangeCheck();
        var tower = LevelSelectDesignerWindow.DrawLollipopTowerFields(p.tower, onInnerPresetCreated: made =>
            Commit("New Lollipop Tower Preset", () =>
            {
                p.tower ??= new LollipopTower();
                p.tower.innerLollipopPreset = made;
            }));
        ClearDesignerTowerFlag();
        if (EditorGUI.EndChangeCheck())
            Commit("Edit Lollipop Tower Preset", () => p.tower = tower);
    }

    private void DrawTurret(TurretTowerPreset p)
    {
        EditorGUI.BeginChangeCheck();
        var turret = LevelSelectDesignerWindow.DrawTurretTowerFields(p.turret);
        ClearDesignerTowerFlag();
        if (EditorGUI.EndChangeCheck())
            Commit("Edit Turret Tower Preset", () => p.turret = turret);
    }

    private void DrawSoulJuiceTank(SoulJuiceTankPreset p)
    {
        EditorGUI.BeginChangeCheck();
        var tower = LevelSelectDesignerWindow.DrawSoulJuiceTankFields(p.tower);
        ClearDesignerTowerFlag();
        if (EditorGUI.EndChangeCheck())
            Commit("Edit Soul Juice Tank Preset", () => p.tower = tower);
    }

    private void DrawSoulFishBowl(SoulFishBowlPreset p)
    {
        EditorGUI.BeginChangeCheck();
        var tower = LevelSelectDesignerWindow.DrawSoulFishBowlFields(p.tower);
        ClearDesignerTowerFlag();
        if (EditorGUI.EndChangeCheck())
            Commit("Edit Soul Fish Bowl Preset", () => p.tower = tower);
    }

    private void DrawNomKabarTree(NomKabarTreePreset p)
    {
        if (p.name == "Default NomKabarTree")
            EditorGUILayout.LabelField("Every new NomKabarTree starts as this, and a tree's extra arms come from it " +
                                       "when its Arm Count is raised. Trees already placed keep their own settings.",
                                       EditorStyles.wordWrappedMiniLabel);

        EditorGUI.BeginChangeCheck();
        var tree = LevelSelectDesignerWindow.DrawNomKabarTreeFields(p.tree, "Preset:" + AssetDatabase.GetAssetPath(p));
        ClearDesignerTowerFlag();
        if (EditorGUI.EndChangeCheck())
            Commit("Edit NomKabar Tree Preset", () => p.tree = tree);
    }

    // ─────────────────────────────────────────────
    // WALL
    // ─────────────────────────────────────────────

    private void DrawWall(WallPreset p)
    {
        EditorGUI.BeginChangeCheck();
        var wall = LevelSelectDesignerWindow.DrawWallSettingsFields(p.wall);
        if (EditorGUI.EndChangeCheck())
            Commit("Edit Wall Preset", () => p.wall = wall);
    }

    // ─────────────────────────────────────────────
    // BRIDGE
    // ─────────────────────────────────────────────

    private void DrawBridge(BridgePreset p)
    {
        EditorGUI.BeginChangeCheck();
        var bridge = LevelSelectDesignerWindow.DrawBridgeSettingsFields(p.bridge);
        if (EditorGUI.EndChangeCheck())
            Commit("Edit Bridge Preset", () => p.bridge = bridge);
    }

    // ─────────────────────────────────────────────
    // OUTPOST
    // ─────────────────────────────────────────────

    private void DrawOutpost(InstallationOutpostPreset p)
    {
        EditorGUILayout.LabelField("On a freestanding structure the size comes from the structure, and the " +
                                   "Downstream / Upstream columns are its Right / Left.",
                                   EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(2);

        EditorGUI.BeginChangeCheck();
        float width = EditorGUILayout.FloatField(
            new GUIContent("Width", "How far it runs along the river."), p.width);
        float depth = EditorGUILayout.FloatField(
            new GUIContent("Depth", "How far it reaches away from the river, out from the run's outer edge."), p.depth);
        float height = EditorGUILayout.FloatField(
            new GUIContent("Height", "How far its floor stands above the rim top of the river run."), p.height);
        float wallThickness = EditorGUILayout.FloatField(
            new GUIContent("Wall Thickness", "Thickness of the wall around the top."), p.wallThickness);
        float wallHeight = EditorGUILayout.FloatField(
            new GUIContent("Wall Height", "How far the wall around the top stands above the floor."), p.wallHeight);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Tower", EditorStyles.miniBoldLabel);
        var towerKind = LevelSelectDesignerWindow.DrawTowerKindField(p.towerKind);
        bool columnDownstream  = p.columnDownstream;
        bool towerOnDownstream = p.towerOnDownstream;
        bool columnUpstream    = p.columnUpstream;
        bool towerOnUpstream   = p.towerOnUpstream;
        LevelSelectDesignerWindow.DrawOutpostColumnRow("Downstream Column", "down", ref columnDownstream, ref towerOnDownstream);
        LevelSelectDesignerWindow.DrawOutpostColumnRow("Upstream Column",   "up",   ref columnUpstream,   ref towerOnUpstream);

        float columnHeight = p.columnHeight;
        float columnRadius = p.columnRadius;
        float offsetAlong  = p.towerOffset.x;
        float offsetAway   = p.towerOffset.y;
        if (columnDownstream || columnUpstream)
        {
            columnHeight = EditorGUILayout.FloatField(
                new GUIContent("Column Height", "How far the columns' tops stand above the floor."), p.columnHeight);
            columnRadius = EditorGUILayout.FloatField(
                new GUIContent("Column Radius", "Radius of the columns, centred in the wall's corner."), p.columnRadius);
        }
        // The floor tower only stands when no column carries one.
        if (!((columnDownstream && towerOnDownstream) || (columnUpstream && towerOnUpstream)))
        {
            offsetAlong = EditorGUILayout.FloatField(
                new GUIContent("Offset Along", "Moves the tower along the river from the floor's centre."),
                p.towerOffset.x);
            offsetAway = EditorGUILayout.FloatField(
                new GUIContent("Offset Away", "Moves the tower away from the river from the floor's centre."),
                p.towerOffset.y);
        }

        // The tower's own sizes live in its preset, edited in a window of its own.
        var towerPreset  = p.towerPreset;
        var turretPreset = p.turretPreset;
        var tower        = p.tower;
        var turret       = p.turret;
        if (towerKind == LevelSelectDesignerData.TowerKind.Turret)
        {
            turretPreset = DrawPresetLink(
                new GUIContent("Turret Preset", "The turret standing on this outpost — its sizes are set in the preset."),
                p.turretPreset, LevelSelectDesignerWindow.TurretPresetFolder, "Turret Tower",
                () => { var made = CreateInstance<TurretTowerPreset>(); made.CopyFrom(p.turret); return made; },
                made => Commit("New Turret Tower Preset", () => { p.turretPreset = made; p.turret = made.Build(); }));
            if (turretPreset != p.turretPreset && turretPreset != null) turret = turretPreset.Build();
            if (turretPreset == null) NoPresetNote("turret");
        }
        else
        {
            towerPreset = DrawPresetLink(
                new GUIContent("Tower Preset", "The lollipop tower standing on this outpost — its sizes are set in the preset."),
                p.towerPreset, LevelSelectDesignerWindow.LollipopPresetFolder, "Lollipop Tower",
                () => { var made = CreateInstance<LollipopTowerPreset>(); made.CopyFrom(p.tower); return made; },
                made => Commit("New Lollipop Tower Preset", () => { p.towerPreset = made; p.tower = made.Build(); }));
            if (towerPreset != p.towerPreset && towerPreset != null) tower = towerPreset.Build();
            if (towerPreset == null) NoPresetNote("tower");
        }

        ClearDesignerTowerFlag();

        if (EditorGUI.EndChangeCheck())
            Commit("Edit Outpost Preset", () =>
            {
                p.width             = Mathf.Max(0.01f, width);
                p.depth             = Mathf.Max(0.01f, depth);
                p.height            = Mathf.Max(0f, height);
                p.wallThickness     = Mathf.Max(0f, wallThickness);
                p.wallHeight        = Mathf.Max(0f, wallHeight);
                p.towerKind         = towerKind;
                p.columnDownstream  = columnDownstream;
                p.towerOnDownstream = towerOnDownstream;
                p.columnUpstream    = columnUpstream;
                p.towerOnUpstream   = towerOnUpstream;
                p.columnHeight      = Mathf.Max(0f, columnHeight);
                p.columnRadius      = Mathf.Max(0.001f, columnRadius);
                p.towerOffset       = new Vector2(offsetAlong, offsetAway);
                p.towerPreset       = towerPreset;
                p.turretPreset      = turretPreset;
                p.tower             = tower;
                p.turret            = turret;
            });
    }

    private static void NoPresetNote(string what)
        => EditorGUILayout.LabelField($"No {what} preset — New saves the {what} this preset already has as one.",
                                      EditorStyles.wordWrappedMiniLabel);

    // ─────────────────────────────────────────────
    // THE PRESET ROW
    // ─────────────────────────────────────────────

    /// <summary>
    /// A preset dropdown with Edit and New beside it — the row the designer shows wherever a
    /// preset is chosen. Returns whichever preset the dropdown now shows; the caller loads it.
    /// Edit opens <paramref name="current"/> here. New makes a preset from
    /// <paramref name="makeFromCurrent"/> (called on the click, so it captures the settings as
    /// they are), asks where to save it after this GUI pass, hands it to
    /// <paramref name="onCreated"/> and opens it.
    /// </summary>
    public static T DrawPresetLink<T>(GUIContent label, T current, string folder, string what,
                                      System.Func<T> makeFromCurrent, System.Action<T> onCreated)
        where T : ScriptableObject
    {
        T picked;
        using (new EditorGUILayout.HorizontalScope())
        {
            picked = LevelSelectRiverPresetLibrary.DrawPicker(label, current, folder);

            using (new EditorGUI.DisabledScope(current == null))
                if (GUILayout.Button(new GUIContent("Edit", $"Open this {what.ToLowerInvariant()} preset in its own " +
                                                            "window — every placed one using it changes with it."),
                                     EditorStyles.miniButtonLeft, GUILayout.Width(38f)))
                    Open(current);

            if (GUILayout.Button(new GUIContent("New", $"Save these settings as a new {what.ToLowerInvariant()} " +
                                                       "preset, use it here, and open it."),
                                 EditorStyles.miniButtonRight, GUILayout.Width(38f)))
            {
                var made = makeFromCurrent();
                // Opened after this GUI pass, so the file panel never lands inside a change check.
                EditorApplication.delayCall += () =>
                {
                    string assetPath = EditorUtility.SaveFilePanelInProject(
                        $"New {what} Preset", $"New{what.Replace(" ", "")}Preset", "asset", "Choose save location",
                        LevelSelectRiverPresetLibrary.EnsureFolder(folder));
                    if (string.IsNullOrEmpty(assetPath))
                    {
                        DestroyImmediate(made);
                        return;
                    }

                    AssetDatabase.CreateAsset(made, assetPath);
                    AssetDatabase.SaveAssets();
                    onCreated(made);
                    Open(made);
                };
            }
        }
        return picked;
    }
}
