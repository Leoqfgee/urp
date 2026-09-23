using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Urp.ArDemo.Editor
{
    public static class ShengDingViewerValidation
    {
        private const string Key = "ShengDingViewerValidation.Running";
        private static int frame;

        public static void RunFromCommandLine()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/UrpARPrototype.unity");
            SessionState.SetBool(Key, true);
            Subscribe();
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void Restore()
        {
            if (SessionState.GetBool(Key, false)) Subscribe();
        }

        private static void Subscribe()
        {
            EditorApplication.playModeStateChanged -= OnState;
            EditorApplication.playModeStateChanged += OnState;
        }

        private static void OnState(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                Screen.SetResolution(1080, 2400, false);
                frame = 0;
                EditorApplication.update += Update;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Key, false);
                EditorApplication.playModeStateChanged -= OnState;
                EditorApplication.Exit(0);
            }
        }

        private static void Update()
        {
            frame++;
            if (frame == 20)
            {
                var app = UnityEngine.Object.FindObjectOfType<UrpAppController>(true);
                var artifact = AssetDatabase.LoadAssetAtPath<ArtifactInfo>(
                    "Assets/Models/Artifacts/ShengDing/ShengDingInfo.asset");
                typeof(UrpAppController).GetMethod("SelectArtifact",
                    BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(app, new object[] { artifact });
            }
            if (frame != 80) return;
            EditorApplication.update -= Update;
            try
            {
                var viewer = UnityEngine.Object.FindObjectOfType<ModelViewerController>(true);
                if (viewer == null || viewer.ActiveModel == null)
                    throw new InvalidOperationException("ShengDing viewer model not instantiated");
                Renderer[] renderers = viewer.ActiveModel.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0 || !renderers[0].enabled)
                    throw new InvalidOperationException("ShengDing viewer renderer not visible");
                Camera camera = viewer.ViewerCamera;
                RenderTexture target = camera.targetTexture;
                if (target == null) throw new InvalidOperationException("Viewer render target missing");
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                try
                {
                    var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    image.Apply();
                    string path = "F:/Au/buildlogs/shengding_viewer_v60.png";
                    File.WriteAllBytes(path, image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                    typeof(UrpUiScreenshotCapture).GetField("controller",
                        BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null,
                        UnityEngine.Object.FindObjectOfType<UrpAppController>(true));
                    typeof(UrpUiScreenshotCapture).GetMethod("Capture",
                        BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null,
                        new object[] { "06-shengding-v60.png" });
                    Debug.Log("SHENGDING_VIEWER_RUNTIME_OK renderers=" + renderers.Length + " screenshot=" + path);
                }
                finally { RenderTexture.active = previous; }
                EditorApplication.ExitPlaymode();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SessionState.SetBool(Key, false);
                EditorApplication.Exit(1);
            }
        }
    }
}
