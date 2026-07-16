using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using Urp.ArDemo.Native;

namespace Urp.ArDemo.Editor
{
    public static class UrpArValidation
    {
        private const string ScenePath = "Assets/Scenes/UrpARPrototype.unity";
        private const string CatalogPath = "Assets/Objects/RestorationObjectCatalog.asset";

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
            ARCameraBackground background = arCamera.GetComponent<ARCameraBackground>();
            Camera viewerCamera = FindRequired("Resource Viewer Camera").GetComponent<Camera>();
            Require(viewerCamera.targetTexture == null,
                "Viewer camera must not persist a full-screen target at scene load.");
            Require((arCamera.cullingMask & viewerCamera.cullingMask) == 0,
                "AR and viewer cameras render the same model layer.");
            Require(background != null, "AR camera background is missing.");

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
            Require(cards.All(item => ((RectTransform)item).rect.height >= 500f),
                "Object selection cards collapsed to zero height.");
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
