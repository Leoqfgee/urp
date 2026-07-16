using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Urp.ArDemo
{
    public sealed class UrpAppController : MonoBehaviour
    {
        private enum Page
        {
            Home,
            Resource,
            TrackingSelection,
            TrackingCamera
        }

        [SerializeField] private Font chineseFont;
        [SerializeField] private Texture2D objectThumbnail;
        [SerializeField] private OrbImageTrackingController orbTracker;
        [SerializeField] private RepairOverlayController repairController;
        [SerializeField] private ModelViewerController modelViewer;

        private readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
        private Canvas canvas;
        private Transform safeArea;
        private Text resourceStatus;
        private Text trackingStatus;
        private GameObject infoModal;
        private Text infoTitle;
        private Text infoBody;
        private Page currentPage;
        private Button damagedButton;
        private Button completeButton;

        private static readonly Color Ink = new Color32(20, 48, 89, 255);
        private static readonly Color Muted = new Color32(82, 96, 118, 255);
        private static readonly Color Accent = new Color32(31, 91, 169, 255);
        private static readonly Color Surface = new Color32(248, 250, 253, 255);
        private static readonly Color Card = Color.white;

        private void Awake()
        {
            BuildInterface();
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
                infoModal.SetActive(false);
                return;
            }

            switch (currentPage)
            {
                case Page.Resource:
                case Page.TrackingSelection:
                    ShowPage(Page.Home);
                    break;
                case Page.TrackingCamera:
                    ShowPage(Page.TrackingSelection);
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
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject safeAreaObject = new GameObject("Safe Area");
            safeAreaObject.transform.SetParent(canvas.transform, false);
            RectTransform safeRect = safeAreaObject.AddComponent<RectTransform>();
            safeRect.anchorMin = Vector2.zero;
            safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = Vector2.zero;
            safeRect.offsetMax = Vector2.zero;
            safeAreaObject.AddComponent<SafeAreaFitter>();
            safeArea = safeAreaObject.transform;

            pages[Page.Home] = BuildHomePage();
            pages[Page.Resource] = BuildResourcePage();
            pages[Page.TrackingSelection] = BuildTrackingSelectionPage();
            pages[Page.TrackingCamera] = BuildTrackingCameraPage();
            infoModal = BuildInfoModal();

            modelViewer?.BindStatusText(resourceStatus);
            orbTracker?.BindStatusText(trackingStatus);
            repairController?.BindStatusText(trackingStatus);
        }

        private GameObject BuildHomePage()
        {
            GameObject page = CreatePage("首页", true);
            CreateText(page.transform, "文化遗址数字修复及 AR 呈现系统", 45, Ink,
                new Vector2(0.08f, 0.74f), new Vector2(0.92f, 0.88f), TextAnchor.MiddleCenter);
            CreateButton(page.transform, "三维资源查看",
                new Vector2(0.14f, 0.49f), new Vector2(0.86f, 0.60f),
                () => ShowPage(Page.Resource), Card, Ink, 38);
            CreateButton(page.transform, "三维跟踪修复",
                new Vector2(0.14f, 0.32f), new Vector2(0.86f, 0.43f),
                () => ShowPage(Page.TrackingSelection), Card, Ink, 38);
            return page;
        }

        private GameObject BuildResourcePage()
        {
            GameObject page = CreatePage("三维资源查看", true);
            CreateHeader(page.transform, "三维资源查看", () => ShowPage(Page.Home));

            GameObject interaction = CreatePanel(page.transform, "模型交互区域", Color.clear,
                new Vector2(0.04f, 0.25f), new Vector2(0.96f, 0.89f), false);
            CreateText(interaction.transform, "单指拖动旋转，双指捏合缩放", 20, Muted,
                new Vector2(0.15f, 0.02f), new Vector2(0.85f, 0.09f), TextAnchor.MiddleCenter);
            modelViewer?.BindInteractionArea(interaction.GetComponent<RectTransform>());

            GameObject modelTabs = CreatePanel(page.transform, "模型切换", new Color32(239, 243, 249, 255),
                new Vector2(0.08f, 0.145f), new Vector2(0.92f, 0.225f), true);
            damagedButton = CreateButton(modelTabs.transform, "残缺模型",
                new Vector2(0.01f, 0.05f), new Vector2(0.495f, 0.95f),
                ShowDamagedResource, Accent, Color.white, 28);
            completeButton = CreateButton(modelTabs.transform, "完整模型",
                new Vector2(0.505f, 0.05f), new Vector2(0.99f, 0.95f),
                ShowCompleteResource, Card, Ink, 28);

            CreateButton(page.transform, "重置视角",
                new Vector2(0.08f, 0.055f), new Vector2(0.47f, 0.125f),
                modelViewer != null ? modelViewer.ResetView : (Action)null, Card, Ink, 26);
            CreateButton(page.transform, "文字介绍",
                new Vector2(0.53f, 0.055f), new Vector2(0.92f, 0.125f),
                ShowInformation, Card, Ink, 26);
            resourceStatus = CreateText(page.transform, string.Empty, 18, Muted,
                new Vector2(0.10f, 0.225f), new Vector2(0.90f, 0.25f), TextAnchor.MiddleCenter);
            return page;
        }

        private GameObject BuildTrackingSelectionPage()
        {
            GameObject page = CreatePage("文物选择", true);
            CreateHeader(page.transform, "文物选择", () => ShowPage(Page.Home));
            CreateText(page.transform, "请选择需要查看或进行增强现实修复的对象", 25, Muted,
                new Vector2(0.08f, 0.80f), new Vector2(0.92f, 0.87f), TextAnchor.MiddleCenter);

            GameObject card = CreatePanel(page.transform, "对象卡片", Card,
                new Vector2(0.08f, 0.25f), new Vector2(0.92f, 0.78f), true);
            if (objectThumbnail != null)
            {
                GameObject preview = new GameObject("真实对象缩略图");
                preview.transform.SetParent(card.transform, false);
                RectTransform rect = preview.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.08f, 0.48f);
                rect.anchorMax = new Vector2(0.92f, 0.92f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                RawImage image = preview.AddComponent<RawImage>();
                image.texture = objectThumbnail;
                image.uvRect = new Rect(0f, 0f, 1f, 1f);
                image.raycastTarget = false;
            }

            CreateText(card.transform, "瓶盖缺失饮料瓶", 34, Ink,
                new Vector2(0.08f, 0.37f), new Vector2(0.92f, 0.48f), TextAnchor.MiddleLeft);
            CreateText(card.transform, "实验替代对象    缺失部位：瓶盖", 23, Accent,
                new Vector2(0.08f, 0.29f), new Vector2(0.92f, 0.38f), TextAnchor.MiddleLeft);
            CreateText(card.transform,
                "通过三维跟踪，将预先重建和处理的虚拟瓶盖叠加至真实瓶口。",
                22, Muted, new Vector2(0.08f, 0.14f), new Vector2(0.92f, 0.29f), TextAnchor.UpperLeft);
            CreateButton(card.transform, "进入跟踪",
                new Vector2(0.08f, 0.025f), new Vector2(0.92f, 0.14f),
                () => ShowPage(Page.TrackingCamera), Accent, Color.white, 28);
            CreateButton(page.transform, "对象介绍",
                new Vector2(0.20f, 0.13f), new Vector2(0.80f, 0.20f),
                ShowInformation, Card, Ink, 26);
            return page;
        }

        private GameObject BuildTrackingCameraPage()
        {
            GameObject page = CreatePage("三维跟踪相机", false);
            CreateHeader(page.transform, "三维跟踪模式", () => ShowPage(Page.TrackingSelection));
            trackingStatus = CreateStatusBar(page.transform,
                "正在准备虚拟瓶盖初始参考位置。", 0.845f);

            GameObject controls = CreatePanel(page.transform, "跟踪控制", Color.clear,
                new Vector2(0.035f, 0.43f), new Vector2(0.245f, 0.79f), false);
            CreateButton(controls.transform, "开始",
                new Vector2(0.02f, 0.78f), new Vector2(0.98f, 0.98f),
                repairController != null ? repairController.StartRecognition : (Action)null,
                Card, Ink, 28);
            CreateButton(controls.transform, "重置",
                new Vector2(0.02f, 0.53f), new Vector2(0.98f, 0.73f),
                repairController != null ? repairController.ResetRecognition : (Action)null,
                Card, Ink, 28);
            CreateButton(controls.transform, "文字介绍",
                new Vector2(0.02f, 0.28f), new Vector2(0.98f, 0.48f),
                ShowInformation, Card, Ink, 23);
            CreateButton(controls.transform, "返回",
                new Vector2(0.02f, 0.03f), new Vector2(0.98f, 0.23f),
                () => ShowPage(Page.TrackingSelection), Card, Ink, 28);
            return page;
        }

        private GameObject BuildInfoModal()
        {
            GameObject blocker = CreatePanel(safeArea, "介绍遮罩", new Color32(10, 20, 32, 150),
                Vector2.zero, Vector2.one, true);
            GameObject card = CreatePanel(blocker.transform, "介绍内容卡", Card,
                new Vector2(0.08f, 0.16f), new Vector2(0.92f, 0.84f), true);
            infoTitle = CreateText(card.transform, string.Empty, 34, Ink,
                new Vector2(0.08f, 0.84f), new Vector2(0.92f, 0.96f), TextAnchor.MiddleCenter);

            GameObject viewport = CreatePanel(card.transform, "滚动区域", new Color32(247, 249, 252, 255),
                new Vector2(0.07f, 0.20f), new Vector2(0.93f, 0.83f), true);
            viewport.AddComponent<RectMask2D>();
            GameObject content = new GameObject("滚动内容");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 1050f);
            infoBody = CreateText(content.transform, string.Empty, 25, new Color32(38, 50, 68, 255),
                Vector2.zero, Vector2.one, TextAnchor.UpperLeft);
            infoBody.verticalOverflow = VerticalWrapMode.Overflow;
            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            CreateButton(card.transform, "关闭",
                new Vector2(0.25f, 0.055f), new Vector2(0.75f, 0.15f),
                () => blocker.SetActive(false), Accent, Color.white, 28);
            blocker.SetActive(false);
            return blocker;
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

        private void ShowPage(Page page)
        {
            currentPage = page;
            foreach (KeyValuePair<Page, GameObject> pair in pages)
            {
                pair.Value.SetActive(pair.Key == page);
            }

            bool resource = page == Page.Resource;
            bool tracking = page == Page.TrackingCamera;
            modelViewer?.SetViewerEnabled(resource);
            if (resource)
            {
                ShowDamagedResource();
            }

            orbTracker?.SetTrackingEnabled(tracking);
            infoModal?.SetActive(false);
        }

        private void ShowInformation()
        {
            if (infoModal == null)
            {
                return;
            }

            switch (currentPage)
            {
                case Page.Resource:
                    infoTitle.text = "三维资源介绍";
                    infoBody.text =
                        "本页面用于查看残缺模型和完整模型。残缺模型展示瓶盖缺失状态，完整模型展示补全后的整体形态。\n\n"
                        + "可单指上下左右拖动模型，双指捏合进行连续缩放，点击“重置视角”恢复初始观察状态。";
                    break;
                case Page.TrackingCamera:
                    infoTitle.text = "增强现实修复说明";
                    infoBody.text =
                        "进入页面后，屏幕首先显示虚拟瓶盖的初始参考位置。移动手机，使参考模型与真实瓶口大致重合，然后点击“开始”。\n\n"
                        + "系统通过 ORB 三维特征匹配和 PnP 位姿估计跟踪瓶身，并将预先处理好的瓶盖模型叠加至缺失位置。跟踪丢失或需要重新对齐时，点击“重置”。\n\n"
                        + "修复流程\n\n多视角图像采集\n→ 三维重建\n→ 模型清理与几何配准\n→ ORB 三维特征建库\n"
                        + "→ PnP 位姿估计\n→ 虚拟修复部件叠加\n→ 遮挡与光照一致性处理";
                    break;
                default:
                    infoTitle.text = "瓶盖缺失饮料瓶";
                    infoBody.text =
                        "当前对象为系统开发阶段使用的实验替代对象。真实瓶身保留瓶口和瓶颈结构，缺失部分为瓶盖。\n\n"
                        + "系统预先完成瓶盖三维重建和模型处理，并在增强现实模式下将虚拟瓶盖叠加至真实瓶口，"
                        + "用于验证残缺物体数字修复与虚实融合展示流程。";
                    break;
            }

            infoModal.SetActive(true);
            infoModal.transform.SetAsLastSibling();
        }

        private GameObject CreatePage(string name, bool opaque)
        {
            return CreatePanel(safeArea, name, opaque ? Surface : Color.clear,
                Vector2.zero, Vector2.one, opaque);
        }

        private void CreateHeader(Transform parent, string title, Action backAction)
        {
            GameObject header = CreatePanel(parent, "顶部导航", new Color32(250, 252, 255, 252),
                new Vector2(0f, 0.91f), new Vector2(1f, 1f), true);
            CreateButton(header.transform, "‹",
                new Vector2(0.015f, 0.06f), new Vector2(0.13f, 0.94f),
                backAction, new Color(1f, 1f, 1f, 0.001f), Ink, 58);
            CreateText(header.transform, title, 36, Ink,
                new Vector2(0.14f, 0.08f), new Vector2(0.94f, 0.92f), TextAnchor.MiddleCenter);
        }

        private Text CreateStatusBar(Transform parent, string value, float bottom)
        {
            GameObject bar = CreatePanel(parent, "状态栏", new Color32(12, 22, 32, 190),
                new Vector2(0.06f, bottom), new Vector2(0.94f, bottom + 0.052f), false);
            return CreateText(bar.transform, value, 20, Color.white,
                new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), TextAnchor.MiddleCenter);
        }

        private GameObject CreatePanel(
            Transform parent,
            string name,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax,
            bool blocksRaycasts)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = panel.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = blocksRaycasts;
            return panel;
        }

        private Button CreateButton(
            Transform parent,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Action action,
            Color background,
            Color foreground,
            int fontSize)
        {
            GameObject buttonObject = CreatePanel(parent, label + "按钮", background,
                anchorMin, anchorMax, true);
            Image graphic = buttonObject.GetComponent<Image>();
            graphic.raycastTarget = true;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.interactable = action != null;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            SetSelected(button, background == Accent);
            if (action != null)
            {
                button.onClick.AddListener(() =>
                {
                    Debug.Log($"URP button clicked: {label}");
                    action();
                });
            }

            CreateText(buttonObject.transform, label, fontSize, foreground,
                Vector2.zero, Vector2.one, TextAnchor.MiddleCenter);
            return button;
        }

        private static void SetSelected(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            Image image = button.targetGraphic as Image;
            if (image != null)
            {
                image.color = selected ? Accent : Card;
            }

            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = selected ? Color.white : Ink;
            }

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.93f, 0.96f, 1f, 1f);
            colors.pressedColor = new Color(0.78f, 0.86f, 0.98f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        private Text CreateText(
            Transform parent,
            string value,
            int fontSize,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax,
            TextAnchor alignment)
        {
            GameObject textObject = new GameObject("文字");
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(12f, 8f);
            rect.offsetMax = new Vector2(-12f, -8f);
            Text text = textObject.AddComponent<Text>();
            text.text = value;
            text.font = chineseFont != null
                ? chineseFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
    }
}
