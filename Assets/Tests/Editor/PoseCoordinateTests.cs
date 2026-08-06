using System;
using NUnit.Framework;
using UnityEngine;
using Urp.ArDemo.Calibration;
using Urp.ArDemo.Native;

namespace Urp.ArDemo.Tests
{
    public sealed class PoseCoordinateTests
    {
        [Test]
        public void NativeImageRotationRoundTripsAtAllRightAngles()
        {
            Vector3[] directions =
            {
                Vector3.right,
                Vector3.up,
                Vector3.forward,
                new Vector3(0.371f, -0.812f, 0.451f).normalized,
                new Vector3(-0.622f, 0.177f, 0.763f).normalized
            };
            foreach (int angle in new[] { 0, 90, 180, 270 })
            {
                foreach (Vector3 direction in directions)
                {
                    Vector3 restored = OpenCvUnityPoseConverter.UndoImageRotation(
                        OpenCvUnityPoseConverter.RotateForNativeImage(
                            direction,
                            angle),
                        angle);
                    Assert.That(
                        Vector3.Distance(direction, restored),
                        Is.LessThan(0.00001f),
                        $"rotation={angle}, direction={direction}");
                }
            }
        }

        [Test]
        public void PortraitTrackingFrameKeepsCanonicalYUpright()
        {
            GameObject cameraObject = new GameObject("Pose Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            RepairCalibrationProfile calibration = CreateCalibration();
            NativeOrbResult uprightPose = CreateUprightPose(3f);
            try
            {
                foreach (int angle in new[] { 0, 90, 180, 270 })
                {
                    Assert.That(
                        OpenCvUnityPoseConverter.TryGetObjectPose(
                            uprightPose,
                            angle,
                            camera,
                            calibration,
                            out _,
                            out Quaternion rotation),
                        Is.True);
                    Assert.That(
                        Vector3.Angle(rotation * Vector3.up, Vector3.up),
                        Is.LessThan(0.00001f),
                        $"portrait tracking frame rolled at {angle} degrees");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(calibration);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void LandmarkRegistrationDerivesImportedHierarchyMatrix()
        {
            GameObject rootObject = new GameObject("TrackedBottleRoot");
            GameObject alignmentObject = new GameObject("ModelCoordinateAlignment");
            GameObject pairObject = new GameObject("BottleRepairRoot");
            GameObject bodyObject = new GameObject("DamagedBottleB");
            alignmentObject.transform.SetParent(rootObject.transform, false);
            pairObject.transform.SetParent(alignmentObject.transform, false);
            pairObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            pairObject.transform.localScale = Vector3.one;
            bodyObject.transform.SetParent(pairObject.transform, false);
            RepairCalibrationProfile calibration = CreateCalibration();
            try
            {
                Assert.That(
                    CanonicalFrameRegistration.TryDerive(
                        rootObject.transform,
                        alignmentObject.transform,
                        bodyObject.transform,
                        calibration,
                        out CanonicalFrameRegistration.Result result,
                        out string reason),
                    Is.True,
                    reason);
                Assert.That(result.landmarkRms, Is.LessThan(0.00001f));
                Assert.That(
                    Quaternion.Angle(result.rotation, Quaternion.Euler(90f, 0f, 0f)),
                    Is.LessThan(0.0001f));
                Assert.That(Vector3.Distance(result.scale, Vector3.one), Is.LessThan(0.00001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(calibration);
                UnityEngine.Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void OpenCvUnityCrossProjectionUsesSameInliers()
        {
            GameObject cameraObject = new GameObject("Cross Projection Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 10f;
            RenderTexture target = new RenderTexture(640, 480, 24);
            camera.targetTexture = target;

            GameObject rootObject = new GameObject("TrackedBottleRoot");
            GameObject alignmentObject = new GameObject("ModelCoordinateAlignment");
            GameObject pairObject = new GameObject("BottleRepairRoot");
            GameObject bodyObject = new GameObject("DamagedBottleB");
            alignmentObject.transform.SetParent(rootObject.transform, false);
            pairObject.transform.SetParent(alignmentObject.transform, false);
            pairObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            bodyObject.transform.SetParent(pairObject.transform, false);
            RepairCalibrationProfile calibration = CreateCalibration();
            try
            {
                Assert.That(
                    CanonicalFrameRegistration.TryDerive(
                        rootObject.transform,
                        alignmentObject.transform,
                        bodyObject.transform,
                        calibration,
                        out CanonicalFrameRegistration.Result alignment,
                        out string alignmentReason),
                    Is.True,
                    alignmentReason);
                alignmentObject.transform.localPosition = alignment.position;
                alignmentObject.transform.localRotation = alignment.rotation;
                alignmentObject.transform.localScale = alignment.scale;

                NativeOrbResult pose = CreateUprightPose(3f);
                Assert.That(
                    OpenCvUnityPoseConverter.TryGetObjectPose(
                        pose, 90, camera, calibration,
                        out Vector3 position, out Quaternion rotation),
                    Is.True);
                Vector3[] model =
                {
                    new Vector3(-0.2f, -0.8f, 0.05f),
                    new Vector3(0.2f, -0.8f, 0.05f),
                    new Vector3(-0.15f, -0.2f, -0.04f),
                    new Vector3(0.15f, -0.2f, -0.04f),
                    new Vector3(0f, 0.05f, 0f)
                };
                const float fx = 520f;
                const float fy = 520f;
                const float cx = 320f;
                const float cy = 240f;
                Vector2[] pixels = new Vector2[model.Length];
                for (int i = 0; i < model.Length; i++)
                {
                    Vector3 cv = OpenCvUnityPoseConverter.TransformModelPoint(pose, model[i]);
                    pixels[i] = new Vector2(
                        fx * cv.x / cv.z + cx,
                        fy * cv.y / cv.z + cy);
                }
                NativeInlierSet inliers = new NativeInlierSet(
                    model,
                    pixels,
                    640,
                    480,
                    new CameraIntrinsics(fx, fy, cx, cy));

                Assert.That(
                    UnityPoseConsistencyGate.TryEvaluate(
                        camera,
                        inliers,
                        position,
                        rotation,
                        rootObject.transform,
                        bodyObject.transform,
                        calibration,
                        5f,
                        out UnityPoseConsistencyResult consistency,
                        out string reason),
                    Is.True,
                    reason);
                Assert.That(consistency.rmsPixels, Is.LessThan(0.001f));

                Assert.That(
                    UnityPoseConsistencyGate.TryEvaluate(
                        camera,
                        inliers,
                        position,
                        Quaternion.Euler(0f, 0f, -90f) * rotation,
                        rootObject.transform,
                        bodyObject.transform,
                        calibration,
                        5f,
                        out UnityPoseConsistencyResult bad,
                        out _),
                    Is.False);
                Assert.That(bad.rmsPixels, Is.GreaterThan(20f));
                Debug.Log(
                    $"UNITY_OPENCV_CROSS_PROJECTION_OK rms={consistency.rmsPixels:F6}px "
                    + $"residual90Rms={bad.rmsPixels:F3}px samples={consistency.samples}");
            }
            finally
            {
                camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(calibration);
                UnityEngine.Object.DestroyImmediate(rootObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static RepairCalibrationProfile CreateCalibration()
        {
            RepairCalibrationProfile calibration =
                ScriptableObject.CreateInstance<RepairCalibrationProfile>();
            calibration.objectOriginInModel = Vector3.zero;
            calibration.mouthCenterInModel = new Vector3(0f, 0.05882353f, 0f);
            calibration.mouthRightInModel = new Vector3(0.1f, 0.05882353f, 0f);
            calibration.mouthFrontInModel = new Vector3(0f, 0.05882353f, 0.1f);
            calibration.neckAxisPointInModel = new Vector3(0f, -0.14117648f, 0f);
            calibration.metersPerModelUnit = 0.17f;
            return calibration;
        }

        private static NativeOrbResult CreateUprightPose(float depth)
        {
            return new NativeOrbResult
            {
                poseValid = 1,
                r00 = -1f,
                r11 = -1f,
                r22 = 1f,
                tvecZ = depth
            };
        }
    }
}
