using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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

    // ---- 3D lobby (1 Oct) ------------------------------------------------------
    // With a walking body the panel is hidden and opens with M (Esc closes).
    // NOT Tab: Tab already frees the mouse for the popcorn / ticket screens
    // (PopcornUiCursorController), in matches and at the practice stations.
    // Without a body yet (still connecting, or lobby bodies switched off) it is
    // always shown, like the old flat lobby.
    [Header("3D lobby")]
    [Tooltip("Opens / closes this panel while walking around the lobby. Do not use Tab (popcorn mouse key).")]
    [SerializeField] private Key panelKey = Key.M;

    private Canvas panelCanvas;
    private bool panelOpen;          // only meaningful while we have a body
    private bool appliedVisible;     // what is on screen now
    private bool registeredOpen;     // counted in OverlayPanels / PersistentHUD
    private bool hasApplied;
    private GameObject hintRoot;
    private readonly List<LobbyPlayerRow> rows = new List<LobbyPlayerRow>(RoomConfig.MaxPlayers);

    // ---- Kick (2 Oct): host-only Kick button on each guest row -------------
    // First click turns it into "Sure?" for 3 s; a second click kicks.
    private readonly List<Button> kickButtons = new List<Button>(RoomConfig.MaxPlayers);
    private readonly List<TMP_Text> kickLabels = new List<TMP_Text>(RoomConfig.MaxPlayers);
    private readonly RoHRoomPlayer[] rowSeats = new RoHRoomPlayer[RoomConfig.MaxPlayers];
    private int armedKickRow = -1;
    private float armedKickUntil;

    private void Awake()
    {
        rowTemplate.gameObject.SetActive(false);
        for (int i = 0; i < RoomConfig.MaxPlayers; i++)
        {
            LobbyPlayerRow row = Instantiate(rowTemplate, playerListContent);
            row.gameObject.SetActive(false);
            rows.Add(row);
            AddKickButton(row, i);
        }

        readyButton.onClick.AddListener(ToggleReady);
        startButton.onClick.AddListener(StartMatch);
        leaveButton.onClick.AddListener(Leave);
    }

    private void Start()
    {
        panelCanvas = GetComponent<Canvas>();
        BuildHint();
    }

    private void OnDisable() => SetRegistered(false);
    private void OnDestroy() => SetRegistered(false);

    private static bool HasBody => PlayerHealth.LocalInstance != null;
    // A spectator (2 Oct) flies around with a camera: treat it like having a body
    // (panel hidden, M opens it).
    private static bool Walking => HasBody || SpectatorSession.Active;

    private void HandlePanelInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (!Walking || keyboard == null) return;

        // Esc menu / shop own the screen while open.
        if (PauseMenuController.IsOpen) return;
        if (!panelOpen && OverlayPanels.AnyOpen) return;

        if (keyboard[panelKey].wasPressedThisFrame) panelOpen = !panelOpen;
        else if (panelOpen && keyboard.escapeKey.wasPressedThisFrame) panelOpen = false;
    }

    private void ApplyPanelVisibility()
    {
        // Visible = covers the screen and owns the mouse. Always up without a body.
        bool visible = !Walking || panelOpen;
        if (hasApplied && visible == appliedVisible) return;
        hasApplied = true;
        appliedVisible = visible;

        if (panelCanvas != null) panelCanvas.enabled = visible;
        if (hintRoot != null) hintRoot.SetActive(!visible && Walking);
        SetRegistered(visible);

        if (visible) OverlayPanels.SetMouseForUi(true);
        else if (Walking) OverlayPanels.SetMouseForUi(false);

        if (HasBody) DisableSceneCameras();
    }

    /// <summary>
    /// Once my body exists, the lobby scene's own camera (and its audio
    /// listener) must stop, or there are two cameras / two listeners.
    /// </summary>
    private static void DisableSceneCameras()
    {
        PlayerHealth me = PlayerHealth.LocalInstance;
        if (me == null) return;
        foreach (Camera cam in FindObjectsByType<Camera>())
        {
            if (!cam.enabled || cam.GetComponentInParent<PlayerHealth>() != null) continue;
            cam.enabled = false;
            AudioListener listener = cam.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
        }
    }

    private void SetRegistered(bool open)
    {
        if (open == registeredOpen) return;
        registeredOpen = open;
        if (open) { OverlayPanels.Opened(); PersistentHUD.PushHidden(); }
        else { OverlayPanels.Closed(); PersistentHUD.PopHidden(); GameplayInput.Blocked = false; }
    }

    /// <summary>Key reminder at the TOP while walking around (the bottom is the hotbar).</summary>
    private void BuildHint()
    {
        hintRoot = new GameObject("Lobby Hint", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = hintRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        CanvasScaler scaler = hintRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Dark strip so it stays readable through the retro screen filter.
        var stripGo = new GameObject("Strip", typeof(RectTransform), typeof(Image));
        stripGo.transform.SetParent(hintRoot.transform, false);
        var strip = (RectTransform)stripGo.transform;
        strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 1f);
        strip.pivot = new Vector2(0.5f, 1f);
        strip.anchoredPosition = new Vector2(0f, -64f); // under the HP bar (top centre)
        strip.sizeDelta = new Vector2(1240f, 56f);
        Image stripImage = stripGo.GetComponent<Image>();
        stripImage.color = new Color(0f, 0f, 0f, 0.6f);
        stripImage.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(strip, false);
        var rt = (RectTransform)textGo.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var text = textGo.GetComponent<TextMeshProUGUI>();
        text.text = $"<b>{panelKey}</b>  Lobby menu (Ready / Start / Leave)     <b>E</b>  Use / Shop     <b>TAB</b>  Free / lock mouse";
        text.fontSize = 28;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(1f, 0.96f, 0.86f, 1f);
        text.raycastTarget = false;
        hintRoot.SetActive(false);
    }

    private void Update()
    {
        HandlePanelInput();
        ApplyPanelVisibility();

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

    /// <summary>My seat. In the 3D lobby the body is the local player and the seat is only owned.</summary>
    private RoHRoomPlayer FindLocal()
    {
        for (int i = 0; i < players.Count; i++)
            if (players[i] != null && players[i].isOwned) return players[i];

        NetworkIdentity id = NetworkClient.localPlayer;
        return id != null ? id.GetComponent<RoHRoomPlayer>() : null;
    }

    private void DrawRoomInfo(RoHRoomManager room)
    {
        RoomConfig config = LobbyController.Instance != null ? LobbyController.Instance.CurrentRoom : null;
        int limit = config != null ? config.playerLimit : room.RoomPlayerLimit;

        titleText.text = config != null && !string.IsNullOrEmpty(config.roomName) ? config.roomName : "Waiting Lobby";

        string map = RoomDisplay.MapName(config != null ? config.mapID : RoomConfig.DemoMapID);
        string difficulty = config != null ? RoomDisplay.Difficulty(config.difficulty) : "?";
        string privacy = config != null && config.isPrivate ? "Private" : "Public";

        infoText.text = $"{map}  |  {difficulty}  |  {privacy}  |  Players {players.Count} / {limit}";
    }

    private void DrawSeats(RoHRoomManager room, RoHRoomPlayer local)
    {
        bool isHost = NetworkServer.active;
        if (armedKickRow >= 0 && Time.unscaledTime > armedKickUntil) armedKickRow = -1;

        RoomConfig config = LobbyController.Instance != null ? LobbyController.Instance.CurrentRoom : null;
        int seats = Mathf.Clamp(config != null ? config.playerLimit : room.RoomPlayerLimit, 1, rows.Count);

        for (int i = 0; i < rows.Count; i++)
        {
            bool visible = i < seats;
            if (rows[i].gameObject.activeSelf != visible) rows[i].gameObject.SetActive(visible);
            if (!visible) continue;

            if (i < players.Count) rows[i].Bind(players[i], players[i] == local);
            else rows[i].BindEmpty();

            // Kick: host only, on guests only.
            RoHRoomPlayer seat = i < players.Count ? players[i] : null;
            if (rowSeats[i] != seat && armedKickRow == i) armedKickRow = -1;
            rowSeats[i] = seat;
            bool canKick = isHost && room.InRoomScene && seat != null && !seat.IsHost;
            if (kickButtons[i].gameObject.activeSelf != canKick) kickButtons[i].gameObject.SetActive(canKick);
            if (canKick) kickLabels[i].text = armedKickRow == i ? "Sure?" : "Kick";
        }
    }

    private void AddKickButton(LobbyPlayerRow row, int index)
    {
        var go = new GameObject("Kick Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(row.transform, false);
        go.transform.SetAsLastSibling();
        LayoutElement layout = go.GetComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = 120f;
        layout.flexibleWidth = 0f;
        layout.minHeight = layout.preferredHeight = 44f;
        go.GetComponent<Image>().color = new Color(0.45f, 0.1f, 0.08f, 1f);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "Kick";
        label.fontSize = 22f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 0.92f, 0.88f);
        label.raycastTarget = false;

        go.GetComponent<Button>().onClick.AddListener(() => OnKickClicked(index));
        go.SetActive(false);
        kickButtons.Add(go.GetComponent<Button>());
        kickLabels.Add(label);
    }

    private void OnKickClicked(int index)
    {
        RoHRoomManager room = RoHRoomManager.Instance;
        RoHRoomPlayer seat = index >= 0 && index < rowSeats.Length ? rowSeats[index] : null;
        if (room == null || seat == null) return;

        if (armedKickRow != index || Time.unscaledTime > armedKickUntil)
        {
            armedKickRow = index;              // first click: ask once more
            armedKickUntil = Time.unscaledTime + 3f;
            return;
        }
        armedKickRow = -1;
        room.KickPlayer(seat);
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
