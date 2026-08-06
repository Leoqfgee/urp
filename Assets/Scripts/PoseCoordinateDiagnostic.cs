using System.Text;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Urp.ArDemo.Calibration;
using Urp.ArDemo.Native;

namespace Urp.ArDemo
{
    /// <summary>
    /// Development-only separation of PnP canonical axes from actual rendered
    /// B axes. It never changes the tracked hierarchy or BottleCapC.
    /// </summary>
    public sealed class PoseCoordinateDiagnostic : MonoBehaviour
    {
        [SerializeField] private bool poseCoordinateDebug = true;
        [SerializeField] private Camera arCamera;
        [SerializeField] private ARCameraManager cameraManager;

        private Transform trackedRoot;
        private Transform alignment;
        private Transform pairRoot;
        private Transform renderedB;
        private RepairCalibrationProfile calibration;
        private readonly LineRenderer[] axisLines = new LineRenderer[6];
        private Vector2 orbX;
        private Vector2 orbY;
        private Vector2 orbZ;
        private Vector2 bX;
        private Vector2 bY;
        private Vector2 bZ;
        private int cpuWidth;
        private int cpuHeight;
        private int rotationClockwise;
        private Vector3 pnpRelativePosition;
        private Vector3 pnpRelativeEuler;
        private Vector3 appliedRelativePosition;
        private Vector3 appliedRelativeEuler;
        private float poseLagCentimetres;
        private float poseLagDegrees;

        public void Bind(
            Camera camera,
            ARCameraManager manager,
            Transform root,
            Transform modelAlignment,
            Transform bottlePair,
            Transform referenceB,
            RepairCalibrationProfile profile)
        {
            arCamera = camera;
            cameraManager = manager;
            trackedRoot = root;
            alignment = modelAlignment;
            pairRoot = bottlePair;
            renderedB = referenceB;
            calibration = profile;
        }

        public string CompactSummary =>
            !DiagnosticsEnabled
                ? string.Empty
                : $"\nDBG CPU {cpuWidth}x{cpuHeight} rot={rotationClockwise} "
                  + $"Screen={Screen.orientation} | "
                  + $"ORB Y=({orbY.x:F2},{orbY.y:F2}) "
                  + $"B Y=({bY.x:F2},{bY.y:F2})"
                  + $"\nPnP rel={FormatShort(pnpRelativeEuler)} Applied rel="
                  + $"{FormatShort(appliedRelativeEuler)} Lag="
                  + $"{poseLagDegrees:F1}deg/{poseLagCentimetres:F1}cm";

