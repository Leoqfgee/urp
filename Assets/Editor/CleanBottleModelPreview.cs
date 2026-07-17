using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Urp.ArDemo.Editor
{
    public static class CleanBottleModelPreview
    {
        public static void CaptureFromCommandLine()
        {
            Capture("Assets/Models/CleanBottleReconstruction/bottle_damaged_clean.obj",
                "Builds/clean-bottle-damaged-preview.png");
            Capture("Assets/Models/CleanBottleReconstruction/bottle_complete_clean.obj",
                "Builds/clean-bottle-complete-preview.png");
            Capture("Assets/Models/CleanBottleReconstruction/bottle_cap_clean.obj",
                "Builds/clean-bottle-cap-preview.png");
            Debug.Log("CLEAN_BOTTLE_PREVIEWS_OK");
        }

        private static void Capture(string modelPath, string outputPath)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (prefab == null)
                throw new FileNotFoundException("Imported clean model is missing.", modelPath);

            GameObject model = Object.Instantiate(prefab);
            model.name = Path.GetFileNameWithoutExtension(modelPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/BottleViewerLit.mat");
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            Bounds bounds = CalculateBounds(model);
            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            Camera camera = new GameObject("Preview Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(232, 238, 246, 255);
            camera.fieldOfView = 28f;
            camera.allowHDR = false;
            const float aspect = 900f / 1200f;
            float verticalHalfAngle = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float fitHeight = bounds.extents.y / Mathf.Tan(verticalHalfAngle);
            float fitWidth = bounds.extents.x / (Mathf.Tan(verticalHalfAngle) * aspect);
            float distance = Mathf.Max(fitHeight, fitWidth) * 1.16f + bounds.extents.z;
            camera.transform.position = bounds.center - Vector3.forward * distance;
            camera.transform.rotation = Quaternion.identity;
            camera.nearClipPlane = 0.001f;
            camera.farClipPlane = largest * 8f;

            Light key = new GameObject("Key Light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.1f;
            key.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            Light fill = new GameObject("Fill Light").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.55f;
            fill.transform.rotation = Quaternion.Euler(18f, 145f, 0f);

            RenderTexture target = new RenderTexture(900, 1200, 24,
                RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D screenshot = new Texture2D(target.width, target.height,
                TextureFormat.RGBA32, false);
            screenshot.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
            screenshot.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? "Builds");
            File.WriteAllBytes(outputPath, screenshot.EncodeToPNG());
            RenderTexture.active = null;
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(screenshot);
            EditorSceneManager.CloseScene(scene, true);
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }
    }
}
