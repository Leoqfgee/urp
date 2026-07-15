using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Urp.ArDemo.Native;

namespace Urp.ArDemo
{
    public sealed class OrbImageTrackingController : MonoBehaviour
    {
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private Camera arCamera;
        [SerializeField] private Transform trackedContentRoot;
        [SerializeField] private Text statusText;
        [SerializeField] private TextAsset[] orbModelFiles;
        [SerializeField] private float processIntervalSeconds = 0.22f;
        [SerializeField] private int maxFrameWidth = 640;
        [SerializeField] private int searchTargetsPerFrame = 2;
        [SerializeField] private int minGoodMatches = 18;
        [SerializeField] private float ratioTest = 0.72f;
        [SerializeField] private float smoothing = 0.35f;
        [SerializeField] private Vector3 repairAnchorInModel = new Vector3(0.43f, -4.38f, 0.24f);
        [SerializeField] private Vector3[] repairAnchorsByModel;
        [SerializeField] private Vector3 bottleUpInModel = new Vector3(0f, -1f, 0f);
        [SerializeField] private Vector3 bottleForwardInModel = new Vector3(0f, 0f, 1f);
        [SerializeField] private float modelUnitsToMeters = 0.18f;
        [SerializeField] private float pnpRepairScale = 1f;
        [SerializeField] private float lostStatusIntervalSeconds = 1.5f;
        [SerializeField] private float maxReprojectionErrorPixels = 4.5f;
        [SerializeField] private float lostPoseGraceSeconds = 0.65f;
        [SerializeField] private float maxViewportJump = 0.18f;
        [SerializeField] private Vector3 initialPreviewLocalPosition = new Vector3(0f, 0.11f, 0.55f);
        [SerializeField] private Vector3 initialPreviewLocalEulerAngles = Vector3.zero;

        private readonly List<TrackedTarget> targets = new List<TrackedTarget>();
        private Texture2D frameTexture;
        private float nextProcessTime;
        private float nextLostStatusTime;
        private float lastValidPoseTime = -10f;
        private bool hasSmoothedPose;
        private bool modeEnabled;
        private bool recognitionRunning;
        private int activeTargetIndex = -1;
        private int searchCursor;
        private int consecutiveActiveTargetMisses;
        private int consecutivePoseJumps;
        private Vector2 lastAcceptedViewport;
        private bool hasDisplayMatrix;
        private Matrix4x4 displayMatrix = Matrix4x4.identity;
        private Renderer[] repairRenderers;
        private MaterialPropertyBlock materialProperties;
        private float smoothedLuminance = 0.8f;
        private bool repairVisibleRequested = true;

        public bool HasTrackedPose => hasSmoothedPose;

        private void Awake()
        {
            materialProperties = new MaterialPropertyBlock();
            if (trackedContentRoot != null)
            {
                trackedContentRoot.gameObject.SetActive(false);
                repairRenderers = trackedContentRoot.GetComponentsInChildren<Renderer>(true);
            }

            BuildTargetFeatures();
        }

        private void OnEnable()
        {
            if (cameraManager != null)
            {
                cameraManager.frameReceived += OnCameraFrameReceived;
            }
        }

        private void OnDisable()
        {
            if (cameraManager != null)
            {
                cameraManager.frameReceived -= OnCameraFrameReceived;
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].Tracker?.Dispose();
            }
        }

        private void Update()
        {
            if (!modeEnabled || !recognitionRunning || Time.unscaledTime < nextProcessTime)
            {
                return;
            }

            nextProcessTime = Time.unscaledTime + processIntervalSeconds;
            ProcessCameraFrame();
        }

        private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
        {
            if (eventArgs.displayMatrix.HasValue)
            {
                displayMatrix = eventArgs.displayMatrix.Value;
                hasDisplayMatrix = true;
            }
        }

        public void ResetTracking()
        {
            hasSmoothedPose = false;
            recognitionRunning = false;
            activeTargetIndex = -1;
            searchCursor = 0;
            consecutiveActiveTargetMisses = 0;
            consecutivePoseJumps = 0;
            lastValidPoseTime = -10f;
            nextProcessTime = 0f;
            ShowInitialPose();
            UpdateStatus("已恢复瓶盖初始位姿。移动手机使瓶盖与真实瓶口大致对齐，再点击开始。");
        }

