using UnityEngine;
using UnityEngine.EventSystems;

namespace Urp.ArDemo
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class ModelViewportInputHandler : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IDragHandler,
        IScrollHandler,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        [SerializeField] private ModelViewerController viewer;
        private bool pointerInside;

        public RectTransform RectTransform => (RectTransform)transform;

        public void Bind(ModelViewerController value)
        {
            viewer = value;
            viewer?.BindViewport(this);
        }

        public bool Contains(Vector2 screenPoint)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(
                RectTransform, screenPoint, null);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.pointerId < 0 && eventData.button == PointerEventData.InputButton.Left)
            {
                viewer?.BeginPointerDrag(eventData.position);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId < 0)
            {
                viewer?.EndPointerDrag();
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId < 0 && eventData.button == PointerEventData.InputButton.Left)
            {
                viewer?.DragPointer(eventData.delta);
            }
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (pointerInside)
            {
                viewer?.Scroll(eventData.scrollDelta.y);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            pointerInside = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            pointerInside = false;
            viewer?.EndPointerDrag();
        }
    }
}
