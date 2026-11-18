using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Throw charge bar (left side), shown while you charge a throw.
///
/// 16 Nov 2026 (Mr.k): the bar is now a real part of the GAME HUD prefab (HudBar "throw":
/// frame + fill + texts, all editable). This script is only the logic. If the GAME HUD has no
/// such bar (run Tools > Pre-Thesis > UI > GAME HUD: Add Missing Parts), the old code-made bar is used.
/// </summary>
public sealed class ThrowChargeHUD : MonoBehaviour
{
    private Image fill;
    private TMP_Text label;
    private TMP_Text hint;
    private HudBar bar; // 16 Nov: the bar inside the GAME HUD (null = code-made bar)

    public static ThrowChargeHUD Create()
    {
        HudBar hudBar = HudBar.Find(HudBar.ThrowCharge);
        if (hudBar != null)
        {
            // Logic only: the pictures and texts live in the GAME HUD prefab.
            ThrowChargeHUD logic = new GameObject("Throw Charge HUD (logic)").AddComponent<ThrowChargeHUD>();
            logic.bar = hudBar;
            hudBar.Show(false);
            return logic;
        }

        GameObject root = new GameObject("Throw Charge HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 51;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        ThrowChargeHUD hud = root.AddComponent<ThrowChargeHUD>();
        RectTransform panel = Rect("Charge", root.transform, new Vector2(220, 235));
        panel.anchorMin = panel.anchorMax = new Vector2(0, 0.5f);
        panel.pivot = new Vector2(0, 0);
        panel.anchoredPosition = new Vector2(28, 45);
        Image background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.08f, 0.07f, 0.07f, 0.85f);
        background.raycastTarget = false;
        hud.label = Text("Item", panel, new Vector2(200, 35), new Vector2(0, 95), 20);
        RectTransform track = Rect("Track", panel, new Vector2(28, 135));
        track.anchoredPosition = new Vector2(0, 0);
        Image trackImage = track.gameObject.AddComponent<Image>();
        trackImage.color = new Color(0.2f, 0.2f, 0.2f);
        trackImage.raycastTarget = false;
        hud.fill = Rect("Fill", track, new Vector2(24, 131)).gameObject.AddComponent<Image>();
        hud.fill.color = new Color(0.85f, 0.64f, 0.25f);
        hud.fill.raycastTarget = false;
        // A bottom-anchored rectangle works without a sprite dependency.
        hud.fill.rectTransform.anchorMin = hud.fill.rectTransform.anchorMax = new Vector2(0.5f, 0);
        hud.fill.rectTransform.pivot = new Vector2(0.5f, 0);
        hud.fill.rectTransform.anchoredPosition = new Vector2(0, 2);
        hud.hint = Text("Hint", panel, new Vector2(210, 40), new Vector2(0, -92), 17);
        hud.Hide();
        return hud;
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
    public void Show(string itemName, float charge)
    {
        charge = Mathf.Clamp01(charge);
        string hintText = Keybinds.Fix(charge >= 1f ? "MAX\nRelease Q to throw" : "Release Q to throw"); // 20 Oct: rebindable
        if (bar != null)
        {
            bar.Show(true);
            bar.SetLabel(itemName);
            bar.Set(charge);
            bar.SetHint(hintText);
            return;
        }
        gameObject.SetActive(true);
        label.text = itemName;
        fill.rectTransform.sizeDelta = new Vector2(24, 131 * charge);
        hint.text = hintText;
    }

    public void Hide()
    {
        if (bar != null) bar.Show(false);
        else gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (bar != null) bar.Show(false);
    }
}
