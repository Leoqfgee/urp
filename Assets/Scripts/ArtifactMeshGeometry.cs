using UnityEngine;

namespace Urp.ArDemo
{
    // Measures the actual rendered mesh vertices after all imported child transforms.
    // Renderer.bounds is an axis-aligned box and can extend below rotated geometry.
    public static class ArtifactMeshGeometry
    {
        public struct Measurement
        {
            public Bounds bounds;
            public Vector3 lowestPoint;
            public int vertexCount;
            public int supportVertexCount;
        }

        public static bool TryMeasure(Transform root, out Measurement measurement)
        {
            measurement = default;
            bool any = false;
            float lowestY = float.PositiveInfinity;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                Mesh mesh = filter.sharedMesh;
                if (renderer == null || !renderer.enabled || !filter.gameObject.activeInHierarchy
                    || mesh == null || !mesh.isReadable) continue;
                Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 point = toRoot.MultiplyPoint3x4(vertex);
                    if (!any) { measurement.bounds = new Bounds(point, Vector3.zero); any = true; }
                    else measurement.bounds.Encapsulate(point);
                    if (point.y < lowestY)
                    {
                        lowestY = point.y;
                        measurement.lowestPoint = point;
                    }
                    measurement.vertexCount++;
                }
            }
            if (!any || measurement.bounds.size.y <= 0.00001f) return false;

            float supportLimit = lowestY + measurement.bounds.size.y * .01f;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                Mesh mesh = filter.sharedMesh;
                if (renderer == null || !renderer.enabled || !filter.gameObject.activeInHierarchy
                    || mesh == null || !mesh.isReadable) continue;
                Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (Vector3 vertex in mesh.vertices)
                    if (toRoot.MultiplyPoint3x4(vertex).y <= supportLimit)
                        measurement.supportVertexCount++;
            }
            return true;
        }
    }
}
