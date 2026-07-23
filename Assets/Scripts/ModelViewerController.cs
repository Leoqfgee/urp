using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using EnhancedTouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Urp.ArDemo
{
    public sealed class ModelViewerController : MonoBehaviour
    {
        [SerializeField] private Camera viewerCamera;
        [SerializeField] private Transform modelViewRoot;
        [SerializeField] private RawImage viewportImage;
        [SerializeField] private Text statusText;
        [SerializeField] private float rotationSensitivity = 0.18f;
        [SerializeField] private float minimumZoom = 0.5f;
        [SerializeField] private float maximumZoom = 2.5f;
        [SerializeField] private int maximumRenderTextureSize = 1536;

        private ModelViewportInputHandler viewport;
        private RestorationObjectProfile profile;
        private Transform modelPivot;
        private Transform actualModel;
        private GameObject damagedInstance;
        private GameObject completeInstance;
        private RenderTexture renderTexture;
        private ModelViewState damagedState;
        private ModelViewState completeState;
        private ModelViewState activeState;
        private bool gesturesBlocked;
        private float previousPinchDistance;
        private Vector2Int lastTextureSize;
        private bool enhancedTouchEnabled;

        public Camera ViewerCamera => viewerCamera;
        public RawImage ViewportImage => viewportImage;
        public Transform ActiveModel => actualModel;

        public void BindStatusText(Text value) => statusText = value;
        public void BindViewportImage(RawImage value) => viewportImage = value;
        public void BindViewport(ModelViewportInputHandler value) => viewport = value;
        public void SetGesturesBlocked(bool value)
        {
            gesturesBlocked = value;
            if (value)
            {
                EndPointerDrag();
            }
        }

        public void SetProfile(RestorationObjectProfile value)
        {
            if (ReferenceEquals(profile, value) && damagedInstance != null)
            {
                ShowDamagedModel();
                return;
            }
            profile = value;
            BuildProfileModels();
            ShowDamagedModel();
        }

        public void SetViewerEnabled(bool enabled)
        {
            SetEnhancedTouchEnabled(enabled);
            if (viewerCamera != null)
            {
                viewerCamera.gameObject.SetActive(enabled);
                viewerCamera.enabled = enabled;
                viewerCamera.targetTexture = enabled ? renderTexture : null;
            }

            if (enabled)
            {
                EnsureRenderTexture(true);
                ReframeActiveModel();
            }
            else
            {
                ReleaseRenderTexture();
            }

            previousPinchDistance = 0f;
        }

        public void ShowDamagedModel()
        {
            SetActiveState(damagedState, damagedInstance, completeInstance);
            UpdateStatus(profile == null
                ? "请选择对象。"
                : $"{profile.displayName} · 残缺模型");
        }

        public void ShowCompleteModel()
        {
            ModelViewState requested = completeState ?? damagedState;
            SetActiveState(requested, completeInstance ?? damagedInstance, damagedInstance);
            UpdateStatus(completeInstance == null
                ? "当前仅有一套重建模型，完整模型尚未提供。"
                : $"{profile.displayName} · 完整模型");
        }

        public void ResetView()
        {
            activeState?.Reset();
            ReframeActiveModel();
            UpdateStatus("已恢复当前模型的标准视角。");
        }

        public void BeginPointerDrag(Vector2 position)
        {
            if (!gesturesBlocked && viewport != null && viewport.Contains(position))
            {
                activeState?.BeginDrag();
            }
        }

        public void DragPointer(Vector2 delta)
        {
            if (!gesturesBlocked && activeState != null && activeState.IsDragging)
            {
                Rotate(delta);
            }
        }

        public void EndPointerDrag()
        {
            activeState?.EndDrag();
        }

        public void Scroll(float amount)
        {
            if (gesturesBlocked || activeState == null || Mathf.Abs(amount) < 0.001f)
            {
                return;
            }

            activeState.Zoom = Mathf.Clamp(
                activeState.Zoom * Mathf.Exp(amount * 0.12f),
                minimumZoom,
                maximumZoom);
            activeState.Apply();
        }

        private void Update()
        {
            if (viewerCamera == null || !viewerCamera.isActiveAndEnabled || activeState == null)
            {
                return;
            }

            EnsureRenderTexture(false);
            HandleTouches();
        }

        private void OnDestroy()
        {
            SetEnhancedTouchEnabled(false);
            ReleaseRenderTexture();
        }

        private void HandleTouches()
        {
            if (gesturesBlocked || viewport == null)
            {
                previousPinchDistance = 0f;
                return;
            }

            var touches = EnhancedTouch.activeTouches;
            if (touches.Count >= 2)
            {
                EnhancedTouch first = touches[0];
                EnhancedTouch second = touches[1];
                activeState.EndDrag();
                if (!viewport.Contains(first.screenPosition)
                    || !viewport.Contains(second.screenPosition))
                {
                    previousPinchDistance = 0f;
                    return;
                }

                float distance = Vector2.Distance(first.screenPosition, second.screenPosition);
                if (previousPinchDistance > 1f)
                {
                    activeState.Zoom = Mathf.Clamp(
                        activeState.Zoom * distance / previousPinchDistance,
                        minimumZoom,
                        maximumZoom);
                    activeState.Apply();
                }

                previousPinchDistance = distance;
                return;
            }

            previousPinchDistance = 0f;
            if (touches.Count != 1)
            {
                activeState.EndDrag();
                return;
            }

            EnhancedTouch touch = touches[0];
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began
                && viewport.Contains(touch.screenPosition))
            {
                activeState.BeginDrag();
            }
            else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved
                     && activeState.IsDragging)
            {
                Rotate(touch.delta);
            }
            else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended
                     || touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
            {
                activeState.EndDrag();
            }
        }

        private void SetEnhancedTouchEnabled(bool enabled)
        {
            if (enabled == enhancedTouchEnabled)
            {
                return;
            }

            if (enabled)
            {
                EnhancedTouchSupport.Enable();
            }
            else
            {
                EnhancedTouchSupport.Disable();
            }
            enhancedTouchEnabled = enabled;
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

        private void BuildProfileModels()
        {
            if (modelViewRoot == null)
            {
                modelViewRoot = transform;
            }

            if (damagedInstance != null)
            {
                Destroy(damagedInstance);
            }

            if (completeInstance != null)
            {
                Destroy(completeInstance);
            }

            damagedInstance = InstantiateModel(
                profile?.damagedViewerPrefab, "Damaged Viewer Model");
            completeInstance = InstantiateModel(
                profile?.completeViewerPrefab, "Complete Viewer Model");
            SetCompletionPartVisible(damagedInstance, false);
            SetCompletionPartVisible(completeInstance, true);
            damagedState = damagedInstance == null ? null : new ModelViewState(damagedInstance.transform);
            completeState = completeInstance == null ? null : new ModelViewState(completeInstance.transform);
        }

        private GameObject InstantiateModel(GameObject prefab, string instanceName)
        {
            if (prefab == null)
            {
                return null;
            }

            GameObject pivotObject = new GameObject(instanceName + " Pivot");
            pivotObject.transform.SetParent(modelViewRoot, false);
            GameObject instance = Instantiate(prefab, pivotObject.transform);
            instance.name = instanceName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(profile.defaultViewerEuler);
            SetLayerRecursively(instance, viewerCamera.gameObject.layer);
            if (profile.viewerMaterial != null)
            {
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterial = profile.viewerMaterial;
                }
            }

            Bounds bounds = CalculateBounds(instance);
            instance.transform.position += pivotObject.transform.position - bounds.center;
            pivotObject.SetActive(false);
            return pivotObject;
        }

        private void SetActiveState(ModelViewState state, GameObject active, GameObject inactive)
        {
            activeState = state;
            if (inactive != null)
            {
                inactive.SetActive(false);
            }

            if (active != null)
            {
                active.SetActive(true);
                actualModel = active.transform;
            }

            activeState?.Reset();
            ReframeActiveModel();
        }

        private void ReframeActiveModel()
        {
            if (viewerCamera == null || actualModel == null || !actualModel.gameObject.activeInHierarchy)
            {
                return;
            }

            Bounds bounds = CalculateBounds(actualModel.gameObject);
            float radius = Mathf.Max(0.01f, bounds.extents.magnitude);
            float aspect = renderTexture != null
                ? renderTexture.width / (float)Mathf.Max(1, renderTexture.height)
                : Mathf.Max(0.1f, viewerCamera.aspect);
            float verticalHalf = viewerCamera.fieldOfView * Mathf.Deg2Rad * 0.5f;
            float horizontalHalf = Mathf.Atan(Mathf.Tan(verticalHalf) * aspect);
            float margin = Mathf.Clamp(profile != null ? profile.viewerMargin : 0.18f, 0.05f, 0.5f);
            float verticalDistance = bounds.extents.y / Mathf.Max(0.01f, Mathf.Tan(verticalHalf));
            float horizontalDistance = bounds.extents.x / Mathf.Max(0.01f, Mathf.Tan(horizontalHalf));
            float distance = (Mathf.Max(verticalDistance, horizontalDistance) + bounds.extents.z)
                * (1f + margin);

            viewerCamera.transform.position = bounds.center - Vector3.forward * distance;
            viewerCamera.transform.rotation = Quaternion.identity;
            viewerCamera.nearClipPlane = Mathf.Max(0.005f, distance - radius * 1.5f);
            viewerCamera.farClipPlane = Mathf.Max(viewerCamera.nearClipPlane + 1f, distance + radius * 3f);
        }

        private void EnsureRenderTexture(bool force)
        {
            if (viewportImage == null || viewerCamera == null)
            {
                return;
            }

            Rect rect = viewportImage.rectTransform.rect;
            float scale = canvasScale(viewportImage.canvas);
            int width = Mathf.Clamp(Mathf.RoundToInt(rect.width * scale), 64, maximumRenderTextureSize);
            int height = Mathf.Clamp(Mathf.RoundToInt(rect.height * scale), 64, maximumRenderTextureSize);
            Vector2Int size = new Vector2Int(width, height);
            if (!force && renderTexture != null && size == lastTextureSize)
            {
                return;
            }

            ReleaseRenderTexture();
            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "URP Model Viewport",
                antiAliasing = 2,
                useMipMap = false
            };
            renderTexture.Create();
            lastTextureSize = size;
            viewportImage.texture = renderTexture;
            viewerCamera.targetTexture = renderTexture;
        }

        private static float canvasScale(Canvas value)
        {
            return value == null ? 1f : Mathf.Max(0.01f, value.scaleFactor);
        }

        private void ReleaseRenderTexture()
        {
            if (viewerCamera != null && viewerCamera.targetTexture == renderTexture)
            {
                viewerCamera.targetTexture = null;
            }

            if (viewportImage != null && viewportImage.texture == renderTexture)
            {
                viewportImage.texture = null;
            }

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static void SetCompletionPartVisible(GameObject root, bool visible)
        {
            Transform completion = FindDescendant(root != null ? root.transform : null, "BottleCapC");
            if (completion == null)
            {
                return;
            }
            foreach (Renderer renderer in completion.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = visible;
            }
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == objectName)
            {
                return root;
            }
            for (int index = 0; index < root.childCount; index++)
            {
                Transform found = FindDescendant(root.GetChild(index), objectName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private sealed class ModelViewState
        {
            private readonly Vector3 initialPosition;
            private readonly Quaternion initialRotation;
            private readonly Vector3 initialScale;

            public ModelViewState(Transform transform)
            {
                Transform = transform;
                initialPosition = transform.localPosition;
                initialRotation = transform.localRotation;
                initialScale = transform.localScale;
            }

            public Transform Transform { get; }
            public float Yaw { get; set; }
            public float Pitch { get; set; }
            public float Zoom { get; set; } = 1f;
            public bool IsDragging { get; private set; }

            public void BeginDrag() => IsDragging = true;
            public void EndDrag() => IsDragging = false;

            public void Reset()
            {
                Yaw = 0f;
                Pitch = 0f;
                Zoom = 1f;
                Transform.localPosition = initialPosition;
                Transform.localRotation = initialRotation;
                Transform.localScale = initialScale;
                IsDragging = false;
            }

            public void Apply()
            {
                Transform.localRotation =
                    initialRotation * Quaternion.Euler(Pitch, Yaw, 0f);
                Transform.localScale = initialScale * Zoom;
            }
        }
    }
}
