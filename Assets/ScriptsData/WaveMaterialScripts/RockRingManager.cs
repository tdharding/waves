using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Anything that wants the water to throw bands around it implements this and registers with
/// RockRingManager. Procedural spikes are the first (and so far only) user, but nothing here
/// knows what a spike is.
/// </summary>
public interface IRockRing
{
    Vector3 RingCentre        { get; }  // world position where the rock meets the water
    float   RingRadius        { get; }  // its radius at the waterline — where band 0 starts
    float   RingLobeCount     { get; }  // how many times the rock's cross-section bulges
    float   RingLobeAmplitude { get; }  // how far it bulges, as a fraction of the radius
    float   RingTwist         { get; }  // radians the lobes turn per world unit travelled outward
    float   RingPhaseOffset   { get; }  // so neighbouring rocks don't pulse in lockstep
    bool    RingActive        { get; }
}

/// <summary>
/// Collects every rock that wants rings, keeps the ones near the boat, and pushes them to the
/// water shader as bare $Globals each frame for RockRings.hlsl to read. One manager serves the
/// whole level and creates itself on first registration, so no scene setup is needed.
///
/// Built on the same bones as InstancedLightManager — statics cleared on play, lazy instance,
/// full slot count pushed every frame so a shader reimport self-heals — with one addition: the
/// LOD pass. Rocks are sorted by distance to the boat and only the nearest few inside
/// <see cref="lodRadius"/> are pushed at all, which is what keeps the per-pixel loop in the
/// water shader short on a level carrying forty rocks.
///
/// The fade at that edge is worked out HERE rather than in the shader, because it is the same
/// answer for every pixel of a given rock. It rides in the unused .y of each packed position.
/// </summary>
[ExecuteAlways]
public class RockRingManager : MonoBehaviour
{
    // Must equal MAX_ROCK_RINGS in RockRings.hlsl — the two are a matched pair and changing one
    // without the other silently drops rocks or reads past the array.
    //
    // This is a CEILING, not a running cost. The shader loop breaks on the pushed count, so a
    // level with three rocks near the boat pays for three however high this sits; what it really
    // buys is uniform space (two float4 per slot) and shader code size. Use `activeBudget` below
    // to trim the working number at runtime — that is the one worth tuning.
    //
    // Raising it is a recompile. If you raise it and nothing changes, restart the editor: a global
    // array locks its size the first time Shader.SetGlobalVectorArray writes it.
    public const int MAX = 48;

    [Header("Budget")]
    [Tooltip("How many rocks may throw bands at once, up to the shader's ceiling. Lower it to buy " +
             "back fill rate on a crowded level without pulling the LOD radius in — the nearest " +
             "rocks keep their rings and the far ones simply go quiet.")]
    [Range(1, MAX)] [SerializeField] int activeBudget = MAX;

    [Header("LOD")]
    [Tooltip("Rocks further than this from the boat throw no bands at all. Driven by the wave " +
             "preset at runtime (Wave Effects Tuner ▸ Rock Rings), so this is the starting value.")]
    [SerializeField] float lodRadius = 18f;

    [Tooltip("Fraction of that distance a rock stays at full strength before fading out, so rocks " +
             "entering range fade up instead of popping. 0.8 = the last fifth of the range fades.")]
    [Range(0f, 1f)] [SerializeField] float lodFeather = 0.75f;

    [Header("References")]
    [Tooltip("The boat the LOD measures from. Found automatically if left unset.")]
    [SerializeField] Transform boat;

    [Tooltip("Wave material to write alongside the globals. Taken from the WaveMaterialController " +
             "if left unset.")]
    [SerializeField] WaveMaterialController waveController;

    static readonly int PositionsId = Shader.PropertyToID("_RockRingPositions");
    static readonly int ShapeId     = Shader.PropertyToID("_RockRingShape");
    static readonly int CountId     = Shader.PropertyToID("_RockRingCount");

    static readonly List<IRockRing> _rocks  = new List<IRockRing>();
    static readonly Vector4[]       _posBuf = new Vector4[MAX];
    static readonly Vector4[]       _shpBuf = new Vector4[MAX];

    // Scratch for the LOD sort. Reused so a per-frame sort never allocates.
    struct Candidate { public IRockRing Rock; public float Distance; }
    static readonly List<Candidate> _near = new List<Candidate>();
    static readonly System.Comparison<Candidate> ByDistance = (a, b) => a.Distance.CompareTo(b.Distance);

    static RockRingManager _instance;
    static bool _budgetWarned;

    // Snapshot of what actually reached the shader last push, so gizmos can tell a rock that is
    // contributing from one the budget dropped.
    static readonly List<IRockRing> _pushed = new List<IRockRing>();

