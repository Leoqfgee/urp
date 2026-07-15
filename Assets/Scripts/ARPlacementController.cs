using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Urp.ArDemo
{
    public sealed class ARPlacementController : MonoBehaviour
    {
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private Camera arCamera;
        [SerializeField] private Transform trackedContentRoot;
        [SerializeField] private Text statusText;
        [SerializeField] private float defaultDistance = 0.75f;
        [SerializeField] private bool allowTapPlacement = true;

        private static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
        }

        private void OnDisable()
        {
            EnhancedTouchSupport.Disable();
        }

        private void Start()
        {
            PlaceInFrontOfCamera();
        }

        private void Update()
        {
            if (!allowTapPlacement || Touch.activeTouches.Count == 0)
            {
                return;
            }

            Touch touch = Touch.activeTouches[0];
            if (touch.phase != UnityEngine.InputSystem.TouchPhase.Began)
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.touchId))
            {
                return;
            }

            TryPlaceAtScreenPosition(touch.screenPosition);
        }

        public void ToggleTapPlacement()
        {
            allowTapPlacement = !allowTapPlacement;
            UpdateStatus(allowTapPlacement ? "已开启点击放置。" : "已锁定点击放置。");
        }

        public void PlaceInFrontOfCamera()
        {
            if (trackedContentRoot == null || arCamera == null)
            {
                return;
            }

            Transform cameraTransform = arCamera.transform;
            Vector3 forward = cameraTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = cameraTransform.forward;
            }

            trackedContentRoot.position = cameraTransform.position + forward.normalized * defaultDistance;
            trackedContentRoot.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            trackedContentRoot.gameObject.SetActive(true);
            UpdateStatus("已把修复结果放到摄像头前方，可继续对准瓶身识别。");
        }

        public void ResetPlacement()
        {
            PlaceInFrontOfCamera();
        }

        public void PlaceAtScreenCenter()
        {
            if (arCamera == null)
            {
                PlaceInFrontOfCamera();
                return;
            }

            TryPlaceAtScreenPosition(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        }

        private void TryPlaceAtScreenPosition(Vector2 screenPosition)
        {
            if (raycastManager == null || trackedContentRoot == null)
            {
                return;
            }

            if (!raycastManager.Raycast(screenPosition, Hits, TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint))
            {
                PlaceByCameraProjection(screenPosition);
                return;
            }

            Pose hitPose = Hits[0].pose;
            trackedContentRoot.SetPositionAndRotation(hitPose.position, hitPose.rotation);
            trackedContentRoot.gameObject.SetActive(true);
            UpdateStatus("已放置到检测到的平面上。");
        }

        private void PlaceByCameraProjection(Vector2 screenPosition)
        {
            if (trackedContentRoot == null || arCamera == null)
            {
                return;
            }

            Ray ray = arCamera.ScreenPointToRay(screenPosition);
            trackedContentRoot.position = ray.origin + ray.direction.normalized * defaultDistance;

            Vector3 forward = arCamera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = arCamera.transform.forward;
            }

            trackedContentRoot.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            trackedContentRoot.gameObject.SetActive(true);
            UpdateStatus("暂未检测到平面，已按摄像头方向临时放置。");
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
