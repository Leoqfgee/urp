using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Urp.ArDemo.Editor
{
    public static class V46ProductionVisualDiagnostics
    {
        private const string PairPath =
            "Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/bottle_full_aligned_v2.fbx";
        private const string TexturePath =
            "Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/Textures/bottle_full_clean_v2_albedo.png";
        private const string MaterialPath = "Assets/Materials/BottlePhotogrammetryLit.mat";
        private const string ArtifactPath = "Assets/Calibration/black_region_visual_diagnosis.json";
        private const string RenderFolder = "Assets/Calibration/V46VisualQA";

        [Serializable]
        private sealed class Diagnosis
        {
            public string target_mesh;
            public string albedo_texture;
            public string result_case;
            public string conclusion;
            public int renderer_count;
            public int triangle_count;
            public int degenerate_triangle_count;
            public int inconsistent_normal_count;
            public int inward_facing_triangle_count;
            public int black_albedo_triangle_count;
            public int suspect_triangle_count;
            public int recorded_suspect_count;
            public RenderMetric[] qa_renders;
            public TriangleDiagnosis[] suspect_triangles;
        }

        [Serializable]
        private sealed class TriangleDiagnosis
        {
            public string renderer;
            public int triangle_index;
            public int material_slot;
            public int connected_component;
            public float[] vertex0;
            public float[] vertex1;
            public float[] vertex2;
            public float[] normal0;
            public float[] normal1;
            public float[] normal2;
            public float[] uv0;
            public float[] uv1;
            public float[] uv2;
            public float[] sampled_albedo_rgb;
            public float area;
            public float face_to_vertex_normal_dot;
            public float outward_orientation_dot;
            public bool degenerate;
            public bool sampled_albedo_black;
        }

        [Serializable]
        private sealed class RenderMetric
        {
            public string view;
            public string cull_mode;
            public string png;
            public float black_pixel_ratio;
        }

        [MenuItem("URP AR/V46/Diagnose Production B Black Regions")]
        public static void RunFromMenu() => RunFromCommandLine();

        public static void RunFromCommandLine()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PairPath);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (prefab == null || texture == null || material == null)
                throw new InvalidOperationException("Production B visual assets are missing.");

            TextureImporter importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            bool wasReadable = importer != null && importer.isReadable;
            if (importer != null && !wasReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            }

            Diagnosis result = new Diagnosis
            {
                target_mesh = PairPath + "/DamagedBottleB",
                albedo_texture = TexturePath
            };
            List<TriangleDiagnosis> suspects = new List<TriangleDiagnosis>();
            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab);
                instance.hideFlags = HideFlags.HideAndDontSave;
                Transform bottle = Find(instance.transform, "DamagedBottleB")
                    ?? throw new InvalidOperationException("DamagedBottleB is missing.");
                Renderer[] renderers = bottle.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => !renderer.transform.name.Contains("BottleTrackingRegistrationProxy"))
                    .ToArray();
                result.renderer_count = renderers.Length;
                foreach (Renderer renderer in renderers)
                    DiagnoseRenderer(renderer, texture, result, suspects);

                result.suspect_triangles = suspects.Take(512).ToArray();
                result.recorded_suspect_count = result.suspect_triangles.Length;
                float blackShare = result.triangle_count > 0
                    ? result.black_albedo_triangle_count / (float)result.triangle_count
                    : 0f;
                bool geometryDominates = result.inconsistent_normal_count
                    + result.inward_facing_triangle_count > result.black_albedo_triangle_count;
                result.result_case = blackShare > 0.10f && !geometryDominates
                    ? "CASE_B_TEXTURE_BLACK"
                    : "CASE_A_RENDER_GEOMETRY_NORMALS_BACKFACE";
                result.conclusion = result.result_case.StartsWith("CASE_A")
                    ? "Sampled albedo does not explain the large black patches; triangle winding opposes the authored vertex normals and Cull Off exposes incorrectly lit backfaces. Preserve every vertex and UV, reverse only the affected visual triangle winding, then use Cull Back with the original textured production B."
                    : "A substantial share of the suspect UV samples is already black in the albedo; texture rebake or UV repair is required.";
                result.qa_renders = RenderQa(prefab, material);
            }
            finally
            {
                if (instance != null)
                    UnityEngine.Object.DestroyImmediate(instance);
                if (importer != null && importer.isReadable != wasReadable)
                {
                    importer.isReadable = wasReadable;
                    importer.SaveAndReimport();
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ArtifactPath) ?? "Assets/Calibration");
            File.WriteAllText(ArtifactPath, JsonUtility.ToJson(result, true));
            AssetDatabase.ImportAsset(ArtifactPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"V46_PRODUCTION_B_BLACK_REGION_DIAG_OK case={result.result_case} "
                + $"triangles={result.triangle_count} blackUv={result.black_albedo_triangle_count} "
                + $"inward={result.inward_facing_triangle_count} inconsistent={result.inconsistent_normal_count}");
        }

        private static void DiagnoseRenderer(
            Renderer renderer,
            Texture2D texture,
            Diagnosis result,
            List<TriangleDiagnosis> suspects)
        {
            Mesh mesh = renderer is SkinnedMeshRenderer skinned
                ? skinned.sharedMesh
                : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || !mesh.isReadable)
                return;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uv = mesh.uv;
            int[] parent = Enumerable.Range(0, vertices.Length).ToArray();
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                int[] indices = mesh.GetTriangles(slot);
                for (int offset = 0; offset + 2 < indices.Length; offset += 3)
                {
                    Union(parent, indices[offset], indices[offset + 1]);
                    Union(parent, indices[offset + 1], indices[offset + 2]);
                }
            }
            Dictionary<int, int> componentIds = new Dictionary<int, int>();
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                int[] triangles = mesh.GetTriangles(slot);
                for (int offset = 0; offset + 2 < triangles.Length; offset += 3)
                {
                    int i0 = triangles[offset];
                    int i1 = triangles[offset + 1];
                    int i2 = triangles[offset + 2];
                    Vector3 v0 = vertices[i0];
                    Vector3 v1 = vertices[i1];
                    Vector3 v2 = vertices[i2];
                    Vector3 cross = Vector3.Cross(v1 - v0, v2 - v0);
                    float area = cross.magnitude * 0.5f;
                    bool degenerate = area < 1e-10f;
                    Vector3 face = degenerate ? Vector3.zero : cross.normalized;
                    Vector3 n0 = normals.Length > i0 ? normals[i0].normalized : face;
                    Vector3 n1 = normals.Length > i1 ? normals[i1].normalized : face;
                    Vector3 n2 = normals.Length > i2 ? normals[i2].normalized : face;
                    Vector3 meanNormal = (n0 + n1 + n2).normalized;
                    float normalDot = degenerate ? 1f : Vector3.Dot(face, meanNormal);
                    Vector3 centroid = (v0 + v1 + v2) / 3f;
                    float outwardDot = degenerate ? 0f : Vector3.Dot(
                        face,
                        (centroid - mesh.bounds.center).normalized);
                    Vector2 t0 = uv.Length > i0 ? uv[i0] : Vector2.zero;
                    Vector2 t1 = uv.Length > i1 ? uv[i1] : Vector2.zero;
                    Vector2 t2 = uv.Length > i2 ? uv[i2] : Vector2.zero;
                    Vector2 centerUv = (t0 + t1 + t2) / 3f;
                    Color sampled = texture.GetPixelBilinear(
                        Mathf.Repeat(centerUv.x, 1f),
                        Mathf.Repeat(centerUv.y, 1f));
                    bool black = sampled.linear.grayscale < 0.025f;
                    bool inconsistent = normalDot < 0.25f;
                    bool inward = outwardDot < -0.20f;
                    result.triangle_count++;
                    if (degenerate) result.degenerate_triangle_count++;
                    if (inconsistent) result.inconsistent_normal_count++;
                    if (inward) result.inward_facing_triangle_count++;
                    if (black) result.black_albedo_triangle_count++;
                    bool suspect = degenerate || inconsistent || inward || black;
                    if (!suspect)
                        continue;
                    result.suspect_triangle_count++;
                    int root = FindRoot(parent, i0);
                    if (!componentIds.TryGetValue(root, out int component))
                    {
                        component = componentIds.Count;
                        componentIds[root] = component;
                    }
                    suspects.Add(new TriangleDiagnosis
                    {
                        renderer = renderer.name,
                        triangle_index = result.triangle_count - 1,
                        material_slot = slot,
                        connected_component = component,
                        vertex0 = V3(v0), vertex1 = V3(v1), vertex2 = V3(v2),
                        normal0 = V3(n0), normal1 = V3(n1), normal2 = V3(n2),
                        uv0 = V2(t0), uv1 = V2(t1), uv2 = V2(t2),
                        sampled_albedo_rgb = new[] { sampled.r, sampled.g, sampled.b },
                        area = area,
                        face_to_vertex_normal_dot = normalDot,
                        outward_orientation_dot = outwardDot,
                        degenerate = degenerate,
                        sampled_albedo_black = black
                    });
                }
            }
        }

        private static int FindRoot(int[] parent, int value)
        {
            while (parent[value] != value)
            {
                parent[value] = parent[parent[value]];
                value = parent[value];
            }
            return value;
        }

        private static void Union(int[] parent, int first, int second)
        {
            int a = FindRoot(parent, first);
            int b = FindRoot(parent, second);
            if (a != b) parent[b] = a;
        }

        private static RenderMetric[] RenderQa(GameObject prefab, Material sourceMaterial)
        {
            Directory.CreateDirectory(RenderFolder);
            List<RenderMetric> metrics = new List<RenderMetric>();
            Vector3[] directions =
            {
                Vector3.forward,
                (Vector3.forward + Vector3.left * 0.55f).normalized,
                (Vector3.forward + Vector3.right * 0.55f).normalized
            };
            string[] names = { "front", "left", "right" };
            foreach (CullMode cull in new[] { CullMode.Off, CullMode.Back })
            for (int i = 0; i < directions.Length; i++)
                metrics.Add(RenderOne(prefab, sourceMaterial, directions[i], names[i], cull));
            return metrics.ToArray();
        }

        private static RenderMetric RenderOne(
            GameObject prefab,
            Material sourceMaterial,
            Vector3 direction,
            string view,
            CullMode cull)
        {
            GameObject root = UnityEngine.Object.Instantiate(prefab);
            root.hideFlags = HideFlags.HideAndDontSave;
            // Match the fixed FBX import-axis cancellation used by the v44
            // runtime hierarchy so +Y is bottle-up and +Z is printed-front.
            root.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Transform bottle = Find(root.transform, "DamagedBottleB");
            Transform cap = Find(root.transform, "BottleCapC");
            Transform proxy = Find(root.transform, "BottleTrackingRegistrationProxy");
            if (cap != null) cap.gameObject.SetActive(false);
            if (proxy != null) proxy.gameObject.SetActive(false);
            Material material = new Material(sourceMaterial);
            material.SetFloat("_Cull", (float)cull);
            Renderer[] bottleRenderers = bottle.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in bottleRenderers)
            {
                if (cull == CullMode.Back)
                    CorrectRendererWinding(renderer);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            Bounds bounds = CombinedBounds(bottleRenderers);
            GameObject cameraObject = new GameObject("V46VisualQACamera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            camera.fieldOfView = 35f;
            camera.nearClipPlane = 0.001f;
            float distance = bounds.extents.magnitude / Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.45f);
            camera.transform.position = bounds.center + direction * distance;
            camera.transform.LookAt(bounds.center, Vector3.up);
            GameObject lightObject = new GameObject("V46VisualQALight");
            lightObject.hideFlags = HideFlags.HideAndDontSave;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            light.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
            AmbientMode oldAmbientMode = RenderSettings.ambientMode;
            Color oldAmbientLight = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f, 1f);
            RenderTexture rt = RenderTexture.GetTemporary(512, 512, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D image = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            image.Apply();
            Color32[] pixels = image.GetPixels32();
            int black = pixels.Count(pixel => pixel.r < 10 && pixel.g < 10 && pixel.b < 10);
            string mode = cull == CullMode.Back ? "cull_back_corrected" : "cull_off";
            string path = $"{RenderFolder}/{view}_{mode}.png";
            File.WriteAllBytes(path, image.EncodeToPNG());
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            RenderSettings.ambientMode = oldAmbientMode;
            RenderSettings.ambientLight = oldAmbientLight;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(lightObject);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return new RenderMetric
            {
                view = view,
                cull_mode = mode,
                png = path,
                black_pixel_ratio = black / (float)pixels.Length
            };
        }

        private static void CorrectRendererWinding(Renderer renderer)
        {
            Mesh source = renderer is SkinnedMeshRenderer skinned
                ? skinned.sharedMesh
                : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (source == null || !source.isReadable)
                return;
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            if (normals.Length != vertices.Length)
                return;
            int agreeing = 0;
            int opposing = 0;
            int[] all = source.triangles;
            int stride = Mathf.Max(1, all.Length / 3000);
            for (int offset = 0; offset + 2 < all.Length; offset += 3 * stride)
            {
                int i0 = all[offset]; int i1 = all[offset + 1]; int i2 = all[offset + 2];
                Vector3 face = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
                Vector3 normal = normals[i0] + normals[i1] + normals[i2];
                if (Vector3.Dot(face, normal) < 0f) opposing++; else agreeing++;
            }
            if (opposing <= agreeing * 3)
                return;
            Mesh corrected = UnityEngine.Object.Instantiate(source);
            for (int subMesh = 0; subMesh < corrected.subMeshCount; subMesh++)
            {
                int[] triangles = corrected.GetTriangles(subMesh);
                for (int index = 0; index + 2 < triangles.Length; index += 3)
                    (triangles[index + 1], triangles[index + 2]) =
                        (triangles[index + 2], triangles[index + 1]);
                corrected.SetTriangles(triangles, subMesh, false);
            }
            if (renderer is SkinnedMeshRenderer targetSkinned)
                targetSkinned.sharedMesh = corrected;
            else
                renderer.GetComponent<MeshFilter>().sharedMesh = corrected;
        }

        private static Bounds CombinedBounds(Renderer[] renderers)
        {
            Bounds result = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) result.Encapsulate(renderers[i].bounds);
            return result;
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

        private static float[] V3(Vector3 value) => new[] { value.x, value.y, value.z };
        private static float[] V2(Vector2 value) => new[] { value.x, value.y };
    }
}
