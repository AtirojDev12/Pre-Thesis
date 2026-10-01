using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One player's end-of-round result. Worked out on the SERVER
/// (MatchDirector.ServerSendResult) and sent to that player only.
/// A plain struct so Mirror can send it in a TargetRpc.
/// </summary>
public struct MatchResult
{
    public byte outcome;          // (byte)PlayerOutcome
    public int downedCount;       // times this player went down
    public int tasksDone;         // this player's own correct sales
    public int currencyPerTask;   // 10 in the prototype
    public int currency;          // what this player earns this round
    public bool consolation;      // true = died, flat consolation prize
}

/// <summary>Where one player is in the round, for the results screen's player list.</summary>
public enum RoundPlayerState : byte
{
    Playing = 0,
    Escaped = 1,
    Dead = 2,
    Left = 3,
}

/// <summary>One row of the results screen's player list. Synced by MatchDirector.</summary>
public struct RoundPlayerEntry
{
    public uint netId;
    public string name;
    public byte state;   // (byte)RoundPlayerState
}

/// <summary>
/// PROTOTYPE RESULTS SCREEN (Mr.k, 29 Sep). Shown to a player when THEIR round
/// ends: they escaped, they died, or 07:00 came.
///
///   - ESCAPED / SURVIVED or YOU DIED
///   - Times downed
///   - Tasks done x 10 = currency, counted up from 0
///   - Died: the consolation prize (+10) with a sorry message
///   - Back to Lobby (host, when the whole round is over) / Main Menu
///   - PLAYERS list (30 Sep): who is still playing / escaped / dead / left.
///     Players whose round is over hear each other flat, like the lobby
///     (VoiceNetwork routes it); players still inside cannot hear them.
///   - Your own body is frozen where it stands (it used to fall through the map).
///
/// The currency is added to the save (SaveManager.Current) the moment this
/// opens, once. Built in code; lives in the gameplay scene only.
/// </summary>
public sealed class MatchResultsUI : MonoBehaviour
{
    private static MatchResultsUI instance;

    /// <summary>True while the results screen is open (pause menu and clock stay away).</summary>
    public static bool IsShowing => instance != null;

    private const float CountUpSeconds = 1.8f;

    private MatchResult result;
    private int savedBefore;
    private float openedAt;
    private TMP_Text currencyText;
    private TMP_Text statusText;
    private Button lobbyButton;
    private Button menuButton;
    private TMP_Text lobbyLabel;
    private bool leaving;
    private string lostItems = "";

    private const int MaxRows = 6;
    private readonly TMP_Text[] playerRows = new TMP_Text[MaxRows];
    private float nextListRefresh;

