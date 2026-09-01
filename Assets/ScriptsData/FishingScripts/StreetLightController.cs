using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Lives on the street light prefab. The fish bowl at the top accepts one caught soul, dropped
/// via the same drag interaction the soul pipe triggers use (LevelSelectDraggableSoul), gated by
/// an optional SoulSlotIndicator range check on the same object. The fish is spent: it stays in
/// the bowl and lights the lamp, which tells the zone's SoulZoneStreetLightChain to draw the
/// path onward to this light.
///
/// Prefab contract (authored by hand, spawned by LevelSpawner at flagged zone nodes):
///  - This component + a collider on the "Interaction" layer (bowl hit target for the drop raycast).
///  - Optional SoulSlotIndicator with its indicatorRoot for the in-range bob.
///  - bowlFishVisual: shown once a soul is deposited. litVisual: shown while lit
///    (light #1 starts lit without an occupant, so the two can differ).
/// </summary>
public class StreetLightController : MonoBehaviour, IInstancedLight, IFogRepeller
{
    [Header("Visuals")]
    [Tooltip("Enabled when a soul has been deposited — the fish living in the bowl.")]
    [SerializeField] private GameObject bowlFishVisual;

    [Tooltip("Enabled while this light is lit. Light #1 starts lit with an empty bowl.")]
    [SerializeField] private GameObject litVisual;

    [Tooltip("Optional cloud of floating quads that drifts around the lamp while it is lit. " +
             "Assign the child holding the ParticleSystem + StreetLightParticles.")]
    [SerializeField] private StreetLightParticles litParticles;

    [Tooltip("Optional shaft of light under the lamp. Assign the child holding the StreetLightCone " +
             "and its base is sat on the waterline at spawn, so one prefab works at any water height.")]
    [SerializeField] private StreetLightCone lightCone;

    [Header("Instanced Light")]
    [Tooltip("Illumination radius this lamp casts onto objects (world units). Separate from the fish pool radius.")]
    [SerializeField] private float lightRadius = 6f;

    [Tooltip("Optional transform for the light source (the bulb). Falls back to this object's transform.")]
    [SerializeField] private Transform lightAnchor;

    [Tooltip("Offset from the anchor to the point the shader actually lights from. Dropping the point " +
             "down towards wall height is the usual fix for a lamp that looks bright but barely lifts " +
             "the wall — a light high above a horizontal wall normal gives a weak N·L.")]
    [SerializeField] private Vector3 lightOffset = Vector3.zero;

    [Tooltip("Apply the offset in the lamp's local space so it rotates with the object. " +
             "Off = world space, so the offset always points the same way regardless of lamp rotation.")]
    [SerializeField] private bool offsetInLocalSpace = true;

    [Header("Screen Glow")]
    [Tooltip("TOP of the cone of light — its tip. Leave blank and it sits on the default lit-from " +
             "point, same as the core glow. Shape is tuned on BrightnessGlowController under Street " +
             "Lights; these two anchors only say WHERE the cone runs.")]
    [SerializeField] private Transform haloGlowAnchor;

    [Tooltip("BOTTOM of the cone — where its base circle is centred. Drop the empty child you made " +
             "for it here; move it sideways to lean the cone. Only its X/Z are used: the height comes " +
             "from the arena baseline, which is the authority on the waterline. Blank = straight down " +
             "from the top point.")]
    [SerializeField] private Transform coneBaseAnchor;

    // Screen glow: while lit, BrightnessGlowController reads LitLights and puts a persistent glow
    // point on InstLightPosition, plus a second one on HaloGlowPosition. Shape is tuned there —
    // one set of knobs for every lamp — so the only per-prefab thing is the anchor above.

    public bool IsLit { get; private set; }
    public int  OccupantSoulIdentity { get; private set; } = -1;

    // Debug read-only: what the prefab actually has in its visual slots, so StreetLightDebugTracer
    // can report an unassigned one rather than leaving it to guesswork.
    /// <summary>The lamp's shaft of light, so the particle cloud can find it without being wired twice.</summary>
    public StreetLightCone LightCone => lightCone;

