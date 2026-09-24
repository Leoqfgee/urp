using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using EnhancedTouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

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
        private const float IndicatorRadius = .06f; // 12 cm diameter in world space.
        private const float IndicatorSmoothing = 12f;
        private GameObject modelRoot;
        private Transform modelVisual;
        private ARAnchor anchor;
        private Camera arCamera;
        private GameObject placementIndicator;
        private Pose placementPose;
        private bool hasPlacementPose;
        private Text status;
        private Button replaceButton;
        private Button informationButton;
        private GameObject informationPanel;
        private float baseHeight;
        private float displayedHeight;
        private bool modelReady;
        private bool placed;
        private bool loadFailed;
        private float lastPinchDistance;
        private float missFeedbackUntil;
        private Coroutine placedMessage;
        private RectTransform headerRect;
        private RectTransform headerBackRect;
        private RectTransform headerTitleRect;
        private RectTransform headerGlyphRect;
        private RectTransform headerDividerRect;
        private Rect lastHeaderSafeArea;
        private Vector2Int lastHeaderScreenSize;
        private static readonly Color Ink = new Color32(24, 50, 67, 255);
        private static readonly Color Surface = new Color32(249, 248, 244, 245);

        private void OnEnable() => EnhancedTouchSupport.Enable();
        private void OnDisable() => EnhancedTouchSupport.Disable();

        private void Start()
        {
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            arCamera = Camera.main;
            if (arCamera == null) Debug.LogError("[ArtifactAR][ERROR] AR camera unavailable");
            BuildPlacementIndicator();
            HidePlaneVisuals();
            BuildUi();
            status.text = "请缓慢移动手机，扫描可放置平面";
            StartCoroutine(RefreshStaticText());
            if (artifact == null || artifact.importedViewerPrefab == null)
            {
                status.text = "文物资源未配置";
                Debug.LogError("[ArtifactAR][ERROR] imported prefab missing from ArtifactInfo");
                return;
            }
            PrepareModel();
        }

        private void PrepareModel()
        {
            modelReady = false;
            loadFailed = false;
            Debug.Log("[ArtifactAR] loading imported prefab " + artifact.importedViewerPrefab.name);
            modelRoot = new GameObject("ShengDing_AR_Root");
            GameObject visual = new GameObject("ShengDing_Model");
            modelVisual = visual.transform;
            modelVisual.SetParent(modelRoot.transform, false);
            bool instantiated;
            try
            {
                instantiated = Instantiate(artifact.importedViewerPrefab, modelVisual) != null;
            }
            catch (Exception exception) { Fail("GLB instantiate failed", exception); return; }
            if (this == null) return;
            if (!instantiated)
            {
                Fail("GLB instantiate failed"); return;
            }
            Renderer[] renderers = modelVisual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { Fail("GLB instantiate failed: zero renderers"); return; }
            Debug.Log("[ArtifactAR] prefab instantiated");
            Debug.Log("[ArtifactAR] renderer count=" + renderers.Length);
            Bounds bounds = VisibleBoundsInModelSpace();
            if (bounds.size.y <= 0.00001f)
            {
                status.text = "文物模型尺寸无效";
                Debug.LogError("[ArtifactAR][ERROR] invalid renderer bounds");
                return;
            }
            Debug.Log("[ArtifactAR] model bounds=" + bounds);
            baseHeight = bounds.size.y;
            displayedHeight = Mathf.Clamp(artifact.defaultHeight, 0.12f, 0.40f);
            modelVisual.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            modelRoot.transform.localScale = Vector3.one * (displayedHeight / baseHeight);
            Debug.Log("[ArtifactAR] model scale=" + modelRoot.transform.localScale.x + " height=" + displayedHeight);
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
            if (headerRect != null && (lastHeaderSafeArea != Screen.safeArea
                || lastHeaderScreenSize.x != Screen.width || lastHeaderScreenSize.y != Screen.height))
                LayoutHeader();
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (informationPanel.activeSelf) informationPanel.SetActive(false);
                else BackToMenu();
            }
            if (!placed) UpdatePlacementIndicator();
            var activeTouches = EnhancedTouch.activeTouches;
            EnhancedTouch? first = null, second = null;
            foreach (EnhancedTouch touch in activeTouches)
            {
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended
                    || touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled) continue;
                if (first == null) first = touch;
                else { second = touch; break; }
            }
            if (first == null) { lastPinchDistance = 0f; return; }
            EnhancedTouch primary = first.Value;
            if (primary.phase == UnityEngine.InputSystem.TouchPhase.Began)
                Debug.Log("[ArtifactAR] touch received position=" + primary.screenPosition);
            if (informationPanel.activeSelf) return;
            if (!placed)
            {
                if (primary.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    if (OverUi(primary.screenPosition)) { Debug.Log("[ArtifactAR] touch blocked by UI"); return; }
                    if (!modelReady) { Debug.LogError("[ArtifactAR][ERROR] touch before prefab ready"); return; }
                    TryPlaceAt();
                }
                return;
            }
            if (second != null)
            {
                float distance = Vector2.Distance(primary.screenPosition, second.Value.screenPosition);
                if (lastPinchDistance > 0f && !OverUi(primary.screenPosition) && !OverUi(second.Value.screenPosition))
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
                if (!OverUi(primary.screenPosition) && primary.phase == UnityEngine.InputSystem.TouchPhase.Moved)
                {
                    float delta = primary.delta.x;
                    modelRoot.transform.Rotate(Vector3.up, -delta * 0.25f, Space.Self);
                }
            }
        }

        private void BuildPlacementIndicator()
        {
            Material material = Resources.Load<Material>("Materials/ArtifactPlacementIndicator");
            if (material == null)
            {
                Debug.LogError("[ArtifactAR][ERROR] placement indicator material unavailable");
                return;
            }
            placementIndicator = new GameObject("PlacementIndicator");
            var filter = placementIndicator.AddComponent<MeshFilter>();
            var renderer = placementIndicator.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            const int segments = 48;
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * IndicatorRadius;
                vertices[i * 2 + 1] = direction * (IndicatorRadius - .008f);
                int next = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = next * 2;
                triangles[t + 2] = i * 2 + 1;
                triangles[t + 3] = i * 2 + 1;
                triangles[t + 4] = next * 2;
                triangles[t + 5] = next * 2 + 1;
            }
            var mesh = new Mesh { name = "Artifact Placement Ring" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            filter.sharedMesh = mesh;
            placementIndicator.SetActive(false);
        }

        private void UpdatePlacementIndicator()
        {
            hasPlacementPose = false;
            if (placementIndicator == null || arCamera == null || !modelReady || loadFailed)
            {
                if (placementIndicator != null) placementIndicator.SetActive(false);
                return;
            }
            Vector2 screenCenter = new Vector2(Screen.width * .5f, Screen.height * .5f);
            if (raycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
            {
                foreach (ARRaycastHit hit in hits)
                {
                    ARPlane plane = planeManager.GetPlane(hit.trackableId);
                    if (plane == null || plane.trackingState != TrackingState.Tracking
                        || plane.alignment != PlaneAlignment.HorizontalUp || plane.subsumedBy != null) continue;
                    placementPose = hit.pose;
                    hasPlacementPose = true;
                    break;
                }
            }
            if (hasPlacementPose)
            {
                Vector3 target = placementPose.position + Vector3.up * .003f;
                if (!placementIndicator.activeSelf) placementIndicator.transform.position = target;
                else placementIndicator.transform.position = Vector3.Lerp(
                    placementIndicator.transform.position, target,
                    1f - Mathf.Exp(-IndicatorSmoothing * Time.deltaTime));
                placementIndicator.transform.rotation = Quaternion.identity;
                placementIndicator.SetActive(true);
            }
            else placementIndicator.SetActive(false);
            if (Time.time >= missFeedbackUntil)
                status.text = hasPlacementPose ? "点击标记位置放置文物" : "请缓慢移动手机，扫描可放置平面";
        }

        private void HidePlaneVisuals()
        {
            foreach (ARPlane plane in planeManager.trackables)
            {
                foreach (Renderer renderer in plane.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                foreach (ARPlaneMeshVisualizer visualizer in plane.GetComponentsInChildren<ARPlaneMeshVisualizer>(true))
                    visualizer.enabled = false;
            }
            foreach (ARPointCloud pointCloud in FindObjectsOfType<ARPointCloud>())
                foreach (Renderer renderer in pointCloud.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        }

        private void TryPlaceAt()
        {
            if (hasPlacementPose) Place(placementPose);
            else status.text = "请缓慢移动手机，扫描可放置平面";
        }

        private static bool OverUi(Vector2 screenPosition)
        {
            if (EventSystem.current == null) return false;
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, results);
            return results.Count > 0;
        }

        private void Place(Pose pose)
        {
            if (anchorManager == null || !anchorManager.enabled)
            {
                FailPlacement("anchor manager unavailable");
                return;
            }
            // AR Foundation 5.1.6 registers a standalone world anchor when ARAnchor is added.
            // Keep only the raycast position; plane rotation refinements must not tilt the model.
            Vector3 forward = Vector3.ProjectOnPlane(arCamera.transform.forward, Vector3.up);
            Quaternion yaw = forward.sqrMagnitude > .0001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
            GameObject anchorObject = new GameObject("ShengDing_Independent_ARAnchor");
            var origin = anchorManager.GetComponent<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null) anchorObject.transform.SetParent(origin.TrackablesParent, true);
            anchorObject.transform.SetPositionAndRotation(pose.position, yaw);
            try { anchor = anchorObject.AddComponent<ARAnchor>(); }
            catch (Exception exception)
            {
                Destroy(anchorObject);
                FailPlacement("anchor exception: " + exception);
                return;
            }
            if (anchor == null || anchor.trackableId == TrackableId.invalidId)
            {
                Destroy(anchorObject);
                anchor = null;
                FailPlacement("world anchor registration failed");
                return;
            }
            Debug.Log("[ArtifactAR] anchor created id=" + anchor.trackableId);
            modelRoot.transform.SetParent(anchor.transform, false);
            modelRoot.transform.localPosition = Vector3.zero;
            modelRoot.transform.localRotation = Quaternion.identity;
            modelRoot.SetActive(true);
            foreach (Renderer renderer in modelRoot.GetComponentsInChildren<Renderer>(true))
            { renderer.enabled = true; renderer.gameObject.layer = 0; }
            // The one-time bounds calculation in PrepareModel already grounds the visual.
            placed = true;
            status.text = "文物已放置";
            placementIndicator.SetActive(false);
            hasPlacementPose = false;
            HidePlaneVisuals();
            Debug.Log("[ArtifactAR] placement complete");
            if (placedMessage != null) StopCoroutine(placedMessage);
            placedMessage = StartCoroutine(HidePlacedMessage());
        }

        private IEnumerator HidePlacedMessage()
        {
            yield return new WaitForSeconds(1f);
            if (placed && status != null) status.transform.parent.gameObject.SetActive(false);
            placedMessage = null;
        }

        private void Replace()
        {
            Debug.Log("[ArtifactAR] reset button clicked");
            if (placedMessage != null) { StopCoroutine(placedMessage); placedMessage = null; }
            status.transform.parent.gameObject.SetActive(true);
            if (modelRoot == null) return;
            if (anchor != null)
            {
                modelRoot.SetActive(false);
                Destroy(anchor.gameObject);
                anchor = null;
            }
            else Destroy(modelRoot);
            modelRoot = null;
            modelVisual = null;
            placed = false;
            hasPlacementPose = false;
            lastPinchDistance = 0f;
            missFeedbackUntil = 0f;
            planeManager.enabled = true;
            status.text = "请缓慢移动手机，扫描可放置平面";
            PrepareModel();
            UpdatePlacementIndicator();
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
            missFeedbackUntil = Time.time + 1f;
            Debug.LogError("[ArtifactAR][ERROR] " + message);
        }

        private void BackToMenu()
        {
            Debug.Log("[ArtifactAR] back button clicked");
            UrpAppController.ReturnToArtifactSelection = true;
            SceneManager.LoadScene("UrpARPrototype");
        }

        private void ShowInformation()
        {
            Debug.Log("[ArtifactAR] info button clicked");
            informationPanel.SetActive(true);
        }

        private void CloseInformation()
        {
            Debug.Log("[ArtifactAR] info close button clicked");
            informationPanel.SetActive(false);
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
            // Fill the inset above Screen.safeArea with the same header artwork.
            // Position all controls within the safe portion of that header.
            GameObject header = Panel(canvasObject.transform, "Header", Surface,
                new Vector2(0f, .92f), Vector2.one);
            headerRect = header.GetComponent<RectTransform>();
            Button back = Button(header.transform, "‹", new Vector2(.015f, 0f),
                new Vector2(.15f, 1f), BackToMenu);
            headerBackRect = back.GetComponent<RectTransform>();
            back.GetComponent<Image>().color = new Color(1f, 1f, 1f, .001f);
            back.GetComponentInChildren<Text>().enabled = false;
            Skin(header.transform, "CloudDivider", "UI/ornament_cloud_divider_v57",
                new Vector2(.24f, 0f), new Vector2(.76f, .30f), new Rect(0f,.35f,1f,.30f));
            headerDividerRect = header.transform.Find("CloudDivider")?.GetComponent<RectTransform>();
            headerTitleRect = Label(header.transform, "文物实景展示",
                new Vector2(.2f, .30f), new Vector2(.8f, 1f), 34, Ink).rectTransform;
            headerGlyphRect = Label(header.transform, "‹",
                new Vector2(.015f, .30f), new Vector2(.15f, 1f), 48, Ink).rectTransform;
            LayoutHeader();
            GameObject statusBar = Panel(safe.transform, "StatusBar", new Color32(24,50,67,198),
                new Vector2(.14f,.895f), new Vector2(.86f,.923f));
            status = Label(statusBar.transform, "", new Vector2(.12f,0f), new Vector2(.98f,1f), 21, Color.white);
            status.alignment = TextAnchor.MiddleLeft;
            Panel(statusBar.transform, "StatusDot", new Color32(216,184,113,255),
                new Vector2(.055f,.34f), new Vector2(.10f,.66f));
            GameObject toolbar = Panel(safe.transform, "ArtifactToolbar", Color.clear,
                new Vector2(.035f,.01f), new Vector2(.965f,.082f));
            Skin(toolbar.transform, "InkGoldToolbar", "UI/button_ar_toolbar_v56", Vector2.zero, Vector2.one,
                new Rect(0,0,1,1));
            replaceButton = Button(toolbar.transform, "重新放置", new Vector2(.035f,.14f), new Vector2(.49f,.86f), Replace);
            informationButton = Button(toolbar.transform, "文物介绍", new Vector2(.51f,.14f), new Vector2(.965f,.86f), ShowInformation);
            StyleToolbarButton(replaceButton, 0);
            StyleToolbarButton(informationButton, 1);
            replaceButton.GetComponentInChildren<Text>().enabled = false;
            informationButton.GetComponentInChildren<Text>().enabled = false;
            Label(toolbar.transform, "重新放置", new Vector2(.045f,.20f), new Vector2(.49f,.80f), 26, Ink);
            Label(toolbar.transform, "文物介绍", new Vector2(.51f,.20f), new Vector2(.955f,.80f), 26, Ink);
            informationPanel = Panel(safe.transform, "Artifact Information", new Color32(249,248,244,250),
                new Vector2(.07f,.35f), new Vector2(.93f,.67f));
            Skin(informationPanel.transform, "PanelOrnament", "UI/ornament_cloud_divider_v57",
                new Vector2(.18f,.89f), new Vector2(.82f,.98f), new Rect(0f,.35f,1f,.30f));
            Label(informationPanel.transform, artifact != null ? artifact.displayName : "青铜升鼎",
                new Vector2(.06f,.77f), new Vector2(.94f,.94f), 40, Ink);
            Label(informationPanel.transform, artifact != null ? artifact.period + " · " + artifact.category : "",
                new Vector2(.06f,.67f), new Vector2(.94f,.78f), 28, Ink);
            Text body = Label(informationPanel.transform, artifact != null ? artifact.description : "",
                new Vector2(.06f,.23f), new Vector2(.94f,.65f), 27, Ink);
            body.alignment = TextAnchor.UpperLeft;
            Button close = Button(informationPanel.transform, "关闭", new Vector2(.3f,.04f), new Vector2(.7f,.16f),
                CloseInformation);
            close.GetComponent<Image>().color = new Color(1f, 1f, 1f, .001f);
            Skin(close.transform, "IvoryButton", "UI/button_home_ivory_v58", Vector2.zero, Vector2.one,
                new Rect(0,0,1,1));
            informationPanel.SetActive(false);
        }

        private void LayoutHeader()
        {
            lastHeaderSafeArea = Screen.safeArea;
            lastHeaderScreenSize = new Vector2Int(Screen.width, Screen.height);
            float topInset = Mathf.Clamp01((Screen.height - Screen.safeArea.yMax) / Mathf.Max(1f, Screen.height));
            float headerHeight = topInset + .062f;
            float safePart = .062f / headerHeight;
            SetAnchors(headerRect, new Vector2(0f, 1f - headerHeight), Vector2.one);
            SetAnchors(headerBackRect, new Vector2(.015f, 0f), new Vector2(.15f, safePart));
            SetAnchors(headerTitleRect, new Vector2(.2f, safePart * .30f), new Vector2(.8f, safePart));
            SetAnchors(headerGlyphRect, new Vector2(.015f, safePart * .30f), new Vector2(.15f, safePart));
            if (headerDividerRect != null)
                SetAnchors(headerDividerRect, new Vector2(.24f, 0f), new Vector2(.76f, safePart * .30f));
        }

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
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
