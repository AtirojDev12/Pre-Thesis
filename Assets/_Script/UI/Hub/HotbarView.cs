using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 16 Nov 2026 (Mr.k): the HOTBAR that lives inside the GAME HUD prefab.
///
/// FOR DESIGNERS (open the GAME HUD prefab, or use the UI Hub)
///   - Every slot has its own parts: Frame (the box), Key (1-4), Item (the name / ON / OFF words)
///     and Icon (optional item picture). Move, resize and restyle them freely.
///   - Selected slot: the frame gets the Selected Colour, and the Selected Picture if you set one.
///     "Tint Frames" off = the frames keep their own colour.
///   - Item Icons list: drag one picture per item. An item with a picture shows its icon.
///     "Hide Name When Icon Shown" removes the name under an icon (ON / OFF / x3 words stay).
///   - Hint (the line above the hotbar) and Voice Status (mic line) are optional slots.
///   - 25 Nov: Battery bar under a slot (flashlights, the free walkie). Optional per slot:
///     drag your own "Charge Frame" + "Charge Fill" Images in. Empty = the game makes a thin bar
///     right under the slot, styled like the "Flashlight Battery" HUD bar (its Frame / Fill pictures).
///
/// FOR PROGRAMMERS
///   HotbarHUD (the logic) finds this view and only writes values into it.
///   No view in the GAME HUD = HotbarHUD builds its old code-made hotbar.
///   LOCAL ONLY (UI).
/// </summary>
[AddComponentMenu("Pre-Thesis/Hotbar View (GAME HUD)")]
[DisallowMultipleComponent]
public sealed class HotbarView : MonoBehaviour
{
    [Serializable]
    public sealed class Slot
    {
        [Tooltip("The slot's box.")]
        public Image frame;
        [Tooltip("The slot number (1-4).")]
        public TMP_Text key;
        [Tooltip("Item name and its state words (ON / OFF / x3).")]
        public TMP_Text item;
        [Tooltip("Optional item picture. Hidden when the item has no picture.")]
        public Image icon;
        [Tooltip("25 Nov. Optional: the box of the battery bar under this slot. Empty = made by the game.")]
        public Image chargeFrame;
        [Tooltip("25 Nov. Optional: the part of the battery bar that grows. Image Type Filled = cut, other = squeezed.")]
        public Image chargeFill;
        [NonSerialized] public float chargeFullMax = -1f; // the fill's full right edge (anchor), remembered once
        [NonSerialized] public float chargeShown = -1f;
    }

    [Serializable]
    public sealed class ItemIcon
    {
        [Tooltip("Item ID, e.g. flashlight, walkie_talkie, battery.")]
        public string itemId = "";
        public Sprite picture;
    }

    [Header("Slots (one per hotbar slot, in order)")]
    [SerializeField] private Slot[] slots = new Slot[0];

    [Header("Other parts (optional)")]
    [Tooltip("The hint line above the hotbar.")]
    [SerializeField] private TMP_Text hint;
    [Tooltip("The mic / radio line.")]
    [SerializeField] private TMP_Text voiceStatus;

    [Header("Selected slot")]
    [Tooltip("On = the game colours the frames. Off = the frames keep their own colour.")]
    [SerializeField] private bool tintFrames = true;
    [SerializeField] private Color normalColour = new Color(0.08f, 0.07f, 0.07f, 0.75f);
    [SerializeField] private Color selectedColour = new Color(0.85f, 0.64f, 0.25f, 0.95f);
    [Tooltip("Optional. The frame picture of the selected slot (the other slots keep their own picture).")]
    [SerializeField] private Sprite selectedPicture;
    [SerializeField] private Color textColour = new Color(0.96f, 0.94f, 0.93f, 1f);
    [SerializeField] private Color selectedTextColour = Color.black;

    [Header("Item icons (optional)")]
    [SerializeField] private List<ItemIcon> itemIcons = new List<ItemIcon>();
    [Tooltip("An item that has an icon shows no name (state words like ON / OFF / x3 stay).")]
    [SerializeField] private bool hideNameWhenIconShown = true;