    public GameObject          DebugBowlFishVisual => bowlFishVisual;
    public GameObject          DebugLitVisual      => litVisual;
    public StreetLightParticles DebugLitParticles  => litParticles;

    // The transform the offset is measured from — the bulb if one is assigned, else this object.
    private Transform LightAnchorTransform => lightAnchor != null ? lightAnchor : transform;

    // IInstancedLight — InstancedLightManager reads these to add this lamp's glow to level objects.
    // Rotation-only for the local offset (TransformDirection, not TransformVector): lightRadius is a
    // raw world-unit number that ignores scale, so the offset ignores it too and the two stay consistent.
    public Vector3 InstLightPosition
    {
        get
        {
            Transform t = LightAnchorTransform;
            return t.position + (offsetInLocalSpace ? t.TransformDirection(lightOffset) : lightOffset);
        }
    }
    public float   InstLightRadius   => lightRadius;
    public bool    InstLightActive   => IsLit;

    /// <summary>True when the cone's tip has been given its own anchor rather than riding on the core point.</summary>
    public bool HasOwnHaloPoint => haloGlowAnchor != null;

    /// <summary>Tip of the cone — the top anchor if one is assigned, else the lit-from point.</summary>
    public Vector3 HaloGlowPosition => haloGlowAnchor != null ? haloGlowAnchor.position : InstLightPosition;

    // ── Fog ──────────────────────────────────────────────────────────────────
    // A lit lamp PUSHES fog out of its pool. It never gathers fog to itself — where fog sits is
    // the arena map's decision alone.
    //
    // The repel radius sits well inside lightRadius on purpose: push fog out as far as the light
    // reaches and it never enters the region where it would have been lit, leaving a clean dark
    // hole ringed by unlit fog. The intended shot is fog banked up glowing at the edge of the
    // lamp's reach, so the clear pool has to be smaller than the lit one.
    //
    // Lamps push harder than rocks. A rock is an obstacle fog wraps close around; a lit lamp is
    // burning it off, and should hold a space that reads as deliberate.
    //
    // It goes quiet when the lamp is unlit, which is the whole "clear the fog" mechanic: light a
    // stretch of lamps and the fog is physically squeezed out; kill one and fog creeps back over
    // the next few seconds on its own, with nothing tracking it.

    // ── Fog ──────────────────────────────────────────────────────────────────
    // A lit lamp PUSHES fog out of its pool. It never gathers fog to itself — where fog sits is
    // the fog map's decision alone.
    //
    // The numbers live on FogFieldManager, not here. How fog behaves around a lamp is a property
    // of the fog, and per-lamp fields meant one level could hold twenty different answers to the
    // same question. All this contributes is where the lamp is, how far it reaches, and whether
    // it is lit.
    //
    // Going quiet when unlit is the whole "clear the fog" mechanic: light a stretch of lamps and
    // fog is physically squeezed out; kill one and fog creeps back over the next few seconds, with
    // nothing tracking it.

    public Vector3 RepelCentre   => InstLightPosition;
    public float   RepelRadius   => lightRadius * FogFieldManager.LampClearFraction;
    public float   RepelClearRadius => FogFieldManager.LampClearRadius;
    public float   RepelStrength => FogFieldManager.LampStrength;
    public bool    RepelActive   => IsLit;

    /// <summary>True when the cone's base has been steered off the vertical by its own anchor.</summary>
    public bool HasConeBaseAnchor => coneBaseAnchor != null;

    /// <summary>Where the cone's base circle is centred. Only X/Z matter — the caller drops it onto the baseline.</summary>
    public Vector3 ConeBasePosition => coneBaseAnchor != null ? coneBaseAnchor.position : HaloGlowPosition;

    // Wired by LevelSpawner when the chain is assembled.
    [HideInInspector] public SoulZoneStreetLightChain chain;
    [HideInInspector] public int orderIndex;   // 0-based position in path order (0 = starts lit)

