using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Urp.ArDemo
{
    public sealed class ModelViewerController : MonoBehaviour
    {
        [SerializeField] private Camera viewerCamera;
        [SerializeField] private Transform damagedModel;
        [SerializeField] private Transform completeModel;
        [SerializeField] private RectTransform interactionArea;
        [SerializeField] private Text statusText;
        [SerializeField] private float rotationSensitivity = 0.18f;
        [SerializeField] private float minimumZoom = 0.5f;
        [SerializeField] private float maximumZoom = 2.5f;

        private ModelViewState damagedState;
        private ModelViewState completeState;
        private ModelViewState activeState;
        private bool mouseDragging;
        private Vector2 previousMouse;
        private float previousPinchDistance;

        public void BindStatusText(Text value)
        {
            statusText = value;
        }

        public void BindInteractionArea(RectTransform value)
        {
            interactionArea = value;
        }

        public void SetViewerEnabled(bool enabled)
        {
            if (viewerCamera != null)
            {
                viewerCamera.gameObject.SetActive(enabled);
            }

            mouseDragging = false;
            previousPinchDistance = 0f;
        }

        public void ShowDamagedModel()
        {
            SetActiveState(damagedState);
            UpdateStatus("残缺模型：单指拖动旋转，双指捏合缩放。");
        }

        public void ShowCompleteModel()
        {
            SetActiveState(completeState);
            UpdateStatus("完整模型：单指拖动旋转，双指捏合缩放。");
        }

        public void ResetView()
        {
            activeState?.Reset();
            UpdateStatus("已恢复当前模型的标准视角。");
        }

        private void Awake()
        {
            damagedState = new ModelViewState(damagedModel);
            completeState = new ModelViewState(completeModel);
            ShowDamagedModel();
        }

        private void Update()
        {
            if (viewerCamera == null
                || !viewerCamera.gameObject.activeInHierarchy
                || activeState == null
                || activeState.Transform == null)
            {
                return;
            }

            HandleTouches();
            HandleMouse();
        }

        private void HandleTouches()
        {
            if (Input.touchCount >= 2)
            {
                Touch first = Input.GetTouch(0);
                Touch second = Input.GetTouch(1);
                if (!Contains(first.position) || !Contains(second.position))
                {
                    previousPinchDistance = 0f;
                    return;
                }

                float distance = Vector2.Distance(first.position, second.position);
                if (previousPinchDistance > 1f)
                {
                    activeState.Zoom *= distance / previousPinchDistance;
                    activeState.Zoom = Mathf.Clamp(activeState.Zoom, minimumZoom, maximumZoom);
                    activeState.Apply();
                }

                previousPinchDistance = distance;
                return;
            }

            previousPinchDistance = 0f;
            if (Input.touchCount != 1)
            {
                return;
            }

            Touch touch = Input.GetTouch(0);
            if (touch.phase != TouchPhase.Moved
                || !Contains(touch.position)
                || IsOverBlockingUi(touch.fingerId))
            {
                return;
            }

            Rotate(touch.deltaPosition);
        }

        private void HandleMouse()
        {
            Vector2 pointer = Input.mousePosition;
            if (Input.GetMouseButtonDown(0) && Contains(pointer) && !IsOverBlockingUi())
            {
                previousMouse = pointer;
                mouseDragging = true;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                mouseDragging = false;
            }
            else if (mouseDragging && Input.GetMouseButton(0))
            {
                Rotate(pointer - previousMouse);
                previousMouse = pointer;
            }

            if (Contains(pointer))
            {
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.001f)
                {
                    activeState.Zoom = Mathf.Clamp(
                        activeState.Zoom * Mathf.Exp(wheel * 0.12f),
                        minimumZoom,
                        maximumZoom);
                    activeState.Apply();
                }
            }
        }

        private void Rotate(Vector2 delta)
        {
            activeState.Yaw -= delta.x * rotationSensitivity;
            activeState.Pitch = Mathf.Clamp(
                activeState.Pitch + delta.y * rotationSensitivity,
                -80f,
                80f);
            activeState.Apply();
        }

        private void SetActiveState(ModelViewState state)
        {
            activeState = state;
            if (damagedModel != null)
            {
                damagedModel.gameObject.SetActive(state == damagedState);
            }

            if (completeModel != null)
            {
                completeModel.gameObject.SetActive(state == completeState);
            }

            activeState?.Reset();
        }

        private bool Contains(Vector2 screenPosition)
        {
            return interactionArea == null
                || RectTransformUtility.RectangleContainsScreenPoint(interactionArea, screenPosition);
        }

        private static bool IsOverBlockingUi(int pointerId = -1)
        {
            return EventSystem.current != null
                && (pointerId >= 0
                    ? EventSystem.current.IsPointerOverGameObject(pointerId)
                    : EventSystem.current.IsPointerOverGameObject());
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private sealed class ModelViewState
        {
            private readonly Vector3 initialPosition;
            private readonly Quaternion initialRotation;
            private readonly Vector3 initialScale;

            public ModelViewState(Transform transform)
            {
                Transform = transform;
                if (transform == null)
                {
                    return;
                }

                initialPosition = transform.localPosition;
                initialRotation = transform.localRotation;
                initialScale = transform.localScale;
            }

            public Transform Transform { get; }
            public float Yaw { get; set; }
            public float Pitch { get; set; }
            public float Zoom { get; set; } = 1f;

            public void Reset()
            {
                if (Transform == null)
                {
                    return;
                }

                Yaw = 0f;
                Pitch = 0f;
                Zoom = 1f;
                Transform.localPosition = initialPosition;
                Apply();
            }

            public void Apply()
            {
                if (Transform == null)
                {
                    return;
                }

                Transform.localRotation = initialRotation * Quaternion.Euler(Pitch, Yaw, 0f);
                Transform.localScale = initialScale * Zoom;
            }
        }
    }
}
