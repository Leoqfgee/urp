using System;
using System.IO;
using UnityEngine;

namespace Urp.ArDemo
{
    /// <summary>
    /// Runtime hand-off between the tracker and the URP paper-occlusion pass.
    /// It stores renderer references only. It never changes mesh data, material,
    /// hierarchy, or transforms of DamagedBottleB or BottleCapC.
    /// </summary>
    public static class PaperOcclusionRegistry
    {
        private static UnityEngine.Object owner;

        public static Camera Camera { get; private set; }
        public static Renderer[] BottleRenderers { get; private set; } =
            Array.Empty<Renderer>();
        public static Renderer[] CapRenderers { get; private set; } =
            Array.Empty<Renderer>();
        public static float DepthEpsilonMeters { get; private set; } = 0.0005f;
        public static bool IsEnabled { get; private set; }
        public static bool CaptureRequested { get; private set; }
        public static string CaptureDirectory { get; private set; }
        private static Color32[] directReferencePixels = Array.Empty<Color32>();
        private static int directReferenceWidth;
        private static int directReferenceHeight;

        public static void Bind(
            UnityEngine.Object bindingOwner,
            Camera camera,
            Renderer[] completeDamagedBottleRenderers,
            Renderer[] unmodifiedCapRenderers,
            float depthEpsilonMeters)
        {
            owner = bindingOwner;
            Camera = camera;
            BottleRenderers = completeDamagedBottleRenderers ?? Array.Empty<Renderer>();
            CapRenderers = unmodifiedCapRenderers ?? Array.Empty<Renderer>();
            DepthEpsilonMeters = Mathf.Max(0f, depthEpsilonMeters);
            IsEnabled = false;
            CaptureRequested = false;
            CaptureDirectory = null;
            directReferencePixels = Array.Empty<Color32>();
        }

        public static void Enable(UnityEngine.Object bindingOwner)
        {
            if (owner == bindingOwner)
            {
                IsEnabled = true;
            }
        }

        public static void RequestDevelopmentCapture(UnityEngine.Object bindingOwner)
        {
            if (owner != bindingOwner || !Debug.isDebugBuild)
            {
                return;
            }
            CaptureDirectory = Path.Combine(
                Application.persistentDataPath,
                "V48PaperOcclusion",
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(CaptureDirectory);
            CaptureRequested = true;
            CaptureCurrentScreen(Path.Combine(CaptureDirectory, "DirectOriginalC.png"));
        }

        public static bool ConsumeCaptureRequest(out string directory)
        {
            directory = CaptureDirectory;
            if (!CaptureRequested || string.IsNullOrEmpty(directory))
            {
                return false;
            }
            CaptureRequested = false;
            return true;
        }

        public static void Disable(UnityEngine.Object bindingOwner)
        {
            if (owner == bindingOwner)
            {
                IsEnabled = false;
            }
        }

        public static void Unbind(UnityEngine.Object bindingOwner)
        {
            if (owner != bindingOwner)
            {
                return;
            }
            owner = null;
            Camera = null;
            BottleRenderers = Array.Empty<Renderer>();
            CapRenderers = Array.Empty<Renderer>();
            IsEnabled = false;
            CaptureRequested = false;
            CaptureDirectory = null;
            directReferencePixels = Array.Empty<Color32>();
        }

        private static void CaptureCurrentScreen(string path)
        {
            try
            {
                Texture2D image = new Texture2D(
                    Screen.width,
                    Screen.height,
                    TextureFormat.RGBA32,
                    false,
                    false);
                image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                image.Apply(false, false);
                directReferencePixels = image.GetPixels32();
                directReferenceWidth = image.width;
                directReferenceHeight = image.height;
                File.WriteAllBytes(path, image.EncodeToPNG());
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEngine.Object.DestroyImmediate(image);
                else
                    UnityEngine.Object.Destroy(image);
#else
                UnityEngine.Object.Destroy(image);
#endif
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[URP_V48_CAP_COLOR_DIAG] direct capture failed: "
                    + exception.Message);
            }
        }

        public static void ReportPaperColorStats(Texture2D paper)
        {
            if (paper == null
                || directReferencePixels.Length == 0
                || paper.width != directReferenceWidth
                || paper.height != directReferenceHeight)
            {
                Debug.LogWarning(
                    "[URP_V48_CAP_COLOR_DIAG] direct/paper dimensions unavailable "
                    + $"direct={directReferenceWidth}x{directReferenceHeight} "
                    + $"paper={(paper != null ? paper.width : 0)}x{(paper != null ? paper.height : 0)}");
                return;
            }
            Color32[] paperPixels = paper.GetPixels32();
            double normal = CompareMasked(paperPixels, false, out int alphaPixels);
            double flipped = CompareMasked(paperPixels, true, out _);
            bool flipY = flipped < normal;
            double rms = Math.Sqrt((flipY ? flipped : normal) / Math.Max(1, alphaPixels * 3));
            Debug.Log(
                "[URP_V48_CAP_COLOR_DIAG] directVsPaper "
                + $"pixelDifferenceRms={rms / 255.0:F6} alphaPixels={alphaPixels} "
                + $"portraitYFlipRequired={flipY} "
                + $"graphicsUVStartsAtTop={SystemInfo.graphicsUVStartsAtTop}");
        }

        private static double CompareMasked(
            Color32[] paperPixels,
            bool flipY,
            out int alphaPixels)
        {
            double sum = 0;
            alphaPixels = 0;
            for (int index = 0; index < paperPixels.Length; index++)
            {
                Color32 paper = paperPixels[index];
                if (paper.a == 0) continue;
                int x = index % directReferenceWidth;
                int y = index / directReferenceWidth;
                int directIndex = flipY
                    ? (directReferenceHeight - 1 - y) * directReferenceWidth + x
                    : index;
                Color32 direct = directReferencePixels[directIndex];
                double dr = paper.r - direct.r;
                double dg = paper.g - direct.g;
                double db = paper.b - direct.b;
                sum += dr * dr + dg * dg + db * db;
                alphaPixels++;
            }
            return sum;
        }
    }
}
