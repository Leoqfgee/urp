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
        public bool mouth_center_independently_measured;
        public bool base_center_independently_measured;
        public bool front_semantics_independently_measured;
        public float mouth_center_error_mm;
        public float base_center_error_mm;
        public float bottle_axis_endpoint_error_mm;
        public float bottle_height_error_mm;
        public SurfaceDistanceStatistics orb_point_to_b_surface_mm;
        public float up_axis_error_deg;
        public float front_axis_error_deg;
        public float[] translation_residual_orb_mm;
        public float yaw_error_deg;
        public float pitch_error_deg;
        public float roll_error_deg;
        public string orb_origin_definition;
        public float[] mouth_center_orb;
        public float[] mouth_center_b;
        public float[] registered_mouth_center_b_orb;
        public float[] base_center_orb;
        public float[] base_center_b;
        public float[] registered_base_center_b_orb;
        public float[] front_axis_orb;
        public float[] front_point_orb;
        public float[] registered_front_point_b_orb;

        public static bool TryParse(
            TextAsset asset,
            string activeRuntimeOrbSha256,
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
            if (string.IsNullOrWhiteSpace(activeRuntimeOrbSha256)
                || !string.Equals(
                    evidence.source_orb_sha256.Trim(),
                    activeRuntimeOrbSha256.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "MODEL_REG_DB_SHA_MISMATCH: evidence source ORB SHA256 "
                    + $"{evidence.source_orb_sha256} does not match active runtime "
                    + $"database {activeRuntimeOrbSha256}.";
                return false;
            }
            if (evidence.orb_point_to_b_surface_mm == null
                || !float.IsFinite(evidence.landmark_rms_mm)
                || !float.IsFinite(evidence.mouth_center_error_mm)
                || !float.IsFinite(evidence.base_center_error_mm)
                || !float.IsFinite(evidence.orb_point_to_b_surface_mm.median_mm)
                || !float.IsFinite(evidence.orb_point_to_b_surface_mm.p95_mm)
                || evidence.landmark_rms_mm > 2.0f
                || evidence.mouth_center_error_mm > 2.0f
                || evidence.base_center_error_mm > 3.0f
                || evidence.orb_point_to_b_surface_mm.median_mm > 7.0f
                || evidence.orb_point_to_b_surface_mm.p95_mm > 12.0f)
            {
                reason = "Model registration exceeds the strict landmark/mouth/base/"
                    + "surface contract (2/2/3/7/12 mm).";
                return false;
            }
            if (!evidence.mouth_center_independently_measured
                || !evidence.base_center_independently_measured
                || !evidence.front_semantics_independently_measured)
            {
                reason = "Mouth, base, and front/barcode evidence must be measured "
                    + "independently on both reconstructions.";
                return false;
            }
            if (!float.IsFinite(evidence.up_axis_error_deg)
                || !float.IsFinite(evidence.front_axis_error_deg)
                || evidence.up_axis_error_deg > 1.5f
                || evidence.front_axis_error_deg > 1.5f)
            {
                reason = "Registered B up/front axes exceed the 1.5 degree contract.";
                return false;
            }
            return true;
        }

    }
}
