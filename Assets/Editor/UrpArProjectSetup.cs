using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.ARSubsystems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Urp.ArDemo.Editor
{
    public static class UrpArProjectSetup
    {
        private const string ScenePath = "Assets/Scenes/UrpARPrototype.unity";
        private const string OrbTargetImagePath = "Assets/Textures/Targets/orb_target.jpg";
        private const string DamagedBottleViewsFolder = "Assets/Textures/Targets/DamagedBottleOrbViews";
        private const string OrbModelsFolder = "Assets/OrbModels";
        private const string ChineseFontPath = "Assets/Fonts/NotoSansSC-Regular.otf";
        private const string MeshroomFullPath = "Assets/Models/MeshroomBottle/texturedMesh.obj";
        private const string MeshroomDamagedPath = "Assets/Models/MeshroomBottleDamaged/texturedMesh.obj";
        private const string MeshroomCapPath = "Assets/Models/MeshroomBottleCap/texturedMesh.obj";
        private const string MeshroomCleanedCapPath = "Assets/Models/MeshroomCapCleaned/meshroom_cap_cleaned.obj";
        private const string MeshroomProcessedCapPath = "Assets/Models/MeshroomCapProcessed/meshroom_cap_processed.obj";
        private const string MarkerLibraryPath = "Assets/Textures/BottleMarkerLibrary.asset";
        private const string PlanePrefabPath = "Assets/Prefabs/TrackedPlane.prefab";

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
                "Assets/Fonts",
                "Assets/Materials",
                "Assets/Models",
                "Assets/OrbModels",
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
            PlayerSettings.productName = "URP AR 数字修复";
            PlayerSettings.companyName = "qfgeeee";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.qfgeeee.urpardemo");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.allowUnsafeCode = true;
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
            ImportMeshroomEvidenceAssets();

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
            var cameraManager = cameraObject.AddComponent<ARCameraManager>();
            cameraObject.AddComponent<ARCameraBackground>();
            origin.Camera = camera;

            ARPlaneManager planeManager = xrOrigin.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            planeManager.planePrefab = CreatePlanePrefab();
            xrOrigin.AddComponent<ARRaycastManager>();

            ARTrackedImageManager imageManager = xrOrigin.AddComponent<ARTrackedImageManager>();
            imageManager.referenceLibrary = CreateMarkerLibrary();
            imageManager.requestedMaxNumberOfMovingImages = 1;
            imageManager.enabled = false;

            GameObject contentRoot = new GameObject("Tracked Repair Root");
            contentRoot.transform.position = new Vector3(0f, 0f, 1.25f);
            GameObject repair = CreateMeshroomBottleCapRepair();
            repair.transform.SetParent(contentRoot.transform, false);
            repair.transform.localPosition = Vector3.zero;
            repair.transform.localRotation = Quaternion.identity;
            repair.transform.localScale = Vector3.one * 0.02f;
            CreateBottleNeckOccluder(contentRoot.transform);

            GameObject slamRepairRoot = new GameObject("SLAM Repair Root");
            GameObject slamRepair = CreateMeshroomBottleCapRepair();
            slamRepair.name = "SLAM Meshroom Bottle Cap";
            slamRepair.transform.SetParent(slamRepairRoot.transform, false);
            slamRepair.transform.localScale = Vector3.one * 0.02f;
            slamRepairRoot.SetActive(false);

            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject application = new GameObject("URP Application");
            var overlayController = application.AddComponent<RepairOverlayController>();
            AssignSerializedReference(overlayController, "repairRoot", repair.transform);

            var orbTracker = xrOrigin.AddComponent<OrbImageTrackingController>();
            AssignSerializedReference(orbTracker, "cameraManager", cameraManager);
            AssignSerializedReference(orbTracker, "arCamera", camera);
            AssignSerializedReference(orbTracker, "trackedContentRoot", contentRoot.transform);
            AssignSerializedObjectArray(orbTracker, "orbModelFiles", LoadOrbModelFiles());
            AssignVector3Array(orbTracker, "repairAnchorsByModel", CreateRepairAnchorsInModel());
            AssignSerializedReference(overlayController, "orbTracker", orbTracker);

            ModelViewerController modelViewer = CreateModelViewer(camera);
            PlanarMarkerSlamController slamController = application.AddComponent<PlanarMarkerSlamController>();
            AssignSerializedReference(slamController, "planeManager", planeManager);
            AssignSerializedReference(slamController, "imageManager", imageManager);
            AssignSerializedReference(slamController, "repairRoot", slamRepairRoot.transform);

            UrpAppController appController = application.AddComponent<UrpAppController>();
            AssignSerializedReference(appController, "chineseFont", AssetDatabase.LoadAssetAtPath<Font>(ChineseFontPath));
            AssignSerializedReference(appController, "orbTracker", orbTracker);
            AssignSerializedReference(appController, "repairController", overlayController);
            AssignSerializedReference(appController, "modelViewer", modelViewer);
            AssignSerializedReference(appController, "slamController", slamController);

            CreateEventSystem();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static XRReferenceImageLibrary CreateMarkerLibrary()
        {
            Texture2D marker = LoadOrbTargetTexture();
            XRReferenceImageLibrary library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(MarkerLibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
                AssetDatabase.CreateAsset(library, MarkerLibraryPath);
            }

            while (library.count > 0)
            {
                library.RemoveAt(0);
            }

            library.Add();
            library.SetName(0, "damaged_bottle_front_marker");
            library.SetTexture(0, marker, true);
            library.SetSpecifySize(0, true);
            float height = 0.12f * marker.height / Mathf.Max(1f, marker.width);
            library.SetSize(0, new Vector2(0.12f, height));
            EditorUtility.SetDirty(library);
            return library;
        }

        private static GameObject CreatePlanePrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlanePrefabPath);
            if (existing != null)
            {
                return existing;
            }

            GameObject plane = new GameObject("Tracked Plane Visual");
            plane.AddComponent<MeshFilter>();
            MeshRenderer renderer = plane.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateMaterial("TrackedPlaneMaterial", new Color(0.10f, 0.75f, 0.55f, 0.22f));
            plane.AddComponent<ARPlaneMeshVisualizer>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(plane, PlanePrefabPath);
            UnityEngine.Object.DestroyImmediate(plane);
            return prefab;
        }

        private static ModelViewerController CreateModelViewer(Camera arCamera)
        {
            const int viewerLayer = 8;
            GameObject viewerRoot = new GameObject("Three Dimensional Resource Viewer");
            ModelViewerController controller = viewerRoot.AddComponent<ModelViewerController>();

            GameObject viewerCameraObject = new GameObject("Resource Viewer Camera");
            viewerCameraObject.transform.SetParent(viewerRoot.transform, false);
            Camera viewerCamera = viewerCameraObject.AddComponent<Camera>();
            viewerCamera.clearFlags = CameraClearFlags.SolidColor;
            viewerCamera.backgroundColor = new Color32(246, 249, 253, 255);
            viewerCamera.fieldOfView = 32f;
            viewerCamera.depth = 5f;
            viewerCamera.cullingMask = 1 << viewerLayer;
            viewerCameraObject.SetActive(false);
            arCamera.cullingMask &= ~(1 << viewerLayer);

            Transform damaged = InstantiateViewerModel(MeshroomDamagedPath, "残缺饮料瓶模型", viewerRoot.transform, viewerLayer);
            Transform complete = InstantiateViewerModel(MeshroomFullPath, "完整饮料瓶模型", viewerRoot.transform, viewerLayer);

            GameObject lightObject = new GameObject("Resource Viewer Light");
            lightObject.transform.SetParent(viewerRoot.transform, false);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.cullingMask = 1 << viewerLayer;
            lightObject.layer = viewerLayer;
            lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            AssignSerializedReference(controller, "viewerCamera", viewerCamera);
            AssignSerializedReference(controller, "damagedModel", damaged);
            AssignSerializedReference(controller, "completeModel", complete);
            return controller;
        }

        private static Transform InstantiateViewerModel(string path, string name, Transform parent, int layer)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.position = new Vector3(0f, 0.35f, 4f);
            if (asset == null)
            {
                return holder.transform;
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            model.transform.SetParent(holder.transform, false);
            SetLayerRecursively(model, layer);
            Bounds bounds = CalculateBounds(model);
            float scale = 2.2f / Mathf.Max(0.001f, Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)));
            model.transform.localScale = Vector3.one * scale;
            bounds = CalculateBounds(model);
            model.transform.position += holder.transform.position - bounds.center;
            return holder.transform;
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
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

        private static void ImportMeshroomEvidenceAssets()
        {
            string[] paths =
            {
                MeshroomFullPath,
                MeshroomDamagedPath,
                MeshroomCapPath,
                MeshroomCleanedCapPath,
                MeshroomProcessedCapPath
            };

            foreach (string path in paths)
            {
                if (File.Exists(path))
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
                else
                {
                    Debug.LogWarning($"Meshroom evidence model is missing: {path}");
                }
            }
        }

        private static Texture2D LoadOrbTargetTexture()
        {
            TextureImporter importer = AssetImporter.GetAtPath(OrbTargetImagePath) as TextureImporter;
            if (importer != null)
            {
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(OrbTargetImagePath);
            if (texture == null)
            {
                throw new FileNotFoundException($"ORB target image not found: {OrbTargetImagePath}");
            }

            return texture;
        }

        private static TextAsset[] LoadOrbModelFiles()
        {
            string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { OrbModelsFolder });
            Array.Sort(guids, (a, b) => string.CompareOrdinal(
                AssetDatabase.GUIDToAssetPath(a),
                AssetDatabase.GUIDToAssetPath(b)));

            var models = new System.Collections.Generic.List<TextAsset>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TextAsset model = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (model != null)
                {
                    models.Add(model);
                }
            }

            if (models.Count == 0)
            {
                throw new FileNotFoundException($"ORB 3D model files not found in {OrbModelsFolder}");
            }

            return models.ToArray();
        }

        private static Texture2D[] LoadDamagedBottleTargetTextures()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { DamagedBottleViewsFolder });
            Array.Sort(guids, (a, b) => string.CompareOrdinal(AssetDatabase.GUIDToAssetPath(a), AssetDatabase.GUIDToAssetPath(b)));

            var textures = new System.Collections.Generic.List<Texture2D>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.Contains("contact_sheet"))
                {
                    continue;
                }

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.isReadable = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture != null)
                {
                    textures.Add(texture);
                }
            }

            return textures.ToArray();
        }

        private static Vector2[] CreateRepairAnchors()
        {
            return new[]
            {
                new Vector2(0.50f, 0.065f),
                new Vector2(0.50f, 0.065f),
                new Vector2(0.54f, 0.070f),
                new Vector2(0.51f, 0.065f),
                new Vector2(0.45f, 0.080f),
                new Vector2(0.49f, 0.070f),
                new Vector2(0.50f, 0.040f),
                new Vector2(0.52f, 0.040f),
                new Vector2(0.52f, 0.070f),
                new Vector2(0.50f, 0.060f),
                new Vector2(0.50f, 0.035f),
                new Vector2(0.50f, 0.050f),
                new Vector2(0.50f, 0.130f),
                new Vector2(0.52f, 0.120f),
                new Vector2(0.54f, 0.090f),
                new Vector2(0.52f, 0.120f),
                new Vector2(0.50f, 0.070f),
                new Vector2(0.49f, 0.070f),
                new Vector2(0.49f, 0.075f),
                new Vector2(0.50f, 0.080f),
            };
        }

        private static Vector3[] CreateRepairAnchorsInModel()
        {
            return new[]
            {
                new Vector3(0.436797f, -4.469445f, 0.241100f),
                new Vector3(0.329223f, -4.503785f, 0.301715f),
                new Vector3(0.499099f, -4.514827f, 0.314027f),
                new Vector3(0.443112f, -4.489468f, 0.246917f),
                new Vector3(0.393167f, -4.494358f, 0.255548f),
                new Vector3(0.339826f, -4.483784f, 0.314265f),
                new Vector3(0.399622f, -4.537598f, 0.410669f),
                new Vector3(0.486929f, -4.534626f, 0.346561f),
                new Vector3(0.437438f, -4.509196f, 0.270578f),
                new Vector3(0.362550f, -4.537083f, 0.353855f),
                new Vector3(0.433431f, -4.572862f, 0.395623f),
                new Vector3(0.405407f, -4.563286f, 0.330579f),
                new Vector3(0.419225f, -4.523258f, 0.348041f),
            };
        }

        private static float[] CreateTargetViewYawDegrees()
        {
            return new[]
            {
                0f,
                25f,
                70f,
                130f,
                180f,
                -120f,
                -35f,
                10f,
                60f,
                160f,
                -20f,
                0f,
                70f,
                180f,
                60f,
                20f,
                20f,
                -60f,
                -110f,
                0f,
            };
        }

        private static GameObject CreateCanvas(out Text statusText)
        {
            GameObject canvasObject = new GameObject("Demo UI");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            statusText = CreateText(
                canvasObject.transform,
                "Status Text",
                "请对准没盖子的饮料瓶，系统会识别瓶身并叠加修复瓶盖。",
                new Vector2(0.5f, 1f),
                new Vector2(0f, -44f),
                new Vector2(1000f, 90f),
                24,
                TextAnchor.MiddleCenter);

            return canvasObject;
        }

        private static GameObject CreateDamagedBottleGuide()
        {
            GameObject guide = new GameObject("Damaged Bottle Alignment Guide");

            GameObject neck = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            neck.name = "Bottle Mouth Occlusion Guide";
            neck.transform.SetParent(guide.transform, false);
            neck.transform.localPosition = new Vector3(0f, 0f, 0f);
            neck.transform.localScale = new Vector3(0.085f, 0.018f, 0.085f);
            neck.GetComponent<Renderer>().sharedMaterial = CreateMaterial("DamagedBottleNeckGuide", new Color(0.3f, 0.85f, 0.62f, 0.18f));

            return guide;
        }

        private static GameObject CreateMeshroomBottleCapRepair()
        {
            AssetDatabase.ImportAsset(MeshroomProcessedCapPath, ImportAssetOptions.ForceUpdate);
            GameObject importedModel = AssetDatabase.LoadAssetAtPath<GameObject>(MeshroomProcessedCapPath);
            if (importedModel != null)
            {
                GameObject cap = (GameObject)PrefabUtility.InstantiatePrefab(importedModel);
                cap.name = "Meshroom Processed Bottle Cap Repair Model";
                cap.transform.localPosition = Vector3.zero;
                cap.transform.localRotation = Quaternion.identity;
                cap.transform.localScale = Vector3.one;
                Material repairMaterial = CreateMaterial("MeshroomCapRepairMaterial", new Color(0.88f, 0.97f, 1f, 1f));
                repairMaterial.EnableKeyword("_EMISSION");
                repairMaterial.SetColor("_EmissionColor", new Color(0.04f, 0.16f, 0.20f, 1f));
                repairMaterial.SetFloat("_Glossiness", 0.45f);
                ApplyMaterialToRenderers(cap, repairMaterial);
                return cap;
            }

            Debug.LogWarning($"Processed Meshroom cap model missing, using fallback primitive: {MeshroomProcessedCapPath}");
            return CreateFallbackBottleCap();
        }

        private static void CreateBottleNeckOccluder(Transform parent)
        {
            const string materialPath = "Assets/Materials/BottleNeckOcclusion.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("URP/DepthOnlyOccluder");
                if (shader == null)
                {
                    Debug.LogWarning("Depth-only occlusion shader was not imported; bottle neck occlusion is disabled.");
                    return;
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            GameObject occluder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            occluder.name = "Bottle Neck Occlusion Geometry";
            occluder.transform.SetParent(parent, false);
            occluder.transform.localPosition = new Vector3(0f, -0.026f, 0f);
            occluder.transform.localScale = new Vector3(0.021f, 0.026f, 0.021f);
            occluder.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(occluder.GetComponent<Collider>());
        }

        private static GameObject CreateFallbackBottleCap()
        {
            GameObject capRoot = new GameObject("Fallback Bottle Cap");
            Material capMaterial = CreateMaterial("CleanWhiteBottleCap", new Color(0.92f, 0.90f, 0.84f, 1f));

            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cap.name = "Virtual Repair Bottle Cap";
            cap.transform.SetParent(capRoot.transform, false);
            cap.transform.localPosition = Vector3.zero;
            cap.transform.localScale = new Vector3(1f, 0.5f, 1f);
            cap.GetComponent<Renderer>().sharedMaterial = capMaterial;
            return capRoot;
        }

        private static void CreateControls(Transform parent, RepairOverlayController repairController)
        {
            GameObject row = new GameObject("Control Row");
            row.transform.SetParent(parent, false);
            RectTransform rowRect = row.AddComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0.08f, 0.035f);
            rowRect.anchorMax = new Vector2(0.92f, 0.035f);
            rowRect.pivot = new Vector2(0.5f, 0f);
            rowRect.anchoredPosition = Vector2.zero;
            rowRect.sizeDelta = new Vector2(0f, 170f);

            GridLayoutGroup grid = row.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(430f, 72f);
            grid.spacing = new Vector2(12f, 12f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.childAlignment = TextAnchor.MiddleCenter;

            CreateButton(row.transform, "开始识别", repairController.StartRecognition);
            CreateButton(row.transform, "修复前", repairController.ShowBeforeRepair);
            CreateButton(row.transform, "修复后", repairController.ShowAfterRepair);
            CreateButton(row.transform, "重新识别", repairController.ResetRecognition);
        }

        private static void CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = new GameObject($"{label} Button");
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.12f, 0.16f, 0.82f);
            Button button = buttonObject.AddComponent<Button>();
            CreateText(buttonObject.transform, $"{label} Label", label, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(236f, 70f), 23, TextAnchor.MiddleCenter);
            UnityEventTools.AddPersistentListener(button.onClick, action);
        }

        private static Text CreateText(Transform parent, string name, string value, Vector2 anchor, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
        {
            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Text text = textObject.AddComponent<Text>();
            text.text = value;
            Font chineseFont = AssetDatabase.LoadAssetAtPath<Font>(ChineseFontPath);
            text.font = chineseFont != null ? chineseFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
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

        private static void ApplyMaterialToRenderers(GameObject root, Material material)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = material;
            }
        }

        private static void CreateEventSystem()
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        private static void AssignSerializedReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignSerializedObjectArray(UnityEngine.Object target, string propertyName, UnityEngine.Object[] values)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignVector2Array(UnityEngine.Object target, string propertyName, Vector2[] values)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).vector2Value = values[i];
            }
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignVector3Array(UnityEngine.Object target, string propertyName, Vector3[] values)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).vector3Value = values[i];
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignFloatArray(UnityEngine.Object target, string propertyName, float[] values)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).floatValue = values[i];
            }
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
