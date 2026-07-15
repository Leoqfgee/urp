using UnityEngine;
using UnityEngine.UI;

namespace Urp.ArDemo
{
    public sealed class ModelViewerController : MonoBehaviour
    {
        [SerializeField] private Camera viewerCamera;
        [SerializeField] private Transform damagedModel;
        [SerializeField] private Transform completeModel;
        [SerializeField] private Text statusText;

        private Transform activeModel;
        private float zoom = 1f;
        private Vector2 previousPointer;
        private bool dragging;

        public void SetViewerEnabled(bool enabled)
        {
            if (viewerCamera != null)
            {
                viewerCamera.gameObject.SetActive(enabled);
            }

            enabled = enabled && gameObject.activeInHierarchy;
            if (!enabled)
            {
                dragging = false;
            }
        }

        public void BindStatusText(Text value)
        {
            statusText = value;
        }

        public void ShowDamagedModel()
        {
            SetActiveModel(damagedModel);
            UpdateStatus("正在查看残缺饮料瓶三维重建模型。");
        }

        public void ShowCompleteModel()
        {
            SetActiveModel(completeModel);
            UpdateStatus("正在查看完整饮料瓶三维重建模型。");
        }

        public void RotateModel()
        {
            if (activeModel != null)
            {
                activeModel.Rotate(Vector3.up, 30f, Space.World);
                UpdateStatus("模型已向右旋转 30°，也可以在模型区域单指拖动。");
            }
        }

        public void ToggleZoom()
        {
            zoom = zoom < 1.1f ? 1.3f : zoom > 1.15f ? 0.82f : 1f;
            ApplyZoom();
            UpdateStatus(zoom > 1f ? "模型已放大。" : zoom < 1f ? "模型已缩小。" : "模型已恢复原始大小。");
        }

        public void ResetView()
        {
            zoom = 1f;
            if (damagedModel != null)
            {
                damagedModel.localRotation = Quaternion.identity;
            }

            if (completeModel != null)
            {
                completeModel.localRotation = Quaternion.identity;
            }

            ApplyZoom();
        }

        private void Awake()
        {
            ShowDamagedModel();
        }

        private void Update()
        {
            if (viewerCamera == null || !viewerCamera.gameObject.activeInHierarchy || activeModel == null)
            {
                return;
            }

            if (Input.touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Moved)
                {
                    activeModel.Rotate(Vector3.up, -touch.deltaPosition.x * 0.2f, Space.World);
                }
            }
            else if (Input.GetMouseButtonDown(0))
            {
                previousPointer = Input.mousePosition;
                dragging = true;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                dragging = false;
            }
            else if (dragging && Input.GetMouseButton(0))
            {
                Vector2 pointer = Input.mousePosition;
                activeModel.Rotate(Vector3.up, -(pointer.x - previousPointer.x) * 0.2f, Space.World);
                previousPointer = pointer;
            }
        }

        private void SetActiveModel(Transform model)
        {
            activeModel = model;
            if (damagedModel != null)
            {
                damagedModel.gameObject.SetActive(model == damagedModel);
            }

            if (completeModel != null)
            {
                completeModel.gameObject.SetActive(model == completeModel);
            }

            ApplyZoom();
        }

        private void ApplyZoom()
        {
            if (activeModel != null)
            {
                activeModel.localScale = Vector3.one * zoom;
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
