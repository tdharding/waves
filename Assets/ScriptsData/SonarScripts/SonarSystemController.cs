using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Cinemachine;

public class SonarSystemController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Material rockMaterial;
      [SerializeField] private Material statueMaterial;
    [SerializeField] private Material rockSonarMaterial;
    [SerializeField] private Material splineWallMaterial;
    [SerializeField] private Material waveplaneMaterial;
    [SerializeField] private Material arenafloorMaterial;
    [SerializeField] public Transform boat;

    private Color arenaFloorOriginalColor;
    private Coroutine arenaColorRoutine;
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColour");

    [Header("Cinemachine Focus Control")]
    [SerializeField] private CinemachineVolumeSettings boatFollowVolume;
    [SerializeField] private float sonarFocusOffsetValue = 1f;
    private float defaultFocusOffset;

    [Header("Shader Properties")]
    [SerializeField] private string boatSonarPosition = "_BoatSonarPosition";
    [SerializeField] private string maxRingRadiusProperty = "_MaxRingRadius";
    [SerializeField] private string SonarRadius = "_Sonar_Radius";
    [SerializeField] private string rockSonarFactorProperty = "_RockSonarFactor";
    [SerializeField] private string rockSonarRadiusProperty = "_SonarRadius";
    [SerializeField] private string ringEmphasisProperty = "_RingEmphasis1";
    [SerializeField] private string extraColourProperty = "_ExtraColour";

    [Header("Rock Material — Baseline (outside sonar)")]
    [SerializeField] private float baselineMaxRingRadius = 0f;
    [SerializeField] private float baselineRingEmphasis = 0f;
    [SerializeField] private Color baselineExtraColour = Color.black;

    [Header("Rock Material — Sonar Targets")]
    [SerializeField] private float sonarMaxRingRadiusTarget = 1f;
    [SerializeField] private float ringEmphasisTarget = 50f;
    [SerializeField] private Color extraColourTarget = Color.white;

    public Color newColor = Color.black;

    [Header("Forward offset of sonar center")]
    public float sonarOffset = 0.5f;

    [Header("Target values")]
    [SerializeField] private float sonarradiusTarget = 0.4f;
    [SerializeField] private float RocksRadiusSonar = 0.5f;
    [SerializeField] private float rockSonarMaskTarget = 1f;
    [SerializeField] private float rockSonarRadiusTarget = 1.13f;

    [Header("Timing")]
    [SerializeField] private float revealDuration = 5f;
    [SerializeField] private float tweenTime = 0.5f;

    [Header("Input")]
    [SerializeField] private KeyCode sonarKey = KeyCode.S;

    [Header("Sonar Fade Globals")]
    [Tooltip("Pushes the sonar fade as globals for the SonarFade subgraph, so any material can use it " +
             "without its own _SonarCenter / _SonarRadius / _RockSonarFactor properties. The per-material " +
             "writes below still happen, so materials not yet migrated keep working.")]
    [SerializeField] private bool pushSonarFadeGlobals = true;
    [Tooltip("Scales the dither noise used as the dissolve edge — the DitherFactor from the old node chain.")]
    [SerializeField] private float sonarFadeDither = 1f;
    [Tooltip("Logs the fade globals once a second, so you can see what the shader is actually receiving.")]
    [SerializeField] private bool debugFadeGlobals = false;

    [Header("Sonar Grid System")]
    [Tooltip("Parent object of the sonar grid (planes + generator). Deactivated whenever sonar is off " +
             "so none of it renders or ticks. Left blank, it falls back to LevelDataController's sonar grid parent.")]
    [SerializeField] private GameObject sonarGridRoot;
    [SerializeField] private bool deactivateGridWhenIdle = true;

    [Header("Radius2Fishing")]
    public float CurrentNormalizedRadius => currentSonarValue;

    [SerializeField] private BoatCameraZoom cameraZoom;

    private float currentSonarValue = 0f;
    private Coroutine sonarRoutine;
    private bool isSonarActive = false;

    private const float SONAR_EPSILON = 0.02f;

    public bool IsSonarActive => isSonarActive;

    public void SetSoulBoat(Transform soulBoat) => boat = soulBoat;

    private void Awake()
    {
        // Apply baseline values to rock material on startup
        ApplyRockMaterialBaseline();

        if (waveplaneMaterial != null)
            waveplaneMaterial.SetFloat(SonarRadius, 0f);

        if (boatFollowVolume != null)
            defaultFocusOffset = boatFollowVolume.FocusOffset;

        if (rockSonarMaterial != null)
        {
            //rockSonarMaterial.SetFloat(rockSonarFactorProperty, 0f);
            rockSonarMaterial.SetFloat(rockSonarRadiusProperty, 0f);
        }

        if (splineWallMaterial != null)
            splineWallMaterial.SetFloat(rockSonarRadiusProperty, 0f);

        if (arenafloorMaterial != null)
            arenaFloorOriginalColor = arenafloorMaterial.GetColor(BaseColorID);
    }

    private void Start()
    {
        // Wait one frame so LevelDataController has finished configuring the grid before we hide it.
        if (deactivateGridWhenIdle)
            StartCoroutine(DeactivateGridAfterSetup());
    }

    // =====================================================
    // SONAR FADE GLOBALS
    // =====================================================

    static readonly int FadeCentreId   = Shader.PropertyToID("_SonarFadeCentre");
    static readonly int FadeRadiusId   = Shader.PropertyToID("_SonarFadeRadius");
    static readonly int FadeStrengthId = Shader.PropertyToID("_SonarFadeStrength");
    static readonly int FadeDitherId   = Shader.PropertyToID("_SonarFadeDither");

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += PushSonarFadeGlobals;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= PushSonarFadeGlobals;
    }

    // The sonar centre is screen space, so it has to be projected with the camera the frame is about
    // to be rendered with — projecting in Update/LateUpdate puts it a frame behind the camera rig.
    // Re-pushed every frame, so a shader reimport that wipes the globals self-heals next frame.
    private void PushSonarFadeGlobals(ScriptableRenderContext context, Camera cam)
    {
        if (!pushSonarFadeGlobals || cam == null) return;
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;

        Vector3 vp = Vector3.zero;
        if (boat != null)
        {
            vp = cam.WorldToViewportPoint(boat.position + boat.forward * sonarOffset);
            Shader.SetGlobalVector(FadeCentreId, new Vector4(vp.x, vp.y, 0f, 0f));
        }

        // Same two values the spline wall and statue materials already receive, as globals.
        float radius   = currentSonarValue * rockSonarRadiusTarget;
        float strength = currentSonarValue * rockSonarMaskTarget;

        Shader.SetGlobalFloat(FadeRadiusId,   radius);
        Shader.SetGlobalFloat(FadeStrengthId, strength);
        Shader.SetGlobalFloat(FadeDitherId,   sonarFadeDither);

        if (debugFadeGlobals && cam.cameraType == CameraType.Game)
        {
            _fadeDebugTimer += Time.deltaTime;
            if (_fadeDebugTimer >= 1f)
            {
                _fadeDebugTimer = 0f;
                Debug.Log($"[SonarSystemController] fade globals — sonarValue:{currentSonarValue:F3} " +
                          $"centre:({vp.x:F2},{vp.y:F2}) radius:{radius:F3} strength:{strength:F3} " +
                          $"dither:{sonarFadeDither:F2} boat:{(boat == null ? "NULL" : "ok")}");
            }
        }
    }

    private float _fadeDebugTimer;

    private IEnumerator DeactivateGridAfterSetup()
    {
        yield return null;

        if (!isSonarActive)
            SetGridActive(false);
    }

    private GameObject ResolveGridRoot()
    {
        if (sonarGridRoot == null)
        {
            Transform parent = LevelDataController.Instance != null
                ? LevelDataController.Instance.GetSonarGridParent()
                : null;

            if (parent != null) sonarGridRoot = parent.gameObject;
        }

        return sonarGridRoot;
    }

    private void SetGridActive(bool active)
    {
        if (!deactivateGridWhenIdle && !active) return;

        GameObject root = ResolveGridRoot();
        if (root == null) return;

        if (root.activeSelf != active)
            root.SetActive(active);
    }

    private void ApplyRockMaterialBaseline()
    {
        if (rockMaterial == null) return;

        rockMaterial.SetFloat(maxRingRadiusProperty, baselineMaxRingRadius);
        rockMaterial.SetFloat(ringEmphasisProperty, baselineRingEmphasis);
        rockMaterial.SetColor(extraColourProperty, baselineExtraColour);
    }

    private void Update()
    {
        Vector3 sonarOrigin = boat.position + boat.forward * sonarOffset;
        waveplaneMaterial.SetVector(boatSonarPosition, sonarOrigin);

        if (Input.GetKeyDown(sonarKey))
        {
            if (isSonarActive)
                DeactivateSonar();
            else
            {
                WavePreset preset = LevelDataController.Instance?.GetActiveWavePreset();
                if (preset == null || preset.sonarEnabled)
                    ActivateSonar();
            }
        }
    }

    public void ActivateSonar()
    {
        if (rockMaterial == null || waveplaneMaterial == null)
            return;

        if (sonarRoutine != null)
            StopCoroutine(sonarRoutine);

        // Grid only exists while sonar is running
        SetGridActive(true);

        cameraZoom?.SetSonarZoom(true);

        // Hold the camera's vertical angle — horizontal turning only while scanning
        if (CameraController.Instance != null)
            CameraController.Instance.SetSonarView(true);

        if (arenafloorMaterial != null)
        {
            if (arenaColorRoutine != null) StopCoroutine(arenaColorRoutine);
            arenaColorRoutine = StartCoroutine(TweenArenaColor(arenaFloorOriginalColor, Color.black, tweenTime));
        }

        if (boatFollowVolume != null)
            boatFollowVolume.FocusOffset = sonarFocusOffsetValue;

        sonarRoutine = StartCoroutine(SonarRoutine());
    }

    public void DeactivateSonar()
    {
        if (!isSonarActive)
            return;

        if (sonarRoutine != null)
            StopCoroutine(sonarRoutine);

        cameraZoom?.SetSonarZoom(false);

        if (arenafloorMaterial != null)
        {
            if (arenaColorRoutine != null) StopCoroutine(arenaColorRoutine);
            arenaColorRoutine = StartCoroutine(TweenArenaColor(arenafloorMaterial.GetColor(BaseColorID), arenaFloorOriginalColor, tweenTime));
        }

        if (boatFollowVolume != null)
            boatFollowVolume.FocusOffset = defaultFocusOffset;

        sonarRoutine = StartCoroutine(ShutdownRoutine());
    }

    private IEnumerator TweenArenaColor(Color from, Color to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            arenafloorMaterial.SetColor(BaseColorID, Color.Lerp(from, to, elapsed / duration));
            yield return null;
        }
        arenafloorMaterial.SetColor(BaseColorID, to);
    }

    private IEnumerator SonarRoutine()
    {
        isSonarActive = true;

        yield return TweenRingRadius(currentSonarValue, 1f, tweenTime);
        yield return new WaitForSeconds(revealDuration);
        yield return ShutdownRoutine();
    }

    private IEnumerator ShutdownRoutine()
    {
        // Gameplay exits sonar immediately — visuals tween out separately
        isSonarActive = false;

        if (CameraController.Instance != null)
            CameraController.Instance.SetSonarView(false);

        if (currentSonarValue <= SONAR_EPSILON)
            SetSonarValue(0f);
        else
            yield return TweenRingRadius(currentSonarValue, 0f, tweenTime);

        ApplyRockMaterialBaseline();

        // Only switch the grid off once it has finished tweening out
        SetGridActive(false);

        sonarRoutine = null;
    }

    private IEnumerator TweenRingRadius(float from, float to, float duration)
    {
        float scaledDuration = Mathf.Max(
            duration * Mathf.Abs(to - from),
            0.05f
        );

        float elapsed = 0f;

        while (elapsed < scaledDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / scaledDuration);
            float value = Mathf.Lerp(from, to, t);

            SetSonarValue(value);
            yield return null;
        }

        SetSonarValue(to);
    }

    private void SetSonarValue(float value)
    {
        currentSonarValue = value;

        if (rockMaterial != null)
        {
            // Lerp all three rock properties between their baseline and sonar target values
            rockMaterial.SetFloat(maxRingRadiusProperty, Mathf.Lerp(baselineMaxRingRadius, sonarMaxRingRadiusTarget, value));
            rockMaterial.SetFloat(ringEmphasisProperty,  Mathf.Lerp(baselineRingEmphasis,  ringEmphasisTarget,      value));
            rockMaterial.SetColor(extraColourProperty,   Color.Lerp(baselineExtraColour,   extraColourTarget,       value));
        }

        if (rockSonarMaterial != null)
        {
            //rockSonarMaterial.SetFloat(rockSonarFactorProperty,   value * rockSonarMaskTarget);
            rockSonarMaterial.SetFloat(rockSonarRadiusProperty,   value * rockSonarRadiusTarget);
            statueMaterial.SetFloat(rockSonarFactorProperty, value * rockSonarMaskTarget);
        }

        if (splineWallMaterial != null)
            splineWallMaterial.SetFloat(rockSonarRadiusProperty, value * rockSonarRadiusTarget);

        if (waveplaneMaterial != null)
            waveplaneMaterial.SetFloat(SonarRadius, value * sonarradiusTarget);
    }
}