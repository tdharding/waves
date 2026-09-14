using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Sits on a generated river run and records everything needed to rebuild it: the path
/// it was swept along, the river whose section it uses, and where its branches cut into it.
///
/// The designer's spline is split at junctions for the boat's routing, which destroys the
/// unbroken curve the run was swept along — so the curve is kept here instead, and the
/// Level Select Designer can rebuild the mesh from it whenever a river's shape changes,
/// without a full regenerate.
/// </summary>
public class RiverRunMesh : MonoBehaviour
{
    [Serializable]
    public class Knot
    {
        public Vector3 position;
        public Vector3 tangentIn;
        public Vector3 tangentOut;

        /// <summary>
        /// The knot's own frame. A <see cref="BezierKnot"/> holds its tangents in this frame —
        /// an auto-smoothed knot carries nothing but a length along local forward and keeps
        /// the whole heading here — so the curve only comes back if this comes back with it.
        /// </summary>
        public Quaternion rotation;

        /// <summary>Records written before the frame was kept carry none.</summary>
        public bool HasRotation =>
            rotation.x * rotation.x + rotation.y * rotation.y +
            rotation.z * rotation.z + rotation.w * rotation.w > 0.0001f;
    }

    /// <summary>A branch meeting this run: its river, and where it arrives in local space.</summary>
    [Serializable]
    public class Mouth
    {
        public string  riverName;
        public Vector3 centre;
        public Vector3 direction;

        /// <summary>How far out along <see cref="direction"/> the branch's own run stops, so
        /// the mouth patch starts on the ring that run really ends with.</summary>
        public float   collar;
    }

    [Tooltip("River whose Run Shape this run is built from.")]
    public string riverName;

    [Tooltip("Name of the generated mesh asset this run writes to.")]
    public string meshAssetName;

    [Tooltip("Name of the generated water mesh asset, when this river is water filled.")]
    public string waterMeshAssetName;

    [Tooltip("How far back past the start of this run its water reaches, so a branch's water " +
             "carries across the mouth to meet the water of the river it joins.")]
    public float waterLeadIn;

    [Tooltip("How far past the end of this run its water carries on, so a river arriving at a " +
             "pool reaches across the rim it was trimmed against and meets the water inside.")]
    public float waterLeadOut;

    [Tooltip("Whether this run's water laps OVER the water it meets at its start rather than " +
             "butting onto it. Only a branch leaving another river does: where a river meets a " +
             "pool it is the POOL's water that laps out over the river, so the river would be " +
             "lapping back over the very thing lapping it. The fact rather than the distance, so " +
             "Rebuild Runs picks up whatever Water Branch Overlap is set to now.")]
    public bool waterLapsAtStart;

    [Tooltip("Sorting Group order for this run's water, on the Default layer: 0 for the main " +
             "river, one higher for each branch deep, so a branch draws over the river it leaves. " +
             "Worked out from the paths on Generate, so Rebuild Runs keeps it.")]
    public int waterSortingOrder;

    [Tooltip("Arena node this run's start carries on into, when it leads to one. The node rather " +
             "than a distance, so a rebuild reaches whatever size that arena's wall is now.")]
    public string arenaAtStart;

    [Tooltip("Arena node this run's end carries on into, when it leads to one.")]
    public string arenaAtEnd;

    [Tooltip("Whether the start of the run is closed off. An end that runs into a pool is left " +
             "open — the pool's mouth patch picks the section up there and carries it round.")]
    public bool capStart = true;

    [Tooltip("Whether the end of the run is closed off. An end that runs into a pool is left " +
             "open — the pool's mouth patch picks the section up there and carries it round.")]
    public bool capEnd = true;

    [Tooltip("The unbroken curve the run was swept along, in this object's local space.")]
    public List<Knot> knots = new List<Knot>();

    public List<Mouth> mouths = new List<Mouth>();

    /// <summary>
    /// Rebuilds the curve the run was swept along.
    ///
    /// A knot's tangents are held in its own frame, so they are only worth replaying with the
    /// frame they were written in. A record that carries none — written before the frame was
    /// kept — is auto-smoothed from its positions instead, which is how it was built in the
    /// first place; taking its tangents at face value would point every one of them along
    /// local forward and throw the run off its own path.
    /// </summary>
    public Spline ToSpline()
    {
        var spline = new Spline();
        foreach (var k in knots)
        {
            if (k.HasRotation)
                spline.Add(new BezierKnot(k.position, k.tangentIn, k.tangentOut, k.rotation),
                           TangentMode.Broken);
            else
                spline.Add(new BezierKnot(k.position), TangentMode.AutoSmooth);
        }
        return spline;
    }

    public void RecordSpline(Spline spline)
    {
        knots.Clear();
        if (spline == null) return;

        for (int i = 0; i < spline.Count; i++)
        {
            var k = spline[i];
            knots.Add(new Knot
            {
                position   = k.Position,
                tangentIn  = k.TangentIn,
                tangentOut = k.TangentOut,
                rotation   = (Quaternion)k.Rotation,
            });
        }
    }
}
