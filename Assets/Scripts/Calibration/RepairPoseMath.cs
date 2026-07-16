using UnityEngine;
using Urp.ArDemo.Native;

namespace Urp.ArDemo.Calibration
{
    public static class RepairPoseMath
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
            {
                return false;
            }

            Vector3 originInCameraCv = TransformModelPoint(
                result,
                calibration.objectOriginInModel);
            originInCameraCv = UndoImageRotation(originInCameraCv, frameRotationClockwise);
            Vector3 originInCameraUnity = CvCameraToUnityCamera(originInCameraCv)
                * calibration.metersPerModelUnit;

            Vector3 upInCameraCv = TransformModelDirection(result, calibration.UpInModel);
            Vector3 forwardInCameraCv = TransformModelDirection(result, calibration.ForwardInModel);
            upInCameraCv = UndoImageRotation(upInCameraCv, frameRotationClockwise);
            forwardInCameraCv = UndoImageRotation(forwardInCameraCv, frameRotationClockwise);

            Vector3 upInCameraUnity = CvCameraToUnityCamera(upInCameraCv).normalized;
            Vector3 forwardInCameraUnity = CvCameraToUnityCamera(forwardInCameraCv);
            forwardInCameraUnity = Vector3.ProjectOnPlane(forwardInCameraUnity, upInCameraUnity);
            if (!IsFinite(originInCameraUnity)
                || upInCameraUnity.sqrMagnitude < 0.5f
                || forwardInCameraUnity.sqrMagnitude < 0.5f)
            {
                return false;
            }

            forwardInCameraUnity.Normalize();
            Vector3 right = Vector3.Cross(upInCameraUnity, forwardInCameraUnity).normalized;
            forwardInCameraUnity = Vector3.Cross(right, upInCameraUnity).normalized;
            if (Vector3.Dot(Vector3.Cross(right, upInCameraUnity), forwardInCameraUnity) < 0f)
            {
                forwardInCameraUnity = -forwardInCameraUnity;
            }

            worldPosition = arCamera.transform.TransformPoint(originInCameraUnity);
            Vector3 worldUp = arCamera.transform.TransformDirection(upInCameraUnity).normalized;
            Vector3 worldForward = arCamera.transform.TransformDirection(forwardInCameraUnity).normalized;
            worldRotation = Quaternion.LookRotation(worldForward, worldUp);
            return IsFinite(worldPosition) && IsFinite(worldRotation);
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
            switch (clockwiseDegrees)
            {
                case 90:
                    return new Vector3(point.y, -point.x, point.z);
                case 180:
                    return new Vector3(-point.x, -point.y, point.z);
                case 270:
                    return new Vector3(-point.y, point.x, point.z);
                default:
                    return point;
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z)
                && float.IsFinite(value.w);
        }
    }
}
