using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using Urp.ArDemo.Calibration;

namespace Urp.ArDemo.Editor
{
    public static class UrpArProjectSetup
    {
        private const string ScenePath = "Assets/Scenes/UrpARPrototype.unity";
        private const string FontPath = "Assets/Fonts/NotoSansSC-Regular.otf";
        private const string BottleDamagedPath =
            "Assets/Models/MeshroomBottleDamagedProcessed/damaged_bottle_processed.obj";
        private const string BottleCompletePath =
            "Assets/Models/MeshroomBottleFullProcessed/complete_bottle_processed.obj";
        private const string BottleTexturePath =
            "Assets/Models/MeshroomBottleDamagedProcessed/damaged_bottle_processed_albedo.png";
        private const string BottleRepairPath =
            "Assets/Models/RegisteredRepair/coconut_bottle_cap_registered.obj";
        private const string BottleOccluderPath =
            "Assets/Objects/CoconutBottle/Prefabs/BottleNeckOccluder.prefab";
        private const string OccluderShaderPath =
            "Assets/Shaders/DepthOnlyOccluder.shader";
        private const string BottleOrbPath = "Assets/OrbModels/bottle_global.bytes";
        private const string BottleCalibrationPath =
            "Assets/Calibration/CoconutBottleRepairCalibration.asset";
        private const string TissueModelPath =
            "Assets/Objects/Tissue/Viewer/Processed/tissue_processed.obj";
        private const string TissueTexturePath =
            "Assets/Objects/Tissue/Viewer/Processed/tissue_albedo.png";
        private const string TissueThumbnailPath =
            "Assets/Objects/Tissue/Thumbnails/tissue_front.png";
        private const string BottleProfilePath =
            "Assets/Objects/CoconutBottle/Profiles/CoconutBottleRepairProfile.asset";
        private const string TissueProfilePath =
            "Assets/Objects/Tissue/Profiles/TissueRepairProfile.asset";
        private const string CatalogPath =
            "Assets/Objects/RestorationObjectCatalog.asset";

