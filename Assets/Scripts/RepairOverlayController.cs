using UnityEngine;
using UnityEngine.UI;

namespace Urp.ArDemo
{
    public sealed class RepairOverlayController : MonoBehaviour
    {
        [SerializeField] private Transform repairRoot;
        [SerializeField] private GameObject infoPanel;
        [SerializeField] private Text statusText;
        [SerializeField] private float rotationStepDegrees = 15f;
        [SerializeField] private float minScale = 0.25f;
        [SerializeField] private float maxScale = 3.0f;

        private Vector3 initialScale = Vector3.one;

        private void Awake()
        {
            if (repairRoot != null)
            {
                initialScale = repairRoot.localScale;
            }

            UpdateStatus("AR demo ready. Point the camera at the target object.");
        }

        public void ToggleRepair()
        {
            if (repairRoot == null)
            {
                UpdateStatus("No repair model assigned.");
                return;
            }

            bool nextState = !repairRoot.gameObject.activeSelf;
            repairRoot.gameObject.SetActive(nextState);
            UpdateStatus(nextState ? "Virtual repair is visible." : "Virtual repair is hidden.");
        }

        public void ToggleInfo()
        {
            if (infoPanel == null)
            {
                return;
            }

            infoPanel.SetActive(!infoPanel.activeSelf);
        }

        public void RotateLeft()
        {
            Rotate(-rotationStepDegrees);
        }

        public void RotateRight()
        {
            Rotate(rotationStepDegrees);
        }

        public void SetScale(float value)
        {
            if (repairRoot == null)
            {
                return;
            }

            float clamped = Mathf.Clamp(value, minScale, maxScale);
            repairRoot.localScale = initialScale * clamped;
            UpdateStatus($"Repair scale: {clamped:0.00}x");
        }

        public void ResetRepair()
        {
            if (repairRoot == null)
            {
                return;
            }

            repairRoot.localRotation = Quaternion.identity;
            repairRoot.localScale = initialScale;
            repairRoot.gameObject.SetActive(true);
            UpdateStatus("Virtual repair reset.");
        }

        private void Rotate(float degrees)
        {
            if (repairRoot == null)
            {
                UpdateStatus("No repair model assigned.");
                return;
            }

            repairRoot.Rotate(Vector3.up, degrees, Space.Self);
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
