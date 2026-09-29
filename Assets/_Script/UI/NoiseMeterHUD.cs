using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// The noise meter: two bars for the local player.
///   MIC  – how loud your real microphone is.
///   GAME – noise you make in the game (moving, the Walkie-Talkie you carry...).
/// Values come from <see cref="PlayerNoise.Local"/>. A white line on each bar
/// marks <see cref="PlayerNoise.LoudThreshold"/>; above it the bar turns red.
///
/// Built in code, lives for the whole game (no prefab, no scene edits). Shown
/// only in a match (when there is a local player) or while it is being moved.
///
/// SETTINGS (Settings > Sound > Noise meter)
///   - Direction: vertical bars (standing) or horizontal bars (lying).
///   - Move meter: the screen dims, drag the meter anywhere, press Done / Enter / Esc.
///   - Reset position: back to the top-right corner.
///   Position is saved as a fraction of the screen, so it survives resolution changes.
/// </summary>
public sealed class NoiseMeterHUD : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private static readonly Color PanelColor = new Color(0.06f, 0.05f, 0.05f, 0.7f);
    private static readonly Color TrackColor = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color QuietColor = new Color(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Color MidColor = new Color(0.95f, 0.78f, 0.3f, 1f);
    private static readonly Color LoudColor = new Color(0.95f, 0.3f, 0.25f, 1f);
    private static readonly Color TextColor = new Color(0.96f, 0.94f, 0.93f, 1f);
    private static readonly Color AccentColor = new Color(0.85f, 0.64f, 0.25f, 1f);

    private const int NormalSortOrder = 49;   // just under the pause menu (50)
    private const int EditSortOrder = 32500;  // above every menu while moving it
    private const float Margin = 24f;

    private static NoiseMeterHUD instance;

    /// <summary>True while the player is dragging the meter into place.</summary>
    public static bool IsEditing => instance != null && instance.editing;

    /// <summary>Frame on which editing ended (so the Esc that ended it does not also close a menu).</summary>
    public static int EditEndedFrame { get; private set; } = -1;

    private Canvas canvas;
    private RectTransform canvasRect;
    private RectTransform meter;
    private Image meterPanel;
    private readonly Bar micBar = new Bar();
    private readonly Bar gameBar = new Bar();
    private GameObject editLayer;
    private bool editing;
    private bool vertical;
    private bool built;
    private Vector2 lastCanvasSize;

    private sealed class Bar
    {
        public RectTransform track;
        public Image fill;
        public RectTransform line;
        public TMP_Text label;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        EditEndedFrame = -1;
    }

    /// <summary>Creates the meter once (called when a local player appears, or from Settings).</summary>
    public static NoiseMeterHUD Ensure()
    {
        if (instance != null) return instance;

        var go = new GameObject("Noise Meter HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(go);
        instance = go.AddComponent<NoiseMeterHUD>();
        instance.Build();
        return instance;
    }

    /// <summary>Settings > "Move meter": dim the screen and let the player drag the meter.</summary>
    public static void BeginEdit(bool verticalPreview)
    {
        NoiseMeterHUD hud = Ensure();
        hud.vertical = verticalPreview;
        hud.LayoutBars();
        hud.editing = true;
        hud.canvas.sortingOrder = EditSortOrder;
        hud.editLayer.SetActive(true);
        hud.editLayer.transform.SetAsFirstSibling(); // dim behind the meter
        hud.meterPanel.raycastTarget = true;          // draggable only now
        EnsureEventSystem();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>Settings > "Reset position": back to the top-right corner (saved at once).</summary>
    public static void ResetPosition()
    {
        GameSettings.ResetNoiseMeterPosition();
        if (instance != null) instance.ApplySavedPosition();
    }

    private void OnDestroy()
    {
        GameSettings.Changed -= OnSettingsChanged;
        if (instance == this) instance = null;
    }

    private void OnSettingsChanged()
    {
        if (editing) return; // the preview keeps the direction chosen in Settings
        bool v = GameSettings.NoiseMeterVertical;
        if (v != vertical)
        {
            vertical = v;
            LayoutBars();
        }
        ApplySavedPosition();
    }

    // ---- Update -------------------------------------------------------------------

    private void LateUpdate()
    {
        if (!built) return;

        PlayerNoise noise = PlayerNoise.Local;
        bool show = editing || noise != null;
        if (meter.gameObject.activeSelf != show) meter.gameObject.SetActive(show);
        if (!show) return;

        // The canvas size is only final after the CanvasScaler ran: re-place when it changes.
        Vector2 canvasSize = canvasRect.rect.size;
        if (canvasSize != lastCanvasSize && !editing)
        {
            lastCanvasSize = canvasSize;
            ApplySavedPosition();
        }

        float mic, game;
        if (noise != null) { mic = noise.Mic; game = noise.Game; }
        else
        {
            // Moving the meter from the main menu: animate so the bars are visible.
            float t = Time.unscaledTime;
            mic = 0.45f + 0.35f * Mathf.Sin(t * 2.1f);
            game = 0.35f + 0.3f * Mathf.Sin(t * 1.3f + 1f);
        }
        SetBar(micBar, mic);
        SetBar(gameBar, game);

        if (editing)
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                EndEdit();
        }
    }

    private void SetBar(Bar bar, float value)
    {
        value = Mathf.Clamp01(value);
        bar.fill.fillAmount = value;
        bar.fill.color = value >= PlayerNoise.LoudThreshold ? LoudColor
            : value >= PlayerNoise.LoudThreshold * 0.66f ? MidColor : QuietColor;
    }

    // ---- Dragging (edit mode only) ----------------------------------------------------

    private Vector2 dragOffset;
    private bool dragging;

    // Drag events from any child bubble up to this component, so check that
    // the drag started ON the meter (not on the dimmed background).
    public void OnBeginDrag(PointerEventData e)
    {
        GameObject hit = e.pointerPressRaycast.gameObject;
        dragging = editing && hit != null && hit.transform.IsChildOf(meter);
        if (!dragging) return;
        dragOffset = meter.anchoredPosition - ToCanvas(e.position);
    }

    public void OnDrag(PointerEventData e)
    {
        if (!dragging || !editing) return;
        meter.anchoredPosition = ClampInside(ToCanvas(e.position) + dragOffset);
    }

    public void OnEndDrag(PointerEventData e) => dragging = false;

    /// <summary>Screen pixels -> canvas units measured from the bottom-left corner.</summary>
    private Vector2 ToCanvas(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
        return local + canvasRect.rect.size * 0.5f; // local is from the centre
    }

    private void EndEdit()
    {
        if (!editing) return;
        editing = false;
        EditEndedFrame = Time.frameCount;
        editLayer.SetActive(false);
        canvas.sortingOrder = NormalSortOrder;
        meterPanel.raycastTarget = false; // never catch clicks during play

        // Save as a fraction of the screen (centre of the meter).
        Vector2 size = canvasRect.rect.size;
        Vector2 centre = meter.anchoredPosition; // anchored at bottom-left, pivot centre
        GameSettings.SetNoiseMeterPosition(centre.x / size.x, centre.y / size.y);
        GameSettings.Save();

        // Back to the saved direction (the preview may have shown an unapplied one).
        vertical = GameSettings.NoiseMeterVertical;
        LayoutBars();
        ApplySavedPosition();
    }

    // ---- Layout ---------------------------------------------------------------------------

    private void Build()
    {
        canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = NormalSortOrder;
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = (RectTransform)transform;

        // Edit layer: dim + instructions + Done button. Swallows every click so
        // nothing behind it can be pressed while moving the meter.
        RectTransform edit = NewRect("Edit Layer", transform);
        Stretch(edit);
        Image dim = edit.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.6f);
        dim.raycastTarget = true;

        TMP_Text hint = MakeText(edit, "Hint", "Drag the noise meter where you want it.\nEnter or Esc = done", 30, TextAlignmentOptions.Center);
        hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        hint.rectTransform.sizeDelta = new Vector2(1000f, 120f);
        hint.rectTransform.anchoredPosition = new Vector2(0f, 60f);

        RectTransform done = NewRect("Done Button", edit);
        done.anchorMin = done.anchorMax = new Vector2(0.5f, 0.5f);
        done.sizeDelta = new Vector2(220f, 64f);
        done.anchoredPosition = new Vector2(0f, -40f);
        Image doneImage = done.gameObject.AddComponent<Image>();
        doneImage.color = AccentColor;
        Button doneButton = done.gameObject.AddComponent<Button>();
        doneButton.targetGraphic = doneImage;
        doneButton.onClick.AddListener(EndEdit);
        TMP_Text doneText = MakeText(done, "Text", "Done", 28, TextAlignmentOptions.Center);
        doneText.color = Color.black;
        editLayer = edit.gameObject;
        editLayer.SetActive(false);

        // The meter itself.
        meter = NewRect("Meter", transform);
        meter.anchorMin = meter.anchorMax = Vector2.zero;   // position in canvas units from bottom-left
        meter.pivot = new Vector2(0.5f, 0.5f);
        meterPanel = meter.gameObject.AddComponent<Image>();
        meterPanel.color = PanelColor;
        meterPanel.raycastTarget = false; // switched on only while moving the meter

        micBar.track = MakeTrack("Mic");
        gameBar.track = MakeTrack("Game");
        micBar.fill = MakeFill(micBar.track);
        gameBar.fill = MakeFill(gameBar.track);
        micBar.line = MakeLine(micBar.track);
        gameBar.line = MakeLine(gameBar.track);
        micBar.label = MakeText(meter, "Mic Label", "MIC", 16, TextAlignmentOptions.Center);
        gameBar.label = MakeText(meter, "Game Label", "GAME", 16, TextAlignmentOptions.Center);

        vertical = GameSettings.NoiseMeterVertical;
        LayoutBars();
        built = true;
        GameSettings.Changed += OnSettingsChanged;
        ApplySavedPosition();
        meter.gameObject.SetActive(false);
    }

    /// <summary>Places the two bars for the current direction and sizes the panel.</summary>
    private void LayoutBars()
    {
        if (meter == null) return;

        if (vertical)
        {
            // Two standing bars side by side, label under each.
            const float barW = 22f, barH = 150f, gap = 26f, pad = 14f, labelH = 22f;
            meter.sizeDelta = new Vector2(pad * 2 + barW * 2 + gap + 40f, pad * 2 + barH + labelH + 6f);
            float left = -meter.sizeDelta.x / 2f + pad + 20f;
            PlaceBar(micBar, new Vector2(left + barW / 2f, 0f), barW, barH, labelH, true);
            PlaceBar(gameBar, new Vector2(left + barW * 1.5f + gap, 0f), barW, barH, labelH, true);
        }
        else
        {
            // Two lying bars one above the other, label on the left.
            const float barW = 190f, barH = 18f, gap = 14f, pad = 14f, labelW = 58f;
            meter.sizeDelta = new Vector2(pad * 2 + labelW + barW + 6f, pad * 2 + barH * 2 + gap);
            float x = -meter.sizeDelta.x / 2f + pad + labelW + 6f + barW / 2f;
            PlaceBar(micBar, new Vector2(x, (barH + gap) / 2f), barW, barH, labelW, false);
            PlaceBar(gameBar, new Vector2(x, -(barH + gap) / 2f), barW, barH, labelW, false);
        }
    }

    private void PlaceBar(Bar bar, Vector2 centre, float w, float h, float labelSize, bool standing)
    {
        bar.track.anchorMin = bar.track.anchorMax = new Vector2(0.5f, 0.5f);
        bar.track.pivot = new Vector2(0.5f, 0.5f);
        bar.track.sizeDelta = new Vector2(w, h);
        bar.track.anchoredPosition = standing ? centre + new Vector2(0f, labelSize / 2f) : centre;

        bar.fill.fillMethod = standing ? Image.FillMethod.Vertical : Image.FillMethod.Horizontal;
        bar.fill.fillOrigin = 0; // bottom / left

        // Threshold line across the bar.
        if (standing)
        {
            bar.line.anchorMin = bar.line.anchorMax = new Vector2(0.5f, PlayerNoise.LoudThreshold);
            bar.line.sizeDelta = new Vector2(w + 8f, 2f);
        }
        else
        {
            bar.line.anchorMin = bar.line.anchorMax = new Vector2(PlayerNoise.LoudThreshold, 0.5f);
            bar.line.sizeDelta = new Vector2(2f, h + 8f);
        }
        bar.line.anchoredPosition = Vector2.zero;

        RectTransform label = bar.label.rectTransform;
        label.anchorMin = label.anchorMax = new Vector2(0.5f, 0.5f);
        if (standing)
        {
            label.sizeDelta = new Vector2(w + 36f, labelSize);
            label.anchoredPosition = new Vector2(bar.track.anchoredPosition.x, bar.track.anchoredPosition.y - h / 2f - labelSize / 2f - 4f);
            bar.label.alignment = TextAlignmentOptions.Center;
        }
        else
        {
            label.sizeDelta = new Vector2(labelSize, h + 6f);
            label.anchoredPosition = new Vector2(centre.x - w / 2f - labelSize / 2f - 6f, centre.y);
            bar.label.alignment = TextAlignmentOptions.MidlineRight;
        }
    }

    private void ApplySavedPosition()
    {
        if (meter == null) return;
        Vector2 size = canvasRect.rect.size;
        if (size.x <= 1f || size.y <= 1f) size = new Vector2(1920f, 1080f);

        Vector2 pos;
        if (GameSettings.TryGetNoiseMeterPosition(out float fx, out float fy))
            pos = new Vector2(fx * size.x, fy * size.y);
        else // default: top-right corner
            pos = new Vector2(size.x - Margin - meter.sizeDelta.x / 2f, size.y - Margin - meter.sizeDelta.y / 2f);

        meter.anchoredPosition = ClampInside(pos);
    }

    private Vector2 ClampInside(Vector2 pos)
    {
        Vector2 size = canvasRect.rect.size;
        Vector2 half = meter.sizeDelta / 2f;
        return new Vector2(Mathf.Clamp(pos.x, half.x, Mathf.Max(half.x, size.x - half.x)),
                           Mathf.Clamp(pos.y, half.y, Mathf.Max(half.y, size.y - half.y)));
    }

    // ---- UI helpers -------------------------------------------------------------------------

    private RectTransform MakeTrack(string name)
    {
        RectTransform rt = NewRect(name + " Bar", meter);
        Image track = rt.gameObject.AddComponent<Image>();
        track.color = TrackColor;
        track.raycastTarget = false;
        return rt;
    }

    private static Image MakeFill(RectTransform track)
    {
        RectTransform rt = NewRect("Fill", track);
        Stretch(rt);
        Image fill = rt.gameObject.AddComponent<Image>();
        fill.type = Image.Type.Filled;
        fill.fillAmount = 0f;
        fill.raycastTarget = false;
        // A filled Image needs a sprite to fill; a 1x1 white one is enough.
        fill.sprite = WhiteSprite();
        return fill;
    }

    private static RectTransform MakeLine(RectTransform track)
    {
        RectTransform rt = NewRect("Loud Line", track);
        rt.pivot = new Vector2(0.5f, 0.5f);
        Image line = rt.gameObject.AddComponent<Image>();
        line.color = new Color(1f, 1f, 1f, 0.85f);
        line.raycastTarget = false;
        return rt;
    }

    private static Sprite whiteSprite;

    private static Sprite WhiteSprite()
    {
        if (whiteSprite != null) return whiteSprite;
        Texture2D tex = Texture2D.whiteTexture;
        whiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        return whiteSprite;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static TMP_Text MakeText(Transform parent, string name, string text, float size, TextAlignmentOptions align)
    {
        RectTransform rt = NewRect(name, parent);
        Stretch(rt);
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = TextColor;
        label.alignment = align;
        label.raycastTarget = false;
        return label;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }
}
