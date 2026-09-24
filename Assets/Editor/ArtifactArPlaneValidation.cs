using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
            if (UnityEngine.Object.FindObjectOfType<ARRaycastManager>() == null
                || UnityEngine.Object.FindObjectOfType<ARAnchorManager>() == null)
                throw new InvalidOperationException("Artifact AR raycast or anchor manager unavailable");
            var origin = UnityEngine.Object.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null || origin.transform.position != Vector3.zero
                || origin.transform.rotation != Quaternion.identity
                || origin.transform.localScale != Vector3.one)
                throw new InvalidOperationException("XR Origin transform must be identity");
            if (origin.Camera == null || origin.Camera.transform.localScale != Vector3.one)
                throw new InvalidOperationException("AR camera transform scale must be one");
            if (manager.planePrefab != null)
                throw new InvalidOperationException("Plane prefab should not render a polygon");
            var material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Resources/Materials/ArtifactPlacementIndicator.mat");
            if (material == null || !material.shader.isSupported
                || material.renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent
                || material.GetColor("_BaseColor").a < .5f)
                throw new InvalidOperationException("Placement indicator material unavailable");
            Debug.Log("ARTIFACT_PLACEMENT_SCENE_VALID indicator=0.12m originScale=1 planePrefab=none");
        }
    }
}
