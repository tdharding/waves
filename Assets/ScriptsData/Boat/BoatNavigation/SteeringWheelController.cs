using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class SteeringWheelController : MonoBehaviour
{
    [Header("Wheel Settings")]
    public RectTransform wheel;
    public float maxWheelAngle = 90f;
    public float deadZone = 20f;
    public float snapSpeed = 15f;

    [Header("Notches")]
    public bool useNotches = false;
    public int notchCount = 10;

    [Header("Audio")]
    public AudioSource notchAudio;
    private int lastNotchIndex = 0;

    [Header("Virtual Cursor")]
    public VirtualCursor virtualCursor;

    [Header("Fade Settings")]
    public Image wheelImage;
    public float fadeSpeed = 3f;

    private Material wheelMat;
    private float targetAlpha = 1f;
    private bool fading = false;

    private bool wheelActive = false;  // OFF until slide completes
    public float currentWheelAngle = 0f;

    // ---------------------------------------------------------
    // CLEANED POSITIONS — Y ONLY
    // ---------------------------------------------------------
    [Header("Wheel Positions (Y Only)")]
    public float introWheelPosY;
    public float gameplayWheelPosY;

    [Header("Wheel Transition")]
    public float wheelTransitionDuration = 0.5f;


    // ---------------------------------------------------------
    // INIT
    // ---------------------------------------------------------
    void Start()
    {
        if (wheelImage != null)
        {
            wheelMat = Instantiate(wheelImage.material);
            wheelImage.material = wheelMat;

            Color c = wheelMat.color;
            c.a = 1f;
            wheelMat.color = c;
        }
    }


    // ---------------------------------------------------------
    // UPDATE
    // ---------------------------------------------------------
    void Update()
    {
        // Fades should continue even while paused
        if (fading)
            UpdateWheelFade();

        // STOP steering behaviour while paused
        if (PauseManager.IsPaused)
            return;

        if (!wheelActive)
            return;

        HandleWheel();
    }


    // ---------------------------------------------------------
    // WHEEL INTERACTION
    // ---------------------------------------------------------
    void HandleWheel()
    {
        Vector2 cursorPos = virtualCursor.Position;
        Vector2 center = RectTransformUtility.WorldToScreenPoint(null, wheel.position);

        float deltaX = cursorPos.x - center.x;
        float absX = Mathf.Abs(deltaX);

        if (absX <= deadZone)
            return;

        float t = Mathf.InverseLerp(deadZone, Screen.width * 0.25f, absX);
        float targetAngle = Mathf.Sign(deltaX) * t * maxWheelAngle;

        if (useNotches)
        {
            float anglePerNotch = maxWheelAngle / notchCount;
            targetAngle = Mathf.Round(targetAngle / anglePerNotch) * anglePerNotch;

            int currentNotchIndex = Mathf.RoundToInt(targetAngle / anglePerNotch);

            if (currentNotchIndex != lastNotchIndex)
            {
                if (notchAudio != null)
                {
                    notchAudio.pitch = 1f + (currentNotchIndex * 0.05f);
                    notchAudio.Play();
                }

                lastNotchIndex = currentNotchIndex;
            }
        }

        currentWheelAngle = Mathf.Lerp(currentWheelAngle, targetAngle, Time.deltaTime * snapSpeed);
        wheel.localRotation = Quaternion.Euler(0f, 0f, -currentWheelAngle);
    }


    // ---------------------------------------------------------
    // PUBLIC API
    // ---------------------------------------------------------
    public void DisableWheel()
    {
        wheelActive = false;
        currentWheelAngle = 0f;
        wheel.localRotation = Quaternion.identity;
    }

    public void EnableWheel()
    {
        wheelActive = true;
        currentWheelAngle = 0f;
        wheel.localRotation = Quaternion.identity;
    }


    // Set intro pos instantly
    public void SetToIntroPosition()
    {
        if (wheel != null)
            wheel.anchoredPosition = new Vector2(wheel.anchoredPosition.x, introWheelPosY);
    }

    // Set gameplay pos instantly
    public void SetToGameplayPosition()
    {
        if (wheel != null)
            wheel.anchoredPosition = new Vector2(wheel.anchoredPosition.x, gameplayWheelPosY);
    }


    // ---------------------------------------------------------
    // FORCE SAME-FRAME START
    // ---------------------------------------------------------
    public void StartWheelTransition()
    {
        StopAllCoroutines();
        StartCoroutine(TransitionToGameplayPosition());
    }


    // ---------------------------------------------------------
    // SMOOTH TRANSITION INTRO → GAMEPLAY  (Y only)
    // ---------------------------------------------------------
    public IEnumerator TransitionToGameplayPosition()
    {
        if (wheel == null)
            yield break;

        wheelActive = false;

        // Let Canvas/Layout update before we move
        yield return null;

        float duration = wheelTransitionDuration;
        float t = 0f;

        float startY = wheel.anchoredPosition.y;
        float endY = gameplayWheelPosY;

        while (t < duration)
        {
            t += Time.deltaTime;
            float newY = Mathf.Lerp(startY, endY, t / duration);
            wheel.anchoredPosition = new Vector2(wheel.anchoredPosition.x, newY);
            yield return null;
        }

        wheel.anchoredPosition = new Vector2(wheel.anchoredPosition.x, gameplayWheelPosY);

        // Activate wheel after transition finishes
        EnableWheel();
    }


    // ---------------------------------------------------------
    // FADE
    // ---------------------------------------------------------
    public void FadeOutWheel()
    {
        targetAlpha = 0f;
        fading = true;
    }

    public void FadeInWheel()
    {
        targetAlpha = 1f;
        fading = true;
    }

    void UpdateWheelFade()
    {
        if (wheelMat == null)
            return;

        Color c = wheelMat.color;
        c.a = Mathf.MoveTowards(c.a, targetAlpha, fadeSpeed * Time.deltaTime);
        wheelMat.color = c;

        if (Mathf.Abs(c.a - targetAlpha) < 0.01f)
        {
            c.a = targetAlpha;
            wheelMat.color = c;
            fading = false;
        }
    }
}
