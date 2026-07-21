using UnityEngine;

// Lives on the FishBowlTower prefab (typically a child of the root, alongside the bowl). The bowl
// (e.g. "FishBowlTop") is a real object with its own Rigidbody, kept KINEMATIC while the tower
// stands. The soul-fish shoal is parented under the bowl at spawn, so it is fixed to the bowl.
//
// When the tower's StatueDestruction fires (catapult smash), this controller un-kinematics the bowl
// Rigidbody so the whole bowl — glass + fish — falls. When the bowl reaches the water surface it is
// frozen and the shoal is released (fish become catchable). The tower carries StatueDestruction but
// no StatueBehaviour, so catchability opens on LANDING, not on the break.
//
// The bowl swim area is defined by the prefab: assign `bowlCenter` (fish spawn there) and `bowlRadius`.
public class FishBowlTowerController : MonoBehaviour
{
    [Header("Bowl")]
    [Tooltip("The centre of the fish bowl — fish spawn and swim around this point. Assign an empty at " +
             "the bowl's centre. No height number needed; the true world position is used.")]
    public Transform bowlCenter;

    [Tooltip("Swim radius around the bowl centre (world units). Fish stay within this — keep it just " +
             "inside the visible bowl so they don't clip through the glass.")]
    public float bowlRadius = 1f;

    [Header("Drop")]
    [Tooltip("Rigidbody of the bowl object (e.g. FishBowlTop). Kept kinematic while standing; released " +
             "to fall when the tower is smashed. The shoal is parented under this so they fall together.")]
    public Rigidbody bowlBody;

    [Tooltip("The tower's StatueDestruction. Assign it explicitly — it usually lives in a separate " +
             "subtree (INTACTROOT), which auto-find can't reach.")]
    public StatueDestruction destruction;

    [Header("Landing")]
    [Tooltip("Bowl glass meshes deleted the moment the bowl reaches the water (e.g. the two FishBowl " +
             "meshes). The fish remain and settle into the water.")]
    public GameObject[] bowlMeshes;

    [Tooltip("Duration of the settle, in seconds — how long the fish take to ease from the surface " +
             "down to their exact underwater swim depth after the bowl lands.")]
    public float settleDuration = 0.5f;

    // World-space centre of the bowl (falls back to this transform if bowlCenter is unassigned).
    public Vector3 BowlCenterWorld => bowlCenter != null ? bowlCenter.position : transform.position;

    // Swim radius in world units, scaled by the tower's overall scale so a scaled tower still fits.
    public float BowlWorldRadius => bowlRadius * transform.lossyScale.x;

    // The bowl object the shoal is parented under (its Rigidbody's transform, else bowlCenter/self).
    public Transform BowlRoot =>
        bowlBody != null ? bowlBody.transform : (bowlCenter != null ? bowlCenter : transform);

    private SoulShoalController _container;
    private float _fallbackWaterY;
    private bool  _dropping;
    private bool  _landed;

    // Resolved the same way the fish do, so the bowl lands exactly where the fish will swim.
    private Transform _waterTransform;
    private Material  _waterMat;

    void Awake()
    {
        // Prefer the explicit reference; fall back to a search for simple hierarchies.
        if (destruction == null)
        {
            destruction = GetComponentInParent<StatueDestruction>(true);
            if (destruction == null) destruction = GetComponentInChildren<StatueDestruction>(true);
        }

        if (destruction != null)
            destruction.OnTriggered += OnTowerSmashed;
        else
            Debug.LogWarning("[FishBowlTower] No StatueDestruction assigned/found — the bowl will never drop.", this);

        // Hold the bowl in place until it is smashed.
        if (bowlBody != null)
        {
            bowlBody.isKinematic = true;
            bowlBody.useGravity  = false;
        }
    }

    void OnDestroy()
    {
        if (destruction != null)
            destruction.OnTriggered -= OnTowerSmashed;
    }

    void Start()
    {
        // Resolve the real water surface (the wave mesh) — the same source the fish follow.
        var ldc = LevelDataController.Instance;
        if (ldc != null) _waterTransform = ldc.GetWaveTransform();
        if (_waterTransform != null)
        {
            var mr = _waterTransform.GetComponent<MeshRenderer>();
            _waterMat = mr != null ? mr.sharedMaterial : null;
        }
        if (_waterTransform == null || _waterMat == null)
            Debug.LogWarning("[FishBowlTower] Couldn't resolve the wave surface — falling back to the passed water Y (may be wrong).", this);
    }

    // Called by LevelSpawner once the shoal is parented under the bowl. waterY is only a fallback if
    // the wave surface can't be resolved at runtime.
    public void SetContainer(SoulShoalController container, float fallbackWaterY)
    {
        _container      = container;
        _fallbackWaterY = fallbackWaterY;
    }

    // World Y of the water surface below `atPos`, sampled from the live wave (falls back if needed).
    private float WaterSurfaceY(Vector3 atPos)
    {
        if (_waterTransform == null || _waterMat == null) return _fallbackWaterY;
        var p = WaveUtils.ReadParams(_waterTransform, _waterMat);
        return p.origin.y + WaveUtils.SampleHeight(atPos, p, 1f);
    }

    private void OnTowerSmashed(Vector3 hitPosition)
    {
        if (_dropping || _landed) return;
        _dropping = true;

        Debug.Log($"[FishBowlTower] Tower smashed — dropping bowl on '{name}'.", this);

        if (bowlBody != null)
        {
            // Drop straight down so the shoal stays readable and lands on its own footprint.
            bowlBody.constraints = RigidbodyConstraints.FreezeRotation
                                 | RigidbodyConstraints.FreezePositionX
                                 | RigidbodyConstraints.FreezePositionZ;
            bowlBody.interpolation = RigidbodyInterpolation.Interpolate; // smooth the visible fall
            bowlBody.isKinematic = false;
            bowlBody.useGravity  = true;
        }
        else
        {
            Debug.LogWarning("[FishBowlTower] No bowlBody assigned — bowl can't fall.", this);
        }
    }

    void Update()
    {
        if (!_dropping || _landed || bowlBody == null) return;

        // Measure the actual bowl centre (rides the falling bowl) against the live water surface.
        Vector3 probe = ProbePos();
        if (probe.y <= WaterSurfaceY(probe))
            Land();
    }

    // The point we compare against the water — the bowl centre if assigned, else the Rigidbody pivot.
    private Vector3 ProbePos() => bowlCenter != null ? bowlCenter.position : bowlBody.transform.position;

    private void Land()
    {
        _landed = true;

        // Freeze the bowl with its CENTRE exactly on the surface (offset the pivot by the same delta).
        bowlBody.isKinematic = true;
        bowlBody.useGravity  = false;
        Vector3 probe = ProbePos();
        float delta = WaterSurfaceY(probe) - probe.y;
        bowlBody.transform.position += Vector3.up * delta;

        // Delete the glass meshes — the fish remain and settle into the water.
        if (bowlMeshes != null)
            foreach (var m in bowlMeshes)
                if (m != null) Destroy(m);

        Debug.Log($"[FishBowlTower] Bowl landed at Y={ProbePos().y:0.##} on '{name}' — dissolving glass, settling shoal.", this);

        // The shoal eases each fish from the surface down to its exact wave depth, then opens catching.
        if (_container != null) _container.BeginSettle(settleDuration);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Vector3 centre = BowlCenterWorld;
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireSphere(centre, BowlWorldRadius);
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.25f);
        Gizmos.DrawLine(transform.position, centre);
    }
#endif
}