    /// <summary>Did this rock's data reach the shader on the last push?</summary>
    public static bool IsContributing(IRockRing rock) => _pushed.Contains(rock);

    // Live counts for the inspector readout. Three separate numbers because they answer three
    // different questions: how many rocks exist at all, how many the LOD let through, and how many
    // actually reached the shader. When the middle one exceeds the last, the budget is biting.
    /// <summary>Every rock registered on the level, in range or not.</summary>
    public static int RegisteredCount => _rocks.Count;
    /// <summary>Rocks inside the LOD radius on the last push, before the budget was applied.</summary>
    public static int InRangeCount { get; private set; }
    /// <summary>Rocks whose data actually reached the shader on the last push.</summary>
    public static int RenderingCount => _pushed.Count;
    /// <summary>The budget the last push ran under, after clamping to MAX.</summary>
    public static int LastBudget { get; private set; }

    // Domain-reload-safe: clear statics at play start regardless of the "Reload Domain" setting.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _rocks.Clear();
        _pushed.Clear();
        _near.Clear();
        _instance = null;
        _budgetWarned = false;
        InRangeCount = 0;
        LastBudget = 0;
    }

    public static void Register(IRockRing rock)
    {
        if (rock == null || _rocks.Contains(rock)) return;
        _rocks.Add(rock);
        EnsureInstance();
    }

    public static void Unregister(IRockRing rock)
    {
        _rocks.Remove(rock);
        _pushed.Remove(rock);
    }

    /// <summary>
    /// Sets the LOD range from the wave preset, so how far the rings carry travels with the rest
    /// of the water's look instead of being pinned to whatever this component was left holding.
    /// Called every frame by WaveMaterialController; ignored when no manager exists yet.
    /// </summary>
    public static void SetLod(float radius, float feather)
    {
        if (_instance == null) return;
        _instance.lodRadius  = Mathf.Max(radius, 0f);
        _instance.lodFeather = Mathf.Clamp01(feather);
    }

    static void EnsureInstance()
    {
        if (_instance != null) return;
#if UNITY_2023_1_OR_NEWER
        _instance = FindFirstObjectByType<RockRingManager>();
#else
        _instance = FindObjectOfType<RockRingManager>();
#endif
        if (_instance == null)
        {
            var go = new GameObject("RockRingManager") { hideFlags = HideFlags.DontSave };
            _instance = go.AddComponent<RockRingManager>();
        }
    }

    void Awake()    => _instance = this;
    void OnDestroy() { if (_instance == this) _instance = null; }

    // LateUpdate so rocks and the boat have both finished moving for the frame.
    void LateUpdate() => Push();

    void Push()
    {
        Vector3 boatPos = ResolveBoat();
        float   full    = lodRadius * lodFeather;   // stays at full strength inside this

        // ── Gather what is in range ──────────────────────────────────────────
        _near.Clear();
        for (int i = 0; i < _rocks.Count; i++)
        {
            var r = _rocks[i];
            if (r == null || !r.RingActive) continue;

            Vector3 c  = r.RingCentre;
            float   dx = c.x - boatPos.x;
            float   dz = c.z - boatPos.z;
            float   d  = Mathf.Sqrt(dx * dx + dz * dz);
            if (d > lodRadius) continue;

            _near.Add(new Candidate { Rock = r, Distance = d });
        }

        // Nearest first, so when more rocks are in range than there are slots the ones that get
        // dropped are the faint far ones already fading out.
        if (_near.Count > 1) _near.Sort(ByDistance);

        int budget = Mathf.Clamp(activeBudget, 1, MAX);
        InRangeCount = _near.Count;
        LastBudget   = budget;
        if (_near.Count > budget) WarnBudget(budget);

        // ── Pack ─────────────────────────────────────────────────────────────
        _pushed.Clear();
        int count = Mathf.Min(_near.Count, budget);
        for (int i = 0; i < count; i++)
        {
            var     rock = _near[i].Rock;
            Vector3 c    = rock.RingCentre;

            // 1 well inside the range, easing to 0 at its edge. Same shape as the boat light's
            // falloff, so a rock fades out the way everything else around the boat does.
            //
            // The distance is normalised FIRST, then smoothed. Mathf.SmoothStep is not HLSL's
            // smoothstep: it interpolates between its first two arguments by the third, clamped to
            // 0..1 — so handing it a raw world distance returns the endpoint, not a 0..1 blend.
            // Done that way every rock got the same fade regardless of where it stood, and that
            // fade was a large negative number, which inverted the bands and multiplied them.
            float k    = Mathf.InverseLerp(full, lodRadius, _near[i].Distance);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, k);

            // .y carries the fade because the water is a horizontal plane — the shader only ever
            // reads .xz, so world height is free.
            _posBuf[i] = new Vector4(c.x, fade, c.z, Mathf.Max(rock.RingRadius, 0.0001f));
            _shpBuf[i] = new Vector4(rock.RingLobeCount, rock.RingLobeAmplitude,
                                     rock.RingTwist, rock.RingPhaseOffset);
            _pushed.Add(rock);
        }

        // Park unused slots far away with no radius so they can never reach a pixel.
        for (int i = count; i < MAX; i++)
        {
            _posBuf[i] = new Vector4(99999f, 0f, 99999f, 0.0001f);
            _shpBuf[i] = Vector4.zero;
        }

        // ── Push ─────────────────────────────────────────────────────────────
        // Both routes, every frame: globally for the SRP-batched path, and onto the material for
        // the non-batched one. A global array locks its size on first set, so the full slot count
        // always goes out even when only three rocks are near.
        Shader.SetGlobalVectorArray(PositionsId, _posBuf);
        Shader.SetGlobalVectorArray(ShapeId,     _shpBuf);
        Shader.SetGlobalFloat(CountId, count);

        Material mat = ResolveMaterial();
        if (mat != null)
        {
            mat.SetVectorArray(PositionsId, _posBuf);
            mat.SetVectorArray(ShapeId,     _shpBuf);
            mat.SetFloat(CountId, count);
        }
    }

    static readonly int BoatCentreId = Shader.PropertyToID("_BoatWorldCenter");

    // Where the LOD measures from. Preference order matters:
    //   1. An explicitly assigned transform, if someone wanted this pinned to something specific.
    //   2. _BoatWorldCenter — the global BoatToWaterMaterial already pushes every frame from the
    //      boat VISUAL. Using it means the ring LOD agrees exactly with the rock darkness mask and
    //      every other boat-centred effect, rather than measuring from a transform half a metre
    //      away, and it costs nothing to read.
    //   3. Finding a BoatMovement, for the frames before anything has pushed the global.
    // A zero global is treated as "never pushed" — the boat sitting exactly on the world origin
    // would be indistinguishable, but it would also mean the level had not started properly.
    Vector3 ResolveBoat()
    {
        if (boat != null) return boat.position;

        Vector4 g = Shader.GetGlobalVector(BoatCentreId);
        if (g.sqrMagnitude > 1e-8f) return new Vector3(g.x, g.y, g.z);

#if UNITY_2023_1_OR_NEWER
        var mover = FindFirstObjectByType<BoatMovement>();
#else
        var mover = FindObjectOfType<BoatMovement>();
#endif
        return mover != null ? mover.transform.position : Vector3.zero;
    }

    Material ResolveMaterial()
    {
        if (waveController == null)
        {
#if UNITY_2023_1_OR_NEWER
            waveController = FindFirstObjectByType<WaveMaterialController>();
#else
            waveController = FindObjectOfType<WaveMaterialController>();
#endif
        }
        return waveController != null ? waveController.waveMaterial : null;
    }

    static void WarnBudget(int budget)
    {
        if (_budgetWarned) return;
        _budgetWarned = true;
        string how = budget < MAX
            ? $"Active Budget is set to {budget} of a possible {MAX} — raise it on the manager."
            : $"That is the ceiling: raise MAX here and MAX_ROCK_RINGS in RockRings.hlsl together, " +
              $"then restart the editor so the global array resizes.";
        Debug.LogWarning($"[RockRingManager] More than {budget} rocks are inside the ring LOD range — the " +
                         $"furthest are dropped. Pull the LOD radius in, or: {how}");
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 c = ResolveBoat();
        c.y += 0.02f;   // just clear of the water so the discs read

        // Solid discs, not wire circles: the filled area is the range, and the gap between the
        // two is the band where rocks fade out.
        UnityEditor.Handles.color = new Color(0.3f, 0.8f, 1f, 0.10f);
        UnityEditor.Handles.DrawSolidDisc(c, Vector3.up, lodRadius);

        UnityEditor.Handles.color = new Color(0.3f, 0.8f, 1f, 0.14f);
        UnityEditor.Handles.DrawSolidDisc(c, Vector3.up, lodRadius * lodFeather);

        for (int i = 0; i < _pushed.Count; i++)
        {
            if (_pushed[i] == null) continue;
            UnityEditor.Handles.color = new Color(1f, 0.9f, 0.4f, 0.5f);
            UnityEditor.Handles.DrawSolidDisc(_pushed[i].RingCentre + Vector3.up * 0.02f,
                                              Vector3.up, _pushed[i].RingRadius);
        }
    }
#endif
}
