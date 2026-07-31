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
            "Assets/Models/CleanBottleReconstruction/BottleCleanCapV30/"
            + "bottle_no_cap_clean_cap_v30.fbx";
        private const string NewPairReportPath =
            "Assets/Models/CleanBottleReconstruction/BottleCleanCapV30/"
            + "bottle_no_cap_clean_cap_v30_report.json";
        private const string DatabasePath =
            "Assets/OrbModels/bottle_reference_b.bytes";
        private const string DatabaseManifestPath =
            "Assets/OrbModels/bottle_reference_b_manifest.json";
        private const string BottleAlbedoPath =
            "Assets/Models/CleanBottleReconstruction/BottleCleanCapV30/"
            + "Textures/bottle_full_clean_v2_albedo.png";
        private const string BottleSurfaceMaterialPath =
            "Assets/Materials/BottlePhotogrammetryLit.mat";
        private const string BottleCapMaterialPath =
            "Assets/Materials/CleanBottleCapLit.mat";
        private const string ControllerPath =
            "Assets/Scripts/OrbImageTrackingController.cs";
        private const string SetupPath =
            "Assets/Editor/UrpArProjectSetup.cs";
        private const string NativeSourcePath =
            "Native/UrpOrbNative/src/urp_orb_native.cpp";
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

        private static bool TryValidateOrbDatabase(
            byte[] database,
            out int records,
            out int viewGroups,
            out string databaseFormat)
        {
            records = 0;
            viewGroups = 0;
            databaseFormat = string.Empty;
            if (database == null || database.Length < 12)
            {
                return false;
            }

            byte[] magicV1 =
                { 0x55, 0x52, 0x50, 0x33, 0x44, 0x4D, 0x31, 0x00 };
            byte[] magicV2 =
                { 0x55, 0x52, 0x50, 0x33, 0x44, 0x4D, 0x32, 0x00 };
            records = BitConverter.ToInt32(database, 8);
            if (database.Take(8).SequenceEqual(magicV1))
            {
                viewGroups = 1;
                databaseFormat = "URP3DM1";
                return records >= 0
                    && (long)database.Length == 12L + (long)records * 44L;
            }

            if (!database.Take(8).SequenceEqual(magicV2)
                || database.Length < 16)
            {
                return false;
            }

            databaseFormat = "URP3DM2";
            viewGroups = BitConverter.ToInt32(database, 12);
            if (records < 0 || viewGroups < 1)
            {
                return false;
            }

            long cursor = 16;
            long parsedRecords = 0;
            for (int group = 0; group < viewGroups; group++)
            {
                if (cursor + 8L > database.Length)
                {
                    return false;
                }
                int groupRecords =
                    BitConverter.ToInt32(database, (int)cursor + 4);
                if (groupRecords < 0)
                {
                    return false;
                }
                cursor += 8L + (long)groupRecords * 44L;
                parsedRecords += groupRecords;
                if (cursor > database.Length)
                {
                    return false;
                }
            }
            return cursor == database.Length && parsedRecords == records;
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
                profile.objectId == "bottle_no_cap_clean_cap_v30",
                "The formal bottle profile still has the legacy object id.");
            Require(
                AssetDatabase.GetAssetPath(profile.registeredBottlePairPrefab) == NewPairPath,
                "registeredBottlePairPrefab does not point to BottleCleanCapV30.");
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
                && Mathf.Abs(profile.calibration.metersPerModelUnit - 0.17f) < 0.0001f,
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
                && profile.preAlignmentMaterial == profile.viewerMaterial
                && profile.preAlignmentMaterial.GetTexture("_BaseMap")
                    == profile.viewerMaterial.GetTexture("_BaseMap")
                && AssetDatabase.GetAssetPath(profile.preAlignmentMaterial)
                    == BottleSurfaceMaterialPath,
                "Pre-alignment B+C must use the opaque textured bottle material.");

            byte[] database = File.ReadAllBytes(DatabasePath);
            int records;
            int viewGroups;
            string databaseFormat;
            Require(
                TryValidateOrbDatabase(
                    database,
                    out records,
                    out viewGroups,
                    out databaseFormat),
                "B database has invalid URP3DM1/URP3DM2 structure.");
            Require(
                records >= 1000 && viewGroups >= 2,
                $"B database coverage is insufficient: {records} records, {viewGroups} groups.");
            string manifest = File.ReadAllText(DatabaseManifestPath);
            Require(
                manifest.Contains("bottle-no-cap-grouped-multiview-v30")
                && manifest.Contains($"\"database_format\": \"{databaseFormat}\"")
                && manifest.Contains($"\"view_group_count\": {viewGroups}")
                && manifest.Contains("\"rendered_mesh_descriptors_used\": false")
                && manifest.Contains("bottle_damaged")
                && manifest.Contains("\"repair_c_excluded_from_matching\": true")
                && manifest.Contains("\"device_overlay_verified\": false"),
                "B database manifest does not describe the real-photo B-only pipeline.");
            string report = File.ReadAllText(NewPairReportPath);
            Require(
                report.Contains("bottle-no-cap-clean-cap-v30")
                && report.Contains("bottle_cap_clean_39x10mm.obj")
                && report.Contains("\"physicalMouthCentreModel\"")
                && report.Contains("\"mouthPlaneModelY\": 0.058823529")
                && report.Contains("\"neckHeightMeters\": 0.01")
                && report.Contains("\"referenceNeckGuideB\"")
                && report.Contains("\"cIsNeverPositionedIndependentlyAtRuntime\": true"),
                "Blender report does not describe the approved clean 39x10mm cap.");

            GameObject pairPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(NewPairPath);
            GameObject pair = PrefabUtility.InstantiatePrefab(pairPrefab) as GameObject;
            Require(pair != null, "Could not instantiate the new B+C FBX.");
            Transform body = FindDescendant(pair.transform, "DamagedBottleB");
            Transform cap = FindDescendant(pair.transform, "BottleCapC");
            Transform neck = FindDescendant(pair.transform, "ReferenceNeckProxyB");
            Transform root = FindDescendant(pair.transform, "BottleRepairRoot");
            Require(body != null && cap != null && neck != null && root != null,
                "New FBX is missing BottleRepairRoot/DamagedBottleB/ReferenceNeckProxyB/BottleCapC. "
                + $"Imported transforms: {string.Join(", ", pair.GetComponentsInChildren<Transform>(true).Select(item => item.name))}");
            Require(
                body.parent == root && cap.parent == root && neck.parent == body,
                "B and C are not rigid siblings or the B-only neck guide is outside B.");
            Require(IsIdentity(body),
                "B must remain at the canonical ORB tracking datum.");
            Vector3 capLiftInPair =
                pair.transform.InverseTransformPoint(cap.position)
                - pair.transform.InverseTransformPoint(root.position);
            Vector3 neckLiftInPair =
                pair.transform.InverseTransformPoint(neck.position)
                - pair.transform.InverseTransformPoint(root.position);
            Require(
                capLiftInPair.magnitude < 0.0002f
                && neckLiftInPair.magnitude < 0.0002f
                && Quaternion.Angle(cap.localRotation, Quaternion.identity) < 0.05f
                && Quaternion.Angle(neck.localRotation, Quaternion.identity) < 0.05f
                && Vector3.Distance(cap.localScale, Vector3.one) < 0.001f
                && Vector3.Distance(neck.localScale, Vector3.one) < 0.001f,
                "The imported v30 geometry lost the 10 mm shoulder-to-mouth registration. "
                + $"capLift={capLiftInPair}, neckLift={neckLiftInPair}, "
                + $"rootScale={root.localScale}.");
            Bounds neckBounds = CalculateLocalMeshBounds(root, neck);
            Bounds capBounds = CalculateLocalMeshBounds(root, cap);
            // The mouth lift is baked into C's Y-up mesh so both FBX object
            // transforms stay at identity and cannot be axis-converted into a
            // sideways cap offset.
            float capToNeckHeightRatio = capBounds.size.y / neckBounds.size.y;
            Require(
                neckBounds.size.y > 0.00001f
                && Mathf.Abs(capToNeckHeightRatio - 1.012f) < 0.03f
                && Mathf.Abs(neckBounds.min.y) < 0.00002f
                && neckBounds.max.y > 0f
                && capBounds.min.y > neckBounds.min.y
                && capBounds.min.y < neckBounds.max.y,
                "The imported v30 neck/cap dimensions no longer match the photographed "
                + "overlapping Blender registration. "
                + $"neck={neckBounds.size.y:F6}, cap={capBounds.size.y:F6}, "
                + $"ratio={capToNeckHeightRatio:F4}, "
                + $"neck={neckBounds.min.y:F5}..{neckBounds.max.y:F5}, "
                + $"cap={capBounds.min.y:F5}..{capBounds.max.y:F5}.");
            Require(
                body.GetComponentsInChildren<Renderer>(true).Length > 0
                && cap.GetComponentsInChildren<Renderer>(true).Length > 0,
                "B or C has no Renderer.");
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
                "ViewportPointToRay",
                "AlignmentOutline",
                "initialMouthPositionInCamera",
                "initialObjectEulerInCamera",
                "ARAnchor",
                "registeredRepairPart.localPosition",
                "registeredRepairPart.localRotation",
                "registeredRepairPart.localScale"
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
                && controller.Contains("IsRepairProjectedIntoCamera")
                && controller.Contains("sessionCoordinateFrameCalibrated")
                && controller.Contains("hasReadyPoseCandidate")
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
            string native = File.ReadAllText(NativeSourcePath);
            Require(
                native.Contains("SetPosePrior")
                && native.Contains("guidedMatches")
                && native.Contains("strictRatioMatches")
                && native.Contains("guidedSolution")
                && native.Contains("modelGroups_")
                && native.Contains("coarseDescriptors_")
                && native.Contains("kRelocalizationGroupLimit")
                && native.Contains("SOLVEPNP_SQPNP")
                && native.Contains("SampleReferenceHsv")
                && native.Contains("priorRotationErrorDegrees")
                && native.Contains("> 100.0f")
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
                setup.Contains("BottleCleanCapV30")
                && setup.Contains("BottlePhotogrammetryLit")
                && setup.Contains("CleanBottleCapLit")
                && setup.Contains("AROcclusionManager")
                && setup.Contains("RepairAppearanceConsistencyController"),
                "Scene generator does not bind texture, clean C, AR resources, and light consistency.");
            int buildStart = setup.IndexOf(
                "public static void BuildAndroidFromCommandLine()",
                StringComparison.Ordinal);
            int buildEnd = setup.IndexOf(
                "private static void DeletePreviousTargetApk()",
                buildStart + 1,
                StringComparison.Ordinal);
            string buildMethod = setup.Substring(buildStart, buildEnd - buildStart);
            Require(
                !buildMethod.Contains("SetupPrototypeScene()"),
                "Android build must consume the saved production scene without regenerating it.");
            Require(
                buildMethod.Contains("BuildOptions.None")
                && !buildMethod.Contains("BuildOptions.Development"),
                "Android build must be a release build without the runtime Display Stats overlay.");
            string buildIdentityRuntime = File.ReadAllText(
                "Assets/Scripts/BuildIdentityRuntime.cs");
            Require(
                buildIdentityRuntime.Contains("enableRuntimeUI = false"),
                "Runtime must explicitly disable the URP debug display.");
            Require(
                ui.Contains("arOcclusionManager.enabled = false")
                && !ui.Contains("arOcclusionManager.enabled = true"),
                "Glossy bottle tracking must not enable unreliable environment depth.");
        }

        private static void ValidateRuntimeRendererGate()
        {
            RestorationObjectProfile profile =
                AssetDatabase.LoadAssetAtPath<RestorationObjectProfile>(ProfilePath);
            GameObject cameraObject = new GameObject("Renderer Gate Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 5f;
            camera.fieldOfView = 60f;
            RenderTexture validationTarget =
                new RenderTexture(720, 1280, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = validationTarget;
            GameObject lightObject = new GameObject("Renderer Gate Light");
            Light validationLight = lightObject.AddComponent<Light>();
            validationLight.type = LightType.Directional;
            validationLight.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
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
            MethodInfo initialPoseGate =
                typeof(OrbImageTrackingController).GetMethod(
                    "IsInitialPoseCorrectionAcceptable",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Require(
                initialPoseGate != null
                && (bool)initialPoseGate.Invoke(
                    controller,
                    new object[]
                    {
                        rootObject.transform.position
                            + camera.transform.right * 0.05f,
                        Quaternion.AngleAxis(20f, camera.transform.up)
                            * rootObject.transform.rotation
                    })
                && !(bool)initialPoseGate.Invoke(
                    controller,
                    new object[]
                    {
                        rootObject.transform.position,
                        Quaternion.AngleAxis(112f, camera.transform.up)
                            * rootObject.transform.rotation
                    })
                && !(bool)initialPoseGate.Invoke(
                    controller,
                    new object[]
                    {
                        rootObject.transform.position
                            + camera.transform.forward * 0.20f,
                        rootObject.transform.rotation
                    }),
                "Initial A-to-B gate must accept coarse overlap but reject the former 112-degree or 20-centimetre pose jump.");
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
            NativeOrbResult priorPose = new NativeOrbResult
            {
                poseValid = 1,
                tvecX = prior[3],
                tvecY = prior[7],
                tvecZ = prior[11],
                r00 = prior[0],
                r01 = prior[1],
                r02 = prior[2],
                r10 = prior[4],
                r11 = prior[5],
                r12 = prior[6],
                r20 = prior[8],
                r21 = prior[9],
                r22 = prior[10]
            };
            Require(
                OpenCvUnityPoseConverter.TryGetObjectPose(
                    priorPose,
                    0,
                    camera,
                    profile.calibration,
                    out Vector3 priorOrbPosition,
                    out Quaternion priorOrbRotation),
                "Could not convert the visible B prior back to a model pose.");
            Quaternion alignmentLocalRotationBefore =
                alignmentObject.transform.localRotation;
            MethodInfo establishRegistration =
                typeof(OrbImageTrackingController).GetMethod(
                    "EstablishRegistration",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Require(establishRegistration != null,
                "The direct full-pose A-to-B registration path is missing.");
            establishRegistration.Invoke(
                controller,
                new object[] { priorOrbPosition, priorOrbRotation });
            Require(
                GetPrivateField<bool>(
                    controller,
                    "sessionCoordinateFrameCalibrated")
                && Vector3.Distance(
                    rootObject.transform.position,
                    priorOrbPosition) < 0.0001f
                && Quaternion.Angle(
                    rootObject.transform.rotation,
                    priorOrbRotation) < 0.1f
                && Quaternion.Angle(
                    alignmentObject.transform.localRotation,
                    alignmentLocalRotationBefore) < 0.1f
                && Vector3.Distance(
                    alignmentObject.transform.localPosition,
                    profile.calibration.orbToModelLocalPosition) < 0.0001f,
                "Registration must apply the full PnP pose to the root while preserving the fixed ORB-to-Blender child transform.");
            object[] calibratedPriorArguments = { null };
            Require(
                (bool)buildPrior.Invoke(controller, calibratedPriorArguments),
                "The calibrated ORB root did not produce a valid pose prior.");
            float[] calibratedPrior = calibratedPriorArguments[0] as float[];
            NativeOrbResult calibratedPriorPose = new NativeOrbResult
            {
                poseValid = 1,
                tvecX = calibratedPrior[3],
                tvecY = calibratedPrior[7],
                tvecZ = calibratedPrior[11],
                r00 = calibratedPrior[0],
                r01 = calibratedPrior[1],
                r02 = calibratedPrior[2],
                r10 = calibratedPrior[4],
                r11 = calibratedPrior[5],
                r12 = calibratedPrior[6],
                r20 = calibratedPrior[8],
                r21 = calibratedPrior[9],
                r22 = calibratedPrior[10]
            };
            Require(
                OpenCvUnityPoseConverter.TryGetObjectPose(
                    calibratedPriorPose,
                    0,
                    camera,
                    profile.calibration,
                    out Vector3 roundTripPosition,
                    out Quaternion roundTripRotation)
                && Vector3.Distance(
                    rootObject.transform.position,
                    roundTripPosition) < 0.0001f
                && Quaternion.Angle(
                    rootObject.transform.rotation,
                    roundTripRotation) < 0.1f,
                "The calibrated ORB pose prior must round-trip directly to TrackedBottleRoot.");
            Vector3 canonicalRootPosition = rootObject.transform.position;
            Quaternion canonicalRootRotation = rootObject.transform.rotation;
            Quaternion[] validationRotations =
            {
                Quaternion.Euler(24f, 37f, 11f) * canonicalRootRotation,
                Quaternion.Euler(-55f, 18f, -7f) * canonicalRootRotation
            };
            string[] validationPoseNames = { "oblique", "top" };
            for (int index = 0; index < validationRotations.Length; index++)
            {
                rootObject.transform.SetPositionAndRotation(
                    canonicalRootPosition,
                    validationRotations[index]);
                object[] fullPosePriorArguments = { null };
                Require(
                    (bool)buildPrior.Invoke(controller, fullPosePriorArguments),
                    $"The {validationPoseNames[index]} B pose did not produce a valid PnP prior.");
                float[] fullPosePrior = fullPosePriorArguments[0] as float[];
                NativeOrbResult fullPosePriorResult = new NativeOrbResult
                {
                    poseValid = 1,
                    tvecX = fullPosePrior[3],
                    tvecY = fullPosePrior[7],
                    tvecZ = fullPosePrior[11],
                    r00 = fullPosePrior[0],
                    r01 = fullPosePrior[1],
                    r02 = fullPosePrior[2],
                    r10 = fullPosePrior[4],
                    r11 = fullPosePrior[5],
                    r12 = fullPosePrior[6],
                    r20 = fullPosePrior[8],
                    r21 = fullPosePrior[9],
                    r22 = fullPosePrior[10]
                };
                Require(
                    OpenCvUnityPoseConverter.TryGetObjectPose(
                        fullPosePriorResult,
                        0,
                        camera,
                        profile.calibration,
                        out Vector3 fullPosePosition,
                        out Quaternion fullPoseRotation)
                    && Vector3.Distance(
                        rootObject.transform.position,
                        fullPosePosition) < 0.0001f
                    && Quaternion.Angle(
                        rootObject.transform.rotation,
                        fullPoseRotation) < 0.1f,
                    $"The {validationPoseNames[index]} full 6DoF pose did not round-trip.");
            }
            rootObject.transform.SetPositionAndRotation(
                canonicalRootPosition,
                canonicalRootRotation);

            Transform body =
                GetPrivateField<Transform>(controller, "registeredReferenceModel");
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
            Require(body.parent == pair && cap.parent == pair,
                "Runtime changed the Blender-authored B/C parent relationship.");
            Require(
                Mathf.Abs(
                    Vector3.Distance(body.position, cap.position))
                    < 0.0002f,
                "Imported C no longer shares B's Blender-authored mouth origin. "
                + $"Runtime distance={Vector3.Distance(body.position, cap.position):F5}m.");
            Require(
                AnyEnabled(body.GetComponentsInChildren<Renderer>(true))
                && AnyEnabled(cap.GetComponentsInChildren<Renderer>(true)),
                "Entering tracking must show the Blender-aligned B+C pair.");
            Require(
                body.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer =>
                        !renderer.name.Contains("ReferenceNeckProxyB"))
                    .All(renderer =>
                        renderer.sharedMaterials.All(material =>
                            material == profile.preAlignmentMaterial))
                && FindDescendant(body, "ReferenceNeckProxyB")
                    .GetComponentsInChildren<Renderer>(true)
                    .All(renderer =>
                        renderer.sharedMaterials.All(material =>
                            material == profile.repairMaterial))
                && AllUseMaterial(
                    cap.GetComponentsInChildren<Renderer>(true),
                    profile.repairMaterial),
                "Pre-alignment must use opaque textured B and the clean white C material.");
            Matrix4x4 bodyBefore = body.localToWorldMatrix;
            Matrix4x4 capLocalBefore = pair.worldToLocalMatrix * cap.localToWorldMatrix;
            int preAlignmentPixels = CaptureRepairPixels(
                camera,
                "prealignment-front",
                validationTarget);
            Require(
                preAlignmentPixels > 10000,
                $"Opaque B+C pre-alignment render is unexpectedly empty: "
                + $"{preAlignmentPixels} pixels.");

            controller.ShowRepairPresentation();
            Require(
                !AnyEnabled(body.GetComponentsInChildren<Renderer>(true))
                && AnyEnabled(cap.GetComponentsInChildren<Renderer>(true)),
                "Repair stage must disable B Renderers while keeping C visible.");
            Require(
                controller.IsRepairActuallyRenderable,
                "C is enabled but is not projected into the active camera after Start.");
            Quaternion frontRotation = rootObject.transform.rotation;
            int frontPixels = CaptureRepairPixels(
                camera,
                "front",
                validationTarget);
            rootObject.transform.rotation =
                Quaternion.AngleAxis(32f, camera.transform.up) * frontRotation;
            int obliquePixels = CaptureRepairPixels(
                camera,
                "oblique",
                validationTarget);
            rootObject.transform.rotation =
                Quaternion.AngleAxis(-32f, camera.transform.right)
                * Quaternion.AngleAxis(18f, camera.transform.up)
                * frontRotation;
            int topPixels = CaptureRepairPixels(
                camera,
                "top",
                validationTarget);
            rootObject.transform.rotation = frontRotation;
            Require(
                frontPixels > 3000
                && obliquePixels > 3000
                && topPixels > 7000,
                $"C-only pixel render failed: front={frontPixels}, "
                + $"oblique={obliquePixels}, top={topPixels}.");
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
            Require(MatrixApproximately(bodyBefore, body.localToWorldMatrix),
                "B transform changed while hiding its Renderers.");

            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(lightObject);
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(validationTarget);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        private static int CaptureRepairPixels(
            Camera camera,
            string viewName,
            RenderTexture target)
        {
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(
                target.width,
                target.height,
                TextureFormat.RGBA32,
                false);
            image.ReadPixels(
                new Rect(0, 0, target.width, target.height),
                0,
                0);
            image.Apply(false);
            Color32[] pixels = image.GetPixels32();
            Color32 background = pixels[0];
            int visiblePixels = pixels.Count(pixel =>
                Mathf.Abs(pixel.r - background.r)
                + Mathf.Abs(pixel.g - background.g)
                + Mathf.Abs(pixel.b - background.b) >= 12);
            string outputDirectory = Path.GetFullPath(
                "Builds/Validation/v30");
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllBytes(
                Path.Combine(outputDirectory, $"repair-c-{viewName}.png"),
                image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.active = previous;
            return visiblePixels;
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
            AROcclusionManager bottleOcclusion =
                UnityEngine.Object.FindObjectOfType<AROcclusionManager>(true);
            Require(
                bottleOcclusion != null && !bottleOcclusion.enabled,
                "Bottle environment-depth manager must remain disabled to keep C visible.");
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

        private static Bounds CalculateLocalMeshBounds(
            Transform coordinateRoot,
            Transform branch)
        {
            bool initialized = false;
            Bounds bounds = default;
            foreach (MeshFilter filter in branch.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 point = coordinateRoot.InverseTransformPoint(
                        filter.transform.TransformPoint(vertex));
                    if (!initialized)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }
            Require(initialized, $"{branch.name} has no imported mesh vertices.");
            return bounds;
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

        private static bool AnyEnabled(Renderer[] renderers)
        {
            return renderers.Any(renderer =>
                renderer != null
                && renderer.enabled
                && renderer.gameObject.activeInHierarchy);
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