        public void StartRecognition()
        {
            if (!modeEnabled)
            {
                return;
            }

            recognitionRunning = true;
            nextProcessTime = 0f;
            UpdateStatus("已开始 ORB 三维跟踪。请保持瓶口和瓶身文字清晰可见。");
        }

        public void SetRepairVisible(bool visible)
        {
            repairVisibleRequested = visible;
            if (trackedContentRoot != null)
            {
                trackedContentRoot.gameObject.SetActive(visible && hasSmoothedPose);
            }
        }

        public void SetTrackingEnabled(bool enabled)
        {
            modeEnabled = enabled;
            recognitionRunning = false;
            if (enabled)
            {
                ShowInitialPose();
                UpdateStatus("瓶盖已按初始位姿显示。移动手机使其与瓶口大致对齐，再点击开始。");
            }
            else
            {
                HideRepairModel(true);
            }
        }

        public void BindStatusText(Text value)
        {
            statusText = value;
        }

        private void BuildTargetFeatures()
        {
            targets.Clear();
            if (orbModelFiles == null || orbModelFiles.Length == 0)
            {
                UpdateStatus("没有加载残缺饮料瓶的 ORB 三维特征库。");
                return;
            }

            for (int i = 0; i < orbModelFiles.Length; i++)
            {
                TextAsset model = orbModelFiles[i];
                if (model == null)
                {
                    continue;
                }

                NativeOrbTracker tracker = new NativeOrbTracker(1400, ratioTest, minGoodMatches, maxFrameWidth);
                if (!tracker.IsValid || !tracker.SetModel(model))
                {
                    tracker.Dispose();
                    continue;
                }

                Vector3 repairAnchor = repairAnchorInModel;
                if (repairAnchorsByModel != null && i < repairAnchorsByModel.Length)
                {
                    repairAnchor = repairAnchorsByModel[i];
                }

                tracker.SetRepairAnchor(repairAnchor);
                targets.Add(new TrackedTarget(tracker, repairAnchor, i + 1));
            }

            UpdateStatus(targets.Count > 0
                ? $"已加载 {targets.Count} 组 ORB 三维特征，等待识别残缺饮料瓶。"
                : "ORB 三维特征库加载失败。");
        }

