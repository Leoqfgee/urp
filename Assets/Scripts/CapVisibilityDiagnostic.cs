using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Urp.ArDemo
{
    /// <summary>
    /// Android/Editor diagnostic for locating rigid cap C in the real AR camera.
    /// It is inert in non-development players and never changes C's transform.
    /// </summary>
    public sealed class CapVisibilityDiagnostic : MonoBehaviour
    {
        [SerializeField] private Camera arCamera;
        [SerializeField] private ARCameraBackground arCameraBackground;
        [SerializeField] private AROcclusionManager arOcclusionManager;
        [SerializeField] private bool showCapAxesAndMarker;
        [SerializeField] private bool forceCapDiagnosticMaterial;

        private Transform trackedRoot;
        private Transform pairRoot;
        private Transform referenceB;
        private Transform capC;
        private Renderer[] capRenderers = System.Array.Empty<Renderer>();
        private readonly Dictionary<Renderer, Material[]> originalMaterials =
            new Dictionary<Renderer, Material[]>();
        private GameObject markerRoot;
        private Material magentaMaterial;

        public void BindRigidTarget(
            Transform root,
            Transform pair,
            Transform reference,
            Transform cap,
            Renderer[] renderers)
        {
            RestoreOriginalMaterials();
            trackedRoot = root;
            pairRoot = pair;
            referenceB = reference;
            capC = cap;
            capRenderers = renderers ?? System.Array.Empty<Renderer>();
            originalMaterials.Clear();
            foreach (Renderer renderer in capRenderers)
            {
                if (renderer != null)
                {
                    originalMaterials[renderer] = renderer.sharedMaterials;
                }
            }
        }

        public void ConfigureVisualDiagnostic(bool showMarker, bool forceMagenta)
        {
            showCapAxesAndMarker = showMarker;
            forceCapDiagnosticMaterial = forceMagenta;
        }

        private void Update()
        {
            if (!DiagnosticsEnabled)
            {
                return;
            }
            UpdateDiagnosticMaterial();
            UpdateMarker();
        }

        private void OnDisable()
        {
            RestoreOriginalMaterials();
            DestroyMarker();
        }

        private void OnDestroy()
        {
            RestoreOriginalMaterials();
            DestroyMarker();
            if (magentaMaterial != null)
            {
                Destroy(magentaMaterial);
            }
        }

        public void LogSnapshot(string stage)
        {
            if (!DiagnosticsEnabled)
            {
                return;
            }

            StringBuilder log = new StringBuilder(4096);
            log.Append("[URP_CAP_DIAG] stage=").Append(stage).AppendLine();
            AppendTransform(log, "TrackedBottleRoot", trackedRoot);
            AppendTransform(log, "BottleRepairRoot", pairRoot);
            AppendTransform(log, "DamagedBottleB", referenceB);
            AppendTransform(log, "BottleCapC", capC);
            if (pairRoot != null && capC != null)
            {
                log.Append("BottleCapC.relativeToPair=")
                    .Append(FormatMatrix(
                        pairRoot.worldToLocalMatrix * capC.localToWorldMatrix))
                    .AppendLine();
            }

            Bounds capBounds = default;
            bool hasCapBounds = TryCombinedBounds(capRenderers, out capBounds);
            Renderer[] bodyRenderers = referenceB != null
                ? referenceB.GetComponentsInChildren<Renderer>(true)
                : System.Array.Empty<Renderer>();
            if (TryCombinedBounds(bodyRenderers, out Bounds bodyBounds))
            {
                log.Append("DamagedBottleB.bounds.center=").Append(Format(bodyBounds.center))
                    .Append(" extents=").Append(Format(bodyBounds.extents)).AppendLine();
            }
            if (hasCapBounds)
            {
                log.Append("BottleCapC.bounds.center=").Append(Format(capBounds.center))
                    .Append(" extents=").Append(Format(capBounds.extents)).AppendLine();
            }

            if (arCamera != null)
            {
                log.Append("ARCamera.near=").Append(arCamera.nearClipPlane.ToString("F6"))
                    .Append(" far=").Append(arCamera.farClipPlane.ToString("F6"))
                    .Append(" fov=").Append(arCamera.fieldOfView.ToString("F6"))
                    .Append(" aspect=").Append(arCamera.aspect.ToString("F6"))
                    .Append(" cullingMask=").Append(arCamera.cullingMask)
                    .AppendLine();
                log.Append("ARCamera.projectionMatrix=")
                    .Append(FormatMatrix(arCamera.projectionMatrix)).AppendLine();
                if (hasCapBounds)
                {
                    Plane[] planes = GeometryUtility.CalculateFrustumPlanes(arCamera);
                    log.Append("BottleCapC.frustumIntersects=")
                        .Append(GeometryUtility.TestPlanesAABB(planes, capBounds))
                        .AppendLine();
                    Vector3 min = capBounds.min;
                    Vector3 max = capBounds.max;
                    int cornerIndex = 0;
                    for (int x = 0; x <= 1; x++)
                    {
                        for (int y = 0; y <= 1; y++)
                        {
                            for (int z = 0; z <= 1; z++)
                            {
                                Vector3 world = new Vector3(
                                    x == 0 ? min.x : max.x,
                                    y == 0 ? min.y : max.y,
                                    z == 0 ? min.z : max.z);
                                Vector3 camera = arCamera.transform.InverseTransformPoint(world);
                                bool insideDepth = camera.z > arCamera.nearClipPlane
                                    && camera.z < arCamera.farClipPlane;
                                log.Append("BottleCapC.corner[").Append(cornerIndex++)
                                    .Append("] camera=").Append(Format(camera))
                                    .Append(" depthValid=").Append(insideDepth).AppendLine();
                            }
                        }
                    }
                }
            }

            foreach (Renderer renderer in capRenderers)
            {
                AppendRenderer(log, renderer);
            }
            log.Append("ARCameraBackground.enabled=")
                .Append(arCameraBackground != null && arCameraBackground.enabled)
                .AppendLine();
            if (arOcclusionManager != null)
            {
                log.Append("AROcclusionManager.enabled=")
                    .Append(arOcclusionManager.enabled)
                    .Append(" requestedEnvironmentDepthMode=")
                    .Append(arOcclusionManager.requestedEnvironmentDepthMode)
                    .Append(" currentEnvironmentDepthMode=")
                    .Append(arOcclusionManager.currentEnvironmentDepthMode)
                    .AppendLine();
            }
            else
            {
                log.AppendLine("AROcclusionManager=null");
            }
            Debug.Log(log.ToString());
        }

        private void AppendRenderer(StringBuilder log, Renderer renderer)
        {
            if (renderer == null)
            {
                log.AppendLine("BottleCapC.renderer=null");
                return;
            }
            int layer = renderer.gameObject.layer;
            bool layerInMask = arCamera != null
                && (arCamera.cullingMask & (1 << layer)) != 0;
            log.Append("BottleCapC.renderer name=").Append(renderer.name)
                .Append(" layer=").Append(layer)
                .Append(" layerInCameraMask=").Append(layerInMask)
                .Append(" enabled=").Append(renderer.enabled)
                .Append(" forceRenderingOff=").Append(renderer.forceRenderingOff)
                .Append(" isVisible=").Append(renderer.isVisible)
                .Append(" activeInHierarchy=").Append(renderer.gameObject.activeInHierarchy)
                .Append(" bounds.center=").Append(Format(renderer.bounds.center))
                .Append(" bounds.extents=").Append(Format(renderer.bounds.extents))
                .AppendLine();

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            log.Append("BottleCapC.propertyBlock.isEmpty=").Append(block.isEmpty)
                .Append(" _BaseColor=").Append(Format(block.GetColor("_BaseColor")))
                .AppendLine();
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null)
                {
                    log.AppendLine("BottleCapC.material=null");
                    continue;
                }
                log.Append("BottleCapC.material name=").Append(material.name)
                    .Append(" shader=").Append(material.shader != null ? material.shader.name : "null")
                    .Append(" renderQueue=").Append(material.renderQueue)
                    .Append(" _Surface=").Append(GetFloat(material, "_Surface"))
                    .Append(" _ZWrite=").Append(GetFloat(material, "_ZWrite"))
                    .Append(" _Cull=").Append(GetFloat(material, "_Cull"))
                    .Append(" _BaseColor=").Append(
                        material.HasProperty("_BaseColor")
                            ? Format(material.GetColor("_BaseColor"))
                            : "n/a")
                    .AppendLine();
            }
        }

        private void UpdateDiagnosticMaterial()
        {
            if (!forceCapDiagnosticMaterial)
            {
                RestoreOriginalMaterials();
                return;
            }
            if (magentaMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color");
                if (shader == null)
                {
                    return;
                }
                magentaMaterial = new Material(shader)
                {
                    name = "CapDiagnosticMagenta_Runtime"
                };
                if (magentaMaterial.HasProperty("_BaseColor"))
                    magentaMaterial.SetColor("_BaseColor", Color.magenta);
                if (magentaMaterial.HasProperty("_Color"))
                    magentaMaterial.SetColor("_Color", Color.magenta);
            }
            foreach (Renderer renderer in capRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }
                int count = Mathf.Max(1, renderer.sharedMaterials.Length);
                Material[] materials = new Material[count];
                for (int index = 0; index < count; index++)
                    materials[index] = magentaMaterial;
                renderer.sharedMaterials = materials;
            }
        }

        private void RestoreOriginalMaterials()
        {
            foreach (KeyValuePair<Renderer, Material[]> entry in originalMaterials)
            {
                if (entry.Key != null && entry.Value != null)
                {
                    entry.Key.sharedMaterials = entry.Value;
                }
            }
        }

        private void UpdateMarker()
        {
            if (!showCapAxesAndMarker || capC == null
                || !TryCombinedBounds(capRenderers, out Bounds bounds))
            {
                DestroyMarker();
                return;
            }
            if (markerRoot == null)
            {
                markerRoot = new GameObject("CapVisibilityDiagnostic_Runtime");
                markerRoot.hideFlags = HideFlags.DontSave;
                CreateMarkerGeometry(markerRoot.transform);
            }
            markerRoot.SetActive(true);
            markerRoot.transform.position = bounds.center;
            markerRoot.transform.rotation = capC.rotation;
        }

        private static void CreateMarkerGeometry(Transform root)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "CapCenter";
            sphere.transform.SetParent(root, false);
            sphere.transform.localScale = Vector3.one * 0.008f;
            Collider collider = sphere.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            Renderer sphereRenderer = sphere.GetComponent<Renderer>();
            Material markerMaterial = CreateUnlitMaterial(Color.magenta, "CapCenterMaterial");
            if (sphereRenderer != null && markerMaterial != null)
                sphereRenderer.sharedMaterial = markerMaterial;
            CreateAxis(root, "X", Vector3.right, Color.red);
            CreateAxis(root, "Y", Vector3.up, Color.green);
            CreateAxis(root, "Z", Vector3.forward, Color.blue);
        }

        private static void CreateAxis(
            Transform root,
            string name,
            Vector3 direction,
            Color color)
        {
            GameObject axis = new GameObject("CapAxis" + name);
            axis.transform.SetParent(root, false);
            LineRenderer line = axis.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, direction * 0.03f);
            line.startWidth = 0.002f;
            line.endWidth = 0.002f;
            line.sharedMaterial = CreateUnlitMaterial(color, "CapAxis" + name + "Material");
        }

        private static Material CreateUnlitMaterial(Color color, string name)
        {
            Shader shader = Shader.Find("Sprites/Default")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color");
            if (shader == null)
                return null;
            Material material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            return material;
        }

        private void DestroyMarker()
        {
            if (markerRoot != null)
            {
                Destroy(markerRoot);
                markerRoot = null;
            }
        }

        private static bool TryCombinedBounds(Renderer[] renderers, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (Renderer renderer in renderers ?? System.Array.Empty<Renderer>())
            {
                if (renderer == null)
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found;
        }

        private static void AppendTransform(StringBuilder log, string label, Transform value)
        {
            if (value == null)
            {
                log.Append(label).AppendLine("=null");
                return;
            }
            log.Append(label).Append(".position=").Append(Format(value.position))
                .Append(" rotation=").Append(Format(value.rotation))
                .Append(" lossyScale=").Append(Format(value.lossyScale))
                .Append(" worldMatrix=").Append(FormatMatrix(value.localToWorldMatrix))
                .AppendLine();
        }

        private static string GetFloat(Material material, string property)
        {
            return material.HasProperty(property)
                ? material.GetFloat(property).ToString("F6")
                : "n/a";
        }

        private static string Format(Vector3 value) =>
            $"({value.x:F6},{value.y:F6},{value.z:F6})";

        private static string Format(Quaternion value) =>
            $"({value.x:F6},{value.y:F6},{value.z:F6},{value.w:F6})";

        private static string Format(Color value) =>
            $"({value.r:F6},{value.g:F6},{value.b:F6},{value.a:F6})";

        private static string FormatMatrix(Matrix4x4 value) =>
            value.ToString("F6").Replace('\n', ' ').Replace('\r', ' ');

        private static bool DiagnosticsEnabled => Debug.isDebugBuild || Application.isEditor;
    }
}
