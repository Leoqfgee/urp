using System;
using Unity.Collections;
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
            var boundary = new NativeArray<Vector2>(4, Allocator.Temp);
            try
            {
                boundary[0] = new Vector2(-.15f, -.15f);
                boundary[1] = new Vector2(.15f, -.15f);
                boundary[2] = new Vector2(.15f, .15f);
                boundary[3] = new Vector2(-.15f, .15f);
                if (Mathf.Abs(ArtifactPlaneGeometry.Area(boundary) - .09f) > .0001f)
                    throw new InvalidOperationException("Plane polygon area incorrect");
                if (!ArtifactPlaneGeometry.WithinOrNearBoundary(boundary, Vector2.zero, .06f)
                    || !ArtifactPlaneGeometry.WithinOrNearBoundary(boundary, new Vector2(.19f, 0f), .06f)
                    || ArtifactPlaneGeometry.WithinOrNearBoundary(boundary, new Vector2(.25f, 0f), .06f))
                    throw new InvalidOperationException("Plane fallback boundary margin incorrect");
            }
            finally { boundary.Dispose(); }
            EditorSceneManager.OpenScene("Assets/Scenes/ArtifactARScene.unity");
            var manager = UnityEngine.Object.FindObjectOfType<ARPlaneManager>();
            if (manager == null || (manager.requestedDetectionMode & PlaneDetectionMode.Horizontal) == 0)
                throw new InvalidOperationException("Horizontal AR plane detection unavailable");
            if (UnityEngine.Object.FindObjectOfType<ARRaycastManager>() == null
                || UnityEngine.Object.FindObjectOfType<ARAnchorManager>() == null)
                throw new InvalidOperationException("Artifact AR raycast or anchor manager unavailable");
            var material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Resources/Materials/ArtifactPlaneHint.mat");
            if (material == null || !material.shader.isSupported
                || material.renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent
                || material.GetColor("_BaseColor").a >= .3f)
                throw new InvalidOperationException("Translucent plane hint material unavailable");
            Debug.Log("ARTIFACT_PLANE_GEOMETRY_VALID area=0.09 margin=0.06 material="
                + material.shader.name);
        }
    }
}
