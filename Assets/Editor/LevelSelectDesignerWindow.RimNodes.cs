using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

/// <summary>
/// Rim nodes — a round platform standing on a river's rim at one of its nodes, one side of the
/// river or both (see <see cref="LevelSelectDesignerData.DesignerRimNode"/>). Set from the
/// selected node's panel; built as a piece of its own under each run, by
/// <see cref="RiverMeshBuilder.BuildRimNodes"/>, so the run itself is untouched.
///
/// A run records every node of its path (<see cref="RiverRunMesh.pathNodes"/>), so a rim node can
/// be added, resized or taken away live — only the rim node piece and the banks are rebuilt.
///
/// Something can stand on it (<see cref="LevelSelectDesignerData.RimNodeTopper"/>) — on the
/// river-side circle, on the plinth when it has one:
/// a lollipop tower or a turret. Or, in place of the old vert display point, a Screen: a square
/// opening cut into the river-side wall by the builder, with a flat glowing quad at its back
/// facing out into the river. All are rebuilt with the piece.
///
/// A screen can also carry an interaction: stand the boat near it, press the key, and the poster
/// it names fades in on the screen itself, the view cutting to the screen's camera when it has one
/// — see DrawInteractBlock, in the Interact part of this window, and the ScreenCameras part.
/// </summary>
public partial class LevelSelectDesignerWindow
{
    // ─────────────────────────────────────────────
    // WHERE ONE CAN GO
    // ─────────────────────────────────────────────

    /// <summary>
    /// Whether a node can carry a rim node: a plain node partway along one river. Not a junction
    /// or a node where rivers meet, not a pool or a lead-in, and not either end of a river — the
    /// rim there is already cut away, locked, or stops.
    /// </summary>
    private bool CanHoldRimNode(string nodeId, out string why)
    {
        why = null;
        var node = _data.nodes.Find(n => n.id == nodeId);
        if (node == null) { why = "Node not found."; return false; }

        if (node.type != LevelSelectDesignerData.NodeType.Waypoint ||
            _data.junctions.Exists(j => j.nodeId == nodeId))
        { why = "Rim nodes can't go on a junction, arena or shop node."; return false; }

        if (_data.PoolAt(nodeId) != null) { why = "Rim nodes can't go on a pool."; return false; }
        if (IsLeadInNode(nodeId))         { why = "Rim nodes can't go on a lead-in."; return false; }

        var paths = _data.paths.Where(p => p.nodeIds.Contains(nodeId)).ToList();
        if (paths.Count != 1) { why = "Rim nodes can't go where rivers meet."; return false; }

        int at = paths[0].nodeIds.IndexOf(nodeId);
        if (at <= 0 || at >= paths[0].nodeIds.Count - 1)
        { why = "Rim nodes can't go on the end of a river."; return false; }

        return true;
    }

    // ─────────────────────────────────────────────
    // PANEL
    // ─────────────────────────────────────────────

