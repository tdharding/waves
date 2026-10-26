using UnityEngine;

/// <summary>
/// A NomKabarTree's settings — base, stem, hub and every arm — saved so they can be put on another
/// tree from the dropdown in the Level Select Designer, freestanding or on a natural pool's island.
/// Where the tree stands, its turn and its Placement Scale are not part of it; those stay with each
/// placement.
///
/// The one named "Default NomKabarTree" is what every newly placed tree starts as, and where a
/// tree's extra arms come from when its Arm Count is raised.
/// </summary>
[CreateAssetMenu(menuName = "Waves/NomKabar Tree Preset")]
public class NomKabarTreePreset : ScriptableObject
{
    public NomKabarTree tree = NomKabarTree.CodeDefault();

    /// <summary>Takes the tree's settings, as a copy — nothing is shared with it.</summary>
    public void CopyFrom(NomKabarTree source)
    {
        tree = (source ?? NomKabarTree.CodeDefault()).Clone();
        tree.EnsureArms();
    }

    /// <summary>The preset's settings as a fresh tree — nothing is shared with it.</summary>
    public NomKabarTree Build()
    {
        var built = (tree ?? NomKabarTree.CodeDefault()).Clone();
        built.EnsureArms();
        return built;
    }
}
