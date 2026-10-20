using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// 20 Oct 2026 (Mr.k): the Tab screen (lobby and match).
///
///   - Every player in the room, host first by seat order.
///   - Ping of each player (the server measures it: RoHRoomPlayer.PingMs).
///   - A voice volume slider for each FRIEND (0% = muted, up to 200%).
///     Local to this PC only (FriendVoiceVolume), nobody else is affected.
///   - In the 3D lobby it is also the lobby menu (the old M key was removed):
///     room info, Ready / Cancel ready (guest), Start (host), Leave, Kick (host).
///     Same calls as WaitingLobbyController (RoHRoomManager does the work on the server).
///
/// Tab (Settings > Controls) opens / closes it. Esc closes it too.
/// While open the mouse is free and the player stands still (OverlayPanels).
///
/// Built in code at runtime: no scene or prefab change needed.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerListHUD : MonoBehaviour
{
    private static readonly Color PanelColor = new Color32(0x0E, 0x0B, 0x0A, 0xF0);
    private static readonly Color RowColor = new Color32(0x2E, 0x24, 0x20, 0xFF);
    private static readonly Color MyRowColor = new Color32(0x3E, 0x30, 0x1E, 0xFF);
    private static readonly Color AccentColor = new Color32(0xD9, 0xA4, 0x41, 0xFF);
    private static readonly Color TextColor = new Color32(0xF4, 0xEF, 0xED, 0xFF);
    private static readonly Color MutedColor = new Color32(0x9A, 0x8F, 0x8B, 0xFF);
    private static readonly Color GoodColor = new Color32(0x6C, 0xD0, 0x6A, 0xFF);
    private static readonly Color MidColor = new Color32(0xE8, 0xC1, 0x4A, 0xFF);
    private static readonly Color BadColor = new Color32(0xFF, 0x6A, 0x5A, 0xFF);
    private static readonly Color ButtonColor = new Color32(0x4A, 0x3B, 0x34, 0xFF);
    private static readonly Color KickColor = new Color32(0x73, 0x1A, 0x14, 0xFF);

    private const int MaxRows = RoomConfig.MaxPlayers;
    private const float PanelWidth = 1180f;
    private const float RowHeight = 64f;
    private const float RowStep = RowHeight + 8f;
    private const float RowsTop = -150f;

    private static PlayerListHUD instance;
    public static bool IsOpen => instance != null && instance.open;

    private sealed class Row
    {
        public GameObject root;
        public Image background;
        public TMP_Text name, ping, volumeText, kickLabel;
        public Slider slider;
        public Button kick;
        public string key;          // FriendVoiceVolume key shown in this row (null = own row / empty)
        public RoHRoomPlayer seat;
    }

    private Canvas canvas;
    private RectTransform panel;
    private TMP_Text title, infoText, emptyText, hintText, statusText;
    private GameObject lobbyBar;
    private Button readyButton, startButton, leaveButton;
    private TMP_Text readyLabel;
    private readonly Row[] rows = new Row[MaxRows];
    private readonly List<RoHRoomPlayer> seats = new List<RoHRoomPlayer>(MaxRows);
    private bool open;
    private float nextRefresh;
    private bool volumeChanged;
    private int armedKickRow = -1;
    private float armedKickUntil;
    private bool starting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("Player List HUD");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PlayerListHUD>();
    }

    private void Update()
    {
        if (!open)
        {
            if (Keybinds.WasPressed(GameAction.PlayerList) && CanOpen()) SetOpen(true);
            return;
        }

        // Close: same key again, Esc, or something more important took the screen.
        bool esc = UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
        bool walking = PlayerHealth.LocalInstance != null || SpectatorSession.Active;
        if (esc || Keybinds.WasPressed(GameAction.PlayerList) || PauseMenuController.IsOpen || MatchResultsUI.IsShowing
            || !NetworkClient.active || !walking)
        {
            SetOpen(false);
            return;
        }

        if (Time.unscaledTime >= nextRefresh) Refresh();
    }

    private static bool CanOpen()
    {
        if (!NetworkClient.active) return false;                          // online rooms only
        if (GameplayInput.Blocked || OverlayPanels.AnyOpen) return false; // another screen owns the mouse
        if (PauseMenuController.IsOpen || MatchResultsUI.IsShowing) return false;
        return PlayerHealth.LocalInstance != null || SpectatorSession.Active;
    }

    private void SetOpen(bool value)
    {
        if (open == value) return;
        open = value;
        if (canvas == null) Build();
        canvas.enabled = value;

        if (value)
        {
            armedKickRow = -1;
            starting = false;
            OverlayPanels.Opened();
            OverlayPanels.SetMouseForUi(true);
            Refresh();
        }
        else
        {
            OverlayPanels.Closed();
            if (PlayerHealth.LocalInstance != null || SpectatorSession.Active) OverlayPanels.SetMouseForUi(false);
            else GameplayInput.Blocked = false;
            if (volumeChanged) { GameSettings.Save(); volumeChanged = false; }
        }
    }

    private void OnDisable()
    {
        if (open) SetOpen(false);
    }

    // ---- Content ---------------------------------------------------------------------

    private static RoHRoomManager Room => RoHRoomManager.Instance;
    private static bool InLobby => Room != null && Room.InRoomScene;

    private void Refresh()
    {
        nextRefresh = Time.unscaledTime + 0.5f;
        bool lobby = InLobby;
        bool isHost = NetworkServer.active;
        if (armedKickRow >= 0 && Time.unscaledTime > armedKickUntil) armedKickRow = -1;

        seats.Clear();
        foreach (RoHRoomPlayer seat in RoHRoomPlayer.All)
            if (seat != null) seats.Add(seat);
        seats.Sort((a, b) => a.index.CompareTo(b.index));

        RoomConfig config = LobbyController.Instance != null ? LobbyController.Instance.CurrentRoom : null;
        int limit = config != null ? config.playerLimit : Room != null ? Room.RoomPlayerLimit : RoomConfig.MaxPlayers;
        title.text = lobby && config != null && !string.IsNullOrEmpty(config.roomName) ? SafeName(config.roomName).ToUpperInvariant() : "PLAYERS";
        string players = $"Players {seats.Count} / {limit}";
        infoText.text = lobby && config != null
            ? $"{RoomDisplay.MapName(config.mapID)}   |   {RoomDisplay.Difficulty(config.difficulty)}   |   {(config.isPrivate ? "Private" : "Public")}   |   {players}"
            : players;

        hintText.text = $"[{Keybinds.KeyName(GameAction.PlayerList)}] or [Esc] close   ·   Voice volume is only on your PC   ·   0% = muted";
        emptyText.gameObject.SetActive(seats.Count == 0);

        RoHRoomPlayer local = null;
        for (int i = 0; i < MaxRows; i++)
        {
            Row row = rows[i];
            if (i >= seats.Count) { row.root.SetActive(false); row.key = null; row.seat = null; continue; }
            row.root.SetActive(true);

            RoHRoomPlayer seat = seats[i];
            if (row.seat != seat && armedKickRow == i) armedKickRow = -1;
            row.seat = seat;
            bool me = seat.isOwned;
            if (me) local = seat;
            row.background.color = me ? MyRowColor : RowColor;

            string state = !lobby ? "" : seat.IsHost ? "  <color=#D9A441>(Host)</color>"
                : seat.readyToBegin ? "  <color=#6CD06A>Ready</color>" : "  <color=#9A8F8B>Not ready</color>";
            if (!lobby && seat.IsHost) state = "  <color=#D9A441>(Host)</color>";
            row.name.text = SafeName(seat.DisplayName) + state + (me ? "  <color=#9A8F8B>(You)</color>" : "");

            if (seat.IsHost) { row.ping.text = "Host"; row.ping.color = MutedColor; }
            else
            {
                int ms = seat.PingMs;
                row.ping.text = ms <= 0 ? "-- ms" : ms + " ms";
                row.ping.color = ms <= 0 ? MutedColor : ms < 80 ? GoodColor : ms < 150 ? MidColor : BadColor;
            }

            // Kick: host only, in the lobby, on guests only. First click = "Sure?".
            bool canKick = lobby && isHost && !seat.IsHost;
            if (row.kick.gameObject.activeSelf != canKick) row.kick.gameObject.SetActive(canKick);
            if (canKick) row.kickLabel.text = armedKickRow == i ? "Sure?" : "Kick";

            if (me)
            {
                row.key = null;
                row.slider.gameObject.SetActive(false);
                row.volumeText.text = "your voice";
                row.volumeText.color = MutedColor;
                continue;
            }

            string key = FriendVoiceVolume.KeyFor(seat);
            row.slider.gameObject.SetActive(true);
            if (key != row.key)
            {
                // New person in this row (or their EOS id arrived): show their saved volume.
                row.key = key;
                row.slider.SetValueWithoutNotify(FriendVoiceVolume.Get(key));
            }
            ShowVolume(row);
        }

        // Lobby menu (was M): Ready / Start / Leave + status line.
        lobbyBar.SetActive(lobby);
        if (lobby)
        {
            bool canStart = isHost && Room != null && Room.AllGuestsReady();
            readyButton.gameObject.SetActive(!isHost && local != null);
            if (local != null) readyLabel.text = local.readyToBegin ? "Cancel ready" : "Ready";
            startButton.gameObject.SetActive(isHost);
            startButton.interactable = canStart && !starting;

            if (starting) statusText.text = "Starting...";
            else if (isHost) statusText.text = canStart ? "Everyone is ready. Press Start." : "Waiting for players to get ready...";
            else if (local != null && local.readyToBegin) statusText.text = "Ready. Waiting for the host to start...";
            else statusText.text = "Press Ready when you are set.";
        }

        float height = -RowsTop + Mathf.Max(1, seats.Count) * RowStep + (lobby ? 140f : 60f);
        panel.sizeDelta = new Vector2(PanelWidth, height);
    }

    private void OnSlider(int index, float value)
    {
        Row row = rows[index];
        if (string.IsNullOrEmpty(row.key)) return;
        FriendVoiceVolume.Set(row.key, value);
        volumeChanged = true;
        ShowVolume(row);
    }

    private static void ShowVolume(Row row)
    {
        int percent = Mathf.RoundToInt(row.slider.value * 100f);
        row.volumeText.text = percent == 0 ? "Muted" : percent + "%";
        row.volumeText.color = percent == 0 ? BadColor : TextColor;
    }

    private void OnKick(int index)
    {
        RoHRoomPlayer seat = rows[index].seat;
        if (Room == null || seat == null || !NetworkServer.active) return;
        if (armedKickRow != index || Time.unscaledTime > armedKickUntil)
        {
            armedKickRow = index;              // first click: ask once more
            armedKickUntil = Time.unscaledTime + 3f;
        }
        else
        {
            armedKickRow = -1;
            Room.KickPlayer(seat);
        }
        Refresh();
    }

    private void OnReady()
    {
        foreach (RoHRoomPlayer seat in RoHRoomPlayer.All)
            if (seat != null && seat.isOwned) { seat.SetReady(!seat.readyToBegin); break; }
        nextRefresh = Time.unscaledTime + 0.15f; // show the answer from the server soon
    }

    private void OnStart()
    {
        if (Room != null && Room.StartMatch()) starting = true;
        Refresh();
    }

    private void OnLeave()
    {
        leaveButton.interactable = false;
        SetOpen(false);
        if (Room != null) Room.LeaveSession();
    }

    // Names are typed by players: stop them from being read as rich-text tags.
    private static string SafeName(string name) => string.IsNullOrEmpty(name) ? "?" : name.Replace("<", "<​");

    // ---- UI (built once, in code) --------------------------------------------------------

    private void Build()
    {
        EnsureEventSystem();

        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 450; // above the HUD, below the Esc menu
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        panel = NewRect("Panel", transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(PanelWidth, 400f);
        panel.gameObject.AddComponent<Image>().color = PanelColor;

        title = NewText("Title", panel, "PLAYERS", 34f, AccentColor, TextAlignmentOptions.MidlineLeft);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -66f), new Vector2(-32f, -18f));
        infoText = NewText("Info", panel, string.Empty, 22f, TextColor, TextAlignmentOptions.MidlineLeft);
        Place(infoText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -100f), new Vector2(-32f, -68f));

        Header("Head Name", "NAME", 48f, 470f);
        Header("Head Ping", "PING", 520f, 640f);
        Header("Head Volume", "VOICE VOLUME (ON YOUR PC)", 650f, 1030f);

        for (int i = 0; i < MaxRows; i++) rows[i] = BuildRow(i);

        emptyText = NewText("Empty", panel, "Nobody here yet.", 24f, MutedColor, TextAlignmentOptions.Center);
        Place(emptyText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, RowsTop - RowHeight), new Vector2(-32f, RowsTop));

        // Lobby bar (bottom, above the hint): status + Ready / Start / Leave.
        RectTransform bar = NewRect("Lobby Bar", panel);
        Place(bar, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(32f, 56f), new Vector2(-32f, 120f));
        lobbyBar = bar.gameObject;
        statusText = NewText("Status", bar, string.Empty, 22f, TextColor, TextAlignmentOptions.MidlineLeft);
        Place(statusText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(560f, 0f));
        readyButton = NewButton("Ready Button", bar, "Ready", AccentColor, Color.black, out readyLabel);
        Place((RectTransform)readyButton.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(620f, 4f), new Vector2(840f, -4f));
        readyButton.onClick.AddListener(OnReady);
        startButton = NewButton("Start Button", bar, "Start", AccentColor, Color.black, out _);
        Place((RectTransform)startButton.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(620f, 4f), new Vector2(840f, -4f));
        startButton.onClick.AddListener(OnStart);
        leaveButton = NewButton("Leave Button", bar, "Leave", ButtonColor, TextColor, out _);
        Place((RectTransform)leaveButton.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(866f, 4f), new Vector2(1086f, -4f));
        leaveButton.onClick.AddListener(OnLeave);

        hintText = NewText("Hint", panel, string.Empty, 18f, MutedColor, TextAlignmentOptions.Center);
        Place(hintText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(24f, 12f), new Vector2(-24f, 44f));

        canvas.enabled = false;
    }

    private void Header(string name, string text, float left, float right)
    {
        TMP_Text t = NewText(name, panel, text, 20f, MutedColor, TextAlignmentOptions.MidlineLeft);
        Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, RowsTop + 4f), new Vector2(right, RowsTop + 36f));
    }

    private Row BuildRow(int index)
    {
        var row = new Row();
        RectTransform rt = NewRect("Row " + (index + 1), panel);
        float top = RowsTop - index * RowStep;
        Place(rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, top - RowHeight), new Vector2(-32f, top));
        row.background = rt.gameObject.AddComponent<Image>();
        row.background.color = RowColor;
        row.root = rt.gameObject;

        row.name = NewText("Name", rt, string.Empty, 26f, TextColor, TextAlignmentOptions.MidlineLeft);
        row.name.richText = true;
        row.name.textWrappingMode = TextWrappingModes.NoWrap;
        row.name.overflowMode = TextOverflowModes.Ellipsis;
        Place(row.name.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(16f, 0f), new Vector2(470f, 0f));

        row.ping = NewText("Ping", rt, string.Empty, 24f, TextColor, TextAlignmentOptions.MidlineLeft);
        Place(row.ping.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(488f, 0f), new Vector2(608f, 0f));

        row.slider = NewSlider(rt);
        Place((RectTransform)row.slider.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(626f, -12f), new Vector2(876f, 12f));
        int captured = index;
        row.slider.onValueChanged.AddListener(v => OnSlider(captured, v));

        row.volumeText = NewText("Volume", rt, string.Empty, 22f, TextColor, TextAlignmentOptions.MidlineRight);
        Place(row.volumeText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(884f, 0f), new Vector2(980f, 0f));

        row.kick = NewButton("Kick Button", rt, "Kick", KickColor, new Color(1f, 0.92f, 0.88f), out row.kickLabel);
        Place((RectTransform)row.kick.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(996f, 10f), new Vector2(1104f, -10f));
        row.kick.onClick.AddListener(() => OnKick(captured));
        row.kick.gameObject.SetActive(false);

        row.root.SetActive(false);
        return row;
    }

    private static Button NewButton(string name, Transform parent, string text, Color color, Color textColor, out TMP_Text label)
    {
        RectTransform rt = NewRect(name, parent);
        Image image = rt.gameObject.AddComponent<Image>();
        image.color = color;
        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        label = NewText("Label", rt, text, 22f, textColor, TextAlignmentOptions.Center);
        label.fontStyle = FontStyles.Bold;
        Place(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return button;
    }

    private static Slider NewSlider(Transform parent)
    {
        GameObject go = DefaultControls.CreateSlider(new DefaultControls.Resources());
        go.name = "Volume Slider";
        go.transform.SetParent(parent, false);
        Slider slider = go.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = FriendVoiceVolume.Max;
        slider.wholeNumbers = false;
        slider.value = 1f;

        // Colour it like the menus (DefaultControls makes plain white parts).
        foreach (Image image in go.GetComponentsInChildren<Image>(true))
        {
            switch (image.gameObject.name)
            {
                case "Background": image.color = new Color32(0x1A, 0x14, 0x12, 0xFF); break;
                case "Fill": image.color = AccentColor; break;
                case "Handle": image.color = TextColor; break;
            }
        }
        return slider;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static TMP_Text NewText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        RectTransform rt = NewRect(name, parent);
        TextMeshProUGUI tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        tmp.richText = false;
        return tmp;
    }

    private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(go);
    }
}
