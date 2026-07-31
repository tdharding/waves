using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Anything that wants to act as an instanced point light implements this and registers with
/// InstancedLightManager. Street lights are the first user, but it is deliberately generic.
/// </summary>
public interface IInstancedLight
{
    Vector3 InstLightPosition { get; }   // world position of the light
    float   InstLightRadius   { get; }   // illumination radius (world units)
    bool    InstLightActive   { get; }   // only active lights contribute (e.g. a lit street light)
}

/// <summary>
/// Collects every active IInstancedLight and pushes them to the shader as bare $Globals each frame
/// (Shader.SetGlobalVectorArray / SetGlobalFloat) for InstancedLights.hlsl to read. One manager
/// serves the whole level; it is created lazily on first registration, so no scene setup is needed.
///
/// Mirrors BoatLightController's "push a global every frame" approach — re-pushing each frame means
/// a shader reimport that wipes the globals self-heals next frame. The intensity/falloff here are
/// the shared knobs, matching the boat's single radius/softness.
/// </summary>
[ExecuteAlways]
public class InstancedLightManager : MonoBehaviour
{
    // Must equal INST_LIGHT_MAX in InstancedLights.hlsl. Raising it costs a per-pixel loop
    // iteration on every shader that reads the lights, so keep it tight.
    public const int MAX = 16;

    [Tooltip("Global brightness multiplier applied to every instanced light.")]
    [SerializeField] float intensity = 1f;

    [Tooltip("Fraction of each light's radius that stays full brightness before fading to its edge (0..1).")]
    [Range(0f, 1f)] [SerializeField] float innerFalloff = 0.3f;

    static readonly int PositionsId = Shader.PropertyToID("_InstLightPositions");
    static readonly int CountId     = Shader.PropertyToID("_InstLightCount");
    static readonly int IntensityId = Shader.PropertyToID("_InstLightIntensity");
    static readonly int FalloffId   = Shader.PropertyToID("_InstLightFalloff");

    static readonly List<IInstancedLight> _lights = new List<IInstancedLight>();
    static readonly Vector4[] _buffer = new Vector4[MAX];
    static InstancedLightManager _instance;
    static bool _budgetWarned;

    // Snapshot of the lights that actually made it into _buffer on the last push. Debug gizmos read
    // this so a light dropped by the MAX budget looks different from one that is contributing.
    static readonly List<IInstancedLight> _pushed = new List<IInstancedLight>();
    static bool _hasPushed;

    /// <summary>True once a push has run, so gizmos know whether IsContributing means anything yet.</summary>
    public static bool HasPushData => _hasPushed;

    /// <summary>Did this light's position reach _InstLightPositions on the last push?</summary>
    public static bool IsContributing(IInstancedLight light) => _pushed.Contains(light);

    /// <summary>Inner falloff currently being pushed — lets gizmos draw the full-brightness core.</summary>
    public static float CurrentInnerFalloff => _instance != null ? _instance.innerFalloff : 0.3f;

    // Domain-reload-safe: clear statics at play start regardless of the "Reload Domain" setting.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _lights.Clear();
        _pushed.Clear();
        _instance = null;
        _budgetWarned = false;
        _hasPushed = false;
    }

    public static void Register(IInstancedLight light)
    {
        if (light == null || _lights.Contains(light)) return;
        _lights.Add(light);
        EnsureInstance();
    }

    public static void Unregister(IInstancedLight light)
    {
        _lights.Remove(light);
    }

    static void EnsureInstance()
    {
        if (_instance != null) return;
#if UNITY_2023_1_OR_NEWER
        _instance = FindFirstObjectByType<InstancedLightManager>();
#else
        _instance = FindObjectOfType<InstancedLightManager>();
#endif
        if (_instance == null)
        {
            var go = new GameObject("InstancedLightManager") { hideFlags = HideFlags.DontSave };
            _instance = go.AddComponent<InstancedLightManager>();
        }
    }

    void Awake()   => _instance = this;
    void OnDestroy() { if (_instance == this) _instance = null; }

    // LateUpdate so lights that moved this frame are captured after their own updates.
    void LateUpdate() => Push();

    void Push()
    {
        int count = 0;
        _pushed.Clear();
        for (int i = 0; i < _lights.Count; i++)
        {
            var l = _lights[i];
            if (l == null || !l.InstLightActive) continue;
            if (count >= MAX) { WarnBudget(); break; }

            Vector3 p = l.InstLightPosition;
            _buffer[count] = new Vector4(p.x, p.y, p.z, Mathf.Max(l.InstLightRadius, 0.0001f));
            _pushed.Add(l);
            count++;
        }
        _hasPushed = true;

        // Park unused slots far away with a zero radius so they never contribute.
        for (int i = count; i < MAX; i++)
            _buffer[i] = new Vector4(99999f, 99999f, 99999f, 0.0001f);

        Shader.SetGlobalVectorArray(PositionsId, _buffer);
        Shader.SetGlobalFloat(CountId, count);
        Shader.SetGlobalFloat(IntensityId, intensity);
        Shader.SetGlobalFloat(FalloffId, innerFalloff);
    }

    static void WarnBudget()
    {
        if (_budgetWarned) return;
        _budgetWarned = true;
        Debug.LogWarning($"[InstancedLightManager] More than {MAX} active instanced lights — extras are dropped. " +
                         $"Raise MAX here and INST_LIGHT_MAX in InstancedLights.hlsl together (costs a per-pixel loop iteration).");
    }
}