    /// <summary>Side, radius and height for the selected node, when it is on a river.</summary>
    private void DrawSelectedRimNodeProps()
    {
        if (string.IsNullOrEmpty(_selectedNodeId)) return;

        var path = _data.paths.Find(p => p.nodeIds.Contains(_selectedNodeId));
        if (path == null) return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Rim Node", EditorStyles.boldLabel);

        if (!CanHoldRimNode(_selectedNodeId, out string why))
        {
            EditorGUILayout.LabelField(why, EditorStyles.miniLabel);
            return;
        }

        var profile = RunProfileFor(path, path.riverName);
        var rim     = _data.RimNodeAt(_selectedNodeId);

        if (rim != null && OcclusionToggle(ref rim.occlusion, ref rim.occlusionStyle)) RebuildRimNodesAt(_selectedNodeId);

        // None, then the three sides.
        int current = rim == null ? 0 : (int)rim.side + 1;

        EditorGUI.BeginChangeCheck();
        int picked = EditorGUILayout.Popup(
            new GUIContent("Side", "Which bank it stands on, looking down the path the way it " +
                                   "was drawn — or both."),
            current, new[] { new GUIContent("None"), new GUIContent("Left"),
                          new GUIContent("Right"), new GUIContent("Both") });

        float radius       = rim?.radius ?? 0.1f;
        float height       = rim?.height ?? 0.01f;
        float width        = rim?.width ?? 0f;
        float plinthRadius = rim?.plinthRadius ?? 0f;
        float plinthHeight = rim?.plinthHeight ?? 0.01f;
        var   shape        = rim?.shape ?? LevelSelectDesignerData.RimNodeShape.Rounded;
        var   toppings = new List<(int side, LevelSelectDesignerData.RimNodeTopping topping)>();
        if (rim != null)
        {
            shape = (LevelSelectDesignerData.RimNodeShape)EditorGUILayout.EnumPopup(
                new GUIContent("Shape", "Rounded: round ends. Slanted: square ends, the " +
                                        "river-side one a flat face leaning out into the river " +
                                        "at the channel wall's lean above the water — no plinth."),
                shape);
            bool slanted = shape == LevelSelectDesignerData.RimNodeShape.Slanted;

            radius = EditorGUILayout.Slider(
                new GUIContent("Radius", "Radius of the platform — both sides the same."),
                radius, RiverMeshBuilder.RimNodeMinRadius(profile),
                RiverMeshBuilder.RimNodeMaxRadius(profile));
            height = Mathf.Max(RiverMeshBuilder.RimNodeMinHeight, EditorGUILayout.FloatField(
                new GUIContent("Height", "How far its top stands above the rim."), height));
            width = EditorGUILayout.Slider(
                new GUIContent("Width", "How far apart its two circles stand, across the rim — " +
                                        "one pushed out past the run's outer edge, one in " +
                                        "towards the river. Zero is one plain circle. Nothing " +
                                        "holds it back: wide enough and the river-side circle " +
                                        "stands out in the water."),
                width, 0f, RiverMeshBuilder.RimNodeMaxWidth(profile));

            if (!slanted)
                plinthRadius = EditorGUILayout.Slider(
                    new GUIContent("Plinth Radius", "An extra round plinth standing on the top, " +
                                                    "centred on the river-side circle — what stands " +
                                                    "on the rim node stands on it. Zero for none."),
                    plinthRadius, 0f, radius);
            if (!slanted && plinthRadius > RiverMeshBuilder.RimNodePlinthMinRadius)
                plinthHeight = Mathf.Max(RiverMeshBuilder.RimNodeMinHeight,
                    EditorGUILayout.FloatField(
                        new GUIContent("Plinth Height",
                                       "How far the plinth's top stands above the platform's."),
                        plinthHeight));

            if (rim.side == LevelSelectDesignerData.RimNodeSide.Both)
            {
                toppings.Add((-1, DrawRimNodeTopping(rim, -1, "On Top (Left)",  "Screen (Left)")));
                toppings.Add(( 1, DrawRimNodeTopping(rim,  1, "On Top (Right)", "Screen (Right)")));
            }
            else
            {
                int only = rim.side == LevelSelectDesignerData.RimNodeSide.Left ? -1 : 1;
                toppings.Add((only, DrawRimNodeTopping(rim, only, "On Top", "Screen")));
            }
        }

        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(_data, "Edit Rim Node");
        _data.rimNodes ??= new List<LevelSelectDesignerData.DesignerRimNode>();

        if (picked == 0)
        {
            _data.rimNodes.RemoveAll(r => r.nodeId == _selectedNodeId);
        }
        else
        {
            if (rim == null)
            {
                rim = new LevelSelectDesignerData.DesignerRimNode
                {
                    nodeId = _selectedNodeId,
                    radius = Mathf.Clamp(radius, RiverMeshBuilder.RimNodeMinRadius(profile),
                                         RiverMeshBuilder.RimNodeMaxRadius(profile)),
                    height = height,
                };
                width        = 0f;
                plinthRadius = 0f;
                _data.rimNodes.Add(rim);
            }
            // Written back against the side they were drawn for, before the side can change.
            foreach (var (side, topping) in toppings) rim.SetToppingOn(side, topping);

            rim.side         = (LevelSelectDesignerData.RimNodeSide)(picked - 1);
            rim.radius       = radius;
            rim.height       = height;
            rim.width        = width;
            rim.plinthRadius = Mathf.Min(plinthRadius, radius);
            rim.plinthHeight = plinthHeight;
            rim.shape        = shape;
        }

        MarkDirty();
        string rimNodeId = _selectedNodeId;
        RebuildAfterTowerEdit("RimNode:" + rimNodeId, () => RebuildRimNodesAt(rimNodeId));
        Repaint();
    }

