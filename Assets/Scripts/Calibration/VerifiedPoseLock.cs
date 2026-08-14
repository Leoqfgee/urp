using System;
using System.Collections.Generic;
using UnityEngine;

namespace Urp.ArDemo.Calibration
{
    [Serializable]
    public sealed class VerifiedPoseLockSettings
    {
        public bool enabled = true;
        public int confirmationFrames = 10;
        public int minimumLockInliers = 20;
        public float maximumLockRmsPixels = 2.0f;
        public float maximumPositionSpreadMeters = 0.003f;
        public float maximumRotationSpreadDegrees = 0.5f;
        public float positionDeadbandMeters = 0.003f;
        public float rotationDeadbandDegrees = 0.5f;
        public int persistentDriftConfirmationFrames = 8;
        public float persistentDriftPositionMeters = 0.004f;
        public float persistentDriftRotationDegrees = 0.7f;
        [Range(0.01f, 0.2f)] public float slowDriftCorrectionAlpha = 0.05f;
        public float relocationPositionThresholdMeters = 0.018f;
        public float relocationRotationThresholdDegrees = 3.0f;
        public int relocationConfirmationFrames = 4;
        public int relocalizationStableFrames = 6;
    }

    /// <summary>
    /// Locks a world-space object pose only after a high-quality, low-spread
    /// window. PnP continues while locked; sub-deadband noise is ignored,
    /// coherent medium drift is corrected slowly, and sustained large motion
    /// starts a new multi-frame lock. It never references the AR camera.
    /// </summary>
    public sealed class VerifiedPoseLock
    {
        public enum LockState
        {
            Searching,
            Acquiring,
            Locked,
            Relocalizing
        }

        public readonly struct Candidate
        {
            public readonly Vector3 position;
            public readonly Quaternion rotation;
            public readonly int inliers;
            public readonly float inlierRatio;
            public readonly float rmsPixels;
            public readonly float coverage;
            public readonly float confidence;

            public Candidate(
                Vector3 position,
                Quaternion rotation,
                int inliers,
                float inlierRatio,
                float rmsPixels,
                float coverage,
                float confidence)
            {
                this.position = position;
                this.rotation = Quaternion.Normalize(rotation);
                this.inliers = inliers;
                this.inlierRatio = inlierRatio;
                this.rmsPixels = rmsPixels;
                this.coverage = coverage;
                this.confidence = confidence;
            }
        }

        public readonly struct Result
        {
            public readonly Vector3 position;
            public readonly Quaternion rotation;
            public readonly bool applyToRoot;
            public readonly bool deadband;
            public readonly float positionDeltaMeters;
            public readonly float rotationDeltaDegrees;
            public readonly float positionSpreadMeters;
            public readonly float rotationSpreadDegrees;

            public Result(
                Vector3 position,
                Quaternion rotation,
                bool applyToRoot,
                bool deadband,
                float positionDeltaMeters,
                float rotationDeltaDegrees,
                float positionSpreadMeters,
                float rotationSpreadDegrees)
            {
                this.position = position;
                this.rotation = rotation;
                this.applyToRoot = applyToRoot;
                this.deadband = deadband;
                this.positionDeltaMeters = positionDeltaMeters;
                this.rotationDeltaDegrees = rotationDeltaDegrees;
                this.positionSpreadMeters = positionSpreadMeters;
                this.rotationSpreadDegrees = rotationSpreadDegrees;
            }
        }

        private readonly VerifiedPoseLockSettings settings;
        private readonly List<Candidate> window = new List<Candidate>(16);
        private int persistentDriftFrames;
        private int relocationFrames;
        private Vector3 persistentDirection;

        public VerifiedPoseLock(VerifiedPoseLockSettings settings)
        {
            this.settings = settings ?? new VerifiedPoseLockSettings();
        }

        public LockState State { get; private set; } = LockState.Searching;
        public Vector3 LockedPosition { get; private set; }
        public Quaternion LockedRotation { get; private set; } = Quaternion.identity;
        public int WindowFrames => window.Count;
        public int PersistentDriftFrames => persistentDriftFrames;
        public int RelocationFrames => relocationFrames;

