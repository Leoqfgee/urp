using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Urp.ArDemo.Calibration;

namespace Urp.ArDemo.Tests
{
    public sealed class OrbStateMachineTests
    {
        private const string ProfilePath =
            "Assets/Objects/CoconutBottle/Profiles/CoconutBottleRepairProfile.asset";

        private GameObject cameraObject;
        private GameObject rootObject;
        private GameObject alignmentObject;
        private GameObject controllerObject;
        private OrbImageTrackingController controller;
        private Transform body;
        private Transform cap;
        private Transform pair;

        [SetUp]
        public void SetUp()
        {
            RestorationObjectProfile profile =
                AssetDatabase.LoadAssetAtPath<RestorationObjectProfile>(ProfilePath);
            Assert.That(profile, Is.Not.Null);

            cameraObject = new GameObject("State Machine Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            rootObject = new GameObject("TrackedBottleRoot");
            alignmentObject = new GameObject("ModelCoordinateAlignment");
            alignmentObject.transform.SetParent(rootObject.transform, false);
            controllerObject = new GameObject("State Machine Test Controller");
            controller = controllerObject.AddComponent<OrbImageTrackingController>();
            SetPrivateField("arCamera", camera);
            SetPrivateField("trackedObjectPoseRoot", rootObject.transform);
            SetPrivateField("modelCoordinateAlignment", alignmentObject.transform);
            controller.SetProfile(profile);
            controller.SetTrackingEnabled(true);
            SetPrivateField("registrationConfirmationFrames", 3);
            SetPrivateField("maximumInitialCorrectionMeters", 10f);

            body = GetPrivateField<Transform>("registeredReferenceModel");
            cap = GetPrivateField<Transform>("registeredRepairPart");
            pair = GetPrivateField<Transform>("registeredBottlePairRoot");
            Assert.That(body, Is.Not.Null);
            Assert.That(cap, Is.Not.Null);
            Assert.That(pair, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        [Test]
        public void PreAlignmentFrontIsActualPrintedFront()
        {
            RepairCalibrationProfile calibration =
                GetPrivateField<RepairCalibrationProfile>("calibration");
            Vector3 printedFront = body.TransformDirection(
                (calibration.mouthFrontInModel - calibration.mouthCenterInModel).normalized);
            Vector3 bottleUp = body.TransformDirection(
                (calibration.mouthCenterInModel - calibration.neckAxisPointInModel).normalized);

            Assert.That(
                Vector3.Dot(printedFront, -cameraObject.transform.forward),
                Is.GreaterThan(0.99f));
            Assert.That(
                Vector3.Dot(bottleUp, cameraObject.transform.up),
                Is.GreaterThan(0.99f));
            Assert.That(
                Vector3.Angle(printedFront, -cameraObject.transform.forward),
                Is.LessThan(2f));
            Assert.That(
                Vector3.Angle(bottleUp, cameraObject.transform.up),
                Is.LessThan(2f));
        }

        [Test]
        public void FirstGlobalAcquisitionHasNoPosePrior()
        {
            MethodInfo buildPrior = typeof(OrbImageTrackingController).GetMethod(
                "TryBuildCurrentPosePrior",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(buildPrior, Is.Not.Null);
            object[] searchingArguments = { 0, null };
            Assert.That(
                (bool)buildPrior.Invoke(controller, searchingArguments),
                Is.False,
                "Visual PreAlignment must never become the first-acquisition pose prior.");

            Vector3 stablePosition = new Vector3(0.08f, -0.03f, 0.62f);
            Quaternion stableRotation = Quaternion.Euler(24f, 37f, -12f);
            ApplyReliablePose(stablePosition, stableRotation);
            ApplyReliablePose(stablePosition, stableRotation);
            Assert.That(ApplyReliablePose(stablePosition, stableRotation), Is.True);

            object[] registeredArguments = { 0, null };
            Assert.That(
                (bool)buildPrior.Invoke(controller, registeredArguments),
                Is.True,
                "Only a quality/stability-approved PnP pose may guide the next frame.");
        }

        [Test]
        public void PreStartStablePoseIsActuallyApplied()
        {
            Vector3 stablePosition = new Vector3(0.08f, -0.03f, 0.62f);
            Quaternion stableRotation = Quaternion.Euler(24f, 37f, -12f);
            Matrix4x4 capRelativeBefore =
                pair.worldToLocalMatrix * cap.localToWorldMatrix;

            Assert.That(ApplyReliablePose(stablePosition, stableRotation), Is.False);
            Assert.That(ApplyReliablePose(stablePosition, stableRotation), Is.False);
            Assert.That(ApplyReliablePose(stablePosition, stableRotation), Is.True);

            Assert.That(controller.IsRigidRegistrationEstablished, Is.True);
            Assert.That(
                controller.State,
                Is.EqualTo(OrbImageTrackingController.TrackingState.ReadyForRepair));
            Assert.That(
                Vector3.Distance(rootObject.transform.position, stablePosition),
                Is.LessThan(0.00001f));
            Assert.That(
                Quaternion.Angle(rootObject.transform.rotation, stableRotation),
                Is.LessThan(0.01f));
            Assert.That(AllVisible(body), Is.True);
            Assert.That(AllVisible(cap), Is.True);
            AssertMatrixUnchanged(
                capRelativeBefore,
                pair.worldToLocalMatrix * cap.localToWorldMatrix,
                "C relative to BottleRepairRoot");

            Matrix4x4 capRelativeBeforeUpdate =
                pair.worldToLocalMatrix * cap.localToWorldMatrix;
            Vector3 rootBeforeUpdate = rootObject.transform.position;
            Assert.That(
                ApplyReliablePose(
                    stablePosition + new Vector3(0.005f, 0f, 0f),
                    stableRotation * Quaternion.Euler(0f, 2f, 0f)),
                Is.True);
            Assert.That(
                Vector3.Distance(rootBeforeUpdate, rootObject.transform.position),
                Is.GreaterThan(0.000001f));
            AssertMatrixUnchanged(
                capRelativeBeforeUpdate,
                pair.worldToLocalMatrix * cap.localToWorldMatrix,
                "C rigid relationship during pre-Start tracking");
        }

        [Test]
        public void StartDoesNotChangeRigidPose()
        {
            Vector3 stablePosition = new Vector3(0.08f, -0.03f, 0.62f);
            Quaternion stableRotation = Quaternion.Euler(24f, 37f, -12f);
            ApplyReliablePose(stablePosition, stableRotation);
            ApplyReliablePose(stablePosition, stableRotation);
            Assert.That(ApplyReliablePose(stablePosition, stableRotation), Is.True);

            Matrix4x4 rootBefore = rootObject.transform.localToWorldMatrix;
            Matrix4x4 pairBefore = pair.localToWorldMatrix;
            Matrix4x4 bodyBefore = body.localToWorldMatrix;
            Matrix4x4 capBefore = cap.localToWorldMatrix;
            Material[][] capMaterialsBefore = cap
                .GetComponentsInChildren<Renderer>(true)
                .Select(renderer => renderer.sharedMaterials)
                .ToArray();

            controller.StartRecognition();

            Assert.That(
                controller.State,
                Is.EqualTo(OrbImageTrackingController.TrackingState.Repair));
            AssertMatrixUnchanged(rootBefore, rootObject.transform.localToWorldMatrix, "root");
            AssertMatrixUnchanged(pairBefore, pair.localToWorldMatrix, "pair");
            AssertMatrixUnchanged(bodyBefore, body.localToWorldMatrix, "B");
            AssertMatrixUnchanged(capBefore, cap.localToWorldMatrix, "C");
            Assert.That(AllPaperOnly(body), Is.True);
            Assert.That(AllVisible(cap), Is.True);
            Assert.That(PaperOcclusionRegistry.IsEnabled, Is.True);

            Renderer[] capRenderers = cap.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0;
                 rendererIndex < capRenderers.Length;
                 rendererIndex++)
            {
                CollectionAssert.AreEqual(
                    capMaterialsBefore[rendererIndex],
                    capRenderers[rendererIndex].sharedMaterials,
                    "Start must not replace C materials.");
            }
        }

        [Test]
        public void StartDoesNotChangeCMatrix()
        {
            Vector3 position = new Vector3(0.08f, -0.03f, 0.62f);
            Quaternion rotation = Quaternion.Euler(24f, 37f, -12f);
            ApplyReliablePose(position, rotation);
            ApplyReliablePose(position, rotation);
            ApplyReliablePose(position, rotation);
            Matrix4x4 before = cap.localToWorldMatrix;
            controller.StartRecognition();
            AssertMatrixUnchanged(before, cap.localToWorldMatrix, "C Start matrix");
        }

        [Test]
        public void DisplayDiagnosticFailureDoesNotBlockPoseApplication()
        {
            Vector3 stablePosition = new Vector3(0.04f, -0.01f, 0.58f);
            Quaternion stableRotation = Quaternion.Euler(8f, 19f, -4f);
            PoseConsistencyResult displayWarn = PassingConsistency(8f);

            Assert.That(ApplyReliablePose(stablePosition, stableRotation, displayWarn), Is.False);
            Assert.That(ApplyReliablePose(stablePosition, stableRotation, displayWarn), Is.False);
            Assert.That(ApplyReliablePose(stablePosition, stableRotation, displayWarn), Is.True);

            Assert.That(controller.IsPoseAppliedToRigidRoot, Is.True);
            Assert.That(controller.IsPoseChainVerified, Is.True);
            Assert.That(controller.IsHierarchyTransformRoundTripVerified, Is.True);
            Assert.That(controller.IsModelRegistrationVerified, Is.True);
            Assert.That(controller.CanStartRepair, Is.True);
            Assert.That(controller.State,
                Is.EqualTo(OrbImageTrackingController.TrackingState.ReadyForRepair));
            Assert.That(
                Vector3.Distance(rootObject.transform.position, stablePosition),
                Is.LessThan(0.00001f));
        }

        [Test]
        public void GuidedAcceptedPoseCanBecomeReadyLatch()
        {
            Vector3 position = new Vector3(0.03f, -0.01f, 0.57f);
            Quaternion rotation = Quaternion.Euler(6f, 14f, -3f);
            ApplyReliablePose(position, rotation);
            ApplyReliablePose(position, rotation);
            Assert.That(ApplyReliablePose(position, rotation), Is.True);
            Assert.That(controller.HasVerifiedReadyPoseSinceReset, Is.True);
            Assert.That(controller.CanStartRepair, Is.True);
        }

        [Test]
        public void SingleTransientConsistencyFailureDoesNotClearReadyLatch()
        {
            Vector3 position = new Vector3(0.03f, -0.01f, 0.57f);
            Quaternion rotation = Quaternion.Euler(6f, 14f, -3f);
            ApplyReliablePose(position, rotation);
            ApplyReliablePose(position, rotation);
            Assert.That(ApplyReliablePose(position, rotation), Is.True);

            PoseConsistencyResult transient = new PoseConsistencyResult(
                1.4f, 1.0f, 1.0f, 1.0f, 30, false, false);
            Assert.That(ApplyReliablePose(position, rotation, transient), Is.True);
            Assert.That(controller.HasVerifiedReadyPoseSinceReset, Is.True);
            Assert.That(controller.CanStartRepair, Is.True);
        }

        [Test]
        public void SustainedLossClearsReadyLatch()
        {
            Vector3 position = new Vector3(0.03f, -0.01f, 0.57f);
            Quaternion rotation = Quaternion.Euler(6f, 14f, -3f);
            ApplyReliablePose(position, rotation);
            ApplyReliablePose(position, rotation);
            Assert.That(ApplyReliablePose(position, rotation), Is.True);
            SetPrivateField("lastValidPoseTime", float.NegativeInfinity);
            InvokePrivate("HandleTrackingLoss");
            Assert.That(controller.HasVerifiedReadyPoseSinceReset, Is.False);
            Assert.That(controller.CanStartRepair, Is.False);
        }

        [Test]
        public void StartGateReportsExactBlockingFlag()
        {
            string diagnostic = (string)InvokePrivate("BuildStartGateDiagnostic");
            StringAssert.Contains("blockedBy=registered", diagnostic);
            StringAssert.Contains("readyLatch=False", diagnostic);
            StringAssert.Contains("lastReliablePoseAge=", diagnostic);
        }

        [Test]
        public void FailedPoseChainBlocksStartButKeepsPreview()
        {
            Vector3 stablePosition = new Vector3(0.06f, -0.02f, 0.61f);
            Quaternion stableRotation = Quaternion.Euler(12f, 25f, -6f);
            PoseConsistencyResult failedPoseChain = new PoseConsistencyResult(
                1.2f,
                2.5f,
                0.01f,
                8f,
                12,
                false,
                true);

            Assert.That(
                ApplyReliablePose(stablePosition, stableRotation, failedPoseChain),
                Is.False);
            Assert.That(
                ApplyReliablePose(stablePosition, stableRotation, failedPoseChain),
                Is.False);
            Assert.That(
                ApplyReliablePose(stablePosition, stableRotation, failedPoseChain),
                Is.True);

            Assert.That(controller.IsRigidRegistrationEstablished, Is.True);
            Assert.That(controller.IsPoseAppliedToRigidRoot, Is.True);
            Assert.That(controller.CanStartRepair, Is.False);
            Assert.That(controller.State,
                Is.EqualTo(OrbImageTrackingController.TrackingState.PoseValidating));
            Assert.That(AllVisible(body), Is.True);
            Assert.That(AllVisible(cap), Is.True);
            Assert.That(
                Vector3.Distance(rootObject.transform.position, stablePosition),
                Is.LessThan(0.00001f));

            Matrix4x4 rootBefore = rootObject.transform.localToWorldMatrix;
            Matrix4x4 capBefore = cap.localToWorldMatrix;
            controller.StartRecognition();
            Assert.That(controller.IsRepairMode, Is.False);
            Assert.That(AllVisible(body), Is.True);
            Assert.That(AllVisible(cap), Is.True);
            AssertMatrixUnchanged(rootBefore, rootObject.transform.localToWorldMatrix, "root");
            AssertMatrixUnchanged(capBefore, cap.localToWorldMatrix, "C");
        }

        private bool ApplyReliablePose(
            Vector3 position,
            Quaternion rotation,
            PoseConsistencyResult? consistency = null)
        {
            MethodInfo method = typeof(OrbImageTrackingController).GetMethod(
                "TryApplyReliablePose",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments =
            {
                position,
                rotation,
                PassingNativePose(),
                consistency ?? PassingConsistency(0f),
                null
            };
            return (bool)method.Invoke(controller, arguments);
        }

        private static PoseConsistencyResult PassingConsistency(float displayRms) =>
            new PoseConsistencyResult(
                1.0f,
                0.01f,
                0.01f,
                displayRms,
                12,
                true,
                true);

        private static Urp.ArDemo.Native.NativeOrbResult PassingNativePose() =>
            new Urp.ArDemo.Native.NativeOrbResult
            {
                poseValid = 1,
                poseInliers = 48,
                uniqueMatches = 60,
                inlierRatio = 0.8f,
                reprojectionError = 1.4f,
                coverageX = 0.42f,
                coverageY = 0.72f,
                occupiedGridCells = 9
            };

        private void SetPrivateField<T>(string name, T value)
        {
            FieldInfo field = typeof(OrbImageTrackingController).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(controller, value);
        }

        private T GetPrivateField<T>(string name)
        {
            FieldInfo field = typeof(OrbImageTrackingController).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(controller);
        }

        private object InvokePrivate(string name)
        {
            MethodInfo method = typeof(OrbImageTrackingController).GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            return method.Invoke(controller, null);
        }

        private static bool AllVisible(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            return renderers.Length > 0 && renderers.All(renderer =>
                renderer.enabled
                && !renderer.forceRenderingOff
                && renderer.gameObject.activeInHierarchy);
        }

        private static bool AllHidden(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            return renderers.Length > 0 && renderers.All(renderer =>
                !renderer.enabled && renderer.forceRenderingOff);
        }

        private static bool AllPaperOnly(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            return renderers.Length > 0 && renderers.All(renderer =>
                !renderer.enabled && !renderer.forceRenderingOff);
        }

        private static void AssertMatrixUnchanged(
            Matrix4x4 before,
            Matrix4x4 after,
            string label)
        {
            Vector3 beforePosition = before.GetColumn(3);
            Vector3 afterPosition = after.GetColumn(3);
            Assert.That(
                Vector3.Distance(beforePosition, afterPosition),
                Is.LessThan(0.00001f),
                label + " position");
            Assert.That(
                Quaternion.Angle(before.rotation, after.rotation),
                Is.LessThan(0.01f),
                label + " rotation");
            Assert.That(
                Vector3.Distance(MatrixScale(before), MatrixScale(after)),
                Is.LessThan(0.000001f),
                label + " scale");
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    Assert.That(
                        Mathf.Abs(before[row, column] - after[row, column]),
                        Is.LessThan(0.00001f),
                        $"{label} matrix[{row},{column}]");
                }
            }
        }

        private static Vector3 MatrixScale(Matrix4x4 matrix)
        {
            return new Vector3(
                matrix.GetColumn(0).magnitude,
                matrix.GetColumn(1).magnitude,
                matrix.GetColumn(2).magnitude);
        }
    }
}
