using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Management;

namespace Urp.ArDemo.Editor
{
    public static class UrpArProjectSetup
    {
        private const string ScenePath = "Assets/Scenes/UrpARPrototype.unity";

        [MenuItem("URP AR/Setup Prototype Scene")]
        public static void SetupPrototypeScene()
        {
            EnsureFolders();
            ConfigureAndroidProject();
            ConfigureXRManagement();
            CreatePrototypeScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void SetupFromCommandLine()
        {
            SetupPrototypeScene();
        }

        public static void BuildAndroidFromCommandLine()
        {
            SetupPrototypeScene();
            Directory.CreateDirectory("Builds");

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/urp-ar-demo.apk",
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException($"Android build failed: {report.summary.result}");
            }
        }

        private static void EnsureFolders()
        {
            string[] folders =
            {
                "Assets/Docs",
                "Assets/Editor",
                "Assets/Materials",
                "Assets/Models",
                "Assets/Prefabs",
                "Assets/Scripts",
                "Assets/Textures",
            };

            foreach (string folder in folders)
            {
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    Directory.CreateDirectory(folder);
                }
            }
        }

        private static void ConfigureAndroidProject()
        {
            PlayerSettings.productName = "URP AR Demo";
            PlayerSettings.companyName = "qfgeeee";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.qfgeeee.urpardemo");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        private static void ConfigureXRManagement()
        {
            XRGeneralSettingsPerBuildTarget settings = GetOrCreateXRSettings();
            if (!settings.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                settings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            }

            if (!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            }

            XRManagerSettings managerSettings = settings.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            const string arCoreLoaderType = "UnityEngine.XR.ARCore.ARCoreLoader";
            if (!XRPackageMetadataStore.IsLoaderAssigned(arCoreLoaderType, BuildTargetGroup.Android))
            {
                XRPackageMetadataStore.AssignLoader(managerSettings, arCoreLoaderType, BuildTargetGroup.Android);
            }

            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(managerSettings);
        }

        private static XRGeneralSettingsPerBuildTarget GetOrCreateXRSettings()
        {
            var method = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
                "GetOrCreate",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

            if (method == null)
            {
                throw new MissingMethodException("XRGeneralSettingsPerBuildTarget.GetOrCreate");
            }

            return (XRGeneralSettingsPerBuildTarget)method.Invoke(null, null);
        }

        private static void CreatePrototypeScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject arSession = new GameObject("AR Session");
            arSession.AddComponent<UnityEngine.XR.ARFoundation.ARSession>();
            arSession.AddComponent<UnityEngine.XR.ARFoundation.ARInputManager>();

            GameObject xrOrigin = new GameObject("XR Origin");
            var origin = xrOrigin.AddComponent<Unity.XR.CoreUtils.XROrigin>();
            xrOrigin.AddComponent<OrbTrackingPlaceholder>();

            GameObject cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOrigin.transform, false);
            origin.CameraFloorOffsetObject = cameraOffset;

            GameObject cameraObject = new GameObject("AR Camera");
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<UnityEngine.XR.ARFoundation.ARCameraManager>();
            cameraObject.AddComponent<UnityEngine.XR.ARFoundation.ARCameraBackground>();
            origin.Camera = camera;

            GameObject contentRoot = new GameObject("Tracked Artifact Root");
            contentRoot.transform.position = new Vector3(0f, 0f, 1.25f);

            GameObject repair = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            repair.name = "Placeholder Virtual Repair Part";
            repair.transform.SetParent(contentRoot.transform, false);
            repair.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            repair.transform.localScale = new Vector3(0.35f, 0.08f, 0.35f);
            repair.GetComponent<Renderer>().sharedMaterial = CreateMaterial("VirtualRepairPreview", new Color(0.1f, 0.72f, 0.85f, 0.82f));

            GameObject baseHint = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseHint.name = "Physical Artifact Alignment Hint";
            baseHint.transform.SetParent(contentRoot.transform, false);
            baseHint.transform.localScale = new Vector3(0.34f, 0.42f, 0.34f);
            baseHint.GetComponent<Renderer>().sharedMaterial = CreateMaterial("PhysicalArtifactHint", new Color(0.75f, 0.55f, 0.35f, 0.35f));

            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject canvasObject = CreateCanvas(out Text statusText, out GameObject infoPanel);
            var controller = canvasObject.AddComponent<RepairOverlayController>();
            AssignSerializedReference(controller, "repairRoot", repair.transform);
            AssignSerializedReference(controller, "infoPanel", infoPanel);
            AssignSerializedReference(controller, "statusText", statusText);

            var tracker = xrOrigin.GetComponent<OrbTrackingPlaceholder>();
            AssignSerializedReference(tracker, "trackedContentRoot", contentRoot.transform);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static Material CreateMaterial(string name, Color color)
        {
            string path = $"Assets/Materials/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            return material;
        }

        private static GameObject CreateCanvas(out Text statusText, out GameObject infoPanel)
        {
            GameObject canvasObject = new GameObject("Demo UI");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();

            statusText = CreateText(canvasObject.transform, "Status Text", "URP AR Demo", new Vector2(0.5f, 1f), new Vector2(0f, -40f), 20, TextAnchor.MiddleCenter);
            infoPanel = new GameObject("Info Panel");
            infoPanel.transform.SetParent(canvasObject.transform, false);
            RectTransform panelRect = infoPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.05f, 0.05f);
            panelRect.anchorMax = new Vector2(0.95f, 0.23f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            Image panelImage = infoPanel.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.56f);
            CreateText(infoPanel.transform, "Artifact Info", "Cultural heritage AR prototype: physical object plus virtual restoration overlay.", new Vector2(0.5f, 0.5f), Vector2.zero, 18, TextAnchor.MiddleCenter);

            return canvasObject;
        }

        private static Text CreateText(Transform parent, string name, string value, Vector2 anchor, Vector2 position, int fontSize, TextAnchor alignment)
        {
            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.sizeDelta = new Vector2(900f, 90f);
            rect.anchoredPosition = position;
            Text text = textObject.AddComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            return text;
        }

        private static void AssignSerializedReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
