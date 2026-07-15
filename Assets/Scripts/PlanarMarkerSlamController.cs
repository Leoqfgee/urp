using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Urp.ArDemo
{
    public sealed class PlanarMarkerSlamController : MonoBehaviour
    {
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private ARTrackedImageManager imageManager;
        [SerializeField] private Transform repairRoot;
        [SerializeField] private Vector3 markerToRepairPosition = new Vector3(0f, 0.11f, 0f);
        [SerializeField] private Vector3 markerToRepairEuler = new Vector3(90f, 0f, 0f);
        [SerializeField] private float markerRepairScale = 1f;

        private Text statusText;
        private GameObject slamAnchor;
        private bool mapReady;
        private bool waitingForMarker;

        public void BindStatusText(Text value)
        {
            statusText = value;
        }

        public void SetModeEnabled(bool enabled)
        {
            if (planeManager != null)
            {
                planeManager.enabled = enabled;
                SetPlaneVisuals(enabled);
            }

            if (imageManager != null)
            {
                imageManager.enabled = enabled && waitingForMarker;
            }

            if (!enabled && repairRoot != null)
            {
                repairRoot.gameObject.SetActive(false);
            }
        }

        public void SaveAndContinue()
        {
            int trackedPlanes = 0;
            if (planeManager != null)
            {
                foreach (ARPlane plane in planeManager.trackables)
                {
                    if (plane.trackingState == TrackingState.Tracking)
                    {
                        trackedPlanes++;
                    }
                }
            }

            if (trackedPlanes == 0)
            {
                UpdateStatus("尚未建立稳定空间地图，请缓慢移动手机扫描桌面和瓶子周围。");
                return;
            }

            mapReady = true;
            SetPlaneVisuals(false);
            UpdateStatus("空间地图已保存。请点击检测平面标志，并将瓶身正面标志图放入画面。");
        }

        public void DetectMarker()
        {
            if (!mapReady)
            {
                UpdateStatus("请先扫描环境并点击保存并继续。");
                return;
            }

            waitingForMarker = true;
            if (imageManager != null)
            {
                imageManager.enabled = true;
            }

            UpdateStatus("正在检测饮料瓶平面标志。检测成功后将转为 SLAM 空间锚定。");
        }

        public void RotateRepair()
        {
            if (repairRoot != null && repairRoot.gameObject.activeInHierarchy)
            {
                repairRoot.Rotate(Vector3.up, 15f, Space.Self);
                UpdateStatus("虚拟修复瓶盖已旋转 15 度。");
            }
            else
            {
                UpdateStatus("尚未检测到平面标志。");
            }
        }

        public void ToggleRepairScale()
        {
            if (repairRoot == null || !repairRoot.gameObject.activeInHierarchy)
            {
                UpdateStatus("尚未检测到平面标志。");
                return;
            }

            float next = repairRoot.localScale.x < markerRepairScale * 1.1f
                ? markerRepairScale * 1.2f
                : markerRepairScale;
            repairRoot.localScale = Vector3.one * next;
            UpdateStatus(next > markerRepairScale ? "虚拟修复瓶盖已放大。" : "虚拟修复瓶盖已恢复原始比例。");
        }

        public void ResetMode()
        {
            mapReady = false;
            waitingForMarker = false;
            if (imageManager != null)
            {
                imageManager.enabled = false;
            }

            if (slamAnchor != null)
            {
                Destroy(slamAnchor);
                slamAnchor = null;
            }

            if (repairRoot != null)
            {
                repairRoot.gameObject.SetActive(false);
            }

            SetPlaneVisuals(true);
            UpdateStatus("请缓慢移动手机，扫描残缺饮料瓶周围环境。");
        }

        private void OnEnable()
        {
            if (imageManager != null)
            {
                imageManager.trackedImagesChanged += OnTrackedImagesChanged;
            }
        }

        private void OnDisable()
        {
            if (imageManager != null)
            {
                imageManager.trackedImagesChanged -= OnTrackedImagesChanged;
            }
        }

        private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs eventArgs)
        {
            if (!waitingForMarker)
            {
                return;
            }

            List<ARTrackedImage> images = new List<ARTrackedImage>(eventArgs.added);
            images.AddRange(eventArgs.updated);
            foreach (ARTrackedImage image in images)
            {
                if (image.trackingState != TrackingState.Tracking)
                {
                    continue;
                }

                AnchorRepairToMarker(image.transform);
                return;
            }
        }

        private void AnchorRepairToMarker(Transform marker)
        {
            if (slamAnchor != null)
            {
                Destroy(slamAnchor);
            }

            slamAnchor = new GameObject("Bottle Repair SLAM Anchor");
            slamAnchor.transform.SetPositionAndRotation(marker.position, marker.rotation);
            slamAnchor.AddComponent<ARAnchor>();

            repairRoot.SetParent(slamAnchor.transform, false);
            repairRoot.localPosition = markerToRepairPosition;
            repairRoot.localRotation = Quaternion.Euler(markerToRepairEuler);
            repairRoot.localScale = Vector3.one * markerRepairScale;
            repairRoot.gameObject.SetActive(true);

            waitingForMarker = false;
            if (imageManager != null)
            {
                imageManager.enabled = false;
            }

            UpdateStatus("平面标志已定位，虚拟修复瓶盖已转为 SLAM 空间锚定。");
        }

        private void SetPlaneVisuals(bool visible)
        {
            if (planeManager == null)
            {
                return;
            }

            foreach (ARPlane plane in planeManager.trackables)
            {
                plane.gameObject.SetActive(visible);
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
