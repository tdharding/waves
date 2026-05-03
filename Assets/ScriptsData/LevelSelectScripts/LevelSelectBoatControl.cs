using UnityEngine;
using UnityEngine.Splines;

public class LevelSelectBoatControl : MonoBehaviour
{
    [SerializeField] private float defaultSplineProgress = 0f;
    [SerializeField] private string defaultSegmentID = "SplineRiverPart1";

    [Header("References")]
    [SerializeField] private SplineAnimate _splineAnimate;
    [SerializeField] private Collider _boatCollider;
    [SerializeField] private Transform _boatTransform;

    [Header("Visual")]
    [SerializeField] private Transform _meshTransform;
    [SerializeField] private float meshFlipSpeed = 5f;

    [Header("Movement")]
    [SerializeField] private float baseSpeed = 2f;
    [SerializeField] private float boostMultiplier = 2.5f;

    [Header("Debug")]
    [SerializeField] private bool debugMovement = false;

    // ── State ──────────────────────────────────────────────────────
    private float   _progress;
    private float   _speed;
    private bool    _blocked;
    private bool    _isReversed;
    private bool    _isLeftPath;
    private bool    _isRightPath;
    private bool    _wantsLeft;
    private bool    _wantsRight;

    private Quaternion _meshTargetRotation = Quaternion.identity;

    // ── Public API ─────────────────────────────────────────────────
    public bool ControlsFrozen   { get; set; }
    public bool IsReversed       => _isReversed;
    public bool IsLeftPath       => _isLeftPath;
    public bool IsRightPath      => _isRightPath;
    public float CurrentProgress => _progress;

    // Accounts for reversal: when going backwards, left/right flip
    public bool WantsLeft  => _isReversed ? _wantsRight : _wantsLeft;
    public bool WantsRight => _isReversed ? _wantsLeft  : _wantsRight;

    // Raw key state — LEFT key = left, RIGHT key = right, no reversal mapping
    public bool RawWantsLeft  => _wantsLeft;
    public bool RawWantsRight => _wantsRight;

    public Transform BoatTransform => _boatTransform;
    public Transform MeshTransform => _meshTransform;
    public SplineContainer GetCurrentContainer() => _splineAnimate.Container;

    public string CurrentSegmentID
    {
        get
        {
            var id = _splineAnimate.Container?.GetComponent<RiverSegmentID>();
            return id != null ? id.SegmentID : string.Empty;
        }
    }

    // ── Lifecycle ──────────────────────────────────────────────────
    private void Start()
    {
        if (_splineAnimate == null) { Debug.LogError("No SplineAnimate assigned!", this); return; }
        if (_boatCollider == null)  { Debug.LogError("No Collider assigned!", this); return; }
        if (_boatTransform == null) { Debug.LogError("No BoatTransform assigned!", this); return; }

        _splineAnimate.Pause();

        string savedID = GameProgressData.GetBoatSegmentID();
        if (!string.IsNullOrEmpty(savedID))
        {
            var seg = RiverSegmentRegistry.Instance?.GetSegment(savedID);
            if (seg != null)
            {
                AttachContainer(seg.GetComponent<SplineContainer>());
                _isLeftPath  = seg.IsLeftPath;
                _isRightPath = seg.IsRightPath;
            }
        }
        else
        {
            var seg = RiverSegmentRegistry.Instance?.GetSegment(defaultSegmentID);
            if (seg != null)
                AttachContainer(seg.GetComponent<SplineContainer>());
        }

        _progress = GameProgressData.GetBoatProgress(defaultSplineProgress);
        _splineAnimate.NormalizedTime = _progress;
    }

