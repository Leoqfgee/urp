using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Serialization;
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
            StablePoseApplied,
            ReadyForRepair,
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
        [SerializeField] private CapVisibilityDiagnostic capVisibilityDiagnostic;
        [SerializeField] private PoseCoordinateDiagnostic poseCoordinateDiagnostic;

        [Header("Runtime profile")]
        [SerializeField] private RestorationObjectProfile activeProfile;
        [SerializeField] private int maxFrameWidth = 640;
        [SerializeField] private int minGoodMatches = 8;
        [SerializeField] private int minPoseInliers = 6;
        [SerializeField] private float minimumInlierRatio = 0.35f;
        [SerializeField] private float maximumReprojectionErrorPixels = 3.0f;
        [SerializeField] private float maximumReprojectionMaxPixels = 8.0f;
        [FormerlySerializedAs("maximumUnityCrossProjectionRmsPixels")]
        [SerializeField] private float displayProjectionWarningRmsPixels = 5.0f;
        [SerializeField] private float maximumPoseChainRoundTripRmsPixels = 0.25f;
        [SerializeField] private float maximumHierarchyTransformRoundTripRmsPixels = 0.50f;
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

        [Header("Stable full-pose registration")]
        [SerializeField] private int registrationConfirmationFrames = 8;
        [SerializeField] private int consistencyConfirmationFrames = 3;
        [SerializeField] private int consistencyFailureHoldFrames = 2;
        [SerializeField] private float registrationPositionToleranceMeters = 0.025f;
        [SerializeField] private float registrationRotationToleranceDegrees = 8f;
        [SerializeField] private float temporaryLossHoldSeconds = 0.35f;
        [SerializeField] private float startReliablePoseGraceSeconds = 0.75f;
        [Range(0.01f, 1f)]
        [SerializeField] private float positionSmoothing = 0.30f;
        [Range(0.01f, 1f)]
        [SerializeField] private float rotationSmoothing = 0.25f;

        private readonly List<NativeOrbTracker> trackers = new List<NativeOrbTracker>();
        private readonly List<Mesh> correctedVisualMeshes = new List<Mesh>();
        private Texture2D frameTexture;
        private Transform registeredBottlePairRoot;
        private Transform registeredReferenceModel;
        private Transform registeredReferenceNeck;
        private Transform registeredTrackingRegistrationProxy;
        private Transform registeredRepairOccluder;
        private Transform registeredRepairPart;
        private Renderer[] referenceBodyRenderers = Array.Empty<Renderer>();
        private Renderer[] referenceNeckRenderers = Array.Empty<Renderer>();
        private Renderer[] referenceRenderers = Array.Empty<Renderer>();
        private Renderer[] repairRenderers = Array.Empty<Renderer>();
        private Renderer[] repairOccluderRenderers = Array.Empty<Renderer>();
        private RepairCalibrationProfile calibration;
        private bool modeEnabled;
        private bool recognitionRunning;
        private bool repairRequested;
        private bool hasEverRegisteredSinceReset;
        private bool registrationEstablished;
        private bool stablePnpPoseAvailable;
        private bool poseAppliedToRigidRoot;
        private bool poseChainVerified;
        private bool hierarchyTransformRoundTripVerified;
        private bool modelRegistrationVerified;
        private ModelRegistrationEvidence modelRegistrationEvidence;
        private string modelRegistrationReason = string.Empty;
        private bool readyForRepair;
        private bool hasVerifiedReadyPoseSinceReset;
        private float lastVerifiedReadyPoseTime = float.NegativeInfinity;
        private float lastReliablePnpTime = float.NegativeInfinity;
        private int consistencyVerifiedFrames;
        private int consistencyFailureFrames;
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
        private float lastPoseFusionConfidence = 1f;
        private float lastPoseFusionPositionAlpha = 1f;
        private float lastPoseFusionRotationAlpha = 1f;
        private Vector3 derivedAlignmentPosition;
        private Quaternion derivedAlignmentRotation = Quaternion.identity;
        private Vector3 derivedAlignmentScale = Vector3.one;
        private Matrix4x4 derivedOrbToRenderedBMatrix = Matrix4x4.identity;
        private float derivedAlignmentLandmarkRms;
        private TrackingState trackingState = TrackingState.Idle;
        private readonly CameraFrameSample[] cameraFrameSamples =
            new CameraFrameSample[16];
        private int cameraFrameSampleCount;
        private int cameraFrameSampleWriteIndex;
        private int runtimeDatabaseRecords;
        private string runtimeDatabaseSha256 = "NONE";
        private string runtimeDatabaseShaPrefix = "UNKNOWN";
        private bool lastPosePriorWasReliable;

        private struct CameraFrameSample
        {
            public long timestampNs;
            public Vector3 position;
            public Quaternion rotation;
        }

        public bool HasTrackedPose => registrationEstablished;
        public bool IsRigidRegistrationEstablished => registrationEstablished;
        public bool StablePnpPoseAvailable => stablePnpPoseAvailable;
        public bool IsPoseAppliedToRigidRoot => poseAppliedToRigidRoot;
        public bool IsPoseChainVerified => poseChainVerified;
        public bool IsHierarchyTransformRoundTripVerified =>
            hierarchyTransformRoundTripVerified;
        public bool IsModelRegistrationVerified => modelRegistrationVerified;
        public bool HasVerifiedReadyPoseSinceReset =>
            hasVerifiedReadyPoseSinceReset;
        public float LastReliablePnpAgeSeconds => float.IsFinite(lastReliablePnpTime)
            ? Mathf.Max(0f, Time.unscaledTime - lastReliablePnpTime)
            : float.PositiveInfinity;
        public bool CanStartRepair =>
            hasVerifiedReadyPoseSinceReset
            && registrationEstablished
            && stablePnpPoseAvailable
            && poseAppliedToRigidRoot
            && modelRegistrationVerified
            && LastReliablePnpAgeSeconds < Mathf.Clamp(
                startReliablePoseGraceSeconds,
                0.5f,
                1.0f);
        public bool IsRepairMode =>
            repairRequested
            && registrationEstablished
            && trackingState == TrackingState.Repair;
        public TrackingState State => trackingState;
        public bool IsRepairActuallyRenderable =>
            ValidateRigidHierarchy(out _) && AnyEnabled(repairRenderers);

        private void Awake()
        {
            poseCoordinateDiagnostic?.HideAllDebugLines();
            SetReferenceHierarchyVisible(false);
            SetRepairHierarchyVisible(false);
            SetRepairOccluderVisible(false);
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

        private void OnEnable()
        {
            if (cameraManager != null)
            {
                cameraManager.frameReceived += OnCameraFrameReceived;
            }
        }

        private void OnDisable()
        {
            poseCoordinateDiagnostic?.HideAllDebugLines();
            if (cameraManager != null)
            {
                cameraManager.frameReceived -= OnCameraFrameReceived;
            }
        }

        private void LateUpdate()
        {
            if (!modeEnabled
                || !repairRequested
                || !hasEverRegisteredSinceReset)
            {
                return;
            }

            // Start changes presentation only. It must never deactivate C or
            // leave the authored B neck proxy in the colour pass. Reassert the
            // renderer contract after every frame so loss/relocalisation and
            // imported renderer state cannot make C disappear.
            ShowRepairPresentation();
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
            UpdateRuntimeDatabaseIdentity(
                profile != null ? profile.trackingReferenceDatabase : null);
            modelRegistrationVerified = ModelRegistrationEvidence.TryParse(
                calibration != null ? calibration.modelRegistrationArtifact : null,
                runtimeDatabaseSha256,
                out modelRegistrationEvidence,
                out modelRegistrationReason);
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

            modelCoordinateAlignment.localPosition = Vector3.zero;
            modelCoordinateAlignment.localRotation = Quaternion.identity;
            modelCoordinateAlignment.localScale = Vector3.one;

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
            registeredReferenceNeck = FindDescendant(
                instance.transform,
                "ReferenceNeckProxyB");
            registeredTrackingRegistrationProxy = FindDescendant(
                instance.transform,
                "BottleTrackingRegistrationProxy");
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

            // v43 bakes clean production B, its neck, and C together into the
            // proven A046 ORB frame. Runtime alignment only cancels Unity's
            // fixed FBX import-axis conversion; there is no empirical offset.
            derivedAlignmentPosition = calibration.orbToModelLocalPosition;
            derivedAlignmentRotation = Quaternion.Euler(
                calibration.orbToModelLocalEulerAngles);
            derivedAlignmentScale = calibration.orbToModelLocalScale;
            derivedOrbToRenderedBMatrix = Matrix4x4.identity;
            derivedAlignmentLandmarkRms = modelRegistrationEvidence != null
                ? modelRegistrationEvidence.landmark_rms_mm
                    / Mathf.Max(0.000001f, calibration.metersPerModelUnit * 1000f)
                : float.PositiveInfinity;
            RestoreProfileCoordinateAlignment();

            Renderer[] allReferenceRenderers =
                registeredReferenceModel.GetComponentsInChildren<Renderer>(true);
            referenceNeckRenderers = registeredReferenceNeck != null
                ? registeredReferenceNeck.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
            referenceBodyRenderers = ExcludeRenderers(
                allReferenceRenderers,
                referenceNeckRenderers);
            referenceRenderers = MergeRenderers(
                referenceBodyRenderers,
                referenceNeckRenderers);
            CorrectProductionVisualWinding(referenceBodyRenderers);
            repairRenderers =
                registeredRepairPart.GetComponentsInChildren<Renderer>(true);
            CreateRepairOccluder(profile.referenceDepthOcclusionMaterial);
            if (registeredTrackingRegistrationProxy != null)
            {
                foreach (Renderer renderer in registeredTrackingRegistrationProxy
                    .GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = false;
                    renderer.forceRenderingOff = true;
                }
            }
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
            if (capVisibilityDiagnostic != null)
            {
                capVisibilityDiagnostic.BindRigidTarget(
                    trackedObjectPoseRoot,
                    registeredBottlePairRoot,
                    registeredReferenceModel,
                    registeredRepairPart,
                    repairRenderers);
                capVisibilityDiagnostic.BindRepairOccluder(
                    registeredRepairOccluder,
                    repairOccluderRenderers);
            }
            if (poseCoordinateDiagnostic != null)
            {
                poseCoordinateDiagnostic.Bind(
                    arCamera,
                    cameraManager,
                    trackedObjectPoseRoot,
                    modelCoordinateAlignment,
                    registeredBottlePairRoot,
                    registeredReferenceModel,
                    calibration,
                    runtimeDatabaseSha256);
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
            repairRequested = false;
            hasEverRegisteredSinceReset = false;
            ResetRegistration();

            if (!enabled)
            {
                poseCoordinateDiagnostic?.HideAllDebugLines();
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
                || !activeProfile.HasTrackingAssets)
            {
                UpdateStatus("当前对象尚不具备可用的 A→B 三维跟踪资源。");
                return;
            }

            if (!CanStartRepair)
            {
                ShowPreAlignmentPair();
                UpdateStatus(
                    "A 与 B 尚未完成稳定对齐，请保持瓶子在画面中。"
                    + "只有 B+C 已应用可靠 PnP 位姿并正在跟踪时才可开始。");
                string diagnostic = BuildStartGateDiagnostic();
                Debug.LogWarning($"[URP_START_GATE_DIAG] {diagnostic}");
                UpdateStatus("START BLOCKED:\n" + diagnostic);
                return;
            }

            RigidPoseSnapshot before = CaptureRigidPoseSnapshot();
            capVisibilityDiagnostic?.LogSnapshot("start-before");

            // Start is a pure presentation gate. The stable A-to-B pose was
            // already applied before this method became eligible to run.
            repairRequested = true;
            trackingState = TrackingState.Repair;
            poseCoordinateDiagnostic?.HideAllDebugLines();
            ShowRepairPresentation();

            RigidPoseSnapshot after = CaptureRigidPoseSnapshot();
            AssertStartPoseUnchanged(before, after);
            capVisibilityDiagnostic?.LogSnapshot("start-after");
            UpdateStatus(
                "已隐藏参考瓶 B；瓶盖 C 保持 Start 前完全相同的三维位姿。"
                + "ORB/PnP 将继续驱动整个 B+C 刚性根节点。");
        }

        public void ResetTracking()
        {
            poseCoordinateDiagnostic?.HideAllDebugLines();
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
            UpdateStatus(BuildPreAlignmentFrontStatus(cameraTransform));
        }

        private string BuildPreAlignmentFrontStatus(Transform cameraTransform)
        {
            Vector3 printedFront = registeredReferenceModel.TransformDirection(
                (calibration.mouthFrontInModel - calibration.mouthCenterInModel).normalized);
            Vector3 bottleUp = registeredReferenceModel.TransformDirection(
                (calibration.mouthCenterInModel - calibration.neckAxisPointInModel).normalized);
            float frontAngle = Vector3.Angle(printedFront, -cameraTransform.forward);
            float upAngle = Vector3.Angle(bottleUp, cameraTransform.up);
            string status = $"PreAlignFront: +Z calibration angleToCamera={frontAngle:F2} deg\n"
                + $"PreAlignUp: calibration angleToCameraUp={upAngle:F2} deg";
            if (frontAngle > 2f || upAngle > 2f)
            {
                Debug.LogError("PREALIGNMENT_FRONT_VALIDATION_FAIL " + status);
            }
            else
            {
                Debug.Log("PREALIGNMENT_FRONT_IS_ACTUAL_PRINTED_FRONT_OK " + status);
            }
            return status;
        }

        private void UpdateRuntimeDatabaseIdentity(TextAsset database)
        {
            runtimeDatabaseRecords = 0;
            runtimeDatabaseSha256 = "NONE";
            runtimeDatabaseShaPrefix = "NONE";
            if (database == null || database.bytes == null || database.bytes.Length < 12)
            {
                return;
            }

            byte[] bytes = database.bytes;
            runtimeDatabaseRecords = BitConverter.ToInt32(bytes, 8);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                runtimeDatabaseSha256 = BitConverter.ToString(digest)
                    .Replace("-", string.Empty);
                runtimeDatabaseShaPrefix = runtimeDatabaseSha256.Substring(0, 8);
            }
        }

        private Quaternion CalculatePreAlignmentRotation(Transform cameraTransform)
        {
            // Calibration landmarks are the only semantic-axis truth. In the
            // measured contract printed front is mouthCenter -> mouthFront
            // (+Z), while up is neckAxis -> mouthCenter (+Y). Transform both
            // through the complete imported hierarchy, including the fixed
            // v41-B-to-v40-ORB bridge, before orienting the outer tracked root.
            Vector3 modelFront =
                calibration.mouthFrontInModel - calibration.mouthCenterInModel;
            Vector3 modelUp =
                calibration.mouthCenterInModel - calibration.neckAxisPointInModel;
            if (modelFront.sqrMagnitude < 0.000001f
                || modelUp.sqrMagnitude < 0.000001f)
            {
                throw new InvalidOperationException(
                    "Calibration mouthFront/mouthCenter/neckAxis landmarks are degenerate.");
            }
            Vector3 modelFrontInRoot = trackedObjectPoseRoot.InverseTransformDirection(
                registeredReferenceModel.TransformDirection(modelFront.normalized));
            Vector3 modelUpInRoot = trackedObjectPoseRoot.InverseTransformDirection(
                registeredReferenceModel.TransformDirection(modelUp.normalized));
            modelFrontInRoot.Normalize();
            modelUpInRoot = Vector3.ProjectOnPlane(
                modelUpInRoot,
                modelFrontInRoot).normalized;

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
            Vector3 canonicalForwardInRoot =
                trackedObjectPoseRoot.InverseTransformDirection(
                    registeredReferenceModel.TransformDirection(Vector3.forward));
            canonicalUpInRoot.Normalize();
            canonicalForwardInRoot = Vector3.ProjectOnPlane(
                canonicalForwardInRoot,
                canonicalUpInRoot).normalized;
            return Quaternion.LookRotation(
                canonicalForwardInRoot,
                canonicalUpInRoot);
        }

        private bool SetReliableTrackedPosePrior(
            NativeOrbTracker tracker,
            int frameRotationClockwise)
        {
            if (tracker == null)
            {
                return false;
            }
            if (!registrationEstablished || !stablePnpPoseAvailable)
            {
                tracker.ClearPosePrior();
                return false;
            }
            if (!TryBuildCurrentPosePrior(
                    frameRotationClockwise,
                    out float[] rotationTranslation))
            {
                tracker.ClearPosePrior();
                return false;
            }
            return tracker.SetPosePrior(
                rotationTranslation,
                Mathf.Min(0.09f, guidedMatchRadiusFraction));
        }

        private bool TryBuildCurrentPosePrior(
            int frameRotationClockwise,
            out float[] rotationTranslation)
        {
            rotationTranslation = null;
            if (!registrationEstablished
                || !stablePnpPoseAvailable
                || arCamera == null
                || modelCoordinateAlignment == null
                || calibration == null
                || calibration.metersPerModelUnit <= 0f)
            {
                return false;
            }

            // TrackedBottleRoot is the canonical ORB object frame. FBX import
            // axis conversion stays below ModelCoordinateAlignment and must
            // not be folded into the native PnP prior a second time.
            Quaternion priorFrameInRoot = Quaternion.identity;
            Vector3 originWorld = trackedObjectPoseRoot.TransformPoint(
                calibration.objectOriginInModel);
            Vector3 originCameraUnity =
                arCamera.transform.InverseTransformPoint(originWorld);
            Vector3 originOrientedCameraCv = new Vector3(
                originCameraUnity.x,
                -originCameraUnity.y,
                originCameraUnity.z) / calibration.metersPerModelUnit;
            Vector3 originCameraCv = OpenCvUnityPoseConverter.UndoImageRotation(
                originOrientedCameraCv,
                frameRotationClockwise);
            if (!IsFinite(originCameraCv) || originCameraCv.z <= 0f)
            {
                return false;
            }

            // OpenCvUnityPoseConverter reconstructs Unity orientation from
            // OpenCV up/forward. Reversing that handedness conversion requires
            // the model-right column to be negated here.
            Vector3 right = OpenCvUnityPoseConverter.UndoImageRotation(
                -ModelDirectionToCameraCv(priorFrameInRoot * Vector3.right),
                frameRotationClockwise);
            Vector3 up = OpenCvUnityPoseConverter.UndoImageRotation(
                ModelDirectionToCameraCv(priorFrameInRoot * Vector3.up),
                frameRotationClockwise);
            Vector3 forward = OpenCvUnityPoseConverter.UndoImageRotation(
                ModelDirectionToCameraCv(priorFrameInRoot * Vector3.forward),
                frameRotationClockwise);
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

        public void SetRepairOccluderVisible(bool visible)
        {
            SetRenderersEnabled(repairOccluderRenderers, visible);
        }

        public void ShowRepairPresentation()
        {
            if (activeProfile == null)
            {
                SetReferenceHierarchyVisible(false);
                SetRepairHierarchyVisible(false);
                SetRepairOccluderVisible(false);
                return;
            }
            // Start changes presentation only: full B/neck colour is removed,
            // the small neck-region proxy writes depth, and C remains colour-on.
            // The occluder is a rigid child of BottleRepairRoot and never has
            // its own PnP, anchor, or runtime correction.
            SetReferenceHierarchyVisible(false);
            SetRepairOccluderVisible(true);
            SetRepairHierarchyVisible(true);
            capVisibilityDiagnostic?.LogOcclusionSnapshot("repair");
        }

        private void ShowPreAlignmentPair()
        {
            if (activeProfile == null)
            {
                return;
            }
            ApplyMaterial(
                referenceRenderers,
                activeProfile.preAlignmentMaterial != null
                    ? activeProfile.preAlignmentMaterial
                    : activeProfile.viewerMaterial);
            ApplyMaterial(
                repairRenderers,
                activeProfile.repairMaterial != null
                    ? activeProfile.repairMaterial
                    : activeProfile.viewerMaterial);
            SetRepairOccluderVisible(false);
            SetReferenceHierarchyVisible(true);
            SetRepairHierarchyVisible(true);
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
                if (!TryGetClosestCameraPose(
                        image.timestamp,
                        out Vector3 captureCameraPosition,
                        out Quaternion captureCameraRotation,
                        out float capturePoseDeltaMs,
                        out string captureMotionClass))
                {
                    HandleTrackingLoss();
                    UpdateStatus("CAMERA_SYNC_UNAVAILABLE: CPU image has no timestamp-matched AR camera pose.");
                    return;
                }
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
                NativeOrbTracker bestTracker = null;
                lastPosePriorWasReliable = registrationEstablished
                    && stablePnpPoseAvailable;
                foreach (NativeOrbTracker tracker in trackers)
                {
                    bool priorSet = SetReliableTrackedPosePrior(
                        tracker,
                        rotationClockwise);
                    lastPosePriorWasReliable &= priorSet;
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
                        bestTracker = tracker;
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
                        ? BuildAcquisitionDiagnostics(best) + qualityReason
                        : "尚未在真实瓶身 A 中找到足够稳定的 B 自然特征。");
                    return;
                }

                if (!OpenCvUnityPoseConverter.TryGetObjectPose(
                        best,
                        rotationClockwise,
                        captureCameraPosition,
                        captureCameraRotation,
                        calibration,
                        out Vector3 targetPosition,
                        out Quaternion targetRotation))
                {
                    HandleTrackingLoss();
                    UpdateStatus("已找到自然特征，但三维姿态坐标转换无效。");
                    return;
                }
                if (bestTracker == null
                    || !bestTracker.TryGetLastInliers(out NativeInlierSet inliers))
                {
                    HandleTrackingLoss();
                    UpdateStatus(
                        "PnP 数学解有效，但没有取得用于 Unity 一致性验证的内点。");
                    return;
                }
                bool hardConsistencyPassed = UnityPoseConsistencyGate.TryEvaluate(
                    arCamera,
                    best,
                    inliers,
                    targetPosition,
                    targetRotation,
                    trackedObjectPoseRoot,
                    registeredReferenceModel,
                    calibration,
                    maximumPoseChainRoundTripRmsPixels,
                    maximumHierarchyTransformRoundTripRmsPixels,
                    out PoseConsistencyResult consistency,
                    out string consistencyReason);
                poseCoordinateDiagnostic?.UpdatePose(
                    best,
                    inliers,
                    image.width,
                    image.height,
                    rotationClockwise,
                    targetPosition,
                    targetRotation,
                    derivedOrbToRenderedBMatrix,
                    consistency);
                poseCoordinateDiagnostic?.UpdateCameraSynchronization(
                    capturePoseDeltaMs,
                    captureMotionClass,
                    captureCameraPosition,
                    captureCameraRotation);
                if (appearanceConsistency != null
                    && best.sampledConfidence > 0f)
                {
                    appearanceConsistency.ObserveReferenceHsv(
                        best.sampledHue,
                        best.sampledSaturation,
                        best.sampledValue,
                        best.sampledConfidence);
                }

                if (!TryApplyReliablePose(
                        targetPosition,
                        targetRotation,
                        best,
                        consistency,
                        out string poseApplicationReason))
                {
                    UpdateStatus(BuildPoseStatus(
                        best,
                        consistency,
                        hardConsistencyPassed,
                        consistencyReason,
                        poseApplicationReason));
                    return;
                }
                poseCoordinateDiagnostic?.UpdateFusion(
                    targetPosition,
                    targetRotation,
                    trackedObjectPoseRoot,
                    lastPoseFusionConfidence,
                    lastPoseFusionPositionAlpha,
                    lastPoseFusionRotationAlpha);

                if (repairRequested)
                {
                    UpdateStatus(BuildPoseStatus(
                        best,
                        consistency,
                        hardConsistencyPassed,
                        consistencyReason,
                        "Repair：B hidden / C retained；刚性根节点继续跟踪。"));
                }
                else
                {
                    string stateMessage = CanStartRepair
                        ? "B+C 已应用稳定 PnP Pose；数学坐标链连续验证通过，可点击开始。"
                        : "稳定 PnP Pose 已应用到 B+C，但数学坐标链仍在连续验证。";
                    UpdateStatus(BuildPoseStatus(
                        best,
                        consistency,
                        hardConsistencyPassed,
                        consistencyReason,
                        stateMessage));
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

        private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
        {
            if (!args.timestampNs.HasValue || arCamera == null)
            {
                return;
            }
            cameraFrameSamples[cameraFrameSampleWriteIndex] = new CameraFrameSample
            {
                timestampNs = args.timestampNs.Value,
                position = arCamera.transform.position,
                rotation = arCamera.transform.rotation
            };
            cameraFrameSampleWriteIndex =
                (cameraFrameSampleWriteIndex + 1) % cameraFrameSamples.Length;
            cameraFrameSampleCount = Mathf.Min(
                cameraFrameSampleCount + 1,
                cameraFrameSamples.Length);
        }

        private bool TryGetClosestCameraPose(
            double cpuTimestampSeconds,
            out Vector3 capturePosition,
            out Quaternion captureRotation,
            out float deltaMs,
            out string motionClass)
        {
            capturePosition = Vector3.zero;
            captureRotation = Quaternion.identity;
            deltaMs = float.PositiveInfinity;
            motionClass = "UNAVAILABLE";
            if (cameraFrameSampleCount == 0)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                Debug.Log(
                    $"[URP_CAMERA_SYNC_DIAG] cpuTimestampSeconds={cpuTimestampSeconds:F9} "
                    + "closestArTimestampNs=unavailable deltaMs=unavailable");
#endif
                return false;
            }

            long cpuTimestampNs = (long)Math.Round(cpuTimestampSeconds * 1e9);
            CameraFrameSample closest = cameraFrameSamples[0];
            long closestDelta = long.MaxValue;
            for (int i = 0; i < cameraFrameSampleCount; i++)
            {
                CameraFrameSample candidate = cameraFrameSamples[i];
                long delta = Math.Abs(candidate.timestampNs - cpuTimestampNs);
                if (delta < closestDelta)
                {
                    closest = candidate;
                    closestDelta = delta;
                }
            }

            float poseDeltaCentimetres = arCamera != null
                ? Vector3.Distance(arCamera.transform.position, closest.position) * 100f
                : 0f;
            float poseDeltaDegrees = arCamera != null
                ? Quaternion.Angle(arCamera.transform.rotation, closest.rotation)
                : 0f;
            motionClass = poseDeltaCentimetres < 0.2f
                && poseDeltaDegrees < 0.2f
                    ? "STATIC"
                    : "MOVING";
            capturePosition = closest.position;
            captureRotation = closest.rotation;
            deltaMs = (float)(closestDelta / 1e6);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log(
                $"[URP_CAMERA_SYNC_DIAG] cpuTimestampNs={cpuTimestampNs} "
                + $"closestArTimestampNs={closest.timestampNs} "
                + $"deltaMs={deltaMs:F3} sync={(deltaMs <= 40f ? "PASS" : "CAMERA_SYNC_WARN")} "
                + $"cameraPoseDeltaCm={poseDeltaCentimetres:F3} "
                + $"cameraPoseDeltaDeg={poseDeltaDegrees:F3} motion={motionClass}");
#endif
            return true;
        }

        private bool TryApplyReliablePose(
            Vector3 targetPosition,
            Quaternion targetRotation,
            NativeOrbResult pose,
            PoseConsistencyResult consistency,
            out string reason)
        {
            if (!registrationEstablished)
            {
                float initialPositionCorrection =
                    Vector3.Distance(trackedObjectPoseRoot.position, targetPosition);
                float initialRotationCorrection =
                    Quaternion.Angle(trackedObjectPoseRoot.rotation, targetRotation);
                if (initialPositionCorrection > maximumInitialCorrectionMeters)
                {
                    trackingState = TrackingState.Candidate;
                    reason =
                        $"Pose candidate 与初始 B 粗对齐差异过大："
                        + $"{initialPositionCorrection:F2}m，"
                        + $"{initialRotationCorrection:F0}°。"
                        + "尚未应用到 B+C；请让瓶体进入正确视野。";
                    return false;
                }

                ShowPreAlignmentPair();
                trackingState = TrackingState.PoseValidating;
                ObserveMathematicalConsistency(consistency);
                if (!TryAccumulateStableRegistration(
                        targetPosition,
                        targetRotation,
                        out Vector3 stablePosition,
                        out Quaternion stableRotation,
                        out reason))
                {
                    return false;
                }

                // Stable A-to-B PnP is applied immediately, before Start.
                // Both B and C stay visible so the user can visually verify
                // the complete coordinate chain against the real bottle A.
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
                    reason =
                        $"A→B Pose 跳变被拒绝：{positionJump:F3}m，"
                        + $"{rotationJump:F1}°。";
                    return false;
                }

                bool wasReady = readyForRepair;
                ObserveMathematicalConsistency(consistency);
                bool holdLastVerifiedPose = wasReady
                    && !consistency.InternalMathPassed
                    && consistencyFailureFrames <=
                        Mathf.Max(0, consistencyFailureHoldFrames);
                if (!holdLastVerifiedPose)
                {
                    // Before Ready, even a mathematically unverified stable
                    // candidate remains visible on B+C for real-device
                    // diagnosis. A transient failure after Ready instead holds
                    // the last verified pose for the configured grace frames.
                    float confidence = CalculatePoseFusionConfidence(
                        pose,
                        targetPosition,
                        targetRotation);
                    ApplyTrackedRootPose(
                        targetPosition,
                        targetRotation,
                        true,
                        confidence);
                }
            }

            lastAcceptedPosition = targetPosition;
            lastAcceptedRotation = targetRotation;
            lastValidPoseTime = Time.unscaledTime;
            lastReliablePnpTime = Time.unscaledTime;
            TryEstablishVerifiedReadyLatch();
            trackingState = repairRequested
                ? TrackingState.Repair
                : hasVerifiedReadyPoseSinceReset
                    ? TrackingState.ReadyForRepair
                    : TrackingState.PoseValidating;
            ShowPresentationForCurrentState();
            reason = readyForRepair
                ? string.Empty
                : "Stable PnP preview is applied; waiting for consecutive "
                  + $"PoseRT/HierarchyRT verification "
                  + $"{consistencyVerifiedFrames}/"
                  + $"{Mathf.Max(3, consistencyConfirmationFrames)}.";
            return true;
        }

        private void ObserveMathematicalConsistency(
            PoseConsistencyResult consistency)
        {
            if (consistency.InternalMathPassed)
            {
                consistencyFailureFrames = 0;
                consistencyVerifiedFrames++;
                int required = Mathf.Max(3, consistencyConfirmationFrames);
                if (consistencyVerifiedFrames >= required)
                {
                    poseChainVerified = true;
                    hierarchyTransformRoundTripVerified = true;
                    TryEstablishVerifiedReadyLatch();
                }
                return;
            }

            consistencyVerifiedFrames = 0;
            consistencyFailureFrames++;
            if (readyForRepair
                && consistencyFailureFrames <=
                    Mathf.Max(0, consistencyFailureHoldFrames))
            {
                return;
            }

            poseChainVerified = false;
            hierarchyTransformRoundTripVerified = false;
            // Current-frame mathematical diagnostics are intentionally not a
            // control latch. A single transient failure may turn these flags
            // red, but it cannot revoke a pose that previously completed the
            // full stable-PnP/PoseRT/HierarchyRT/ModelReg contract.
            readyForRepair = hasVerifiedReadyPoseSinceReset;
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
                    $"正在检查 Pose stability：A→B 六自由度位姿 "
                    + $"{registrationStableFrames}/{requiredFrames}；"
                    + "稳定确认完成前 B+C 保持初始粗对齐位姿。";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private void ApplyTrackedRootPose(
            Vector3 position,
            Quaternion rotation,
            bool smooth,
            float confidence = 1f)
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
                float elapsed = float.IsFinite(lastRootPoseApplicationTime)
                    ? Mathf.Clamp(
                        Time.unscaledTime - lastRootPoseApplicationTime,
                        0.02f,
                        0.25f)
                    : Mathf.Max(0.02f, relocationIntervalSeconds);
                ConfidenceWeightedPoseFusion.Result fused =
                    ConfidenceWeightedPoseFusion.Step(
                        smoothedRootPosition,
                        smoothedRootRotation,
                        position,
                        rotation,
                        confidence,
                        positionSmoothing,
                        rotationSmoothing,
                        elapsed,
                        relocationIntervalSeconds);
                smoothedRootPosition = fused.position;
                smoothedRootRotation = fused.rotation;
                lastPoseFusionConfidence = fused.confidence;
                lastPoseFusionPositionAlpha = fused.positionAlpha;
                lastPoseFusionRotationAlpha = fused.rotationAlpha;
            }
            trackedObjectPoseRoot.position = smoothedRootPosition;
            trackedObjectPoseRoot.rotation = smoothedRootRotation;
            trackedObjectPoseRoot.localScale =
                Vector3.one * calibration.metersPerModelUnit;
            lastRootPoseApplicationTime = Time.unscaledTime;
        }

        private float CalculatePoseFusionConfidence(
            NativeOrbResult pose,
            Vector3 targetPosition,
            Quaternion targetRotation)
        {
            float positionContinuity = registrationPositionToleranceMeters > 0f
                ? Vector3.Distance(lastAcceptedPosition, targetPosition)
                    / (registrationPositionToleranceMeters * 2f)
                : 0f;
            float rotationContinuity = registrationRotationToleranceDegrees > 0f
                ? Quaternion.Angle(lastAcceptedRotation, targetRotation)
                    / (registrationRotationToleranceDegrees * 2f)
                : 0f;
            return ConfidenceWeightedPoseFusion.Score(
                pose,
                minimumInlierRatio,
                maximumReprojectionErrorPixels,
                minimumCoverageX,
                minimumCoverageY,
                positionContinuity,
                rotationContinuity);
        }

        private void EstablishRegistration(
            Vector3 orbRootPosition,
            Quaternion orbRootRotation)
        {
            // The ORB database and Blender B+C asset share the same canonical
            // reconstruction frame. Preserve the measured pitch, roll and yaw
            // by applying the complete PnP pose directly to their common root.
            // A session-specific upright correction would overwrite the very
            // viewing-angle change that C must inherit from B.
            RestoreProfileCoordinateAlignment();
            ApplyTrackedRootPose(
                orbRootPosition,
                orbRootRotation,
                false);

            registrationEstablished = true;
            stablePnpPoseAvailable = true;
            poseAppliedToRigidRoot = true;
            hasEverRegisteredSinceReset = true;
            lastAcceptedPosition = orbRootPosition;
            lastAcceptedRotation = orbRootRotation;
            lastValidPoseTime = Time.unscaledTime;
            lastReliablePnpTime = Time.unscaledTime;
            TryEstablishVerifiedReadyLatch();
            trackingState = TrackingState.StablePoseApplied;
            ShowPresentationForCurrentState();
            capVisibilityDiagnostic?.LogSnapshot("registration-established");
            trackingState = repairRequested
                ? TrackingState.Repair
                : hasVerifiedReadyPoseSinceReset
                    ? TrackingState.ReadyForRepair
                    : TrackingState.PoseValidating;
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
                registrationStableFrames = 0;
                consistencyVerifiedFrames = 0;
                consistencyFailureFrames = 0;
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
            stablePnpPoseAvailable = false;
            registrationStableFrames = 0;
            poseChainVerified = false;
            hierarchyTransformRoundTripVerified = false;
            readyForRepair = false;
            hasVerifiedReadyPoseSinceReset = false;
            lastVerifiedReadyPoseTime = float.NegativeInfinity;
            consistencyVerifiedFrames = 0;
            consistencyFailureFrames = 0;
            // Retain the last accepted world pose while ORB relocalizes. AR
            // camera motion continues to provide correct perspective during
            // the hold; the next accepted pose is validated before correction.
            ShowPresentationForCurrentState();
        }

        private void ResetRegistration()
        {
            registrationEstablished = false;
            stablePnpPoseAvailable = false;
            poseAppliedToRigidRoot = false;
            poseChainVerified = false;
            hierarchyTransformRoundTripVerified = false;
            readyForRepair = false;
            hasVerifiedReadyPoseSinceReset = false;
            lastVerifiedReadyPoseTime = float.NegativeInfinity;
            registrationStableFrames = 0;
            consistencyVerifiedFrames = 0;
            consistencyFailureFrames = 0;
            hasSmoothedPose = false;
            lastValidPoseTime = float.NegativeInfinity;
            lastReliablePnpTime = float.NegativeInfinity;
            registrationAveragePosition = Vector3.zero;
            registrationAverageRotation = Quaternion.identity;
            lastCandidatePosition = Vector3.zero;
            lastCandidateRotation = Quaternion.identity;
            lastAcceptedPosition = Vector3.zero;
            lastAcceptedRotation = Quaternion.identity;
            lastRootPoseApplicationTime = float.NegativeInfinity;
            RestoreProfileCoordinateAlignment();
        }

        private void TryEstablishVerifiedReadyLatch()
        {
            if (!registrationEstablished
                || !stablePnpPoseAvailable
                || !poseAppliedToRigidRoot
                || !poseChainVerified
                || !hierarchyTransformRoundTripVerified
                || !modelRegistrationVerified)
            {
                readyForRepair = hasVerifiedReadyPoseSinceReset;
                return;
            }

            hasVerifiedReadyPoseSinceReset = true;
            lastVerifiedReadyPoseTime = Time.unscaledTime;
            readyForRepair = true;
        }

        private string BuildStartGateDiagnostic()
        {
            string blocker = !registrationEstablished ? "registered"
                : !stablePnpPoseAvailable ? "stablePnp"
                : !poseAppliedToRigidRoot ? "poseApplied"
                : !modelRegistrationVerified ? "modelReg"
                : !hasVerifiedReadyPoseSinceReset ? "readyLatch"
                : LastReliablePnpAgeSeconds >= Mathf.Clamp(
                    startReliablePoseGraceSeconds,
                    0.5f,
                    1.0f) ? "lastReliablePoseAge"
                : "NONE";
            float ageMs = float.IsFinite(LastReliablePnpAgeSeconds)
                ? LastReliablePnpAgeSeconds * 1000f
                : float.PositiveInfinity;
            return $"blockedBy={blocker}\n"
                + $"registered={registrationEstablished} "
                + $"stablePnp={stablePnpPoseAvailable} "
                + $"poseApplied={poseAppliedToRigidRoot}\n"
                + $"poseRT={poseChainVerified} "
                + $"hierarchyRT={hierarchyTransformRoundTripVerified} "
                + $"modelReg={modelRegistrationVerified}\n"
                + $"readyLatch={hasVerifiedReadyPoseSinceReset} "
                + $"lastReliablePoseAge={ageMs:F0}ms\n"
                + $"stableFrames={registrationStableFrames}/"
                + $"{Mathf.Max(2, registrationConfirmationFrames)} "
                + $"consistencyFrames={consistencyVerifiedFrames}/"
                + $"{Mathf.Max(3, consistencyConfirmationFrames)} "
                + $"state={trackingState}";
        }

        private void RestoreProfileCoordinateAlignment()
        {
            if (modelCoordinateAlignment == null)
            {
                return;
            }
            modelCoordinateAlignment.localPosition = derivedAlignmentPosition;
            modelCoordinateAlignment.localRotation = derivedAlignmentRotation;
            modelCoordinateAlignment.localScale = derivedAlignmentScale;
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
            registeredReferenceNeck = null;
            registeredRepairOccluder = null;
            registeredRepairPart = null;
            referenceBodyRenderers = Array.Empty<Renderer>();
            referenceNeckRenderers = Array.Empty<Renderer>();
            referenceRenderers = Array.Empty<Renderer>();
            repairRenderers = Array.Empty<Renderer>();
            repairOccluderRenderers = Array.Empty<Renderer>();
            foreach (Mesh mesh in correctedVisualMeshes)
            {
                DestroyRuntimeObject(mesh);
            }
            correctedVisualMeshes.Clear();
        }

        private void CorrectProductionVisualWinding(Renderer[] renderers)
        {
            foreach (Renderer renderer in renderers ?? Array.Empty<Renderer>())
            {
                Mesh source = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (source == null || !source.isReadable || !WindingOpposesNormals(source))
                {
                    continue;
                }
                Mesh corrected = Instantiate(source);
                corrected.name = source.name + "_V46CorrectedWinding";
                corrected.hideFlags = HideFlags.DontSave;
                for (int subMesh = 0; subMesh < corrected.subMeshCount; subMesh++)
                {
                    int[] triangles = corrected.GetTriangles(subMesh);
                    for (int index = 0; index + 2 < triangles.Length; index += 3)
                    {
                        (triangles[index + 1], triangles[index + 2]) =
                            (triangles[index + 2], triangles[index + 1]);
                    }
                    corrected.SetTriangles(triangles, subMesh, false);
                }
                corrected.RecalculateBounds();
                correctedVisualMeshes.Add(corrected);
                if (renderer is SkinnedMeshRenderer targetSkinned)
                {
                    targetSkinned.sharedMesh = corrected;
                }
                else
                {
                    renderer.GetComponent<MeshFilter>().sharedMesh = corrected;
                }
            }
        }

        private static bool WindingOpposesNormals(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (normals == null || normals.Length != vertices.Length)
            {
                return false;
            }
            int agreeing = 0;
            int opposing = 0;
            int stride = Mathf.Max(1, mesh.triangles.Length / 3000);
            int[] triangles = mesh.triangles;
            for (int offset = 0; offset + 2 < triangles.Length; offset += 3 * stride)
            {
                int i0 = triangles[offset];
                int i1 = triangles[offset + 1];
                int i2 = triangles[offset + 2];
                Vector3 face = Vector3.Cross(
                    vertices[i1] - vertices[i0],
                    vertices[i2] - vertices[i0]);
                if (face.sqrMagnitude < 1e-12f)
                {
                    continue;
                }
                Vector3 normal = normals[i0] + normals[i1] + normals[i2];
                if (Vector3.Dot(face, normal) < 0f) opposing++;
                else agreeing++;
            }
            return opposing > agreeing * 3;
        }

        private void CreateRepairOccluder(Material depthOnlyMaterial)
        {
            registeredRepairOccluder = null;
            repairOccluderRenderers = Array.Empty<Renderer>();
            if (registeredReferenceNeck == null
                || registeredBottlePairRoot == null
                || depthOnlyMaterial == null)
            {
                return;
            }

            GameObject clone = Instantiate(registeredReferenceNeck.gameObject);
            clone.name = "BottleRepairOccluder";
            Transform cloneTransform = clone.transform;
            cloneTransform.SetParent(registeredBottlePairRoot, false);
            Matrix4x4 local = registeredBottlePairRoot.worldToLocalMatrix
                * registeredReferenceNeck.localToWorldMatrix;
            cloneTransform.localPosition = local.GetColumn(3);
            cloneTransform.localRotation = local.rotation;
            cloneTransform.localScale = local.lossyScale;
            // Occluder-only 2% radial seam margin. The source proxy is just
            // inside the cap shell; without this conservative X/Z dilation it
            // writes no cap pixels at any tested angle. Pose and height stay
            // identical and BottleCapC is never modified.
            cloneTransform.localScale = Vector3.Scale(
                cloneTransform.localScale,
                new Vector3(1.02f, 1f, 1.02f));
            registeredRepairOccluder = cloneTransform;

            foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            repairOccluderRenderers = clone.GetComponentsInChildren<Renderer>(true);
            CorrectProductionVisualWinding(repairOccluderRenderers);
            ApplyMaterial(repairOccluderRenderers, depthOnlyMaterial);
            foreach (Renderer renderer in repairOccluderRenderers)
            {
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            SetRepairOccluderVisible(false);
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
            renderer.forceRenderingOff = false;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Renderer[] MergeRenderers(
            Renderer[] first,
            Renderer[] second)
        {
            List<Renderer> merged = new List<Renderer>();
            HashSet<Renderer> seen = new HashSet<Renderer>();
            foreach (Renderer[] group in new[]
                     {
                         first ?? Array.Empty<Renderer>(),
                         second ?? Array.Empty<Renderer>()
                     })
            {
                foreach (Renderer renderer in group)
                {
                    if (renderer != null && seen.Add(renderer))
                    {
                        merged.Add(renderer);
                    }
                }
            }
            return merged.ToArray();
        }

        private static Renderer[] ExcludeRenderers(
            Renderer[] source,
            Renderer[] excluded)
        {
            HashSet<Renderer> excludedSet = new HashSet<Renderer>(
                excluded ?? Array.Empty<Renderer>());
            List<Renderer> kept = new List<Renderer>();
            foreach (Renderer renderer in source ?? Array.Empty<Renderer>())
            {
                if (renderer != null && !excludedSet.Contains(renderer))
                {
                    kept.Add(renderer);
                }
            }
            return kept.ToArray();
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
                    // enabled alone is not a rendering guarantee: imported
                    // renderers can retain forceRenderingOff from an earlier
                    // depth/diagnostic pass.  C must always return to the
                    // ordinary colour pass when B is hidden.
                    renderer.forceRenderingOff = !enabled;
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
                    && !renderer.forceRenderingOff
                    && renderer.gameObject.activeInHierarchy)
                {
                    return true;
                }
            }
            return false;
        }

        private readonly struct RigidPoseSnapshot
        {
            public readonly Matrix4x4 root;
            public readonly Matrix4x4 pair;
            public readonly Matrix4x4 reference;
            public readonly Matrix4x4 cap;

            public RigidPoseSnapshot(
                Matrix4x4 root,
                Matrix4x4 pair,
                Matrix4x4 reference,
                Matrix4x4 cap)
            {
                this.root = root;
                this.pair = pair;
                this.reference = reference;
                this.cap = cap;
            }
        }

        private RigidPoseSnapshot CaptureRigidPoseSnapshot()
        {
            if (trackedObjectPoseRoot == null
                || registeredBottlePairRoot == null
                || registeredReferenceModel == null
                || registeredRepairPart == null)
            {
                throw new InvalidOperationException(
                    "Cannot capture the rigid B+C pose because the hierarchy is incomplete.");
            }
            return new RigidPoseSnapshot(
                trackedObjectPoseRoot.localToWorldMatrix,
                registeredBottlePairRoot.localToWorldMatrix,
                registeredReferenceModel.localToWorldMatrix,
                registeredRepairPart.localToWorldMatrix);
        }

        private static void AssertStartPoseUnchanged(
            RigidPoseSnapshot before,
            RigidPoseSnapshot after)
        {
            AssertMatrixUnchanged("TrackedBottleRoot", before.root, after.root);
            AssertMatrixUnchanged("BottleRepairRoot", before.pair, after.pair);
            AssertMatrixUnchanged("DamagedBottleB", before.reference, after.reference);
            AssertMatrixUnchanged("BottleCapC", before.cap, after.cap);

            if (Debug.isDebugBuild || Application.isEditor)
            {
                Debug.Log(
                    "[URP_CAP_DIAG] StartPoseDelta "
                    + $"rootPositionMm={MatrixPositionDeltaMeters(before.root, after.root) * 1000f:F6} "
                    + $"rootRotationDeg={Quaternion.Angle(before.root.rotation, after.root.rotation):F6} "
                    + $"capPositionMm={MatrixPositionDeltaMeters(before.cap, after.cap) * 1000f:F6} "
                    + $"capRotationDeg={Quaternion.Angle(before.cap.rotation, after.cap.rotation):F6} "
                    + $"capScaleDelta={Vector3.Distance(MatrixScale(before.cap), MatrixScale(after.cap)):E6}");
            }
        }

        private static void AssertMatrixUnchanged(
            string label,
            Matrix4x4 before,
            Matrix4x4 after)
        {
            float positionMeters = MatrixPositionDeltaMeters(before, after);
            float rotationDegrees = Quaternion.Angle(before.rotation, after.rotation);
            float scaleDelta = Vector3.Distance(MatrixScale(before), MatrixScale(after));
            float matrixDelta = MaximumMatrixElementDelta(before, after);
            if (positionMeters >= 0.00001f
                || rotationDegrees >= 0.01f
                || scaleDelta >= 0.000001f
                || matrixDelta >= 0.00001f)
            {
                throw new InvalidOperationException(
                    $"Start changed {label}: position={positionMeters * 1000f:F6}mm, "
                    + $"rotation={rotationDegrees:F6}deg, scale={scaleDelta:E6}, "
                    + $"matrix={matrixDelta:E6}.");
            }
        }

        private static float MatrixPositionDeltaMeters(Matrix4x4 a, Matrix4x4 b)
        {
            return Vector3.Distance(a.GetColumn(3), b.GetColumn(3));
        }

        private static Vector3 MatrixScale(Matrix4x4 matrix)
        {
            return new Vector3(
                matrix.GetColumn(0).magnitude,
                matrix.GetColumn(1).magnitude,
                matrix.GetColumn(2).magnitude);
        }

        private static float MaximumMatrixElementDelta(Matrix4x4 a, Matrix4x4 b)
        {
            float maximum = 0f;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    maximum = Mathf.Max(
                        maximum,
                        Mathf.Abs(a[row, column] - b[row, column]));
                }
            }
            return maximum;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z);
        }

        private string BuildAcquisitionDiagnostics(NativeOrbResult pose)
        {
            float lastGoodMs = float.IsFinite(LastReliablePnpAgeSeconds)
                ? LastReliablePnpAgeSeconds * 1000f
                : float.PositiveInfinity;
            return $"DB: records={runtimeDatabaseRecords} sha={runtimeDatabaseShaPrefix}\n"
                + $"Mode: {(lastPosePriorWasReliable ? "GUIDED" : "GLOBAL")}\n"
                + $"ORB: detected={pose.detectedKeypoints} "
                + $"ratioMatches={pose.ratioMatches} guidedMatches={pose.guidedMatches} "
                + $"uniqueMatches={pose.uniqueMatches} poseInliers={pose.poseInliers}\n"
                + $"PnP: RMS={pose.reprojectionError:F2}px "
                + $"rejectionCode={RejectionCodeName(pose.rejectionCode)}\n"
                + $"Stable: {registrationStableFrames}/"
                + $"{Mathf.Max(2, registrationConfirmationFrames)} "
                + $"Consistency: {consistencyVerifiedFrames}/"
                + $"{Mathf.Max(3, consistencyConfirmationFrames)}\n"
                + $"ReadyLatch: {(hasVerifiedReadyPoseSinceReset ? "YES" : "NO")} "
                + $"LastGood: {lastGoodMs:F0}ms "
                + $"Start: {(CanStartRepair ? "READY" : "BLOCKED")}\n"
                + $"Prior: {(lastPosePriorWasReliable ? "RELIABLE_LAST_POSE" : "NONE")}\n";
        }

        private static string RejectionCodeName(int code)
        {
            switch (code)
            {
                case 0: return "ACCEPTED";
                case 1: return "INVALID_INPUT";
                case 2: return "NO_DESCRIPTORS";
                case 3: return "INSUFFICIENT_UNIQUE_MATCHES";
                case 4: return "INSUFFICIENT_SPATIAL_DISTRIBUTION";
                case 5: return "PNP_FAILED";
                case 6: return "INSUFFICIENT_POSE_INLIERS";
                case 7: return "LOW_INLIER_RATIO";
                case 8: return "HIGH_REPROJECTION_ERROR";
                case 9: return "NEGATIVE_DEPTH";
                case 10: return "LOW_COUNT_POSE_UNSTABLE";
                default: return $"UNKNOWN_{code}";
            }
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

        private string BuildPoseStatus(
            NativeOrbResult pose,
            PoseConsistencyResult consistency,
            bool hardConsistencyPassed,
            string consistencyReason,
            string detail)
        {
            string state;
            if (repairRequested)
            {
                state = "REPAIR";
            }
            else if (CanStartRepair)
            {
                state = "READY";
            }
            else if (!consistency.poseChainPassed)
            {
                state = "POSE CONVERSION FAIL";
            }
            else if (!consistency.hierarchyTransformRoundTripPassed)
            {
                state = "HIERARCHY MATH FAIL";
            }
            else if (!modelRegistrationVerified)
            {
                state = "MODEL REGISTRATION FAIL";
            }
            else if (!registrationEstablished)
            {
                state = "POSE STABILITY";
            }
            else
            {
                state = "VERIFYING";
            }

            string displayState =
                consistency.displayProjectionDiagnosticRmsPixels
                    > displayProjectionWarningRmsPixels
                    ? "WARN"
                    : "OK";
            string hardNote = hardConsistencyPassed
                ? string.Empty
                : " " + consistencyReason;
            return BuildAcquisitionDiagnostics(pose)
                + $"PnP: {pose.poseInliers}/{pose.uniqueMatches}, "
                + $"native {pose.reprojectionError:F2}px/"
                + $"observed {consistency.nativePnpRmsPixels:F2}px\n"
                + $"PoseRT: {consistency.poseChainRoundTripRmsPixels:F3}px "
                + $"{(consistency.poseChainPassed ? "PASS" : "FAIL")} | "
                + BuildModelRegistrationStatus() + "\n"
                + $"DisplayDiag: "
                + $"{consistency.displayProjectionDiagnosticRmsPixels:F2}px "
                + $"{displayState}（仅诊断，不阻止配准） | State: {state}\n"
                + detail + hardNote;
        }

        private string BuildModelRegistrationStatus()
        {
            if (!modelRegistrationVerified || modelRegistrationEvidence == null)
            {
                return "ModelReg: FAIL " + modelRegistrationReason;
            }
            return $"Mouth {modelRegistrationEvidence.mouth_center_error_mm:F2}mm "
                + "DBSHA PASS | "
                + $"Base {modelRegistrationEvidence.base_center_error_mm:F2}mm | "
                + $"SurfaceP95 {modelRegistrationEvidence.orb_point_to_b_surface_mm.p95_mm:F2}mm "
                + $"Front {modelRegistrationEvidence.front_axis_error_deg:F2}deg "
                + $"Up {modelRegistrationEvidence.up_axis_error_deg:F2}deg PASS";
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message
                    + (poseCoordinateDiagnostic != null
                        ? poseCoordinateDiagnostic.CompactSummary
                        : string.Empty);
            }
        }

    }
}
