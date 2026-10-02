using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One room in the Room Browser list.</summary>
[DisallowMultipleComponent]
public class RoomBrowserRow : MonoBehaviour
{
    [Tooltip("Optional (added 1 Oct): the room's name.")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text mapText;
    [SerializeField] private TMP_Text difficultyText;
    [SerializeField] private TMP_Text playersText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button joinButton;

    private RoomListEntry entry;
    private Action<RoomListEntry> onJoin;
    private Action<RoomListEntry> onSpectate;
    private Button spectateButton; // Development Build only (2 Oct)

    private void Awake()
    {
        joinButton.onClick.AddListener(() => onJoin?.Invoke(entry));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // A copy of the Join button, placed after it by the row's layout.
        // Rows are cloned from a template row: reuse a copy that came along.
        const string SpectateName = "Spectate Button (dev)";
        Transform existing = joinButton.transform.parent.Find(SpectateName);
        GameObject copy = existing != null ? existing.gameObject : Instantiate(joinButton.gameObject, joinButton.transform.parent);
        copy.name = SpectateName;
        spectateButton = copy.GetComponent<Button>();
        spectateButton.onClick.RemoveAllListeners();
        spectateButton.onClick.AddListener(() => onSpectate?.Invoke(entry));
        TMP_Text label = copy.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = "Spectate";
#endif
    }

    public void Bind(RoomListEntry room, Action<RoomListEntry> joinCallback, Action<RoomListEntry> spectateCallback = null)
    {
        entry = room;
        onJoin = joinCallback;
        onSpectate = spectateCallback;
        if (spectateButton != null)
        {
            spectateButton.gameObject.SetActive(spectateCallback != null);
            spectateButton.interactable = true; // full rooms and matches in progress too
        }

        if (nameText != null) nameText.text = room.roomName;
        mapText.text = RoomDisplay.MapName(room.mapID);
        difficultyText.text = RoomDisplay.Difficulty(room.difficulty);
        playersText.text = $"{room.currentPlayers} / {room.maxPlayers}";

        if (room.inProgress) statusText.text = "In game";
        else if (room.IsFull) statusText.text = "Full";
        else if (room.isLocked) statusText.text = "Private";
        else statusText.text = "Open";

        joinButton.interactable = room.IsJoinable;
    }
}
