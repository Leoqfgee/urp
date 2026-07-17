using UnityEngine;
using Urp.ArDemo.Native;

namespace Urp.ArDemo.Calibration
{
    /// <summary>
    /// The only runtime path from OpenCV PnP R,t to a Unity world transform.
    /// Screen/display matrices are deliberately absent from this class.
    /// </summary>
    public static class OpenCvUnityPoseConverter
    {
        public static bool TryGetObjectPose(
            NativeOrbResult result,
            int frameRotationClockwise,
            Camera arCamera,
            RepairCalibrationProfile calibration,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;
            if (arCamera == null || calibration == null || !calibration.HasValidFrame)
                return false;

            // Native rotates both the CPU image and its camera intrinsics before
            // solvePnP. R/t therefore already describe the oriented camera frame.
            // Applying frameRotationClockwise again here used to swap X/Y a
            // second time and sent a valid cap pose to the left of the bottle.
            Vector3 originCameraCv = TransformModelPoint(result, calibration.objectOriginInModel);
            Vector3 originCameraUnity = CvCameraToUnityCamera(originCameraCv)
                * calibration.metersPerModelUnit;

            Vector3 upCamera = ConvertDirection(result, calibration.UpInModel);
            Vector3 forwardCamera = ConvertDirection(result, calibration.ForwardInModel);
            forwardCamera = Vector3.ProjectOnPlane(forwardCamera, upCamera);
            if (!IsFinite(originCameraUnity)
                || upCamera.sqrMagnitude < 0.5f
                || forwardCamera.sqrMagnitude < 0.5f)
                return false;

            upCamera.Normalize();
            forwardCamera.Normalize();
            Vector3 rightCamera = Vector3.Cross(upCamera, forwardCamera).normalized;
            forwardCamera = Vector3.Cross(rightCamera, upCamera).normalized;
            Quaternion cameraRotation = Quaternion.LookRotation(forwardCamera, upCamera);
            worldPosition = arCamera.transform.TransformPoint(originCameraUnity);
            worldRotation = arCamera.transform.rotation * cameraRotation;
            return IsFinite(worldPosition) && IsFinite(worldRotation)
                && arCamera.transform.InverseTransformPoint(worldPosition).z > 0f;
        }

        public static Vector3 TransformModelPoint(NativeOrbResult result, Vector3 point)
        {
            return TransformModelDirection(result, point)
                + new Vector3(result.tvecX, result.tvecY, result.tvecZ);
        }

        public static Vector3 TransformModelDirection(NativeOrbResult result, Vector3 direction)
        {
            return new Vector3(
                result.r00 * direction.x + result.r01 * direction.y + result.r02 * direction.z,
                result.r10 * direction.x + result.r11 * direction.y + result.r12 * direction.z,
                result.r20 * direction.x + result.r21 * direction.y + result.r22 * direction.z);
        }

        public static Vector3 CvCameraToUnityCamera(Vector3 point)
        {
            return new Vector3(point.x, -point.y, point.z);
        }

        public static Vector3 UndoImageRotation(Vector3 point, int clockwiseDegrees)
        {
            switch ((clockwiseDegrees % 360 + 360) % 360)
            {
                case 90: return new Vector3(point.y, -point.x, point.z);
                case 180: return new Vector3(-point.x, -point.y, point.z);
                case 270: return new Vector3(-point.y, point.x, point.z);
                default: return point;
            }
        }

        private static Vector3 ConvertDirection(
            NativeOrbResult result, Vector3 direction)
        {
            return CvCameraToUnityCamera(TransformModelDirection(result, direction));
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y)
                && float.IsFinite(value.z) && float.IsFinite(value.w);
        }
    }
}
