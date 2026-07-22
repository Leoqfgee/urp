using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Urp.ArDemo.Calibration;
using Urp.ArDemo.Generated;
using Urp.ArDemo.Native;

namespace Urp.ArDemo
{
    public sealed class OrbImageTrackingController : MonoBehaviour
    {
        public enum TrackingState
        {
            Aligning,
            Searching,
            Candidate,
            PoseValidating,
            Tracking,
            TemporarilyLost,
            Lost
        }
        [Header("AR input")]
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private Camera arCamera;

        [Header("Object-coordinate overlay")]
        [SerializeField] private Transform trackedObjectPoseRoot;
        [SerializeField] private Transform modelCoordinateAlignment;
        [SerializeField] private Transform modelReferenceRoot;
        [SerializeField] private Transform repairPartRoot;
        [SerializeField] private Transform occlusionRoot;
        [SerializeField] private Transform debugRoot;
        [SerializeField] private Text statusText;

        [Header("Runtime profile")]
        [SerializeField] private RestorationObjectProfile activeProfile;
        [SerializeField] private int maxFrameWidth = 640;
        [SerializeField] private int minGoodMatches = 9;
        [SerializeField] private int minPoseInliers = 6;
        [SerializeField] private float minimumInlierRatio = 0.50f;
        [SerializeField] private float maximumReprojectionErrorPixels = 3.0f;
        [SerializeField] private float minimumCoverageX = 0.05f;
        [SerializeField] private float minimumCoverageY = 0.18f;
        [SerializeField] private float ratioTest = 0.72f;

        [Header("Timing and continuity")]
        [SerializeField] private float relocationIntervalSeconds = 0.14f;
        [SerializeField] private float trackingIntervalSeconds = 0.09f;
        [SerializeField] private float lostPoseGraceSeconds = 0.45f;
        [SerializeField] private float maximumPositionJumpMeters = 0.16f;
        [SerializeField] private float maximumRotationJumpDegrees = 32f;
        [SerializeField] private int relocationConfirmationFrames = 4;
        [SerializeField] private float positionResponse = 6f;
        [SerializeField] private float rotationResponse = 7f;
        [SerializeField] private float positionDeadZoneMeters = 0.0025f;
        [SerializeField] private float rotationDeadZoneDegrees = 1.5f;

        [Header("Initial paper-style alignment")]
        [SerializeField] private Vector3 initialMouthPositionInCamera = new Vector3(0f, 0.16f, 0.42f);
        [SerializeField] private Vector3 initialObjectEulerInCamera = new Vector3(0f, 180f, 0f);

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
        private Vector3 pendingRelocationPosition;
        private Quaternion pendingRelocationRotation;
        private float lastPoseApplyTime = -1f;
        private Transform registeredReferenceModel;
        private Renderer[] referenceRenderers;
        private GameObject alignmentOutlineRoot;
        private Material alignmentOutlineMaterial;
        private bool referenceModelVisible;
        private Transform registeredRepairPart;
        private GameObject registeredOccluder;
        private RepairCalibrationProfile calibration;
        private bool forceRepairDebug;
        private Material forceDebugMaterial;
        private Material[][] repairMaterialsBeforeDebug;
        private LineRenderer debugBoundsLine;
        private Material boundsDebugMaterial;
        private string lastProjectionDiagnostic = "projection not evaluated";
        private TrackingState trackingState = TrackingState.Searching;
        private NativeOrbResult lastNativeResult;
        private bool hasLastNativeResult;
        private CameraIntrinsics lastIntrinsics;
        private int lastFrameRotation;
        private int lastSourceWidth;
        private int lastSourceHeight;
        private Matrix4x4 lastDisplayMatrix = Matrix4x4.identity;
        private bool hasDisplayMatrix;
        private readonly List<NativeDebugPoint> lastDebugPoints = new List<NativeDebugPoint>();
        private bool showNativeDebugOverlay = true;
        private bool alignmentGuideVisible;
        private bool hasStartAlignmentPose;
        private bool alignmentPriorConsumed;
        private Vector3 startAlignmentPosition;
        private Quaternion startAlignmentRotation;
        private float initialAlignmentMaximumViewportError = 0.28f;
        private float initialAlignmentMaximumUpAxisErrorDegrees = 55f;

        public bool HasTrackedPose => hasTrackedPose;
        public TrackingState State => trackingState;
        public bool IsRepairActuallyRenderable =>
            ValidateRepairRenderable(out _);

        private void Awake()
        {
            materialProperties = new MaterialPropertyBlock();
            SetRepairHierarchyVisible(false);

            Debug.Log($"URP native tracker version: {NativeOrbTracker.BuildVersion}");
            if (activeProfile != null)
            {
                SetProfile(activeProfile);
            }
        }

        private void OnDestroy()
        {
            foreach (NativeOrbTracker tracker in trackers)
            {
                tracker.Dispose();
            }
            if (forceDebugMaterial != null) Destroy(forceDebugMaterial);
            if (boundsDebugMaterial != null) Destroy(boundsDebugMaterial);
            if (alignmentOutlineMaterial != null) Destroy(alignmentOutlineMaterial);
        }

        private void OnEnable()
        {
            if (cameraManager != null) cameraManager.frameReceived += OnCameraFrameReceived;
        }

        private void OnDisable()
        {
            if (cameraManager != null) cameraManager.frameReceived -= OnCameraFrameReceived;
        }

        private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
        {
            if (!args.displayMatrix.HasValue) return;
            lastDisplayMatrix = args.displayMatrix.Value;
            hasDisplayMatrix = true;
        }