        [MenuItem("URP AR/Setup Prototype Scene")]
        public static void SetupPrototypeScene()
        {
            EnsureFolders();
            ConfigureAndroidProject();
            ConfigureXRManagement();
            ConfigureRenderPipeline();
            AssetDatabase.Refresh();
            ConfigureImportedAssets();
            RestorationObjectCatalog catalog = CreateProfiles();
            CreatePrototypeScene(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void SetupFromCommandLine() => SetupPrototypeScene();

        public static void BuildAndroidFromCommandLine()
        {
            SetupPrototypeScene();
            CleanStaleSimulationTempAssets();
            Directory.CreateDirectory("Builds");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/urp-ar-rebuilt.apk",
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException($"Android build failed: {report.summary.result}");
            }
        }

        private static void CleanStaleSimulationTempAssets()
        {
            foreach (string path in new[]
                     {
                         "Assets/XR/Temp/XRSimulationPreferences.asset",
                         "Assets/XR/Temp/XRSimulationRuntimeSettings.asset"
                     })
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                    AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.Refresh();
        }

        private static void EnsureFolders()
        {
            string[] folders =
            {
                "Assets/Calibration", "Assets/Docs", "Assets/Materials",
                "Assets/Objects/CoconutBottle/Profiles",
                "Assets/Objects/CoconutBottle/Prefabs",
                "Assets/Objects/Tissue/Profiles",
                "Assets/Objects/Tissue/Calibration",
                "Assets/Shaders"
            };
            foreach (string folder in folders) Directory.CreateDirectory(folder);
        }

        private static void ConfigureAndroidProject()
        {
            PlayerSettings.productName = "文化遗址数字修复及 AR 呈现系统";
            PlayerSettings.companyName = "qfgeeee";
            PlayerSettings.bundleVersion = "0.4";
            PlayerSettings.SetApplicationIdentifier(
                BuildTargetGroup.Android, "com.qfgeeee.urpardemo");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.bundleVersionCode = 4;
            PlayerSettings.SetScriptingBackend(
                BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.allowUnsafeCode = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android, BuildTarget.Android);
        }

        private static void ConfigureXRManagement()
        {
            XRGeneralSettingsPerBuildTarget settings = GetOrCreateXRSettings();
            if (!settings.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                settings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            XRManagerSettings manager =
                settings.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            const string loaderType = "UnityEngine.XR.ARCore.ARCoreLoader";
            if (!XRPackageMetadataStore.IsLoaderAssigned(loaderType, BuildTargetGroup.Android))
                XRPackageMetadataStore.AssignLoader(manager, loaderType, BuildTargetGroup.Android);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(manager);
        }

        private static void ConfigureRenderPipeline()
        {
            const string pipelinePath = "Assets/Settings/UrpMobilePipeline.asset";
            const string rendererPath = "Assets/Settings/UrpMobileRenderer.asset";
            Directory.CreateDirectory("Assets/Settings");
            UniversalRendererData renderer =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            EnsureArBackgroundRendererFeature(renderer);
            UniversalRenderPipelineAsset pipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }
            pipeline.renderScale = 1f;
            pipeline.supportsHDR = false;
            pipeline.msaaSampleCount = 2;
            SerializedObject pipelineSerialized = new SerializedObject(pipeline);
            pipelineSerialized.FindProperty("m_SupportsHDR").boolValue = false;
            pipelineSerialized.FindProperty("m_MSAA").intValue = 2;
            pipelineSerialized.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.renderPipelineAsset = pipeline;
            QualitySettings.renderPipeline = pipeline;
            EditorUtility.SetDirty(pipeline);
        }

        private static void EnsureArBackgroundRendererFeature(UniversalRendererData renderer)
        {
            renderer.rendererFeatures.RemoveAll(feature => feature == null);
            ARBackgroundRendererFeature feature = renderer.rendererFeatures
                .OfType<ARBackgroundRendererFeature>()
                .FirstOrDefault();
            if (feature != null)
            {
                return;
            }

            feature = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();
            feature.name = "AR Background Renderer Feature";
            feature.Create();
            AssetDatabase.AddObjectToAsset(feature, renderer);
            renderer.rendererFeatures.Add(feature);

            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
            SerializedObject serializedRenderer = new SerializedObject(renderer);
            SerializedProperty featureMap = serializedRenderer.FindProperty("m_RendererFeatureMap");
            if (featureMap != null)
            {
                int index = featureMap.arraySize;
                featureMap.InsertArrayElementAtIndex(index);
                featureMap.GetArrayElementAtIndex(index).longValue = localId;
                serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(renderer);
        }

        private static XRGeneralSettingsPerBuildTarget GetOrCreateXRSettings()
        {
            var method = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
                "GetOrCreate",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic);
            if (method == null)
                throw new MissingMethodException("XRGeneralSettingsPerBuildTarget.GetOrCreate");
            return (XRGeneralSettingsPerBuildTarget)method.Invoke(null, null);
        }

        private static void ConfigureImportedAssets()
        {
            foreach (string path in new[]
                     {
                         BottleDamagedPath, BottleCompletePath, BottleRepairPath, TissueModelPath
                     })
            {
                RequireFile(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                if (AssetImporter.GetAtPath(path) is ModelImporter importer)
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

            ConfigureTexture(BottleTexturePath, 2048);
            ConfigureTexture(TissueTexturePath, 2048);
            ConfigureTexture(TissueThumbnailPath, 1024);
        }

        private static void ConfigureTexture(string path, int maximumSize)
        {
            RequireFile(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.maxTextureSize = maximumSize;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
        }

        private static RestorationObjectCatalog CreateProfiles()
        {
            Material bottleMaterial = CreateLitMaterial(
                "Assets/Materials/BottleViewerLit.mat", BottleTexturePath, 0.20f);
            Material tissueMaterial = CreateLitMaterial(
                "Assets/Objects/Tissue/Materials/TissueViewerLit.mat", TissueTexturePath, 0.16f);
            Material repairMaterial = CreateLitMaterial(
                "Assets/Materials/RegisteredBottleCap.mat", null, 0.34f);
            GameObject bottleOccluder = CreateBottleNeckOccluder();

            RestorationObjectProfile bottle = LoadOrCreate<RestorationObjectProfile>(
                BottleProfilePath);
            bottle.objectId = "coconut_bottle";
            bottle.displayName = "瓶盖缺失饮料瓶";
            bottle.shortDescription = "瓶身保留瓶口与螺纹结构，缺失部分为瓶盖。";
            bottle.viewerDescription =
                "残缺模型来自清理后的 Meshroom 瓶身；完整模型为离线组合预览。"
                + "瓶口外径 34 mm、瓶盖外径 39 mm、高 10 mm 已用于当前视觉标定。";
            bottle.trackingDescription =
                "使用该对象自己的 ORB 2D—3D 数据和 PnP 位姿驱动已注册瓶盖。"
                + "跟踪坐标原点位于瓶口中心，并使用瓶颈深度遮挡体改善虚实穿插。";
            bottle.missingPartName = "瓶盖";
            bottle.thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Textures/Targets/DamagedBottleOrbViews/orb_view_01.jpg");
            bottle.damagedViewerPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(BottleDamagedPath);
            bottle.completeViewerPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(BottleCompletePath);
            bottle.registeredRepairPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(BottleRepairPath);
            bottle.registeredOccluderPrefab = bottleOccluder;
            bottle.orbModelDatabase = AssetDatabase.LoadAssetAtPath<TextAsset>(BottleOrbPath);
            RepairCalibrationProfile bottleCalibration =
                AssetDatabase.LoadAssetAtPath<RepairCalibrationProfile>(BottleCalibrationPath);
            bottleCalibration.objectOriginInModel = Vector3.zero;
            bottleCalibration.mouthCenterInModel = Vector3.zero;
            bottleCalibration.mouthRightInModel = new Vector3(0.1f, 0f, 0f);
            bottleCalibration.mouthFrontInModel = new Vector3(0f, 0f, 0.1f);
            bottleCalibration.neckAxisPointInModel = new Vector3(0f, -0.2f, 0f);
            bottleCalibration.metersPerModelUnit = 0.17f;
            bottleCalibration.physicalScaleVerified = true;
            bottleCalibration.expectedPhysicalNeckDiameter = 0.034f;
            bottleCalibration.expectedPhysicalCapDiameter = 0.039f;
            bottleCalibration.expectedPhysicalCapHeight = 0.010f;
            bottleCalibration.capLocalPosition = Vector3.zero;
            bottleCalibration.capLocalEulerAngles = Vector3.zero;
            bottleCalibration.capLocalScale = Vector3.one;
            bottleCalibration.occluderVerified = false;
            bottleCalibration.occluderLocalPosition = Vector3.zero;
            bottleCalibration.occluderLocalEulerAngles = Vector3.zero;
            bottleCalibration.occluderLocalScale = Vector3.one;
            EditorUtility.SetDirty(bottleCalibration);
            bottle.calibration = bottleCalibration;
            bottle.viewerMaterial = bottleMaterial;
            bottle.repairMaterial = repairMaterial;
            bottle.initialGuideMaterial = repairMaterial;
            bottle.defaultViewerEuler = Vector3.zero;
            bottle.viewerMargin = 0.18f;
            bottle.physicalScaleVerified =
                bottle.calibration != null && bottle.calibration.physicalScaleVerified;
            bottle.physicalMeasurements = new[]
            {
                new PhysicalMeasurement
                {
                    label = "瓶口螺纹最外侧直径",
                    modelDistanceUnits = 0.2f,
                    realDistanceMeters = 0.034f,
                    verified = true
                },
                new PhysicalMeasurement
                {
                    label = "瓶盖外径",
                    modelDistanceUnits = 0.039f / 0.17f,
                    realDistanceMeters = 0.039f,
                    verified = true
                },
                new PhysicalMeasurement
                {
                    label = "瓶盖高度",
                    modelDistanceUnits = 0.010f / 0.17f,
                    realDistanceMeters = 0.010f,
                    verified = true
                }
            };
            EditorUtility.SetDirty(bottle);

            RepairCalibrationProfile tissueCalibration =
                LoadOrCreate<RepairCalibrationProfile>(
                    "Assets/Objects/Tissue/Calibration/TissueCalibration.asset");
            tissueCalibration.objectOriginInModel = new Vector3(0.027f, -0.151f, 1.789f);
            tissueCalibration.mouthCenterInModel = tissueCalibration.objectOriginInModel;
            tissueCalibration.mouthRightInModel =
                tissueCalibration.objectOriginInModel + Vector3.right;
            tissueCalibration.mouthFrontInModel =
                tissueCalibration.objectOriginInModel + Vector3.forward;
            tissueCalibration.neckAxisPointInModel =
                tissueCalibration.objectOriginInModel - Vector3.up;
            tissueCalibration.metersPerModelUnit = 1f;
            tissueCalibration.physicalScaleVerified = false;
            tissueCalibration.expectedPhysicalNeckDiameter = 0f;
            tissueCalibration.expectedPhysicalCapDiameter = 0f;
            tissueCalibration.expectedPhysicalCapHeight = 0f;
            tissueCalibration.occluderVerified = false;
            EditorUtility.SetDirty(tissueCalibration);

            RestorationObjectProfile tissue = LoadOrCreate<RestorationObjectProfile>(
                TissueProfilePath);
            tissue.objectId = "tissue";
            tissue.displayName = "Tissue 重建对象";
            tissue.shortDescription =
                "源照片中的维达纸巾盒 Meshroom 重建；当前仅确认一套带纹理网格。";
            tissue.viewerDescription =
                "已从绿色行李箱和室内背景中提取纸巾盒主体用于三维查看。"
                + "源数据未提供独立完整/残缺模型，因此完整模型入口会明确提示资料缺失。";
            tissue.trackingDescription =
                "当前对象的 ORB 数据、修复部件与连接区域仍需完成标定，"
                + "不会加载饮料瓶的模型、文字或标定参数。";
            tissue.missingPartName = "尚未确认";
            tissue.thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>(TissueThumbnailPath);
            tissue.damagedViewerPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(TissueModelPath);
            tissue.completeViewerPrefab = null;
            tissue.registeredRepairPrefab = null;
            tissue.registeredOccluderPrefab = null;
            tissue.orbModelDatabase = null;
            tissue.calibration = tissueCalibration;
            tissue.viewerMaterial = tissueMaterial;
            tissue.repairMaterial = null;
            tissue.initialGuideMaterial = null;
            tissue.defaultViewerEuler = new Vector3(-90f, 0f, 0f);
            tissue.viewerMargin = 0.20f;
            tissue.physicalScaleVerified = false;
            EditorUtility.SetDirty(tissue);

            RestorationObjectCatalog catalog =
                LoadOrCreate<RestorationObjectCatalog>(CatalogPath);
            catalog.objects = new[] { bottle, tissue };
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static void CreatePrototypeScene(RestorationObjectCatalog catalog)
        {
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject sessionObject = new GameObject("AR Session");
            ARSession arSession = sessionObject.AddComponent<ARSession>();
            sessionObject.AddComponent<ARInputManager>();
            arSession.enabled = false;

            GameObject originObject = new GameObject("XR Origin");
            var origin = originObject.AddComponent<Unity.XR.CoreUtils.XROrigin>();
            GameObject offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originObject.transform, false);
            origin.CameraFloorOffsetObject = offset;

            GameObject cameraObject = new GameObject("AR Camera");
            cameraObject.transform.SetParent(offset.transform, false);
            Camera arCamera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            arCamera.clearFlags = CameraClearFlags.SolidColor;
            arCamera.backgroundColor = Color.black;
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraObject.AddComponent<AudioListener>();
            ARCameraManager cameraManager = cameraObject.AddComponent<ARCameraManager>();
            ARCameraBackground cameraBackground = cameraObject.AddComponent<ARCameraBackground>();
            cameraBackground.enabled = false;
            cameraManager.enabled = false;
            arCamera.enabled = false;
            origin.Camera = arCamera;

            GameObject trackedRoot = new GameObject("TrackedObjectPoseRoot");
            GameObject alignment = new GameObject("ModelCoordinateAlignment");
            alignment.transform.SetParent(trackedRoot.transform, false);
            GameObject debug = new GameObject("DebugAxes");
            debug.transform.SetParent(alignment.transform, false);
            debug.SetActive(false);
            trackedRoot.SetActive(false);

            GameObject application = new GameObject("URP Application");
            RepairOverlayController overlay = application.AddComponent<RepairOverlayController>();
            OrbImageTrackingController tracker =
                originObject.AddComponent<OrbImageTrackingController>();
            AssignReference(tracker, "cameraManager", cameraManager);
            AssignReference(tracker, "arCamera", arCamera);
            AssignReference(tracker, "trackedObjectPoseRoot", trackedRoot.transform);
            AssignReference(tracker, "modelCoordinateAlignment", alignment.transform);
            AssignReference(overlay, "orbTracker", tracker);

            ModelViewerController viewer = CreateModelViewer(arCamera);
            UrpAppController app = application.AddComponent<UrpAppController>();
            AssignReference(app, "chineseFont", AssetDatabase.LoadAssetAtPath<Font>(FontPath));
            AssignReference(app, "catalog", catalog);
            AssignReference(app, "orbTracker", tracker);
            AssignReference(app, "repairController", overlay);
            AssignReference(app, "modelViewer", viewer);
            AssignReference(app, "arSession", arSession);
            AssignReference(app, "arCamera", arCamera);
            AssignReference(app, "arCameraManager", cameraManager);
            AssignReference(app, "arCameraBackground", cameraBackground);

            CreateEventSystem();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes =
                new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static ModelViewerController CreateModelViewer(Camera arCamera)
        {
            const int viewerLayer = 8;
            GameObject root = new GameObject("Three Dimensional Resource Viewer");
            GameObject modelRoot = new GameObject("ModelViewRoot");
            modelRoot.transform.SetParent(root.transform, false);
            modelRoot.layer = viewerLayer;
            ModelViewerController controller = root.AddComponent<ModelViewerController>();
            GameObject cameraObject = new GameObject("Resource Viewer Camera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.layer = viewerLayer;
            Camera viewerCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            viewerCamera.clearFlags = CameraClearFlags.SolidColor;
            viewerCamera.backgroundColor = new Color32(235, 241, 248, 255);
            viewerCamera.fieldOfView = 32f;
            viewerCamera.allowHDR = false;
            viewerCamera.allowMSAA = true;
            viewerCamera.depth = 5f;
            viewerCamera.cullingMask = 1 << viewerLayer;
            viewerCamera.targetTexture = null;
            cameraObject.SetActive(false);
            arCamera.cullingMask &= ~(1 << viewerLayer);

            GameObject lightObject = new GameObject("Resource Viewer Key Light");
            lightObject.transform.SetParent(root.transform, false);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.72f;
            light.color = new Color(1f, 0.98f, 0.94f);
            light.cullingMask = 1 << viewerLayer;
            lightObject.transform.rotation = Quaternion.Euler(42f, -38f, 0f);
            AssignReference(controller, "viewerCamera", viewerCamera);
            AssignReference(controller, "modelViewRoot", modelRoot.transform);
            return controller;
        }

        private static Material CreateLitMaterial(
            string path, string texturePath, float smoothness)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("Universal Render Pipeline/Lit shader is missing.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.DisableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", Color.black);
            if (!string.IsNullOrEmpty(texturePath))
                material.SetTexture("_BaseMap",
                    AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject CreateBottleNeckOccluder()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(OccluderShaderPath);
            if (shader == null)
                throw new InvalidOperationException("Depth-only occluder shader is missing.");
            const string materialPath = "Assets/Materials/BottleNeckOccluder.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.shader = shader;
            material.renderQueue = 1990;
            EditorUtility.SetDirty(material);

            GameObject root = new GameObject("BottleNeckOccluder");
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = "MeasuredNeckDepth";
            cylinder.transform.SetParent(root.transform, false);
            cylinder.transform.localPosition = new Vector3(0f, -0.09f, 0f);
            cylinder.transform.localScale = new Vector3(0.2f, 0.11f, 0.2f);
            Collider collider = cylinder.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            MeshRenderer renderer = cylinder.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            PrefabUtility.SaveAsPrefabAsset(root, BottleOccluderPath);
            UnityEngine.Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(BottleOccluderPath);
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets");
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static void RequireFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);
        }

        private static void CreateEventSystem()
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        private static void AssignReference(
            UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
