using UnityEngine;

namespace Urp.ArDemo.Calibration
{
    /// <summary>
    /// Derives the fixed ORB-to-rendered-B parent transform from corresponding
    /// landmarks through the actual imported FBX hierarchy. No Euler angle is
    /// guessed from an Inspector transform.
    /// </summary>
    public static class CanonicalFrameRegistration
    {
        public readonly struct Result
        {
            public readonly Vector3 position;
            public readonly Quaternion rotation;
            public readonly Vector3 scale;
            public readonly float landmarkRms;
            public readonly Matrix4x4 matrix;

            public Result(
                Vector3 position,
                Quaternion rotation,
                Vector3 scale,
                float landmarkRms,
                Matrix4x4 matrix)
            {
                this.position = position;
                this.rotation = rotation;
                this.scale = scale;
                this.landmarkRms = landmarkRms;
                this.matrix = matrix;
            }
        }

        // Unity's FBX mesh conversion reflects the Blender/SfM X coordinate
        // in vertex data. This is the model-side handedness conversion paired
        // with OpenCV-camera (Y down) to Unity-camera (Y up).
        // OpenCV and Unity camera frames have opposite handedness. A proper
        // Unity Quaternion therefore acts on this X-reflected canonical point.
        // This is the explicit model-side H=diag(-1,1,1) in F*R*H.
        public static Vector3 OrbToUnityCanonicalPoint(Vector3 point) =>
            new Vector3(-point.x, point.y, point.z);

        public static Vector3 OrbToImportedMeshPoint(Vector3 point) =>
            OrbToUnityCanonicalPoint(point);

        public static Vector3 OrbToImportedMeshDirection(Vector3 direction) =>
            new Vector3(-direction.x, direction.y, direction.z);

        public static Vector3 OrbToImportedMeshLocalPoint(
            Transform trackedRoot,
            Transform renderedB,
            Vector3 orbPoint)
        {
            float hierarchyScale = GetImportedHierarchyScale(trackedRoot, renderedB);
            return OrbToImportedMeshPoint(orbPoint) / hierarchyScale;
        }

        public static float GetImportedHierarchyScale(
            Transform trackedRoot,
            Transform renderedB)
        {
            if (trackedRoot == null || renderedB == null)
            {
                return 1f;
            }
            Matrix4x4 relative =
                trackedRoot.worldToLocalMatrix * renderedB.localToWorldMatrix;
            Vector3 x = relative.MultiplyVector(Vector3.right);
            Vector3 y = relative.MultiplyVector(Vector3.up);
            Vector3 z = relative.MultiplyVector(Vector3.forward);
            float value = (x.magnitude + y.magnitude + z.magnitude) / 3f;
            return value > 0.000001f ? value : 1f;
        }

        public static bool TryDerive(
            Transform trackedRoot,
            Transform alignment,
            Transform renderedB,
            RepairCalibrationProfile calibration,
            out Result result,
            out string reason)
        {
            result = default;
            reason = string.Empty;
            if (trackedRoot == null || alignment == null || renderedB == null
                || calibration == null || alignment.parent != trackedRoot
                || !calibration.hasAuthoredBLandmarks)
            {
                reason = "Canonical registration requires exported Blender B landmarks.";
                return false;
            }

            Vector3 savedPosition = alignment.localPosition;
            Quaternion savedRotation = alignment.localRotation;
            Vector3 savedScale = alignment.localScale;
            alignment.localPosition = Vector3.zero;
            alignment.localRotation = Quaternion.identity;
            alignment.localScale = Vector3.one;

            Vector3[] orb =
            {
                calibration.objectOriginInModel,
                calibration.mouthCenterInModel,
                calibration.mouthRightInModel,
                calibration.mouthFrontInModel,
                calibration.neckAxisPointInModel
            };
            Vector3[] authoredB =
            {
                calibration.authoredBOrigin,
                calibration.authoredBMouthCenter,
                calibration.authoredBMouthRight,
                calibration.authoredBMouthFront,
                calibration.authoredBNeckAxisPoint
            };
            Vector3[] source = new Vector3[orb.Length];
            Vector3[] target = new Vector3[orb.Length];
            float importedHierarchyScale = GetImportedHierarchyScale(
                trackedRoot,
                renderedB);
            for (int i = 0; i < orb.Length; i++)
            {
                // Source landmarks come from Blender authoring metadata. They
                // are intentionally not generated from orb[i], avoiding the
                // v38 construct-and-verify self-consistency loop.
                Vector3 meshPoint = OrbToImportedMeshPoint(authoredB[i])
                    / importedHierarchyScale;
                source[i] = trackedRoot.InverseTransformPoint(
                    renderedB.TransformPoint(meshPoint));
                target[i] = OrbToImportedMeshPoint(orb[i]);
            }

            Debug.Log(
                "[URP_POSE_DIAG] landmark-derive "
                + $"hierarchyScale={importedHierarchyScale:F9} "
                + $"sourceUp={Vector3.Distance(source[1], source[4]):F9} "
                + $"targetUp={Vector3.Distance(target[1], target[4]):F9}");

            bool solved = TrySolveSimilarity(source, target, out result, out reason);
            if (!solved)
            {
                alignment.localPosition = savedPosition;
                alignment.localRotation = savedRotation;
                alignment.localScale = savedScale;
            }
            return solved;
        }

