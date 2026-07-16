using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Urp.ArDemo.Calibration;
using Urp.ArDemo.Native;

namespace Urp.ArDemo
{
    public sealed class OrbImageTrackingController : MonoBehaviour
    {
        [Header("AR input")]
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private Camera arCamera;

        [Header("Object-coordinate overlay")]
        [SerializeField] private Transform trackedObjectPoseRoot;
        [SerializeField] private Transform registeredBottleCap;
        [SerializeField] private RepairCalibrationProfile calibration;
        [SerializeField] private Text statusText;

        [Header("ORB database")]
        [SerializeField] private TextAsset[] orbModelFiles;
        [SerializeField] private int maxFrameWidth = 640;
        [SerializeField] private int minGoodMatches = 24;
        [SerializeField] private int minPoseInliers = 20;
        [SerializeField] private float minimumInlierRatio = 0.5f;
        [SerializeField] private float maximumReprojectionErrorPixels = 2.5f;
        [SerializeField] private float minimumCoverageX = 0.12f;
        [SerializeField] private float minimumCoverageY = 0.20f;
        [SerializeField] private float ratioTest = 0.72f;

        [Header("Timing and continuity")]
        [SerializeField] private float relocationIntervalSeconds = 0.14f;
        [SerializeField] private float trackingIntervalSeconds = 0.09f;
        [SerializeField] private float lostPoseGraceSeconds = 0.45f;
        [SerializeField] private float maximumPositionJumpMeters = 0.16f;
        [SerializeField] private float maximumRotationJumpDegrees = 32f;
        [SerializeField] private float positionResponse = 16f;
        [SerializeField] private float rotationResponse = 18f;

        [Header("Initial paper-style alignment")]
        [SerializeField] private Vector3 initialMouthPositionInCamera = new Vector3(0f, 0.10f, 0.55f);
        [SerializeField] private Vector3 initialObjectEulerInCamera = Vector3.zero;

        private readonly List<NativeOrbTracker> trackers = new List<NativeOrbTracker>();
        private Texture2D frameTexture;
        private Renderer[] capRenderers;
        private MaterialPropertyBlock materialProperties;
        private bool modeEnabled;
        private bool recognitionRunning;
        private bool hasTrackedPose;
        private bool repairVisibleRequested = true;
        private float nextProcessTime;
        private float lastValidPoseTime = -10f;
        private float smoothedLuminance = 0.75f;
        private Vector3 lastTargetPosition;
        private Quaternion lastTargetRotation;
        private int rejectedJumpFrames;

        public bool HasTrackedPose => hasTrackedPose;

        private void Awake()
        {
            materialProperties = new MaterialPropertyBlock();
            if (registeredBottleCap != null)
            {
                capRenderers = registeredBottleCap.GetComponentsInChildren<Renderer>(true);
            }

            if (trackedObjectPoseRoot != null)
            {
                trackedObjectPoseRoot.gameObject.SetActive(false);
            }

            BuildTrackers();
            Debug.Log($"URP native tracker version: {NativeOrbTracker.BuildVersion}");
        }

        private void OnDestroy()
        {
            foreach (NativeOrbTracker tracker in trackers)
            {
                tracker.Dispose();
            }
        }

        private void Update()
        {
            if (!modeEnabled || !recognitionRunning || Time.unscaledTime < nextProcessTime)
            {
                return;
            }

            ProcessCameraFrame();
        }

        public void BindStatusText(Text value)
        {
            statusText = value;
        }

        public void SetTrackingEnabled(bool enabled)
        {
            modeEnabled = enabled;
            recognitionRunning = false;
            if (enabled)
            {
                ShowInitialPose();
                UpdateStatus("移动手机，使参考瓶盖与真实瓶口大致重合，然后点击“开始”。");
            }
            else
            {
                HideOverlay(true);
            }
        }

        public void StartRecognition()
        {
            if (!modeEnabled)
            {
                return;
            }

            recognitionRunning = true;
            nextProcessTime = 0f;
            UpdateStatus("正在识别瓶身，请保持瓶口和瓶身文字清晰可见。");
        }

        public void ResetTracking()
        {
            recognitionRunning = false;
            hasTrackedPose = false;
            rejectedJumpFrames = 0;
            lastValidPoseTime = -10f;
            ShowInitialPose();
            UpdateStatus("已恢复初始参考位置，请重新粗对齐后点击“开始”。");
        }

