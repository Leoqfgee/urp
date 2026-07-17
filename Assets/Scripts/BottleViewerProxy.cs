using UnityEngine;

namespace Urp.ArDemo
{
    internal static class BottleViewerProxy
    {
        private const int RadialSegments = 96;
        private static Material whiteMaterial;
        private static Material greenMaterial;
        private static Material blackMaterial;
        private static Material redMaterial;

        public static GameObject Create(bool withCap)
        {
            EnsureMaterials();
            GameObject root = new GameObject(withCap
                ? "Clean Complete Bottle Viewer"
                : "Clean Damaged Bottle Viewer");

            float[] bodyY =
            {
                0.00f, 0.025f, 0.07f, 0.16f, 0.32f, 1.30f, 1.40f, 1.50f,
                1.56f, 1.59f, 1.61f, 1.64f, 1.66f, 1.69f, 1.71f, 1.74f
            };
            float[] bodyR =
            {
                0.25f, 0.30f, 0.34f, 0.36f, 0.37f, 0.36f, 0.33f, 0.27f,
                0.205f, 0.175f, 0.184f, 0.174f, 0.185f, 0.174f, 0.181f, 0.170f
            };
            CreateLathe(root.transform, "Bottle body", bodyY, bodyR, whiteMaterial, true);

            float[] sleeveY = { 0.04f, 0.08f, 0.17f, 0.36f, 0.48f };
            float[] sleeveR = { 0.286f, 0.337f, 0.367f, 0.372f, 0.369f };
            CreateLathe(root.transform, "Green lower label", sleeveY, sleeveR, greenMaterial, false);

            AddFrontPanel(root.transform, "Brand mark", redMaterial,
                new Vector3(0f, 1.22f, -0.364f), new Vector3(0.19f, 0.075f, 0.012f));
            AddFrontPanel(root.transform, "Main label A", blackMaterial,
                new Vector3(-0.095f, 0.99f, -0.367f), new Vector3(0.12f, 0.055f, 0.011f));
            AddFrontPanel(root.transform, "Main label B", blackMaterial,
                new Vector3(0.10f, 0.99f, -0.367f), new Vector3(0.13f, 0.055f, 0.011f));
            AddFrontPanel(root.transform, "Main label C", blackMaterial,
                new Vector3(-0.08f, 0.86f, -0.369f), new Vector3(0.14f, 0.055f, 0.011f));
            AddFrontPanel(root.transform, "Main label D", blackMaterial,
                new Vector3(0.12f, 0.86f, -0.369f), new Vector3(0.11f, 0.055f, 0.011f));
            AddFrontPanel(root.transform, "Green callout", greenMaterial,
                new Vector3(-0.13f, 0.70f, -0.371f), new Vector3(0.11f, 0.055f, 0.012f));

            if (withCap)
            {
                float[] capY = { 1.70f, 1.71f, 1.73f, 1.80f, 1.82f, 1.83f };
                float[] capR = { 0.18f, 0.195f, 0.198f, 0.198f, 0.190f, 0.17f };
                CreateLathe(root.transform, "Reconstructed cap", capY, capR,
                    whiteMaterial, true);
            }

            return root;
        }

        private static void AddFrontPanel(Transform parent, string name, Material material,
            Vector3 position, Vector3 scale)
        {
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = name;
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = position;
            panel.transform.localScale = scale;
            Object.Destroy(panel.GetComponent<Collider>());
            panel.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void CreateLathe(Transform parent, string name, float[] heights,
            float[] radii, Material material, bool closeEnds)
        {
            int rings = heights.Length;
            int vertexCount = rings * (RadialSegments + 1) + (closeEnds ? 2 : 0);
            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uv = new Vector2[vertexCount];
            int cursor = 0;
            for (int ring = 0; ring < rings; ring++)
            {
                for (int segment = 0; segment <= RadialSegments; segment++)
                {
                    float u = segment / (float)RadialSegments;
                    float angle = u * Mathf.PI * 2f;
                    vertices[cursor] = new Vector3(
                        Mathf.Sin(angle) * radii[ring], heights[ring],
                        Mathf.Cos(angle) * radii[ring]);
                    uv[cursor] = new Vector2(u, ring / (float)(rings - 1));
                    cursor++;
                }
            }

            int sideTriangles = (rings - 1) * RadialSegments * 6;
            int capTriangles = closeEnds ? RadialSegments * 6 : 0;
            int[] triangles = new int[sideTriangles + capTriangles];
            cursor = 0;
            int stride = RadialSegments + 1;
            for (int ring = 0; ring < rings - 1; ring++)
            {
                for (int segment = 0; segment < RadialSegments; segment++)
                {
                    int a = ring * stride + segment;
                    int b = a + stride;
                    triangles[cursor++] = a;
                    triangles[cursor++] = b;
                    triangles[cursor++] = a + 1;
                    triangles[cursor++] = a + 1;
                    triangles[cursor++] = b;
                    triangles[cursor++] = b + 1;
                }
            }

            if (closeEnds)
            {
                int bottomCenter = rings * stride;
                int topCenter = bottomCenter + 1;
                vertices[bottomCenter] = new Vector3(0f, heights[0], 0f);
                vertices[topCenter] = new Vector3(0f, heights[rings - 1], 0f);
                uv[bottomCenter] = uv[topCenter] = new Vector2(0.5f, 0.5f);
                int topStart = (rings - 1) * stride;
                for (int segment = 0; segment < RadialSegments; segment++)
                {
                    triangles[cursor++] = bottomCenter;
                    triangles[cursor++] = segment + 1;
                    triangles[cursor++] = segment;
                    triangles[cursor++] = topCenter;
                    triangles[cursor++] = topStart + segment;
                    triangles[cursor++] = topStart + segment + 1;
                }
            }

            Mesh mesh = new Mesh { name = name + " mesh" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void EnsureMaterials()
        {
            if (whiteMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
            whiteMaterial = CreateMaterial(shader, "Bottle pearl white",
                new Color32(242, 242, 236, 255), 0.36f);
            greenMaterial = CreateMaterial(shader, "Bottle label green",
                new Color32(66, 184, 48, 255), 0.22f);
            blackMaterial = CreateMaterial(shader, "Bottle label ink",
                new Color32(18, 20, 19, 255), 0.10f);
            redMaterial = CreateMaterial(shader, "Bottle brand red",
                new Color32(196, 35, 35, 255), 0.18f);
        }

        private static Material CreateMaterial(Shader shader, string name, Color color,
            float smoothness)
        {
            Material material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else material.color = color;
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            return material;
        }
    }
}
