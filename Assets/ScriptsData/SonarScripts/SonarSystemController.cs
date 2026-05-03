using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

public class SonarSystemController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Material rockMaterial;
      [SerializeField] private Material statueMaterial;
    [SerializeField] private Material rockSonarMaterial;
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

    [Header("Timing")]
    [SerializeField] private float revealDuration = 5f;
    [SerializeField] private float tweenTime = 0.5f;

    [Header("Input")]
    [SerializeField] private KeyCode sonarKey = KeyCode.S;

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
            rockSonarMaterial.SetFloat(rockSonarFactorProperty, 0f);

        if (arenafloorMaterial != null)
            arenaFloorOriginalColor = arenafloorMaterial.GetColor(BaseColorID);
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

        cameraZoom?.SetSonarZoom(true);

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
        if (currentSonarValue <= SONAR_EPSILON)
            SetSonarValue(0f);
        else
            yield return TweenRingRadius(currentSonarValue, 0f, tweenTime);

        // Restore rock material to inspector-defined baseline
        ApplyRockMaterialBaseline();

        isSonarActive = false;
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
    rockSonarMaterial.SetFloat(rockSonarFactorProperty, value * rockSonarMaskTarget);
    statueMaterial.SetFloat(rockSonarFactorProperty, value * rockSonarMaskTarget);
}

if (waveplaneMaterial != null)
    waveplaneMaterial.SetFloat(SonarRadius, value * sonarradiusTarget);
    }
}