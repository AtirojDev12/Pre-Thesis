using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// 8 Oct (Mr.k). LOBBY STORAGE SCREEN, opened by <see cref="StorageTerminal"/> in the storage room.
///
///   Left:  your HOTBAR (4 slots). [Store] moves an item into storage.
///   Right: your STORAGE (everything you own that is not in the hotbar). [Take] puts it in the hotbar.
///
/// There is one storage room, but each player only ever sees THEIR OWN items:
/// this screen reads PlayerInventory.Local (your own body) only.
/// Only the hotbar goes into a match; storage stays safe in the lobby.
/// Built in code like ShopUI; Esc or Close shuts it.
/// 24 Nov 2026: colours and fonts come from Resources/UI/Styles/Storage Style (Inspector), see CodeScreenStyle.
/// </summary>
public sealed class StorageUI : MonoBehaviour
{
    private static StorageUI instance;
    public static bool IsOpen => instance != null;

    // 24 Nov: no colours in code any more. Edit Assets/Resources/UI/Styles/Storage Style in the Inspector.
    private static CodeScreenStyle Style => CodeScreenStyle.Load("Storage Style", OriginalColours);

    /// <summary>Used only when the Storage Style asset is missing (the look before 24 Nov).</summary>
    private static void OriginalColours(CodeScreenStyle s)
    {
        s.dim = new Color(0f, 0f, 0f, 0.7f);
        s.panel = new Color(0.11f, 0.09f, 0.08f, 1f);
        s.listBack = new Color(0f, 0f, 0f, 0.2f);
        s.row = s.myRow = new Color(0.17f, 0.14f, 0.12f, 1f);
        s.title = s.accent = s.value = new Color(1f, 0.8f, 0.3f);
        s.text = new Color(0.95f, 0.93f, 0.9f);
        s.muted = new Color(0.65f, 0.62f, 0.6f);
        s.faint = new Color32(0x7F, 0x7A, 0x75, 0xFF);
        s.warning = new Color32(0xFF, 0x8A, 0x70, 0xFF);
        s.good = new Color(0.45f, 1f, 0.55f);
        s.bad = new Color(1f, 0.4f, 0.35f);
        s.button = new Color(0.3f, 0.22f, 0.12f, 1f);
        s.buttonText = s.buttonHoverText = s.text;
        s.buttonHighlighted = new Color(1.4f, 1.3f, 1.1f);
        s.buttonDisabled = new Color(0.45f, 0.45f, 0.45f, 0.6f);
    }

    private PlayerInventory inventory;
    private RectTransform hotbarList;
    private RectTransform storageContent;
    private TMP_Text messageText;
    private bool dirty = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    public static void Open()
    {
        if (instance != null || PlayerInventory.Local == null) return;
        var go = new GameObject("Lobby Storage");
        instance = go.AddComponent<StorageUI>();
        instance.inventory = PlayerInventory.Local;
        instance.inventory.Changed += instance.MarkDirty;
        instance.Build();
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
        lift = UiMonitorLift.On(card, "Storage", dim != null ? dim.GetComponent<Graphic>() : null);
        if (lift != null) lift.PlayOpen();
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        if (inventory != null) inventory.Changed -= MarkDirty;
        OverlayPanels.Closed();
        PersistentHUD.PopHidden();
        if (PlayerHealth.LocalInstance != null) OverlayPanels.SetMouseForUi(false);
        else GameplayInput.Blocked = false;
    }

