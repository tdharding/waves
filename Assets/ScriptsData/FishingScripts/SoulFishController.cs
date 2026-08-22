using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;

/// <summary>
/// Level-wide controller for soul fish. Owns the shared swim speed (overriding whatever each fish
/// prefab's SplineAnimate authored) and reports what's live in the level for the Inspector.
///
/// Speed is applied continuously, so it retunes live in play mode and automatically covers fish
/// that appear later (a fish-bowl shoal joining, a tributary route rebuild, etc.). It also forces
/// SplineAnimate into Speed mode — in Time mode a fish would cover its whole spline in a fixed
/// duration, so it would speed up every time the street-light route got longer.
///
/// Add this to a GameObject in the scene so your tuning persists between play sessions. If none
/// exists one is created at runtime with the defaults below, so nothing breaks without it.
/// </summary>
[ExecuteAlways]
public class SoulFishController : MonoBehaviour
{
    public static SoulFishController Instance { get; private set; }

    // ─────────────────────────────────────────────
    // SWIMMING
    // ─────────────────────────────────────────────

    [Header("Swimming")]
    [Tooltip("Swim speed in world units per second for every soul fish in the level. Overrides the " +
             "Max Speed on the fish prefab's SplineAnimate. The fish prefab authored 0.165, which is " +
             "roughly 6 seconds per world unit — very slow for a large arena.")]
    [SerializeField] float swimSpeed = 2f;

    [Tooltip("Per-fish spread around Swim Speed so the shoal doesn't swim in lockstep. " +
             "0 = identical speeds, 0.2 = ±20%. Deterministic per fish, so it never jitters.")]
    [Range(0f, 0.6f)] [SerializeField] float speedVariance = 0.15f;

    [Tooltip("Untick to leave every fish on whatever speed its prefab authored.")]
    [SerializeField] bool overrideSpeed = true;

    [Tooltip("How wide fish orbit a street light, as a fraction of that light's painted pool radius. " +
             "1 = they swim on the pool's outer rim; lower keeps the shoal inside the visible zone. " +
             "Applies to river lights and joined tributaries alike, and retunes live.")]
    [Range(0.1f, 1f)] [SerializeField] float swimRadiusFactor = 0.6f;

    /// <summary>
    /// Shared orbit fraction read by SoulZoneStreetLightChain and SoulZoneTributaryLink. Falls back
    /// to the default when no controller exists yet, so spawning never depends on load order.
    /// </summary>
    public static float SwimRadiusFactor => Instance != null ? Instance.swimRadiusFactor : 0.6f;

    [Tooltip("How loosely fish follow the zone path. 0 = strict single file down the centreline; " +
             "1 = spread right across the painted band. Each fish keeps its own offset for life, " +
             "so the shoal holds formation rather than shimmering.")]
    [Range(0f, 1f)] [SerializeField] float pathSpread = 0.5f;

    /// <summary>Shared path-adherence spread, read by the street-light chain and tributary links.</summary>
    public static float PathSpread => Instance != null ? Instance.pathSpread : 0.5f;

    static readonly Dictionary<Transform, float> _lateralFactor = new Dictionary<Transform, float>();
    static int _lateralSeed;

    /// <summary>
    /// Nudges each fish sideways off the spline centreline so a shoal fills the corridor instead of
    /// swimming in single file. Called from LateUpdate — SplineAnimate writes the fish's position in
    /// Update, so this layers on top each frame and never accumulates.
    /// </summary>
    public static void ApplyLateralSpread(IReadOnlyList<SplineAnimate> fish, float corridorRadius)
    {
        float spread = PathSpread;
        if (fish == null || spread <= 0f || corridorRadius <= 0f) return;

        foreach (var sa in fish)
        {
            if (sa == null || !sa.IsPlaying) continue;   // paused fish would drift if we kept adding

            Transform t = sa.transform;
            if (!_lateralFactor.TryGetValue(t, out float f))
            {
                _lateralSeed++;
                f = (Mathf.Abs(Mathf.Sin(_lateralSeed * 78.233f) * 43758.5453f) % 1f) * 2f - 1f;  // -1..1
                _lateralFactor[t] = f;
            }
            t.position += t.right * (f * spread * corridorRadius);
        }
    }

    [Tooltip("Seconds between speed/count refreshes. Cheap either way; 0 = every frame.")]
    [SerializeField] float refreshInterval = 0.25f;

    // ─────────────────────────────────────────────
    // SURFACE SPRITES
    // One image stamped on the water above each fish. Published as globals every frame and read by
    // the SoulFishSurfaceSprites subgraph — the texture is global too, so the node needs no wiring.
    // ─────────────────────────────────────────────

    [Header("Surface Sprites")]
    [Tooltip("Image drawn on the water surface above each soul fish. Leave empty to disable.")]
    [SerializeField] Texture2D fishSurfaceSprite;

