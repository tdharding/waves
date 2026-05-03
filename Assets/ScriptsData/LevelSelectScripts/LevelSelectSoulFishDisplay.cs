using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Shared display controller for the soul-fish render-texture preview.
/// One instance lives in the level-select scene; every arena points at it.
///
/// Setup in the prefab:
///   - Assign <see cref="fishPrefab"/>  (a prefab with <see cref="DisplayFishSwimmer"/>).
///   - Assign <see cref="spawnRoot"/>   (a Transform centred in front of the display camera).
///   - Set    <see cref="swimExtents"/> to define the half-extents of the swim volume.
///   - Optionally cap <see cref="maxDisplayFish"/> if the volume is small.
/// </summary>
public class LevelSelectSoulFishDisplay : MonoBehaviour
{
    // ─────────────────────────────────────────────
    // SINGLETON
    // ─────────────────────────────────────────────

    public static LevelSelectSoulFishDisplay Instance { get; private set; }

    // ─────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────

    [Header("Fish")]
    [Tooltip("Prefab to spawn for each uncaught soul fish. Should carry a DisplayFishSwimmer component.")]
    public GameObject fishPrefab;

    [Tooltip("Maximum number of fish shown regardless of how many are in the level.")]
    public int maxDisplayFish = 10;

    [Header("Fade")]
    [Tooltip("Value the renderer's _Alpha property fades to when the display is active.")]
    public float fadeTargetAlpha = 0f;

    [Tooltip("Duration of the fade in seconds.")]
    public float fadeDuration = 0.4f;

    [Header("Swim Volume")]
    [Tooltip("Parent transform for spawned fish; also the centre of the swim circle.")]
    public Transform spawnRoot;

    [Tooltip("Half-extents of the bounding volume in spawnRoot's local space. " +
             "The circle radius is derived from the smallest XZ extent multiplied by circleRadiusScale.")]
    public Vector3 swimExtents = new Vector3(2f, 0.5f, 2f);

    [Header("Path Settings")]
    [Tooltip("Number of waypoints placed around each circular path. More = smoother curve.")]
    [Min(3)]
    public int pathPointCount = 10;

    [Tooltip("Degrees each successive path is rotated from the previous, spreading fish apart.")]
    public float pathRotationStep = 22f;

    [Tooltip("Random ± scale variation on the radius of each generated path.")]
    [Range(0f, 0.5f)]
    public float radiusVariation = 0.15f;

    [Tooltip("Max random XZ displacement applied to each waypoint for an organic feel.")]
    public float pointRandomness = 0.1f;

    [Tooltip("Units per second fish travel along their path.")]
    public float swimSpeed = 0.8f;

    [Tooltip("Degrees per second fish rotate to face their travel direction.")]
    public float turnSpeed = 120f;


    // ─────────────────────────────────────────────
    // STATE
    // ─────────────────────────────────────────────

    private static readonly int _alphaPropID = Shader.PropertyToID("_Alpha");

    private readonly List<GameObject>        _spawnedFish  = new List<GameObject>();
    private readonly List<SplineContainer>   _spawnedPaths = new List<SplineContainer>();
    private GridData  _currentGridData;
    private Renderer  _activeFadeTarget;
    private Renderer  _activeFadeInTarget;
    private float     _fadeBaseAlpha = 1f;
    private Coroutine _fadeOutCoroutine;
    private Coroutine _fadeInCoroutine;
    private Coroutine _hideCoroutine;

    // ─────────────────────────────────────────────
    // UNITY
    // ─────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[SoulFishDisplay] Duplicate instance destroyed.", gameObject);
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─────────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────────

    /// <summary>
    /// Switch the display to show the uncaught soul fish for <paramref name="gridData"/>.
    /// Pass the arena's <paramref name="fadeTarget"/> renderer to fade it out while the
    /// display is active; pass null to skip fading.
    /// Calling this again with a different GridData seamlessly replaces the previous fish.
    /// </summary>
    public void Show(GridData gridData, Renderer fadeTarget = null, Renderer fadeInTarget = null)
    {
        if (gridData == null) { Hide(); return; }
        if (gridData == _currentGridData) return; // already showing this level

        // Cancel any in-progress hide so it doesn't despawn the incoming fish
        if (_hideCoroutine != null)
        {
            StopCoroutine(_hideCoroutine);
            _hideCoroutine = null;
        }

        // If switching arenas, restore the previous renderers before swapping
        if (_activeFadeTarget != null && _activeFadeTarget != fadeTarget)
            StartFadeOut(_activeFadeTarget, _fadeBaseAlpha);
        if (_activeFadeInTarget != null && _activeFadeInTarget != fadeInTarget)
            StartFadeIn(_activeFadeInTarget, 0f);

        // Capture base alpha of the fade-out renderer so we can restore it on Hide
        if (fadeTarget != null)
            _fadeBaseAlpha = fadeTarget.material.GetFloat(_alphaPropID);

        _activeFadeTarget   = fadeTarget;
        _activeFadeInTarget = fadeInTarget;
        _currentGridData    = gridData;
        DespawnAllFish();

        int uncaught = GetUncaughtCount(gridData);
        int toSpawn  = Mathf.Min(uncaught, maxDisplayFish);

        for (int i = 0; i < toSpawn; i++)
            SpawnOneFish(i, toSpawn);

        // Start fade-out immediately, but delay fade-in by one frame so
        // fish have completed their first Update and are on their spline paths
        StartFadeOut(_activeFadeTarget, fadeTargetAlpha);
        StartFadeIn(_activeFadeInTarget, 1f);
    }

