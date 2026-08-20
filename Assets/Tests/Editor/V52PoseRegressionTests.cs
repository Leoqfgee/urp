using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Urp.ArDemo.Calibration;
using Urp.ArDemo.Native;

namespace Urp.ArDemo.Tests.Editor
{
    public sealed class V52PoseRegressionTests
    {
        [Test]
        public void V52DoesNotDropV50AcceptedPoseUpdates()
        {
            GameObject host = new GameObject("v52-pose-regression");
            try
            {
                OrbImageTrackingController controller =
                    host.AddComponent<OrbImageTrackingController>();
                NativeOrbResult accepted = AcceptedObliquePose();
                MethodInfo method = typeof(OrbImageTrackingController).GetMethod(
                    "CalculatePoseFusionConfidence",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(method);
                float confidence = (float)method.Invoke(
                    controller,
                    new object[] { accepted, Vector3.zero, Quaternion.identity });
                float v50BaselineConfidence = ConfidenceWeightedPoseFusion.Score(
                    accepted,
                    0.35f,
                    3.0f,
                    0.05f,
                    0.18f,
                    0f,
                    0f);
                Assert.GreaterOrEqual(confidence, 0.30f);
                Assert.GreaterOrEqual(
                    confidence,
                    v50BaselineConfidence,
                    "v52 must never down-weight a pose accepted by the v50 fusion path.");

                ConfidenceWeightedPoseFusion.Result result =
                    ConfidenceWeightedPoseFusion.Step(
                        Vector3.zero,
                        Quaternion.identity,
                        new Vector3(0.004f, 0f, 0f),
                        Quaternion.Euler(0f, 18f, 0f),
                        confidence,
                        0.20f,
                        0.18f,
                        0.14f,
                        0.14f);
                Assert.IsFalse(result.held);
                Assert.Greater(result.positionAlpha, 0f);
                Assert.Greater(result.rotationAlpha, 0f);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ObliqueAcceptedPoseStillUpdatesRoot()
        {
            GameObject host = new GameObject("v52-oblique-regression");
            try
            {
                OrbImageTrackingController controller =
                    host.AddComponent<OrbImageTrackingController>();
                NativeOrbResult accepted = AcceptedObliquePose();
                MethodInfo method = typeof(OrbImageTrackingController).GetMethod(
                    "CalculatePoseFusionConfidence",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                float confidence = (float)method.Invoke(
                    controller,
                    new object[] { accepted, Vector3.zero, Quaternion.identity });

                Vector3 position = Vector3.zero;
                Quaternion rotation = Quaternion.identity;
                Vector3 targetPosition = new Vector3(0.008f, 0.002f, -0.003f);
                Quaternion targetRotation = Quaternion.Euler(12f, 28f, -4f);
                float initialAngle = Quaternion.Angle(rotation, targetRotation);
                for (int frame = 0; frame < 8; frame++)
                {
                    ConfidenceWeightedPoseFusion.Result result =
                        ConfidenceWeightedPoseFusion.Step(
                            position,
                            rotation,
                            targetPosition,
                            targetRotation,
                            confidence,
                            0.20f,
                            0.18f,
                            0.14f,
                            0.14f);
                    Assert.IsFalse(result.held);
                    position = result.position;
                    rotation = result.rotation;
                }

                Assert.Greater(position.magnitude, 0f);
                Assert.Less(Quaternion.Angle(rotation, targetRotation), initialAngle);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static NativeOrbResult AcceptedObliquePose()
        {
            return new NativeOrbResult
            {
                poseValid = 1,
                uniqueMatches = 58,
                poseInliers = 36,
                inlierRatio = 0.62f,
                reprojectionError = 1.85f,
                reprojectionMax = 4.2f,
                coverageX = 0.052f,
                coverageY = 0.181f,
                occupiedGridCells = 4,
                rejectionCode = 0
            };
        }
    }
}
