using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace Unity.AI.Assistant.PlayModeTest
{
    [InitializeOnLoad]
    internal static class PlayModeTestRunner
    {
        private const string StateKey = "PlayModeTest.State";
        private const string ResultKey = "PlayModeTest.Result";
        private const string ScriptPathKey = "PlayModeTest.ScriptPath";
        private const string SentinelLog = "PLAY_MODE_TEST_COMPLETE";

        private static readonly int WaitFrames = SessionState.GetInt("PlayModeTest.WaitFrames", 10);
        private static List<string> _capturedLogs = new List<string>();

        static PlayModeTestRunner()
        {
            string state = SessionState.GetString(StateKey, "Idle");
            if (state == "WaitingForCompile")
            {
                EditorApplication.delayCall += () => {
                    SessionState.SetString(StateKey, "EnteringPlayMode");
                    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                    EditorApplication.isPlaying = true;
                };
            }
            else if (state == "EnteringPlayMode" && EditorApplication.isPlaying)
            {
                SessionState.SetString(StateKey, "InPlayMode");
                EditorApplication.update += WaitFramesThenRun;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                SessionState.SetString(StateKey, "InPlayMode");
                EditorApplication.update += WaitFramesThenRun;
            }
        }

        private static int _frameCount = 0;
        private static void WaitFramesThenRun()
        {
            _frameCount++;
            if (_frameCount < WaitFrames) return;
            EditorApplication.update -= WaitFramesThenRun;

            string resultJson = RunTestLogic();
            SessionState.SetString(ResultKey, resultJson);
            SessionState.SetString(StateKey, "Done");
            EditorApplication.isPlaying = false;
        }

        private static string RunTestLogic()
        {
            var controller = Object.FindAnyObjectByType<FishingController>();
            if (controller == null) return "{ \"success\": false, \"error\": \"FishingController not found\" }";

            var field = typeof(FishingController).GetField("registeredFish", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            int count = -1;
            if (field != null)
            {
                var list = field.GetValue(controller) as List<FishFishingBehaviour>;
                if (list != null) count = list.Count;
            }

            return "{ \"success\": true, \"registered_count\": " + count + " }";
        }

        private static void SelfDestruct() { /* ... */ }
    }
}