    /// <summary>
    /// Fades out the reveal material, then despawns fish once the fade completes.
    /// </summary>
    public void Hide()
    {
        if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);
        _hideCoroutine = StartCoroutine(HideSequence(_activeFadeTarget, _activeFadeInTarget));
        _activeFadeTarget   = null;
        _activeFadeInTarget = null;
        _currentGridData    = null;
    }

    private IEnumerator HideSequence(Renderer fadeOutTarget, Renderer fadeInTarget)
    {
        // Both fades start simultaneously
        StartFadeIn(fadeInTarget, 0f);
        StartFadeOut(fadeOutTarget, _fadeBaseAlpha);

        // Wait for fades to finish before removing the fish
        yield return new WaitForSecondsRealtime(fadeDuration);

        DespawnAllFish();
        _hideCoroutine = null;
    }

    /// <summary>
    /// Returns how many soul fish in <paramref name="gridData"/> have not yet been caught.
    /// </summary>
    public static int GetUncaughtCount(GridData gridData)
    {
        if (gridData == null) return 0;

        int total  = gridData.soulSpawnPoints?.Count ?? 0;
        int caught = GameProgressData.GetCaughtSoulIDs(gridData.levelID)?.Count ?? 0;
        return Mathf.Max(0, total - caught);
    }

    // ─────────────────────────────────────────────
    // EDITOR PREVIEW
    // ─────────────────────────────────────────────

#if UNITY_EDITOR
    /// <summary>
    /// Spawns <paramref name="fishCount"/> fish using the current path settings.
    /// Called from the custom inspector — works in prefab edit mode.
    /// </summary>
    public void StartPreview(int fishCount)
    {
        StopPreview();
        int toSpawn = Mathf.Min(fishCount, maxDisplayFish);
        for (int i = 0; i < toSpawn; i++)
            SpawnOneFish(i, toSpawn);
    }

    /// <summary>Cleans up all preview fish.</summary>
    public void StopPreview() => DespawnAllFish();
