using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// LOBBY SHOP SCREEN (1 Oct). Opened by <see cref="ShopTerminal"/>.
///
/// Lists every item in <see cref="ItemCatalog"/> with a price. Buying:
///   - spends currency from YOUR save (SaveManager.Current is the only source of truth),
///   - marks the permanent item as owned (max 1 per type) and saves to disk,
///   - asks the server to put it in your hotbar now (PlayerInventory.RequestAddOwned).
/// Permanent items are LOST if you die in a match (MatchResultsUI).
///
/// Built in code; Esc or Close shuts it.
/// 24 Nov 2026: colours and fonts come from Resources/UI/Styles/Shop Style (Inspector), see CodeScreenStyle.
/// </summary>
public sealed class ShopUI : MonoBehaviour
{
    private static ShopUI instance;
    public static bool IsOpen => instance != null;

    private TMP_Text currencyText;
    private TMP_Text messageText;
    private readonly List<Row> rows = new List<Row>();

    private sealed class Row
    {
        public ItemCatalog.ItemInfo item;
        public TMP_Text status;
        public Button buy;
    }

    // 24 Nov: no colours in code any more. Edit Assets/Resources/UI/Styles/Shop Style in the Inspector.
    private static CodeScreenStyle Style => CodeScreenStyle.Load("Shop Style", OriginalColours);
    private static Color Muted => Style.muted;
    private static Color Good => Style.good;
    private static Color Bad => Style.bad;

    /// <summary>Used only when the Shop Style asset is missing (the look before 24 Nov).</summary>
    private static void OriginalColours(CodeScreenStyle s)
    {
        s.dim = new Color(0f, 0f, 0f, 0.7f);
        s.panel = new Color(0.11f, 0.09f, 0.08f, 1f);
        s.listBack = new Color(0f, 0f, 0f, 0f);
        s.row = s.myRow = new Color(0.17f, 0.14f, 0.12f, 1f);
        s.title = s.accent = new Color(1f, 0.8f, 0.3f);
        s.value = new Color32(0xFF, 0xCC, 0x4D, 0xFF);
        s.text = new Color(0.95f, 0.93f, 0.9f);
        s.muted = new Color(0.65f, 0.62f, 0.6f);
        s.faint = new Color32(0xA6, 0xA0, 0x9A, 0xFF);
        s.good = new Color(0.45f, 1f, 0.55f);
        s.warning = new Color(1f, 0.8f, 0.3f);
        s.bad = new Color(1f, 0.4f, 0.35f);
        s.button = new Color(0.3f, 0.22f, 0.12f, 1f);
        s.buttonText = s.buttonHoverText = s.text;
        s.buttonHighlighted = new Color(1.4f, 1.3f, 1.1f);
        s.buttonDisabled = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        s.scrollbarBack = new Color(0f, 0f, 0f, 0.35f);
        s.scrollbarHandle = new Color(1f, 0.8f, 0.3f);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    public static void Open()
    {
        if (instance != null) return;
        var go = new GameObject("Lobby Shop");
        instance = go.AddComponent<ShopUI>();
        instance.Build();
        instance.Refresh();
        instance.StartMonitor(); // 25 Nov
        OverlayPanels.Opened();
        PersistentHUD.PushHidden();
        OverlayPanels.SetMouseForUi(true);
    }

    public static void Close()
    {
        if (instance == null || instance.closing) return;
        // 25 Nov: the monitor switches off and is lowered first (UiMonitorLift), then the screen goes.
        instance.closing = true;
        GameObject go = instance.gameObject;
        if (instance.lift != null) instance.lift.PlayClose(() => { if (go != null) Destroy(go); });
        else Destroy(go);
    }

    // ---- 25 Nov: "lift the monitor up" open / close animation + monitor frame -----------------
    // Settings and the frame drawing: Assets/Resources/UI/Styles/Monitor Frame (MonitorFrameStyle).
    private UiMonitorLift lift;
    private bool closing;

    private void StartMonitor()
    {
        var card = transform.Find("Card") as RectTransform;
        Transform dim = transform.Find("Dim");
        lift = UiMonitorLift.On(card, "Shop", dim != null ? dim.GetComponent<Graphic>() : null);
        if (lift != null) lift.PlayOpen();
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        OverlayPanels.Closed();
        PersistentHUD.PopHidden();
        if (PlayerHealth.LocalInstance != null) OverlayPanels.SetMouseForUi(false);
        else GameplayInput.Blocked = false;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
    }

    // ---- Buying ------------------------------------------------------------------

    private void Buy(ItemCatalog.ItemInfo item)
    {
        SaveData save = SaveManager.Current;
        if (save == null) { Message("No save loaded.", Bad); return; }

        if (Owns(item.id)) { Message($"You already own the {item.displayName}.", Muted); return; }
        // 5 Oct: consumables (Battery) have a max you can own, and need hotbar room.
        if (item.Consumable)
        {
            int max = Mathf.Max(1, item.maxStack);
            if (SaveManager.GetConsumableQuantity(item.id) >= max) { Message($"You can carry at most {max} {item.displayName}.", Muted); return; }
        }
        // 3 Oct (bug #8): carrying one you picked up (ownership message not here yet) = no buy.
        if (item.permanent && PlayerInventory.Local != null && (PlayerInventory.Local.CountOf(item.id) > 0 || PlayerInventory.Local.IsStored(item.id)))
        {
            Message($"You already carry a {item.displayName}.", Muted);
            return;
        }

        if (item.price > 0 && !SaveManager.SpendCurrency(item.price))
        {
            Message($"Not enough currency. You need {item.price}.", Bad);
            return;
        }

        if (item.permanent)
        {
            PermanentItemData entry = FindPermanent(item.id);
            if (entry != null) entry.isOwned = true;
            else save.permanentItems.Add(new PermanentItemData(item.id, true));
        }
        else
        {
            SaveManager.AddConsumable(item.id, 1); // one entry per item, quantity goes up
        }

        SaveManager.SaveToDisk();

        // Into the hotbar right away (the server checks it). 8 Oct: full hotbar = your storage.
        PlayerInventory inv = PlayerInventory.Local;
        bool toStorage = inv != null && (item.Stackable
            ? inv.IsStored(item.id) || inv.UnitsOf(item.id) == 0 && !inv.HasEmptySlot
            : !inv.HasEmptySlot);
        if (inv != null) inv.RequestAddOwned(item.id);

        string done = item.price > 0 ? $"Bought the {item.displayName}!" : $"Claimed the {item.displayName} (free)!";
        Message(toStorage ? done + " Your hotbar is full: it is in your STORAGE room." : done, Good);
        Refresh();
    }

    private static PermanentItemData FindPermanent(string id)
    {
        List<PermanentItemData> list = SaveManager.Current != null ? SaveManager.Current.permanentItems : null;
        if (list == null) return null;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && list[i].itemID == id) return list[i];
        return null;
    }

