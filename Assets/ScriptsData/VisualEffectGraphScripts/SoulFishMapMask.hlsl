// Soul fish zone mask painted onto the Map UI surface.
//
// Points arrive from SoulFishMapLinker already projected onto the map surface AND already run
// through the map's zoom transform, so everything here is in map-surface world space — the same
// space WorldPos is in. That's what keeps the zone in step with the procedurally-drawn walls and
// blocks, which scale because they're parented under the zoom content root.
//
// Layout per point (mirrors SoulFishWaveMask so both masks read the same registration data):
//   .xz = position on the map surface   .y = this point's radius   .w = 2 -> connect to the next
// The map plane only ever rotates about Y, so comparing in XZ stays distortion-free.
//
// NOTE THE _Map_ INFIX. These are bare uniforms, so they live in $Globals — a single slot shared
// across every shader in the project, NOT per-material. They were previously named
// _SoulFishPositions/_SoulFishCount, identical to SoulFishWaveMask's, so whichever linker wrote
// last won and the wave's 17-point river was being truncated to the map's 10-point cap. Never give
// these the same name as the wave mask's.
float4 _SoulFishMapPositions[10];
float _SoulFishMapCount;

// Still pushed by SoulFishMapLinker for other map shaders; this mask no longer needs them.
float4 _MapCenter;
float4 _MapSize;

float distToSegment_SoulMap(float2 p, float2 a, float2 b)
{
    float2 pa = p - a, ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h);
}

// Radius: fallback footprint (map units) for points that carry no radius of their own — fed from
// _SoulFishMarkerRadius, which the linker scales with zoom alongside the per-point radii.
void SoulFishMask_float(float3 WorldPos, float Radius, out float Mask)
{
    float mask  = 0.0;
    int   count = (int)_SoulFishMapCount;

    for (int i = 0; i < 10; i++)
    {
        if (i >= count)
            break;

        float2 p = _SoulFishMapPositions[i].xz;

        // Per-point radius rides in .y (the authored Grid Designer swim radius, converted to map
        // units and zoom-scaled). Loose fish register with y == 0 and fall back to Radius.
        float r = _SoulFishMapPositions[i].y > 0.0001 ? _SoulFishMapPositions[i].y : max(Radius, 0.0001);

        // Normalized distance to this node: 0 = centre, 1 = edge.
        float t = distance(WorldPos.xz, p) / r;

        // Connected points (w > 1.5) extend the zone along the segment, so a node chain reads as
        // one continuous river rather than beads. A closed loop keeps connecting off the end.
        if (i < 9 && i < count - 1 && _SoulFishMapPositions[i].w > 1.5)
            t = min(t, distToSegment_SoulMap(WorldPos.xz, p, _SoulFishMapPositions[i+1].xz) / r);

        mask = max(mask, 1.0 - saturate(t));
    }

    Mask = mask;
}