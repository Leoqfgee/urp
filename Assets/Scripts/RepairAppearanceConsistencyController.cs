using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace Urp.ArDemo
{
    /// <summary>
    /// Matches C to camera lighting without changing its geometry or pose.
    /// AR Foundation supplies scene light estimates. The tracker also samples
    /// low-saturation bottle pixels around verified A-to-B inliers, following
    /// the thesis HSV consistency step. Both signals are smoothed in HSV space
    /// and affect only C's material, never the B/C rigid transform.
    /// </summary>
    public sealed class RepairAppearanceConsistencyController : MonoBehaviour
    {
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private Light estimatedMainLight;
        [Range(0.01f, 1f)]
        [SerializeField] private float smoothing = 0.18f;
        [SerializeField] private float minimumValue = 0.45f;
        [SerializeField] private float maximumValue = 1.35f;
        [Range(0.01f, 1f)]
        [SerializeField] private float referenceSampleSmoothing = 0.14f;
        [Range(0f, 1f)]
        [SerializeField] private float maximumReferenceSaturation = 0.28f;
        [Range(0f, 1f)]
        [SerializeField] private float referenceSampleWeight = 0.65f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private Renderer[] repairRenderers = Array.Empty<Renderer>();
        private MaterialPropertyBlock propertyBlock;
        private Color sceneCorrection = Color.white;
        private Color referenceCorrection = Color.white;
        private Color smoothedCorrection = Color.white;
        private bool hasSceneCorrection;
        private bool hasReferenceCorrection;
        private float referenceConfidence;

        public bool HasLightEstimate { get; private set; }
        public Color CurrentCorrection => smoothedCorrection;

        public void BindRepairRenderers(Renderer[] renderers)
        {
            repairRenderers = renderers ?? Array.Empty<Renderer>();
            ApplyCorrection();
        }

        public void ObserveReferenceHsv(
            float hue,
            float saturation,
            float value,
            float confidence)
        {
            if (!float.IsFinite(hue)
                || !float.IsFinite(saturation)
                || !float.IsFinite(value)
                || !float.IsFinite(confidence)
                || confidence <= 0f)
            {
                return;
            }

            Color target = Color.HSVToRGB(
                Mathf.Repeat(hue, 1f),
                Mathf.Clamp(saturation, 0f, maximumReferenceSaturation),
                Mathf.Clamp(value, minimumValue, 1f));
            target.a = 1f;
            referenceCorrection = hasReferenceCorrection
                ? SmoothHsv(
                    referenceCorrection,
                    target,
                    referenceSampleSmoothing * Mathf.Clamp01(confidence))
                : target;
            referenceConfidence = Mathf.Lerp(
                referenceConfidence,
                Mathf.Clamp01(confidence),
                referenceSampleSmoothing);
            hasReferenceCorrection = true;
            RebuildCombinedCorrection();
        }

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            if (cameraManager == null)
            {
                return;
            }
            cameraManager.requestedLightEstimation =
                LightEstimation.AmbientIntensity
                | LightEstimation.AmbientColor
                | LightEstimation.AmbientSphericalHarmonics
                | LightEstimation.MainLightDirection
                | LightEstimation.MainLightIntensity;
            cameraManager.frameReceived += OnCameraFrameReceived;
        }

        private void OnDisable()
        {
            if (cameraManager != null)
            {
                cameraManager.frameReceived -= OnCameraFrameReceived;
            }
        }

        private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
        {
            ARLightEstimationData estimate = args.lightEstimation;
            Color target = estimate.colorCorrection ?? Color.white;
            if (estimate.averageBrightness.HasValue)
            {
                float valueScale = Mathf.Lerp(
                    minimumValue,
                    maximumValue,
                    Mathf.Clamp01(estimate.averageBrightness.Value));
                target.r *= valueScale;
                target.g *= valueScale;
                target.b *= valueScale;
            }
            target.a = 1f;
            sceneCorrection = hasSceneCorrection
                ? SmoothHsv(sceneCorrection, target, smoothing)
                : target;
            hasSceneCorrection = true;
            HasLightEstimate =
                estimate.colorCorrection.HasValue
                || estimate.averageBrightness.HasValue
                || estimate.mainLightDirection.HasValue
                || estimate.ambientSphericalHarmonics.HasValue;

            if (estimatedMainLight != null)
            {
                if (estimate.mainLightDirection.HasValue)
                {
                    Vector3 direction = estimate.mainLightDirection.Value;
                    if (direction.sqrMagnitude > 0.0001f)
                    {
                        estimatedMainLight.transform.rotation =
                            Quaternion.LookRotation(direction.normalized);
                    }
                }
                if (estimate.mainLightColor.HasValue)
                {
                    estimatedMainLight.color = estimate.mainLightColor.Value;
                }
                float? brightness = estimate.averageMainLightBrightness;
                if (brightness.HasValue)
                {
                    estimatedMainLight.intensity =
                        Mathf.Clamp(brightness.Value, 0.15f, 2.5f);
                }
            }
            if (estimate.ambientSphericalHarmonics.HasValue)
            {
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientProbe =
                    estimate.ambientSphericalHarmonics.Value;
            }
            RebuildCombinedCorrection();
        }

        private void RebuildCombinedCorrection()
        {
            Color target = hasSceneCorrection ? sceneCorrection : Color.white;
            if (hasReferenceCorrection)
            {
                target = SmoothHsv(
                    target,
                    referenceCorrection,
                    referenceSampleWeight * Mathf.Clamp01(referenceConfidence));
            }
            smoothedCorrection = SmoothHsv(
                smoothedCorrection,
                target,
                smoothing);
            ApplyCorrection();
        }

        private void ApplyCorrection()
        {
            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }
            foreach (Renderer renderer in repairRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }
                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(BaseColorId, smoothedCorrection);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private static Color SmoothHsv(Color current, Color target, float amount)
        {
            Color.RGBToHSV(current, out float currentHue, out float currentSaturation,
                out float currentValue);
            Color.RGBToHSV(target, out float targetHue, out float targetSaturation,
                out float targetValue);
            float hue = Mathf.Repeat(
                currentHue + Mathf.DeltaAngle(currentHue * 360f, targetHue * 360f)
                    / 360f * amount,
                1f);
            float saturation = Mathf.Lerp(currentSaturation, targetSaturation, amount);
            float value = Mathf.Lerp(currentValue, targetValue, amount);
            Color result = Color.HSVToRGB(hue, saturation, value);
            result.a = 1f;
            return result;
        }
    }
}