        public void Reset()
        {
            State = LockState.Searching;
            window.Clear();
            persistentDriftFrames = 0;
            relocationFrames = 0;
            persistentDirection = Vector3.zero;
            LockedPosition = Vector3.zero;
            LockedRotation = Quaternion.identity;
        }

        public Result Step(Candidate candidate, Vector3 currentPosition,
            Quaternion currentRotation)
        {
            if (!settings.enabled)
            {
                return Build(candidate.position, candidate.rotation, true, false,
                    candidate, 0f, 0f);
            }

            if (State == LockState.Searching)
            {
                State = LockState.Acquiring;
            }

            if (State == LockState.Acquiring || State == LockState.Relocalizing)
            {
                return Accumulate(candidate, currentPosition, currentRotation);
            }

            float positionDelta = Vector3.Distance(candidate.position, LockedPosition);
            float rotationDelta = Quaternion.Angle(candidate.rotation, LockedRotation);
            bool inDeadband = positionDelta <= settings.positionDeadbandMeters
                && rotationDelta <= settings.rotationDeadbandDegrees;
            if (inDeadband)
            {
                persistentDriftFrames = 0;
                relocationFrames = 0;
                persistentDirection = Vector3.zero;
                return Build(LockedPosition, LockedRotation, false, true,
                    candidate, 0f, 0f);
            }

            bool large = positionDelta >= settings.relocationPositionThresholdMeters
                || rotationDelta >= settings.relocationRotationThresholdDegrees;
            if (large && IsHighQuality(candidate))
            {
                relocationFrames++;
                persistentDriftFrames = 0;
                if (relocationFrames >= Mathf.Max(2, settings.relocationConfirmationFrames))
                {
                    State = LockState.Relocalizing;
                    window.Clear();
                    window.Add(candidate);
                    return Build(LockedPosition, LockedRotation, false, false,
                        candidate, 0f, 0f);
                }
                return Build(LockedPosition, LockedRotation, false, false,
                    candidate, 0f, 0f);
            }

            relocationFrames = 0;
            bool medium = positionDelta >= settings.persistentDriftPositionMeters
                || rotationDelta >= settings.persistentDriftRotationDegrees;
            if (!medium || !IsHighQuality(candidate))
            {
                persistentDriftFrames = 0;
                persistentDirection = Vector3.zero;
                return Build(LockedPosition, LockedRotation, false, false,
                    candidate, 0f, 0f);
            }

            Vector3 direction = candidate.position - LockedPosition;
            bool directionConsistent = persistentDirection.sqrMagnitude < 1e-10f
                || direction.sqrMagnitude < 1e-10f
                || Vector3.Dot(persistentDirection.normalized, direction.normalized) >= 0.75f;
            if (!directionConsistent)
            {
                persistentDriftFrames = 1;
                persistentDirection = direction;
                return Build(LockedPosition, LockedRotation, false, false,
                    candidate, 0f, 0f);
            }
            persistentDriftFrames++;
            persistentDirection = direction;
            if (persistentDriftFrames < Mathf.Max(2,
                    settings.persistentDriftConfirmationFrames))
            {
                return Build(LockedPosition, LockedRotation, false, false,
                    candidate, 0f, 0f);
            }

            float alpha = Mathf.Clamp(settings.slowDriftCorrectionAlpha, 0.01f, 0.2f);
            LockedPosition = Vector3.Lerp(LockedPosition, candidate.position, alpha);
            LockedRotation = Quaternion.Slerp(LockedRotation, candidate.rotation, alpha);
            persistentDriftFrames = 0;
            persistentDirection = Vector3.zero;
            return Build(LockedPosition, LockedRotation, true, false,
                candidate, 0f, 0f);
        }