    [Tooltip("World-space size of each sprite.")]
    [SerializeField] float spriteSize = 1f;

    [Tooltip("Master multiplier on the sprite's colour and alpha.")]
    [SerializeField] float spriteStrength = 1f;

    [Tooltip("Turn each sprite to face the fish's swim direction. Off = all sprites axis-aligned.")]
    [SerializeField] bool spriteFollowsHeading = true;

    // Shader-side budget — must equal SOULFISH_SPRITE_MAX in SoulFishSurfaceSprites.hlsl.
    const int SPRITE_MAX = 24;
    static readonly int SpritePositionsId = Shader.PropertyToID("_SoulFishSpritePositions");
    static readonly int SpriteCountId     = Shader.PropertyToID("_SoulFishSpriteCount");
    static readonly int SpriteSizeId      = Shader.PropertyToID("_SoulFishSpriteSize");
    static readonly int SpriteStrengthId  = Shader.PropertyToID("_SoulFishSpriteStrength");
    static readonly int SpriteTexId       = Shader.PropertyToID("_SoulFishSpriteTex");
    readonly Vector4[] _spriteBuffer = new Vector4[SPRITE_MAX];
    bool _spriteBudgetWarned;

    /// <summary>Fish whose sprite reached the shader on the last push (for the Inspector readout).</summary>
    public int SpritesDrawn { get; private set; }

    // ─────────────────────────────────────────────
    // LIVE READOUT (populated at runtime, shown in the Inspector)
    // ─────────────────────────────────────────────

    public int   FishInLevel     { get; private set; }
    public int   ShoalCount      => _shoals.Count;
    public int   CatchableFish   { get; private set; }
    public int   LitStreetLights { get; private set; }
    public int   StreetLights    { get; private set; }
    public int   MaskZones       { get; private set; }
    public int   MaskPackedPts   { get; private set; }
    public float SwimSpeed       => swimSpeed;

    static readonly List<SoulShoalController> _shoals = new List<SoulShoalController>();
    readonly Dictionary<Transform, SplineAnimate> _animCache   = new Dictionary<Transform, SplineAnimate>();
    readonly Dictionary<Transform, float>         _speedFactor = new Dictionary<Transform, float>();
    int   _factorSeed;
    float _nextRefresh;

