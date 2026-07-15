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
            TrackingCamera,
            SlamSelection,
            SlamCamera
        }

        [SerializeField] private Font chineseFont;
        [SerializeField] private OrbImageTrackingController orbTracker;
        [SerializeField] private RepairOverlayController repairController;
        [SerializeField] private ModelViewerController modelViewer;
        [SerializeField] private PlanarMarkerSlamController slamController;

        private readonly Dictionary<Page, GameObject> pages = new Dictionary<Page, GameObject>();
        private Canvas canvas;
        private Text trackingStatus;
        private Text slamStatus;
        private GameObject infoModal;

        private static readonly Color Ink = new Color32(20, 48, 89, 255);
        private static readonly Color Accent = new Color32(23, 82, 155, 255);
        private static readonly Color White = new Color32(250, 252, 255, 255);
        private static readonly Color CameraButton = new Color32(15, 30, 45, 210);

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
            pages[Page.TrackingSelection] = BuildSelectionPage(
                "三维跟踪模式",
                () => ShowPage(Page.TrackingCamera),
                () => ShowPage(Page.Home));
            pages[Page.TrackingCamera] = BuildTrackingCameraPage();
            pages[Page.SlamSelection] = BuildSelectionPage(
                "平面标志与SLAM模式",
                () => ShowPage(Page.SlamCamera),
                () => ShowPage(Page.Home));
            pages[Page.SlamCamera] = BuildSlamCameraPage();
            infoModal = BuildInfoModal();

            if (orbTracker != null)
            {
                orbTracker.BindStatusText(trackingStatus);
            }

            if (repairController != null)
            {
                repairController.BindStatusText(trackingStatus);
            }

            if (slamController != null)
            {
                slamController.BindStatusText(slamStatus);
            }
        }

        private GameObject BuildHomePage()
        {
            GameObject page = CreatePage("首页", true);
            CreateText(page.transform, "文化遗址数字修复及AR呈现系统", 48, Ink,
                new Vector2(0.08f, 0.76f), new Vector2(0.92f, 0.91f), TextAnchor.MiddleCenter);

            CreateButton(page.transform, "三维资源查看", new Vector2(0.14f, 0.54f), new Vector2(0.86f, 0.64f),
                () => ShowPage(Page.Resource), White, Ink, 38);
            CreateButton(page.transform, "三维跟踪模式", new Vector2(0.14f, 0.39f), new Vector2(0.86f, 0.49f),
                () => ShowPage(Page.TrackingSelection), White, Ink, 38);
            CreateButton(page.transform, "平面标志与SLAM模式", new Vector2(0.14f, 0.24f), new Vector2(0.86f, 0.34f),
                () => ShowPage(Page.SlamSelection), White, Ink, 36);
            return page;
        }

        private GameObject BuildResourcePage()
        {
            GameObject page = CreatePage("三维资源查看", false);
            CreateHeader(page.transform, "三维资源查看", () => ShowPage(Page.Home));

            GameObject tabs = CreatePanel(page.transform, "模型切换", new Color32(245, 248, 252, 235),
                new Vector2(0.10f, 0.22f), new Vector2(0.90f, 0.31f));
            CreateButton(tabs.transform, "残缺模型", new Vector2(0.02f, 0.08f), new Vector2(0.49f, 0.92f),
                modelViewer != null ? modelViewer.ShowDamagedModel : (Action)null, Accent, Color.white, 28);
            CreateButton(tabs.transform, "完整模型", new Vector2(0.51f, 0.08f), new Vector2(0.98f, 0.92f),
                modelViewer != null ? modelViewer.ShowCompleteModel : (Action)null, White, Ink, 28);

            CreateButton(page.transform, "旋转", new Vector2(0.10f, 0.12f), new Vector2(0.34f, 0.19f),
                modelViewer != null ? modelViewer.RotateModel : (Action)null, White, Ink, 26);
            CreateButton(page.transform, "缩放", new Vector2(0.38f, 0.12f), new Vector2(0.62f, 0.19f),
                modelViewer != null ? modelViewer.ToggleZoom : (Action)null, White, Ink, 26);
            CreateButton(page.transform, "文字介绍", new Vector2(0.66f, 0.12f), new Vector2(0.90f, 0.19f),
                ShowInformation, White, Ink, 24);
            return page;
        }

        private GameObject BuildSelectionPage(string title, Action selectAction, Action backAction)
        {
            GameObject page = CreatePage(title, true);
            CreateHeader(page.transform, title, backAction);
            CreateText(page.transform, "请选择实际展示对象", 26, new Color32(75, 88, 110, 255),
                new Vector2(0.10f, 0.68f), new Vector2(0.90f, 0.74f), TextAnchor.MiddleCenter);
            CreateButton(page.transform, "残缺饮料瓶（瓶盖缺失）", new Vector2(0.14f, 0.48f), new Vector2(0.86f, 0.60f),
                selectAction, White, Ink, 34);
            CreateButton(page.transform, "返回", new Vector2(0.14f, 0.08f), new Vector2(0.86f, 0.16f),
                backAction, White, Ink, 30);
            return page;
        }

        private GameObject BuildTrackingCameraPage()
        {
            GameObject page = CreatePage("三维跟踪相机", false);
            CreateHeader(page.transform, "三维跟踪模式", () => ShowPage(Page.TrackingSelection));
            trackingStatus = CreateStatusBar(page.transform, "点击开始识别，对准无瓶盖饮料瓶。", 0.885f);

            GameObject controls = CreatePanel(page.transform, "三维跟踪控制", new Color32(7, 15, 25, 150),
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

        private GameObject BuildSlamCameraPage()
        {
            GameObject page = CreatePage("平面标志与SLAM相机", false);
            CreateHeader(page.transform, "平面标志与SLAM模式", () => ShowPage(Page.SlamSelection));
            slamStatus = CreateStatusBar(page.transform, "缓慢移动手机，先扫描瓶子周围环境。", 0.885f);

            GameObject controls = CreatePanel(page.transform, "SLAM 控制", new Color32(7, 15, 25, 150),
                new Vector2(0.035f, 0.025f), new Vector2(0.43f, 0.49f));
            CreateVerticalButton(controls.transform, "保存并继续", 0.80f, slamController != null ? slamController.SaveAndContinue : (Action)null);
            CreateVerticalButton(controls.transform, "检测平面标志", 0.64f, slamController != null ? slamController.DetectMarker : (Action)null);
            CreateVerticalButton(controls.transform, "旋转", 0.48f, slamController != null ? slamController.RotateRepair : (Action)null);
            CreateVerticalButton(controls.transform, "缩放", 0.32f, slamController != null ? slamController.ToggleRepairScale : (Action)null);
            CreateVerticalButton(controls.transform, "文字介绍", 0.16f, ShowInformation);
            CreateVerticalButton(controls.transform, "重新扫描", 0.00f, slamController != null ? slamController.ResetMode : (Action)null);
            return page;
        }

        private GameObject BuildInfoModal()
        {
            GameObject modal = CreatePanel(canvas.transform, "项目说明", new Color32(10, 20, 32, 238),
                new Vector2(0.08f, 0.20f), new Vector2(0.92f, 0.80f));
            CreateText(modal.transform, "数字修复说明", 38, Color.white,
                new Vector2(0.08f, 0.80f), new Vector2(0.92f, 0.94f), TextAnchor.MiddleCenter);
            CreateText(modal.transform,
                "展示对象：生榨椰子汁饮料瓶（瓶盖缺失）\n\n" +
                "三维跟踪模式使用 ORB 特征、三维点与 PnP 位姿估计，将虚拟修复瓶盖叠加到真实瓶口。\n\n" +
                "平面标志与SLAM模式先扫描环境，再检测瓶身平面标志并建立空间锚点，适合物体静止时从多视角观察。\n\n" +
                "模型来源：Meshroom 三维重建；修复部分：瓶盖模型。",
                26, Color.white, new Vector2(0.10f, 0.24f), new Vector2(0.90f, 0.78f), TextAnchor.UpperLeft);
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
            bool slam = page == Page.SlamCamera;
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

            if (slamController != null)
            {
                slamController.SetModeEnabled(slam);
                if (slam)
                {
                    slamController.ResetMode();
                }
            }

            if (infoModal != null)
            {
                infoModal.SetActive(false);
            }
        }

        private void ShowInformation()
        {
            infoModal.SetActive(true);
            infoModal.transform.SetAsLastSibling();
        }

        private GameObject CreatePage(string name, bool opaque)
        {
            GameObject page = CreatePanel(canvas.transform, name, opaque ? White : Color.clear, Vector2.zero, Vector2.one);
            return page;
        }

        private void CreateHeader(Transform parent, string title, Action backAction)
        {
            GameObject header = CreatePanel(parent, "顶部导航", new Color32(250, 252, 255, 245),
                new Vector2(0f, 0.91f), new Vector2(1f, 1f));
            CreateButton(header.transform, "<", new Vector2(0.02f, 0.10f), new Vector2(0.13f, 0.90f),
                backAction, Color.clear, Ink, 48);
            CreateText(header.transform, title, 36, Ink, new Vector2(0.14f, 0.08f), new Vector2(0.94f, 0.92f), TextAnchor.MiddleCenter);
        }

        private Text CreateStatusBar(Transform parent, string value, float bottom)
        {
            GameObject bar = CreatePanel(parent, "状态栏", new Color32(12, 22, 32, 205),
                new Vector2(0.08f, bottom), new Vector2(0.92f, bottom + 0.055f));
            return CreateText(bar.transform, value, 22, Color.white, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), TextAnchor.MiddleCenter);
        }

        private void CreateVerticalButton(Transform parent, string label, float bottom, Action action)
        {
            CreateButton(parent, label, new Vector2(0.06f, bottom + 0.025f), new Vector2(0.94f, bottom + 0.145f),
                action, White, Ink, label.Length > 5 ? 22 : 25);
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
            if (action != null)
            {
                button.onClick.AddListener(() => action());
            }

            CreateText(buttonObject.transform, label, fontSize, foreground, Vector2.zero, Vector2.one, TextAnchor.MiddleCenter);
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
