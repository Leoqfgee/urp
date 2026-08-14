using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using Urp.ArDemo.Calibration;
using Urp.ArDemo.Native;

namespace Urp.ArDemo.Editor
{
    public static class UrpArValidation
    {
        private const string ScenePath = "Assets/Scenes/UrpARPrototype.unity";
        private const string CatalogPath =
            "Assets/Objects/RestorationObjectCatalog.asset";
        private const string ProfilePath =
            "Assets/Objects/CoconutBottle/Profiles/CoconutBottleRepairProfile.asset";
        private const string NewPairPath =
            "Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/"
            + "bottle_full_aligned_v2.fbx";
        private const string NewPairReportPath =
            "Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/"
            + "bottle_full_aligned_v2_report.json";
        private const string DatabasePath =
            "Assets/OrbModels/bottle_reference_b.bytes";
        private const string DatabaseManifestPath =
            "Assets/OrbModels/bottle_reference_b_manifest.json";
        private const string ModelRegistrationArtifactPath =
            "Assets/Calibration/bottle_orb_to_b_registration_v44.json";
        private const string ProductionVisualQaPath =
            "Assets/Calibration/production_b_visual_qa_v44.json";
        private const string BottleAlbedoPath =
            "Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/"
            + "Textures/bottle_full_clean_v2_albedo.png";
        private const string BottleCapMaterialPath =
            "Assets/Materials/CleanBottleCapLit.mat";
        private const string BottleGhostMaterialPath =
            "Assets/Materials/BottlePreAlignmentGhost.mat";
        private const string ControllerPath =
            "Assets/Scripts/OrbImageTrackingController.cs";
        private const string SetupPath =
            "Assets/Editor/UrpArProjectSetup.cs";
        private const string NativeSourcePath =
            "Native/UrpOrbNative/src/urp_orb_native.cpp";
        private const string CapDiagnosticPath =
            "Assets/Scripts/CapVisibilityDiagnostic.cs";
        private const string PoseDiagnosticPath =
            "Assets/Scripts/PoseCoordinateDiagnostic.cs";
        private const string AppControllerPath =
            "Assets/Scripts/UrpAppController.cs";
        private const string BuildIdentityPath =
            "Assets/Generated/BuildIdentity.cs";
        private const string CanonicalRegistrationPath =
            "Assets/Scripts/Calibration/CanonicalFrameRegistration.cs";
        private const string UnityPoseGatePath =
            "Assets/Scripts/Calibration/UnityPoseConsistencyGate.cs";
        private const string PlayModeSessionKey =
            "UrpArValidation.PlayModeSmokeRunning";

        public static void RunFromCommandLine()
        {
            UrpArProjectSetup.SetupPrototypeScene();
            ValidatePoseConversion();
            ValidateFormalAssets();
            ValidateSingleTrackingArchitecture();
            ValidateRuntimeRendererGate();
            ValidateGeneratedScene();
            Debug.Log("URP_AR_VALIDATION_OK");
        }

