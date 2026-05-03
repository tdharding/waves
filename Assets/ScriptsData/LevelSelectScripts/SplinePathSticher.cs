using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;

public class SplinePathStitcher : MonoBehaviour
{
    [SerializeField] private List<SplineContainer> _sourceSegments;
    [SerializeField] private GameObject _pathPrefab; // A prefab with a SplineContainer and RiverSegmentID

    private Dictionary<string, SplineContainer> _bakedPaths = new Dictionary<string, SplineContainer>();

    [ContextMenu("Bake Playable Paths")]
    public void BakePaths()
    {
        // 1. Cleanup old baked paths
        var children = new List<GameObject>();
        foreach (Transform child in transform)
            children.Add(child.gameObject);
        foreach (var child in children)
        {
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        _bakedPaths.Clear();

        // 2. Iterate and Stitch
        var firstIDPerPath = new Dictionary<string, RiverSegmentID>();
        var lastIDPerPath  = new Dictionary<string, RiverSegmentID>();

        foreach (var source in _sourceSegments)
        {
            if (source == null) continue;
            var id = source.GetComponent<RiverSegmentID>();
            if (id == null) continue;

            string pathKey = id.SegmentID;

            if (!_bakedPaths.ContainsKey(pathKey))
            {
                _bakedPaths[pathKey]    = CreateNewHighway(pathKey, id);
                firstIDPerPath[pathKey] = id;
            }

            lastIDPerPath.TryGetValue(pathKey, out RiverSegmentID prevID);
            AppendSplineData(source, _bakedPaths[pathKey], prevID);
            lastIDPerPath[pathKey] = id;
        }

        // 3. Bake arena-end flags onto each stitched path
        foreach (var kvp in _bakedPaths)
        {
            var bakedID = kvp.Value.GetComponent<RiverSegmentID>();
            if (bakedID == null) continue;

            firstIDPerPath.TryGetValue(kvp.Key, out var firstID);
            lastIDPerPath.TryGetValue(kvp.Key, out var lastID);

            bool arenaAtEnd   = lastID  != null && lastID.LeadsToArena  &&  lastID.ArenaIsAtEnd;
            bool arenaAtStart = firstID != null && firstID.LeadsToArena && !firstID.ArenaIsAtEnd;

            if      (arenaAtEnd)   bakedID.SetArenaConnection(true,  true);
            else if (arenaAtStart) bakedID.SetArenaConnection(true,  false);
            else                   bakedID.SetArenaConnection(false, true);
        }

        Debug.Log($"Successfully baked {_bakedPaths.Count} playable paths.");

        // Notify any arena controllers in the scene so they can auto-assign their arena paths
        var bakedList = new List<SplineContainer>(_bakedPaths.Values);
        foreach (var controller in FindObjectsOfType<LevelSelectArenaController>())
            controller.AutoDetectPaths(bakedList);
    }

    private SplineContainer CreateNewHighway(string name, RiverSegmentID originalID)
    {
        GameObject go = Instantiate(_pathPrefab, transform);
        go.name = name;

        // Place at world origin so baked local space == world space.
        // This means if source segments are also at identity, values copy 1:1.
        go.transform.position   = Vector3.zero;
        go.transform.rotation   = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var container = go.GetComponent<SplineContainer>();
        var newID     = go.GetComponent<RiverSegmentID>();

        if (container == null || newID == null)
        {
            Debug.LogError($"Bake Failed: Prefab is missing components!");
            return null;
        }

        newID.SetSegmentID(name);
        newID.CopyFrom(originalID);

        if (container.Splines.Count > 0) container.RemoveSplineAt(0);
        container.AddSpline(new Spline());

        return container;
    }

    private void AppendSplineData(SplineContainer source, SplineContainer target, RiverSegmentID prevID = null)
    {
        var sourceSpline = source.Spline;
        var targetSpline = target.Spline;

        for (int i = 0; i < sourceSpline.Count; i++)
        {
            var knot = sourceSpline[i];

            // Position: source local → world → target local
            Vector3 worldPos = source.transform.TransformPoint((Vector3)knot.Position);
            Vector3 localPos = target.transform.InverseTransformPoint(worldPos);

            // Tangents: TransformVector preserves handle length (scale-aware)
            Vector3 worldIn  = source.transform.TransformVector((Vector3)knot.TangentIn);
            Vector3 worldOut = source.transform.TransformVector((Vector3)knot.TangentOut);
            Vector3 localIn  = target.transform.InverseTransformVector(worldIn);
            Vector3 localOut = target.transform.InverseTransformVector(worldOut);

            TangentMode originalMode = sourceSpline.GetTangentMode(i);

            // Welding: merge overlapping first knot with the last baked knot
            if (targetSpline.Count > 0 && i == 0)
            {
                int lastIdx = targetSpline.Count - 1;
                float dist = Vector3.Distance((Vector3)targetSpline[lastIdx].Position, localPos);

                if (dist < 0.1f)
                {
                    // Don't weld if the previous segment's end leads into an arena portal
                    bool prevEndsAtArena = prevID != null && prevID.LeadsToArena && prevID.ArenaIsAtEnd;
                    if (!prevEndsAtArena)
                    {
                        BezierKnot existingKnot  = targetSpline[lastIdx];
                        existingKnot.TangentOut  = (Unity.Mathematics.float3)localOut;
                        targetSpline[lastIdx]    = existingKnot;
                        targetSpline.SetTangentMode(lastIdx, TangentMode.Continuous);
                        continue;
                    }
                }
            }

            BezierKnot newKnot = new BezierKnot
            {
                Position   = (Unity.Mathematics.float3)localPos,
                TangentIn  = (Unity.Mathematics.float3)localIn,
                TangentOut = (Unity.Mathematics.float3)localOut,
                Rotation   = knot.Rotation
            };

            targetSpline.Add(newKnot);
            targetSpline.SetTangentMode(targetSpline.Count - 1, originalMode);
        }
    }
}
