using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Travel paths first, then the character who walks each. A path is points clicked in the Scene
/// view, saved as its own file, that its character walks to in turn — standing a while at each,
/// then back to the first or back and forth. One character per path; the path is the only thing
/// that moves it.
///
/// Characters are the level's, kept in its NPC Character Registry file: a name and a body — one of
/// the prefabs in the NPC prefabs folder that carry an ObserverManNPCController, shared by as many
/// characters as like. A path picks one to walk it, and Place in Scene puts its body down. Clicks land on what is drawn (the Scene View Navigation hit), and a character's feet — its
/// PrefabBaselineAlignment disc — go on the clicked point.
/// </summary>
public class NPCPathToolWindow : EditorWindow
{
    private const string PrefabFolder   = "Assets/Prefab/NPCPrefabs";
    private const string PathFolder     = "Assets/ScriptsData/DataScripts/NPCPaths";
    private const string RegistryFolder = "Assets/ScriptsData/DataScripts/NPCCharacters";
    private const string CharacterField = "characterId";
    private const string PathField      = "points";
    private const string WalkField      = "walk";
    private const string GroundField    = "ground";
    private const string PauseField     = "pauses";
    private const string PathFileField  = "travelPath";
    private const string RotPer90Field  = "rotSecondsPer90";
    private const string WalkSpeedField = "walkSpeed";

    private const string HeightKey        = "NPCPathTool.Height";
    private const string MeasureKey       = "NPCPathTool.Measure";
    private const string LineWidthKey     = "NPCPathTool.LineWidth";
    private const string PointSizeKey     = "NPCPathTool.PointSize";
    private const string PathColourKey    = "NPCPathTool.PathColour";
    private const string MeasureColourKey = "NPCPathTool.MeasureColour";

    private enum ClickMode { None, Place, TravelPath }

    // What a Travel Path click does: add to the end, add after a point, or move a point.
    private enum PointAction { Append, Insert, Redraw }

    // The path being worked on — kept across script reloads.
    [SerializeField] private NPCTravelPath _path;

    // The character picked to walk the path who isn't in the scene yet — Place in Scene puts them down.
    [SerializeField] private string _pendingId;

    // The active scene's characters, and the scene they were loaded for.
    private NPCCharacterRegistry _registry;
    private string               _registryScene;
    private string               _newName = "";
    private bool                 _showCharacters = true;

    private readonly List<NPCTravelPath> _pathFiles = new();
    private readonly List<GameObject>    _prefabs   = new();
    private List<ObserverManNPCController> _inScene = new();

    private int         _prefabIndex;
    private ClickMode   _mode;
    private PointAction _pointAction;
    private int         _pointIndex = -1;

    private bool    _showDisplay;
    private float   _height;
    private float   _measure;
    private float   _lineWidth;
    private float   _pointSize;
    private Color   _pathColour;
    private Color   _measureColour;
    private Vector2 _scroll;

    [MenuItem("Tools/Characters/NPC Path Tool", false, 602)]
    private static void Open() => GetWindow<NPCPathToolWindow>("NPC Path Tool");

    private void OnEnable()
    {
        _height     = EditorPrefs.GetFloat(HeightKey, 0.6f);
        _measure    = Mathf.Max(0.001f, EditorPrefs.GetFloat(MeasureKey, 0.6f));
        _lineWidth  = EditorPrefs.GetFloat(LineWidthKey, 4f);
        _pointSize  = EditorPrefs.GetFloat(PointSizeKey, 0.08f);
        _pathColour = ColorUtility.TryParseHtmlString("#" + EditorPrefs.GetString(PathColourKey, "FFC400FF"), out var c)
                          ? c : new Color(1f, 0.77f, 0f);
        _measureColour = ColorUtility.TryParseHtmlString("#" + EditorPrefs.GetString(MeasureColourKey, "1E88E5FF"), out var m)
                          ? m : new Color(0.12f, 0.53f, 0.9f);

        LoadPrefabs();
        LoadPathFiles();
        FindInScene();
        OnSelectionChange();

        SceneView.duringSceneGui           += OnSceneGUI;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        EditorApplication.projectChanged   += OnProjectChanged;
        Undo.undoRedoPerformed             += Repaint;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui           -= OnSceneGUI;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        EditorApplication.projectChanged   -= OnProjectChanged;
        Undo.undoRedoPerformed             -= Repaint;
        _mode = ClickMode.None;
        SceneView.RepaintAll();
    }

    private void OnProjectChanged()
    {
        LoadPathFiles();
        _registry = null;
        Repaint();
    }

    private void OnHierarchyChanged()
    {
        FindInScene();
        Repaint();
    }

    // Picking a character in the Hierarchy or Scene view brings up the path it walks.
    private void OnSelectionChange()
    {
        var go  = Selection.activeGameObject;
        var npc = go != null ? go.GetComponentInParent<ObserverManNPCController>(true) : null;
        if (npc == null || npc.TravelPath == null || npc.TravelPath == _path) return;
        _path      = npc.TravelPath;
        _pendingId = null;
        SetMode(ClickMode.None);
        Repaint();
        SceneView.RepaintAll();
    }