        public void UpdatePose(
            NativeOrbResult pose,
            NativeInlierSet inliers,
            int sourceCpuWidth,
            int sourceCpuHeight,
            int frameRotation,
            Vector3 targetPosition,
            Quaternion targetRotation,
            Matrix4x4 orbToRenderedB,
            PoseConsistencyResult consistency)
        {
            if (!DiagnosticsEnabled || arCamera == null || calibration == null
                || trackedRoot == null || renderedB == null)
            {
                return;
            }

            cpuWidth = sourceCpuWidth;
            cpuHeight = sourceCpuHeight;
            rotationClockwise = frameRotation;
            float axisLength = 0.12f;
            Vector3 origin = calibration.objectOriginInModel;
            Vector3[] endpoints =
            {
                origin + calibration.RightInModel * axisLength,
                origin + calibration.UpInModel * axisLength,
                origin + calibration.ForwardInModel * axisLength
            };
            Vector3 orbOriginWorld = PnpPointToWorld(pose, origin);
            Vector3[] orbWorld = new Vector3[3];
            Vector3[] bWorld = new Vector3[3];
            Matrix4x4 rootMatrix = Matrix4x4.TRS(
                targetPosition,
                targetRotation,
                Vector3.one * calibration.metersPerModelUnit);
            Vector3 bOriginInRoot = GetRenderedPointInRoot(origin);
            Vector3 bOriginWorld = rootMatrix.MultiplyPoint3x4(bOriginInRoot);
            for (int i = 0; i < 3; i++)
            {
                orbWorld[i] = PnpPointToWorld(pose, endpoints[i]);
                bWorld[i] = rootMatrix.MultiplyPoint3x4(
                    GetRenderedPointInRoot(endpoints[i]));
            }

            orbX = ScreenDirection(orbOriginWorld, orbWorld[0]);
            orbY = ScreenDirection(orbOriginWorld, orbWorld[1]);
            orbZ = ScreenDirection(orbOriginWorld, orbWorld[2]);
            bX = ScreenDirection(bOriginWorld, bWorld[0]);
            bY = ScreenDirection(bOriginWorld, bWorld[1]);
            bZ = ScreenDirection(bOriginWorld, bWorld[2]);
            UpdateLine(0, "ORB X", Color.red, orbOriginWorld, orbWorld[0], 0.003f);
            UpdateLine(1, "ORB Y", Color.green, orbOriginWorld, orbWorld[1], 0.003f);
            UpdateLine(2, "ORB Z", Color.blue, orbOriginWorld, orbWorld[2], 0.003f);
            UpdateLine(3, "B X", new Color(1f, 0.4f, 0.4f), bOriginWorld, bWorld[0], 0.0015f);
            UpdateLine(4, "B Y", new Color(0.4f, 1f, 0.4f), bOriginWorld, bWorld[1], 0.0015f);
            UpdateLine(5, "B Z", new Color(0.4f, 0.6f, 1f), bOriginWorld, bWorld[2], 0.0015f);

            StringBuilder log = new StringBuilder(4096);
            log.Append("[URP_POSE_DIAG] ")
                .Append("inliers=").Append(pose.poseInliers)
                .Append('/').Append(pose.uniqueMatches)
                .Append(" nativeReportedRms=")
                .Append(pose.reprojectionError.ToString("F3"))
                .Append(" nativeObservedRms=")
                .Append(consistency.nativePnpRmsPixels.ToString("F3"))
                .Append(" poseRtRms=")
                .Append(consistency.poseChainRoundTripRmsPixels.ToString("F4"))
                .Append(" poseRt=")
                .Append(consistency.poseChainPassed ? "PASS" : "FAIL")
                .Append(" hierarchyRtRms=")
                .Append(consistency.hierarchyTransformRoundTripRmsPixels.ToString("F4"))
                .Append(" hierarchyRt=")
                .Append(consistency.hierarchyTransformRoundTripPassed ? "PASS" : "FAIL")
                .Append(" displayDiagnosticRms=")
                .Append(consistency.displayProjectionDiagnosticRmsPixels.ToString("F3"))
                .Append(" displayGate=DISABLED")
                .AppendLine();
            log.Append("Screen=").Append(Screen.width).Append('x').Append(Screen.height)
                .Append(" orientation=").Append(Screen.orientation)
                .Append(" ARCamera=").Append(arCamera.pixelWidth).Append('x')
                .Append(arCamera.pixelHeight).AppendLine();
            log.Append("CPU=").Append(sourceCpuWidth).Append('x').Append(sourceCpuHeight)
                .Append(" rotationClockwise=").Append(frameRotation)
                .Append(" native=").Append(inliers.FrameWidth).Append('x')
                .Append(inliers.FrameHeight)
                .Append(" K=")
                .Append(inliers.Intrinsics.FocalLengthX.ToString("F3")).Append(',')
                .Append(inliers.Intrinsics.FocalLengthY.ToString("F3")).Append(',')
                .Append(inliers.Intrinsics.PrincipalPointX.ToString("F3")).Append(',')
                .Append(inliers.Intrinsics.PrincipalPointY.ToString("F3"))
                .Append(" facing=")
                .Append(cameraManager != null
                    ? cameraManager.currentFacingDirection.ToString()
                    : "unknown")
                .AppendLine();
            log.Append("ORB screen dirs X=").Append(Format(orbX))
                .Append(" Y=").Append(Format(orbY))
                .Append(" Z=").Append(Format(orbZ)).AppendLine();
            log.Append("B screen dirs X=").Append(Format(bX))
                .Append(" Y=").Append(Format(bY))
                .Append(" Z=").Append(Format(bZ)).AppendLine();
            log.Append("orbToRenderedB=").Append(Format(orbToRenderedB)).AppendLine();
            AppendTransform(log, "TrackedBottleRoot", trackedRoot);
            AppendTransform(log, "ModelCoordinateAlignment", alignment);
            AppendTransform(log, "BottleRepairRoot", pairRoot);
            AppendTransform(log, "DamagedBottleB", renderedB);
            MeshFilter filter = renderedB.GetComponentInChildren<MeshFilter>(true);
            Renderer renderer = renderedB.GetComponentInChildren<Renderer>(true);
            if (filter != null && filter.sharedMesh != null)
            {
                log.Append("DamagedBottleB.mesh.bounds=")
                    .Append(Format(filter.sharedMesh.bounds.center)).Append('/')
                    .Append(Format(filter.sharedMesh.bounds.extents)).AppendLine();
            }
            if (renderer != null)
            {
                log.Append("DamagedBottleB.renderer.bounds=")
                    .Append(Format(renderer.bounds.center)).Append('/')
                    .Append(Format(renderer.bounds.extents)).AppendLine();
            }
            Debug.Log(log.ToString());
        }

