using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// LOBBY BOARD (2 Oct). The M panel as a board standing in the 3D lobby.
/// Walk up, look at it, press E:
///
///   Guest  : E on the big button       = Ready / Cancel ready
///   Host   : E on the big button       = Start (once every guest is Ready)
///   Host   : HOLD E 1.5 s on a name    = kick that player (a red bar fills)
///
/// Everything here is LOCAL (ILocalInteractable): a guest asks through their
/// own seat's Command (RoHRoomPlayer.SetReady), the host IS the server and
/// calls RoHRoomManager directly (StartMatch / KickPlayer). The board only
/// draws what RoHRoomManager already syncs, so it can never disagree with the
/// M panel.
///
/// The display, rows and button colliders are built in code at runtime.
/// Placed by Tools > Pre-Thesis > Build 3D Lobby (the editor tool also makes
/// the solid back panel and post, so no runtime-made primitive needs a material).
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyBoard : MonoBehaviour
{
    // Layout in canvas pixels (1000 px = 1 m). Big text: the retro filter eats small letters.
    private const float Width = 2200f, Height = 1650f, Margin = 70f;
    private const float RowTop = 280f, RowHeight = 120f, RowGap = 12f;
    private const float ButtonTop = 1220f, ButtonHeight = 190f;

    [SerializeField, Min(0.2f)] private float kickHoldSeconds = 1.5f;
    [Tooltip("How far you may stand from the board while holding E to kick.")]
    [SerializeField] private float kickRange = 4f;

    private static readonly Color BoardColor = new Color(0.03f, 0.025f, 0.025f, 1f);
    private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.07f);
    private static readonly Color RowLocal = new Color(0.85f, 0.64f, 0.25f, 0.25f);
    private static readonly Color RowLit = new Color(1f, 1f, 1f, 0.2f);
    private static readonly Color Gold = new Color(1f, 0.8f, 0.3f);
    private static readonly Color TextColor = new Color(1f, 0.97f, 0.9f);
    private static readonly Color Muted = new Color(0.7f, 0.68f, 0.65f);
    private static readonly Color Good = new Color(0.45f, 1f, 0.55f);
    private static readonly Color KickRed = new Color(0.9f, 0.15f, 0.12f, 0.85f);

    private readonly List<RoHRoomPlayer> players = new List<RoHRoomPlayer>(RoomConfig.MaxPlayers);
    private readonly LobbyBoardRow[] rows = new LobbyBoardRow[RoomConfig.MaxPlayers];
    private LobbyBoardButton button;
    private TMP_Text titleText, infoText, statusText, buttonText, hintText;
    private Image buttonImage;
    private float nextRefresh;

    // Kick hold (host only)
    private LobbyBoardRow holdRow;
    private RoHRoomPlayer holdSeat;
    private float holdStarted;

    private static bool IsHost => NetworkServer.active;
    private static RoHRoomManager Room => RoHRoomManager.Instance;

    private void Awake() => Build();

    private void Update()
    {
        UpdateKickHold();
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.2f;
        Refresh();
    }

    // ---- Queries used by the row / button components ------------------------

    internal RoHRoomPlayer SeatAt(int index) => index >= 0 && index < players.Count ? players[index] : null;

    internal RoHRoomPlayer LocalSeat()
    {
        for (int i = 0; i < players.Count; i++)
            if (players[i] != null && players[i].isOwned) return players[i];
        return null;
    }

    internal bool CanKick(RoHRoomPlayer seat) => IsHost && Room != null && Room.InRoomScene && seat != null && !seat.IsHost;

    internal string ButtonPrompt()
    {
        if (Room == null) return string.Empty;
        if (IsHost) return Room.AllGuestsReady() ? "[E] Start the match" : "Waiting for everyone to be Ready";
        RoHRoomPlayer me = LocalSeat();
        if (me == null) return string.Empty;
        return me.readyToBegin ? "[E] Cancel ready" : "[E] Ready";
    }

    internal float KickProgress(LobbyBoardRow row) =>
        row != null && row == holdRow ? Mathf.Clamp01((Time.unscaledTime - holdStarted) / kickHoldSeconds) : 0f;

    // ---- Actions -------------------------------------------------------------

    internal void PressButton()
    {
        RoHRoomManager room = Room;
        if (room == null || !room.InRoomScene) return;

        if (IsHost)
        {
            if (room.AllGuestsReady()) room.StartMatch();
        }
        else
        {
            RoHRoomPlayer me = LocalSeat();
            if (me != null) me.SetReady(!me.readyToBegin);
        }
        nextRefresh = 0f;
    }

    /// <summary>E pressed on a name: start the 1.5 s hold. Kept only while E stays down and you keep looking at it.</summary>
    internal void BeginKickHold(LobbyBoardRow row)
    {
        RoHRoomPlayer seat = row != null ? SeatAt(row.Index) : null;
        if (!CanKick(seat)) return;
        holdRow = row;
        holdSeat = seat;
        holdStarted = Time.unscaledTime;
    }

    private void UpdateKickHold()
    {
        if (holdRow == null) return;

        Keyboard keyboard = Keyboard.current;
        bool keep = keyboard != null && keyboard.eKey.isPressed && !GameplayInput.Blocked
            && CanKick(holdSeat) && SeatAt(holdRow.Index) == holdSeat && IsLookingAt(holdRow.Collider);
        if (!keep) { CancelKickHold(); return; }

        if (Time.unscaledTime - holdStarted < kickHoldSeconds) return;

        RoHRoomPlayer seat = holdSeat;
        CancelKickHold();
        if (Room != null) Room.KickPlayer(seat);
        nextRefresh = 0f;
    }

    private void CancelKickHold()
    {
        holdRow = null;
        holdSeat = null;
    }

    private bool IsLookingAt(Collider target)
    {
        PlayerHealth me = PlayerHealth.LocalInstance;
        Camera cam = me != null ? me.GetComponentInChildren<Camera>() : null;
        if (cam == null || target == null) return false;
        // Closest hit that is not part of my own body / held item.
        RaycastHit[] hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, kickRange, ~0, QueryTriggerInteraction.Ignore);
        Collider closest = null;
        float best = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.IsChildOf(me.transform) || hit.distance >= best) continue;
            best = hit.distance;
            closest = hit.collider;
        }
        return closest == target;
    }

    // ---- Drawing -------------------------------------------------------------

    private void Refresh()
    {
        RoHRoomManager room = Room;
        if (room == null)
        {
            titleText.text = "LOBBY";
            infoText.text = string.Empty;
            statusText.text = "No room. Start from the Main Menu.";
            for (int i = 0; i < rows.Length; i++) rows[i].Draw(null, false, false, 0);
            buttonText.text = string.Empty;
            return;
        }

        room.GetOrderedRoomPlayers(players);
        RoomConfig config = LobbyController.Instance != null ? LobbyController.Instance.CurrentRoom : null;
        int limit = config != null ? config.playerLimit : room.RoomPlayerLimit;

        titleText.text = config != null && !string.IsNullOrEmpty(config.roomName) ? config.roomName : "LOBBY";
        string map = RoomDisplay.MapName(config != null ? config.mapID : RoomConfig.DemoMapID);
        string difficulty = config != null ? RoomDisplay.Difficulty(config.difficulty) : "?";
        infoText.text = $"{map}  |  {difficulty}  |  Players {players.Count} / {limit}";

        RoHRoomPlayer me = LocalSeat();
        int seats = Mathf.Clamp(limit, 1, rows.Length);
        for (int i = 0; i < rows.Length; i++)
        {
            RoHRoomPlayer seat = i < players.Count ? players[i] : null;
            rows[i].Draw(seat, seat != null && seat == me, i < seats, i);
        }

        bool allReady = room.AllGuestsReady();
        if (IsHost)
        {
            buttonText.text = allReady ? "START" : "START  (waiting)";
            buttonImage.color = allReady ? new Color(0.55f, 0.4f, 0.1f, 1f) : new Color(0.22f, 0.2f, 0.19f, 1f);
            statusText.text = allReady ? "Everyone is ready." : "Waiting for players to get ready...";
            hintText.text = "Host: HOLD <color=#FFCC4D>E</color> on a name to kick";
        }
        else if (SpectatorSession.Active)
        {
            buttonText.text = "SPECTATING";
            buttonImage.color = new Color(0.22f, 0.2f, 0.19f, 1f);
            statusText.text = allReady ? "Everyone is ready." : "Waiting for players to get ready...";
            hintText.text = "You are watching (no body)";
        }
        else
        {
            bool ready = me != null && me.readyToBegin;
            buttonText.text = ready ? "CANCEL READY" : "READY";
            buttonImage.color = ready ? new Color(0.22f, 0.2f, 0.19f, 1f) : new Color(0.15f, 0.42f, 0.2f, 1f);
            statusText.text = ready ? "Ready. Waiting for the host..." : "Press E on READY when you are set.";
            hintText.text = "<color=#FFCC4D>M</color> opens the same menu anywhere";
        }
    }

    // ---- Build (runtime, code only) --------------------------------------------

    private void Build()
    {
        var display = new GameObject("Board Display", typeof(RectTransform), typeof(Canvas));
        display.transform.SetParent(transform, false);
        display.transform.localScale = Vector3.one * 0.001f;
        display.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var displayRt = (RectTransform)display.transform;
        displayRt.sizeDelta = new Vector2(Width, Height);

        Image bg = NewImage("Board", displayRt, BoardColor);
        Stretch(bg.rectTransform);

        titleText = NewText("Title", displayRt, "LOBBY", 110f, Gold, TextAlignmentOptions.Center);
        Place(titleText.rectTransform, 40f, 130f);
        infoText = NewText("Info", displayRt, "", 60f, Muted, TextAlignmentOptions.Center);
        Place(infoText.rectTransform, 175f, 80f);

        for (int i = 0; i < rows.Length; i++)
        {
            float top = RowTop + i * (RowHeight + RowGap);
            Image rowBg = NewImage("Row " + (i + 1), displayRt, RowColor);
            Place(rowBg.rectTransform, top, RowHeight);

            Image fill = NewImage("Kick Fill", rowBg.rectTransform, KickRed);
            RectTransform fillRt = fill.rectTransform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;

            TMP_Text name = NewText("Name", rowBg.rectTransform, "", 76f, TextColor, TextAlignmentOptions.MidlineLeft);
            name.overflowMode = TextOverflowModes.Ellipsis;
            Anchor(name.rectTransform, new Vector2(0f, 0f), new Vector2(0.68f, 1f), new Vector2(40f, 0f), Vector2.zero);
            TMP_Text state = NewText("State", rowBg.rectTransform, "", 66f, Muted, TextAlignmentOptions.MidlineRight);
            Anchor(state.rectTransform, new Vector2(0.68f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-40f, 0f));

            BoxCollider col = NewCollider("Row " + (i + 1) + " (hold E: kick)", top, RowHeight);
            LobbyBoardRow row = col.gameObject.AddComponent<LobbyBoardRow>();
            row.Init(this, i, col, rowBg, fillRt, name, state);
            rows[i] = row;
        }

        statusText = NewText("Status", displayRt, "", 58f, Muted, TextAlignmentOptions.Center);
        Place(statusText.rectTransform, ButtonTop - 120f, 90f);

        buttonImage = NewImage("Button", displayRt, new Color(0.15f, 0.42f, 0.2f, 1f));
        Place(buttonImage.rectTransform, ButtonTop, ButtonHeight, 380f);
        buttonText = NewText("Label", buttonImage.rectTransform, "READY", 96f, TextColor, TextAlignmentOptions.Center);
        Stretch(buttonText.rectTransform);
        BoxCollider buttonCol = NewCollider("Ready / Start Button (E)", ButtonTop, ButtonHeight, 380f);
        button = buttonCol.gameObject.AddComponent<LobbyBoardButton>();
        button.Init(this, buttonImage);

        hintText = NewText("Hint", displayRt, "", 52f, Muted, TextAlignmentOptions.Center);
        Place(hintText.rectTransform, ButtonTop + ButtonHeight + 25f, 80f);

        Refresh();
    }

    /// <summary>A thin collider just in front of the board face, matching a strip of the canvas.</summary>
    private BoxCollider NewCollider(string name, float topPx, float heightPx, float sidePx = Margin)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        float centerY = (Height * 0.5f - topPx - heightPx * 0.5f) * 0.001f;
        go.transform.localPosition = new Vector3(0f, centerY, -0.02f);
        BoxCollider col = go.AddComponent<BoxCollider>();
        col.size = new Vector3((Width - 2f * sidePx) * 0.001f, heightPx * 0.001f, 0.04f);
        return col;
    }

    private static void Place(RectTransform rt, float topPx, float heightPx, float sidePx = Margin)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(sidePx, -topPx - heightPx);
        rt.offsetMax = new Vector2(-sidePx, -topPx);
    }

    private static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text NewText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = FontStyles.Bold;
        tmp.characterSpacing = 4f;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        return tmp;
    }

    internal static Color RowBase(bool local) => local ? RowLocal : RowColor;
    internal static Color RowHighlight => RowLit;
    internal static Color ReadyColor => Good;
    internal static Color MutedColor => Muted;
}

