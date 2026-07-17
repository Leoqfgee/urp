using UnityEngine;
using Urp.ArDemo.Native;

namespace Urp.ArDemo.Calibration
{
    [System.Obsolete("Use OpenCvUnityPoseConverter. This compatibility facade contains no pose logic.")]
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
            return OpenCvUnityPoseConverter.TryGetObjectPose(result, frameRotationClockwise,
                arCamera, calibration, out worldPosition, out worldRotation);
        }

        public static Vector3 TransformModelPoint(NativeOrbResult result, Vector3 point)
        {
            return OpenCvUnityPoseConverter.TransformModelPoint(result, point);
        }

        public static Vector3 TransformModelDirection(NativeOrbResult result, Vector3 direction)
        {
            return OpenCvUnityPoseConverter.TransformModelDirection(result, direction);
        }

        public static Vector3 CvCameraToUnityCamera(Vector3 point)
        {
            return OpenCvUnityPoseConverter.CvCameraToUnityCamera(point);
        }

        public static Vector3 UndoImageRotation(Vector3 point, int clockwiseDegrees)
        {
            return OpenCvUnityPoseConverter.UndoImageRotation(point, clockwiseDegrees);
        }
    }
}
