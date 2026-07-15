using UnityEngine;
using UnityEngine.UI;

namespace Urp.ArDemo
{
    public sealed class RepairOverlayController : MonoBehaviour
    {
        [SerializeField] private Transform repairRoot;
        [SerializeField] private Text statusText;
        [SerializeField] private OrbImageTrackingController orbTracker;

        private bool showRepair = true;

        public bool ShowRepair => showRepair;

        public void BindStatusText(Text value)
        {
            statusText = value;
        }

        private void Awake()
        {
            if (repairRoot != null)
            {
                repairRoot.gameObject.SetActive(false);
            }
        }

        public void StartRecognition()
        {
            showRepair = true;
            if (orbTracker != null)
            {
                orbTracker.SetRepairVisible(true);
                orbTracker.StartRecognition();
            }
            else
            {
                UpdateStatus("识别模块未加载。");
            }
        }

        public void ShowBeforeRepair()
        {
            showRepair = false;
            if (orbTracker != null)
            {
                orbTracker.SetRepairVisible(false);
            }
            else
            {
                ApplyRepairVisibility();
            }

            UpdateStatus("修复前：仅显示真实的无瓶盖饮料瓶。");
        }

        public void ShowAfterRepair()
        {
            showRepair = true;
            if (orbTracker == null || !orbTracker.HasTrackedPose)
            {
                UpdateStatus("尚未获得稳定瓶身位姿，请先点击开始识别并对准瓶身。");
                return;
            }

            orbTracker.SetRepairVisible(true);
            UpdateStatus("修复后：虚拟瓶盖已按瓶口位姿进行叠加。");
        }

        public void ResetRecognition()
        {
            showRepair = true;
            if (orbTracker != null)
            {
                orbTracker.SetRepairVisible(true);
                orbTracker.ResetTracking();
            }
            else
            {
                UpdateStatus("识别模块未加载。");
            }
        }

        private void ApplyRepairVisibility()
        {
            if (repairRoot != null)
            {
                repairRoot.gameObject.SetActive(showRepair);
            }
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
