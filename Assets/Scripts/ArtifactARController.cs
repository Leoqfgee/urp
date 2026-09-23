using System;
using System.Collections.Generic;
using System.Collections;
using GLTFast;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Urp.ArDemo
{
    // Runs only in ArtifactARScene. The bottle ORB scene and its tracking controller stay separate.
    public sealed class ArtifactARController : MonoBehaviour
    {
        [SerializeField] private ArtifactInfo artifact;
        [SerializeField] private Font chineseFont;
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private ARAnchorManager anchorManager;

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private GltfImport importer;
        private GameObject modelRoot;
        private Transform modelVisual;
        private ARAnchor anchor;
        private Text status;
        private Button replaceButton;
        private Button informationButton;
        private GameObject informationPanel;
        private float baseHeight;
        private float displayedHeight;
        private bool modelReady;
        private bool placed;
        private bool hadPlane;
        private bool loadFailed;
        private float lastPinchDistance;
        private static readonly Color Ink = new Color32(24, 50, 67, 255);
        private static readonly Color Surface = new Color32(249, 248, 244, 245);

        private async void Start()
        {
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            BuildUi();
            status.text = "请缓慢移动手机，扫描周围环境";
            StartCoroutine(RefreshStaticText());
            if (artifact == null || string.IsNullOrEmpty(artifact.streamingAssetsModelPath))
            {
                status.text = "文物资源未配置";
                Debug.LogError("ArtifactARScene has no ArtifactInfo model path.");
                return;
            }
            Debug.Log("[ArtifactAR] loading glb " + artifact.streamingAssetsModelPath);
            modelRoot = new GameObject("ShengDing_AR_Root");
            GameObject visual = new GameObject("ShengDing_Model");
            modelVisual = visual.transform;
            modelVisual.SetParent(modelRoot.transform, false);
            bool instantiated;
            try
            {
                if (artifact.importedViewerPrefab != null)
                {
                    Instantiate(artifact.importedViewerPrefab, modelVisual);
                    instantiated = true;
                    Debug.Log("[ArtifactAR] glb load success (project imported prefab)");
                }
                else
                {
                    importer = await ArtifactModelLoader.Load(artifact.streamingAssetsModelPath);
                    if (this == null) return;
                    Debug.Log("[ArtifactAR] glb load success (runtime)");
                    instantiated = await importer.InstantiateMainSceneAsync(modelVisual);
                }
            }
            catch (Exception exception) { Fail("GLB instantiate failed", exception); return; }
            if (this == null) return;
            if (!instantiated)
            {
                Fail("GLB instantiate failed"); return;
            }
            Renderer[] renderers = modelVisual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { Fail("GLB instantiate failed: zero renderers"); return; }
            Debug.Log("[ArtifactAR] instantiate success renderers=" + renderers.Length);
            Bounds bounds = VisibleBoundsInModelSpace();
            if (bounds.size.y <= 0.00001f)
            {
                status.text = "文物模型尺寸无效";
                Debug.LogError("[ArtifactAR][ERROR] invalid renderer bounds");
                return;
            }
            Debug.Log("[ArtifactAR] bounds=" + bounds);
            baseHeight = bounds.size.y;
            displayedHeight = Mathf.Clamp(artifact.defaultHeight, 0.12f, 0.40f);
            modelVisual.localPosition = new Vector3(0f, -bounds.min.y, 0f);
            modelRoot.transform.localScale = Vector3.one * (displayedHeight / baseHeight);
            Debug.Log("[ArtifactAR] final scale=" + modelRoot.transform.localScale.x + " height=" + displayedHeight);
            modelRoot.SetActive(false);
            modelReady = true;
        }

        private IEnumerator RefreshStaticText()
        {
            yield return null;
            if (status != null && status.canvas != null)
                foreach (Text label in status.canvas.GetComponentsInChildren<Text>(true)) label.SetAllDirty();
        }

        private Bounds VisibleBoundsInModelSpace()
        {
            bool any = false;
            Bounds result = default;
            foreach (Renderer renderer in modelVisual.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds world = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = world.center + Vector3.Scale(world.extents, new Vector3(x, y, z));
                    Vector3 local = modelVisual.InverseTransformPoint(corner);
                    if (!any) { result = new Bounds(local, Vector3.zero); any = true; }
                    else result.Encapsulate(local);
                }
            }
            return result;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (informationPanel.activeSelf) informationPanel.SetActive(false);
                else BackToMenu();
            }
            if (!placed)
            {
                bool planeFound = false;
                foreach (ARPlane plane in planeManager.trackables)
                    if (plane.trackingState == TrackingState.Tracking && plane.alignment == PlaneAlignment.HorizontalUp)
                    { planeFound = true; break; }
                if (planeFound != hadPlane || (planeFound && modelReady && status.text != "点击平面放置文物"))
                {
                    hadPlane = planeFound;
                    if (!loadFailed) status.text = planeFound && modelReady
                        ? "点击平面放置文物" : "请缓慢移动手机，扫描周围环境";
                }
            }
            if (informationPanel.activeSelf || Touchscreen.current == null) return;
            Touchscreen screen = Touchscreen.current;
            TouchControl first = null, second = null;
            foreach (TouchControl touch in screen.touches)
            {
                if (!touch.press.isPressed) continue;
                if (first == null) first = touch;
                else { second = touch; break; }
            }
            if (first == null) { lastPinchDistance = 0f; return; }
            if (!placed)
            {
                if (first.press.wasPressedThisFrame)
                {
                    Debug.Log("[ArtifactAR] touch received position=" + first.position.ReadValue());
                    if (OverUi(first)) { Debug.Log("[ArtifactAR] touch blocked by UI"); return; }
                    if (!modelReady) { Debug.LogError("[ArtifactAR][ERROR] touch before GLB ready"); return; }
                    if (!raycastManager.Raycast(first.position.ReadValue(), hits, TrackableType.PlaneWithinPolygon))
                    { Debug.LogError("[ArtifactAR][ERROR] raycast no plane hit"); return; }
                    Debug.Log("[ArtifactAR] raycast hit count=" + hits.Count + " id=" + hits[0].trackableId);
                    ARPlane plane = planeManager.GetPlane(hits[0].trackableId);
                    if (plane == null || plane.alignment != PlaneAlignment.HorizontalUp
                        || plane.trackingState != TrackingState.Tracking || plane.subsumedBy != null)
                    { Debug.LogError("[ArtifactAR][ERROR] raycast plane invalid"); return; }
                    Place(plane, hits[0].pose);
                }
                return;
            }
            if (second != null)
            {
                float distance = Vector2.Distance(first.position.ReadValue(), second.position.ReadValue());
                if (lastPinchDistance > 0f && !OverUi(first) && !OverUi(second))
                {
                    float next = Mathf.Clamp(displayedHeight * distance / lastPinchDistance, 0.12f, 0.40f);
                    displayedHeight = next;
                    modelRoot.transform.localScale = Vector3.one * (displayedHeight / baseHeight);
                }
                lastPinchDistance = distance;
            }
            else
            {
                lastPinchDistance = 0f;
                if (!OverUi(first) && !first.press.wasPressedThisFrame)
                {
                    float delta = first.delta.ReadValue().x;
                    modelRoot.transform.Rotate(Vector3.up, -delta * 0.25f, Space.Self);
                }
            }
        }

        private static bool OverUi(TouchControl touch)
        {
            if (EventSystem.current == null) return false;
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = touch.position.ReadValue()
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, results);
            return results.Count > 0;
        }

        private void Place(ARPlane plane, Pose pose)
        {
            if (anchorManager == null || !anchorManager.enabled)
            {
                FailPlacement("anchor manager unavailable");
                return;
            }
            try { anchor = anchorManager.AttachAnchor(plane, pose); }
            catch (Exception exception) { FailPlacement("anchor exception: " + exception); return; }
            if (anchor == null) { FailPlacement("anchor creation returned null"); return; }
            Debug.Log("[ArtifactAR] anchor created id=" + anchor.trackableId);
            modelRoot.transform.SetParent(anchor.transform, false);
            modelRoot.transform.localPosition = Vector3.zero;
            modelRoot.transform.localRotation = Quaternion.identity;
            modelRoot.SetActive(true);
            foreach (Renderer renderer in modelRoot.GetComponentsInChildren<Renderer>(true))
            { renderer.enabled = true; renderer.gameObject.layer = 0; }
            placed = true;
            status.text = "文物已放置";
            replaceButton.interactable = true;
            informationButton.interactable = true;
            planeManager.enabled = false;
            Debug.Log("[ArtifactAR] placement complete");
        }

        private void Replace()
        {
            if (anchor != null)
            {
                modelRoot.transform.SetParent(null, true);
                if (!anchorManager.RemoveAnchor(anchor))
                    Debug.LogError("[ArtifactAR][ERROR] anchor removal failed");
                anchor = null;
            }
            modelRoot.SetActive(false);
            modelRoot.transform.localRotation = Quaternion.identity;
            displayedHeight = Mathf.Clamp(artifact.defaultHeight, 0.12f, 0.40f);
            modelRoot.transform.localScale = Vector3.one * (displayedHeight / baseHeight);
            placed = false;
            hadPlane = false;
            lastPinchDistance = 0f;
            replaceButton.interactable = false;
            informationButton.interactable = false;
            planeManager.enabled = true;
            status.text = "请缓慢移动手机，扫描周围环境";
            Debug.Log("[ArtifactAR] placement reset");
        }

        private void Fail(string message, Exception exception = null)
        {
            loadFailed = true;
            if (status != null) status.text = "文物模型加载失败";
            Debug.LogError("[ArtifactAR][ERROR] " + message + (exception == null ? "" : ": " + exception));
        }

        private void FailPlacement(string message)
        {
            status.text = "放置失败，请重新扫描平面";
            Debug.LogError("[ArtifactAR][ERROR] " + message);
        }

        private void BackToMenu()
        {
            UrpAppController.ReturnToArtifactSelection = true;
            SceneManager.LoadScene("UrpARPrototype");
        }

        private void BuildUi()
        {
            GameObject canvasObject = new GameObject("Artifact AR UI");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2400);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            GameObject safe = new GameObject("SafeArea");
            safe.transform.SetParent(canvasObject.transform, false);
            RectTransform safeRect = safe.AddComponent<RectTransform>();
            safeRect.anchorMin = Vector2.zero; safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
            safe.AddComponent<SafeAreaFitter>();
            GameObject header = Panel(safe.transform, "Header", Surface, new Vector2(0, .94f), new Vector2(1f,1.02f));
            Button back = Button(header.transform, "‹", new Vector2(.015f,.04f), new Vector2(.15f,.96f), BackToMenu);
            back.GetComponent<Image>().color = new Color(1f, 1f, 1f, .001f);
            back.GetComponentInChildren<Text>().enabled = false;
            Skin(header.transform, "CloudDivider", "UI/ornament_cloud_divider_v57",
                new Vector2(.24f, 0f), new Vector2(.76f,.30f), new Rect(0f,.35f,1f,.30f));
            GameObject statusBar = Panel(safe.transform, "StatusBar", new Color32(24,50,67,225),
                new Vector2(.12f,.84f), new Vector2(.88f,.90f));
            status = Label(statusBar.transform, "", new Vector2(.12f,0f), new Vector2(.96f,1f), 23, Color.white);
            status.alignment = TextAnchor.MiddleLeft;
            Panel(statusBar.transform, "StatusDot", new Color32(216,184,113,255),
                new Vector2(.055f,.34f), new Vector2(.10f,.66f));
            GameObject toolbar = Panel(safe.transform, "ArtifactToolbar", Color.clear,
                new Vector2(.02f,.005f), new Vector2(.98f,.105f));
            Skin(toolbar.transform, "InkGoldToolbar", "UI/button_ar_toolbar_v56", Vector2.zero, Vector2.one,
                new Rect(0,0,1,1));
            replaceButton = Button(toolbar.transform, "重新放置", new Vector2(.035f,.14f), new Vector2(.49f,.86f), Replace);
            informationButton = Button(toolbar.transform, "文物介绍", new Vector2(.51f,.14f), new Vector2(.965f,.86f), () => informationPanel.SetActive(true));
            StyleToolbarButton(replaceButton, 0);
            StyleToolbarButton(informationButton, 1);
            replaceButton.GetComponentInChildren<Text>().enabled = false;
            informationButton.GetComponentInChildren<Text>().enabled = false;
            Label(toolbar.transform, "重新放置", new Vector2(.045f,.20f), new Vector2(.49f,.80f), 29, Ink);
            Label(toolbar.transform, "文物介绍", new Vector2(.51f,.20f), new Vector2(.955f,.80f), 29, Ink);
            replaceButton.interactable = false;
            informationButton.interactable = false;
            informationPanel = Panel(safe.transform, "Artifact Information", new Color32(249,248,244,250),
                new Vector2(.07f,.30f), new Vector2(.93f,.72f));
            Skin(informationPanel.transform, "PanelOrnament", "UI/ornament_cloud_divider_v57",
                new Vector2(.18f,.89f), new Vector2(.82f,.98f), new Rect(0f,.35f,1f,.30f));
            Label(informationPanel.transform, artifact != null ? artifact.displayName : "青铜升鼎",
                new Vector2(.06f,.77f), new Vector2(.94f,.94f), 40, Ink);
            Label(informationPanel.transform, artifact != null ? artifact.period + " · " + artifact.category : "",
                new Vector2(.06f,.67f), new Vector2(.94f,.78f), 28, Ink);
            Text body = Label(informationPanel.transform, artifact != null ? artifact.description : "",
                new Vector2(.06f,.19f), new Vector2(.94f,.65f), 28, Ink);
            body.alignment = TextAnchor.UpperLeft;
            Button close = Button(informationPanel.transform, "关闭", new Vector2(.3f,.04f), new Vector2(.7f,.16f),
                () => informationPanel.SetActive(false));
            close.GetComponent<Image>().color = new Color(1f, 1f, 1f, .001f);
            Skin(close.transform, "IvoryButton", "UI/button_home_ivory_v58", Vector2.zero, Vector2.one,
                new Rect(0,0,1,1));
            informationPanel.SetActive(false);
            Label(safe.transform, "文物实景展示", new Vector2(.2f,.948f), new Vector2(.8f,1.015f), 36, Ink);
            Label(safe.transform, "‹", new Vector2(.015f,.948f), new Vector2(.15f,1.015f), 52, Ink);
        }

        private static GameObject Panel(Transform parent, string name, Color color, Vector2 min, Vector2 max)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = obj.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return obj;
        }

        private Text Label(Transform parent, string value, Vector2 min, Vector2 max, int size, Color color)
        {
            GameObject obj = new GameObject(value);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Text label = obj.AddComponent<Text>();
            label.font = chineseFont;
            label.text = value;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            return label;
        }

        private Button Button(Transform parent, string value, Vector2 min, Vector2 max, Action action)
        {
            GameObject obj = Panel(parent, value + " Button", Surface, min, max);
            Button button = obj.AddComponent<Button>();
            obj.GetComponent<Image>().raycastTarget = true;
            button.onClick.AddListener(() => action());
            Text label = Label(obj.transform, value, Vector2.zero, Vector2.one, 30, Ink);
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private void StyleToolbarButton(Button button, int iconIndex)
        {
            button.GetComponent<Image>().color = new Color(1f, 1f, 1f, .001f);
            // The existing toolbar artwork supplies the gold separators. Chinese
            // action labels are overlaid on the toolbar itself to stay legible.
        }

        private static void Skin(Transform parent, string name, string path, Vector2 min, Vector2 max, Rect uv)
        {
            Texture2D texture = Resources.Load<Texture2D>(path);
            if (texture == null) { Debug.LogError("[ArtifactAR][ERROR] UI resource missing " + path); return; }
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.SetAsFirstSibling();
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            RawImage image = obj.AddComponent<RawImage>();
            image.texture = texture; image.uvRect = uv; image.raycastTarget = false;
        }
    }
}
