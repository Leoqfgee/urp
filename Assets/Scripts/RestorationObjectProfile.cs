using System;
using UnityEngine;
using UnityEngine.Serialization;
using Urp.ArDemo.Calibration;

namespace Urp.ArDemo
{
    [Serializable]
    public struct PhysicalMeasurement
    {
        public string label;
        public float modelDistanceUnits;
        public float realDistanceMeters;
        public bool verified;
    }

    [Serializable]
    public sealed class TrackingSettings
    {
        public int minimumGoodMatches = 8;
        public int minimumPoseInliers = 6;
        public float minimumInlierRatio = 0.35f;
        public float maximumReprojectionErrorPixels = 3.0f;
        public float maximumReprojectionMaxPixels = 8.0f;
        public float minimumCoverageX = 0.05f;
        public float minimumCoverageY = 0.18f;
        public int registrationConfirmationFrames = 8;
        public float registrationPositionToleranceMeters = 0.025f;
        public float registrationRotationToleranceDegrees = 8f;
        public float temporaryLossHoldSeconds = 0.8f;
        [Range(0.01f, 1f)] public float positionSmoothing = 0.30f;
        [Range(0.01f, 1f)] public float rotationSmoothing = 0.25f;
    }

    [CreateAssetMenu(menuName = "URP AR/Restoration Object Profile")]
    public sealed class RestorationObjectProfile : ScriptableObject
    {
        [Header("Identity and copy")]
        public string objectId;
        public string displayName;
        [TextArea] public string shortDescription;
        [TextArea] public string viewerDescription;
        [TextArea] public string trackingDescription;
        public string missingPartName;
        public Texture2D thumbnail;

        [Header("Viewer assets")]
        public GameObject damagedViewerPrefab;
        public GameObject completeViewerPrefab;
        public Material viewerMaterial;
        public Vector3 defaultViewerEuler;
        [Range(0.05f, 0.5f)] public float viewerMargin = 0.18f;

        [Header("Tracking and repair")]
        [Tooltip("Blender-authored rigid hierarchy BottleRepairRoot/DamagedBottleB + BottleCapC.")]
        public GameObject registeredBottlePairPrefab;
        [Tooltip("The same new B asset used for inspection and tracking.")]
        public GameObject trackingReferencePrefab;
        [Tooltip("Natural features generated from B only; C is excluded.")]
        public TextAsset trackingReferenceDatabase;
        public RepairCalibrationProfile calibration;
        public TrackingSettings trackingSettings = new TrackingSettings();
        public Material repairMaterial;
        [FormerlySerializedAs("referenceValidationMaterial")]
        [Tooltip("Depth-only material for B after Start; B remains in the rigid hierarchy and occludes C without drawing color.")]
        public Material referenceDepthOcclusionMaterial;

        [Header("Physical scale")]
        public bool physicalScaleVerified;
        public PhysicalMeasurement[] physicalMeasurements = Array.Empty<PhysicalMeasurement>();

        public bool HasTrackingAssets =>
            registeredBottlePairPrefab != null
            && trackingReferenceDatabase != null
            && calibration != null;
    }
}
