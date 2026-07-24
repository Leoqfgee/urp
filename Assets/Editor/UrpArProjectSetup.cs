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
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using Urp.ArDemo.Calibration;
using Urp.ArDemo.Generated;

namespace Urp.ArDemo.Editor
{
    public static class UrpArProjectSetup
    {
        private const string ScenePath = "Assets/Scenes/UrpARPrototype.unity";
        private const string FontPath = "Assets/Fonts/NotoSansSC-Regular.otf";
        private const string BottleRegisteredPairPath =
            "Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/"
            + "bottle_full_aligned_v2.fbx";
        private const string BottleThumbnailPath =
            "Assets/Textures/Targets/bottle_full_aligned_v2.png";
        private const string BottleAlbedoPath =
            "Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/"
            + "Textures/bottle_full_clean_v2_albedo.png";
        private const string BottleSurfaceMaterialPath =
            "Assets/Materials/BottlePhotogrammetryLit.mat";
        private const string BottleCapMaterialPath =
            "Assets/Materials/CleanBottleCapLit.mat";
        private const string BottleDepthMaterialPath =
            "Assets/Materials/BottleDepthOccluder.mat";
        private const string AndroidApkPath = "Builds/BottleFullAlignedV2AR.apk";
        private const string BottleReferenceOrbPath =
            "Assets/OrbModels/bottle_reference_b.bytes";
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
            if (!File.Exists(ScenePath))
            {
                throw new BuildFailedException(
                    $"Saved production scene is missing: {ScenePath}");
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CleanStaleSimulationTempAssets();
            Directory.CreateDirectory("Builds");
            DeletePreviousTargetApk();
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

        private static void DeletePreviousTargetApk()
        {
            string apk = Path.GetFullPath(AndroidApkPath);
            if (File.Exists(apk))
            {
                Debug.Log($"Deleting previous target APK: {apk}");
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
            PlayerSettings.productName = "刚性瓶体配准修复";
            PlayerSettings.companyName = "qfgeeee";
            PlayerSettings.bundleVersion = "4.2.0";
            PlayerSettings.SetApplicationIdentifier(
                BuildTargetGroup.Android, "com.qfgeeee.paper52objecttrackingar");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.bundleVersionCode = 420;
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
                         BottleRegisteredPairPath, TissueModelPath
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
                    importer.preserveHierarchy = path == BottleRegisteredPairPath;
                    importer.materialImportMode =
                        path == BottleRegisteredPairPath
                            ? ModelImporterMaterialImportMode.ImportStandard
                            : ModelImporterMaterialImportMode.None;
                    importer.SaveAndReimport();
                }
            }

            ConfigureTexture(BottleThumbnailPath, 1024);
            ConfigureTexture(BottleAlbedoPath, 4096);
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
            Material tissueMaterial = CreateLitMaterial(
                "Assets/Objects/Tissue/Materials/TissueViewerLit.mat", TissueTexturePath, 0.16f);
            Material bottleSurfaceMaterial = CreateLitMaterial(
                BottleSurfaceMaterialPath, BottleAlbedoPath, 0.12f, true);
            Material bottleCapMaterial = CreateLitMaterial(
                BottleCapMaterialPath, null, 0.28f, true);
            bottleCapMaterial.SetColor(
                "_BaseColor",
                new Color(0.96f, 0.96f, 0.94f, 1f));
            Material bottleDepthMaterial =
                CreateDepthOcclusionMaterial(BottleDepthMaterialPath);
            GameObject bottlePair =
                AssetDatabase.LoadAssetAtPath<GameObject>(BottleRegisteredPairPath);
            if (bottlePair == null)
                throw new MissingReferenceException("BottleFullAlignedV2 FBX import failed.");

            RestorationObjectProfile bottle = LoadOrCreate<RestorationObjectProfile>(
                BottleProfilePath);
            bottle.objectId = "bottle_full_aligned_v2";
            bottle.displayName = "新重建无盖饮料瓶与瓶盖";
            bottle.shortDescription =
                "Blender 中刚性对齐的无盖瓶身 B 与干净白色瓶盖 C。";
            bottle.viewerDescription =
                "DamagedBottleB 是保留真实纹理的无盖瓶身；"
                + "BottleCapC 是 39mm x 10mm 的干净瓶盖。"
                + "两者在 Blender 中以共同瓶口坐标系对齐，"
                + "并作为 BottleRepairRoot 下的固定同级子对象保存。";
            bottle.trackingDescription =
                "进入页面后 B+C 以正面初始位姿显示在画面中央，"
                + "同时使用真实无盖瓶照片的 ORB 特征识别 A→B 六自由度位姿。"
                + "点击开始后，只在姿态稳定时隐藏 B 的颜色，"
                + "但保留 B 深度遮挡和 B/C 刚性关系。"
                + "C 不单独识别，不挂在屏幕或摄像机下。"
                + "C 的外观结合真实瓶身 HSV 样本和 AR 光照估计平滑校正。";
            bottle.missingPartName = "瓶盖 C";
            bottle.thumbnail =
                AssetDatabase.LoadAssetAtPath<Texture2D>(BottleThumbnailPath);
            bottle.damagedViewerPrefab = bottlePair;
            bottle.completeViewerPrefab = bottlePair;
            bottle.trackingReferencePrefab = bottlePair;
            bottle.registeredBottlePairPrefab = bottlePair;
            bottle.trackingReferenceDatabase =
                AssetDatabase.LoadAssetAtPath<TextAsset>(BottleReferenceOrbPath);
            RepairCalibrationProfile bottleCalibration =
                LoadOrCreate<RepairCalibrationProfile>(BottleCalibrationPath);
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
            bottleCalibration.orbToModelLocalPosition = Vector3.zero;
            bottleCalibration.orbToModelLocalEulerAngles = Vector3.zero;
            bottleCalibration.orbToModelLocalScale = Vector3.one;
            EditorUtility.SetDirty(bottleCalibration);
            bottle.calibration = bottleCalibration;
            bottle.viewerMaterial = bottleSurfaceMaterial;
            bottle.repairMaterial = bottleCapMaterial;
            bottle.referenceDepthOcclusionMaterial = bottleDepthMaterial;
            bottle.defaultViewerEuler = Vector3.zero;
            bottle.viewerMargin = 0.18f;
            bottle.trackingSettings.minimumGoodMatches = 8;
            bottle.trackingSettings.minimumPoseInliers = 6;
            bottle.trackingSettings.minimumInlierRatio = 0.35f;
            bottle.trackingSettings.maximumReprojectionErrorPixels = 3.0f;
            bottle.trackingSettings.maximumReprojectionMaxPixels = 8.0f;
            bottle.trackingSettings.minimumCoverageX = 0.05f;
            bottle.trackingSettings.minimumCoverageY = 0.10f;
            bottle.trackingSettings.registrationConfirmationFrames = 5;
            bottle.trackingSettings.registrationPositionToleranceMeters = 0.025f;
            bottle.trackingSettings.registrationRotationToleranceDegrees = 8f;
            bottle.trackingSettings.temporaryLossHoldSeconds = 2.5f;
            bottle.trackingSettings.positionSmoothing = 0.20f;
            bottle.trackingSettings.rotationSmoothing = 0.18f;
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
            tissue.trackingReferencePrefab = null;
            tissue.registeredBottlePairPrefab = null;
            tissue.trackingReferenceDatabase = null;
            tissue.calibration = tissueCalibration;
            tissue.viewerMaterial = tissueMaterial;
            tissue.repairMaterial = null;
            tissue.referenceDepthOcclusionMaterial = null;
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
            cameraManager.requestedLightEstimation =
                LightEstimation.AmbientIntensity
                | LightEstimation.AmbientColor
                | LightEstimation.AmbientSphericalHarmonics
                | LightEstimation.MainLightDirection
                | LightEstimation.MainLightIntensity;
            ARCameraBackground cameraBackground = cameraObject.AddComponent<ARCameraBackground>();
            AROcclusionManager occlusionManager =
                cameraObject.AddComponent<AROcclusionManager>();
            occlusionManager.requestedEnvironmentDepthMode =
                EnvironmentDepthMode.Fastest;
            occlusionManager.requestedOcclusionPreferenceMode =
                OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            cameraBackground.enabled = false;
            cameraManager.enabled = false;
            occlusionManager.enabled = false;
            arCamera.enabled = false;
            origin.Camera = arCamera;
            Light estimatedMainLight = CreateRepairLighting();

            GameObject trackedRoot = new GameObject("TrackedBottleRoot");
            GameObject alignment = new GameObject("ModelCoordinateAlignment");
            alignment.transform.SetParent(trackedRoot.transform, false);
            GameObject occlusionRoot = new GameObject("OcclusionRoot");
            occlusionRoot.transform.SetParent(alignment.transform, false);
            occlusionRoot.SetActive(false);
            GameObject debugRoot = new GameObject("DebugRoot");
            debugRoot.transform.SetParent(alignment.transform, false);
            debugRoot.SetActive(false);
            trackedRoot.SetActive(true);

            GameObject application = new GameObject("URP Application");
            application.AddComponent<BuildIdentityRuntime>();
            RepairOverlayController overlay = application.AddComponent<RepairOverlayController>();
            OrbImageTrackingController tracker =
                originObject.AddComponent<OrbImageTrackingController>();
            RepairAppearanceConsistencyController appearance =
                originObject.AddComponent<RepairAppearanceConsistencyController>();
            AssignReference(appearance, "cameraManager", cameraManager);
            AssignReference(appearance, "estimatedMainLight", estimatedMainLight);
            AssignReference(tracker, "cameraManager", cameraManager);
            AssignReference(tracker, "arCamera", arCamera);
            AssignReference(tracker, "appearanceConsistency", appearance);
            AssignReference(tracker, "trackedObjectPoseRoot", trackedRoot.transform);
            AssignReference(tracker, "modelCoordinateAlignment", alignment.transform);
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
            AssignReference(app, "arOcclusionManager", occlusionManager);

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

        private static Light CreateRepairLighting()
        {
            GameObject keyObject = new GameObject("AR Estimated Main Light");
            keyObject.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 0.8f;
            key.color = Color.white;
            key.cullingMask = 1;
            key.shadows = LightShadows.None;
            return key;
        }

        private static Material CreateLitMaterial(
            string path, string texturePath, float smoothness, bool doubleSided = false)
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
            material.SetFloat(
                "_Cull",
                (float)(doubleSided ? CullMode.Off : CullMode.Back));
            material.doubleSidedGI = doubleSided;
            material.DisableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", Color.black);
            if (!string.IsNullOrEmpty(texturePath))
                material.SetTexture("_BaseMap",
                    AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateDepthOcclusionMaterial(string path)
        {
            Shader shader = Shader.Find("URP AR/Bottle Depth Occluder");
            if (shader == null)
                throw new InvalidOperationException(
                    "URP AR/Bottle Depth Occluder shader is missing.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.name = "BottleDepthOccluder";
            material.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(material);
            return material;
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
