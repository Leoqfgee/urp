using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Urp.ArDemo.Editor
{
    public static class UrpUiScreenshotCapture
    {
        private const string ScenePath = "Assets/Scenes/UrpARPrototype.unity";
        private const string SessionKey = "UrpUiScreenshotCapture.Running";
        private static readonly string OutputDirectory =
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../buildlogs/v58-ui-screenshots"));
        private static int frame;
        private static UrpAppController controller;
        private static MethodInfo showPage;
        private static MethodInfo selectAndOpen;
        private static Type pageType;

        public static void RunFromCommandLine()
        {
            Directory.CreateDirectory(OutputDirectory);
            EditorSceneManager.OpenScene(ScenePath);
            SessionState.SetBool(SessionKey, true);
            Subscribe();
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void RestoreAfterDomainReload()
        {
            if (SessionState.GetBool(SessionKey, false)) Subscribe();
        }

        private static void Subscribe()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                Screen.SetResolution(1080, 2400, false);
                controller = UnityEngine.Object.FindObjectOfType<UrpAppController>(true);
                pageType = typeof(UrpAppController).GetNestedType("Page", BindingFlags.NonPublic);
                showPage = typeof(UrpAppController).GetMethod(
                    "ShowPage", BindingFlags.Instance | BindingFlags.NonPublic);
                selectAndOpen = typeof(UrpAppController).GetMethod(
                    "SelectAndOpen", BindingFlags.Instance | BindingFlags.NonPublic);
                frame = 0;
                EditorApplication.update -= CaptureSequence;
                EditorApplication.update += CaptureSequence;
                Debug.Log($"UI_CAPTURE_STARTED {Screen.width}x{Screen.height}");
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(SessionKey, false);
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                Debug.Log("URP_UI_SCREENSHOTS_OK");
                EditorApplication.Exit(0);
            }
        }

        private static void CaptureSequence()
        {
            if (!EditorApplication.isPlaying || controller == null || showPage == null) return;
            frame++;
            switch (frame)
            {
                case 30:
                    Capture("01-home.png");
                    break;
                case 50:
                    Show("Selection");
                    break;
                case 80:
                    Capture("02-selection.png");
                    break;
                case 100:
                    SelectAndOpen("Resource");
                    break;
                case 150:
                    Capture("03-resource.png");
                    break;
                case 170:
                    SelectAndOpen("Tracking");
                    break;
                case 210:
                    Capture("04-tracking.png");
                    break;
                case 240:
                    EditorApplication.update -= CaptureSequence;
                    EditorApplication.ExitPlaymode();
                    break;
            }
        }

        private static void Show(string pageName)
        {
            object page = Enum.Parse(pageType, pageName);
            showPage.Invoke(controller, new[] { page });
        }

        private static void SelectAndOpen(string pageName)
        {
            FieldInfo selectedProfileField = typeof(UrpAppController).GetField(
                "selectedProfile", BindingFlags.Instance | BindingFlags.NonPublic);
            object selectedProfile = selectedProfileField?.GetValue(controller);
            if (selectedProfile == null || selectAndOpen == null)
            {
                Show(pageName);
                return;
            }
            object page = Enum.Parse(pageType, pageName);
            selectAndOpen.Invoke(controller, new[] { selectedProfile, page });
        }

        private static void Capture(string fileName)
        {
            string path = Path.Combine(OutputDirectory, fileName);
            Canvas canvas = controller.GetComponentInChildren<Canvas>(true);
            if (canvas == null) throw new InvalidOperationException("Runtime canvas was not found.");

            var originalLayers = new Dictionary<GameObject, int>();
            foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true))
            {
                originalLayers[child.gameObject] = child.gameObject.layer;
                child.gameObject.layer = 5;
            }

            RenderMode originalMode = canvas.renderMode;
            Camera originalCamera = canvas.worldCamera;
            float originalPlaneDistance = canvas.planeDistance;
            GameObject cameraObject = new GameObject("UI Screenshot Camera");
            Camera captureCamera = cameraObject.AddComponent<Camera>();
            captureCamera.transform.position = new Vector3(0f, 0f, -100f);
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = fileName.Contains("tracking")
                ? new Color32(72, 78, 78, 255)
                : new Color32(246, 242, 232, 255);
            captureCamera.cullingMask = 1 << 5;
            captureCamera.nearClipPlane = 0.01f;
            captureCamera.farClipPlane = 200f;

            RenderTexture target = new RenderTexture(1080, 2400, 24, RenderTextureFormat.ARGB32);
            Texture2D readback = new Texture2D(1080, 2400, TextureFormat.RGBA32, false);
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = captureCamera;
                canvas.planeDistance = 1f;
                captureCamera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                captureCamera.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0f, 0f, 1080f, 2400f), 0, 0, false);
                readback.Apply(false, false);
                File.WriteAllBytes(path, readback.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousActive;
                captureCamera.targetTexture = null;
                canvas.renderMode = originalMode;
                canvas.worldCamera = originalCamera;
                canvas.planeDistance = originalPlaneDistance;
                foreach (KeyValuePair<GameObject, int> pair in originalLayers)
                {
                    if (pair.Key != null) pair.Key.layer = pair.Value;
                }
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
            Debug.Log($"UI_CAPTURE_WRITTEN {path} 1080x2400");
        }
    }
}
