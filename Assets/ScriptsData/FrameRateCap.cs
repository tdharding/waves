using UnityEngine;

/// <summary>
/// Caps the frame rate for the whole game, set once before the first scene loads.
///
/// VSync (Quality Settings) is the main cap on desktop builds, and while it is on Unity ignores
/// targetFrameRate there. This target covers the cases VSync does not: play mode in the editor
/// with the Game view's VSync toggle off, and platforms that ignore vSyncCount.
/// </summary>
public static class FrameRateCap
{
    public const int TargetFrameRate = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Apply()
    {
        Application.targetFrameRate = TargetFrameRate;
    }
}
