using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Urp.ArDemo.Editor
{
    public static class V51CapCullingDiagnostics
    {
        private const string PairPath = "Assets/Models/CleanBottleReconstruction/"
            + "BottleFullAlignedV2/bottle_full_aligned_v2.fbx";
        private const string OutputRoot = "Assets/Calibration/V51CapCullingQA";

        [Serializable]
        private sealed class ViewResult
        {
            public string view;
            public int visible_cap_pixels_before;
            public int visible_cap_pixels_after;
            public float occluded_ratio;
            public int cull_off_pixels;
            public int cull_back_pixels;
            public float cull_back_retained_ratio;
            public string cull_off_image;
            public string cull_back_image;
            public string bottle_depth_visualization;
            public string after_depth_test;
        }

        [Serializable]
        private sealed class Artifact
        {
            public string algorithm;
            public string production_path;
            public string geometry_b;
            public string geometry_c;
            public string render_order;
            public ViewResult[] views;
            public bool left_right_masks_differ;
            public bool full_cap_survives_without_b_depth;
            public float cap_normal_winding_consistency;
            public bool production_cull_back;
            public string top25_zero_reason;
        }

        [MenuItem("URP AR/V51/Run Cap Culling + Elevation Sweep QA")]
        public static void RunFromCommandLine()
        {
            Directory.CreateDirectory(OutputRoot);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PairPath);
            Material bottleMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/BottlePhotogrammetryLit.mat");
            Material capMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/CleanBottleCapLit.mat");
            if (prefab == null || bottleMaterial == null || capMaterial == null)
                throw new InvalidOperationException("V51 QA assets are missing.");

            Vector3[] directions =
            {
                Vector3.forward,
                (Vector3.forward + Vector3.left * 0.47f).normalized,
                (Vector3.forward + Vector3.right * 0.47f).normalized,
                Elevation(10f),
                Elevation(20f),
                Elevation(25f),
                Elevation(30f),
                Elevation(40f),
                Elevation(50f),
                Elevation(-25f)
            };
            string[] names =
            {
                "front", "left25", "right25", "top10", "top20", "top25",
                "top30", "top40", "top50", "bottom_oblique25"
            };
            List<ViewResult> results = new List<ViewResult>();
            List<bool[]> masks = new List<bool[]>();
            for (int index = 0; index < names.Length; index++)
                results.Add(RenderView(
                    prefab,
                    bottleMaterial,
                    capMaterial,
                    directions[index],
                    names[index],
                    masks));

            float normalConsistency = MeasureCapNormalConsistency(prefab);
            ViewResult top25 = results.First(result => result.view == "top25");
            Artifact artifact = new Artifact
            {
                algorithm = "complete B writes Main Camera depth; original C uses normal URP ForwardLit and standard ZTest",
                production_path = "no CColorRT, no blit, no fullscreen cap composite",
                geometry_b = "the one complete DamagedBottleB plus authored ReferenceNeckProxyB hierarchy",
                geometry_c = "the original unchanged BottleCapC MeshRenderer",
                render_order = "AR background -> B depth at BeforeRenderingOpaques -> C ForwardLit -> UI",
                views = results.ToArray(),
                left_right_masks_differ = masks.Count >= 3
                    && masks[1].Zip(masks[2], (left, right) => left != right).Any(value => value),
                full_cap_survives_without_b_depth = results.All(result =>
                    result.visible_cap_pixels_before > 100),
                cap_normal_winding_consistency = normalConsistency,
                production_cull_back = normalConsistency > 0.98f
                    && results.All(result => result.cull_back_pixels > 100),
                top25_zero_reason = top25.occluded_ratio <= 0.001f
                    ? "measured B/C geometry has no B surface in front of outward-facing C pixels at this exact elevation"
                    : "top25 has non-zero depth occlusion in the current geometry"
            };
            File.WriteAllText(
                "Assets/Calibration/v51_cap_culling_and_top_sweep_qa.json",
                JsonUtility.ToJson(artifact, true));
            AssetDatabase.Refresh();
            if (!artifact.left_right_masks_differ
                || !artifact.full_cap_survives_without_b_depth
                || results.Any(result => result.visible_cap_pixels_after <= 0))
                throw new InvalidOperationException(
                    "V51 main-depth QA did not preserve a visible cap in every view.");
        }

        private static ViewResult RenderView(
            GameObject prefab,
            Material bottleMaterial,
            Material capMaterial,
            Vector3 direction,
            string view,
            List<bool[]> masks)
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Transform bottle = Find(instance.transform, "DamagedBottleB");
            Transform neck = Find(instance.transform, "ReferenceNeckProxyB");
            Transform cap = Find(instance.transform, "BottleCapC");
            Transform trackingProxy = Find(instance.transform, "BottleTrackingRegistrationProxy");
            if (trackingProxy != null) trackingProxy.gameObject.SetActive(false);
            Renderer[] bottleRenderers = Merge(
                bottle.GetComponentsInChildren<Renderer>(true),
                neck != null ? neck.GetComponentsInChildren<Renderer>(true) : null);
            Renderer[] capRenderers = cap.GetComponentsInChildren<Renderer>(true);
            Apply(bottleRenderers, bottleMaterial);
            Apply(capRenderers, capMaterial);
            Bounds bounds = CombinedBounds(bottleRenderers.Concat(capRenderers));

            GameObject cameraObject = new GameObject("V51CapCullingCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.16f, 0.20f, 1f);
            camera.fieldOfView = 38f;
            camera.nearClipPlane = 0.001f;
            camera.farClipPlane = 10f;
            camera.transform.position = bounds.center + direction
                * (bounds.extents.magnitude / Mathf.Tan(17f * Mathf.Deg2Rad));
            camera.transform.LookAt(bounds.center, Vector3.up);
            RenderTexture target = new RenderTexture(720, 1280, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            GameObject lightObject = new GameObject("V51CapCullingLight");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);

            Material cullOff = new Material(capMaterial) { hideFlags = HideFlags.HideAndDontSave };
            Material cullBack = new Material(capMaterial) { hideFlags = HideFlags.HideAndDontSave };
            cullOff.SetFloat("_Cull", 0f);
            cullOff.doubleSidedGI = true;
            cullBack.SetFloat("_Cull", 2f);
            cullBack.doubleSidedGI = false;
            foreach (Renderer renderer in bottleRenderers) renderer.enabled = false;
            Apply(capRenderers, cullOff);
            camera.Render();
            Texture2D cullOffImage = Read(target);
            bool[] cullOffMask = CapMask(cullOffImage.GetPixels32());

            Apply(capRenderers, cullBack);
            camera.Render();
            Texture2D before = Read(target);
            bool[] beforeMask = CapMask(before.GetPixels32());

            GameObject owner = new GameObject("V51RegistryOwner");
            PaperOcclusionRegistry.Bind(owner, camera, bottleRenderers, capRenderers, 0.0005f);
            PaperOcclusionRegistry.Enable(owner);
            camera.Render();
            Texture2D after = Read(target);
            bool[] afterMask = CapMask(after.GetPixels32());
            masks.Add(afterMask);
            PaperOcclusionRegistry.Unbind(owner);

            string folder = $"{OutputRoot}/{view}";
            Directory.CreateDirectory(folder);
            string cullOffPath = $"{folder}/CapCullOff.png";
            string beforePath = $"{folder}/CapCullBack.png";
            string afterPath = $"{folder}/AfterDepthTest.png";
            File.WriteAllBytes(cullOffPath, cullOffImage.EncodeToPNG());
            File.WriteAllBytes(beforePath, before.EncodeToPNG());
            File.WriteAllBytes(afterPath, after.EncodeToPNG());

            foreach (Renderer renderer in capRenderers) renderer.enabled = false;
            foreach (Renderer renderer in bottleRenderers)
            {
                renderer.enabled = true;
                renderer.sharedMaterial = bottleMaterial;
            }
            camera.Render();
            Texture2D bottleVisual = Read(target);
            string depthPath = $"{folder}/BDepthVisualization.png";
            File.WriteAllBytes(depthPath, bottleVisual.EncodeToPNG());

            int beforePixels = beforeMask.Count(value => value);
            int afterPixels = afterMask.Count(value => value);
            ViewResult result = new ViewResult
            {
                view = view,
                visible_cap_pixels_before = beforePixels,
                visible_cap_pixels_after = afterPixels,
                occluded_ratio = beforePixels > 0
                    ? 1f - afterPixels / (float)beforePixels
                    : 0f,
                cull_off_pixels = cullOffMask.Count(value => value),
                cull_back_pixels = beforePixels,
                cull_back_retained_ratio = cullOffMask.Count(value => value) > 0
                    ? beforePixels / (float)cullOffMask.Count(value => value)
                    : 0f,
                cull_off_image = cullOffPath,
                cull_back_image = beforePath,
                bottle_depth_visualization = depthPath,
                after_depth_test = afterPath
            };
            UnityEngine.Object.DestroyImmediate(before);
            UnityEngine.Object.DestroyImmediate(cullOffImage);
            UnityEngine.Object.DestroyImmediate(after);
            UnityEngine.Object.DestroyImmediate(bottleVisual);
            UnityEngine.Object.DestroyImmediate(owner);
            UnityEngine.Object.DestroyImmediate(lightObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(instance);
            UnityEngine.Object.DestroyImmediate(cullOff);
            UnityEngine.Object.DestroyImmediate(cullBack);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            return result;
        }

        private static Vector3 Elevation(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(0f, Mathf.Sin(radians), Mathf.Cos(radians)).normalized;
        }

        private static float MeasureCapNormalConsistency(GameObject prefab)
        {
            Transform cap = Find(prefab.transform, "BottleCapC");
            int compared = 0;
            int consistent = 0;
            foreach (MeshFilter filter in cap.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || mesh.normals == null || mesh.normals.Length != mesh.vertexCount)
                    continue;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    int[] triangles = mesh.GetTriangles(subMesh);
                    for (int i = 0; i + 2 < triangles.Length; i += 3)
                    {
                        int a = triangles[i];
                        int b = triangles[i + 1];
                        int c = triangles[i + 2];
                        Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                        if (face.sqrMagnitude < 1e-16f) continue;
                        Vector3 average = normals[a] + normals[b] + normals[c];
                        if (average.sqrMagnitude < 1e-16f) continue;
                        compared++;
                        if (Vector3.Dot(face, average) > 0f) consistent++;
                    }
                }
            }
            return compared > 0 ? consistent / (float)compared : 0f;
        }

        private static bool[] CapMask(Color32[] pixels) => pixels.Select(pixel =>
            pixel.r > 120 && pixel.g > 120 && pixel.b > 110
            && Mathf.Abs(pixel.r - pixel.g) < 35
            && Mathf.Abs(pixel.g - pixel.b) < 45).ToArray();

        private static Texture2D Read(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            return image;
        }

        private static Renderer[] Merge(Renderer[] first, Renderer[] second) =>
            (first ?? Array.Empty<Renderer>()).Concat(second ?? Array.Empty<Renderer>())
            .Where(renderer => renderer != null).Distinct().ToArray();

        private static void Apply(Renderer[] renderers, Material material)
        {
            foreach (Renderer renderer in renderers)
            {
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private static Bounds CombinedBounds(IEnumerable<Renderer> renderers)
        {
            Renderer[] values = renderers.Where(renderer => renderer != null).ToArray();
            Bounds bounds = values[0].bounds;
            foreach (Renderer renderer in values.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
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
