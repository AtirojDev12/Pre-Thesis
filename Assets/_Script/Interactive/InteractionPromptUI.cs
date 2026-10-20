using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space prompt ("Press E to open") that follows the currently targeted
/// interactable and faces the camera. Purely local and cosmetic -- only the
/// local player's PlayerInteractor ever drives this.
///
/// Setup: a World Space Canvas with a TextMeshProUGUI child, this script on the
/// Canvas, promptText assigned. It can live either on the player prefab or in
/// the scene; PlayerInteractor finds it either way.
/// </summary>
public class InteractionPromptUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI promptText;
    [Tooltip("Optional scene/prefab label for object names. Its RectTransform, font, size and color are edited directly in GAME HUD.")]
    [SerializeField] private TextMeshProUGUI focusNameText;
    [SerializeField] private Vector3 worldOffset = new Vector3(0, 1.5f, 0);

    [Tooltip("Leave empty to resolve the active camera at runtime.")]
    [SerializeField] private Camera targetCamera;

    private Transform _followTarget;
    private Canvas _canvas;
    private string _currentText;
    private bool _followsTargetInWorld;
    private Vector3 _activeOffset;
    private GameObject _fixedNameCanvas;
    private TextMeshProUGUI _fixedNameText;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();

        // Only a World Space canvas can be moved to sit on top of the object.
        // On a Screen Space canvas Unity drives the RectTransform itself, so
        // writing position/rotation below would be silently discarded -- the
        // prompt just stays wherever the canvas puts it. That is a perfectly
        // good look for a first-person prompt (most games put it at screen
        // centre), so this supports both instead of demanding one.
        _followsTargetInWorld = _canvas != null && _canvas.renderMode == RenderMode.WorldSpace;

        Hide();
    }

    public void Show(string text, Transform followTarget, Vector3? offset = null)
    {
        if (focusNameText != null) focusNameText.enabled = false;
        if (_fixedNameCanvas != null) _fixedNameCanvas.SetActive(false);
        if (string.IsNullOrEmpty(text))
        {
            Hide();
            return;
        }

        _followTarget = followTarget;
        _activeOffset = offset ?? worldOffset;

        // Only reassign when it actually changed -- setting TMP text forces a
        // mesh rebuild, and PlayerInteractor may call this on consecutive frames.
        if (promptText != null && text != _currentText)
        {
            _currentText = text;
            promptText.text = Keybinds.Fix(text); // 20 Oct: "[E]" -> the player's key
        }

        if (_canvas != null) _canvas.enabled = true;
    }

    /// <summary>Popcorn focus names stay below the crosshair on the local HUD.</summary>
    public void ShowFixedName(string text, PopcornFocusSettings settings = null)
    {
        _followTarget = null;
        _currentText = null;
        if (_canvas != null) _canvas.enabled = false;
        if (focusNameText != null)
        {
            if (_fixedNameCanvas != null) _fixedNameCanvas.SetActive(false);
            bool show = !string.IsNullOrWhiteSpace(text);
            if (show && focusNameText.text != text) focusNameText.text = text;
            focusNameText.enabled = show;
            return;
        }
        if (string.IsNullOrWhiteSpace(text)) { Hide(); return; }
        if (_fixedNameCanvas == null)
        {
            _fixedNameCanvas = new GameObject("Popcorn Focus Name", typeof(RectTransform), typeof(Canvas));
            Canvas canvas = _fixedNameCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 50;
            if (PersistentHUD.Instance != null)
            {
                _fixedNameCanvas.transform.SetParent(PersistentHUD.Instance.transform, false);
                RectTransform rect = (RectTransform)_fixedNameCanvas.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            else
            {
                CanvasScaler scaler = _fixedNameCanvas.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = .5f;
            }
            var label = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(_fixedNameCanvas.transform, false);
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(.5f, .5f);
            labelRect.anchoredPosition = new Vector2(0, -65);
            labelRect.sizeDelta = new Vector2(640, 44);
            _fixedNameText = label.GetComponent<TextMeshProUGUI>();
            if (promptText != null && promptText.font != null) _fixedNameText.font = promptText.font;
            _fixedNameText.fontSize = 26;
            _fixedNameText.alignment = TextAlignmentOptions.Center;
            _fixedNameText.color = Color.white;
            _fixedNameText.outlineWidth = .2f;
            _fixedNameText.raycastTarget = false;
            _fixedNameText.richText = false;
        }
        RectTransform nameRect = _fixedNameText.rectTransform;
        nameRect.anchoredPosition = settings != null ? settings.screenOffset : new Vector2(0, -65);
        Vector2 box = settings != null ? settings.textBoxSize : new Vector2(640, 44);
        nameRect.sizeDelta = new Vector2(Mathf.Max(1, box.x), Mathf.Max(1, box.y));
        _fixedNameText.fontSize = settings != null ? Mathf.Max(1, settings.fontSize) : 26;
        _fixedNameText.color = settings != null ? settings.textColor : Color.white;
        TMP_FontAsset font = settings != null && settings.font != null ? settings.font :
            promptText != null && promptText.font != null ? promptText.font : TMP_Settings.defaultFontAsset;
        if (font != null && _fixedNameText.font != font) _fixedNameText.font = font;
        if (_fixedNameText.text != text) _fixedNameText.text = text;
        if (!_fixedNameCanvas.activeSelf) _fixedNameCanvas.SetActive(true);
    }

    public void Hide()
    {
        if (focusNameText != null) focusNameText.enabled = false;
        if (_fixedNameCanvas != null) _fixedNameCanvas.SetActive(false);
        _followTarget = null;
        _currentText = null;
        if (_canvas != null) _canvas.enabled = false;
    }

    private void OnDisable() => Hide();
    private void OnDestroy()
    {
        if (_fixedNameCanvas != null) Destroy(_fixedNameCanvas);
    }

    private void LateUpdate()
    {
        if (!_followsTargetInWorld || _followTarget == null) return;

        // Resolved lazily rather than in Awake: in a networked match the local
        // player's camera doesn't exist yet when this object wakes up, and
        // grabbing Camera.main too early can latch onto the wrong one.
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera == null) return;
        }

        transform.position = _followTarget.position + _activeOffset;
        transform.forward = targetCamera.transform.forward;
    }
}
