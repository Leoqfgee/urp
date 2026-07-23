using UnityEngine;

namespace Urp.ArDemo.Calibration
{
    [CreateAssetMenu(menuName = "URP AR/Repair Calibration Profile")]
    public sealed class RepairCalibrationProfile : ScriptableObject
    {
        [Header("SfM object frame")]
        public Vector3 objectOriginInModel = Vector3.zero;
        public Vector3 mouthCenterInModel;
        public Vector3 mouthRightInModel;
        public Vector3 mouthFrontInModel;
        public Vector3 neckAxisPointInModel;

        [Header("Physical scale")]
        [Min(0.000001f)] public float metersPerModelUnit = 0.18f;
        public bool physicalScaleVerified;
        [Min(0f)] public float expectedPhysicalNeckDiameter;
        [Min(0f)] public float expectedPhysicalCapDiameter = 0.0408f;
        [Min(0f)] public float expectedPhysicalCapHeight = 0.02f;

        [Header("Fixed ORB to Blender B transform (T_orb_to_b)")]
        [Tooltip("Applied once on ModelCoordinateAlignment; never applied to BottleCapC directly.")]
        public Vector3 orbToModelLocalPosition = Vector3.zero;
        public Vector3 orbToModelLocalEulerAngles = Vector3.zero;
        public Vector3 orbToModelLocalScale = Vector3.one;

        public Vector3 UpInModel
        {
            get
            {
                Vector3 value = mouthCenterInModel - neckAxisPointInModel;
                return value.sqrMagnitude > 0.000001f ? value.normalized : Vector3.up;
            }
        }

        public Vector3 RightInModel
        {
            get
            {
                Vector3 up = UpInModel;
                Vector3 value = mouthRightInModel - mouthCenterInModel;
                value = Vector3.ProjectOnPlane(value, up);
                return value.sqrMagnitude > 0.000001f ? value.normalized : Vector3.right;
            }
        }

        public Vector3 ForwardInModel
        {
            get
            {
                Vector3 up = UpInModel;
                Vector3 right = RightInModel;
                Vector3 indicatedFront = Vector3.ProjectOnPlane(
                    mouthFrontInModel - mouthCenterInModel,
                    up);
                Vector3 forward = Vector3.Cross(right, up).normalized;
                if (indicatedFront.sqrMagnitude > 0.000001f
                    && Vector3.Dot(forward, indicatedFront) < 0f)
                {
                    forward = -forward;
                }

                return forward;
            }
        }

        public bool HasValidFrame
        {
            get
            {
                Vector3 up = UpInModel;
                Vector3 right = RightInModel;
                Vector3 forward = ForwardInModel;
                return up.sqrMagnitude > 0.9f
                    && right.sqrMagnitude > 0.9f
                    && forward.sqrMagnitude > 0.9f
                    && Mathf.Abs(Vector3.Dot(up, right)) < 0.01f
                    && Mathf.Abs(Vector3.Dot(up, forward)) < 0.01f;
            }
        }
    }
}
