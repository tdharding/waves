using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The names of a generated mesh's bits, kept inside the mesh's own asset so the Level Select
/// designer overlay can write them on the piece when it is selected — see <see cref="PartLabels"/>.
/// Written by the designer each time the mesh is generated; nothing else reads it.
/// </summary>
public class GeneratedPartLabelSet : ScriptableObject
{
    public List<PartLabel> labels = new List<PartLabel>();
}