        private void Update()
        {
            if (forceRepairDebug)
            {
                UpdateDebugBounds();
                return;
            }

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
            activeProfile = profile;
            calibration = profile != null ? profile.calibration : null;
            if (profile != null && profile.trackingSettings != null)
            {
                minGoodMatches = profile.trackingSettings.minimumGoodMatches;
                minPoseInliers = profile.trackingSettings.minimumPoseInliers;
                minimumInlierRatio = profile.trackingSettings.minimumInlierRatio;
                maximumReprojectionErrorPixels =
                    profile.trackingSettings.maximumReprojectionErrorPixels;
                minimumCoverageX = profile.trackingSettings.minimumCoverageX;
                minimumCoverageY = profile.trackingSettings.minimumCoverageY;
                lostPoseGraceSeconds = profile.trackingSettings.lostPoseGraceSeconds;
                maximumPositionJumpMeters =
                    profile.trackingSettings.maximumPositionJumpMeters;
                maximumRotationJumpDegrees =
                    profile.trackingSettings.maximumRotationJumpDegrees;
                initialAlignmentMaximumViewportError =
                    profile.trackingSettings.initialAlignmentMaximumViewportError;
                initialAlignmentMaximumUpAxisErrorDegrees =
                    profile.trackingSettings.initialAlignmentMaximumUpAxisErrorDegrees;
            }
            foreach (NativeOrbTracker tracker in trackers)
            {
                tracker.Dispose();
            }
            trackers.Clear();

            ExitForceRepairDebug(false);
            if (registeredReferenceModel != null)
            {
                Destroy(registeredReferenceModel.gameObject);
            }
            if (alignmentOutlineRoot != null)
            {
                Destroy(alignmentOutlineRoot);
            }
            if (alignmentOutlineMaterial != null)
            {
                Destroy(alignmentOutlineMaterial);
            }
            if (registeredRepairPart != null)
            {
                Destroy(registeredRepairPart.gameObject);
            }
            if (registeredOccluder != null)
            {
                Destroy(registeredOccluder);
            }

            registeredReferenceModel = null;
            referenceRenderers = null;
            alignmentOutlineRoot = null;
            alignmentOutlineMaterial = null;
            referenceModelVisible = false;
            registeredRepairPart = null;
            registeredOccluder = null;
            capRenderers = null;
            Transform repairParent = repairPartRoot != null
                ? repairPartRoot
                : modelCoordinateAlignment;
            Transform referenceParent = modelReferenceRoot != null
                ? modelReferenceRoot
                : modelCoordinateAlignment;
            Transform occluderParent = occlusionRoot != null
                ? occlusionRoot
                : modelCoordinateAlignment;
            if (profile != null)
            {
                // b is the no-cap photogrammetry model registered with c in Blender.
                // Paper section 5.2 requires b to be visible only for the initial
                // user alignment. Start hides b; successful tracking renders only c.
                if (profile.trackingReferencePrefab != null && referenceParent != null)
                {
                    GameObject reference = Instantiate(profile.trackingReferencePrefab, referenceParent);
                    reference.name = "ReferenceBottleB_AlignmentGuide";
                    reference.transform.localPosition = Vector3.zero;
                    reference.transform.localRotation = Quaternion.identity;
                    reference.transform.localScale = Vector3.one;
                    registeredReferenceModel = reference.transform;
                    referenceRenderers = reference.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer renderer in referenceRenderers)
                    {
                        if (profile.initialGuideMaterial != null)
                        {
                            Material[] guideMaterials = new Material[
                                Mathf.Max(1, renderer.sharedMaterials.Length)];
                            for (int slot = 0; slot < guideMaterials.Length; slot++)
                                guideMaterials[slot] = profile.initialGuideMaterial;
                            renderer.sharedMaterials = guideMaterials;
                        }
                        renderer.enabled = true;
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                    }
                    foreach (Collider collider in reference.GetComponentsInChildren<Collider>(true))
                        collider.enabled = false;

                    BuildAlignmentOutlineGuide(referenceParent);
                }
                if (profile.registeredRepairPrefab != null && repairParent != null)
                {
                    GameObject repair = Instantiate(profile.registeredRepairPrefab, repairParent);
                    repair.name = "RegisteredBottleCap";
                    repair.transform.localPosition = calibration != null ? calibration.capLocalPosition : Vector3.zero;
                    repair.transform.localRotation = Quaternion.Euler(
                        calibration != null ? calibration.capLocalEulerAngles : Vector3.zero);
                    repair.transform.localScale = calibration != null ? calibration.capLocalScale : Vector3.one;
                    ApplyMaterial(repair, profile.repairMaterial);
                    registeredRepairPart = repair.transform;
                    capRenderers = repair.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer renderer in capRenderers)
                    {
                        SetLayerRecursively(renderer.gameObject, repairParent.gameObject.layer);
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        renderer.enabled = true;
                    }
                }
                // A depth-only proxy can erase the entire cap when its physical fit is
                // not calibrated. Never enable a provisional occluder on the device.
                if (profile.registeredOccluderPrefab != null && calibration != null)
                {
                    registeredOccluder = Instantiate(profile.registeredOccluderPrefab, occluderParent);
                    registeredOccluder.name = "RegisteredNeckOccluder";
                    registeredOccluder.transform.localPosition =
                        calibration != null ? calibration.occluderLocalPosition : Vector3.zero;
                    registeredOccluder.transform.localRotation = Quaternion.Euler(
                        calibration != null
                            ? calibration.occluderLocalEulerAngles
                            : Vector3.zero);
                    registeredOccluder.transform.localScale =
                        calibration != null ? calibration.occluderLocalScale : Vector3.one;
                }
            }

            if (occlusionRoot != null)
            {
                occlusionRoot.gameObject.SetActive(false);
            }

