using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the catapult tool on the boat.
/// Loading:  drag a soul icon onto the 3D SoulSlot (SoulFishLoadingPoint, Interaction layer).
/// Firing:   press Space to fire.
/// Distance: hold Up / Down to slide the loading point out along the fire direction,
///           which extends the trajectory and raises the arc (see AdjustLaunchDistance).
///
/// [ExecuteAlways] so the loading-point offset, the arc gizmo, and the generated arm mesh
/// all update live in the Scene view while designing — no need to press Play. In Edit mode the
/// real launchPoint transform is never moved (its serialised position is treated as the retracted
/// rest); the offset is only applied to the transform at runtime.
/// </summary>
[ExecuteAlways]
public class CatapultController : MonoBehaviour
{
    [Header("Refs")]
    public SoulSlot soulSlot;
    public Transform armTransform;
    public Transform launchPoint;
    public GameObject catapultProjectilePrefab;

    [Header("Arm Rotation")]
    [Tooltip("Local X degrees the arm swings through on fire.")]
    public float armFireRotation = 120f;
    [Tooltip("Duration of the forward swing.")]
    public float swingDuration = 0.2f;
    [Tooltip("Duration of the arm returning to rest.")]
    public float returnDuration = 0.4f;

    [Header("Fire Direction")]
    [Tooltip("Local-space direction the soul is launched. Adjust until the Scene gizmo arrow points where you want.")]
    public Vector3 fireDirectionLocal = Vector3.forward;

    [Header("Throw")]
    public float throwDistance = 12f;

    [Header("Distance Adjustment (hold Up / Down)")]
    [Tooltip("Design default: how far (world units, along the fire direction) the loading point sits out from its retracted rest position. Updates the gizmo and generated arm live in the Scene view.")]
    public float launchOffsetDefault = 0f;
    [Tooltip("Maximum outward offset of the loading point at full power.")]
    public float maxLaunchOffset = 0.6f;
    [Tooltip("Units per second the loading point moves while Up/Down is held.")]
    public float launchAdjustSpeed = 0.5f;
    [Tooltip("Extra throw distance added at full power, on top of the moved-out loading point.")]
    public float extraThrowDistanceAtMax = 0.5f;
    [Tooltip("Extra arc peak height added at full power (a longer throw arcs higher).")]
    public float extraArcPeakAtMax = 0.2f;
    [Tooltip("If true, the distance resets to the design default after each shot; otherwise it persists.")]
    public bool resetAfterFire = false;

    [Header("Generated Arm")]
    [Tooltip("Fixed 'centre' end of the arm. If left empty, the catapult root transform is used.")]
    public Transform armAnchor;
    [Tooltip("Thickness (world units) of the generated cube arm.")]
    public float armThickness = 0.03f;
    [Tooltip("Gap along the arm axis between the arm's end and the loading point. Positive extends the arm past the loading point; negative stops it short.")]
    public float armEndOffset = 0f;
    [Tooltip("Optional material for the generated arm. Falls back to the default cube material if empty.")]
    public Material armMaterial;

    [Header("Projectile Arc")]
    public float arcDuration = 1.0f;
    public float arcPeakHeight = 5f;

    [Header("Projectile Collision")]
    public float obstacleCheckRadius = 0.45f;
    [Tooltip("Blast radius that shatters DestructibleWall pieces on impact. Make it larger than obstacleCheckRadius so one hit clears a gap through a densely-tiled spline wall.")]
    public float wallDestructionRadius = 2.5f;
    public string[] collidableTags = { "MazeWalls", "LowHMazeWall", "TallHMazeWall", "LowSpikeTrap", "BadGuySnake" };

    [Header("Debug")]
    public bool showDebugGizmos = true;

    [Header("Projectile Wave Sampling")]
    public float extraYOffset = 0.01f;
    public float heightMultiplier = 1f;

    [Header("Projectile Prefabs")]
    public GameObject droppedSoulPrefab;
    public GameObject explosionVFXPrefab;
    public GameObject splashPrefab;

