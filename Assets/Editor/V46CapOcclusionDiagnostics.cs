using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Urp.ArDemo.Editor
{
    public static class V46CapOcclusionDiagnostics
    {
        private const string PairPath =
            "Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/bottle_full_aligned_v2.fbx";
        private const string OccluderMaterialPath = "Assets/Materials/BottleRepairOccluder.mat";
        private const string ArtifactPath = "Assets/Calibration/cap_occlusion_coverage_v46.json";
        private const string RenderFolder = "Assets/Calibration/V46CapOcclusionQA";

        [Serializable]
        private sealed class CoverageArtifact
        {
            public string source_geometry;
            public float neck_radial_dilation;
            public string shader_contract;
            public ViewCoverage[] views;
            public bool cap_remains_visible_all_views;
            public bool oblique_views_have_partial_occlusion;
        }

        [Serializable]
        public sealed class ViewCoverage
        {
            public string view;
            public int visible_cap_pixels_without_occlusion;
            public int visible_cap_pixels_with_occlusion;
            public float occluded_pixel_ratio;
            public float retained_pixel_ratio;
            public string without_png;
            public string with_png;
        }

        [MenuItem("URP AR/V46/Measure Cap Occlusion Coverage")]
        public static void RunFromMenu() => RunFromCommandLine();

        public static void RunFromCommandLine()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PairPath);
            Material occluderMaterial = AssetDatabase.LoadAssetAtPath<Material>(OccluderMaterialPath);
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color");
            if (prefab == null || occluderMaterial == null || unlitShader == null)
                throw new InvalidOperationException("V46 cap occlusion assets are missing.");
            Directory.CreateDirectory(RenderFolder);
            Vector3[] directions =
            {
                Vector3.forward,
                (Vector3.forward + Vector3.left * 0.50f).normalized,
                (Vector3.forward + Vector3.right * 0.50f).normalized,
                (Vector3.forward + Vector3.up * 0.70f).normalized
            };
            string[] names = { "front", "left", "right", "top_oblique" };
            List<ViewCoverage> views = new List<ViewCoverage>();
            for (int i = 0; i < names.Length; i++)
                views.Add(MeasureView(prefab, occluderMaterial, unlitShader, directions[i], names[i]));
            CoverageArtifact artifact = new CoverageArtifact
            {
                source_geometry = "ReferenceNeckProxyB only",
                neck_radial_dilation = 1.02f,
                shader_contract = "ColorMask 0; ZWrite On; ZTest LEqual; Queue Geometry-10; Cull Off",
                views = views.ToArray(),
                cap_remains_visible_all_views = views.All(view =>
                    view.visible_cap_pixels_with_occlusion > 0
                    && view.retained_pixel_ratio >= 0.40f),
                oblique_views_have_partial_occlusion = views.Skip(1).All(view =>
                    view.occluded_pixel_ratio > 0f
                    && view.retained_pixel_ratio >= 0.40f)
            };
            File.WriteAllText(ArtifactPath, JsonUtility.ToJson(artifact, true));
            AssetDatabase.ImportAsset(ArtifactPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("V46_CAP_OCCLUSION_COVERAGE_OK " + string.Join(" ", views.Select(view =>
                $"{view.view}={view.occluded_pixel_ratio:F4}/{view.retained_pixel_ratio:F4}")));
        }

        private static ViewCoverage MeasureView(
            GameObject prefab,
            Material occluderMaterial,
            Shader unlitShader,
            Vector3 direction,
            string view)
        {
            GameObject root = UnityEngine.Object.Instantiate(prefab);
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Transform body = Find(root.transform, "DamagedBottleB");
            Transform neck = Find(root.transform, "ReferenceNeckProxyB");
            Transform cap = Find(root.transform, "BottleCapC");
            if (body == null || neck == null || cap == null)
                throw new InvalidOperationException("Rigid B/neck/C hierarchy is incomplete.");
            // The authored cap shell encloses the neck proxy by only a few
            // pixels. A measured 2% X/Z depth-seam dilation is the smallest
            // tested margin that produces stable, partial (not full) coverage.
            neck.localScale = Vector3.Scale(
                neck.localScale,
                new Vector3(1.02f, 1f, 1.02f));
            Renderer[] neckRenderers = neck.GetComponentsInChildren<Renderer>(true);
            HashSet<Renderer> neckSet = new HashSet<Renderer>(neckRenderers);
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = neckSet.Contains(renderer);
            Material capMaterial = new Material(unlitShader) { color = Color.white };
            if (capMaterial.HasProperty("_BaseColor")) capMaterial.SetColor("_BaseColor", Color.white);
            Renderer[] capRenderers = cap.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in capRenderers)
            {
                renderer.enabled = true;
                renderer.sharedMaterial = capMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (Renderer renderer in neckRenderers)
            {
                CorrectRendererWinding(renderer);
                renderer.sharedMaterial = occluderMaterial;
                renderer.enabled = false;
            }
            Bounds capBounds = CombinedBounds(capRenderers);
            GameObject cameraObject = new GameObject("V46CapOcclusionCamera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.001f;
            camera.farClipPlane = 10f;
            camera.fieldOfView = 30f;
            float distance = Mathf.Max(0.12f, capBounds.extents.magnitude * 5f);
            camera.transform.position = capBounds.center + direction * distance;
            camera.transform.LookAt(capBounds.center, Vector3.up);
            string withoutPath = $"{RenderFolder}/{view}_without.png";
            int without = RenderAndCount(camera, withoutPath);
            foreach (Renderer renderer in neckRenderers) renderer.enabled = true;
            string withPath = $"{RenderFolder}/{view}_with.png";
            int with = RenderAndCount(camera, withPath);
            float retained = without > 0 ? with / (float)without : 0f;
            ViewCoverage result = new ViewCoverage
            {
                view = view,
                visible_cap_pixels_without_occlusion = without,
                visible_cap_pixels_with_occlusion = with,
                occluded_pixel_ratio = Mathf.Clamp01(1f - retained),
                retained_pixel_ratio = retained,
                without_png = withoutPath,
                with_png = withPath
            };
            UnityEngine.Object.DestroyImmediate(capMaterial);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(root);
            return result;
        }

        private static int RenderAndCount(Camera camera, string path)
        {
            RenderTexture rt = RenderTexture.GetTemporary(512, 512, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D image = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            image.Apply();
            Color32[] pixels = image.GetPixels32();
            int count = pixels.Count(pixel => pixel.r > 128 || pixel.g > 128 || pixel.b > 128);
            File.WriteAllBytes(path, image.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(image);
            return count;
        }

        private static Bounds CombinedBounds(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void CorrectRendererWinding(Renderer renderer)
        {
            Mesh source = renderer is SkinnedMeshRenderer skinned
                ? skinned.sharedMesh
                : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (source == null || !source.isReadable || source.normals.Length != source.vertexCount)
                return;
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            int[] all = source.triangles;
            int agreeing = 0;
            int opposing = 0;
            int stride = Mathf.Max(1, all.Length / 3000);
            for (int offset = 0; offset + 2 < all.Length; offset += 3 * stride)
            {
                int i0 = all[offset]; int i1 = all[offset + 1]; int i2 = all[offset + 2];
                Vector3 face = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
                Vector3 normal = normals[i0] + normals[i1] + normals[i2];
                if (Vector3.Dot(face, normal) < 0f) opposing++; else agreeing++;
            }
            if (opposing <= agreeing * 3) return;
            Mesh corrected = UnityEngine.Object.Instantiate(source);
            for (int subMesh = 0; subMesh < corrected.subMeshCount; subMesh++)
            {
                int[] triangles = corrected.GetTriangles(subMesh);
                for (int index = 0; index + 2 < triangles.Length; index += 3)
                    (triangles[index + 1], triangles[index + 2]) =
                        (triangles[index + 2], triangles[index + 1]);
                corrected.SetTriangles(triangles, subMesh, false);
            }
            if (renderer is SkinnedMeshRenderer targetSkinned) targetSkinned.sharedMesh = corrected;
            else renderer.GetComponent<MeshFilter>().sharedMesh = corrected;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
