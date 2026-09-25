using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-centre hotbar for the local player, built in code (no prefab, no
/// scene edits). Shows the 4 slots, which one is in your hand, Walkie-Talkie
/// ON/OFF, when you are talking on the radio, and the mic state.
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
    private TMP_Text statusText;
    private bool dirty = true;

    public static HotbarHUD Create(PlayerInventory owner)
    {
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
                frames[i].color = selected ? SelectedColor : SlotColor;

                if (slot.IsEmpty) labels[i].text = string.Empty;
                else if (ItemCatalog.IsRadio(slot.itemId))
                    labels[i].text = ItemCatalog.DisplayName(slot.itemId) + "\n" +
                        (slot.poweredOn ? Colour("ON", OnColor) : Colour("OFF", OffColor));
                else labels[i].text = ItemCatalog.DisplayName(slot.itemId);
                labels[i].color = selected ? Color.black : TextColor;
            }

            hintText.text = inventory.IsHoldingRadio
                ? $"Hold [{BoundButton.DisplayName(GameSettings.WalkieTalkBinding)}] talk on radio   ·   " +
                  $"[{BoundButton.DisplayName(GameSettings.WalkiePowerBinding)}] on / off"
                : string.Empty;
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
        statusText.text = talking ? mic + "\n" + Colour("RADIO: TALKING", SelectedColor) : mic;
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
