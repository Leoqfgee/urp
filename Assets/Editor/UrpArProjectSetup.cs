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
using Urp.ArDemo.Generated;

namespace Urp.ArDemo.Editor
{
    public static class UrpArProjectSetup
    {
        private const string ScenePath = "Assets/Scenes/UrpARPrototype.unity";
        private const string FontPath = "Assets/Fonts/NotoSansSC-Regular.otf";
        private const string BottleDamagedPath =
            "Assets/Models/CleanBottleReconstruction/bottle_damaged_clean.obj";
        private const string BottleCompletePath =
            "Assets/Models/CleanBottleReconstruction/bottle_complete_clean.obj";
        private const string BottleTexturePath =
            "Assets/Models/CleanBottleReconstruction/bottle_atlas.png";
        private const string BottleRepairPath =
            "Assets/Models/CleanBottleReconstruction/bottle_cap_clean.obj";
        private const string BottleOccluderPath =
            "Assets/Objects/CoconutBottle/Prefabs/BottleNeckOccluder.prefab";
        private const string OccluderShaderPath =
            "Assets/Shaders/DepthOnlyOccluder.shader";
        private const string ForceMagentaShaderPath =
            "Assets/Shaders/ForceMagentaDebug.shader";
        private const string ForceMagentaMaterialPath =
            "Assets/Resources/ForceMagentaDebug.mat";
        private const string AndroidApkPath = "Builds/CanonicalBottleRepairAR.apk";
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
            BuildIdentityData identity = BuildIdentityGenerator.Generate();
            SetupPrototypeScene();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CleanStaleSimulationTempAssets();
            Directory.CreateDirectory("Builds");
            DeleteLegacyApks();
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = AndroidApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException($"Android build failed: {report.summary.result}");
            }
            BuildIdentityGenerator.VerifyApk(AndroidApkPath, identity);
            BuildIdentityGenerator.VerifyNativePluginInApk(AndroidApkPath);
            Debug.Log($"[BuildIdentity] APK SHA256: {BuildIdentityGenerator.Sha256(AndroidApkPath)}");
        }

        private static void DeleteLegacyApks()
        {
            string buildsPath = Path.GetFullPath("Builds");
            if (!Directory.Exists(buildsPath)) return;
            foreach (string apk in Directory.GetFiles(
                         buildsPath, "*.apk", SearchOption.TopDirectoryOnly))
            {
                Debug.Log($"Deleting legacy APK: {apk}");
                File.Delete(apk);
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
                "Assets/Shaders", "Assets/Resources"
            };
            foreach (string folder in folders) Directory.CreateDirectory(folder);
        }

        private static void ConfigureAndroidProject()
        {
            PlayerSettings.productName = "瓶口拼合 AR";
            PlayerSettings.companyName = "qfgeeee";
            PlayerSettings.bundleVersion = "2.0.0";
            PlayerSettings.SetApplicationIdentifier(
                BuildTargetGroup.Android, "com.qfgeeee.canonicalbottlerepairar");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.bundleVersionCode = 200;
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
            CreateForceMagentaDebugMaterial();
            Material bottleMaterial = CreateLitMaterial(
                "Assets/Materials/BottleViewerLit.mat", BottleTexturePath, 0.20f);
            Material tissueMaterial = CreateLitMaterial(
                "Assets/Objects/Tissue/Materials/TissueViewerLit.mat", TissueTexturePath, 0.16f);
            Material repairMaterial = CreateLitMaterial(
                "Assets/Materials/RegisteredBottleCap.mat", null, 0.34f);
            if (repairMaterial.HasProperty("_Cull"))
                repairMaterial.SetFloat("_Cull", 0f);
            repairMaterial.doubleSidedGI = true;
            repairMaterial.EnableKeyword("_EMISSION");
            repairMaterial.SetColor("_EmissionColor", new Color(0.16f, 0.16f, 0.15f, 1f));
            EditorUtility.SetDirty(repairMaterial);
            GameObject bottleOccluder = CreateBottleNeckOccluder();

            RestorationObjectProfile bottle = LoadOrCreate<RestorationObjectProfile>(
                BottleProfilePath);
            bottle.objectId = "coconut_bottle";
            bottle.displayName = "瓶盖缺失饮料瓶";
            bottle.shortDescription = "瓶身保留瓶口与螺纹结构，缺失部分为瓶盖。";
            bottle.viewerDescription =
                "残缺模型 b 保留用户指定的原始摄影测量瓶身，只切除错误重建的旧瓶盖，"
                + "并在 Blender 中补齐瓶口坐标基准。该模型仍保留源扫描的粗糙与缺损，不再用圆柱替换瓶身。"
                + "瓶盖 c 按外径 39 mm、高 10 mm 建模并与 34 mm 瓶口同轴注册。";
            bottle.trackingDescription =
                "真实瓶 a 与无盖参考模型 b 通过瓶身自然特征和 ORB 2D—3D/PnP 配准。"
                + "b 与瓶盖 c 已在 Blender 中共享瓶口 canonical 坐标系；运行时 b 的 Renderer 永久关闭，"
                + "只显示随共同位姿根节点移动的 c。遮挡体在实机配准通过前保持关闭。";
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
            // The registration is baked into bottle_cap_clean.obj. Runtime
            // transform remains identity in the mouth-centred canonical frame.
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
            bottle.trackingSettings.lostPoseGraceSeconds = 2.5f;
            bottle.trackingSettings.minimumGoodMatches = 14;
            bottle.trackingSettings.minimumPoseInliers = 10;
            bottle.trackingSettings.minimumInlierRatio = 0.50f;
            bottle.trackingSettings.maximumReprojectionErrorPixels = 2.5f;
            bottle.trackingSettings.minimumCoverageX = 0.06f;
            bottle.trackingSettings.minimumCoverageY = 0.20f;
            bottle.trackingSettings.maximumPositionJumpMeters = 0.06f;
            bottle.trackingSettings.maximumRotationJumpDegrees = 18f;
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
            arCamera.nearClipPlane = 0.02f;
            arCamera.farClipPlane = 20f;
            arCamera.cullingMask |= 1 << 0;
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraObject.AddComponent<AudioListener>();
            ARCameraManager cameraManager = cameraObject.AddComponent<ARCameraManager>();
            ARCameraBackground cameraBackground = cameraObject.AddComponent<ARCameraBackground>();
            cameraBackground.enabled = false;
            cameraManager.enabled = false;
            arCamera.enabled = false;
            origin.Camera = arCamera;
            CreateRepairLighting(cameraObject.transform);

            GameObject trackedRoot = new GameObject("TrackedObjectPoseRoot");
            GameObject alignment = new GameObject("ModelCoordinateAlignment");
            alignment.transform.SetParent(trackedRoot.transform, false);
            GameObject referenceRoot = new GameObject("ModelReferenceRoot");
            referenceRoot.transform.SetParent(alignment.transform, false);
            GameObject repairRoot = new GameObject("RepairPartRoot");
            repairRoot.transform.SetParent(alignment.transform, false);
            GameObject occlusionRoot = new GameObject("OcclusionRoot");
            occlusionRoot.transform.SetParent(alignment.transform, false);
            occlusionRoot.SetActive(false);
            GameObject debugRoot = new GameObject("DebugRoot");
            debugRoot.transform.SetParent(alignment.transform, false);
            debugRoot.SetActive(false);
            trackedRoot.SetActive(false);

            GameObject application = new GameObject("URP Application");
            application.AddComponent<BuildIdentityRuntime>();
            RepairOverlayController overlay = application.AddComponent<RepairOverlayController>();
            OrbImageTrackingController tracker =
                originObject.AddComponent<OrbImageTrackingController>();
            AssignReference(tracker, "cameraManager", cameraManager);
            AssignReference(tracker, "arCamera", arCamera);
            AssignReference(tracker, "trackedObjectPoseRoot", trackedRoot.transform);
            AssignReference(tracker, "modelCoordinateAlignment", alignment.transform);
            AssignReference(tracker, "modelReferenceRoot", referenceRoot.transform);
            AssignReference(tracker, "repairPartRoot", repairRoot.transform);
            AssignReference(tracker, "occlusionRoot", occlusionRoot.transform);
            AssignReference(tracker, "debugRoot", debugRoot.transform);
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

        private static void CreateRepairLighting(Transform cameraTransform)
        {
            GameObject keyObject = new GameObject("AR Repair Key Light");
            keyObject.transform.SetParent(cameraTransform, false);
            keyObject.transform.localRotation = Quaternion.Euler(38f, -32f, 0f);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 0.92f;
            key.color = new Color(1f, 0.98f, 0.95f);
            key.cullingMask = 1;
            key.shadows = LightShadows.None;

            GameObject fillObject = new GameObject("AR Repair Fill Light");
            fillObject.transform.SetParent(cameraTransform, false);
            fillObject.transform.localRotation = Quaternion.Euler(18f, 148f, 0f);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.34f;
            fill.color = new Color(0.88f, 0.93f, 1f);
            fill.cullingMask = 1;
            fill.shadows = LightShadows.None;
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

        private static void CreateForceMagentaDebugMaterial()
        {
            AssetDatabase.ImportAsset(ForceMagentaShaderPath, ImportAssetOptions.ForceUpdate);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ForceMagentaShaderPath);
            if (shader == null || shader.name != "Hidden/URP/ForceMagentaDebug")
                throw new InvalidOperationException("Hard-coded force-magenta shader is missing.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(ForceMagentaMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, ForceMagentaMaterialPath);
            }
            material.shader = shader;
            material.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(material);
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
