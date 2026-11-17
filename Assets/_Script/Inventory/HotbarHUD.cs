using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-centre hotbar for the local player. Shows the 4 slots, which one is in your
/// hand, Walkie-Talkie ON/OFF, when you are talking on the radio, and the mic state.
///
/// 16 Nov 2026 (Mr.k): the hotbar is now a real part of the GAME HUD prefab
/// (<see cref="HotbarView"/>: frames, texts, optional item icons, all editable). This script
/// is only the logic and writes into that view. If the GAME HUD has no hotbar (run
/// Tools > Pre-Thesis > UI > GAME HUD: Add Missing Parts), it builds the old code-made one.
/// </summary>
public sealed class HotbarHUD : MonoBehaviour
{
    private static readonly Color SlotColor = new Color(0.08f, 0.07f, 0.07f, 0.75f);
    private static readonly Color SelectedColor = new Color(0.85f, 0.64f, 0.25f, 0.95f);
    private static readonly Color TextColor = new Color(0.96f, 0.94f, 0.93f, 1f);
    private static readonly Color OnColor = new Color(0.45f, 0.95f, 0.45f, 1f);
    private static readonly Color OffColor = new Color(0.95f, 0.4f, 0.35f, 1f);

    private PlayerInventory inventory;
    private readonly Image[] frames = new Image[PlayerInventory.SlotCount];
    private readonly TMP_Text[] labels = new TMP_Text[PlayerInventory.SlotCount];
    private TMP_Text hintText;
    private bool showingShoutHint; // 31 Oct
    private TMP_Text statusText;
    private bool dirty = true;
    private HotbarView view;    // 16 Nov: the hotbar inside the GAME HUD (null = code-made hotbar)
    private string shownHint = string.Empty;

