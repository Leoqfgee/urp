using System.Text;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Urp.ArDemo
{
    /// <summary>
    /// Read-only Android/Editor diagnostics for the immutable BottleCapC.
    /// This component never creates geometry, swaps materials, or changes a
    /// renderer/transform. Visual diagnostics are intentionally unsupported.
    /// </summary>
    public sealed class CapVisibilityDiagnostic : MonoBehaviour
    {
        [SerializeField] private Camera arCamera;
        [SerializeField] private ARCameraBackground arCameraBackground;
        [SerializeField] private AROcclusionManager arOcclusionManager;

        private Transform trackedRoot;
        private Transform pairRoot;
        private Transform referenceB;
        private Transform capC;
        private Renderer[] capRenderers = System.Array.Empty<Renderer>();

        public void BindRigidTarget(
            Transform root,
            Transform pair,
            Transform reference,
            Transform cap,
            Renderer[] renderers)
        {
            trackedRoot = root;
            pairRoot = pair;
            referenceB = reference;
            capC = cap;
            capRenderers = renderers ?? System.Array.Empty<Renderer>();
        }

        public void LogSnapshot(string stage)
        {
            if (!DiagnosticsEnabled)
                return;

            StringBuilder log = new StringBuilder(3072);
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

            bool hasCapBounds = TryCombinedBounds(capRenderers, out Bounds capBounds);
            if (hasCapBounds)
            {
                log.Append("BottleCapC.bounds.center=").Append(Format(capBounds.center))
                    .Append(" extents=").Append(Format(capBounds.extents)).AppendLine();
            }
            if (arCamera != null)
            {
                log.Append("ARCamera.projectionMatrix=")
                    .Append(FormatMatrix(arCamera.projectionMatrix)).AppendLine();
                if (hasCapBounds)
                {
                    Plane[] planes = GeometryUtility.CalculateFrustumPlanes(arCamera);
                    log.Append("BottleCapC.frustumIntersects=")
                        .Append(GeometryUtility.TestPlanesAABB(planes, capBounds))
                        .AppendLine();
                }
            }
            foreach (Renderer renderer in capRenderers)
                AppendRenderer(log, renderer);
            log.Append("ARCameraBackground.enabled=")
                .Append(arCameraBackground != null && arCameraBackground.enabled)
                .AppendLine();
            log.Append("AROcclusionManager.enabled=")
                .Append(arOcclusionManager != null && arOcclusionManager.enabled)
                .AppendLine();
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
                .Append(" bounds.center=").Append(Format(renderer.bounds.center))
                .Append(" bounds.extents=").Append(Format(renderer.bounds.extents))
                .AppendLine();
            foreach (Material material in renderer.sharedMaterials)
            {
                log.Append("BottleCapC.material name=")
                    .Append(material != null ? material.name : "null")
                    .Append(" shader=")
                    .Append(material != null && material.shader != null
                        ? material.shader.name
                        : "null")
                    .AppendLine();
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

        private static void AppendTransform(
            StringBuilder log,
            string label,
            Transform value)
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

        private static string Format(Vector3 value) =>
            $"({value.x:F6},{value.y:F6},{value.z:F6})";

        private static string Format(Quaternion value) =>
            $"({value.x:F6},{value.y:F6},{value.z:F6},{value.w:F6})";

        private static string FormatMatrix(Matrix4x4 value) =>
            value.ToString("F6").Replace('\n', ' ').Replace('\r', ' ');

        private static bool DiagnosticsEnabled =>
            Debug.isDebugBuild || Application.isEditor;
    }
}
