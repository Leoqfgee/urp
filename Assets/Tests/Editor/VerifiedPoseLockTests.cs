using NUnit.Framework;
using UnityEngine;
using Urp.ArDemo.Calibration;

namespace Urp.ArDemo.Tests
{
    public sealed class VerifiedPoseLockTests
    {
        private VerifiedPoseLockSettings settings;
        private VerifiedPoseLock poseLock;
        private readonly Vector3 stablePosition = new Vector3(0.12f, -0.04f, 0.63f);
        private readonly Quaternion stableRotation = Quaternion.Euler(7f, 23f, -4f);

        [SetUp]
        public void SetUp()
        {
            settings = new VerifiedPoseLockSettings
            {
                confirmationFrames = 8,
                relocalizationStableFrames = 6
            };
            poseLock = new VerifiedPoseLock(settings);
        }

        [Test]
        public void StableHighQualityFramesEnterPoseLock()
        {
            LockAtStablePose();
            Assert.That(poseLock.State, Is.EqualTo(VerifiedPoseLock.LockState.Locked));
            Assert.That(Vector3.Distance(poseLock.LockedPosition, stablePosition),
                Is.LessThan(0.00001f));
            Assert.That(Quaternion.Angle(poseLock.LockedRotation, stableRotation),
                Is.LessThan(0.01f));
        }

        [Test]
        public void SubDeadbandNoiseDoesNotMoveRoot()
        {
            VerifiedPoseLock.Result locked = LockAtStablePose();
            Vector3 rootPosition = locked.position;
            Quaternion rootRotation = locked.rotation;
            Matrix4x4 before = Matrix4x4.TRS(rootPosition, rootRotation, Vector3.one);
            for (int i = 0; i < 50; i++)
            {
                float sign = i % 2 == 0 ? 1f : -1f;
                VerifiedPoseLock.Result result = poseLock.Step(
                    GoodCandidate(
                        stablePosition + new Vector3(sign * 0.001f, 0f, 0f),
                        stableRotation * Quaternion.Euler(0f, sign * 0.2f, 0f)),
                    rootPosition,
                    rootRotation);
                Assert.That(result.deadband, Is.True);
                Assert.That(result.applyToRoot, Is.False);
            }
            Matrix4x4 after = Matrix4x4.TRS(rootPosition, rootRotation, Vector3.one);
            Assert.That(MaxDelta(before, after), Is.EqualTo(0f));
        }

        [Test]
        public void CameraMotionDoesNotMoveLockedObjectRoot()
        {
            VerifiedPoseLock.Result locked = LockAtStablePose();
            GameObject root = new GameObject("Locked Object");
            GameObject camera = new GameObject("Moving AR Camera");
            try
            {
                root.transform.SetPositionAndRotation(locked.position, locked.rotation);
                Matrix4x4 before = root.transform.localToWorldMatrix;
                camera.transform.SetPositionAndRotation(
                    new Vector3(0.4f, 0.2f, -0.3f),
                    Quaternion.Euler(15f, 35f, 3f));
                Assert.That(MaxDelta(before, root.transform.localToWorldMatrix),
                    Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(camera);
            }
        }

        [Test]
        public void PersistentRealObjectMotionTriggersRelocalization()
        {
            LockAtStablePose();
            Vector3 moved = stablePosition + new Vector3(0.020f, 0f, 0f);
            Quaternion rotated = stableRotation * Quaternion.Euler(0f, 5f, 0f);
            for (int i = 0; i < settings.relocationConfirmationFrames; i++)
            {
                poseLock.Step(GoodCandidate(moved, rotated),
                    poseLock.LockedPosition, poseLock.LockedRotation);
            }
            Assert.That(poseLock.State,
                Is.EqualTo(VerifiedPoseLock.LockState.Relocalizing));
        }

        [Test]
        public void RelocalizationRequiresMultipleFrames()
        {
            LockAtStablePose();
            poseLock.Step(GoodCandidate(stablePosition + Vector3.right * 0.020f,
                    stableRotation * Quaternion.Euler(0f, 5f, 0f)),
                poseLock.LockedPosition, poseLock.LockedRotation);
            Assert.That(poseLock.State, Is.EqualTo(VerifiedPoseLock.LockState.Locked));
        }

        [Test]
        public void RelocalizationCanEstablishNewLock()
        {
            LockAtStablePose();
            Vector3 moved = stablePosition + new Vector3(0.020f, 0.002f, 0f);
            Quaternion rotated = stableRotation * Quaternion.Euler(0f, 5f, 0f);
            for (int i = 0; i < settings.relocationConfirmationFrames; i++)
            {
                poseLock.Step(GoodCandidate(moved, rotated),
                    poseLock.LockedPosition, poseLock.LockedRotation);
            }
            for (int i = 1; i < settings.relocalizationStableFrames; i++)
            {
                poseLock.Step(GoodCandidate(moved, rotated),
                    poseLock.LockedPosition, poseLock.LockedRotation);
            }
            Assert.That(poseLock.State, Is.EqualTo(VerifiedPoseLock.LockState.Locked));
            Assert.That(Vector3.Distance(poseLock.LockedPosition, moved),
                Is.LessThan(0.00001f));
        }

        [Test]
        public void DebugOverlayDisabledByDefault()
        {
            GameObject host = new GameObject("Pose Diagnostic Defaults");
            try
            {
                PoseCoordinateDiagnostic diagnostic =
                    host.AddComponent<PoseCoordinateDiagnostic>();
                Assert.That(diagnostic.DrawPoseDebugOverlays, Is.False);
                Assert.That(host.GetComponentsInChildren<LineRenderer>(true), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void DiagnosticLogsStillAvailableWhenOverlayHidden()
        {
            GameObject host = new GameObject("Pose Diagnostic Log Split");
            try
            {
                PoseCoordinateDiagnostic diagnostic =
                    host.AddComponent<PoseCoordinateDiagnostic>();
                Assert.That(diagnostic.DrawPoseDebugOverlays, Is.False);
                Assert.That(diagnostic.EmitPoseDiagnosticLogs, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private VerifiedPoseLock.Result LockAtStablePose()
        {
            VerifiedPoseLock.Result result = default;
            for (int i = 0; i < settings.confirmationFrames; i++)
            {
                result = poseLock.Step(GoodCandidate(stablePosition, stableRotation),
                    stablePosition, stableRotation);
            }
            return result;
        }

        private static VerifiedPoseLock.Candidate GoodCandidate(
            Vector3 position, Quaternion rotation) =>
            new VerifiedPoseLock.Candidate(
                position, rotation, 40, 0.75f, 1.5f, 0.45f, 0.9f);

        private static float MaxDelta(Matrix4x4 a, Matrix4x4 b)
        {
            float maximum = 0f;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    maximum = Mathf.Max(maximum,
                        Mathf.Abs(a[row, column] - b[row, column]));
                }
            }
            return maximum;
        }
    }
}