    private const string ArmObjectName = "GeneratedCatapultArm";

    private int _loadedSoulIdentity = -1;
    private int _loadedLinkID = -1;
    private bool _firing;
    public bool IsFiring => _firing;
    private Vector3 _armRestEulers;
    private float _swingAngle;   // current fire-swing angle applied to the generated arm

    // Retracted (offset 0) local position of the loading point. Auto-captured once and kept
    // authoritative so driving the transform for the offset never corrupts the true rest.
    [SerializeField, HideInInspector] private Vector3 _launchRestLocalPos;
    [SerializeField, HideInInspector] private bool _launchRestCaptured;
    private float _currentOffset;
    private Transform _armObject;

    private void Awake()
    {
        if (armTransform != null)
            _armRestEulers = armTransform.localEulerAngles;

        if (!Application.isPlaying)
            return;

        EnsureRestCaptured();
        _currentOffset = Mathf.Clamp(launchOffsetDefault, 0f, maxLaunchOffset);

        if (soulSlot != null)
        {
            soulSlot.onFilled.AddListener(OnSoulLoaded);
            soulSlot.onEmptied.AddListener(OnSoulEjected);
        }

        ApplyLoadingPointPosition();
    }

    private void OnDestroy()
    {
        if (!Application.isPlaying || soulSlot == null)
            return;

        soulSlot.onFilled.RemoveListener(OnSoulLoaded);
        soulSlot.onEmptied.RemoveListener(OnSoulEjected);
    }

    private void Update()
    {
        // Slide the loading point (and its child visual) to the arm end for the current offset,
        // then stretch the generated arm to follow — live in Edit mode and at runtime.
        ApplyLoadingPointPosition();
        UpdateGeneratedArm();
    }

    private void OnValidate()
    {
        if (maxLaunchOffset < 0f) maxLaunchOffset = 0f;
        launchOffsetDefault = Mathf.Clamp(launchOffsetDefault, 0f, maxLaunchOffset);
        if (armThickness < 0f) armThickness = 0f;
    }

    // ─────────────────────────────────────────────
    // SOUL LOADING
    // ─────────────────────────────────────────────

    private void OnSoulLoaded(int soulIdentity)
    {
        _loadedSoulIdentity = soulIdentity;
        _loadedLinkID = -1;

        if (LevelSoulTracker.Instance != null)
        {
            _loadedLinkID = LevelSoulTracker.Instance.GetLinkIDForIdentity(soulIdentity);
            LevelSoulTracker.Instance.RemoveTemporarySoul(soulIdentity);
        }
    }

    private void OnSoulEjected(int soulIdentity)
    {
        // When the soul is returned to the boat (not fired), restore the tracker state.
        // The slot's own RemoveSoul already handles GameProgressData and the display icon.
        if (!_firing && soulIdentity >= 0 && LevelSoulTracker.Instance != null)
            LevelSoulTracker.Instance.ReinsertSoul(_loadedLinkID, soulIdentity);
    }

    // ─────────────────────────────────────────────
    // DISTANCE ADJUSTMENT (called from BoatControlRouter while Catapult tool is active)
    // ─────────────────────────────────────────────

    /// <summary>
    /// axis > 0 (Up) slides the loading point further from the boat; axis &lt; 0 (Down) pulls it back.
    /// The offset is clamped to [0, maxLaunchOffset]. Blocked while firing.
    /// </summary>
    public void AdjustLaunchDistance(float axis)
    {
        if (!Application.isPlaying || _firing || Mathf.Approximately(axis, 0f))
            return;

        _currentOffset = Mathf.Clamp(_currentOffset + axis * launchAdjustSpeed * Time.deltaTime, 0f, maxLaunchOffset);
        ApplyLoadingPointPosition();
    }

    // Auto-capture the retracted rest the first time we have a valid loading point.
    private void EnsureRestCaptured()
    {
        if (_launchRestCaptured || launchPoint == null) return;
        _launchRestLocalPos = launchPoint.localPosition;
        _launchRestCaptured = true;
    }

