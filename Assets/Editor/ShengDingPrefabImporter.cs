using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GLTFast;
using UnityEditor;
using UnityEngine;

namespace Urp.ArDemo.Editor
{
    public static class ShengDingPrefabImporter
    {
        private const string DirectoryPath = "Assets/Models/Artifacts/ShengDing/Imported";
        private const string GlbPath = "Assets/StreamingAssets/Models/Artifacts/ShengDing/ShengDing.glb";
        private const string PrefabPath = DirectoryPath + "/ShengDingImported.prefab";

        [Serializable] private sealed class GlbManifest
        {
            public EmbeddedImage[] images;
            public BufferView[] bufferViews;
        }
        [Serializable] private sealed class EmbeddedImage { public string mimeType; public int bufferView; }
        [Serializable] private sealed class BufferView { public int byteOffset; public int byteLength; }

        private static void ExtractImages(byte[] glb)
        {
            int jsonLength = BitConverter.ToInt32(glb, 12);
            var manifest = JsonUtility.FromJson<GlbManifest>(Encoding.UTF8.GetString(glb, 20, jsonLength));
            int binaryStart = 20 + jsonLength + 8;
            for (int i = 0; i < manifest.images.Length; i++)
            {
                var image = manifest.images[i];
                var view = manifest.bufferViews[image.bufferView];
                string extension = image.mimeType == "image/jpeg" ? ".jpg" : ".png";
                var bytes = new byte[view.byteLength];
                Buffer.BlockCopy(glb, binaryStart + view.byteOffset, bytes, 0, bytes.Length);
                File.WriteAllBytes(DirectoryPath + "/Image" + i + extension, bytes);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public static void RelinkCompressedTexturesFromCommandLine()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(DirectoryPath + "/Material0.mat");
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(DirectoryPath + "/Image0.jpg");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(DirectoryPath + "/Image1.png");
            if (material == null || albedo == null || normal == null)
                throw new InvalidOperationException("Imported ShengDing material or GLB images missing");
            var normalImporter = (TextureImporter)AssetImporter.GetAtPath(DirectoryPath + "/Image1.png");
            if (normalImporter.sRGBTexture)
            {
                normalImporter.sRGBTexture = false;
                normalImporter.SaveAndReimport();
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>(DirectoryPath + "/Image1.png");
            }
            material.SetTexture("baseColorTexture", albedo);
            material.SetTexture("normalTexture", normal);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            AssetDatabase.DeleteAsset(DirectoryPath + "/Texture0.asset");
            AssetDatabase.DeleteAsset(DirectoryPath + "/Texture1.asset");
            Debug.Log("SHENGDING_TEXTURES_LINKED source embedded JPEG/PNG");
        }

        public static void ApplyUrpLitMaterialFromCommandLine()
        {
            string normalPath = DirectoryPath + "/Image1.png";
            var normalImporter = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            if (normalImporter.textureType != TextureImporterType.NormalMap)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
            }
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(DirectoryPath + "/Image0.jpg");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(DirectoryPath + "/Material0.mat");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (albedo == null || normal == null || material == null || shader == null)
                throw new InvalidOperationException("URP material prerequisites missing");
            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", albedo);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.18f);
            material.SetFloat("_Cull", 0f);
            material.EnableKeyword("_NORMALMAP");
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            Debug.Log("SHENGDING_URP_LIT_READY shader=" + shader.name);
        }

