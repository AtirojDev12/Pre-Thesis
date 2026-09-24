using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The main menu screen: switches between the Main / Create Room / Room
/// Browser / Settings panels, shows EOS connection status, a "please wait"
/// overlay while EOS or Mirror is working, and one popup for errors.
///
/// Built and wired by Tools > Pre-Thesis > Build Main Menu + Lobby. All button
/// listeners are added here in code, so rebuilding the UI never loses them.
///
/// It never talks to EOS or Mirror directly for rooms — only through
/// LobbyController, which is the one place that knows how.
/// </summary>
[DisallowMultipleComponent]
public class MainMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private CreateRoomPanel createPanel;
    [SerializeField] private RoomBrowserPanel browserPanel;
    [SerializeField] private SettingsPanel settingsPanel;

    [Header("Main panel buttons")]
    [SerializeField] private Button createButton;
    [SerializeField] private Button browseButton;
    [SerializeField] private Button quickJoinButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;

    [Header("Status")]
    [SerializeField] private TMP_Text connectionText;
    [SerializeField] private TMP_Text playerNameText;

    [Header("Busy overlay")]
    [SerializeField] private GameObject busyOverlay;
    [SerializeField] private TMP_Text busyText;

    [Header("Message popup")]
    [SerializeField] private GameObject messagePopup;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Button messageOkButton;

    private LobbyController boundLobby;

    // True from the moment we ask EOS/Mirror to do something until it either
    // fails (popup) or succeeds (scene changes and this object is gone).
    private bool waitingForResult;

    public LobbyController Lobby => LobbyController.Instance;

    private void Awake()
    {
        createButton.onClick.AddListener(() => ShowPanel(createPanel.gameObject));
        browseButton.onClick.AddListener(() => ShowPanel(browserPanel.gameObject));
        quickJoinButton.onClick.AddListener(QuickJoin);
        settingsButton.onClick.AddListener(() => ShowPanel(settingsPanel.gameObject));
        quitButton.onClick.AddListener(Quit);
        messageOkButton.onClick.AddListener(() => messagePopup.SetActive(false));

        messagePopup.SetActive(false);
        busyOverlay.SetActive(false);
        ShowMain();
    }

    private void OnEnable()
    {
        // Coming back from a match leaves the cursor locked by the camera.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PersistentHUD.PushHidden();
    }

    private void OnDisable()
    {
        Unbind();
        PersistentHUD.PopHidden();
    }

    private void Update()
    {
        // LobbyController lives on the NetworkManager prefab; its Awake may run
        // after ours on the first frame, so bind lazily.
        if (boundLobby == null && Lobby != null) Bind(Lobby);

        bool eosReady = LobbyController.EosReady;
        bool lobbyBusy = boundLobby != null && boundLobby.IsBusy;
        bool connecting = NetworkClient.active && !NetworkClient.isConnected;
        bool busy = waitingForResult || lobbyBusy || connecting;

        connectionText.text = eosReady ? "Online" : "Connecting to Epic Online Services...";
        playerNameText.text = "Playing as " + GameSettings.PlayerName;

        bool canAct = eosReady && boundLobby != null && !busy;
        createButton.interactable = canAct;
        browseButton.interactable = canAct;
        quickJoinButton.interactable = canAct;

        if (busyOverlay.activeSelf != busy) busyOverlay.SetActive(busy);

        // A session that ended while we sat here (wrong password, room full,
        // host left) leaves its reason behind. Show it once.
        if (!string.IsNullOrEmpty(RoHRoomManager.LastDisconnectReason) && !NetworkClient.active)
        {
            waitingForResult = false;
            ShowMessage(RoHRoomManager.LastDisconnectReason);
            RoHRoomManager.LastDisconnectReason = null;
        }
    }

    // ---- Panels --------------------------------------------------------------

    public void ShowMain() => ShowPanel(mainPanel);

    private void ShowPanel(GameObject panel)
    {
        mainPanel.SetActive(panel == mainPanel);
        createPanel.gameObject.SetActive(panel == createPanel.gameObject);
        browserPanel.gameObject.SetActive(panel == browserPanel.gameObject);
        settingsPanel.gameObject.SetActive(panel == settingsPanel.gameObject);
    }

    // ---- Actions (also called by the panels) --------------------------------

    public void CreateRoom(RoomConfig config)
    {
        if (Lobby == null) return;
        waitingForResult = true;
        busyText.text = "Creating room...";
        Lobby.CreateRoom(config);
    }

    public void JoinRoom(RoomListEntry entry, string password)
    {
        if (Lobby == null) return;
        waitingForResult = true;
        busyText.text = "Joining room...";
        Lobby.JoinRoom(entry, password);
    }

    private void QuickJoin()
    {
        if (Lobby == null) return;
        waitingForResult = true;
        busyText.text = "Looking for an open room...";
        Lobby.QuickJoin();
    }

    public void ShowMessage(string text)
    {
        messageText.text = text;
        messagePopup.SetActive(true);
        messagePopup.transform.SetAsLastSibling();
    }

    private static void Quit()
    {
        GameSettings.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- LobbyController events ----------------------------------------------

    private void Bind(LobbyController lobby)
    {
        boundLobby = lobby;
        boundLobby.OperationFailed += OnOperationFailed;
        boundLobby.StatusChanged += OnStatusChanged;
    }

    private void Unbind()
    {
        if (boundLobby == null) return;
        boundLobby.OperationFailed -= OnOperationFailed;
        boundLobby.StatusChanged -= OnStatusChanged;
        boundLobby = null;
    }

    private void OnOperationFailed(string message)
    {
        waitingForResult = false;
        ShowMessage(message);
    }

    private void OnStatusChanged(string status)
    {
        busyText.text = status;
    }
}
