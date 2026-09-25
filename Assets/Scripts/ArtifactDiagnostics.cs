using UnityEngine;

namespace Urp.ArDemo
{
    internal static class ArtifactDiagnostics
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            Application.logMessageReceived += Forward;
        }

        private static void Forward(string condition, string stackTrace, LogType type)
        {
            if (!condition.Contains("[Artifact") && !condition.Contains("glTFast")) return;
            try
            {
                using (var log = new AndroidJavaClass("android.util.Log"))
                {
                    string message = condition + (type == LogType.Error || type == LogType.Exception
                        ? "\n" + stackTrace : "");
                    log.CallStatic<int>(type == LogType.Error || type == LogType.Exception ? "e" : "i",
                        "ArtifactAR", message);
                }
            }
            catch { /* Unity's own log remains available if Java logging is unavailable. */ }
        }
#endif
    }
}
