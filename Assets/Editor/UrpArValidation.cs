using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
            Require(Calibration.RepairPoseMath.TryGetObjectPose(
                identity, 0, camera, profile, out Vector3 position, out _),
                "PnP pose conversion failed.");
            Require(Vector3.Distance(position, new Vector3(0.2f, 0.3f, 2f)) < 0.0001f,
                $"Full PnP translation was not preserved: {position}");
            UnityEngine.Object.DestroyImmediate(profile);
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
                        < 0.000001f,
                "Bottle canonical physical calibration is incomplete.");
            Require(bottle.physicalMeasurements.Length == 3
                    && bottle.physicalMeasurements.All(item => item.verified),
                "Bottle measurements are not recorded in the profile.");
            Require(bottle.defaultViewerEuler == Vector3.zero,
                "Bottle viewer must open in an upright front view.");
            Require(AssetDatabase.GetAssetPath(bottle.damagedViewerPrefab)
                        == CleanBottleFolder + "bottle_damaged_clean.obj"
                    && AssetDatabase.GetAssetPath(bottle.completeViewerPrefab)
                        == CleanBottleFolder + "bottle_complete_clean.obj"
                    && AssetDatabase.GetAssetPath(bottle.registeredRepairPrefab)
                        == CleanBottleFolder + "bottle_cap_clean.obj",
                "Bottle profile is not using all three clean reconstructed models.");
            ValidateCleanBottleGeometry();
            Require(tissue != null && tissue.damagedViewerPrefab != null,
                "Tissue viewer profile is incomplete.");
            Require(tissue.orbModelDatabase == null && tissue.registeredRepairPrefab == null,
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
                new Vector3(0.40f, 1.20f, 0.40f), 0.012f, 4800, 9000);
            ValidateObjBounds(CleanBottleFolder + "bottle_complete_clean.obj",
                new Vector3(0.40f, 1.2088235f, 0.40f), 0.015f, 7000, 13500);
            ValidateObjBounds(CleanBottleFolder + "bottle_cap_clean.obj",
                new Vector3(0.2294118f, 0.0588235f, 0.2294118f), 0.004f, 2000, 4000);
        }

        private static void ValidateObjBounds(string path, Vector3 expectedSize,
            float tolerance, int minimumVertices, int minimumFaces)
        {
            Require(File.Exists(path), $"Clean model is missing: {path}");
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
                $"Clean model is unexpectedly sparse: {path}");
            Require(Mathf.Abs(size.x - expectedSize.x) <= tolerance
                    && Mathf.Abs(size.y - expectedSize.y) <= tolerance
                    && Mathf.Abs(size.z - expectedSize.z) <= tolerance,
                $"Clean model bounds do not match the measured envelope: {path}, {size}");
            Require(Mathf.Max(size.x, size.z) < 0.45f && size.y < 1.30f,
                $"Clean model contains out-of-envelope background geometry: {path}");
        }

        private static void ValidateGeneratedScene()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RestorationObjectCatalog catalog =
                AssetDatabase.LoadAssetAtPath<RestorationObjectCatalog>(CatalogPath);
            Require(catalog != null, "Restoration object catalog is missing.");
            Transform objectRoot = FindRequired("TrackedObjectPoseRoot");
            Transform alignment = FindRequired("ModelCoordinateAlignment");
            Require(alignment.IsChildOf(objectRoot), "Model alignment is outside pose root.");

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
            Require(arSession != null && !arSession.enabled,
                "AR Session must stay disabled outside tracking mode at scene load.");
            Require(cameraManager != null && !cameraManager.enabled
                    && !background.enabled && !arCamera.enabled,
                "AR camera components must stay disabled outside tracking mode.");
            UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                "Assets/Settings/UrpMobileRenderer.asset");
            Require(renderer != null && renderer.rendererFeatures
                    .OfType<ARBackgroundRendererFeature>().Any(),
                "URP renderer is missing the AR camera background feature.");

            UrpAppController app =
                UnityEngine.Object.FindObjectOfType<UrpAppController>(true);
            Require(app != null, "Application controller is missing.");
            MethodInfo build = typeof(UrpAppController).GetMethod(
                "BuildInterface", BindingFlags.Instance | BindingFlags.NonPublic);
            build?.Invoke(app, null);
            Transform ui = FindRequired("URP Application UI");
            Require(FindChild(ui, "FullScreenBackground") != null,
                "Full-screen background is missing.");
            Transform safeArea = FindChild(ui, "SafeArea");
            Require(safeArea != null, "SafeArea is missing.");
            Require(FindChild(ui, "ModalLayer") != null, "ModalLayer is missing.");
            foreach (string page in new[]
                     {
                         "HomePageContent", "ObjectSelectionPageContent",
                         "ResourcePageContent", "TrackingPageContent"
                     })
                Require(FindChild(safeArea, page) != null, $"Page is missing: {page}");
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