    // Every lit light in the level — kept for any consumer that wants the list directly. The
    // instanced-light shader gets its positions via InstancedLightManager (this registers as an
    // IInstancedLight below). Instances remove themselves on destroy, so level teardown cleans up.
    public static readonly List<StreetLightController> LitLights = new List<StreetLightController>();

    // Every street light in the level (lit or not) — the Map UI enumerates this to place markers.
    public static readonly List<StreetLightController> All = new List<StreetLightController>();

    // Domain-reload-safe, matching InstancedLightManager: with "Reload Domain" off these lists would
    // otherwise carry entries into the next play session, and BrightnessGlowController reads LitLights.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        LitLights.Clear();
        All.Clear();
        waterBaseline = null;
    }

    void Start()
    {
        // Left to Start rather than Awake: the lamp is instantiated and then placed, and the level's
        // baseline has to exist before its height can be asked for.
        AlignConeToWater();
    }

    /// <summary>
    /// Sits the shaft of light's base on the waterline. Called at spawn; public so a level that
    /// moves its rig afterwards can ask for it again.
    /// </summary>
    public void AlignConeToWater()
    {
        if (lightCone == null) return;
        if (!TryGetWaterHeight(out float waterY)) return;
        lightCone.SetBaseAtHeight(waterY);

        // The cloud sized itself to the shaft at Awake, before this moved the base onto the water,
        // so it has to be told the shape has changed under it.
        if (litParticles != null) litParticles.Reapply();
    }

    // The arena baseline owns waterline height, the same authority BoatToWaterMaterial uses. Cached
    // across lamps so a level full of them costs one scene search rather than one each.
    static BaselineMarker waterBaseline;

    static bool TryGetWaterHeight(out float y)
    {
        if (waterBaseline == null)
        {
            var spawner = FindFirstObjectByType<LevelSpawner>();
            waterBaseline = spawner != null ? spawner.GetBaselineMarker() : null;
        }

        y = waterBaseline != null ? waterBaseline.height : 0f;
        return waterBaseline != null;
    }

    void Awake()
    {
        if (!All.Contains(this)) All.Add(this);
        RefreshVisuals();
        // Register always; InstLightActive (= IsLit) gates whether it actually contributes light.
        InstancedLightManager.Register(this);
        // Same deal for fog: registered from the start, with RepelActive gating on IsLit.
        FogFieldManager.Register(this);
    }

    void OnDestroy()
    {
        All.Remove(this);
        LitLights.Remove(this);
        InstancedLightManager.Unregister(this);
        FogFieldManager.Unregister(this);
    }

    /// <summary>Called by LevelSpawner for light #1's initial state, and internally on feed.</summary>
    public void SetLit(bool lit, int occupantIdentity = -1)
    {
        IsLit = lit;
        OccupantSoulIdentity = occupantIdentity;

        if (lit) { if (!LitLights.Contains(this)) LitLights.Add(this); }
        else LitLights.Remove(this);

        RefreshVisuals();
    }

    /// <summary>
    /// Drop-target entry point (same contract as SoulEnterPipeTrigger.TryInsertSoul).
    /// Rejects when already lit or when this isn't the next light in path order — a rejected
    /// soul returns to the boat inventory.
    /// </summary>
    public bool TryInsertSoul(int soulIdentity)
    {
        if (IsLit)
        {
            Debug.Log($"[StreetLight] '{name}' already lit — soul {soulIdentity} rejected.");
            return false;
        }
        if (chain == null)
        {
            Debug.LogWarning($"[StreetLight] '{name}' has no chain wired — soul {soulIdentity} rejected.");
            return false;
        }
        if (!chain.CanFeed(this))
        {
            Debug.Log($"[StreetLight] '{name}' (#{orderIndex + 1}) is not the next light — soul {soulIdentity} rejected.");
            return false;
        }

        SetLit(true, soulIdentity);
        chain.OnStreetLightFed(this);
        Debug.Log($"[StreetLight] '{name}' (#{orderIndex + 1}) lit by soul {soulIdentity}.");
        return true;
    }

    void RefreshVisuals()
    {
        if (bowlFishVisual != null) bowlFishVisual.SetActive(OccupantSoulIdentity >= 0);
        if (litVisual      != null) litVisual.SetActive(IsLit);
        if (litParticles   != null) litParticles.SetShowing(IsLit);
        if (lightCone      != null) lightCone.SetShowing(IsLit);
    }

