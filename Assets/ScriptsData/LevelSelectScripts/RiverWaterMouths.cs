using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where the level select's waters open into one another, as the water's lines need to know it.
///
/// A pool's rings are drawn off the shore really seen — its waterline circle with a gap at each
/// river's mouth, and that river's two waterlines running out from the gap's corners — so they
/// bend round each corner and carry on down the river as its bank lines. And a river's lines,
/// for the first Join Overlap metres past where its water butts onto a pool's, cross-fade over
/// from the pool's look to their own. Neither can be baked into the faces: the distance to a
/// corner is not a straight line, and a face lying between two mouths would interpolate the two.
/// So every mouth is a record here, in world space, pushed as four global arrays every frame
/// (see <see cref="LevelSelectDesignerData.ApplyAesthetics"/>), and each water surface carries on
/// UV3 only which records to read — the same number on every corner, so it cannot interpolate.
///
/// A junction is the same problem on a river: the river's near bank is opened where a branch
/// leaves it, so its bank lines round the mouth's two corners and run on down the branch rather
/// than straight across the gap. See <see cref="JunctionKind"/>.
///
/// UV3, read by RiverEdgeRipples.hlsl:
///   pool water   (first record + 1, how many, 0, 0) — every mouth of that pool
///   river water  (record + 1 at its start, record + 1 at its end, first junction record + 1,
///                how many junctions) — 0 where no pool / no junction. The junctions are the
///                branches leaving THIS river; a branch's own water reads none of them.
///
/// Written by the designer once everything is built (RefreshWaterMouths), so the records and the
/// UV3 on the meshes always come from the same pass.
/// </summary>
[Serializable]
public struct RiverWaterMouth
{
    /// <summary>(pool centre x, z, flat unit direction out along the river x, z).</summary>
    public Vector4 a;

    /// <summary>(one corner x, z, the other corner x, z) — where the river's waterlines meet the
    /// pool's.</summary>
    public Vector4 b;

    /// <summary>(the pool's outer waterline radius, metres out along the direction from the pool
    /// centre to where the river's own water starts, kind — 2 a pool, 0 unused).</summary>
    public Vector4 c;

    /// <summary>Junctions only; zero on a pool mouth. See <see cref="JunctionKind"/>.</summary>
    public Vector4 d;

    public const float PoolKind = 2f;

    /// <summary>
    /// A junction: a branch leaving a river, recorded against the RIVER, whose bank is the one
    /// opened. Laid out differently from a pool mouth:
    ///   a  (corner x, z, other corner x, z) — where the branch's waterlines meet the river's near one
    ///   b  (branch heading x, z, a point on the branch's centreline x, z) — the branch's own frame
    ///   c  (branch waterline half-width, the branch's along at that point, kind 1, 1 when the
    ///       river's near bank is UV1.y / 0 when it is UV1.x)
    ///   d  (unit direction across the river toward the branch x, z, the butt line as dot(p, that),
    ///       metres from the mouth's middle past which the junction is left out)
    /// </summary>
    public const float JunctionKind = 1f;

    /// <summary>The fixed length of every array pushed. A global array takes the length it is
    /// first set with for the rest of the session, so it is ALWAYS pushed this long.</summary>
    public const int MaxRecords = 64;

    private static readonly int AId = Shader.PropertyToID("_RiverMouthA");
    private static readonly int BId = Shader.PropertyToID("_RiverMouthB");
    private static readonly int CId = Shader.PropertyToID("_RiverMouthC");
    private static readonly int DId = Shader.PropertyToID("_RiverMouthD");

    private static readonly Vector4[] _a = new Vector4[MaxRecords];
    private static readonly Vector4[] _b = new Vector4[MaxRecords];
    private static readonly Vector4[] _c = new Vector4[MaxRecords];
    private static readonly Vector4[] _d = new Vector4[MaxRecords];

    /// <summary>Puts the records into the shader's globals; null or empty clears them.</summary>
    public static void Push(List<RiverWaterMouth> mouths)
    {
        int n = mouths != null ? Mathf.Min(mouths.Count, MaxRecords) : 0;
        for (int i = 0; i < MaxRecords; i++)
        {
            _a[i] = i < n ? mouths[i].a : Vector4.zero;
            _b[i] = i < n ? mouths[i].b : Vector4.zero;
            _c[i] = i < n ? mouths[i].c : Vector4.zero;
            _d[i] = i < n ? mouths[i].d : Vector4.zero;
        }

        Shader.SetGlobalVectorArray(AId, _a);
        Shader.SetGlobalVectorArray(BId, _b);
        Shader.SetGlobalVectorArray(CId, _c);
        Shader.SetGlobalVectorArray(DId, _d);
    }
}
