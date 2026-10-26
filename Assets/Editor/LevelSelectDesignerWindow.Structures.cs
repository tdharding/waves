using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Freestanding structures: a block or a round column dropped anywhere on the map, with something
/// on top — nothing, a lollipop tower, a turret, or an outpost.
///
/// Add ▸ Freestanding Structures places them: click the canvas to drop one, click one to select
/// it, drag to move it, Delete to remove it, D to duplicate it. Select mode picks and moves them
/// too. A structure picked is the only thing selected, so the left panel shows it alone.
///
/// Each stands on the landscape under its centre — the tile base plus any hill there, the same
/// ground a spike stands on — and is re-seated whenever the hills are synced. Its sides go down
/// the one Drop below that, so a slope never shows its underside.
///
/// The outpost top is a whole outpost's settings (presets, walls, columns, towers)
/// with the structure's own size: square walls on a block, a ring wall on a round column, with
/// its two columns at +x and -x either side of the turn. Scenery only — no collider.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    private const string StructuresParent = "FREESTANDINGSTRUCTURES";

    private string  _selectedStructureId;
    private bool    _isDraggingStructure;
    private Vector2 _structureDragOffset;

    private LevelSelectDesignerData.DesignerStructure SelectedStructure =>
        _data?.structures?.Find(s => s.structureId == _selectedStructureId);

    // ─────────────────────────────────────────────
    // SHAPE AND FRAME
    // ─────────────────────────────────────────────

    private static bool IsRound(LevelSelectDesignerData.DesignerStructure s)
        => s.shape == LevelSelectDesignerData.StructureShape.RoundColumn;

    private static bool HasOutpostTop(LevelSelectDesignerData.DesignerStructure s)
        => s.top == LevelSelectDesignerData.StructureTop.Outpost;

    private static Quaternion StructureRotation(LevelSelectDesignerData.DesignerStructure s)
        => Quaternion.Euler(0f, s.turn, 0f);

    // A block is built in the outpost's frame, z running from 0 to its depth, so its centre is
    // half a depth in. A round column is built round its centre.
    private static Vector3 StructureLocalCentre(LevelSelectDesignerData.DesignerStructure s)
        => IsRound(s) ? Vector3.zero : new Vector3(0f, 0f, s.depth * 0.5f);

    /// <summary>A point in the structure's own frame, flat on the map in world X and Z.</summary>
    private static Vector3 StructureLocalToWorldFlat(LevelSelectDesignerData.DesignerStructure s, Vector3 local)
    {
        Vector3 offset = StructureRotation(s) * (local - StructureLocalCentre(s));
        return new Vector3(s.positionXZ.x + offset.x, 0f, s.positionXZ.y + offset.z);
    }

    /// <summary>How far it reaches from its centre, roughly — for picking and duplicating.</summary>
    private static float StructureReach(LevelSelectDesignerData.DesignerStructure s)
        => IsRound(s) ? s.radius : 0.5f * Mathf.Sqrt(s.width * s.width + s.depth * s.depth);

    // The outpost top always takes the structure's own size.
    private static void SyncStructureOutpost(LevelSelectDesignerData.DesignerStructure s)
    {
        if (s.outpost == null) s.outpost = new LevelSelectDesignerData.DesignerOutpost();
        s.outpost.width  = s.width;
        s.outpost.depth  = s.depth;
        s.outpost.height = s.height;
    }

    private static float StructureWallThickness(LevelSelectDesignerData.DesignerStructure s)
        => HasOutpostTop(s) ? s.outpost.wallThickness : 0f;

    private static float StructureWallHeight(LevelSelectDesignerData.DesignerStructure s)
        => HasOutpostTop(s) ? s.outpost.wallHeight : 0f;

    private static float StructureWallInset(LevelSelectDesignerData.DesignerStructure s)
        => IsRound(s)
            ? RoundStructureMesh.WallInset(s.radius, StructureWallThickness(s), StructureWallHeight(s))
            : InstallationOutpostMesh.WallInset(s.width * 0.5f, s.depth,
                                                StructureWallThickness(s), StructureWallHeight(s));

    // The outpost's two columns: its "right" one at +x, its "left" one at -x.
    private static InstallationOutpostMesh.Columns StructureColumns(LevelSelectDesignerData.DesignerStructure s)
    {
        if (!HasOutpostTop(s)) return default;
        var o = s.outpost;
        return new InstallationOutpostMesh.Columns
        {
            plusX         = o.columnDownstream,
            minusX        = o.columnUpstream,
            towerOnPlusX  = o.towerOnDownstream,
            towerOnMinusX = o.towerOnUpstream,
        };
    }

    /// <summary>Which tower stands on top, or null for none.</summary>
    private static LevelSelectDesignerData.TowerKind? StructureTowerKind(LevelSelectDesignerData.DesignerStructure s)
        => s.top switch
        {
            LevelSelectDesignerData.StructureTop.Lollipop => LevelSelectDesignerData.TowerKind.Lollipop,
            LevelSelectDesignerData.StructureTop.Turret   => LevelSelectDesignerData.TowerKind.Turret,
            LevelSelectDesignerData.StructureTop.Outpost  => s.outpost.towerKind,
            _                                             => null,
        };

    // The tower's sizes at its placement scale, whichever block of settings it comes from.
    private static LollipopTower StructureLollipop(LevelSelectDesignerData.DesignerStructure s)
    {
        if (StructureTowerKind(s) != LevelSelectDesignerData.TowerKind.Lollipop) return null;
        return HasOutpostTop(s) ? s.outpost.tower?.Scaled(s.outpost.towerScale) : s.tower?.Scaled(s.towerScale);
    }

    private static TurretTower StructureTurret(LevelSelectDesignerData.DesignerStructure s)
    {
        if (StructureTowerKind(s) != LevelSelectDesignerData.TowerKind.Turret) return null;
        return HasOutpostTop(s) ? s.outpost.turret?.Scaled(s.outpost.towerScale) : s.turret?.Scaled(s.towerScale);
    }

    /// <summary>
    /// Where each tower's base stands, in the structure's own frame — the top's centre, or with an
    /// outpost its column tops or its floor off the tower offset. Empty with nothing on top.
    /// </summary>
    private static List<Vector3> StructureTowerBases(LevelSelectDesignerData.DesignerStructure s)
    {
        if (StructureTowerKind(s) == null) return new List<Vector3>();

        var     o       = s.outpost;
        Vector2 offset  = HasOutpostTop(s) ? o.towerOffset : Vector2.zero;
        var     columns = StructureColumns(s);

        return IsRound(s)
            ? RoundStructureMesh.TowerBases(s.radius, s.height, StructureWallThickness(s), StructureWallHeight(s),
                                            offset, columns, o.columnHeight)
            : InstallationOutpostMesh.TowerBases(s.width, s.depth, s.height,
                                                 StructureWallThickness(s), StructureWallHeight(s),
                                                 offset, columns, o.columnHeight);
    }

    private static float StructureTowerTopY(LevelSelectDesignerData.DesignerStructure s)
    {
        var lollipop = StructureLollipop(s);
        if (lollipop != null) return lollipop.TopY;
        var turret = StructureTurret(s);
        return turret != null ? turret.TopY : 0f;
    }

    // ─────────────────────────────────────────────
    // SELECTION
    // ─────────────────────────────────────────────

    /// <summary>Selects one structure and lets go of everything else, so the panel shows it alone.</summary>
    private void SelectStructureAlone(string structureId)
    {
        ClearSelectionForLandscape();
        _selectedStructureId = structureId;
        Repaint();
    }

    /// Add ▸ Freestanding Structures owns the left panel: everything else drops, the picked one stays.
    private void ClearSelectionForStructures()
    {
        string keep = _selectedStructureId;
        ClearSelectionForLandscape();
        _selectedStructureId = keep;
    }

    // ─────────────────────────────────────────────
    // CANVAS INPUT
    // ─────────────────────────────────────────────

    private void HandleStructureMode(Event e)
    {
        if (HandleStructureKeys(e)) return;
        if (HandleStructureDrag(e)) return;

        if (e.type != EventType.MouseDown || e.button != 0) return;

        // A click on one already there picks it up rather than stacking another on it.
        if (TryPickStructure(e)) return;

        AddStructure(e.mousePosition);
        e.Use();
    }

    /// <summary>Delete and D for the selected structure. Shared with Select mode.</summary>
    private bool HandleStructureKeys(Event e)
    {
        if (e.type != EventType.KeyDown || SelectedStructure == null) return false;

        if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
        {
            DeleteSelectedStructure();
            e.Use();
            return true;
        }
        if (e.keyCode == KeyCode.D)
        {
            DuplicateSelectedStructure();
            e.Use();
            return true;
        }
        return false;
    }

    private void DeleteSelectedStructure()
    {
        var selected = SelectedStructure;
        if (selected == null) return;

        Undo.RecordObject(_data, "Delete Structure");
        _data.structures.Remove(selected);
        _selectedStructureId = null;
        _isDraggingStructure = false;
        MarkDirty();
        RemoveStructureObject(selected.structureId);
        Repaint();
    }

    /// <summary>Copies the selected structure, stood just beside it, and selects the copy.</summary>
    private void DuplicateSelectedStructure()
    {
        var selected = SelectedStructure;
        if (selected == null) return;

        Undo.RecordObject(_data, "Duplicate Structure");

        var dupe = selected.Clone();
        dupe.structureId = System.Guid.NewGuid().ToString();
        dupe.positionXZ += new Vector2(StructureReach(selected) * 2.2f, 0f);
        // Only one perch should start her on the map — the copy doesn't take that over.
        if (dupe.perch != null) dupe.perch.startPerch = false;

        _data.structures.Insert(_data.structures.IndexOf(selected) + 1, dupe);
        _selectedStructureId = dupe.structureId;
        _isDraggingStructure = false;
        MarkDirty();
        RebuildStructure(dupe);
        Repaint();
    }

    /// <summary>Selects the structure under the mouse, alone, and starts carrying it. Shared with Select mode.</summary>
    private bool TryPickStructure(Event e)
    {
        if (!FindStructureAtCanvas(e.mousePosition, out string hit)) return false;

        SelectStructureAlone(hit);
        var picked = SelectedStructure;
        _structureDragOffset = e.mousePosition -
                               WorldToCanvas(new Vector3(picked.positionXZ.x, 0f, picked.positionXZ.y));
        _isDraggingStructure = true;
        e.Use();
        return true;
    }

    /// <summary>Moves the carried structure and lets it go. True when it took the event.</summary>
    private bool HandleStructureDrag(Event e)
    {
        if (!_isDraggingStructure) return false;

        if (e.type == EventType.MouseDrag)
        {
            var dragged = SelectedStructure;
            if (dragged != null)
            {
                Undo.RecordObject(_data, "Move Structure");
                dragged.positionXZ = CanvasToWorld2D(e.mousePosition - _structureDragOffset);
                MarkDirty();
            }
            Repaint();
            e.Use();
            return true;
        }

        if (e.type == EventType.MouseUp)
        {
            // Placed once on release rather than on every step of the drag.
            _isDraggingStructure = false;
            var moved = SelectedStructure;
            if (moved != null) PlaceStructureObject(moved);
            e.Use();
            return true;
        }

        return false;
    }

    private void AddStructure(Vector2 canvas)
    {
        Undo.RecordObject(_data, "Add Structure");

        // A GUID, not a count: the mesh asset is named off it, and the Generated folder is shared
        // by every world.
        var structure = new LevelSelectDesignerData.DesignerStructure
        {
            structureId = System.Guid.NewGuid().ToString(),
            positionXZ  = CanvasToWorld2D(canvas),
        };
        _data.structures.Add(structure);

        SelectStructureAlone(structure.structureId);
        MarkDirty();
        RebuildStructure(structure);
    }

    private bool FindStructureAtCanvas(Vector2 canvas, out string structureId)
    {
        structureId = null;
        if (_data?.structures == null) return false;

        // Last drawn is on top, so it is the one a click lands on.
        for (int i = _data.structures.Count - 1; i >= 0; i--)
        {
            var s = _data.structures[i];
            if (s == null) continue;

            Vector2 at = WorldToCanvas(new Vector3(s.positionXZ.x, 0f, s.positionXZ.y));
            bool hit = IsRound(s)
                ? Vector2.Distance(at, canvas) <= Mathf.Max(8f, s.radius * _zoom)
                : InsideConvex(canvas, StructureCanvasRect(s, 0f)) || Vector2.Distance(at, canvas) <= 8f;
            if (!hit) continue;

            structureId = s.structureId;
            return true;
        }
        return false;
    }

    // ─────────────────────────────────────────────
    // CANVAS DRAWING
    // ─────────────────────────────────────────────

    // A block's outline on the canvas, pulled in by `inset` on every side.
    private Vector3[] StructureCanvasRect(LevelSelectDesignerData.DesignerStructure s, float inset)
    {
        float hw = s.width * 0.5f - inset;
        float z0 = inset, z1 = s.depth - inset;
        return new[]
        {
            (Vector3)WorldToCanvas(StructureLocalToWorldFlat(s, new Vector3(-hw, 0f, z0))),
            (Vector3)WorldToCanvas(StructureLocalToWorldFlat(s, new Vector3( hw, 0f, z0))),
            (Vector3)WorldToCanvas(StructureLocalToWorldFlat(s, new Vector3( hw, 0f, z1))),
            (Vector3)WorldToCanvas(StructureLocalToWorldFlat(s, new Vector3(-hw, 0f, z1))),
        };
    }

    /// <summary>
    /// Each structure as a solid fill — the block's outline or the column's disc — with an outpost's
    /// floor darker inside its wall, a solid dot the size of the orb where each tower stands,
    /// and a dark dot at the centre. The selected one lighter.
    /// </summary>
    private void DrawStructures()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (_data?.structures == null) return;

        foreach (var s in _data.structures)
        {
            if (s == null) continue;
            SyncStructureOutpost(s);

            bool    selected = s.structureId == _selectedStructureId;
            Vector2 centre   = WorldToCanvas(new Vector3(s.positionXZ.x, 0f, s.positionXZ.y));
            var     body     = selected ? new Color(0.9f, 0.9f, 0.9f, 0.95f) : new Color(0.62f, 0.6f, 0.56f, 0.95f);
            var     floor    = selected ? new Color(0.6f, 0.6f, 0.6f, 0.95f) : new Color(0.4f, 0.38f, 0.35f, 0.95f);
            var     tower    = selected ? new Color(0.45f, 0.45f, 0.45f, 1f) : new Color(0.28f, 0.27f, 0.25f, 1f);

            if (s.perch != null && s.perch.enabled && s.perch.perchRadius > 0f
                && LevelSelectDesignerSettings.ShowPerchRadius)
            {
                Handles.color = new Color(1f, 0.93f, 0.55f, selected ? 0.16f : 0.08f);
                Handles.DrawSolidDisc(centre, Vector3.forward, s.perch.perchRadius * _zoom);
            }

            // Fog Zones mode draws these itself, over the zone sheets.
            if (_mode != DesignerMode.FogZone) DrawStructureFogRepel(s, centre, selected);

            float inset = StructureWallInset(s);
            Handles.color = body;
            if (IsRound(s)) Handles.DrawSolidDisc(centre, Vector3.forward, Mathf.Max(3f, s.radius * _zoom));
            else            Handles.DrawAAConvexPolygon(StructureCanvasRect(s, 0f));

            if (inset > 0f)
            {
                Handles.color = floor;
                if (IsRound(s)) Handles.DrawSolidDisc(centre, Vector3.forward, Mathf.Max(1f, (s.radius - inset) * _zoom));
                else            Handles.DrawAAConvexPolygon(StructureCanvasRect(s, inset));
            }

            // Outpost columns, solid, then the towers on top of everything they stand on.
            if (HasOutpostTop(s))
            {
                var o = s.outpost;
                Handles.color = body;
                foreach (bool plusX in new[] { true, false })
                {
                    if (!(plusX ? o.columnDownstream : o.columnUpstream)) continue;
                    Vector3 top = IsRound(s)
                        ? RoundStructureMesh.ColumnTop(s.radius, s.height, o.wallThickness, o.wallHeight, o.columnHeight, plusX)
                        : InstallationOutpostMesh.ColumnTop(s.width, s.depth, s.height, o.wallThickness, o.wallHeight,
                                                            o.columnHeight, plusX);
                    Handles.DrawSolidDisc(WorldToCanvas(StructureLocalToWorldFlat(s, top)), Vector3.forward,
                                          Mathf.Max(2f, o.columnRadius * _zoom));
                }
            }

            var kind = StructureTowerKind(s);
            if (kind != null)
            {
                float orb = HasOutpostTop(s)
                    ? TowerOrbRadius(kind.Value, s.outpost.tower, s.outpost.turret, s.outpost.towerScale)
                    : TowerOrbRadius(kind.Value, s.tower, s.turret, s.towerScale);
                Handles.color = tower;
                foreach (var b in StructureTowerBases(s))
                    Handles.DrawSolidDisc(WorldToCanvas(StructureLocalToWorldFlat(s, b)), Vector3.forward,
                                          Mathf.Max(2f, orb * _zoom));
            }

            Handles.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            Handles.DrawSolidDisc(centre, Vector3.forward, 2.5f);
        }
    }

    /// <summary>Every structure's fog repel circle on its own — Fog Zones mode draws these over the sheets.</summary>
    private void DrawStructureFogRepelCircles()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (_data?.structures == null) return;

        foreach (var s in _data.structures)
        {
            if (s == null) continue;
            DrawStructureFogRepel(s, WorldToCanvas(new Vector3(s.positionXZ.x, 0f, s.positionXZ.y)),
                                  s.structureId == _selectedStructureId);
        }
    }

    // The radius fog is held off from — Fog Repeller's own size or its Override Radius.
    private void DrawStructureFogRepel(LevelSelectDesignerData.DesignerStructure s, Vector2 centre, bool selected)
    {
        if (!s.fogRepel || !LevelSelectDesignerSettings.ShowFogRepelRadius) return;

        float r = StructureFogRadius(s) * _zoom;
        Handles.color = selected ? LevelSelectDesignerSettings.FogRepelSelFillColour
                                 : LevelSelectDesignerSettings.FogRepelFillColour;
        Handles.DrawSolidDisc(centre, Vector3.forward, r);

        float edge = LevelSelectDesignerSettings.FogRepelEdgeWidth;
        if (edge > 0f)
        {
            Handles.color = selected ? LevelSelectDesignerSettings.FogRepelSelEdgeColour
                                     : LevelSelectDesignerSettings.FogRepelEdgeColour;
            DrawThickCircle(centre, r, edge);
        }
    }


    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    private Vector2 _structureListScroll;

    /// <summary>The structures, listed with the selected one opened up. Shown in their Add mode.</summary>
    private void DrawStructurePanel()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Freestanding Structures", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Click the canvas to drop one — D duplicates the selected one. Each " +
                                   "stands on the landscape under its centre and follows the hills as " +
                                   "they are edited.", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(4);

        if (_data.structures.Count == 0)
        {
            EditorGUILayout.LabelField("None yet.", EditorStyles.miniLabel);
            return;
        }

        _structureListScroll = EditorGUILayout.BeginScrollView(_structureListScroll, GUILayout.MaxHeight(600f));

        LevelSelectDesignerData.DesignerStructure toDelete = null;
        foreach (var s in _data.structures)
        {
            if (s == null) continue;

            bool   open  = s.structureId == _selectedStructureId;
            string label = StructureLabel(s);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(open ? $"▾ {label}" : $"▸ {label}", EditorStyles.miniButton))
                {
                    if (open) _selectedStructureId = null;
                    else      SelectStructureAlone(s.structureId);
                    GUI.FocusControl(null);
                }
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22f)))
                    toDelete = s;
            }

            if (!open) continue;

            EditorGUI.indentLevel++;
            DrawStructureFields(s);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndScrollView();

        if (toDelete != null)
        {
            Undo.RecordObject(_data, "Delete Structure");
            _data.structures.Remove(toDelete);
            if (_selectedStructureId == toDelete.structureId) _selectedStructureId = null;
            MarkDirty();
            RemoveStructureObject(toDelete.structureId);
            Repaint();
        }

        EditorGUILayout.Space(2);
        if (GUILayout.Button("Rebuild Structures", EditorStyles.miniButton))
            RebuildStructures();
    }

    /// <summary>The structure picked in Select mode, shown on its own.</summary>
    private void DrawSelectedStructureProps()
    {
        if (_mode == DesignerMode.Structure) return;

        var s = SelectedStructure;
        if (s == null) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(StructureLabel(s), EditorStyles.boldLabel);
        DrawStructureFields(s);

        EditorGUILayout.Space(2);
        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("Delete Structure")) DeleteSelectedStructure();
        GUI.backgroundColor = Color.white;
    }

    private static readonly string[] StructureShapeNames = { "Block", "Round Column" };
    private static readonly string[] StructureTopNames   = { "None", "Lollipop Tower", "Turret", "Outpost" };

    /// <summary>"Structure 3 — Round Column, Outpost": its place in the list, its shape and its top.</summary>
    private string StructureLabel(LevelSelectDesignerData.DesignerStructure s) =>
        $"Structure {_data.structures.IndexOf(s) + 1} — {StructureShapeNames[(int)s.shape]}, " +
        StructureTopNames[(int)s.top];

    /// <summary>One structure's own fields — where, its shape and size, and what stands on top.</summary>
    private void DrawStructureFields(LevelSelectDesignerData.DesignerStructure s)
    {
        SyncStructureOutpost(s);

        EditorGUI.BeginChangeCheck();

        Vector2 at = EditorGUILayout.Vector2Field(
            new GUIContent("Position", "Where its centre stands on the map, in world X and Z."), s.positionXZ);

        var shape = (LevelSelectDesignerData.StructureShape)EditorGUILayout.Popup(
            new GUIContent("Shape", "A rectangular block, or a round column."), (int)s.shape, StructureShapeNames);

        float width = s.width, depth = s.depth, radius = s.radius;
        if (shape == LevelSelectDesignerData.StructureShape.Block)
        {
            width = EditorGUILayout.FloatField(new GUIContent("Width", "The block's size along its own x."), s.width);
            depth = EditorGUILayout.FloatField(new GUIContent("Depth", "The block's size along its own z."), s.depth);
        }
        else
        {
            radius = EditorGUILayout.FloatField(new GUIContent("Radius", "The round column's radius."), s.radius);
        }
        float height = EditorGUILayout.FloatField(
            new GUIContent("Height", "How far its top stands above the landscape under its centre."), s.height);
        float turn = EditorGUILayout.Slider(
            new GUIContent("Turn", "Which way it is turned, in degrees, clockwise seen from above."),
            s.turn, -180f, 180f);

        EditorGUILayout.Space(2);
        var top = (LevelSelectDesignerData.StructureTop)EditorGUILayout.Popup(
            new GUIContent("Top", "What stands on top: nothing, a lollipop tower, a turret, or an outpost — " +
                                  "walls round the top, with its columns and towers."),
            (int)s.top, StructureTopNames);

        var   tower      = s.tower;
        var   turret     = s.turret;
        float towerScale = s.towerScale;
        if (top == LevelSelectDesignerData.StructureTop.Lollipop)
        {
            DrawLollipopPresetRow(s.towerPreset, s.tower, p => s.towerPreset = p, t => s.tower = t);
            tower      = s.tower;   // read back: picking a preset just loaded it
            towerScale = DrawLollipopScaleField(s.towerScale);
        }
        else if (top == LevelSelectDesignerData.StructureTop.Turret)
        {
            DrawTurretPresetRow(s.turretPreset, s.turret, p => s.turretPreset = p, t => s.turret = t);
            turret     = s.turret;   // read back: picking a preset just loaded it
            towerScale = DrawLollipopScaleField(s.towerScale);
        }

        var perch = DrawPerchBlock(s.perch);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Structure");
            s.positionXZ = at;
            s.shape      = shape;
            s.width      = Mathf.Max(0.01f, width);
            s.depth      = Mathf.Max(0.01f, depth);
            s.radius     = Mathf.Max(0.01f, radius);
            s.height     = Mathf.Max(0f, height);
            s.turn       = turn;
            s.top        = top;
            s.tower      = tower;
            s.turret     = turret;
            s.towerScale = towerScale;
            s.perch      = perch;
            SyncStructureOutpost(s);
            MarkDirty();
            RebuildAfterTowerEdit(s, () => RebuildStructure(s));
        }

        if (OcclusionToggle(ref s.occlusion, ref s.occlusionStyle)) RebuildStructure(s);

        DrawStructureFogRepelFields(s);

        if (s.top == LevelSelectDesignerData.StructureTop.Outpost)
            DrawStructureOutpostFields(s);
    }

    /// <summary>
    /// Fog Repeller, and its Override Radius. Not overridden, the radius field shows (greyed) the
    /// structure's own size, which is what it pushes from. Only the repeller is redone — no rebuild.
    /// </summary>
    private void DrawStructureFogRepelFields(LevelSelectDesignerData.DesignerStructure s)
    {
        EditorGUI.BeginChangeCheck();

        bool repel = EditorGUILayout.Toggle(
            new GUIContent("Fog Repeller", "Pushes fog away from it, as a spike does — the FogMap's Rock numbers."),
            s.fogRepel);

        bool  over   = s.fogRepelOverride;
        float radius = s.fogRepelRadius;
        if (repel)
        {
            EditorGUI.indentLevel++;
            over = EditorGUILayout.Toggle(
                new GUIContent("Override Radius", "Push from the radius below instead of the structure's own size."),
                s.fogRepelOverride);

            // Ticking it starts from the size it was pushing from.
            if (over && !s.fogRepelOverride) radius = StructureReach(s);

            using (new EditorGUI.DisabledScope(!over))
                radius = EditorGUILayout.FloatField(
                    new GUIContent("Radius", "The radius fog is held off from, around its centre."),
                    over ? radius : StructureReach(s));
            EditorGUI.indentLevel--;
        }

        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(_data, "Edit Structure Fog Repeller");
        s.fogRepel         = repel;
        s.fogRepelOverride = over;
        if (over) s.fogRepelRadius = Mathf.Max(0f, radius);
        MarkDirty();

        var go = FindStructureObject(s.structureId);
        if (go != null) ApplyStructureFogRepel(s, go);
    }

    /// <summary>Puts the fog repeller on the structure's object, or takes it off when unticked.</summary>
    private static void ApplyStructureFogRepel(LevelSelectDesignerData.DesignerStructure s, GameObject go)
    {
        var fog = go.GetComponent<StructureFogRepeller>();
        if (!s.fogRepel)
        {
            if (fog != null) Undo.DestroyObjectImmediate(fog);
            return;
        }

        if (fog == null) fog = Undo.AddComponent<StructureFogRepeller>(go);
        Undo.RecordObject(fog, "Edit Structure Fog Repeller");
        fog.Configure(StructureFogRadius(s), StructureLocalCentre(s), StructureFogTop(s, go));
        EditorUtility.SetDirty(fog);
    }

    /// <summary>
    /// How far its top stands above the ground under its centre: its own mesh (floor, wall,
    /// columns and a merged lollipop), or a turret on top, whichever reaches higher.
    /// </summary>
    private static float StructureFogTop(LevelSelectDesignerData.DesignerStructure s, GameObject go)
    {
        float top = s.height;
        var filter = go.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null) top = Mathf.Max(top, filter.sharedMesh.bounds.max.y);

        float towerTop = StructureTowerTopY(s);
        foreach (var b in StructureTowerBases(s)) top = Mathf.Max(top, b.y + towerTop);
        return top;
    }

    /// <summary>The radius it holds fog off from: the Override Radius when ticked, else its own size.</summary>
    private static float StructureFogRadius(LevelSelectDesignerData.DesignerStructure s)
        => s.fogRepelOverride ? s.fogRepelRadius : StructureReach(s);

    /// <summary>
    /// The outpost top's preset and its tower's Placement Scale. Everything else is set in the
    /// preset, in the preset editor — less its size, which is always the structure's.
    /// </summary>
    private void DrawStructureOutpostFields(LevelSelectDesignerData.DesignerStructure s)
    {
        var o = s.outpost;

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Outpost", EditorStyles.miniBoldLabel);

        // The preset puts its settings on the outpost; its size stays the structure's.
        DrawOutpostPresetRow(o, () =>
        {
            SyncStructureOutpost(s);
            RebuildStructure(s);
        });

        EditorGUI.BeginChangeCheck();
        float towerScale = DrawLollipopScaleField(o.towerScale);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_data, "Edit Structure Outpost");
            o.towerScale = towerScale;
            MarkDirty();
            RebuildAfterTowerEdit(s, () => RebuildStructure(s));
        }
    }

    // ─────────────────────────────────────────────
    // GENERATION
    // ─────────────────────────────────────────────

    private static string StructureObjectName(string structureId) => $"Structure_{SanitiseAssetName(structureId)}";

    private GameObject FindStructureObject(string structureId)
    {
        var parent = GameObject.Find(StructuresParent);
        if (parent == null) return null;
        var child = parent.transform.Find(StructureObjectName(structureId));
        return child != null ? child.gameObject : null;
    }

    private void RemoveStructureObject(string structureId)
    {
        var go = FindStructureObject(structureId);
        if (go != null) Undo.DestroyObjectImmediate(go);
    }

    /// <summary>
    /// Builds every structure afresh. Anything under the parent that no longer has a structure in
    /// the data is cleared, so one deleted in the panel leaves the scene too.
    /// </summary>
    private void RebuildStructures()
    {
        var parent = FindOrCreateParent(StructuresParent);

        var wanted = new HashSet<string>();
        foreach (var s in _data.structures)
            if (s != null) wanted.Add(StructureObjectName(s.structureId));

        for (int i = parent.transform.childCount - 1; i >= 0; i--)
        {
            var child = parent.transform.GetChild(i).gameObject;
            if (!wanted.Contains(child.name)) Undo.DestroyObjectImmediate(child);
        }

        foreach (var s in _data.structures)
            if (s != null) BuildStructure(s, parent);

        AssetDatabase.SaveAssets();
    }

    private void RebuildStructure(LevelSelectDesignerData.DesignerStructure s)
    {
        if (s == null) return;
        BuildStructure(s, FindOrCreateParent(StructuresParent));
        AssetDatabase.SaveAssets();
    }

    private void BuildStructure(LevelSelectDesignerData.DesignerStructure s, GameObject parent)
    {
        SyncStructureOutpost(s);
        string name = StructureObjectName(s.structureId);

        var go = FindStructureObject(s.structureId);
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Generate Structure");
            go.transform.SetParent(parent.transform, false);
        }

        PlaceStructureObject(s, go);

        var   o        = s.outpost;
        bool  outpost  = HasOutpostTop(s);
        float wallT    = StructureWallThickness(s);
        float wallH    = StructureWallHeight(s);
        var   columns  = StructureColumns(s);
        var   offset   = outpost ? o.towerOffset : Vector2.zero;
        var   lollipop = StructureLollipop(s);   // a lollipop is merged into the body; a turret moves, so it can't be

        // The rivers' rim top in the structure's own space — what the waterline shading is placed off.
        float rimTop = WallRimTopY - go.transform.position.y;

        // The structure's own parts in the tuner (Freestanding Structures), not the outposts'.
        var shading = RiverRunShadingSettings.ForStructure(StoneShadingForBuild);
        var built = IsRound(s)
            ? RoundStructureMesh.Build(s.radius, s.height, _data.RunDepth, wallT, wallH, lollipop, offset,
                                       shading, columns, o.columnRadius, o.columnHeight, rimTop)
            : InstallationOutpostMesh.Build(s.width, s.depth, s.height, _data.RunDepth, wallT, wallH, lollipop, offset,
                                            shading, columns, o.columnRadius, o.columnHeight, rimTop);
        var mesh = SaveGeneratedMesh(name, built);
        if (mesh == null) return;

        // An outpost top in the outposts' material; everything else in the rivers'.
        var material = outpost && _data.arenaWallMaterial != null ? _data.arenaWallMaterial : _data.riverMaterial;
        SetTurretPiece(go, mesh, material);

        var bases  = StructureTowerBases(s);
        var turret = StructureTurret(s);
        RefreshTurrets(go, turret, turret != null ? bases : null, name, material);

        // Her feet go on the top of the (first) tower, or on the middle of the top with none.
        Vector3 tip = bases.Count > 0
            ? bases[0] + Vector3.up * StructureTowerTopY(s)
            : StructureLocalCentre(s) + Vector3.up * s.height;
        ApplyPerch(go, s.perch, tip);

        ApplyOcclusion(go, s.occlusion, s.occlusionStyle);

        ApplyStructureFogRepel(s, go);
    }

    /// <summary>Stands it where it belongs — its centre on the landscape under it, turned.</summary>
    private void PlaceStructureObject(LevelSelectDesignerData.DesignerStructure s, GameObject go = null,
                                      bool recordUndo = true)
    {
        if (go == null) go = FindStructureObject(s.structureId);
        if (go == null) return;

        var rot = StructureRotation(s);
        if (recordUndo) Undo.RecordObject(go.transform, "Place Structure");
        go.transform.localPosition = new Vector3(s.positionXZ.x, LandscapeGroundY(s.positionXZ), s.positionXZ.y)
                                   - rot * StructureLocalCentre(s);
        go.transform.localRotation = rot;
        go.transform.localScale    = Vector3.one;
    }

    /// <summary>
    /// Re-seats every structure on the landscape as it now stands — called from the hill sync
    /// with the spikes. Only moved, never rebuilt; no undo record, it follows the hill edit.
    /// </summary>
    private void PlaceAllStructures()
    {
        if (_data?.structures == null || _data.structures.Count == 0) return;
        if (GameObject.Find(StructuresParent) == null) return;

        foreach (var s in _data.structures)
            if (s != null) PlaceStructureObject(s, recordUndo: false);
    }
}

#endif
