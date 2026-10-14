using UnityEngine;

/// <summary>How a level select world lays its fog sheets out.</summary>
public enum FogPlaneMode
{
    Single,   // one sheet over the whole landscape — the original, and the default
    Zones,    // a sheet per authored fog zone, each at its own height
}

/// <summary>
/// One level select fog zone: a square fog sheet at its own height, shown while the boat is inside
/// the zone's bounds and faded out and hidden when it leaves. Built by the Level Select Designer's
/// Fog Zones mode — the numbers here are written by it, not authored on the component.
///
/// THE FOG IS NOT SIMULATED PER ZONE. FogFieldManager paints one field, and the fog sheet material
/// looks it up by world X and Z only, so every sheet at every height shows the same fog. A zone
/// costs one flat mesh draw while shown and nothing while hidden.
///
/// What is per zone is pushed to the sheet through a MaterialPropertyBlock as _FogZone
/// (FogZoneFade.hlsl): the edge fade width, the sheet's size, and how far it is faded out. A sheet
/// with no block — the single landscape sheet, every interior level's sheet — reads zero there,
/// which is no effect.
///
/// The bounds are a trigger BoxCollider on the Ignore Raycast layer, so the boat's water raycasts
/// pass straight through it. The boat is tested against the box in X and Z only: the boat sits
/// down in a river channel and the sheet hovers over the landscape, so height is not what decides
/// whether you are in the zone.
/// </summary>
[ExecuteAlways]
public class FogZone : MonoBehaviour
{
    [Tooltip("The fog sheet this zone shows and hides.")]
    [SerializeField] MeshRenderer sheet;

    [Tooltip("The zone's bounds. The boat inside it in X and Z shows the sheet.")]
    [SerializeField] BoxCollider bounds;

    [Tooltip("The sheet's width in world units — turns its UV into a distance for the edge fade.")]
    [SerializeField] float sheetSize = 10f;

    [Tooltip("How far in from the sheet's edge the fog fades to nothing, in world units.")]
    [SerializeField] float edgeFade = 1f;

    [Tooltip("Seconds to fade in on entering the bounds and out on leaving.")]
    [SerializeField] float fadeTime = 1f;

    static readonly int FogZoneId = Shader.PropertyToID("_FogZone");

    // Found once by name, the way LevelSelectDataController finds it, and re-found only once it
    // has gone — levels load and unload around the map.
    const string BoatName = "LevelSelectBoat";
    static Transform s_boat;
    static float     s_boatSearchClock;

    MaterialPropertyBlock _block;
    float _shown = -1f;      // 0 = faded out and hidden, 1 = fully shown; -1 = not decided yet
    Vector4 _pushed = new Vector4(float.NaN, 0f, 0f, 0f);

    public void Configure(MeshRenderer sheetRenderer, BoxCollider zoneBounds,
                          float size, float fade, float seconds)
    {
        sheet     = sheetRenderer;
        bounds    = zoneBounds;
        sheetSize = size;
        edgeFade  = fade;
        fadeTime  = seconds;
        _pushed   = new Vector4(float.NaN, 0f, 0f, 0f);
    }

    void OnEnable()
    {
        _shown  = -1f;
        _pushed = new Vector4(float.NaN, 0f, 0f, 0f);
    }

    void Update()
    {
        if (sheet == null) return;

        float target;
        if (!Application.isPlaying)
        {
            // Editing: always shown, so the zone can be seen while it is being placed.
            target = 1f;
            _shown = 1f;
        }
        else
        {
            target = BoatInside() ? 1f : 0f;

            // Whatever state the boat starts the scene in is taken as-is, not faded into.
            if (_shown < 0f) _shown = target;
            else if (fadeTime <= 0f) _shown = target;
            else _shown = Mathf.MoveTowards(_shown, target, Time.deltaTime / fadeTime);
        }

        bool visible = _shown > 0f;
        if (sheet.enabled != visible) sheet.enabled = visible;
        if (!visible) return;

        Push(new Vector4(Mathf.Max(edgeFade, 0f), Mathf.Max(sheetSize, 0.01f), 1f - _shown, 0f));
    }

    void Push(Vector4 zone)
    {
        if (zone == _pushed) return;
        _pushed = zone;

        if (_block == null) _block = new MaterialPropertyBlock();
        sheet.GetPropertyBlock(_block);
        _block.SetVector(FogZoneId, zone);
        sheet.SetPropertyBlock(_block);
    }

    bool BoatInside()
    {
        if (bounds == null) return false;

        var boat = ResolveBoat();
        if (boat == null) return false;

        // In the box's own frame, X and Z only.
        Vector3 local = bounds.transform.InverseTransformPoint(boat.position) - bounds.center;
        Vector3 half  = bounds.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
    }

    static Transform ResolveBoat()
    {
        if (s_boat != null) return s_boat;

        // A missing boat is searched for once a second, not every frame by every zone.
        if (Time.unscaledTime < s_boatSearchClock) return null;
        s_boatSearchClock = Time.unscaledTime + 1f;

        var go = GameObject.Find(BoatName);
        s_boat = go != null ? go.transform : null;
        return s_boat;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_boat            = null;
        s_boatSearchClock = 0f;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (bounds == null) return;
        Gizmos.color  = new Color(0.6f, 0.8f, 1f, 0.6f);
        Gizmos.matrix = bounds.transform.localToWorldMatrix;
        Gizmos.DrawWireCube(bounds.center, bounds.size);
    }
#endif
}