    /// <summary>
    /// If you move the loading point to a new resting spot, set the offset to 0 and invoke this
    /// (right-click the component) to record the new retracted rest.
    /// </summary>
    [ContextMenu("Recapture Loading Point Rest (set offset to 0 first)")]
    private void RecaptureLoadingPointRest()
    {
        if (launchPoint == null) return;
        _launchRestLocalPos = launchPoint.localPosition;
        _launchRestCaptured = true;
    }

    // Drives the loading point out from its rest toward the arm end (rest → away from the arm
    // anchor), and — while firing — swings it about the anchor with the arm so the soul rides
    // the arm tip. Applied in both Edit and Play mode (swing only occurs at runtime).
    private void ApplyLoadingPointPosition()
    {
        if (launchPoint == null) return;
        EnsureRestCaptured();

        float     offset = DisplayOffset();
        Transform parent = launchPoint.parent;
        Vector3   rest   = _launchRestLocalPos;
        Vector3   anchor = ArmAnchorPos();

        // Base (unswung) extension direction, rest → away from the anchor, in local space.
        Vector3 dirLocal;
        if (parent != null)
        {
            Vector3 anchorLocal = parent.InverseTransformPoint(anchor);
            dirLocal = rest - anchorLocal;
            if (dirLocal.sqrMagnitude < 1e-8f)
                dirLocal = parent.InverseTransformDirection(WorldFireDir());
        }
        else
        {
            dirLocal = rest - anchor;
            if (dirLocal.sqrMagnitude < 1e-8f)
                dirLocal = WorldFireDir();
        }

        Vector3 baseLocal = rest + dirLocal.normalized * offset;

        if (Mathf.Abs(_swingAngle) > 0.001f)
        {
            // Swing the loading point about the anchor in the vertical plane, matching the arm.
            Vector3 baseWorld = parent != null ? parent.TransformPoint(baseLocal) : baseLocal;
            Vector3 armDir    = baseWorld - anchor;
            Vector3 target    = baseWorld;
            if (armDir.sqrMagnitude > 1e-8f)
            {
                Vector3 swingAxis = Vector3.Cross(armDir.normalized, Vector3.up);
                if (swingAxis.sqrMagnitude < 1e-6f) swingAxis = Vector3.right;
                Quaternion swing = Quaternion.AngleAxis(_swingAngle, swingAxis.normalized);
                target = anchor + swing * (baseWorld - anchor);
            }
            launchPoint.position = target;
        }
        else if ((launchPoint.localPosition - baseLocal).sqrMagnitude > 1e-10f)
        {
            launchPoint.localPosition = baseLocal;
        }
    }

    // ─────────────────────────────────────────────
    // FIRE (called from BoatControlRouter on Space down)
    // ─────────────────────────────────────────────

    public void Fire()
    {
        if (_firing || soulSlot == null) return;

        // Empty slot + Space → auto-load a soul from the boat, bypassing point-and-click.
        // A second Space (now loaded) fires, leaving room to aim distance with Up/Down between.
        if (!soulSlot.IsFilled)
        {
            TryAutoLoad();
            return;
        }

        _firing = true;
        soulSlot.SetAllowRemoval(false);
        soulSlot.SetInteractable(false);
        StartCoroutine(FireRoutine());
    }