            BuildTrackers();
            ResetTracking();
        }

        public void SetTrackingEnabled(bool enabled)
        {
            ExitForceRepairDebug(false);
            modeEnabled = enabled;
            recognitionRunning = false;
            if (enabled)
            {
                if (activeProfile == null)
                {
                    UpdateStatus("尚未选择跟踪对象。");
                    return;
                }
                if (!activeProfile.HasTrackingAssets)
                {
                    HideOverlay(true);
                    UpdateStatus(
                        $"{activeProfile.displayName} 的 ORB 数据、修复部件与连接区域仍需完成标定。");
                    return;
                }
                ShowInitialPose();
                UpdateStatus(
                    $"移动手机，把青色参考 B 瓶体轮廓套住真实 {activeProfile.displayName}，然后点击“开始”。");
            }
            else
            {
                HideOverlay(true);
            }
        }

        public void StartRecognition()
        {
            ExitForceRepairDebug(false);
            if (!modeEnabled || activeProfile == null || !activeProfile.HasTrackingAssets)
            {
                UpdateStatus("当前对象尚不具备可用的独立跟踪与修复标定数据。");
                return;
            }

            recognitionRunning = true;
            trackingState = TrackingState.Searching;
            CaptureStartAlignmentPose();
            SetReferenceHierarchyVisible(false);
            SetRepairHierarchyVisible(false);
            nextProcessTime = 0f;
            UpdateStatus($"正在用真实瓶 A 的自然特征配准参考模型 B；配准成功后仅显示修复模型 C。");
        }

        public void ResetTracking()
        {
            ExitForceRepairDebug(false);
            recognitionRunning = false;
            trackingState = TrackingState.Searching;
            hasTrackedPose = false;
            rejectedJumpFrames = 0;
            pendingRelocationPosition = Vector3.zero;
            pendingRelocationRotation = Quaternion.identity;
            lastPoseApplyTime = -1f;
            lastValidPoseTime = -10f;
            hasStartAlignmentPose = false;
            alignmentPriorConsumed = false;
            ShowInitialPose();
            UpdateStatus("已恢复参考 B 的青色瓶体轮廓，请套住实物瓶身后点击“开始”。");
        }

        public void SetRepairVisible(bool visible)
        {
            repairVisibleRequested = visible;
            SetRepairHierarchyVisible(visible && (modeEnabled || hasTrackedPose));
        }

        public void SetRepairHierarchyVisible(bool visible)
        {
            bool rootVisible = visible || alignmentGuideVisible || referenceModelVisible;
            if (trackedObjectPoseRoot != null)
                trackedObjectPoseRoot.gameObject.SetActive(rootVisible);
            if (repairPartRoot != null)
                repairPartRoot.gameObject.SetActive(visible);
            if (registeredRepairPart != null)
                registeredRepairPart.gameObject.SetActive(visible);
            if (capRenderers == null) return;
            foreach (Renderer renderer in capRenderers)
            {
                if (renderer != null)
                    renderer.enabled = visible;
            }
        }

        public void SetReferenceHierarchyVisible(bool visible)
        {
            referenceModelVisible = visible;
            alignmentGuideVisible = false;
            bool rootVisible = referenceModelVisible
                || (registeredRepairPart != null && registeredRepairPart.gameObject.activeSelf);
            if (trackedObjectPoseRoot != null)
                trackedObjectPoseRoot.gameObject.SetActive(rootVisible);
            if (modelReferenceRoot != null)
                modelReferenceRoot.gameObject.SetActive(referenceModelVisible);
            if (registeredReferenceModel != null)
                registeredReferenceModel.gameObject.SetActive(referenceModelVisible);
            if (alignmentOutlineRoot != null)
                alignmentOutlineRoot.SetActive(false);
            if (referenceRenderers != null)
            {
                foreach (Renderer renderer in referenceRenderers)
                {
                    if (renderer != null) renderer.enabled = referenceModelVisible;
                }
            }
        }

        private void SetInitialAlignmentGuideVisible(bool visible)
        {
            alignmentGuideVisible = visible;
            referenceModelVisible = false;
            bool repairVisible = registeredRepairPart != null
                && registeredRepairPart.gameObject.activeSelf;
            if (trackedObjectPoseRoot != null)
                trackedObjectPoseRoot.gameObject.SetActive(visible || repairVisible);
            if (modelReferenceRoot != null)
                modelReferenceRoot.gameObject.SetActive(visible);
            if (registeredReferenceModel != null)
                registeredReferenceModel.gameObject.SetActive(false);
            if (referenceRenderers != null)
            {
                foreach (Renderer renderer in referenceRenderers)
                {
                    if (renderer != null) renderer.enabled = false;
                }
            }
            if (alignmentOutlineRoot != null)
                alignmentOutlineRoot.SetActive(visible);
        }

        private void BuildAlignmentOutlineGuide(Transform parent)
        {
            if (parent == null || calibration == null) return;

            alignmentOutlineRoot = new GameObject("ReferenceBottleB_AlignmentOutline");
            alignmentOutlineRoot.transform.SetParent(parent, false);
            SetLayerRecursively(alignmentOutlineRoot, parent.gameObject.layer);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                throw new MissingReferenceException("URP/Unlit is required for the alignment outline.");
            alignmentOutlineMaterial = new Material(shader)
            {
                name = "ReferenceBottleBOutlineRuntime"
            };
            Color color = new Color(0.08f, 0.95f, 1f, 0.95f);
            alignmentOutlineMaterial.SetColor("_BaseColor", color);
            alignmentOutlineMaterial.SetColor("_Color", color);

            const float frontZ = 0.205f;
            Vector3[] outline =
            {
                new Vector3(-0.10f, 0.00f, frontZ),
                new Vector3(-0.12f, -0.18f, frontZ),
                new Vector3(-0.20f, -0.28f, frontZ),
                new Vector3(-0.27f, -0.43f, frontZ),
                new Vector3(-0.29f, -1.10f, frontZ),
                new Vector3(-0.24f, -1.38f, frontZ),
                new Vector3(0.24f, -1.38f, frontZ),
                new Vector3(0.29f, -1.10f, frontZ),
                new Vector3(0.27f, -0.43f, frontZ),
                new Vector3(0.20f, -0.28f, frontZ),
                new Vector3(0.12f, -0.18f, frontZ),
                new Vector3(0.10f, 0.00f, frontZ)
            };
            CreateAlignmentLine("BottleBodyOutline", outline, false);

            const int rimSegments = 40;
            Vector3[] rim = new Vector3[rimSegments];
            for (int index = 0; index < rimSegments; index++)
            {
                float angle = index / (float)rimSegments * Mathf.PI * 2f;
                rim[index] = new Vector3(
                    Mathf.Cos(angle) * 0.105f,
                    Mathf.Sin(angle) * 0.018f,
                    frontZ + 0.004f);
            }
            CreateAlignmentLine("BottleMouthOutline", rim, true);
            alignmentOutlineRoot.SetActive(false);
        }

        private void CreateAlignmentLine(string name, Vector3[] points, bool loop)
        {
            GameObject lineObject = new GameObject(name);
            lineObject.transform.SetParent(alignmentOutlineRoot.transform, false);
            SetLayerRecursively(lineObject, alignmentOutlineRoot.layer);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = loop;
            line.alignment = LineAlignment.View;
            line.widthMultiplier = 0.010f;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.sharedMaterial = alignmentOutlineMaterial;
            line.startColor = Color.cyan;
            line.endColor = Color.cyan;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
        }

        public void ShowRegisteredPairDiagnostic()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (arCamera == null || trackedObjectPoseRoot == null
                || registeredReferenceModel == null || registeredRepairPart == null)
            {
                UpdateStatus("B+C 注册检查失败：相机、参考模型 B 或修复模型 C 未加载。");
                return;
            }

            PrepareRegisteredPairDiagnosticPose();
            SetReferenceHierarchyVisible(true);
            SetRepairHierarchyVisible(true);
            ApplyForceDebugMaterial();
            EnsureDebugBoundsLine();
            if (debugRoot != null) debugRoot.gameObject.SetActive(true);
            UpdateDebugBounds();
            string diagnostics = BuildRepairDiagnostics();
            Debug.Log($"[RegisteredPair B+C] {diagnostics}");
            UpdateStatus(IsRepairActuallyRenderable
                ? "注册检查：半透明无盖瓶模型 B 与洋红修复模型 C 以 Blender 固定关系共同显示；该模式不运行 ORB/PnP。"
                : $"B+C 注册检查失败：{diagnostics}");