    private void MarkDirty() => dirty = true;

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { Close(); return; }
        if (inventory == null) { Close(); return; }
        if (dirty) { dirty = false; Refresh(); }
    }

    // ---- Lists ----------------------------------------------------------------------

    private void Refresh()
    {
        Clear(hotbarList);
        Clear(storageContent);

        for (int i = 0; i < PlayerInventory.SlotCount; i++)
        {
            InventorySlot slot = inventory.GetSlot(i);
            int index = i;
            string label = (i + 1) + ".  " + (slot.IsEmpty ? CodeScreenStyle.Paint("(empty)", Style.faint) : Describe(slot));
            string why = slot.IsEmpty ? null
                : slot.spare ? "A friend's spare: give it back (Q)"
                : ItemCatalog.MaxStack(slot.itemId) <= 1 && inventory.IsStored(slot.itemId) ? "One is already stored"
                : null;
            AddRow(hotbarList, i, label, "Store", !slot.IsEmpty && why == null, () => Store(index), why);
        }
        hotbarList.sizeDelta = new Vector2(0f, PlayerInventory.SlotCount * RowStep);

        int count = inventory.StoredCount;
        for (int i = 0; i < count; i++)
        {
            InventorySlot item = inventory.GetStored(i);
            int index = i;
            bool stack = ItemCatalog.MaxStack(item.itemId) > 1;
            string why = !stack && inventory.CountOf(item.itemId) > 0 ? "You already carry one"
                : !inventory.HasEmptySlot && !(stack && inventory.UnitsOf(item.itemId) > 0) ? "Hotbar full: store something first"
                : null;
            AddRow(storageContent, i, Describe(item), "Take", why == null, () => Take(index), why);
        }
        if (count == 0)
        {
            TMP_Text empty = NewText("Empty", storageContent, "Nothing in storage.\nItems you buy with a full hotbar wait here.", 24f, Style.muted, FontStyles.Italic, TextAlignmentOptions.Center);
            Anchor(empty.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -100f), Vector2.zero);
        }
        storageContent.sizeDelta = new Vector2(0f, Mathf.Max(1, count) * RowStep);
    }

    private static string Describe(InventorySlot slot)
    {
        string name = ItemCatalog.DisplayName(slot.itemId);
        if (slot.Units > 1 || ItemCatalog.MaxStack(slot.itemId) > 1) name += "  x" + slot.Units;
        if (ItemCatalog.IsFlashlight(slot.itemId)) name += "  " + CodeScreenStyle.Paint(Mathf.CeilToInt(slot.charge * 100f) + "%", Style.muted);
        if (slot.spare) name += "  " + CodeScreenStyle.Paint("SPARE", Style.warning);
        return name;
    }

    private void Store(int slot)
    {
        inventory.RequestStore(slot);
        messageText.text = "Stored.";
    }

    private void Take(int index)
    {
        inventory.RequestTakeStored(index);
        messageText.text = "Moved to your hotbar.";
    }

    // ---- Layout ---------------------------------------------------------------------

    private const float RowStep = 92f;

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

        Image cardImage = NewImage("Card", transform, style.panel);
        style.StyleCard(cardImage);
        RectTransform card = cardImage.rectTransform;
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(1500f, 900f);

        TMP_Text title = NewText("Title", card, "STORAGE", 64f, style.title, FontStyles.Bold, TextAlignmentOptions.Center);
        style.StyleText(title, style.title, true);
        Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -110f), new Vector2(-40f, -30f));
        TMP_Text sub = NewText("Subtitle", card, "Only you can see your storage.  Only your HOTBAR goes into a match.", 26f, style.muted, FontStyles.Normal, TextAlignmentOptions.Center);
        Anchor(sub.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -150f), new Vector2(-40f, -110f));

        // Left column: hotbar
        TMP_Text left = NewText("Hotbar Title", card, "YOUR HOTBAR", 32f, style.text, FontStyles.Bold, TextAlignmentOptions.Left);
        Anchor(left.rectTransform, new Vector2(0f, 1f), new Vector2(0.45f, 1f), new Vector2(50f, -210f), new Vector2(0f, -160f));
        hotbarList = new GameObject("Hotbar", typeof(RectTransform)).GetComponent<RectTransform>();
        hotbarList.SetParent(card, false);
        hotbarList.anchorMin = new Vector2(0f, 1f);
        hotbarList.anchorMax = new Vector2(0.45f, 1f);
        hotbarList.pivot = new Vector2(0.5f, 1f);
        hotbarList.offsetMin = new Vector2(50f, -220f - PlayerInventory.SlotCount * RowStep);
        hotbarList.offsetMax = new Vector2(-10f, -220f);

        // Right column: storage (scrolls)
        TMP_Text right = NewText("Storage Title", card, "IN STORAGE", 32f, style.text, FontStyles.Bold, TextAlignmentOptions.Left);
        Anchor(right.rectTransform, new Vector2(0.47f, 1f), new Vector2(1f, 1f), new Vector2(10f, -210f), new Vector2(-50f, -160f));

        Image viewportImage = NewImage("Viewport", card, style.listBack);
        CodeScreenStyle.PaintImage(viewportImage, style.listBack, style.listSprite);
        viewportImage.raycastTarget = true;
        viewportImage.gameObject.AddComponent<RectMask2D>();
        RectTransform viewport = viewportImage.rectTransform;
        viewport.anchorMin = new Vector2(0.47f, 0f);
        viewport.anchorMax = new Vector2(1f, 1f);
        viewport.offsetMin = new Vector2(10f, 150f);
        viewport.offsetMax = new Vector2(-50f, -220f);

        storageContent = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
        storageContent.SetParent(viewport, false);
        storageContent.anchorMin = new Vector2(0f, 1f);
        storageContent.anchorMax = new Vector2(1f, 1f);
        storageContent.pivot = new Vector2(0.5f, 1f);

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = storageContent;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        messageText = NewText("Message", card, "", 26f, style.muted, FontStyles.Normal, TextAlignmentOptions.Center);
        Anchor(messageText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(40f, 105f), new Vector2(-40f, 145f));

        Button close = NewButton("Close", card, "Close (Esc)");
        RectTransform closeRt = (RectTransform)close.transform;
        closeRt.anchorMin = closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 25f);
        closeRt.sizeDelta = new Vector2(320f, 70f);
        close.onClick.AddListener(Close);
    }

    private void AddRow(RectTransform parent, int index, string label, string buttonText, bool enabled, System.Action action, string why)
    {
        Image rowImage = NewImage("Row " + index, parent, Style.row);
        CodeScreenStyle.PaintImage(rowImage, Style.row, Style.rowSprite);
        RectTransform row = rowImage.rectTransform;
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.offsetMin = new Vector2(0f, -index * RowStep - (RowStep - 12f));
        row.offsetMax = new Vector2(0f, -index * RowStep);

        TMP_Text text = NewText("Label", row, label, 28f, Style.text, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        Anchor(text.rectTransform, new Vector2(0f, 0.35f), new Vector2(0.68f, 1f), new Vector2(18f, 0f), Vector2.zero);
        if (!string.IsNullOrEmpty(why))
        {
            TMP_Text note = NewText("Why", row, why, 20f, Style.muted, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            Anchor(note.rectTransform, new Vector2(0f, 0f), new Vector2(0.7f, 0.38f), new Vector2(18f, 4f), Vector2.zero);
        }

        Button button = NewButton(buttonText, row, buttonText);
        Anchor((RectTransform)button.transform, new Vector2(0.72f, 0.15f), new Vector2(1f, 0.85f), Vector2.zero, new Vector2(-14f, 0f));
        button.interactable = enabled;
        button.onClick.AddListener(() => action());
    }

    private static void Clear(RectTransform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);
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
        tmp.text = Keybinds.Fix(text); // 20 Oct: "(Q)" -> the player's key
        tmp.fontSize = size;
        Style.StyleText(tmp, color); // 24 Nov: font + colour from Storage Style
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
        Style.StyleButton(button, text); // 24 Nov: colours, picture, hover words from Storage Style
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