        public static bool TrySolveSimilarity(
            Vector3[] source,
            Vector3[] target,
            out Result result,
            out string reason)
        {
            result = default;
            reason = string.Empty;
            if (source == null || target == null
                || source.Length != target.Length || source.Length < 5)
            {
                reason = "Five corresponding canonical landmarks are required.";
                return false;
            }

            // Indices: origin, mouth centre, mouth right, mouth front, neck axis.
            Vector3 sourceUp = (source[1] - source[4]).normalized;
            Vector3 targetUp = (target[1] - target[4]).normalized;
            Vector3 sourceRight = Vector3.ProjectOnPlane(
                source[2] - source[1], sourceUp).normalized;
            Vector3 targetRight = Vector3.ProjectOnPlane(
                target[2] - target[1], targetUp).normalized;
            if (sourceUp.sqrMagnitude < 0.99f || targetUp.sqrMagnitude < 0.99f
                || sourceRight.sqrMagnitude < 0.99f
                || targetRight.sqrMagnitude < 0.99f)
            {
                reason = "Canonical landmark axes are degenerate.";
                return false;
            }

            Vector3 sourceForward = Vector3.Cross(sourceRight, sourceUp).normalized;
            Vector3 targetForward = Vector3.Cross(targetRight, targetUp).normalized;
            Quaternion sourceFrame = Quaternion.LookRotation(sourceForward, sourceUp);
            Quaternion targetFrame = Quaternion.LookRotation(targetForward, targetUp);
            Quaternion rotation = targetFrame * Quaternion.Inverse(sourceFrame);

            float sourceLength =
                (Vector3.Distance(source[1], source[4])
                 + Vector3.Distance(source[2], source[1])
                 + Vector3.Distance(source[3], source[1])) / 3f;
            float targetLength =
                (Vector3.Distance(target[1], target[4])
                 + Vector3.Distance(target[2], target[1])
                 + Vector3.Distance(target[3], target[1])) / 3f;
            if (sourceLength < 0.000001f || targetLength < 0.000001f)
            {
                reason = "Canonical landmark scale is degenerate.";
                return false;
            }
            float uniformScale = targetLength / sourceLength;
            Vector3 position = target[0] - rotation * (source[0] * uniformScale);
            Matrix4x4 matrix = Matrix4x4.TRS(
                position,
                rotation,
                Vector3.one * uniformScale);

            float squared = 0f;
            for (int i = 0; i < source.Length; i++)
            {
                Vector3 delta = matrix.MultiplyPoint3x4(source[i]) - target[i];
                squared += delta.sqrMagnitude;
            }
            float rms = Mathf.Sqrt(squared / source.Length);
            if (!float.IsFinite(rms) || rms > 0.00001f)
            {
                reason = $"Canonical landmark fit RMS is {rms:E6}.";
                return false;
            }

            result = new Result(
                position,
                rotation,
                Vector3.one * uniformScale,
                rms,
                matrix);
            return true;
        }
    }
}
