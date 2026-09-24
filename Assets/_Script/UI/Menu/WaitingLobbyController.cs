using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The waiting lobby screen (Lobby scene).
///
///   Everyone sees: room info, the seats (name, host, ready), and Leave.
///   Guests see:    Ready / Cancel ready.
///   Host sees:     Start — enabled once every guest is ready. The host does
///                  not ready up; pressing Start is their ready.
///
/// It reads live state straight from RoHRoomManager every frame (at most six
/// seats), instead of listening to events. Mirror spawns and removes room
/// players in several code paths, and polling six objects can never miss one.
/// </summary>
[DisallowMultipleComponent]
public class WaitingLobbyController : MonoBehaviour
{
    [Header("Room info")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text infoText;
    [SerializeField] private TMP_Text statusText;

    [Header("Players")]
    [SerializeField] private RectTransform playerListContent;
    [SerializeField] private LobbyPlayerRow rowTemplate;

    [Header("Buttons")]
    [SerializeField] private Button readyButton;
    [SerializeField] private TMP_Text readyButtonText;
    [SerializeField] private Button startButton;
    [SerializeField] private Button leaveButton;

    private readonly List<RoHRoomPlayer> players = new List<RoHRoomPlayer>(RoomConfig.MaxPlayers);
    private readonly List<LobbyPlayerRow> rows = new List<LobbyPlayerRow>(RoomConfig.MaxPlayers);

    private void Awake()
    {
        rowTemplate.gameObject.SetActive(false);
        for (int i = 0; i < RoomConfig.MaxPlayers; i++)
        {
            LobbyPlayerRow row = Instantiate(rowTemplate, playerListContent);
            row.gameObject.SetActive(false);
            rows.Add(row);
        }

        readyButton.onClick.AddListener(ToggleReady);
        startButton.onClick.AddListener(StartMatch);
        leaveButton.onClick.AddListener(Leave);
    }

    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PersistentHUD.PushHidden();
    }

    private void OnDisable()
    {
        PersistentHUD.PopHidden();
    }

    private void Update()
    {
        RoHRoomManager room = RoHRoomManager.Instance;
        if (room == null)
        {
            statusText.text = "No room manager. Open this scene from the Main Menu.";
            readyButton.gameObject.SetActive(false);
            startButton.gameObject.SetActive(false);
            return;
        }

        room.GetOrderedRoomPlayers(players);
        RoHRoomPlayer local = FindLocal();
        bool isHost = NetworkServer.active;

        DrawRoomInfo(room);
        DrawSeats(room, local);

        // Buttons
        readyButton.gameObject.SetActive(!isHost && local != null);
        if (local != null) readyButtonText.text = local.readyToBegin ? "Cancel ready" : "Ready";

        startButton.gameObject.SetActive(isHost);
        bool canStart = isHost && room.AllGuestsReady();
        startButton.interactable = canStart;

        // Status line
        if (isHost)
            statusText.text = canStart ? "Everyone is ready. Press Start." : "Waiting for players to get ready...";
        else if (local != null && local.readyToBegin)
            statusText.text = "Ready. Waiting for the host to start...";
        else
            statusText.text = "Press Ready when you are set.";
    }

    private RoHRoomPlayer FindLocal()
    {
        NetworkIdentity id = NetworkClient.localPlayer;
        return id != null ? id.GetComponent<RoHRoomPlayer>() : null;
    }

    private void DrawRoomInfo(RoHRoomManager room)
    {
        RoomConfig config = LobbyController.Instance != null ? LobbyController.Instance.CurrentRoom : null;
        int limit = config != null ? config.playerLimit : room.RoomPlayerLimit;

        titleText.text = "Waiting Lobby";

        string map = RoomDisplay.MapName(config != null ? config.mapID : RoomConfig.DemoMapID);
        string difficulty = config != null ? RoomDisplay.Difficulty(config.difficulty) : "?";
        string privacy = config != null && config.isPrivate ? "Private" : "Public";

        infoText.text = $"{map}  |  {difficulty}  |  {privacy}  |  Players {players.Count} / {limit}";
    }

    private void DrawSeats(RoHRoomManager room, RoHRoomPlayer local)
    {
        RoomConfig config = LobbyController.Instance != null ? LobbyController.Instance.CurrentRoom : null;
        int seats = Mathf.Clamp(config != null ? config.playerLimit : room.RoomPlayerLimit, 1, rows.Count);

        for (int i = 0; i < rows.Count; i++)
        {
            bool visible = i < seats;
            if (rows[i].gameObject.activeSelf != visible) rows[i].gameObject.SetActive(visible);
            if (!visible) continue;

            if (i < players.Count) rows[i].Bind(players[i], players[i] == local);
            else rows[i].BindEmpty();
        }
    }

    private void ToggleReady()
    {
        RoHRoomPlayer local = FindLocal();
        if (local != null) local.SetReady(!local.readyToBegin);
    }

    private void StartMatch()
    {
        RoHRoomManager room = RoHRoomManager.Instance;
        if (room != null && room.StartMatch())
        {
            startButton.interactable = false;
            statusText.text = "Starting...";
        }
    }

    private void Leave()
    {
        leaveButton.interactable = false;
        RoHRoomManager room = RoHRoomManager.Instance;
        if (room != null) room.LeaveSession();
    }
}