        private void ProcessCameraFrame()
        {
            if (cameraManager == null || arCamera == null || trackedContentRoot == null || targets.Count == 0)
            {
                return;
            }

            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
            {
                return;
            }

            try
            {
                Texture2D texture = ConvertCpuImage(cpuImage);
                CameraIntrinsics intrinsics = GetCameraIntrinsics(
                    cpuImage.width,
                    cpuImage.height,
                    texture.width,
                    texture.height);
                int rotationClockwise = ResolveFrameRotation(texture.width, texture.height);
                byte[] frameRgba = NativeOrbTracker.GetRgbaBytes(texture);

                TrackedTarget bestTarget = null;
                NativeOrbResult bestResult = default;
                int bestTargetIndex = -1;
                bool hasResult = false;

                if (activeTargetIndex >= 0 && activeTargetIndex < targets.Count)
                {
                    TrackedTarget activeTarget = targets[activeTargetIndex];
                    bool tracked = activeTarget.Tracker.Track(
                        frameRgba,
                        texture.width,
                        texture.height,
                        intrinsics,
                        rotationClockwise,
                        out NativeOrbResult activeResult);
                    bestResult = activeResult;
                    hasResult = true;
                    if (tracked)
                    {
                        bestTarget = activeTarget;
                        bestTargetIndex = activeTargetIndex;
                        consecutiveActiveTargetMisses = 0;
                    }
                    else if (++consecutiveActiveTargetMisses >= 3)
                    {
                        activeTargetIndex = -1;
                        consecutiveActiveTargetMisses = 0;
                    }
                }

                if (bestTarget == null && activeTargetIndex < 0)
                {
                    int checks = Mathf.Clamp(searchTargetsPerFrame, 1, targets.Count);
                    for (int offset = 0; offset < checks; offset++)
                    {
                        int index = (searchCursor + offset) % targets.Count;
                        TrackedTarget candidate = targets[index];
                        candidate.Tracker.Track(
                            frameRgba,
                            texture.width,
                            texture.height,
                            intrinsics,
                            rotationClockwise,
                            out NativeOrbResult result);
                        if (!hasResult || IsBetterResult(result, bestResult))
                        {
                            bestResult = result;
                            bestTarget = result.poseValid != 0 ? candidate : null;
                            bestTargetIndex = result.poseValid != 0 ? index : -1;
                            hasResult = true;
                        }
                    }

                    searchCursor = (searchCursor + checks) % targets.Count;
                }

                if (bestTarget == null || bestResult.poseValid == 0)
                {
                    if (hasSmoothedPose)
                    {
                        HideRepairModel(false);
                    }
                    if (Time.unscaledTime >= nextLostStatusTime)
                    {
                        nextLostStatusTime = Time.unscaledTime + lostStatusIntervalSeconds;
                        UpdateStatus($"未获得稳定三维位姿：最高匹配点 {bestResult.goodMatches}/{minGoodMatches}。");
                    }

                    return;
                }

                activeTargetIndex = bestTargetIndex;
                consecutiveActiveTargetMisses = 0;
                if (!TryApplyTrackedOverlayPose(bestResult, bestTarget.RepairAnchor, rotationClockwise, out string rejectionReason))
                {
                    HideRepairModel(false);
                    UpdateStatus($"已识别瓶身，但位姿未通过叠加校验：{rejectionReason}");
                    return;
                }

                trackedContentRoot.gameObject.SetActive(repairVisibleRequested);
                lastValidPoseTime = Time.unscaledTime;
                ApplyLightingConsistency(bestResult.localLuminance);
                UpdateStatus(
                    $"已识别残缺瓶视角 {bestTarget.Index}：匹配点 {bestResult.goodMatches}，" +
                    $"PnP 内点 {bestResult.poseInliers}，重投影误差 {bestResult.reprojectionError:F1}px，叠加稳定。");
            }
            finally
            {
                cpuImage.Dispose();
            }
        }

        private static bool IsBetterResult(NativeOrbResult current, NativeOrbResult best)
        {
            if (current.poseValid != best.poseValid)
            {
                return current.poseValid > best.poseValid;
            }

            if (current.poseValid != 0 && !Mathf.Approximately(current.reprojectionError, best.reprojectionError))
            {
                return current.reprojectionError < best.reprojectionError;
            }

            if (current.poseInliers != best.poseInliers)
            {
                return current.poseInliers > best.poseInliers;
            }

            return current.goodMatches > best.goodMatches;
        }

        private CameraIntrinsics GetCameraIntrinsics(int sourceWidth, int sourceHeight, int outputWidth, int outputHeight)
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

        private static int ResolveFrameRotation(int width, int height)
        {
            if (Screen.height >= Screen.width && width > height)
            {
                return Screen.orientation == ScreenOrientation.PortraitUpsideDown ? 270 : 90;
            }

            return 0;
        }

        private Texture2D ConvertCpuImage(XRCpuImage cpuImage)
        {
            int outputWidth = Mathf.Min(maxFrameWidth, cpuImage.width);
            int outputHeight = Mathf.Max(1, Mathf.RoundToInt(cpuImage.height * (outputWidth / (float)cpuImage.width)));
            XRCpuImage.ConversionParams parameters = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                outputDimensions = new Vector2Int(outputWidth, outputHeight),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.None
            };

            int size = cpuImage.GetConvertedDataSize(parameters);
            using (NativeArray<byte> buffer = new NativeArray<byte>(size, Allocator.Temp))
            {
                cpuImage.Convert(parameters, buffer);
                if (frameTexture == null || frameTexture.width != outputWidth || frameTexture.height != outputHeight)
                {
                    frameTexture = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false);
                }

