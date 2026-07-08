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
            UpdateStatus(allowTapPlacement ? "Tap placement enabled." : "Tap placement locked.");
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
            UpdateStatus("Model placed in front of camera. Tap a surface to reposition.");
        }

        public void ResetPlacement()
        {
            PlaceInFrontOfCamera();
        }

        private void TryPlaceAtScreenPosition(Vector2 screenPosition)
        {
            if (raycastManager == null || trackedContentRoot == null)
            {
                return;
            }

            if (!raycastManager.Raycast(screenPosition, Hits, TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint))
            {
                UpdateStatus("No surface found. Move the phone slowly and try again.");
                return;
            }

            Pose hitPose = Hits[0].pose;
            trackedContentRoot.SetPositionAndRotation(hitPose.position, hitPose.rotation);
            trackedContentRoot.gameObject.SetActive(true);
            UpdateStatus("Model placed on detected surface.");
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