        public void SetRepairVisible(bool visible)
        {
            repairVisibleRequested = visible;
            if (trackedObjectPoseRoot != null)
            {
                trackedObjectPoseRoot.gameObject.SetActive(visible && (modeEnabled || hasTrackedPose));
            }
        }

        private void BuildTrackers()
        {
            trackers.Clear();
            if (orbModelFiles == null)
            {
                return;
            }

            foreach (TextAsset model in orbModelFiles)
            {
                if (model == null)
                {
                    continue;
                }

                NativeOrbTracker tracker = new NativeOrbTracker(
                    1800,
                    ratioTest,
                    minGoodMatches,
                    maxFrameWidth);
                if (tracker.IsValid && tracker.SetModel(model))
                {
                    trackers.Add(tracker);
                }
                else
                {
                    tracker.Dispose();
                }
            }

            if (trackers.Count == 0)
            {
                UpdateStatus("ORB 三维特征库加载失败。");
            }
        }

        private void ProcessCameraFrame()
        {
            nextProcessTime = Time.unscaledTime
                + (hasTrackedPose ? trackingIntervalSeconds : relocationIntervalSeconds);
            if (cameraManager == null
                || arCamera == null
                || trackedObjectPoseRoot == null
                || calibration == null
                || trackers.Count == 0
                || !cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            {
                return;
            }

            try
            {
                Texture2D texture = ConvertCpuImage(image);
                CameraIntrinsics intrinsics = GetCameraIntrinsics(
                    image.width,
                    image.height,
                    texture.width,
                    texture.height);
                int rotationClockwise = ResolveFrameRotation(texture.width, texture.height);
                byte[] frameRgba = NativeOrbTracker.GetRgbaBytes(texture);

                NativeOrbResult best = default;
                bool hasResult = false;
                foreach (NativeOrbTracker tracker in trackers)
                {
                    tracker.Track(
                        frameRgba,
                        texture.width,
                        texture.height,
                        intrinsics,
                        rotationClockwise,
                        out NativeOrbResult candidate);
                    if (!hasResult || IsBetter(candidate, best))
                    {
                        best = candidate;
                        hasResult = true;
                    }
                }

                if (!hasResult)
                {
                    HideOverlay(false);
                    UpdateStatus("尚未找到稳定的瓶身三维位姿。");
                    return;
                }

                if (!PassesPoseQuality(best, out string qualityReason))
                {
                    HideOverlay(false);
                    UpdateStatus(qualityReason);
                    return;
                }

                if (!RepairPoseMath.TryGetObjectPose(
                        best,
                        rotationClockwise,
                        arCamera,
                        calibration,
                        out Vector3 targetPosition,
                        out Quaternion targetRotation))
                {
                    HideOverlay(false);
                    UpdateStatus("瓶身位姿有效，但坐标转换结果无效。");
                    return;
                }

                if (!PassesPoseContinuity(targetPosition, targetRotation))
                {
                    HideOverlay(false);
                    UpdateStatus("检测到异常位姿跳变，正在等待稳定结果。");
                    return;
                }

                ApplyPose(targetPosition, targetRotation);
                ApplyLightingConsistency(best.localLuminance);
                trackedObjectPoseRoot.gameObject.SetActive(repairVisibleRequested);
                lastValidPoseTime = Time.unscaledTime;

                if (!ValidateCapVisibility(out string visibilityReason))
                {
                    UpdateStatus($"瓶身位姿有效，但修复模型未进入有效视野：{visibilityReason}");
                    return;
                }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
                UpdateStatus(
                    $"跟踪稳定：内点 {best.poseInliers}，比例 {best.inlierRatio:P0}，"
                    + $"误差 {best.reprojectionError:F2}px，耗时 {best.processingMilliseconds:F0}ms。");
#else
                UpdateStatus(calibration.physicalScaleVerified
                    ? "跟踪稳定，虚拟瓶盖已叠加至瓶口。"
                    : "跟踪稳定，虚拟瓶盖已叠加；当前物理尺寸仍待实测标定。");
#endif
            }
            finally
            {
                image.Dispose();
            }
        }

        private bool PassesPoseQuality(NativeOrbResult result, out string reason)
        {
            if (result.goodMatches < minGoodMatches)
            {
                reason = $"正在搜索瓶身：有效匹配 {result.goodMatches}/{minGoodMatches}。";
                return false;
            }

            if (result.poseValid == 0
                || result.poseInliers < minPoseInliers
                || result.inlierRatio < minimumInlierRatio
                || result.reprojectionError > maximumReprojectionErrorPixels
                || result.coverageX < minimumCoverageX
                || result.coverageY < minimumCoverageY)
            {
                reason = "已识别瓶身，但当前特征分布或位姿稳定性不足。";
                return false;
            }

            if (!float.IsFinite(result.tvecZ) || result.tvecZ <= 0f)
            {
                reason = "已识别瓶身，但估计深度无效。";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool PassesPoseContinuity(Vector3 position, Quaternion rotation)
        {
            if (!hasTrackedPose)
            {
                rejectedJumpFrames = 0;
                return true;
            }

            float positionJump = Vector3.Distance(lastTargetPosition, position);
            float rotationJump = Quaternion.Angle(lastTargetRotation, rotation);
            if (positionJump <= maximumPositionJumpMeters
                && rotationJump <= maximumRotationJumpDegrees)
            {
                rejectedJumpFrames = 0;
                return true;
            }

            rejectedJumpFrames++;
            if (rejectedJumpFrames < 3)
            {
                return false;
            }

            hasTrackedPose = false;
            rejectedJumpFrames = 0;
            return true;
        }

        private void ApplyPose(Vector3 position, Quaternion rotation)
        {
            float deltaTime = Mathf.Max(0.001f, Time.unscaledDeltaTime);
            if (!hasTrackedPose)
            {
                trackedObjectPoseRoot.SetParent(null, true);
                trackedObjectPoseRoot.SetPositionAndRotation(position, rotation);
                trackedObjectPoseRoot.localScale = Vector3.one * calibration.metersPerModelUnit;
                hasTrackedPose = true;
            }
            else
            {
                float positionAlpha = 1f - Mathf.Exp(-positionResponse * deltaTime);
                float rotationAlpha = 1f - Mathf.Exp(-rotationResponse * deltaTime);
                trackedObjectPoseRoot.position = Vector3.Lerp(
                    trackedObjectPoseRoot.position,
                    position,
                    positionAlpha);
                trackedObjectPoseRoot.rotation = Quaternion.Slerp(
                    trackedObjectPoseRoot.rotation,
                    rotation,
                    rotationAlpha);
            }

            lastTargetPosition = position;
            lastTargetRotation = rotation;
        }

        private void ShowInitialPose()
        {
            if (trackedObjectPoseRoot == null || arCamera == null || calibration == null)
            {
                return;
            }

            hasTrackedPose = false;
            Quaternion frame = Quaternion.LookRotation(
                calibration.ForwardInModel,
                calibration.UpInModel);
            Vector3 canonicalMouth = Quaternion.Inverse(frame)
                * (calibration.mouthCenterInModel - calibration.objectOriginInModel);
            Quaternion previewRotation = Quaternion.Euler(initialObjectEulerInCamera);
            Vector3 rootPosition = initialMouthPositionInCamera
                - previewRotation * (canonicalMouth * calibration.metersPerModelUnit);

            trackedObjectPoseRoot.SetParent(arCamera.transform, false);
            trackedObjectPoseRoot.localPosition = rootPosition;
            trackedObjectPoseRoot.localRotation = previewRotation;
            trackedObjectPoseRoot.localScale = Vector3.one * calibration.metersPerModelUnit;
            trackedObjectPoseRoot.gameObject.SetActive(repairVisibleRequested);
        }

        private bool ValidateCapVisibility(out string reason)
        {
            if (registeredBottleCap == null || !registeredBottleCap.gameObject.activeInHierarchy)
            {
                reason = "瓶盖对象未激活";
                return false;
            }

            Bounds bounds = default;
            bool initialized = false;
            foreach (Renderer renderer in capRenderers)
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!initialized)
            {
                reason = "瓶盖没有启用的 Renderer";
                return false;
            }

            Vector3 center = arCamera.WorldToViewportPoint(bounds.center);
            Vector3 top = arCamera.WorldToViewportPoint(bounds.center + Vector3.up * bounds.extents.y);
            Vector3 side = arCamera.WorldToViewportPoint(bounds.center + Vector3.right * bounds.extents.x);
            float screenSize = Mathf.Max(
                Vector2.Distance(center, top),
                Vector2.Distance(center, side)) * 2f;
            if (center.z <= 0f)
            {
                reason = "瓶盖位于相机后方";
                return false;
            }

            if (center.x < -0.15f || center.x > 1.15f || center.y < -0.15f || center.y > 1.15f)
            {
                reason = "瓶盖投影位于屏幕外";
                return false;
            }

            if (screenSize < 0.008f || screenSize > 0.45f)
            {
                reason = "瓶盖屏幕尺寸不合理";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void ApplyLightingConsistency(float luminance)
        {
            if (!float.IsFinite(luminance) || capRenderers == null)
            {
                return;
            }

            smoothedLuminance = Mathf.Lerp(smoothedLuminance, Mathf.Clamp(luminance, 0.2f, 0.95f), 0.12f);
            float value = Mathf.Lerp(0.68f, 1f, smoothedLuminance);
            Color color = new Color(value, value * 0.99f, value * 0.97f, 1f);
            foreach (Renderer renderer in capRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(materialProperties);
                materialProperties.SetColor("_BaseColor", color);
                materialProperties.SetColor("_Color", color);
                renderer.SetPropertyBlock(materialProperties);
            }
        }

        private void HideOverlay(bool force)
        {
            if (!force && hasTrackedPose && Time.unscaledTime - lastValidPoseTime <= lostPoseGraceSeconds)
            {
                return;
            }

            if (trackedObjectPoseRoot != null)
            {
                trackedObjectPoseRoot.gameObject.SetActive(false);
            }

            hasTrackedPose = false;
        }

        private static bool IsBetter(NativeOrbResult current, NativeOrbResult best)
        {
            if (current.poseValid != best.poseValid)
            {
                return current.poseValid > best.poseValid;
            }

            if (current.poseValid != 0 && current.reprojectionError != best.reprojectionError)
            {
                return current.reprojectionError < best.reprojectionError;
            }

            if (current.poseInliers != best.poseInliers)
            {
                return current.poseInliers > best.poseInliers;
            }

            return current.goodMatches > best.goodMatches;
        }

        private CameraIntrinsics GetCameraIntrinsics(
            int sourceWidth,
            int sourceHeight,
            int outputWidth,
            int outputHeight)
        {
            float scaleX = outputWidth / (float)Mathf.Max(1, sourceWidth);
            float scaleY = outputHeight / (float)Mathf.Max(1, sourceHeight);
            if (cameraManager.TryGetIntrinsics(out XRCameraIntrinsics intrinsics))
            {
                return new CameraIntrinsics(
                    intrinsics.focalLength.x * scaleX,
                    intrinsics.focalLength.y * scaleY,
                    intrinsics.principalPoint.x * scaleX,
                    intrinsics.principalPoint.y * scaleY);
            }

            float focal = Mathf.Max(outputWidth, outputHeight) * 0.9f;
            return new CameraIntrinsics(focal, focal, outputWidth * 0.5f, outputHeight * 0.5f);
        }

        private Texture2D ConvertCpuImage(XRCpuImage image)
        {
            int outputWidth = Mathf.Min(maxFrameWidth, image.width);
            int outputHeight = Mathf.Max(
                1,
                Mathf.RoundToInt(image.height * (outputWidth / (float)image.width)));
            XRCpuImage.ConversionParams conversion = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(outputWidth, outputHeight),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.None
            };

            using (NativeArray<byte> buffer = new NativeArray<byte>(
                       image.GetConvertedDataSize(conversion),
                       Allocator.Temp))
            {
                image.Convert(conversion, buffer);
                if (frameTexture == null
                    || frameTexture.width != outputWidth
                    || frameTexture.height != outputHeight)
                {
                    frameTexture = new Texture2D(
                        outputWidth,
                        outputHeight,
                        TextureFormat.RGBA32,
                        false);
                }

                frameTexture.LoadRawTextureData(buffer);
                frameTexture.Apply(false);
            }

            return frameTexture;
        }

        private static int ResolveFrameRotation(int width, int height)
        {
            if (Screen.height >= Screen.width && width > height)
            {
                return Screen.orientation == ScreenOrientation.PortraitUpsideDown ? 270 : 90;
            }

            return 0;
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
