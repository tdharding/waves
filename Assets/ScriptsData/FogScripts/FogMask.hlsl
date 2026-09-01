// The two masks that decide where fog is allowed to appear on screen.
//
// Both are FRAGMENT masks: they multiply the fog sheet's alpha and touch nothing in the
// simulation. That separation is the whole point.
//
//   BoatMask      how far from the boat fog is drawn, radius and feather. A material property in
//                 the truest sense — it changes what you see and nothing else. It sizes no
//                 texture, retires no mass and moves no birth.
//
//   ObstacleMask  cuts fog off rocks, walls and lamps exactly. The skeleton is also pushed aside
//                 by the same obstacles, but that push cannot be exact: it has to be strong to
//                 hold a boundary, and a point pinned to a circle slides around it as the mass
//                 drifts past, which reads as fog accelerating. Letting this mask hold the
//                 boundary means the push only has to shape the fog, so it can be gentle enough
//                 not to lurch. Displacement for behaviour, mask for the edge.
//
// Every uniform here is a bare $Globals, pushed every frame by FogFieldManager — same reasoning
// as the soul-fish masks and the rock rings, so a shader reimport self-heals on the next frame.

#ifndef FOG_MASK_INCLUDED
#define FOG_MASK_INCLUDED

// Must match FOG_OBSTACLE_SLOTS in FogFieldManager. A global array locks its size on first set,
// so the manager always sends the full count even when three obstacles are near.
#define FOG_OBSTACLE_SLOTS 32

float4 _FogObstacles[FOG_OBSTACLE_SLOTS];   // xy = centre, z = clear radius, w = edge softness
float  _FogObstacleCount;

float4 _BoatWorldCenter;
float  _FogMaskRadius;
float  _FogMaskFeather;
float  _FogOpacity;

void FogMask_float(float3 WorldPos, out float BoatMask, out float ObstacleMask, out float Opacity)
{
    // ── the boat ─────────────────────────────────────────────────────────────
    // Feather is a FRACTION of the radius, so the fade always scales with the circle: 0 is a hard
    // edge at the radius, 1 fades all the way from the boat.
    float d     = length(WorldPos.xz - _BoatWorldCenter.xz);
    float inner = _FogMaskRadius * saturate(1.0 - _FogMaskFeather);
    BoatMask    = 1.0 - smoothstep(inner, max(_FogMaskRadius, inner + 1e-4), d);

    // ── obstacles ────────────────────────────────────────────────────────────
    // Multiplied rather than taken as a minimum, so two overlapping rocks clear their shared water
    // completely instead of leaving a wedge where neither is quite the nearest.
    ObstacleMask = 1.0;

    // ── overall opacity ──────────────────────────────────────────────────────
    // A master on the whole sheet, separate from Interior Fill: that one controls how solid a
    // mass reads from the middle to its edge, this one turns the entire fog up and down.
    Opacity = _FogOpacity;

    int n = min((int)_FogObstacleCount, FOG_OBSTACLE_SLOTS);
    for (int i = 0; i < n; i++)
    {
        float4 o = _FogObstacles[i];
        if (o.z <= 0.0) continue;

        float od = length(WorldPos.xz - o.xy);
        ObstacleMask *= smoothstep(o.z, o.z + max(o.w, 1e-4), od);
    }
}

void FogMask_half(half3 WorldPos, out half BoatMask, out half ObstacleMask, out half Opacity)
{
    float b, o, a;
    FogMask_float(WorldPos, b, o, a);
    BoatMask = (half)b;
    ObstacleMask = (half)o;
    Opacity = (half)a;
}

#endif
