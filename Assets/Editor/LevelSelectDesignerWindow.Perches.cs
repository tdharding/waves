using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Angel perches on the map — somewhere for her to land.
///
/// She flies above the boat and comes down onto the nearest perch the BOAT has sailed into, so
/// every perch carries its own radii rather than there being one range on her. The tip she stands
/// on is not authored: whatever she lands on hands over its own top as it is generated, so a
/// tower resized in the panel moves her feet with it and nothing has to be dialled in twice.
///
/// Two ways one gets onto the map:
///
///   on a tower ..... a perch block on a rim node topper, a pool island tower or an outpost
///                    tower. The tip is the top of its orb.
///   a marker ....... dropped anywhere in Perch mode, standing at its own height on nothing at
///                    all. This is what covers everything with no block of its own — park one
///                    over a pipe, a wall or a hill and she lands there just the same.
///
/// The components this writes are the same ones the Grid Designer puts on a marked spike, so a
/// perch behaves identically in a level and on the map.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const string PerchMarkersParent = "ANGELPERCHES";

    // ─────────────────────────────────────────────
    // PANEL BLOCK
    // ─────────────────────────────────────────────

    /// <summary>
    /// The perch fields for whatever she would stand on here. Hands back an edited copy, so the
    /// caller writes it only inside its own change check — the same shape as the interact block.
    /// </summary>
    private LevelSelectDesignerData.DesignerPerch DrawPerchBlock(
        LevelSelectDesignerData.DesignerPerch perch)
    {
        var edited = perch?.Clone() ?? new LevelSelectDesignerData.DesignerPerch();

        edited.enabled = EditorGUILayout.Toggle(
            new GUIContent("Angel Perch", "Whether the angel can land on top of this."),
            edited.enabled);

        if (!edited.enabled) return edited;

        EditorGUI.indentLevel++;

        edited.perchRadius = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Perch Radius", "Sail inside this and she comes down here; back " +
                                           "outside it and she leaves."),
            edited.perchRadius));

        edited.priority = EditorGUILayout.Toggle(
            new GUIContent("Priority", "Always come down the moment the boat arrives. Left off " +
                                       "she settles here only when she happens to be looking for " +
                                       "somewhere to land."),
            edited.priority);

        edited.startPerch = EditorGUILayout.Toggle(
            new GUIContent("Start Here", "She begins the map stood here, and holds it until the " +
                                         "boat first sails inside the perch radius. Only one " +
                                         "perch on the map should have this."),
            edited.startPerch);

        edited.landingCurveSize = Mathf.Max(0f, EditorGUILayout.FloatField(
            new GUIContent("Landing Curve", "Radius of the curve she lands along. 0 = straight " +
                                            "at it. A perch hemmed in wants a tight curve, one " +
                                            "in the open a wide one."),
            edited.landingCurveSize));

        edited.talkEnabled = EditorGUILayout.Toggle(
            new GUIContent("Talk", "Let her be talked to here, using the map's own key and prompt."),
            edited.talkEnabled);

        if (edited.talkEnabled)
        {
            EditorGUI.indentLevel++;

            // Kept inside the perch radius: a talk range reaching further than the range that
            // brought her here would prompt for an angel who is already leaving.
            edited.talkRadius = Mathf.Clamp(EditorGUILayout.FloatField(
                new GUIContent("Talk Radius", "How near the boat has to be to talk to her here. " +
                                              "Held inside the perch radius."),
                edited.talkRadius), 0f, edited.perchRadius);

            edited.talkPrompt = EditorGUILayout.TextField(
                new GUIContent("Prompt", "What the prompt reads. The key is added by the prompt " +
                                         "itself, so this is just the doing."),
                edited.talkPrompt);

            EditorGUILayout.LabelField(new GUIContent("Says",
                "What she says here. Split on / into one line per press."));
            EditorGUILayout.LabelField("Type / to start a new line — each press shows the next one.",
                                       EditorStyles.wordWrappedMiniLabel);
            edited.talkText = EditorGUILayout.TextArea(edited.talkText ?? "", GUILayout.Height(40f));

            int lines = SplitTalkPreview(edited.talkText).Length;
            EditorGUILayout.LabelField(" ",
                lines == 0 ? "nothing to say" : $"{lines} line{(lines == 1 ? "" : "s")}",
                EditorStyles.miniLabel);

            edited.talkCameraDistance = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Camera Distance", "How far her camera sits from her while she " +
                                                  "talks here, along its own view — smaller is " +
                                                  "closer up. 0 = the angel prefab's own."),
                edited.talkCameraDistance));

            // The default is read off the angel actually in the scene, so it is the number her
            // camera really uses rather than one remembered here.
            var angel = FindFirstObjectByType<AngelCompanion>();
            string fallback = angel == null
                ? "no angel in the scene to read it from"
                : $"{angel.DefaultTalkCameraDistance:0.###}";
            EditorGUILayout.LabelField(" ",
                edited.talkCameraDistance > 0f ? $"default {fallback}" : $"using default: {fallback}",
                EditorStyles.miniLabel);

            EditorGUI.indentLevel--;
        }

        EditorGUI.indentLevel--;
        return edited;
    }

    /// <summary>
    /// Mirrors AngelPerchPoint.SplitTalkLines so the panel counts exactly what will play — blank
    /// pieces dropped, so a trailing slash is a typo rather than an empty beat.
    /// </summary>
    private static string[] SplitTalkPreview(string say)
    {
        if (string.IsNullOrWhiteSpace(say)) return System.Array.Empty<string>();
        return say.Split('/').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
    }

    // ─────────────────────────────────────────────
    // PUTTING ONE ON SOMETHING
    // ─────────────────────────────────────────────

    /// <summary>
    /// Puts a perch onto something just generated, standing her at <paramref name="tipLocal"/> in
    /// that object's own space — so a scaled or turned piece carries her tip with it. Nothing is
    /// left behind when the block is off, so turning it off and generating again takes the perch
    /// away.
    /// </summary>
    private void ApplyPerch(GameObject placed, LevelSelectDesignerData.DesignerPerch perch,
                            Vector3 tipLocal)
    {
        if (placed == null) return;

        var point = placed.GetComponent<AngelPerchPoint>();
        var talk  = placed.GetComponent<LevelSelectAngelTalk>();
        var reach = placed.GetComponent<LevelSelectInteractPoint>();

        if (perch == null || !perch.enabled)
        {
            // Order matters: the talk requires the perch, so Unity refuses to take the perch away
            // while it is still sitting there.
            if (talk  != null) Undo.DestroyObjectImmediate(talk);
            if (point != null) Undo.DestroyObjectImmediate(point);
            return;
        }

        if (point == null) point = Undo.AddComponent<AngelPerchPoint>(placed);

        point.Configure(tipLocal, perch.perchRadius, perch.talkRadius, perch.priority,
                        perch.talkEnabled, perch.talkText, perch.landingCurveSize,
                        perch.startPerch, perch.talkPrompt, perch.talkCameraDistance);
        EditorUtility.SetDirty(point);

        // Talking runs through the map's own interact system, so a talking perch needs the point
        // that notices the boat as well — the same pair a poster gets.
        if (!perch.talkEnabled)
        {
            if (talk != null) Undo.DestroyObjectImmediate(talk);
            return;
        }

        if (talk  == null) talk  = Undo.AddComponent<LevelSelectAngelTalk>(placed);
        if (reach == null) reach = Undo.AddComponent<LevelSelectInteractPoint>(placed);

        reach.prompt = string.IsNullOrWhiteSpace(perch.talkPrompt) ? "Talk" : perch.talkPrompt;
        reach.radius = perch.talkRadius;

        EditorUtility.SetDirty(talk);
        EditorUtility.SetDirty(reach);
    }

    // ─────────────────────────────────────────────
    // FREE MARKERS
    // ─────────────────────────────────────────────

    private string  _selectedPerchId;
    private bool    _isDraggingPerch;
    private Vector2 _perchDragOffset;

    private LevelSelectDesignerData.DesignerPerchMarker SelectedPerchMarker =>
        _data?.perchMarkers?.Find(m => m.perchId == _selectedPerchId);

    private void HandlePerchMode(Event e)
    {
        if (e.type == EventType.KeyDown &&
            (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
        {
            DeleteSelectedPerchMarker();
            e.Use();
            return;
        }

        if (HandlePerchDrag(e)) return;

        if (e.type != EventType.MouseDown || e.button != 0) return;

        // A click on one that is already there picks it up rather than stacking another on it,
        // and holding the button carries it with the mouse.
        if (TryPickPerchMarker(e)) return;

        AddPerchMarker(e.mousePosition);
        e.Use();
    }

    /// <summary>
    /// Removes the selected marker, from the data and from the scene. Shared by Perch and Select
    /// mode.
    /// </summary>
    private void DeleteSelectedPerchMarker()
    {
        var selected = SelectedPerchMarker;
        if (selected == null) return;

        Undo.RecordObject(_data, "Delete Perch Marker");
        _data.perchMarkers.Remove(selected);
        _selectedPerchId = null;
        _isDraggingPerch = false;
        MarkDirty();
        RebuildPerchMarkers();
        Repaint();
    }

    /// <summary>
    /// Selects the marker under the mouse and starts carrying it. Shared by Perch and Select
    /// mode, so a marker can be picked up from either without switching over.
    /// </summary>
    private bool TryPickPerchMarker(Event e)
    {
        if (!FindPerchMarkerAtCanvas(e.mousePosition, out string hit)) return false;

        _selectedPerchId = hit;
        var picked = SelectedPerchMarker;
        _perchDragOffset = e.mousePosition -
                           WorldToCanvas(new Vector3(picked.positionXZ.x, 0f, picked.positionXZ.y));
        _isDraggingPerch = true;
        e.Use();
        Repaint();
        return true;
    }

    /// <summary>Moves the carried marker and lets it go. True when it took the event.</summary>
    private bool HandlePerchDrag(Event e)
    {
        if (!_isDraggingPerch) return false;

        if (e.type == EventType.MouseDrag)
        {
            var dragged = SelectedPerchMarker;
            if (dragged != null)
            {
                Undo.RecordObject(_data, "Move Perch Marker");
                dragged.positionXZ = CanvasToWorld2D(e.mousePosition - _perchDragOffset);
                MarkDirty();
            }
            Repaint();
            e.Use();
            return true;
        }

        if (e.type == EventType.MouseUp)
        {
            // Built once on release rather than on every step of the drag.
            _isDraggingPerch = false;
            RebuildPerchMarkers();
            e.Use();
            return true;
        }

        return false;
    }

    private void AddPerchMarker(Vector2 canvas)
    {
        Undo.RecordObject(_data, "Add Perch Marker");

        int    number = _data.perchMarkers.Count + 1;
        string id     = $"perch{number}";
        while (_data.perchMarkers.Any(m => m.perchId == id)) id = $"perch{++number}";

        _data.perchMarkers.Add(new LevelSelectDesignerData.DesignerPerchMarker
        {
            perchId    = id,
            positionXZ = CanvasToWorld2D(canvas),
        });

        _selectedPerchId = id;
        MarkDirty();
        Repaint();
    }

    private bool FindPerchMarkerAtCanvas(Vector2 canvas, out string perchId)
    {
        perchId = null;
        if (_data?.perchMarkers == null) return false;

        const float grabPixels = 10f;
        float       best       = grabPixels;

        foreach (var marker in _data.perchMarkers)
        {
            if (marker == null) continue;

            Vector2 at = WorldToCanvas(new Vector3(marker.positionXZ.x, 0f, marker.positionXZ.y));
            float   d  = Vector2.Distance(at, canvas);
            if (d > best) continue;

            best    = d;
            perchId = marker.perchId;
        }

        return perchId != null;
    }

    // ─────────────────────────────────────────────
    // CANVAS
    // ─────────────────────────────────────────────

    /// <summary>
    /// A solid dot where each marker stands, with its perch range washed around it — the selected
    /// one drawn larger. Solid throughout: a ring here would read as one of the map's own pieces.
    /// </summary>
    private void DrawPerchMarkers()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (_data?.perchMarkers == null) return;

        foreach (var marker in _data.perchMarkers)
        {
            if (marker == null) continue;

            Vector2 at       = WorldToCanvas(new Vector3(marker.positionXZ.x, 0f, marker.positionXZ.y));
            bool    selected = marker.perchId == _selectedPerchId;
            bool    on       = marker.perch != null && marker.perch.enabled;

            if (on && marker.perch.perchRadius > 0f)
            {
                Handles.color = new Color(1f, 0.93f, 0.55f, selected ? 0.16f : 0.08f);
                Handles.DrawSolidDisc(at, Vector3.forward, marker.perch.perchRadius * PerchCanvasScale);
            }

            Handles.color = !on                       ? new Color(0.55f, 0.55f, 0.55f, 0.9f)
                          : marker.perch.startPerch   ? new Color(1f, 0.75f, 0.20f, 1f)
                          : marker.perch.priority     ? new Color(1f, 0.85f, 0.35f, 1f)
                                                      : new Color(1f, 0.95f, 0.65f, 1f);
            Handles.DrawSolidDisc(at, Vector3.forward, selected ? 6f : 4f);

            if (on && marker.perch.startPerch)
                Handles.Label(at + new Vector2(8f, -8f), "start", EditorStyles.miniLabel);
        }
    }

    // How many canvas pixels one world unit covers, read off the canvas itself rather than kept
    // in step with a zoom field by hand.
    private float PerchCanvasScale
    {
        get
        {
            Vector2 origin = WorldToCanvas(Vector3.zero);
            Vector2 unit   = WorldToCanvas(new Vector3(1f, 0f, 0f));
            return Mathf.Max(0.0001f, Vector2.Distance(origin, unit));
        }
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    private Vector2 _perchListScroll;

    /// <summary>
    /// The free markers, listed with the selected one opened up. Shown in Perch mode; the perches
    /// that ride on towers are authored in those towers' own panels instead, where the thing she
    /// would be standing on is in front of you.
    /// </summary>
    private void DrawPerchPanel()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Angel Perches", EditorStyles.boldLabel);

        if (FindFirstObjectByType<AngelCompanion>() == null)
            EditorGUILayout.HelpBox("No angel in the scene — perches will be built, but there is " +
                                    "nobody to land on them. Drop the angel prefab into the map.",
                                    MessageType.Info);

        int starts = _data.perchMarkers.Count(m => m?.perch != null && m.perch.enabled && m.perch.startPerch)
                   + CountTowerStartPerches();
        if (starts > 1)
            EditorGUILayout.HelpBox($"{starts} perches are set to Start Here. She can only open on " +
                                    "one, and the first found wins — untick the others.",
                                    MessageType.Warning);

        EditorGUILayout.LabelField("Click the canvas to drop one. She lands on the tip, so Height " +
                                   "is how far her feet stand above the water.",
                                   EditorStyles.miniLabel);

        if (_data.perchMarkers.Count == 0)
        {
            EditorGUILayout.LabelField("None yet.", EditorStyles.miniLabel);
            return;
        }

        _perchListScroll = EditorGUILayout.BeginScrollView(_perchListScroll, GUILayout.MaxHeight(360f));

        LevelSelectDesignerData.DesignerPerchMarker toDelete = null;

        foreach (var marker in _data.perchMarkers)
        {
            if (marker == null) continue;

            bool open = marker.perchId == _selectedPerchId;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(open ? $"▾ {marker.perchId}" : $"▸ {marker.perchId}",
                                     EditorStyles.miniButton))
                {
                    _selectedPerchId = open ? null : marker.perchId;
                    Repaint();
                }
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22f)))
                    toDelete = marker;
            }

            if (!open) continue;

            EditorGUI.indentLevel++;
            DrawPerchMarkerFields(marker);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndScrollView();

        if (toDelete != null)
        {
            Undo.RecordObject(_data, "Delete Perch Marker");
            _data.perchMarkers.Remove(toDelete);
            if (_selectedPerchId == toDelete.perchId) _selectedPerchId = null;
            MarkDirty();
            RebuildPerchMarkers();
            Repaint();
        }

        EditorGUILayout.Space(2);
        if (GUILayout.Button("Rebuild Perch Markers", EditorStyles.miniButton))
            RebuildPerchMarkers();
    }

    /// <summary>One marker's own fields — where it stands, how high, and its perch block.</summary>
    private void DrawPerchMarkerFields(LevelSelectDesignerData.DesignerPerchMarker marker)
    {
        EditorGUI.BeginChangeCheck();

        Vector2 at = EditorGUILayout.Vector2Field(
            new GUIContent("Position", "Where it stands on the map, in world X and Z."),
            marker.positionXZ);

        float height = EditorGUILayout.FloatField(
            new GUIContent("Height", "How far her feet stand above the water here. Put it on " +
                                     "top of whatever she should look to be standing on."),
            marker.height);

        var perch = DrawPerchBlock(marker.perch);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Perch Marker");
            marker.positionXZ = at;
            marker.height     = height;
            marker.perch      = perch;
            MarkDirty();
            RebuildPerchMarkers();
        }
    }

    /// <summary>
    /// The marker picked in Select mode, shown on its own. Perch mode lists every marker instead,
    /// so this stays out of the way there.
    /// </summary>
    private void DrawSelectedPerchProps()
    {
        if (_mode == DesignerMode.Perch) return;

        var marker = SelectedPerchMarker;
        if (marker == null) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"Angel Perch — {marker.perchId}", EditorStyles.boldLabel);
        DrawPerchMarkerFields(marker);
    }

    /// <summary>
    /// How many perches riding on towers are set to open the map. Counted so the warning covers
    /// every perch there is, not only the markers listed above it.
    /// </summary>
    private int CountTowerStartPerches()
    {
        int count = 0;

        bool Starts(LevelSelectDesignerData.DesignerPerch p) => p != null && p.enabled && p.startPerch;

        foreach (var pool in _data.pools)
            if (pool != null && pool.hasTower && Starts(pool.towerPerch)) count++;

        foreach (var outpost in _data.outposts)
            if (outpost != null && Starts(outpost.towerPerch)) count++;

        foreach (var rim in _data.rimNodes)
        {
            if (rim == null) continue;
            foreach (var side in new[] { -1, 1 })
            {
                var topping = rim.ToppingOn(side);
                if (topping != null &&
                    topping.topper == LevelSelectDesignerData.RimNodeTopper.LollipopTower &&
                    Starts(topping.perch)) count++;
            }
        }

        foreach (var wall in _data.walls)
        {
            if (wall == null) continue;
            foreach (var node in wall.nodes)
                if (node != null && node.hasTower && Starts(node.towerPerch)) count++;
        }

        return count;
    }

    // ─────────────────────────────────────────────
    // GENERATION
    // ─────────────────────────────────────────────

    /// <summary>
    /// Builds the free markers: one empty object each, under a parent of their own, carrying the
    /// perch and — where she can be talked to — the pair that prompts for it. They stand on the
    /// water, and the authored height takes her feet up off it.
    /// </summary>
    private void RebuildPerchMarkers()
    {
        var parent = FindOrCreateParent(PerchMarkersParent);

        // Cleared and built afresh, the way every other generated piece is: a marker deleted in
        // the panel has to leave the scene too.
        for (int i = parent.transform.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(parent.transform.GetChild(i).gameObject);

        if (_data?.perchMarkers == null) return;

        float waterY = -(_data.waterFilled ? _data.waterLevel : 0f);

        foreach (var marker in _data.perchMarkers)
        {
            if (marker?.perch == null || !marker.perch.enabled) continue;

            var go = new GameObject($"AngelPerch_{SanitiseAssetName(marker.perchId)}");
            Undo.RegisterCreatedObjectUndo(go, "Generate Perch Marker");
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(marker.positionXZ.x, waterY, marker.positionXZ.y);

            ApplyPerch(go, marker.perch, Vector3.up * marker.height);
        }
    }

    // ─────────────────────────────────────────────
    // THE ANGEL ON HER START PERCH
    // ─────────────────────────────────────────────

    /// <summary>
    /// Stands the angel already in the scene on the perch marked Start Here, in her perched pose,
    /// so the map in the editor shows her where the first frame of play puts her. Run at the end
    /// of Generate, once everything she could stand on is built and settled on the landscape. No
    /// start perch, or no angel in the scene, and nothing is touched.
    /// </summary>
    private void PlaceAngelOnStartPerch()
    {
        var angel = FindAnyObjectByType<AngelCompanion>();
        if (angel == null) return;

        // Searched for rather than read off AngelPerchPoint.All: that list fills in OnEnable, and
        // a perch's OnEnable never runs outside play mode.
        var start = FindObjectsByType<AngelPerchPoint>()
            .FirstOrDefault(p => p.enabled && p.gameObject.activeInHierarchy && p.IsStartPerch);
        if (start == null) return;

        // Every transform in her, not only the root — the pose moves her bones. Recorded, so it
        // all goes back on Generate's one undo and is kept as the scene's overrides on her prefab.
        Undo.RecordObjects(angel.GetComponentsInChildren<Transform>(true), "Place Angel On Start Perch");

        var boat = FindAnyObjectByType<LevelSelectBoatControl>();
        angel.StandOn(start, boat != null ? boat.BoatTransform : null);

        var body = angel.BodyAnimator;
        var clip = body != null ? ClipForState(body, angel.PerchedState) : null;
        if (clip == null)
        {
            Debug.LogWarning($"[LevelSelectDesigner] Stood the angel on '{start.name}', but found no " +
                             $"'{angel.PerchedState}' clip on her animator to pose her with.", angel);
            return;
        }

        // Its first frame: CrossFadeInFixedTime starts the state from 0, so this is the pose play
        // opens on.
        clip.SampleAnimation(body.gameObject, 0f);
    }

    /// <summary>
    /// The clip a state plays, by the state's name — the name AngelCompanion plays it by, which
    /// need not be the clip's. Null when there is no such state or it holds no plain clip.
    /// </summary>
    private static AnimationClip ClipForState(Animator animator, string state)
    {
        var controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        if (controller == null || string.IsNullOrEmpty(state)) return null;

        foreach (var layer in controller.layers)
            foreach (var child in layer.stateMachine.states)
                if (child.state != null && child.state.name == state)
                    return child.state.motion as AnimationClip;

        return null;
    }
}

#endif
