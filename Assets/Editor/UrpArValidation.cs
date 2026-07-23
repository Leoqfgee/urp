using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
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

            Require(
                OpenCvUnityPoseConverter.TryGetObjectPose(
                    identity,
                    90,
                    camera,
                    profile,
                    out Vector3 portraitPosition,
                    out Quaternion portraitRotation),
                "Portrait PnP pose conversion failed.");
            Require(
                Vector3.Distance(position, portraitPosition) < 0.0001f
                && Quaternion.Angle(rotation, portraitRotation) < 0.01f,
                "Display rotation was incorrectly applied to an already-oriented PnP pose.");

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
                profile.objectId == "bottle_full_aligned_v2",
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
                && Mathf.Abs(profile.calibration.metersPerModelUnit - 0.17f) < 0.0001f,
                "The new canonical B frame or physical scale is invalid.");
            Require(
                profile.referenceValidationMaterial != null
                && profile.referenceValidationMaterial.renderQueue
                >= (int)RenderQueue.Transparent,
                "B validation material must be translucent.");
            Color validationColor =
                profile.referenceValidationMaterial.GetColor("_BaseColor");
            Require(
                !(validationColor.g > 0.8f
                  && validationColor.b > 0.8f
                  && validationColor.r < 0.3f),
                "The prohibited cyan guide color is still configured.");

            byte[] database = File.ReadAllBytes(DatabasePath);
            Require(
                database.Length >= 12
                && database.Take(8).SequenceEqual(
                    new byte[] { 0x55, 0x52, 0x50, 0x33, 0x44, 0x4D, 0x31, 0x00 }),
                "B database has invalid URP3DM1 magic.");
            int records = BitConverter.ToInt32(database, 8);
            Require(
                records >= 1000 && database.Length == 12 + records * 44,
                $"B database record count/length is invalid: {records}.");
            string manifest = File.ReadAllText(DatabaseManifestPath);
            Require(
                manifest.Contains("bottle-full-aligned-v2-reference-b-rendered-v1")
                && manifest.Contains("\"repair_c_excluded_from_matching\": true")
                && manifest.Contains("\"device_overlay_verified\": false"),
                "B database manifest does not describe the new B-only unverified pipeline.");

            GameObject pairPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(NewPairPath);
            GameObject pair = PrefabUtility.InstantiatePrefab(pairPrefab) as GameObject;
            Require(pair != null, "Could not instantiate the new B+C FBX.");
            Transform body = FindDescendant(pair.transform, "DamagedBottleB");
            Transform cap = FindDescendant(pair.transform, "BottleCapC");
            Transform root = FindDescendant(pair.transform, "BottleRepairRoot");
            Require(body != null && cap != null && root != null,
                "New FBX is missing BottleRepairRoot/DamagedBottleB/BottleCapC. "
                + $"Imported transforms: {string.Join(", ", pair.GetComponentsInChildren<Transform>(true).Select(item => item.name))}");
            Require(
                body.parent == root && cap.parent == root,
                "B and C are not fixed siblings under BottleRepairRoot.");
            Require(IsIdentity(body) && IsIdentity(cap),
                "Blender B/C local transforms were not baked to identity.");
            Require(
                body.GetComponentsInChildren<Renderer>(true).Length > 0
                && cap.GetComponentsInChildren<Renderer>(true).Length > 0,
                "B or C has no Renderer.");
            Require(
                HasTexturedMaterial(body) && HasTexturedMaterial(cap),
                "The embedded FBX texture is missing from B or C.");
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
                && controller.Contains("TrackingState.AlignmentProof")
                && controller.Contains("trackingState = TrackingState.Repair")
                && controller.Contains("renderer.enabled = enabled"),
                "Production tracker does not implement pre-aligned B+C, guided B PnP, and Renderer gate.");
            Require(
                !controller.Contains("ConfirmReferenceAlignment")
                && !controller.Contains("ShowReferenceValidation")
                && !controller.Contains("SetRepairVisible"),
                "Production tracker still exposes the removed manual B/C stage controls.");
            string native = File.ReadAllText(NativeSourcePath);
            Require(
                native.Contains("SetPosePrior")
                && native.Contains("guidedMatches")
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
                && setup.Contains("ReferenceBottleBValidation"),
                "Scene generator does not bind the new formal model and validation material.");
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
            Transform cap =
                GetPrivateField<Transform>(controller, "registeredRepairPart");
            Transform pair =
                GetPrivateField<Transform>(controller, "registeredBottlePairRoot");
            Require(body.parent == pair && cap.parent == pair,
                "Runtime changed the Blender-authored B/C parent relationship.");
            Require(
                AnyEnabled(body.GetComponentsInChildren<Renderer>(true))
                && AnyEnabled(cap.GetComponentsInChildren<Renderer>(true)),
                "Entering tracking must show the Blender-aligned B+C pair.");
            Matrix4x4 bodyBefore = body.localToWorldMatrix;
            Matrix4x4 capLocalBefore = pair.worldToLocalMatrix * cap.localToWorldMatrix;

            controller.SetReferenceHierarchyVisible(false);
            controller.SetRepairHierarchyVisible(true);
            Require(
                !AnyEnabled(body.GetComponentsInChildren<Renderer>(true))
                && AnyEnabled(cap.GetComponentsInChildren<Renderer>(true)),
                "Repair stage must disable only B Renderers and show C.");
            Require(body.gameObject.activeSelf && cap.gameObject.activeSelf,
                "Renderer gate disabled B or C GameObjects instead of Renderers.");
            Matrix4x4 capLocalAfter = pair.worldToLocalMatrix * cap.localToWorldMatrix;
            Require(MatrixApproximately(capLocalBefore, capLocalAfter),
                "C local relationship changed while hiding B.");
            Require(MatrixApproximately(bodyBefore, body.localToWorldMatrix),
                "B transform changed while hiding its Renderer.");

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

        private static bool AnyEnabled(Renderer[] renderers)
        {
            return renderers.Any(renderer =>
                renderer != null
                && renderer.enabled
                && renderer.gameObject.activeInHierarchy);
        }

        private static bool HasTexturedMaterial(Transform root)
        {
            return root.GetComponentsInChildren<Renderer>(true).Any(
                renderer => renderer.sharedMaterials.Any(
                    material => material != null && material.mainTexture != null));
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
