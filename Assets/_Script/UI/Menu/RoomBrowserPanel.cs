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
    [Tooltip("Optional (added 1 Oct): type part of a room name to filter the list.")]
    [SerializeField] private TMP_InputField searchField;

    [Header("Password prompt")]
    [SerializeField] private GameObject passwordPrompt;
    [SerializeField] private TMP_Text promptTitle;
    [SerializeField] private TMP_InputField promptPasswordField;
    [SerializeField] private Button promptJoinButton;
    [SerializeField] private Button promptCancelButton;

    private readonly List<RoomBrowserRow> rows = new List<RoomBrowserRow>();
    private LobbyController boundLobby;
    private RoomListEntry pendingLockedRoom;
    private List<RoomListEntry> lastFound;
    private readonly List<RoomListEntry> filtered = new List<RoomListEntry>();

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
        if (searchField != null)
        {
            searchField.characterLimit = RoomConfig.MaxRoomNameLength;
            searchField.onValueChanged.AddListener(_ => ShowRooms(lastFound, lastFound == null ? "Searching..." : "No rooms found. Create one!"));
        }
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

        lastFound = null;
        ShowRooms(null, "Searching...");
        lobby.FindRooms();
    }

    private void OnRoomsFound(List<RoomListEntry> found)
    {
        lastFound = found;
        ShowRooms(found, "No rooms found. Create one!");
    }

    private void ShowRooms(List<RoomListEntry> all, string emptyMessage)
    {
        // Search box: keep rooms whose name contains the typed text (any case).
        string search = searchField != null ? searchField.text.Trim() : string.Empty;
        filtered.Clear();
        if (all != null)
        {
            foreach (RoomListEntry room in all)
            {
                if (search.Length == 0 ||
                    (room.roomName ?? string.Empty).IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    filtered.Add(room);
            }
        }
        if (all != null && all.Count > 0 && filtered.Count == 0) emptyMessage = $"No room named \"{search}\".";
        List<RoomListEntry> found = filtered;
        int count = found.Count;

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
            promptTitle.text = $"\"{entry.roomName}\" is private";
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
