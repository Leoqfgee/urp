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

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }
    }
}
