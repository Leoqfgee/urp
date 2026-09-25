using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Urp.ArDemo
{
    /// <summary>
    /// Read-only measurement of the authored B/C interface.  It derives the
    /// neck mouth and cap seat from the two independent meshes in their common
    /// BottleRepairRoot frame; it never edits either transform or mesh.
    /// </summary>
    public static class BottlePairRegistrationGeometry
    {
        public readonly struct Result
        {
            public readonly Vector3 neckCenterInPair;
            public readonly Vector3 capSeatCenterInPair;
            public readonly Vector3 neckAxisInPair;
            public readonly Vector3 capAxisInPair;
            public readonly int neckVertexCount;
            public readonly int capVertexCount;

            public Result(
                Vector3 neckCenterInPair,
                Vector3 capSeatCenterInPair,
                Vector3 neckAxisInPair,
                Vector3 capAxisInPair,
                int neckVertexCount,
                int capVertexCount)
            {
                this.neckCenterInPair = neckCenterInPair;
                this.capSeatCenterInPair = capSeatCenterInPair;
                this.neckAxisInPair = neckAxisInPair;
                this.capAxisInPair = capAxisInPair;
                this.neckVertexCount = neckVertexCount;
                this.capVertexCount = capVertexCount;
            }
        }

        public static bool TryMeasure(
            Transform pairRoot,
            Transform neckRoot,
            Transform capRoot,
            out Result result,
            out string reason)
        {
            result = default;
            reason = string.Empty;
            if (pairRoot == null || neckRoot == null || capRoot == null)
            {
                reason = "BottleRepairRoot, ReferenceNeckProxyB and BottleCapC are required.";
                return false;
            }
            Vector3[] neck = CollectPoints(pairRoot, neckRoot);
            Vector3[] cap = CollectPoints(pairRoot, capRoot);
            if (neck.Length < 16 || cap.Length < 16)
            {
                reason = "B neck or C cap mesh has too few vertices.";
                return false;
            }

            Vector3 neckAxis = ShortestBoundsAxis(neck);
            Vector3 capAxis = ShortestBoundsAxis(cap);
            if (Vector3.Dot(neckAxis, Vector3.up) < 0f) neckAxis = -neckAxis;
            if (Vector3.Dot(capAxis, Vector3.up) < 0f) capAxis = -capAxis;

            // Both current production meshes use the measured +Y bottle axis.
            // Work in that authored frame so the two centres remain independent.
            float neckX = Median(neck.Select(point => point.x));
            float neckZ = Median(neck.Select(point => point.z));
            float neckTopY = neck.Max(point => point.y);
            Vector3 neckCenter = new Vector3(neckX, neckTopY, neckZ);

            float capX = Median(cap.Select(point => point.x));
            float capZ = Median(cap.Select(point => point.z));
            float[] capY = cap.Select(point => point.y).OrderBy(value => value).ToArray();
            float[] radii = cap.Select(point =>
                    new Vector2(point.x - capX, point.z - capZ).magnitude)
                .OrderBy(value => value).ToArray();
            float outerRadius = Quantile(radii, 0.90f);
            float upperThreshold = Quantile(capY, 0.70f);
            Vector3[] independentInnerSeat = cap.Where(point =>
            {
                float radius = new Vector2(point.x - capX, point.z - capZ).magnitude;
                return point.y >= upperThreshold
                    && radius >= outerRadius * 0.82f
                    && radius <= outerRadius * 0.94f;
            }).ToArray();
            float capSeatY = independentInnerSeat.Length >= 16
                ? Median(independentInnerSeat.Select(point => point.y))
                : Quantile(capY, 0.80f);
            Vector3 capSeat = new Vector3(capX, capSeatY, capZ);

            result = new Result(
                neckCenter,
                capSeat,
                neckAxis,
                capAxis,
                neck.Length,
                cap.Length);
            return true;
        }

        private static Vector3[] CollectPoints(Transform pairRoot, Transform root)
        {
            List<Vector3> points = new List<Vector3>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                foreach (Vector3 point in filter.sharedMesh.vertices)
                    points.Add(pairRoot.InverseTransformPoint(filter.transform.TransformPoint(point)));
            }
            foreach (SkinnedMeshRenderer renderer in
                     root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                foreach (Vector3 point in renderer.sharedMesh.vertices)
                    points.Add(pairRoot.InverseTransformPoint(renderer.transform.TransformPoint(point)));
            }
            return points.ToArray();
        }

        private static Vector3 ShortestBoundsAxis(Vector3[] points)
        {
            Bounds bounds = new Bounds(points[0], Vector3.zero);
            for (int i = 1; i < points.Length; i++) bounds.Encapsulate(points[i]);
            Vector3 size = bounds.size;
            if (size.y <= size.x && size.y <= size.z) return Vector3.up;
            if (size.x <= size.z) return Vector3.right;
            return Vector3.forward;
        }

        private static float Median(IEnumerable<float> values) =>
            Quantile(values.OrderBy(value => value).ToArray(), 0.5f);

        private static float Quantile(float[] sorted, float q)
        {
            if (sorted == null || sorted.Length == 0) return float.NaN;
            float index = Mathf.Clamp01(q) * (sorted.Length - 1);
            int low = Mathf.FloorToInt(index);
            int high = Mathf.CeilToInt(index);
            return Mathf.Lerp(sorted[low], sorted[high], index - low);
        }
    }
}
