using System;
using UnityEngine;

namespace Urp.ArDemo.Calibration
{
    [Serializable]
    public sealed class ModelRegistrationEvidence
    {
        [Serializable]
        public sealed class SurfaceDistanceStatistics
        {
            public float rms_mm;
            public float median_mm;
            public float p90_mm;
            public float p95_mm;
            public float max_mm;
        }

        public string version;
        public string registration_method;
        public bool independent_model_registration_verified;
        public bool device_verified;
        public string source_orb_sha256;
        public string target_b_mesh_sha256;
        public float[] T_ORB_FROM_B;
        public float scale;
        public float determinant;
        public float landmark_rms_mm;
        public SurfaceDistanceStatistics orb_point_to_b_surface_mm;
        public float up_axis_agreement;
        public float front_axis_agreement;
        public string orb_origin_definition;
        public float[] mouth_center_orb;
        public float[] base_center_orb;
        public float[] front_axis_orb;

        public static bool TryParse(
            TextAsset asset,
            out ModelRegistrationEvidence evidence,
            out string reason)
        {
            evidence = null;
            reason = string.Empty;
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                reason = "Independent ORB-to-B registration artifact is missing.";
                return false;
            }

            try
            {
                evidence = JsonUtility.FromJson<ModelRegistrationEvidence>(asset.text);
            }
            catch (Exception exception)
            {
                reason = $"Registration artifact JSON is invalid: {exception.Message}";
                return false;
            }

            if (evidence == null || !evidence.independent_model_registration_verified)
            {
                reason = "Independent ORB-to-B model registration is not verified.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(evidence.source_orb_sha256)
                || string.IsNullOrWhiteSpace(evidence.target_b_mesh_sha256)
                || evidence.T_ORB_FROM_B == null
                || evidence.T_ORB_FROM_B.Length != 16)
            {
                reason = "Registration artifact is missing hashes or its 4x4 matrix.";
                return false;
            }
            if (evidence.orb_point_to_b_surface_mm == null
                || !float.IsFinite(evidence.landmark_rms_mm)
                || !float.IsFinite(evidence.orb_point_to_b_surface_mm.p95_mm)
                || evidence.landmark_rms_mm > 2.0f
                || evidence.orb_point_to_b_surface_mm.p95_mm > 12.0f)
            {
                reason = "Independent model registration exceeds the 2 mm landmark "
                    + "or 12 mm cross-reconstruction surface-p95 contract.";
                return false;
            }
            if (evidence.up_axis_agreement < 0.995f
                || evidence.front_axis_agreement < 0.995f)
            {
                reason = "Registered B up/front axes do not satisfy the orientation contract.";
                return false;
            }
            return true;
        }
    }
}
