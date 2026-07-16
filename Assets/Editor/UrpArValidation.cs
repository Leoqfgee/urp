using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Urp.ArDemo.Calibration;
using Urp.ArDemo.Native;

namespace Urp.ArDemo.Editor
{
    public static class UrpArValidation
    {
        private const string ScenePath = "Assets/Scenes/UrpARPrototype.unity";
        private const string CalibrationPath =
            "Assets/Calibration/CoconutBottleRepairCalibration.asset";

        public static void RunFromCommandLine()
        {
            ValidatePoseConversion();
            ValidateGeneratedScene();
            Debug.Log("URP_AR_VALIDATION_OK");
        }

        private static void ValidatePoseConversion()
        {
            GameObject cameraObject = new GameObject("Validation Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            RepairCalibrationProfile profile =
                ScriptableObject.CreateInstance<RepairCalibrationProfile>();
            profile.objectOriginInModel = Vector3.zero;
            profile.mouthCenterInModel = Vector3.zero;
            profile.mouthRightInModel = Vector3.right;
            profile.mouthFrontInModel = Vector3.forward;
            profile.neckAxisPointInModel = Vector3.up;
            profile.metersPerModelUnit = 1f;

            NativeOrbResult identity = IdentityResult(new Vector3(0.2f, -0.3f, 2f));
            Require(
                RepairPoseMath.TryGetObjectPose(
                    identity, 0, camera, profile, out Vector3 position, out Quaternion rotation),
                "Identity PnP pose could not be converted.");
            Require(Vector3.Distance(position, new Vector3(0.2f, 0.3f, 2f)) < 0.0001f,
                $"Identity translation conversion failed: {position}");
            Require(Quaternion.Angle(rotation, Quaternion.identity) < 0.01f,
                $"Identity rotation conversion failed: {rotation.eulerAngles}");

            Require(
                RepairPoseMath.TryGetObjectPose(
                    identity, 90, camera, profile, out Vector3 rotatedPosition, out _),
                "Rotated camera pose could not be converted.");
            Require(Vector3.Distance(rotatedPosition, new Vector3(-0.3f, 0.2f, 2f)) < 0.0001f,
                $"90 degree frame conversion failed: {rotatedPosition}");

            NativeOrbResult rightTranslation = IdentityResult(new Vector3(1f, 0f, 3f));
            Require(
                RepairPoseMath.TryGetObjectPose(
                    rightTranslation, 0, camera, profile, out Vector3 rightPosition, out _),
                "Right translation pose could not be converted.");
            Require(rightPosition.x > 0.99f && rightPosition.z > 2.99f,
                $"Right/depth translation conversion failed: {rightPosition}");

            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        private static void ValidateGeneratedScene()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform objectRoot = FindRequired("Tracked Object Pose Root");
            Transform alignmentRoot = FindRequired("Repair Alignment Root");
            Transform cap = FindRequired("Registered Bottle Cap");
            Transform occluder = FindRequired("Registered Neck Occluder");
            Require(alignmentRoot.IsChildOf(objectRoot), "Alignment root is outside tracked object root.");
            Require(cap.IsChildOf(alignmentRoot), "Registered cap is outside alignment root.");
            Require(occluder.IsChildOf(alignmentRoot), "Occluder is outside alignment root.");
            Require(!occluder.gameObject.activeSelf, "Unverified occluder must remain disabled.");
            Require(cap.GetComponentsInChildren<Renderer>(true).Length > 0,
                "Registered cap has no renderer.");

            RepairCalibrationProfile profile =
                AssetDatabase.LoadAssetAtPath<RepairCalibrationProfile>(CalibrationPath);
            Require(profile != null && profile.HasValidFrame, "Calibration profile is missing or invalid.");

            OrbImageTrackingController tracker =
                UnityEngine.Object.FindObjectOfType<OrbImageTrackingController>(true);
            Require(tracker != null, "ORB tracking controller is missing.");
            SerializedObject trackerObject = new SerializedObject(tracker);
            SerializedProperty models = trackerObject.FindProperty("orbModelFiles");
            Require(models != null && models.arraySize == 1,
                "Tracker must use exactly one merged global ORB model.");
            Require(trackerObject.FindProperty("calibration").objectReferenceValue == profile,
                "Tracker does not reference the generated calibration profile.");
            Require(trackerObject.FindProperty("trackedObjectPoseRoot").objectReferenceValue
                    == objectRoot,
                "Tracker object-pose root reference is incorrect.");

            UrpAppController app =
                UnityEngine.Object.FindObjectOfType<UrpAppController>(true);
            Require(app != null, "Application controller is missing.");
            MethodInfo buildInterface = typeof(UrpAppController).GetMethod(
                "BuildInterface", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(buildInterface != null, "UI builder method is missing.");
            buildInterface.Invoke(app, null);
            Button[] buttons = app.GetComponentsInChildren<Button>(true);
            Require(buttons.Length >= 15, $"Expected complete UI button set, found {buttons.Length}.");
            foreach (Button button in buttons)
            {
                Require(button.targetGraphic != null,
                    $"Button {button.name} has no target graphic.");
                Require(button.targetGraphic.raycastTarget,
                    $"Button {button.name} cannot receive pointer rays.");
                Require(button.onClick.GetPersistentEventCount() > 0
                        || HasRuntimeListener(button),
                    $"Button {button.name} has no click action.");
            }

            Require(buttons.All(button => button.name != "旋转按钮" && button.name != "缩放按钮"),
                "Legacy rotate/zoom buttons still exist.");
            GameObject generatedUi = GameObject.Find("URP Application UI");
            if (generatedUi != null)
            {
                UnityEngine.Object.DestroyImmediate(generatedUi);
            }
        }

        private static bool HasRuntimeListener(Button button)
        {
            FieldInfo callsField = typeof(UnityEngine.Events.UnityEventBase).GetField(
                "m_Calls", BindingFlags.Instance | BindingFlags.NonPublic);
            object calls = callsField?.GetValue(button.onClick);
            FieldInfo runtimeCallsField = calls?.GetType().GetField(
                "m_RuntimeCalls", BindingFlags.Instance | BindingFlags.NonPublic);
            object runtimeCalls = runtimeCallsField?.GetValue(calls);
            PropertyInfo count = runtimeCalls?.GetType().GetProperty("Count");
            return count != null && (int)count.GetValue(runtimeCalls) > 0;
        }

        private static NativeOrbResult IdentityResult(Vector3 translation)
        {
            return new NativeOrbResult
            {
                tracked = 1,
                poseValid = 1,
                tvecX = translation.x,
                tvecY = translation.y,
                tvecZ = translation.z,
                r00 = 1f,
                r11 = 1f,
                r22 = 1f
            };
        }

        private static Transform FindRequired(string name)
        {
            GameObject gameObject = Resources.FindObjectsOfTypeAll<GameObject>()
                .FirstOrDefault(candidate =>
                    candidate.name == name
                    && candidate.scene.IsValid()
                    && candidate.scene.isLoaded);
            Require(gameObject != null, $"Scene object is missing: {name}");
            return gameObject.transform;
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
