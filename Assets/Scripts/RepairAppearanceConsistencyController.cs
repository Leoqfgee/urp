using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace Urp.ArDemo
{
    /// <summary>
    /// Matches C to camera lighting without changing its geometry or pose.
    /// AR Foundation supplies real-scene color/intensity and, when supported,
    /// main-light and spherical-harmonic estimates. Color correction is
    /// smoothed in HSV space before it is applied through property blocks.
    /// </summary>
    public sealed class RepairAppearanceConsistencyController : MonoBehaviour
    {
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private Light estimatedMainLight;
        [Range(0.01f, 1f)]
        [SerializeField] private float smoothing = 0.18f;
        [SerializeField] private float minimumValue = 0.45f;
        [SerializeField] private float maximumValue = 1.35f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private Renderer[] repairRenderers = Array.Empty<Renderer>();
        private MaterialPropertyBlock propertyBlock;
        private Color smoothedCorrection = Color.white;
        private bool hasCorrection;

        public bool HasLightEstimate { get; private set; }
        public Color CurrentCorrection => smoothedCorrection;

        public void BindRepairRenderers(Renderer[] renderers)
        {
            repairRenderers = renderers ?? Array.Empty<Renderer>();
            ApplyCorrection();
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
            smoothedCorrection = hasCorrection
                ? SmoothHsv(smoothedCorrection, target, smoothing)
                : target;
            hasCorrection = true;
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
