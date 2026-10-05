using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 5 Oct (Mr.k). Battery bar for the flashlight in your hand (bottom right).
/// Built in code like ThrowChargeHUD. Shown by FlashlightController.
/// </summary>
public sealed class FlashlightHUD : MonoBehaviour
{
    private const float BarWidth = 260f;
    private Image fill;
    private TMP_Text label;
    private TMP_Text hint;
    private int shownPercent = -1, shownState = -1; // rebuild text only on change (no garbage per frame)

    private static readonly Color Full = new Color(0.95f, 0.85f, 0.45f);
    private static readonly Color Low = new Color(1f, 0.35f, 0.3f);

    public static FlashlightHUD Create()
    {
        var root = new GameObject("Flashlight HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 51;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        FlashlightHUD hud = root.AddComponent<FlashlightHUD>();

        RectTransform panel = Rect("Panel", root.transform, new Vector2(300, 110));
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1, 0);
        panel.anchoredPosition = new Vector2(-30, 40);
        Image background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.08f, 0.07f, 0.07f, 0.8f);
        background.raycastTarget = false;

        hud.label = Text("Label", panel, new Vector2(280, 30), new Vector2(0, 30), 20);

        RectTransform track = Rect("Track", panel, new Vector2(BarWidth + 4, 18));
        track.anchoredPosition = new Vector2(0, 0);
        Image trackImage = track.gameObject.AddComponent<Image>();
        trackImage.color = new Color(0.2f, 0.2f, 0.2f);
        trackImage.raycastTarget = false;

        hud.fill = Rect("Fill", track, new Vector2(BarWidth, 14)).gameObject.AddComponent<Image>();
        hud.fill.raycastTarget = false;
        RectTransform fillRect = hud.fill.rectTransform;
        fillRect.anchorMin = fillRect.anchorMax = fillRect.pivot = new Vector2(0, 0.5f);
        fillRect.anchoredPosition = new Vector2(2, 0);

        hud.hint = Text("Hint", panel, new Vector2(290, 30), new Vector2(0, -32), 17);
        hud.Hide();
        return hud;
    }

    /// <param name="usesBatteries">Paid flashlight: hint says R + how many Batteries you carry.</param>
    public void Show(string itemName, float charge, bool switchedOn, bool usesBatteries, int batteries)
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        charge = Mathf.Clamp01(charge);
        int percent = Mathf.CeilToInt(charge * 100f);
        bool low = charge < 0.25f;
        int state = (charge <= 0f ? 0 : switchedOn ? 1 : 2) + (low ? 10 : 0) + (usesBatteries ? 100 + batteries * 1000 : 0);
        if (percent != shownPercent || state != shownState)
        {
            shownPercent = percent;
            shownState = state;
            string word = charge <= 0f ? "<color=#FF6A5A>EMPTY</color>" : switchedOn ? "ON" : "OFF";
            label.text = $"{itemName}  {word}  {percent}%";
            if (usesBatteries)
                hint.text = batteries > 0
                    ? (low ? $"PRESS [R] FOR A NEW BATTERY (x{batteries})" : $"[{FlashlightController.ToggleHint}] on/off   [R] battery x{batteries}")
                    : (low ? "NO BATTERIES - buy them in the lobby shop" : $"[{FlashlightController.ToggleHint}] on/off   no batteries");
            else
                hint.text = low ? "MASH [SPACE] TO CHARGE" : $"[{FlashlightController.ToggleHint}] on/off   [Space] charge";
        }
        fill.rectTransform.sizeDelta = new Vector2(BarWidth * charge, 14);
        fill.color = low ? Low : Full;
        // Pulse the prompt so a dying light is noticed.
        hint.alpha = low ? 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 8f) : 1f;
    }

    public void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 size)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        return rect;
    }

    private static TMP_Text Text(string name, Transform parent, Vector2 size, Vector2 position, float fontSize)
    {
        RectTransform rect = Rect(name, parent, size);
        rect.anchoredPosition = position;
        TMP_Text text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }
}
