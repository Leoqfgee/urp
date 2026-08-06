using UnityEngine;
using Urp.ArDemo.Native;

namespace Urp.ArDemo.Calibration
{
    public readonly struct UnityPoseConsistencyResult
    {
        public readonly bool valid;
        public readonly float rmsPixels;
        public readonly float maximumPixels;
        public readonly int samples;

        public UnityPoseConsistencyResult(
            bool valid,
            float rmsPixels,
            float maximumPixels,
            int samples)
        {
            this.valid = valid;
            this.rmsPixels = rmsPixels;
            this.maximumPixels = maximumPixels;
            this.samples = samples;
        }
    }

    /// <summary>
    /// Cross-projects the same native PnP inlier correspondences through the
    /// oriented OpenCV camera and through the prospective rendered-B hierarchy.
    /// </summary>
    public static class UnityPoseConsistencyGate
    {
        public static bool TryEvaluate(
            Camera arCamera,
            NativeInlierSet inliers,
            Vector3 rootPosition,
            Quaternion rootRotation,
            Transform trackedRoot,
            Transform renderedB,
            RepairCalibrationProfile calibration,
            float maximumRmsPixels,
            out UnityPoseConsistencyResult result,
            out string reason)
        {
            result = default;
            reason = string.Empty;
            if (arCamera == null || trackedRoot == null || renderedB == null
                || calibration == null || inliers.Count < 4)
            {
                reason = "Unity/OpenCV cross-projection has insufficient data.";
                return false;
            }

            Matrix4x4 prospectiveRoot = Matrix4x4.TRS(
                rootPosition,
                rootRotation,
                Vector3.one * calibration.metersPerModelUnit);
            float squared = 0f;
            float maximum = 0f;
            int validSamples = 0;
            for (int i = 0; i < inliers.Count; i++)
            {
                Vector2 observed = inliers.FramePoints[i];
                Vector3 orientedCvRay = new Vector3(
                    (observed.x - inliers.Intrinsics.PrincipalPointX)
                        / inliers.Intrinsics.FocalLengthX,
                    (observed.y - inliers.Intrinsics.PrincipalPointY)
                        / inliers.Intrinsics.FocalLengthY,
                    1f);
                Vector3 observedUnityCamera =
                    OpenCvUnityPoseConverter.CvCameraToUnityCamera(orientedCvRay);
                Vector3 observedScreen = arCamera.WorldToScreenPoint(
                    arCamera.transform.TransformPoint(observedUnityCamera));

                Vector3 meshPoint =
                    CanonicalFrameRegistration.OrbToImportedMeshLocalPoint(
                        trackedRoot,
                        renderedB,
                        inliers.ModelPoints[i]);
                Vector3 currentWorld = renderedB.TransformPoint(meshPoint);
                Vector3 pointInTrackedRoot =
                    trackedRoot.InverseTransformPoint(currentWorld);
                Vector3 prospectiveWorld =
                    prospectiveRoot.MultiplyPoint3x4(pointInTrackedRoot);
                Vector3 renderedScreen =
                    arCamera.WorldToScreenPoint(prospectiveWorld);
                if (observedScreen.z <= 0f || renderedScreen.z <= 0f
                    || !IsFinite(observedScreen) || !IsFinite(renderedScreen))
                {
                    continue;
                }

                float error = Vector2.Distance(observedScreen, renderedScreen);
                squared += error * error;
                maximum = Mathf.Max(maximum, error);
                validSamples++;
            }

            if (validSamples < 4)
            {
                reason = "Unity/OpenCV cross-projection has fewer than four visible inliers.";
                return false;
            }
            float rms = Mathf.Sqrt(squared / validSamples);
            bool valid = float.IsFinite(rms) && rms <= maximumRmsPixels;
            result = new UnityPoseConsistencyResult(
                valid,
                rms,
                maximum,
                validSamples);
            if (!valid)
            {
                reason =
                    $"PnP math is valid, but PnP-to-Unity consistency failed: "
                    + $"RMS {rms:F2}px > {maximumRmsPixels:F2}px.";
            }
            return valid;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x)
            && float.IsFinite(value.y)
            && float.IsFinite(value.z);
    }
}
