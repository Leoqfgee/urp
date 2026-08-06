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
        private const string BottleAlbedoPath =
            "Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/"
            + "Textures/bottle_full_clean_v2_albedo.png";
        private const string BottleCapMaterialPath =
            "Assets/Materials/CleanBottleCapLit.mat";
        private const string ControllerPath =
            "Assets/Scripts/OrbImageTrackingController.cs";
        private const string SetupPath =
            "Assets/Editor/UrpArProjectSetup.cs";
        private const string NativeSourcePath =
            "Native/UrpOrbNative/src/urp_orb_native.cpp";
        private const string CapDiagnosticPath =
            "Assets/Scripts/CapVisibilityDiagnostic.cs";
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

            NativeOrbResult portraitOriented = new NativeOrbResult
            {
                poseValid = 1,
                tvecX = 0.3f,
                tvecY = 0.2f,
                tvecZ = 2f,
                r00 = 0f,
                r01 = -1f,
                r10 = 1f,
                r11 = 0f,
                r22 = 1f
            };
            Require(
                OpenCvUnityPoseConverter.TryGetObjectPose(
                    portraitOriented,
                    90,
                    camera,
                    profile,
                    out Vector3 portraitPosition,
                    out Quaternion portraitRotation),
                "Portrait-oriented PnP pose conversion failed.");
            Require(
                Vector3.Distance(position, portraitPosition) < 0.0001f
                && Quaternion.Angle(rotation, portraitRotation) < 0.01f,
                "The oriented PnP camera frame did not return to the physical AR camera frame.");

            NativeOrbResult portraitUpsideDownOriented = new NativeOrbResult
            {
                poseValid = 1,
                tvecX = -0.3f,
                tvecY = -0.2f,
                tvecZ = 2f,
                r00 = 0f,
                r01 = 1f,
                r10 = -1f,
                r11 = 0f,
                r22 = 1f
            };
            Require(
                OpenCvUnityPoseConverter.TryGetObjectPose(
                    portraitUpsideDownOriented,
                    270,
                    camera,
                    profile,
                    out Vector3 portraitUpsideDownPosition,
                    out Quaternion portraitUpsideDownRotation)
                && Vector3.Distance(
                    position,
                    portraitUpsideDownPosition) < 0.0001f
                && Quaternion.Angle(
                    rotation,
                    portraitUpsideDownRotation) < 0.01f,
                "The 270-degree PnP camera frame did not return to the physical AR camera frame.");

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
                profile.objectId == "bottle_full_aligned_v2_v37",
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
                && Mathf.Abs(profile.calibration.metersPerModelUnit - 0.17f) < 0.0001f
                && Quaternion.Angle(
                    Quaternion.Euler(profile.calibration.orbToModelLocalEulerAngles),
                    Quaternion.Euler(90f, 0f, 0f)) < 0.01f
                && Mathf.Abs(
                    profile.calibration.mouthCenterInModel.y - 0.05882353f) < 0.0001f,
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
                profile.preAlignmentMaterial == profile.viewerMaterial
                && profile.preAlignmentMaterial != null
                && profile.preAlignmentMaterial.GetTexture("_BaseMap") != null
                && profile.preAlignmentMaterial.GetFloat("_Surface") < 0.5f
                && profile.preAlignmentMaterial.GetColor("_BaseColor").a > 0.95f,
                "Pre-alignment B+C must use the requested opaque textured material.");
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
                $"B device-proven database is invalid: {records} records.");
            string manifest = File.ReadAllText(DatabaseManifestPath);
            Require(
                manifest.Contains("bottle-full-aligned-v2-reference-b-real-observations-v32")
                && manifest.Contains("\"rendered_mesh_descriptors_used\": false")
                && manifest.Contains("bottle_damaged")
                && manifest.Contains("\"repair_c_excluded_from_matching\": true")
                && manifest.Contains("\"device_overlay_verified\": false"),
                "B database manifest does not describe the real-photo B-only pipeline.");
            string report = File.ReadAllText(NewPairReportPath);
            Require(
                report.Contains("bottle-full-aligned-v2-rigid-neck-cap-v33")
                && report.Contains("bottle_cap_clean_39x10mm.obj")
                && report.Contains("\"heightMeters\": 0.01")
                && report.Contains("\"mouthPlaneModelY\": 0.058823529411764705")
                && report.Contains("\"capOverlapsNeckAxially\": true")
                && report.Contains("\"rigidRelationshipPreserved\": true"),
                "Blender report does not describe the approved 10 mm neck and clean cap.");

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
            Vector3 expectedBodyMin =
                new Vector3(-0.0021447852f, -0.0119999993f, -0.0026533404f);
            Vector3 expectedBodyMax =
                new Vector3(0.0028399414f, 0.0005882353f, 0.0022504458f);
            Require(
                Vector3.Distance(importedBodyBounds.min, expectedBodyMin) < 0.00005f
                && Vector3.Distance(importedBodyBounds.max, expectedBodyMax) < 0.00005f
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
                && controller.Contains("SetCurrentPosePrior")
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
                && controller.Contains("worldPositionDeadbandMeters")
                && controller.Contains("maximumWorldRotationCorrectionDegreesPerSecond")
                && controller.Contains("trackingState = TrackingState.Repair")
                && controller.Contains("renderer.enabled = enabled"),
                "Production tracker does not implement pre-alignment, guided PnP, hidden B, and stabilized C.");
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
            string native = File.ReadAllText(NativeSourcePath);
            Require(
                native.Contains("SetPosePrior")
                && native.Contains("guidedMatches")
                && native.Contains("strictSolution")
                && native.Contains("guidedSolution")
                && native.Contains("SOLVEPNP_SQPNP")
                && native.Contains("SampleReferenceHsv")
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
                && setup.Contains("CapVisibilityDiagnostic"),
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
            object[] priorArguments = { null };
            Require(
                buildPrior != null
                && (bool)buildPrior.Invoke(controller, priorArguments),
                "The world-space B+C pre-alignment pose did not produce a valid PnP prior.");
            float[] prior = priorArguments[0] as float[];
            float determinant =
                prior[0] * (prior[5] * prior[10] - prior[6] * prior[9])
                - prior[1] * (prior[4] * prior[10] - prior[6] * prior[8])
                + prior[2] * (prior[4] * prior[9] - prior[5] * prior[8]);
            Require(
                prior.Length == 12
                && Mathf.Abs(determinant - 1f) < 0.01f
                && prior[11] > 0f,
                "The coarse model-to-camera prior is not a proper positive-depth rotation.");
            Transform body =
                GetPrivateField<Transform>(controller, "registeredReferenceModel");
            Transform neck =
                GetPrivateField<Transform>(controller, "registeredReferenceNeck");
            Transform cap =
                GetPrivateField<Transform>(controller, "registeredRepairPart");
            Transform pair =
                GetPrivateField<Transform>(controller, "registeredBottlePairRoot");
            Require(
                Vector3.Dot(
                    body.TransformDirection(Vector3.right),
                    -camera.transform.forward) > 0.99f
                && Vector3.Dot(
                    body.TransformDirection(Vector3.up),
                    camera.transform.up) > 0.99f,
                "Initial B+C pose must show the imported B mesh front-facing and upright.");
            MethodInfo getCanonicalRotation =
                typeof(OrbImageTrackingController).GetMethod(
                    "GetCanonicalModelRotationInTrackedRoot",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Quaternion canonicalRotation =
                (Quaternion)getCanonicalRotation.Invoke(controller, null);
            Require(
                Quaternion.Angle(canonicalRotation, Quaternion.identity) < 0.01f,
                "ModelCoordinateAlignment did not cancel the fixed FBX -90 degree axis conversion.");
            Debug.Log("FBX_AXIS_CONVERSION_CANCELLED_OK");
            Require(body.parent == pair && neck.IsChildOf(body) && cap.parent == pair,
                "Runtime changed the Blender-authored B/C parent relationship.");
            Require(
                Vector3.Distance(body.position, cap.position) < 0.0001f,
                "Imported B and C no longer share the Blender mouth origin.");
            Require(
                AnyEnabled(body.GetComponentsInChildren<Renderer>(true))
                && AnyEnabled(cap.GetComponentsInChildren<Renderer>(true)),
                "Entering tracking must show the Blender-aligned B+C pair.");
            Require(
                AllUseMaterial(
                    body.GetComponentsInChildren<Renderer>(true),
                    profile.preAlignmentMaterial)
                && AllUseMaterial(
                    cap.GetComponentsInChildren<Renderer>(true),
                    profile.repairMaterial),
                "Pre-alignment must use opaque textured B and the clean white C material.");
            Matrix4x4 capLocalBefore = pair.worldToLocalMatrix * cap.localToWorldMatrix;

            Vector3 measuredPosition = new Vector3(0.08f, -0.03f, 0.62f);
            Quaternion measuredRotation = Quaternion.Euler(24f, 37f, -12f);
            SetPrivateField(controller, "registrationConfirmationFrames", 3);
            SetPrivateField(controller, "maximumInitialCorrectionMeters", 10f);
            MethodInfo applyReliablePose =
                typeof(OrbImageTrackingController).GetMethod(
                    "TryApplyReliablePose",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Require(applyReliablePose != null,
                "Reliable pre-Start pose application path is missing.");
            for (int frame = 0; frame < 3; frame++)
            {
                object[] poseArguments =
                    { measuredPosition, measuredRotation, null };
                bool applied = (bool)applyReliablePose.Invoke(
                    controller,
                    poseArguments);
                Require(
                    applied == (frame == 2),
                    "Stable Pose must be applied exactly when the confirmation window completes.");
            }
            Require(
                controller.IsRigidRegistrationEstablished
                && controller.State == OrbImageTrackingController.TrackingState.ReadyForRepair
                && Vector3.Distance(rootObject.transform.position, measuredPosition) < 0.0001f
                && Quaternion.Angle(rootObject.transform.rotation, measuredRotation) < 0.1f
                && Vector3.Distance(
                    alignmentObject.transform.localPosition,
                    profile.calibration.orbToModelLocalPosition) < 0.0001f
                && Quaternion.Angle(
                    alignmentObject.transform.localRotation,
                    Quaternion.Euler(
                        profile.calibration.orbToModelLocalEulerAngles)) < 0.1f,
                "PreStartStablePoseIsActuallyApplied failed: B+C did not receive the stable PnP Pose.");
            Require(
                AnyEnabled(body.GetComponentsInChildren<Renderer>(true))
                && AnyEnabled(cap.GetComponentsInChildren<Renderer>(true)),
                "Stable pre-Start registration must keep both B and C visible.");
            object[] registeredPriorArguments = { null };
            Require(
                (bool)buildPrior.Invoke(controller, registeredPriorArguments),
                "Registered B root did not produce a canonical ORB pose prior.");
            float[] registeredPrior = registeredPriorArguments[0] as float[];
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
            Renderer[] capRenderersAfterStart =
                cap.GetComponentsInChildren<Renderer>(true);
            Require(
                bodyRenderers.All(renderer =>
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
            Require(
                MatrixApproximately(
                    pair.worldToLocalMatrix * body.localToWorldMatrix,
                    pair.worldToLocalMatrix * cap.localToWorldMatrix),
                "B and C no longer share the same rigid authored frame after registration.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                Renderer[] capRenderers =
                    cap.GetComponentsInChildren<Renderer>(true);
                int repairPixels =
                    CountEditorSyntheticRepairPixelDifference(camera, capRenderers);
                Require(
                    repairPixels >= 32,
                    "C is enabled but does not produce visible colour pixels after B is hidden.");
                Debug.Log(
                    $"EDITOR_SYNTHETIC_CAP_RENDER_SMOKE_OK pixels={repairPixels} "
                    + "notDeviceVerification=true");
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
