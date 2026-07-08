using System;
using System.Collections.Generic;
using OpenCvSharp;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Urp.ArDemo
{
    public sealed class OrbImageTrackingController : MonoBehaviour
    {
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private Camera arCamera;
        [SerializeField] private Transform trackedContentRoot;
        [SerializeField] private Text statusText;
        [SerializeField] private Texture2D targetTexture;
        [SerializeField] private float processIntervalSeconds = 0.3f;
        [SerializeField] private int maxFrameWidth = 640;
        [SerializeField] private int minGoodMatches = 18;
        [SerializeField] private float ratioTest = 0.72f;
        [SerializeField] private float placementDistance = 0.75f;
        [SerializeField] private float targetPhysicalWidthMeters = 0.09f;

        private ORB orb;
        private BFMatcher matcher;
        private KeyPoint[] targetKeypoints = Array.Empty<KeyPoint>();
        private Mat targetDescriptors;
        private Texture2D frameTexture;
        private float nextProcessTime;

        private void Awake()
        {
            if (trackedContentRoot != null)
            {
                trackedContentRoot.gameObject.SetActive(false);
            }

            orb = ORB.Create(900);
            matcher = new BFMatcher(NormTypes.Hamming, false);
            BuildTargetFeatures();
            UpdateStatus("ORB ready. Point camera at the target object.");
        }

        private void OnDestroy()
        {
            targetDescriptors?.Dispose();
            matcher?.Dispose();
            orb?.Dispose();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextProcessTime)
            {
                return;
            }

            nextProcessTime = Time.unscaledTime + processIntervalSeconds;
            ProcessCameraFrame();
        }

        private void BuildTargetFeatures()
        {
            if (targetTexture == null)
            {
                UpdateStatus("No ORB target texture assigned.");
                return;
            }

            using (Mat targetMat = OpenCvSharp.Unity.TextureToMat(targetTexture))
            using (Mat gray = new Mat())
            {
                Cv2.CvtColor(targetMat, gray, ColorConversionCodes.BGR2GRAY);
                targetDescriptors = new Mat();
                orb.DetectAndCompute(gray, null, out targetKeypoints, targetDescriptors);
            }
        }

        private void ProcessCameraFrame()
        {
            if (cameraManager == null || arCamera == null || trackedContentRoot == null)
            {
                return;
            }

            if (targetDescriptors == null || targetDescriptors.Empty() || targetKeypoints.Length < 8)
            {
                UpdateStatus("ORB target has too few features.");
                return;
            }

            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
            {
                return;
            }

            try
            {
                Texture2D texture = ConvertCpuImage(cpuImage);
                using (Mat frameMat = OpenCvSharp.Unity.TextureToMat(texture))
                using (Mat gray = new Mat())
                using (Mat resized = ResizeForTracking(frameMat))
                using (Mat frameDescriptors = new Mat())
                {
                    Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);
                    orb.DetectAndCompute(gray, null, out KeyPoint[] frameKeypoints, frameDescriptors);
                    if (frameDescriptors.Empty() || frameKeypoints.Length < 8)
                    {
                        SetTracked(false, "ORB: not enough camera features.");
                        return;
                    }

                    DMatch[][] knnMatches = matcher.KnnMatch(targetDescriptors, frameDescriptors, 2);
                    List<DMatch> goodMatches = FilterGoodMatches(knnMatches);
                    if (goodMatches.Count < minGoodMatches)
                    {
                        SetTracked(false, $"ORB: {goodMatches.Count}/{minGoodMatches} matches.");
                        return;
                    }

                    if (!TryEstimateTargetCenter(goodMatches, frameKeypoints, out Vector2 center01, out float relativeWidth))
                    {
                        SetTracked(false, "ORB: homography failed.");
                        return;
                    }

                    ApplyTrackedPlacement(center01, Mathf.Max(relativeWidth, 0.05f));
                    SetTracked(true, $"ORB tracking: {goodMatches.Count} matches.");
                }
            }
            finally
            {
                cpuImage.Dispose();
            }
        }

        private Texture2D ConvertCpuImage(XRCpuImage cpuImage)
        {
            XRCpuImage.ConversionParams conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                outputDimensions = new Vector2Int(cpuImage.width, cpuImage.height),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.MirrorY
            };

            int size = cpuImage.GetConvertedDataSize(conversionParams);
            using (NativeArray<byte> buffer = new NativeArray<byte>(size, Allocator.Temp))
            {
                cpuImage.Convert(conversionParams, buffer);
                if (frameTexture == null || frameTexture.width != cpuImage.width || frameTexture.height != cpuImage.height)
                {
                    frameTexture = new Texture2D(cpuImage.width, cpuImage.height, TextureFormat.RGBA32, false);
                }

                frameTexture.LoadRawTextureData(buffer);
                frameTexture.Apply(false);
            }

            return frameTexture;
        }

        private Mat ResizeForTracking(Mat source)
        {
            if (source.Width <= maxFrameWidth)
            {
                return source.Clone();
            }

            double scale = (double)maxFrameWidth / source.Width;
            Mat resized = new Mat();
            Cv2.Resize(source, resized, new Size(maxFrameWidth, (int)(source.Height * scale)));
            return resized;
        }

        private List<DMatch> FilterGoodMatches(DMatch[][] knnMatches)
        {
            List<DMatch> good = new List<DMatch>();
            foreach (DMatch[] pair in knnMatches)
            {
                if (pair.Length >= 2 && pair[0].Distance < ratioTest * pair[1].Distance)
                {
                    good.Add(pair[0]);
                }
            }

            return good;
        }

        private bool TryEstimateTargetCenter(List<DMatch> goodMatches, KeyPoint[] frameKeypoints, out Vector2 center01, out float relativeWidth)
        {
            center01 = new Vector2(0.5f, 0.5f);
            relativeWidth = 0.2f;

            List<Point2d> targetPoints = new List<Point2d>();
            List<Point2d> framePoints = new List<Point2d>();
            foreach (DMatch match in goodMatches)
            {
                Point2f targetPoint = targetKeypoints[match.QueryIdx].Pt;
                Point2f framePoint = frameKeypoints[match.TrainIdx].Pt;
                targetPoints.Add(new Point2d(targetPoint.X, targetPoint.Y));
                framePoints.Add(new Point2d(framePoint.X, framePoint.Y));
            }

            using (Mat homography = Cv2.FindHomography(targetPoints, framePoints, HomographyMethods.Ransac, 4.0))
            {
                if (homography == null || homography.Empty())
                {
                    return false;
                }

                Point2d[] corners =
                {
                    new Point2d(0, 0),
                    new Point2d(targetTexture.width, 0),
                    new Point2d(targetTexture.width, targetTexture.height),
                    new Point2d(0, targetTexture.height)
                };

                Point2d[] projected = PerspectiveTransform(corners, homography);
                double minX = projected[0].X;
                double maxX = projected[0].X;
                double minY = projected[0].Y;
                double maxY = projected[0].Y;
                foreach (Point2d point in projected)
                {
                    minX = Math.Min(minX, point.X);
                    maxX = Math.Max(maxX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxY = Math.Max(maxY, point.Y);
                }

                double width = Math.Max(1.0, maxX - minX);
                double height = Math.Max(1.0, maxY - minY);
                center01 = new Vector2((float)((minX + width * 0.5) / maxFrameWidth), (float)(1.0 - ((minY + height * 0.5) / (maxFrameWidth * ((float)frameTexture.height / frameTexture.width)))));
                relativeWidth = (float)(width / maxFrameWidth);
                return true;
            }
        }

        private static Point2d[] PerspectiveTransform(Point2d[] points, Mat homography)
        {
            Point2d[] result = new Point2d[points.Length];
            double h00 = homography.Get<double>(0, 0);
            double h01 = homography.Get<double>(0, 1);
            double h02 = homography.Get<double>(0, 2);
            double h10 = homography.Get<double>(1, 0);
            double h11 = homography.Get<double>(1, 1);
            double h12 = homography.Get<double>(1, 2);
            double h20 = homography.Get<double>(2, 0);
            double h21 = homography.Get<double>(2, 1);
            double h22 = homography.Get<double>(2, 2);

            for (int i = 0; i < points.Length; i++)
            {
                double x = points[i].X;
                double y = points[i].Y;
                double w = h20 * x + h21 * y + h22;
                result[i] = new Point2d((h00 * x + h01 * y + h02) / w, (h10 * x + h11 * y + h12) / w);
            }

            return result;
        }

        private void ApplyTrackedPlacement(Vector2 center01, float relativeWidth)
        {
            Vector3 screenPoint = new Vector3(center01.x * Screen.width, center01.y * Screen.height, 0f);
            Ray ray = arCamera.ScreenPointToRay(screenPoint);
            trackedContentRoot.SetParent(null, true);
            trackedContentRoot.position = ray.origin + ray.direction.normalized * placementDistance;
            Vector3 forward = arCamera.transform.forward;
            forward.y = 0f;
            trackedContentRoot.rotation = forward.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : arCamera.transform.rotation;
            trackedContentRoot.localScale = Vector3.one * Mathf.Clamp(relativeWidth / targetPhysicalWidthMeters * 0.08f, 0.25f, 1.4f);
        }

        private void SetTracked(bool tracked, string message)
        {
            trackedContentRoot.gameObject.SetActive(tracked);
            UpdateStatus(message);
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }
    }
}
