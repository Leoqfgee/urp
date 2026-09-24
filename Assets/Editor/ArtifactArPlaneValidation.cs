using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Urp.ArDemo.Editor
{
    public static class ArtifactArPlaneValidation
    {
        public static void RunFromCommandLine()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/ArtifactARScene.unity");
            var manager = UnityEngine.Object.FindObjectOfType<ARPlaneManager>();
            if (manager == null || (manager.requestedDetectionMode & PlaneDetectionMode.Horizontal) == 0)
                throw new InvalidOperationException("Horizontal AR plane detection unavailable");
            var raycasts = UnityEngine.Object.FindObjectOfType<ARRaycastManager>();
            var anchors = UnityEngine.Object.FindObjectOfType<ARAnchorManager>();
            if (raycasts == null || anchors == null || UnityEngine.Object.FindObjectOfType<ARSession>() == null)
                throw new InvalidOperationException("Artifact AR raycast or anchor manager unavailable");
            var controller = UnityEngine.Object.FindObjectOfType<ArtifactARController>();
            if (controller == null) throw new InvalidOperationException("Artifact AR controller unavailable");
            var wiring = new SerializedObject(controller);
            if (wiring.FindProperty("raycastManager").objectReferenceValue != raycasts
                || wiring.FindProperty("planeManager").objectReferenceValue != manager
                || wiring.FindProperty("anchorManager").objectReferenceValue != anchors
                || wiring.FindProperty("chineseFont").objectReferenceValue == null)
                throw new InvalidOperationException("Artifact AR scene references invalid");
            var info = wiring.FindProperty("artifact").objectReferenceValue as ArtifactInfo;
            if (info == null || info.importedViewerPrefab == null || Mathf.Abs(info.defaultHeight - .22f) > .0001f)
                throw new InvalidOperationException("ShengDing profile or prefab invalid");
            var origin = UnityEngine.Object.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null || origin.transform.position != Vector3.zero
                || origin.transform.rotation != Quaternion.identity
                || origin.transform.localScale != Vector3.one)
                throw new InvalidOperationException("XR Origin transform must be identity");
            if (origin.Camera == null || origin.Camera.transform.localScale != Vector3.one
                || origin.Camera.rect != new Rect(0f, 0f, 1f, 1f)
                || origin.Camera.GetComponent<ARCameraManager>() == null
                || origin.Camera.GetComponent<ARCameraBackground>() == null
                || origin.Camera.GetComponent<TrackedPoseDriver>() == null
                || origin.Camera.transform.parent != origin.CameraFloorOffsetObject.transform
                || origin.CameraFloorOffsetObject.transform.localScale != Vector3.one)
                throw new InvalidOperationException("AR camera transform scale must be one");
            if (manager.planePrefab != null)
                throw new InvalidOperationException("Plane prefab should not render a polygon");
            var material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Resources/Materials/ArtifactPlacementIndicator.mat");
            if (material == null || !material.shader.isSupported
                || material.renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent
                || material.GetColor("_BaseColor").a < .5f)
                throw new InvalidOperationException("Placement indicator material unavailable");
            ValidateModelGeometry(info);
            Debug.Log("ARTIFACT_PLACEMENT_SCENE_VALID indicator=0.12m originScale=1 planePrefab=none");
        }

        private static void ValidateModelGeometry(ArtifactInfo info)
        {
            GameObject anchor = new GameObject("Simulated ARAnchor Container");
            GameObject root = new GameObject("ShengDing_AR_Root");
            GameObject visual = new GameObject("ShengDing_Model");
            try
            {
                visual.transform.SetParent(root.transform, false);
                GameObject imported = UnityEngine.Object.Instantiate(info.importedViewerPrefab, visual.transform);
                Transform conversion = imported.transform.Find("Sketchfab_model");
                if (conversion == null || Mathf.Abs(conversion.localScale.x - conversion.localScale.y) > .0001f
                    || Mathf.Abs(conversion.localScale.y - conversion.localScale.z) > .0001f)
                    throw new InvalidOperationException("Imported axis conversion or scale invalid");
                MeshRenderer renderer = imported.GetComponentInChildren<MeshRenderer>();
                if (renderer == null || renderer.sharedMaterial == null
                    || renderer.sharedMaterial.GetTexture("_BaseMap") == null)
                    throw new InvalidOperationException("Original bronze material/texture missing");
                Bounds originalRendererBounds = renderer.bounds;
                if (!ArtifactMeshGeometry.TryMeasure(visual.transform, out var original)
                    || original.vertexCount < 1000 || original.supportVertexCount < 3)
                    throw new InvalidOperationException("Imported mesh feet geometry invalid");
                float scale = .22f / original.bounds.size.y;
                visual.transform.localPosition = new Vector3(
                    -original.bounds.center.x, 0f, -original.bounds.center.z);
                root.transform.localScale = Vector3.one * scale;
                if (!ArtifactMeshGeometry.TryMeasure(root.transform, out var scaled))
                    throw new InvalidOperationException("Scaled mesh geometry invalid");
                float correction = -scaled.bounds.min.y;
                visual.transform.localPosition += Vector3.up * correction;
                anchor.transform.position = new Vector3(1.1f, .75f, 2f);
                root.transform.SetParent(anchor.transform, false);
                root.transform.localRotation = Quaternion.Euler(0f, 70f, 0f);
                if (!ArtifactMeshGeometry.TryMeasure(anchor.transform, out var placed)
                    || Mathf.Abs(placed.bounds.size.y - .22f) > .001f
                    || Mathf.Abs(placed.bounds.min.y) > .001f
                    || Mathf.Abs(placed.lowestPoint.y) > .001f)
                    throw new InvalidOperationException("Ding height or foot-to-plane alignment invalid");
                root.transform.localScale = Vector3.one * (scale * (0.30f / .22f));
                root.transform.localRotation = Quaternion.Euler(0f, 145f, 0f);
                if (!ArtifactMeshGeometry.TryMeasure(anchor.transform, out var resized)
                    || Mathf.Abs(resized.bounds.size.y - .30f) > .001f
                    || Mathf.Abs(resized.bounds.min.y) > .001f)
                    throw new InvalidOperationException("User yaw/scale changed the foot alignment");
                Debug.Log($"ARTIFACT_GEOMETRY_VALID vertices={original.vertexCount} "
                    + $"supports={original.supportVertexCount} originalHeight={original.bounds.size.y:F5}m "
                    + $"rendererBoxHeight={originalRendererBounds.size.y:F5}m "
                    + $"scale={scale:F6} localGroundCorrection={correction:F5}m "
                    + $"worldGroundCorrection={correction * scale:F5}m "
                    + $"importedRotation={conversion.localRotation.eulerAngles} "
                    + $"importedScale={conversion.localScale}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(anchor);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