    // ─────────────────────────────────────────────
    // CANVAS
    // ─────────────────────────────────────────────

    /// <summary>
    /// Each rim node where it stands on the rim: its two circles and the straight sides joining
    /// them — one circle when it has no width — with its plinth marked on the river-side one.
    /// </summary>
    private void DrawRimNodes()
    {
        if (Event.current.type != EventType.Repaint || _data.rimNodes == null) return;

        foreach (var rim in _data.rimNodes)
        {
            if (rim == null || !CanHoldRimNode(rim.nodeId, out _)) continue;

            var path    = _data.paths.Find(p => p.nodeIds.Contains(rim.nodeId));
            var profile = ProfileAtNode(RunProfileFor(path, path.riverName), rim.nodeId);
            int at      = path.nodeIds.IndexOf(rim.nodeId);

            Vector3 here = _data.NodeWorldPosition(rim.nodeId);
            Vector3 dir  = _data.NodeWorldPosition(path.nodeIds[at + 1])
                         - _data.NodeWorldPosition(path.nodeIds[at - 1]);
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-8f) continue;

            Vector3 right  = Vector3.Cross(Vector3.up, dir.normalized);
            float   across = profile.innerWidth * 0.5f + profile.rimWidth * 0.5f;
            float   radius = Mathf.Clamp(rim.radius, RiverMeshBuilder.RimNodeMinRadius(profile),
                                         RiverMeshBuilder.RimNodeMaxRadius(profile));

            float   halfWidth = Mathf.Max(rim.width, 0f) * 0.5f;
            float   plinth    = Mathf.Min(Mathf.Max(rim.plinthRadius, 0f), radius);
            float   drawn     = Mathf.Max(2f, radius * _zoom);
            Vector3 along     = Vector3.Cross(Vector3.up, right);

            bool selected = rim.nodeId == _selectedNodeId;
            var  shade    = selected ? new Color(1f, 1f, 1f, 0.85f)
                                     : new Color(0.85f, 0.75f, 0.55f, 0.8f);

            foreach (int side in RimSides(rim.side))
            {
                Vector3 axis   = right * side;
                Vector3 centre = here + axis * across;
                Vector3 near   = centre - axis * halfWidth;   // the circle towards the river
                Vector3 far    = centre + axis * halfWidth;

                Handles.color = Faded(shade);

                // Slanted: the rectangle over the two circles, seen from above at its top.
                if (rim.shape == LevelSelectDesignerData.RimNodeShape.Slanted)
                {
                    Vector3 farEnd  = centre + axis * (halfWidth + radius);
                    Vector3 nearEnd = centre - axis * (halfWidth + radius);
                    Vector3 side3   = along * radius;
                    Handles.DrawAAConvexPolygon(
                        (Vector3)WorldToCanvas(nearEnd + side3), (Vector3)WorldToCanvas(farEnd + side3),
                        (Vector3)WorldToCanvas(farEnd - side3),  (Vector3)WorldToCanvas(nearEnd - side3));

                    if (selected) DrawInteractReach(rim.ToppingOn(side), centre);
                    continue;
                }

                Handles.DrawSolidDisc(WorldToCanvas(near), Vector3.forward, drawn);
                Handles.DrawSolidDisc(WorldToCanvas(far),  Vector3.forward, drawn);

                // The straight sides joining them, filled in as the platform really is.
                if (halfWidth > 0f)
                {
                    Vector3 out3 = along * radius;
                    Handles.DrawAAConvexPolygon(
                        (Vector3)WorldToCanvas(near + out3), (Vector3)WorldToCanvas(far + out3),
                        (Vector3)WorldToCanvas(far - out3),  (Vector3)WorldToCanvas(near - out3));
                }

                // The plinth, on the river-side circle — where whatever stands on it stands.
                if (plinth > RiverMeshBuilder.RimNodePlinthMinRadius)
                {
                    Handles.color = Faded(new Color(0.25f, 0.22f, 0.18f, 0.9f));
                    Handles.DrawSolidDisc(WorldToCanvas(near), Vector3.forward,
                                          Mathf.Max(1.5f, plinth * _zoom));
                }

                // How near the boat has to come, while this node is the one being edited —
                // the same circle the point itself draws in the scene, seen from above.
                if (selected) DrawInteractReach(rim.ToppingOn(side), near);
            }
        }
    }

    /// <summary>
    /// The reach of whatever stands on a platform, as a ring around it on the canvas — drawn
    /// where the topper stands, on the river-side circle, and only while its node is selected.
    /// </summary>
    private void DrawInteractReach(LevelSelectDesignerData.RimNodeTopping topping, Vector3 at)
    {
        if (topping == null || !topping.HasScreen) return;

        var interact = topping.interact;
        if (interact == null || !interact.enabled || interact.radius <= 0f) return;

        Vector3 centre = WorldToCanvas(at);
        float   reach  = interact.radius * _zoom;

        Handles.color = Faded(new Color(1f, 0.85f, 0.3f, 0.12f));
        Handles.DrawSolidDisc(centre, Vector3.forward, reach);

        Handles.color = Faded(new Color(1f, 0.85f, 0.3f, 0.9f));
        Handles.DrawWireDisc(centre, Vector3.forward, reach);
    }

    private static IEnumerable<int> RimSides(LevelSelectDesignerData.RimNodeSide side)
    {
        if (side != LevelSelectDesignerData.RimNodeSide.Left)  yield return  1;
        if (side != LevelSelectDesignerData.RimNodeSide.Right) yield return -1;
    }

    // ─────────────────────────────────────────────
    // BUILDING
    // ─────────────────────────────────────────────

    /// <summary>Records every node of the path a run was swept along, in the run's own space.</summary>
    private void RecordPathNodes(RiverRunMesh record, LevelSelectDesignerData.DesignerPath path,
                                 GameObject segmentGO)
    {
        record.pathNodes = new List<RiverRunMesh.PathNode>();
        if (path == null || segmentGO == null) return;

        foreach (string id in path.nodeIds)
            record.pathNodes.Add(new RiverRunMesh.PathNode
            {
                nodeId   = id,
                position = segmentGO.transform.InverseTransformPoint(WorldPosOfNode(id)),
            });
    }

    /// <summary>
    /// The rim nodes standing along a run, as the builder takes them. <paramref name="sources"/>,
    /// when given, is filled alongside with the designer rim node each one came from.
    /// </summary>
    private List<RiverMeshBuilder.RimNode> RimNodesFor(
        RiverRunMesh record, List<LevelSelectDesignerData.DesignerRimNode> sources = null)
    {
        var rims = new List<RiverMeshBuilder.RimNode>();
        if (record?.pathNodes == null || _data.rimNodes == null) return rims;

        var runProfile = RunProfileFor(record);

        foreach (var node in record.pathNodes)
        {
            var rim = _data.RimNodeAt(node.nodeId);
            if (rim == null || !CanHoldRimNode(node.nodeId, out _)) continue;

            // Leaning as the channel wall beside it does, at the river's width there.
            float lean = RiverMeshBuilder.RimNodeSlantLean(ProfileAtNode(runProfile, node.nodeId),
                                                           _data.waterLevel);

            foreach (int side in RimSides(rim.side))
            {
                var topping = rim.ToppingOn(side);
                rims.Add(new RiverMeshBuilder.RimNode
                {
                    screenSize   = topping.HasScreen ? topping.screenSize : 0f,
                    screenHeight = topping.HasScreen ? topping.ScreenHeight : 0f,
                    waterDrop    = _data.WaterSurfaceDrop,
                    at           = node.position,
                    side         = side,
                    radius       = rim.radius,
                    height       = rim.height,
                    width        = rim.width,
                    plinthRadius = rim.plinthRadius,
                    plinthHeight = rim.plinthHeight,
                    slanted      = rim.shape == LevelSelectDesignerData.RimNodeShape.Slanted,
                    lean         = lean,
                });
                sources?.Add(rim);
            }
        }
        return rims;
    }

    /// <summary>
    /// Builds the rim node piece hanging off a run, or takes it away when the run carries none.
    /// In the river material, like the run it stands on.
    /// </summary>
    private void RefreshRimNodes(RiverRunMesh record, RiverProfile profile,
                                 List<Vector3> centres, List<Vector3> forwards)
    {
        if (record == null || string.IsNullOrEmpty(record.meshAssetName)) return;

        string name     = $"RiverRimNodes_{record.meshAssetName.Replace("RiverRun_", string.Empty)}";
        var    existing = record.transform.Find(name);
        var    sources  = new List<LevelSelectDesignerData.DesignerRimNode>();
        var    rims     = RimNodesFor(record, sources);

        // Each one ticked for Occlusion is built as a mesh of its own, so it fades on its own;
        // the rest share the one piece, which carries every rim node's toppers either way.
        var pieces = new List<(string name, Mesh built, OcclusionStyle style)>();
        var shared = new List<RiverMeshBuilder.RimNode>();
        for (int i = 0; i < rims.Count; i++)
        {
            if (!sources[i].occlusion) { shared.Add(rims[i]); continue; }

            string label = $"RimNode_{SanitiseAssetName(sources[i].nodeId)}_{(rims[i].side > 0 ? "R" : "L")}";
            pieces.Add((OcclusionPieceName(name, label), RiverMeshBuilder.BuildRimNodes(
                profile, centres, forwards, new[] { rims[i] }, MeshEdge, StoneShadingForBuild),
                sources[i].occlusionStyle));
        }
        RefreshOcclusionPieces(record, name + OcclusionPiecePrefix, pieces);

        if (rims.Count == 0)
        {
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            record.rimNodesMeshAssetName = null;
            EditorUtility.SetDirty(record);
            return;
        }

        var mesh = SaveGeneratedMesh(name,
            RiverMeshBuilder.BuildRimNodes(profile, centres, forwards, shared, MeshEdge, StoneShadingForBuild));
        if (mesh == null) return;

        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
            var filter = go.GetComponent<MeshFilter>();
            if (filter == null) filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            EditorUtility.SetDirty(filter);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _data.riverMaterial;
            EditorUtility.SetDirty(renderer);
        }
        else
        {
            go = NewMeshChild(record.gameObject, name, mesh);
        }

        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;

        RefreshRimNodeToppers(go, record, profile, centres, forwards);

        record.rimNodesMeshAssetName = name;
        EditorUtility.SetDirty(record);
    }

    // ─────────────────────────────────────────────
    // ON TOP
    // ─────────────────────────────────────────────

    // Screen is its own toggle now, and Vert Display Point is retired: both are left off the
    // list, and a saved one of either shows as None here with Screen ticked.
    private static readonly GUIContent[] RimNodeTopperLabels =
    {
        new GUIContent("None"), new GUIContent("Lollipop Tower"), new GUIContent("Turret Tower"),
    };
    private static readonly int[] RimNodeTopperValues =
    {
        (int)LevelSelectDesignerData.RimNodeTopper.None,
        (int)LevelSelectDesignerData.RimNodeTopper.LollipopTower,
        (int)LevelSelectDesignerData.RimNodeTopper.TurretTower,
    };

    /// <summary>
    /// The On Top dropdown for one platform (+1 right, -1 left) and the fields for what it picks,
    /// then the Screen toggle and its fields — the two apart, so a platform can have both. Hands
    /// back an edited copy, written by the caller inside its own change check.
    /// </summary>
    private LevelSelectDesignerData.RimNodeTopping DrawRimNodeTopping(
        LevelSelectDesignerData.DesignerRimNode rim, int side, string label, string screenLabel)
    {
        var edited = rim.ToppingOn(side);

        // An old Screen pick moves onto the toggle the first time it is edited.
        edited.screen = edited.HasScreen;
        edited.topper = (LevelSelectDesignerData.RimNodeTopper)EditorGUILayout.IntPopup(
            new GUIContent(label, "What stands on this platform, on its plinth when it has one."),
            (int)edited.OnTop, RimNodeTopperLabels, RimNodeTopperValues);

        EditorGUI.indentLevel++;
        if (edited.topper == LevelSelectDesignerData.RimNodeTopper.LollipopTower)
        {
            // The preset row writes straight onto the rim node (New does it a frame later), so
            // the tower is read back after it rather than off the copy.
            DrawLollipopPresetRow(edited.towerPreset, edited.tower,
                p => { var t = rim.ToppingOn(side); t.towerPreset = p; rim.SetToppingOn(side, t); },
                w => { var t = rim.ToppingOn(side); t.tower       = w; rim.SetToppingOn(side, t); });

            var fresh = rim.ToppingOn(side);
            edited.towerPreset = fresh.towerPreset;
            edited.tower       = fresh.tower;
            edited.towerScale  = DrawLollipopScaleField(edited.towerScale);
            edited.perch       = DrawPerchBlock(edited.perch);
        }
        else if (edited.topper == LevelSelectDesignerData.RimNodeTopper.TurretTower)
        {
            // Read back after the preset row, as the lollipop is above.
            DrawTurretPresetRow(edited.turretPreset, edited.turret,
                p => { var t = rim.ToppingOn(side); t.turretPreset = p; rim.SetToppingOn(side, t); },
                w => { var t = rim.ToppingOn(side); t.turret       = w; rim.SetToppingOn(side, t); });

            var fresh = rim.ToppingOn(side);
            edited.turretPreset = fresh.turretPreset;
            edited.turret       = fresh.turret;
            edited.towerScale   = DrawLollipopScaleField(edited.towerScale);
            edited.perch        = DrawPerchBlock(edited.perch);
        }
        EditorGUI.indentLevel--;

        edited.screen = EditorGUILayout.Toggle(
            new GUIContent(screenLabel, "A screen set into this platform's river-side wall — " +
                                        "whatever stands on top as well."),
            edited.screen);

        EditorGUI.indentLevel++;
        if (edited.screen)
        {
            edited.screenSize = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Screen Width", "How wide the screen is."), edited.screenSize));
            edited.screenHeight = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Screen Height", "How tall the screen is. Held to what fits between " +
                                                "the waterline and the top."), edited.ScreenHeight));
            edited.interact = DrawInteractBlock(edited.interact);
            DrawScreenInteractExtras(edited, rim.nodeId, side);
        }
        EditorGUI.indentLevel--;

        return edited;
    }

    private const string RimNodeToppersChild = "OnTop";

    private const string RimNodeScreenMaterialPath =
        "Assets/TextureMatShader/LevelSelectMaterials/RimNodeScreenMat.mat";

    /// <summary>
    /// The one material every rim node screen shares — a plain grey glow, made the first time
    /// it is asked for. Swap its look in the asset; the artwork goes on it later.
    /// </summary>
    private static Material RimNodeScreenMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RimNodeScreenMaterialPath);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogWarning("[LevelSelectDesigner] URP Lit shader not found — rim node screens " +
                             "are left without a material.");
            return null;
        }

        mat = new Material(shader) { name = "RimNodeScreenMat" };
        mat.SetColor("_BaseColor", new Color(0.2f, 0.2f, 0.2f, 1f));
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.8f, 0.8f, 0.8f, 1f));
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        AssetDatabase.CreateAsset(mat, RimNodeScreenMaterialPath);
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>
    /// Stands what each rim node carries in the middle of its top, under an "OnTop" child of the
    /// run's rim node piece — cleared and built afresh each time the piece is.
    /// </summary>
    private void RefreshRimNodeToppers(GameObject piece, RiverRunMesh record, RiverProfile profile,
                                       List<Vector3> centres, List<Vector3> forwards)
    {
        var old = piece.transform.Find(RimNodeToppersChild);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var sources = new List<LevelSelectDesignerData.DesignerRimNode>();
        var rims    = RimNodesFor(record, sources);
        var tops    = RiverMeshBuilder.RimNodeTops(profile, centres, forwards, rims, MeshEdge)
                          .FindAll(t =>
                          {
                              var topper = sources[t.index].ToppingOn(rims[t.index].side).OnTop;
                              return topper != LevelSelectDesignerData.RimNodeTopper.None;
                          });
        var screens = RiverMeshBuilder.RimNodeScreens(profile, centres, forwards, rims, MeshEdge);
        if (tops.Count == 0 && screens.Count == 0) return;

        var container = new GameObject(RimNodeToppersChild);
        Undo.RegisterCreatedObjectUndo(container, "Generate Rim Node Toppers");
        container.transform.SetParent(piece.transform, false);

        // Each screen fills the back of the opening the rim node piece leaves for it: a plain
        // quad, 0–1 across it, facing out into the river — its picture and camera with it.
        foreach (var screen in screens)
        {
            var    rim   = sources[screen.index];
            int    side  = rims[screen.index].side;
            string label = $"{SanitiseAssetName(rim.nodeId)}_{(side > 0 ? "R" : "L")}";

            var go = new GameObject($"RimNodeScreen_{label}");
            Undo.RegisterCreatedObjectUndo(go, "Place Rim Node Screen");
            go.transform.SetParent(container.transform, false);
            go.transform.localPosition = screen.centre;
            go.transform.localRotation = Quaternion.LookRotation(-screen.facing, Vector3.up);
            go.transform.localScale    = new Vector3(screen.width, screen.height, 1f);

            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            go.AddComponent<MeshRenderer>().sharedMaterial = RimNodeScreenMaterial();

            ApplyScreenInteract(go, rim.ToppingOn(side), rim.nodeId, side, screen.width, screen.height);
        }

        foreach (var top in tops)
        {
            var     rim     = sources[top.index];
            var     topping = rim.ToppingOn(rims[top.index].side);
            string  label   = $"{SanitiseAssetName(rim.nodeId)}_{(rims[top.index].side > 0 ? "R" : "L")}";
            Vector3 at      = piece.transform.TransformPoint(top.top);
            Vector3 toRiver = piece.transform.TransformDirection(top.toRiver);
            toRiver.y = 0f;
            if (toRiver.sqrMagnitude < 1e-8f) toRiver = piece.transform.forward;

            switch (topping.OnTop)
            {
                case LevelSelectDesignerData.RimNodeTopper.LollipopTower:
                {
                    var tower = (topping.tower ?? new LollipopTower()).Scaled(topping.towerScale);

                    var mesh = SaveGeneratedMesh($"RimNodeTower_{label}", tower.Build(StoneShadingForBuild));
                    if (mesh == null) break;

                    var go = NewMeshChild(container, $"RimNodeTower_{label}", mesh);
                    go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(toRiver, Vector3.up));

                    // Her feet go on the top of the orb, which the tower works out from the sizes
                    // this mesh was just built to — resize it and she moves with it.
                    ApplyPerch(go, topping.perch, Vector3.up * tower.TopY);
                    ApplyOcclusion(go, rim.occlusion, rim.occlusionStyle);
                    break;
                }

                case LevelSelectDesignerData.RimNodeTopper.TurretTower:
                {
                    var turret = (topping.turret ?? new TurretTower()).Scaled(topping.towerScale);

                    var go = PlaceTurret(container.transform, $"RimNodeTurret_{label}", turret, _data.riverMaterial);
                    if (go == null) break;

                    go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(toRiver, Vector3.up));
                    ApplyPerch(go, topping.perch, Vector3.up * turret.TopY);
                    ApplyOcclusion(go, rim.occlusion, rim.occlusionStyle);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Instances a prefab so its PrefabBaselineAlignment disc sits on <paramref name="at"/> and its
    /// forward override faces <paramref name="face"/>, scaled so the aligner's top marker stands
    /// <paramref name="height"/> above the disc — or at the prefab's own size when it has none.
    /// </summary>
    private static GameObject PlaceOffAligner(string prefabPath, Transform parent, string name,
                                              Vector3 at, Vector3 face, float height)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[LevelSelectDesigner] No prefab at {prefabPath} — nothing placed for {name}.");
            return null;
        }

        // What the prefab's aligner says, in the prefab root's own frame.
        var       aligner = prefab.GetComponentInChildren<PrefabBaselineAlignment>(true);
        Transform root    = prefab.transform;
        Vector3   contact = Vector3.zero;
        Vector3   faceWay = Vector3.forward;
        float     topHeight = 0f;
        if (aligner != null)
        {
            contact   = Quaternion.Inverse(root.rotation) * (aligner.transform.position - root.position);
            faceWay   = Quaternion.Inverse(root.rotation) * aligner.transform.TransformDirection(aligner.LocalForward);
            topHeight = aligner.PrefabTopHeight;
        }
        else
        {
            Debug.LogWarning($"[LevelSelectDesigner] {prefab.name} has no PrefabBaselineAlignment — " +
                             "placed off its root.");
        }
        faceWay.y = 0f;
        if (faceWay.sqrMagnitude < 1e-8f) faceWay = Vector3.forward;
        if (topHeight <= 0f)
            Debug.LogWarning($"[LevelSelectDesigner] {prefab.name} has no top marker — its height " +
                             "is ignored and it is placed at the prefab's own size.");
        float scale = topHeight > 0f ? height / topHeight : 1f;

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(go, "Place " + prefab.name);
        go.transform.SetParent(parent, false);

        var rot = Quaternion.LookRotation(face, Vector3.up) *
                  Quaternion.Inverse(Quaternion.LookRotation(faceWay, Vector3.up));

        go.name                 = name;
        go.transform.localScale = root.localScale * scale;
        go.transform.rotation   = rot;
        go.transform.position   = at - rot * (contact * scale);

        return go;
    }

    /// <summary>
    /// Rebuilds the rim nodes and the banks of every run passing through a node — all a rim node
    /// edit changes, so the run itself is left alone. Runs generated before rim nodes existed
    /// carry no path nodes, and need one Generate first.
    /// </summary>
    private void RebuildRimNodesAt(string nodeId)
    {
        int found = 0;
        foreach (var record in FindObjectsOfType<RiverRunMesh>())
        {
            if (record.pathNodes == null || !record.pathNodes.Exists(n => n.nodeId == nodeId)) continue;
            if (record.knots.Count < 2) continue;

            var profile = RunProfileFor(record);
            var notches = ToNotches(record.mouths);
            if (!SampleRun(record.ToSpline(), MeshEdge, profile, notches,
                           out var centres, out var forwards)) continue;

            FitRunToArena(centres, forwards,
                          ArenaReachAt(record.arenaAtStart), ArenaReachAt(record.arenaAtEnd),
                          MeshEdge);

            RefreshRimNodes(record, profile, centres, forwards);
            RefreshBanks(record, profile, centres, forwards, notches,
                         record.capStart, record.capEnd);
            found++;
        }

        if (found == 0)
            _consoleStatusMsg = "Rim node saved — Generate once to build it (this river's runs " +
                                "were generated before rim nodes existed).";
    }
}

#endif