    /// <summary>
    /// Loads the first available soul from the boat inventory into the slot, doing the same
    /// bookkeeping as a successful drag-and-drop (TryInsertSoul → onFilled → OnSoulLoaded,
    /// then remove from save data and the on-boat display).
    /// </summary>
    private void TryAutoLoad()
    {
        if (soulSlot == null || soulSlot.IsFilled) return;

        // Prefer the live runtime list — it includes souls caught THIS session, which are not
        // persisted to GameProgressData until a soul is removed. Fall back to save data.
        int identity = -1;
        if (LevelSoulTracker.Instance != null)
        {
            List<int> live = LevelSoulTracker.Instance.GetAllCaughtIdentities();
            if (live != null && live.Count > 0)
                identity = live[live.Count - 1];
        }
        if (identity < 0)
        {
            List<int> saved = GameProgressData.GetSoulsOnBoatIdentities();
            if (saved != null && saved.Count > 0)
                identity = saved[0];
        }

        if (identity < 0)
        {
            Debug.Log("[CatapultController] Auto-load skipped — no souls on the boat.");
            return;
        }

        if (!soulSlot.TryInsertSoul(identity))
        {
            Debug.LogWarning($"[CatapultController] Auto-load: TryInsertSoul({identity}) failed (slot not interactable or already filled?).");
            return;
        }

        // Mirror the drag-drop bookkeeping. For session souls, OnSoulLoaded → RemoveTemporarySoul
        // already synced these (so the calls below are harmless no-ops); for previous-session souls
        // this performs the real removal.
        GameProgressData.RemoveSoulFromBoat(identity);
        SoulsOnBoatDisplayManager.Instance?.ConsumeSoulFromDisplay(identity);

        Debug.Log($"[CatapultController] Auto-loaded soul #{identity} onto the catapult.");
    }

    public void CancelArm()
    {
        // No-op now — kept so BoatControlRouter compiles without changes
    }

    // ─────────────────────────────────────────────
    // FIRE COROUTINE
    // ─────────────────────────────────────────────

    private IEnumerator FireRoutine()
    {
        bool launched = false;

        for (float t = 0f; t < 1f; t += Time.deltaTime / swingDuration)
        {
            _swingAngle = t * armFireRotation;

            if (armTransform != null)
                armTransform.localEulerAngles = _armRestEulers + new Vector3(t * armFireRotation, 0f, 0f);

            if (!launched && t > 0.35f)
            {
                LaunchSoul();
                launched = true;
            }

            yield return null;
        }

        _swingAngle = armFireRotation;

        if (armTransform != null)
            armTransform.localEulerAngles = _armRestEulers + new Vector3(armFireRotation, 0f, 0f);

        if (!launched)
            LaunchSoul();

        yield return StartCoroutine(ReturnArmRoutine());

        _firing = false;
        soulSlot.SetAllowRemoval(true);
        soulSlot.SetInteractable(true);

        if (resetAfterFire)
        {
            _currentOffset = Mathf.Clamp(launchOffsetDefault, 0f, maxLaunchOffset);
            ApplyLoadingPointPosition();
        }
    }

    private IEnumerator ReturnArmRoutine()
    {
        float startAngle = armFireRotation;
        for (float t = 0f; t < 1f; t += Time.deltaTime / returnDuration)
        {
            _swingAngle = Mathf.Lerp(startAngle, 0f, t);

            if (armTransform != null)
                armTransform.localEulerAngles = _armRestEulers + new Vector3(Mathf.Lerp(startAngle, 0f, t), 0f, 0f);
            yield return null;
        }
        ResetArmRotation();
    }

    private void ResetArmRotation()
    {
        _swingAngle = 0f;
        if (armTransform != null)
            armTransform.localEulerAngles = _armRestEulers;
    }

    // ─────────────────────────────────────────────
    // LAUNCH
    // ─────────────────────────────────────────────

    private void LaunchSoul()
    {
        Vector3 fireDirection = WorldFireDir();
        Vector3 startPos      = ExtendedLaunchPos();

        if (catapultProjectilePrefab != null)
        {
            GameObject proj       = Object.Instantiate(catapultProjectilePrefab, startPos, Quaternion.identity);
            var        projectile = proj.GetComponent<CatapultProjectile>();
            if (projectile != null)
            {
                projectile.arcDuration          = arcDuration;
                projectile.arcPeakHeight        = EffectiveArcPeak();
                projectile.obstacleCheckRadius  = obstacleCheckRadius;
                projectile.wallDestructionRadius = wallDestructionRadius;
                projectile.collidableTags       = collidableTags;
                projectile.extraYOffset         = extraYOffset;
                projectile.heightMultiplier     = heightMultiplier;
                projectile.droppedSoulPrefab    = droppedSoulPrefab;
                projectile.explosionVFXPrefab   = explosionVFXPrefab;
                projectile.splashPrefab         = splashPrefab;
                projectile.Launch(startPos, fireDirection, EffectiveThrowDistance(), _loadedSoulIdentity, _loadedLinkID);
            }
        }

        soulSlot.EjectSoul();

        _loadedSoulIdentity = -1;
        _loadedLinkID = -1;
    }