    private void Update()
    {
        // Reset per-frame signals
        _wantsLeft  = false;
        _wantsRight = false;

        // Direction toggle — both Up and Down flip direction
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow))
        {
            _isReversed = !_isReversed;
            _meshTargetRotation = _isReversed ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
        }

        // Path selection signals (junction node reads these)
        if (Input.GetKeyDown(KeyCode.LeftArrow))  _wantsLeft  = true;
        if (Input.GetKeyDown(KeyCode.RightArrow)) _wantsRight = true;

        // Mesh rotation smoothing
        if (_meshTransform != null)
            _meshTransform.localRotation = Quaternion.Lerp(
                _meshTransform.localRotation, _meshTargetRotation, Time.deltaTime * meshFlipSpeed);

  
        // Obstacle check
        _blocked = false;
        Collider[] hits = Physics.OverlapBox(
            _boatCollider.bounds.center,
            _boatCollider.bounds.extents,
            transform.rotation);
        foreach (var hit in hits)
            if (hit.CompareTag("LevelSelectPathObstacle")) { _blocked = true; break; }

        // Auto-advance
        float dir = _isReversed ? -1f : 1f;
        if (_blocked && dir > 0f)
        {
            if (debugMovement) Debug.Log("[Boat] Blocked by obstacle");
            return;
        }

        if (debugMovement && _speed < 0.0001f)
            Debug.Log($"[Boat] Speed is near zero — container={_splineAnimate.Container?.name} speed={_speed}");

        bool boosting = Input.GetKey(KeyCode.Space);
        float frameSpeed = boosting ? _speed * boostMultiplier : _speed;
        _progress = Mathf.Clamp01(_progress + dir * frameSpeed * Time.deltaTime);
        _splineAnimate.NormalizedTime = _progress;
    }

    // ── Junction interface ─────────────────────────────────────────
    public void HandOffToJunction()
    {
        ControlsFrozen = true;
        _splineAnimate.Pause();
    }

    public void ResumeMovement()
    {
        ControlsFrozen = false;
        _blocked       = false;
    }

    public void AttachToSegment(SplineContainer newSegment, float startT = 0f,
                                bool isLeftPath = false, bool isRightPath = false)
    {
        AttachContainer(newSegment);
        _progress      = startT;
        ControlsFrozen = false;
        _isLeftPath    = isLeftPath;
        _isRightPath   = isRightPath;

        // Play then immediately pause to force SplineAnimate to re-evaluate
        // position with the new container — prevents stuck-after-transition.
        _splineAnimate.Play();
        _splineAnimate.NormalizedTime = _progress;

        // Entry at t=1 means travelling toward t=0, so mark as reversed.
        _isReversed = startT > 0.5f;
        _meshTargetRotation = _isReversed ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
        SnapMeshRotation(_meshTargetRotation);

        var segID = newSegment.GetComponent<RiverSegmentID>();
        if (segID != null)
            GameProgressData.SaveBoatState(segID.SegmentID, startT, isLeftPath, isRightPath);
    }

    public void WireBoatReferences(SplineAnimate splineAnimate, Collider boatCollider, Transform boatTransform, Transform meshTransform = null)
    {
        _splineAnimate = splineAnimate;
        _boatCollider  = boatCollider;
        _boatTransform = boatTransform;
        if (meshTransform != null) _meshTransform = meshTransform;
    }

    public void RestoreToSegment(SplineContainer segment, float progress)
    {
        AttachContainer(segment);
        _progress = progress;
        _splineAnimate.NormalizedTime = _progress;
    }

    public void SnapMeshRotation(Quaternion rotation)
    {
        _meshTargetRotation = rotation;
        if (_meshTransform != null)
            _meshTransform.localRotation = rotation;
    }

    // ── Helpers ────────────────────────────────────────────────────
    private void AttachContainer(SplineContainer container)
    {
        if (container == null) return;
        _splineAnimate.Container = container;
        float worldLength = container.Spline.GetLength();
        _speed = worldLength > 0f ? baseSpeed / worldLength : baseSpeed;
    }

    private void OnDestroy()
    {
        if (_splineAnimate == null || _splineAnimate.Container == null) return;
        var segID = _splineAnimate.Container.GetComponent<RiverSegmentID>();
        string id = segID != null ? segID.SegmentID : string.Empty;
        GameProgressData.SaveBoatState(id, _progress, _isLeftPath, _isRightPath);
    }
}
