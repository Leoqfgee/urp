using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Urp.ArDemo.Editor
{
    public static class MeshroomBottleDamagedPreview
    {
        private const string ModelFolder = "Assets/Models/MeshroomBottleDamaged";
        private const string ModelPath = ModelFolder + "/texturedMesh.obj";
        private const string ScenePath = "Assets/Scenes/MeshroomBottleDamagedPreview.unity";
        private const string PreviewPath = "Builds/meshroom-bottle-damaged-unity-preview.png";
        private const string ReportPath = "Builds/meshroom-bottle-damaged-unity-report.txt";

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
            model.name = "Meshroom Damaged Bottle Reconstruction";
            model.transform.position = Vector3.zero;
            model.transform.rotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            Bounds bounds = CalculateBounds(model);
            int removedTriangles = CropOutsideBottleRegion(model);
            bounds = CalculateBounds(model);
            model.transform.position -= bounds.center;
            bounds = CalculateBounds(model);

            int vertices;
            int triangles;
            CountMesh(model, out vertices, out triangles);

            Material previewMaterial = new Material(Shader.Find("Standard"));
            previewMaterial.color = new Color(0.82f, 0.86f, 0.82f, 1f);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = previewMaterial;
            }

            Light key = new GameObject("Preview Key Light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.6f;
            key.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

            Light fill = new GameObject("Preview Fill Light").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.8f;
            fill.transform.rotation = Quaternion.Euler(18f, 130f, 0f);

            Camera camera = new GameObject("Preview Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.14f, 0.14f, 0.14f, 1f);
            camera.fieldOfView = 28f;

            float radius = Mathf.Max(bounds.extents.magnitude, 0.1f);
            camera.transform.position = bounds.center + new Vector3(radius * 0.45f, radius * 0.25f, -radius * 3.2f);
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
                $"Vertices: {vertices}\n" +
                $"Triangles: {triangles}\n" +
                $"Removed triangles for focused preview: {removedTriangles}\n" +
                $"Bounds center: {bounds.center}\n" +
                $"Bounds size: {bounds.size}\n");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Meshroom damaged bottle preview generated. Vertices={vertices}, Triangles={triangles}, Bounds={bounds.size}");
        }

        private static int CropOutsideBottleRegion(GameObject root)
        {
            const float minX = -0.85f;
            const float maxX = 0.85f;
            const float minZ = -1.25f;
            const float maxZ = 0.05f;
            const float minY = 0.30f;

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
                var newTriangles = new System.Collections.Generic.List<int>(sourceTriangles.Length);
                var oldToNew = new System.Collections.Generic.Dictionary<int, int>();
                var newVertices = new System.Collections.Generic.List<Vector3>();
                var newNormals = new System.Collections.Generic.List<Vector3>();
                var newUvs = new System.Collections.Generic.List<Vector2>();

                for (int i = 0; i < sourceTriangles.Length; i += 3)
                {
                    int a = sourceTriangles[i];
                    int b = sourceTriangles[i + 1];
                    int c = sourceTriangles[i + 2];
                    Vector3 aw = filter.transform.TransformPoint(sourceVertices[a]);
                    Vector3 bw = filter.transform.TransformPoint(sourceVertices[b]);
                    Vector3 cw = filter.transform.TransformPoint(sourceVertices[c]);
                    if (InBottleRegion(aw, minX, maxX, minZ, maxZ, minY) ||
                        InBottleRegion(bw, minX, maxX, minZ, maxZ, minY) ||
                        InBottleRegion(cw, minX, maxX, minZ, maxZ, minY))
                    {
                        newTriangles.Add(MapVertex(a, oldToNew, newVertices, newNormals, newUvs, sourceVertices, sourceNormals, sourceUvs));
                        newTriangles.Add(MapVertex(b, oldToNew, newVertices, newNormals, newUvs, sourceVertices, sourceNormals, sourceUvs));
                        newTriangles.Add(MapVertex(c, oldToNew, newVertices, newNormals, newUvs, sourceVertices, sourceNormals, sourceUvs));
                    }
                    else
                    {
                        removedTriangles++;
                    }
                }

                Mesh cropped = new Mesh();
                cropped.indexFormat = source.indexFormat;
                cropped.vertices = newVertices.ToArray();
                if (newNormals.Count == newVertices.Count)
                {
                    cropped.normals = newNormals.ToArray();
                }
                if (newUvs.Count == newVertices.Count)
                {
                    cropped.uv = newUvs.ToArray();
                }
                cropped.triangles = newTriangles.ToArray();
                cropped.RecalculateBounds();
                if (cropped.normals == null || cropped.normals.Length == 0)
                {
                    cropped.RecalculateNormals();
                }
                filter.sharedMesh = cropped;
            }

            return removedTriangles;
        }

        private static int MapVertex(
            int oldIndex,
            System.Collections.Generic.Dictionary<int, int> oldToNew,
            System.Collections.Generic.List<Vector3> newVertices,
            System.Collections.Generic.List<Vector3> newNormals,
            System.Collections.Generic.List<Vector2> newUvs,
            Vector3[] sourceVertices,
            Vector3[] sourceNormals,
            Vector2[] sourceUvs)
        {
            if (oldToNew.TryGetValue(oldIndex, out int newIndex))
            {
                return newIndex;
            }

            newIndex = newVertices.Count;
            oldToNew.Add(oldIndex, newIndex);
            newVertices.Add(sourceVertices[oldIndex]);
            if (sourceNormals != null && sourceNormals.Length == sourceVertices.Length)
            {
                newNormals.Add(sourceNormals[oldIndex]);
            }
            if (sourceUvs != null && sourceUvs.Length == sourceVertices.Length)
            {
                newUvs.Add(sourceUvs[oldIndex]);
            }
            return newIndex;
        }

        private static bool InBottleRegion(Vector3 point, float minX, float maxX, float minZ, float maxZ, float minY)
        {
            return point.x >= minX && point.x <= maxX && point.z >= minZ && point.z <= maxZ && point.y >= minY;
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
