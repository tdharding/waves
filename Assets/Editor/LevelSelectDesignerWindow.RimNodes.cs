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
/// a lollipop tower, or the vert display point prefab placed off its PrefabBaselineAlignment —
/// its disc on the top, its forward facing into the river. Both are rebuilt with the piece.
///
/// A display point can also carry an interaction: stand the boat near it, press the key, and the
/// poster it names comes up on screen — see DrawInteractBlock, in the Interact part of this
/// window.
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

        var profile = _data.ProfileFor(path.riverName);
        var rim     = _data.RimNodeAt(_selectedNodeId);

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
        var   toppings = new List<(int side, LevelSelectDesignerData.RimNodeTopping topping)>();
        if (rim != null)
        {
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

            plinthRadius = EditorGUILayout.Slider(
                new GUIContent("Plinth Radius", "An extra round plinth standing on the top, " +
                                                "centred on the river-side circle — what stands " +
                                                "on the rim node stands on it. Zero for none."),
                plinthRadius, 0f, radius);
            if (plinthRadius > RiverMeshBuilder.RimNodePlinthMinRadius)
                plinthHeight = Mathf.Max(RiverMeshBuilder.RimNodeMinHeight,
                    EditorGUILayout.FloatField(
                        new GUIContent("Plinth Height",
                                       "How far the plinth's top stands above the platform's."),
                        plinthHeight));

            if (rim.side == LevelSelectDesignerData.RimNodeSide.Both)
            {
                toppings.Add((-1, DrawRimNodeTopping(rim, -1, "On Top (Left)")));
                toppings.Add(( 1, DrawRimNodeTopping(rim,  1, "On Top (Right)")));
            }
            else
            {
                int only = rim.side == LevelSelectDesignerData.RimNodeSide.Left ? -1 : 1;
                toppings.Add((only, DrawRimNodeTopping(rim, only, "On Top")));
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
        }

        MarkDirty();
        RebuildRimNodesAt(_selectedNodeId);
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
            var profile = _data.ProfileFor(path.riverName);
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

                Handles.color = shade;
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
                    Handles.color = new Color(0.25f, 0.22f, 0.18f, 0.9f);
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
        if (topping == null || topping.topper != LevelSelectDesignerData.RimNodeTopper.VertDisplayPoint)
            return;

        var interact = topping.interact;
        if (interact == null || !interact.enabled || interact.radius <= 0f) return;

        Vector3 centre = WorldToCanvas(at);
        float   reach  = interact.radius * _zoom;

        Handles.color = new Color(1f, 0.85f, 0.3f, 0.12f);
        Handles.DrawSolidDisc(centre, Vector3.forward, reach);

        Handles.color = new Color(1f, 0.85f, 0.3f, 0.9f);
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

        foreach (var node in record.pathNodes)
        {
            var rim = _data.RimNodeAt(node.nodeId);
            if (rim == null || !CanHoldRimNode(node.nodeId, out _)) continue;

            foreach (int side in RimSides(rim.side))
            {
                rims.Add(new RiverMeshBuilder.RimNode
                {
                    at           = node.position,
                    side         = side,
                    radius       = rim.radius,
                    height       = rim.height,
                    width        = rim.width,
                    plinthRadius = rim.plinthRadius,
                    plinthHeight = rim.plinthHeight,
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
        var    rims     = RimNodesFor(record);

        if (rims.Count == 0)
        {
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            record.rimNodesMeshAssetName = null;
            EditorUtility.SetDirty(record);
            return;
        }

        var mesh = SaveGeneratedMesh(name,
            RiverMeshBuilder.BuildRimNodes(profile, centres, forwards, rims, MeshEdge));
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

    private static readonly GUIContent[] RimNodeTopperLabels =
    {
        new GUIContent("None"), new GUIContent("Lollipop Tower"), new GUIContent("Vert Display Point"),
    };

    /// <summary>
    /// The On Top dropdown for one platform (+1 right, -1 left), and the fields for what it
    /// picks. Hands back an edited copy, written by the caller inside its own change check.
    /// </summary>
    private LevelSelectDesignerData.RimNodeTopping DrawRimNodeTopping(
        LevelSelectDesignerData.DesignerRimNode rim, int side, string label)
    {
        var edited = rim.ToppingOn(side);

        edited.topper = (LevelSelectDesignerData.RimNodeTopper)EditorGUILayout.Popup(
            new GUIContent(label, "What stands on this platform, on its plinth when it has one."),
            (int)edited.topper, RimNodeTopperLabels);

        EditorGUI.indentLevel++;
        if (edited.topper == LevelSelectDesignerData.RimNodeTopper.LollipopTower)
        {
            // The preset row writes straight onto the rim node (Save as New does it a frame
            // later), so the tower is read back after it rather than off the copy.
            DrawLollipopPresetRow(edited.towerPreset, edited.tower,
                p => { var t = rim.ToppingOn(side); t.towerPreset = p; rim.SetToppingOn(side, t); },
                w => { var t = rim.ToppingOn(side); t.tower       = w; rim.SetToppingOn(side, t); });

            var fresh = rim.ToppingOn(side);
            edited.towerPreset = fresh.towerPreset;
            edited.tower       = DrawLollipopTowerFields(fresh.tower);
        }
        else if (edited.topper == LevelSelectDesignerData.RimNodeTopper.VertDisplayPoint)
        {
            edited.displayHeight = Mathf.Max(0.001f, EditorGUILayout.FloatField(
                new GUIContent("Display Height", "How tall it stands, from the node floor to " +
                                                 "the top marker on its prefab."), edited.displayHeight));
            edited.interact = DrawInteractBlock(edited.interact);
        }
        EditorGUI.indentLevel--;

        return edited;
    }

    private const string VertDisplayPointPrefabPath =
        "Assets/Prefab/LevelSelectPrefabs/LevelSelectVertDisplayPoint.prefab";
    private const string RimNodeToppersChild = "OnTop";

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
                          .FindAll(t => sources[t.index].ToppingOn(rims[t.index].side).topper !=
                                        LevelSelectDesignerData.RimNodeTopper.None);
        if (tops.Count == 0) return;

        var container = new GameObject(RimNodeToppersChild);
        Undo.RegisterCreatedObjectUndo(container, "Generate Rim Node Toppers");
        container.transform.SetParent(piece.transform, false);

        foreach (var top in tops)
        {
            var     rim     = sources[top.index];
            var     topping = rim.ToppingOn(rims[top.index].side);
            string  label   = $"{SanitiseAssetName(rim.nodeId)}_{(rims[top.index].side > 0 ? "R" : "L")}";
            Vector3 at      = piece.transform.TransformPoint(top.top);
            Vector3 toRiver = piece.transform.TransformDirection(top.toRiver);
            toRiver.y = 0f;
            if (toRiver.sqrMagnitude < 1e-8f) toRiver = piece.transform.forward;

            switch (topping.topper)
            {
                case LevelSelectDesignerData.RimNodeTopper.LollipopTower:
                {
                    var mesh = SaveGeneratedMesh($"RimNodeTower_{label}",
                        (topping.tower ?? new LollipopTower()).Build(StoneShadingForBuild));
                    if (mesh == null) break;

                    var go = NewMeshChild(container, $"RimNodeTower_{label}", mesh);
                    go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(toRiver, Vector3.up));
                    break;
                }

                case LevelSelectDesignerData.RimNodeTopper.VertDisplayPoint:
                {
                    var display = PlaceOffAligner(VertDisplayPointPrefabPath, container.transform,
                                                  $"VertDisplayPoint_{label}", at, toRiver,
                                                  topping.displayHeight);
                    ApplyInteract(display, topping.interact);
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

            var profile = _data.ProfileFor(record.riverName);
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
