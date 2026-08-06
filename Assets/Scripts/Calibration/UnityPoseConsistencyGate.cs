using UnityEngine;
using Urp.ArDemo.Native;

namespace Urp.ArDemo.Calibration
{
    public readonly struct PoseConsistencyResult
    {
        public readonly float nativePnpRmsPixels;
        public readonly float poseChainRoundTripRmsPixels;
        public readonly float renderedHierarchyRmsPixels;
        public readonly float displayProjectionDiagnosticRmsPixels;
        public readonly int samples;
        public readonly bool poseChainPassed;
        public readonly bool renderedHierarchyPassed;

        public bool HardGatePassed => poseChainPassed && renderedHierarchyPassed;

        public PoseConsistencyResult(
            float nativePnpRmsPixels,
            float poseChainRoundTripRmsPixels,
            float renderedHierarchyRmsPixels,
            float displayProjectionDiagnosticRmsPixels,
            int samples,
            bool poseChainPassed,
            bool renderedHierarchyPassed)
        {
            this.nativePnpRmsPixels = nativePnpRmsPixels;
            this.poseChainRoundTripRmsPixels = poseChainRoundTripRmsPixels;
            this.renderedHierarchyRmsPixels = renderedHierarchyRmsPixels;
            this.displayProjectionDiagnosticRmsPixels =
                displayProjectionDiagnosticRmsPixels;
            this.samples = samples;
            this.poseChainPassed = poseChainPassed;
            this.renderedHierarchyPassed = renderedHierarchyPassed;
        }
    }

    /// <summary>
    /// Verifies the PnP-to-Unity conversion in the same oriented native camera
    /// and K used by solvePnP. WorldToScreenPoint is intentionally confined to
    /// a non-gating display diagnostic because AR background crop and display
    /// projection are not the native CPU-image coordinate system.
    /// </summary>
    public static class UnityPoseConsistencyGate
    {
        public const string DisplayDiagnosticContract = "displayGate=DISABLED";

