using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The noise meter: two bars for the local player.
///   MIC  – how loud your real microphone is.
///   GAME – noise you make in the game (moving, the Walkie-Talkie you carry...).
/// Values come from <see cref="PlayerNoise.Local"/>. A white line on each bar
/// marks <see cref="PlayerNoise.LoudThreshold"/>; above it the bar turns red.
///
/// 16 Nov 2026 (Mr.k):
///   - MIC and GAME are now TWO SEPARATE BARS inside the GAME HUD prefab (<see cref="HudBar"/>:
///     frame + fill + line + label, each editable and movable on its own).
///   - Each bar has a lying (horizontal) and a standing (vertical) version in the prefab.
///     Settings > Sound > Noise meter still lets the PLAYER pick the direction; the game
///     shows that version and hides the other one.
///   - Players can no longer drag the meter around: the designer places the bars in the
///     GAME HUD prefab. ("Move meter" / "Reset position" are gone from Settings.)
///   - This script is only the logic. If the GAME HUD has no noise bars (run
///     Tools > Pre-Thesis > UI > GAME HUD: Add Missing Parts), it builds the old code-made
///     meter in the top-right corner.
///
/// Lives for the whole game. Shown only in a match (when there is a local player).
/// LOCAL ONLY (UI).
/// </summary>
public sealed class NoiseMeterHUD : MonoBehaviour
{
    private static readonly Color PanelColor = new Color(0.06f, 0.05f, 0.05f, 0.7f);
    private static readonly Color TrackColor = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color QuietColor = new Color(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Color MidColor = new Color(0.95f, 0.78f, 0.3f, 1f);
    private static readonly Color LoudColor = new Color(0.95f, 0.3f, 0.25f, 1f);
    private static readonly Color TextColor = new Color(0.96f, 0.94f, 0.93f, 1f);

    private const int NormalSortOrder = 49;   // just under the pause menu (50)
    private const float Margin = 24f;

    private static NoiseMeterHUD instance;

    // The bars inside the GAME HUD prefab (null = not there).
    private HudBar micLying, micStanding, gameLying, gameStanding;
    private PersistentHUD barsFrom;

    // Old code-made meter (only when the GAME HUD has no noise bars).
    private RectTransform canvasRect;
    private RectTransform meter;
    private readonly Bar micBar = new Bar();
    private readonly Bar gameBar = new Bar();
    private bool built;
    private Vector2 lastCanvasSize;

    private bool vertical;

    private sealed class Bar
    {
        public RectTransform track;
        public Image fill;
        public RectTransform line;
        public TMP_Text label;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>Creates the meter logic once (called when a local player appears).</summary>
    public static NoiseMeterHUD Ensure()
    {
        if (instance != null) return instance;
        var go = new GameObject("Noise Meter HUD (logic)");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<NoiseMeterHUD>();
        instance.vertical = GameSettings.NoiseMeterVertical;
        GameSettings.Changed += instance.OnSettingsChanged;
        return instance;
    }

    private void OnDestroy()
    {
        GameSettings.Changed -= OnSettingsChanged;
        ShowHudBars(false, null, null);
        if (instance == this) instance = null;
    }

    private void OnSettingsChanged()
    {
        bool v = GameSettings.NoiseMeterVertical;
        if (v == vertical) return;
        vertical = v;
        if (built) { LayoutBars(); PlaceTopRight(); }
    }

    // ---- Update -------------------------------------------------------------------

    private void LateUpdate()
    {
        PlayerNoise noise = PlayerNoise.Local;
        bool show = noise != null;

        if (FindHudBars())
        {
            if (built && meter.gameObject.activeSelf) meter.gameObject.SetActive(false);
            // The version for the chosen direction; the other direction if the designer removed one.
            HudBar mic = vertical ? (micStanding != null ? micStanding : micLying) : (micLying != null ? micLying : micStanding);
            HudBar game = vertical ? (gameStanding != null ? gameStanding : gameLying) : (gameLying != null ? gameLying : gameStanding);
            ShowHudBars(show, mic, game);
            if (!show) return;
            SetHudBar(mic, noise.Mic);
            SetHudBar(game, noise.Game);
            return;
        }

        // Old code-made meter.
        if (!show && !built) return;
        if (!built) BuildOldMeter();
        if (meter.gameObject.activeSelf != show) meter.gameObject.SetActive(show);
        if (!show) return;

        // The canvas size is only final after the CanvasScaler ran: re-place when it changes.
        Vector2 canvasSize = canvasRect.rect.size;
        if (canvasSize != lastCanvasSize)
        {
            lastCanvasSize = canvasSize;
            PlaceTopRight();
        }
        SetOldBar(micBar, noise.Mic);
        SetOldBar(gameBar, noise.Game);
    }

    // ---- Bars in the GAME HUD prefab ---------------------------------------------------

    /// <summary>Looks the bars up again when the GAME HUD appears or is replaced. True = at least MIC and GAME exist.</summary>
    private bool FindHudBars()
    {
        PersistentHUD hud = PersistentHUD.Instance;
        if (hud != barsFrom)
        {
            barsFrom = hud;
            micLying = HudBar.Find(HudBar.MicNoiseHorizontal);
            micStanding = HudBar.Find(HudBar.MicNoiseVertical);
            gameLying = HudBar.Find(HudBar.GameNoiseHorizontal);
            gameStanding = HudBar.Find(HudBar.GameNoiseVertical);
        }
        return (micLying != null || micStanding != null) && (gameLying != null || gameStanding != null);
    }

    private void ShowHudBars(bool show, HudBar mic, HudBar game)
    {
        if (micLying != null) micLying.Show(show && micLying == mic);
        if (micStanding != null) micStanding.Show(show && micStanding == mic);
        if (gameLying != null) gameLying.Show(show && gameLying == game);
        if (gameStanding != null) gameStanding.Show(show && gameStanding == game);
    }

    private static void SetHudBar(HudBar bar, float value)
    {
        if (bar == null) return;
        value = Mathf.Clamp01(value);
        bar.Set(value);
        bar.SetFillColour(ColourFor(value));
        bar.SetMarker(PlayerNoise.LoudThreshold);
    }

    private static Color ColourFor(float value) =>
        value >= PlayerNoise.LoudThreshold ? LoudColor
        : value >= PlayerNoise.LoudThreshold * 0.66f ? MidColor : QuietColor;

    // ---- Old code-made meter (fallback) ----------------------------------------------------

    private static void SetOldBar(Bar bar, float value)
    {
        value = Mathf.Clamp01(value);
        bar.fill.fillAmount = value;
        bar.fill.color = ColourFor(value);
    }

    private void BuildOldMeter()
    {
        var root = new GameObject("Noise Meter HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = NormalSortOrder;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = (RectTransform)root.transform;

        meter = NewRect("Meter", root.transform);
        meter.anchorMin = meter.anchorMax = Vector2.zero;   // position in canvas units from bottom-left
        meter.pivot = new Vector2(0.5f, 0.5f);
        Image meterPanel = meter.gameObject.AddComponent<Image>();
        meterPanel.color = PanelColor;
        meterPanel.raycastTarget = false;

        micBar.track = MakeTrack("Mic");
        gameBar.track = MakeTrack("Game");
        micBar.fill = MakeFill(micBar.track);
        gameBar.fill = MakeFill(gameBar.track);
        micBar.line = MakeLine(micBar.track);
        gameBar.line = MakeLine(gameBar.track);
        micBar.label = MakeText(meter, "Mic Label", "MIC", 16, TextAlignmentOptions.Center);
        gameBar.label = MakeText(meter, "Game Label", "GAME", 16, TextAlignmentOptions.Center);

        LayoutBars();
        built = true;
        PlaceTopRight();
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

    /// <summary>The old meter always sits in the top-right corner (16 Nov: players no longer move it).</summary>
    private void PlaceTopRight()
    {
        if (meter == null) return;
        Vector2 size = canvasRect.rect.size;
        if (size.x <= 1f || size.y <= 1f) size = new Vector2(1920f, 1080f);
        meter.anchoredPosition = new Vector2(size.x - Margin - meter.sizeDelta.x / 2f, size.y - Margin - meter.sizeDelta.y / 2f);
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
}
