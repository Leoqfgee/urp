using System;
using System.Collections.Generic;
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
        private float lastPinchDistance;
        private static readonly Color Ink = new Color32(24, 50, 67, 255);
        private static readonly Color Surface = new Color32(249, 248, 244, 245);

        private async void Start()
        {
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            BuildUi();
            status.text = "请缓慢移动手机，扫描周围环境";
            if (artifact == null || string.IsNullOrEmpty(artifact.streamingAssetsModelPath))
            {
                status.text = "文物资源未配置";
                Debug.LogError("ArtifactARScene has no ArtifactInfo model path.");
                return;
            }
            string url = Application.streamingAssetsPath.TrimEnd('/') + "/"
                + artifact.streamingAssetsModelPath.TrimStart('/');
            importer = new GltfImport();
            bool loaded = await importer.Load(url);
            if (!loaded || this == null)
            {
                if (this != null) status.text = "文物模型加载失败";
                Debug.LogError("Could not load bundled artifact GLB: " + url);
                return;
            }
            modelRoot = new GameObject("ShengDing_AR_Root");
            GameObject visual = new GameObject("ShengDing_Model");
            modelVisual = visual.transform;
            modelVisual.SetParent(modelRoot.transform, false);
            if (!await importer.InstantiateMainSceneAsync(modelVisual) || this == null)
            {
                if (this != null) status.text = "文物模型实例化失败";
                Debug.LogError("Bundled artifact GLB scene could not be instantiated.");
                return;
            }
            Bounds bounds = VisibleBoundsInModelSpace();
            if (bounds.size.y <= 0.00001f)
            {
                status.text = "文物模型尺寸无效";
                Debug.LogError("Artifact GLB has no visible renderer bounds.");
                return;
            }
            baseHeight = bounds.size.y;
            displayedHeight = Mathf.Clamp(artifact.defaultHeight, 0.12f, 0.40f);
            modelVisual.localPosition = new Vector3(0f, -bounds.min.y, 0f);
            modelRoot.transform.localScale = Vector3.one * (displayedHeight / baseHeight);
            modelRoot.SetActive(false);
            modelReady = true;
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
                if (planeFound != hadPlane)
                {
                    hadPlane = planeFound;
                    status.text = planeFound ? "点击平面放置文物" : "请缓慢移动手机，扫描周围环境";
                }
            }
            if (!modelReady || informationPanel.activeSelf || Touchscreen.current == null) return;
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
                if (first.press.wasPressedThisFrame && !OverUi(first)
                    && raycastManager.Raycast(first.position.ReadValue(), hits, TrackableType.PlaneWithinPolygon))
                {
                    ARPlane plane = planeManager.GetPlane(hits[0].trackableId);
                    if (plane != null && plane.alignment == PlaneAlignment.HorizontalUp)
                        Place(hits[0].pose);
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

        private void Place(Pose pose)
        {
            if (anchorManager == null || !anchorManager.enabled)
            {
                status.text = "放置失败，请重新点击平面";
                return;
            }
            GameObject anchorObject = new GameObject("ShengDing AR Anchor");
            anchorObject.transform.SetPositionAndRotation(pose.position, pose.rotation);
            anchor = anchorObject.AddComponent<ARAnchor>();
            modelRoot.transform.SetParent(anchor.transform, false);
            modelRoot.transform.localPosition = Vector3.zero;
            modelRoot.transform.localRotation = Quaternion.identity;
            modelRoot.SetActive(true);
            placed = true;
            status.text = "文物已放置";
            replaceButton.interactable = true;
            informationButton.interactable = true;
            planeManager.enabled = false;
        }

        private void Replace()
        {
            if (anchor != null)
            {
                modelRoot.transform.SetParent(null, false);
                Destroy(anchor.gameObject);
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
            status.text = "点击平面放置文物";
        }

        private void BackToMenu()
        {
            UrpAppController.ReturnToArDisplayMenu = true;
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
            Panel(safe.transform, "Header", Surface, new Vector2(0, .925f), Vector2.one);
            Button back = Button(safe.transform, "‹", new Vector2(.015f,.93f), new Vector2(.15f,.995f), BackToMenu);
            back.GetComponentInChildren<Text>().fontSize = 52;
            Text title = Label(safe.transform, "文物实景展示", new Vector2(.2f,.93f), new Vector2(.8f,.995f), 36, Ink);
            title.alignment = TextAnchor.MiddleCenter;
            status = Label(safe.transform, "", new Vector2(.12f,.82f), new Vector2(.88f,.89f), 27, Color.white);
            status.alignment = TextAnchor.MiddleCenter;
            status.gameObject.AddComponent<Outline>().effectColor = Ink;
            replaceButton = Button(safe.transform, "重新放置", new Vector2(.055f,.025f), new Vector2(.46f,.105f), Replace);
            informationButton = Button(safe.transform, "文物介绍", new Vector2(.54f,.025f), new Vector2(.945f,.105f), () => informationPanel.SetActive(true));
            replaceButton.interactable = false;
            informationButton.interactable = false;
            informationPanel = Panel(safe.transform, "Artifact Information", new Color32(249,248,244,250),
                new Vector2(.07f,.30f), new Vector2(.93f,.72f));
            Label(informationPanel.transform, artifact != null ? artifact.displayName : "青铜升鼎",
                new Vector2(.06f,.77f), new Vector2(.94f,.94f), 40, Ink);
            Label(informationPanel.transform, artifact != null ? artifact.period + " · " + artifact.category : "",
                new Vector2(.06f,.67f), new Vector2(.94f,.78f), 28, Ink);
            Text body = Label(informationPanel.transform, artifact != null ? artifact.description : "",
                new Vector2(.06f,.19f), new Vector2(.94f,.65f), 28, Ink);
            body.alignment = TextAnchor.UpperLeft;
            Button(informationPanel.transform, "关闭", new Vector2(.3f,.04f), new Vector2(.7f,.16f),
                () => informationPanel.SetActive(false));
            informationPanel.SetActive(false);
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
            button.onClick.AddListener(() => action());
            Text label = Label(obj.transform, value, Vector2.zero, Vector2.one, 30, Ink);
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }
    }
}