    private static bool Owns(string id)
    {
        PermanentItemData entry = FindPermanent(id);
        return entry != null && entry.isOwned;
    }

    private void Refresh()
    {
        int money = SaveManager.Current != null ? SaveManager.Current.currency : 0;
        CodeScreenStyle style = Style;
        currencyText.text = "Currency: " + CodeScreenStyle.Paint(money.ToString(), style.value);

        foreach (Row row in rows)
        {
            bool owned = row.item.permanent && Owns(row.item.id);
            if (row.item.Consumable)
            {
                // 5 Oct: "x2 / 3" and the price; the button hides at the max.
                int have = SaveManager.GetConsumableQuantity(row.item.id);
                int max = Mathf.Max(1, row.item.maxStack);
                row.buy.gameObject.SetActive(have < max);
                row.buy.interactable = money >= row.item.price;
                row.status.text = have >= max ? CodeScreenStyle.Paint($"x{have} (MAX)", style.good)
                    : $"<size=70%>x{have}/{max}</size>  " + Price(row.item, style); // 17 Nov: free syringe
                continue;
            }
            row.buy.gameObject.SetActive(!owned);
            row.buy.interactable = money >= row.item.price;
            row.status.text = owned ? CodeScreenStyle.Paint("OWNED", style.good) : Price(row.item, style);
        }
    }

    private static string Price(ItemCatalog.ItemInfo item, CodeScreenStyle style) =>
        item.price > 0 ? CodeScreenStyle.Paint(item.price.ToString(), style.value) : CodeScreenStyle.Paint("FREE", style.good);

    private void Message(string text, Color color)
    {
        messageText.text = text;
        messageText.color = color;
    }

    // ---- Layout --------------------------------------------------------------------

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
        canvas.sortingOrder = 30500;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        CodeScreenStyle style = Style;
        Image dim = NewImage("Dim", transform, style.dim);
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;

        // 8 Oct: fixed-size card; the item list scrolls (mouse wheel / drag / scrollbar).
        Image cardImage = NewImage("Card", transform, style.panel);
        style.StyleCard(cardImage);
        RectTransform card = cardImage.rectTransform;
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(1100f, 1000f);

        float y = 30f;
        TMP_Text title = NewText("Title", card, "SHOP", 64f, style.title, FontStyles.Bold, TextAlignmentOptions.Center);
        style.StyleText(title, style.title, true);
        Place(title, card, 40f, ref y, 80f);
        currencyText = NewText("Currency", card, "", 34f, style.text, FontStyles.Normal, TextAlignmentOptions.Center);
        Place(currencyText, card, 40f, ref y, 50f);
        y += 10f;

        const float RowStep = 130f;
        const float FooterHeight = 160f; // message + close button