#endif

    // ─────────────────────────────────────────────
    // INTERNAL
    // ─────────────────────────────────────────────

    private float BaseRadius => Mathf.Min(swimExtents.x, swimExtents.z);

    private SplineContainer BuildSplinePath(int fishIndex)
    {
        // Create path GameObject as a child of spawnRoot with identity local transform
        // so spline knot positions are in spawnRoot local space
        var pathGO = new GameObject($"FishPath_{fishIndex}");
        pathGO.transform.SetParent(spawnRoot);
        pathGO.transform.localPosition = Vector3.zero;
        pathGO.transform.localRotation = Quaternion.identity;
        pathGO.transform.localScale    = Vector3.one;
        pathGO.AddComponent<DisplayFishPath>();

        var container  = pathGO.AddComponent<SplineContainer>();
        var spline     = container.Spline;
        spline.Closed  = true;

        float radius    = BaseRadius * UnityEngine.Random.Range(1f - radiusVariation, 1f + radiusVariation);
        float rotOffset = fishIndex * pathRotationStep;

        for (int p = 0; p < pathPointCount; p++)
        {
            float angle = rotOffset + (p / (float)pathPointCount) * 360f;
            float rad   = angle * Mathf.Deg2Rad;

            // Generate and clamp in spawnRoot local space
            Vector3 local = new Vector3(
                radius * Mathf.Sin(rad) + UnityEngine.Random.Range(-pointRandomness, pointRandomness),
                UnityEngine.Random.Range(-pointRandomness, pointRandomness),
                radius * Mathf.Cos(rad) + UnityEngine.Random.Range(-pointRandomness, pointRandomness));

            local.x = Mathf.Clamp(local.x, -swimExtents.x, swimExtents.x);
            local.y = Mathf.Clamp(local.y, -swimExtents.y, swimExtents.y);
            local.z = Mathf.Clamp(local.z, -swimExtents.z, swimExtents.z);

            spline.Add(new BezierKnot((float3)local), TangentMode.AutoSmooth);
        }

        _spawnedPaths.Add(container);
        return container;
    }

    private void SpawnOneFish(int index, int total)
    {
        if (fishPrefab == null || spawnRoot == null)
        {
            Debug.LogWarning("[SoulFishDisplay] fishPrefab or spawnRoot not assigned.", this);
            return;
        }

        SplineContainer path = BuildSplinePath(index);

        // Place fish at a random point on the spline so they don't all start together
        SplineUtility.Evaluate(path.Spline, 0f, out float3 startLocal, out _, out _);
        Vector3 startWorld = path.transform.TransformPoint((Vector3)startLocal);

        GameObject fish = Instantiate(fishPrefab, startWorld, Quaternion.identity, spawnRoot);
        fish.name = $"DisplayFish_{index}";

        if (fish.TryGetComponent<DisplayFishSwimmer>(out var swimmer))
            swimmer.Initialise(path, swimSpeed, turnSpeed);

        _spawnedFish.Add(fish);
    }

    private void StartFadeOut(Renderer target, float targetAlpha)
    {
        if (target == null)
        {
            Debug.LogWarning("[SoulFishDisplay] StartFadeOut: target is null — skipping.");
            return;
        }
        Debug.Log($"[SoulFishDisplay] StartFadeOut: '{target.name}' → {targetAlpha} over {fadeDuration}s.");
        if (_fadeOutCoroutine != null) StopCoroutine(_fadeOutCoroutine);
        _fadeOutCoroutine = StartCoroutine(FadeRoutine(target, targetAlpha,
            () => _fadeOutCoroutine = null));
    }

    private void StartFadeIn(Renderer target, float targetAlpha)
    {
        if (target == null)
        {
            Debug.LogWarning("[SoulFishDisplay] StartFadeIn: target is null — assign soulFishDisplayFadeIn on the LevelSelectArenaController.");
            return;
        }
        float currentAlpha = target.material.color.a;
        Debug.Log($"[SoulFishDisplay] StartFadeIn: '{target.name}' color.a {currentAlpha} → {targetAlpha} over {fadeDuration}s.");
        if (_fadeInCoroutine != null) StopCoroutine(_fadeInCoroutine);
        _fadeInCoroutine = StartCoroutine(FadeInRoutine(target, targetAlpha,
            () => _fadeInCoroutine = null));
    }

    private IEnumerator FadeInRoutine(Renderer target, float targetAlpha,
                                      System.Action onComplete)
    {
        Material mat    = target.material;
        float fromAlpha = mat.color.a;
        float elapsed   = 0f;

        Debug.Log($"[SoulFishDisplay] FadeInRoutine START: mat='{mat.name}' shader='{mat.shader.name}' color.a={fromAlpha} → {targetAlpha}");

        while (elapsed < fadeDuration)
        {
            elapsed    += Time.unscaledDeltaTime;
            Color col   = mat.color;
            col.a       = Mathf.Lerp(fromAlpha, targetAlpha, Mathf.Clamp01(elapsed / fadeDuration));
            mat.color   = col;
            yield return null;
        }

        Color final = mat.color;
        final.a     = targetAlpha;
        mat.color   = final;
        Debug.Log($"[SoulFishDisplay] FadeInRoutine END: mat='{mat.name}' final color.a={mat.color.a}");
        onComplete?.Invoke();
    }

    private IEnumerator FadeRoutine(Renderer target, float targetAlpha,
                                    System.Action onComplete)
    {
        Material mat    = target.material;
        float fromAlpha = mat.GetFloat(_alphaPropID);
        float elapsed   = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t  = Mathf.Clamp01(elapsed / fadeDuration);
            mat.SetFloat(_alphaPropID, Mathf.Lerp(fromAlpha, targetAlpha, t));
            yield return null;
        }

        mat.SetFloat(_alphaPropID, targetAlpha);
        onComplete?.Invoke();
    }

    private void DespawnAllFish()
    {
        foreach (var fish in _spawnedFish)
            DestroyObject(fish);
        _spawnedFish.Clear();

        foreach (var path in _spawnedPaths)
            DestroyObject(path != null ? path.gameObject : null);
        _spawnedPaths.Clear();
    }

    private static void DestroyObject(Object obj)
    {
        if (obj == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            DestroyImmediate(obj);
        else
#endif
            Destroy(obj);
    }

    // ─────────────────────────────────────────────
    // GIZMOS
    // ─────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (spawnRoot == null) return;

        // Bounding box
        Gizmos.color  = new Color(0.3f, 0.8f, 1f, 0.3f);
        Gizmos.matrix = spawnRoot.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, swimExtents * 2f);
        Gizmos.color  = new Color(0.3f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireCube(Vector3.zero, swimExtents * 2f);

        // Base radius reference circle
        Gizmos.matrix = Matrix4x4.identity;
        float radius   = BaseRadius;
        Vector3 center = spawnRoot.position;
        const int segments = 48;
        UnityEditor.Handles.color = new Color(0.4f, 1f, 0.6f, 0.4f);
        Vector3 prev = center + new Vector3(Mathf.Sin(0f), 0f, Mathf.Cos(0f)) * radius;
        for (int i = 1; i <= segments; i++)
        {
            float rad    = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * radius;
            UnityEditor.Handles.DrawLine(prev, next, 1f);
            prev = next;
        }
        // Generated spline paths draw themselves via SplineContainer — no extra gizmo needed
    }
#endif
}
