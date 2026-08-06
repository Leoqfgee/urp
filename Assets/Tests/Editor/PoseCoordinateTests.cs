using NUnit.Framework;
using UnityEditor;
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
                        Is.LessThan(0.00001f));
                }
            }
        }

        [Test] public void PnpUnityRoundTrip_0Deg() => AssertRoundTrip(0);
        [Test] public void PnpUnityRoundTrip_90DegPortrait() => AssertRoundTrip(90);
        [Test] public void PnpUnityRoundTrip_180Deg() => AssertRoundTrip(180);
        [Test] public void PnpUnityRoundTrip_270Deg() => AssertRoundTrip(270);

        [Test]
        public void ConfidenceWeightedFusionTracksHighConfidenceSixDof()
        {
            Vector3 start = new Vector3(0f, 0f, 0.6f);
            Quaternion startRotation = Quaternion.identity;
            Vector3 candidate = new Vector3(0.04f, -0.02f, 0.64f);
            Quaternion candidateRotation = Quaternion.Euler(18f, 27f, -9f);
            ConfidenceWeightedPoseFusion.Result result =
                ConfidenceWeightedPoseFusion.Step(
                    start,
                    startRotation,
                    candidate,
                    candidateRotation,
                    0.95f,
                    0.20f,
                    0.18f,
                    0.14f,
                    0.14f);
            Assert.That(result.held, Is.False);
            Assert.That(result.positionAlpha, Is.GreaterThan(0.85f));
            Assert.That(result.rotationAlpha, Is.GreaterThan(0.84f));
            Assert.That(Vector3.Distance(result.position, candidate), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(result.rotation, candidateRotation), Is.LessThan(6f));
        }

        [Test]
        public void ConfidenceWeightedFusionHoldsLowConfidencePose()
        {
            Vector3 start = new Vector3(0f, 0f, 0.6f);
            Quaternion startRotation = Quaternion.Euler(1f, 2f, 3f);
            ConfidenceWeightedPoseFusion.Result result =
                ConfidenceWeightedPoseFusion.Step(
                    start,
                    startRotation,
                    new Vector3(0.1f, 0.1f, 0.8f),
                    Quaternion.Euler(40f, 50f, 60f),
                    0.20f,
                    0.20f,
                    0.18f,
                    0.14f,
                    0.14f);
            Assert.That(result.held, Is.True);
            Assert.That(result.position, Is.EqualTo(start));
            Assert.That(Quaternion.Angle(result.rotation, startRotation), Is.LessThan(0.001f));
        }

        [Test]
        public void RenderedHierarchyRoundTrip()
        {
            using (PoseFixture fixture = new PoseFixture())
            {
                NativeOrbResult pose = CreateOrientedPose(90);
                PoseConsistencyResult good = fixture.Evaluate(pose);
                Assert.That(good.poseChainPassed, Is.True);
                Assert.That(good.hierarchyTransformRoundTripPassed, Is.True);
                Assert.That(good.poseChainRoundTripRmsPixels, Is.LessThan(0.01f));
                Assert.That(good.hierarchyTransformRoundTripRmsPixels, Is.LessThan(0.01f));

                fixture.Alignment.localRotation *= Quaternion.Euler(0f, 6f, 0f);
                PoseConsistencyResult badHierarchy = fixture.Evaluate(pose);
                Assert.That(badHierarchy.poseChainPassed, Is.True);
                Assert.That(badHierarchy.hierarchyTransformRoundTripPassed, Is.False);
                Assert.That(badHierarchy.hierarchyTransformRoundTripRmsPixels, Is.GreaterThan(1f));
            }
        }

        [Test]
        public void ProductionModelRegistrationArtifactIsIndependentAndNonIdentity()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Calibration/bottle_orb_to_b_registration.json");
            Assert.That(
                ModelRegistrationEvidence.TryParse(
                    asset,
                    out ModelRegistrationEvidence evidence,
                    out string reason),
                Is.True,
                reason);
            Assert.That(evidence.landmark_rms_mm, Is.LessThan(2f));
            Assert.That(evidence.orb_point_to_b_surface_mm.p95_mm, Is.LessThan(12f));
            Assert.That(evidence.front_axis_agreement, Is.GreaterThan(0.995f));
            Assert.That(evidence.up_axis_agreement, Is.GreaterThan(0.995f));
            Assert.That(
                new Vector3(
                    evidence.mouth_center_orb[0],
                    evidence.mouth_center_orb[1],
                    evidence.mouth_center_orb[2]),
                Is.EqualTo(Vector3.zero));
            Assert.That(
                Mathf.Abs(evidence.T_ORB_FROM_B[0] - 1f),
                Is.GreaterThan(0.1f),
                "The two Meshroom reconstructions must not be self-certified as identity.");
        }

        private static void AssertRoundTrip(int rotationClockwise)
        {
            using (PoseFixture fixture = new PoseFixture())
            {
                NativeOrbResult pose = CreateOrientedPose(rotationClockwise);
                PoseConsistencyResult result = fixture.Evaluate(pose);
                Assert.That(result.poseChainPassed, Is.True);
                Assert.That(result.hierarchyTransformRoundTripPassed, Is.True);
                Assert.That(
                    result.poseChainRoundTripRmsPixels,
                    Is.LessThan(0.01f),
                    $"rotation={rotationClockwise}");
                Assert.That(
                    result.hierarchyTransformRoundTripRmsPixels,
                    Is.LessThan(0.01f),
                    $"rotation={rotationClockwise}");
                Debug.Log(
                    $"PNP_UNITY_ROUNDTRIP_{rotationClockwise}_OK "
                    + $"poseRt={result.poseChainRoundTripRmsPixels:F6}px "
                    + $"hierarchy={result.hierarchyTransformRoundTripRmsPixels:F6}px");
            }
        }

        private static NativeOrbResult CreateOrientedPose(int clockwiseDegrees)
        {
            Matrix4x4 rawRotation = Matrix4x4.Rotate(
                Quaternion.Euler(11f, -17f, 7f));
            Vector3 x = OpenCvUnityPoseConverter.RotateForNativeImage(
                rawRotation.GetColumn(0),
                clockwiseDegrees);
            Vector3 y = OpenCvUnityPoseConverter.RotateForNativeImage(
                rawRotation.GetColumn(1),
                clockwiseDegrees);
            Vector3 z = OpenCvUnityPoseConverter.RotateForNativeImage(
                rawRotation.GetColumn(2),
                clockwiseDegrees);
            Vector3 t = OpenCvUnityPoseConverter.RotateForNativeImage(
                new Vector3(0.10f, -0.08f, 3.2f),
                clockwiseDegrees);
            return new NativeOrbResult
            {
                poseValid = 1,
                r00 = x.x, r01 = y.x, r02 = z.x,
                r10 = x.y, r11 = y.y, r12 = z.y,
                r20 = x.z, r21 = y.z, r22 = z.z,
                tvecX = t.x, tvecY = t.y, tvecZ = t.z,
                poseInliers = 6,
                uniqueMatches = 6
            };
        }

        private sealed class PoseFixture : System.IDisposable
        {
            private readonly GameObject cameraObject;
            private readonly GameObject rootObject;
            private readonly GameObject alignmentObject;
            private readonly GameObject pairObject;
            private readonly GameObject bodyObject;
            private readonly RenderTexture target;
            private readonly RepairCalibrationProfile calibration;
            private readonly Camera camera;

            public Transform Alignment => alignmentObject.transform;
            public Transform Root => rootObject.transform;
            public Transform Body => bodyObject.transform;
            public RepairCalibrationProfile Calibration => calibration;

            public PoseFixture()
            {
                cameraObject = new GameObject("Pose Round Trip Camera");
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.transform.SetPositionAndRotation(
                    new Vector3(0.3f, -0.15f, 0.2f),
                    Quaternion.Euler(4f, 13f, -3f));
                target = new RenderTexture(640, 480, 24);
                camera.targetTexture = target;

                rootObject = new GameObject("TrackedBottleRoot");
                alignmentObject = new GameObject("ModelCoordinateAlignment");
                pairObject = new GameObject("BottleRepairRoot");
                bodyObject = new GameObject("DamagedBottleB");
                alignmentObject.transform.SetParent(rootObject.transform, false);
                pairObject.transform.SetParent(alignmentObject.transform, false);
                pairObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                bodyObject.transform.SetParent(pairObject.transform, false);
                calibration = CreateCalibration();
                alignmentObject.transform.localRotation =
                    Quaternion.Euler(90f, 0f, 0f);
            }

            public PoseConsistencyResult Evaluate(NativeOrbResult pose)
            {
                Assert.That(
                    OpenCvUnityPoseConverter.TryGetObjectPose(
                        pose,
                        0,
                        camera,
                        calibration,
                        out Vector3 position,
                        out Quaternion rotation),
                    Is.True);
                NativeInlierSet inliers = BuildInliers(pose);
                bool passed = UnityPoseConsistencyGate.TryEvaluate(
                    camera,
                    pose,
                    inliers,
                    position,
                    rotation,
                    rootObject.transform,
                    bodyObject.transform,
                    calibration,
                    0.25f,
                    0.50f,
                    out PoseConsistencyResult result,
                    out string reason);
                if (!passed)
                {
                    Debug.Log(reason);
                }
                return result;
            }

            private static NativeInlierSet BuildInliers(NativeOrbResult pose)
            {
                Vector3[] model =
                {
                    new Vector3(-0.20f, -0.80f, 0.05f),
                    new Vector3(0.20f, -0.80f, 0.05f),
                    new Vector3(-0.15f, -0.20f, -0.04f),
                    new Vector3(0.15f, -0.20f, -0.04f),
                    new Vector3(0f, 0.05f, 0f),
                    new Vector3(0.08f, -0.45f, 0.11f)
                };
                const float fx = 520f;
                const float fy = 518f;
                const float cx = 320f;
                const float cy = 240f;
                Vector2[] pixels = new Vector2[model.Length];
                for (int i = 0; i < model.Length; i++)
                {
                    Vector3 cv = OpenCvUnityPoseConverter.TransformModelPoint(
                        pose,
                        model[i]);
                    pixels[i] = new Vector2(
                        fx * cv.x / cv.z + cx,
                        fy * cv.y / cv.z + cy);
                }
                return new NativeInlierSet(
                    model,
                    pixels,
                    640,
                    480,
                    new CameraIntrinsics(fx, fy, cx, cy));
            }

            public void Dispose()
            {
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(calibration);
                Object.DestroyImmediate(rootObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        private static RepairCalibrationProfile CreateCalibration()
        {
            RepairCalibrationProfile calibration =
                ScriptableObject.CreateInstance<RepairCalibrationProfile>();
            calibration.objectOriginInModel = Vector3.zero;
            calibration.mouthCenterInModel = Vector3.zero;
            calibration.mouthRightInModel = new Vector3(0.1f, 0f, 0f);
            calibration.mouthFrontInModel = new Vector3(0f, 0f, 0.1f);
            calibration.neckAxisPointInModel = new Vector3(0f, -0.2f, 0f);
            calibration.hasAuthoredBLandmarks = false;
            calibration.metersPerModelUnit = 0.17f;
            return calibration;
        }
    }
}