#else
            UpdateStatus("B+C 注册检查仅在 Development Build 中可用。");
#endif
        }

        public void ExitForceRepairDebug()
        {
            ExitForceRepairDebug(true);
        }

        public void DebugShowRepairOnly()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            PrepareRegisteredPairDiagnosticPose();
            SetReferenceHierarchyVisible(false);
            SetRepairHierarchyVisible(true);
            if (occlusionRoot != null) occlusionRoot.gameObject.SetActive(false);
            ApplyForceDebugMaterial();
            UpdateStatus("诊断视图：只显示修复模型 C，用于确认模型、材质、Layer 与 Camera 可见性。");
#endif
        }

        public void DebugShowReferenceOnly()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            PrepareRegisteredPairDiagnosticPose();
            RestoreRepairMaterials();
            SetRepairHierarchyVisible(false);
            SetReferenceHierarchyVisible(true);
            if (occlusionRoot != null) occlusionRoot.gameObject.SetActive(false);
            UpdateStatus("诊断视图：只显示无盖参考模型 B；运行时正式跟踪成功后会隐藏 B。");
#endif
        }

        public void ExitDiagnosticsToPaperFlow()
        {
            ExitForceRepairDebug(false);
            ResetTracking();
        }

        private void PrepareRegisteredPairDiagnosticPose()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (forceRepairDebug)
            {
                RestoreRepairMaterials();
            }
            forceRepairDebug = true;
            recognitionRunning = false;
            hasTrackedPose = false;
            if (occlusionRoot != null) occlusionRoot.gameObject.SetActive(false);
            trackedObjectPoseRoot.SetParent(arCamera.transform, false);
            trackedObjectPoseRoot.localPosition = new Vector3(0f, 0.08f, 0.58f);
            trackedObjectPoseRoot.localRotation = Quaternion.Euler(4f, 12f, 0f);
            trackedObjectPoseRoot.localScale = Vector3.one
                * (calibration != null ? calibration.metersPerModelUnit : 0.17f);
