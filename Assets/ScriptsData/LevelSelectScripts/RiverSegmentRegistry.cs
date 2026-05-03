using UnityEngine;
using System.Collections.Generic;

public class RiverSegmentRegistry : MonoBehaviour
{
    public static RiverSegmentRegistry Instance;

    private Dictionary<string, RiverSegmentID> _segments = new();

private void Awake()
{
    Instance = this;

    // Pick up any segments that already called OnEnable before we were ready,
    // but skip visual source segments that opted out of registration.
    foreach (var segment in FindObjectsOfType<RiverSegmentID>())
        if (!segment.SkipRegistration)
            Register(segment);
}

    public void Register(RiverSegmentID segment)
    {
        if (segment.SkipRegistration) return;
        if (!_segments.ContainsKey(segment.SegmentID))
            _segments.Add(segment.SegmentID, segment);
    }

    public void Unregister(RiverSegmentID segment)
    {
        _segments.Remove(segment.SegmentID);
    }

    public RiverSegmentID GetSegment(string id)
    {
        _segments.TryGetValue(id, out var segment);
        return segment;
    }

    public RiverSegmentID GetFirstByType(RiverSegmentID.SegmentType type)
    {
        foreach (var seg in _segments.Values)
            if (seg.Type == type) return seg;
        return null;
    }
}