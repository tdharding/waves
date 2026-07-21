using System.Collections;
using UnityEngine;

/// <summary>
/// Shared water-level tween. Moves the four things <see cref="LevelDataController"/> sets to the
/// baseline water Y at spawn so they stay in lockstep whenever the level changes:
///   1. wave plane transform Y
///   2. wave plane material _ArenaMask.y
///   3. sonar grid parent transform Y
///   4. sonar grid material _ArenaMask.y   ← the legacy WaterLevelModifier tween omitted this,
///      which left the sonar plane's arena edge-mask desynced from the water after a change.
///
/// Run via StartCoroutine(WaveLevelTween.To(targetY, duration)) from any MonoBehaviour.
/// </summary>
public static class WaveLevelTween
{
    static readonly int ArenaMaskID = Shader.PropertyToID("_ArenaMask");

    public static IEnumerator To(float targetY, float duration)
    {
        var ldc = LevelDataController.Instance;
        if (ldc == null) yield break;

        Transform wavePlane = ldc.GetWaveTransform();
        if (wavePlane == null) yield break;

        Transform sonarGrid = ldc.GetSonarGridParent();
        Material  waveMat   = ldc.GetWaveMaterial();
        Material  sonarMat  = ldc.GetSonarGridMaterial();

        float fromWaveY = wavePlane.position.y;
        float toWaveY   = targetY;

        // The sonar grid parent follows the wave by the same delta (mirrors legacy WaterLevelModifier).
        float fromGridY = sonarGrid != null ? sonarGrid.position.y : 0f;
        float toGridY   = fromGridY + (toWaveY - fromWaveY);

        // Anchor masks to their own current value so only the Y component moves.
        Vector4 waveMaskFrom  = waveMat  != null ? waveMat.GetVector(ArenaMaskID)  : Vector4.zero;
        Vector4 waveMaskTo    = waveMaskFrom;  waveMaskTo.y  = toWaveY;
        Vector4 sonarMaskFrom = sonarMat != null ? sonarMat.GetVector(ArenaMaskID) : Vector4.zero;
        Vector4 sonarMaskTo   = sonarMaskFrom; sonarMaskTo.y = toGridY;

        float elapsed = 0f;
        while (duration > 0f && elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);

            SetY(wavePlane, Mathf.Lerp(fromWaveY, toWaveY, t));
            if (sonarGrid != null) SetY(sonarGrid, Mathf.Lerp(fromGridY, toGridY, t));
            if (waveMat  != null)  waveMat.SetVector(ArenaMaskID,  Vector4.Lerp(waveMaskFrom,  waveMaskTo,  t));
            if (sonarMat != null)  sonarMat.SetVector(ArenaMaskID, Vector4.Lerp(sonarMaskFrom, sonarMaskTo, t));
            yield return null;
        }

        // Snap to final values.
        SetY(wavePlane, toWaveY);
        if (sonarGrid != null) SetY(sonarGrid, toGridY);
        if (waveMat  != null)  waveMat.SetVector(ArenaMaskID,  waveMaskTo);
        if (sonarMat != null)  sonarMat.SetVector(ArenaMaskID, sonarMaskTo);
    }

    static void SetY(Transform t, float y)
    {
        Vector3 p = t.position;
        p.y = y;
        t.position = p;
    }
}
