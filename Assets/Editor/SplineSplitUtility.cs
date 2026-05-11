using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

#if UNITY_EDITOR

/// <summary>
/// Shared arc-length split math used by both SplineToolsWindow and LevelSelectDesignerWindow.
/// Mirrors the DoSplit loop exactly so both tools produce identical geometry.
/// </summary>
public static class SplineSplitUtility
{
    public struct SplitResult
    {
        public bool      valid;
        public List<float3> posA;            // local space of source container
        public List<float3> posB;
        public float3    endA;               // local space — last point of A (gap start)
        public float3    startB;             // local space — first point of B (gap end)
        public Vector3   endAWorld;
        public Vector3   startBWorld;
        public Vector3   splitPosWorld;
        public Vector3   splitTangentWorld;  // normalised main-path tangent at split
        public float     halfGap;
    }

    /// <summary>
    /// Runs the arc-length walk on <paramref name="source"/> at normalised <paramref name="splitT"/>,
    /// cuts a gap of <paramref name="halfGap"/> world units either side, and returns posA / posB lists
    /// ready to hand to BuildSplineFromPositions.
    /// </summary>
    public static SplitResult ComputeSplit(
        SplineContainer source, float splitT, float halfGap, int subdivisions = 20)
    {
        var result = new SplitResult { valid = false };
        if (source == null || source.Splines.Count == 0) return result;

        var   spline = source.Splines[0];
        if (spline.Count < 2) return result;

        float4x4 l2w = source.transform.localToWorldMatrix;
        float4x4 w2l = source.transform.worldToLocalMatrix;

        float totalLength = SplineUtility.CalculateLength(spline, l2w);
        float splitLength = totalLength * splitT;
        int   steps       = subdivisions * Mathf.Max(1, spline.Count - 1);

        // ── Arc-length walk to find tangent at split ──────────────
        float3 splitPosLocal = spline.EvaluatePosition(splitT);
        float3 splitTanWorld = math.normalize(
            math.mul(l2w, new float4(spline.EvaluateTangent(splitT), 0f)).xyz);

        float prevDist = 0f;
        for (int i = 1; i <= steps; i++)
        {
            float  t     = (float)i / steps;
            float3 posW  = math.transform(l2w, spline.EvaluatePosition(t));
            float3 prevW = math.transform(l2w, spline.EvaluatePosition((float)(i - 1) / steps));
            float  dist  = prevDist + math.length(posW - prevW);
            if (dist >= splitLength)
            {
                splitPosLocal = spline.EvaluatePosition(t);
                splitTanWorld = math.normalize(
                    math.mul(l2w, new float4(spline.EvaluateTangent(t), 0f)).xyz);
                break;
            }
            prevDist = dist;
        }

        // ── Gap endpoints ─────────────────────────────────────────
        float3 splitPosWorld3 = math.transform(l2w, splitPosLocal);
        float3 endAWorld3     = splitPosWorld3 - splitTanWorld * halfGap;
        float3 startBWorld3   = splitPosWorld3 + splitTanWorld * halfGap;
        float3 endA           = math.transform(w2l, endAWorld3);
        float3 startB         = math.transform(w2l, startBWorld3);

        float endALength   = math.max(0f,         splitLength - halfGap);
        float startBLength = math.min(totalLength, splitLength + halfGap);

        // ── Build posA / posB ─────────────────────────────────────
        var  posA    = new List<float3>();
        var  posB    = new List<float3>();
        prevDist = 0f;
        bool closedA = false, openedB = false;

        for (int i = 0; i <= steps; i++)
        {
            float  t    = (float)i / steps;
            float3 posL = spline.EvaluatePosition(t);
            float3 posW = math.transform(l2w, posL);
            float  dist = 0f;
            if (i > 0)
            {
                float3 prevW = math.transform(l2w, spline.EvaluatePosition((float)(i - 1) / steps));
                dist = prevDist + math.length(posW - prevW);
            }
            prevDist = dist;

            if (!closedA) { if (dist >= endALength) { posA.Add(endA); closedA = true; } else posA.Add(posL); }
            if (closedA && !openedB && dist >= startBLength) { posB.Add(startB); openedB = true; }
            else if (openedB) posB.Add(posL);
        }

        if (!closedA)       posA.Add(endA);
        if (!openedB)       posB.Add(startB);
        if (posB.Count < 2) posB.Add(spline.EvaluatePosition(1f));

        if (posA.Count < 2 || posB.Count < 2)
        {
            Debug.LogWarning($"[SplineSplitUtility] Gap ({halfGap * 2f:F3}u) is too large for spline length ({totalLength:F3}u).");
            return result;
        }

        result.valid             = true;
        result.posA              = posA;
        result.posB              = posB;
        result.endA              = endA;
        result.startB            = startB;
        result.endAWorld         = endAWorld3;
        result.startBWorld       = startBWorld3;
        result.splitPosWorld     = splitPosWorld3;
        result.splitTangentWorld = new Vector3(splitTanWorld.x, splitTanWorld.y, splitTanWorld.z);
        result.halfGap           = halfGap;
        return result;
    }

    // ─────────────────────────────────────────────────────────────
    // Shared helpers
    // ─────────────────────────────────────────────────────────────

    public static float MeasurePrefabXExtent(GameObject prefab)
    {
        var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        temp.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var renderers = temp.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 1f;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b.size.x;
        }
        finally { Object.DestroyImmediate(temp); }
    }

    /// <summary>
    /// Builds a Unity Spline from a list of local-space positions.
    /// </summary>
    public static Spline BuildSplineFromPositions(
        List<float3> positions, TangentMode mode = TangentMode.AutoSmooth)
    {
        var spline = new Spline();
        for (int i = 0; i < positions.Count; i++)
        {
            float3 pos    = positions[i];
            float3 tanIn  = float3.zero;
            float3 tanOut = float3.zero;

            if (i > 0 && i < positions.Count - 1)
            {
                tanIn  = (pos - positions[i - 1]) * 0.333f;
                tanOut = (positions[i + 1] - pos) * 0.333f;
            }
            else if (i == 0 && positions.Count > 1)
            {
                tanOut = (positions[1] - pos) * 0.333f;
                tanIn  = -tanOut;
            }
            else if (i == positions.Count - 1 && positions.Count > 1)
            {
                tanIn  = (pos - positions[i - 1]) * 0.333f;
                tanOut = -tanIn;
            }

            spline.Add(new BezierKnot(pos, -tanIn, tanOut, quaternion.identity), mode);
        }
        ZeroKnotRollAngles(spline);
        return spline;
    }

    /// <summary>
    /// Zeroes the Z (roll) euler component of every knot rotation in a spline.
    /// Prevents AutoSmooth from introducing out-of-plane twist on flat river paths.
    /// </summary>
    public static void ZeroKnotRollAngles(Spline spline)
    {
        for (int i = 0; i < spline.Count; i++)
        {
            var     knot  = spline[i];
            Vector3 euler = ((Quaternion)knot.Rotation).eulerAngles;
            euler.z       = 0f;
            knot.Rotation = (quaternion)Quaternion.Euler(euler);
            spline.SetKnot(i, knot);
        }
    }

    /// <summary>
    /// 2D cross product in XZ plane.
    /// Positive  → branchDir is to the RIGHT of mainTangent (bottom / down-facing).
    /// Negative  → branchDir is to the LEFT  of mainTangent (top  / up-facing).
    /// </summary>
    public static float CrossXZ(Vector3 mainTangent, Vector3 branchDir)
        => mainTangent.x * branchDir.z - mainTangent.z * branchDir.x;
}

#endif
