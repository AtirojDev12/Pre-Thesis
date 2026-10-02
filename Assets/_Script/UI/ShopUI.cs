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

    private static readonly Color Panel = new Color(0.11f, 0.09f, 0.08f, 1f);
    private static readonly Color RowColor = new Color(0.17f, 0.14f, 0.12f, 1f);
    private static readonly Color Gold = new Color(1f, 0.8f, 0.3f);
    private static readonly Color TextColor = new Color(0.95f, 0.93f, 0.9f);
    private static readonly Color Muted = new Color(0.65f, 0.62f, 0.6f);
    private static readonly Color Good = new Color(0.45f, 1f, 0.55f);
    private static readonly Color Bad = new Color(1f, 0.4f, 0.35f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    public static void Open()
    {
        if (instance != null) return;
        var go = new GameObject("Lobby Shop");
        instance = go.AddComponent<ShopUI>();
        instance.Build();
        instance.Refresh();
        OverlayPanels.Opened();
        PersistentHUD.PushHidden();
        OverlayPanels.SetMouseForUi(true);
    }

    public static void Close()
    {
        if (instance != null) Destroy(instance.gameObject);
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

        if (!SaveManager.SpendCurrency(item.price))
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
            save.consumables.Add(new ConsumableItemData(item.id, 1));
        }

        SaveManager.SaveToDisk();

        // Into the hotbar right away (the server checks it).
        if (PlayerInventory.Local != null) PlayerInventory.Local.RequestAddOwned(item.id);

        Message($"Bought the {item.displayName}!", Good);
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
        currencyText.text = $"Currency: <color=#FFCC4D>{money}</color>";

        foreach (Row row in rows)
        {
            bool owned = row.item.permanent && Owns(row.item.id);
            row.buy.gameObject.SetActive(!owned);
            row.buy.interactable = money >= row.item.price;
            row.status.text = owned ? "<color=#73FF8C>OWNED</color>" : $"<color=#FFCC4D>{row.item.price}</color>";
        }
    }

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

        Image dim = NewImage("Dim", transform, new Color(0f, 0f, 0f, 0.7f));
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;

        RectTransform card = NewImage("Card", transform, Panel).rectTransform;
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        int count = ItemCatalog.All.Count;
        card.sizeDelta = new Vector2(1100f, 330f + 130f * Mathf.Max(1, count));

        float y = 30f;
        Place(NewText("Title", card, "SHOP", 64f, Gold, FontStyles.Bold, TextAlignmentOptions.Center), card, 40f, ref y, 80f);
        currencyText = NewText("Currency", card, "", 34f, TextColor, FontStyles.Normal, TextAlignmentOptions.Center);
        Place(currencyText, card, 40f, ref y, 50f);
        y += 10f;

        foreach (ItemCatalog.ItemInfo item in ItemCatalog.All)
        {
            if (item.price <= 0) continue; // not for sale

            RectTransform rowRt = NewImage("Item " + item.id, card, RowColor).rectTransform;
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.offsetMin = new Vector2(40f, -y - 115f);
            rowRt.offsetMax = new Vector2(-40f, -y);
            y += 130f;

            TMP_Text name = NewText("Name", rowRt, item.displayName + (item.permanent ? "  <size=70%><color=#A6A09A>(permanent, lost if you die)</color></size>" : ""),
                34f, TextColor, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            Anchor(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.72f, 1f), new Vector2(20f, 0f), new Vector2(0f, -10f));

            TMP_Text desc = NewText("Description", rowRt, item.description ?? "", 22f, Muted, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            desc.textWrappingMode = TextWrappingModes.Normal;
            Anchor(desc.rectTransform, new Vector2(0f, 0f), new Vector2(0.72f, 0.5f), new Vector2(20f, 8f), new Vector2(0f, 0f));

            TMP_Text status = NewText("Status", rowRt, "", 34f, TextColor, FontStyles.Bold, TextAlignmentOptions.Center);
            Anchor(status.rectTransform, new Vector2(0.72f, 0.5f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-20f, -8f));

            Button buy = NewButton("Buy", rowRt, "Buy");
            Anchor((RectTransform)buy.transform, new Vector2(0.74f, 0f), new Vector2(1f, 0.5f), new Vector2(0f, 10f), new Vector2(-20f, -4f));
            ItemCatalog.ItemInfo captured = item;
            buy.onClick.AddListener(() => Buy(captured));

            rows.Add(new Row { item = item, status = status, buy = buy });
        }

        messageText = NewText("Message", card, "", 26f, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
        Place(messageText, card, 40f, ref y, 40f);

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
        tmp.color = color;
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
        Image bg = NewImage(name, parent, new Color(0.3f, 0.22f, 0.12f, 1f));
        bg.raycastTarget = true;
        var button = bg.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.4f, 1.3f, 1.1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        button.colors = colors;

        TMP_Text text = NewText("Label", bg.transform, label, 30f, TextColor, FontStyles.Bold, TextAlignmentOptions.Center);
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
