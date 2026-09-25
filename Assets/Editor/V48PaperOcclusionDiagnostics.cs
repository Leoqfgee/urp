using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace Urp.ArDemo.Editor
{
    public static class V48PaperOcclusionDiagnostics
    {
        private const int Size = 512;
        private const float FarSentinel = 1000f;
        private const float EpsilonMeters = 0.0005f;
        private const string PairPath =
            "Assets/Models/CleanBottleReconstruction/BottleFullAlignedV2/"
            + "bottle_full_aligned_v2.fbx";
        private const string OutputRoot = "Artifacts/V48OcclusionQA";
        private const string ArtifactPath =
            "Assets/Calibration/paper_occlusion_qa_v48.json";
        private const string IntegrityArtifactPath =
            "Assets/Calibration/bottle_cap_asset_integrity_v48.json";
        private const string CapMaterialPath =
            "Assets/Materials/CleanBottleCapLit.mat";
        private const string CompositeMaterialPath =
            "Assets/Materials/PaperDepthComposite.mat";

        [Serializable]
        private sealed class Artifact
        {
            public string algorithm;
            public string source_geometry_b;
            public string source_geometry_c;
            public string depth_format;
            public string depth_unit;
            public float epsilon_meters;
            public string bottle_cap_c_mesh_sha256;
            public string pair_fbx_sha256;
            public int cap_vertex_count;
            public int cap_triangle_count;
            public Vector3 cap_bounds_center;
            public Vector3 cap_bounds_size;
            public Vector3 cap_local_position;
            public Quaternion cap_local_rotation;
            public Vector3 cap_local_scale;
            public float synthetic_gpu_cpu_mask_agreement;
            public ViewResult[] views;
        }

        [Serializable]
        private sealed class CapIntegrityArtifact
        {
            public string baseline_commit;
            public string pair_fbx_sha256;
            public string imported_mesh_sha256;
            public int vertex_count;
            public int triangle_count;
            public int index_count;
            public int sub_mesh_count;
            public Vector3 local_bounds_center;
            public Vector3 local_bounds_size;
            public Vector3 local_position;
            public Quaternion local_rotation;
            public Vector3 local_scale;
            public string parent_path;
            public string material_guid;
            public string material_path;
            public bool matches_v46;
        }

        [Serializable]
        public sealed class ViewResult
        {
            public string view;
            public string camera_png;
            public string b_depth_exr;
            public string b_depth_png;
            public string c_depth_exr;
            public string c_depth_png;
            public string c_color_png;
            public string direct_original_c_png;
            public string no_occlusion_composite_png;
            public string mask_png;
            public string composite_png;
            public int cap_pixel_count;
            public int visible_cap_pixel_count;
            public int b_in_front_pixel_count;
            public int c_in_front_pixel_count;
            public float visible_ratio;
            public float mask_centroid_x;
            public float mask_centroid_y;
            public float gpu_cpu_mask_agreement;
            public bool every_mask_pixel_explained_by_depth;
            public float direct_vs_paper_rgb_rms;
            public int direct_alpha_pixels;
            public int paper_alpha_pixels;
            public Vector4 direct_bounds;
            public Vector4 paper_bounds;
            public Vector2 direct_centroid;
            public Vector2 paper_centroid;
        }

        [MenuItem("URP AR/V48/Run Paper Occlusion QA")]
        public static void RunFromMenu() => RunFromCommandLine();

        public static void RunFromCommandLine()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PairPath);
            Material composite = AssetDatabase.LoadAssetAtPath<Material>(
                CompositeMaterialPath);
            Shader depthShader = Shader.Find("Hidden/URP/Paper Linear Eye Depth");
            if (prefab == null || composite == null || depthShader == null)
            {
                throw new InvalidOperationException(
                    "V48 paper-occlusion assets are not imported.");
            }
            Directory.CreateDirectory(OutputRoot);

            GameObject root = UnityEngine.Object.Instantiate(prefab);
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Transform body = Find(root.transform, "DamagedBottleB");
            Transform cap = Find(root.transform, "BottleCapC");
            if (body == null || cap == null)
            {
                throw new InvalidOperationException("B/C hierarchy is incomplete.");
            }
            Renderer[] bodyRenderers = body.GetComponentsInChildren<Renderer>(true);
            Renderer[] capRenderers = cap.GetComponentsInChildren<Renderer>(true);
            CorrectBodyWindingForRuntime(bodyRenderers);
            Bounds capBounds = CombinedBounds(capRenderers);

            Vector3[] directions =
            {
                Vector3.forward,
                Quaternion.AngleAxis(-25f, Vector3.up) * Vector3.forward,
                Quaternion.AngleAxis(25f, Vector3.up) * Vector3.forward,
                (Vector3.forward + Vector3.up * 0.70f).normalized
            };
            string[] names = { "front", "left", "right", "top" };
            List<ViewResult> views = new List<ViewResult>();
            for (int index = 0; index < names.Length; index++)
            {
                views.Add(RenderView(
                    root,
                    bodyRenderers,
                    capRenderers,
                    capBounds,
                    directions[index],
                    names[index],
                    depthShader,
                    composite));
            }

            MeshStats capStats = HashMeshes(capRenderers);
            Artifact artifact = new Artifact
            {
                algorithm =
                    "paper_3_4_1: visibleC = noB || depthC < depthB - epsilon",
                source_geometry_b = "complete DamagedBottleB hierarchy",
                source_geometry_c = "unmodified original BottleCapC",
                depth_format = "R32_SFloat",
                depth_unit = "linear camera-space metres (-viewPosition.z)",
                epsilon_meters = EpsilonMeters,
                bottle_cap_c_mesh_sha256 = capStats.sha256,
                pair_fbx_sha256 = Sha256(File.ReadAllBytes(PairPath)),
                cap_vertex_count = capStats.vertices,
                cap_triangle_count = capStats.triangles,
                cap_bounds_center = capStats.bounds.center,
                cap_bounds_size = capStats.bounds.size,
                cap_local_position = cap.localPosition,
                cap_local_rotation = cap.localRotation,
                cap_local_scale = cap.localScale,
                synthetic_gpu_cpu_mask_agreement =
                    RunSyntheticGpuCpuReference(composite),
                views = views.ToArray()
            };
            File.WriteAllText(ArtifactPath, JsonUtility.ToJson(artifact, true));
            Material capMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                CapMaterialPath);
            CapIntegrityArtifact integrity = new CapIntegrityArtifact
            {
                baseline_commit = "774f79f4d4a2f4e2cfecd4d17d01564f9c0932bc",
                pair_fbx_sha256 = artifact.pair_fbx_sha256,
                imported_mesh_sha256 = capStats.sha256,
                vertex_count = capStats.vertices,
                triangle_count = capStats.triangles,
                index_count = capStats.indices,
                sub_mesh_count = capStats.subMeshes,
                local_bounds_center = capStats.bounds.center,
                local_bounds_size = capStats.bounds.size,
                local_position = cap.localPosition,
                local_rotation = cap.localRotation,
                local_scale = cap.localScale,
                parent_path = "BottleRepairRoot/BottleCapC",
                material_guid = AssetDatabase.AssetPathToGUID(CapMaterialPath),
                material_path = CapMaterialPath,
                matches_v46 = artifact.pair_fbx_sha256
                    == "F0661ADB5E953A1DA4605A943995E251ACD4039C0397A14B99F0418319562D21"
                    && capStats.vertices == 11911
                    && capStats.triangles == 9504
                    && cap.localPosition == Vector3.zero
                    && Quaternion.Angle(cap.localRotation, Quaternion.identity) < 0.0001f
                    && cap.localScale == Vector3.one
                    && capMaterial != null
            };
            File.WriteAllText(
                IntegrityArtifactPath,
                JsonUtility.ToJson(integrity, true));
            AssetDatabase.ImportAsset(ArtifactPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(
                IntegrityArtifactPath,
                ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            UnityEngine.Object.DestroyImmediate(root);

            if (artifact.synthetic_gpu_cpu_mask_agreement < 0.99f
                || views.Any(view => view.gpu_cpu_mask_agreement < 0.99f)
                || views.Any(view => !view.every_mask_pixel_explained_by_depth))
            {
                throw new InvalidOperationException(
                    "GPU paper mask diverges from the CPU depth comparator.");
            }
            if (views.Any(view => view.direct_vs_paper_rgb_rms > 0.08f)
                || views.Any(view => view.direct_alpha_pixels != view.paper_alpha_pixels))
            {
                throw new InvalidOperationException(
                    "Paper CColorRT is not visually equivalent to direct original C.");
            }
            Debug.Log(
                "V48_PAPER_OCCLUSION_QA_OK synthetic="
                + artifact.synthetic_gpu_cpu_mask_agreement.ToString("F6")
                + " "
                + string.Join(" ", views.Select(view =>
                    $"{view.view}={view.visible_ratio:F4}/{view.gpu_cpu_mask_agreement:F6}")));
        }

        private static ViewResult RenderView(
            GameObject root,
            Renderer[] bodyRenderers,
            Renderer[] capRenderers,
            Bounds capBounds,
            Vector3 direction,
            string view,
            Shader depthShader,
            Material compositeMaterial)
        {
            string folder = $"{OutputRoot}/{view}";
            Directory.CreateDirectory(folder);
            GameObject cameraObject = new GameObject("V48OcclusionQACamera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 10f;
            camera.fieldOfView = 32f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            GameObject lightObject = new GameObject("V47OcclusionQALight");
            lightObject.hideFlags = HideFlags.HideAndDontSave;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.98f, 0.95f);
            lightObject.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
            float distance = Mathf.Max(0.09f, capBounds.extents.magnitude * 6f);
            camera.transform.position = capBounds.center + direction * distance;
            camera.transform.LookAt(capBounds.center, Vector3.up);

            SetOnlyEnabled(root, bodyRenderers);
            Texture2D bDepth = RenderDepth(camera, depthShader);
            SetOnlyEnabled(root, capRenderers);
            Texture2D cDepth = RenderDepth(camera, depthShader);
            Texture2D directColor = RenderColour(camera);
            Texture2D cColor = RenderExtractedCapColor(
                compositeMaterial,
                directColor,
                cDepth);
            Texture2D noOcclusion = RenderNoBottleComposite(
                compositeMaterial,
                cDepth,
                cColor);
            PixelStats directStats = MeasurePixels(directColor);
            PixelStats paperStats = MeasurePixels(cColor);
            float directPaperRms = RgbRms(directColor, cColor);
            Texture2D background = CreateBackground();
            Color[] b = bDepth.GetPixels();
            Color[] c = cDepth.GetPixels();
            Color32[] cap = cColor.GetPixels32();
            Color32[] bg = background.GetPixels32();
            bool[] cpuMask = new bool[Size * Size];
            Color32[] final = new Color32[cpuMask.Length];
            int capCount = 0;
            int visibleCount = 0;
            int bFront = 0;
            int cFront = 0;
            double centroidX = 0;
            double centroidY = 0;
            for (int i = 0; i < cpuMask.Length; i++)
            {
                bool capExists = cap[i].a > 0 && c[i].r < 999f;
                bool bottleExists = b[i].r < 999f;
                bool visible = capExists
                    && (!bottleExists || c[i].r < b[i].r - EpsilonMeters);
                cpuMask[i] = visible;
                final[i] = visible ? cap[i] : bg[i];
                if (capExists) capCount++;
                if (capExists && bottleExists)
                {
                    if (visible) cFront++; else bFront++;
                }
                if (visible)
                {
                    visibleCount++;
                    centroidX += i % Size;
                    centroidY += i / Size;
                }
            }

            Texture2D gpuMask = RenderGpuMask(
                compositeMaterial,
                bDepth,
                cDepth,
                cColor);
            Color32[] gpu = gpuMask.GetPixels32();
            int matches = 0;
            for (int i = 0; i < gpu.Length; i++)
            {
                bool gpuVisible = gpu[i].r > 127;
                if (gpuVisible == cpuMask[i]) matches++;
            }
            float agreement = matches / (float)gpu.Length;
            Texture2D cpuMaskImage = BoolTexture(cpuMask);
            Texture2D finalImage = new Texture2D(
                Size, Size, TextureFormat.RGBA32, false, true);
            finalImage.SetPixels32(final);
            finalImage.Apply();

            SavePng(background, $"{folder}/camera.png");
            SaveExr(bDepth, $"{folder}/B_depth.exr");
            SaveDepthPng(bDepth, $"{folder}/B_depth.png");
            SaveExr(cDepth, $"{folder}/C_depth.exr");
            SaveDepthPng(cDepth, $"{folder}/C_depth.png");
            SavePng(directColor, $"{folder}/DirectOriginalC.png");
            SavePng(cColor, $"{folder}/PaperCColorRT.png");
            SavePng(noOcclusion, $"{folder}/NoOcclusionComposite.png");
            SavePng(cpuMaskImage, $"{folder}/OcclusionMask.png");
            SavePng(finalImage, $"{folder}/FinalComposite.png");

            ViewResult result = new ViewResult
            {
                view = view,
                camera_png = $"{folder}/camera.png",
                b_depth_exr = $"{folder}/B_depth.exr",
                b_depth_png = $"{folder}/B_depth.png",
                c_depth_exr = $"{folder}/C_depth.exr",
                c_depth_png = $"{folder}/C_depth.png",
                c_color_png = $"{folder}/PaperCColorRT.png",
                direct_original_c_png = $"{folder}/DirectOriginalC.png",
                no_occlusion_composite_png = $"{folder}/NoOcclusionComposite.png",
                mask_png = $"{folder}/OcclusionMask.png",
                composite_png = $"{folder}/FinalComposite.png",
                cap_pixel_count = capCount,
                visible_cap_pixel_count = visibleCount,
                b_in_front_pixel_count = bFront,
                c_in_front_pixel_count = cFront,
                visible_ratio = capCount > 0 ? visibleCount / (float)capCount : 0f,
                mask_centroid_x = visibleCount > 0
                    ? (float)(centroidX / visibleCount) : -1f,
                mask_centroid_y = visibleCount > 0
                    ? (float)(centroidY / visibleCount) : -1f,
                gpu_cpu_mask_agreement = agreement,
                every_mask_pixel_explained_by_depth = agreement >= 0.99f,
                direct_vs_paper_rgb_rms = directPaperRms,
                direct_alpha_pixels = directStats.alphaPixels,
                paper_alpha_pixels = paperStats.alphaPixels,
                direct_bounds = directStats.bounds,
                paper_bounds = paperStats.bounds,
                direct_centroid = directStats.centroid,
                paper_centroid = paperStats.centroid
            };

            foreach (UnityEngine.Object item in new UnityEngine.Object[]
                     {
                         bDepth, cDepth, directColor, cColor, noOcclusion,
                         background, gpuMask, cpuMaskImage, finalImage,
                         lightObject, cameraObject
                     })
            {
                UnityEngine.Object.DestroyImmediate(item);
            }
            return result;
        }

        private static Texture2D RenderDepth(Camera camera, Shader shader)
        {
            RenderTexture rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.RFloat)
            {
                antiAliasing = 1,
                filterMode = FilterMode.Point
            };
            rt.Create();
            camera.targetTexture = rt;
            camera.backgroundColor = new Color(FarSentinel, 0f, 0f, 0f);
            camera.SetReplacementShader(shader, string.Empty);
            camera.Render();
            camera.ResetReplacementShader();
            Texture2D image = ReadTexture(rt, TextureFormat.RFloat, true);
            camera.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            return image;
        }

        private static Texture2D RenderColour(Camera camera)
        {
            RenderTexture rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1
            };
            rt.Create();
            camera.targetTexture = rt;
            camera.backgroundColor = Color.clear;
            camera.Render();
            Texture2D image = ReadTexture(rt, TextureFormat.RGBA32, false);
            camera.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            return image;
        }

        private static Texture2D RenderExtractedCapColor(
            Material material,
            Texture2D directColor,
            Texture2D capDepth)
        {
            RenderTexture rt = RenderTexture.GetTemporary(
                Size,
                Size,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            material.SetTexture("_PaperCDepthRT", capDepth);
            // Graphics.Blit binds its source as _MainTex, while the URP
            // Blitter path used at runtime binds _BlitTexture. Bind the same
            // source explicitly so this Editor golden test exercises the
            // exact shader input used by the renderer feature.
            material.SetTexture("_BlitTexture", directColor);
            material.SetVector("_BlitScaleBias", new Vector4(1f, 1f, 0f, 0f));
            Graphics.Blit(directColor, rt, material, 2);
            Texture2D image = ReadTexture(rt, TextureFormat.RGBA32, false);
            RenderTexture.ReleaseTemporary(rt);
            return image;
        }

        private static Texture2D RenderNoBottleComposite(
            Material material,
            Texture2D capDepth,
            Texture2D capColor)
        {
            Texture2D noBottleDepth = FloatTexture(Size, (x, y) => FarSentinel);
            RenderTexture rt = RenderTexture.GetTemporary(
                Size,
                Size,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            material.SetTexture("_PaperBDepthRT", noBottleDepth);
            material.SetTexture("_PaperCDepthRT", capDepth);
            material.SetTexture("_PaperCColorRT", capColor);
            material.SetFloat("_PaperOcclusionDepthEpsilonMeters", EpsilonMeters);
            material.SetVector("_BlitScaleBias", new Vector4(1f, 1f, 0f, 0f));
            Graphics.Blit(Texture2D.blackTexture, rt, material, 0);
            Texture2D image = ReadTexture(rt, TextureFormat.RGBA32, false);
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(noBottleDepth);
            return image;
        }

        private struct PixelStats
        {
            public int alphaPixels;
            public Vector4 bounds;
            public Vector2 centroid;
        }

        private static PixelStats MeasurePixels(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            int count = 0;
            int minX = texture.width;
            int minY = texture.height;
            int maxX = -1;
            int maxY = -1;
            double sumX = 0;
            double sumY = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                if (pixels[index].a == 0) continue;
                int x = index % texture.width;
                int y = index / texture.width;
                count++;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
                sumX += x;
                sumY += y;
            }
            return new PixelStats
            {
                alphaPixels = count,
                bounds = new Vector4(minX, minY, maxX, maxY),
                centroid = count > 0
                    ? new Vector2((float)(sumX / count), (float)(sumY / count))
                    : new Vector2(-1f, -1f)
            };
        }

        private static float RgbRms(Texture2D left, Texture2D right)
        {
            Color32[] a = left.GetPixels32();
            Color32[] b = right.GetPixels32();
            if (a.Length != b.Length) return float.PositiveInfinity;
            double sum = 0;
            for (int index = 0; index < a.Length; index++)
            {
                float dr = (a[index].r - b[index].r) / 255f;
                float dg = (a[index].g - b[index].g) / 255f;
                float db = (a[index].b - b[index].b) / 255f;
                sum += dr * dr + dg * dg + db * db;
            }
            return Mathf.Sqrt((float)(sum / (a.Length * 3.0)));
        }

        private static Texture2D RenderGpuMask(
            Material material,
            Texture2D bDepth,
            Texture2D cDepth,
            Texture2D cColor)
        {
            RenderTexture rt = RenderTexture.GetTemporary(
                Size, Size, 0, RenderTextureFormat.ARGB32);
            material.SetTexture("_PaperBDepthRT", bDepth);
            material.SetTexture("_PaperCDepthRT", cDepth);
            material.SetTexture("_PaperCColorRT", cColor);
            material.SetFloat("_PaperOcclusionDepthEpsilonMeters", EpsilonMeters);
            material.SetVector("_BlitScaleBias", new Vector4(1f, 1f, 0f, 0f));
            Graphics.Blit(Texture2D.blackTexture, rt, material, 1);
            Texture2D image = ReadTexture(rt, TextureFormat.RGBA32, false);
            RenderTexture.ReleaseTemporary(rt);
            return image;
        }

        private static float RunSyntheticGpuCpuReference(Material material)
        {
            const int syntheticSize = 64;
            // Horizontal-only pattern avoids API-specific render-target Y
            // orientation while still exercising no-B, B-in-front, and
            // C-in-front branches pixel by pixel.
            Texture2D bDepth = FloatTexture(syntheticSize, (x, y) =>
                x < 16 ? FarSentinel : 0.50f);
            Texture2D cDepth = FloatTexture(syntheticSize, (x, y) =>
                x >= 56 ? 0.52f : 0.49f);
            Texture2D cColor = new Texture2D(
                syntheticSize, syntheticSize, TextureFormat.RGBA32, false, true);
            Color[] colors = Enumerable.Repeat(
                Color.white,
                syntheticSize * syntheticSize).ToArray();
            cColor.SetPixels(colors);
            cColor.Apply();
            RenderTexture rt = RenderTexture.GetTemporary(
                syntheticSize, syntheticSize, 0, RenderTextureFormat.ARGB32);
            material.SetTexture("_PaperBDepthRT", bDepth);
            material.SetTexture("_PaperCDepthRT", cDepth);
            material.SetTexture("_PaperCColorRT", cColor);
            material.SetFloat("_PaperOcclusionDepthEpsilonMeters", EpsilonMeters);
            material.SetVector("_BlitScaleBias", new Vector4(1f, 1f, 0f, 0f));
            Graphics.Blit(Texture2D.blackTexture, rt, material, 1);
            Texture2D gpu = ReadTexture(rt, TextureFormat.RGBA32, false);
            Color32[] values = gpu.GetPixels32();
            int matches = 0;
            for (int y = 0; y < syntheticSize; y++)
            {
                for (int x = 0; x < syntheticSize; x++)
                {
                    float db = bDepth.GetPixel(x, y).r;
                    float dc = cDepth.GetPixel(x, y).r;
                    bool expected = db >= 999f
                        || dc < db - EpsilonMeters;
                    bool actual = values[y * syntheticSize + x].r > 127;
                    if (expected == actual) matches++;
                }
            }
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(bDepth);
            UnityEngine.Object.DestroyImmediate(cDepth);
            UnityEngine.Object.DestroyImmediate(cColor);
            UnityEngine.Object.DestroyImmediate(gpu);
            return matches / (float)(syntheticSize * syntheticSize);
        }

        private static Texture2D FloatTexture(
            int size,
            Func<int, int, float> value)
        {
            Texture2D texture = new Texture2D(
                size, size, TextureFormat.RFloat, false, true);
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = new Color(value(x, y), 0f, 0f, 0f);
            texture.SetPixels(pixels);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply();
            return texture;
        }

        private static Texture2D ReadTexture(
            RenderTexture rt,
            TextureFormat format,
            bool linear)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D image = new Texture2D(rt.width, rt.height, format, false, linear);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            return image;
        }

        private static Texture2D BoolTexture(bool[] values)
        {
            Texture2D image = new Texture2D(
                Size, Size, TextureFormat.RGBA32, false, true);
            Color32[] pixels = values.Select(value => value
                ? new Color32(255, 255, 255, 255)
                : new Color32(0, 0, 0, 255)).ToArray();
            image.SetPixels32(pixels);
            image.Apply();
            return image;
        }

        private static Texture2D CreateBackground()
        {
            Texture2D image = new Texture2D(
                Size, Size, TextureFormat.RGBA32, false, true);
            Color32[] pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    pixels[y * Size + x] = new Color32(
                        (byte)(35 + x * 50 / Size),
                        (byte)(45 + y * 45 / Size),
                        60,
                        255);
            image.SetPixels32(pixels);
            image.Apply();
            return image;
        }

        private static void SaveDepthPng(Texture2D depth, string path)
        {
            Color[] source = depth.GetPixels();
            float[] finite = source.Select(pixel => pixel.r)
                .Where(value => value < 999f && value > 0f).ToArray();
            float min = finite.Length > 0 ? finite.Min() : 0f;
            float max = finite.Length > 0 ? finite.Max() : 1f;
            Texture2D preview = new Texture2D(
                depth.width, depth.height, TextureFormat.RGBA32, false, true);
            Color32[] pixels = source.Select(pixel =>
            {
                if (pixel.r >= 999f) return new Color32(0, 0, 0, 255);
                byte value = (byte)Mathf.RoundToInt(
                    255f * (1f - Mathf.InverseLerp(min, max, pixel.r)));
                return new Color32(value, value, value, 255);
            }).ToArray();
            preview.SetPixels32(pixels);
            preview.Apply();
            SavePng(preview, path);
            UnityEngine.Object.DestroyImmediate(preview);
        }

        private static void SaveExr(Texture2D image, string path)
        {
            File.WriteAllBytes(path, image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static void SavePng(Texture2D image, string path)
        {
            File.WriteAllBytes(path, image.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static void SetOnlyEnabled(GameObject root, Renderer[] enabled)
        {
            HashSet<Renderer> set = new HashSet<Renderer>(enabled);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = set.Contains(renderer);
        }

        private static Bounds CombinedBounds(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
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

        private static void CorrectBodyWindingForRuntime(Renderer[] renderers)
        {
            foreach (Renderer renderer in renderers)
            {
                Mesh source = GetMesh(renderer);
                if (source == null || !source.isReadable
                    || source.normals.Length != source.vertexCount)
                    continue;
                int[] triangles = source.triangles;
                Vector3[] vertices = source.vertices;
                Vector3[] normals = source.normals;
                int agreeing = 0, opposing = 0;
                int stride = Mathf.Max(1, triangles.Length / 3000);
                for (int i = 0; i + 2 < triangles.Length; i += 3 * stride)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    Vector3 normal = normals[a] + normals[b] + normals[c];
                    if (Vector3.Dot(face, normal) < 0f) opposing++; else agreeing++;
                }
                if (opposing <= agreeing * 3) continue;
                Mesh corrected = UnityEngine.Object.Instantiate(source);
                for (int sub = 0; sub < corrected.subMeshCount; sub++)
                {
                    int[] indices = corrected.GetTriangles(sub);
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                        (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                    corrected.SetTriangles(indices, sub, false);
                }
                if (renderer is SkinnedMeshRenderer skinned) skinned.sharedMesh = corrected;
                else renderer.GetComponent<MeshFilter>().sharedMesh = corrected;
            }
        }

        private struct MeshStats
        {
            public string sha256;
            public int vertices;
            public int triangles;
            public int indices;
            public int subMeshes;
            public Bounds bounds;
        }

        private static MeshStats HashMeshes(Renderer[] renderers)
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream);
            int vertices = 0, triangles = 0, indices = 0, subMeshes = 0;
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (Renderer renderer in renderers.OrderBy(item => item.name))
            {
                Mesh mesh = GetMesh(renderer);
                if (mesh == null) continue;
                writer.Write(renderer.name);
                writer.Write(mesh.vertexCount);
                foreach (Vector3 value in mesh.vertices) Write(writer, value);
                foreach (Vector3 value in mesh.normals) Write(writer, value);
                foreach (Vector2 value in mesh.uv) { writer.Write(value.x); writer.Write(value.y); }
                writer.Write(mesh.subMeshCount);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] subIndices = mesh.GetTriangles(sub);
                    indices += subIndices.Length;
                    foreach (int index in subIndices) writer.Write(index);
                }
                vertices += mesh.vertexCount;
                triangles += mesh.triangles.Length / 3;
                subMeshes += mesh.subMeshCount;
                if (!hasBounds) { bounds = mesh.bounds; hasBounds = true; }
                else bounds.Encapsulate(mesh.bounds);
            }
            return new MeshStats
            {
                sha256 = Sha256(stream.ToArray()),
                vertices = vertices,
                triangles = triangles,
                indices = indices,
                subMeshes = subMeshes,
                bounds = bounds
            };
        }

        private static Mesh GetMesh(Renderer renderer) =>
            renderer is SkinnedMeshRenderer skinned
                ? skinned.sharedMesh
                : renderer.GetComponent<MeshFilter>()?.sharedMesh;

        private static void Write(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x); writer.Write(value.y); writer.Write(value.z);
        }

        private static string Sha256(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("X2")));
        }
    }
}
