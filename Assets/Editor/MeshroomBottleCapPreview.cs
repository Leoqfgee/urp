using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Urp.ArDemo.Editor
{
    public static class MeshroomBottleCapPreview
    {
        private const string ModelFolder = "Assets/Models/MeshroomBottleCap";
        private const string ModelPath = ModelFolder + "/texturedMesh.obj";
        private const string ScenePath = "Assets/Scenes/MeshroomBottleCapPreview.unity";
        private const string PreviewPath = "Builds/meshroom-bottle-cap-unity-preview.png";
        private const string ReportPath = "Builds/meshroom-bottle-cap-unity-report.txt";

        public static void GenerateFromCommandLine()
        {
            Directory.CreateDirectory("Builds");
            AssetDatabase.ImportAsset(ModelFolder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException($"Unity could not import model: {ModelPath}");
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            model.name = "Meshroom Bottle Cap Reconstruction";
            model.transform.position = Vector3.zero;
            model.transform.rotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            Bounds bounds = CalculateBounds(model);
            int originalVertices = 0;
            int originalTriangles = 0;
            CountMesh(model, out originalVertices, out originalTriangles);
            float cropY = bounds.min.y + Mathf.Max(bounds.size.y * 0.08f, 0.01f);
            int removedTriangles = CropBelowWorldY(model, cropY);
            model.transform.position -= bounds.center;
            bounds = CalculateBounds(model);

            int vertices = 0;
            int triangles = 0;
            CountMesh(model, out vertices, out triangles);

            Material previewMaterial = new Material(Shader.Find("Standard"));
            previewMaterial.color = new Color(0.86f, 0.84f, 0.78f, 1f);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = previewMaterial;
            }

            Light key = new GameObject("Preview Key Light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.7f;
            key.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

            Light fill = new GameObject("Preview Fill Light").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.7f;
            fill.transform.rotation = Quaternion.Euler(18f, 120f, 0f);

            Camera camera = new GameObject("Preview Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.14f, 0.14f, 0.14f, 1f);
            camera.fieldOfView = 32f;

            float radius = Mathf.Max(bounds.extents.magnitude, 0.1f);
            camera.transform.position = bounds.center + new Vector3(radius * 0.65f, radius * 0.55f, -radius * 3.8f);
            camera.transform.LookAt(bounds.center);

            RenderTexture renderTexture = new RenderTexture(1200, 900, 24);
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            Texture2D screenshot = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
            screenshot.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            screenshot.Apply();
            File.WriteAllBytes(PreviewPath, screenshot.EncodeToPNG());

            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.DestroyImmediate(screenshot);
            Object.DestroyImmediate(renderTexture);

            EditorSceneManager.SaveScene(scene, ScenePath);
            File.WriteAllText(ReportPath,
                $"Model: {ModelPath}\n" +
                $"Scene: {ScenePath}\n" +
                $"Preview: {PreviewPath}\n" +
                $"Original vertices: {originalVertices}\n" +
                $"Original triangles: {originalTriangles}\n" +
                $"Cropped world Y: {cropY}\n" +
                $"Removed triangles: {removedTriangles}\n" +
                $"Vertices: {vertices}\n" +
                $"Triangles: {triangles}\n" +
                $"Bounds center: {bounds.center}\n" +
                $"Bounds size: {bounds.size}\n");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Meshroom bottle cap preview generated. Vertices={vertices}, Triangles={triangles}, Bounds={bounds.size}");
        }

        private static void CountMesh(GameObject root, out int vertices, out int triangles)
        {
            vertices = 0;
            triangles = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                vertices += filter.sharedMesh.vertexCount;
                triangles += filter.sharedMesh.triangles.Length / 3;
            }
        }

        private static int CropBelowWorldY(GameObject root, float minWorldY)
        {
            int removedTriangles = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                Mesh source = filter.sharedMesh;
                if (source == null)
                {
                    continue;
                }

                Vector3[] sourceVertices = source.vertices;
                Vector3[] sourceNormals = source.normals;
                Vector2[] sourceUvs = source.uv;
                int[] sourceTriangles = source.triangles;
                var keptTriangles = new System.Collections.Generic.List<int>(sourceTriangles.Length);

                for (int i = 0; i < sourceTriangles.Length; i += 3)
                {
                    int a = sourceTriangles[i];
                    int b = sourceTriangles[i + 1];
                    int c = sourceTriangles[i + 2];
                    float ay = filter.transform.TransformPoint(sourceVertices[a]).y;
                    float by = filter.transform.TransformPoint(sourceVertices[b]).y;
                    float cy = filter.transform.TransformPoint(sourceVertices[c]).y;
                    if (ay >= minWorldY || by >= minWorldY || cy >= minWorldY)
                    {
                        keptTriangles.Add(a);
                        keptTriangles.Add(b);
                        keptTriangles.Add(c);
                    }
                    else
                    {
                        removedTriangles++;
                    }
                }

                Mesh cropped = new Mesh();
                cropped.indexFormat = source.indexFormat;
                cropped.vertices = sourceVertices;
                if (sourceNormals != null && sourceNormals.Length == sourceVertices.Length)
                {
                    cropped.normals = sourceNormals;
                }
                if (sourceUvs != null && sourceUvs.Length == sourceVertices.Length)
                {
                    cropped.uv = sourceUvs;
                }
                cropped.triangles = keptTriangles.ToArray();
                cropped.RecalculateBounds();
                if (cropped.normals == null || cropped.normals.Length == 0)
                {
                    cropped.RecalculateNormals();
                }
                filter.sharedMesh = cropped;
            }

            return removedTriangles;
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }
    }
}
