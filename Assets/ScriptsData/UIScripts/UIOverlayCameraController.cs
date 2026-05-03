using UnityEngine;
using System.Collections;

public class UIOverlayCameraController : MonoBehaviour
{
    [Header("Camera Reference (Orthographic UI Camera)")]
    public Camera uiCamera;

    [Header("Ortho Size Settings")]
    public float hiddenSize = 0.01f;      // Essentially invisible
    public float shownSize = 5f;          // Normal UI view
    public float transitionDuration = 0.4f;

    private Coroutine sizeRoutine;

    public void ShowUI()
    {
        SetOrthoSize(shownSize);
    }

    public void HideUI()
    {
        SetOrthoSize(hiddenSize);
    }

    void SetOrthoSize(float targetSize)
    {
        if (sizeRoutine != null)
            StopCoroutine(sizeRoutine);

        sizeRoutine = StartCoroutine(AnimateOrthoSize(targetSize));
    }

    IEnumerator AnimateOrthoSize(float targetSize)
    {
        float start = uiCamera.orthographicSize;
        float t = 0f;

        while (t < transitionDuration)
        {
            t += Time.deltaTime;
            float p = t / transitionDuration;

            uiCamera.orthographicSize = Mathf.Lerp(start, targetSize, p);
            yield return null;
        }

        uiCamera.orthographicSize = targetSize;
        sizeRoutine = null;
    }
}