        public static bool TryEvaluate(
            Camera arCamera,
            NativeOrbResult pose,
            NativeInlierSet inliers,
            Vector3 rootPosition,
            Quaternion rootRotation,
            Transform trackedRoot,
            Transform renderedB,
            RepairCalibrationProfile calibration,
            float maximumPoseChainRmsPixels,
            float maximumRenderedHierarchyRmsPixels,
            out PoseConsistencyResult result,
            out string reason)
        {
            result = default;
            reason = string.Empty;
            if (arCamera == null || trackedRoot == null || renderedB == null
                || calibration == null || inliers.Count < 4)
            {
                reason = "Pose consistency has insufficient data.";
                return false;
            }

            CameraIntrinsics k = inliers.Intrinsics;
            Matrix4x4 candidateRoot = Matrix4x4.TRS(
                rootPosition,
                rootRotation,
                Vector3.one * calibration.metersPerModelUnit);
            // Camera.worldToCameraMatrix follows the graphics convention in
            // which camera-forward is -Z. PnP and Transform camera space use
            // +Z forward, so use the camera Transform inverse here. Applying
            // the graphics Z flip would invalidate every otherwise correct
            // round trip.
            Matrix4x4 worldToCamera = arCamera.transform.worldToLocalMatrix;
            double nativeSquared = 0d;
            double poseRoundTripSquared = 0d;
            double hierarchySquared = 0d;
            double displaySquared = 0d;
            int validSamples = 0;

            for (int i = 0; i < inliers.Count; i++)
            {
                Vector3 modelPoint = inliers.ModelPoints[i];
                Vector3 pnpCv = OpenCvUnityPoseConverter.TransformModelPoint(
                    pose,
                    modelPoint);
                if (!TryProjectOrientedCv(pnpCv, k, out Vector2 pnpPixel))
                {
                    continue;
                }

                // Path B: canonical ORB point -> candidate Unity root -> Unity
                // camera -> the same oriented OpenCV camera -> the same K.
                Vector3 unityCanonicalPoint =
                    CanonicalFrameRegistration.OrbToUnityCanonicalPoint(modelPoint);
                Vector3 canonicalWorld =
                    candidateRoot.MultiplyPoint3x4(unityCanonicalPoint);
                Vector3 canonicalUnityCamera =
                    worldToCamera.MultiplyPoint3x4(canonicalWorld);
                Vector3 canonicalRoundTripCv = UnityCameraToCvCamera(
                    canonicalUnityCamera);
                if (!TryProjectOrientedCv(
                        canonicalRoundTripCv,
                        k,
                        out Vector2 poseRoundTripPixel))
                {
                    continue;
                }

                // Path C traverses the real imported B transform hierarchy. It
                // is kept separate from the root-pose round trip so a model
                // frame error cannot be misreported as a camera-frame error.
                Vector3 importedMeshPoint =
                    CanonicalFrameRegistration.OrbToImportedMeshLocalPoint(
                        trackedRoot,
                        renderedB,
                        modelPoint);
                Vector3 currentRenderedWorld =
                    renderedB.TransformPoint(importedMeshPoint);
                Vector3 renderedPointInRoot =
                    trackedRoot.InverseTransformPoint(currentRenderedWorld);
                Vector3 renderedWorld =
                    candidateRoot.MultiplyPoint3x4(renderedPointInRoot);
                Vector3 renderedUnityCamera =
                    worldToCamera.MultiplyPoint3x4(renderedWorld);
                Vector3 renderedCv = UnityCameraToCvCamera(renderedUnityCamera);
                if (!TryProjectOrientedCv(
                        renderedCv,
                        k,
                        out Vector2 renderedPixel))
                {
                    continue;
                }

                Vector2 observed = inliers.FramePoints[i];
                nativeSquared += (pnpPixel - observed).sqrMagnitude;
                poseRoundTripSquared +=
                    (poseRoundTripPixel - pnpPixel).sqrMagnitude;
                hierarchySquared += (renderedPixel - pnpPixel).sqrMagnitude;

                // Old v38 metric retained as diagnostic only. It mixes the
                // native CPU image with Unity's display projection and must
                // never participate in HardGatePassed.
                Vector3 observedRayCv = new Vector3(
                    (observed.x - k.PrincipalPointX) / k.FocalLengthX,
                    (observed.y - k.PrincipalPointY) / k.FocalLengthY,
                    1f);
                Vector3 observedRayUnity =
                    OpenCvUnityPoseConverter.CvCameraToUnityCamera(observedRayCv);
                Vector3 observedScreen = arCamera.WorldToScreenPoint(
                    arCamera.transform.TransformPoint(observedRayUnity));
                Vector3 renderedScreen = arCamera.WorldToScreenPoint(renderedWorld);
                if (IsFinite(observedScreen) && IsFinite(renderedScreen)
                    && observedScreen.z > 0f && renderedScreen.z > 0f)
                {
                    displaySquared += new Vector2(
                        renderedScreen.x - observedScreen.x,
                        renderedScreen.y - observedScreen.y).sqrMagnitude;
                }
                validSamples++;
            }

            if (validSamples < 4)
            {
                reason = "Pose consistency has fewer than four visible inliers.";
                return false;
            }

            float nativeRms = Rms(nativeSquared, validSamples);
            float poseRms = Rms(poseRoundTripSquared, validSamples);
            float hierarchyRms = Rms(hierarchySquared, validSamples);
            float displayRms = Rms(displaySquared, validSamples);
            bool posePassed = float.IsFinite(poseRms)
                && poseRms <= maximumPoseChainRmsPixels;
            bool hierarchyPassed = float.IsFinite(hierarchyRms)
                && hierarchyRms <= maximumRenderedHierarchyRmsPixels;
            result = new PoseConsistencyResult(
                nativeRms,
                poseRms,
                hierarchyRms,
                displayRms,
                validSamples,
                posePassed,
                hierarchyPassed);
            if (!posePassed)
            {
                reason = $"POSE CONVERSION FAIL: PoseRT {poseRms:F3}px > "
                    + $"{maximumPoseChainRmsPixels:F3}px.";
            }
            else if (!hierarchyPassed)
            {
                reason = $"MODEL FRAME FAIL: BHierarchy {hierarchyRms:F3}px > "
                    + $"{maximumRenderedHierarchyRmsPixels:F3}px.";
            }
            return result.HardGatePassed;
        }

        private static Vector3 UnityCameraToCvCamera(Vector3 point) =>
            new Vector3(point.x, -point.y, point.z);

        private static bool TryProjectOrientedCv(
            Vector3 point,
            CameraIntrinsics k,
            out Vector2 pixel)
        {
            pixel = default;
            if (!IsFinite(point) || point.z <= 0.000001f)
            {
                return false;
            }
            pixel = new Vector2(
                k.FocalLengthX * point.x / point.z + k.PrincipalPointX,
                k.FocalLengthY * point.y / point.z + k.PrincipalPointY);
            return float.IsFinite(pixel.x) && float.IsFinite(pixel.y);
        }

        private static float Rms(double squared, int count) =>
            Mathf.Sqrt((float)(squared / count));

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x)
            && float.IsFinite(value.y)
            && float.IsFinite(value.z);
    }
}