/// <summary>One name on the lobby board. Host: hold E on it to kick.</summary>
public sealed class LobbyBoardRow : MonoBehaviour, ILocalInteractable, IInteractionHighlight
{
    private LobbyBoard board;
    private Image background;
    private RectTransform kickFill;
    private TMP_Text nameText, stateText;
    private bool highlighted, isLocal;

    public int Index { get; private set; }
    public Collider Collider { get; private set; }

    internal void Init(LobbyBoard owner, int index, Collider col, Image bg, RectTransform fill, TMP_Text name, TMP_Text state)
    {
        board = owner;
        Index = index;
        Collider = col;
        background = bg;
        kickFill = fill;
        nameText = name;
        stateText = state;
    }

    internal void Draw(RoHRoomPlayer seat, bool local, bool seatOpen, int index)
    {
        isLocal = local;
        background.gameObject.SetActive(seatOpen);
        if (seat == null)
        {
            nameText.text = seatOpen ? "Waiting for player..." : string.Empty;
            nameText.color = LobbyBoard.MutedColor;
            stateText.text = string.Empty;
        }
        else
        {
            nameText.text = seat.DisplayName + (local ? "  (you)" : string.Empty);
            nameText.color = Color.white;
            if (seat.IsHost) { stateText.text = "Host"; stateText.color = LobbyBoard.ReadyColor; }
            else
            {
                stateText.text = seat.readyToBegin ? "Ready" : "Not ready";
                stateText.color = seat.readyToBegin ? LobbyBoard.ReadyColor : LobbyBoard.MutedColor;
            }
        }
        ApplyColor();
    }

