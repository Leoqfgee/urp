using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Urp.ArDemo
{
    public sealed class ImageTrackedRepairController : MonoBehaviour
    {
        [SerializeField] private ARTrackedImageManager trackedImageManager;
        [SerializeField] private Transform trackedContentRoot;
        [SerializeField] private Text statusText;
        [SerializeField] private Vector3 contentLocalPosition = new Vector3(0f, 0.08f, 0f);
        [SerializeField] private Vector3 contentLocalEulerAngles = new Vector3(90f, 0f, 180f);
        [SerializeField] private Vector3 contentLocalScale = new Vector3(0.65f, 0.65f, 0.65f);

        private void Awake()
        {
            if (trackedContentRoot != null)
            {
                trackedContentRoot.gameObject.SetActive(false);
            }

            UpdateStatus("Point the camera at the coconut juice label.");
        }

        private void OnEnable()
        {
            if (trackedImageManager != null)
            {
                trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
            }
        }

        private void OnDisable()
        {
            if (trackedImageManager != null)
            {
                trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
            }
        }

        private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs args)
        {
            foreach (ARTrackedImage trackedImage in args.added)
            {
                ApplyTrackedImagePose(trackedImage);
            }

            foreach (ARTrackedImage trackedImage in args.updated)
            {
                ApplyTrackedImagePose(trackedImage);
            }
        }

        private void ApplyTrackedImagePose(ARTrackedImage trackedImage)
        {
            if (trackedContentRoot == null)
            {
                return;
            }

            bool isTracking = trackedImage.trackingState == TrackingState.Tracking;
            trackedContentRoot.gameObject.SetActive(isTracking);
            if (!isTracking)
            {
                UpdateStatus("Target found, tracking is unstable. Hold the phone steady.");
                return;
            }

            trackedContentRoot.SetParent(trackedImage.transform, false);
            trackedContentRoot.localPosition = contentLocalPosition;
            trackedContentRoot.localRotation = Quaternion.Euler(contentLocalEulerAngles);
            trackedContentRoot.localScale = contentLocalScale;
            UpdateStatus($"Tracking target: {trackedImage.referenceImage.name}");
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
