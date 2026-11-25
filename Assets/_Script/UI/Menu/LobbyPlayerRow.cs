using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One seat in the waiting lobby's player list.
///
/// 25 Nov 2026: VCR look. WaitingLobbyController calls ApplyStyle with the
/// "Player List Style" asset (same look as the Tab player list): seat numbers (01..06),
/// a ▶ marker beside your own row, Host / Ready / (you) in the style's colours.
/// Without that asset nothing changes (the colours below are used, like before).
/// </summary>
[DisallowMultipleComponent]
public class LobbyPlayerRow : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private Image background;

    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.06f);
    [SerializeField] private Color localColor = new Color(0.85f, 0.64f, 0.25f, 0.18f);
    [SerializeField] private Color readyTextColor = new Color(0.45f, 0.85f, 0.5f);
    [SerializeField] private Color waitingTextColor = new Color(0.75f, 0.75f, 0.75f);
    [Tooltip("\"Host\" words. Same green as Ready = the old look.")]
    [SerializeField] private Color hostTextColor = new Color(0.45f, 0.85f, 0.5f);

    // ---- VCR look (25 Nov) --------------------------------------------------------
    private bool styled;              // ApplyStyle was called
    private Color mutedColor = new Color(0.75f, 0.75f, 0.75f);
    private Color faintColor = new Color(0.75f, 0.75f, 0.75f, 0.5f);
    private int seatNumber;           // 1..6, 0 = not shown
    private GameObject localMarker;   // ▶ beside your own row

    /// <summary>Seat number shown in front of the name (VCR look only). 0 = none.</summary>
    public void SetSeatNumber(int number) => seatNumber = number;

    /// <summary>Colours / font / ▶ marker from a Code Screen Style (VCR look).</summary>
    public void ApplyStyle(CodeScreenStyle style, Sprite marker)
    {
        if (style == null) return;
        styled = true;
        normalColor = style.row;
        localColor = style.myRow;
        readyTextColor = style.good;
        waitingTextColor = style.muted;
        hostTextColor = style.accent;
        mutedColor = style.muted;
        faintColor = style.faint;

        style.StyleText(nameText, style.text);
        style.StyleText(stateText, style.muted);
        if (background != null)
        {
            background.sprite = style.rowSprite; // no sprite = square row (the old one was rounded)
            CodeScreenStyle.PaintImage(background, normalColor, style.rowSprite);
        }

        if (marker != null && localMarker == null)
        {
            var go = new GameObject("You Marker", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(transform, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-10f, 0f); // just left of the row
            rt.sizeDelta = new Vector2(22f, 22f);
            Image image = go.GetComponent<Image>();
            image.sprite = marker;
            image.color = style.text;
            image.raycastTarget = false;
            localMarker = go;
            go.SetActive(false);
        }
    }

    private string SeatPrefix() =>
        styled && seatNumber > 0 ? CodeScreenStyle.Paint(seatNumber.ToString("00"), faintColor) + "   " : string.Empty;

    public void Bind(RoHRoomPlayer player, bool isLocal)
    {
        string suffix = !isLocal ? string.Empty
            : styled ? "  " + CodeScreenStyle.Paint("(you)", mutedColor) : "  (you)";
        nameText.text = SeatPrefix() + player.DisplayName + suffix;

        if (player.IsHost)
        {
            stateText.text = "Host";
            stateText.color = hostTextColor;
        }
        else
        {
            stateText.text = player.readyToBegin ? "Ready" : "Not ready";
            stateText.color = player.readyToBegin ? readyTextColor : waitingTextColor;
        }

        if (background != null) background.color = isLocal ? localColor : normalColor;
        if (localMarker != null && localMarker.activeSelf != isLocal) localMarker.SetActive(isLocal);
    }

    /// <summary>A seat nobody is sitting in (room limit not reached yet).</summary>
    public void BindEmpty()
    {
        nameText.text = styled
            ? SeatPrefix() + CodeScreenStyle.Paint("Waiting for player...", faintColor)
            : "Waiting for player...";
        stateText.text = string.Empty;
        if (background != null) background.color = normalColor;
        if (localMarker != null && localMarker.activeSelf) localMarker.SetActive(false);
    }
}