    // ─────────────────────────────────────────────
    // DISTANCE / ARC HELPERS
    // ─────────────────────────────────────────────

    private Vector3 WorldFireDir() => transform.TransformDirection(fireDirectionLocal.normalized);

    // Edit mode previews the design default; Play mode uses the live, key-adjusted offset.
    private float DisplayOffset() => Application.isPlaying ? _currentOffset : Mathf.Clamp(launchOffsetDefault, 0f, maxLaunchOffset);

    // The loading point transform is driven to the arm end in both Edit and Play mode,
    // so its current world position is the true launch origin.
    private Vector3 ExtendedLaunchPos()
    {
        return launchPoint != null ? launchPoint.position : transform.position;
    }

    private float NormOffset() => maxLaunchOffset > 0f ? Mathf.Clamp01(DisplayOffset() / maxLaunchOffset) : 0f;

    public float EffectiveThrowDistance() => throwDistance + NormOffset() * extraThrowDistanceAtMax;

    public float EffectiveArcPeak() => arcPeakHeight + NormOffset() * extraArcPeakAtMax;

    private Vector3 ArmAnchorPos() => armAnchor != null ? armAnchor.position : transform.position;

    // ─────────────────────────────────────────────
    // GENERATED ARM (cube mesh stretched anchor → loading point)
    // ─────────────────────────────────────────────

    private void UpdateGeneratedArm()
    {
        if (launchPoint == null || !EnsureArm())
            return;

        Vector3 a       = ArmAnchorPos();
        Vector3 b       = ExtendedLaunchPos();
        Vector3 axis    = b - a;
        float   axisLen = axis.magnitude;
        Vector3 dir     = axisLen > 0.0001f ? axis / axisLen : WorldFireDir();

        // Offset the arm's end relative to the loading point along the arm axis.
        Vector3 end   = b + dir * armEndOffset;
        Vector3 delta = end - a;
        float   len   = delta.magnitude;

        // The arm follows the loading point, which is itself swung about the anchor while firing
        // (see ApplyLoadingPointPosition), so no separate swing is applied here.
        _armObject.position = (a + end) * 0.5f;
        if (len > 0.0001f)
            _armObject.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);

