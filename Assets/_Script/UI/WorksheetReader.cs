using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// 5 Nov 2026 (Mr.k): the WORK SHEET (item "worksheet" from the lobby Staff NPC) as a MEMO BOOK.
///
///   Work sheet selected in the hotbar + PRESS the read key once (Settings > Controls
///   "Read map / open work sheet", default Left Click) = the book opens. No holding.
///   LEFT page  = map, difficulty, players, tonight's goal, and the TOPIC list.
///   RIGHT page = the picked topic: Your goal / How to win / one page per zone / Staff tips.
///   Click a topic (or Up / Down, W / S). Mouse wheel scrolls a long page.
///   Close: Esc, Right Click, or the X button. The player stands still while it is open.
///
/// Pictures: each zone (and the goal page) can show a picture set in the map's MapGuide
/// asset (Resources/MapGuides/&lt;mapID&gt;). Empty = a drawn colour card.
///
/// LIVE and LIGHT: the numbers (map, difficulty, players, zone goal) are checked twice a
/// second only while the book is open, and the pages are rebuilt only when one changed.
/// No network traffic at all. Also shows the sheet in your hand (first person) while the
/// item is selected; the model is the first child of Resources/Items/WorldWorksheet.
/// Builds itself at start-up; no scene setup needed.
/// </summary>
public sealed class WorksheetReader : MonoBehaviour
{
    private const float RefreshSeconds = 0.5f;

    /// <summary>The book is open (the hotbar mouse wheel is off meanwhile).</summary>
    public static bool IsReading { get; private set; }

    private static WorksheetReader instance;

    // ---- Look ----
    private static readonly Color Ink = new Color(0.13f, 0.11f, 0.10f);
    private static readonly Color Muted = new Color(0.42f, 0.38f, 0.34f);
    private static readonly Color PaperRight = new Color(0.98f, 0.96f, 0.89f);
    private static readonly Color PaperLeft = new Color(0.95f, 0.92f, 0.84f);
    private static readonly Color CoverColor = new Color(0.42f, 0.08f, 0.09f);
    private static readonly Color Danger = new Color(0.68f, 0.07f, 0.07f);
    private static readonly Color Highlight = new Color(1f, 0.84f, 0.48f);
    private static readonly Color TopicIdle = new Color(1f, 1f, 1f, 0.35f);
    private static readonly Color CardBg = new Color(1f, 1f, 1f, 0.62f);
    private static readonly Color GoalColor = new Color(0.82f, 0.42f, 0.04f);
    private static readonly Color WinColor = new Color(0.13f, 0.52f, 0.24f);
    private static readonly Color TipsColor = new Color(0.36f, 0.34f, 0.32f);
    private static readonly Color[] ZoneColors =
    {
        new Color(0.16f, 0.38f, 0.74f), new Color(0.80f, 0.36f, 0.10f), new Color(0.48f, 0.24f, 0.66f),
        new Color(0.06f, 0.52f, 0.52f), new Color(0.70f, 0.18f, 0.40f), new Color(0.40f, 0.44f, 0.12f),
    };

    private enum Kind { Goal, Win, Zone, Tips }

    private struct Topic
    {
        public Kind kind;
        public int zone;
        public string badge, label, detail;
        public Color color;
    }

    // ---- UI ----
    private GameObject canvasRoot;
    private RectTransform topicList, pageContent;
    private ScrollRect pageScroll;
    private TextMeshProUGUI subText, goalText, pageNumber;
    private Sprite rounded, circle;
    private readonly List<Topic> topics = new List<Topic>();
    private readonly List<Image> topicBgs = new List<Image>();
    private readonly List<TextMeshProUGUI> topicLabels = new List<TextMeshProUGUI>();
    private int current;

    // ---- Live data ----
    private MapGuide guide;
    private string mapId = "";
    private DifficultyLevel difficulty;
    private int players = 1;
    private int required;
    private string signature = "";
    private float nextRefresh;

