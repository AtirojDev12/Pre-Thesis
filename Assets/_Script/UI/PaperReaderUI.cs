using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>One reader per local player; generated lazily, with no scene-wide input listener.</summary>
[DisallowMultipleComponent]
public sealed class PaperReaderUI : MonoBehaviour
{
    private ReadablePaper document;
    private GameObject owner;
    private PlayerHealth health;
    private Canvas canvas;
    private RectTransform screen;
    private RectTransform paper;
    private RawImage image;
    private Image backdrop;
    private TMP_Text hint;
    private bool registered;
    private bool closing;
    private float progress;
    private int pageIndex;
    private int openedFrame;

    public bool IsOpen => registered;

    public bool Open(ReadablePaper source, GameObject player)
    {
        if (registered || source == null || !source.CanInteract() || GameplayInput.Blocked) return false;
        if (canvas == null) Build();
        document = source;
        owner = player;
        health = player.GetComponent<PlayerHealth>();
        canvas.sortingOrder = source.SortingOrder;
        pageIndex = 0;
        progress = 0;
        closing = false;
        openedFrame = Time.frameCount;
        registered = true;
        canvas.enabled = true;
        OverlayPanels.Opened();
        PersistentHUD.PushHidden();
        // Reading uses the keyboard and keeps the mouse captured by the game.
        GameplayInput.Blocked = true;
        ShowPage();
        Canvas.ForceUpdateCanvases();
        Layout();
        return true;
    }

    public void Close() { if (registered) closing = true; }

    private void Update()
    {
        if (!registered) return;
        if (document == null || !document.isActiveAndEnabled || owner == null || !owner.activeInHierarchy ||
            (health != null && (health.IsDead || health.IsDowned)))
        {
            Finish();
            return;
        }
        // The E that opened the document must never close it in the same frame.
        Keyboard keyboard = Keyboard.current;
        if (!closing && Time.frameCount > openedFrame && keyboard != null)
        {
            if (keyboard.escapeKey.wasPressedThisFrame || Pressed(keyboard, document.CloseKey)) Close();
            else if (Pressed(keyboard, document.NextPageKey)) ChangePage(1);
            else if (Pressed(keyboard, document.PreviousPageKey)) ChangePage(-1);
        }
        float duration = closing ? document.CloseDuration : document.OpenDuration;
        progress = duration <= 0 ? (closing ? 0 : 1) :
            Mathf.MoveTowards(progress, closing ? 0 : 1, Time.unscaledDeltaTime / duration);
        Layout();
        if (closing && progress <= 0) Finish();
    }

    private static bool Pressed(Keyboard keyboard, Key key) => key != Key.None && keyboard[key].wasPressedThisFrame;

    private void ChangePage(int direction)
    {
        // Skip empty slots so documents can be extended gradually in the Inspector.
        for (int i = pageIndex + direction; i >= 0 && i < document.PageCount; i += direction)
        {
            if (document.GetPage(i) == null) continue;
            pageIndex = i;
            ShowPage();
            return;
        }
    }

    private void ShowPage()
    {
        image.texture = document.GetPage(pageIndex);
        string close = document.CloseKey == Key.None || document.CloseKey == Key.Escape
            ? "Esc" : document.CloseKey + " / Esc";
        hint.text = document.PageCount > 1
            ? $"{pageIndex + 1} / {document.PageCount}    {document.PreviousPageKey} / {document.NextPageKey}: Page    {close}: Close"
            : close + ": Close";
    }

    private void Layout()
    {
        if (document == null || image.texture == null) return;
        Vector2 viewport = screen.rect.size;
        Vector2 offset = document.CenterOffset;
        // Clamp offset and dimensions so the full sheet stays readable on narrow screens too.
        Vector2 available = new Vector2(
            Mathf.Max(1, viewport.x - 2 * Mathf.Max(0, document.ScreenMargin.x)),
            Mathf.Max(1, viewport.y - 2 * Mathf.Max(0, document.ScreenMargin.y)));
        offset.x = Mathf.Clamp(offset.x, -available.x * .45f, available.x * .45f);
        offset.y = Mathf.Clamp(offset.y, -available.y * .45f, available.y * .45f);
        Vector2 limit = new Vector2(
            Mathf.Max(1, Mathf.Min(document.MaximumSize.x, available.x - 2 * Mathf.Abs(offset.x))),
            Mathf.Max(1, Mathf.Min(document.MaximumSize.y, available.y - 2 * Mathf.Abs(offset.y))));
        float aspect = (float)image.texture.width / Mathf.Max(1, image.texture.height);
        float height = Mathf.Min(limit.y, limit.x / aspect);
        paper.sizeDelta = new Vector2(height * aspect, height);
        Vector2 belowScreen = new Vector2(offset.x, -viewport.y * .5f - height * .5f - document.OffscreenPadding);
        float t = document.SlideCurve != null && document.SlideCurve.length > 0
            ? document.SlideCurve.Evaluate(progress) : Mathf.SmoothStep(0, 1, progress);
        // Always end at the reading position even if the authored curve has unusual endpoints.
        if (progress <= 0) t = 0;
        else if (progress >= 1) t = 1;
        paper.anchoredPosition = Vector2.LerpUnclamped(belowScreen, offset, t);
        Color color = document.BackdropColor;
        color.a *= progress;
        backdrop.color = color;
        hint.rectTransform.sizeDelta = new Vector2(Mathf.Max(1, viewport.x - 32), 50);
        hint.alpha = progress;
    }

    private void Finish()
    {
        if (!registered) return;
        registered = false;
        ReadablePaper previous = document;
        document = null;
        owner = null;
        health = null;
        if (image != null) image.texture = null;
        if (canvas != null) canvas.enabled = false;
        OverlayPanels.Closed();
        PersistentHUD.PopHidden();
        if (!OverlayPanels.AnyOpen && !PauseMenuController.IsOpen && !MatchResultsUI.IsShowing)
        {
            GameplayInput.Blocked = false;
            if (PlayerHealth.LocalInstance != null)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
        if (previous != null) previous.NotifyClosed();
    }

    private void OnDisable() => Finish();
    private void OnDestroy() => Finish();

    private void Build()
    {
        screen = (RectTransform)transform;
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.enabled = false;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;

        backdrop = Child<Image>("Backdrop");
        RectTransform background = backdrop.rectTransform;
        background.anchorMin = Vector2.zero;
        background.anchorMax = Vector2.one;
        background.offsetMin = background.offsetMax = Vector2.zero;
        image = Child<RawImage>("Paper Image");
        paper = image.rectTransform;
        paper.anchorMin = paper.anchorMax = paper.pivot = new Vector2(.5f, .5f);
        hint = Child<TextMeshProUGUI>("Controls");
        hint.fontSize = 24;
        hint.enableAutoSizing = true;
        hint.fontSizeMin = 10;
        hint.fontSizeMax = 24;
        hint.alignment = TextAlignmentOptions.Center;
        hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(.5f, 0);
        hint.rectTransform.pivot = new Vector2(.5f, 0);
        hint.rectTransform.anchoredPosition = new Vector2(0, 16);
        hint.rectTransform.sizeDelta = new Vector2(1800, 50);
    }

    private T Child<T>(string objectName) where T : Graphic
    {
        var child = new GameObject(objectName, typeof(RectTransform));
        child.transform.SetParent(transform, false);
        T graphic = child.AddComponent<T>();
        graphic.raycastTarget = false;
        return graphic;
    }
}
