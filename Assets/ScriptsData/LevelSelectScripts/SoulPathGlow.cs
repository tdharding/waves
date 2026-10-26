using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How far each soul route's band has drawn out, handed to the River Glow shader so the soul path
/// glow baked round a route comes up behind its band's front and nowhere ahead of it.
///
/// The bake (Bake Soul Path Glow, in the Level Select River Tuner) gives every route a slot and
/// keeps the routes in slot order on <see cref="LevelSelectLight.soulGlowRouteIds"/>; the shader
/// reads route N's front from slot N of <c>_SoulGlowFronts</c>.
///
/// In play mode <see cref="TravellingSoulFishController"/> hands over its fronts every frame. Out
/// of play mode nothing draws out, so every route is shown whole — that is how the bake is seen
/// while it is being tuned. In play mode with no controller handing fronts over, none shows.
/// </summary>
public static class SoulPathGlow
{
    /// <summary>Most routes the bake keeps. Must match SOULGLOW_MAX_ROUTES in RiverGlow.hlsl.</summary>
    public const int MaxRoutes = 32;

    // Past the end of any route, and before the start of any.
    private const float Whole  = 1e9f;
    private const float NotYet = -1e9f;

    private static readonly int     FrontsId = Shader.PropertyToID("_SoulGlowFronts");
    private static readonly float[] Fronts   = new float[MaxRoutes];

    private static IReadOnlyDictionary<string, float> _fronts;
    private static int _frontsFrame = -1;

    /// <summary>
    /// Every open route's band front, in world units along the route, keyed by route id. Called
    /// every frame by the controller; a route missing from it shows no glow.
    /// </summary>
    public static void SetFronts(IReadOnlyDictionary<string, float> fronts)
    {
        _fronts      = fronts;
        _frontsFrame = Time.frameCount;
        PushFronts(LevelSelectLight.Current);
    }

    /// <summary>
    /// Publishes the fronts, by the slots <paramref name="light"/>'s bake gave the routes. Every
    /// frame, the whole array, the same length each time — a bare global array keeps the length
    /// it was first sent with.
    /// </summary>
    public static void PushFronts(LevelSelectLight light)
    {
        bool driven = Application.isPlaying && _fronts != null && Time.frameCount - _frontsFrame <= 1;
        var  ids    = light != null ? light.soulGlowRouteIds : null;

        for (int i = 0; i < MaxRoutes; i++)
        {
            if (!driven)
            {
                Fronts[i] = Application.isPlaying ? NotYet : Whole;
                continue;
            }

            string id = ids != null && i < ids.Count ? ids[i] : null;
            Fronts[i] = id != null && _fronts.TryGetValue(id, out float front) ? front : NotYet;
        }

        Shader.SetGlobalFloatArray(FrontsId, Fronts);
    }
}
