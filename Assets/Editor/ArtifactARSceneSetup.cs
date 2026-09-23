using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using GLTFast;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Urp.ArDemo.Editor
{
    public static class ArtifactARSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/ArtifactARScene.unity";
        private const string InfoPath = "Assets/Models/Artifacts/ShengDing/ShengDingInfo.asset";

        [MenuItem("URP AR/Setup Artifact AR Scene")]
        public static void CreateFromCommandLine()
        {
            System.IO.Directory.CreateDirectory("Assets/Models/Artifacts/ShengDing");
            ArtifactInfo info = AssetDatabase.LoadAssetAtPath<ArtifactInfo>(InfoPath);
            if (info == null)
            {
                info = ScriptableObject.CreateInstance<ArtifactInfo>();
                AssetDatabase.CreateAsset(info, InfoPath);
            }
            info.id = "shengding";
            info.displayName = "青铜升鼎";
            info.period = "春秋时期";
            info.category = "青铜礼器";
            info.description = "青铜升鼎是中国古代用于盛放食物的重要青铜礼器之一。\n器物两侧设有立耳，下部以多足承托，整体造型庄重厚重。\n其器形与装饰体现了先秦时期青铜铸造工艺和礼制文化的发展。";
            info.streamingAssetsModelPath = "Models/Artifacts/ShengDing/ShengDing.glb";
            info.defaultHeight = 0.22f;
            EditorUtility.SetDirty(info);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject sessionObject = new GameObject("AR Session");
            sessionObject.AddComponent<ARSession>();
            sessionObject.AddComponent<ARInputManager>();

            GameObject originObject = new GameObject("XR Origin");
            var origin = originObject.AddComponent<Unity.XR.CoreUtils.XROrigin>();
            var raycast = originObject.AddComponent<ARRaycastManager>();
            var planes = originObject.AddComponent<ARPlaneManager>();
            planes.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            var anchors = originObject.AddComponent<ARAnchorManager>();
            GameObject offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originObject.transform, false);
            origin.CameraFloorOffsetObject = offset;
            GameObject cameraObject = new GameObject("AR Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offset.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 20f;
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<ARCameraManager>();
            cameraObject.AddComponent<ARCameraBackground>();
            var poseDriver = cameraObject.AddComponent<TrackedPoseDriver>();
            var positionAction = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            positionAction.AddBinding("<HandheldARInputDevice>/devicePosition");
            var rotationAction = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            rotationAction.AddBinding("<HandheldARInputDevice>/deviceRotation");
            poseDriver.positionInput = new InputActionProperty(positionAction);
            poseDriver.rotationInput = new InputActionProperty(rotationAction);
            origin.Camera = camera;

            GameObject lightObject = new GameObject("Artifact Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            GameObject app = new GameObject("Artifact AR Application");
            ArtifactARController controller = app.AddComponent<ArtifactARController>();
            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("artifact").objectReferenceValue = info;
            serialized.FindProperty("chineseFont").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansSC-Regular.otf");
            serialized.FindProperty("raycastManager").objectReferenceValue = raycast;
            serialized.FindProperty("planeManager").objectReferenceValue = planes;
            serialized.FindProperty("anchorManager").objectReferenceValue = anchors;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/UrpARPrototype.unity", true),
                new EditorBuildSettingsScene(ScenePath, true)
            };
            AssetDatabase.SaveAssets();
            Debug.Log("ARTIFACT_AR_SCENE_READY");
        }

        public static void BuildAndroidFromCommandLine()
        {
            if (!System.IO.File.Exists(ScenePath))
                throw new System.InvalidOperationException("Artifact AR scene has not been generated.");
            System.IO.Directory.CreateDirectory("F:/Au/buildlogs");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/UrpARPrototype.unity", ScenePath },
                locationPathName = "F:/Au/buildlogs/ArtifactAR_validation.apk",
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.InvalidOperationException("Artifact AR Android build failed: " + report.summary.result);
            Debug.Log("ARTIFACT_AR_ANDROID_BUILD_OK");
        }

        public static async void ValidateBundledModelFromCommandLine()
        {
            string path = System.IO.Path.GetFullPath(
                "Assets/StreamingAssets/Models/Artifacts/ShengDing/ShengDing.glb");
            if (!System.IO.File.Exists(path)) throw new System.IO.FileNotFoundException(path);
            var gltf = new GltfImport(deferAgent: new UninterruptedDeferAgent());
            if (!await gltf.LoadGltfBinary(System.IO.File.ReadAllBytes(path)))
                throw new System.InvalidOperationException("GLB import failed.");
            GameObject root = new GameObject("GLB Validation Root");
            try
            {
                if (!await gltf.InstantiateMainSceneAsync(root.transform))
                    throw new System.InvalidOperationException("GLB instantiate failed.");
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                int textured = 0;
                Bounds bounds = default;
                bool any = false;
                foreach (Renderer renderer in renderers)
                {
                    if (!any) { bounds = renderer.bounds; any = true; }
                    else bounds.Encapsulate(renderer.bounds);
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null && material.mainTexture != null) textured++;
                }
                if (!any || bounds.size.y <= 0f || textured == 0)
                    throw new System.InvalidOperationException("GLB mesh, bounds, or texture is missing.");
                Debug.Log($"ARTIFACT_GLB_OK renderers={renderers.Length} texturedMaterials={textured} height={bounds.size.y:F4}m");
            }
            finally
            {
                Object.DestroyImmediate(root);
                EditorApplication.Exit(0);
            }
        }
    }
}
