using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One room in the Room Browser list.</summary>
[DisallowMultipleComponent]
public class RoomBrowserRow : MonoBehaviour
{
    [SerializeField] private TMP_Text mapText;
    [SerializeField] private TMP_Text difficultyText;
    [SerializeField] private TMP_Text playersText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button joinButton;

    private RoomListEntry entry;
    private Action<RoomListEntry> onJoin;

    private void Awake()
    {
        joinButton.onClick.AddListener(() => onJoin?.Invoke(entry));
    }

    public void Bind(RoomListEntry room, Action<RoomListEntry> joinCallback)
    {
        entry = room;
        onJoin = joinCallback;

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