    [Tooltip("Draw order. The Esc menu is 50.")]
    [SerializeField] private int sortingOrder = 50;
    [Tooltip("Off (as before) = stays on screen while a full-screen panel is open (shop, storage, results, work sheet). On = hidden with the rest of the HUD.")]
    [SerializeField] private bool hideWithRestOfHud;

    private Sprite[] normalPictures;

    public int SlotCount => slots != null ? slots.Length : 0;
    public TMP_Text Hint => hint;
    public TMP_Text VoiceStatus => voiceStatus;
    public bool HideNameWhenIconShown => hideNameWhenIconShown;

    /// <summary>The hotbar inside the live GAME HUD (also when switched off) with enough usable slots, or null.</summary>
    public static HotbarView Find(int neededSlots)
    {
        PersistentHUD hud = PersistentHUD.Instance;
        if (hud == null) return null;
        HotbarView view = hud.GetComponentInChildren<HotbarView>(true);
        if (view == null || view.SlotCount < neededSlots) return null;
        for (int i = 0; i < neededSlots; i++)
            if (view.slots[i] == null || view.slots[i].item == null) return null; // the item text is the one part the game needs
        return view;
    }

    private void Awake()
    {
        // Lift the hotbar above the sanity dark edges (same trick as SanityBarUI).
        if (!TryGetComponent(out Canvas canvas)) canvas = gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;

        // The GAME HUD fades out as a whole while a full-screen panel is open (PersistentHUD.PushHidden).
        // The old code-made version was its own canvas and stayed visible: keep that unless asked otherwise.
        if (!TryGetComponent(out CanvasGroup group)) group = gameObject.AddComponent<CanvasGroup>();
        group.ignoreParentGroups = !hideWithRestOfHud;
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    public void Show(bool show)
    {
        if (gameObject.activeSelf != show) gameObject.SetActive(show);
    }

    /// <summary>The picture for an item, or null (no icon).</summary>
    public Sprite IconOf(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        for (int i = 0; i < itemIcons.Count; i++)
            if (itemIcons[i] != null && itemIcons[i].itemId == itemId) return itemIcons[i].picture;
        return null;
    }

    /// <summary>Writes one slot. <paramref name="words"/> = rich text for the item line.</summary>
    public void SetSlot(int index, bool selected, string words, Sprite icon)
    {
        if (index < 0 || index >= SlotCount) return;
        Slot s = slots[index];
        if (s.frame != null)
        {
            if (tintFrames) s.frame.color = selected ? selectedColour : normalColour;
            if (selectedPicture != null)
            {
                if (normalPictures == null) // remember every slot's own picture before the first swap
                {
                    normalPictures = new Sprite[slots.Length];
                    for (int i = 0; i < slots.Length; i++)
                        normalPictures[i] = slots[i] != null && slots[i].frame != null ? slots[i].frame.sprite : null;
                }
                Sprite want = selected ? selectedPicture : normalPictures[index];
                if (s.frame.sprite != want) s.frame.sprite = want;
            }
        }
        if (s.item != null)
        {
            if (s.item.text != words) s.item.text = words;
            s.item.color = selected ? selectedTextColour : textColour;
        }
        if (s.icon != null)
        {
            bool show = icon != null;
            if (show && s.icon.sprite != icon) s.icon.sprite = icon;
            if (s.icon.enabled != show) s.icon.enabled = show;
        }
    }

    /// <summary>25 Nov: the battery bar under a slot. Hidden when <paramref name="show"/> is false.</summary>
    public void SetCharge(int index, bool show, float value, Color colour)
    {
        if (index < 0 || index >= SlotCount || slots[index] == null) return;
        Slot s = slots[index];
        if (show && (s.chargeFrame == null || s.chargeFill == null)) MakeCharge(s);
        if (s.chargeFrame == null || s.chargeFill == null) return;
        GameObject box = s.chargeFrame.gameObject;
        if (box.activeSelf != show) box.SetActive(show);
        if (!s.chargeFill.transform.IsChildOf(box.transform) && s.chargeFill.gameObject.activeSelf != show)
            s.chargeFill.gameObject.SetActive(show);
        if (!show) return;
        if (s.chargeFill.color != colour) s.chargeFill.color = colour;
        value = Mathf.Clamp01(value);
        if (Mathf.Approximately(value, s.chargeShown)) return;
        s.chargeShown = value;
        if (s.chargeFill.type == Image.Type.Filled && s.chargeFill.sprite != null) { s.chargeFill.fillAmount = value; return; }
        RectTransform rt = s.chargeFill.rectTransform;
        if (s.chargeFullMax < 0f) s.chargeFullMax = rt.anchorMax.x;
        Vector2 max = rt.anchorMax;
        max.x = Mathf.Lerp(rt.anchorMin.x, s.chargeFullMax, value);
        rt.anchorMax = max;
    }

    /// <summary>25 Nov: a thin bar right under the slot box, styled like the Flashlight Battery bar.</summary>
    private static void MakeCharge(Slot s)
    {
        RectTransform under = s.frame != null ? s.frame.rectTransform : s.item != null ? s.item.rectTransform.parent as RectTransform : null;
        if (under == null) return;
        HudBar style = HudBar.Find(HudBar.Flashlight);

        var boxGo = new GameObject("Battery Bar", typeof(RectTransform));
        boxGo.transform.SetParent(under, false);
        var box = (RectTransform)boxGo.transform;
        box.anchorMin = new Vector2(0f, 0f);
        box.anchorMax = new Vector2(1f, 0f);
        box.pivot = new Vector2(0.5f, 1f);
        box.anchoredPosition = new Vector2(0f, -4f);
        box.sizeDelta = new Vector2(0f, 10f);
        Image frame = boxGo.AddComponent<Image>();
        frame.raycastTarget = false;
        Copy(style != null ? style.Frame : null, frame, new Color(0.2f, 0.2f, 0.2f, 0.9f));
        if (frame.type == Image.Type.Filled) frame.type = Image.Type.Simple;

        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(box, false);
        var fill = (RectTransform)fillGo.transform;
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = new Vector2(2f, 2f);
        fill.offsetMax = new Vector2(-2f, -2f);
        Image fillImage = fillGo.AddComponent<Image>();
        fillImage.raycastTarget = false;
        Copy(style != null ? style.Fill : null, fillImage, new Color(0.95f, 0.85f, 0.45f));
        if (fillImage.type == Image.Type.Filled && fillImage.sprite != null)
        {
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = 0;
        }
        else fillImage.type = Image.Type.Simple;

        s.chargeFrame = frame;
        s.chargeFill = fillImage;
        s.chargeFullMax = -1f;
        s.chargeShown = -1f;
    }

    private static void Copy(Image from, Image to, Color fallback)
    {
        if (from == null) { to.color = fallback; return; }
        to.sprite = from.sprite;
        to.type = from.type;
        to.color = from.color;
        to.material = from.material;
        to.pixelsPerUnitMultiplier = from.pixelsPerUnitMultiplier;
    }

    public void SetHint(string text)
    {
        if (hint != null && hint.text != text) hint.text = text;
    }

    public void SetVoiceStatus(string text)
    {
        if (voiceStatus != null && voiceStatus.text != text) voiceStatus.text = text;
    }

#if UNITY_EDITOR
    /// <summary>Editor tool (GameHudSetup): fill the slots of a new hotbar.</summary>
    public void EditorSetup(Slot[] newSlots, TMP_Text hintText, TMP_Text voiceText, List<string> itemIds)
    {
        slots = newSlots;
        hint = hintText;
        voiceStatus = voiceText;
        EditorAddItemIds(itemIds);
    }

    /// <summary>Editor tool: one empty icon row per item that has none yet (existing rows are kept).</summary>
    public int EditorAddItemIds(List<string> itemIds)
    {
        int added = 0;
        foreach (string id in itemIds)
        {
            bool found = false;
            for (int i = 0; i < itemIcons.Count; i++) if (itemIcons[i] != null && itemIcons[i].itemId == id) { found = true; break; }
            if (found) continue;
            itemIcons.Add(new ItemIcon { itemId = id });
            added++;
        }
        return added;
    }
#endif
}
