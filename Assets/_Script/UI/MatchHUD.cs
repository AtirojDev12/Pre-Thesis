using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PROTOTYPE MATCH CLOCK (Mr.k, 29 Sep). Top-centre of the screen:
///   - the in-game time, 00:00 -> 06:00 (2.5 real minutes per in-game hour, set on MatchDirector),
///     then the extra hour 06:00 -> 07:00;
///   - one status line (zones done / exit open / ghosts hunting).
///
/// Built in code by MatchDirector.Start on every machine (no prefab, no scene
/// edit). Lives in the gameplay scene only, so it disappears on scene change.
/// Reads MatchDirector's synced values only; it never decides anything.
/// </summary>
public sealed class MatchHUD : MonoBehaviour
{
    private static MatchHUD instance;

    private TMP_Text clockText;
    private TMP_Text statusText;
    private GameObject root;
    private int shownMinute = -1;
    private string shownStatus;

    private static readonly Color Normal = new Color(0.95f, 0.93f, 0.9f);
    private static readonly Color Good = new Color(0.45f, 1f, 0.55f);
    private static readonly Color Danger = new Color(1f, 0.35f, 0.3f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    public static void Ensure()
    {
        if (instance != null) return;
        var go = new GameObject("Match HUD (clock)");
        instance = go.AddComponent<MatchHUD>();
        instance.Build();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 45;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root = new GameObject("Top", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        var rt = (RectTransform)root.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -16f);
        rt.sizeDelta = new Vector2(1100f, 120f);

        var bg = new GameObject("Clock Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(root.transform, false);
        var bgRt = (RectTransform)bg.transform;
        bgRt.anchorMin = bgRt.anchorMax = new Vector2(0.5f, 1f);
        bgRt.pivot = new Vector2(0.5f, 1f);
        bgRt.sizeDelta = new Vector2(200f, 64f);
        var bgImage = bg.GetComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.55f);
        bgImage.raycastTarget = false;

        clockText = NewText("Clock", root.transform, 48f, FontStyles.Bold);
        Place(clockText.rectTransform, 0f, 64f);

        statusText = NewText("Status", root.transform, 26f, FontStyles.Bold);
        Place(statusText.rectTransform, 68f, 40f);
    }

    private void LateUpdate()
    {
        MatchDirector director = MatchDirector.Instance;
        bool show = director != null && director.Phase != MatchPhase.Ended && !MatchResultsUI.IsShowing;
        if (root.activeSelf != show) root.SetActive(show);
        if (!show) return;

        director.GetInGameTime(out int hour, out int minute);
        int total = hour * 60 + minute;
        if (total != shownMinute)
        {
            shownMinute = total;
            clockText.text = $"{hour:00}:{minute:00}";
            clockText.color = director.ExtraHour ? Danger : Normal;
        }

        string status;
        Color color;
        switch (director.Phase)
        {
            case MatchPhase.Night:
                if (director.MinimumMet) { status = "All zones done - the exit opens at 06:00"; color = Good; }
                else { status = $"Zones done: {director.ZonesCompleted}/{director.ZonesRequired}"; color = Normal; }
                break;
            case MatchPhase.Escape:
                if (director.MinimumMet) { status = "THE EXIT IS OPEN - get out before 07:00!"; color = Good; }
                else
                {
                    status = $"THE GHOSTS ARE HUNTING - finish every zone ({director.ZonesCompleted}/{director.ZonesRequired}) and get out before 07:00!";
                    color = Danger;
                }
                break;
            default:
                status = "";
                color = Normal;
                break;
        }

        if (status != shownStatus)
        {
            shownStatus = status;
            statusText.text = status;
            statusText.color = color;
        }
    }

    private static TMP_Text NewText(string name, Transform parent, float size, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = new Color32(0, 0, 0, 255);
        return tmp;
    }

    private static void Place(RectTransform rt, float top, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(0f, -top - height);
        rt.offsetMax = new Vector2(0f, -top);
    }
}
