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
///   25 Nov: ONE page of pixel-art paper. The pages are picked with the POST-ITS on the
///   right edge: Your goal / How to win / one page per zone / Staff tips.
///   Click a post-it (or Up / Down, W / S, or the arrows at the bottom). Mouse wheel scrolls a long page.
///   Close: Esc, Right Click, or the X post-it. The player stands still while it is open.
///   From 00:00 your own Memo Book shows only the live page: bloody paper, no post-its, no arrows.
///   The book itself is a PREFAB the team can edit: Resources/UI/Memo Book (MemoBookView,
///   Tools > Pre-Thesis > Designer Settings > Memo Book Prefab). Colours: Resources/Tuning/MemoBookLook.
///
/// Pictures on the pages: each zone (and the goal page) can show a picture set in the map's
/// MapGuide asset (Resources/MapGuides/&lt;mapID&gt;). Empty = a drawn colour card.
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
    private static Color Ink => MemoBookLook.Current.ink;
    private static readonly Color Muted = new Color(0.42f, 0.38f, 0.34f);
    private static readonly Color Danger = new Color(0.68f, 0.07f, 0.07f);
    private static readonly Color Highlight = new Color(1f, 0.84f, 0.48f);
    private static readonly Color CardBg = new Color(1f, 1f, 1f, 0.62f);
    private static readonly Color GoalColor = new Color(0.82f, 0.42f, 0.04f);
    private static readonly Color WinColor = new Color(0.13f, 0.52f, 0.24f);
    private static readonly Color TipsColor = new Color(0.36f, 0.34f, 0.32f);
    private static readonly Color[] ZoneColors =
    {
        new Color(0.16f, 0.38f, 0.74f), new Color(0.80f, 0.36f, 0.10f), new Color(0.48f, 0.24f, 0.66f),
        new Color(0.06f, 0.52f, 0.52f), new Color(0.70f, 0.18f, 0.40f), new Color(0.40f, 0.44f, 0.12f),
    };

    private enum Kind { Goal, Win, Zone, Tips, Live }

    // ---- 17 Nov (Task 8): the LIVE guide (own Memo Book, its map, from 00:00) ----
    private bool live;
    private string liveKey = "";

    private struct Topic
    {
        public Kind kind;
        public int zone;
        public string badge, label, detail;
        public Color color;
    }

    // ---- UI ----
    private GameObject canvasRoot;
    private MemoBookView view;
    private RectTransform pageContent, viewport;
    private ScrollRect pageScroll;
    private Sprite rounded;
    private readonly List<Topic> topics = new List<Topic>();
    // 25 Nov: the post-its of the prefab (their place and colour as saved), and the ones in use now.
    private readonly List<Vector2> postItHome = new List<Vector2>();
    private readonly List<Color> postItColor = new List<Color>();
    private readonly List<string> postItWords = new List<string>(); // the words written on each post-it in the prefab
    private readonly List<Image> tabs = new List<Image>();
    private readonly List<Vector2> tabHome = new List<Vector2>();
    private readonly List<Color> tabColors = new List<Color>();
    private readonly List<GameObject> extraTabs = new List<GameObject>();
    private Vector2 viewAnchorMin, viewAnchorMax, viewPivot, viewPosition, viewSize;
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
                if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame ||
                    keyboard.leftArrowKey.wasPressedThisFrame) Select(current - 1);
                else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame ||
                         keyboard.rightArrowKey.wasPressedThisFrame) Select(current + 1);
            }
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshSeconds;
                bool nowLive = MemoGuide.LocalLive();
                if (RefreshData() || nowLive != live) { live = nowLive; Rebuild(); }
                else if (live)
                {
                    // The step can change.
                    MemoGuide.LocalStep(out string key, out _, out _);
                    if (key != liveKey) ShowPage(current);
                }
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
        live = MemoGuide.LocalLive();
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
        if (live)
        {
            // 17 Nov (Task 8): one live page. No topics to pick.
            topics.Add(new Topic { kind = Kind.Live, badge = "!", label = MemoGuideTexts.Current.liveTitle, detail = "", color = GoalColor });
        }
        else if (guide != null)
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

        // 25 Nov: the live page has bloody paper, no post-its and no arrows (only the X post-it).
        MemoBookLook look = MemoBookLook.Current;
        // 25 Nov: each page takes the colour of its post-it.
        if (!live && look.pageColors != null && look.pageColors.Length > 0)
            for (int i = 0; i < topics.Count; i++)
            {
                Topic t = topics[i];
                t.color = look.pageColors[i % look.pageColors.Length];
                topics[i] = t;
            }
        if (view.livePaper != null)
        {
            view.livePaper.gameObject.SetActive(live);
            if (view.paper != null) view.paper.gameObject.SetActive(!live);
        }
        if (view.previous != null) view.previous.gameObject.SetActive(!live);
        if (view.next != null) view.next.gameObject.SetActive(!live);
        if (view.pageNumber != null) view.pageNumber.gameObject.SetActive(!live);
        if (live && view.liveTextArea != null) CopyRect(view.liveTextArea, viewport);
        else
        {
            viewport.anchorMin = viewAnchorMin;
            viewport.anchorMax = viewAnchorMax;
            viewport.pivot = viewPivot;
            viewport.anchoredPosition = viewPosition;
            viewport.sizeDelta = viewSize;
        }

        // Post-its: one per page; none on the live page.
        foreach (GameObject extra in extraTabs)
        {
            if (extra == null) continue;
            extra.SetActive(false);
            Destroy(extra);
        }
        extraTabs.Clear();
        tabs.Clear();
        tabHome.Clear();
        tabColors.Clear();
        foreach (Image p in view.postIts) if (p != null) p.gameObject.SetActive(false);
        if (!live)
            for (int i = 0; i < topics.Count; i++) UseTab(i);

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
        // The open page's post-it sticks out; the others are a little darker.
        MemoBookLook look = MemoBookLook.Current;
        for (int i = 0; i < tabs.Count; i++)
        {
            bool on = i == index;
            tabs[i].color = on ? tabColors[i] : tabColors[i] * look.idleTabTint;
            tabs[i].rectTransform.anchoredPosition = tabHome[i] + (on ? new Vector2(look.selectedTabOut, 0f) : Vector2.zero);
        }
        if (view.previous != null) view.previous.interactable = index > 0;
        if (view.next != null) view.next.interactable = index < topics.Count - 1;

        for (int i = pageContent.childCount - 1; i >= 0; i--)
        {
            GameObject old = pageContent.GetChild(i).gameObject;
            old.SetActive(false);
            Destroy(old);
        }
        if (view.pageNumber != null) view.pageNumber.text = topics.Count > 0 ? (index + 1) + " / " + topics.Count : "";
        if (live)
        {
            LivePage();
        }
        else if (guide == null || topics.Count == 0)
        {
            Heading("Memo Book", Ink);
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

    /// <summary>
    /// 17 Nov (Task 8): the live guide: ONE step, then the zone list (names + where).
    /// 25 Nov: the old left page (map, players, zones done X / Y) is gone.
    /// </summary>
    private void LivePage()
    {
        MemoGuideTexts t = MemoGuideTexts.Current;
        MemoGuide.LocalStep(out string key, out string text, out bool danger);
        liveKey = key;
        Heading(danger ? t.dangerTitle : t.liveTitle, danger ? Danger : GoalColor);
        Body(pageContent, text, 32f, danger ? Danger : Ink);
        Body(pageContent, t.costNote, 20f, Muted);
        List<string> zones = MemoGuide.ZoneLines(guide);
        if (zones.Count > 0)
        {
            Section("Zones", Ink);
            Bullets(pageContent, zones, 24f, Ink);
        }
        MemoGuide.LocalStepShown(key); // a NEW step costs sanity once (only while the book is open)
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

        // 23 Nov (Mr.k): the Map Rules (whole map, every player, from 3 AM).
        if (guide.mapRules != null && guide.mapRules.Count > 0)
        {
            Section("Map Rules (from 3 AM)", Danger);
            foreach (MapGuide.RuleText r in guide.mapRules)
            {
                if (r == null) continue;
                RectTransform card = Card(Danger, CardBg);
                Body(card, "<b>" + r.title + "</b>", 22f, Danger);
                if (!string.IsNullOrWhiteSpace(r.whatToDo)) Body(card, "<b>DO:</b> " + r.whatToDo.Trim(), 25f, Ink);
                if (!string.IsNullOrWhiteSpace(r.ifYouBreakIt)) Body(card, "<i>" + r.ifYouBreakIt.Trim() + "</i>", 22f, Muted);
            }
        }

        Body(pageContent, "<i>Pick a zone's post-it on the right to see where it is, what to do there and its rules.</i>", 24f, Muted);
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

    /// <summary>
    /// 25 Nov: page i uses the prefab's post-it i. More pages than post-its: a copy of post-it
    /// (i mod count), further down by the gap between the first two post-its.
    /// </summary>
    private void UseTab(int index)
    {
        int n = view.postIts.Count;
        if (n == 0 || view.postIts[index % n] == null) return;
        Image source = view.postIts[index % n];
        Image tab = source;
        Vector2 home = postItHome[index % n];
        if (index >= n)
        {
            GameObject copy = Instantiate(source.gameObject, source.transform.parent, false);
            copy.name = "Post-it " + (index + 1);
            extraTabs.Add(copy);
            tab = copy.GetComponent<Image>();
            float step = n > 1 ? postItHome[0].y - postItHome[1].y : source.rectTransform.rect.height + 8f;
            home.y -= step * n * (index / n);
        }
        tab.gameObject.SetActive(true);

        // 25 Nov: the words written on the post-it in the prefab stay. A copy (more pages than
        // post-its) or an empty post-it gets the page's name.
        TextMeshProUGUI mark = tab.GetComponentInChildren<TextMeshProUGUI>(true);
        if (mark != null)
            mark.text = index < n && !string.IsNullOrWhiteSpace(postItWords[index]) ? postItWords[index] : topics[index].label;
        Button button = tab.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            int captured = index;
            button.onClick.AddListener(() => Select(captured));
        }

        tabs.Add(tab);
        tabHome.Add(home);
        tabColors.Add(postItColor[index % n]);
    }

    /// <summary>25 Nov: the book comes from the prefab Resources/UI/Memo Book (made by code when it is missing).</summary>
    private void EnsureUi()
    {
        if (canvasRoot != null) return;
        rounded = MakeRoundedSprite(64, 18);

        MemoBookView prefab = Resources.Load<MemoBookView>(MemoBookView.ResourcePath);
        if (prefab != null) view = Instantiate(prefab, transform, false);
        else
        {
            Debug.LogWarning("[WorksheetReader] No Memo Book prefab (" + MemoBookView.PrefabPath + "). Using the plain book. " +
                             "Make it: Tools > Pre-Thesis > Designer Settings > Memo Book Prefab.");
            view = MemoBookView.Build(null);
            view.transform.SetParent(transform, false);
        }
        canvasRoot = view.gameObject;
        canvasRoot.name = "Worksheet Book"; // the UI Hub knows the screen by this name

        pageScroll = view.scroll;
        pageContent = view.content;
        viewport = pageScroll.viewport != null ? pageScroll.viewport : (RectTransform)pageScroll.transform;
        viewAnchorMin = viewport.anchorMin;
        viewAnchorMax = viewport.anchorMax;
        viewPivot = viewport.pivot;
        viewPosition = viewport.anchoredPosition;
        viewSize = viewport.sizeDelta;

        foreach (Image p in view.postIts)
        {
            postItHome.Add(p != null ? p.rectTransform.anchoredPosition : Vector2.zero);
            postItColor.Add(p != null ? p.color : Color.white);
            TextMeshProUGUI words = p != null ? p.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            postItWords.Add(words != null ? words.text : "");
        }

        if (view.previous != null) view.previous.onClick.AddListener(() => Select(current - 1));
        if (view.next != null) view.next.onClick.AddListener(() => Select(current + 1));
        if (view.close != null) view.close.onClick.AddListener(Close);

        canvasRoot.SetActive(false);
    }

    /// <summary>Gives <paramref name="target"/> the same place and size as <paramref name="from"/> (both in the same parent).</summary>
    private static void CopyRect(RectTransform from, RectTransform target)
    {
        if (from.parent != target.parent)
        {
            // Different parents: copy the corners instead.
            var corners = new Vector3[4];
            from.GetWorldCorners(corners);
            var parent = (RectTransform)target.parent;
            Vector2 min = parent.InverseTransformPoint(corners[0]);
            Vector2 max = parent.InverseTransformPoint(corners[2]);
            target.anchorMin = target.anchorMax = parent.pivot;
            target.pivot = new Vector2(0.5f, 0.5f);
            target.sizeDelta = max - min;
            target.anchoredPosition = (min + max) * 0.5f;
            return;
        }
        target.anchorMin = from.anchorMin;
        target.anchorMax = from.anchorMax;
        target.pivot = from.pivot;
        target.anchoredPosition = from.anchoredPosition;
        target.sizeDelta = from.sizeDelta;
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