    // ─────────────────────────────────────────────
    // LIFECYCLE / REGISTRATION
    // ─────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _shoals.Clear();
        Instance = null;
    }

    void Awake()     => Instance = this;
    void OnEnable()  => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    public static void RegisterShoal(SoulShoalController shoal)
    {
        if (shoal == null || _shoals.Contains(shoal)) return;
        _shoals.Add(shoal);
        EnsureInstance();
        Instance?.Apply();   // catch the new shoal's fish immediately
    }

    public static void UnregisterShoal(SoulShoalController shoal)
    {
        _shoals.Remove(shoal);
        PruneCaches();
    }

    static void EnsureInstance()
    {
        if (Instance != null) return;
#if UNITY_2023_1_OR_NEWER
        Instance = FindFirstObjectByType<SoulFishController>();
#else
        Instance = FindObjectOfType<SoulFishController>();
#endif
        if (Instance == null)
        {
            var go = new GameObject("SoulFishController (auto)");
            Instance = go.AddComponent<SoulFishController>();
            Debug.Log("[SoulFishController] No controller in the scene — created one with default settings. " +
                      "Add a SoulFishController to a scene object if you want your tuning to persist.");
        }
    }

    // ─────────────────────────────────────────────
    // APPLY / COUNT
    // ─────────────────────────────────────────────

    void LateUpdate()
    {
        // Sprites track moving fish, so they publish every frame regardless of the refresh interval.
        PushSprites();

        if (refreshInterval > 0f && Time.realtimeSinceStartup < _nextRefresh) return;
        _nextRefresh = Time.realtimeSinceStartup + refreshInterval;
        Apply();
    }

    // Publishes one entry per live fish: .xz world position, .y heading (radians), .w per-fish scale
    // (0 = use the global size). Globals only, re-pushed each frame — that's what survives a shader
    // reimport wiping $Globals, exactly like the wave mask and instanced lights.
    void PushSprites()
    {
        int count = 0;

        if (fishSurfaceSprite != null && spriteStrength > 0f)
        {
            for (int i = _shoals.Count - 1; i >= 0 && count < SPRITE_MAX; i--)
            {
                var shoal = _shoals[i];
                if (shoal == null) { _shoals.RemoveAt(i); continue; }

                foreach (var t in shoal.FishList)
                {
                    if (t == null) continue;
                    if (count >= SPRITE_MAX) { WarnSpriteBudget(); break; }

                    Vector3 p = t.position;
                    float heading = 0f;
                    if (spriteFollowsHeading)
                    {
                        Vector3 f = t.forward;
                        heading = Mathf.Atan2(f.x, f.z);
                    }
                    _spriteBuffer[count] = new Vector4(p.x, heading, p.z, 0f);
                    count++;
                }
            }
        }

        for (int i = count; i < SPRITE_MAX; i++)
            _spriteBuffer[i] = new Vector4(99999f, 0f, 99999f, 0f);

        SpritesDrawn = count;

        if (fishSurfaceSprite != null) Shader.SetGlobalTexture(SpriteTexId, fishSurfaceSprite);
        Shader.SetGlobalVectorArray(SpritePositionsId, _spriteBuffer);
        Shader.SetGlobalFloat(SpriteCountId, count);
        Shader.SetGlobalFloat(SpriteSizeId, Mathf.Max(spriteSize, 0.0001f));
        Shader.SetGlobalFloat(SpriteStrengthId, spriteStrength);
    }

    void WarnSpriteBudget()
    {
        if (_spriteBudgetWarned) return;
        _spriteBudgetWarned = true;
        Debug.LogWarning($"[SoulFishController] More than {SPRITE_MAX} fish on screen — extra surface sprites are " +
                         $"dropped. Raise SPRITE_MAX here and SOULFISH_SPRITE_MAX in SoulFishSurfaceSprites.hlsl together " +
                         $"(costs a per-pixel loop iteration on the water).");
    }

    void OnValidate() => Apply();   // live retune while dragging the slider

    /// <summary>Pushes the current speed to every live fish and refreshes the readout.</summary>
    public void Apply()
    {
        int fish = 0, catchable = 0;

        for (int i = _shoals.Count - 1; i >= 0; i--)
        {
            var shoal = _shoals[i];
            if (shoal == null) { _shoals.RemoveAt(i); continue; }

            bool shoalCatchable = shoal.CanFish;
            foreach (var t in shoal.FishList)
            {
                if (t == null) continue;
                fish++;
                if (shoalCatchable) catchable++;

                if (!overrideSpeed) continue;

                if (!_animCache.TryGetValue(t, out var sa) || sa == null)
                {
                    sa = t.GetComponent<SplineAnimate>();
                    if (sa == null) continue;
                    _animCache[t] = sa;
                }

                // Speed mode: constant pace no matter how long the route grows.
                sa.AnimationMethod = SplineAnimate.Method.Speed;
                sa.MaxSpeed        = SpeedFor(t);
            }
        }

        FishInLevel   = fish;
        CatchableFish = catchable;

        // Street lights + mask usage — handy context in the same place.
        int lit = 0, lamps = 0;
        foreach (var l in StreetLightController.LitLights) if (l != null) lit++;
        LitStreetLights = lit;
#if UNITY_2023_1_OR_NEWER
        lamps = FindObjectsByType<StreetLightController>(FindObjectsSortMode.None).Length;
#else
        lamps = FindObjectsOfType<StreetLightController>().Length;
#endif
        StreetLights = lamps;

        if (Application.isPlaying)
        {
            MaskZones     = SoulFishWaveLinker.ActiveZoneCount;
            MaskPackedPts = SoulFishWaveLinker.PackedPointCount;
        }
    }

    /// <summary>
    /// Per-fish speed. Each fish is handed a 0..1 factor the first time it's seen and keeps it for
    /// life, so speeds never jitter frame to frame. The raw factor is what's cached (not the final
    /// speed), so dragging Swim Speed or Speed Variance retunes the whole shoal live.
    /// </summary>
    float SpeedFor(Transform fish)
    {
        if (speedVariance <= 0f) return swimSpeed;

        if (!_speedFactor.TryGetValue(fish, out float n))
        {
            _factorSeed++;
            n = Mathf.Abs(Mathf.Sin(_factorSeed * 12.9898f) * 43758.5453f) % 1f;   // 0..1
            _speedFactor[fish] = n;
        }
        return swimSpeed * (1f + (n * 2f - 1f) * speedVariance);
    }

    // Drops entries whose fish have been destroyed, so the caches don't grow across level loads.
    static void PruneCaches()
    {
        var c = Instance;
        if (c == null) return;

        var stale = new List<Transform>();
        foreach (var kv in c._animCache) if (kv.Key == null) stale.Add(kv.Key);
        foreach (var t in stale) c._animCache.Remove(t);

        stale.Clear();
        foreach (var kv in c._speedFactor) if (kv.Key == null) stale.Add(kv.Key);
        foreach (var t in stale) c._speedFactor.Remove(t);
    }

    /// <summary>Set the shared swim speed from code (e.g. a slow-motion or panic state later).</summary>
    public void SetSwimSpeed(float speed)
    {
        swimSpeed = Mathf.Max(0f, speed);
        Apply();
    }
}