    private void Update()
    {
        // Red bar while the host holds E on this name.
        float progress = board != null ? board.KickProgress(this) : 0f;
        if (!Mathf.Approximately(kickFill.anchorMax.x, progress)) kickFill.anchorMax = new Vector2(progress, 1f);
    }

    private void ApplyColor() => background.color = highlighted ? LobbyBoard.RowHighlight : LobbyBoard.RowBase(isLocal);

    public string GetInteractionPrompt()
    {
        RoHRoomPlayer seat = board.SeatAt(Index);
        return seat != null ? "Hold [E] Kick " + seat.DisplayName : string.Empty;
    }

    public bool CanInteract() => board != null && board.CanKick(board.SeatAt(Index));
    public Transform GetTransform() => transform;
    public void Interact(GameObject interactor) => board.BeginKickHold(this);

    public void SetHighlighted(bool on)
    {
        highlighted = on;
        ApplyColor();
    }
}

/// <summary>The big Ready / Start button on the lobby board.</summary>
public sealed class LobbyBoardButton : MonoBehaviour, ILocalInteractable, IInteractionHighlight
{
    private LobbyBoard board;
    private Image image;

    internal void Init(LobbyBoard owner, Image buttonImage)
    {
        board = owner;
        image = buttonImage;
    }

    public string GetInteractionPrompt() => board.ButtonPrompt();
    public bool CanInteract() => board != null && RoHRoomManager.Instance != null && RoHRoomManager.Instance.InRoomScene;
    public Transform GetTransform() => transform;
    public void Interact(GameObject interactor) => board.PressButton();

    public void SetHighlighted(bool on)
    {
        // A little bigger while looked at.
        if (image != null) image.rectTransform.localScale = on ? new Vector3(1.04f, 1.08f, 1f) : Vector3.one;
    }
}
