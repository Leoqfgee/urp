using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using Urp.ArDemo.Native;

namespace Urp.ArDemo.Editor
{
    public static class UrpArValidation
    {
        private const string ScenePath = "Assets/Scenes/UrpARPrototype.unity";
        private const string CatalogPath = "Assets/Objects/RestorationObjectCatalog.asset";
        private const string CleanBottleFolder = "Assets/Models/CleanBottleReconstruction/";

        public static void RunFromCommandLine()
        {
            UrpArProjectSetup.SetupPrototypeScene();
            UrpArProjectSetup.SetupPrototypeScene();
            ValidatePoseConversion();
            ValidateContinuousRigidPairTracking();
            ValidateInvalidProjectionRejection();
            ValidateAssets();
            ValidateGeneratedScene();
            Debug.Log("URP_AR_VALIDATION_OK");
        }

        private static void ValidatePoseConversion()
        {
            GameObject cameraObject = new GameObject("Validation Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            var profile = ScriptableObject.CreateInstance<Calibration.RepairCalibrationProfile>();
            profile.objectOriginInModel = Vector3.zero;
            profile.mouthCenterInModel = Vector3.zero;
            profile.mouthRightInModel = Vector3.right;
            profile.mouthFrontInModel = Vector3.forward;
            profile.neckAxisPointInModel = -Vector3.up;
            profile.metersPerModelUnit = 1f;
            NativeOrbResult identity = new NativeOrbResult
            {
                poseValid = 1, tvecX = 0.2f, tvecY = -0.3f, tvecZ = 2f,
                r00 = 1f, r11 = 1f, r22 = 1f
            };
            Require(Calibration.OpenCvUnityPoseConverter.TryGetObjectPose(
                identity, 0, camera, profile, out Vector3 position, out Quaternion baseRotation),
                "PnP pose conversion failed.");
            Require(Vector3.Distance(position, new Vector3(0.2f, 0.3f, 2f)) < 0.0001f,
                $"Full PnP translation was not preserved: {position}");

            Require(Calibration.OpenCvUnityPoseConverter.TryGetObjectPose(
                identity, 90, camera, profile,
                out Vector3 portraitPosition, out Quaternion portraitRotation),
                "Portrait PnP pose conversion failed.");
            Require(Vector3.Distance(portraitPosition, new Vector3(0.2f, 0.3f, 2f))
                    < 0.0001f,
                $"Portrait translation was converted incorrectly: {portraitPosition}");
            Require(Quaternion.Angle(portraitRotation, baseRotation) < 0.01f,
                "An already-oriented PnP pose was rotated a second time.");

            ValidateOrientation(identity, 0, new Vector3(0.2f, 0.3f, 2f), camera, profile);
            ValidateOrientation(identity, 90, new Vector3(0.2f, 0.3f, 2f), camera, profile);
            ValidateOrientation(identity, 180, new Vector3(0.2f, 0.3f, 2f), camera, profile);
            ValidateOrientation(identity, 270, new Vector3(0.2f, 0.3f, 2f), camera, profile);
            NativeOrbResult right = identity;
            right.tvecX = 0.5f;
            ValidateOrientation(right, 0, new Vector3(0.5f, 0.3f, 2f), camera, profile);
            NativeOrbResult up = identity;
            up.tvecY = -0.6f;
            ValidateOrientation(up, 0, new Vector3(0.2f, 0.6f, 2f), camera, profile);
            ValidateSyntheticRotation(Quaternion.Euler(0f, 30f, 0f), camera, profile, "yaw 30");
            ValidateSyntheticRotation(Quaternion.Euler(20f, 0f, 0f), camera, profile, "pitch 20");
            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        private static void ValidateOrientation(NativeOrbResult result, int rotation,
            Vector3 expected, Camera camera, Calibration.RepairCalibrationProfile profile)
        {
            Require(Calibration.OpenCvUnityPoseConverter.TryGetObjectPose(
                    result, rotation, camera, profile, out Vector3 position,
                    out Quaternion converted),
                $"Pose conversion failed for display rotation {rotation}.");
            Require(Vector3.Distance(position, expected) < 0.0001f,
                $"Position mismatch for display rotation {rotation}: {position} != {expected}");
            Require(float.IsFinite(converted.x) && float.IsFinite(converted.w),
                $"Quaternion invalid for display rotation {rotation}.");
            Require(camera.transform.InverseTransformPoint(position).z > 0f,
                $"Pose moved behind camera for display rotation {rotation}.");
            Vector3 right = converted * Vector3.right;
            Vector3 up = converted * Vector3.up;
            Vector3 forward = converted * Vector3.forward;
            Require(Mathf.Abs(Vector3.Dot(right, up)) < 0.001f
                    && Mathf.Abs(Vector3.Dot(up, forward)) < 0.001f,
                $"Converted basis is mirrored or non-orthogonal at {rotation} degrees.");
        }

        private static void ValidateSyntheticRotation(Quaternion source, Camera camera,
            Calibration.RepairCalibrationProfile profile, string label)
        {
            Matrix4x4 matrix = Matrix4x4.Rotate(source);
            NativeOrbResult result = new NativeOrbResult
            {
                poseValid = 1, tvecZ = 2f,
                r00 = matrix.m00, r01 = matrix.m01, r02 = matrix.m02,
                r10 = matrix.m10, r11 = matrix.m11, r12 = matrix.m12,
                r20 = matrix.m20, r21 = matrix.m21, r22 = matrix.m22
            };
            Require(Calibration.OpenCvUnityPoseConverter.TryGetObjectPose(
                    result, 0, camera, profile, out Vector3 position,
                    out Quaternion rotation),
                $"Synthetic {label} conversion failed.");
            Require(position.z > 0f && float.IsFinite(rotation.w),
                $"Synthetic {label} produced an invalid or rear-camera pose.");
        }

        private static void ValidateContinuousRigidPairTracking()
        {
            GameObject cameraObject = new GameObject("Rigid-pair Validation Camera");
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(1.2f, -0.4f, 2.1f), Quaternion.Euler(8f, 37f, -3f));
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.03f;
            GameObject rootObject = new GameObject("TrackedBottleRoot");
            GameObject alignmentObject = new GameObject("ModelCoordinateAlignment");
            alignmentObject.transform.SetParent(rootObject.transform, false);
            GameObject pairRoot = new GameObject("BottleRepairRoot");
            pairRoot.transform.SetParent(alignmentObject.transform, false);
            GameObject referenceRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            referenceRoot.name = "DamagedBottleB";
            referenceRoot.transform.SetParent(pairRoot.transform, false);
            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cap.name = "BottleCapC";
            cap.transform.SetParent(pairRoot.transform, false);
            cap.transform.localScale = new Vector3(0.23f, 0.06f, 0.23f);
            Vector3 fixedCapPosition = cap.transform.localPosition;
            Quaternion fixedCapRotation = cap.transform.localRotation;
            Vector3 fixedCapScale = cap.transform.localScale;
            GameObject controllerObject = new GameObject("ORB Controller Validation");
            OrbImageTrackingController controller =
                controllerObject.AddComponent<OrbImageTrackingController>();
            var calibration = ScriptableObject.CreateInstance<Calibration.RepairCalibrationProfile>();
            calibration.metersPerModelUnit = 0.17f;
            SetPrivateField(controller, "arCamera", camera);
            SetPrivateField(controller, "trackedObjectPoseRoot", rootObject.transform);
            SetPrivateField(controller, "modelCoordinateAlignment", alignmentObject.transform);
            SetPrivateField(controller, "registeredBottlePairRoot", pairRoot.transform);
            SetPrivateField(controller, "registeredReferenceModel", referenceRoot.transform);
            SetPrivateField(controller, "referenceRenderers",
                referenceRoot.GetComponentsInChildren<Renderer>());
            SetPrivateField(controller, "registeredRepairPart", cap.transform);
            SetPrivateField(controller, "capRenderers", cap.GetComponentsInChildren<Renderer>());
            SetPrivateField(controller, "calibration", calibration);

            Vector3 expectedCameraPosition = new Vector3(0.04f, 0.08f, 0.42f);
            Quaternion expectedCameraRotation = Quaternion.Euler(12f, 24f, 2f);
            Vector3 worldPosition = camera.transform.TransformPoint(expectedCameraPosition);
            Quaternion worldRotation = camera.transform.rotation * expectedCameraRotation;
            MethodInfo establish = typeof(OrbImageTrackingController).GetMethod(
                "EstablishRigidRegistration", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo apply = typeof(OrbImageTrackingController).GetMethod(
                "ApplyTrackedRootPose", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(establish != null && apply != null,
                "Continuous rigid-pair pose methods are missing.");
            establish.Invoke(controller, new object[] { worldPosition, worldRotation });

            Require(rootObject.transform.parent == null,
                "TrackedBottleRoot must remain independent of the AR Camera.");
            Require(Vector3.Distance(rootObject.transform.position, worldPosition)
                    < 0.0001f,
                $"Rigid B+C position mismatch: {rootObject.transform.position}.");
            Require(Quaternion.Angle(rootObject.transform.rotation, worldRotation)
                    < 0.01f,
                "Rigid B+C rotation mismatch.");
            Require(controller.IsRigidRegistrationEstablished && controller.HasTrackedPose,
                "Controller did not establish A-to-B registration.");
            Require(referenceRoot.activeSelf
                    && !referenceRoot.GetComponent<Renderer>().enabled
                    && cap.activeInHierarchy && cap.GetComponent<Renderer>().enabled,
                "Registration must hide only B's Renderer and leave C visible.");

            Vector3 nextPosition = worldPosition + new Vector3(0.04f, -0.01f, 0.03f);
            Quaternion nextRotation = Quaternion.Euler(5f, 12f, 0f) * worldRotation;
            apply.Invoke(controller, new object[] { nextPosition, nextRotation, false });
            Require(Vector3.Distance(rootObject.transform.position, nextPosition) < 0.0001f,
                "Continuous PnP update did not move the complete B+C root.");
            Require(cap.transform.parent == pairRoot.transform
                    && cap.transform.localPosition == fixedCapPosition
                    && Quaternion.Angle(cap.transform.localRotation, fixedCapRotation) < 0.001f
                    && cap.transform.localScale == fixedCapScale,
                "BottleCapC's fixed Blender transform changed during tracking.");
            Require(cap.transform.IsChildOf(rootObject.transform)
                    && !cap.transform.IsChildOf(camera.transform)
                    && cap.GetComponent<RectTransform>() == null,
                "BottleCapC is attached to Camera/Canvas instead of digital bottle B.");

            UnityEngine.Object.DestroyImmediate(calibration);
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, $"Missing private field {name} on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private static void ValidateInvalidProjectionRejection()
        {
            GameObject cameraObject = new GameObject("Projection Validation Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.pixelRect = new Rect(0f, 0f, 1080f, 2400f);
            GameObject controllerObject = new GameObject("Projection Validation Controller");
            OrbImageTrackingController controller =
                controllerObject.AddComponent<OrbImageTrackingController>();
            SetPrivateField(controller, "arCamera", camera);
            SetPrivateField(controller, "hasDisplayMatrix", true);
            SetPrivateField(controller, "lastDisplayMatrix", Matrix4x4.identity);
            SetPrivateField(controller, "lastFrameRotation", 0);
            SetPrivateField(controller, "maximumProjectionConsistencyErrorPixels", 80f);
            MethodInfo validate = typeof(OrbImageTrackingController).GetMethod(
                "TryValidateProjectionConsistency",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(validate != null, "Projection consistency validator is missing.");

            NativeOrbResult invalidZero = new NativeOrbResult
            {
                anchorVisible = 1,
                anchorDepth = 1f,
                anchorX01 = 0f,
                anchorY01 = 0f
            };
            object[] zeroArgs = { invalidZero, Vector3.forward, null };
            Require(!(bool)validate.Invoke(controller, zeroArgs)
                    && ((string)zeroArgs[2]).Contains("投影无效"),
                "B=(0,0) default projection was incorrectly accepted.");

            NativeOrbResult hugeError = invalidZero;
            hugeError.anchorX01 = 0.95f;
            hugeError.anchorY01 = 0.95f;
            object[] hugeArgs = { hugeError, Vector3.forward, null };
            Require(!(bool)validate.Invoke(controller, hugeArgs)
                    && ((string)hugeArgs[2]).Contains("拒绝当前位姿"),
                "A/B projection error far above threshold was incorrectly accepted.");

            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        private static void ValidateAssets()
        {
            RestorationObjectCatalog catalog =
                AssetDatabase.LoadAssetAtPath<RestorationObjectCatalog>(CatalogPath);
            Require(catalog != null, "Restoration object catalog is missing.");
            Require(catalog.objects.Length == 2, "Catalog must contain exactly two real objects.");
            RestorationObjectProfile bottle =
                catalog.objects.SingleOrDefault(item => item.objectId == "coconut_bottle");
            RestorationObjectProfile tissue =
                catalog.objects.SingleOrDefault(item => item.objectId == "tissue");
            Require(bottle != null && bottle.HasTrackingAssets,
                "Bottle profile tracking references are incomplete.");
            Require(bottle.registeredOccluderPrefab != null,
                "Bottle neck occluder is missing.");
            Require(bottle.physicalScaleVerified
                    && bottle.calibration != null
                    && bottle.calibration.objectOriginInModel == Vector3.zero
                    && bottle.calibration.mouthCenterInModel == Vector3.zero
                    && Mathf.Abs(bottle.calibration.metersPerModelUnit - 0.17f) < 0.000001f
                    && Mathf.Abs(bottle.calibration.expectedPhysicalNeckDiameter - 0.034f)
                        < 0.000001f
                    && Mathf.Abs(bottle.calibration.expectedPhysicalCapDiameter - 0.039f)
                        < 0.000001f
                    && Mathf.Abs(bottle.calibration.expectedPhysicalCapHeight - 0.010f)
                        < 0.000001f
                    && bottle.calibration.orbToModelLocalPosition == Vector3.zero
                    && bottle.calibration.orbToModelLocalEulerAngles == Vector3.zero
                    && bottle.calibration.orbToModelLocalScale == Vector3.one
                    && bottle.calibration.capLocalPosition == Vector3.zero
                    && !bottle.calibration.occluderVerified,
                "Bottle canonical physical calibration is incomplete.");
            Require(bottle.physicalMeasurements.Length == 3
                    && bottle.physicalMeasurements.All(item => item.verified),
                "Bottle measurements are not recorded in the profile.");
            Require(File.Exists(CleanBottleFolder + "bottle_cap_registration_report.json"),
                "Bottle cap registration report is missing.");
            Require(bottle.registeredBottlePairPrefab != null
                    && FindChild(bottle.registeredBottlePairPrefab.transform, "DamagedBottleB") != null
                    && FindChild(bottle.registeredBottlePairPrefab.transform, "BottleCapC") != null,
                "Blender FBX does not preserve BottleRepairRoot/DamagedBottleB + BottleCapC.");
            string registrationReport = File.ReadAllText(
                CleanBottleFolder + "bottle_cap_registration_report.json");
            Require(registrationReport.Contains("\"T_b_c\"")
                    && registrationReport.Contains("\"T_orb_to_b\"")
                    && registrationReport.Contains("coconut-photogrammetry-b-c-rigid-registration-v5"),
                "B+C registration report lacks the fixed rigid transforms.");
            Require(bottle.defaultViewerEuler == Vector3.zero,
                "Bottle viewer must open in an upright front view.");
            Require(AssetDatabase.GetAssetPath(bottle.damagedViewerPrefab)
                        == CleanBottleFolder + "bottle_damaged_clean.obj"
                    && AssetDatabase.GetAssetPath(bottle.completeViewerPrefab)
                        == CleanBottleFolder + "bottle_complete_clean.obj"
                    && AssetDatabase.GetAssetPath(bottle.trackingReferencePrefab)
                        == CleanBottleFolder + "bottle_damaged_clean.obj"
                    && AssetDatabase.GetAssetPath(bottle.registeredBottlePairPrefab)
                        == CleanBottleFolder + "bottle_repair_registered.fbx"
                    && AssetDatabase.GetAssetPath(bottle.trackingReferenceDatabase)
                        == "Assets/OrbModels/bottle_reference_b.bytes"
                    && bottle.orbModelDatabase == null
                    && AssetDatabase.GetAssetPath(bottle.registeredRepairPrefab)
                        == CleanBottleFolder + "bottle_cap_clean.obj",
                "Bottle profile is not using the explicit a-to-b-to-c tracking set.");
            ValidateCleanBottleGeometry();
            Require(tissue != null && tissue.damagedViewerPrefab != null,
                "Tissue viewer profile is incomplete.");
            Require(tissue.trackingReferencePrefab == null
                    && tissue.registeredBottlePairPrefab == null
                    && tissue.trackingReferenceDatabase == null
                    && tissue.orbModelDatabase == null
                    && tissue.registeredRepairPrefab == null,
                "Tissue must not inherit bottle tracking or repair assets.");
            Require(!tissue.physicalScaleVerified,
                "Tissue physical scale cannot be marked verified without measurements.");
            Require(tissue.calibration != null
                    && tissue.calibration.expectedPhysicalNeckDiameter == 0f
                    && tissue.calibration.expectedPhysicalCapDiameter == 0f
                    && tissue.calibration.expectedPhysicalCapHeight == 0f,
                "Tissue calibration must not inherit bottle dimensions.");
            foreach (RestorationObjectProfile profile in catalog.objects)
            {
                Require(profile.viewerMaterial != null
                        && profile.viewerMaterial.shader.name == "Universal Render Pipeline/Lit",
                    $"{profile.objectId} viewer material is not URP/Lit.");
            }
            Require(bottle.repairMaterial != null
                    && bottle.repairMaterial.IsKeywordEnabled("_EMISSION")
                    && bottle.repairMaterial.GetColor("_EmissionColor").maxColorComponent >= 0.15f
                    && bottle.repairMaterial.HasProperty("_Cull")
                    && Mathf.Approximately(bottle.repairMaterial.GetFloat("_Cull"), 0f),
                "Bottle cap material lacks fill light or two-sided rendering.");
            Require(bottle.initialGuideMaterial != null
                    && bottle.initialGuideMaterial.shader != null
                    && bottle.initialGuideMaterial.shader.name == "Universal Render Pipeline/Lit"
                    && bottle.initialGuideMaterial.renderQueue >= (int)RenderQueue.Transparent
                    && bottle.initialGuideMaterial.GetColor("_BaseColor").a < 0.5f,
                "Reference model B does not use the translucent initial-alignment guide material.");
            Require(bottle.trackingSettings.minimumGoodMatches == 9
                    && bottle.trackingSettings.minimumPoseInliers == 6
                    && bottle.trackingSettings.minimumInlierRatio >= 0.50f
                    && bottle.trackingSettings.maximumReprojectionErrorPixels <= 3.0f
                    && bottle.trackingSettings.minimumCoverageX <= 0.05f
                    && bottle.trackingSettings.registrationConfirmationFrames >= 10
                    && bottle.trackingSettings.registrationPositionToleranceMeters <= 0.025f
                    && bottle.trackingSettings.registrationRotationToleranceDegrees <= 8f
                    && bottle.trackingSettings.maximumProjectionConsistencyErrorPixels <= 80f
                    && bottle.trackingSettings.temporaryLossHoldSeconds >= 0.5f
                    && bottle.trackingSettings.initialAlignmentMaximumViewportError <= 0.28f
                    && bottle.trackingSettings.initialAlignmentMaximumUpAxisErrorDegrees <= 55f,
                "Bottle A-to-B registration settings do not protect continuous rigid tracking.");

            string trackingControllerSource = File.ReadAllText(
                "Assets/Scripts/OrbImageTrackingController.cs");
            Require(trackingControllerSource.Contains("EstablishRigidRegistration")
                    && trackingControllerSource.Contains("ApplyTrackedRootPose")
                    && trackingControllerSource.Contains("TryValidateProjectionConsistency")
                    && trackingControllerSource.Contains("defaultZero")
                    && !trackingControllerSource.Contains("LockRegisteredPairInWorld")
                    && !trackingControllerSource.Contains("registeredRepairPart.localPosition =")
                    && !trackingControllerSource.Contains("registeredRepairPart.localRotation =")
                    && !trackingControllerSource.Contains("registeredRepairPart.localScale =")
                    && !trackingControllerSource.Contains("ViewportPointToRay")
                    && !trackingControllerSource.Contains("ScreenPointToRay"),
                "Runtime still contains direct C placement, camera-ray placement, or one-shot lock logic.");

            string[] runtimeFiles = Directory.GetFiles("Assets", "*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".cs") || path.EndsWith(".asset")
                    || path.EndsWith(".prefab") || path.EndsWith(".unity"))
                .Where(path => !path.Replace('\\', '/').StartsWith("Assets/Editor/"))
                .ToArray();
            foreach (string path in runtimeFiles)
            {
                string text = File.ReadAllText(path);
                Require(!text.Contains(@"F:\Au\暑期任务\Tissue"),
                    $"Runtime asset contains a source-drive absolute path: {path}");
            }
        }

        private static void ValidateCleanBottleGeometry()
        {
            ValidateObjBounds(CleanBottleFolder + "bottle_damaged_clean.obj",
                new Vector3(0.524581f, 1.20f, 0.719884f), 0.015f, 50000, 100000);
            ValidateObjBounds(CleanBottleFolder + "bottle_complete_clean.obj",
                new Vector3(0.524581f, 1.2088235f, 0.719884f), 0.018f, 50000, 100000);
            ValidateObjBounds(CleanBottleFolder + "bottle_cap_clean.obj",
                new Vector3(0.2294118f, 0.0588235f, 0.2294118f), 0.004f, 1400, 2500);
        }

        private static void ValidateObjBounds(string path, Vector3 expectedSize,
            float tolerance, int minimumVertices, int minimumFaces)
        {
            Require(File.Exists(path), $"Registered model is missing: {path}");
            int vertices = 0;
            int faces = 0;
            Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity,
                float.PositiveInfinity);
            Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity,
                float.NegativeInfinity);
            foreach (string line in File.ReadLines(path))
            {
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    string[] values = line.Split(new[] { ' ' },
                        StringSplitOptions.RemoveEmptyEntries);
                    Vector3 vertex = new Vector3(
                        float.Parse(values[1], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(values[2], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(values[3], System.Globalization.CultureInfo.InvariantCulture));
                    minimum = Vector3.Min(minimum, vertex);
                    maximum = Vector3.Max(maximum, vertex);
                    vertices++;
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    faces++;
                }
            }

            Vector3 size = maximum - minimum;
            Require(vertices >= minimumVertices && faces >= minimumFaces,
                $"Registered model is unexpectedly sparse: {path}");
            Require(Mathf.Abs(size.x - expectedSize.x) <= tolerance
                    && Mathf.Abs(size.y - expectedSize.y) <= tolerance
                    && Mathf.Abs(size.z - expectedSize.z) <= tolerance,
                $"Clean model bounds do not match the measured envelope: {path}, {size}");
            Require(Mathf.Max(size.x, size.z) < 0.75f && size.y < 1.30f,
                $"Registered model exceeds the approved photogrammetry envelope: {path}");
        }

        private static void ValidateGeneratedScene()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(PlayerSettings.productName == "刚性瓶体配准修复"
                    && PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android)
                        == "com.qfgeeee.paper52objecttrackingar"
                    && PlayerSettings.bundleVersion == "4.2.0"
                    && PlayerSettings.Android.bundleVersionCode == 420,
                "Android app identity reverted to the legacy application.");
            RestorationObjectCatalog catalog =
                AssetDatabase.LoadAssetAtPath<RestorationObjectCatalog>(CatalogPath);
            Require(catalog != null, "Restoration object catalog is missing.");
            Transform objectRoot = FindRequired("TrackedBottleRoot");
            Transform alignment = FindRequired("ModelCoordinateAlignment");
            Require(alignment.IsChildOf(objectRoot), "Model alignment is outside pose root.");
            Transform occlusionRoot = FindRequired("OcclusionRoot");
            Transform debugRoot = FindRequired("DebugRoot");
            OrbImageTrackingController tracker =
                UnityEngine.Object.FindObjectOfType<OrbImageTrackingController>(true);
            Require(tracker != null, "ORB tracking controller is missing.");
            SerializedObject trackerSerialized = new SerializedObject(tracker);
            Require(trackerSerialized.FindProperty("initialMouthPositionInCamera").vector3Value
                        == new Vector3(0f, 0.16f, 0.42f)
                    && trackerSerialized.FindProperty("initialObjectEulerInCamera").vector3Value
                        == new Vector3(0f, 180f, 0f),
                "Initial B outline framing or front-facing convention has regressed.");
            Require(occlusionRoot.parent == alignment && !occlusionRoot.gameObject.activeSelf,
                "Unverified occluder must be disabled by default.");
            Require(debugRoot.parent == alignment && !debugRoot.gameObject.activeSelf,
                "DebugRoot must be disabled outside Development diagnostics.");

            Camera arCamera = FindRequired("AR Camera").GetComponent<Camera>();
            ARSession arSession = FindRequired("AR Session").GetComponent<ARSession>();
            ARCameraManager cameraManager = arCamera.GetComponent<ARCameraManager>();
            ARCameraBackground background = arCamera.GetComponent<ARCameraBackground>();
            Camera viewerCamera = FindRequired("Resource Viewer Camera").GetComponent<Camera>();
            Require(viewerCamera.targetTexture == null,
                "Viewer camera must not persist a full-screen target at scene load.");
            Require((arCamera.cullingMask & viewerCamera.cullingMask) == 0,
                "AR and viewer cameras render the same model layer.");
            Require(background != null, "AR camera background is missing.");
            Require((arCamera.cullingMask & 1) != 0,
                "AR camera cullingMask does not include the bottle-cap layer.");
            Require(arCamera.nearClipPlane <= 0.021f && arCamera.farClipPlane >= 10f,
                $"AR camera clipping range rejects a near bottle cap: "
                + $"near={arCamera.nearClipPlane:F3}, far={arCamera.farClipPlane:F1}.");
            Require(arSession != null && !arSession.enabled,
                "AR Session must stay disabled outside tracking mode at scene load.");
            Require(cameraManager != null && !cameraManager.enabled
                    && !background.enabled && !arCamera.enabled,
                "AR camera components must stay disabled outside tracking mode.");
            Light repairKey = FindRequired("AR Repair Key Light").GetComponent<Light>();
            Light repairFill = FindRequired("AR Repair Fill Light").GetComponent<Light>();
            Require(repairKey != null && repairFill != null
                    && repairKey.transform.IsChildOf(arCamera.transform)
                    && repairFill.transform.IsChildOf(arCamera.transform)
                    && repairKey.cullingMask == 1 && repairFill.cullingMask == 1,
                "AR repair lighting is missing or affects the wrong render layer.");
            UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                "Assets/Settings/UrpMobileRenderer.asset");
            Require(renderer != null && renderer.rendererFeatures
                    .OfType<ARBackgroundRendererFeature>().Any(),
                "URP renderer is missing the AR camera background feature.");
            Material forceMagenta = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Resources/ForceMagentaDebug.mat");
            Require(forceMagenta != null
                    && forceMagenta.shader != null
                    && forceMagenta.shader.name == "Hidden/URP/ForceMagentaDebug",
                "Force-render test does not use the hard-coded magenta shader.");
            Material alignmentOutline = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Resources/AlignmentOutline.mat");
            Require(alignmentOutline != null
                    && alignmentOutline.shader != null
                    && alignmentOutline.shader.name == "Hidden/URP/AlignmentOutline",
                "Alignment outline does not use its packaged unlit shader.");

            UrpAppController app =
                UnityEngine.Object.FindObjectOfType<UrpAppController>(true);
            Require(app != null, "Application controller is missing.");
            Require(UnityEngine.Object.FindObjectOfType<BuildIdentityRuntime>(true) != null,
                "BuildIdentity runtime logger is missing.");
            MethodInfo build = typeof(UrpAppController).GetMethod(
                "BuildInterface", BindingFlags.Instance | BindingFlags.NonPublic);
            build?.Invoke(app, null);
            Transform ui = FindRequired("URP Application UI");
            CanvasScaler scaler = ui.GetComponent<CanvasScaler>();
            Require(scaler != null
                    && scaler.referenceResolution == new Vector2(1080f, 2400f)
                    && Mathf.Approximately(scaler.matchWidthOrHeight, 0.5f),
                "Canvas scaling has reverted to the oversized legacy phone proportions.");
            Require(FindChild(ui, "FullScreenBackground") != null,
                "Full-screen background is missing.");
            RectTransform topCover = FindChild(ui, "TrackingTopSystemBarCover") as RectTransform;
            RectTransform bottomCover = FindChild(ui, "TrackingBottomSystemBarCover") as RectTransform;
            Require(topCover != null && bottomCover != null
                    && topCover.anchorMin.y <= 0.8951f && topCover.anchorMax.y == 1f
                    && bottomCover.anchorMin.y == 0f && bottomCover.anchorMax.y >= 0.0249f,
                "Tracking UI does not cover the unsafe top/bottom system-bar regions.");
            Transform safeArea = FindChild(ui, "SafeArea");
            Require(safeArea != null, "SafeArea is missing.");
            Require(FindChild(ui, "ModalLayer") != null, "ModalLayer is missing.");
            foreach (string page in new[]
                     {
                         "HomePageContent", "ObjectSelectionPageContent",
                         "ResourcePageContent", "TrackingPageContent"
                     })
                Require(FindChild(safeArea, page) != null, $"Page is missing: {page}");
            Transform developmentPanel = FindChild(ui, "DevelopmentDebugPanel");
            Require(developmentPanel != null && !developmentPanel.gameObject.activeSelf,
                "Development diagnostics must be collapsed by default.");
            Transform trackingPage = FindChild(safeArea, "TrackingPageContent");
            RectTransform trackingHeader = FindChild(trackingPage, "Header") as RectTransform;
            Require(trackingHeader != null && trackingHeader.anchorMin.y >= 0.919f,
                "Tracking title bar is too tall or leaves an unsafe camera strip above it.");
            string[] diagnosticLabels =
            {
                "静态检查 B+CButton", "半透明 B 对准模式Button", "仅显示固定 CButton",
                "保存失败数据Button", "重置 A↔B 配准Button"
            };
            Require(diagnosticLabels.All(label => FindChild(developmentPanel, label) != null),
                "Development panel is not aligned with the continuous rigid B+C flow.");
            RawImage viewport = UnityEngine.Object.FindObjectsOfType<RawImage>(true)
                .FirstOrDefault(item => item.name == "ModelViewport");
            Require(viewport != null && viewport.GetComponent<ModelViewportInputHandler>() != null,
                "ModelViewport input handler is missing.");
            foreach (Button button in app.GetComponentsInChildren<Button>(true))
            {
                Require(button.targetGraphic != null && button.targetGraphic.raycastTarget,
                    $"Button cannot receive pointer input: {button.name}");
            }
            RectTransform cardViewport =
                FindChild(ui, "ObjectCardViewport") as RectTransform;
            RectTransform cardContent =
                FindChild(ui, "ObjectCardContent") as RectTransform;
            Require(cardViewport != null && cardContent != null,
                "Object selection layout is missing.");
            LayoutRebuilder.ForceRebuildLayoutImmediate(cardContent);
            Canvas.ForceUpdateCanvases();
            Transform[] cards = cardContent.Cast<Transform>()
                .Where(item => item.name.EndsWith(" Card", StringComparison.Ordinal))
                .ToArray();
            Require(cards.Length == catalog.objects.Count(item => item != null),
                "Object selection cards do not match the catalog.");
            Require(cards.All(item => ((RectTransform)item).rect.height >= 350f),
                "Object selection cards collapsed to zero height.");
            Require(cards.All(item => item.GetComponentsInChildren<Button>(true).Length == 1),
                "Each object selection card must be one direct selection target without duplicate mode buttons.");
            Bounds firstCardBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                cardViewport, (RectTransform)cards[0]);
            Rect viewportRect = cardViewport.rect;
            Require(firstCardBounds.max.x > viewportRect.xMin
                    && firstCardBounds.min.x < viewportRect.xMax
                    && firstCardBounds.max.y > viewportRect.yMin
                    && firstCardBounds.min.y < viewportRect.yMax,
                "The first object card is outside the visible selection viewport.");
            UnityEngine.Object.DestroyImmediate(ui.gameObject);
        }

        private static Transform FindRequired(string name)
        {
            GameObject gameObject = Resources.FindObjectsOfTypeAll<GameObject>()
                .FirstOrDefault(candidate => candidate.name == name
                    && candidate.scene.IsValid() && candidate.scene.isLoaded);
            Require(gameObject != null, $"Scene object is missing: {name}");
            return gameObject.transform;
        }

        private static Transform FindChild(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == name);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
