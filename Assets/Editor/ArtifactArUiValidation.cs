using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Urp.ArDemo.Editor
{
    public static class ArtifactArUiValidation
    {
        private const string Key = "ArtifactArUiValidation.Running";
        private static int frame;

        public static void RunFromCommandLine()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/ArtifactARScene.unity");
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
            if (++frame != 50) return;
            EditorApplication.update -= Update;
            try
            {
                Canvas canvas = null;
                foreach (Canvas candidate in UnityEngine.Object.FindObjectsOfType<Canvas>(true))
                    if (candidate.name == "Artifact AR UI") canvas = candidate;
                if (canvas == null) throw new InvalidOperationException("Artifact AR canvas missing");
                ValidateInputAndButtons(canvas);
                Capture(canvas, "F:/Au/buildlogs/artifact_ar_ui_v62.png");
                Debug.Log("ARTIFACT_AR_UI_CAPTURE_OK");
                EditorApplication.ExitPlaymode();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SessionState.SetBool(Key, false);
                EditorApplication.Exit(1);
            }
        }

        private static void ValidateInputAndButtons(Canvas canvas)
        {
            if (EventSystem.current == null ||
                !(EventSystem.current.currentInputModule is InputSystemUIInputModule))
                throw new InvalidOperationException("Artifact AR requires InputSystemUIInputModule");
            Button reset = null, info = null, close = null;
            foreach (string name in new[] { "‹ Button", "重新放置 Button", "文物介绍 Button", "关闭 Button" })
            {
                Button found = null;
                foreach (Button button in canvas.GetComponentsInChildren<Button>(true))
                    if (button.name == name) found = button;
                if (found == null || !found.interactable ||
                    !found.GetComponent<Image>().raycastTarget)
                    throw new InvalidOperationException("Artifact AR button cannot receive input: " + name);
                if (name == "重新放置 Button") reset = found;
                if (name == "文物介绍 Button") info = found;
                if (name == "关闭 Button") close = found;
            }
            foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>(true))
                if (graphic.raycastTarget && graphic.GetComponent<Button>() == null)
                    throw new InvalidOperationException("Decorative UI intercepts AR input: " + graphic.name);
            Debug.Log("ARTIFACT_AR_UI_INPUT_VALID");
            GameObject panel = null;
            foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true))
                if (child.name == "Artifact Information") panel = child.gameObject;
            if (panel == null || panel.activeSelf)
                throw new InvalidOperationException("Information panel initial state invalid");
            info.onClick.Invoke();
            if (!panel.activeSelf) throw new InvalidOperationException("Information button did not open panel");
            Capture(canvas, "F:/Au/buildlogs/artifact_ar_info_v62.png");
            close.onClick.Invoke();
            if (panel.activeSelf) throw new InvalidOperationException("Close button did not close panel");
            reset.onClick.Invoke();
            Debug.Log("ARTIFACT_AR_UI_ACTIONS_VALID");
        }

        private static void Capture(Canvas canvas, string path)
        {
            var oldLayers = new Dictionary<GameObject, int>();
            foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true))
            {
                oldLayers[child.gameObject] = child.gameObject.layer;
                child.gameObject.layer = 5;
            }
            RenderMode oldMode = canvas.renderMode;
            Camera oldCamera = canvas.worldCamera;
            float oldDistance = canvas.planeDistance;
            var cameraObject = new GameObject("Artifact UI Capture Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -100);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(80, 91, 94, 255);
            camera.cullingMask = 1 << 5;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 200f;
            var target = new RenderTexture(1080, 2400, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1080, 2400, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1080, 2400), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                canvas.renderMode = oldMode;
                canvas.worldCamera = oldCamera;
                canvas.planeDistance = oldDistance;
                foreach (var pair in oldLayers) if (pair.Key != null) pair.Key.layer = pair.Value;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
