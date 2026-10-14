using System.Collections.Generic;
using UnityEngine;

public static partial class RiverMeshBuilder
{
    /// <summary>
    /// The pool floor's grid fill, for pieces built outside this class: a grid of quads inside a
    /// closed, convex outline of an even number of points (8 or more), the outline kept exactly as
    /// its border. Null when the outline can't be gridded. Used by the souls pool tower's basin.
    /// </summary>
    public static Vector3[][] FillOutline(IList<Vector3> outline) => GridFill(outline);
}
