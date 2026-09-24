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
            frame++;
            if (frame != 50 && frame != 52) return;
            try
            {
                Canvas canvas = null;
                foreach (Canvas candidate in UnityEngine.Object.FindObjectsOfType<Canvas>(true))
                    if (candidate.name == "Artifact AR UI") canvas = candidate;
                if (canvas == null) throw new InvalidOperationException("Artifact AR canvas missing");
                if (frame == 50)
                {
                    ValidateInputAndButtons(canvas);
                    return;
                }
                EditorApplication.update -= Update;
                int roots = 0, anchors = 0;
                foreach (Transform transform in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                {
                    if (transform.name == "ShengDing_AR_Root") roots++;
                    if (transform.name == "ShengDing_Independent_ARAnchor") anchors++;
                }
                if (roots != 1 || anchors != 0)
                    throw new InvalidOperationException("Re-place did not clean the old model/anchor");
                Debug.Log("ARTIFACT_AR_REPLACE_CLEANUP_VALID roots=1 anchors=0");
                ValidateLayout(canvas);
                ValidateAspectRatios(canvas);
                Capture(canvas, "F:/Au/buildlogs/artifact_ar_ui_v64_1080x2400.png", 1080, 2400);
                Capture(canvas, "F:/Au/buildlogs/artifact_ar_ui_v64_1080x1920.png", 1080, 1920);
                Debug.Log("ARTIFACT_AR_UI_CAPTURE_OK targets=1080x2400,1080x1920");
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
            Capture(canvas, "F:/Au/buildlogs/artifact_ar_info_v64.png", 1080, 2400);
            close.onClick.Invoke();
            if (panel.activeSelf) throw new InvalidOperationException("Close button did not close panel");
            reset.onClick.Invoke();
            Debug.Log("ARTIFACT_AR_UI_ACTIONS_VALID");
        }

        private static void ValidateLayout(Canvas canvas)
        {
            RectTransform status = null, toolbar = null, header = null;
            foreach (RectTransform rect in canvas.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect.name == "StatusBar") status = rect;
                else if (rect.name == "ArtifactToolbar") toolbar = rect;
                else if (rect.name == "Header") header = rect;
            }
            if (status == null || toolbar == null || header == null)
                throw new InvalidOperationException("Artifact AR layout elements missing");
            Canvas.ForceUpdateCanvases();
            var statusCorners = new Vector3[4];
            var toolbarCorners = new Vector3[4];
            status.GetWorldCorners(statusCorners);
            toolbar.GetWorldCorners(toolbarCorners);
            if (toolbarCorners[1].y >= statusCorners[0].y || header.rect.height <= 0f
                || status.rect.width <= 0f || toolbar.rect.width <= 0f)
                throw new InvalidOperationException("Artifact AR controls overlap or have zero size");
            Debug.Log($"ARTIFACT_AR_UI_LAYOUT_VALID {Screen.width}x{Screen.height} "
                + $"status={status.rect.size} toolbar={toolbar.rect.size}");
        }

        private static void ValidateAspectRatios(Canvas canvas)
        {
            RectTransform status = null, toolbar = null;
            foreach (RectTransform rect in canvas.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect.name == "StatusBar") status = rect;
                else if (rect.name == "ArtifactToolbar") toolbar = rect;
            }
            if (status == null || toolbar == null)
                throw new InvalidOperationException("Status or toolbar missing");
            foreach (int height in new[] { 1920, 2400 })
            {
                float statusBottom = status.anchorMin.y * height;
                float statusTop = status.anchorMax.y * height;
                float toolbarTop = toolbar.anchorMax.y * height;
                float headerBottom = .938f * height; // Header occupies the top 6.2% in a full safe area.
                if (statusBottom <= toolbarTop || statusTop >= headerBottom)
                    throw new InvalidOperationException("Portrait UI regions overlap at 1080x" + height);
            }
            Debug.Log("ARTIFACT_AR_UI_ASPECT_RULES_VALID 1080x1920 1080x2400");
        }

        private static void Capture(Canvas canvas, string path, int width, int height)
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
            camera.aspect = (float)width / height;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
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
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
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
