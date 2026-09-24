using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One seat in the waiting lobby's player list.</summary>
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

    public void Bind(RoHRoomPlayer player, bool isLocal)
    {
        string suffix = isLocal ? "  (you)" : string.Empty;
        nameText.text = player.DisplayName + suffix;

        if (player.IsHost)
        {
            stateText.text = "Host";
            stateText.color = readyTextColor;
        }
        else
        {
            stateText.text = player.readyToBegin ? "Ready" : "Not ready";
            stateText.color = player.readyToBegin ? readyTextColor : waitingTextColor;
        }

        if (background != null) background.color = isLocal ? localColor : normalColor;
    }

    /// <summary>A seat nobody is sitting in (room limit not reached yet).</summary>
    public void BindEmpty()
    {
        nameText.text = "Waiting for player...";
        stateText.text = string.Empty;
        if (background != null) background.color = normalColor;
    }
}