    public static HotbarHUD Create(PlayerInventory owner)
    {
        HotbarView hudView = HotbarView.Find(PlayerInventory.SlotCount);
        if (hudView != null)
        {
            // Logic only: the pictures and texts live in the GAME HUD prefab.
            var logic = new GameObject("Hotbar HUD (logic)").AddComponent<HotbarHUD>();
            logic.inventory = owner;
            logic.view = hudView;
            hudView.Show(true);
            owner.Changed += logic.MarkDirty;
            GameSettings.Changed += logic.MarkDirty;
            return logic;
        }

        var go = new GameObject("Hotbar HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50; // under the pause menu
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var hud = go.AddComponent<HotbarHUD>();
        hud.inventory = owner;
        hud.Build();
        owner.Changed += hud.MarkDirty;
        GameSettings.Changed += hud.MarkDirty;
        return hud;
    }

    private void MarkDirty() => dirty = true;

    private void OnDestroy()
    {
        if (inventory != null) inventory.Changed -= MarkDirty;
        GameSettings.Changed -= MarkDirty;
        if (view != null) view.Show(false);
    }

    private void Build()
    {
        const float size = 96f, gap = 10f;
        float total = PlayerInventory.SlotCount * size + (PlayerInventory.SlotCount - 1) * gap;

        for (int i = 0; i < PlayerInventory.SlotCount; i++)
        {
            RectTransform slot = NewRect("Slot " + (i + 1), transform);
            slot.anchorMin = slot.anchorMax = new Vector2(0.5f, 0f);
            slot.pivot = new Vector2(0f, 0f);
            slot.sizeDelta = new Vector2(size, size);
            slot.anchoredPosition = new Vector2(-total / 2f + i * (size + gap), 24f);
            frames[i] = slot.gameObject.AddComponent<Image>();
            frames[i].raycastTarget = false;

            TMP_Text number = MakeText(slot, "Key", (i + 1).ToString(), 18, TextAlignmentOptions.TopLeft);
            number.rectTransform.offsetMin = new Vector2(6f, 0f);
            number.rectTransform.offsetMax = new Vector2(0f, -4f);

            labels[i] = MakeText(slot, "Item", string.Empty, 17, TextAlignmentOptions.Center);
            labels[i].rectTransform.offsetMin = new Vector2(4f, 4f);
            labels[i].rectTransform.offsetMax = new Vector2(-4f, -18f);
            labels[i].enableAutoSizing = true;
            labels[i].fontSizeMin = 10f;
            labels[i].fontSizeMax = 17f;
        }

        hintText = MakeText(transform, "Hint", string.Empty, 20, TextAlignmentOptions.Bottom);
        Place(hintText.rectTransform, new Vector2(0.5f, 0f), new Vector2(900f, 30f), new Vector2(0f, 24f + size + 10f));

        statusText = MakeText(transform, "Voice Status", string.Empty, 20, TextAlignmentOptions.BottomLeft);
        Place(statusText.rectTransform, new Vector2(0f, 0f), new Vector2(520f, 60f), new Vector2(24f, 24f));
        statusText.rectTransform.pivot = new Vector2(0f, 0f);
    }

    private void LateUpdate()
    {
        if (inventory == null) { Destroy(gameObject); return; }

        if (dirty)
        {
            dirty = false;
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                bool selected = i == inventory.SelectedSlot;

                // The item's name, and its state words (second line).
                string itemName = slot.IsEmpty ? string.Empty : ItemCatalog.DisplayName(slot.itemId);
                string state = string.Empty;
                if (slot.IsEmpty) { }
                else if (slot.spare) state = Colour("SPARE", OffColor);
                else if (ItemCatalog.IsRadio(slot.itemId))
                    state = ItemCatalog.UsesWalkieBattery(slot.itemId) // 17 Nov: the free walkie's battery
                        ? (slot.charge <= 0f ? Colour("EMPTY", OffColor)
                            : (slot.poweredOn ? Colour("ON", OnColor) : Colour("OFF", OffColor)) + " " + Mathf.CeilToInt(slot.charge * 100f) + "%")
                        : slot.poweredOn ? Colour("ON", OnColor) : Colour("OFF", OffColor);
                else if (ItemCatalog.IsFlashlight(slot.itemId)) // 5 Oct
                    state = slot.charge <= 0f ? Colour("EMPTY", OffColor)
                        : (slot.poweredOn ? Colour("ON", OnColor) : Colour("OFF", OffColor)) + " " + Mathf.CeilToInt(slot.charge * 100f) + "%";
                else if (ItemCatalog.MaxStack(slot.itemId) > 1) // 5 Oct: Battery x3
                    state = Colour("x" + slot.Units, OnColor);

                if (view != null) // 16 Nov: the hotbar in the GAME HUD prefab
                {
                    Sprite icon = slot.IsEmpty ? null : view.IconOf(slot.itemId);
                    string shown = icon != null && view.HideNameWhenIconShown ? string.Empty : itemName;
                    string viewWords = state.Length == 0 ? shown : shown.Length == 0 ? state : shown + "\n" + state;
                    view.SetSlot(i, selected, viewWords, icon);
                    continue;
                }

                frames[i].color = selected ? SelectedColor : SlotColor;
                labels[i].text = state.Length == 0 ? itemName : itemName + "\n" + state;
                labels[i].color = selected ? Color.black : TextColor;
            }

            // 20 Oct: key names come from Settings > Controls (Keybinds).
            SetHint(inventory.HandsBlocked // 17 Nov: a large two-handed object (sign)
                ? $"Hands full: put the sign down first [{Keybinds.KeyName(GameAction.ReloadOrDiscard)}]"
                : inventory.IsHoldingSyringe
                ? $"Hold [{Keybinds.KeyName(GameAction.UseSyringe)}] heal yourself   ·   look at a downed friend: get them up"
                : inventory.IsHolding(ItemCatalog.BlackBox)
                ? $"[E] on the drop spot: throw it in   ·   [{Keybinds.KeyName(GameAction.ReloadOrDiscard)}] put it back"
                : inventory.IsHolding(ItemCatalog.GhostFlavor)
                ? $"Bring it back to the popcorn station   ·   [{Keybinds.KeyName(GameAction.ReloadOrDiscard)}] drop it"
                : inventory.IsHoldingSpare
                ? $"Spare copy: you cannot use it.   [{Keybinds.KeyName(GameAction.ThrowOrDrop)}] drop it for a friend"
                : inventory.IsHoldingRadio
                ? $"Hold [{Keybinds.KeyName(GameAction.WalkieTalk)}] talk on radio   ·   " +
                  $"[{Keybinds.KeyName(GameAction.WalkiePower)}] on / off"
                : inventory.IsHoldingFlashlight
                ? $"[{FlashlightController.ToggleHint}] light on / off   ·   " +
                  (ItemCatalog.Find(inventory.HeldSlot.itemId)?.usesBatteries == true
                      ? $"[{Keybinds.KeyName(GameAction.ReloadOrDiscard)}] new battery"
                      : $"Mash [{Keybinds.KeyName(GameAction.FlashlightCrank)}] to charge")
                : inventory.IsHoldingMap // 24 Oct
                ? $"Hold [{Keybinds.KeyName(GameAction.ReadMap)}] read the map   ·   [{Keybinds.KeyName(GameAction.ThrowOrDrop)}] drop it for a friend"
                : !inventory.HeldSlot.IsEmpty && !inventory.HeldSlot.spare && ItemCatalog.IsWorksheet(inventory.HeldSlot.itemId) // 5 Nov
                ? $"[{Keybinds.KeyName(GameAction.ReadMap)}] open the work sheet   ·   [{Keybinds.KeyName(GameAction.ThrowOrDrop)}] drop it"
                : PlayerSanity.HotbarHint(inventory)); // 8 Oct: snack / holy items
        }

        // 31 Oct: while downed, the hint line shows the downed hint (5 Nov: the walkie key, polled).
        string shoutHint = PlayerVoice.Local != null ? PlayerVoice.Local.LocalShoutHint : null;
        if (shoutHint != null)
        {
            SetHint(shoutHint);
            showingShoutHint = true;
        }
        else if (showingShoutHint)
        {
            showingShoutHint = false;
            dirty = true; // put the item hint back next frame
        }

        // Voice status changes without events (menu, connection), so poll it.
        string mic;
        switch (VoiceChatManager.CurrentStatus)
        {
            case VoiceChatManager.Status.Live: mic = Colour("Mic ON", OnColor); break;
            case VoiceChatManager.Status.MutedByMenu: mic = Colour("Mic OFF (menu open)", OffColor); break;
            case VoiceChatManager.Status.Connecting: mic = "Voice: connecting..."; break;
            default: mic = Colour("Voice: offline", OffColor); break;
        }
        bool talking = PlayerVoice.Local != null && PlayerVoice.Local.RadioTransmitting;
        string status = talking ? mic + "\n" + Colour("RADIO: TALKING", SelectedColor) : mic;
        if (view != null) view.SetVoiceStatus(status);
        else if (statusText.text != status) statusText.text = status;
    }

    private void SetHint(string text)
    {
        if (text == null) text = string.Empty;
        if (text == shownHint) return;
        shownHint = text;
        if (view != null) view.SetHint(text);
        else hintText.text = text;
    }

    // ---- UI helpers ----------------------------------------------------------

    private static string Colour(string text, Color colour) =>
        $"<color=#{ColorUtility.ToHtmlStringRGB(colour)}>{text}</color>";

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static TMP_Text MakeText(Transform parent, string name, string text, float size, TextAlignmentOptions align)
    {
        RectTransform rt = NewRect(name, parent);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = TextColor;
        label.alignment = align;
        label.raycastTarget = false;
        label.richText = true;
        return label;
    }

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 position)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(anchor.x, 0f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
    }
}
