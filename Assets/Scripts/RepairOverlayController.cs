using UnityEngine;
using UnityEngine.UI;

namespace Urp.ArDemo
{
    public sealed class RepairOverlayController : MonoBehaviour
    {
        [SerializeField] private Text statusText;
        [SerializeField] private OrbImageTrackingController orbTracker;

        public void BindStatusText(Text value)
        {
            statusText = value;
        }

        public void StartRecognition()
        {
            if (orbTracker == null)
            {
                UpdateStatus("识别模块未加载。");
                return;
            }

            orbTracker.StartRecognition();
        }

        public void ResetRecognition()
        {
            if (orbTracker == null)
            {
                UpdateStatus("识别模块未加载。");
                return;
            }

            orbTracker.ResetTracking();
        }

        public void SetRepairVisible(bool visible)
        {
            if (orbTracker == null)
            {
                UpdateStatus("识别模块未加载。");
                return;
            }

            // This changes presentation only. The tracked rigid root, PnP pose,
            // B/C local transforms and occlusion calibration are untouched.
            orbTracker.SetRepairPresentationEnabled(visible);
        }

        public void ToggleRegistrationDebugMode()
        {
            if (orbTracker == null)
            {
                UpdateStatus("RegistrationDebugMode unavailable.");
                return;
            }
            orbTracker.ToggleRegistrationDebugMode();
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }
    }
}