#endif
        }

        private void ExitForceRepairDebug(bool restoreInitialPose)
        {
            if (!forceRepairDebug) return;
            forceRepairDebug = false;
            RestoreRepairMaterials();
            if (debugBoundsLine != null) debugBoundsLine.enabled = false;
            if (debugRoot != null) debugRoot.gameObject.SetActive(false);
            if (occlusionRoot != null) occlusionRoot.gameObject.SetActive(false);
            if (restoreInitialPose && modeEnabled) ShowInitialPose();
        }

        private void ApplyForceDebugMaterial()
        {
            if (capRenderers == null) return;
            Material source = Resources.Load<Material>("ForceMagentaDebug");
            if (source == null || source.shader == null
                || source.shader.name != "Hidden/URP/ForceMagentaDebug")
            {
                throw new MissingReferenceException(
                    "Hard-coded force-magenta debug material is missing from Resources.");
            }
            // The shader fragment returns (1,0,1,1) directly. It cannot be
            // turned white by imported material properties or property blocks.
            forceDebugMaterial = new Material(source)
            {
                name = "ForceRepairHardMagentaRuntime"
            };
            repairMaterialsBeforeDebug = new Material[capRenderers.Length][];
            for (int i = 0; i < capRenderers.Length; i++)
            {
                Renderer renderer = capRenderers[i];
                if (renderer == null) continue;
                renderer.SetPropertyBlock(null);
                repairMaterialsBeforeDebug[i] = renderer.sharedMaterials;
                Material[] replacements = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int slot = 0; slot < replacements.Length; slot++)
                    replacements[slot] = forceDebugMaterial;
                renderer.sharedMaterials = replacements;
                renderer.enabled = true;
            }
        }

        private void RestoreRepairMaterials()
        {
            if (capRenderers != null && repairMaterialsBeforeDebug != null)
            {
                for (int i = 0; i < capRenderers.Length && i < repairMaterialsBeforeDebug.Length; i++)
                {
                    if (capRenderers[i] != null && repairMaterialsBeforeDebug[i] != null)
                    {
                        capRenderers[i].sharedMaterials = repairMaterialsBeforeDebug[i];
                        capRenderers[i].SetPropertyBlock(null);
                    }
                }
            }
            repairMaterialsBeforeDebug = null;
            if (forceDebugMaterial != null) Destroy(forceDebugMaterial);
            forceDebugMaterial = null;
        }

        private void EnsureDebugBoundsLine()
        {
            if (debugRoot == null || debugBoundsLine != null) return;
            debugBoundsLine = debugRoot.gameObject.AddComponent<LineRenderer>();
            debugBoundsLine.useWorldSpace = true;
            debugBoundsLine.loop = false;
            debugBoundsLine.widthMultiplier = 0.0015f;
            debugBoundsLine.positionCount = 16;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            boundsDebugMaterial = new Material(shader) { name = "RepairBoundsGreenRuntime" };
            boundsDebugMaterial.SetColor("_BaseColor", Color.green);
            boundsDebugMaterial.SetColor("_Color", Color.green);
            debugBoundsLine.sharedMaterial = boundsDebugMaterial;
            debugBoundsLine.startColor = Color.green;
            debugBoundsLine.endColor = Color.green;
            debugBoundsLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            debugBoundsLine.receiveShadows = false;
        }

        private void UpdateDebugBounds()
        {
            if (!forceRepairDebug || debugBoundsLine == null || capRenderers == null) return;
            if (!TryGetRepairBounds(out Bounds bounds)) return;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3[] points =
            {
                new Vector3(min.x,min.y,min.z), new Vector3(max.x,min.y,min.z),
                new Vector3(max.x,max.y,min.z), new Vector3(min.x,max.y,min.z),
                new Vector3(min.x,min.y,min.z), new Vector3(min.x,min.y,max.z),
                new Vector3(max.x,min.y,max.z), new Vector3(max.x,max.y,max.z),
                new Vector3(min.x,max.y,max.z), new Vector3(min.x,min.y,max.z),
                new Vector3(min.x,max.y,max.z), new Vector3(min.x,max.y,min.z),
                new Vector3(max.x,max.y,min.z), new Vector3(max.x,max.y,max.z),
                new Vector3(max.x,min.y,max.z), new Vector3(max.x,min.y,min.z)
            };
            debugBoundsLine.SetPositions(points);
            debugBoundsLine.enabled = true;
        }

        private string BuildRepairDiagnostics()
        {
            StringBuilder text = new StringBuilder();
            text.Append($"root={trackedObjectPoseRoot?.gameObject.activeSelf}, ");
            text.Append($"repairRoot={repairPartRoot?.gameObject.activeSelf}, ");
            text.Append($"cap={registeredRepairPart?.gameObject.activeSelf}, ");
            text.Append($"cameraMask=0x{(arCamera != null ? arCamera.cullingMask : 0):X8}; ");
            if (capRenderers == null || capRenderers.Length == 0)
                return text.Append("renderers=0").ToString();
            foreach (Renderer renderer in capRenderers)
            {
                if (renderer == null) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                text.Append($"[{renderer.name}: active={renderer.gameObject.activeInHierarchy}, ");
                text.Append($"enabled={renderer.enabled}, layer={renderer.gameObject.layer}, ");
                text.Append($"shader={renderer.sharedMaterial?.shader?.name ?? "null"}, ");
                text.Append($"verts={(mesh != null ? mesh.vertexCount : 0)}, ");
                text.Append($"tris={(mesh != null ? mesh.triangles.Length / 3 : 0)}, ");
                text.Append($"scale={renderer.transform.lossyScale}] ");
            }
            return text.ToString();
        }

        private void BuildTrackers()
        {
            trackers.Clear();
            if (activeProfile == null || activeProfile.trackingReferenceDatabase == null)
            {
                return;
            }

            // This database estimates the pose of hidden reference model b.
            // Repair part c never contributes descriptors and only inherits the
            // solved b pose through their common Blender-registered root.
            foreach (TextAsset model in new[] { activeProfile.trackingReferenceDatabase })
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
                if (tracker.IsValid
                    && tracker.SetModel(model)
                    && (calibration == null
                        || tracker.SetRepairAnchor(calibration.mouthCenterInModel)))
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
                UpdateStatus($"{activeProfile.displayName} 的 ORB 三维特征库加载失败。");
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
                lastIntrinsics = intrinsics;
                lastFrameRotation = rotationClockwise;
                lastSourceWidth = image.width;
                lastSourceHeight = image.height;
                byte[] frameRgba = NativeOrbTracker.GetRgbaBytes(texture);

                NativeOrbResult best = default;
                NativeOrbTracker bestTracker = null;
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
                        bestTracker = tracker;
                        hasResult = true;
                    }
                }

                if (!hasResult)
                {
                    HideOverlay(false);
                    UpdateStatus($"已识别 {activeProfile.displayName}，正在求解稳定姿态。");
                    return;
                }
                lastNativeResult = best;
                hasLastNativeResult = true;
                bestTracker?.GetDebugPoints(lastDebugPoints);

                if (!PassesPoseQuality(best, out string qualityReason))
                {
                    trackingState = best.goodMatches < minGoodMatches
                        ? TrackingState.Candidate
                        : TrackingState.PoseValidating;
                    HideOverlay(false);
                    UpdateStatus(qualityReason);
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
                    HideOverlay(false);
                    UpdateStatus("对象位姿有效，但坐标转换结果无效。");
                    return;
                }

                if (!PassesStartAlignmentPrior(
                        targetPosition, targetRotation, out string alignmentReason))
                {
                    trackingState = TrackingState.PoseValidating;
                    HideOverlay(false);
                    UpdateStatus(alignmentReason);
                    return;
                }

                UpdateAnchorProjectionDiagnostic(best, targetPosition);
                best.translationJumpMeters = hasTrackedPose
                    ? Vector3.Distance(lastTargetPosition, targetPosition)
                    : 0f;
                best.rotationJumpDegrees = hasTrackedPose
                    ? Quaternion.Angle(lastTargetRotation, targetRotation)
                    : 0f;

                if (!PassesPoseContinuity(targetPosition, targetRotation))
                {
                    trackingState = hasTrackedPose
                        ? TrackingState.TemporarilyLost
                        : TrackingState.PoseValidating;
                    if (trackedObjectPoseRoot != null && hasTrackedPose)
                    {
                        SetRepairHierarchyVisible(repairVisibleRequested);
                    }
                    UpdateStatus("检测到异常位姿跳变，正在等待稳定结果。");
                    return;
                }

                ApplyPose(targetPosition, targetRotation);
                alignmentPriorConsumed = true;
                trackingState = TrackingState.Tracking;
                ApplyLightingConsistency(best.localLuminance);
                SetReferenceHierarchyVisible(false);
                SetRepairHierarchyVisible(repairVisibleRequested);
                lastValidPoseTime = Time.unscaledTime;

                if (!ValidateRepairVisibility(out string visibilityReason))
                {
                    UpdateStatus($"对象位姿有效，正在验证修复部件位置：{visibilityReason}");
                    return;
                }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
                UpdateStatus(
                    $"跟踪稳定，瓶盖 Renderer 已提交相机（等待真机像素确认）："
                    + $"{activeProfile.objectId}，内点 {best.poseInliers}，"
                    + $"比例 {best.inlierRatio:P0}，误差 {best.reprojectionError:F2}px；"
                    + $"{lastProjectionDiagnostic}；"
                    + $"标定 {(activeProfile.physicalScaleVerified ? "已验证" : "未验证")}。");