    private void LoadPrefabs()
    {
        _prefabs.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab != null && prefab.GetComponent<ObserverManNPCController>() != null)
                _prefabs.Add(prefab);
        }
        _prefabIndex = Mathf.Clamp(_prefabIndex, 0, Mathf.Max(0, _prefabs.Count - 1));
    }

    private void LoadPathFiles()
    {
        _pathFiles.Clear();
        foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(NPCTravelPath)}"))
        {
            var file = AssetDatabase.LoadAssetAtPath<NPCTravelPath>(AssetDatabase.GUIDToAssetPath(guid));
            if (file != null) _pathFiles.Add(file);
        }
        _pathFiles.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
    }

    private void FindInScene()
    {
        _inScene = FindObjectsByType<ObserverManNPCController>(FindObjectsInactive.Include)
                       .Where(n => !EditorUtility.IsPersistent(n))
                       .OrderBy(Label)
                       .ToList();
        foreach (var npc in _inScene)
        {
            MoveScenePathToFile(npc);
            AddToRegistry(npc);
        }
    }

    // Grouped by what it stands under — the popup turns "/" into a submenu.
    private static string Label(ObserverManNPCController npc)
    {
        Transform parent = npc.transform.parent;
        return parent != null ? $"{parent.name}/{npc.name}" : npc.name;
    }

    // The one character walking the path, if any.
    private ObserverManNPCController CharacterOn(NPCTravelPath path)
        => path == null ? null : _inScene.FirstOrDefault(n => n != null && n.TravelPath == path);

    // ------------------------------------------------------------------------------------------
    // WINDOW
    // ------------------------------------------------------------------------------------------

    private void OnGUI()
    {
        // Generate in the Level Select Designer can destroy things before the hierarchy-changed
        // refresh comes round — drop them rather than read a destroyed one.
        _inScene.RemoveAll(n => n == null);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUI.BeginChangeCheck();
        _measure = Mathf.Max(0.001f, EditorGUILayout.FloatField(new GUIContent("Measure",
            "The length walk speeds are counted in — the path is drawn in bands this long. " +
            "The same for every path."), _measure));
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetFloat(MeasureKey, _measure);
            SceneView.RepaintAll();
        }
        EditorGUILayout.Space(8);

        DrawTravelPath();
        EditorGUILayout.Space(8);
        DrawCharacter();
        EditorGUILayout.Space(8);
        DrawDisplaySection();

        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------------------------------------
    // TRAVEL PATH
    // ------------------------------------------------------------------------------------------

    private void DrawTravelPath()
    {
        EditorGUILayout.LabelField("Travel Path", EditorStyles.boldLabel);
        DrawPathPicker();

        if (_path == null)
        {
            EditorGUILayout.HelpBox("Pick a path, or make a New Path.", MessageType.None);
            return;
        }

        EditorGUILayout.Space(4);
        bool appending = _mode == ClickMode.TravelPath && _pointAction == PointAction.Append;
        bool editing   = ModeToggle(appending, "Travel Path Tool",
                                    "Click in the Scene view to add a point to the end of the path. Esc to stop.");
        if (editing != appending) SetPointAction(editing ? PointAction.Append : (PointAction?)null, -1);
        if (_mode == ClickMode.TravelPath)
        {
            string say = _pointAction switch
            {
                PointAction.Insert => $"Click in the Scene view to add a point after Point {_pointIndex + 1}.",
                PointAction.Redraw => $"Click in the Scene view to put Point {_pointIndex + 1} there.",
                _                  => "Click in the Scene view to add a point to the end of the path.",
            };
            EditorGUILayout.HelpBox(say, MessageType.None);
        }

        var so     = new SerializedObject(_path);
        var points = so.FindProperty(PathField);
        if (points == null)
        {
            EditorGUILayout.HelpBox($"{nameof(NPCTravelPath)} has no '{PathField}' list.", MessageType.Error);
            return;
        }

        if (points.arraySize == 0)
        {
            EditorGUILayout.HelpBox("No points yet.", MessageType.None);
            return;
        }

        var walk = so.FindProperty(WalkField);
        if (walk != null)
        {
            int was = walk.enumValueIndex;
            int now = GUILayout.Toolbar(was, new[] { "Loop", "Back and Forth", "Static" });
            if (now != was)
            {
                walk.enumValueIndex = now;
                Apply(so);
                StandAtPathStart(CharacterOn(_path));
                SceneView.RepaintAll();
            }
        }

        var pauses = so.FindProperty(PauseField);
        if (pauses != null && pauses.arraySize != points.arraySize)
        {
            SyncPauses(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        int remove = -1;
        EditorGUI.BeginChangeCheck();
        for (int i = 0; i < points.arraySize; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Point {i + 1}", GUILayout.Width(60));
                if (pauses != null && i < pauses.arraySize)
                {
                    var pause   = pauses.GetArrayElementAtIndex(i);
                    var on      = pause.FindPropertyRelative("pause");
                    var seconds = pause.FindPropertyRelative("seconds");
                    on.boolValue = GUILayout.Toggle(on.boolValue, new GUIContent("Pause",
                        "Stands here this many seconds before moving on, counted once any Rot turn is done. " +
                        "0 = walks straight on (with Rot on, it turns, then goes). Off = a random time " +
                        "between the character's Standing min and max."), GUILayout.Width(56));
                    using (new EditorGUI.DisabledScope(!on.boolValue))
                    {
                        seconds.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField(seconds.floatValue, GUILayout.Width(48)));
                        GUILayout.Label("s", GUILayout.Width(12));
                    }

                    // Which way it faces while it stands here.
                    GUILayout.Space(6);
                    var face  = pause.FindPropertyRelative("face");
                    var angle = pause.FindPropertyRelative("faceAngle");
                    face.boolValue = GUILayout.Toggle(face.boolValue, new GUIContent("Rot",
                        "Rotates to face this way while it stands here — degrees clockwise seen from above. " +
                        "The arrow at the point in the Scene view shows it."), GUILayout.Width(40));
                    using (new EditorGUI.DisabledScope(!face.boolValue))
                        angle.floatValue = EditorGUILayout.Slider(Mathf.Repeat(angle.floatValue, 360f), 0f, 360f,
                                                                  GUILayout.MinWidth(120));
                }
                GUILayout.FlexibleSpace();

                bool redrawing = PointActionPending(PointAction.Redraw, i);
                if (GUILayout.Toggle(redrawing, new GUIContent("Redraw",
                        "Click in the Scene view to put this point somewhere else."), "Button", GUILayout.Width(56)) != redrawing)
                    SetPointAction(redrawing ? (PointAction?)null : PointAction.Redraw, i);

                bool inserting = PointActionPending(PointAction.Insert, i);
                if (GUILayout.Toggle(inserting, new GUIContent("+",
                        "Click in the Scene view to add a point between this one and the next."), "Button", GUILayout.Width(24)) != inserting)
                    SetPointAction(inserting ? (PointAction?)null : PointAction.Insert, i);

                if (GUILayout.Button("✕", GUILayout.Width(24))) remove = i;
            }
        }
        if (EditorGUI.EndChangeCheck() && remove < 0)
        {
            Apply(so);
            // A Static path faces point 1's Rot where it stands.
            if (_path.walk == NPCTravelPath.PathWalk.Static) StandAtPathStart(CharacterOn(_path));
            SceneView.RepaintAll();
        }

        if (remove >= 0)
        {
            points.DeleteArrayElementAtIndex(remove);
            if (pauses != null && remove < pauses.arraySize) pauses.DeleteArrayElementAtIndex(remove);
            SyncPauses(so);
            FitToGround(so, GroundHeight());
            Apply(so);
            StandAtPathStart(CharacterOn(_path));
            SceneView.RepaintAll();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("Fit to Ground",
                    "Lays the line between the points over what's drawn under it again — for when the " +
                    "ground has changed since the path was drawn.")))
            {
                FitToGround(so, GroundHeight());
                Apply(so);
                StandAtPathStart(CharacterOn(_path));
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("Clear Path"))
            {
                points.ClearArray();
                SyncPauses(so);
                FitToGround(so, GroundHeight());
                Apply(so);
                SceneView.RepaintAll();
            }
        }
    }

    // The path being worked on: any saved one, or a new one.
    private void DrawPathPicker()
    {
        if (_path != null && !_pathFiles.Contains(_path)) LoadPathFiles();

        var labels = new List<string> { "None" };
        labels.AddRange(_pathFiles.Select(f => f.name));

        int  was     = _path == null ? 0 : _pathFiles.IndexOf(_path) + 1;
        int  now     = was;
        bool newFile = false;
        using (new EditorGUILayout.HorizontalScope())
        {
            now = EditorGUILayout.Popup(new GUIContent("Path", "The path file to work on."), was, labels.ToArray());
            newFile = GUILayout.Button(new GUIContent("New Path", "Makes a new, empty path file."), GUILayout.Width(72));
            using (new EditorGUI.DisabledScope(_path == null))
                if (GUILayout.Button("Show File", GUILayout.Width(72))) EditorGUIUtility.PingObject(_path);
        }

        if (now == was && !newFile) return;
        SetMode(ClickMode.None);
        _pendingId = null;
        _path = newFile ? CreatePathFile(EditorSceneManager.GetActiveScene().name + "_Path")
              : now == 0 ? null
              : _pathFiles[now - 1];
        SceneView.RepaintAll();
    }

    // ------------------------------------------------------------------------------------------
    // CHARACTER
    // ------------------------------------------------------------------------------------------

    private void DrawCharacter()
    {
        if (_path == null) return;

        EditorGUILayout.LabelField("Character", EditorStyles.boldLabel);

        var registry   = LevelRegistry(false);
        var characters = registry != null ? registry.characters.Where(c => c != null).ToList()
                                          : new List<NPCCharacterRegistry.Character>();

        // Who walks it: none, or any of the level's characters — placed or not yet.
        var walker = CharacterOn(_path);
        if (walker != null) _pendingId = null;
        string current = walker != null ? walker.CharacterId : _pendingId;

        var labels = new List<string> { "None" };
        labels.AddRange(characters.Select(CharacterLabel));
        int was = characters.FindIndex(c => c.id == current) + 1;
        if (was == 0 && walker != null)
        {
            labels.Add($"{walker.name}  (not in the registry)");
            was = labels.Count - 1;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            int now = EditorGUILayout.Popup(new GUIContent("Walked By",
                "The character who walks this path — one per path. Picking one already on another path " +
                "moves it onto this one; one not in the scene yet is put down with Place in Scene."),
                was, labels.ToArray());
            if (now != was && now <= characters.Count) WalkedBy(now == 0 ? null : characters[now - 1]);

            using (new EditorGUI.DisabledScope(walker == null))
                if (GUILayout.Button("Frame", GUILayout.Width(72)))
                {
                    Selection.activeGameObject = walker.gameObject;
                    SceneView.FrameLastActiveSceneView();
                }
        }

        walker = CharacterOn(_path);
        if (walker != null) DrawCharacterSpeeds(walker);
        else DrawPlace(registry?.Find(_pendingId));

        EditorGUILayout.Space(8);
        DrawCharacterList(registry);
    }

    // The picked character who isn't in the scene yet: how big, and where to stand them.
    private void DrawPlace(NPCCharacterRegistry.Character character)
    {
        if (character == null) return;
        if (character.prefab == null)
        {
            EditorGUILayout.HelpBox($"{character.name} has no prefab to place.", MessageType.Warning);
            return;
        }

        EditorGUILayout.HelpBox($"{character.name} isn't in the scene yet.", MessageType.None);

        EditorGUI.BeginChangeCheck();
        _height = Mathf.Max(0.001f, EditorGUILayout.FloatField(
            new GUIContent("Height", "How tall it stands, from its feet to the top marker on its prefab — " +
                                     "how big it stands in the scene."), _height));
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetFloat(HeightKey, _height);

        bool placing = ModeToggle(_mode == ClickMode.Place, "Place in Scene",
                                  $"Click in the Scene view to stand {character.name} there, walking this path. Esc to stop.");
        SetMode(placing ? ClickMode.Place : (_mode == ClickMode.Place ? ClickMode.None : _mode));
        if (_mode == ClickMode.Place)
            EditorGUILayout.HelpBox($"Click in the Scene view to stand {character.name} there.", MessageType.None);
    }

    // The level's characters: rename, remove, and make new ones from a prefab.
    private void DrawCharacterList(NPCCharacterRegistry registry)
    {
        _showCharacters = EditorGUILayout.Foldout(_showCharacters, "Level's Characters", true);
        if (!_showCharacters) return;

        if (string.IsNullOrEmpty(EditorSceneManager.GetActiveScene().name))
        {
            EditorGUILayout.HelpBox("Save the scene first — its characters are kept in a file named after it.",
                                    MessageType.Info);
            return;
        }

        if (registry == null || registry.characters.Count == 0)
            EditorGUILayout.HelpBox("No characters yet.", MessageType.None);
        else
        {
            NPCCharacterRegistry.Character remove = null;
            foreach (var character in registry.characters)
            {
                if (character == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    string name = EditorGUILayout.DelayedTextField(character.name);
                    if (name != character.name && !string.IsNullOrWhiteSpace(name))
                        RenameCharacter(registry, character, name);
                    GUILayout.Label(character.prefab != null ? character.prefab.name : "No prefab",
                                    EditorStyles.miniLabel, GUILayout.Width(110));
                    if (GUILayout.Button(new GUIContent("✕", "Takes it out of the level's characters. A body " +
                            "already in the scene stays where it is."), GUILayout.Width(24)))
                        remove = character;
                }
            }
            if (remove != null) RemoveCharacter(registry, remove);

            if (GUILayout.Button("Show File")) EditorGUIUtility.PingObject(registry);
        }

        // A new one: a name and the prefab that is its body.
        EditorGUILayout.Space(4);
        if (_prefabs.Count == 0)
        {
            EditorGUILayout.HelpBox($"No prefabs with an ObserverManNPCController in {PrefabFolder}.", MessageType.Info);
            if (GUILayout.Button("Look Again")) LoadPrefabs();
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            _newName     = EditorGUILayout.TextField(new GUIContent("New Character",
                "A name for the new character, and the prefab that is its body."), _newName);
            _prefabIndex = EditorGUILayout.Popup(_prefabIndex, _prefabs.Select(p => p.name).ToArray(),
                                                 GUILayout.Width(110));
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newName)))
                if (GUILayout.Button("Add", GUILayout.Width(48)))
                {
                    AddCharacter(LevelRegistry(true), _newName, _prefabs[_prefabIndex]);
                    _newName = "";
                    GUI.FocusControl(null);
                }
        }
    }

    // "Name", "Name  (Other Path)" when it walks another path, "Name  (not placed)" with no body yet.
    private string CharacterLabel(NPCCharacterRegistry.Character character)
    {
        var body = BodyOf(character.id);
        if (body == null) return $"{character.name}  (not placed)";
        return body.TravelPath != null && body.TravelPath != _path
                   ? $"{character.name}  ({body.TravelPath.name})" : character.name;
    }

    // The character's body in the scene, if it has one.
    private ObserverManNPCController BodyOf(string id)
        => string.IsNullOrEmpty(id) ? null : _inScene.FirstOrDefault(n => n != null && n.CharacterId == id);

    // Puts a character on this path. One in the scene walks it at once; one that isn't waits for
    // Place in Scene, and the old walker comes off now.
    private void WalkedBy(NPCCharacterRegistry.Character character)
    {
        if (_mode == ClickMode.Place) SetMode(ClickMode.None);

        var body = character != null ? BodyOf(character.id) : null;
        if (character == null || body != null)
        {
            _pendingId = null;
            GiveCharacterPath(body);
            return;
        }

        var walker = CharacterOn(_path);
        if (walker != null) SetPathFile(walker, null);
        _pendingId = character.id;
        SceneView.RepaintAll();
    }

    // The character's own speeds — on the character, not the path file.
    private void DrawCharacterSpeeds(ObserverManNPCController npc)
    {
        var npcSo = new SerializedObject(npc);

        // Kept in world units a second; shown and set in measures a second.
        var walkSpeed = npcSo.FindProperty(WalkSpeedField);
        if (walkSpeed != null)
        {
            EditorGUI.BeginChangeCheck();
            float perSecond = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Walk Speed (measures/s)",
                "How many measures it walks a second — each band along the drawn path is one measure."),
                walkSpeed.floatValue / _measure));
            if (EditorGUI.EndChangeCheck())
            {
                walkSpeed.floatValue = perSecond * _measure;
                npcSo.ApplyModifiedProperties();
            }
        }

        var rotPer90 = npcSo.FindProperty(RotPer90Field);
        if (rotPer90 != null)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(rotPer90, new GUIContent("Rot (s per 90°)",
                "How long it takes to turn 90° to a point's Rot, in seconds — 180° takes twice as long. The pause starts once it faces that way."));
            if (EditorGUI.EndChangeCheck()) npcSo.ApplyModifiedProperties();
        }
    }

    // Puts the character on this path — the one already on it comes off — and stands it at the start.
    private void GiveCharacterPath(ObserverManNPCController npc)
    {
        var walker = CharacterOn(_path);
        if (walker == npc) return;
        if (walker != null) SetPathFile(walker, null);
        if (npc != null)
        {
            SetPathFile(npc, _path);
            StandAtPathStart(npc);
            Selection.activeGameObject = npc.gameObject;
        }
        SceneView.RepaintAll();
    }

    private void DrawDisplaySection()
    {
        _showDisplay = EditorGUILayout.Foldout(_showDisplay, "Display", true);
        if (!_showDisplay) return;

        EditorGUI.BeginChangeCheck();
        _lineWidth  = EditorGUILayout.Slider("Line Width", _lineWidth, 1f, 12f);
        _pointSize  = EditorGUILayout.Slider("Point Size", _pointSize, 0.02f, 0.3f);
        _pathColour = EditorGUILayout.ColorField("Path Colour", _pathColour);
        _measureColour = EditorGUILayout.ColorField(new GUIContent("Measure Colour",
            "Every other measure along the path is this colour, so the bands can be counted."), _measureColour);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetFloat(LineWidthKey, _lineWidth);
            EditorPrefs.SetFloat(PointSizeKey, _pointSize);
            EditorPrefs.SetString(PathColourKey, ColorUtility.ToHtmlStringRGBA(_pathColour));
            EditorPrefs.SetString(MeasureColourKey, ColorUtility.ToHtmlStringRGBA(_measureColour));
            SceneView.RepaintAll();
        }
    }

    private static bool ModeToggle(bool on, string label, string tooltip)
        => GUILayout.Toggle(on, new GUIContent(label, tooltip), "Button", GUILayout.Height(24));

    private void SetMode(ClickMode mode)
    {
        if (mode != ClickMode.TravelPath)
        {
            _pointAction = PointAction.Append;
            _pointIndex  = -1;
        }
        if (mode == _mode) return;
        _mode = mode;
        SceneView.RepaintAll();
    }

    // Arms the Travel Path click for one point action, or turns it off with null.
    private void SetPointAction(PointAction? action, int index)
    {
        if (action == null)
        {
            SetMode(ClickMode.None);
            return;
        }
        SetMode(ClickMode.TravelPath);
        _pointAction = action.Value;
        _pointIndex  = action == PointAction.Append ? -1 : index;
        SceneView.RepaintAll();
    }

    private bool PointActionPending(PointAction action, int index)
        => _mode == ClickMode.TravelPath && _pointAction == action && _pointIndex == index;

    // ------------------------------------------------------------------------------------------
    // PATH FILES
    // ------------------------------------------------------------------------------------------

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int cut = folder.LastIndexOf('/');
        AssetDatabase.CreateFolder(folder.Substring(0, cut), folder.Substring(cut + 1));
    }

    private static string SafeFileName(string name)
    {
        foreach (char bad in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        return name;
    }

    private NPCTravelPath CreatePathFile(string name)
    {
        EnsureFolder(PathFolder);
        name = SafeFileName(name);

        var file = CreateInstance<NPCTravelPath>();
        AssetDatabase.CreateAsset(file, AssetDatabase.GenerateUniqueAssetPath($"{PathFolder}/{name}.asset"));
        AssetDatabase.SaveAssets();
        LoadPathFiles();
        Debug.Log($"[NPCPathTool] New path file {AssetDatabase.GetAssetPath(file)}.", file);
        return file;
    }

    private static void SetPathFile(ObserverManNPCController npc, NPCTravelPath file)
    {
        var npcSo = new SerializedObject(npc);
        npcSo.FindProperty(PathFileField).objectReferenceValue = file;
        npcSo.ApplyModifiedProperties();
    }

    // A path drawn before paths were files is moved into a new one, once.
    private void MoveScenePathToFile(ObserverManNPCController npc)
    {
        if (npc == null || npc.TravelPath != null || !npc.HasScenePath) return;

        var file = CreatePathFile($"{npc.gameObject.scene.name}_{npc.name}");
        Undo.RecordObject(npc, "Move Path to File");
        npc.TakeScenePath(file);
        PrefabUtility.RecordPrefabInstancePropertyModifications(npc);
        EditorUtility.SetDirty(file);
        AssetDatabase.SaveAssetIfDirty(file);
        SetPathFile(npc, file);
        Debug.Log($"[NPCPathTool] Moved {npc.name}'s path out of the scene into {AssetDatabase.GetAssetPath(file)} " +
                  "— save the scene to keep the link.", file);
    }

    // ------------------------------------------------------------------------------------------
    // CHARACTER REGISTRY
    // ------------------------------------------------------------------------------------------

    // The active scene's characters — made on first need when create is set. None for an unsaved scene.
    private NPCCharacterRegistry LevelRegistry(bool create)
    {
        string scene = EditorSceneManager.GetActiveScene().name;
        if (_registry == null || _registryScene != scene)
        {
            _registry      = LoadRegistry(scene, false);
            _registryScene = scene;
        }
        if (_registry == null && create) _registry = LoadRegistry(scene, true);
        return _registry;
    }

    // {scene}_Characters in the characters folder.
    private static NPCCharacterRegistry LoadRegistry(string scene, bool create)
    {
        if (string.IsNullOrEmpty(scene)) return null;

        string path     = $"{RegistryFolder}/{SafeFileName(scene)}_Characters.asset";
        var    registry = AssetDatabase.LoadAssetAtPath<NPCCharacterRegistry>(path);
        if (registry != null || !create) return registry;

        EnsureFolder(RegistryFolder);
        registry = CreateInstance<NPCCharacterRegistry>();
        AssetDatabase.CreateAsset(registry, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"[NPCPathTool] New character registry {path}.", registry);
        return registry;
    }

    private static NPCCharacterRegistry.Character AddCharacter(NPCCharacterRegistry registry, string name, GameObject prefab)
    {
        Undo.RecordObject(registry, "New Character");
        var character = new NPCCharacterRegistry.Character
        {
            id     = System.Guid.NewGuid().ToString("N"),
            name   = UniqueName(registry, name, null),
            prefab = prefab,
        };
        registry.characters.Add(character);
        SaveRegistry(registry);
        return character;
    }

    // Renames the body in the scene too, so the Hierarchy reads the same.
    private void RenameCharacter(NPCCharacterRegistry registry, NPCCharacterRegistry.Character character, string name)
    {
        Undo.RecordObject(registry, "Rename Character");
        character.name = UniqueName(registry, name, character);
        SaveRegistry(registry);

        foreach (var body in _inScene.Where(n => n != null && n.CharacterId == character.id))
        {
            Undo.RecordObject(body.gameObject, "Rename Character");
            body.gameObject.name = character.name;
        }
    }

    private void RemoveCharacter(NPCCharacterRegistry registry, NPCCharacterRegistry.Character character)
    {
        Undo.RecordObject(registry, "Remove Character");
        registry.characters.Remove(character);
        SaveRegistry(registry);
        if (_pendingId == character.id)
        {
            _pendingId = null;
            if (_mode == ClickMode.Place) SetMode(ClickMode.None);
        }
    }

    // Two characters can't share a name — the dropdown couldn't tell them apart.
    private static string UniqueName(NPCCharacterRegistry registry, string name, NPCCharacterRegistry.Character except)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? "Character" : name.Trim();
        string unique   = baseName;
        for (int k = 2; registry.characters.Any(c => c != null && c != except && c.name == unique); k++)
            unique = $"{baseName} {k}";
        return unique;
    }

    private static void SaveRegistry(NPCCharacterRegistry registry)
    {
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssetIfDirty(registry);
    }

    private static void SetCharacterId(ObserverManNPCController npc, string id)
    {
        var npcSo = new SerializedObject(npc);
        npcSo.FindProperty(CharacterField).stringValue = id;
        npcSo.ApplyModifiedProperties();
    }

    // A character placed before there was a registry joins its scene's, once, under its own name.
    private static void AddToRegistry(ObserverManNPCController npc)
    {
        if (npc == null || !string.IsNullOrEmpty(npc.CharacterId)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        var registry = LoadRegistry(npc.gameObject.scene.name, true);
        if (registry == null) return;

        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(npc.gameObject);
        var    prefab     = string.IsNullOrEmpty(prefabPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var    character  = AddCharacter(registry, npc.name, prefab);
        SetCharacterId(npc, character.id);
        Debug.Log($"[NPCPathTool] Added {npc.name} to {AssetDatabase.GetAssetPath(registry)} as '{character.name}' " +
                  "— save the scene to keep the link.", registry);
    }

    // Applies an edit and, for a path file, writes the file straight away.
    private static void Apply(SerializedObject so)
    {
        so.ApplyModifiedProperties();
        if (EditorUtility.IsPersistent(so.targetObject)) AssetDatabase.SaveAssetIfDirty(so.targetObject);
    }

    // ------------------------------------------------------------------------------------------
    // SCENE VIEW
    // ------------------------------------------------------------------------------------------

    private void OnSceneGUI(SceneView view)
    {
        DrawPath();

        if (_mode == ClickMode.None || _path == null) return;

        Event e  = Event.current;
        int   id = GUIUtility.GetControlID(FocusType.Passive);

        if (e.type == EventType.Layout)
            HandleUtility.AddDefaultControl(id); // clicks come here, not to the selection

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            SetMode(ClickMode.None);
            Repaint();
            e.Use();
            return;
        }

        if (e.type != EventType.MouseDown || e.button != 0 || e.alt) return;

        if (!SceneViewNavigation.TryHitDrawnSurface(e.mousePosition, out Vector3 point))
        {
            Debug.Log("[NPCPathTool] Nothing drawn under the click — nothing placed.");
            e.Use();
            return;
        }

        if (_mode == ClickMode.Place) PlaceCharacter(point);
        else
        {
            AddPathPoint(point);
            // Adding after a point or redrawing one is a single click; adding to the end carries on.
            if (_pointAction != PointAction.Append) SetMode(ClickMode.None);
        }

        e.Use();
        Repaint();
    }

    private void PlaceCharacter(Vector3 point)
    {
        var character = LevelRegistry(false)?.Find(_pendingId);
        var prefab    = character?.prefab;
        if (prefab == null)
        {
            SetMode(ClickMode.None);
            return;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, EditorSceneManager.GetActiveScene());
        Undo.RegisterCreatedObjectUndo(go, "Place Character");
        go.name = character.name;

        // Feet on the clicked point: the prefab's PrefabBaselineAlignment disc, or its root.
        // Sized by Height against the prefab's top marker.
        var   aligner = go.GetComponentInChildren<PrefabBaselineAlignment>(true);
        float top     = aligner != null ? aligner.PrefabTopHeight : 0f;
        if (top > 0f) go.transform.localScale = prefab.transform.localScale * (_height / top);
        else Debug.LogWarning($"[NPCPathTool] {prefab.name} has no top marker — Height is ignored and it " +
                              "stands at the prefab's own size.");

        var npc = go.GetComponent<ObserverManNPCController>();
        SetCharacterId(npc, character.id);
        go.transform.position += point - npc.FeetWorld();
        GameObjectUtility.EnsureUniqueNameForSibling(go);

        _pendingId = null;
        SetMode(ClickMode.None);
        FindInScene();
        GiveCharacterPath(npc);
        Selection.activeGameObject = go;
    }

    private void AddPathPoint(Vector3 world)
    {
        var so     = new SerializedObject(_path);
        var points = so.FindProperty(PathField);
        if (points == null) return;

        int  at    = _pointIndex;
        bool known = at >= 0 && at < points.arraySize;

        if (_pointAction == PointAction.Redraw && known)
        {
            points.GetArrayElementAtIndex(at).vector3Value = world;
        }
        else
        {
            // After the point asked for, or on the end.
            int i = _pointAction == PointAction.Insert && known ? at + 1 : points.arraySize;
            points.InsertArrayElementAtIndex(i);
            points.GetArrayElementAtIndex(i).vector3Value = world;

            // The new point's pause starts off — an insert would otherwise copy its neighbour's.
            var pauses = so.FindProperty(PauseField);
            if (pauses != null && i <= pauses.arraySize)
            {
                pauses.InsertArrayElementAtIndex(i);
                var pause = pauses.GetArrayElementAtIndex(i);
                pause.FindPropertyRelative("pause").boolValue    = false;
                pause.FindPropertyRelative("seconds").floatValue = 0f;
                pause.FindPropertyRelative("face").boolValue     = false;
            }
        }
        SyncPauses(so);
        FitToGround(so, GroundHeight());
        Apply(so);
        StandAtPathStart(CharacterOn(_path));
    }

    // One pause per point. Growing a list copies its last entry, so new ones are set to no pause.
    private static void SyncPauses(SerializedObject so)
    {
        var points = so.FindProperty(PathField);
        var pauses = so.FindProperty(PauseField);
        if (points == null || pauses == null) return;

        int had = pauses.arraySize;
        pauses.arraySize = points.arraySize;
        for (int i = had; i < pauses.arraySize; i++)
        {
            var pause = pauses.GetArrayElementAtIndex(i);
            pause.FindPropertyRelative("pause").boolValue    = false;
            pause.FindPropertyRelative("seconds").floatValue = 0f;
            pause.FindPropertyRelative("face").boolValue     = false;
        }
    }

    // On the first point, facing along the path — where it sets off from in play.
    private static void StandAtPathStart(ObserverManNPCController npc)
    {
        if (npc == null || !npc.HasPath) return;
        Undo.RecordObject(npc.transform, "Stand at Path Start");
        npc.PlaceAtPathStart();
    }

    // The height the ground is felt for at: the walker's, or Height when no one walks it yet.
    private float GroundHeight()
    {
        var walker = CharacterOn(_path);
        return walker != null ? CharacterHeight(walker) : _height;
    }

    // ------------------------------------------------------------------------------------------
    // GROUND
    // ------------------------------------------------------------------------------------------

    // How finely the ground between two points is felt for, and how far a bump must stand off a
    // straight run to be kept — both as a share of the walker's height, so they suit its size.
    private const float SampleSpacing    = 0.1f;
    private const float KeepTolerance    = 0.01f;
    private const int   MaxSamplesPerLeg = 400;

    private struct Surface
    {
        public Mesh      mesh;
        public Matrix4x4 toWorld;
        public Bounds    bounds;
    }

    /// <summary>
    /// Lays the line between each point and the next over what's drawn under it, and writes the
    /// in-between points to the path file's ground (not applied — the caller applies).
    ///
    /// Walking from one point to the next, it feels straight down for the ground from a head's
    /// height above where it was last standing: anything lower than its head it walks up onto,
    /// anything higher (a bridge, a tower's head) it walks under. Where nothing is found the line
    /// runs straight.
    /// </summary>
    private static void FitToGround(SerializedObject so, float height)
    {
        var points = so.FindProperty(PathField);
        var legs   = so.FindProperty(GroundField);
        if (points == null || legs == null) return;

        int count = points.arraySize;
        legs.arraySize = count;
        if (count < 2)
        {
            for (int i = 0; i < count; i++) legs.GetArrayElementAtIndex(i).FindPropertyRelative("ground").ClearArray();
            return;
        }

        var world = new Vector3[count];
        for (int i = 0; i < count; i++)
            world[i] = points.GetArrayElementAtIndex(i).vector3Value;

        var   area   = new Bounds(world[0], Vector3.zero);
        foreach (var p in world) area.Encapsulate(p);
        area.Expand(new Vector3(height, height * 4f, height) * 2f);
        var surfaces = GroundSurfaces(area);
        Physics.SyncTransforms();

        for (int i = 0; i < count; i++)
        {
            var ground = FitLeg(world[i], world[(i + 1) % count], height, surfaces);
            var prop   = legs.GetArrayElementAtIndex(i).FindPropertyRelative("ground");
            prop.arraySize = ground.Count;
            for (int j = 0; j < ground.Count; j++)
                prop.GetArrayElementAtIndex(j).vector3Value = ground[j];
        }
    }

    // The in-between points from a to b, ends left out, with flat stretches thinned away.
    private static List<Vector3> FitLeg(Vector3 a, Vector3 b, float height, List<Surface> surfaces)
    {
        float length  = Vector3.Distance(a, b);
        int   samples = Mathf.Clamp(Mathf.CeilToInt(length / (height * SampleSpacing)), 1, MaxSamplesPerLeg);

        var   line  = new List<Vector3>(samples + 1) { a };
        float lastY = a.y;
        for (int k = 1; k < samples; k++)
        {
            Vector3 p      = Vector3.Lerp(a, b, k / (float)samples);
            Vector3 origin = new Vector3(p.x, Mathf.Max(p.y, lastY) + height, p.z);
            if (TryFeelGround(origin, surfaces, out float y)) p.y = y;
            line.Add(p);
            lastY = p.y;
        }
        line.Add(b);

        var keep = new bool[line.Count];
        keep[0] = keep[line.Count - 1] = true;
        Thin(line, 0, line.Count - 1, height * KeepTolerance, keep);

        var ground = new List<Vector3>();
        for (int i = 1; i < line.Count - 1; i++)
            if (keep[i]) ground.Add(line[i]);
        return ground;
    }

    // Keeps the point standing furthest off the straight run between two kept points, if it's
    // far enough off, then does the same either side of it.
    private static void Thin(List<Vector3> line, int from, int to, float tolerance, bool[] keep)
    {
        if (to - from < 2) return;
        Vector3 a = line[from], b = line[to];
        int   furthest = -1;
        float most     = tolerance;
        for (int i = from + 1; i < to; i++)
        {
            Vector3 onRun = a + Vector3.Project(line[i] - a, b - a);
            float   off   = Vector3.Distance(line[i], onRun);
            if (off > most) { most = off; furthest = i; }
        }
        if (furthest < 0) return;
        keep[furthest] = true;
        Thin(line, from, furthest, tolerance, keep);
        Thin(line, furthest, to, tolerance, keep);
    }

    // Straight down from the origin to the first thing drawn, or a collider.
    private static bool TryFeelGround(Vector3 origin, List<Surface> surfaces, out float y)
    {
        var   ray     = new Ray(origin, Vector3.down);
        float nearest = Mathf.Infinity;

        foreach (var s in surfaces)
        {
            if (!s.bounds.IntersectRay(ray)) continue;
            if (SceneViewNavigation.TryHitMesh(ray, s.mesh, s.toWorld, out Vector3 hit))
            {
                float d = origin.y - hit.y;
                if (d >= 0f && d < nearest) nearest = d;
            }
        }

        foreach (var hit in Physics.RaycastAll(ray, Mathf.Infinity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<ObserverManNPCController>(true) != null) continue;
            if (hit.distance < nearest) nearest = hit.distance;
        }

        y = origin.y - nearest;
        return nearest < Mathf.Infinity;
    }

    // Every mesh drawn in the area, leaving out characters and anything hidden in the Scene view
    // (hide a fog sheet or the like and the line stops landing on it).
    private static List<Surface> GroundSurfaces(Bounds area)
    {
        var surfaces = new List<Surface>();
        foreach (var filter in FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude))
        {
            var go = filter.gameObject;
            if (filter.sharedMesh == null || EditorUtility.IsPersistent(go)) continue;
            if ((go.hideFlags & HideFlags.HideInHierarchy) != 0) continue;
            if (!go.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled) continue;
            if (!renderer.bounds.Intersects(area)) continue;
            if (SceneVisibilityManager.instance.IsHidden(go)) continue;
            if (go.GetComponentInParent<ObserverManNPCController>(true) != null) continue;

            surfaces.Add(new Surface
            {
                mesh    = filter.sharedMesh,
                toWorld = go.transform.localToWorldMatrix,
                bounds  = renderer.bounds,
            });
        }
        return surfaces;
    }

    // Feet to top marker, at the size it stands in the scene.
    private static float CharacterHeight(ObserverManNPCController npc)
    {
        var aligner = npc.GetComponentInChildren<PrefabBaselineAlignment>(true);
        if (aligner != null && aligner.PrefabTopHeight > 0f)
            return aligner.PrefabTopHeight * npc.transform.lossyScale.y;

        var renderers = npc.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            if (b.size.y > 0f) return b.size.y;
        }
        return 1f;
    }


    // ------------------------------------------------------------------------------------------
    // PATH DRAWING
    // ------------------------------------------------------------------------------------------

    // The path line in bands one measure long along the ground it walks, path colour and measure
    // colour in turn, so counting bands counts measures.
    private void DrawMeasuredLine(List<Vector3> line)
    {
        // Too many bands to read (a tiny measure on a long path) — one colour instead.
        float length = 0f;
        for (int i = 1; i < line.Count; i++) length += Vector3.Distance(line[i - 1], line[i]);
        if (length / _measure > 2000f)
        {
            DrawBand(line, false);
            return;
        }

        var   band = new List<Vector3> { line[0] };
        float left = _measure;   // still to go in this band
        bool  odd  = false;

        for (int i = 1; i < line.Count; i++)
        {
            Vector3 from = line[i - 1], to = line[i];
            float   span = Vector3.Distance(from, to);
            float   done = 0f;

            while (span - done >= left)
            {
                done += left;
                Vector3 cut = Vector3.Lerp(from, to, done / span);
                band.Add(cut);
                DrawBand(band, odd);
                band.Clear();
                band.Add(cut);
                odd  = !odd;
                left = _measure;
            }
            left -= span - done;
            band.Add(to);
        }
        if (band.Count > 1) DrawBand(band, odd);
    }

    private void DrawBand(List<Vector3> band, bool odd)
    {
        Handles.color = odd ? _measureColour : _pathColour;
        Handles.DrawAAPolyLine(Texture2D.whiteTexture, _lineWidth, band.ToArray());
    }

    // The path: a line from point to point over the fitted ground — back round to the first when
    // it loops — a numbered dot at each, and an arrow where a point has Rot.
    private void DrawPath()
    {
        if (_path == null || Event.current.type != EventType.Repaint) return;

        var points = _path.points;
        int count  = points?.Count ?? 0;
        if (count == 0) return;

        bool fitted = _path.ground != null && _path.ground.Count == count;
        bool loops  = _path.walk == NPCTravelPath.PathWalk.Loop && count > 2;
        int  legs   = loops ? count : count - 1;
        var  line   = new List<Vector3>();
        for (int i = 0; i < legs; i++)
        {
            line.Add(points[i]);
            if (fitted && _path.ground[i]?.ground != null) line.AddRange(_path.ground[i].ground);
        }
        line.Add(points[loops ? 0 : count - 1]);

        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        if (count > 1) DrawMeasuredLine(line);
        Handles.color = _pathColour;

        for (int i = 0; i < count; i++)
        {
            Vector3 p    = points[i];
            float   size = HandleUtility.GetHandleSize(p) * _pointSize;
            Handles.DrawSolidDisc(p, Camera.current != null ? -Camera.current.transform.forward : Vector3.up, size);
            Handles.Label(p + Vector3.up * size * 1.5f, (i + 1).ToString(), EditorStyles.whiteBoldLabel);

            // An arrow the way it faces while it stands here.
            var stop = _path.pauses != null && i < _path.pauses.Count ? _path.pauses[i] : null;
            if (stop != null && stop.face)
            {
                Vector3 face = Quaternion.Euler(0f, stop.faceAngle, 0f) * Vector3.forward;
                Vector3 side = Vector3.Cross(Vector3.up, face);
                Vector3 tip  = p + face * size * 5f;
                Handles.DrawAAPolyLine(Texture2D.whiteTexture, _lineWidth, p, tip);
                Handles.DrawAAPolyLine(Texture2D.whiteTexture, _lineWidth,
                                       tip - face * size * 1.5f + side * size, tip, tip - face * size * 1.5f - side * size);
            }
        }
    }
}