    private static readonly Color Gold = new Color(1f, 0.8f, 0.3f);
    private static readonly Color Good = new Color(0.45f, 1f, 0.55f);
    private static readonly Color Danger = new Color(1f, 0.35f, 0.3f);
    private static readonly Color TextColor = new Color(0.95f, 0.93f, 0.9f);
    private static readonly Color Muted = new Color(0.65f, 0.62f, 0.6f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>Open the results screen with this result (called on the owning player's machine).</summary>
    public static void Show(MatchResult result)
    {
        if (instance != null) return; // one result per player per round

        var go = new GameObject("Match Results");
        instance = go.AddComponent<MatchResultsUI>();
        instance.Open(result);
    }

    private void Open(MatchResult r)
    {
        result = r;
        openedAt = Time.unscaledTime;

        // Pay once, now, into the local save.
        savedBefore = SaveManager.Current != null ? SaveManager.Current.currency : 0;
        if (SaveManager.Current != null && r.currency > 0)
        {
            SaveManager.AddCurrency(r.currency);
            SaveManager.SaveToDisk();
        }

        // Died: the permanent items you brought into this match are lost (1 Oct).
        if (r.consolation) lostItems = LoseCarriedItems();

        // Your round is over: your body stays exactly where it is.
        if (PlayerHealth.LocalInstance != null) MatchDirector.FreezeBody(PlayerHealth.LocalInstance.gameObject);

        PersistentHUD.PushHidden();
        Build();
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        PersistentHUD.PopHidden();
        GameplayInput.Blocked = false;
    }

    /// <summary>Removes the permanent items brought into this match from the save. Returns their names.</summary>
    private static string LoseCarriedItems()
    {
        SaveData save = SaveManager.Current;
        if (save == null || save.permanentItems == null || PlayerInventory.LastLoadout.Count == 0) return "";

        var names = new System.Text.StringBuilder();
        foreach (string id in PlayerInventory.LastLoadout)
        {
            for (int i = 0; i < save.permanentItems.Count; i++)
            {
                PermanentItemData item = save.permanentItems[i];
                if (item == null || item.itemID != id || !item.isOwned) continue;
                item.isOwned = false;
                if (names.Length > 0) names.Append(", ");
                names.Append(ItemCatalog.DisplayName(id));
            }
        }
        PlayerInventory.LastLoadout.Clear();
        SaveManager.SaveToDisk();
        return names.ToString();
    }

    // ---- Layout --------------------------------------------------------------

    private void Build()
    {
        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31000; // under the pause menu's "leaving" cover (32000)
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        Image dim = NewImage("Dim", transform, new Color(0f, 0f, 0f, 0.93f));
        Stretch(dim.rectTransform);
        dim.raycastTarget = true; // blocks clicks to anything behind

        var card = NewImage("Card", transform, new Color(0.11f, 0.09f, 0.08f, 1f)).rectTransform;
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(1500f, 780f);

        // Left: this player's result. Right: everyone in the room.
        RectTransform left = Column(card, "Result", 0f, 0.56f);
        RectTransform right = Column(card, "Players", 0.56f, 1f);
        var divider = NewImage("Divider", card, new Color(1f, 1f, 1f, 0.08f)).rectTransform;
        divider.anchorMin = new Vector2(0.56f, 0f);
        divider.anchorMax = new Vector2(0.56f, 1f);
        divider.offsetMin = new Vector2(-1f, 150f);
        divider.offsetMax = new Vector2(1f, -40f);

        PlayerOutcome outcome = (PlayerOutcome)result.outcome;
        bool dead = outcome == PlayerOutcome.Dead;
        string heading = dead ? "YOU DIED" : outcome == PlayerOutcome.EscapedThroughGate ? "YOU ESCAPED" : "YOU SURVIVED";

        float y = 40f;
        Row(left, NewText("Heading", left, heading, 84f, dead ? Danger : Good, FontStyles.Bold), ref y, 110f);
        Row(left, NewText("Downed", left, $"Times downed: {result.downedCount}", 40f, TextColor, FontStyles.Normal), ref y, 60f);
        Row(left, NewText("Tasks", left, $"Tasks done: {result.tasksDone}", 40f, TextColor, FontStyles.Normal), ref y, 60f);

        if (dead)
        {
            TMP_Text sorry = NewText("Consolation", left,
                "We feel sorry for your loss.\nThis is your consolation prize." +
                (lostItems.Length > 0 ? $"\n<color=#FF594D>Lost: {lostItems}</color>" : ""), 30f, Muted, FontStyles.Italic);
            sorry.textWrappingMode = TextWrappingModes.Normal;
            Row(left, sorry, ref y, 100f);
        }
        else
        {
            Row(left, NewText("Formula", left,
                $"{result.tasksDone} tasks x {result.currencyPerTask} = {result.currency}", 34f, Muted, FontStyles.Normal), ref y, 100f);
        }

        currencyText = NewText("Currency", left, "+0", 60f, Gold, FontStyles.Bold);
        currencyText.textWrappingMode = TextWrappingModes.Normal;
        Row(left, currencyText, ref y, 150f);

        statusText = NewText("Status", left, "", 28f, Muted, FontStyles.Normal);
        Row(left, statusText, ref y, 50f);

        // Player list
        float py = 40f;
        Row(right, NewText("Players Title", right, "PLAYERS", 44f, Gold, FontStyles.Bold), ref py, 70f);
        TMP_Text hint = NewText("Voice Hint", right,
            "Players who have finished can talk to each other.", 24f, Muted, FontStyles.Italic);
        hint.textWrappingMode = TextWrappingModes.Normal;
        Row(right, hint, ref py, 60f);
        for (int i = 0; i < MaxRows; i++)
        {
            playerRows[i] = NewText("Player " + (i + 1), right, "", 32f, TextColor, FontStyles.Normal);
            playerRows[i].alignment = TextAlignmentOptions.MidlineLeft;
            playerRows[i].overflowMode = TextOverflowModes.Ellipsis;
            Row(right, playerRows[i], ref py, 58f);
        }

        // Buttons
        var row = new GameObject("Buttons", typeof(RectTransform)).GetComponent<RectTransform>();
        row.SetParent(card, false);
        row.anchorMin = new Vector2(0f, 0f);
        row.anchorMax = new Vector2(1f, 0f);
        row.pivot = new Vector2(0.5f, 0f);
        row.offsetMin = new Vector2(200f, 40f);
        row.offsetMax = new Vector2(-200f, 120f);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 40f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        lobbyButton = NewButton("Back To Lobby", row, "Back to Lobby", out lobbyLabel);
        menuButton = NewButton("Main Menu", row, "Main Menu", out _);
        lobbyButton.onClick.AddListener(BackToLobby);
        menuButton.onClick.AddListener(MainMenu);
    }

    // ---- Every frame -----------------------------------------------------------

    private void LateUpdate()
    {
        // Keep the mouse free and the player still while this is open.
        GameplayInput.Blocked = true;
        if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Currency counts up from 0.
        float t = Mathf.Clamp01((Time.unscaledTime - openedAt) / CountUpSeconds);
        float eased = 1f - (1f - t) * (1f - t);
        int shown = Mathf.RoundToInt(result.currency * eased);
        currencyText.text = t < 1f
            ? $"+{shown}"
            : $"+{result.currency}   (total {savedBefore} -> {savedBefore + result.currency})";

        // Buttons.
        MatchDirector director = MatchDirector.Instance;
        bool roundOver = director == null || director.Phase == MatchPhase.Ended;
        bool hosting = NetworkServer.active && NetworkClient.active;
        bool offline = NetworkMode.IsOffline;

        lobbyButton.gameObject.SetActive(!offline);
        lobbyButton.interactable = hosting && roundOver && !leaving;
        lobbyLabel.text = hosting ? "Back to Lobby" : "Host takes everyone to Lobby";

        // The host leaving early would end the match for everyone still playing.
        menuButton.interactable = !leaving && (!hosting || roundOver);

        statusText.text = roundOver
            ? (hosting ? "Round over." : "Round over. Waiting for the host...")
            : "Waiting for the other players...";

        if (Time.unscaledTime >= nextListRefresh)
        {
            nextListRefresh = Time.unscaledTime + 0.25f;
            RefreshPlayerList(director);
        }
    }

    private void RefreshPlayerList(MatchDirector director)
    {
        int count = director != null ? Mathf.Min(director.RoundPlayerCount, MaxRows) : 0;
        uint me = PlayerHealth.LocalInstance != null && PlayerHealth.LocalInstance.netIdentity != null
            ? PlayerHealth.LocalInstance.netIdentity.netId : 0u;

        for (int i = 0; i < MaxRows; i++)
        {
            TMP_Text row = playerRows[i];
            if (i >= count)
            {
                if (row.text.Length > 0) row.text = "";
                continue;
            }

            RoundPlayerEntry entry = director.RoundPlayerAt(i);
            string name = string.IsNullOrEmpty(entry.name) ? "Player " + (i + 1) : entry.name;
            if (entry.netId == me) name += " (you)";

            string label;
            string color;
            switch ((RoundPlayerState)entry.state)
            {
                case RoundPlayerState.Escaped: label = "ESCAPED"; color = "#73FF8C"; break;
                case RoundPlayerState.Dead:    label = "DEAD"; color = "#FF594D"; break;
                case RoundPlayerState.Left:    label = "LEFT THE GAME"; color = "#8C8A88"; break;
                default:                       label = "STILL PLAYING"; color = "#F2EDE6"; break;
            }

            string text = $"{name}   <color={color}>{label}</color>";
            if (row.text != text) row.text = text;
        }
    }

    // ---- Buttons ---------------------------------------------------------------

    private void BackToLobby()
    {
        RoHRoomManager room = RoHRoomManager.Instance;
        if (room == null || !NetworkServer.active || leaving) return;
        leaving = true;

        // The room is joinable / listed again, then Mirror swaps everyone back
        // to their lobby player (NetworkRoomManager handles the return).
        if (LobbyController.Instance != null) LobbyController.Instance.MarkRoomInProgress(false);
        room.ServerChangeScene(room.RoomScene);
    }

    private void MainMenu()
    {
        if (leaving) return;
        leaving = true;

        if (PauseMenuController.LeaveMatch()) return;

        // No pause menu (should not happen): leave the simple way.
        if (RoHRoomManager.Instance != null && (NetworkServer.active || NetworkClient.active))
            RoHRoomManager.Instance.LeaveSession();
        else
            SceneManager.LoadScene(0);
    }

    // ---- Small UI helpers -------------------------------------------------------

    /// <summary>A full-height column of the card, from x0 to x1 (0..1 of the card width).</summary>
    private static RectTransform Column(RectTransform card, string name, float x0, float x1)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(card, false);
        rt.anchorMin = new Vector2(x0, 0f);
        rt.anchorMax = new Vector2(x1, 1f);
        rt.offsetMin = new Vector2(0f, 140f); // leave room for the buttons
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static void Row(RectTransform card, TMP_Text text, ref float y, float height)
    {
        RectTransform rt = text.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(40f, -y - height);
        rt.offsetMax = new Vector2(-40f, -y);
        y += height;
    }

    private static TMP_Text NewText(string name, Transform parent, string text, float size, Color color, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Button NewButton(string name, Transform parent, string label, out TMP_Text text)
    {
        Image bg = NewImage(name, parent, new Color(0.18f, 0.14f, 0.12f, 1f));
        bg.raycastTarget = true;
        var button = bg.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.4f, 1.3f, 1.2f);
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        button.colors = colors;

        text = NewText("Label", bg.transform, label, 32f, TextColor, FontStyles.Bold);
        text.textWrappingMode = TextWrappingModes.Normal;
        Stretch(text.rectTransform);
        return button;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