#else
                UpdateStatus(activeProfile.physicalScaleVerified
                    ? "跟踪稳定，修复部件 Renderer 已进入有效视野；请以真机可见瓶盖确认完成。"
                    : "修复部件已进入视野，但物理尺寸与连接区域标定尚未验证。");
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
                reason = $"找到外观候选：关键点 {result.detectedKeypoints}，"
                    + $"比例匹配 {result.ratioMatches}，互检 {result.mutualMatches}，"
                    + $"唯一匹配 {result.uniqueMatches}/{minGoodMatches}；尚不足以进入 PnP。";
                return false;
            }

            if (result.coverageY < minimumCoverageY)
            {
                reason = $"已找到瓶身候选，但匹配点垂直覆盖仅 {result.coverageY:P0}，"
                    + $"不足 {minimumCoverageY:P0}。";
                return false;
            }
            if (result.coverageX < minimumCoverageX)
            {
                reason = $"已找到瓶身候选，但匹配点水平覆盖仅 {result.coverageX:P0}，"
                    + $"不足 {minimumCoverageX:P0}。";
                return false;
            }
            if (result.occupiedGridCells < 4)
            {
                reason = $"已找到瓶身候选，但 8×12 网格仅占用 {result.occupiedGridCells} 格，"
                    + "特征过度集中。";
                return false;
            }
            if (result.rejectionCode == 5)
            {
                reason = $"对应点 {result.uniqueMatches} 个且分布有效，但 solvePnPRansac 求解失败。";
                return false;
            }
            int requiredPoseInliers = Mathf.Clamp(
                Mathf.CeilToInt(result.uniqueMatches * 0.50f),
                minPoseInliers,
                10);
            if (result.poseInliers < requiredPoseInliers)
            {
                reason = $"PnP 内点 {result.poseInliers}/{result.uniqueMatches}，"
                    + $"当前匹配规模至少需要 {requiredPoseInliers} 个。";
                return false;
            }
            if (result.inlierRatio < minimumInlierRatio)
            {
                reason = $"PnP 内点 {result.poseInliers}/{result.uniqueMatches}，"
                    + $"内点比例 {result.inlierRatio:P1}，低于 {minimumInlierRatio:P0}。";
                return false;
            }
            if (result.reprojectionError > maximumReprojectionErrorPixels)
            {
                reason = $"PnP 重投影 RMS {result.reprojectionError:F2}px、"
                    + $"最大 {result.reprojectionMax:F2}px，超过 {maximumReprojectionErrorPixels:F2}px。";
                return false;
            }
            if (result.rejectionCode == 10)
            {
                reason = $"低点数 PnP 尚不稳定：内点 {result.poseInliers}/{result.uniqueMatches}，"
                    + $"网格 {result.occupiedGridCells}，RMS {result.reprojectionError:F2}px；"
                    + "低点数位姿要求至少 5 个网格且 RMS 不超过 1.50px。";
                return false;
            }
            if (result.poseValid == 0)
            {
                reason = $"PnP 被拒绝：rejectionCode={result.rejectionCode}，"
                    + $"唯一匹配 {result.uniqueMatches}，内点 {result.poseInliers}。";
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

            bool agreesWithPending = rejectedJumpFrames > 0
                && Vector3.Distance(pendingRelocationPosition, position)
                    <= maximumPositionJumpMeters * 0.35f
                && Quaternion.Angle(pendingRelocationRotation, rotation)
                    <= maximumRotationJumpDegrees * 0.35f;
            if (!agreesWithPending)
            {
                pendingRelocationPosition = position;
                pendingRelocationRotation = rotation;
                rejectedJumpFrames = 1;
                return false;
            }

            float weight = 1f / (rejectedJumpFrames + 1f);
            pendingRelocationPosition = Vector3.Lerp(
                pendingRelocationPosition, position, weight);
            pendingRelocationRotation = Quaternion.Slerp(
                pendingRelocationRotation, rotation, weight);
            rejectedJumpFrames++;
            if (rejectedJumpFrames < Mathf.Max(2, relocationConfirmationFrames))
            {
                return false;
            }

            hasTrackedPose = false;
            rejectedJumpFrames = 0;
            return true;
        }

        private void CaptureStartAlignmentPose()
        {
            if (trackedObjectPoseRoot == null)
            {
                hasStartAlignmentPose = false;
                return;
            }

            startAlignmentPosition = trackedObjectPoseRoot.position;
            startAlignmentRotation = trackedObjectPoseRoot.rotation;
            hasStartAlignmentPose = true;
            alignmentPriorConsumed = false;
            trackedObjectPoseRoot.SetParent(null, true);
        }

        private bool PassesStartAlignmentPrior(
            Vector3 position, Quaternion rotation, out string reason)
        {
            if (!hasStartAlignmentPose || alignmentPriorConsumed)
            {
                reason = string.Empty;
                return true;
            }

            if (arCamera == null)
            {
                reason = string.Empty;
                return true;
            }

            Vector3 startViewport = arCamera.WorldToViewportPoint(startAlignmentPosition);
            Vector3 poseViewport = arCamera.WorldToViewportPoint(position);
            float viewportError = Vector2.Distance(
                new Vector2(startViewport.x, startViewport.y),
                new Vector2(poseViewport.x, poseViewport.y));
            Vector3 startUp = startAlignmentRotation * Vector3.up;
            Vector3 poseUp = rotation * Vector3.up;
            float upAxisError = Vector3.Angle(startUp, poseUp);

            // The bottle can differ by almost 180 degrees around its vertical
            // axis because the initial guide is only a framing aid while PnP
            // resolves the textured front. Comparing complete Quaternions here
            // rejected otherwise valid upright poses. The paper-style prior now
            // checks only the mouth projection and the bottle's vertical axis.
            if (startViewport.z > 0f
                && poseViewport.z > 0f
                && viewportError <= initialAlignmentMaximumViewportError
                && upAxisError <= initialAlignmentMaximumUpAxisErrorDegrees)
            {
                reason = string.Empty;
                return true;
            }

            reason = $"已匹配到瓶身并求得 PnP，但瓶口投影或瓶身竖直方向与开始前引导差异过大："
                + $"屏幕误差 {viewportError:P0}/{initialAlignmentMaximumViewportError:P0}，"
                + $"竖直轴误差 {upAxisError:F0}°/{initialAlignmentMaximumUpAxisErrorDegrees:F0}°。"
                + "请点“重置”，把青色瓶体轮廓套住实物后再开始。";
            return false;
        }

        private void ApplyPose(Vector3 position, Quaternion rotation)
        {
            if (arCamera == null || trackedObjectPoseRoot == null)
                return;

            float now = Time.unscaledTime;
            float deltaTime = lastPoseApplyTime < 0f
                ? trackingIntervalSeconds
                : Mathf.Max(0.001f, now - lastPoseApplyTime);
            lastPoseApplyTime = now;
            Vector3 cameraPosition = arCamera.transform.InverseTransformPoint(position);
            Quaternion cameraRotation = Quaternion.Inverse(arCamera.transform.rotation) * rotation;
            if (!hasTrackedPose)
            {
                trackedObjectPoseRoot.SetParent(arCamera.transform, false);
                trackedObjectPoseRoot.localPosition = cameraPosition;
                trackedObjectPoseRoot.localRotation = cameraRotation;
                trackedObjectPoseRoot.localScale = Vector3.one * calibration.metersPerModelUnit;
                hasTrackedPose = true;
            }
            else
            {
                if (trackedObjectPoseRoot.parent != arCamera.transform)
                    trackedObjectPoseRoot.SetParent(arCamera.transform, true);
                if (Vector3.Distance(trackedObjectPoseRoot.localPosition, cameraPosition)
                    <= positionDeadZoneMeters)
                {
                    cameraPosition = trackedObjectPoseRoot.localPosition;
                }
                if (Quaternion.Angle(trackedObjectPoseRoot.localRotation, cameraRotation)
                    <= rotationDeadZoneDegrees)
                {
                    cameraRotation = trackedObjectPoseRoot.localRotation;
                }
                float positionAlpha = 1f - Mathf.Exp(-positionResponse * deltaTime);
                float rotationAlpha = 1f - Mathf.Exp(-rotationResponse * deltaTime);
                trackedObjectPoseRoot.localPosition = Vector3.Lerp(
                    trackedObjectPoseRoot.localPosition,
                    cameraPosition,
                    positionAlpha);
                trackedObjectPoseRoot.localRotation = Quaternion.Slerp(
                    trackedObjectPoseRoot.localRotation,
                    cameraRotation,
                    rotationAlpha);
            }

            lastTargetPosition = position;
            lastTargetRotation = rotation;
        }

        private void UpdateAnchorProjectionDiagnostic(NativeOrbResult result, Vector3 worldPosition)
        {
            if (arCamera == null || result.anchorVisible == 0)
            {
                lastProjectionDiagnostic = "Native 二维瓶口投影无效（不参与世界位姿）";
                return;
            }

            Vector3 viewport = arCamera.WorldToViewportPoint(worldPosition);
            if (!float.IsFinite(viewport.x)
                || !float.IsFinite(viewport.y)
                || viewport.z <= 0f)
            {
                lastProjectionDiagnostic = $"A 链 viewport 无效：{viewport}";
                return;
            }

            if (!TryConvertOrientedImageToViewport(
                    new Vector2(result.anchorX01, result.anchorY01), out Vector2 nativeAnchor))
            {
                lastProjectionDiagnostic =
                    $"A=({viewport.x:F3},{viewport.y:F3}); B displayMatrix unavailable";
                return;
            }
            Vector2 unityAnchor = new Vector2(viewport.x, viewport.y);
            float dx = (nativeAnchor.x - unityAnchor.x) * Screen.width;
            float dy = (nativeAnchor.y - unityAnchor.y) * Screen.height;
            float pixels = Mathf.Sqrt(dx * dx + dy * dy);
            lastProjectionDiagnostic =
                $"A=({unityAnchor.x:F3},{unityAnchor.y:F3}) "
                + $"B=({nativeAnchor.x:F3},{nativeAnchor.y:F3}) Δ={pixels:F1}px";
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
            trackingState = TrackingState.Aligning;
            if (occlusionRoot != null) occlusionRoot.gameObject.SetActive(false);
            SetRepairHierarchyVisible(false);
            SetReferenceHierarchyVisible(false);
            SetInitialAlignmentGuideVisible(true);
        }

        private bool ValidateRepairVisibility(out string reason)
        {
            if (!ValidateRepairRenderable(out reason) || !TryGetRepairBounds(out Bounds bounds))
                return false;

            Vector3 center = arCamera.WorldToViewportPoint(bounds.center);
            Vector3 top = arCamera.WorldToViewportPoint(bounds.center + Vector3.up * bounds.extents.y);
            Vector3 side = arCamera.WorldToViewportPoint(bounds.center + Vector3.right * bounds.extents.x);
            float screenSize = Mathf.Max(
                Vector2.Distance(center, top),
                Vector2.Distance(center, side)) * 2f;
            if (center.z <= 0f)
            {
                reason = "修复部件位于相机后方";
                return false;
            }

            if (center.x < -0.15f || center.x > 1.15f || center.y < -0.15f || center.y > 1.15f)
            {
                reason = "修复部件投影位于屏幕外";
                return false;
            }

            if (screenSize < 0.008f || screenSize > 0.45f)
            {
                reason = "修复部件屏幕尺寸不合理";
                return false;
            }

            if (activeProfile == null || !activeProfile.physicalScaleVerified)
            {
                reason = "修复部件已进入视野，但标定尚未验证";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool ValidateRepairRenderable(out string reason)
        {
            if (registeredRepairPart == null || !registeredRepairPart.gameObject.activeInHierarchy)
            {
                reason = "修复部件未激活";
                return false;
            }
            if (arCamera == null)
            {
                reason = "AR Camera 为空";
                return false;
            }
            if (capRenderers == null || capRenderers.Length == 0)
            {
                reason = "修复部件没有 Renderer";
                return false;
            }

            bool found = false;
            foreach (Renderer renderer in capRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if ((arCamera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
                {
                    reason = $"Renderer Layer {renderer.gameObject.layer} 不在 Camera cullingMask 中";
                    return false;
                }
                Material material = renderer.sharedMaterial;
                if (material == null || material.shader == null || !material.shader.isSupported)
                {
                    reason = $"Renderer {renderer.name} 的材质或 Shader 无效";
                    return false;
                }
                Vector3 cameraPoint = arCamera.transform.InverseTransformPoint(renderer.bounds.center);
                if (cameraPoint.z <= arCamera.nearClipPlane)
                {
                    float rootZ = trackedObjectPoseRoot != null
                        ? arCamera.transform.InverseTransformPoint(trackedObjectPoseRoot.position).z
                        : float.NaN;
                    float capZ = registeredRepairPart != null
                        ? arCamera.transform.InverseTransformPoint(registeredRepairPart.position).z
                        : float.NaN;
                    reason = $"Renderer {renderer.name} 未进入前方视锥："
                        + $"rawTvecZ={lastNativeResult.tvecZ:F3} model，"
                        + $"rootZ={rootZ:F3}m，capZ={capZ:F3}m，boundsZ={cameraPoint.z:F3}m";
                    return false;
                }
                found = true;
            }
            reason = found ? string.Empty : "没有同时 activeInHierarchy 且 enabled 的 Renderer";
            return found;
        }

        private bool TryGetRepairBounds(out Bounds bounds)
        {
            bounds = default;
            bool initialized = false;
            if (capRenderers == null) return false;
            foreach (Renderer renderer in capRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
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
            return initialized;
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
                FreezeTrackedPoseInWorld();
                trackingState = TrackingState.TemporarilyLost;
                SetReferenceHierarchyVisible(false);
                SetRepairHierarchyVisible(repairVisibleRequested);
                return;
            }

            hasTrackedPose = false;
            if (!force && modeEnabled && recognitionRunning)
            {
                trackingState = TrackingState.Lost;
                SetReferenceHierarchyVisible(false);
                SetRepairHierarchyVisible(false);
                return;
            }

            SetReferenceHierarchyVisible(false);
            SetRepairHierarchyVisible(false);
        }

        private void FreezeTrackedPoseInWorld()
        {
            if (trackedObjectPoseRoot != null && trackedObjectPoseRoot.parent == arCamera?.transform)
                trackedObjectPoseRoot.SetParent(null, true);
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

        private int ResolveFrameRotation(int width, int height)
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

        public void SaveTrackingFailureFrame()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (frameTexture == null)
            {
                UpdateStatus("保存失败：尚未取得 CPU 相机帧。");
                return;
            }

            string root = Path.Combine(Application.persistentDataPath,
                "TrackingFailures", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "frame.png"), frameTexture.EncodeToPNG());
            File.WriteAllText(Path.Combine(root, "intrinsics.json"), JsonUtility.ToJson(
                new IntrinsicsDiagnostics
                {
                    sourceWidth = lastSourceWidth,
                    sourceHeight = lastSourceHeight,
                    convertedWidth = frameTexture.width,
                    convertedHeight = frameTexture.height,
                    fx = lastIntrinsics.FocalLengthX,
                    fy = lastIntrinsics.FocalLengthY,
                    cx = lastIntrinsics.PrincipalPointX,
                    cy = lastIntrinsics.PrincipalPointY
                }, true));
            File.WriteAllText(Path.Combine(root, "orientation.json"), JsonUtility.ToJson(
                new OrientationDiagnostics
                {
                    screenWidth = Screen.width,
                    screenHeight = Screen.height,
                    screenOrientation = Screen.orientation.ToString(),
                    nativeRotationClockwise = lastFrameRotation,
                    hasDisplayMatrix = hasDisplayMatrix,
                    displayMatrix = lastDisplayMatrix
                }, true));
            File.WriteAllText(Path.Combine(root, "matches.json"),
                hasLastNativeResult ? JsonUtility.ToJson(lastNativeResult, true) : "{}");
            File.WriteAllText(Path.Combine(root, "debug_points.json"), JsonUtility.ToJson(
                new DebugPointDiagnostics { points = lastDebugPoints.ToArray() }, true));
            File.WriteAllText(Path.Combine(root, "pose.json"), JsonUtility.ToJson(
                new PoseDiagnostics
                {
                    state = trackingState.ToString(),
                    hasTrackedPose = hasTrackedPose,
                    rootPosition = trackedObjectPoseRoot != null
                        ? trackedObjectPoseRoot.position : Vector3.zero,
                    rootRotation = trackedObjectPoseRoot != null
                        ? trackedObjectPoseRoot.rotation : Quaternion.identity,
                    projectionDiagnostic = lastProjectionDiagnostic,
                    renderDiagnostic = BuildRepairDiagnostics()
                }, true));
            File.WriteAllText(Path.Combine(root, "calibration.json"),
                calibration != null ? JsonUtility.ToJson(calibration, true) : "{}");
            File.WriteAllText(Path.Combine(root, "build_identity.json"),
                JsonUtility.ToJson(BuildIdentity.Current, true));
            Debug.Log($"[TrackingFailure] Saved {root}");
            UpdateStatus($"失败帧已保存：{root}");
#else
            UpdateStatus("失败帧保存仅在 Development Build 中可用。");
#endif
        }

        [Serializable]
        private sealed class IntrinsicsDiagnostics
        {
            public int sourceWidth;
            public int sourceHeight;
            public int convertedWidth;
            public int convertedHeight;
            public float fx;
            public float fy;
            public float cx;
            public float cy;
        }

        [Serializable]
        private sealed class OrientationDiagnostics
        {
            public int screenWidth;
            public int screenHeight;
            public string screenOrientation;
            public int nativeRotationClockwise;
            public bool hasDisplayMatrix;
            public Matrix4x4 displayMatrix;
        }

        [Serializable]
        private sealed class PoseDiagnostics
        {
            public string state;
            public bool hasTrackedPose;
            public Vector3 rootPosition;
            public Quaternion rootRotation;
            public string projectionDiagnostic;
            public string renderDiagnostic;
        }

        [Serializable]
        private sealed class DebugPointDiagnostics
        {
            public NativeDebugPoint[] points;
        }

        private void OnGUI()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (!showNativeDebugOverlay || !modeEnabled || lastDebugPoints.Count == 0) return;
            foreach (NativeDebugPoint point in lastDebugPoints)
            {
                if (!TryConvertOrientedImageToViewport(
                        new Vector2(point.x01, point.y01), out Vector2 display))
                    continue;
                if (!float.IsFinite(display.x) || !float.IsFinite(display.y)) continue;
                float size = point.kind == 3 ? 14f : point.kind == 2 ? 7f : 5f;
                GUI.color = point.kind switch
                {
                    1 => Color.green,
                    2 => Color.cyan,
                    3 => Color.magenta,
                    _ => new Color(1f, 0.75f, 0f, 1f)
                };
                GUI.DrawTexture(new Rect(display.x * Screen.width - size * 0.5f,
                    (1f - display.y) * Screen.height - size * 0.5f, size, size),
                    Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(12f, Screen.height - 54f, 800f, 44f),
                "ORB: 橙=唯一匹配 绿=PnP内点 青=重投影 洋红=瓶口（displayMatrix仅用于调试绘点）");
#endif
        }

        private static Vector2 OrientedToSourceViewport(Vector2 oriented, int rotation)
        {
            switch ((rotation % 360 + 360) % 360)
            {
                case 90: return new Vector2(1f - oriented.y, oriented.x);
                case 180: return new Vector2(1f - oriented.x, 1f - oriented.y);
                case 270: return new Vector2(oriented.y, 1f - oriented.x);
                default: return oriented;
            }
        }

        private bool TryConvertOrientedImageToViewport(
            Vector2 oriented, out Vector2 viewport)
        {
            viewport = default;
            if (!hasDisplayMatrix) return false;
            Vector2 source = OrientedToSourceViewport(oriented, lastFrameRotation);
            // ARFoundation's display matrix maps display UV to camera-texture
            // UV. Native points are camera-image UV, so image-to-display uses
            // the inverse exactly once. This is diagnostic only; PnP R/t is
            // the sole source of the repair Transform.
            Vector3 display = lastDisplayMatrix.inverse.MultiplyPoint3x4(
                new Vector3(source.x, source.y, 0f));
            if (!float.IsFinite(display.x) || !float.IsFinite(display.y)) return false;
            viewport = new Vector2(display.x, display.y);
            return true;
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private static void ApplyMaterial(GameObject root, Material material)
        {
            if (root == null || material == null)
            {
                return;
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
            }
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null) return;
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }
    }
}
