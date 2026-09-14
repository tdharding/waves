using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps the world's aesthetic globals alive OUT of play mode, so the scene view shows what the
/// designer holds rather than 0.
///
/// It has to be a pump rather than a one-off push. The uniforms are bare $Globals with no entry
/// in any material's property block, so nothing on disk remembers them: saving a shader graph,
/// touching a material or reloading the scene leaves them at 0 for good unless something keeps
/// putting them back. In play mode LevelSelectDataController.Update does that job and this
/// stands down.
/// </summary>
[InitializeOnLoad]
public static class LevelSelectAestheticsPump
{
    /// <summary>
    /// The Level Select Designer's workspace copy, while its window is open. That copy carries
    /// edits which are not on the asset yet, so it wins over the scene's data — otherwise every
    /// tick would drag the preview back to the last saved number while a field is being dragged.
    /// </summary>
    public static LevelSelectDesignerData Preview;

    private static LevelSelectDataController _controller;
    private static double _nextSearch;

    static LevelSelectAestheticsPump()
    {
        EditorApplication.update += Pump;
    }

    private static void Pump()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        var data = Preview;

        if (data == null)
        {
            // Searching the scene every tick is wasteful, so the controller is cached and only
            // looked for again once a second while there is none.
            if (_controller == null && EditorApplication.timeSinceStartup >= _nextSearch)
            {
                _nextSearch = EditorApplication.timeSinceStartup + 1.0;
                _controller = Object.FindFirstObjectByType<LevelSelectDataController>();
            }

            if (_controller != null) data = _controller.DesignerData;
        }

        if (data != null) data.ApplyAesthetics();
    }
}
