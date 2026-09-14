using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Turns a place named as a river and a distance along it into a place in the world.
///
/// The boat no longer travels the splines, but they are still how a spot on the map is
/// written down — a door says which river it lets out onto and how far along, and the save
/// from before this changed says the same. This reads one of those and answers with a
/// position and a heading, once, after which the boat is free of it.
/// </summary>
public static class LevelSelectBoatPlacement
{
    /// <summary>
    /// Where the boat stands if it is put on <paramref name="segmentID"/> at
    /// <paramref name="progress"/>. False when no such river is in the scene.
    /// </summary>
    public static bool TryResolve(string segmentID, float progress,
                                  out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (string.IsNullOrEmpty(segmentID)) return false;

        var segment = RiverSegmentRegistry.Instance?.GetSegment(segmentID);
        if (segment == null) return false;

        var container = segment.GetComponent<SplineContainer>();
        return TryResolve(container, progress, out position, out rotation);
    }

    /// <summary>The same, for a river already in hand.</summary>
    public static bool TryResolve(SplineContainer container, float progress,
                                  out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (container == null || container.Spline == null || container.Spline.Count < 2)
            return false;

        float t = Mathf.Clamp01(progress);

        position = container.transform.TransformPoint(
            (Vector3)container.Spline.EvaluatePosition(t));

        // Facing the way the river runs. A spot at the very end of one faces back up it,
        // because that is the only way there is left to go from there.
        Vector3 tangent = container.transform.TransformDirection(
            (Vector3)container.Spline.EvaluateTangent(t));
        if (t > 0.999f) tangent = -tangent;

        tangent.y = 0f;
        if (tangent.sqrMagnitude > 1e-8f)
            rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);

        return true;
    }

    /// <summary>
    /// The river the boat is nearest, and how far along it — the world written back down in
    /// the form a door or a saved journey uses. Empty when there are no rivers registered.
    /// </summary>
    public static string NearestSegment(Vector3 position, out float progress)
    {
        progress = 0f;

        var registry = RiverSegmentRegistry.Instance;
        if (registry == null) return string.Empty;

        string best     = string.Empty;
        float  bestDist = float.MaxValue;

        foreach (var segment in registry.AllSegments)
        {
            if (segment == null) continue;

            var container = segment.GetComponent<SplineContainer>();
            if (container == null || container.Spline == null) continue;

            SplineUtility.GetNearestPoint(
                container.Spline,
                (Unity.Mathematics.float3)container.transform.InverseTransformPoint(position),
                out var nearestLocal, out float t);

            float dist = Vector3.Distance(
                position, container.transform.TransformPoint((Vector3)nearestLocal));

            if (dist < bestDist)
            {
                bestDist = dist;
                best     = segment.SegmentID;
                progress = Mathf.Clamp01(t);
            }
        }

        return best;
    }
}
