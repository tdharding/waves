using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

#if UNITY_EDITOR

/// <summary>
/// Installation outposts: square blocks standing beside a river run, with a wall round the top.
///
/// An outpost is placed at a point along a path and stands on one bank of it. Select a node on a
/// river and press Add Outpost to place one there; clicking an outpost's point selects it, and its
/// numbers are edited in the left panel.
///
/// On the canvas the footprint is drawn against the drawn path. In the scene it is placed against
/// the generated run itself — the run is swept along a smoothed curve, not the straight lines
/// between the nodes, so only the run knows exactly where its outer edge is.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const string OutpostsParent = "OUTPOSTS";

    private string _selectedOutpostId;

    // ─────────────────────────────────────────────
    // CANVAS
    // ─────────────────────────────────────────────

    /// <summary>
    /// Stands a new outpost beside the river at one of its nodes. It goes on the left bank; the
    /// Side setting in its panel moves it across.
    /// </summary>
    private void AddOutpostAt(LevelSelectDesignerData.DesignerPath path, string nodeId)
    {
        int index = path.nodeIds.IndexOf(nodeId);
        if (index < 0 || path.nodeIds.Count < 2) return;

        Undo.RecordObject(_data, "Add Outpost");
        var outpost = new LevelSelectDesignerData.DesignerOutpost
        {
            outpostId = Guid.NewGuid().ToString(),
            pathId    = path.pathId,
            pathT     = index / (float)(path.nodeIds.Count - 1),
            side      = LevelSelectDesignerData.OutpostSide.Left,
        };
        _data.outposts.Add(outpost);

        // Open its settings under the node it was added at, with that node's row left open.
        _selectedOutpostId  = outpost.outpostId;
        _selectedPathId     = path.pathId;
        _selectedNodeId     = nodeId;
        _selectedObstacleId = null;
        GUI.FocusControl(null);

        MarkDirty();
        Repaint();
    }

    private void DrawOutposts()
    {
        if (Event.current.type != EventType.Repaint) return;

        var towersAt = new List<Vector2>();
        foreach (var outpost in _data.outposts)
        foreach (var bank in OutpostBanks(outpost.side))
        {
            if (!TryOutpostFootprint(outpost, bank, out var outer, out var floor)) continue;

            bool selected = outpost.outpostId == _selectedOutpostId;
            var  wall     = selected ? new Color(1f, 1f, 1f, 0.9f) : new Color(0.85f, 0.7f, 0.45f, 0.85f);
            var  inside   = selected ? new Color(0.55f, 0.55f, 0.55f, 0.9f) : new Color(0.35f, 0.28f, 0.18f, 0.85f);

            // Solid fills: the whole block in the wall colour, the floor inside it darker, so the
            // wall reads as the band left showing round the edge.
            Handles.color = Faded(wall);
            Handles.DrawAAConvexPolygon(outer);
            if (floor != null)
            {
                Handles.color = Faded(inside);
                Handles.DrawAAConvexPolygon(floor);
            }

            // The tower, as a solid dot the size of its orb where its base stands.
            if (TryOutpostTowerCanvas(outpost, bank, towersAt))
            {
                var   s   = outpost.OnBank(bank);
                float orb = TowerOrbRadius(s.towerKind, s.tower, s.turret, s.towerScale);
                Handles.color = Faded(wall);
                foreach (var towerAt in towersAt)
                    Handles.DrawSolidDisc(towerAt, Vector3.forward, Mathf.Max(2f, orb * _zoom));
            }
        }

        // Selection points last, so no footprint or tower covers one.
        foreach (var outpost in _data.outposts)
        foreach (var bank in OutpostBanks(outpost.side))
        {
            if (!TryOutpostPointCanvas(outpost, bank, out Vector2 at)) continue;
            bool selected = outpost.outpostId == _selectedOutpostId;
            Handles.color = Faded(new Color(0.1f, 0.1f, 0.1f, 0.9f));
            Handles.DrawSolidDisc(at, Vector3.forward, OutpostPointRadius + 1.5f);
            Handles.color = Faded(selected ? Color.white : new Color(1f, 0.6f, 0.15f, 1f));
            Handles.DrawSolidDisc(at, Vector3.forward, OutpostPointRadius);
        }
    }

    private const float OutpostPointRadius = 5f;

    /// <summary>
    /// A click on an outpost's point, in any mode, selects it and opens its settings at the top of
    /// the left panel. A click anywhere else closes them again.
    /// </summary>
    private bool HandleOutpostPointClick(Event e)
    {
        if (e.type != EventType.MouseDown || e.button != 0) return false;

        string hit = FindOutpostPointAtCanvas(e.mousePosition);
        if (hit == null)
        {
            if (_selectedOutpostId != null)
            {
                _selectedOutpostId = null;
                Repaint();
            }
            return false;
        }

        _selectedOutpostId   = hit;
        _selectedStructureId = null;
        _selectedPathId      = null;
        _selectedNodeId      = null;
        _selectedObstacleId  = null;
        _selectedArenaNodeId = null;
        _selectedShopNodeId  = null;
        _selectedEntranceIdx = -1;
        _leftScroll          = Vector2.zero;
        GUI.FocusControl(null);
        Repaint();
        e.Use();
        return true;
    }

    private string FindOutpostPointAtCanvas(Vector2 canvas)
    {
        float reach = (OutpostPointRadius + 3f) * (OutpostPointRadius + 3f);
        for (int i = _data.outposts.Count - 1; i >= 0; i--)
        {
            var outpost = _data.outposts[i];
            foreach (var bank in OutpostBanks(outpost.side))
            {
                if (!TryOutpostPointCanvas(outpost, bank, out Vector2 at)) continue;
                if ((canvas - at).sqrMagnitude <= reach) return outpost.outpostId;
            }
        }
        return null;
    }

    /// <summary>Canvas position of an outpost's selection point: the centre of its floor.</summary>
    private bool TryOutpostPointCanvas(LevelSelectDesignerData.DesignerOutpost outpost,
                                       LevelSelectDesignerData.OutpostSide bank, out Vector2 canvas)
    {
        canvas = default;
        if (outpost == null) return false;
        var path = _data.paths.Find(p => p.pathId == outpost.pathId);
        if (path == null || path.nodeIds.Count < 2) return false;

        PathFrameAt(path, outpost.pathT, out Vector3 centre, out Vector3 forward);
        Vector3 away = OutpostAway(forward, bank);
        Vector3 edge = centre + away * OutpostStandOff(outpost, path, bank);
        var s = outpost.OnBank(bank);

        canvas = WorldToCanvas(edge + away * (s.depth * 0.5f));
        return true;
    }

    private string FindOutpostAtCanvas(Vector2 canvas)
    {
        // Last drawn is on top, so it is the one a click lands on.
        for (int i = _data.outposts.Count - 1; i >= 0; i--)
        {
            var outpost = _data.outposts[i];
            foreach (var bank in OutpostBanks(outpost.side))
            {
                if (!TryOutpostFootprint(outpost, bank, out var outer, out _)) continue;
                if (InsideConvex(canvas, outer)) return outpost.outpostId;
            }
        }
        return null;
    }

    /// <summary>
    /// Canvas corners of an outpost's footprint, and of the floor inside its wall (null when it
    /// has no wall). Measured off the drawn path, which is close to the run but not on it.
    /// </summary>
    private bool TryOutpostFootprint(LevelSelectDesignerData.DesignerOutpost outpost,
                                     LevelSelectDesignerData.OutpostSide bank,
                                     out Vector3[] outer, out Vector3[] floor)
    {
        outer = null;
        floor = null;
        if (outpost == null) return false;

        var path = _data.paths.Find(p => p.pathId == outpost.pathId);
        if (path == null || path.nodeIds.Count < 2) return false;

        PathFrameAt(path, outpost.pathT, out Vector3 centre, out Vector3 forward);
        Vector3 away = OutpostAway(forward, bank);
        Vector3 edge = centre + away * OutpostStandOff(outpost, path, bank);
        var s = outpost.OnBank(bank);

        outer = CanvasRect(edge, forward, away, s.width * 0.5f, 0f, s.depth);

        float t = Mathf.Min(s.wallThickness,
                            Mathf.Min(s.width * 0.5f, s.depth * 0.5f) - 0.001f);
        if (t > 0.0001f && s.wallHeight > 0.0001f)
            floor = CanvasRect(edge, forward, away, s.width * 0.5f - t, t, s.depth - t);

        return true;
    }

    private bool TryOutpostTowerCanvas(LevelSelectDesignerData.DesignerOutpost outpost,
                                       LevelSelectDesignerData.OutpostSide bank, List<Vector2> canvas)
    {
        canvas.Clear();
        var path = _data.paths.Find(p => p.pathId == outpost.pathId);
        if (path == null || path.nodeIds.Count < 2) return false;

        PathFrameAt(path, outpost.pathT, out Vector3 centre, out Vector3 forward);
        Vector3 away = OutpostAway(forward, bank);
        Vector3 edge = centre + away * OutpostStandOff(outpost, path, bank);
        var s = outpost.OnBank(bank);

        if (!s.TowerOnAColumn)
        {
            canvas.Add(WorldToCanvas(edge + forward * s.towerOffset.x
                                          + away * (s.depth * 0.5f + s.towerOffset.y)));
            return true;
        }

        // On a column it stands in the middle of the wall's back corner, down or up the path the
        // way it was drawn.
        float c = InstallationOutpostMesh.WallInset(s.width * 0.5f, s.depth,
                                                    s.wallThickness, s.wallHeight) * 0.5f;
        float along = s.width * 0.5f - c;
        Vector3 back = edge + away * (s.depth - c);
        if (s.columnDownstream && s.towerOnDownstream) canvas.Add(WorldToCanvas(back + forward * along));
        if (s.columnUpstream   && s.towerOnUpstream)   canvas.Add(WorldToCanvas(back - forward * along));
        return true;
    }

    private Vector3[] CanvasRect(Vector3 edge, Vector3 forward, Vector3 away,
                                 float halfAlong, float awayFrom, float awayTo)
    {
        return new[]
        {
            (Vector3)WorldToCanvas(edge - forward * halfAlong + away * awayFrom),
            (Vector3)WorldToCanvas(edge + forward * halfAlong + away * awayFrom),
            (Vector3)WorldToCanvas(edge + forward * halfAlong + away * awayTo),
            (Vector3)WorldToCanvas(edge - forward * halfAlong + away * awayTo),
        };
    }

    private static bool InsideConvex(Vector2 p, Vector3[] poly)
    {
        float sign = 0f;
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
            float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            if (Mathf.Abs(cross) < 1e-6f) continue;
            if (sign == 0f) sign = Mathf.Sign(cross);
            else if (Mathf.Sign(cross) != sign) return false;
        }
        return true;
    }

    /// <summary>A point on the drawn path and the flat direction it runs in there.</summary>
    private void PathFrameAt(LevelSelectDesignerData.DesignerPath path, float t,
                             out Vector3 position, out Vector3 forward)
    {
        position = GetWorldPosOnPathRaw(path, t);
        forward  = Vector3.forward;

        int   segments = path.nodeIds.Count - 1;
        int   seg      = Mathf.Min(Mathf.FloorToInt(Mathf.Clamp(t * segments, 0, segments)), segments - 1);
        Vector3 dir    = _data.NodeWorldPosition(path.nodeIds[seg + 1]) -
                         _data.NodeWorldPosition(path.nodeIds[seg]);
        dir.y = 0f;
        if (dir.sqrMagnitude > 1e-8f) forward = dir.normalized;
    }

    // Right is the run's own right, Cross(up, forward) — the same frame the run is swept on.
    private static Vector3 OutpostAway(Vector3 forward, LevelSelectDesignerData.OutpostSide side)
    {
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        return side == LevelSelectDesignerData.OutpostSide.Right ? right : -right;
    }

    // The banks an outpost stands on: its one side, or left then right for Both.
    private static IEnumerable<LevelSelectDesignerData.OutpostSide> OutpostBanks(
        LevelSelectDesignerData.OutpostSide side)
    {
        if (side != LevelSelectDesignerData.OutpostSide.Right) yield return LevelSelectDesignerData.OutpostSide.Left;
        if (side != LevelSelectDesignerData.OutpostSide.Left)  yield return LevelSelectDesignerData.OutpostSide.Right;
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    private void DrawSelectedOutpostProps()
    {
        var outpost = _data.outposts.Find(o => o.outpostId == _selectedOutpostId);
        if (outpost == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Installation Outpost", EditorStyles.boldLabel);

        var path = _data.paths.Find(p => p.pathId == outpost.pathId);
        EditorGUILayout.LabelField($"On {(path != null ? PathDisplayName(path) : "(missing path)")}",
                                   EditorStyles.miniLabel);

        EditorGUI.BeginChangeCheck();
        var side = (LevelSelectDesignerData.OutpostSide)EditorGUILayout.EnumPopup(
            new GUIContent("Side", "Which bank it stands on, looking down the path the way it was drawn. " +
                                   "Both stands a block on each bank, each with its own preset and settings."),
            outpost.side);
        float pathT = EditorGUILayout.Slider(
            new GUIContent("Along Path", "Where it stands along the path, start to end."),
            outpost.pathT, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Outpost");
            outpost.side  = side;
            outpost.pathT = pathT;
            MarkDirty();
            RebuildAfterTowerEdit(outpost, () => RebuildOutpost(outpost));
        }

        if (OcclusionToggle(ref outpost.occlusion, ref outpost.occlusionStyle)) RebuildOutpost(outpost);

        // The first time it is on both banks, the Left bank starts as a copy of this one. Also
        // catches an outpost set to Both before the banks had settings of their own — without
        // this, both sections edit the same settings and every pick lands on both banks.
        if (outpost.side == LevelSelectDesignerData.OutpostSide.Both && outpost.bothLeft == null)
        {
            Undo.RecordObject(_data, "Edit Outpost");
            outpost.bothLeft      = outpost.Clone();
            outpost.bothLeft.side = LevelSelectDesignerData.OutpostSide.Left;
            MarkDirty();
        }

        // On both banks each block has its own preset and settings.
        if (outpost.side == LevelSelectDesignerData.OutpostSide.Both)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Left Bank", EditorStyles.boldLabel);
            DrawOutpostBankFields(outpost, outpost.OnBank(LevelSelectDesignerData.OutpostSide.Left),
                                  LevelSelectDesignerData.OutpostSide.Left);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Right Bank", EditorStyles.boldLabel);
        }
        DrawOutpostBankFields(outpost, outpost,
                              outpost.side == LevelSelectDesignerData.OutpostSide.Left
                                  ? LevelSelectDesignerData.OutpostSide.Left
                                  : LevelSelectDesignerData.OutpostSide.Right);

        EditorGUILayout.Space(2);
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("Delete Outpost"))
        {
            Undo.RecordObject(_data, "Delete Outpost");
            _data.outposts.Remove(outpost);
            _selectedOutpostId = null;
            MarkDirty();
        }
        GUI.backgroundColor = Color.white;
    }

    /// <summary>
    /// One bank's preset row and what stays with the placement — the tower's Placement Scale and
    /// its perch. Everything else (block, walls, columns, tower) is set in the preset,
    /// in the preset editor. <paramref name="s"/> is that bank's settings (the outpost itself, or
    /// its Left bank when Side is Both); any change rebuilds the whole <paramref name="outpost"/>.
    /// </summary>
    private void DrawOutpostBankFields(LevelSelectDesignerData.DesignerOutpost outpost,
                                       LevelSelectDesignerData.DesignerOutpost s,
                                       LevelSelectDesignerData.OutpostSide bank)
    {
        DrawOutpostPresetRow(s, () => RebuildOutpost(outpost));

        EditorGUI.BeginChangeCheck();
        float towerScale = DrawLollipopScaleField(s.towerScale);
        var   perch      = DrawPerchBlock(s.towerPerch);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Outpost");
            s.towerScale = towerScale;
            s.towerPerch = perch;
            MarkDirty();
            RebuildAfterTowerEdit(outpost, () => RebuildOutpost(outpost));
        }

        DrawOutpostOpeningFields(outpost, s, bank);
    }

    /// <summary>
    /// The gap in the wall facing the river and the slope from it down onto the walkway. Kept
    /// with the placement rather than the preset, and only open where a river walkway runs
    /// along this bank at the outpost.
    /// </summary>
    private void DrawOutpostOpeningFields(LevelSelectDesignerData.DesignerOutpost outpost,
                                          LevelSelectDesignerData.DesignerOutpost s,
                                          LevelSelectDesignerData.OutpostSide bank)
    {
        var  path       = _data.paths.Find(p => p.pathId == outpost.pathId);
        bool hasWalkway = OuterWalkwayBeside(path, outpost.pathT, bank) != null;

        GUI.enabled = hasWalkway;
        EditorGUI.BeginChangeCheck();
        bool opening = EditorGUILayout.Toggle(
            new GUIContent("Opening", "A gap in the wall facing the river, from the floor up to the " +
                                      "wall's top. Needs a river walkway along this bank."),
            s.opening);

        float width = s.openingWidth, offset = s.openingOffset;
        bool  slope = s.slope;
        var   way   = s.slopeWay;
        float landing = s.landingLength, slopeLength = s.slopeLength, slopeWidth = s.slopeWidth;

        if (opening)
        {
            EditorGUI.indentLevel++;
            width = Mathf.Max(0.01f, EditorGUILayout.FloatField(
                new GUIContent("Width", "How wide the gap is, along the river."), width));
            offset = EditorGUILayout.FloatField(
                new GUIContent("Offset Along", "Moves the gap along the wall from its middle — down " +
                                               "the path the way it was drawn when positive. Kept " +
                                               "clear of the wall's corners."), offset);
            slope = EditorGUILayout.Toggle(
                new GUIContent("Slope", "Flat in front of the opening, then straight down along the " +
                                        "walkway to its top."), slope);
            if (slope)
            {
                EditorGUI.indentLevel++;
                way = (LevelSelectDesignerData.OutpostSlopeWay)EditorGUILayout.EnumPopup(
                    new GUIContent("Runs", "Which way the slope runs along the walkway from the " +
                                           "opening, looking down the path the way it was drawn."), way);
                landing = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent("Landing Length", "How long the flat part is, from the far edge of " +
                                                     "the opening — never shorter than the opening is " +
                                                     "wide."), landing));
                slopeLength = Mathf.Max(0.01f, EditorGUILayout.FloatField(
                    new GUIContent("Slope Length", "How long the slope is, from the end of the flat " +
                                                   "part down to the walkway's top."), slopeLength));
                slopeWidth = Mathf.Max(0.01f, EditorGUILayout.FloatField(
                    new GUIContent("Slope Width", "How far the slope reaches out from the outpost's " +
                                                  "face, toward the river — its own width, not the " +
                                                  "walkway's."), slopeWidth));
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }
        GUI.enabled = true;

        if (!hasWalkway)
            EditorGUILayout.LabelField("Opening needs a River Walkway along this bank here.",
                                       EditorStyles.wordWrappedMiniLabel);

        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(_data, "Edit Outpost Opening");
        s.opening       = opening;
        s.openingWidth  = width;
        s.openingOffset = offset;
        s.slope         = slope;
        s.slopeWay      = way;
        s.landingLength = landing;
        s.slopeLength   = slopeLength;
        s.slopeWidth    = slopeWidth;
        MarkDirty();
        RebuildAfterTowerEdit(outpost, () => RebuildOutpost(outpost));
    }

    // One back corner's column toggle, with its Lollipop toggle beside it while the column is on.
    internal static void DrawOutpostColumnRow(string label, string way, ref bool column, ref bool tower)
    {
        EditorGUILayout.BeginHorizontal();
        column = EditorGUILayout.Toggle(
            new GUIContent(label, $"A round column on the back corner {way} the path the way it was " +
                                  "drawn, centred in the wall's corner and running all the way down."),
            column);
        if (column)
            tower = EditorGUILayout.ToggleLeft(
                new GUIContent("Tower", "Stands the tower on this column's top."),
                tower, GUILayout.Width(80));
        EditorGUILayout.EndHorizontal();
    }

    internal const string OutpostPresetFolder ="Assets/ScriptsData/DataScripts/InstallationOutpostPresets";

    /// <summary>
    /// The outpost's preset: pick one from the dropdown to put its settings on this outpost, Edit
    /// to open it in the preset editor (every outpost using it changes with it), or New to make one
    /// from this outpost's settings and open that. Where the outpost stands is never part of a
    /// preset.
    /// </summary>
    private void DrawOutpostPresetRow(LevelSelectDesignerData.DesignerOutpost outpost,
                                      System.Action onLoaded = null)
    {
        var picked = LevelSelectPresetEditorWindow.DrawPresetLink(
            new GUIContent("Preset", "The outpost presets kept in " + OutpostPresetFolder +
                                     ". Picking one puts its settings on this outpost; Edit changes them."),
            outpost.preset, OutpostPresetFolder, "Outpost",
            () =>
            {
                var made = CreateInstance<InstallationOutpostPreset>();
                made.CopyFrom(outpost);
                return made;
            },
            made =>
            {
                if (_data == null) return;
                Undo.RecordObject(_data, "New Outpost Preset");
                outpost.preset = made;
                MarkDirty();
                Repaint();
            });

        if (picked != outpost.preset)
        {
            Undo.RecordObject(_data, "Load Outpost Preset");
            outpost.preset = picked;
            if (picked != null)
                picked.ApplyTo(outpost);
            MarkDirty();
            if (onLoaded != null) onLoaded();
            else                  RebuildOutpost(outpost);
            GUI.FocusControl(null);
        }

        if (outpost.preset == null)
            EditorGUILayout.LabelField("No preset — New saves this outpost's settings as one to edit.",
                                       EditorStyles.wordWrappedMiniLabel);
    }

    // ─────────────────────────────────────────────
    // GENERATION
    // ─────────────────────────────────────────────

    /// <summary>
    /// Builds every outpost. Runs after the river runs, because an outpost is placed against the
    /// run it stands beside.
    /// </summary>
    private void GenerateOutposts(GameObject outpostsParent)
    {
        foreach (var outpost in _data.outposts)
        {
            if (outpost == null || string.IsNullOrEmpty(outpost.outpostId)) continue;
            BuildOutpostBlocks(outpost, outpostsParent.transform);
        }
    }

    /// <summary>Rebuilds one outpost already standing in the scene, if it is there.</summary>
    private void RebuildOutpost(LevelSelectDesignerData.DesignerOutpost outpost)
    {
        var record = FindObjectsOfType<InstallationOutpostMesh>()
            .FirstOrDefault(r => r.outpostId == outpost.outpostId);
        if (record == null) return;

        BuildOutpostBlocks(outpost, record.transform.parent);

        // A walkway beside it runs straight along its face, which may just have moved.
        if ((_data.walkways  != null && _data.walkways.Exists(w => w != null && w.enabled)) ||
            (_data.walkways2 != null && _data.walkways2.Exists(w => w != null && w.enabled)))
            RebuildWalkwaysOnPath(outpost.pathId, false);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Builds the outpost's block on each bank it stands on. Blocks already in the scene are
    /// reused — the one on the same bank first, then any spare — so a side change moves a block
    /// rather than remaking it; a block on a bank it no longer stands on is removed.
    /// </summary>
    private void BuildOutpostBlocks(LevelSelectDesignerData.DesignerOutpost outpost, Transform parent)
    {
        var spare = FindObjectsOfType<InstallationOutpostMesh>()
            .Where(r => r.outpostId == outpost.outpostId)
            .ToList();
        var banks = OutpostBanks(outpost.side).ToList();

        var blocks = new InstallationOutpostMesh[banks.Count];
        for (int i = 0; i < banks.Count; i++)
        {
            blocks[i] = spare.FirstOrDefault(r => r.bank == banks[i]);
            if (blocks[i] != null) spare.Remove(blocks[i]);
        }
        for (int i = 0; i < banks.Count; i++)
        {
            if (blocks[i] != null) continue;
            if (spare.Count > 0)
            {
                blocks[i] = spare[0];
                spare.RemoveAt(0);
                continue;
            }

            var go = new GameObject(OutpostMeshName(outpost, banks[i]));
            Undo.RegisterCreatedObjectUndo(go, "Generate Outpost");
            go.transform.SetParent(parent, false);
            blocks[i] = go.AddComponent<InstallationOutpostMesh>();
        }

        foreach (var leftover in spare)
            Undo.DestroyObjectImmediate(leftover.gameObject);

        for (int i = 0; i < banks.Count; i++)
            BuildOutpost(outpost, blocks[i].gameObject, banks[i]);
    }

    /// <summary>
    /// Builds one bank's block. Where it stands comes from the outpost; how it is built comes
    /// from that bank's settings (<see cref="LevelSelectDesignerData.DesignerOutpost.OnBank"/>).
    /// </summary>
    private void BuildOutpost(LevelSelectDesignerData.DesignerOutpost outpost, GameObject go,
                              LevelSelectDesignerData.OutpostSide bank)
    {
        var path = _data.paths.Find(p => p.pathId == outpost.pathId);
        if (path == null || path.nodeIds.Count < 2)
        {
            Debug.LogWarning($"[LevelSelectDesigner] Outpost '{outpost.outpostId}' has no path — skipped.");
            return;
        }

        var profile = _data.ProfileFor(path.riverName);
        OutpostSite(outpost, path, out Vector3 rimTop, out Vector3 forward);

        Vector3 away = OutpostAway(forward, bank);
        string  name = OutpostMeshName(outpost, bank);

        Undo.RecordObject(go.transform, "Place Outpost");
        go.name               = name;
        // Out past the run's outer edge — and past a walkway, where one runs along this bank.
        go.transform.position = rimTop + away * OutpostStandOff(outpost, path, bank);
        go.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        go.transform.localScale = Vector3.one;

        var s = outpost.OnBank(bank);

        // A lollipop is merged into the block's mesh; a turret moves, so it stands as its own
        // pieces on the same bases instead (see RefreshOutpostTurrets).
        bool isTurret = s.towerKind == LevelSelectDesignerData.TowerKind.Turret;

        // Down to the same underside as the runs, which are built from their rim top too.
        var mesh = SaveGeneratedMesh(name, InstallationOutpostMesh.Build(
            s.width, s.depth, s.height, _data.RunDepth,
            s.wallThickness, s.wallHeight,
            isTurret ? null : s.tower?.Scaled(s.towerScale),
            TowerOffsetInMesh(s, go.transform, forward),
            StoneShadingForBuild,
            ColumnsInMesh(s, go.transform, forward), s.columnRadius, s.columnHeight,
            0f, OpeningInMesh(s, go.transform, forward, OuterWalkwayBeside(path, outpost.pathT, bank))));
        if (mesh == null) return;

        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        EditorUtility.SetDirty(filter);

        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _data.arenaWallMaterial != null
                                ? _data.arenaWallMaterial : _data.riverMaterial;
        EditorUtility.SetDirty(renderer);

        var record = go.GetComponent<InstallationOutpostMesh>();
        record.outpostId     = outpost.outpostId;
        record.bank          = bank;
        record.meshAssetName = name;
        EditorUtility.SetDirty(record);

        // The outpost's tower is part of this very mesh rather than an object of its own, so the
        // perch goes on the block and reaches up to the orb from where the tower was appended —
        // the same base the mesh used (floor or column top), plus the tower's own height. With a
        // tower on both columns there is still the one perch, on the first of them.
        var towerBases = InstallationOutpostMesh.TowerBases(
            s.width, s.depth, s.height, s.wallThickness, s.wallHeight,
            TowerOffsetInMesh(s, go.transform, forward),
            ColumnsInMesh(s, go.transform, forward), s.columnHeight);
        ApplyPerch(go, s.towerPerch, towerBases[0] + Vector3.up *
                   TowerTopY(s.towerKind, s.tower, s.turret, s.towerScale));

        RefreshOutpostTurrets(s, go, isTurret ? towerBases : null, name);

        ApplyOcclusion(go, outpost.occlusion, outpost.occlusionStyle);
    }

    /// <summary>
    /// Where an outpost's river runs past it: a point on the centre of the run's rim top, and the
    /// flat direction the run is heading there.
    ///
    /// The drawn path only gives the rough spot. The run was swept along a smoothed curve, so the
    /// spot is carried onto the nearest point of that curve — the generated run of the same river
    /// closest to it — and the outpost lands on the run's real edge. With no run generated yet it
    /// falls back to the drawn path and says so.
    /// </summary>
    private void OutpostSite(LevelSelectDesignerData.DesignerOutpost outpost,
                             LevelSelectDesignerData.DesignerPath path,
                             out Vector3 rimTop, out Vector3 forward)
    {
        PathFrameAt(path, outpost.pathT, out rimTop, out forward);
        Vector3 rough = rimTop;

        float best = float.MaxValue;
        foreach (var run in FindObjectsOfType<RiverRunMesh>())
        {
            if (run.knots.Count < 2) continue;
            if (!string.IsNullOrEmpty(path.riverName) && run.riverName != path.riverName) continue;

            var    spline = run.ToSpline();
            float3 local  = run.transform.InverseTransformPoint(rough);
            SplineUtility.GetNearestPoint(spline, local, out float3 nearest, out float t);

            Vector3 world = run.transform.TransformPoint(nearest);
            float   dist  = new Vector2(world.x - rough.x, world.z - rough.z).sqrMagnitude;
            if (dist >= best) continue;

            Vector3 tangent = run.transform.TransformDirection((Vector3)spline.EvaluateTangent(t));
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 1e-8f) continue;

            best    = dist;
            rimTop  = world;
            forward = tangent.normalized;
        }

        if (best == float.MaxValue)
            Debug.LogWarning($"[LevelSelectDesigner] Outpost '{outpost.outpostId}' found no generated " +
                             $"run for river '{path.riverName}' — placed against the drawn path instead.");
    }

    // The mesh's x runs one way on the right bank and the other on the left, so Offset Along is
    // turned into it here — it always means down the path the way it was drawn.
    private static Vector2 TowerOffsetInMesh(LevelSelectDesignerData.DesignerOutpost outpost,
                                             Transform block, Vector3 forward)
    {
        float along = Vector3.Dot(block.right, forward) >= 0f ? 1f : -1f;
        return new Vector2(outpost.towerOffset.x * along, outpost.towerOffset.y);
    }

    // The downstream and upstream columns turned onto the mesh's +x and -x ends — the same bank
    // correction as the offset.
    private static InstallationOutpostMesh.Columns ColumnsInMesh(LevelSelectDesignerData.DesignerOutpost outpost,
                                                                 Transform block, Vector3 forward)
    {
        bool downIsPlusX = Vector3.Dot(block.right, forward) >= 0f;
        return new InstallationOutpostMesh.Columns
        {
            plusX         = downIsPlusX ? outpost.columnDownstream  : outpost.columnUpstream,
            minusX        = downIsPlusX ? outpost.columnUpstream    : outpost.columnDownstream,
            towerOnPlusX  = downIsPlusX ? outpost.towerOnDownstream : outpost.towerOnUpstream,
            towerOnMinusX = downIsPlusX ? outpost.towerOnUpstream   : outpost.towerOnDownstream,
        };
    }

    // The opening and its slope turned onto the mesh's x, the same bank correction as the offset.
    // Nothing is cut without a walkway along this bank — the slope stands on the walkway.
    private static InstallationOutpostMesh.Opening OpeningInMesh(LevelSelectDesignerData.DesignerOutpost outpost,
                                                                 Transform block, Vector3 forward,
                                                                 LevelSelectDesignerData.DesignerWalkway walkway)
    {
        if (!outpost.opening || walkway == null) return default;

        float along = Vector3.Dot(block.right, forward) >= 0f ? 1f : -1f;
        float way   = outpost.slopeWay == LevelSelectDesignerData.OutpostSlopeWay.Downstream ? 1f : -1f;
        return new InstallationOutpostMesh.Opening
        {
            on            = true,
            centre        = outpost.openingOffset * along,
            width         = outpost.openingWidth,
            slope         = outpost.slope,
            slopeSign     = way * along,
            landing       = outpost.landingLength,
            slopeLength   = outpost.slopeLength,
            slopeWidth    = Mathf.Max(0.01f, outpost.slopeWidth),
            walkwayHeight = walkway.height,
        };
    }

    private const string OutpostTurretsChild = "Turrets";

    /// <summary>
    /// Stands a turret on each of <paramref name="bases"/> (the block's frame — floor or column
    /// tops, the same bases a lollipop is merged in at), under a "Turrets" child of the block,
    /// cleared and built afresh each time. Null bases just clears them, for a lollipop outpost.
    /// </summary>
    private void RefreshOutpostTurrets(LevelSelectDesignerData.DesignerOutpost outpost, GameObject go,
                                       List<Vector3> bases, string name)
    {
        RefreshTurrets(go, outpost.turret?.Scaled(outpost.towerScale), bases, name,
                       _data.arenaWallMaterial != null ? _data.arenaWallMaterial : _data.riverMaterial);
    }

    /// <summary>
    /// Stands <paramref name="turret"/> (already scaled) on each of <paramref name="bases"/>, in
    /// <paramref name="go"/>'s frame, under its "Turrets" child — cleared and built afresh each
    /// time. Null bases or turret just clears them. Pieces are named off <paramref name="name"/>.
    /// </summary>
    private void RefreshTurrets(GameObject go, TurretTower turret, List<Vector3> bases, string name,
                                Material material)
    {
        var old = go.transform.Find(OutpostTurretsChild);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        if (bases == null || bases.Count == 0 || turret == null) return;

        var container = new GameObject(OutpostTurretsChild);
        Undo.RegisterCreatedObjectUndo(container, "Generate Turrets");
        container.transform.SetParent(go.transform, false);

        for (int i = 0; i < bases.Count; i++)
        {
            var built = PlaceTurret(container.transform, $"{name}_Turret{i + 1}", turret, material);
            if (built == null) continue;

            built.transform.localPosition = bases[i];
            built.transform.localRotation = Quaternion.identity;
            built.transform.localScale    = Vector3.one;
        }
    }

    // Named off the outpost's id, which survives every regenerate — so the mesh asset is reused.
    // On both banks each block needs its own mesh, so the bank goes on the end.
    private static string OutpostMeshName(LevelSelectDesignerData.DesignerOutpost outpost,
                                          LevelSelectDesignerData.OutpostSide bank)
    {
        string name = $"Outpost_{SanitiseAssetName(outpost.outpostId)}";
        if (outpost.side != LevelSelectDesignerData.OutpostSide.Both) return name;
        return bank == LevelSelectDesignerData.OutpostSide.Right ? name + "_R" : name + "_L";
    }
}

#endif