        public static void ValidateImportedPrefabFromCommandLine()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            ArtifactInfo info = AssetDatabase.LoadAssetAtPath<ArtifactInfo>(
                "Assets/Models/Artifacts/ShengDing/ShengDingInfo.asset");
            if (prefab == null || info == null || info.importedViewerPrefab != prefab)
                throw new InvalidOperationException("ArtifactInfo does not reference imported GLB prefab");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var filter = instance.GetComponentInChildren<MeshFilter>(true);
                var renderer = instance.GetComponentInChildren<Renderer>(true);
                if (filter == null || filter.sharedMesh == null || renderer == null)
                    throw new InvalidOperationException("Imported GLB mesh or renderer missing");
                Mesh mesh = filter.sharedMesh;
                if (mesh.vertexCount == 0 || mesh.uv.Length == 0 || mesh.normals.Length == 0)
                    throw new InvalidOperationException("Imported GLB geometry, UV, or normals missing");
                Material material = renderer.sharedMaterial;
                if (material == null || !material.shader.isSupported
                    || material.shader.name.Contains("InternalError"))
                    throw new InvalidOperationException("Imported GLB material/shader unsupported");
                if (material.GetTexture("_BaseMap") == null
                    || material.GetTexture("_BumpMap") == null)
                    throw new InvalidOperationException("Imported GLB texture missing");
                float height = renderer.bounds.size.y;
                if (height <= 0f || float.IsNaN(height))
                    throw new InvalidOperationException("Imported GLB height invalid");
                Debug.Log($"SHENGDING_PREFAB_VALID renderers=1 vertices={mesh.vertexCount} height={height:F4}m initialScale={info.defaultHeight / height:F6} shader={material.shader.name} albedo={material.GetTexture("_BaseMap").width}x{material.GetTexture("_BaseMap").height}");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        public static void RenderThumbnailFromCommandLine()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new InvalidOperationException("ShengDing imported prefab missing");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var cameraObject = new GameObject("ShengDing Thumbnail Camera");
            var lightObject = new GameObject("ShengDing Thumbnail Light");
            RenderTexture target = null;
            Texture2D image = null;
            RenderTexture oldActive = RenderTexture.active;
            try
            {
                Bounds bounds = instance.GetComponentInChildren<Renderer>().bounds;
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(239, 238, 232, 255);
                camera.orthographic = true;
                camera.orthographicSize = 0.47f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 10f;
                camera.transform.position = bounds.center + new Vector3(0.35f, 0.40f, -1f).normalized * 1.5f;
                camera.transform.LookAt(bounds.center);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.8f;
                light.shadows = LightShadows.None;
                lightObject.transform.rotation = Quaternion.Euler(25f, -20f, 0f);
                target = new RenderTexture(640, 640, 24, RenderTextureFormat.ARGB32);
                image = new Texture2D(640, 640, TextureFormat.RGBA32, false);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 640, 640), 0, 0);
                image.Apply();
                File.WriteAllBytes("Assets/Resources/UI/shengding_thumbnail.png", image.EncodeToPNG());
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("SHENGDING_THUMBNAIL_RENDERED 640x640 from project GLB prefab");
            }
            finally
            {
                RenderTexture.active = oldActive;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(lightObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        public static async void ImportFromCommandLine()
        {
            GameObject root = null;
            try
            {
                if (!File.Exists(GlbPath)) throw new FileNotFoundException(GlbPath);
                Directory.CreateDirectory(DirectoryPath);
                byte[] glb = File.ReadAllBytes(GlbPath);
                ExtractImages(glb);
                var importer = new GltfImport(deferAgent: new UninterruptedDeferAgent());
                if (!await importer.LoadGltfBinary(glb))
                    throw new InvalidOperationException("Source GLB import failed");
                root = new GameObject("ShengDing_Imported_From_GLB");
                if (!await importer.InstantiateMainSceneAsync(root.transform))
                    throw new InvalidOperationException("Source GLB instantiate failed");

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException("Source GLB has no Renderer");
                var meshes = new Dictionary<Mesh, Mesh>();
                int meshIndex = 0;
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null) continue;
                    if (!meshes.TryGetValue(mesh, out Mesh saved))
                    {
                        string path = DirectoryPath + "/Mesh" + meshIndex++ + ".asset";
                        AssetDatabase.CreateAsset(mesh, path);
                        saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                        meshes.Add(mesh, saved);
                    }
                    filter.sharedMesh = saved;
                }
                var textures = new Dictionary<Texture, Texture>();
                var materials = new Dictionary<Material, Material>();
                int textureIndex = 0, materialIndex = 0;
                foreach (Renderer renderer in renderers)
                {
                    Material[] rendererMaterials = renderer.sharedMaterials;
                    for (int i = 0; i < rendererMaterials.Length; i++)
                    {
                        Material material = rendererMaterials[i];
                        if (material == null) throw new InvalidOperationException("GLB material is null");
                        if (!materials.TryGetValue(material, out Material savedMaterial))
                        {
                            foreach (string property in material.GetTexturePropertyNames())
                            {
                                Texture texture = material.GetTexture(property);
                                if (texture == null) continue;
                                if (!textures.TryGetValue(texture, out Texture savedTexture))
                                {
                                    string path = DirectoryPath + "/Texture" + textureIndex++ + ".asset";
                                    AssetDatabase.CreateAsset(texture, path);
                                    savedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                                    textures.Add(texture, savedTexture);
                                }
                                material.SetTexture(property, savedTexture);
                            }
                            string materialPath = DirectoryPath + "/Material" + materialIndex++ + ".mat";
                            AssetDatabase.CreateAsset(material, materialPath);
                            savedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                            materials.Add(material, savedMaterial);
                        }
                        rendererMaterials[i] = savedMaterial;
                    }
                    renderer.sharedMaterials = rendererMaterials;
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                var info = AssetDatabase.LoadAssetAtPath<ArtifactInfo>(
                    "Assets/Models/Artifacts/ShengDing/ShengDingInfo.asset");
                if (info == null) throw new InvalidOperationException("ShengDingInfo missing");
                info.importedViewerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                EditorUtility.SetDirty(info);
                AssetDatabase.SaveAssets();
                RelinkCompressedTexturesFromCommandLine();
                ApplyUrpLitMaterialFromCommandLine();
                Debug.Log($"SHENGDING_PREFAB_OK renderers={renderers.Length} meshes={meshIndex} materials={materialIndex} textures={textureIndex}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