        Vector3 parentScale = _armObject.parent != null ? _armObject.parent.lossyScale : Vector3.one;
        _armObject.localScale = new Vector3(
            SafeDiv(armThickness, parentScale.x),
            SafeDiv(armThickness, parentScale.y),
            SafeDiv(len,          parentScale.z));
    }

    private bool EnsureArm()
    {
        if (_armObject != null)
            return true;

        // Reconnect to an existing generated arm (e.g. after a domain reload) before creating a new one.
        Transform existing = transform.Find(ArmObjectName);
        if (existing != null)
        {
            _armObject = existing;
            return true;
        }

        // Grab the built-in cube mesh + default material from a throwaway primitive.
        GameObject temp       = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh       cubeMesh   = temp.GetComponent<MeshFilter>().sharedMesh;
        Material   defaultMat = temp.GetComponent<MeshRenderer>().sharedMaterial;
        SafeDestroy(temp);

        GameObject arm = new GameObject(ArmObjectName);
        arm.hideFlags  = HideFlags.DontSave;   // never serialised into the scene/prefab
        arm.transform.SetParent(transform, false);
        arm.AddComponent<MeshFilter>().sharedMesh       = cubeMesh;
        arm.AddComponent<MeshRenderer>().sharedMaterial = armMaterial != null ? armMaterial : defaultMat;

        _armObject = arm.transform;
        return true;
    }

    private static float SafeDiv(float a, float b) => Mathf.Approximately(b, 0f) ? a : a / b;

    private static void SafeDestroy(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    // ─────────────────────────────────────────────
    // GIZMO
    // ─────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        Vector3 origin        = ExtendedLaunchPos();
        Vector3 worldDir      = WorldFireDir();
        Vector3 right         = Vector3.Cross(worldDir, Vector3.up).normalized;
        float   effThrow      = EffectiveThrowDistance();
        float   effArcPeak    = EffectiveArcPeak();
        Vector3 landing       = origin + worldDir * effThrow;

        // Arm preview line (anchor → loading point)
        Gizmos.color = new Color(0.5f, 0.7f, 1f, 1f);
        Gizmos.DrawLine(ArmAnchorPos(), origin);

        // Arc preview
        int segments = 32;
        Vector3 prev = origin;
        for (int i = 1; i <= segments; i++)
        {
            float n    = (float)i / segments;
            Vector3 flat = Vector3.Lerp(origin, landing, n);
            float arcY   = Mathf.Sin(n * Mathf.PI) * effArcPeak;
            Vector3 next = new Vector3(flat.x, flat.y + arcY, flat.z);
            Gizmos.color = Color.Lerp(Color.green, Color.yellow, n);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        // Peak marker
        Vector3 midFlat = Vector3.Lerp(origin, landing, 0.5f);
        Vector3 peak    = new Vector3(midFlat.x, origin.y + effArcPeak, midFlat.z);
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(peak, 0.15f);

        // Landing marker + arrowhead
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(landing, 0.2f);
        float headSize = effThrow * 0.06f;
        Gizmos.DrawLine(landing, landing - worldDir * headSize + right * headSize * 0.5f);
        Gizmos.DrawLine(landing, landing - worldDir * headSize - right * headSize * 0.5f);

        UnityEditor.Handles.color = Color.cyan;
        UnityEditor.Handles.Label(peak + Vector3.up * 0.2f, $"Peak +{effArcPeak:0.##}m");
        UnityEditor.Handles.color = Color.yellow;
        UnityEditor.Handles.Label(landing + Vector3.up * 0.2f, $"{effThrow:0.##}m  (offset {DisplayOffset():0.##})");

        // Obstacle check radius — shown at launch, peak, and landing
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.25f);
        Gizmos.DrawSphere(origin,  obstacleCheckRadius);
        Gizmos.DrawSphere(peak,    obstacleCheckRadius);
        Gizmos.DrawSphere(landing, obstacleCheckRadius);
        Gizmos.color = new Color(1f, 0.4f, 0f, 1f);
        Gizmos.DrawWireSphere(origin,  obstacleCheckRadius);
        Gizmos.DrawWireSphere(peak,    obstacleCheckRadius);
        Gizmos.DrawWireSphere(landing, obstacleCheckRadius);
        UnityEditor.Handles.color = new Color(1f, 0.4f, 0f, 1f);
        UnityEditor.Handles.Label(landing + Vector3.up * (obstacleCheckRadius + 0.25f), $"r={obstacleCheckRadius}");

        // Wall-destruction blast radius — shown at the landing point
        Gizmos.color = new Color(0.8f, 0.2f, 1f, 0.12f);
        Gizmos.DrawSphere(landing, wallDestructionRadius);
        Gizmos.color = new Color(0.8f, 0.2f, 1f, 1f);
        Gizmos.DrawWireSphere(landing, wallDestructionRadius);
        UnityEditor.Handles.color = new Color(0.8f, 0.2f, 1f, 1f);
        UnityEditor.Handles.Label(landing + Vector3.up * (wallDestructionRadius + 0.25f), $"blast r={wallDestructionRadius}");
    }
#endif
}
