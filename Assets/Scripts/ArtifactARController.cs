using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using EnhancedTouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Urp.ArDemo
{
    // Runs only in ArtifactARScene. The bottle ORB scene and its tracking controller stay separate.
    public sealed class ArtifactARController : MonoBehaviour
    {
        [SerializeField] private ArtifactInfo artifact;
        [SerializeField] private Font chineseFont;
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private ARAnchorManager anchorManager;

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private const float IndicatorRadius = .06f; // 12 cm diameter in world space.
        private const float IndicatorSmoothing = 12f;
        private GameObject modelRoot;
        private Transform modelVisual;
        private ARAnchor anchor;
        private Camera arCamera;
        private GameObject placementIndicator;
        private bool hasPlacementPose;
        private Text status;
        private Button replaceButton;
        private Button informationButton;
        private GameObject informationPanel;
        private float baseHeight;
        private float displayedHeight;
        private bool modelReady;
        private bool placed;
        private bool loadFailed;
        private bool anchorTrackingUsable;
        private bool anchorRemovedNotified;
        private bool lastAnchorPending;
        private TrackingState lastAnchorTrackingState;
        private ARSessionState lastSessionState;
        private Vector3 previousAnchorPosition;
        private Quaternion previousAnchorRotation;
        private float anchorCreateTime;
        private float lastAnchorJumpLogTime;
        private float groundOffsetMeters;
        private ArtifactMeshGeometry.Measurement modelGeometry;
        private float lastPinchDistance;
        private float missFeedbackUntil;
        private Coroutine placedMessage;
        private RectTransform headerRect;
        private RectTransform headerBackRect;
        private RectTransform headerTitleRect;
        private RectTransform headerGlyphRect;
        private RectTransform headerDividerRect;
        private Rect lastHeaderSafeArea;
        private Vector2Int lastHeaderScreenSize;
        private static readonly Color Ink = new Color32(24, 50, 67, 255);
        private static readonly Color Surface = new Color32(249, 248, 244, 245);

        private void OnEnable() => EnhancedTouchSupport.Enable();
        private void OnDisable() => EnhancedTouchSupport.Disable();

        private void Start()
        {
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            anchorManager.anchorsChanged += OnAnchorsChanged;
            arCamera = Camera.main;
            if (arCamera == null) Debug.LogError("[ArtifactAR][ERROR] AR camera unavailable");
            BuildPlacementIndicator();
            HidePlaneVisuals();
            BuildUi();
            status.text = "请缓慢移动手机，扫描可放置平面";
            StartCoroutine(RefreshStaticText());
            if (artifact == null || artifact.importedViewerPrefab == null)
            {
                status.text = "文物资源未配置";
                Debug.LogError("[ArtifactAR][ERROR] imported prefab missing from ArtifactInfo");
                return;
            }
            PrepareModel();
        }

        private void OnDestroy()
        {
            if (anchorManager != null) anchorManager.anchorsChanged -= OnAnchorsChanged;
        }

        private void OnAnchorsChanged(ARAnchorsChangedEventArgs changes)
        {
            foreach (ARAnchor removed in changes.removed)
            {
                if (removed != anchor || anchorRemovedNotified) continue;
                anchorRemovedNotified = true;
                anchorTrackingUsable = false;
                status.transform.parent.gameObject.SetActive(true);
                status.text = "空间定位丢失，请重新放置";
                Debug.LogError($"[ArtifactAR][ANCHOR_REMOVED] id={removed.trackableId}; waiting for user re-placement");
            }
        }

        private void PrepareModel()
        {
            modelReady = false;
            loadFailed = false;
            Debug.Log("[ArtifactAR] loading imported prefab " + artifact.importedViewerPrefab.name);
            modelRoot = new GameObject("ShengDing_AR_Root");
            GameObject visual = new GameObject("ShengDing_Model");
            modelVisual = visual.transform;
            modelVisual.SetParent(modelRoot.transform, false);
            bool instantiated;
            try
            {
                instantiated = Instantiate(artifact.importedViewerPrefab, modelVisual) != null;
            }
            catch (Exception exception)
            {
                Fail("GLB instantiate failed", exception);
                Destroy(modelRoot);
                modelRoot = null;
                return;
            }
            if (this == null) return;
            if (!instantiated)
            {
                Fail("GLB instantiate failed");
                Destroy(modelRoot);
                modelRoot = null;
                return;
            }
            if (!ArtifactMeshGeometry.TryMeasure(modelVisual, out var unscaled))
            {
                Fail("imported mesh has no readable, visible vertices");
                Destroy(modelRoot);
                modelRoot = null;
                return;
            }
            baseHeight = unscaled.bounds.size.y;
            displayedHeight = artifact.defaultHeight;
            if (displayedHeight <= 0f)
            {
                Fail("invalid requested height");
                Destroy(modelRoot);
                modelRoot = null;
                return;
            }
            // Keep the imported prefab's axis-conversion child untouched. Center it in X/Z,
            // then scale uniformly and measure the transformed vertices again for the feet.
            modelVisual.localPosition = new Vector3(-unscaled.bounds.center.x, 0f, -unscaled.bounds.center.z);
            float scale = displayedHeight / baseHeight;
            modelRoot.transform.localScale = Vector3.one * scale;
            if (!ArtifactMeshGeometry.TryMeasure(modelRoot.transform, out var scaled))
            {
                Fail("scaled mesh measurement failed");
                Destroy(modelRoot);
                modelRoot = null;
                return;
            }
            float groundCorrectionLocal = -scaled.bounds.min.y;
            modelVisual.localPosition += Vector3.up * groundCorrectionLocal;
            groundOffsetMeters = groundCorrectionLocal * scale;
            if (!ArtifactMeshGeometry.TryMeasure(modelRoot.transform, out modelGeometry)
                || Mathf.Abs(modelGeometry.bounds.min.y * scale) > .001f)
            {
                Fail("mesh foot alignment failed");
                Destroy(modelRoot);
                modelRoot = null;
                return;
            }
            Debug.Log($"[ArtifactAR][GEOMETRY] vertices={modelGeometry.vertexCount} "
                + $"supportVertices={modelGeometry.supportVertexCount} baseHeight={baseHeight:F5}m "
                + $"scale={scale:F6} finalHeight={modelGeometry.bounds.size.y * scale:F5}m "
                + $"groundOffset={groundOffsetMeters:F5}m localBottom={modelGeometry.bounds.min.y:F6}m "
                + $"lowestVertex={modelGeometry.lowestPoint}");
            if (modelGeometry.supportVertexCount < 3)
                Debug.LogWarning("[ArtifactAR][GEOMETRY] very few lowest vertices; inspect the feet mesh");
            modelRoot.SetActive(false);
            modelReady = true;
        }

        private IEnumerator RefreshStaticText()
        {
            yield return null;
            if (status != null && status.canvas != null)
                foreach (Text label in status.canvas.GetComponentsInChildren<Text>(true)) label.SetAllDirty();
        }

        private void Update()
        {
            if (headerRect != null && (lastHeaderSafeArea != Screen.safeArea
                || lastHeaderScreenSize.x != Screen.width || lastHeaderScreenSize.y != Screen.height))
                LayoutHeader();
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (informationPanel.activeSelf) informationPanel.SetActive(false);
                else BackToMenu();
            }
            if (anchorRemovedNotified && !placed)
                ResetPlacement("放置失败，请重新选择", true);
            else if (anchor != null || placed) UpdateAnchorState();
            else UpdatePlacementIndicator();
            var activeTouches = EnhancedTouch.activeTouches;
            EnhancedTouch? first = null, second = null;
            foreach (EnhancedTouch touch in activeTouches)
            {
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended
                    || touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled) continue;
                if (first == null) first = touch;
                else { second = touch; break; }
            }
            if (first == null) { lastPinchDistance = 0f; return; }
            EnhancedTouch primary = first.Value;
            if (primary.phase == UnityEngine.InputSystem.TouchPhase.Began)
                Debug.Log("[ArtifactAR] touch received position=" + primary.screenPosition);
            if (informationPanel.activeSelf) return;
            if (!placed)
            {
                if (primary.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    if (OverUi(primary.screenPosition)) { Debug.Log("[ArtifactAR] touch blocked by UI"); return; }
                    if (!modelReady) { Debug.LogError("[ArtifactAR][ERROR] touch before prefab ready"); return; }
                    if (anchor == null) TryPlaceAt(primary.screenPosition);
                }
                return;
            }
            if (second != null)
            {
                float distance = Vector2.Distance(primary.screenPosition, second.Value.screenPosition);
                if (lastPinchDistance > 0f && !OverUi(primary.screenPosition) && !OverUi(second.Value.screenPosition))
                {
                    float next = Mathf.Clamp(displayedHeight * distance / lastPinchDistance, 0.12f, 0.40f);
                    displayedHeight = next;
                    modelRoot.transform.localScale = Vector3.one * (displayedHeight / baseHeight);
                }
                lastPinchDistance = distance;
            }
            else
            {
                lastPinchDistance = 0f;
                if (!OverUi(primary.screenPosition) && primary.phase == UnityEngine.InputSystem.TouchPhase.Moved)
                {
                    float delta = primary.delta.x;
                    modelRoot.transform.Rotate(Vector3.up, -delta * 0.25f, Space.Self);
                }
            }
        }

        private void BuildPlacementIndicator()
        {
            Material material = Resources.Load<Material>("Materials/ArtifactPlacementIndicator");
            if (material == null)
            {
                Debug.LogError("[ArtifactAR][ERROR] placement indicator material unavailable");
                return;
            }
            placementIndicator = new GameObject("PlacementIndicator");
            var filter = placementIndicator.AddComponent<MeshFilter>();
            var renderer = placementIndicator.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            const int segments = 48;
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * IndicatorRadius;
                vertices[i * 2 + 1] = direction * (IndicatorRadius - .008f);
                int next = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = next * 2;
                triangles[t + 2] = i * 2 + 1;
                triangles[t + 3] = i * 2 + 1;
                triangles[t + 4] = next * 2;
                triangles[t + 5] = next * 2 + 1;
            }
            var mesh = new Mesh { name = "Artifact Placement Ring" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            filter.sharedMesh = mesh;
            placementIndicator.SetActive(false);
        }

        private void UpdatePlacementIndicator()
        {
            hasPlacementPose = false;
            if (placementIndicator == null || arCamera == null || !modelReady || loadFailed)
            {
                if (placementIndicator != null) placementIndicator.SetActive(false);
                return;
            }
            Vector2 screenCenter = new Vector2(Screen.width * .5f, Screen.height * .5f);
            Pose previewPose = default;
            if (raycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
            {
                foreach (ARRaycastHit hit in hits)
                {
                    ARPlane plane = planeManager.GetPlane(hit.trackableId);
                    if (plane == null || plane.trackingState != TrackingState.Tracking
                        || plane.alignment != PlaneAlignment.HorizontalUp || plane.subsumedBy != null) continue;
                    previewPose = hit.pose;
                    hasPlacementPose = true;
                    break;
                }
            }
            if (hasPlacementPose)
            {
                Vector3 target = previewPose.position + Vector3.up * .003f;
                if (!placementIndicator.activeSelf) placementIndicator.transform.position = target;
                else placementIndicator.transform.position = Vector3.Lerp(
                    placementIndicator.transform.position, target,
                    1f - Mathf.Exp(-IndicatorSmoothing * Time.deltaTime));
                placementIndicator.transform.rotation = Quaternion.identity;
                placementIndicator.SetActive(true);
            }
            else placementIndicator.SetActive(false);
            if (Time.time >= missFeedbackUntil)
                status.text = hasPlacementPose ? "点击平面放置文物" : "请缓慢移动手机，扫描可放置平面";
        }

        private void HidePlaneVisuals()
        {
            foreach (ARPlane plane in planeManager.trackables)
            {
                foreach (Renderer renderer in plane.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                foreach (ARPlaneMeshVisualizer visualizer in plane.GetComponentsInChildren<ARPlaneMeshVisualizer>(true))
                    visualizer.enabled = false;
            }
            foreach (ARPointCloud pointCloud in FindObjectsOfType<ARPointCloud>())
                foreach (Renderer renderer in pointCloud.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        }

        private void TryPlaceAt(Vector2 touchPosition)
        {
            if (!raycastManager.Raycast(touchPosition, hits, TrackableType.PlaneWithinPolygon))
            {
                ReportPlacementMiss(touchPosition, "raycast miss");
                return;
            }
            foreach (ARRaycastHit hit in hits)
            {
                ARPlane plane = planeManager.GetPlane(hit.trackableId);
                if (plane == null || plane.alignment != PlaneAlignment.HorizontalUp
                    || plane.trackingState != TrackingState.Tracking || plane.subsumedBy != null)
                    continue;
                Debug.Log($"[ArtifactAR][TAP] screen={touchPosition} hit={hit.pose.position} "
                    + $"plane={hit.trackableId} session={ARSession.state}");
                Place(hit.pose);
                return;
            }
            ReportPlacementMiss(touchPosition, "no tracked HorizontalUp plane");
        }

        private void ReportPlacementMiss(Vector2 touchPosition, string reason)
        {
            status.text = "未检测到可放置位置，请重新选择";
            missFeedbackUntil = Time.time + 1.5f;
            Debug.Log($"[ArtifactAR][TAP_MISS] screen={touchPosition} reason={reason}");
        }

        private static bool OverUi(Vector2 screenPosition)
        {
            if (EventSystem.current == null) return false;
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, results);
            return results.Count > 0;
        }

        private void Place(Pose pose)
        {
            if (anchorManager == null || !anchorManager.enabled || modelRoot == null || !modelReady)
            {
                FailPlacement("anchor manager or prepared model unavailable");
                return;
            }
            var origin = anchorManager.GetComponent<Unity.XR.CoreUtils.XROrigin>();
            if (origin == null || origin.TrackablesParent == null
                || origin.transform.lossyScale != Vector3.one
                || origin.TrackablesParent.lossyScale != Vector3.one)
            {
                FailPlacement("XR Origin/Trackables coordinate frame invalid");
                return;
            }
            // The anchor remains upright; user yaw belongs to the artifact root.
            Vector3 forward = Vector3.ProjectOnPlane(arCamera.transform.forward, Vector3.up);
            Quaternion yaw = forward.sqrMagnitude > .0001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
            GameObject anchorObject = new GameObject("ShengDing_Independent_ARAnchor");
            anchorObject.transform.SetParent(origin.TrackablesParent, false);
            anchorObject.transform.SetPositionAndRotation(pose.position, Quaternion.identity);
            try { anchor = anchorObject.AddComponent<ARAnchor>(); }
            catch (Exception exception)
            {
                Destroy(anchorObject);
                FailPlacement("anchor exception: " + exception);
                return;
            }
            if (anchor == null)
            {
                Destroy(anchorObject);
                FailPlacement("ARAnchor component creation failed");
                return;
            }
            modelRoot.transform.SetParent(anchor.transform, false);
            modelRoot.transform.localPosition = Vector3.zero;
            modelRoot.transform.localRotation = yaw;
            modelRoot.SetActive(false);
            anchorCreateTime = Time.time;
            lastAnchorPending = anchor.pending;
            lastAnchorTrackingState = anchor.trackingState;
            lastSessionState = ARSession.state;
            previousAnchorPosition = anchor.transform.position;
            previousAnchorRotation = anchor.transform.rotation;
            anchorTrackingUsable = false;
            anchorRemovedNotified = false;
            status.text = "请缓慢移动手机，恢复空间定位";
            placementIndicator.SetActive(false);
            hasPlacementPose = false;
            HidePlaneVisuals();
            Debug.Log($"[ArtifactAR][ANCHOR_CREATED] id={anchor.trackableId} pending={anchor.pending} "
                + $"tracking={anchor.trackingState} session={ARSession.state} initialPose={pose.position} "
                + $"anchorPose={anchor.transform.position}/{anchor.transform.rotation.eulerAngles} "
                + $"cameraPose={arCamera.transform.position}/{arCamera.transform.rotation.eulerAngles} "
                + $"originScale={origin.transform.lossyScale} trackablesScale={origin.TrackablesParent.lossyScale} "
                + $"rootLocal={modelRoot.transform.localPosition}/{modelRoot.transform.localRotation.eulerAngles}/"
                + $"{modelRoot.transform.localScale} geometry={modelGeometry.bounds} "
                + $"bottomDelta={modelGeometry.bounds.min.y * modelRoot.transform.localScale.y:F6}m");
            UpdateAnchorState();
        }

        private void UpdateAnchorState()
        {
            if (anchor == null)
            {
                if (placed && !anchorRemovedNotified)
                {
                    status.transform.parent.gameObject.SetActive(true);
                    status.text = "空间定位丢失，请重新放置";
                    anchorRemovedNotified = true;
                    Debug.LogError("[ArtifactAR][ANCHOR_REMOVED] tracked anchor no longer exists");
                }
                return;
            }
            if (!placed && anchor.trackableId == TrackableId.invalidId
                && Time.time - anchorCreateTime > 8f)
            {
                Debug.LogError("[ArtifactAR][ANCHOR_FAILED] registration timed out with invalid trackableId");
                ResetPlacement("放置失败，请重新选择", true);
                return;
            }
            if (anchor.pending != lastAnchorPending || anchor.trackingState != lastAnchorTrackingState
                || ARSession.state != lastSessionState)
            {
                Debug.Log($"[ArtifactAR][TRACKING] id={anchor.trackableId} pending={anchor.pending} "
                    + $"anchor={anchor.trackingState} session={ARSession.state} pose={anchor.transform.position}");
                lastAnchorPending = anchor.pending;
                lastAnchorTrackingState = anchor.trackingState;
                lastSessionState = ARSession.state;
            }
            bool usable = !anchor.pending && anchor.trackableId != TrackableId.invalidId
                && anchor.trackingState == TrackingState.Tracking
                && ARSession.state == ARSessionState.SessionTracking;
            if (usable && anchorTrackingUsable)
            {
                float jump = Vector3.Distance(previousAnchorPosition, anchor.transform.position);
                float angle = Quaternion.Angle(previousAnchorRotation, anchor.transform.rotation);
                if ((jump > .08f || angle > 12f) && Time.time - lastAnchorJumpLogTime > 2f)
                {
                    Debug.LogWarning($"[ArtifactAR][ANCHOR_REFINEMENT] positionDelta={jump:F3}m "
                        + $"rotationDelta={angle:F1}deg; application did not overwrite the anchor");
                    lastAnchorJumpLogTime = Time.time;
                }
            }
            previousAnchorPosition = anchor.transform.position;
            previousAnchorRotation = anchor.transform.rotation;
            if (usable != anchorTrackingUsable || (usable && !placed))
            {
                anchorTrackingUsable = usable;
                status.transform.parent.gameObject.SetActive(true);
                if (usable)
                {
                    if (modelRoot == null) { Debug.LogError("[ArtifactAR][ERROR] model lost before anchor tracking"); return; }
                    modelRoot.SetActive(true);
                    placed = true;
                    status.text = "文物已放置";
                    Debug.Log($"[ArtifactAR][PLACED] id={anchor.trackableId} "
                        + $"worldHeight={modelGeometry.bounds.size.y * modelRoot.transform.localScale.y:F5}m "
                        + $"bottomDelta={modelGeometry.bounds.min.y * modelRoot.transform.localScale.y:F6}m");
                    if (placedMessage != null) StopCoroutine(placedMessage);
                    placedMessage = StartCoroutine(HidePlacedMessage());
                }
                else
                {
                    if (placedMessage != null) { StopCoroutine(placedMessage); placedMessage = null; }
                    status.text = "请缓慢移动手机，恢复空间定位";
                }
            }
        }

        private IEnumerator HidePlacedMessage()
        {
            yield return new WaitForSeconds(1f);
            if (placed && anchorTrackingUsable && status != null)
                status.transform.parent.gameObject.SetActive(false);
            placedMessage = null;
        }

        private void Replace()
        {
            Debug.Log($"[ArtifactAR][REPLACE] oldAnchor={(anchor != null ? anchor.trackableId.ToString() : "none")} "
                + $"oldModel={(modelRoot != null ? modelRoot.name : "none")}");
            ResetPlacement("请缓慢移动手机，扫描可放置平面", false);
        }

        private void ResetPlacement(string message, bool failed)
        {
            if (placedMessage != null) { StopCoroutine(placedMessage); placedMessage = null; }
            status.transform.parent.gameObject.SetActive(true);
            if (modelRoot != null)
            {
                modelRoot.SetActive(false);
                Destroy(modelRoot);
            }
            if (anchor != null)
            {
                Destroy(anchor.gameObject);
            }
            anchor = null;
            modelRoot = null;
            modelVisual = null;
            placed = false;
            modelReady = false;
            anchorTrackingUsable = false;
            anchorRemovedNotified = false;
            hasPlacementPose = false;
            lastPinchDistance = 0f;
            missFeedbackUntil = failed ? Time.time + 1.5f : 0f;
            planeManager.enabled = true;
            PrepareModel();
            if (modelReady) status.text = message;
            UpdatePlacementIndicator();
            Debug.Log("[ArtifactAR][RESET] old model and anchor removed; " + message);
        }

        private void Fail(string message, Exception exception = null)
        {
            loadFailed = true;
            if (status != null) status.text = "文物模型加载失败";
            Debug.LogError("[ArtifactAR][ERROR] " + message + (exception == null ? "" : ": " + exception));
        }

        private void FailPlacement(string message)
        {
            status.text = "放置失败，请重新扫描平面";
            missFeedbackUntil = Time.time + 1f;
            Debug.LogError("[ArtifactAR][ERROR] " + message);
        }

        private void BackToMenu()
        {
            Debug.Log("[ArtifactAR] back button clicked");
            UrpAppController.ReturnToArtifactSelection = true;
            SceneManager.LoadScene("UrpARPrototype");
        }

        private void ShowInformation()
        {
            Debug.Log("[ArtifactAR] info button clicked");
            informationPanel.SetActive(true);
        }

        private void CloseInformation()
        {
            Debug.Log("[ArtifactAR] info close button clicked");
            informationPanel.SetActive(false);
        }

        private void BuildUi()
        {
            GameObject canvasObject = new GameObject("Artifact AR UI");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2400);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            GameObject safe = new GameObject("SafeArea");
            safe.transform.SetParent(canvasObject.transform, false);
            RectTransform safeRect = safe.AddComponent<RectTransform>();
            safeRect.anchorMin = Vector2.zero; safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
            safe.AddComponent<SafeAreaFitter>();
            // Fill the inset above Screen.safeArea with the same header artwork.
            // Position all controls within the safe portion of that header.
            GameObject header = Panel(canvasObject.transform, "Header", Surface,
                new Vector2(0f, .92f), Vector2.one);
            headerRect = header.GetComponent<RectTransform>();
            Button back = Button(header.transform, "‹", new Vector2(.015f, 0f),
                new Vector2(.15f, 1f), BackToMenu);
            headerBackRect = back.GetComponent<RectTransform>();
            back.GetComponent<Image>().color = new Color(1f, 1f, 1f, .001f);
            back.GetComponentInChildren<Text>().enabled = false;
            Skin(header.transform, "CloudDivider", "UI/ornament_cloud_divider_v57",
                new Vector2(.24f, 0f), new Vector2(.76f, .30f), new Rect(0f,.35f,1f,.30f));
            headerDividerRect = header.transform.Find("CloudDivider")?.GetComponent<RectTransform>();
            headerTitleRect = Label(header.transform, "文物实景展示",
                new Vector2(.2f, .30f), new Vector2(.8f, 1f), 34, Ink).rectTransform;
            headerGlyphRect = Label(header.transform, "‹",
                new Vector2(.015f, .30f), new Vector2(.15f, 1f), 48, Ink).rectTransform;
            LayoutHeader();
            GameObject statusBar = Panel(safe.transform, "StatusBar", new Color32(24,50,67,198),
                new Vector2(.14f,.895f), new Vector2(.86f,.923f));
            status = Label(statusBar.transform, "", new Vector2(.12f,0f), new Vector2(.98f,1f), 21, Color.white);
            status.alignment = TextAnchor.MiddleLeft;
            Panel(statusBar.transform, "StatusDot", new Color32(216,184,113,255),
                new Vector2(.055f,.34f), new Vector2(.10f,.66f));
            GameObject toolbar = Panel(safe.transform, "ArtifactToolbar", Color.clear,
                new Vector2(.035f,.01f), new Vector2(.965f,.082f));
            Skin(toolbar.transform, "InkGoldToolbar", "UI/button_ar_toolbar_v56", Vector2.zero, Vector2.one,
                new Rect(0,0,1,1));
            replaceButton = Button(toolbar.transform, "重新放置", new Vector2(.035f,.14f), new Vector2(.49f,.86f), Replace);
            informationButton = Button(toolbar.transform, "文物介绍", new Vector2(.51f,.14f), new Vector2(.965f,.86f), ShowInformation);
            StyleToolbarButton(replaceButton, 0);
            StyleToolbarButton(informationButton, 1);
            replaceButton.GetComponentInChildren<Text>().enabled = false;
            informationButton.GetComponentInChildren<Text>().enabled = false;
            Label(toolbar.transform, "重新放置", new Vector2(.045f,.20f), new Vector2(.49f,.80f), 26, Ink);
            Label(toolbar.transform, "文物介绍", new Vector2(.51f,.20f), new Vector2(.955f,.80f), 26, Ink);
            informationPanel = Panel(safe.transform, "Artifact Information", new Color32(249,248,244,250),
                new Vector2(.07f,.35f), new Vector2(.93f,.67f));
            Skin(informationPanel.transform, "PanelOrnament", "UI/ornament_cloud_divider_v57",
                new Vector2(.18f,.89f), new Vector2(.82f,.98f), new Rect(0f,.35f,1f,.30f));
            Label(informationPanel.transform, artifact != null ? artifact.displayName : "青铜升鼎",
                new Vector2(.06f,.77f), new Vector2(.94f,.94f), 40, Ink);
            Label(informationPanel.transform, artifact != null ? artifact.period + " · " + artifact.category : "",
                new Vector2(.06f,.67f), new Vector2(.94f,.78f), 28, Ink);
            Text body = Label(informationPanel.transform, artifact != null ? artifact.description : "",
                new Vector2(.06f,.23f), new Vector2(.94f,.65f), 27, Ink);
            body.alignment = TextAnchor.UpperLeft;
            Button close = Button(informationPanel.transform, "关闭", new Vector2(.3f,.04f), new Vector2(.7f,.16f),
                CloseInformation);
            close.GetComponent<Image>().color = new Color(1f, 1f, 1f, .001f);
            Skin(close.transform, "IvoryButton", "UI/button_home_ivory_v58", Vector2.zero, Vector2.one,
                new Rect(0,0,1,1));
            informationPanel.SetActive(false);
        }

        private void LayoutHeader()
        {
            lastHeaderSafeArea = Screen.safeArea;
            lastHeaderScreenSize = new Vector2Int(Screen.width, Screen.height);
            float topInset = Mathf.Clamp01((Screen.height - Screen.safeArea.yMax) / Mathf.Max(1f, Screen.height));
            float headerHeight = topInset + .062f;
            float safePart = .062f / headerHeight;
            SetAnchors(headerRect, new Vector2(0f, 1f - headerHeight), Vector2.one);
            SetAnchors(headerBackRect, new Vector2(.015f, 0f), new Vector2(.15f, safePart));
            SetAnchors(headerTitleRect, new Vector2(.2f, safePart * .30f), new Vector2(.8f, safePart));
            SetAnchors(headerGlyphRect, new Vector2(.015f, safePart * .30f), new Vector2(.15f, safePart));
            if (headerDividerRect != null)
                SetAnchors(headerDividerRect, new Vector2(.24f, 0f), new Vector2(.76f, safePart * .30f));
        }

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static GameObject Panel(Transform parent, string name, Color color, Vector2 min, Vector2 max)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = obj.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return obj;
        }

        private Text Label(Transform parent, string value, Vector2 min, Vector2 max, int size, Color color)
        {
            GameObject obj = new GameObject(value);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Text label = obj.AddComponent<Text>();
            label.font = chineseFont;
            label.text = value;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            return label;
        }

        private Button Button(Transform parent, string value, Vector2 min, Vector2 max, Action action)
        {
            GameObject obj = Panel(parent, value + " Button", Surface, min, max);
            Button button = obj.AddComponent<Button>();
            obj.GetComponent<Image>().raycastTarget = true;
            button.onClick.AddListener(() => action());
            Text label = Label(obj.transform, value, Vector2.zero, Vector2.one, 30, Ink);
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private void StyleToolbarButton(Button button, int iconIndex)
        {
            button.GetComponent<Image>().color = new Color(1f, 1f, 1f, .001f);
            // The existing toolbar artwork supplies the gold separators. Chinese
            // action labels are overlaid on the toolbar itself to stay legible.
        }

        private static void Skin(Transform parent, string name, string path, Vector2 min, Vector2 max, Rect uv)
        {
            Texture2D texture = Resources.Load<Texture2D>(path);
            if (texture == null) { Debug.LogError("[ArtifactAR][ERROR] UI resource missing " + path); return; }
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.SetAsFirstSibling();
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            RawImage image = obj.AddComponent<RawImage>();
            image.texture = texture; image.uvRect = uv; image.raycastTarget = false;
        }
    }
}