        public static void RunPlayModeSmokeFromCommandLine()
        {
            UrpArProjectSetup.SetupPrototypeScene();
            EditorSceneManager.OpenScene(ScenePath);
            SessionState.SetBool(PlayModeSessionKey, true);
            SubscribePlayModeSmoke();
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void RestorePlayModeSmokeAfterDomainReload()
        {
            if (SessionState.GetBool(PlayModeSessionKey, false))
            {
                SubscribePlayModeSmoke();
            }
        }

        private static void SubscribePlayModeSmoke()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(PlayModeSessionKey, false))
            {
                return;
            }
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.delayCall += ValidateEnteredPlayMode;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(PlayModeSessionKey, false);
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                Debug.Log("URP_AR_PLAYMODE_OK");
                EditorApplication.Exit(0);
            }
        }

        private static void ValidateEnteredPlayMode()
        {
            try
            {
                Require(UnityEngine.Object.FindObjectOfType<UrpAppController>(true) != null,
                    "UrpAppController was not created in Play Mode.");
                Require(
                    UnityEngine.Object.FindObjectsOfType<OrbImageTrackingController>(true).Length
                    == 1,
                    "Play Mode must contain exactly one production tracker.");
                Require(
                    UnityEngine.Object.FindObjectsOfType<RepairOverlayController>(true).Length
                    == 1,
                    "Play Mode must contain exactly one repair UI bridge.");
                ValidateNoMissingComponents();
                EditorApplication.ExitPlaymode();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SessionState.SetBool(PlayModeSessionKey, false);
                EditorApplication.Exit(1);
            }
        }

        private static void ValidatePoseConversion()
        {
            GameObject cameraObject = new GameObject("Pose Validation Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            RepairCalibrationProfile profile =
                ScriptableObject.CreateInstance<RepairCalibrationProfile>();
            profile.objectOriginInModel = Vector3.zero;
            profile.mouthCenterInModel = Vector3.zero;
            profile.mouthRightInModel = Vector3.right;
            profile.mouthFrontInModel = Vector3.forward;
            profile.neckAxisPointInModel = Vector3.down;
            profile.metersPerModelUnit = 1f;

            NativeOrbResult identity = new NativeOrbResult
            {
                poseValid = 1,
                tvecX = 0.2f,
                tvecY = -0.3f,
                tvecZ = 2f,
                r00 = 1f,
                r11 = 1f,
                r22 = 1f
            };
            Require(
                OpenCvUnityPoseConverter.TryGetObjectPose(
                    identity,
                    0,
                    camera,
                    profile,
                    out Vector3 position,
                    out Quaternion rotation),
                "Full PnP pose conversion failed.");
            Require(
                Vector3.Distance(position, new Vector3(0.2f, 0.3f, 2f)) < 0.0001f,
                $"PnP translation changed unexpectedly: {position}");
            Require(IsFinite(rotation), "PnP rotation is not finite.");

            foreach (int angle in new[] { 0, 90, 180, 270 })
            {
                Vector3 probe = new Vector3(0.371f, -0.812f, 0.451f).normalized;
                Vector3 restored = OpenCvUnityPoseConverter.UndoImageRotation(
                    OpenCvUnityPoseConverter.RotateForNativeImage(probe, angle),
                    angle);
                Require(
                    Vector3.Distance(probe, restored) < 0.00001f,
                    $"Native image rotation round trip failed at {angle} degrees.");
            }

            NativeOrbResult upright = new NativeOrbResult
            {
                poseValid = 1,
                tvecZ = 2f,
                r00 = -1f,
                r11 = -1f,
                r22 = 1f
            };
            Require(
                OpenCvUnityPoseConverter.TryGetObjectPose(
                    upright, 90, camera, profile, out _, out Quaternion uprightRotation)
                && Vector3.Angle(uprightRotation * Vector3.up, Vector3.up) < 0.0001f,
                "Display-oriented portrait PnP rolled canonical +Y away from Unity +Y.");
            Debug.Log("POSE_ROTATION_ROUNDTRIP_0_90_180_270_OK");

            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        private static void ValidateFormalAssets()
        {
            Require(File.Exists(NewPairPath), $"Missing new B+C FBX: {NewPairPath}");
            Require(
                File.Exists(NewPairReportPath),
                $"Missing Blender B+C report: {NewPairReportPath}");
            Require(File.Exists(DatabasePath), $"Missing B database: {DatabasePath}");
            Require(
                File.Exists(DatabaseManifestPath),
                $"Missing B database manifest: {DatabaseManifestPath}");
            Require(
                File.Exists(ModelRegistrationArtifactPath),
                $"Missing independent model registration: {ModelRegistrationArtifactPath}");
            Require(File.Exists(ProductionVisualQaPath),
                $"Missing production B visual QA: {ProductionVisualQaPath}");
            Require(File.Exists(BottleAlbedoPath),
                $"Missing bottle photogrammetry texture: {BottleAlbedoPath}");
            Require(File.Exists(BottleCapMaterialPath),
                $"Missing clean C material: {BottleCapMaterialPath}");

            RestorationObjectCatalog catalog =
                AssetDatabase.LoadAssetAtPath<RestorationObjectCatalog>(CatalogPath);
            RestorationObjectProfile profile =
                AssetDatabase.LoadAssetAtPath<RestorationObjectProfile>(ProfilePath);
            Require(catalog != null && profile != null, "Catalog or bottle profile is missing.");
            Require(
                catalog.objects != null
                && catalog.objects.Count(item => item == profile) == 1,
                "The formal catalog must contain the new bottle profile exactly once.");
            Require(
                profile.objectId == "bottle_orb_v42_proven_observations",
                "The formal bottle profile still has the legacy object id.");
            Require(
                AssetDatabase.GetAssetPath(profile.registeredBottlePairPrefab) == NewPairPath,
                "registeredBottlePairPrefab does not point to BottleFullAlignedV2.");
            Require(
                profile.trackingReferencePrefab == profile.registeredBottlePairPrefab
                && profile.damagedViewerPrefab == profile.registeredBottlePairPrefab
                && profile.completeViewerPrefab == profile.registeredBottlePairPrefab,
                "Viewer B, viewer B+C and tracker must all derive from the same new FBX.");
            Require(
                AssetDatabase.GetAssetPath(profile.trackingReferenceDatabase) == DatabasePath,
                "The formal profile does not use the regenerated B-only database.");
            Require(
                profile.calibration != null
                && profile.calibration.HasValidFrame
                && !profile.calibration.hasAuthoredBLandmarks
                && AssetDatabase.GetAssetPath(
                    profile.calibration.modelRegistrationArtifact)
                    == ModelRegistrationArtifactPath
                && Mathf.Abs(profile.calibration.metersPerModelUnit - 0.17f) < 0.0001f
                && Quaternion.Angle(
                    Quaternion.Euler(profile.calibration.orbToModelLocalEulerAngles),
                    Quaternion.Euler(90f, 0f, 0f)) < 0.01f
                && profile.calibration.mouthCenterInModel.magnitude > 0.01f,
                "The new canonical B frame or physical scale is invalid.");
            Require(
                profile.viewerMaterial != null
                && profile.viewerMaterial.GetTexture("_BaseMap") != null
                && profile.repairMaterial != null
                && profile.repairMaterial != profile.viewerMaterial
                && profile.repairMaterial.GetTexture("_BaseMap") == null
                && AssetDatabase.GetAssetPath(profile.repairMaterial)
                    == BottleCapMaterialPath,
                "B must use the photogrammetry texture and clean C must use its own white material.");
            Require(
                profile.preAlignmentMaterial != null
                && profile.preAlignmentMaterial != profile.viewerMaterial
                && AssetDatabase.GetAssetPath(profile.preAlignmentMaterial)
                    == BottleGhostMaterialPath
                && profile.preAlignmentMaterial.GetTexture("_BaseMap") == null
                && profile.preAlignmentMaterial.GetFloat("_Surface") > 0.5f
                && Mathf.Abs(profile.preAlignmentMaterial.GetFloat("_Metallic")) < 0.001f
                && Mathf.Abs(profile.preAlignmentMaterial.GetFloat("_Smoothness") - 0.2f) < 0.01f
                && Mathf.Abs(profile.preAlignmentMaterial.GetColor("_BaseColor").a - 0.28f) < 0.01f,
                "BottlePreviewB must use the untextured translucent ghost material.");
            Require(
                profile.referenceDepthOcclusionMaterial == null,
                "B must not retain a depth material that can occlude C after Start.");

            byte[] database = File.ReadAllBytes(DatabasePath);
            Require(
                database.Length >= 12
                && database.Take(8).SequenceEqual(
                    new byte[] { 0x55, 0x52, 0x50, 0x33, 0x44, 0x4D, 0x31, 0x00 }),
                "B database has invalid URP3DM1 magic.");
            int records = BitConverter.ToInt32(database, 8);
            Require(
                records == 4100 && database.Length == 12 + records * 44,
                $"B device-proven v40 observation database is invalid: {records} records.");
            string manifest = File.ReadAllText(DatabaseManifestPath);
            Require(
                manifest.Contains("bottle-orb-device-proven-observations-v42")
                && manifest.Contains("A046CD3386245B4A255A45088ECD9087366FF32A1352B2E20C3AC713253AC1EF")
                && manifest.Contains("\"rendered_mesh_descriptors_used\": false")
                && manifest.Contains("\"records\": 4100")
                && manifest.Contains("\"descriptor_stream_byte_identical_to_v40\": true")
                && manifest.Contains("\"matching_and_pnp_thresholds_modified\": false")
                && manifest.Contains("\"repair_c_excluded_from_matching\": true")
                && manifest.Contains("\"device_overlay_verified\": false"),
                "B database manifest does not describe the real-photo B-only pipeline.");
            string report = File.ReadAllText(NewPairReportPath);
            Require(
                report.Contains("actual iterative closest-point-on-triangle")
                && report.Contains("sourceT_ORB_FROM_B")
                && report.Contains("cLocalMatrix")
                && report.Contains("\"rigidRelationshipPreserved\": true"),
                "Blender report does not describe the v43 baked production B+C contract.");
            string modelRegistration = File.ReadAllText(ModelRegistrationArtifactPath);
            Require(
                modelRegistration.Contains("\"independent_model_registration_verified\"")
                && modelRegistration.Contains("true")
                && modelRegistration.Contains("\"orb_point_to_b_surface_mm\"")
                && modelRegistration.Contains("\"mouth_center_independently_measured\"")
                && modelRegistration.Contains("\"base_center_independently_measured\"")
                && modelRegistration.Contains("\"front_semantics_independently_measured\"")
                && modelRegistration.Contains("\"T_ORB_FROM_B\"")
                && modelRegistration.Contains("\"device_verified\""),
                "Independent ORB-to-B registration evidence is incomplete.");
            Require(
                File.ReadAllText(ProductionVisualQaPath).Contains("\"difference_mm\"")
                && File.ReadAllText(ProductionVisualQaPath).Contains("\"robust_main_component_base_y\""),
                "ProductionBVisualAssetPassesGeometryAndTextureQA failed.");

            GameObject pairPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(NewPairPath);
            GameObject pair = PrefabUtility.InstantiatePrefab(pairPrefab) as GameObject;
            Require(pair != null, "Could not instantiate the new B+C FBX.");
            Transform body = FindDescendant(pair.transform, "DamagedBottleB");
            Transform neck = FindDescendant(pair.transform, "ReferenceNeckProxyB");
            Transform cap = FindDescendant(pair.transform, "BottleCapC");
            Transform root = FindDescendant(pair.transform, "BottleRepairRoot");
            Require(body != null && neck != null && cap != null && root != null,
                "New FBX is missing BottleRepairRoot/DamagedBottleB/"
                + "ReferenceNeckProxyB/BottleCapC. "
                + $"Imported transforms: {string.Join(", ", pair.GetComponentsInChildren<Transform>(true).Select(item => item.name))}");
            Require(
                body.parent == root && neck.parent == body && cap.parent == root,
                "The neck is not part of B or B/C are not rigid siblings.");
            Require(IsIdentity(body) && IsIdentity(neck) && IsIdentity(cap),
                "Blender B/neck/C local transforms were not baked to identity.");
            Require(
                body.GetComponentsInChildren<Renderer>(true).Length > 0
                && cap.GetComponentsInChildren<Renderer>(true).Length > 0,
                "B or C has no Renderer.");
            Bounds importedBodyBounds = CalculateMeshBoundsInRoot(
                root,
                body.GetComponentsInChildren<Renderer>(true));
            Vector3 importedSize = importedBodyBounds.size;
            Require(
                importedSize.y > 0.010f && importedSize.y < 0.017f
                && importedSize.x > 0.003f && importedSize.x < 0.008f
                && importedSize.z > 0.003f && importedSize.z < 0.008f
                && Mathf.Abs(importedBodyBounds.max.y) < 0.001f
                && Vector3.Distance(root.localScale, Vector3.one * 100f) < 0.01f,
                "Unity FBX import changed the Blender B canonical axes, origin, or scale: "
                + $"min=({importedBodyBounds.min.x:F9},{importedBodyBounds.min.y:F9},{importedBodyBounds.min.z:F9}), "
                + $"max=({importedBodyBounds.max.x:F9},{importedBodyBounds.max.y:F9},{importedBodyBounds.max.z:F9}), "
                + $"rootScale={root.localScale}.");
            Debug.Log(
                "FBX_CANONICAL_BOUNDS_OK "
                + $"min={importedBodyBounds.min} max={importedBodyBounds.max} "
                + $"rootRotation=({root.localRotation.x:F9},{root.localRotation.y:F9},"
                + $"{root.localRotation.z:F9},{root.localRotation.w:F9}) "
                + $"rootEuler={root.localRotation.eulerAngles} rootScale={root.localScale}");
            UnityEngine.Object.DestroyImmediate(pair);
        }

        private static void ValidateSingleTrackingArchitecture()
        {
            string controller = File.ReadAllText(ControllerPath);
            string setup = File.ReadAllText(SetupPath);
            string appController = File.ReadAllText(AppControllerPath);
            string buildIdentity = File.ReadAllText(BuildIdentityPath);
            Require(
                appController.Contains("v45")
                && buildIdentity.Contains(
                    "orb-tracking-v45-verified-pose-lock-ghost-preview")
                && buildIdentity.Contains(
                    "coconut-v44-real-trimmed-sim3-production-b"),
                "Visible application/build identity still reports a pre-v45 build.");
            string[] prohibitedControllerTokens =
            {
                "displayMatrix",
                "WorldToViewportPoint",
                "ScreenPoint",
                "ViewportPointToRay",
                "AlignmentOutline",
                "initialMouthPositionInCamera",
                "initialObjectEulerInCamera",
                "ARAnchor",
                "registeredRepairPart.localPosition",
                "registeredRepairPart.localRotation",
                "registeredRepairPart.localScale",
                "hasReadyPoseCandidate",
                "readyCandidatePosition",
                "readyCandidateRotation"
            };
            foreach (string token in prohibitedControllerTokens)
            {
                Require(
                    !controller.Contains(token),
                    $"Production tracker still contains prohibited logic: {token}");
            }
            Require(
                controller.Contains("trackedObjectPoseRoot.position")
                && controller.Contains("trackedObjectPoseRoot.rotation")
                && controller.Contains("PlacePreAlignmentPose")
                && controller.Contains("SetReliableTrackedPosePrior")
                && controller.Contains("tracker.ClearPosePrior()")
                && controller.Contains("!registrationEstablished")
                && controller.Contains("ShowRepairPresentation")
                && controller.Contains("ShowPresentationForCurrentState")
                && controller.Contains("GetCanonicalModelRotationInTrackedRoot")
                && controller.Contains("RestoreProfileCoordinateAlignment")
                && !controller.Contains("CalibrateSessionCoordinateFrame")
                && !controller.Contains("sessionCoordinateFrameCalibrated")
                && controller.Contains("TryApplyReliablePose")
                && controller.Contains("TrackingState.StablePoseApplied")
                && controller.Contains("TrackingState.ReadyForRepair")
                && controller.Contains("CaptureRigidPoseSnapshot")
                && controller.Contains("AssertStartPoseUnchanged")
                && controller.Contains("repairRequested")
                && controller.Contains("recognitionRunning = true")
                && controller.Contains("SetReferenceHierarchyVisible(false)")
                && controller.Contains("ConfidenceWeightedPoseFusion.Step")
                && controller.Contains("VerifiedPoseLock")
                && controller.Contains("BottlePreviewB")
                && controller.Contains("SetPreviewHierarchyVisible")
                && !controller.Contains("maximumWorldPositionCorrectionMetersPerSecond")
                && !controller.Contains("maximumWorldRotationCorrectionDegreesPerSecond")
                && controller.Contains("trackingState = TrackingState.Repair")
                && controller.Contains("renderer.enabled = enabled"),
                "Production tracker does not implement visual pre-alignment, global acquisition, reliable guided PnP, hidden B, and stabilized C.");
            Require(
                !controller.Contains("ConfirmReferenceAlignment")
                && !controller.Contains("ShowReferenceValidation")
                && !controller.Contains("SetRepairVisible"),
                "Production tracker still exposes the removed manual B/C stage controls.");
            int repairPresentationStart = controller.IndexOf(
                "public void ShowRepairPresentation()",
                StringComparison.Ordinal);
            int preAlignmentStart = controller.IndexOf(
                "private void ShowPreAlignmentPair()",
                repairPresentationStart,
                StringComparison.Ordinal);
            string repairPresentation = controller.Substring(
                repairPresentationStart,
                preAlignmentStart - repairPresentationStart);
            Require(
                repairPresentation.Contains("SetReferenceHierarchyVisible(false)")
                && repairPresentation.Contains("SetRepairHierarchyVisible(true)")
                && !repairPresentation.Contains("ApplyMaterial")
                && !repairPresentation.Contains("ApplyTrackedRootPose")
                && !repairPresentation.Contains("RestoreProfileCoordinateAlignment"),
                "Start presentation must only hide B and retain C.");
            string capDiagnostic = File.ReadAllText(CapDiagnosticPath);
            Require(
                capDiagnostic.Contains("[URP_CAP_DIAG]")
                && capDiagnostic.Contains("CalculateFrustumPlanes")
                && capDiagnostic.Contains("currentEnvironmentDepthMode")
                && capDiagnostic.Contains("forceCapDiagnosticMaterial")
                && capDiagnostic.Contains("BottleCapC.corner"),
                "Android cap diagnostics are incomplete.");
            string poseDiagnostic = File.ReadAllText(PoseDiagnosticPath);
            string canonicalRegistration = File.ReadAllText(CanonicalRegistrationPath);
            string unityPoseGate = File.ReadAllText(UnityPoseGatePath);
            Require(
                poseDiagnostic.Contains("[URP_POSE_DIAG]")
                && poseDiagnostic.Contains("registered_mouth_center_b_orb")
                && poseDiagnostic.Contains("DrawLandmarkPair")
                && poseDiagnostic.Contains("ORB screen dirs")
                && poseDiagnostic.Contains("B screen dirs")
                && canonicalRegistration.Contains("OrbToImportedMeshPoint")
                && unityPoseGate.Contains("NativeInlierSet")
                && unityPoseGate.Contains("poseChainRoundTripRmsPixels")
                && unityPoseGate.Contains("hierarchyTransformRoundTripRmsPixels")
                && unityPoseGate.Contains("displayGate=DISABLED"),
                "v41 native-camera, landmark projection, or hierarchy diagnostics are incomplete.");
            Require(
                controller.Contains("[URP_CAMERA_SYNC_DIAG]")
                && controller.Contains("closestArTimestampNs")
                && controller.Contains("cameraPoseDeltaCm"),
                "v41 CPU-image/AR-camera timestamp diagnostics are incomplete.");
            string native = File.ReadAllText(NativeSourcePath);
            Require(
                native.Contains("SetPosePrior")
                && native.Contains("guidedMatches")
                && native.Contains("strictSolution")
                && native.Contains("guidedSolution")
                && native.Contains("SOLVEPNP_SQPNP")
                && native.Contains("SampleReferenceHsv")
                && native.Contains("urp_orb_get_last_inliers")
                && !native.Contains("frameToTarget")
                && !native.Contains("repairAnchor")
                && !native.Contains("set_repair_anchor"),
                "Native tracker must use guided B correspondences and contain no reverse-mutual or bottle-mouth anchor path.");
            string ui = File.ReadAllText(
                "Assets/Scripts/UrpAppController.cs");
            Require(
                !ui.Contains("查看 B 覆盖")
                && !ui.Contains("显示修复 C")
                && ui.Contains("\"开始\", \"重置\", \"文字介绍\", \"返回\""),
                "Tracking page must contain only Start, Reset, Information and Back.");

            string[] prohibitedSetupTokens =
            {
                "bottle_repair_registered.fbx",
                "bottle_damaged_clean.obj",
                "bottle_complete_clean.obj",
                "bottle_cap_clean.obj",
                "AlignmentOutline",
                "ReferenceBottleBAlignmentGuide"
            };
            foreach (string token in prohibitedSetupTokens)
            {
                Require(
                    !setup.Contains(token),
                    $"Scene generator still restores a removed legacy asset: {token}");
            }
            Require(
                setup.Contains("BottleFullAlignedV2")
                && setup.Contains("BottlePhotogrammetryLit")
                && setup.Contains("CleanBottleCapLit")
                && setup.Contains("AROcclusionManager")
                && setup.Contains("RepairAppearanceConsistencyController")
                && setup.Contains("CapVisibilityDiagnostic")
                && setup.Contains("PoseCoordinateDiagnostic"),
                "Scene generator does not bind texture, AR diagnostics, and light consistency.");
            int buildStart = setup.IndexOf(
                "public static void BuildAndroidFromCommandLine()",
                StringComparison.Ordinal);
            int buildEnd = setup.IndexOf(
                "private static void DeleteSupersededBuildArtifacts()",
                buildStart + 1,
                StringComparison.Ordinal);
            string buildMethod = setup.Substring(buildStart, buildEnd - buildStart);
            Require(
                !buildMethod.Contains("SetupPrototypeScene()"),
                "Android build must consume the saved production scene without regenerating it.");
        }

        private static void ValidateRuntimeRendererGate()
        {
            RestorationObjectProfile profile =
                AssetDatabase.LoadAssetAtPath<RestorationObjectProfile>(ProfilePath);
            GameObject cameraObject = new GameObject("Renderer Gate Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            GameObject rootObject = new GameObject("TrackedBottleRoot");
            GameObject alignmentObject = new GameObject("ModelCoordinateAlignment");
            alignmentObject.transform.SetParent(rootObject.transform, false);
            GameObject controllerObject = new GameObject("Renderer Gate Controller");
            OrbImageTrackingController controller =
                controllerObject.AddComponent<OrbImageTrackingController>();
            SetPrivateField(controller, "arCamera", camera);
            SetPrivateField(controller, "trackedObjectPoseRoot", rootObject.transform);
            SetPrivateField(controller, "modelCoordinateAlignment", alignmentObject.transform);
            controller.SetProfile(profile);
            controller.SetTrackingEnabled(true);
            Require(
                GetPrivateField<bool>(controller, "recognitionRunning")
                && !GetPrivateField<bool>(controller, "repairRequested"),
                "Entering tracking must start A-to-B recognition before Start.");
            MethodInfo buildPrior = typeof(OrbImageTrackingController).GetMethod(
                "TryBuildCurrentPosePrior",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object[] priorArguments = { 90, null };
            Require(
                buildPrior != null
                && !(bool)buildPrior.Invoke(controller, priorArguments)
                && priorArguments[1] == null,
                "PreAlignment is visual-only and must not produce a first-acquisition pose prior.");
            Transform body =
                GetPrivateField<Transform>(controller, "registeredReferenceModel");
            Transform neck =
                GetPrivateField<Transform>(controller, "registeredReferenceNeck");
            Transform preview =
                GetPrivateField<Transform>(controller, "registeredPreviewModel");
            Transform cap =
                GetPrivateField<Transform>(controller, "registeredRepairPart");
            Transform pair =
                GetPrivateField<Transform>(controller, "registeredBottlePairRoot");
            Vector3 calibrationFront =
                (profile.calibration.mouthFrontInModel
                 - profile.calibration.mouthCenterInModel).normalized;
            Vector3 calibrationUp =
                (profile.calibration.mouthCenterInModel
                 - profile.calibration.neckAxisPointInModel).normalized;
            float preAlignFrontAngle = Vector3.Angle(
                body.TransformDirection(calibrationFront),
                -camera.transform.forward);
            float preAlignUpAngle = Vector3.Angle(
                body.TransformDirection(calibrationUp),
                camera.transform.up);
            Require(
                Vector3.Dot(
                    body.TransformDirection(calibrationFront),
                    -camera.transform.forward) > 0.99f
                && Vector3.Dot(
                    body.TransformDirection(calibrationUp),
                    camera.transform.up) > 0.99f
                && preAlignFrontAngle < 2f
                && preAlignUpAngle < 2f,
                "PreAlignmentFrontIsActualPrintedFront failed: "
                + $"front={preAlignFrontAngle:F3}deg up={preAlignUpAngle:F3}deg.");
            Debug.Log(
                "PREALIGNMENT_FRONT_IS_ACTUAL_PRINTED_FRONT_OK "
                + $"front={preAlignFrontAngle:F3}deg up={preAlignUpAngle:F3}deg");
            Matrix4x4 derivedMatrix =
                GetPrivateField<Matrix4x4>(controller, "derivedOrbToRenderedBMatrix");
            float landmarkRms =
                GetPrivateField<float>(controller, "derivedAlignmentLandmarkRms");
            float importedScale = CanonicalFrameRegistration.GetImportedHierarchyScale(
                rootObject.transform,
                body);
            Vector3 renderedX = rootObject.transform.InverseTransformDirection(
                body.TransformDirection(
                    CanonicalFrameRegistration.OrbToImportedMeshDirection(Vector3.right)));
            Vector3 renderedY = rootObject.transform.InverseTransformDirection(
                body.TransformDirection(
                    CanonicalFrameRegistration.OrbToImportedMeshDirection(Vector3.up)));
            Vector3 renderedZ = rootObject.transform.InverseTransformDirection(
                body.TransformDirection(
                    CanonicalFrameRegistration.OrbToImportedMeshDirection(Vector3.forward)));
            Vector3 alignmentZeroLongAxis =
                derivedMatrix.inverse.MultiplyVector(Vector3.up).normalized;
            Debug.Log(
                "ORB_RENDERED_B_BAKED_MATRIX_OK "
                + $"matrix={FormatMatrix(derivedMatrix)} rms={landmarkRms:E6} "
                + $"importedHierarchyScale={importedScale:F6} "
                + $"alignment0LongAxis={alignmentZeroLongAxis} "
                + $"orbXInRoot={renderedX} orbYInRoot={renderedY} "
                + $"orbZInRoot={renderedZ} bottleLongAxis={renderedY}");
            Require(
                Vector3.Angle(
                    renderedY,
                    derivedMatrix.MultiplyVector(Vector3.up).normalized) < 0.1f
                && landmarkRms < 0.008f
                && Mathf.Abs(importedScale - 100f) < 0.1f,
                "Unity hierarchy round-trip does not preserve the baked v44 ORB frame: "
                + $"angle={Vector3.Angle(renderedY, Vector3.up):F6}, "
                + $"rms={landmarkRms:E6}, scale={importedScale:F6}.");
            Require(body.parent == pair && neck.IsChildOf(body) && cap.parent == pair,
                "Runtime changed the Blender-authored B/C parent relationship.");
            Require(
                Vector3.Distance(body.position, cap.position) < 0.0001f,
                "Imported B and C no longer share the Blender mouth origin.");
            Require(
                !AnyEnabled(body.GetComponentsInChildren<Renderer>(true))
                && preview != null
                && AnyEnabled(preview.GetComponentsInChildren<Renderer>(true))
                && AnyEnabled(cap.GetComponentsInChildren<Renderer>(true)),
                "Entering tracking must show BottlePreviewB + C while hiding production B.");
            Require(
                AllUseMaterial(
                    preview.GetComponentsInChildren<Renderer>(true),
                    profile.preAlignmentMaterial)
                && AllUseMaterial(
                    cap.GetComponentsInChildren<Renderer>(true),
                    profile.repairMaterial),
                "Pre-alignment must use the translucent ghost B and clean white C material.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                int previewPixels = CountEditorSyntheticRepairPixelDifference(
                    camera,
                    preview.GetComponentsInChildren<Renderer>(true));
                Require(previewPixels > 100,
                    $"BottlePreviewB ghost did not change enough rendered pixels: {previewPixels}.");
                Debug.Log(
                    $"BOTTLE_PREVIEW_GHOST_PIXEL_VISIBILITY_OK pixels={previewPixels} "
                    + "notDeviceVerification=true");
            }
            Matrix4x4 capLocalBefore = pair.worldToLocalMatrix * cap.localToWorldMatrix;

            Vector3 measuredPosition = new Vector3(0.08f, -0.03f, 0.62f);
            Quaternion measuredRotation = Quaternion.Euler(24f, 37f, -12f);
            SetPrivateField(controller, "registrationConfirmationFrames", 3);
            SetPrivateField(controller, "consistencyConfirmationFrames", 3);
            SetPrivateField(controller, "maximumInitialCorrectionMeters", 10f);
            MethodInfo applyReliablePose =
                typeof(OrbImageTrackingController).GetMethod(
                    "TryApplyReliablePose",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Require(applyReliablePose != null,
                "Reliable pre-Start pose application path is missing.");
            for (int frame = 0; frame < 3; frame++)
            {
                PoseConsistencyResult passingConsistency =
                    new PoseConsistencyResult(
                        1f, 0.01f, 0.01f, 8f, 12, true, true);
                object[] poseArguments =
                    {
                        measuredPosition,
                        measuredRotation,
                        new NativeOrbResult
                        {
                            poseValid = 1,
                            poseInliers = 48,
                            uniqueMatches = 60,
                            inlierRatio = 0.8f,
                            reprojectionError = 1.4f,
                            coverageX = 0.42f,
                            coverageY = 0.72f,
                            occupiedGridCells = 9
                        },
                        passingConsistency,
                        null
                    };
                bool applied = (bool)applyReliablePose.Invoke(
                    controller,
                    poseArguments);
                Require(
                    applied == (frame == 2),
                    "Stable Pose must be applied exactly when the confirmation window completes.");
            }
            Require(
                controller.IsRigidRegistrationEstablished
                && controller.CanStartRepair
                && controller.State == OrbImageTrackingController.TrackingState.ReadyForRepair
                && Vector3.Distance(rootObject.transform.position, measuredPosition) < 0.0001f
                && Quaternion.Angle(rootObject.transform.rotation, measuredRotation) < 0.1f
                && Vector3.Distance(
                    alignmentObject.transform.localPosition,
                    GetPrivateField<Vector3>(controller, "derivedAlignmentPosition")) < 0.0001f
                && Quaternion.Angle(
                    alignmentObject.transform.localRotation,
                    GetPrivateField<Quaternion>(controller, "derivedAlignmentRotation")) < 0.1f,
                "PreStartStablePoseIsActuallyApplied failed: B+C did not receive the stable PnP Pose.");
            Require(
                !AnyEnabled(body.GetComponentsInChildren<Renderer>(true))
                && AnyEnabled(preview.GetComponentsInChildren<Renderer>(true))
                && AnyEnabled(cap.GetComponentsInChildren<Renderer>(true)),
                "Stable pre-Start registration must keep Ghost B and C visible.");
            object[] registeredPriorArguments = { 0, null };
            Require(
                (bool)buildPrior.Invoke(controller, registeredPriorArguments),
                "Registered B root did not produce a canonical ORB pose prior.");
            float[] registeredPrior = registeredPriorArguments[1] as float[];
            NativeOrbResult roundTripResult = new NativeOrbResult
            {
                poseValid = 1,
                tvecX = registeredPrior[3],
                tvecY = registeredPrior[7],
                tvecZ = registeredPrior[11],
                r00 = registeredPrior[0],
                r01 = registeredPrior[1],
                r02 = registeredPrior[2],
                r10 = registeredPrior[4],
                r11 = registeredPrior[5],
                r12 = registeredPrior[6],
                r20 = registeredPrior[8],
                r21 = registeredPrior[9],
                r22 = registeredPrior[10]
            };
            Require(
                OpenCvUnityPoseConverter.TryGetObjectPose(
                    roundTripResult,
                    0,
                    camera,
                    profile.calibration,
                    out Vector3 roundTripPosition,
                    out Quaternion roundTripRotation)
                && Vector3.Distance(
                    rootObject.transform.position,
                    roundTripPosition) < 0.00001f
                && Quaternion.Angle(
                    rootObject.transform.rotation,
                    roundTripRotation) < 0.01f,
                "ORB R,t -> ARCamera -> TrackedBottleRoot round trip changed the canonical B pose.");
            Debug.Log("ORB_BLENDER_COORDINATE_CHAIN_ROUNDTRIP_OK");

            Vector3 trackedPositionBeforeUpdate = rootObject.transform.position;
            object[] updateArguments =
            {
                measuredPosition + new Vector3(0.005f, 0f, 0f),
                measuredRotation * Quaternion.Euler(0f, 2f, 0f),
                new NativeOrbResult
                {
                    poseValid = 1,
                    poseInliers = 48,
                    uniqueMatches = 60,
                    inlierRatio = 0.8f,
                    reprojectionError = 1.4f,
                    coverageX = 0.42f,
                    coverageY = 0.72f,
                    occupiedGridCells = 9
                },
                new PoseConsistencyResult(
                    1f, 0.01f, 0.01f, 8f, 12, true, true),
                null
            };
            Require(
                (bool)applyReliablePose.Invoke(controller, updateArguments)
                && Vector3.Distance(
                    trackedPositionBeforeUpdate,
                    rootObject.transform.position) > 0.000001f
                && controller.State
                    == OrbImageTrackingController.TrackingState.ReadyForRepair,
                "New reliable pre-Start PnP poses must continue moving the whole B+C root.");

            Matrix4x4 rootBeforeStart = rootObject.transform.localToWorldMatrix;
            Matrix4x4 pairBeforeStart = pair.localToWorldMatrix;
            Matrix4x4 bodyBeforeStart = body.localToWorldMatrix;
            Matrix4x4 capBeforeStart = cap.localToWorldMatrix;
            controller.StartRecognition();
            Require(
                controller.State == OrbImageTrackingController.TrackingState.Repair,
                "Start did not enter Repair from ReadyForRepair.");
            RequireStartMatrixUnchanged(
                "TrackedBottleRoot",
                rootBeforeStart,
                rootObject.transform.localToWorldMatrix);
            RequireStartMatrixUnchanged(
                "BottleRepairRoot",
                pairBeforeStart,
                pair.localToWorldMatrix);
            RequireStartMatrixUnchanged(
                "DamagedBottleB",
                bodyBeforeStart,
                body.localToWorldMatrix);
            RequireStartMatrixUnchanged(
                "BottleCapC",
                capBeforeStart,
                cap.localToWorldMatrix);
            Renderer[] bodyRenderers =
                body.GetComponentsInChildren<Renderer>(true);
            Renderer[] previewRenderers =
                preview.GetComponentsInChildren<Renderer>(true);
            Renderer[] capRenderersAfterStart =
                cap.GetComponentsInChildren<Renderer>(true);
            Require(
                bodyRenderers.All(renderer =>
                    renderer != null
                    && !renderer.enabled
                    && renderer.forceRenderingOff)
                && previewRenderers.All(renderer =>
                    renderer != null
                    && !renderer.enabled
                    && renderer.forceRenderingOff)
                && capRenderersAfterStart.All(renderer =>
                    renderer != null
                    && renderer.enabled
                    && !renderer.forceRenderingOff),
                "Repair stage must disable every B renderer while keeping C visible.");
            Require(
                AllUseMaterial(
                    cap.GetComponentsInChildren<Renderer>(true),
                    profile.repairMaterial),
                "Start must retain the clean C material.");
            Require(body.gameObject.activeSelf && cap.gameObject.activeSelf,
                "Hiding B Renderers disabled the B or C GameObject.");
            Matrix4x4 capLocalAfter = pair.worldToLocalMatrix * cap.localToWorldMatrix;
            Require(MatrixApproximately(capLocalBefore, capLocalAfter),
                "C local relationship changed while hiding B.");
            Matrix4x4 bodyInPair =
                pair.worldToLocalMatrix * body.localToWorldMatrix;
            Matrix4x4 capInPair =
                pair.worldToLocalMatrix * cap.localToWorldMatrix;
            Require(
                MatrixApproximately(bodyInPair, derivedMatrix)
                && MatrixApproximately(capInPair, Matrix4x4.identity)
                && body.parent == pair
                && cap.parent == pair,
                "The fixed v41-B bridge or target-frame C relationship changed after registration.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                Renderer[] capRenderers =
                    cap.GetComponentsInChildren<Renderer>(true);
                int repairPixels =
                    CountEditorSyntheticRepairPixelDifference(camera, capRenderers);
                Debug.Log(
                    $"EDITOR_SYNTHETIC_CAP_RENDER_SMOKE pixels={repairPixels} "
                    + "nonGating=true notDeviceVerification=true");
            }
            else
            {
                Debug.Log(
                    "EDITOR_SYNTHETIC_CAP_RENDER_SMOKE_SKIPPED graphicsDevice=Null; "
                    + "this is never real-device overlay evidence.");
            }

            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        private static void ValidateGeneratedScene()
        {
            EditorSceneManager.OpenScene(ScenePath);
            Require(
                UnityEngine.Object.FindObjectsOfType<OrbImageTrackingController>(true).Length
                == 1,
                "Generated scene must contain exactly one production tracker.");
            Require(
                UnityEngine.Object.FindObjectsOfType<RepairOverlayController>(true).Length
                == 1,
                "Generated scene must contain exactly one repair UI bridge.");
            Require(
                UnityEngine.Object.FindObjectsOfType<RepairAppearanceConsistencyController>(true)
                    .Length == 1
                && UnityEngine.Object.FindObjectsOfType<AROcclusionManager>(true).Length == 1,
                "Generated scene must contain one light-consistency controller and one AR occlusion manager.");
            Transform trackedRoot = GameObject.Find("TrackedBottleRoot")?.transform;
            Transform alignment = GameObject.Find("ModelCoordinateAlignment")?.transform;
            Require(
                trackedRoot != null
                && trackedRoot.parent == null
                && alignment != null
                && alignment.parent == trackedRoot,
                "Generated scene root hierarchy is invalid.");
            Require(
                !UnityEngine.Object.FindObjectsOfType<Transform>(true).Any(
                    item => item.name.IndexOf("AlignmentOutline",
                                StringComparison.OrdinalIgnoreCase) >= 0
                            || item.name.IndexOf("ManualBox",
                                StringComparison.OrdinalIgnoreCase) >= 0),
                "Generated scene contains a prohibited outline/manual-box object.");
            ValidateNoMissingComponents();
            ValidateButtonEvents();
        }

        private static void ValidateNoMissingComponents()
        {
            foreach (GameObject gameObject in
                     UnityEngine.Object.FindObjectsOfType<GameObject>(true))
            {
                Component[] components = gameObject.GetComponents<Component>();
                Require(
                    components.All(component => component != null),
                    $"Missing Script found on {GetPath(gameObject.transform)}.");
            }
        }

        private static void ValidateButtonEvents()
        {
            foreach (Button button in UnityEngine.Object.FindObjectsOfType<Button>(true))
            {
                for (int index = 0;
                     index < button.onClick.GetPersistentEventCount();
                     index++)
                {
                    Require(
                        button.onClick.GetPersistentTarget(index) != null
                        && !string.IsNullOrWhiteSpace(
                            button.onClick.GetPersistentMethodName(index)),
                        $"Invalid persistent Button event on {GetPath(button.transform)}.");
                }
            }
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

        private static bool IsIdentity(Transform transform)
        {
            return transform.localPosition.sqrMagnitude < 0.000001f
                && Quaternion.Angle(transform.localRotation, Quaternion.identity) < 0.001f
                && Vector3.Distance(transform.localScale, Vector3.one) < 0.0001f;
        }

        private static bool IsFinite(Quaternion value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z)
                && float.IsFinite(value.w);
        }

        private static bool MatrixApproximately(Matrix4x4 left, Matrix4x4 right)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (Mathf.Abs(left[row, column] - right[row, column]) > 0.0001f)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static Bounds CalculateMeshBoundsInRoot(
            Transform root,
            Renderer[] renderers)
        {
            Bounds result = default;
            bool found = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;
                Bounds localBounds;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    localBounds = filter.sharedMesh.bounds;
                }
                else if (renderer is SkinnedMeshRenderer skinned)
                {
                    localBounds = skinned.localBounds;
                }
                else
                {
                    continue;
                }
                Matrix4x4 toRoot =
                    root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                Vector3 min = localBounds.min;
                Vector3 max = localBounds.max;
                for (int x = 0; x <= 1; x++)
                {
                    for (int y = 0; y <= 1; y++)
                    {
                        for (int z = 0; z <= 1; z++)
                        {
                            Vector3 point = toRoot.MultiplyPoint3x4(new Vector3(
                                x == 0 ? min.x : max.x,
                                y == 0 ? min.y : max.y,
                                z == 0 ? min.z : max.z));
                            if (!found)
                            {
                                result = new Bounds(point, Vector3.zero);
                                found = true;
                            }
                            else
                            {
                                result.Encapsulate(point);
                            }
                        }
                    }
                }
            }
            Require(found, "Could not calculate imported mesh bounds.");
            return result;
        }

        private static void RequireStartMatrixUnchanged(
            string label,
            Matrix4x4 before,
            Matrix4x4 after)
        {
            Vector3 beforePosition = before.GetColumn(3);
            Vector3 afterPosition = after.GetColumn(3);
            float positionMeters = Vector3.Distance(beforePosition, afterPosition);
            float rotationDegrees = Quaternion.Angle(before.rotation, after.rotation);
            Vector3 beforeScale = MatrixScale(before);
            Vector3 afterScale = MatrixScale(after);
            float scaleDelta = Vector3.Distance(beforeScale, afterScale);
            Require(
                positionMeters < 0.00001f
                && rotationDegrees < 0.01f
                && scaleDelta < 0.000001f
                && MatrixApproximately(before, after),
                $"StartDoesNotChangeRigidPose failed for {label}: "
                + $"position={positionMeters * 1000f:F6}mm, "
                + $"rotation={rotationDegrees:F6}deg, scale={scaleDelta:E6}.");
            Debug.Log(
                $"START_POSE_ZERO_DELTA_OK target={label} "
                + $"positionMm={positionMeters * 1000f:F6} "
                + $"rotationDeg={rotationDegrees:F6} scaleDelta={scaleDelta:E6}");
        }

        private static Vector3 MatrixScale(Matrix4x4 matrix)
        {
            return new Vector3(
                matrix.GetColumn(0).magnitude,
                matrix.GetColumn(1).magnitude,
                matrix.GetColumn(2).magnitude);
        }

        private static string FormatMatrix(Matrix4x4 value)
        {
            return
                $"[{value.m00:F6},{value.m01:F6},{value.m02:F6},{value.m03:F6};"
                + $"{value.m10:F6},{value.m11:F6},{value.m12:F6},{value.m13:F6};"
                + $"{value.m20:F6},{value.m21:F6},{value.m22:F6},{value.m23:F6};"
                + $"{value.m30:F6},{value.m31:F6},{value.m32:F6},{value.m33:F6}]";
        }

        private static bool AnyEnabled(Renderer[] renderers)
        {
            return renderers.Any(renderer =>
                renderer != null
                && renderer.enabled
                && !renderer.forceRenderingOff
                && renderer.gameObject.activeInHierarchy);
        }

        private static int CountEditorSyntheticRepairPixelDifference(
            Camera camera,
            Renderer[] capRenderers)
        {
            const int Size = 512;
            RenderTexture target = RenderTexture.GetTemporary(
                Size,
                Size,
                24,
                RenderTextureFormat.ARGB32);
            Texture2D readback = new Texture2D(
                Size,
                Size,
                TextureFormat.RGBA32,
                false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            CameraClearFlags previousClearFlags = camera.clearFlags;
            Color previousBackground = camera.backgroundColor;
            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.targetTexture = target;
                Color32[] withRepair = CapturePixels(camera, target, readback, Size);
                foreach (Renderer renderer in capRenderers)
                {
                    renderer.enabled = false;
                    renderer.forceRenderingOff = true;
                }
                Color32[] withoutRepair = CapturePixels(
                    camera,
                    target,
                    readback,
                    Size);
                foreach (Renderer renderer in capRenderers)
                {
                    renderer.forceRenderingOff = false;
                    renderer.enabled = true;
                }
                int changedPixels = 0;
                for (int index = 0; index < withRepair.Length; index++)
                {
                    int difference =
                        Mathf.Abs(withRepair[index].r - withoutRepair[index].r)
                        + Mathf.Abs(withRepair[index].g - withoutRepair[index].g)
                        + Mathf.Abs(withRepair[index].b - withoutRepair[index].b);
                    if (difference >= 12)
                    {
                        changedPixels++;
                    }
                }
                return changedPixels;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.clearFlags = previousClearFlags;
                camera.backgroundColor = previousBackground;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(readback);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Color32[] CapturePixels(
            Camera camera,
            RenderTexture target,
            Texture2D readback,
            int size)
        {
            camera.Render();
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            readback.Apply();
            return readback.GetPixels32();
        }

        private static bool AllUseMaterial(Renderer[] renderers, Material material)
        {
            return material != null
                && renderers.Length > 0
                && renderers.All(renderer =>
                    renderer != null
                    && renderer.sharedMaterials.Length > 0
                    && renderer.sharedMaterials.All(item => item == material));
        }

        private static string GetPath(Transform transform)
        {
            return transform.parent == null
                ? transform.name
                : $"{GetPath(transform.parent)}/{transform.name}";
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().Name, fieldName);
            }
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().Name, fieldName);
            }
            return (T)field.GetValue(target);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
