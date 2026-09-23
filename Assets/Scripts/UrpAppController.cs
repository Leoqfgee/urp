using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Urp.ArDemo
{
    public sealed class UrpAppController : MonoBehaviour
    {
        private enum Page
        {
            Home,
            ArDisplayMenu,
            Selection,
            Resource,
            Introduction,
            RepairExplanation,
            Tracking
        }

        [SerializeField] private Font chineseFont;
        [SerializeField] private RestorationObjectCatalog catalog;
        [SerializeField] private OrbImageTrackingController orbTracker;
        [SerializeField] private RepairOverlayController repairController;
        [SerializeField] private ModelViewerController modelViewer;
        [SerializeField] private ARSession arSession;
        [SerializeField] private Camera arCamera;
        [SerializeField] private ARCameraManager arCameraManager;
        [SerializeField] private ARCameraBackground arCameraBackground;
        [SerializeField] private AROcclusionManager arOcclusionManager;

        private readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
        private Canvas canvas;
        private Transform safeArea;
        private Transform modalLayer;
        private GameObject fullScreenBackground;
        private GameObject trackingTopChrome;
        private GameObject trackingBottomChrome;
        private RawImage introductionImage;
        private Text introductionName;
        private Text introductionBody;
        private RawImage repairBeforeImage;
        private RawImage repairAfterImage;
        private Text trackingStatus;
        private Image trackingStatusBackground;
        private Image trackingStatusDot;
        private string lastTrackingStatusValue;
        private Page currentPage;
        private Page informationReturnPage = Page.Resource;
        private Page selectionDestination = Page.Resource;
        private RestorationObjectProfile selectedProfile;
        private Button damagedButton;
        private Button completeButton;
        private Button arBeforeButton;
        private Button arAfterButton;
        private Coroutine arActivationRoutine;
        public static bool ReturnToArDisplayMenu;

        private static readonly Color Ink = new Color32(24, 50, 67, 255);
        private static readonly Color Muted = new Color32(100, 116, 126, 255);
        private static readonly Color Accent = new Color32(35, 122, 108, 255);
        private static readonly Color AccentSoft = new Color32(226, 241, 237, 255);
        private static readonly Color HeritageGold = new Color32(190, 142, 65, 255);
        private static readonly Color HeritageGoldSoft = new Color32(224, 207, 174, 255);
        private static readonly Color Surface = new Color32(246, 242, 232, 255);
        private static readonly Color Card = new Color32(255, 253, 248, 246);
        private static Sprite roundedSprite;

        private void Awake()
        {
            BuildInterface();
            selectedProfile = catalog != null && catalog.objects.Length > 0
                ? catalog.objects[0]
                : null;
            bool openArMenu = ReturnToArDisplayMenu;
            ReturnToArDisplayMenu = false;
            ShowPage(openArMenu ? Page.ArDisplayMenu : Page.Home);
        }

        private void Update()
        {
            RefreshTrackingStatusAppearance();
            if (Keyboard.current == null
                || !Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            switch (currentPage)
            {
                case Page.Resource:
                case Page.Tracking:
                    ShowPage(currentPage == Page.Tracking ? Page.ArDisplayMenu : Page.Selection);
                    break;
                case Page.Introduction:
                    ShowPage(informationReturnPage);
                    break;
                case Page.RepairExplanation:
                    ShowPage(Page.Resource);
                    break;
                case Page.Selection:
                case Page.ArDisplayMenu:
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
            CreateHeritageBackground(fullScreenBackground.transform);
            trackingTopChrome = CreatePanel(canvas.transform, "TrackingTopSystemBarCover",
                new Color32(248, 247, 243, 255), new Vector2(0f, 0.895f), Vector2.one, false);
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
            pages[Page.ArDisplayMenu] = BuildArDisplayMenuPage();
            pages[Page.Selection] = BuildSelectionPage();
            pages[Page.Resource] = BuildResourcePage();
            pages[Page.Introduction] = BuildIntroductionPage();
            pages[Page.RepairExplanation] = BuildRepairExplanationPage();
            pages[Page.Tracking] = BuildTrackingPage();

            orbTracker?.BindStatusText(trackingStatus);
            repairController?.BindStatusText(trackingStatus);
        }

        private GameObject BuildHomePage()
        {
            GameObject page = CreatePage("HomePageContent");
            Text homeTitle = CreateText(page.transform, "文化遗址\n数字修复与 AR 呈现", 52, Ink,
                new Vector2(0.07f, 0.70f), new Vector2(0.93f, 0.89f), TextAnchor.MiddleCenter);
            homeTitle.lineSpacing = 1.05f;
            CreateGeneratedImage(page.transform, "HomeTitleCloudDivider",
                "UI/ornament_cloud_divider_v57", new Vector2(0.22f, 0.655f),
                new Vector2(0.78f, 0.68f), new Rect(0f, 0.35f, 1f, 0.30f));

            Button resourceEntry = CreateButton(page.transform, "三维资源查看",
                new Vector2(0.055f, 0.49f), new Vector2(0.945f, 0.61f),
                () => OpenSelection(Page.Resource), Color.clear, Ink, 43);
            ApplyGeneratedSkin(resourceEntry.gameObject, "UI/button_home_ivory_v58");
            ConfigureFeatureButton(resourceEntry, "UI/icon_museum_v57",
                new Color32(135, 96, 43, 255));

            Button trackingEntry = CreateButton(page.transform, "AR实景展示",
                new Vector2(0.055f, 0.335f), new Vector2(0.945f, 0.455f),
                () => ShowPage(Page.ArDisplayMenu), Color.clear, Color.white, 43);
            ApplyGeneratedSkin(trackingEntry.gameObject, "UI/button_home_jade_v58");
            ConfigureFeatureButton(trackingEntry, "UI/icon_ar_scan_v57",
                new Color32(255, 235, 196, 255));
            return page;
        }

        private GameObject BuildArDisplayMenuPage()
        {
            GameObject page = CreatePage("ARDisplayMenuPageContent");
            CreateHeader(page.transform, "AR实景展示", () => ShowPage(Page.Home));
            Button artifact = CreateButton(page.transform, "文物实景展示",
                new Vector2(0.055f, 0.57f), new Vector2(0.945f, 0.68f),
                () => SceneManager.LoadScene("ArtifactARScene"), Color.clear, Ink, 42);
            ApplyGeneratedSkin(artifact.gameObject, "UI/button_home_ivory_v58");
            CreateText(page.transform, "将数字文物放置于现实空间中进行展示", 26, Muted,
                new Vector2(0.07f, 0.515f), new Vector2(0.93f, 0.565f), TextAnchor.MiddleCenter);
            Button overlay = CreateButton(page.transform, "虚实叠加展示",
                new Vector2(0.055f, 0.35f), new Vector2(0.945f, 0.46f),
                OpenExistingTracking, Color.clear, Color.white, 42);
            ApplyGeneratedSkin(overlay.gameObject, "UI/button_home_jade_v58");
            CreateText(page.transform, "识别实体目标并进行三维虚实叠加", 26, Muted,
                new Vector2(0.07f, 0.295f), new Vector2(0.93f, 0.345f), TextAnchor.MiddleCenter);
            return page;
        }

        private void OpenExistingTracking()
        {
            if (selectedProfile != null) SelectAndOpen(selectedProfile, Page.Tracking);
            else OpenSelection(Page.Tracking);
        }

        private GameObject BuildSelectionPage()
        {
            GameObject page = CreatePage("ObjectSelectionPageContent");
            CreateHeader(page.transform, "文物选择", () => ShowPage(Page.Home));

            int count = catalog == null ? 0 : catalog.objects.Length;
            Vector2 viewportMin = count == 1
                ? new Vector2(0.06f, 0.61f)
                : new Vector2(0.06f, 0.035f);
            Vector2 viewportMax = count == 1
                ? new Vector2(0.94f, 0.895f)
                : new Vector2(0.94f, 0.905f);
            GameObject viewport = CreatePanel(page.transform, "ObjectCardViewport", Color.clear,
                viewportMin, viewportMax, false);
            viewport.AddComponent<RectMask2D>();
            GameObject content = new GameObject("ObjectCardContent");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, count == 1
                ? 330f
                : Mathf.Max(700f, 36f + count * 300f
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
            ApplyRoundedAppearance(card);
            AddSoftShadow(card, new Color32(22, 49, 65, 24), new Vector2(0f, -8f));
            AddBorder(card, HeritageGoldSoft, new Vector2(2f, -2f));
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0f, 1f);
            cardRect.anchorMax = new Vector2(1f, 1f);
            cardRect.pivot = new Vector2(0.5f, 1f);
            cardRect.sizeDelta = new Vector2(0f, 300f);
            LayoutElement element = card.AddComponent<LayoutElement>();
            element.preferredHeight = 300f;
            element.minHeight = 300f;
            element.flexibleHeight = 0f;

            if (profile.thumbnail != null)
            {
                GameObject preview = new GameObject("Thumbnail");
                preview.transform.SetParent(card.transform, false);
                RectTransform rect = preview.AddComponent<RectTransform>();
                SetAnchors(rect, new Vector2(0.05f, 0.12f), new Vector2(0.40f, 0.88f));
                RawImage image = preview.AddComponent<RawImage>();
                image.texture = profile.thumbnail;
                // The reconstruction thumbnail is stored camera-upside-down.
                // Flip both UV axes (a 180-degree rotation) only in this card;
                // never rotate the tracking texture, model, or PnP frame.
                image.uvRect = new Rect(1f, 1f, -1f, -1f);
                image.raycastTarget = false;
            }

            CreateText(card.transform, profile.displayName, 38, Ink,
                new Vector2(0.44f, 0.18f), new Vector2(0.84f, 0.82f), TextAnchor.MiddleLeft);
            CreateText(card.transform, "›", 54, Accent,
                new Vector2(0.84f, 0.18f), new Vector2(0.96f, 0.82f), TextAnchor.MiddleCenter);

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
            SetAnchors(viewportRect, new Vector2(0.055f, 0.225f), new Vector2(0.945f, 0.91f));
            RawImage rawImage = viewport.AddComponent<RawImage>();
            rawImage.color = new Color32(248, 246, 239, 245);
            rawImage.raycastTarget = true;
            ApplyRoundedAppearance(viewport);
            AddBorder(viewport, HeritageGoldSoft, new Vector2(2f, -2f));
            AddSoftShadow(viewport, new Color32(22, 49, 65, 28), new Vector2(0f, -7f));
            ModelViewportInputHandler input = viewport.AddComponent<ModelViewportInputHandler>();
            input.Bind(modelViewer);
            modelViewer?.BindViewportImage(rawImage);

            GameObject segment = CreateRoundedPanel(page.transform, "ModelStateSegment",
                Color.clear, new Vector2(0.18f, 0.125f), new Vector2(0.82f, 0.205f), true);
            ApplyGeneratedSkin(segment, "UI/button_segment_v56");
            damagedButton = CreateButton(segment.transform, "修复前",
                new Vector2(0.075f, 0.22f), new Vector2(0.485f, 0.78f),
                ShowDamagedResource, Color.clear, Ink, 29);
            completeButton = CreateButton(segment.transform, "修复后",
                new Vector2(0.515f, 0.22f), new Vector2(0.925f, 0.78f),
                ShowCompleteResource, Color.clear, Muted, 29);
            ConfigureSegmentChoice(damagedButton);
            ConfigureSegmentChoice(completeButton);
            Button resetViewButton = CreateButton(page.transform, "重置视角",
                new Vector2(0.025f, 0.018f), new Vector2(0.33f, 0.115f),
                modelViewer != null ? modelViewer.ResetView : (Action)null, Color.clear, Ink, 28);
            ApplyGeneratedSkin(resetViewButton.gameObject, "UI/button_action_card_v58");
            ConfigureActionButton(resetViewButton, 0);
            Button introductionButton = CreateButton(page.transform, "文物介绍",
                new Vector2(0.3475f, 0.018f), new Vector2(0.6525f, 0.115f),
                ShowInformation, Color.clear, Ink, 28);
            ApplyGeneratedSkin(introductionButton.gameObject, "UI/button_action_card_v58");
            ConfigureActionButton(introductionButton, 1);
            Button explanationButton = CreateButton(page.transform, "修复说明",
                new Vector2(0.67f, 0.018f), new Vector2(0.975f, 0.115f),
                ShowRepairExplanation, Color.clear, Ink, 28);
            ApplyGeneratedSkin(explanationButton.gameObject, "UI/button_action_card_v58");
            ConfigureActionButton(explanationButton, 2);
            return page;
        }

        private GameObject BuildTrackingPage()
        {
            GameObject page = CreatePage("TrackingPageContent");
            CreateHeader(page.transform, "虚实叠加展示", () => ShowPage(Page.ArDisplayMenu));
            trackingStatus = CreateStatusBar(page.transform, "请将目标置于画面中央", 0.84f);

            GameObject segment = CreateRoundedPanel(page.transform, "ARRepairStateSegment",
                Color.clear, new Vector2(0.25f, 0.105f), new Vector2(0.75f, 0.175f), true);
            ApplyGeneratedSkin(segment, "UI/button_segment_v56");
            arBeforeButton = CreateButton(segment.transform, "修复前",
                new Vector2(0.075f, 0.22f), new Vector2(0.485f, 0.78f),
                () => SetArRepairVisible(false), Color.clear, Ink, 26);
            arAfterButton = CreateButton(segment.transform, "修复后",
                new Vector2(0.515f, 0.22f), new Vector2(0.925f, 0.78f),
                () => SetArRepairVisible(true), Color.clear, Muted, 26);
            ConfigureSegmentChoice(arBeforeButton);
            ConfigureSegmentChoice(arAfterButton);

            GameObject controls = CreateRoundedPanel(page.transform, "TrackingControls",
                Color.clear, new Vector2(0.02f, 0.005f), new Vector2(0.98f, 0.105f), true);
            ApplyGeneratedSkin(controls, "UI/button_ar_toolbar_v56");
            string[] labels =
            {
                "重新识别", "文物介绍", "退出 AR"
            };
            Action[] actions =
            {
                repairController != null ? repairController.ResetRecognition : (Action)null,
                ShowInformation,
                () => ShowPage(Page.ArDisplayMenu)
            };
            for (int i = 0; i < labels.Length; i++)
            {
                float left = 0.015f + i * 0.33f;
                Button actionButton = CreateButton(controls.transform, labels[i],
                    new Vector2(left, 0.14f), new Vector2(left + 0.31f, 0.86f),
                    actions[i], Color.clear, Ink, 27);
                ConfigureCompactActionButton(actionButton, i);
            }
            return page;
        }

        private GameObject BuildIntroductionPage()
        {
            GameObject page = CreatePage("IntroductionPageContent");
            CreateHeader(page.transform, "文物介绍", () => ShowPage(informationReturnPage));
            GameObject imageCard = CreateRoundedPanel(page.transform, "IntroductionImageCard", Card,
                new Vector2(0.07f, 0.50f), new Vector2(0.93f, 0.90f), false);
            AddBorder(imageCard, HeritageGoldSoft, new Vector2(2f, -2f));
            introductionImage = CreateRawImage(imageCard.transform, "IntroductionImage",
                new Vector2(0.035f, 0.04f), new Vector2(0.965f, 0.96f));
            introductionName = CreateText(page.transform, string.Empty, 38, Ink,
                new Vector2(0.08f, 0.405f), new Vector2(0.92f, 0.49f), TextAnchor.MiddleCenter);
            introductionBody = CreateText(page.transform, string.Empty, 30,
                new Color32(47, 54, 56, 255),
                new Vector2(0.08f, 0.055f), new Vector2(0.92f, 0.40f), TextAnchor.UpperLeft);
            return page;
        }

        private GameObject BuildRepairExplanationPage()
        {
            GameObject page = CreatePage("RepairExplanationPageContent");
            CreateHeader(page.transform, "修复说明", () => ShowPage(Page.Resource));
            GameObject beforeCard = CreateRoundedPanel(page.transform, "RepairBeforeCard", Card,
                new Vector2(0.05f, 0.58f), new Vector2(0.485f, 0.89f), false);
            AddBorder(beforeCard, HeritageGoldSoft, new Vector2(2f, -2f));
            repairBeforeImage = CreateRawImage(beforeCard.transform, "RepairBeforeImage",
                new Vector2(0.03f, 0.20f), new Vector2(0.97f, 0.97f));
            CreateText(beforeCard.transform, "修复前", 27, Ink,
                new Vector2(0.05f, 0.01f), new Vector2(0.95f, 0.20f), TextAnchor.MiddleCenter);

            GameObject afterCard = CreateRoundedPanel(page.transform, "RepairAfterCard", Card,
                new Vector2(0.515f, 0.58f), new Vector2(0.95f, 0.89f), false);
            AddBorder(afterCard, HeritageGoldSoft, new Vector2(2f, -2f));
            repairAfterImage = CreateRawImage(afterCard.transform, "RepairAfterImage",
                new Vector2(0.03f, 0.20f), new Vector2(0.97f, 0.97f));
            CreateText(afterCard.transform, "修复后", 27, Ink,
                new Vector2(0.05f, 0.01f), new Vector2(0.95f, 0.20f), TextAnchor.MiddleCenter);

            CreateText(page.transform, "修复方法", 32, Ink,
                new Vector2(0.08f, 0.49f), new Vector2(0.92f, 0.56f), TextAnchor.MiddleLeft);
            string[] methods = { "多视角图像重建", "缺失区域补全", "三维模型优化" };
            for (int index = 0; index < methods.Length; index++)
            {
                float top = 0.475f - index * 0.125f;
                GameObject row = CreateRoundedPanel(page.transform, "RepairMethod" + index, Card,
                    new Vector2(0.07f, top - 0.095f), new Vector2(0.93f, top), false);
                AddBorder(row, new Color32(224, 207, 174, 150), new Vector2(1f, -1f));
                CreatePanel(row.transform, "GoldLine", HeritageGold,
                    new Vector2(0.035f, 0.24f), new Vector2(0.043f, 0.76f), false);
                CreateText(row.transform, methods[index], 28, Ink,
                    new Vector2(0.08f, 0f), new Vector2(0.94f, 1f), TextAnchor.MiddleLeft);
            }
            return page;
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
                {
                    orbTracker?.HideFailedProfileVisuals();
                    if (trackingStatus != null)
                    {
                        trackingStatus.text = "对象资源加载失败";
                    }
                }
            }
        }

        private void OpenSelection(Page destination)
        {
            selectionDestination = destination == Page.Tracking ? Page.Tracking : Page.Resource;
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
            ConfigureArMode(tracking);
            modelViewer?.SetViewerEnabled(resource);
            orbTracker?.SetTrackingEnabled(tracking);
            if (tracking)
            {
                SetArRepairVisible(false);
            }
            if (resource)
            {
                ShowDamagedResource();
            }
            if (page == Page.Introduction)
            {
                RefreshIntroductionPage();
            }
            else if (page == Page.RepairExplanation)
            {
                RefreshRepairExplanationPage();
            }
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
                if (arOcclusionManager != null) arOcclusionManager.enabled = false;
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
            // Environment depth is not reliable on the glossy white bottle at
            // this short range. It classified the cap contact surface as real
            // foreground and swallowed the whole virtual repair part even
            // though B tracking was valid. Keep the manager in the scene for
            // future per-object depth policies, but do not feed that unstable
            // depth into the bottle-repair composition.
            if (arOcclusionManager != null) arOcclusionManager.enabled = false;
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

        private void SetArRepairVisible(bool visible)
        {
            repairController?.SetRepairVisible(visible);
            SetSelected(arBeforeButton, !visible);
            SetSelected(arAfterButton, visible);
        }

        private void ShowInformation()
        {
            if (selectedProfile == null) return;
            informationReturnPage = currentPage == Page.Tracking ? Page.Tracking : Page.Resource;
            ShowPage(Page.Introduction);
        }

        private void ShowRepairExplanation()
        {
            if (selectedProfile == null) return;
            ShowPage(Page.RepairExplanation);
        }

        private void RefreshIntroductionPage()
        {
            if (selectedProfile == null) return;
            introductionName.text = selectedProfile.displayName;
            introductionBody.text = selectedProfile.viewerDescription;
            introductionImage.texture = selectedProfile.introductionImage != null
                ? selectedProfile.introductionImage
                : selectedProfile.thumbnail;
            introductionImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            FitRawImage(introductionImage);
        }

        private void RefreshRepairExplanationPage()
        {
            if (selectedProfile == null) return;
            repairBeforeImage.texture = selectedProfile.repairBeforeImage != null
                ? selectedProfile.repairBeforeImage
                : selectedProfile.thumbnail;
            repairAfterImage.texture = selectedProfile.repairAfterImage != null
                ? selectedProfile.repairAfterImage
                : selectedProfile.introductionImage;
            repairBeforeImage.uvRect = selectedProfile.repairBeforeImage == selectedProfile.thumbnail
                ? new Rect(1f, 1f, -1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);
            repairAfterImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            FitRawImage(repairBeforeImage);
            FitRawImage(repairAfterImage);
        }

        private GameObject CreatePage(string name)
        {
            return CreatePanel(safeArea, name, Color.clear, Vector2.zero, Vector2.one, false);
        }

        private void CreateHeader(Transform parent, string title, Action backAction)
        {
            GameObject header = CreatePanel(parent, "Header", new Color32(249, 248, 244, 252),
                new Vector2(0f, 0.925f), new Vector2(1f, 1f), true);
            CreateButton(header.transform, "‹", new Vector2(0.01f, 0f), new Vector2(0.14f, 1f),
                backAction, new Color(1f, 1f, 1f, 0.001f), Ink, 50);
            CreateText(header.transform, title, 36, Ink,
                new Vector2(0.14f, 0.22f), new Vector2(0.94f, 0.96f), TextAnchor.MiddleCenter);
            CreateGeneratedImage(header.transform, "HeaderCloudDivider",
                "UI/ornament_cloud_divider_v57", new Vector2(0.24f, 0.00f),
                new Vector2(0.76f, 0.30f), new Rect(0f, 0.35f, 1f, 0.30f));
        }

        private Text CreateStatusBar(Transform parent, string value, float bottom)
        {
            GameObject bar = CreateRoundedPanel(parent, "TrackingStatus",
                new Color32(24, 50, 67, 225), new Vector2(0.12f, bottom),
                new Vector2(0.88f, bottom + 0.06f), false);
            trackingStatusBackground = bar.GetComponent<Image>();
            AddSoftShadow(bar, new Color32(0, 0, 0, 70), new Vector2(0f, -4f));

            GameObject dot = CreateRoundedPanel(bar.transform, "StatusDot", Color.white,
                new Vector2(0.055f, 0.34f), new Vector2(0.105f, 0.66f), false);
            trackingStatusDot = dot.GetComponent<Image>();
            return CreateText(bar.transform, value, 22, Color.white,
                new Vector2(0.12f, 0f), new Vector2(0.96f, 1f), TextAnchor.MiddleLeft);
        }

        private void ConfigureFeatureButton(Button button, string iconResourcePath, Color tint)
        {
            if (button == null) return;
            Text title = button.GetComponentInChildren<Text>();
            if (title != null)
            {
                SetAnchors(title.rectTransform, new Vector2(0.22f, 0f), new Vector2(0.88f, 1f));
            }
            RawImage icon = CreateGeneratedImage(button.transform, "FeatureIcon", iconResourcePath,
                new Vector2(0.055f, 0.12f), new Vector2(0.225f, 0.88f),
                new Rect(0f, 0f, 1f, 1f));
            if (icon != null) icon.color = tint;
        }

        private void ConfigureActionButton(Button button, int atlasIndex)
        {
            if (button == null) return;
            Text title = button.GetComponentInChildren<Text>();
            if (title != null)
            {
                SetAnchors(title.rectTransform, new Vector2(0.04f, 0.25f), new Vector2(0.96f, 0.50f));
            }
            CreateGeneratedImage(button.transform, "ActionIcon",
                "UI/icons_resource_actions_v57", new Vector2(0.36f, 0.56f),
                new Vector2(0.64f, 0.94f), AtlasCell(atlasIndex, 3));
        }

        private void ConfigureCompactActionButton(Button button, int atlasIndex)
        {
            if (button == null) return;
            Text title = button.GetComponentInChildren<Text>();
            if (title != null)
            {
                SetAnchors(title.rectTransform, new Vector2(0f, 0.29f), new Vector2(1f, 0.57f));
            }
            CreateGeneratedImage(button.transform, "ToolbarIcon",
                "UI/icons_ar_toolbar_v57", new Vector2(0.34f, 0.60f),
                new Vector2(0.66f, 0.96f), AtlasCell(atlasIndex, 3));
        }

        private void ConfigureSegmentChoice(Button button)
        {
            if (button == null) return;
            CreateRoundedPanel(button.transform, "SelectionIndicator", HeritageGold,
                new Vector2(0.27f, 0.02f), new Vector2(0.73f, 0.09f), false);
        }

        private static Rect AtlasCell(int index, int cellCount)
        {
            float width = 1f / Mathf.Max(1, cellCount);
            return new Rect(Mathf.Clamp(index, 0, cellCount - 1) * width, 0f, width, 1f);
        }

        private RawImage CreateGeneratedImage(Transform parent, string name,
            string resourcePath, Vector2 anchorMin, Vector2 anchorMax, Rect uvRect)
        {
            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                Debug.LogWarning($"Generated UI image not found: {resourcePath}");
                return null;
            }

            GameObject imageObject = new GameObject(name);
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.AddComponent<RectTransform>();
            SetAnchors(rect, anchorMin, anchorMax);
            RawImage image = imageObject.AddComponent<RawImage>();
            image.texture = texture;
            image.uvRect = uvRect;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private static void ApplyGeneratedSkin(GameObject target, string resourcePath)
        {
            if (target == null) return;
            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                Debug.LogWarning($"Generated UI skin not found: {resourcePath}");
                return;
            }

            Image baseImage = target.GetComponent<Image>();
            if (baseImage != null) baseImage.color = new Color(1f, 1f, 1f, 0.001f);
            GameObject skinObject = new GameObject("GeneratedSkin");
            skinObject.transform.SetParent(target.transform, false);
            skinObject.transform.SetAsFirstSibling();
            RectTransform rect = skinObject.AddComponent<RectTransform>();
            Stretch(rect);
            RawImage skin = skinObject.AddComponent<RawImage>();
            skin.texture = texture;
            skin.color = Color.white;
            skin.raycastTarget = false;
        }

        private void CreateHeritageBackground(Transform parent)
        {
            Texture2D texture = Resources.Load<Texture2D>("UI/heritage_ink_background_v55");
            if (texture == null) return;

            GameObject imageObject = new GameObject("HeritageInkBackground");
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.AddComponent<RectTransform>();
            Stretch(rect);
            RawImage image = imageObject.AddComponent<RawImage>();
            image.texture = texture;
            image.color = Color.white;
            image.raycastTarget = false;
            AspectRatioFitter fitter = imageObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = texture.width / (float)texture.height;
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

        private GameObject CreateRoundedPanel(Transform parent, string name, Color color,
            Vector2 anchorMin, Vector2 anchorMax, bool blocksRaycasts)
        {
            GameObject panel = CreatePanel(
                parent, name, color, anchorMin, anchorMax, blocksRaycasts);
            ApplyRoundedAppearance(panel);
            return panel;
        }

        private RawImage CreateRawImage(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject imageObject = new GameObject(name);
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.AddComponent<RectTransform>();
            SetAnchors(rect, anchorMin, anchorMax);
            RawImage image = imageObject.AddComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private static void FitRawImage(RawImage image)
        {
            if (image == null || image.texture == null) return;
            AspectRatioFitter fitter = image.GetComponent<AspectRatioFitter>()
                ?? image.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = image.texture.width / (float)image.texture.height;
        }

        private Button CreateButton(Transform parent, string label, Vector2 anchorMin,
            Vector2 anchorMax, Action action, Color background, Color foreground, int fontSize)
        {
            GameObject buttonObject = CreatePanel(parent, label + "Button", background,
                anchorMin, anchorMax, true);
            ApplyRoundedAppearance(buttonObject);
            Image graphic = buttonObject.GetComponent<Image>();
            graphic.raycastTarget = true;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.interactable = action != null;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (action != null) button.onClick.AddListener(() => action());
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.97f, 1f, 0.99f, 1f);
            colors.pressedColor = new Color(0.86f, 0.94f, 0.92f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.8f, 0.82f, 0.82f, 0.5f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
            CreateText(buttonObject.transform, label, fontSize, foreground,
                Vector2.zero, Vector2.one, TextAnchor.MiddleCenter);
            return button;
        }

        private static void ApplyRoundedAppearance(GameObject target)
        {
            Image image = target != null ? target.GetComponent<Image>() : null;
            if (image == null) return;
            image.sprite = GetRoundedSprite();
            image.type = Image.Type.Sliced;
        }

        private static void AddSoftShadow(GameObject target, Color color, Vector2 distance)
        {
            if (target == null || target.GetComponent<Graphic>() == null) return;
            Shadow shadow = target.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = distance;
            shadow.useGraphicAlpha = true;
        }

        private static void AddBorder(GameObject target, Color color, Vector2 distance)
        {
            if (target == null || target.GetComponent<Graphic>() == null) return;
            Outline outline = target.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = distance;
            outline.useGraphicAlpha = true;
        }

        private static Sprite GetRoundedSprite()
        {
            if (roundedSprite != null) return roundedSprite;

            const int size = 64;
            const float radius = 15f;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Runtime Rounded UI",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float cornerX = Mathf.Min(x + 0.5f, size - x - 0.5f);
                    float cornerY = Mathf.Min(y + 0.5f, size - y - 0.5f);
                    float dx = Mathf.Max(0f, radius - cornerX);
                    float dy = Mathf.Max(0f, radius - cornerY);
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    byte alpha = (byte)Mathf.RoundToInt(
                        255f * Mathf.Clamp01(radius + 0.75f - distance));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            roundedSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(16f, 16f, 16f, 16f));
            roundedSprite.name = "Runtime Rounded UI Sprite";
            roundedSprite.hideFlags = HideFlags.HideAndDontSave;
            return roundedSprite;
        }

        private void RefreshTrackingStatusAppearance()
        {
            if (trackingStatus == null
                || trackingStatusBackground == null
                || trackingStatus.text == lastTrackingStatusValue)
            {
                return;
            }

            lastTrackingStatusValue = trackingStatus.text;
            Color background = new Color32(24, 50, 67, 225);
            Color dot = new Color32(132, 164, 177, 255);
            if (trackingStatus.text == "跟踪中")
            {
                background = new Color32(23, 91, 78, 225);
                dot = new Color32(102, 225, 179, 255);
            }
            else if (trackingStatus.text == "已恢复跟踪")
            {
                background = new Color32(25, 92, 128, 225);
                dot = new Color32(117, 211, 244, 255);
            }
            else if (trackingStatus.text.Contains("丢失"))
            {
                background = new Color32(126, 57, 48, 230);
                dot = new Color32(255, 170, 147, 255);
            }
            else if (trackingStatus.text.Contains("调整")
                     || trackingStatus.text.Contains("完整"))
            {
                background = new Color32(124, 89, 37, 230);
                dot = new Color32(255, 205, 105, 255);
            }
            trackingStatusBackground.color = background;
            if (trackingStatusDot != null) trackingStatusDot.color = dot;
        }

        private static void SetSelected(Button button, bool selected)
        {
            if (button == null) return;
            Image image = button.targetGraphic as Image;
            if (image != null) image.color = Color.clear;
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = selected ? Ink : Muted;
                label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
            }
            Transform indicator = button.transform.Find("SelectionIndicator");
            if (indicator != null) indicator.gameObject.SetActive(selected);
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
