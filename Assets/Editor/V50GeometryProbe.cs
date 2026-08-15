using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Urp.ArDemo.Editor
{
    public static class V50GeometryProbe
    {
        [Serializable]
        private sealed class RegistrationArtifact
        {
            public string version;
            public string measurement_method;
            public string coordinate_frame;
            public Vector3 b_neck_mouth_center_pair;
            public Vector3 c_inner_seat_center_pair;
            public Vector3 b_neck_axis_pair;
            public Vector3 c_cap_axis_pair;
            public float radial_center_error_mm;
            public float axis_angle_deg;
            public float seat_plane_gap_mm;
            public int b_neck_vertex_count;
            public int c_cap_vertex_count;
            public float[] T_C_relative_to_B;
            public bool model_space_correction_applied;
            public bool screen_space_correction_present;
            public string conclusion;
        }

        [MenuItem("URP AR/V50/Probe B C Geometry")]
        public static void RunFromCommandLine()
        {
            const string path = "Assets/Models/CleanBottleReconstruction/"
                + "BottleFullAlignedV2/bottle_full_aligned_v2.fbx";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            Transform pair = Find(instance.transform, "BottleRepairRoot");
            Transform cap = Find(instance.transform, "BottleCapC");
            Transform neck = Find(instance.transform, "ReferenceNeckProxyB");
            Vector3[] capPoints = Points(pair, cap);
            Vector3[] neckPoints = Points(pair, neck);
            string report = Build("cap", capPoints) + Environment.NewLine
                + Build("neck", neckPoints);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/v50_geometry_probe.txt", report);
            if (!BottlePairRegistrationGeometry.TryMeasure(
                    pair,
                    neck,
                    cap,
                    out BottlePairRegistrationGeometry.Result geometry,
                    out string reason))
                throw new InvalidOperationException(reason);
            Vector3 neckWorld = pair.TransformPoint(geometry.neckCenterInPair);
            Vector3 capWorld = pair.TransformPoint(geometry.capSeatCenterInPair);
            Vector3 neckAxisWorld = pair.TransformDirection(
                geometry.neckAxisInPair).normalized;
            Vector3 capAxisWorld = pair.TransformDirection(
                geometry.capAxisInPair).normalized;
            const float metresPerModelUnit = 0.17f;
            float gapMm = Vector3.Dot(capWorld - neckWorld, neckAxisWorld)
                * metresPerModelUnit * 1000f;
            float radialMm = Vector3.ProjectOnPlane(
                    capWorld - neckWorld,
                    neckAxisWorld).magnitude * metresPerModelUnit * 1000f;
            RegistrationArtifact artifact = new RegistrationArtifact
            {
                version = "v50-independent-b-neck-to-original-c-geometry",
                measurement_method = "independent current ReferenceNeckProxyB top ring and BottleCapC upper inner-seat vertex bands",
                coordinate_frame = "shared rigid BottleRepairRoot model space; no camera or screen coordinates",
                b_neck_mouth_center_pair = geometry.neckCenterInPair,
                c_inner_seat_center_pair = geometry.capSeatCenterInPair,
                b_neck_axis_pair = geometry.neckAxisInPair,
                c_cap_axis_pair = geometry.capAxisInPair,
                radial_center_error_mm = radialMm,
                axis_angle_deg = Vector3.Angle(neckAxisWorld, capAxisWorld),
                seat_plane_gap_mm = gapMm,
                b_neck_vertex_count = geometry.neckVertexCount,
                c_cap_vertex_count = geometry.capVertexCount,
                T_C_relative_to_B = new[]
                {
                    1f, 0f, 0f, 0f,
                    0f, 1f, 0f, 0f,
                    0f, 0f, 1f, 0f,
                    0f, 0f, 0f, 1f
                },
                model_space_correction_applied = false,
                screen_space_correction_present = false,
                conclusion = radialMm < 0.5f && Mathf.Abs(gapMm) < 1f
                    ? "B and C are already concentrically authored with a small seating overlap; missing B depth caused the apparent floating/full-ring cap."
                    : "B/C geometry exceeds the strict interface tolerance and requires reviewed model-space calibration."
            };
            File.WriteAllText(
                "Assets/Calibration/bottle_bc_registration_v50.json",
                JsonUtility.ToJson(artifact, true));
            AssetDatabase.ImportAsset(
                "Assets/Calibration/bottle_bc_registration_v50.json",
                ImportAssetOptions.ForceUpdate);
            Debug.Log("[REPAIR_REGISTRATION_DIAG] " + report.Replace('\n', ' '));
            UnityEngine.Object.DestroyImmediate(instance);
        }

        private static string Build(string label, Vector3[] points)
        {
            float[] ys = points.Select(p => p.y).OrderBy(v => v).ToArray();
            float cx = Median(points.Select(p => p.x));
            float cz = Median(points.Select(p => p.z));
            float[] radii = points.Select(p => new Vector2(p.x - cx, p.z - cz).magnitude)
                .OrderBy(v => v).ToArray();
            float outer = Quantile(radii, .9f);
            Vector3[] upperInner = points.Where(p =>
                    p.y >= Quantile(ys, .70f)
                    && new Vector2(p.x - cx, p.z - cz).magnitude >= outer * .82f
                    && new Vector2(p.x - cx, p.z - cz).magnitude <= outer * .94f)
                .ToArray();
            List<string> slices = new List<string>();
            for (int i = 0; i <= 10; i++)
            {
                float y = Quantile(ys, i / 10f);
                float band = Mathf.Max(0.00001f, (ys.Last() - ys.First()) * 0.015f);
                Vector3[] sample = points.Where(p => Mathf.Abs(p.y - y) <= band).ToArray();
                float[] rr = sample.Select(p => new Vector2(p.x - cx, p.z - cz).magnitude)
                    .OrderBy(v => v).ToArray();
                slices.Add($"q{i}=y:{y:F9},n:{sample.Length},r10:{Quantile(rr, .1f):F9},r50:{Quantile(rr, .5f):F9},r90:{Quantile(rr, .9f):F9}");
            }
            return $"{label} count={points.Length} centerXZ=({cx:F9},{cz:F9}) "
                + $"y=({ys.First():F9},{ys.Last():F9}) r=({radii.First():F9},{radii.Last():F9}) "
                + $"upperInnerN={upperInner.Length},upperInnerCenter=({Median(upperInner.Select(p => p.x)):F9},{Median(upperInner.Select(p => p.y)):F9},{Median(upperInner.Select(p => p.z)):F9}) "
                + string.Join(" | ", slices);
        }

        private static Vector3[] Points(Transform pair, Transform root)
        {
            List<Vector3> result = new List<Vector3>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                foreach (Vector3 point in filter.sharedMesh.vertices)
                    result.Add(pair.InverseTransformPoint(filter.transform.TransformPoint(point)));
            }
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                foreach (Vector3 point in renderer.sharedMesh.vertices)
                    result.Add(pair.InverseTransformPoint(renderer.transform.TransformPoint(point)));
            }
            return result.ToArray();
        }

        private static float Median(IEnumerable<float> values)
        {
            float[] sorted = values.OrderBy(v => v).ToArray();
            return Quantile(sorted, .5f);
        }

        private static float Quantile(float[] sorted, float q)
        {
            if (sorted == null || sorted.Length == 0) return float.NaN;
            float index = Mathf.Clamp01(q) * (sorted.Length - 1);
            int low = Mathf.FloorToInt(index);
            int high = Mathf.CeilToInt(index);
            return Mathf.Lerp(sorted[low], sorted[high], index - low);
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
