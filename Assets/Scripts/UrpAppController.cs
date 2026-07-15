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
        [SerializeField] private OrbImageTrackingController orbTracker;
        [SerializeField] private RepairOverlayController repairController;
        [SerializeField] private ModelViewerController modelViewer;

        private readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
        private Canvas canvas;
        private Text resourceStatus;
        private Text trackingStatus;
        private GameObject infoModal;

        private static readonly Color Ink = new Color32(20, 48, 89, 255);
        private static readonly Color Accent = new Color32(23, 82, 155, 255);
        private static readonly Color Surface = new Color32(250, 252, 255, 255);
        private static readonly Color CameraButton = new Color32(15, 30, 45, 218);

        private void Awake()
        {
            BuildInterface();
            ShowPage(Page.Home);
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

            pages[Page.Home] = BuildHomePage();
            pages[Page.Resource] = BuildResourcePage();
            pages[Page.TrackingSelection] = BuildTrackingSelectionPage();
            pages[Page.TrackingCamera] = BuildTrackingCameraPage();
            infoModal = BuildInfoModal();

            if (modelViewer != null)
            {
                modelViewer.BindStatusText(resourceStatus);
            }

            if (orbTracker != null)
            {
                orbTracker.BindStatusText(trackingStatus);
            }

            if (repairController != null)
            {
                repairController.BindStatusText(trackingStatus);
            }
        }

        private GameObject BuildHomePage()
        {
            GameObject page = CreatePage("首页", true);
            CreateText(page.transform, "文化遗址数字修复及AR呈现系统", 48, Ink,
                new Vector2(0.08f, 0.74f), new Vector2(0.92f, 0.88f), TextAnchor.MiddleCenter);

            CreateButton(page.transform, "三维资源查看", new Vector2(0.14f, 0.48f), new Vector2(0.86f, 0.59f),
                () => ShowPage(Page.Resource), Surface, Ink, 38);
            CreateButton(page.transform, "三维跟踪修复", new Vector2(0.14f, 0.31f), new Vector2(0.86f, 0.42f),
                () => ShowPage(Page.TrackingSelection), Surface, Ink, 38);
            return page;
        }

        private GameObject BuildResourcePage()
        {
            GameObject page = CreatePage("三维资源查看", false);
            CreateHeader(page.transform, "三维资源查看", () => ShowPage(Page.Home));
            resourceStatus = CreateStatusBar(page.transform, "正在加载残缺饮料瓶三维模型。", 0.205f);

            GameObject tabs = CreatePanel(page.transform, "模型切换", new Color32(245, 248, 252, 242),
                new Vector2(0.08f, 0.105f), new Vector2(0.92f, 0.19f));
            CreateButton(tabs.transform, "残缺模型", new Vector2(0.02f, 0.08f), new Vector2(0.49f, 0.92f),
                modelViewer != null ? modelViewer.ShowDamagedModel : (Action)null, Accent, Color.white, 28);
            CreateButton(tabs.transform, "完整模型", new Vector2(0.51f, 0.08f), new Vector2(0.98f, 0.92f),
                modelViewer != null ? modelViewer.ShowCompleteModel : (Action)null, Surface, Ink, 28);

            CreateButton(page.transform, "旋转", new Vector2(0.08f, 0.025f), new Vector2(0.34f, 0.09f),
                modelViewer != null ? modelViewer.RotateModel : (Action)null, Surface, Ink, 26);
            CreateButton(page.transform, "缩放", new Vector2(0.37f, 0.025f), new Vector2(0.63f, 0.09f),
                modelViewer != null ? modelViewer.ToggleZoom : (Action)null, Surface, Ink, 26);
            CreateButton(page.transform, "文字介绍", new Vector2(0.66f, 0.025f), new Vector2(0.92f, 0.09f),
                ShowInformation, Surface, Ink, 24);
            return page;
        }

        private GameObject BuildTrackingSelectionPage()
        {
            GameObject page = CreatePage("三维跟踪修复", true);
            CreateHeader(page.transform, "三维跟踪修复", () => ShowPage(Page.Home));
            CreateText(page.transform, "选择展示对象", 28, new Color32(75, 88, 110, 255),
                new Vector2(0.10f, 0.68f), new Vector2(0.90f, 0.74f), TextAnchor.MiddleCenter);
            CreateButton(page.transform, "残缺饮料瓶（瓶盖缺失）", new Vector2(0.12f, 0.47f), new Vector2(0.88f, 0.59f),
                () => ShowPage(Page.TrackingCamera), Surface, Ink, 32);
            CreateButton(page.transform, "返回", new Vector2(0.14f, 0.08f), new Vector2(0.86f, 0.16f),
                () => ShowPage(Page.Home), Surface, Ink, 30);
            return page;
        }

        private GameObject BuildTrackingCameraPage()
        {
            GameObject page = CreatePage("三维跟踪相机", false);
            CreateHeader(page.transform, "残缺饮料瓶虚拟修复", () => ShowPage(Page.TrackingSelection));
            trackingStatus = CreateStatusBar(page.transform, "点击开始识别，并让瓶身与瓶口同时进入画面。", 0.855f);

            GameObject controls = CreatePanel(page.transform, "三维跟踪控制", new Color32(7, 15, 25, 158),
                new Vector2(0.04f, 0.025f), new Vector2(0.96f, 0.20f));
            CreateButton(controls.transform, "开始识别", new Vector2(0.02f, 0.53f), new Vector2(0.48f, 0.95f),
                repairController != null ? repairController.StartRecognition : (Action)null, CameraButton, Color.white, 27);
            CreateButton(controls.transform, "重新识别", new Vector2(0.52f, 0.53f), new Vector2(0.98f, 0.95f),
                repairController != null ? repairController.ResetRecognition : (Action)null, CameraButton, Color.white, 27);
            CreateButton(controls.transform, "修复前", new Vector2(0.02f, 0.05f), new Vector2(0.32f, 0.47f),
                repairController != null ? repairController.ShowBeforeRepair : (Action)null, CameraButton, Color.white, 26);
            CreateButton(controls.transform, "修复后", new Vector2(0.35f, 0.05f), new Vector2(0.65f, 0.47f),
                repairController != null ? repairController.ShowAfterRepair : (Action)null, Accent, Color.white, 26);
            CreateButton(controls.transform, "文字介绍", new Vector2(0.68f, 0.05f), new Vector2(0.98f, 0.47f),
                ShowInformation, CameraButton, Color.white, 24);
            return page;
        }

        private GameObject BuildInfoModal()
        {
            GameObject modal = CreatePanel(canvas.transform, "项目说明", new Color32(10, 20, 32, 242),
                new Vector2(0.08f, 0.24f), new Vector2(0.92f, 0.76f));
            CreateText(modal.transform, "残缺饮料瓶数字修复", 36, Color.white,
                new Vector2(0.08f, 0.80f), new Vector2(0.92f, 0.94f), TextAnchor.MiddleCenter);
            CreateText(modal.transform,
                "展示对象：瓶盖缺失的生榨椰子汁饮料瓶。\n\n" +
                "系统使用多视图 ORB 特征匹配和 PnP 三维位姿估计，定位真实瓶身与瓶口，并将 Meshroom 重建、清理后的虚拟瓶盖叠加到缺失位置。\n\n" +
                "三维资源页面可查看残缺模型与完整模型。",
                26, Color.white, new Vector2(0.10f, 0.24f), new Vector2(0.90f, 0.77f), TextAnchor.UpperLeft);
            CreateButton(modal.transform, "关闭", new Vector2(0.22f, 0.07f), new Vector2(0.78f, 0.18f),
                () => modal.SetActive(false), Accent, Color.white, 28);
            modal.SetActive(false);
            return modal;
        }

        private void ShowPage(Page page)
        {
            foreach (KeyValuePair<Page, GameObject> item in pages)
            {
                item.Value.SetActive(item.Key == page);
            }

            bool resource = page == Page.Resource;
            bool tracking = page == Page.TrackingCamera;
            if (modelViewer != null)
            {
                modelViewer.SetViewerEnabled(resource);
                if (resource)
                {
                    modelViewer.ResetView();
                    modelViewer.ShowDamagedModel();
                }
            }

            if (orbTracker != null)
            {
                orbTracker.SetTrackingEnabled(tracking);
            }

            if (infoModal != null)
            {
                infoModal.SetActive(false);
            }
        }

        private void ShowInformation()
        {
            if (infoModal != null)
            {
                infoModal.SetActive(true);
                infoModal.transform.SetAsLastSibling();
            }
        }

        private GameObject CreatePage(string name, bool opaque)
        {
            return CreatePanel(canvas.transform, name, opaque ? Surface : Color.clear, Vector2.zero, Vector2.one);
        }

        private void CreateHeader(Transform parent, string title, Action backAction)
        {
            GameObject header = CreatePanel(parent, "顶部导航", new Color32(250, 252, 255, 248),
                new Vector2(0f, 0.91f), new Vector2(1f, 1f));
            CreateButton(header.transform, "‹", new Vector2(0.02f, 0.10f), new Vector2(0.13f, 0.90f),
                backAction, Color.clear, Ink, 58);
            CreateText(header.transform, title, 36, Ink,
                new Vector2(0.14f, 0.08f), new Vector2(0.94f, 0.92f), TextAnchor.MiddleCenter);
        }

        private Text CreateStatusBar(Transform parent, string value, float bottom)
        {
            GameObject bar = CreatePanel(parent, "状态栏", new Color32(12, 22, 32, 210),
                new Vector2(0.06f, bottom), new Vector2(0.94f, bottom + 0.06f));
            return CreateText(bar.transform, value, 22, Color.white,
                new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), TextAnchor.MiddleCenter);
        }

        private GameObject CreatePanel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax)
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
            image.raycastTarget = color.a > 0.01f;
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
            GameObject buttonObject = CreatePanel(parent, label + "按钮", background, anchorMin, anchorMax);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.interactable = action != null;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.92f, 0.96f, 1f, 1f);
            colors.pressedColor = new Color(0.72f, 0.84f, 0.98f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            if (action != null)
            {
                button.onClick.AddListener(() => action());
            }

            CreateText(buttonObject.transform, label, fontSize, foreground,
                Vector2.zero, Vector2.one, TextAnchor.MiddleCenter);
            return button;
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
            text.font = chineseFont != null ? chineseFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
