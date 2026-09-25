using UnityEngine;
using Urp.ArDemo.Native;

namespace Urp.ArDemo.Calibration
{
    /// <summary>
    /// Adaptive SE(3) EMA. High-quality, continuous PnP samples follow quickly;
    /// marginal samples are strongly smoothed and low-confidence samples hold.
    /// No fixed metres/second or degrees/second cap freezes legitimate 6DoF motion.
    /// </summary>
    public static class ConfidenceWeightedPoseFusion
    {
        public readonly struct Result
        {
            public readonly Vector3 position;
            public readonly Quaternion rotation;
            public readonly float confidence;
            public readonly float positionAlpha;
            public readonly float rotationAlpha;
            public readonly bool held;

            public Result(
                Vector3 position,
                Quaternion rotation,
                float confidence,
                float positionAlpha,
                float rotationAlpha,
                bool held)
            {
                this.position = position;
                this.rotation = rotation;
                this.confidence = confidence;
                this.positionAlpha = positionAlpha;
                this.rotationAlpha = rotationAlpha;
                this.held = held;
            }
        }

        public static float Score(
            NativeOrbResult pose,
            float minimumInlierRatio,
            float maximumRms,
            float minimumCoverageX,
            float minimumCoverageY,
            float positionContinuity,
            float rotationContinuity)
        {
            float count = Mathf.InverseLerp(6f, 55f, pose.poseInliers);
            float ratio = Mathf.InverseLerp(
                minimumInlierRatio,
                Mathf.Max(minimumInlierRatio + 0.01f, 0.85f),
                pose.inlierRatio);
            float rms = 1f - Mathf.InverseLerp(0.75f, maximumRms, pose.reprojectionError);
            float coverageX = Mathf.InverseLerp(
                minimumCoverageX,
                Mathf.Max(minimumCoverageX + 0.01f, 0.45f),
                pose.coverageX);
            float coverageY = Mathf.InverseLerp(
                minimumCoverageY,
                Mathf.Max(minimumCoverageY + 0.01f, 0.75f),
                pose.coverageY);
            float continuity = Mathf.Clamp01(
                1f - Mathf.Max(positionContinuity, rotationContinuity));
            return Mathf.Clamp01(
                0.20f * count
                + 0.20f * ratio
                + 0.20f * rms
                + 0.10f * coverageX
                + 0.10f * coverageY
                + 0.20f * continuity);
        }

        public static Result Step(
            Vector3 currentPosition,
            Quaternion currentRotation,
            Vector3 candidatePosition,
            Quaternion candidateRotation,
            float confidence,
            float positionSmoothing,
            float rotationSmoothing,
            float elapsedSeconds,
            float nominalSampleInterval)
        {
            confidence = Mathf.Clamp01(confidence);
            if (confidence < 0.28f)
            {
                return new Result(
                    currentPosition,
                    currentRotation,
                    confidence,
                    0f,
                    0f,
                    true);
            }

            float follow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.90f, confidence));
            float positionBase = Mathf.Lerp(
                Mathf.Clamp01(positionSmoothing),
                0.92f,
                follow);
            float rotationBase = Mathf.Lerp(
                Mathf.Clamp01(rotationSmoothing),
                0.90f,
                follow);
            float timeScale = Mathf.Clamp(
                elapsedSeconds / Mathf.Max(0.01f, nominalSampleInterval),
                0.15f,
                2.5f);
            float positionAlpha = 1f - Mathf.Pow(1f - positionBase, timeScale);
            float rotationAlpha = 1f - Mathf.Pow(1f - rotationBase, timeScale);
            return new Result(
                Vector3.Lerp(currentPosition, candidatePosition, positionAlpha),
                Quaternion.Slerp(currentRotation, candidateRotation, rotationAlpha),
                confidence,
                positionAlpha,
                rotationAlpha,
                false);
        }
    }
}
