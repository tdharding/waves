using UnityEngine;
using System;
using System.Collections;
using TMPro;

public class TimeTrialTimer : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Optional UI text to display remaining time")]
    [SerializeField] private TMP_Text timeText;

    private float remainingTime;
    private bool isRunning;
    private Action onTimerFinished;

    private Coroutine timerRoutine;

    private void Update()
{
#if UNITY_EDITOR
    if (Input.GetKeyDown(KeyCode.T))
    {
        Debug_ForceFinish();
    }
#endif
}


    // =====================================================
    // START TIMER
    // =====================================================
    public void StartTimer(float durationSeconds, Action onFinished)
    {
        StopTimer();

        remainingTime = durationSeconds;
        onTimerFinished = onFinished;
        isRunning = true;

        UpdateTimeText();

        timerRoutine = StartCoroutine(TimerCoroutine());
    }

    // =====================================================
    // STOP TIMER
    // =====================================================
    public void StopTimer()
    {
        if (timerRoutine != null)
        {
            StopCoroutine(timerRoutine);
            timerRoutine = null;
        }

        isRunning = false;
    }

    // =====================================================
    // TIMER LOOP
    // =====================================================
    private IEnumerator TimerCoroutine()
    {
        while (remainingTime > 0f)
        {
            remainingTime -= Time.deltaTime;
            UpdateTimeText();
            yield return null;
        }

        remainingTime = 0f;
        UpdateTimeText();

        isRunning = false;
        onTimerFinished?.Invoke();
    }

    // =====================================================
    // UI UPDATE
    // =====================================================
    private void UpdateTimeText()
    {
        if (timeText == null)
            return;

        timeText.text = FormatTime(remainingTime);
    }

    private string FormatTime(float timeSeconds)
    {
        timeSeconds = Mathf.Max(0f, timeSeconds);

        int minutes = Mathf.FloorToInt(timeSeconds / 60f);
        int seconds = Mathf.FloorToInt(timeSeconds % 60f);
        int milliseconds = Mathf.FloorToInt((timeSeconds * 100f) % 100f);

        // Format: MM:SS:MS
        return $"{minutes:00}:{seconds:00}:{milliseconds:00}";
    }

    // =====================================================
    // PUBLIC QUERY (OPTIONAL)
    // =====================================================
    public float GetRemainingTime()
    {
        return remainingTime;
    }

    public bool IsRunning()
    {
        return isRunning;
    }

    public void Debug_ForceFinish()
{
    if (!isRunning)
        return;

    remainingTime = 0f;
    UpdateTimeText();

    isRunning = false;

    if (timerRoutine != null)
    {
        StopCoroutine(timerRoutine);
        timerRoutine = null;
    }

    onTimerFinished?.Invoke();
}

}
