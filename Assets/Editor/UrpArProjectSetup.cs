using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using Urp.ArDemo.Calibration;

namespace Urp.ArDemo.Editor
{
    public static class UrpArProjectSetup
    {
        private const string ScenePath = "Assets/Scenes/UrpARPrototype.unity";
        private const string CalibrationPath =
            "Assets/Calibration/CoconutBottleRepairCalibration.asset";
        private const string ChineseFontPath = "Assets/Fonts/NotoSansSC-Regular.otf";
        private const string GlobalOrbModelPath = "Assets/OrbModels/bottle_global.bytes";
        private const string DamagedProcessedPath =
            "Assets/Models/MeshroomBottleDamagedProcessed/damaged_bottle_processed.obj";
        private const string DamagedAlbedoPath =
            "Assets/Models/MeshroomBottleDamagedProcessed/damaged_bottle_processed_albedo.png";
        private const string RegisteredCapPath =
            "Assets/Models/RegisteredRepair/coconut_bottle_cap_registered.obj";
        private const string ViewerCapPath =
            "Assets/Models/MeshroomCapProcessed/meshroom_cap_processed.obj";
        private const string ObjectThumbnailPath =
            "Assets/Textures/Targets/DamagedBottleOrbViews/orb_view_01.jpg";

        [MenuItem("URP AR/Setup Prototype Scene")]
        public static void SetupPrototypeScene()
        {
            EnsureFolders();
            ConfigureAndroidProject();
            ConfigureXRManagement();
            ConfigureImportedAssets();
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
                locationPathName = "Builds/urp-ar-rebuilt.apk",
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
                "Assets/Calibration",
                "Assets/Docs",
                "Assets/Materials",
                "Assets/Models/RegisteredRepair",
                "Assets/Scripts/Calibration"
            };
            foreach (string folder in folders)
            {
                Directory.CreateDirectory(folder);
            }
        }

        private static void ConfigureAndroidProject()
        {
            PlayerSettings.productName = "URP AR 数字修复";
            PlayerSettings.companyName = "qfgeeee";
            PlayerSettings.bundleVersion = "0.3";
            PlayerSettings.SetApplicationIdentifier(
                BuildTargetGroup.Android,
                "com.qfgeeee.urpardemo");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.bundleVersionCode = 3;
            PlayerSettings.SetScriptingBackend(
                BuildTargetGroup.Android,
                ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.allowUnsafeCode = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android,
                new[] { GraphicsDeviceType.OpenGLES3 });
            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android,
                BuildTarget.Android);
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

