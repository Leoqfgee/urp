using System;
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
    /// <summary>
    /// The single production A -> B -> C tracking path.
    ///
    /// A is the real damaged bottle observed by ARCameraManager.
    /// B is DamagedBottleB in the Blender-authored rigid asset.
    /// C is BottleCapC, a fixed sibling of B under BottleRepairRoot.
    ///
    /// Runtime code estimates only the complete six-degree-of-freedom pose of B
    /// and applies it to TrackedBottleRoot. It never positions C independently.
    /// </summary>
    public sealed class OrbImageTrackingController : MonoBehaviour
    {
        public enum TrackingState
        {
            Idle,
            PreAlignment,
            Searching,
            Candidate,
            PoseValidating,
            Repair,
            Lost
        }

        [Header("AR input")]
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private Camera arCamera;

        [Header("Rigid object-coordinate hierarchy")]
        [SerializeField] private Transform trackedObjectPoseRoot;
        [SerializeField] private Transform modelCoordinateAlignment;
        [SerializeField] private Transform occlusionRoot;
        [SerializeField] private Transform debugRoot;
        [SerializeField] private Text statusText;
        [SerializeField] private RepairAppearanceConsistencyController appearanceConsistency;

        [Header("Runtime profile")]
        [SerializeField] private RestorationObjectProfile activeProfile;
        [SerializeField] private int maxFrameWidth = 640;
        [SerializeField] private int minGoodMatches = 8;
        [SerializeField] private int minPoseInliers = 6;
        [SerializeField] private float minimumInlierRatio = 0.35f;
        [SerializeField] private float maximumReprojectionErrorPixels = 3.0f;
        [SerializeField] private float maximumReprojectionMaxPixels = 8.0f;
        [SerializeField] private float minimumCoverageX = 0.05f;
        [SerializeField] private float minimumCoverageY = 0.18f;
        [SerializeField] private float ratioTest = 0.72f;
        [SerializeField] private float relocationIntervalSeconds = 0.14f;

        [Header("World-space B+C pre-alignment")]
        [SerializeField] private float preAlignmentDistanceMeters = 0.35f;
        [SerializeField] private float preAlignmentMouthHeightMeters = 0.105f;
        [Range(0.08f, 0.35f)]
        [SerializeField] private float guidedMatchRadiusFraction = 0.18f;
        [SerializeField] private float maximumInitialCorrectionMeters = 0.30f;
        [SerializeField] private float maximumInitialCorrectionDegrees = 60f;

        [Header("Stable full-pose registration")]
        [SerializeField] private int registrationConfirmationFrames = 8;
        [SerializeField] private float registrationPositionToleranceMeters = 0.025f;
        [SerializeField] private float registrationRotationToleranceDegrees = 8f;
        [SerializeField] private float temporaryLossHoldSeconds = 0.35f;
        [Range(0.01f, 1f)]
        [SerializeField] private float positionSmoothing = 0.30f;
        [Range(0.01f, 1f)]
        [SerializeField] private float rotationSmoothing = 0.25f;

        private readonly List<NativeOrbTracker> trackers = new List<NativeOrbTracker>();
        private Texture2D frameTexture;
        private Transform registeredBottlePairRoot;
        private Transform registeredReferenceModel;
        private Transform registeredRepairPart;
        private Renderer[] referenceRenderers = Array.Empty<Renderer>();
        private Renderer[] repairRenderers = Array.Empty<Renderer>();
        private RepairCalibrationProfile calibration;
        private bool modeEnabled;
        private bool recognitionRunning;
        private bool registrationEstablished;
        private bool hasSmoothedPose;
        private int registrationStableFrames;
        private float nextProcessTime;
        private float lastValidPoseTime = float.NegativeInfinity;
        private Vector3 registrationAveragePosition;
        private Quaternion registrationAverageRotation = Quaternion.identity;
        private Vector3 lastCandidatePosition;
        private Quaternion lastCandidateRotation = Quaternion.identity;
        private Vector3 lastAcceptedPosition;
        private Quaternion lastAcceptedRotation = Quaternion.identity;
        private Vector3 smoothedRootPosition;
        private Quaternion smoothedRootRotation = Quaternion.identity;
        private TrackingState trackingState = TrackingState.Idle;

        public bool HasTrackedPose => registrationEstablished;
        public bool IsRigidRegistrationEstablished => registrationEstablished;
        public bool IsRepairMode =>
            registrationEstablished && trackingState == TrackingState.Repair;
        public TrackingState State => trackingState;
        public bool IsRepairActuallyRenderable =>
            ValidateRigidHierarchy(out _) && AnyEnabled(repairRenderers);

        private void Awake()
        {
            SetReferenceHierarchyVisible(false);
            SetRepairHierarchyVisible(false);
            if (activeProfile != null)
            {
                SetProfile(activeProfile);
            }
        }

        private void OnDestroy()
        {
            DisposeTrackers();
            if (frameTexture != null)
            {
                Destroy(frameTexture);
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

        public void SetProfile(RestorationObjectProfile profile)
        {
            if (ReferenceEquals(activeProfile, profile)
                && registeredBottlePairRoot != null
                && trackers.Count > 0)
            {
                return;
            }

            activeProfile = profile;
            calibration = profile != null ? profile.calibration : null;
            ApplyTrackingSettings(profile != null ? profile.trackingSettings : null);
            DisposeTrackers();
            DestroyRegisteredPair();

            if (profile == null)
            {
                ResetTracking();
                return;
            }
            if (trackedObjectPoseRoot == null || modelCoordinateAlignment == null)
            {
                throw new MissingReferenceException(
                    "TrackedBottleRoot and ModelCoordinateAlignment are required.");
            }
            if (trackedObjectPoseRoot.parent != null)
            {
                throw new InvalidOperationException(
                    "TrackedBottleRoot must remain a world root.");
            }

            modelCoordinateAlignment.localPosition = calibration != null
                ? calibration.orbToModelLocalPosition
                : Vector3.zero;
            modelCoordinateAlignment.localRotation = Quaternion.Euler(
                calibration != null
                    ? calibration.orbToModelLocalEulerAngles
                    : Vector3.zero);
            modelCoordinateAlignment.localScale = calibration != null
                ? calibration.orbToModelLocalScale
                : Vector3.one;

            if (profile.registeredBottlePairPrefab == null)
            {
                throw new MissingReferenceException(
                    "The Blender-authored BottleRepairRoot prefab is missing.");
            }

            GameObject instance = Instantiate(
                profile.registeredBottlePairPrefab,
                modelCoordinateAlignment);
            instance.name = "BottleFullAlignedV2";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            registeredReferenceModel = FindDescendant(instance.transform, "DamagedBottleB");
            registeredRepairPart = FindDescendant(instance.transform, "BottleCapC");
            registeredBottlePairRoot = FindDescendant(instance.transform, "BottleRepairRoot");
            if (registeredBottlePairRoot == null
                && registeredReferenceModel != null
                && registeredReferenceModel.parent == registeredRepairPart?.parent)
            {
                registeredBottlePairRoot = registeredReferenceModel.parent;
            }

            if (!ValidateRigidHierarchy(out string hierarchyReason))
            {
                DestroyRuntimeObject(instance);
                registeredBottlePairRoot = null;
                registeredReferenceModel = null;
                registeredRepairPart = null;
                throw new MissingReferenceException(hierarchyReason);
            }

            referenceRenderers =
                registeredReferenceModel.GetComponentsInChildren<Renderer>(true);
            repairRenderers =
                registeredRepairPart.GetComponentsInChildren<Renderer>(true);
            if (referenceRenderers.Length == 0 || repairRenderers.Length == 0)
            {
                throw new MissingReferenceException(
                    "DamagedBottleB and BottleCapC must each contain a Renderer.");
            }

            foreach (Collider collider in
                     registeredBottlePairRoot.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            ApplyMaterial(referenceRenderers, profile.viewerMaterial);
            ApplyMaterial(
                repairRenderers,
                profile.repairMaterial != null
                    ? profile.repairMaterial
                    : profile.viewerMaterial);
            foreach (Renderer renderer in referenceRenderers)
            {
                PrepareOverlayRenderer(renderer);
            }
            foreach (Renderer renderer in repairRenderers)
            {
                PrepareOverlayRenderer(renderer);
            }
            if (appearanceConsistency != null)
            {
                appearanceConsistency.BindRepairRenderers(repairRenderers);
            }
            if (occlusionRoot != null)
            {
                occlusionRoot.gameObject.SetActive(false);
            }
            if (debugRoot != null)
            {
                debugRoot.gameObject.SetActive(false);
            }

            BuildTrackers();
            ResetTracking();
        }

        public void SetTrackingEnabled(bool enabled)
        {
            modeEnabled = enabled;
            recognitionRunning = false;
            ResetRegistration();

            if (!enabled)
            {
                trackingState = TrackingState.Idle;
                SetReferenceHierarchyVisible(false);
                SetRepairHierarchyVisible(false);
                UpdateStatus(string.Empty);
                return;
            }
            if (activeProfile == null)
            {
                trackingState = TrackingState.Idle;
                SetReferenceHierarchyVisible(false);
                SetRepairHierarchyVisible(false);
                UpdateStatus("尚未选择跟踪对象。");
                return;
            }
            if (!activeProfile.HasTrackingAssets)
            {
                trackingState = TrackingState.Idle;
                SetReferenceHierarchyVisible(false);
                SetRepairHierarchyVisible(false);
                UpdateStatus($"{activeProfile.displayName} 的新模型 B 或三维特征库不可用。");
                return;
            }
            PlacePreAlignmentPose();
            UpdateStatus(
                "已显示带纹理的 Blender 对齐 B+C。移动手机，让 B "
                + "与真实残缺瓶 A 大致重合，然后点击“开始”。");
        }

        public void StartRecognition()
        {
            if (!modeEnabled
                || activeProfile == null
                || !activeProfile.HasTrackingAssets
                || trackers.Count == 0)
            {
                UpdateStatus("当前对象尚不具备可用的 A→B 三维跟踪资源。");
                return;
            }

            ResetRegistration();
            recognitionRunning = true;
            trackingState = TrackingState.Searching;
            nextProcessTime = 0f;
            ShowRepairPresentation();
            UpdateStatus(
                "已切换为只显示瓶盖 C；B 仅写深度用于遮挡。"
                + "正在以当前粗对齐姿态引导真实照片 ORB 特征和 A→B 多点 PnP。");
        }

        public void ResetTracking()
        {
            recognitionRunning = false;
            ResetRegistration();
            if (modeEnabled)
            {
                PlacePreAlignmentPose();
                UpdateStatus(
                    "已重置并重新显示初始 B+C。移动手机让 B 粗略覆盖 A，"
                    + "然后点击“开始”。");
            }
            else
            {
                trackingState = TrackingState.Idle;
                SetReferenceHierarchyVisible(false);
                SetRepairHierarchyVisible(false);
            }
        }

        private void PlacePreAlignmentPose()
        {
            trackingState = TrackingState.PreAlignment;
            if (trackedObjectPoseRoot == null || arCamera == null || calibration == null)
            {
                SetReferenceHierarchyVisible(false);
                SetRepairHierarchyVisible(false);
                return;
            }
            if (trackedObjectPoseRoot.parent != null)
            {
                throw new InvalidOperationException(
                    "TrackedBottleRoot must remain a world root.");
            }

            Transform cameraTransform = arCamera.transform;
            trackedObjectPoseRoot.position =
                cameraTransform.position
                + cameraTransform.forward * preAlignmentDistanceMeters
                + cameraTransform.up * preAlignmentMouthHeightMeters;
            trackedObjectPoseRoot.rotation = Quaternion.LookRotation(
                -cameraTransform.forward,
                cameraTransform.up);
            trackedObjectPoseRoot.localScale =
                Vector3.one * calibration.metersPerModelUnit;
            smoothedRootPosition = trackedObjectPoseRoot.position;
            smoothedRootRotation = trackedObjectPoseRoot.rotation;
            hasSmoothedPose = true;
            ShowPreAlignmentPair();
        }

        private bool SetCurrentPosePrior(NativeOrbTracker tracker)
        {
            if (tracker == null)
            {
                return false;
            }
            if (!TryBuildCurrentPosePrior(out float[] rotationTranslation))
            {
                tracker.ClearPosePrior();
                return false;
            }
            return tracker.SetPosePrior(
                rotationTranslation,
                guidedMatchRadiusFraction);
        }

        private bool TryBuildCurrentPosePrior(out float[] rotationTranslation)
        {
            rotationTranslation = null;
            if (arCamera == null
                || modelCoordinateAlignment == null
                || calibration == null
                || calibration.metersPerModelUnit <= 0f)
            {
                return false;
            }

            Vector3 originWorld =
                modelCoordinateAlignment.TransformPoint(Vector3.zero);
            Vector3 originCameraUnity =
                arCamera.transform.InverseTransformPoint(originWorld);
            Vector3 originCameraCv = new Vector3(
                originCameraUnity.x,
                -originCameraUnity.y,
                originCameraUnity.z) / calibration.metersPerModelUnit;
            if (!IsFinite(originCameraCv) || originCameraCv.z <= 0f)
            {
                return false;
            }

            // OpenCvUnityPoseConverter reconstructs Unity orientation from
            // OpenCV up/forward. Reversing that handedness conversion requires
            // the model-right column to be negated here.
            Vector3 right = -ModelDirectionToCameraCv(Vector3.right);
            Vector3 up = ModelDirectionToCameraCv(Vector3.up);
            Vector3 forward = ModelDirectionToCameraCv(Vector3.forward);
            if (right.sqrMagnitude < 0.000001f
                || up.sqrMagnitude < 0.000001f
                || forward.sqrMagnitude < 0.000001f)
            {
                return false;
            }
            right.Normalize();
            up = Vector3.ProjectOnPlane(up, right).normalized;
            forward = Vector3.Cross(right, up).normalized;
            up = Vector3.Cross(forward, right).normalized;

            rotationTranslation = new[]
            {
                right.x, up.x, forward.x, originCameraCv.x,
                right.y, up.y, forward.y, originCameraCv.y,
                right.z, up.z, forward.z, originCameraCv.z
            };
            return true;
        }

        private Vector3 ModelDirectionToCameraCv(Vector3 modelDirection)
        {
            Vector3 worldDirection =
                modelCoordinateAlignment.TransformVector(modelDirection);
            Vector3 cameraDirection =
                arCamera.transform.InverseTransformVector(worldDirection);
            return new Vector3(
                cameraDirection.x,
                -cameraDirection.y,
                cameraDirection.z);
        }

        public void SetRepairHierarchyVisible(bool visible)
        {
            SetRenderersEnabled(repairRenderers, visible);
        }

        public void SetReferenceHierarchyVisible(bool visible)
        {
            SetRenderersEnabled(referenceRenderers, visible);
        }

        public void ShowRepairPresentation()
        {
            if (activeProfile == null)
            {
                SetReferenceHierarchyVisible(false);
                SetRepairHierarchyVisible(false);
                return;
            }
            ApplyMaterial(
                referenceRenderers,
                activeProfile.referenceDepthOcclusionMaterial);
            ApplyMaterial(
                repairRenderers,
                activeProfile.repairMaterial != null
                    ? activeProfile.repairMaterial
                    : activeProfile.viewerMaterial);
            SetReferenceHierarchyVisible(true);
            SetRepairHierarchyVisible(true);
        }

        private void ShowPreAlignmentPair()
        {
            if (activeProfile == null)
            {
                return;
            }
            ApplyMaterial(referenceRenderers, activeProfile.viewerMaterial);
            ApplyMaterial(
                repairRenderers,
                activeProfile.repairMaterial != null
                    ? activeProfile.repairMaterial
                    : activeProfile.viewerMaterial);
            SetReferenceHierarchyVisible(true);
            SetRepairHierarchyVisible(true);
        }

        public void HideFailedProfileVisuals()
        {
            ResetTracking();
        }

        private void ProcessCameraFrame()
        {
            nextProcessTime = Time.unscaledTime + relocationIntervalSeconds;
            if (cameraManager == null
                || arCamera == null
                || calibration == null
                || trackedObjectPoseRoot == null
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
                int rotationClockwise =
                    ResolveFrameRotation(texture.width, texture.height);
                byte[] rgba = NativeOrbTracker.GetRgbaBytes(texture);

                NativeOrbResult best = default;
                bool hasResult = false;
                foreach (NativeOrbTracker tracker in trackers)
                {
                    SetCurrentPosePrior(tracker);
                    tracker.Track(
                        rgba,
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

                string qualityReason = string.Empty;
                if (!hasResult || !PassesPoseQuality(best, out qualityReason))
                {
                    trackingState = hasResult
                        ? TrackingState.Candidate
                        : TrackingState.Searching;
                    HandleTrackingLoss();
                    UpdateStatus(hasResult
                        ? qualityReason
                        : "尚未找到可用于 PnP 的 B 特征。");
                    return;
                }

                if (!OpenCvUnityPoseConverter.TryGetObjectPose(
                        best,
                        rotationClockwise,
                        arCamera,
                        calibration,
                        out Vector3 targetPosition,
                        out Quaternion targetRotation))
                {
                    HandleTrackingLoss();
                    UpdateStatus("PnP 已返回，但三维坐标转换无效。");
                    return;
                }

                if (!registrationEstablished)
                {
                    float initialPositionCorrection =
                        Vector3.Distance(trackedObjectPoseRoot.position, targetPosition);
                    float initialRotationCorrection =
                        Quaternion.Angle(trackedObjectPoseRoot.rotation, targetRotation);
                    if (initialPositionCorrection > maximumInitialCorrectionMeters
                        || initialRotationCorrection > maximumInitialCorrectionDegrees)
                    {
                        trackingState = TrackingState.Candidate;
                        UpdateStatus(
                            $"PnP 与当前 B 粗对齐差异过大："
                            + $"{initialPositionCorrection:F2}m，"
                            + $"{initialRotationCorrection:F0}°。"
                            + "请重置后先让 B 大致覆盖 A。");
                        return;
                    }

                    ApplyTrackedRootPose(targetPosition, targetRotation, false);
                    ShowRepairPresentation();
                    trackingState = TrackingState.PoseValidating;
                    if (!TryAccumulateStableRegistration(
                            targetPosition,
                            targetRotation,
                            out Vector3 stablePosition,
                            out Quaternion stableRotation,
                            out string stabilityReason))
                    {
                        UpdateStatus(stabilityReason);
                        return;
                    }

                    ApplyTrackedRootPose(stablePosition, stableRotation, false);
                    registrationEstablished = true;
                    trackingState = TrackingState.Repair;
                    ShowRepairPresentation();
                }
                else
                {
                    float positionJump =
                        Vector3.Distance(lastAcceptedPosition, targetPosition);
                    float rotationJump =
                        Quaternion.Angle(lastAcceptedRotation, targetRotation);
                    if (positionJump > registrationPositionToleranceMeters * 2f
                        || rotationJump > registrationRotationToleranceDegrees * 2f)
                    {
                        HandleTrackingLoss();
                        UpdateStatus(
                            $"A→B 位姿跳变被拒绝：{positionJump:F3}m，"
                            + $"{rotationJump:F1}°。");
                        return;
                    }
                    ApplyTrackedRootPose(targetPosition, targetRotation, true);
                }

                lastAcceptedPosition = targetPosition;
                lastAcceptedRotation = targetRotation;
                lastValidPoseTime = Time.unscaledTime;
                trackingState = TrackingState.Repair;
                ShowRepairPresentation();
                UpdateStatus(
                    $"B 已稳定配准 A：内点 {best.poseInliers}/"
                    + $"{best.uniqueMatches}，RMS {best.reprojectionError:F2}px。"
                    + "B 只写深度而不写颜色；C 继承同一三维位姿并获得正确遮挡。");
            }
            finally
            {
                image.Dispose();
            }
        }

        private bool PassesPoseQuality(NativeOrbResult result, out string reason)
        {
            if (result.uniqueMatches < minGoodMatches)
            {
                reason =
                    $"B 特征匹配 {result.uniqueMatches}/{minGoodMatches}，"
                    + "尚不足以求解完整 PnP。";
                return false;
            }
            if (result.poseValid == 0)
            {
                reason =
                    $"PnP 未通过：内点 {result.poseInliers}/"
                    + $"{result.uniqueMatches}，代码 {result.rejectionCode}。";
                return false;
            }
            int requiredInliers = Mathf.Max(
                minPoseInliers,
                Mathf.CeilToInt(result.uniqueMatches * minimumInlierRatio));
            if (result.poseInliers < requiredInliers
                || result.inlierRatio < minimumInlierRatio)
            {
                reason =
                    $"PnP 内点 {result.poseInliers}/{result.uniqueMatches}，"
                    + $"需要至少 {requiredInliers} 个且比例不低于 "
                    + $"{minimumInlierRatio:P0}。";
                return false;
            }
            if (result.coverageX < minimumCoverageX
                || result.coverageY < minimumCoverageY
                || result.occupiedGridCells < 4)
            {
                reason =
                    $"匹配分布不足：水平 {result.coverageX:P0}，"
                    + $"垂直 {result.coverageY:P0}，网格 {result.occupiedGridCells}。";
                return false;
            }
            if (!float.IsFinite(result.reprojectionError)
                || result.reprojectionError > maximumReprojectionErrorPixels
                || !float.IsFinite(result.reprojectionMax)
                || result.reprojectionMax > maximumReprojectionMaxPixels)
            {
                reason =
                    $"多点重投影误差过大：RMS {result.reprojectionError:F2}px，"
                    + $"最大 {result.reprojectionMax:F2}px。";
                return false;
            }
            if (!float.IsFinite(result.tvecX)
                || !float.IsFinite(result.tvecY)
                || !float.IsFinite(result.tvecZ)
                || result.tvecZ <= 0f)
            {
                reason = "PnP 深度无效。";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private bool TryAccumulateStableRegistration(
            Vector3 position,
            Quaternion rotation,
            out Vector3 stablePosition,
            out Quaternion stableRotation,
            out string reason)
        {
            int requiredFrames = Mathf.Max(2, registrationConfirmationFrames);
            if (registrationStableFrames == 0)
            {
                registrationStableFrames = 1;
                registrationAveragePosition = position;
                registrationAverageRotation = rotation;
                lastCandidatePosition = position;
                lastCandidateRotation = rotation;
            }
            else
            {
                float positionJump =
                    Vector3.Distance(lastCandidatePosition, position);
                float rotationJump =
                    Quaternion.Angle(lastCandidateRotation, rotation);
                if (positionJump > registrationPositionToleranceMeters
                    || rotationJump > registrationRotationToleranceDegrees)
                {
                    registrationStableFrames = 1;
                    registrationAveragePosition = position;
                    registrationAverageRotation = rotation;
                }
                else
                {
                    registrationStableFrames++;
                    float weight = 1f / registrationStableFrames;
                    registrationAveragePosition = Vector3.Lerp(
                        registrationAveragePosition,
                        position,
                        weight);
                    registrationAverageRotation = Quaternion.Slerp(
                        registrationAverageRotation,
                        rotation,
                        weight);
                }
                lastCandidatePosition = position;
                lastCandidateRotation = rotation;
            }

            stablePosition = registrationAveragePosition;
            stableRotation = registrationAverageRotation;
            if (registrationStableFrames < requiredFrames)
            {
                reason =
                    $"正在确认 A→B 六自由度位姿 "
                    + $"{registrationStableFrames}/{requiredFrames}；"
                    + "B 与 C 保持 Blender 固定关系并共同跟随候选位姿。";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private void ApplyTrackedRootPose(
            Vector3 position,
            Quaternion rotation,
            bool smooth)
        {
            if (trackedObjectPoseRoot == null || calibration == null)
            {
                return;
            }
            if (trackedObjectPoseRoot.parent != null)
            {
                throw new InvalidOperationException(
                    "TrackedBottleRoot must remain outside Camera, Canvas and AR anchors.");
            }

            if (!hasSmoothedPose || !smooth)
            {
                smoothedRootPosition = position;
                smoothedRootRotation = rotation;
                hasSmoothedPose = true;
            }
            else
            {
                smoothedRootPosition = Vector3.Lerp(
                    smoothedRootPosition,
                    position,
                    positionSmoothing);
                smoothedRootRotation = Quaternion.Slerp(
                    smoothedRootRotation,
                    rotation,
                    rotationSmoothing);
            }
            trackedObjectPoseRoot.position = smoothedRootPosition;
            trackedObjectPoseRoot.rotation = smoothedRootRotation;
            trackedObjectPoseRoot.localScale =
                Vector3.one * calibration.metersPerModelUnit;
        }

        private void HandleTrackingLoss()
        {
            TrackingState previousState = trackingState;
            trackingState = TrackingState.Lost;
            if (!registrationEstablished)
            {
                // Start already switched to C plus depth-only B. Keep that
                // world-space coarse pose while searching.
                ShowRepairPresentation();
                return;
            }
            if (registrationEstablished
                && Time.unscaledTime - lastValidPoseTime <= temporaryLossHoldSeconds)
            {
                // The root remains a world-space object pose, never a screen
                // coordinate. AR camera motion still changes perspective.
                trackingState = previousState;
                ShowRepairPresentation();
                return;
            }

            registrationEstablished = false;
            registrationStableFrames = 0;
            hasSmoothedPose = false;
            SetReferenceHierarchyVisible(false);
            SetRepairHierarchyVisible(false);
        }

        private void ResetRegistration()
        {
            registrationEstablished = false;
            registrationStableFrames = 0;
            hasSmoothedPose = false;
            lastValidPoseTime = float.NegativeInfinity;
            registrationAveragePosition = Vector3.zero;
            registrationAverageRotation = Quaternion.identity;
            lastCandidatePosition = Vector3.zero;
            lastCandidateRotation = Quaternion.identity;
            lastAcceptedPosition = Vector3.zero;
            lastAcceptedRotation = Quaternion.identity;
        }

        private void BuildTrackers()
        {
            if (activeProfile == null
                || activeProfile.trackingReferenceDatabase == null)
            {
                return;
            }
            try
            {
                NativeOrbTracker tracker =
                    new NativeOrbTracker(2600, ratioTest, minGoodMatches, maxFrameWidth);
                if (tracker.IsValid
                    && tracker.SetModel(activeProfile.trackingReferenceDatabase))
                {
                    trackers.Add(tracker);
                }
                else
                {
                    tracker.Dispose();
                }
            }
            catch (DllNotFoundException)
            {
                // The production plugin is Android ARM64-only. Static Editor
                // validation still exercises the hierarchy and Renderer gate.
                if (!Application.isEditor)
                {
                    throw;
                }
            }
        }

        private void DisposeTrackers()
        {
            foreach (NativeOrbTracker tracker in trackers)
            {
                tracker.Dispose();
            }
            trackers.Clear();
        }

        private void DestroyRegisteredPair()
        {
            if (registeredBottlePairRoot != null)
            {
                Transform outer = registeredBottlePairRoot;
                while (outer.parent != null && outer.parent != modelCoordinateAlignment)
                {
                    outer = outer.parent;
                }
                DestroyRuntimeObject(outer.gameObject);
            }
            registeredBottlePairRoot = null;
            registeredReferenceModel = null;
            registeredRepairPart = null;
            referenceRenderers = Array.Empty<Renderer>();
            repairRenderers = Array.Empty<Renderer>();
        }

        private bool ValidateRigidHierarchy(out string reason)
        {
            if (registeredBottlePairRoot == null
                || registeredReferenceModel == null
                || registeredRepairPart == null)
            {
                reason =
                    "新模型必须包含 BottleRepairRoot/DamagedBottleB/BottleCapC。";
                return false;
            }
            if (registeredReferenceModel.parent != registeredBottlePairRoot
                || registeredRepairPart.parent != registeredBottlePairRoot)
            {
                reason = "B 与 C 必须是 BottleRepairRoot 下的固定同级子对象。";
                return false;
            }
            if (!registeredBottlePairRoot.IsChildOf(modelCoordinateAlignment))
            {
                reason = "BottleRepairRoot 必须位于 ModelCoordinateAlignment 下。";
                return false;
            }
            if (arCamera != null
                && registeredRepairPart.IsChildOf(arCamera.transform))
            {
                reason = "C 不能挂在 AR Camera 下。";
                return false;
            }
            if (registeredRepairPart.GetComponentInParent<Canvas>() != null
                || registeredRepairPart.GetComponent<RectTransform>() != null)
            {
                reason = "C 不能挂在 Canvas 或二维 UI 下。";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private void ApplyTrackingSettings(TrackingSettings settings)
        {
            if (settings == null)
            {
                return;
            }
            minGoodMatches = settings.minimumGoodMatches;
            minPoseInliers = settings.minimumPoseInliers;
            minimumInlierRatio = settings.minimumInlierRatio;
            maximumReprojectionErrorPixels =
                settings.maximumReprojectionErrorPixels;
            maximumReprojectionMaxPixels =
                settings.maximumReprojectionMaxPixels;
            minimumCoverageX = settings.minimumCoverageX;
            minimumCoverageY = settings.minimumCoverageY;
            registrationConfirmationFrames =
                settings.registrationConfirmationFrames;
            registrationPositionToleranceMeters =
                settings.registrationPositionToleranceMeters;
            registrationRotationToleranceDegrees =
                settings.registrationRotationToleranceDegrees;
            temporaryLossHoldSeconds = settings.temporaryLossHoldSeconds;
            positionSmoothing = settings.positionSmoothing;
            rotationSmoothing = settings.rotationSmoothing;
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
            return new CameraIntrinsics(
                focal,
                focal,
                outputWidth * 0.5f,
                outputHeight * 0.5f);
        }

        private Texture2D ConvertCpuImage(XRCpuImage image)
        {
            int outputWidth = Mathf.Min(maxFrameWidth, image.width);
            int outputHeight = Mathf.Max(
                1,
                Mathf.RoundToInt(image.height * (outputWidth / (float)image.width)));
            XRCpuImage.ConversionParams conversion =
                new XRCpuImage.ConversionParams
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
                    if (frameTexture != null)
                    {
                        Destroy(frameTexture);
                    }
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
            if (width <= height)
            {
                return 0;
            }
            switch (Screen.orientation)
            {
                case ScreenOrientation.Portrait:
                    return 90;
                case ScreenOrientation.PortraitUpsideDown:
                    return 270;
                case ScreenOrientation.LandscapeLeft:
                    return 180;
                case ScreenOrientation.LandscapeRight:
                    return 0;
                default:
                    return Screen.height >= Screen.width ? 90 : 0;
            }
        }

        private static bool IsBetter(NativeOrbResult current, NativeOrbResult best)
        {
            if (current.poseValid != best.poseValid)
            {
                return current.poseValid > best.poseValid;
            }
            if (current.poseInliers != best.poseInliers)
            {
                return current.poseInliers > best.poseInliers;
            }
            if (current.poseValid != 0
                && current.reprojectionError != best.reprojectionError)
            {
                return current.reprojectionError < best.reprojectionError;
            }
            return current.uniqueMatches > best.uniqueMatches;
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == objectName)
            {
                return root;
            }
            for (int index = 0; index < root.childCount; index++)
            {
                Transform found = FindDescendant(root.GetChild(index), objectName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void ApplyMaterial(Renderer[] renderers, Material material)
        {
            if (material == null)
            {
                return;
            }
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }
                int count = Mathf.Max(1, renderer.sharedMaterials.Length);
                Material[] materials = new Material[count];
                for (int index = 0; index < count; index++)
                {
                    materials[index] = material;
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static void PrepareOverlayRenderer(Renderer renderer)
        {
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void SetRenderersEnabled(Renderer[] renderers, bool enabled)
        {
            if (renderers == null)
            {
                return;
            }
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = enabled;
                }
            }
        }

        private static bool AnyEnabled(Renderer[] renderers)
        {
            if (renderers == null)
            {
                return false;
            }
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null
                    && renderer.enabled
                    && renderer.gameObject.activeInHierarchy)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z);
        }

        private static void DestroyRuntimeObject(UnityEngine.Object value)
        {
            if (value == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
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
