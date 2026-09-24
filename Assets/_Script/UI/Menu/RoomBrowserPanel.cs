using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Room Browser: searches EOS when opened, lists every room with its map,
/// difficulty, player count and lock, and joins the one the player picks.
/// Locked rooms ask for a password first.
///
/// Rows are cloned from one hidden template row, so an artist can restyle the
/// template in the scene and every row follows.
/// </summary>
[DisallowMultipleComponent]
public class RoomBrowserPanel : MonoBehaviour
{
    [SerializeField] private MainMenuController menu;

    [Header("List")]
    [SerializeField] private RectTransform listContent;
    [SerializeField] private RoomBrowserRow rowTemplate;
    [SerializeField] private TMP_Text emptyText;
    [SerializeField] private Button refreshButton;
    [SerializeField] private Button backButton;

    [Header("Password prompt")]
    [SerializeField] private GameObject passwordPrompt;
    [SerializeField] private TMP_Text promptTitle;
    [SerializeField] private TMP_InputField promptPasswordField;
    [SerializeField] private Button promptJoinButton;
    [SerializeField] private Button promptCancelButton;

    private readonly List<RoomBrowserRow> rows = new List<RoomBrowserRow>();
    private LobbyController boundLobby;
    private RoomListEntry pendingLockedRoom;

    private void Awake()
    {
        rowTemplate.gameObject.SetActive(false);

        refreshButton.onClick.AddListener(Refresh);
        backButton.onClick.AddListener(() => menu.ShowMain());

        promptPasswordField.contentType = TMP_InputField.ContentType.Password;
        promptPasswordField.characterLimit = CreateRoomPanel.PasswordMaxLength;
        promptJoinButton.onClick.AddListener(ConfirmPassword);
        promptCancelButton.onClick.AddListener(ClosePrompt);
        promptPasswordField.onSubmit.AddListener(_ => ConfirmPassword());
    }

    private void OnEnable()
    {
        ClosePrompt();
        Refresh();
    }

    private void OnDisable()
    {
        if (boundLobby != null) boundLobby.RoomsFound -= OnRoomsFound;
        boundLobby = null;
    }

    private void Refresh()
    {
        LobbyController lobby = LobbyController.Instance;
        if (lobby == null || !LobbyController.EosReady)
        {
            ShowRooms(null, "Not connected to Epic Online Services yet.");
            return;
        }

        if (boundLobby != lobby)
        {
            if (boundLobby != null) boundLobby.RoomsFound -= OnRoomsFound;
            boundLobby = lobby;
            boundLobby.RoomsFound += OnRoomsFound;
        }

        ShowRooms(null, "Searching...");
        lobby.FindRooms();
    }

    private void OnRoomsFound(List<RoomListEntry> found)
    {
        ShowRooms(found, "No rooms found. Create one!");
    }

    private void ShowRooms(List<RoomListEntry> found, string emptyMessage)
    {
        int count = found?.Count ?? 0;

        while (rows.Count < count)
        {
            RoomBrowserRow row = Instantiate(rowTemplate, listContent);
            rows.Add(row);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            bool used = i < count;
            rows[i].gameObject.SetActive(used);
            if (used) rows[i].Bind(found[i], OnJoinClicked);
        }

        emptyText.gameObject.SetActive(count == 0);
        emptyText.text = emptyMessage;
    }

    private void OnJoinClicked(RoomListEntry entry)
    {
        if (entry.isLocked)
        {
            pendingLockedRoom = entry;
            promptTitle.text = $"{RoomDisplay.MapName(entry.mapID)} | {RoomDisplay.Difficulty(entry.difficulty)} is private";
            promptPasswordField.SetTextWithoutNotify(string.Empty);
            passwordPrompt.SetActive(true);
            promptPasswordField.ActivateInputField();
            return;
        }

        menu.JoinRoom(entry, null);
    }

    private void ConfirmPassword()
    {
        if (pendingLockedRoom == null) return;

        string password = promptPasswordField.text.Trim();
        if (string.IsNullOrEmpty(password)) return;

        RoomListEntry target = pendingLockedRoom;
        ClosePrompt();
        menu.JoinRoom(target, password);
    }

    private void ClosePrompt()
    {
        pendingLockedRoom = null;
        passwordPrompt.SetActive(false);
    }
}