        private Result Accumulate(Candidate candidate, Vector3 currentPosition,
            Quaternion currentRotation)
        {
            if (!IsHighQuality(candidate))
            {
                window.Clear();
                return Build(currentPosition, currentRotation, false, false,
                    candidate, float.PositiveInfinity, float.PositiveInfinity);
            }
            window.Add(candidate);
            int required = State == LockState.Relocalizing
                ? Mathf.Max(3, settings.relocalizationStableFrames)
                : Mathf.Max(3, settings.confirmationFrames);
            while (window.Count > required)
            {
                window.RemoveAt(0);
            }
            Estimate(window, out Vector3 estimatePosition,
                out Quaternion estimateRotation, out float positionSpread,
                out float rotationSpread);
            bool concentrated = window.Count >= required
                && positionSpread <= settings.maximumPositionSpreadMeters
                && rotationSpread <= settings.maximumRotationSpreadDegrees;
            if (!concentrated)
            {
                // Acquisition can follow reliable PnP until a verified lock;
                // relocalization deliberately holds the old world pose.
                bool acquiring = State == LockState.Acquiring;
                return Build(acquiring ? candidate.position : LockedPosition,
                    acquiring ? candidate.rotation : LockedRotation,
                    acquiring, false, candidate, positionSpread, rotationSpread);
            }
            LockedPosition = estimatePosition;
            LockedRotation = estimateRotation;
            State = LockState.Locked;
            persistentDriftFrames = 0;
            relocationFrames = 0;
            persistentDirection = Vector3.zero;
            window.Clear();
            return Build(LockedPosition, LockedRotation, true, false,
                candidate, positionSpread, rotationSpread);
        }

        private bool IsHighQuality(Candidate candidate) =>
            candidate.inliers >= Mathf.Max(1, settings.minimumLockInliers)
            && candidate.rmsPixels <= Mathf.Max(0.1f, settings.maximumLockRmsPixels);

        private Result Build(Vector3 position, Quaternion rotation,
            bool apply, bool deadband, Candidate candidate,
            float positionSpread, float rotationSpread) =>
            new Result(position, rotation, apply, deadband,
                State == LockState.Locked
                    ? Vector3.Distance(candidate.position, LockedPosition) : 0f,
                State == LockState.Locked
                    ? Quaternion.Angle(candidate.rotation, LockedRotation) : 0f,
                positionSpread, rotationSpread);

        private static void Estimate(IReadOnlyList<Candidate> samples,
            out Vector3 position, out Quaternion rotation,
            out float positionSpread, out float rotationSpread)
        {
            int count = samples.Count;
            float[] xs = new float[count];
            float[] ys = new float[count];
            float[] zs = new float[count];
            for (int i = 0; i < count; i++)
            {
                xs[i] = samples[i].position.x;
                ys[i] = samples[i].position.y;
                zs[i] = samples[i].position.z;
            }
            Array.Sort(xs); Array.Sort(ys); Array.Sort(zs);
            position = new Vector3(Median(xs), Median(ys), Median(zs));

            Quaternion reference = samples[0].rotation;
            Vector4 sum = Vector4.zero;
            float weightSum = 0f;
            for (int i = 0; i < count; i++)
            {
                Candidate sample = samples[i];
                Quaternion q = sample.rotation;
                if (Quaternion.Dot(reference, q) < 0f)
                {
                    q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                }
                float weight = Mathf.Max(1f, sample.inliers)
                    * Mathf.Max(0.1f, sample.inlierRatio)
                    * Mathf.Max(0.1f, sample.coverage)
                    * Mathf.Max(0.1f, sample.confidence)
                    / Mathf.Max(0.25f, sample.rmsPixels * sample.rmsPixels);
                sum += new Vector4(q.x, q.y, q.z, q.w) * weight;
                weightSum += weight;
            }
            sum /= Mathf.Max(0.0001f, weightSum);
            rotation = Quaternion.Normalize(new Quaternion(sum.x, sum.y, sum.z, sum.w));

            positionSpread = 0f;
            rotationSpread = 0f;
            for (int i = 0; i < count; i++)
            {
                positionSpread = Mathf.Max(positionSpread,
                    Vector3.Distance(samples[i].position, position));
                rotationSpread = Mathf.Max(rotationSpread,
                    Quaternion.Angle(samples[i].rotation, rotation));
            }
        }

        private static float Median(float[] sorted)
        {
            int middle = sorted.Length / 2;
            return sorted.Length % 2 == 0
                ? (sorted[middle - 1] + sorted[middle]) * 0.5f
                : sorted[middle];
        }
    }
}
