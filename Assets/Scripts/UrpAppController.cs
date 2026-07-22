using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using Urp.ArDemo.Generated;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Urp.ArDemo
{
    public sealed class UrpAppController : MonoBehaviour
    {
        private enum Page { Home, Selection, Resource, Tracking }

        [SerializeField] private Font chineseFont;
        [SerializeField] private RestorationObjectCatalog catalog;
        [SerializeField] private OrbImageTrackingController orbTracker;
        [SerializeField] private RepairOverlayController repairController;
        [SerializeField] private ModelViewerController modelViewer;
        [SerializeField] private ARSession arSession;
        [SerializeField] private Camera arCamera;
        [SerializeField] private ARCameraManager arCameraManager;
        [SerializeField] private ARCameraBackground arCameraBackground;

        private readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
        private Canvas canvas;
        private Transform safeArea;
        private Transform modalLayer;
        private GameObject fullScreenBackground;
        private GameObject trackingTopChrome;
        private GameObject trackingBottomChrome;
        private GameObject developmentDebugPanel;
        private Text resourceStatus;
        private Text selectionInstruction;
        private Text trackingStatus;
        private Text trackingSubtitle;
        private GameObject infoModal;
        private Text infoTitle;
        private Text infoBody;
        private Page currentPage;
        private Page selectionDestination = Page.Resource;
        private RestorationObjectProfile selectedProfile;
        private Button damagedButton;
        private Button completeButton;
        private Coroutine arActivationRoutine;

        private static readonly Color Ink = new Color32(20, 48, 89, 255);
        private static readonly Color Muted = new Color32(82, 96, 118, 255);
        private static readonly Color Accent = new Color32(31, 91, 169, 255);
        private static readonly Color Surface = new Color32(246, 249, 253, 255);
        private static readonly Color Card = Color.white;

        private void Awake()
        {
            BuildInterface();
            selectedProfile = catalog != null && catalog.objects.Length > 0
                ? catalog.objects[0]
                : null;
            ShowPage(Page.Home);
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape))
            {
                return;
            }

            if (infoModal != null && infoModal.activeSelf)
            {
                CloseInformation();
                return;
            }

            switch (currentPage)
            {
                case Page.Resource:
                case Page.Tracking:
                    ShowPage(Page.Selection);
                    break;
                case Page.Selection:
                    ShowPage(Page.Home);
                    break;
                case Page.Home:
                    Application.Quit();
                    break;
            }
        }

        private void BuildInterface()
        {
            GameObject canvasObject = new GameObject("URP Application UI");
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 2400f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            fullScreenBackground = CreatePanel(canvas.transform, "FullScreenBackground", Surface,
                Vector2.zero, Vector2.one, false);
            trackingTopChrome = CreatePanel(canvas.transform, "TrackingTopSystemBarCover",
                new Color32(250, 252, 255, 255), new Vector2(0f, 0.895f), Vector2.one, false);
            trackingBottomChrome = CreatePanel(canvas.transform, "TrackingBottomSystemBarCover",
                new Color32(9, 16, 25, 235), Vector2.zero, new Vector2(1f, 0.025f), false);
            trackingTopChrome.SetActive(false);
            trackingBottomChrome.SetActive(false);

            GameObject safeAreaObject = new GameObject("SafeArea");
            safeAreaObject.transform.SetParent(canvas.transform, false);
            RectTransform safeRect = safeAreaObject.AddComponent<RectTransform>();
            Stretch(safeRect);
            safeAreaObject.AddComponent<SafeAreaFitter>();
            safeArea = safeAreaObject.transform;

            GameObject modalObject = new GameObject("ModalLayer");
            modalObject.transform.SetParent(canvas.transform, false);
            RectTransform modalRect = modalObject.AddComponent<RectTransform>();
            Stretch(modalRect);
            modalLayer = modalObject.transform;

            pages[Page.Home] = BuildHomePage();
            pages[Page.Selection] = BuildSelectionPage();
            pages[Page.Resource] = BuildResourcePage();
            pages[Page.Tracking] = BuildTrackingPage();
            infoModal = BuildInfoModal();

            modelViewer?.BindStatusText(resourceStatus);
            orbTracker?.BindStatusText(trackingStatus);
            repairController?.BindStatusText(trackingStatus);
        }

        private GameObject BuildHomePage()
        {
            GameObject page = CreatePage("HomePageContent");
            CreateText(page.transform, "文化遗址数字修复及 AR 呈现系统", 48, Ink,
                new Vector2(0.10f, 0.65f), new Vector2(0.90f, 0.83f), TextAnchor.MiddleCenter);
            CreateFixedButton(page.transform, "三维资源查看", 810f, 124f, 0f, 80f,
                () => OpenSelection(Page.Resource), Card, Ink, 36);
            CreateFixedButton(page.transform, "三维跟踪修复", 810f, 124f, 0f, -100f,
                () => OpenSelection(Page.Tracking), Card, Ink, 36);
            return page;
        }

        private GameObject BuildSelectionPage()
        {
            GameObject page = CreatePage("ObjectSelectionPageContent");
            CreateHeader(page.transform, "文物选择", () => ShowPage(Page.Home));
            selectionInstruction = CreateText(page.transform, "请选择要查看的文物", 24, Muted,
                new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.88f), TextAnchor.MiddleCenter);

            GameObject viewport = CreatePanel(page.transform, "ObjectCardViewport", Color.clear,
                new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.81f), false);
            viewport.AddComponent<RectMask2D>();
            GameObject content = new GameObject("ObjectCardContent");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            int count = catalog == null ? 0 : catalog.objects.Length;
            contentRect.sizeDelta = new Vector2(0f, Mathf.Max(700f, 36f + count * 390f
                + Mathf.Max(0, count - 1) * 28f));
            VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 18, 18);
            layout.spacing = 28f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            if (catalog != null && count > 0)
            {
                foreach (RestorationObjectProfile profile in catalog.objects)
                {
                    if (profile != null)
                    {
                        BuildObjectCard(content.transform, profile);
                    }
                }
            }
            else
            {
                BuildCatalogErrorCard(content.transform);
            }

            // Two cards fit on the phone without scrolling.  A ScrollRect on this
            // page used to win the Android touch gesture before the child Button
            // received PointerClick, making an otherwise visible card appear dead.
            // Only install scrolling when the catalog actually overflows.
            if (count > 2)
            {
                ScrollRect scroll = viewport.AddComponent<ScrollRect>();
                scroll.viewport = viewport.GetComponent<RectTransform>();
                scroll.content = contentRect;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            return page;
        }

        private void BuildObjectCard(Transform parent, RestorationObjectProfile profile)
        {
            GameObject card = CreatePanel(parent, profile.objectId + " Card", Card,
                Vector2.zero, Vector2.one, true);
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0f, 1f);
            cardRect.anchorMax = new Vector2(1f, 1f);
            cardRect.pivot = new Vector2(0.5f, 1f);
            cardRect.sizeDelta = new Vector2(0f, 390f);
            LayoutElement element = card.AddComponent<LayoutElement>();
            element.preferredHeight = 390f;
            element.minHeight = 390f;
            element.flexibleHeight = 0f;

            if (profile.thumbnail != null)
            {
                GameObject preview = new GameObject("Thumbnail");
                preview.transform.SetParent(card.transform, false);
                RectTransform rect = preview.AddComponent<RectTransform>();
                SetAnchors(rect, new Vector2(0.05f, 0.12f), new Vector2(0.40f, 0.88f));
                RawImage image = preview.AddComponent<RawImage>();
                image.texture = profile.thumbnail;
                image.uvRect = new Rect(0f, 0f, 1f, 1f);
                image.raycastTarget = false;
            }

            CreateText(card.transform, profile.displayName, 32, Ink,
                new Vector2(0.44f, 0.67f), new Vector2(0.94f, 0.88f), TextAnchor.MiddleLeft);
            CreateText(card.transform, $"缺失部位：{profile.missingPartName}", 22, Accent,
                new Vector2(0.44f, 0.52f), new Vector2(0.94f, 0.69f), TextAnchor.MiddleLeft);
            CreateText(card.transform, profile.shortDescription, 21, Muted,
                new Vector2(0.44f, 0.14f), new Vector2(0.94f, 0.53f), TextAnchor.UpperLeft);

            // Keep the tap target as the last child so it is the top-most UI
            // raycast target.  Text and thumbnails never participate in raycasts.
            Button tapTarget = CreateButton(card.transform, string.Empty,
                Vector2.zero, Vector2.one,
                () => SelectAndOpen(profile, selectionDestination),
                new Color(1f, 1f, 1f, 0.001f), Color.clear, 1);
            tapTarget.gameObject.name = profile.objectId + " Card Tap Target";
            tapTarget.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = tapTarget.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.001f);
            colors.highlightedColor = new Color(0.75f, 0.88f, 1f, 0.06f);
            colors.pressedColor = new Color(0.35f, 0.65f, 1f, 0.14f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = Color.clear;
            colors.colorMultiplier = 1f;
            tapTarget.colors = colors;
        }

        private void BuildCatalogErrorCard(Transform parent)
        {
            GameObject card = CreatePanel(parent, "Catalog Error Card", Card,
                Vector2.zero, Vector2.one, true);
            LayoutElement element = card.AddComponent<LayoutElement>();
            element.preferredHeight = 260f;
            element.minHeight = 260f;
            CreateText(card.transform, "文物目录未加载，请重新安装当前构建。",
                28, Ink, new Vector2(0.08f, 0.18f), new Vector2(0.92f, 0.82f),
                TextAnchor.MiddleCenter);
        }

        private GameObject BuildResourcePage()
        {
            GameObject page = CreatePage("ResourcePageContent");
            CreateHeader(page.transform, "三维资源查看", () => ShowPage(Page.Selection));
            GameObject viewport = new GameObject("ModelViewport");
            viewport.transform.SetParent(page.transform, false);
            RectTransform viewportRect = viewport.AddComponent<RectTransform>();
            SetAnchors(viewportRect, new Vector2(0.06f, 0.27f), new Vector2(0.94f, 0.87f));
            RawImage rawImage = viewport.AddComponent<RawImage>();
            rawImage.color = new Color32(235, 241, 248, 255);
            rawImage.raycastTarget = true;
            ModelViewportInputHandler input = viewport.AddComponent<ModelViewportInputHandler>();
            input.Bind(modelViewer);
            modelViewer?.BindViewportImage(rawImage);

            CreateText(page.transform, "单指拖动旋转，双指捏合缩放", 20, Muted,
                new Vector2(0.15f, 0.23f), new Vector2(0.85f, 0.27f), TextAnchor.MiddleCenter);
            resourceStatus = CreateText(page.transform, string.Empty, 18, Muted,
                new Vector2(0.10f, 0.20f), new Vector2(0.90f, 0.235f), TextAnchor.MiddleCenter);
            damagedButton = CreateButton(page.transform, "残缺模型",
                new Vector2(0.08f, 0.115f), new Vector2(0.48f, 0.19f),
                ShowDamagedResource, Accent, Color.white, 27);
            completeButton = CreateButton(page.transform, "完整模型",
                new Vector2(0.52f, 0.115f), new Vector2(0.92f, 0.19f),
                ShowCompleteResource, Card, Ink, 27);
            CreateButton(page.transform, "重置视角",
                new Vector2(0.08f, 0.025f), new Vector2(0.48f, 0.095f),
                modelViewer != null ? modelViewer.ResetView : (Action)null, Card, Ink, 25);
            CreateButton(page.transform, "文字介绍",
                new Vector2(0.52f, 0.025f), new Vector2(0.92f, 0.095f),
                ShowInformation, Card, Ink, 25);
            return page;
        }

        private GameObject BuildTrackingPage()
        {
            GameObject page = CreatePage("TrackingPageContent");
            CreateHeader(page.transform, "三维物体跟踪", () => ShowPage(Page.Selection));
            trackingSubtitle = CreateText(page.transform, string.Empty, 20, Color.white,
                new Vector2(0.52f, 0.872f), new Vector2(0.94f, 0.915f), TextAnchor.MiddleRight);
            trackingStatus = CreateStatusBar(page.transform, "请选择对象。", 0.805f);
            GameObject controls = CreatePanel(page.transform, "TrackingControls", Color.clear,
                new Vector2(0.025f, 0.405f), new Vector2(0.205f, 0.735f), false);
            string[] labels = { "开始", "重置", "文字介绍", "返回" };
            Action[] actions =
            {
                repairController != null ? repairController.StartRecognition : (Action)null,
                repairController != null ? repairController.ResetRecognition : (Action)null,
                ShowInformation,
                () => ShowPage(Page.Selection)
            };
            for (int i = 0; i < labels.Length; i++)
            {
                float top = 0.98f - i * 0.25f;
                CreateButton(controls.transform, labels[i],
                    new Vector2(0.02f, top - 0.18f), new Vector2(0.98f, top),
                    actions[i], new Color32(255, 255, 255, 235), Ink,
                    labels[i].Length > 2 ? 19 : 24);
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            CreateButton(page.transform, "诊断",
                new Vector2(0.825f, 0.035f), new Vector2(0.965f, 0.077f),
                ToggleDevelopmentDebugPanel, new Color32(25, 43, 67, 220), Color.white, 17);
            developmentDebugPanel = CreatePanel(page.transform, "DevelopmentDebugPanel",
                new Color32(12, 22, 32, 232), new Vector2(0.56f, 0.085f),
                new Vector2(0.97f, 0.43f), true);
            string[] debugLabels =
            {
                "检查 B+C 固定关系", "仅显示参考 B", "仅显示修复 C", "保存诊断帧", "返回 A→B 配准"
            };
            Action[] debugActions =
            {
                orbTracker != null ? orbTracker.ShowRegisteredPairDiagnostic : (Action)null,
                orbTracker != null ? orbTracker.DebugShowReferenceOnly : (Action)null,
                orbTracker != null ? orbTracker.DebugShowRepairOnly : (Action)null,
                orbTracker != null ? orbTracker.SaveTrackingFailureFrame : (Action)null,
                ReturnToPaperTrackingFlow
            };
            for (int i = 0; i < debugLabels.Length; i++)
            {
                float top = 0.97f - i * 0.14f;
                CreateButton(developmentDebugPanel.transform, debugLabels[i],
                    new Vector2(0.055f, top - 0.105f), new Vector2(0.945f, top),
                    debugActions[i], new Color32(40, 61, 88, 255), Color.white, 16);
            }
            CreateText(developmentDebugPanel.transform,
                $"流程：A 特征 → B 稳定位姿 → 锁定世界锚点 → 隐藏 B → 仅显示 C\n{BuildIdentity.Current.ShortText}", 11,
                new Color32(180, 210, 245, 255), new Vector2(0.045f, 0.015f),
                new Vector2(0.955f, 0.255f), TextAnchor.LowerLeft);
            developmentDebugPanel.SetActive(false);
#endif
            return page;
        }

        private void ToggleDevelopmentDebugPanel()
        {
            if (developmentDebugPanel != null)
                developmentDebugPanel.SetActive(!developmentDebugPanel.activeSelf);
        }

        private void ReturnToPaperTrackingFlow()
        {
            orbTracker?.ExitDiagnosticsToPaperFlow();
            if (developmentDebugPanel != null) developmentDebugPanel.SetActive(false);
        }

        private GameObject BuildInfoModal()
        {
            GameObject blocker = CreatePanel(modalLayer, "InformationModal",
                new Color32(10, 20, 32, 160), Vector2.zero, Vector2.one, true);
            GameObject card = CreatePanel(blocker.transform, "InformationCard", Card,
                new Vector2(0.08f, 0.14f), new Vector2(0.92f, 0.86f), true);
            infoTitle = CreateText(card.transform, string.Empty, 34, Ink,
                new Vector2(0.08f, 0.84f), new Vector2(0.92f, 0.96f), TextAnchor.MiddleCenter);
            GameObject viewport = CreatePanel(card.transform, "InformationViewport",
                new Color32(247, 249, 252, 255), new Vector2(0.07f, 0.20f),
                new Vector2(0.93f, 0.83f), true);
            viewport.AddComponent<RectMask2D>();
            GameObject content = new GameObject("InformationContent");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 1100f);
            infoBody = CreateText(content.transform, string.Empty, 25, new Color32(38, 50, 68, 255),
                Vector2.zero, Vector2.one, TextAnchor.UpperLeft);
            infoBody.verticalOverflow = VerticalWrapMode.Overflow;
            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            CreateButton(card.transform, "关闭",
                new Vector2(0.25f, 0.055f), new Vector2(0.75f, 0.15f),
                CloseInformation, Accent, Color.white, 28);
            blocker.SetActive(false);
            return blocker;
        }

        private void SelectAndOpen(RestorationObjectProfile profile, Page page)
        {
            selectedProfile = profile;
            // Navigation must be visible immediately.  Rebuilding the viewer mesh
            // and native ORB database can take long enough on Android to make a tap
            // look ignored, and an exception previously prevented ShowPage entirely.
            ShowPage(page);
            StartCoroutine(ApplySelectedProfileNextFrame(profile, page));
        }

        private IEnumerator ApplySelectedProfileNextFrame(
            RestorationObjectProfile profile, Page destination)
        {
            yield return null;
            try
            {
                if (destination == Page.Resource)
                {
                    // The resource viewer owns a separate off-screen model and
                    // render texture.  Never rebuild it while entering AR tracking.
                    modelViewer?.SetProfile(profile);
                    ShowDamagedResource();
                }
                else if (destination == Page.Tracking)
                {
                    // Tracking only needs B, C and the ORB database.  Rebuilding
                    // viewer objects here caused the MissingReferenceException
                    // seen on the device before the tracker could initialize.
                    orbTracker?.SetProfile(profile);
                    orbTracker?.SetTrackingEnabled(true);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (destination == Page.Tracking)
                    orbTracker?.HideFailedProfileVisuals();
                Text target = destination == Page.Tracking ? trackingStatus : resourceStatus;
                if (target != null)
                {
                    target.text = $"已进入页面，但对象资源加载失败：{exception.GetType().Name}：{exception.Message}";
                }
            }
        }

        private void OpenSelection(Page destination)
        {
            selectionDestination = destination == Page.Tracking ? Page.Tracking : Page.Resource;
            if (selectionInstruction != null)
            {
                selectionInstruction.text = selectionDestination == Page.Tracking
                    ? "请选择要跟踪修复的文物"
                    : "请选择要查看的文物";
            }
            ShowPage(Page.Selection);
        }

        private void ShowPage(Page page)
        {
            currentPage = page;
            foreach (KeyValuePair<Page, GameObject> pair in pages)
            {
                pair.Value.SetActive(pair.Key == page);
            }

            bool resource = page == Page.Resource;
            bool tracking = page == Page.Tracking;
            fullScreenBackground.SetActive(!tracking);
            trackingTopChrome?.SetActive(tracking);
            trackingBottomChrome?.SetActive(tracking);
            if (!tracking && developmentDebugPanel != null)
                developmentDebugPanel.SetActive(false);
            ConfigureArMode(tracking);
            modelViewer?.SetViewerEnabled(resource);
            orbTracker?.SetTrackingEnabled(tracking);
            if (tracking && trackingSubtitle != null)
            {
                trackingSubtitle.text = selectedProfile?.displayName ?? "未选择对象";
            }
            if (resource)
            {
                ShowDamagedResource();
            }
            infoModal?.SetActive(false);
            modelViewer?.SetGesturesBlocked(false);
        }

        private void ConfigureArMode(bool enabled)
        {
            if (arActivationRoutine != null)
            {
                StopCoroutine(arActivationRoutine);
                arActivationRoutine = null;
            }

            if (!enabled)
            {
                if (arCameraBackground != null) arCameraBackground.enabled = false;
                if (arCameraManager != null) arCameraManager.enabled = false;
                if (arCamera != null) arCamera.enabled = false;
                if (arSession != null) arSession.enabled = false;
                return;
            }

            arActivationRoutine = StartCoroutine(ActivateArCamera());
        }

        private IEnumerator ActivateArCamera()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                if (trackingStatus != null)
                    trackingStatus.text = "请允许相机权限以进入三维跟踪模式。";
                Permission.RequestUserPermission(Permission.Camera);
                float deadline = Time.realtimeSinceStartup + 20f;
                while (!Permission.HasUserAuthorizedPermission(Permission.Camera)
                       && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
                if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
                {
                    if (trackingStatus != null)
                        trackingStatus.text = "未获得相机权限，请在系统设置中允许后重试。";
                    arActivationRoutine = null;
                    yield break;
                }
            }
#endif
            if (currentPage != Page.Tracking)
            {
                arActivationRoutine = null;
                yield break;
            }

            if (arSession != null) arSession.enabled = true;
            yield return null;
            if (arCameraManager != null) arCameraManager.enabled = true;
            if (arCamera != null) arCamera.enabled = true;
            if (arCameraBackground != null) arCameraBackground.enabled = true;
            arActivationRoutine = null;
        }

        private void ShowDamagedResource()
        {
            modelViewer?.ShowDamagedModel();
            SetSelected(damagedButton, true);
            SetSelected(completeButton, false);
        }

        private void ShowCompleteResource()
        {
            modelViewer?.ShowCompleteModel();
            SetSelected(damagedButton, false);
            SetSelected(completeButton, true);
        }

        private void ShowInformation()
        {
            if (infoModal == null || selectedProfile == null) return;
            infoTitle.text = selectedProfile.displayName;
            string calibration = selectedProfile.physicalScaleVerified
                ? "物理比例：已验证。"
                : "物理比例与修复连接区域：尚未完成实物测量验证。";
            infoBody.text = currentPage == Page.Resource
                ? $"{selectedProfile.viewerDescription}\n\n操作：单指拖动旋转，双指捏合缩放。\n\n{calibration}"
                : currentPage == Page.Tracking
                    ? $"{selectedProfile.trackingDescription}\n\n{calibration}"
                    : $"{selectedProfile.shortDescription}\n\n缺失部位：{selectedProfile.missingPartName}\n\n{calibration}";
            infoModal.SetActive(true);
            infoModal.transform.SetAsLastSibling();
            modelViewer?.SetGesturesBlocked(true);
        }

        private void CloseInformation()
        {
            infoModal?.SetActive(false);
            modelViewer?.SetGesturesBlocked(false);
        }

        private GameObject CreatePage(string name)
        {
            return CreatePanel(safeArea, name, Color.clear, Vector2.zero, Vector2.one, false);
        }

        private void CreateHeader(Transform parent, string title, Action backAction)
        {
            GameObject header = CreatePanel(parent, "Header", new Color32(250, 252, 255, 252),
                new Vector2(0f, 0.92f), new Vector2(1f, 1f), true);
            CreateButton(header.transform, "‹", new Vector2(0.01f, 0f), new Vector2(0.14f, 1f),
                backAction, new Color(1f, 1f, 1f, 0.001f), Ink, 50);
            CreateText(header.transform, title, 32, Ink,
                new Vector2(0.14f, 0.08f), new Vector2(0.94f, 0.92f), TextAnchor.MiddleCenter);
        }

        private Text CreateStatusBar(Transform parent, string value, float bottom)
        {
            GameObject bar = CreatePanel(parent, "TrackingStatus",
                new Color32(12, 22, 32, 190), new Vector2(0.06f, bottom),
                new Vector2(0.94f, bottom + 0.065f), false);
            return CreateText(bar.transform, value, 20, Color.white,
                new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), TextAnchor.MiddleCenter);
        }

        private GameObject CreateFixedButton(Transform parent, string label, float width,
            float height, float x, float y, Action action, Color background,
            Color foreground, int fontSize)
        {
            GameObject buttonObject = CreateButton(parent, label,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                action, background, foreground, fontSize).gameObject;
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);
            return buttonObject;
        }

        private GameObject CreatePanel(Transform parent, string name, Color color,
            Vector2 anchorMin, Vector2 anchorMax, bool blocksRaycasts)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.AddComponent<RectTransform>();
            SetAnchors(rect, anchorMin, anchorMax);
            Image image = panel.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = blocksRaycasts;
            return panel;
        }

        private Button CreateButton(Transform parent, string label, Vector2 anchorMin,
            Vector2 anchorMax, Action action, Color background, Color foreground, int fontSize)
        {
            GameObject buttonObject = CreatePanel(parent, label + "Button", background,
                anchorMin, anchorMax, true);
            Image graphic = buttonObject.GetComponent<Image>();
            graphic.raycastTarget = true;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.interactable = action != null;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (action != null) button.onClick.AddListener(() => action());
            CreateText(buttonObject.transform, label, fontSize, foreground,
                Vector2.zero, Vector2.one, TextAnchor.MiddleCenter);
            return button;
        }

        private static void SetSelected(Button button, bool selected)
        {
            if (button == null) return;
            Image image = button.targetGraphic as Image;
            if (image != null) image.color = selected ? Accent : Card;
            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = selected ? Color.white : Ink;
        }

        private Text CreateText(Transform parent, string value, int fontSize, Color color,
            Vector2 anchorMin, Vector2 anchorMax, TextAnchor alignment)
        {
            GameObject textObject = new GameObject("Text");
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.AddComponent<RectTransform>();
            SetAnchors(rect, anchorMin, anchorMax);
            rect.offsetMin = new Vector2(12f, 8f);
            rect.offsetMax = new Vector2(-12f, -8f);
            Text text = textObject.AddComponent<Text>();
            text.text = value;
            text.font = chineseFont != null ? chineseFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Stretch(RectTransform rect)
        {
            SetAnchors(rect, Vector2.zero, Vector2.one);
        }
    }
}