#if UNITY_EDITOR
    [Header("Debug")]
    [Tooltip("Draw the exact point and radius this lamp registers with InstancedLightManager, i.e. " +
             "the value the InstancedLights custom function reads out of _InstLightPositions.")]
    [SerializeField] private bool showLightGizmo = true;

    // Colour code: grey = not lit (registered but InstLightActive is false, so it contributes
    // nothing), amber = in the buffer and lighting the level, red = lit but dropped because more
    // than InstancedLightManager.MAX lights were active.
    static readonly Color GizmoLit     = new Color(1f, 0.85f, 0.35f);
    static readonly Color GizmoUnlit   = new Color(0.45f, 0.45f, 0.45f, 0.6f);
    static readonly Color GizmoDropped = Color.red;

    Color LightGizmoColour()
    {
        if (!InstLightActive) return GizmoUnlit;
        bool dropped = InstancedLightManager.HasPushData && !InstancedLightManager.IsContributing(this);
        return dropped ? GizmoDropped : GizmoLit;
    }

    // Always-on marker: just the point, so a level full of lamps stays readable.
    void OnDrawGizmos()
    {
        if (!showLightGizmo) return;

        Vector3 anchor = LightAnchorTransform.position;
        Vector3 p      = InstLightPosition;

        Gizmos.color = LightGizmoColour();
        Gizmos.DrawSphere(p, 0.15f);

        // Show where the offset moved the point from, so the shader's light point can never silently
        // drift from the bulb mesh without it being visible in the scene.
        if (p != anchor)
        {
            Gizmos.DrawLine(anchor, p);
            Gizmos.DrawWireSphere(anchor, 0.07f);
        }
    }

    // Selected: the falloff shape the shader actually evaluates.
    void OnDrawGizmosSelected()
    {
        if (!showLightGizmo) return;

        Vector3 p = InstLightPosition;
        float   r = Mathf.Max(lightRadius, 0.0001f);
        Color   c = LightGizmoColour();

        // Outer sphere = radius, where the light reaches zero. Inner = radius * innerFalloff, the
        // full-brightness core that InstancedLights.hlsl smoothsteps out from.
        Gizmos.color = c;
        Gizmos.DrawWireSphere(p, r);

        Gizmos.color = new Color(c.r, c.g, c.b, 0.35f);
        Gizmos.DrawWireSphere(p, r * InstancedLightManager.CurrentInnerFalloff);

        // Where the screen-space halo is centred. No sphere for it — its radius is in viewport units,
        // not world units, so there is no world size to draw.
        Vector3 top = HaloGlowPosition;
        Gizmos.color = new Color(0.6f, 0.8f, 1f, 0.9f);
        if (HasOwnHaloPoint)
        {
            Gizmos.DrawSphere(top, 0.1f);
            Gizmos.DrawLine(p, top);
            UnityEditor.Handles.Label(top + Vector3.up * 0.2f, "cone top");
        }

        if (HasConeBaseAnchor)
        {
            Vector3 b = ConeBasePosition;
            Gizmos.DrawSphere(b, 0.1f);
            Gizmos.DrawLine(top, b);
            UnityEditor.Handles.Label(b + Vector3.up * 0.2f, "cone base (X/Z)");
        }

        string state = !InstLightActive                        ? "not lit — contributes nothing"
                     : InstancedLightManager.IsContributing(this) ? "contributing"
                     : InstancedLightManager.HasPushData        ? "LIT BUT DROPPED (over budget)"
                                                                : "lit — no push yet";
        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(p + Vector3.up * 0.35f, $"{name}\nradius {r:0.##}\n{state}");
    }
#endif
}
