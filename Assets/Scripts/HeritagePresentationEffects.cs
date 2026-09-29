using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Urp.ArDemo
{
    /// <summary>Small, dependency-free presentation effects shared by the heritage UI.</summary>
    public sealed class HeritageAudioController : MonoBehaviour
    {
        public const string MusicName = "《平生意》";
        private const string ResourcePath = "Audio/PingShengYi";
        private const string PreferenceKey = "urp.music.enabled";
        private static HeritageAudioController instance;
        private AudioSource source;

        public static HeritageAudioController Instance => EnsureExists();
        public bool IsPlaying => source != null && source.isPlaying;

        public static HeritageAudioController EnsureExists()
        {
            if (instance != null) return instance;
            instance = FindObjectOfType<HeritageAudioController>();
            if (instance != null) return instance;
            GameObject host = new GameObject("Heritage Background Music");
            instance = host.AddComponent<HeritageAudioController>();
            DontDestroyOnLoad(host);
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.volume = 0.32f;
            source.spatialBlend = 0f;
            source.clip = Resources.Load<AudioClip>(ResourcePath);
            if (source.clip == null)
            {
                Debug.LogWarning("[HeritageAudio] Missing Resources/" + ResourcePath);
                return;
            }
            if (PlayerPrefs.GetInt(PreferenceKey, 1) == 1) source.Play();
        }

        public bool Toggle()
        {
            if (source == null || source.clip == null) return false;
            if (source.isPlaying) source.Pause(); else source.Play();
            PlayerPrefs.SetInt(PreferenceKey, source.isPlaying ? 1 : 0);
            PlayerPrefs.Save();
            return source.isPlaying;
        }
    }

    public sealed class HeritageTypewriterEffect : MonoBehaviour
    {
        [SerializeField] private float characterDelay = 0.085f;
        private Text label;
        private string fullText;

        public void Play(Text target)
        {
            label = target;
            fullText = target != null ? target.text : string.Empty;
            Restart();
        }

        private void OnEnable()
        {
            if (label != null && !string.IsNullOrEmpty(fullText)) Restart();
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (label != null && !string.IsNullOrEmpty(fullText)) label.text = fullText;
        }

        private void Restart()
        {
            StopAllCoroutines();
            if (label != null && isActiveAndEnabled) StartCoroutine(Animate());
        }

        private IEnumerator Animate()
        {
            label.text = string.Empty;
            RectTransform rect = label.rectTransform;
            Vector3 normalScale = rect.localScale;
            rect.localScale = normalScale * 0.94f;
            for (int i = 0; i < fullText.Length; i++)
            {
                label.text += fullText[i];
                rect.localScale = Vector3.Lerp(rect.localScale, normalScale, 0.22f);
                yield return new WaitForSecondsRealtime(fullText[i] == '\n' ? characterDelay * 1.8f : characterDelay);
            }
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * 4f;
                rect.localScale = Vector3.Lerp(rect.localScale, normalScale, t);
                yield return null;
            }
            rect.localScale = normalScale;
        }
    }

    public sealed class HeritageFloatingCloud : MonoBehaviour
    {
        public Vector2 travel = new Vector2(42f, 13f);
        public float cycleSeconds = 11f;
        public float phase;
        private RectTransform rect;
        private Vector2 origin;
        private RawImage image;

        private void Awake()
        {
            rect = transform as RectTransform;
            if (rect != null) origin = rect.anchoredPosition;
            image = GetComponent<RawImage>();
        }

        private void Update()
        {
            if (rect == null) return;
            float a = (Time.unscaledTime / Mathf.Max(1f, cycleSeconds) + phase) * Mathf.PI * 2f;
            rect.anchoredPosition = origin + new Vector2(Mathf.Sin(a) * travel.x, Mathf.Cos(a * .73f) * travel.y);
            if (image != null)
            {
                Color c = image.color;
                c.a = 0.10f + (Mathf.Sin(a * .55f) + 1f) * 0.035f;
                image.color = c;
            }
        }
    }

    public sealed class HeritageButtonEffect : MonoBehaviour, IPointerDownHandler,
        IPointerUpHandler, IPointerExitHandler
    {
        private static Sprite shineSprite;
        private RectTransform rect;
        private Coroutine scaleRoutine;
        private Coroutine shineRoutine;

        private void Awake()
        {
            rect = transform as RectTransform;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            scaleRoutine = null;
            shineRoutine = null;
            if (rect != null) rect.localScale = Vector3.one;
            Transform oldShine = transform.Find("Gold Shine Sweep");
            if (oldShine != null) Destroy(oldShine.gameObject);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!isActiveAndEnabled) return;
            StartScale(Vector3.one * .965f, .075f);
            if (shineRoutine != null) StopCoroutine(shineRoutine);
            shineRoutine = StartCoroutine(Shine());
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (isActiveAndEnabled) StartCoroutine(Rebound());
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (isActiveAndEnabled) StartScale(Vector3.one, .10f);
        }

        private void StartScale(Vector3 destination, float duration)
        {
            if (scaleRoutine != null) StopCoroutine(scaleRoutine);
            scaleRoutine = StartCoroutine(ScaleTo(destination, duration));
        }

        private IEnumerator ScaleTo(Vector3 destination, float duration)
        {
            if (rect == null) yield break;
            Vector3 start = rect.localScale;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(.01f, duration);
                float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                rect.localScale = Vector3.LerpUnclamped(start, destination, eased);
                yield return null;
            }
            rect.localScale = destination;
            scaleRoutine = null;
        }

        private IEnumerator Rebound()
        {
            StartScale(Vector3.one * 1.012f, .10f);
            yield return new WaitForSecondsRealtime(.10f);
            StartScale(Vector3.one, .12f);
        }

        private IEnumerator Shine()
        {
            Transform previous = transform.Find("Gold Shine Sweep");
            if (previous != null) Destroy(previous.gameObject);
            GameObject shine = new GameObject("Gold Shine Sweep");
            shine.transform.SetParent(transform, false);
            RectTransform shineRect = shine.AddComponent<RectTransform>();
            shineRect.anchorMin = new Vector2(-.22f, -.15f);
            shineRect.anchorMax = new Vector2(-.04f, 1.15f);
            shineRect.offsetMin = shineRect.offsetMax = Vector2.zero;
            shineRect.localRotation = Quaternion.Euler(0f, 0f, -12f);
            Image image = shine.AddComponent<Image>();
            image.sprite = GetShineSprite();
            image.type = Image.Type.Sliced;
            image.color = new Color32(244, 211, 126, 105);
            image.raycastTarget = false;
            shine.transform.SetAsLastSibling();
            float t = 0f;
            while (t < 1f && shine != null)
            {
                t += Time.unscaledDeltaTime / .48f;
                float x = Mathf.SmoothStep(-.22f, 1.04f, Mathf.Clamp01(t));
                shineRect.anchorMin = new Vector2(x, -.15f);
                shineRect.anchorMax = new Vector2(x + .18f, 1.15f);
                Color c = image.color;
                c.a = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * .42f;
                image.color = c;
                yield return null;
            }
            if (shine != null) Destroy(shine);
            shineRoutine = null;
        }

        private static Sprite GetShineSprite()
        {
            if (shineSprite != null) return shineSprite;
            const int width = 64;
            Texture2D texture = new Texture2D(width, 2, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[width * 2];
            for (int x = 0; x < width; x++)
            {
                float distance = Mathf.Abs((x / (float)(width - 1)) * 2f - 1f);
                byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Pow(1f - distance, 2.2f));
                pixels[x] = pixels[width + x] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply(false, true);
            shineSprite = Sprite.Create(texture, new Rect(0, 0, width, 2), new Vector2(.5f, .5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(20f, 0f, 20f, 0f));
            shineSprite.name = "Runtime Gold Shine Sweep";
            return shineSprite;
        }
    }

    public sealed class HeritageWorldInfoCard : MonoBehaviour
    {
        private Transform target;
        private Camera viewCamera;
        private Canvas worldCanvas;
        private float height;
        private Vector3 smoothPosition;

        public static HeritageWorldInfoCard Create(Transform followTarget, ArtifactInfo artifact,
            Font font, Camera camera, float objectHeight)
        {
            GameObject root = new GameObject("AR Floating Heritage Information");
            HeritageWorldInfoCard card = root.AddComponent<HeritageWorldInfoCard>();
            card.target = followTarget;
            card.viewCamera = camera;
            card.height = objectHeight;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30;
            card.worldCanvas = canvas;
            RectTransform canvasRect = root.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(620f, 245f);
            canvasRect.localScale = Vector3.one * .00042f;

            Image panel = root.AddComponent<Image>();
            panel.color = new Color32(18, 47, 39, 218);
            panel.raycastTarget = false;

            Text title = CreateText(root.transform, artifact != null ? artifact.displayName : "数字文物",
                font, 46, new Color32(244, 222, 171, 255), new Vector2(.055f, .61f), new Vector2(.945f, .94f));
            title.fontStyle = FontStyle.Bold;
            string subtitle = artifact == null ? "AR 信息可视化"
                : artifact.period + " · " + artifact.category + "\n" + artifact.description;
            Text body = CreateText(root.transform, subtitle, font, 27, Color.white,
                new Vector2(.055f, .08f), new Vector2(.945f, .62f));
            body.alignment = TextAnchor.UpperLeft;
            canvas.enabled = false;
            return card;
        }

        private static Text CreateText(Transform parent, string value, Font font, int size,
            Color color, Vector2 min, Vector2 max)
        {
            GameObject obj = new GameObject("FloatingText");
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            Text text = obj.AddComponent<Text>();
            text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size; text.color = color; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private void LateUpdate()
        {
            if (target == null || viewCamera == null) return;
            bool visible = target.gameObject.activeInHierarchy;
            if (worldCanvas != null) worldCanvas.enabled = visible;
            if (!visible) return;
            Vector3 desired = target.position + Vector3.up * (height + .10f);
            smoothPosition = smoothPosition == Vector3.zero ? desired
                : Vector3.Lerp(smoothPosition, desired, 1f - Mathf.Exp(-8f * Time.deltaTime));
            transform.position = smoothPosition + Vector3.up * Mathf.Sin(Time.time * 1.2f) * .006f;
            transform.rotation = Quaternion.LookRotation(transform.position - viewCamera.transform.position, Vector3.up);
        }
    }
}
