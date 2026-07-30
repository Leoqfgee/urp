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
        [SerializeField] private float preAlignmentMouthHeightMeters = 0f;
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

        [Header("AR world-pose stabilization")]
        [SerializeField] private float worldPositionDeadbandMeters = 0.003f;
        [SerializeField] private float worldRotationDeadbandDegrees = 1.5f;
        [SerializeField] private float maximumWorldPositionCorrectionMetersPerSecond = 0.018f;
        [SerializeField] private float maximumWorldRotationCorrectionDegreesPerSecond = 6f;

        private readonly List<NativeOrbTracker> trackers = new List<NativeOrbTracker>();
        private Texture2D frameTexture;
        private Transform registeredBottlePairRoot;
        private Transform registeredReferenceModel;
        private Transform registeredRepairPart;
        private Renderer[] referenceRenderers = Array.Empty<Renderer>();
        private Renderer[] repairRenderers = Array.Empty<Renderer>();
        private Renderer[] geometricOcclusionRenderers = Array.Empty<Renderer>();
        private RepairCalibrationProfile calibration;
        private bool modeEnabled;
        private bool recognitionRunning;
        private bool repairRequested;
        private bool hasEverRegisteredSinceReset;
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
        private float lastRootPoseApplicationTime = float.NegativeInfinity;
        private bool sessionCoordinateFrameCalibrated;
        private bool hasReadyPoseCandidate;
        private Vector3 readyCandidatePosition;
        private Quaternion readyCandidateRotation = Quaternion.identity;
        private float readyCandidateTime = float.NegativeInfinity;
        private TrackingState trackingState = TrackingState.Idle;

        public bool HasTrackedPose => registrationEstablished;
        public bool IsRigidRegistrationEstablished => registrationEstablished;
        public bool IsRepairMode =>
            repairRequested
            && registrationEstablished
            && trackingState == TrackingState.Repair;
        public TrackingState State => trackingState;
        public bool IsRepairActuallyRenderable =>
            ValidateRigidHierarchy(out _)
            && AnyEnabled(repairRenderers)
            && IsRepairProjectedIntoCamera();

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
            instance.name = "BottleCleanCapV25";
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
                FindNamedRenderers(referenceRenderers, "ReferenceNeckProxyB"),
                profile.repairMaterial != null
                    ? profile.repairMaterial
                    : profile.viewerMaterial);
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
            BuildGeometricOcclusionProxy(profile);
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
            repairRequested = false;
            hasEverRegisteredSinceReset = false;
            ResetRegistration();

            if (!enabled)
            {
                trackingState = TrackingState.Idle;
                SetReferenceHierarchyVisible(false);
                SetRepairHierarchyVisible(false);
                SetGeometricOcclusionVisible(false);
                UpdateStatus(string.Empty);
                return;
            }
            if (activeProfile == null)
            {
                trackingState = TrackingState.Idle;
                SetReferenceHierarchyVisible(false);
                SetRepairHierarchyVisible(false);
                SetGeometricOcclusionVisible(false);
                UpdateStatus("尚未选择跟踪对象。");
                return;
            }
            if (!activeProfile.HasTrackingAssets)
            {
                trackingState = TrackingState.Idle;
                SetReferenceHierarchyVisible(false);
                SetRepairHierarchyVisible(false);
                SetGeometricOcclusionVisible(false);
                UpdateStatus($"{activeProfile.displayName} 的新模型 B 或三维特征库不可用。");
                return;
            }
            PlacePreAlignmentPose();
            recognitionRunning = true;
            nextProcessTime = 0f;
            UpdateStatus(
                "已在画面中央正面显示 Blender 对齐的 B+C，识别已经开始。"
                + "移动手机让 B 与真实残缺瓶 A 大致重合；识别稳定后点击“开始”。");
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

            repairRequested = true;
            nextProcessTime = 0f;
            if (registrationEstablished)
            {
                trackingState = TrackingState.Repair;
                ShowRepairPresentation();
                UpdateStatus(
                    "A 与 B 的三维姿态已经稳定。现在隐藏 B 的颜色，只显示瓶盖 C；"
                    + "B 仍保留完整位置和旋转关系，但其 Renderer 已关闭。");
            }
            else if (hasReadyPoseCandidate
                     && Time.unscaledTime - readyCandidateTime <= 0.75f)
            {
                EstablishRegistration(
                    readyCandidatePosition,
                    readyCandidateRotation);
                UpdateStatus(
                    "已用当前粗对齐自动标定 B 与 ORB 重建坐标系，B 已隐藏且只显示瓶盖 C。"
                    + "之后 C 只继承 B 的完整三维跟踪位姿。");
            }
            else
            {
                ShowPreAlignmentPair();
                UpdateStatus(
                    "已收到“开始”。正在继续确认 A 与 B 的稳定三维姿态；"
                    + "确认完成前保留 B+C，完成后会自动隐藏 B 的颜色。");
            }
        }

        public void ResetTracking()
        {
            recognitionRunning = modeEnabled;
            repairRequested = false;
            hasEverRegisteredSinceReset = false;
            ResetRegistration();
            if (modeEnabled)
            {
                PlacePreAlignmentPose();
                nextProcessTime = 0f;
                UpdateStatus(
                    "已重置。B+C 已回到画面中央的正面初始姿态，识别正在运行。"
                    + "移动手机让 B 粗略覆盖 A，识别稳定后点击“开始”。");
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
            trackedObjectPoseRoot.rotation =
                CalculatePreAlignmentRotation(cameraTransform);
            trackedObjectPoseRoot.localScale =
                Vector3.one * calibration.metersPerModelUnit;
            smoothedRootPosition = trackedObjectPoseRoot.position;
            smoothedRootRotation = trackedObjectPoseRoot.rotation;
            hasSmoothedPose = true;
            ShowPreAlignmentPair();
        }

        private Quaternion CalculatePreAlignmentRotation(Transform cameraTransform)
        {
            // The authored mesh uses +X as the printed front and +Y from the
            // body towards the mouth. Unity's FBX importer keeps an extra
            // axis-conversion transform (currently -90 degrees around X) on
            // BottleRepairRoot, so those are not the outer root's +X/+Y axes.
            // Read the rendered B axes through the complete imported hierarchy
            // and map them to a front-facing, upright camera-space frame.
            Quaternion canonicalModelInRoot =
                GetCanonicalModelRotationInTrackedRoot();
            Vector3 modelFrontInRoot =
                canonicalModelInRoot * Vector3.forward;
            Vector3 modelUpInRoot =
                canonicalModelInRoot * Vector3.up;
            modelFrontInRoot.Normalize();
            modelUpInRoot.Normalize();

            Quaternion importedModelFrame = Quaternion.LookRotation(
                modelFrontInRoot,
                modelUpInRoot);
            Quaternion desiredCameraFrame = Quaternion.LookRotation(
                -cameraTransform.forward,
                cameraTransform.up);
            return desiredCameraFrame * Quaternion.Inverse(importedModelFrame);
        }

        private Quaternion GetCanonicalModelRotationInTrackedRoot()
        {
            if (registeredReferenceModel == null || trackedObjectPoseRoot == null)
            {
                return Quaternion.identity;
            }

            Vector3 canonicalUpInRoot =
                trackedObjectPoseRoot.InverseTransformDirection(
                    registeredReferenceModel.TransformDirection(Vector3.up));
            Vector3 canonicalFrontInRoot =
                trackedObjectPoseRoot.InverseTransformDirection(
                    registeredReferenceModel.TransformDirection(Vector3.right));
            canonicalUpInRoot.Normalize();
            canonicalFrontInRoot = Vector3.ProjectOnPlane(
                canonicalFrontInRoot,
                canonicalUpInRoot).normalized;
            return Quaternion.LookRotation(
                canonicalFrontInRoot,
                canonicalUpInRoot);
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
                registrationEstablished
                    ? Mathf.Min(0.09f, guidedMatchRadiusFraction)
                    : guidedMatchRadiusFraction);
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

            // TrackedBottleRoot is the canonical ORB object frame. Imported
            // FBX axis conversion belongs below ModelCoordinateAlignment and
            // must never be applied a second time to the native pose prior.
            Quaternion priorFrameInRoot = Quaternion.identity;
            Vector3 originWorld = sessionCoordinateFrameCalibrated
                ? trackedObjectPoseRoot.position
                : trackedObjectPoseRoot.TransformPoint(
                    priorFrameInRoot * calibration.objectOriginInModel);
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

            // OpenCV camera coordinates and Unity camera coordinates have
            // opposite handedness. Reflect the model's semantic right axis
            // once when building the proper OpenCV rotation. This is
            // calibration-driven: v25 printed-front is +X and object-right
            // is -Z, so hard-coding the raw X column was incorrect.
            Vector3 semanticRight = calibration.RightInModel.normalized;
            Vector3 columnX = ModelDirectionToCameraCv(
                priorFrameInRoot * ReflectAcrossAxis(
                    Vector3.right,
                    semanticRight));
            Vector3 columnY = ModelDirectionToCameraCv(
                priorFrameInRoot * ReflectAcrossAxis(
                    Vector3.up,
                    semanticRight));
            Vector3 columnZ = ModelDirectionToCameraCv(
                priorFrameInRoot * ReflectAcrossAxis(
                    Vector3.forward,
                    semanticRight));
            if (columnX.sqrMagnitude < 0.000001f
                || columnY.sqrMagnitude < 0.000001f
                || columnZ.sqrMagnitude < 0.000001f)
            {
                return false;
            }
            columnX.Normalize();
            columnY = Vector3.ProjectOnPlane(columnY, columnX).normalized;
            columnZ = Vector3.Cross(columnX, columnY).normalized;
            columnY = Vector3.Cross(columnZ, columnX).normalized;

            rotationTranslation = new[]
            {
                columnX.x, columnY.x, columnZ.x, originCameraCv.x,
                columnX.y, columnY.y, columnZ.y, originCameraCv.y,
                columnX.z, columnY.z, columnZ.z, originCameraCv.z
            };
            return true;
        }

        private static Vector3 ReflectAcrossAxis(
            Vector3 value,
            Vector3 axis)
        {
            return value - 2f * Vector3.Dot(value, axis) * axis;
        }

        private Vector3 ModelDirectionToCameraCv(Vector3 modelDirection)
        {
            Vector3 worldDirection =
                trackedObjectPoseRoot.TransformVector(modelDirection);
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

        private void SetGeometricOcclusionVisible(bool visible)
        {
            SetRenderersEnabled(geometricOcclusionRenderers, visible);
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
                repairRenderers,
                activeProfile.repairMaterial != null
                    ? activeProfile.repairMaterial
                    : activeProfile.viewerMaterial);
            // B stays in the hierarchy as the tracked rigid reference, but its
            // noisy photogrammetry surface must not depth-occlude C. Device
            // environment depth is also disabled by UrpAppController for this
            // glossy close-range bottle, because it can swallow C completely.
            SetReferenceHierarchyVisible(false);
            SetRepairHierarchyVisible(true);
            SetGeometricOcclusionVisible(true);
        }

        private void ShowPreAlignmentPair()
        {
            if (activeProfile == null)
            {
                return;
            }
            ApplyMaterial(
                referenceRenderers,
                activeProfile.viewerMaterial);
            ApplyMaterial(
                FindNamedRenderers(referenceRenderers, "ReferenceNeckProxyB"),
                activeProfile.repairMaterial != null
                    ? activeProfile.repairMaterial
                    : activeProfile.viewerMaterial);
            ApplyMaterial(
                repairRenderers,
                activeProfile.repairMaterial != null
                    ? activeProfile.repairMaterial
                    : activeProfile.viewerMaterial);
            SetReferenceHierarchyVisible(true);
            SetRepairHierarchyVisible(true);
            SetGeometricOcclusionVisible(false);
        }

        private void ShowPresentationForCurrentState()
        {
            if (repairRequested && hasEverRegisteredSinceReset)
            {
                ShowRepairPresentation();
            }
            else
            {
                ShowPreAlignmentPair();
            }
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
                        : "尚未在真实瓶身 A 中找到足够稳定的 B 自然特征。");
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
                    UpdateStatus("已找到自然特征，但三维姿态坐标转换无效。");
                    return;
                }
                if (appearanceConsistency != null
                    && best.sampledConfidence > 0f)
                {
                    appearanceConsistency.ObserveReferenceHsv(
                        best.sampledHue,
                        best.sampledSaturation,
                        best.sampledValue,
                        best.sampledConfidence);
                }

                if (!registrationEstablished)
                {
                    float initialPositionCorrection =
                        Vector3.Distance(trackedObjectPoseRoot.position, targetPosition);
                    float initialRotationCorrection =
                        Quaternion.Angle(trackedObjectPoseRoot.rotation, targetRotation);
                    if (initialPositionCorrection > maximumInitialCorrectionMeters
                        || (sessionCoordinateFrameCalibrated
                            && initialRotationCorrection
                                > maximumInitialCorrectionDegrees))
                    {
                        trackingState = TrackingState.Candidate;
                        UpdateStatus(
                            $"识别姿态与当前 B 粗对齐差异过大："
                            + $"{initialPositionCorrection:F2}m，"
                            + $"{initialRotationCorrection:F0}°。"
                            + "请重置后先让 B 大致覆盖 A。");
                        return;
                    }

                    ShowPresentationForCurrentState();
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

                    hasReadyPoseCandidate = true;
                    readyCandidatePosition = stablePosition;
                    readyCandidateRotation = stableRotation;
                    readyCandidateTime = Time.unscaledTime;
                    if (!repairRequested)
                    {
                        trackingState = TrackingState.Candidate;
                        UpdateStatus(
                            $"已在点击开始前识别到稳定瓶身姿态：内点 {best.poseInliers}/"
                            + $"{best.uniqueMatches}，误差 {best.reprojectionError:F2}px。"
                            + "请让半透明 B 粗略覆盖真实 A，然后点击“开始”。");
                        return;
                    }

                    EstablishRegistration(stablePosition, stableRotation);
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
                trackingState = repairRequested
                    ? TrackingState.Repair
                    : TrackingState.PreAlignment;
                ShowPresentationForCurrentState();
                if (repairRequested)
                {
                    UpdateStatus(
                        $"A 与 B 已稳定跟踪：有效内点 {best.poseInliers}/"
                        + $"{best.uniqueMatches}，重投影误差 "
                        + $"{best.reprojectionError:F2}px。"
                        + "B 的 Renderer 已关闭；C 继承 B 的完整三维姿态。");
                }
                else
                {
                    UpdateStatus(
                        $"已识别并稳定跟踪瓶子：有效内点 {best.poseInliers}/"
                        + $"{best.uniqueMatches}，误差 "
                        + $"{best.reprojectionError:F2}px。"
                        + "请确认 B 覆盖真实瓶身 A，然后点击“开始”。");
                }
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
                    + "尚不足以确认完整三维姿态。";
                return false;
            }
            if (result.poseValid == 0)
            {
                reason =
                    $"三维姿态尚未通过：内点 {result.poseInliers}/"
                    + $"{result.uniqueMatches}。请保持瓶身清晰并缓慢移动手机。";
                return false;
            }
            int requiredInliers = Mathf.Max(
                minPoseInliers,
                Mathf.CeilToInt(result.uniqueMatches * minimumInlierRatio));
            if (result.poseInliers < requiredInliers
                || result.inlierRatio < minimumInlierRatio)
            {
                reason =
                    $"有效姿态内点 {result.poseInliers}/{result.uniqueMatches}，"
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
                reason = "识别得到的三维深度无效，请让瓶身完整进入画面。";
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
                // AR Foundation already supplies the camera's continuous
                // world motion. Keep the registered object almost stationary
                // in that world and let PnP correct only slow accumulated
                // drift. Driving the root with every raw PnP sample amplified
                // cylindrical-bottle yaw noise into a visibly shaking cap.
                float elapsed = float.IsFinite(lastRootPoseApplicationTime)
                    ? Mathf.Clamp(
                        Time.unscaledTime - lastRootPoseApplicationTime,
                        0.02f,
                        0.25f)
                    : Mathf.Max(0.02f, relocationIntervalSeconds);
                float positionError =
                    Vector3.Distance(smoothedRootPosition, position);
                if (positionError > worldPositionDeadbandMeters)
                {
                    float positionStep = Mathf.Min(
                        positionError - worldPositionDeadbandMeters,
                        maximumWorldPositionCorrectionMetersPerSecond * elapsed);
                    smoothedRootPosition = Vector3.MoveTowards(
                        smoothedRootPosition,
                        position,
                        positionStep);
                }

                float rotationError =
                    Quaternion.Angle(smoothedRootRotation, rotation);
                if (rotationError > worldRotationDeadbandDegrees)
                {
                    float rotationStep = Mathf.Min(
                        rotationError - worldRotationDeadbandDegrees,
                        maximumWorldRotationCorrectionDegreesPerSecond * elapsed);
                    smoothedRootRotation = Quaternion.RotateTowards(
                        smoothedRootRotation,
                        rotation,
                        rotationStep);
                }
            }
            trackedObjectPoseRoot.position = smoothedRootPosition;
            trackedObjectPoseRoot.rotation = smoothedRootRotation;
            trackedObjectPoseRoot.localScale =
                Vector3.one * calibration.metersPerModelUnit;
            lastRootPoseApplicationTime = Time.unscaledTime;
        }

        private void EstablishRegistration(
            Vector3 orbRootPosition,
            Quaternion orbRootRotation)
        {
            // The production ORB database is authored in the exact same
            // mouth-centred canonical frame as Blender B+C.  Apply that full
            // six-degree-of-freedom pose directly.  The former session
            // compensation preserved the coarse upright overlay instead of
            // the measured pitch/roll, which made C stay front-facing in
            // oblique and top-down views and could move it outside the camera
            // frustum after Start.
            modelCoordinateAlignment.localPosition =
                calibration.orbToModelLocalPosition;
            modelCoordinateAlignment.localRotation = Quaternion.Euler(
                calibration.orbToModelLocalEulerAngles);
            modelCoordinateAlignment.localScale =
                calibration.orbToModelLocalScale;
            ApplyTrackedRootPose(
                orbRootPosition,
                orbRootRotation,
                false);
            sessionCoordinateFrameCalibrated = true;

            registrationEstablished = true;
            hasEverRegisteredSinceReset = true;
            lastAcceptedPosition = orbRootPosition;
            lastAcceptedRotation = orbRootRotation;
            lastValidPoseTime = Time.unscaledTime;
            trackingState = repairRequested
                ? TrackingState.Repair
                : TrackingState.PreAlignment;
            ShowPresentationForCurrentState();
        }

        private void HandleTrackingLoss()
        {
            TrackingState previousState = trackingState;
            trackingState = TrackingState.Lost;
            if (!registrationEstablished)
            {
                // Before the first valid registration the visible B+C pair
                // remains at its world-space coarse pose. After a prior lock,
                // keep the last C pose while relocalizing; never move it to a
                // camera or screen coordinate.
                ShowPresentationForCurrentState();
                return;
            }
            if (registrationEstablished
                && Time.unscaledTime - lastValidPoseTime <= temporaryLossHoldSeconds)
            {
                // The root remains a world-space object pose, never a screen
                // coordinate. AR camera motion still changes perspective.
                trackingState = previousState;
                ShowPresentationForCurrentState();
                return;
            }

            registrationEstablished = false;
            registrationStableFrames = 0;
            // Retain the last accepted world pose while ORB relocalizes. AR
            // camera motion continues to provide correct perspective during
            // the hold; the next accepted pose is validated before correction.
            ShowPresentationForCurrentState();
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
            sessionCoordinateFrameCalibrated = false;
            hasReadyPoseCandidate = false;
            readyCandidatePosition = Vector3.zero;
            readyCandidateRotation = Quaternion.identity;
            readyCandidateTime = float.NegativeInfinity;
            lastRootPoseApplicationTime = float.NegativeInfinity;
            RestoreProfileCoordinateAlignment();
        }

        private void RestoreProfileCoordinateAlignment()
        {
            if (modelCoordinateAlignment == null)
            {
                return;
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
            geometricOcclusionRenderers = Array.Empty<Renderer>();
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

        private static Renderer[] FindNamedRenderers(
            Renderer[] renderers,
            string nameFragment)
        {
            if (renderers == null || string.IsNullOrEmpty(nameFragment))
            {
                return Array.Empty<Renderer>();
            }
            List<Renderer> matches = new List<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null
                    && renderer.name.IndexOf(
                        nameFragment,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matches.Add(renderer);
                }
            }
            return matches.ToArray();
        }

        private void BuildGeometricOcclusionProxy(
            RestorationObjectProfile profile)
        {
            geometricOcclusionRenderers = Array.Empty<Renderer>();
            if (profile == null
                || profile.referenceDepthOcclusionMaterial == null
                || registeredBottlePairRoot == null
                || registeredReferenceModel == null)
            {
                return;
            }

            Transform source = FindDescendant(
                registeredReferenceModel,
                "ReferenceNeckProxyB");
            if (source == null)
            {
                return;
            }

            GameObject depthProxy = Instantiate(
                source.gameObject,
                registeredBottlePairRoot);
            depthProxy.name = "ReferenceNeckDepthOccluder";
            depthProxy.transform.localPosition = source.localPosition;
            depthProxy.transform.localRotation = source.localRotation;
            depthProxy.transform.localScale = source.localScale;
            geometricOcclusionRenderers =
                depthProxy.GetComponentsInChildren<Renderer>(true);
            ApplyMaterial(
                geometricOcclusionRenderers,
                profile.referenceDepthOcclusionMaterial);
            foreach (Renderer renderer in geometricOcclusionRenderers)
            {
                PrepareOverlayRenderer(renderer);
            }
            SetGeometricOcclusionVisible(false);
        }

        private bool IsRepairProjectedIntoCamera()
        {
            if (arCamera == null
                || repairRenderers == null
                || repairRenderers.Length == 0)
            {
                return false;
            }

            bool hasPositiveDepth = false;
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            foreach (Renderer renderer in repairRenderers)
            {
                if (renderer == null
                    || !renderer.enabled
                    || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                Vector3 centre = bounds.center;
                Vector3 extents = bounds.extents;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 screen = arCamera.WorldToScreenPoint(
                                centre + Vector3.Scale(
                                    extents,
                                    new Vector3(x, y, z)));
                            if (screen.z <= arCamera.nearClipPlane)
                            {
                                continue;
                            }
                            hasPositiveDepth = true;
                            minX = Mathf.Min(minX, screen.x);
                            minY = Mathf.Min(minY, screen.y);
                            maxX = Mathf.Max(maxX, screen.x);
                            maxY = Mathf.Max(maxY, screen.y);
                        }
                    }
                }
            }

            if (!hasPositiveDepth)
            {
                return false;
            }
            float width = Mathf.Max(1f, arCamera.pixelWidth);
            float height = Mathf.Max(1f, arCamera.pixelHeight);
            bool overlapsViewport =
                maxX >= 0f && maxY >= 0f && minX <= width && minY <= height;
            return overlapsViewport
                && maxX - minX >= 4f
                && maxY - minY >= 4f;
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
                    renderer.forceRenderingOff = false;
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
