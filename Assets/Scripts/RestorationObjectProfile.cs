using System;
using UnityEngine;
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
        public int minimumGoodMatches = 24;
        public int minimumPoseInliers = 20;
        public float minimumInlierRatio = 0.5f;
        public float maximumReprojectionErrorPixels = 2.5f;
        public float minimumCoverageX = 0.12f;
        public float minimumCoverageY = 0.20f;
        public float lostPoseGraceSeconds = 2.5f;
        public float maximumPositionJumpMeters = 0.06f;
        public float maximumRotationJumpDegrees = 18f;
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
        public GameObject registeredRepairPrefab;
        public GameObject registeredOccluderPrefab;
        public TextAsset orbModelDatabase;
        public RepairCalibrationProfile calibration;
        public TrackingSettings trackingSettings = new TrackingSettings();
        public Material repairMaterial;
        public Material initialGuideMaterial;

        [Header("Physical scale")]
        public bool physicalScaleVerified;
        public PhysicalMeasurement[] physicalMeasurements = Array.Empty<PhysicalMeasurement>();

        public bool HasTrackingAssets =>
            orbModelDatabase != null && calibration != null && registeredRepairPrefab != null;
    }
}