    // ---- In hand ----
    private Transform heldAnchor;
    private bool heldMissingWarned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsReading = false;
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null || Application.isBatchMode) return;
        var go = new GameObject("[Worksheet Reader]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<WorksheetReader>();
    }

    // =====================================================================================
    // Update
    // =====================================================================================

    private void Update()
    {
        bool holding = HoldingSheet();
        UpdateHeld(holding && !IsReading);

        if (IsReading)
        {
            if (!holding) { Close(); return; }
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            // Not E: the same E press would also reach the object you look at once the book closes.
            bool close = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
                         (mouse != null && mouse.rightButton.wasPressedThisFrame);
            if (close) { Close(); return; }
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) Select(current - 1);
                else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) Select(current + 1);
            }
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshSeconds;
                if (RefreshData()) Rebuild();
            }
            return;
        }

        if (holding && !GameplayInput.Blocked && Cursor.lockState == CursorLockMode.Locked &&
            !OverlayPanels.AnyOpen && !PauseMenuController.IsOpen && Keybinds.WasPressed(GameAction.ReadMap))
            Open();
    }

    /// <summary>The local player has the work sheet selected, and is alive and standing.</summary>
    private static bool HoldingSheet()
    {
        PlayerInventory inventory = PlayerInventory.Local;
        if (inventory == null) return false;
        InventorySlot held = inventory.HeldSlot;
        if (held.IsEmpty || held.spare || !ItemCatalog.IsWorksheet(held.itemId)) return false;
        PlayerHealth me = PlayerHealth.LocalInstance;
        return me == null || (!me.IsDead && !me.IsDowned);
    }

    private void Open()
    {
        EnsureUi();
        EnsureEventSystem();
        signature = "";
        RefreshData();
        Rebuild();
        canvasRoot.SetActive(true);
        IsReading = true;
        nextRefresh = Time.unscaledTime + RefreshSeconds;
        OverlayPanels.Opened();
        PersistentHUD.PushHidden();
        OverlayPanels.SetMouseForUi(true);
        ShowPage(current); // the layout needs the canvas active
    }

    private void Close()
    {
        if (!IsReading) return;
        IsReading = false;
        if (canvasRoot != null) canvasRoot.SetActive(false);
        OverlayPanels.Closed();
        PersistentHUD.PopHidden();
        if (PlayerHealth.LocalInstance != null) OverlayPanels.SetMouseForUi(false);
        else GameplayInput.Blocked = false;
    }

    private void OnDisable() => Close();

    // =====================================================================================
    // Data
    // =====================================================================================

    /// <summary>Reads map / difficulty / players / goal. True when something changed.</summary>
    private bool RefreshData()
    {
        MatchDirector match = MatchDirector.Instance;
        int need = 0;
        if (match != null && !PlayerInventory.InLobby)
        {
            mapId = match.MapID;
            difficulty = match.Difficulty;
            players = Mathf.Max(1, match.StartPlayerCount);
            need = match.ZonesRequired;
        }
        else
        {
            LobbyController lobby = LobbyController.Instance;
            RoomConfig room = lobby != null ? lobby.CurrentRoom : null;
            mapId = room != null && !string.IsNullOrEmpty(room.mapID) ? room.mapID : RoomConfig.DemoMapID;
            difficulty = room != null ? room.difficulty : DifficultyLevel.Normal;
            players = Mathf.Max(1, RoHRoomPlayer.All.Count);
        }
        string now = mapId + "|" + difficulty + "|" + players + "|" + need;
        if (now == signature) return false;
        signature = now;
        guide = MapGuide.For(mapId);
        required = need > 0 ? need : guide != null ? guide.ZonesToFinish(players) : 0;
        return true;
    }

    // =====================================================================================
    // Pages
    // =====================================================================================

    private void Rebuild()
    {
        topics.Clear();
        if (guide != null)
        {
            topics.Add(new Topic { kind = Kind.Goal, badge = "!", label = "Your goal", detail = "", color = GoalColor });
            topics.Add(new Topic { kind = Kind.Win, badge = "?", label = "How to win", detail = "", color = WinColor });
            for (int i = 0; i < guide.zones.Count; i++)
            {
                MapGuide.ZoneText z = guide.zones[i];
                int rules = z.rules != null ? z.rules.Count : 0;
                topics.Add(new Topic
                {
                    kind = Kind.Zone, zone = i, badge = (i + 1).ToString(), label = z.name,
                    detail = rules > 0 ? rules + " rules" : "", color = ZoneColors[i % ZoneColors.Length],
                });
            }
            if (!string.IsNullOrWhiteSpace(guide.generalTips))
                topics.Add(new Topic { kind = Kind.Tips, badge = "i", label = "Staff tips", detail = "", color = TipsColor });
        }

        subText.text = (guide != null ? guide.mapName : mapId) + "  ·  night shift\n" +
                       "Difficulty: <b>" + RoomDisplay.Difficulty(difficulty) + "</b>     Players: <b>" + players + "</b>";
        goalText.text = guide == null ? "<size=60%>No guide for this map yet.</size>"
            : "<size=55%><color=#5A4A2A>TONIGHT'S GOAL</color></size>\nFinish <color=#A00000>" + required + (required == 1 ? " zone" : " zones") + "</color>";

        // Topic buttons.
        for (int i = topicList.childCount - 1; i >= 0; i--)
        {
            GameObject old = topicList.GetChild(i).gameObject;
            old.SetActive(false);
            Destroy(old);
        }
        topicBgs.Clear();
        topicLabels.Clear();
        const float listHeight = 452f, spacing = 6f;
        float itemHeight = topics.Count > 0 ? Mathf.Clamp((listHeight - spacing * (topics.Count - 1)) / topics.Count, 38f, 60f) : 60f;
        for (int i = 0; i < topics.Count; i++) MakeTopicButton(i, itemHeight);

        current = Mathf.Clamp(current, 0, Mathf.Max(0, topics.Count - 1));
        if (IsReading) ShowPage(current);
    }

    private void Select(int index)
    {
        if (topics.Count == 0) return;
        int next = Mathf.Clamp(index, 0, topics.Count - 1);
        if (next == current) return;
        current = next;
        ShowPage(current);
    }

    private void ShowPage(int index)
    {
        for (int i = 0; i < topicBgs.Count; i++)
        {
            bool on = i == index;
            topicBgs[i].color = on ? Highlight : TopicIdle;
            topicLabels[i].fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
        }

        for (int i = pageContent.childCount - 1; i >= 0; i--)
        {
            GameObject old = pageContent.GetChild(i).gameObject;
            old.SetActive(false);
            Destroy(old);
        }
        pageNumber.text = topics.Count > 0 ? (index + 1) + " / " + topics.Count : "";
        if (guide == null || topics.Count == 0)
        {
            Heading("Work sheet", Ink);
            Body(pageContent, "There is no guide for the map \"" + mapId + "\" yet.", 28f, Muted);
        }
        else
        {
            Topic t = topics[index];
            switch (t.kind)
            {
                case Kind.Goal: GoalPage(t.color); break;
                case Kind.Win: WinPage(t.color); break;
                case Kind.Zone: ZonePage(t.zone, t.color); break;
                default: TipsPage(t.color); break;
            }
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(pageContent);
        pageScroll.verticalNormalizedPosition = 1f;
    }

    private void GoalPage(Color color)
    {
        Heading("Your goal tonight", color);
        Picture(guide.mapPicture, "MAP", guide.mapName, color);

        RectTransform goal = Card(Highlight, new Color(1f, 0.93f, 0.72f));
        Body(goal, "Finish <b><color=#A00000>" + required + (required == 1 ? " zone" : " zones") + "</color></b>", 46f, Ink, TextAlignmentOptions.Center);
        Body(goal, "Any " + required + " of the " + guide.zones.Count + " zones in this book.", 24f, Muted, TextAlignmentOptions.Center);

        Section("More players = more zones", color);
        PlayerTable();

        Section("When is a zone done?", color);
        Body(pageContent, "When every line on its <b>task board</b> is done. The board is inside the zone.", 27f, Ink);

        Section("Rules tonight", color);
        Body(pageContent, MapGuide.RulesTonight(difficulty), 27f, Ink);

        Body(pageContent, "<i>Pick a zone on the left to see where it is, what to do there and its rules.</i>", 24f, Muted);
    }

    private void WinPage(Color color)
    {
        Heading("How to win", color);
        List<string> steps = MapGuide.Lines(guide.howToWin);
        for (int i = 0; i < steps.Count; i++)
        {
            RectTransform card = Card(color, CardBg);
            Body(card, "<b>STEP " + (i + 1) + "</b>", 20f, color);
            Body(card, steps[i], 27f, Ink);
        }
    }

    private void ZonePage(int zoneIndex, Color color)
    {
        MapGuide.ZoneText z = guide.zones[zoneIndex];
        Heading((zoneIndex + 1) + ". " + z.name, color);
        Picture(z.picture, (zoneIndex + 1).ToString(), z.name, color);

        if (!string.IsNullOrWhiteSpace(z.where))
        {
            Section("Where", color);
            Body(pageContent, z.where.Trim(), 27f, Ink);
        }

        List<string> tasks = MapGuide.Lines(z.tasks);
        if (tasks.Count > 0)
        {
            Section("What to do", color);
            Bullets(pageContent, tasks, 27f, Ink);
        }

        int ruleCount = z.rules != null ? z.rules.Count : 0;
        if (ruleCount > 0)
        {
            Section("Rules: " + MapGuide.ActiveRulesText(z, difficulty), color);
            if (z.ActiveRules(difficulty) < ruleCount)
                Body(pageContent, "<i>You will not know which ones are on. Learn them all.</i>", 23f, Muted);
            else if (z.allRulesOn)
                Body(pageContent, "<i>Every rule here is on, on every difficulty.</i>", 23f, Muted);
            foreach (MapGuide.RuleText r in z.rules)
            {
                RectTransform card = Card(color, CardBg);
                Body(card, "<b>" + r.title + "</b>", 28f, Ink);
                if (!string.IsNullOrWhiteSpace(r.whatToDo))
                    Body(card, "<color=#146B2E><b>DO:</b></color>  " + r.whatToDo.Trim(), 26f, Ink);
                if (!string.IsNullOrWhiteSpace(r.ifYouBreakIt))
                    Body(card, "<b>IF YOU BREAK IT:</b>  " + r.ifYouBreakIt.Trim(), 24f, Danger);
            }
        }
        else
        {
            Section("Rules", color);
            Body(pageContent, "No rules in this zone.", 26f, Muted);
        }

        List<string> danger = MapGuide.Lines(z.otherDangers);
        if (danger.Count > 0)
        {
            Section("Watch out", Danger);
            RectTransform card = Card(Danger, new Color(1f, 0.88f, 0.86f, 0.75f));
            Bullets(card, danger, 25f, Ink);
        }
    }

    private void TipsPage(Color color)
    {
        Heading("Staff tips", color);
        Bullets(pageContent, MapGuide.Lines(guide.generalTips), 28f, Ink);
    }

    /// <summary>1..6 players and their zone goal. The room's player count is highlighted.</summary>
    private void PlayerTable()
    {
        var row = new GameObject("Player Table", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(pageContent, false);
        row.GetComponent<LayoutElement>().preferredHeight = 84f;
        HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = true;
        for (int p = 1; p <= 6; p++)
        {
            bool me = p == players;
            Image cell = NewImage("Cell " + p, row.transform, me ? Highlight : CardBg, rounded);
            int zones = guide.ZonesToFinish(p);
            TextMeshProUGUI label = NewText("Text", cell.transform, (p == 1 ? "1 player" : p + " players") +
                "\n<size=130%><b>" + zones + (zones == 1 ? " zone" : " zones") + "</b></size>", 19f, Ink, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, 4f);
            if (me) label.fontStyle = FontStyles.Bold;
        }
    }

    // =====================================================================================
    // Page building blocks (all go into the right page's scrolling column)
    // =====================================================================================

    private void Heading(string text, Color color)
    {
        Body(pageContent, "<b>" + text + "</b>", 44f, Ink);
        var bar = new GameObject("Underline", typeof(RectTransform), typeof(LayoutElement));
        bar.transform.SetParent(pageContent, false);
        bar.GetComponent<LayoutElement>().preferredHeight = 6f;
        Image line = NewImage("Line", bar.transform, color, rounded);
        RectTransform r = line.rectTransform;
        r.anchorMin = new Vector2(0f, 0f);
        r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 0.5f);
        r.sizeDelta = new Vector2(160f, 0f);
    }

    private void Section(string text, Color color)
    {
        TextMeshProUGUI t = Body(pageContent, text.ToUpperInvariant(), 22f, color);
        t.fontStyle = FontStyles.Bold;
        t.characterSpacing = 4f;
        t.margin = new Vector4(0f, 10f, 0f, 0f);
    }

    /// <summary>A picture (from the MapGuide), or a drawn colour card with a big mark.</summary>
    private void Picture(Sprite sprite, string mark, string caption, Color color)
    {
        var holder = new GameObject("Picture", typeof(RectTransform), typeof(LayoutElement));
        holder.transform.SetParent(pageContent, false);
        holder.GetComponent<LayoutElement>().preferredHeight = 250f;
        if (sprite != null)
        {
            Image frame = NewImage("Frame", holder.transform, new Color(0.15f, 0.12f, 0.1f), rounded);
            Stretch(frame.rectTransform, 0f);
            Image pic = NewImage("Image", frame.transform, Color.white, null);
            pic.sprite = sprite;
            pic.preserveAspect = true;
            Stretch(pic.rectTransform, 6f);
            return;
        }
        Image card = NewImage("Card", holder.transform, color, rounded);
        Stretch(card.rectTransform, 0f);
        TextMeshProUGUI big = NewText("Mark", card.transform, mark, 150f, new Color(1f, 1f, 1f, 0.28f), TextAlignmentOptions.Center);
        big.fontStyle = FontStyles.Bold;
        Stretch(big.rectTransform, 0f);
        TextMeshProUGUI cap = NewText("Caption", card.transform, caption.ToUpperInvariant(), 38f, Color.white, TextAlignmentOptions.Center);
        cap.fontStyle = FontStyles.Bold;
        cap.characterSpacing = 6f;
        Stretch(cap.rectTransform, 0f);
    }

    /// <summary>A rounded card with a colour bar on the left. Returns where to put its text.</summary>
    private RectTransform Card(Color bar, Color background)
    {
        Image card = NewImage("Card", pageContent, background, rounded);
        card.gameObject.AddComponent<LayoutElement>();
        VerticalLayoutGroup v = card.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(30, 20, 14, 16);
        v.spacing = 6f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        Image stripe = NewImage("Bar", card.transform, bar, rounded);
        stripe.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        RectTransform r = stripe.rectTransform;
        r.anchorMin = new Vector2(0f, 0f);
        r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 0.5f);
        r.anchoredPosition = new Vector2(8f, 0f);
        r.sizeDelta = new Vector2(8f, -16f);
        return card.rectTransform;
    }

    private void Bullets(RectTransform parent, List<string> lines, float size, Color color)
    {
        foreach (string line in lines)
            Body(parent, "•<indent=1.1em>" + line + "</indent>", size, color);
    }

    private TextMeshProUGUI Body(Transform parent, string text, float size, Color color,
        TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
    {
        TextMeshProUGUI t = NewText("Text", parent, text, size, color, align);
        t.textWrappingMode = TextWrappingModes.Normal;
        t.lineSpacing = 6f;
        return t;
    }

    // =====================================================================================
    // Book (built once)
    // =====================================================================================

    private void MakeTopicButton(int index, float height)
    {
        Topic t = topics[index];
        Image bg = NewImage("Topic " + (index + 1), topicList, TopicIdle, rounded);
        bg.raycastTarget = true;
        bg.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        Button button = bg.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.9f, 0.86f, 0.76f);
        colors.pressedColor = new Color(0.8f, 0.74f, 0.62f);
        colors.selectedColor = Color.white;
        button.colors = colors;
        button.targetGraphic = bg;
        int captured = index;
        button.onClick.AddListener(() => Select(captured));

        float dot = Mathf.Min(42f, height - 12f);
        Image badge = NewImage("Badge", bg.transform, t.color, circle);
        RectTransform b = badge.rectTransform;
        b.anchorMin = b.anchorMax = new Vector2(0f, 0.5f);
        b.pivot = new Vector2(0f, 0.5f);
        b.anchoredPosition = new Vector2(12f, 0f);
        b.sizeDelta = new Vector2(dot, dot);
        TextMeshProUGUI mark = NewText("Mark", badge.transform, t.badge, dot * 0.55f, Color.white, TextAlignmentOptions.Center);
        mark.fontStyle = FontStyles.Bold;
        Stretch(mark.rectTransform, 0f);

        TextMeshProUGUI label = NewText("Label", bg.transform, t.label, height >= 54f ? 27f : 23f, Ink, TextAlignmentOptions.MidlineLeft);
        RectTransform l = label.rectTransform;
        l.anchorMin = Vector2.zero;
        l.anchorMax = Vector2.one;
        l.offsetMin = new Vector2(24f + dot, 0f);
        l.offsetMax = new Vector2(-90f, 0f);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;

        TextMeshProUGUI detail = NewText("Detail", bg.transform, t.detail, 19f, Muted, TextAlignmentOptions.MidlineRight);
        RectTransform d = detail.rectTransform;
        d.anchorMin = Vector2.zero;
        d.anchorMax = Vector2.one;
        d.offsetMin = Vector2.zero;
        d.offsetMax = new Vector2(-14f, 0f);

        topicBgs.Add(bg);
        topicLabels.Add(label);
    }

    private void EnsureUi()
    {
        if (canvasRoot != null) return;
        rounded = MakeRoundedSprite(64, 18);
        circle = MakeRoundedSprite(64, 32);

        canvasRoot = new GameObject("Worksheet Book", typeof(RectTransform));
        canvasRoot.transform.SetParent(transform, false);
        Canvas canvas = canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 450;
        CanvasScaler scaler = canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRoot.AddComponent<GraphicRaycaster>();

        Image dim = NewImage("Dim", canvasRoot.transform, new Color(0f, 0f, 0f, 0.6f), null);
        dim.raycastTarget = true;
        Stretch(dim.rectTransform, 0f);

        // Cover.
        Image cover = NewImage("Cover", canvasRoot.transform, CoverColor, rounded);
        RectTransform book = cover.rectTransform;
        book.anchorMin = book.anchorMax = new Vector2(0.5f, 0.5f);
        book.sizeDelta = new Vector2(1560f, 900f);
        book.anchoredPosition = new Vector2(0f, 6f);

        const float edge = 24f, leftWidth = 560f, gap = 56f;
        float pageHeight = 900f - edge * 2f;

        // ---- Left page ----
        Image left = NewImage("Left Page", book, PaperLeft, rounded);
        RectTransform lp = left.rectTransform;
        lp.anchorMin = lp.anchorMax = lp.pivot = new Vector2(0f, 1f);
        lp.anchoredPosition = new Vector2(edge, -edge);
        lp.sizeDelta = new Vector2(leftWidth, pageHeight);

        TextMeshProUGUI title = NewText("Title", lp, "<b>WORK SHEET</b>", 46f, Ink, TextAlignmentOptions.TopLeft);
        Place(title.rectTransform, 36f, 26f, -36f, 60f);
        title.characterSpacing = 3f;
        subText = NewText("Sub", lp, "", 23f, Muted, TextAlignmentOptions.TopLeft);
        Place(subText.rectTransform, 36f, 86f, -36f, 66f);

        Image goalCard = NewImage("Goal", lp, new Color(1f, 0.87f, 0.5f), rounded);
        Place(goalCard.rectTransform, 30f, 160f, -30f, 112f);
        goalText = NewText("Goal Text", goalCard.transform, "", 44f, Ink, TextAlignmentOptions.Center);
        goalText.fontStyle = FontStyles.Bold;
        Stretch(goalText.rectTransform, 6f);

        TextMeshProUGUI topicsLabel = NewText("Topics Label", lp, "TOPICS", 20f, Muted, TextAlignmentOptions.TopLeft);
        topicsLabel.fontStyle = FontStyles.Bold;
        topicsLabel.characterSpacing = 6f;
        Place(topicsLabel.rectTransform, 36f, 290f, -36f, 28f);

        var list = new GameObject("Topics", typeof(RectTransform), typeof(VerticalLayoutGroup));
        list.transform.SetParent(lp, false);
        topicList = (RectTransform)list.transform;
        Place(topicList, 24f, 324f, -24f, 452f);
        VerticalLayoutGroup v = list.GetComponent<VerticalLayoutGroup>();
        v.spacing = 6f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperLeft;

        TextMeshProUGUI help = NewText("Help", lp, "Click a topic   ·   Up / Down = next topic\nEsc or Right Click = close",
            19f, Muted, TextAlignmentOptions.Bottom);
        RectTransform hr = help.rectTransform;
        hr.anchorMin = new Vector2(0f, 0f);
        hr.anchorMax = new Vector2(1f, 0f);
        hr.pivot = new Vector2(0.5f, 0f);
        hr.anchoredPosition = new Vector2(0f, 16f);
        hr.sizeDelta = new Vector2(-48f, 56f);

        // ---- Spiral rings in the gap ----
        float ringX = edge + leftWidth + gap * 0.5f;
        for (int i = 0; i < 12; i++)
        {
            float y = -edge - 40f - i * ((pageHeight - 80f) / 11f);
            Image wire = NewImage("Ring " + (i + 1), book, new Color(0.72f, 0.72f, 0.74f), rounded);
            RectTransform w = wire.rectTransform;
            w.anchorMin = w.anchorMax = new Vector2(0f, 1f);
            w.pivot = new Vector2(0.5f, 0.5f);
            w.anchoredPosition = new Vector2(ringX, y);
            w.sizeDelta = new Vector2(gap + 44f, 13f);
        }

        // ---- Right page (lined paper with a red margin) ----
        Image right = NewImage("Right Page", book, PaperRight, rounded);
        RectTransform rp = right.rectTransform;
        rp.anchorMin = new Vector2(0f, 0f);
        rp.anchorMax = new Vector2(1f, 1f);
        rp.offsetMin = new Vector2(edge + leftWidth + gap, edge);
        rp.offsetMax = new Vector2(-edge, -edge);
        for (float y = 96f; y < pageHeight - 20f; y += 46f)
        {
            Image line = NewImage("Line", rp, new Color(0.25f, 0.42f, 0.8f, 0.10f), null);
            RectTransform r = line.rectTransform;
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -y);
            r.sizeDelta = new Vector2(0f, 2f);
        }
        Image margin = NewImage("Margin", rp, new Color(0.85f, 0.2f, 0.2f, 0.35f), null);
        RectTransform m = margin.rectTransform;
        m.anchorMin = new Vector2(0f, 0f);
        m.anchorMax = new Vector2(0f, 1f);
        m.pivot = new Vector2(0f, 0.5f);
        m.anchoredPosition = new Vector2(70f, 0f);
        m.sizeDelta = new Vector2(2f, 0f);

        // Scrolling column.
        var view = new GameObject("View", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
        view.transform.SetParent(rp, false);
        view.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f); // catches the mouse wheel
        RectTransform viewport = (RectTransform)view.transform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(96f, 76f);
        viewport.offsetMax = new Vector2(-44f, -30f);

        var column = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        column.transform.SetParent(viewport, false);
        pageContent = (RectTransform)column.transform;
        pageContent.anchorMin = new Vector2(0f, 1f);
        pageContent.anchorMax = new Vector2(1f, 1f);
        pageContent.pivot = new Vector2(0.5f, 1f);
        pageContent.sizeDelta = Vector2.zero;
        VerticalLayoutGroup cv = column.GetComponent<VerticalLayoutGroup>();
        cv.spacing = 14f;
        cv.padding = new RectOffset(0, 10, 4, 24);
        cv.childControlWidth = cv.childControlHeight = true;
        cv.childForceExpandWidth = true;
        cv.childForceExpandHeight = false;
        column.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        pageScroll = view.AddComponent<ScrollRect>();
        pageScroll.viewport = viewport;
        pageScroll.content = pageContent;
        pageScroll.horizontal = false;
        pageScroll.vertical = true;
        pageScroll.movementType = ScrollRect.MovementType.Clamped;
        pageScroll.scrollSensitivity = 45f;

        // Footer: previous / page number / next.
        Button prev = FooterButton(rp, "< Previous", -190f);
        prev.onClick.AddListener(() => Select(current - 1));
        Button next = FooterButton(rp, "Next >", 190f);
        next.onClick.AddListener(() => Select(current + 1));
        pageNumber = NewText("Page", rp, "", 22f, Muted, TextAlignmentOptions.Center);
        RectTransform pn = pageNumber.rectTransform;
        pn.anchorMin = pn.anchorMax = new Vector2(0.5f, 0f);
        pn.pivot = new Vector2(0.5f, 0f);
        pn.anchoredPosition = new Vector2(0f, 22f);
        pn.sizeDelta = new Vector2(160f, 40f);

        // Close (X) on the cover corner.
        Image x = NewImage("Close", book, new Color(0.2f, 0.05f, 0.05f), circle);
        x.raycastTarget = true;
        RectTransform xr = x.rectTransform;
        xr.anchorMin = xr.anchorMax = new Vector2(1f, 1f);
        xr.pivot = new Vector2(0.5f, 0.5f);
        xr.anchoredPosition = new Vector2(-8f, -8f);
        xr.sizeDelta = new Vector2(54f, 54f);
        Button closeButton = x.gameObject.AddComponent<Button>();
        closeButton.targetGraphic = x;
        closeButton.onClick.AddListener(Close);
        TextMeshProUGUI xt = NewText("X", x.transform, "X", 28f, Color.white, TextAlignmentOptions.Center);
        xt.fontStyle = FontStyles.Bold;
        Stretch(xt.rectTransform, 0f);

        canvasRoot.SetActive(false);
    }

    private Button FooterButton(Transform parent, string label, float x)
    {
        Image bg = NewImage(label, parent, new Color(0.42f, 0.08f, 0.09f, 0.9f), rounded);
        bg.raycastTarget = true;
        RectTransform r = bg.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
        r.pivot = new Vector2(0.5f, 0f);
        r.anchoredPosition = new Vector2(x, 18f);
        r.sizeDelta = new Vector2(190f, 46f);
        Button button = bg.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        TextMeshProUGUI t = NewText("Text", bg.transform, label, 22f, Color.white, TextAlignmentOptions.Center);
        t.fontStyle = FontStyles.Bold;
        Stretch(t.rectTransform, 0f);
        return button;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    // =====================================================================================
    // In hand (first person, local only)
    // =====================================================================================

    private void UpdateHeld(bool show)
    {
        if (!show)
        {
            if (heldAnchor != null && heldAnchor.gameObject.activeSelf) heldAnchor.gameObject.SetActive(false);
            return;
        }
        if (heldAnchor == null && !BuildHeld()) return;
        if (!heldAnchor.gameObject.activeSelf) heldAnchor.gameObject.SetActive(true);
    }

    /// <summary>A copy of the model inside WorldWorksheet, in the lower right of the view.</summary>
    private bool BuildHeld()
    {
        PlayerInventory inventory = PlayerInventory.Local;
        Camera cam = inventory != null ? inventory.GetComponentInChildren<Camera>() : null;
        GameObject world = Resources.Load<GameObject>(ItemCatalog.WorldWorksheetPath);
        Transform source = world != null && world.transform.childCount > 0 ? world.transform.GetChild(0) : null;
        if (cam == null || source == null)
        {
            if (source == null && !heldMissingWarned)
                Debug.LogWarning("[WorksheetReader] No work sheet model. Run Tools > Pre-Thesis > One Step > Items: Work Sheet.");
            heldMissingWarned = true;
            return false;
        }

        heldAnchor = new GameObject("Held Work Sheet").transform;
        GameObject model = Instantiate(source.gameObject, heldAnchor, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (longest > 0.0001f) model.transform.localScale *= 0.2f / longest;
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            model.transform.position -= bounds.center - heldAnchor.position;
        }
        heldAnchor.SetParent(cam.transform, false);
        heldAnchor.localPosition = new Vector3(0.25f, -0.26f, 0.46f);
        heldAnchor.localRotation = Quaternion.Euler(38f, -18f, 0f);
        return true;
    }

    // =====================================================================================
    // Small UI helpers
    // =====================================================================================

    private static Image NewImage(string name, Transform parent, Color color, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        }
        return image;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    private static void Stretch(RectTransform r, float inset)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(inset, inset);
        r.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>Top-anchored block: left inset, distance from the top, right inset (negative), height.</summary>
    private static void Place(RectTransform r, float left, float top, float right, float height)
    {
        r.anchorMin = new Vector2(0f, 1f);
        r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(0.5f, 1f);
        r.offsetMin = new Vector2(left, -top - height);
        r.offsetMax = new Vector2(right, -top);
    }

    /// <summary>White rounded square made in code (no art asset needed), 9-sliced.</summary>
    private static Sprite MakeRoundedSprite(int size, int radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        int border = radius < size / 2 ? radius : 0;
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }
}
