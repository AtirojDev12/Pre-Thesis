using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 14 Oct 2026. Local "hold E" progress bar for ZoneChoreSpot (bottom centre).
/// Created the first time it is needed; one per scene. Purely visual.
///
/// 16 Nov 2026 (Mr.k): the bar is now a real part of the GAME HUD prefab (HudBar "task":
/// frame + fill + text, all editable). This script is only the logic. If the GAME HUD has no
/// such bar (run Tools > Pre-Thesis > UI > GAME HUD: Add Missing Parts), the old code-made bar is used.
/// </summary>
public sealed class ChoreProgressHUD : MonoBehaviour
{
    private const float BarWidth = 360f;

    private static ChoreProgressHUD instance;
    private static Object owner;

    private RectTransform fill;
    private TMP_Text label;
    private string shownText;
    private HudBar bar; // 16 Nov: the bar inside the GAME HUD (null = code-made bar)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        owner = null;
    }

    public static void Show(Object who, string action, float progress) =>
        ShowText(who, action + "...  (keep holding E)", progress);

    /// <summary>16 Oct: same bar with your own words (e.g. a zone rule's "wait" bar).</summary>
    public static void ShowText(Object who, string text, float progress)
    {
        if (instance == null) instance = Create();
        owner = who;
        if (instance.bar != null)
        {
            instance.bar.Show(true);
            instance.bar.SetLabel(text);
            instance.bar.Set(progress);
            return;
        }
        if (!instance.gameObject.activeSelf) instance.gameObject.SetActive(true);
        if (instance.shownText != text)
        {
            instance.shownText = text;
            instance.label.text = text;
        }
        instance.fill.sizeDelta = new Vector2(BarWidth * Mathf.Clamp01(progress), instance.fill.sizeDelta.y);
    }

    /// <summary>Hides the bar only if <paramref name="who"/> is the one showing it.</summary>
    public static void Hide(Object who)
    {
        if (instance == null || owner != who) return;
        owner = null;
        if (instance.bar != null) instance.bar.Show(false);
        else instance.gameObject.SetActive(false);
    }

    private static ChoreProgressHUD Create()
    {
        HudBar hudBar = HudBar.Find(HudBar.TaskProgress);
        if (hudBar != null)
        {
            // Logic only: the pictures and text live in the GAME HUD prefab.
            ChoreProgressHUD logic = new GameObject("Chore Progress HUD (logic)").AddComponent<ChoreProgressHUD>();
            logic.bar = hudBar;
            hudBar.Show(false);
            return logic;
        }

        var root = new GameObject("Chore Progress HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 51;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        ChoreProgressHUD hud = root.AddComponent<ChoreProgressHUD>();

        RectTransform panel = Rect("Panel", root.transform, new Vector2(BarWidth + 20f, 64f));
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 190f);
        AddImage(panel, new Color(0.08f, 0.07f, 0.07f, 0.85f));

        RectTransform track = Rect("Track", panel, new Vector2(BarWidth, 12f));
        track.anchoredPosition = new Vector2(0f, -14f);
        AddImage(track, new Color(0.2f, 0.2f, 0.2f));

        hud.fill = Rect("Fill", track, new Vector2(0f, 12f));
        hud.fill.anchorMin = hud.fill.anchorMax = new Vector2(0f, 0.5f);
        hud.fill.pivot = new Vector2(0f, 0.5f);
        hud.fill.anchoredPosition = Vector2.zero;
        AddImage(hud.fill, new Color(0.85f, 0.64f, 0.25f));

        RectTransform text = Rect("Label", panel, new Vector2(BarWidth, 30f));
        text.anchoredPosition = new Vector2(0f, 12f);
        hud.label = text.gameObject.AddComponent<TextMeshProUGUI>();
        hud.label.fontSize = 20f;
        hud.label.color = Color.white;
        hud.label.alignment = TextAlignmentOptions.Center;
        hud.label.raycastTarget = false;

        root.SetActive(false);
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

    private static void AddImage(RectTransform rect, Color color)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private void OnDestroy()
    {
        if (bar != null) bar.Show(false);
        if (instance == this) instance = null;
    }
}