                frameTexture.LoadRawTextureData(buffer);
                frameTexture.Apply(false);
            }

            return frameTexture;
        }

        private bool TryApplyTrackedOverlayPose(
            NativeOrbResult result,
            Vector3 repairAnchor,
            int rotationClockwise,
            out string rejectionReason)
        {
            rejectionReason = string.Empty;
            if (result.anchorVisible == 0)
            {
                rejectionReason = "瓶口锚点不在画面内";
                return false;
            }

            if (!float.IsFinite(result.reprojectionError) || result.reprojectionError > maxReprojectionErrorPixels)
            {
                rejectionReason = $"重投影误差 {result.reprojectionError:F1}px 过大";
                return false;
            }

            Vector3 anchorInCamera = TransformModelPoint(result, repairAnchor);
            Vector3 cameraLocalAnchor = CvToUnity(anchorInCamera) * modelUnitsToMeters;
            float depthMeters = cameraLocalAnchor.z;
            if (depthMeters < 0.08f || depthMeters > 5f)
            {
                rejectionReason = "估计距离超出有效范围";
                return false;
            }

            Vector2 rawViewport = OrientedToSourceViewport(
                new Vector2(result.anchorX01, result.anchorY01),
                rotationClockwise);
            Vector2 targetViewport = ResolveDisplayViewport(rawViewport);
            if (targetViewport.x < 0.02f || targetViewport.x > 0.98f
                || targetViewport.y < 0.02f || targetViewport.y > 0.98f)
            {
                rejectionReason = "校正后的瓶口位置超出屏幕";
                return false;
            }

            if (hasSmoothedPose && Vector2.Distance(lastAcceptedViewport, targetViewport) > maxViewportJump)
            {
                consecutivePoseJumps++;
                if (consecutivePoseJumps < 3)
                {
                    rejectionReason = "检测到位姿跳变，正在复核";
                    return false;
                }
            }
            else
            {
                consecutivePoseJumps = 0;
            }

            Ray anchorRay = arCamera.ViewportPointToRay(new Vector3(targetViewport.x, targetViewport.y, 0f));
            Vector3 rayInCamera = arCamera.transform.InverseTransformDirection(anchorRay.direction);
            float rayDistance = depthMeters / Mathf.Max(0.001f, rayInCamera.z);
            Vector3 targetPosition = anchorRay.origin + anchorRay.direction * rayDistance;

            Vector3 upInCamera = TransformModelDirection(result, bottleUpInModel);
            Vector3 forwardInCamera = TransformModelDirection(result, bottleForwardInModel);
            upInCamera = UndoFrameRotation(upInCamera, rotationClockwise);
            forwardInCamera = UndoFrameRotation(forwardInCamera, rotationClockwise);
            Vector3 targetUp = arCamera.transform.TransformDirection(CvToUnity(upInCamera)).normalized;
            Vector3 targetForward = arCamera.transform.TransformDirection(CvToUnity(forwardInCamera)).normalized;
            targetForward = Vector3.ProjectOnPlane(targetForward, targetUp).normalized;
            if (targetForward.sqrMagnitude < 0.0001f)
            {
                targetForward = Vector3.ProjectOnPlane(arCamera.transform.forward, targetUp).normalized;
            }

            Quaternion targetRotation = Quaternion.LookRotation(targetForward, targetUp);
            ApplySmoothedPose(targetPosition, targetRotation, Vector3.one * pnpRepairScale);
            lastAcceptedViewport = targetViewport;
            return true;
        }

        private Vector2 ResolveDisplayViewport(Vector2 sourceViewport)
        {
            if (!hasDisplayMatrix)
            {
                return new Vector2(sourceViewport.x, 1f - sourceViewport.y);
            }

            // ARCoreBackground.shader maps display UV to camera texture UV as:
            // displayMatrix * (screenX, 1-screenY, 1, 0). Invert that exact
            // operation to place the CPU-image repair anchor on the displayed frame.
            Vector2 textureUv = new Vector2(sourceViewport.x, 1f - sourceViewport.y);
            Vector4 displayInput = displayMatrix.inverse * new Vector4(textureUv.x, textureUv.y, 1f, 0f);
            return new Vector2(displayInput.x, 1f - displayInput.y);
        }

        private static Vector2 OrientedToSourceViewport(Vector2 oriented, int rotationClockwise)
        {
            switch (rotationClockwise)
            {
                case 90:
                    return new Vector2(1f - oriented.y, oriented.x);
                case 180:
                    return new Vector2(1f - oriented.x, 1f - oriented.y);
                case 270:
                    return new Vector2(oriented.y, 1f - oriented.x);
                default:
                    return oriented;
            }
        }

        private static Vector3 TransformModelPoint(NativeOrbResult result, Vector3 point)
        {
            return TransformModelDirection(result, point)
                + new Vector3(result.tvecX, result.tvecY, result.tvecZ);
        }

        private static Vector3 TransformModelDirection(NativeOrbResult result, Vector3 direction)
        {
            return new Vector3(
                result.r00 * direction.x + result.r01 * direction.y + result.r02 * direction.z,
                result.r10 * direction.x + result.r11 * direction.y + result.r12 * direction.z,
                result.r20 * direction.x + result.r21 * direction.y + result.r22 * direction.z);
        }

        private static Vector3 CvToUnity(Vector3 point)
        {
            return new Vector3(point.x, -point.y, point.z);
        }

        private static Vector3 UndoFrameRotation(Vector3 point, int rotationClockwise)
        {
            switch (rotationClockwise)
            {
                case 90:
                    return new Vector3(point.y, -point.x, point.z);
                case 180:
                    return new Vector3(-point.x, -point.y, point.z);
                case 270:
                    return new Vector3(-point.y, point.x, point.z);
                default:
                    return point;
            }
        }

        private void ApplyLightingConsistency(float measuredLuminance)
        {
            if (!float.IsFinite(measuredLuminance) || measuredLuminance <= 0f || repairRenderers == null)
            {
                return;
            }

            smoothedLuminance = Mathf.Lerp(smoothedLuminance, Mathf.Clamp01(measuredLuminance), 0.2f);
            float value = Mathf.Lerp(0.56f, 1f, smoothedLuminance);
            Color color = new Color(value, value * 0.99f, value * 0.97f, 1f);
            for (int i = 0; i < repairRenderers.Length; i++)
            {
                Renderer renderer = repairRenderers[i];
                if (renderer == null || renderer.gameObject.name.Contains("Occlusion"))
                {
                    continue;
                }

                renderer.GetPropertyBlock(materialProperties);
                materialProperties.SetColor("_Color", color);
                renderer.SetPropertyBlock(materialProperties);
            }
        }

        private void ApplySmoothedPose(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            trackedContentRoot.SetParent(null, true);
            if (!hasSmoothedPose)
            {
                trackedContentRoot.SetPositionAndRotation(position, rotation);
                trackedContentRoot.localScale = scale;
                hasSmoothedPose = true;
                return;
            }

            float amount = Mathf.Clamp01(smoothing);
            trackedContentRoot.position = Vector3.Lerp(trackedContentRoot.position, position, amount);
            trackedContentRoot.rotation = Quaternion.Slerp(trackedContentRoot.rotation, rotation, amount);
            trackedContentRoot.localScale = Vector3.Lerp(trackedContentRoot.localScale, scale, amount);
        }

        private void HideRepairModel(bool force)
        {
            if (!force && Time.unscaledTime - lastValidPoseTime <= lostPoseGraceSeconds)
            {
                return;
            }

            if (trackedContentRoot != null)
            {
                trackedContentRoot.gameObject.SetActive(false);
            }

            hasSmoothedPose = false;
        }

        private void ShowInitialPose()
        {
            if (trackedContentRoot == null || arCamera == null)
            {
                return;
            }

            hasSmoothedPose = false;
            trackedContentRoot.SetParent(arCamera.transform, false);
            trackedContentRoot.localPosition = initialPreviewLocalPosition;
            trackedContentRoot.localRotation = Quaternion.Euler(initialPreviewLocalEulerAngles);
            trackedContentRoot.localScale = Vector3.one * pnpRepairScale;
            trackedContentRoot.gameObject.SetActive(repairVisibleRequested);
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private sealed class TrackedTarget
        {
            public TrackedTarget(NativeOrbTracker tracker, Vector3 repairAnchor, int index)
            {
                Tracker = tracker;
                RepairAnchor = repairAnchor;
                Index = index;
            }

            public NativeOrbTracker Tracker { get; }
            public Vector3 RepairAnchor { get; }
            public int Index { get; }
        }
    }
}
