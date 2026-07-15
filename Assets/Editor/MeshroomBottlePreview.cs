using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Urp.ArDemo.Editor
{
    public static class MeshroomBottlePreview
    {
        private const string ModelPath = "Assets/Models/MeshroomBottle/texturedMesh.obj";
        private const string ScenePath = "Assets/Scenes/MeshroomBottlePreview.unity";
        private const string PreviewPath = "Builds/meshroom-bottle-unity-preview.png";
        private const string ReportPath = "Builds/meshroom-bottle-unity-report.txt";

        public static void GenerateFromCommandLine()
        {
            Directory.CreateDirectory("Builds");
            AssetDatabase.ImportAsset("Assets/Models/MeshroomBottle", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException($"Unity could not import model: {ModelPath}");
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            model.name = "Meshroom Bottle Reconstruction";
            model.transform.position = Vector3.zero;
            model.transform.rotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            Bounds bounds = CalculateBounds(model);
            Vector3 centerOffset = bounds.center;
            model.transform.position -= centerOffset;
            bounds = CalculateBounds(model);

            int vertices = 0;
            int triangles = 0;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                vertices += filter.sharedMesh.vertexCount;
                triangles += filter.sharedMesh.triangles.Length / 3;
            }

            Light light = new GameObject("Preview Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.8f;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            Camera camera = new GameObject("Preview Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
            camera.fieldOfView = 35f;
            float radius = Mathf.Max(bounds.extents.magnitude, 0.1f);
            camera.transform.position = bounds.center + new Vector3(0f, radius * 0.25f, -radius * 2.6f);
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
                $"Bounds center: {bounds.center}\n" +
                $"Bounds size: {bounds.size}\n");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Meshroom bottle preview generated. Vertices={vertices}, Triangles={triangles}, Bounds={bounds.size}");
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