        public void UpdateFusion(
            Vector3 pnpWorldPosition,
            Quaternion pnpWorldRotation,
            Transform appliedRoot,
            float confidence,
            float positionAlpha,
            float rotationAlpha)
        {
            if (!DiagnosticsEnabled || arCamera == null || appliedRoot == null)
            {
                return;
            }
            Quaternion worldToCameraRotation = Quaternion.Inverse(
                arCamera.transform.rotation);
            pnpRelativePosition = arCamera.transform.InverseTransformPoint(
                pnpWorldPosition);
            pnpRelativeEuler = SignedEuler(
                worldToCameraRotation * pnpWorldRotation);
            appliedRelativePosition = arCamera.transform.InverseTransformPoint(
                appliedRoot.position);
            appliedRelativeEuler = SignedEuler(
                worldToCameraRotation * appliedRoot.rotation);
            poseLagCentimetres = Vector3.Distance(
                pnpRelativePosition,
                appliedRelativePosition) * 100f;
            poseLagDegrees = Quaternion.Angle(
                pnpWorldRotation,
                appliedRoot.rotation);
            Debug.Log(
                "[URP_POSE_FUSION_DIAG] "
                + $"pnpCameraPosition={Format(pnpRelativePosition)} "
                + $"pnpCameraYpr={Format(pnpRelativeEuler)} "
                + $"appliedCameraPosition={Format(appliedRelativePosition)} "
                + $"appliedCameraYpr={Format(appliedRelativeEuler)} "
                + $"lagCm={poseLagCentimetres:F3} "
                + $"lagDeg={poseLagDegrees:F3} confidence={confidence:F3} "
                + $"positionAlpha={positionAlpha:F3} rotationAlpha={rotationAlpha:F3}");
        }

        private Vector3 PnpPointToWorld(NativeOrbResult pose, Vector3 modelPoint)
        {
            Vector3 cv = OpenCvUnityPoseConverter.TransformModelPoint(pose, modelPoint);
            Vector3 unity = OpenCvUnityPoseConverter.CvCameraToUnityCamera(cv)
                * calibration.metersPerModelUnit;
            return arCamera.transform.TransformPoint(unity);
        }

        private Vector3 GetRenderedPointInRoot(Vector3 orbPoint)
        {
            Vector3 meshPoint = CanonicalFrameRegistration.OrbToImportedMeshLocalPoint(
                trackedRoot,
                renderedB,
                orbPoint);
            return trackedRoot.InverseTransformPoint(renderedB.TransformPoint(meshPoint));
        }

        private Vector2 ScreenDirection(Vector3 origin, Vector3 endpoint)
        {
            Vector3 a = arCamera.WorldToScreenPoint(origin);
            Vector3 b = arCamera.WorldToScreenPoint(endpoint);
            Vector2 delta = new Vector2(b.x - a.x, b.y - a.y);
            return delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.zero;
        }

        private void UpdateLine(
            int index,
            string lineName,
            Color color,
            Vector3 start,
            Vector3 end,
            float width)
        {
            if (axisLines[index] == null)
            {
                GameObject lineObject = new GameObject(lineName);
                lineObject.transform.SetParent(transform, false);
                LineRenderer line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.material = new Material(Shader.Find("Sprites/Default"));
                line.startColor = color;
                line.endColor = color;
                axisLines[index] = line;
            }
            axisLines[index].startWidth = width;
            axisLines[index].endWidth = width;
            axisLines[index].SetPosition(0, start);
            axisLines[index].SetPosition(1, end);
        }

        private static void AppendTransform(
            StringBuilder log,
            string label,
            Transform value)
        {
            if (value == null)
            {
                log.Append(label).Append("=null").AppendLine();
                return;
            }
            log.Append(label)
                .Append(" localP=").Append(Format(value.localPosition))
                .Append(" localR=").Append(Format(value.localRotation))
                .Append(" localS=").Append(Format(value.localScale))
                .Append(" world=").Append(Format(value.localToWorldMatrix))
                .AppendLine();
        }

        private static string Format(Vector2 value) =>
            $"({value.x:F4},{value.y:F4})";

        private static string Format(Vector3 value) =>
            $"({value.x:F6},{value.y:F6},{value.z:F6})";

        private static string FormatShort(Vector3 value) =>
            $"({value.y:F0},{value.x:F0},{value.z:F0})";

        private static Vector3 SignedEuler(Quaternion rotation)
        {
            Vector3 euler = rotation.eulerAngles;
            euler.x = Mathf.DeltaAngle(0f, euler.x);
            euler.y = Mathf.DeltaAngle(0f, euler.y);
            euler.z = Mathf.DeltaAngle(0f, euler.z);
            return euler;
        }

        private static string Format(Quaternion value) =>
            $"({value.x:F6},{value.y:F6},{value.z:F6},{value.w:F6})";

        private static string Format(Matrix4x4 value) =>
            $"[{value.m00:F6},{value.m01:F6},{value.m02:F6},{value.m03:F6};"
            + $"{value.m10:F6},{value.m11:F6},{value.m12:F6},{value.m13:F6};"
            + $"{value.m20:F6},{value.m21:F6},{value.m22:F6},{value.m23:F6};"
            + $"{value.m30:F6},{value.m31:F6},{value.m32:F6},{value.m33:F6}]";

        private bool DiagnosticsEnabled =>
            poseCoordinateDebug && (Debug.isDebugBuild || Application.isEditor);
    }
}
