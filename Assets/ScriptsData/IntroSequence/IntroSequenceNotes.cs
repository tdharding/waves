using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Inspector-only documentation component.
/// No runtime behaviour.
/// </summary>
public class IntroSequenceNotes : MonoBehaviour
{
    [Header("Purpose")]
    [TextArea(4, 8)]
    public string purpose =
        "Inspector-only documentation for the Intro cutscene sequence.";

    [Header("Sequence Blocks (Drag to Reorder)")]
    public List<SequenceBlock> sequenceBlocks = new List<SequenceBlock>();
}

[System.Serializable]
public class SequenceBlock
{
    [Tooltip("Storyboard / reference image for this step.")]
    public Texture2D thumbnail;

    [Tooltip("Short heading for this step.")]
    public string title;

    [TextArea(6, 14)]
    public string description;
}