            XRManagerSettings manager = settings.ManagerSettingsForBuildTarget(
                BuildTargetGroup.Android);
            const string loaderType = "UnityEngine.XR.ARCore.ARCoreLoader";
            if (!XRPackageMetadataStore.IsLoaderAssigned(loaderType, BuildTargetGroup.Android))
            {
                XRPackageMetadataStore.AssignLoader(
                    manager,
                    loaderType,
                    BuildTargetGroup.Android);
            }

            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(manager);
        }

        private static XRGeneralSettingsPerBuildTarget GetOrCreateXRSettings()
        {
            var method = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
                "GetOrCreate",
                System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException("XRGeneralSettingsPerBuildTarget.GetOrCreate");
            }

            return (XRGeneralSettingsPerBuildTarget)method.Invoke(null, null);
        }

        private static void ConfigureImportedAssets()
        {
            foreach (string path in new[]
                     {
                         DamagedProcessedPath,
                         RegisteredCapPath,
                         ViewerCapPath
                     })
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException($"Required model is missing: {path}");
                }

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null)
                {
                    importer.importAnimation = false;
                    importer.addCollider = false;
                    importer.importCameras = false;
                    importer.importLights = false;
                    importer.isReadable = true;
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.SaveAndReimport();
                }
            }

            if (File.Exists(DamagedAlbedoPath))
            {
                AssetDatabase.ImportAsset(DamagedAlbedoPath, ImportAssetOptions.ForceUpdate);
                TextureImporter importer = AssetImporter.GetAtPath(DamagedAlbedoPath) as TextureImporter;
                if (importer != null)
                {
                    importer.sRGBTexture = true;
                    importer.mipmapEnabled = true;
                    importer.maxTextureSize = 2048;
                    importer.textureCompression = TextureImporterCompression.Compressed;
                    importer.SaveAndReimport();
                }
            }
        }

        private static void CreatePrototypeScene()
        {
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            RepairCalibrationProfile calibration = GetOrCreateCalibration();

            GameObject arSession = new GameObject("AR Session");
            arSession.AddComponent<ARSession>();
            arSession.AddComponent<ARInputManager>();

            GameObject xrOrigin = new GameObject("XR Origin");
            var origin = xrOrigin.AddComponent<Unity.XR.CoreUtils.XROrigin>();
            GameObject cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOrigin.transform, false);
            origin.CameraFloorOffsetObject = cameraOffset;

            GameObject cameraObject = new GameObject("AR Camera");
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            cameraObject.AddComponent<AudioListener>();
            ARCameraManager cameraManager = cameraObject.AddComponent<ARCameraManager>();
            cameraObject.AddComponent<ARCameraBackground>();
            origin.Camera = camera;

            Transform objectPoseRoot = CreateRepairObjectHierarchy(calibration, out Transform cap);

            GameObject lightObject = new GameObject("AR Repair Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.72f;
            light.color = new Color(1f, 0.98f, 0.94f);
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            GameObject application = new GameObject("URP Application");
            RepairOverlayController overlay = application.AddComponent<RepairOverlayController>();
            OrbImageTrackingController tracker =
                xrOrigin.AddComponent<OrbImageTrackingController>();
            AssignReference(tracker, "cameraManager", cameraManager);
            AssignReference(tracker, "arCamera", camera);
            AssignReference(tracker, "trackedObjectPoseRoot", objectPoseRoot);
            AssignReference(tracker, "registeredBottleCap", cap);
            AssignReference(tracker, "calibration", calibration);
            AssignObjectArray(tracker, "orbModelFiles", LoadOrbModelFiles());
            AssignReference(overlay, "orbTracker", tracker);

            ModelViewerController viewer = CreateModelViewer(camera);
            UrpAppController app = application.AddComponent<UrpAppController>();
            AssignReference(app, "chineseFont",
                AssetDatabase.LoadAssetAtPath<Font>(ChineseFontPath));
            AssignReference(app, "objectThumbnail",
                AssetDatabase.LoadAssetAtPath<Texture2D>(ObjectThumbnailPath));
            AssignReference(app, "orbTracker", tracker);
            AssignReference(app, "repairController", overlay);
            AssignReference(app, "modelViewer", viewer);

            CreateEventSystem();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes =
                new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static Transform CreateRepairObjectHierarchy(
            RepairCalibrationProfile calibration,
            out Transform capTransform)
        {
            GameObject objectRoot = new GameObject("Tracked Object Pose Root");
            GameObject alignmentRoot = new GameObject("Repair Alignment Root");
            alignmentRoot.transform.SetParent(objectRoot.transform, false);
            Quaternion modelFrame = Quaternion.LookRotation(
                calibration.ForwardInModel,
                calibration.UpInModel);
            alignmentRoot.transform.localRotation = Quaternion.Inverse(modelFrame);
            alignmentRoot.transform.localPosition =
                alignmentRoot.transform.localRotation
                * -calibration.objectOriginInModel;

            GameObject capAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                RegisteredCapPath);
            GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(capAsset);
            cap.name = "Registered Bottle Cap";
            cap.transform.SetParent(alignmentRoot.transform, false);
            cap.transform.localPosition = calibration.capLocalPosition;
            cap.transform.localRotation = Quaternion.Euler(
                calibration.capLocalEulerAngles);
            cap.transform.localScale = calibration.capLocalScale;
            ApplyMaterial(cap, GetOrCreateCapMaterial());
            capTransform = cap.transform;

            GameObject occluder = new GameObject("Registered Neck Occluder");
            occluder.transform.SetParent(alignmentRoot.transform, false);
            occluder.SetActive(false);

            GameObject debug = new GameObject("Debug Pose Visualization");
            debug.transform.SetParent(objectRoot.transform, false);
            debug.SetActive(false);
            objectRoot.SetActive(false);
            return objectRoot.transform;
        }

        private static ModelViewerController CreateModelViewer(Camera arCamera)
        {
            const int viewerLayer = 8;
            GameObject root = new GameObject("Three Dimensional Resource Viewer");
            ModelViewerController controller = root.AddComponent<ModelViewerController>();

            GameObject cameraObject = new GameObject("Resource Viewer Camera");
            cameraObject.transform.SetParent(root.transform, false);
            Camera viewerCamera = cameraObject.AddComponent<Camera>();
            viewerCamera.clearFlags = CameraClearFlags.SolidColor;
            viewerCamera.backgroundColor = new Color32(246, 249, 253, 255);
            viewerCamera.fieldOfView = 30f;
            viewerCamera.allowHDR = false;
            viewerCamera.depth = 5f;
            viewerCamera.cullingMask = 1 << viewerLayer;
            cameraObject.SetActive(false);
            arCamera.cullingMask &= ~(1 << viewerLayer);

            Material bottleMaterial = GetOrCreateBottleViewerMaterial();
            Transform damaged = InstantiateViewerBottle(
                "残缺饮料瓶模型",
                root.transform,
                viewerLayer,
                bottleMaterial,
                false);
            Transform complete = InstantiateViewerBottle(
                "完整饮料瓶模型",
                root.transform,
                viewerLayer,
                bottleMaterial,
                true);

            GameObject keyLightObject = new GameObject("Resource Viewer Key Light");
            keyLightObject.transform.SetParent(root.transform, false);
            Light key = keyLightObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 0.62f;
            key.color = new Color(1f, 0.98f, 0.94f);
            key.cullingMask = 1 << viewerLayer;
            keyLightObject.layer = viewerLayer;
            keyLightObject.transform.rotation = Quaternion.Euler(42f, -38f, 0f);

            AssignReference(controller, "viewerCamera", viewerCamera);
            AssignReference(controller, "damagedModel", damaged);
            AssignReference(controller, "completeModel", complete);
            return controller;
        }

        private static Transform InstantiateViewerBottle(
            string name,
            Transform parent,
            int layer,
            Material bottleMaterial,
            bool addCap)
        {
            GameObject holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.position = new Vector3(0f, 0.25f, 4f);

            GameObject bottleAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                DamagedProcessedPath);
            GameObject bottle = (GameObject)PrefabUtility.InstantiatePrefab(bottleAsset);
            bottle.transform.SetParent(holder.transform, false);
            ApplyMaterial(bottle, bottleMaterial);
            SetLayerRecursively(bottle, layer);

            Bounds originalBounds = CalculateBounds(bottle);
            float scale = 2.35f / Mathf.Max(0.001f, originalBounds.size.y);
            bottle.transform.localScale = Vector3.one * scale;
            Bounds scaledBounds = CalculateBounds(bottle);
            bottle.transform.position += holder.transform.position - scaledBounds.center;

            if (addCap)
            {
                AddViewerCap(holder.transform, bottle, layer);
            }

            return holder.transform;
        }

        private static void AddViewerCap(Transform holder, GameObject bottle, int layer)
        {
            GameObject capAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ViewerCapPath);
            GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(capAsset);
            cap.name = "Viewer Completion Bottle Cap";
            cap.transform.SetParent(holder, false);
            SetLayerRecursively(cap, layer);
            ApplyMaterial(cap, GetOrCreateCapMaterial());

            Bounds capBounds = CalculateBounds(cap);
            if (!TryCalculateTopOpening(bottle, out Vector3 openingCenter, out float targetDiameter))
            {
                throw new InvalidOperationException(
                    "Could not estimate the bottle opening from the processed mesh.");
            }

            float capScale = targetDiameter / Mathf.Max(0.001f, capBounds.size.x);
            cap.transform.localScale = Vector3.one * capScale;
            capBounds = CalculateBounds(cap);
            cap.transform.position += openingCenter
                - new Vector3(capBounds.center.x, capBounds.min.y, capBounds.center.z);
        }

        private static bool TryCalculateTopOpening(
            GameObject bottle,
            out Vector3 center,
            out float diameter)
        {
            center = Vector3.zero;
            diameter = 0f;
            Bounds bottleBounds = CalculateBounds(bottle);
            float topBandMinimum = bottleBounds.min.y + bottleBounds.size.y * 0.90f;
            List<float> xValues = new List<float>();
            List<float> zValues = new List<float>();
            List<float> yValues = new List<float>();
            foreach (MeshFilter filter in bottle.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                foreach (Vector3 localVertex in mesh.vertices)
                {
                    Vector3 worldVertex = filter.transform.TransformPoint(localVertex);
                    if (worldVertex.y < topBandMinimum)
                    {
                        continue;
                    }

                    xValues.Add(worldVertex.x);
                    yValues.Add(worldVertex.y);
                    zValues.Add(worldVertex.z);
                }
            }

            if (xValues.Count < 20)
            {
                return false;
            }

            xValues.Sort();
            yValues.Sort();
            zValues.Sort();
            float xLow = Percentile(xValues, 0.05f);
            float xHigh = Percentile(xValues, 0.95f);
            float zLow = Percentile(zValues, 0.05f);
            float zHigh = Percentile(zValues, 0.95f);
            diameter = Mathf.Max(xHigh - xLow, zHigh - zLow);
            center = new Vector3(
                (xLow + xHigh) * 0.5f,
                yValues[yValues.Count - 1],
                (zLow + zHigh) * 0.5f);
            return diameter > 0.001f;
        }

        private static float Percentile(List<float> sortedValues, float percentile)
        {
            float index = Mathf.Clamp01(percentile) * (sortedValues.Count - 1);
            int lower = Mathf.FloorToInt(index);
            int upper = Mathf.CeilToInt(index);
            return Mathf.Lerp(sortedValues[lower], sortedValues[upper], index - lower);
        }

        private static RepairCalibrationProfile GetOrCreateCalibration()
        {
            RepairCalibrationProfile profile =
                AssetDatabase.LoadAssetAtPath<RepairCalibrationProfile>(CalibrationPath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<RepairCalibrationProfile>();
                AssetDatabase.CreateAsset(profile, CalibrationPath);
            }

            profile.objectOriginInModel = Vector3.zero;
            profile.mouthCenterInModel = new Vector3(
                0.419225f, -4.514827f, 0.314265f);
            profile.mouthRightInModel = new Vector3(
                0.519225f, -4.514827f, 0.314265f);
            profile.mouthFrontInModel = new Vector3(
                0.419225f, -4.514827f, 0.214265f);
            profile.neckAxisPointInModel = new Vector3(
                0.419225f, -4.314827f, 0.314265f);
            profile.metersPerModelUnit = 0.18f;
            profile.physicalScaleVerified = false;
            profile.expectedPhysicalCapDiameter = 0.0408f;
            profile.expectedPhysicalCapHeight = 0.02f;
            profile.capLocalPosition = Vector3.zero;
            profile.capLocalEulerAngles = Vector3.zero;
            profile.capLocalScale = Vector3.one;
            profile.occluderVerified = false;
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static TextAsset[] LoadOrbModelFiles()
        {
            TextAsset global = AssetDatabase.LoadAssetAtPath<TextAsset>(
                GlobalOrbModelPath);
            if (global == null)
            {
                throw new FileNotFoundException(
                    $"Merged ORB model is missing: {GlobalOrbModelPath}");
            }

            return new[] { global };
        }

        private static Material GetOrCreateCapMaterial()
        {
            const string path = "Assets/Materials/RegisteredBottleCap.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(FindLitShader());
                AssetDatabase.CreateAsset(material, path);
            }

            Color whitePlastic = new Color(0.88f, 0.87f, 0.83f, 1f);
            SetMaterialColor(material, whitePlastic);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.34f);
            material.DisableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateBottleViewerMaterial()
        {
            const string path = "Assets/Materials/BottleViewerLit.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(FindLitShader());
                AssetDatabase.CreateAsset(material, path);
            }

            SetMaterialColor(material, Color.white);
            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(
                DamagedAlbedoPath);
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", albedo);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", albedo);
            }

            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.20f);
            material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Shader FindLitShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static void ApplyMaterial(GameObject root, Material material)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
            }
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static void CreateEventSystem()
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        private static void AssignReference(
            UnityEngine.Object target,
            string propertyName,
            UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new MissingFieldException(target.GetType().Name, propertyName);
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignObjectArray(
            UnityEngine.Object target,
            string propertyName,
            UnityEngine.Object[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