        // Viewport: clips the rows and catches the mouse wheel.
        Image viewportImage = NewImage("Viewport", card, style.listBack);
        CodeScreenStyle.PaintImage(viewportImage, style.listBack, style.listSprite);
        viewportImage.raycastTarget = true;
        viewportImage.gameObject.AddComponent<RectMask2D>();
        RectTransform viewport = viewportImage.rectTransform;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(40f, FooterHeight);
        viewport.offsetMax = new Vector2(-56f, -y);

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewport, false);
        RectTransform content = (RectTransform)contentGo.transform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);

        float rowY = 0f;
        foreach (ItemCatalog.ItemInfo item in ItemCatalog.All)
        {
            if (!item.inShop) continue; // not for sale (5 Oct: price 0 + inShop = FREE)

            Image rowImage = NewImage("Item " + item.id, content, style.row);
            CodeScreenStyle.PaintImage(rowImage, style.row, style.rowSprite);
            RectTransform rowRt = rowImage.rectTransform;
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.offsetMin = new Vector2(0f, -rowY - 115f);
            rowRt.offsetMax = new Vector2(0f, -rowY);
            rowY += RowStep;

            string note = item.permanent ? "(permanent, lost if you die)" : "(max " + item.maxStack + ", lost if you die)";
            TMP_Text name = NewText("Name", rowRt, item.displayName + "  <size=70%>" + CodeScreenStyle.Paint(note, style.faint) + "</size>",
                34f, style.text, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            Anchor(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.72f, 1f), new Vector2(20f, 0f), new Vector2(0f, -10f));

            TMP_Text desc = NewText("Description", rowRt, Keybinds.Fix(item.description ?? ""), 22f, Muted, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            desc.textWrappingMode = TextWrappingModes.Normal;
            Anchor(desc.rectTransform, new Vector2(0f, 0f), new Vector2(0.72f, 0.5f), new Vector2(20f, 8f), new Vector2(0f, 0f));

            TMP_Text status = NewText("Status", rowRt, "", 34f, style.text, FontStyles.Bold, TextAlignmentOptions.Center);
            Anchor(status.rectTransform, new Vector2(0.72f, 0.5f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-20f, -8f));

            Button buy = NewButton("Buy", rowRt, item.price > 0 ? "Buy" : "Claim");
            Anchor((RectTransform)buy.transform, new Vector2(0.74f, 0f), new Vector2(1f, 0.5f), new Vector2(0f, 10f), new Vector2(-20f, -4f));
            ItemCatalog.ItemInfo captured = item;
            buy.onClick.AddListener(() => Buy(captured));

            rows.Add(new Row { item = item, status = status, buy = buy });
        }
        content.sizeDelta = new Vector2(0f, Mathf.Max(0f, rowY - (RowStep - 115f)));

        // Scrollbar on the right of the list.
        RectTransform barRt = NewImage("Scrollbar", card, style.scrollbarBack).rectTransform;
        barRt.GetComponent<Image>().raycastTarget = true;
        barRt.anchorMin = new Vector2(1f, 0f);
        barRt.anchorMax = new Vector2(1f, 1f);
        barRt.pivot = new Vector2(1f, 0.5f);
        barRt.offsetMin = new Vector2(-50f, FooterHeight);
        barRt.offsetMax = new Vector2(-40f, -y);
        var slidingArea = new GameObject("Sliding Area", typeof(RectTransform));
        slidingArea.transform.SetParent(barRt, false);
        Stretch((RectTransform)slidingArea.transform);
        Image handle = NewImage("Handle", slidingArea.transform, style.scrollbarHandle);
        handle.raycastTarget = true;
        Stretch(handle.rectTransform);
        Scrollbar scrollbar = barRt.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.verticalNormalizedPosition = 1f;

        messageText = NewText("Message", card, "", 26f, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
        RectTransform messageRt = messageText.rectTransform;
        messageRt.anchorMin = new Vector2(0f, 0f);
        messageRt.anchorMax = new Vector2(1f, 0f);
        messageRt.pivot = new Vector2(0.5f, 0f);
        messageRt.offsetMin = new Vector2(40f, 110f);
        messageRt.offsetMax = new Vector2(-40f, 150f);

        Button close = NewButton("Close", card, "Close (Esc)");
        RectTransform closeRt = (RectTransform)close.transform;
        closeRt.anchorMin = closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 30f);
        closeRt.sizeDelta = new Vector2(320f, 70f);
        close.onClick.AddListener(Close);
    }

    private static void Place(TMP_Text text, RectTransform card, float side, ref float y, float height)
    {
        RectTransform rt = text.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(side, -y - height);
        rt.offsetMax = new Vector2(-side, -y);
        y += height;
    }

    private static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static TMP_Text NewText(string name, Transform parent, string text, float size, Color color,
        FontStyles style, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        Style.StyleText(tmp, color); // 24 Nov: font + colour from Shop Style
        tmp.fontStyle = style;
        tmp.alignment = alignment;
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

    private static Button NewButton(string name, Transform parent, string label)
    {
        Image bg = NewImage(name, parent, Style.button);
        bg.raycastTarget = true;
        var button = bg.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;

        TMP_Text text = NewText("Label", bg.transform, label, 30f, Style.buttonText, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        Style.StyleButton(button, text); // 24 Nov: colours, picture, hover words from Shop Style
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
