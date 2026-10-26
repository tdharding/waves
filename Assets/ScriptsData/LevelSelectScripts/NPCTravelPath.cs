using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A character's travel path, saved as its own file: the points it walks to in turn, what it does
/// at each, and the ground between them. Made and edited in the NPC Path Tool; the character's
/// ObserverManNPCController points at it.
///
/// Points are world positions.
/// </summary>
public class NPCTravelPath : ScriptableObject
{
    public enum PathWalk { Loop, BackAndForth, Static }

    /// <summary>How long it stands at one path point before moving on, and which way it faces there.</summary>
    [System.Serializable]
    public class PathPause
    {
        public bool  pause;
        [Min(0f)] public float seconds;

        [Tooltip("Turns to face a set way while it stands at this point.")]
        public bool  face;
        [Tooltip("The way it faces, in degrees clockwise seen from above — in the same space as the points.")]
        public float faceAngle;
    }

    /// <summary>The in-between points along the ground from one path point to the next.</summary>
    [System.Serializable]
    public class PathLeg
    {
        public List<Vector3> ground = new();
    }

    [Tooltip("Points it walks to in turn, standing a while at each.")]
    public List<Vector3> points = new();

    [Tooltip("Loop: after the last point it heads back to the first. Back and Forth: it turns round " +
             "at each end and walks the points the other way. Static: it stands on the first point, " +
             "facing its Rot when set, and never moves or starts an animation.")]
    public PathWalk walk = PathWalk.Loop;

    [Tooltip("The ground between each point and the next — the last leg runs back to the first — " +
             "fitted to what's drawn by the NPC Path Tool. Out of step with the points = straight lines.")]
    public List<PathLeg> ground = new();

    [Tooltip("What it does at each point, one per point. Pause off = it stands a random time between " +
             "the character's Standing min and max; pause on with 0 seconds = it walks straight on. " +
             "Rot on = it turns to face that way first.")]
    public List<PathPause> pauses = new();
}
