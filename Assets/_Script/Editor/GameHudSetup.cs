using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 16 Nov 2026 (Mr.k): puts the HUD parts that used to be built by code INTO the GAME HUD prefab,
/// as real children a designer can edit.
///
///   Tools > Pre-Thesis > UI > GAME HUD: Add Missing Parts (bars + hotbar)
///
///   "HUD Bars" : Mic Noise + Game Noise (a lying and a standing version of each),
///                Flashlight Battery, Throw Charge, Task Progress, Walkie Battery.
///                Each bar = HudBar with a separate Frame picture and Fill picture.
///   "Hotbar"   : 4 slots (Frame, Key, Item, Icon) + Hint + Voice Status, and the Item Icons list.
///
/// SAFE: it only ADDS what is missing. It never deletes, moves or restyles a part that is
/// already in the prefab, so run it again any time (e.g. after a new item or bar was added).
/// The parts start with the same look and place as the old code-made ones.
/// </summary>
public static class GameHudSetup
{
    public const string PrefabPath = "Assets/Prefab/GAME HUD.prefab";
    public const string BarsGroup = "HUD Bars";
    public const string HotbarName = "Hotbar";
    private const string Title = "GAME HUD parts";

    private static readonly Color Panel = new Color(0.08f, 0.07f, 0.07f, 0.8f);
    private static readonly Color Track = new Color(0.2f, 0.2f, 0.2f, 1f);
    private static readonly Color Gold = new Color(0.85f, 0.64f, 0.25f, 1f);
    private static readonly Color Quiet = new Color(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Color TextColour = new Color(0.96f, 0.94f, 0.93f, 1f);

    /// <summary>True when the GAME HUD prefab already has every part this tool makes.</summary>
    public static bool HasEveryPart()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return false;
        var ids = new HashSet<string>();
        foreach (HudBar b in prefab.GetComponentsInChildren<HudBar>(true)) ids.Add(b.BarId);
        foreach (string id in BarIds) if (!ids.Contains(id)) return false;
        return prefab.GetComponentInChildren<HotbarView>(true) != null;
    }

    private static readonly string[] BarIds =
    {
        HudBar.MicNoiseHorizontal, HudBar.GameNoiseHorizontal, HudBar.MicNoiseVertical, HudBar.GameNoiseVertical,
        HudBar.Flashlight, HudBar.ThrowCharge, HudBar.TaskProgress, HudBar.WalkieBattery,
    };

    [MenuItem("Tools/Pre-Thesis/UI/GAME HUD: Add Missing Parts (bars + hotbar)", priority = 1)]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            EditorUtility.DisplayDialog(Title, "Cannot find " + PrefabPath + ".", "OK");
            return;
        }
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == PrefabPath)
        {
            EditorUtility.DisplayDialog(Title, "The GAME HUD prefab is open. Save it, go back to the scene (the < arrow), then run this again.", "OK");
            return;
        }

        var log = new StringBuilder();
        using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
        {
            Transform root = scope.prefabContentsRoot.transform;
            AddBars(root, log);
            AddHotbar(root, log);
        }
        RemoveOldSkins(log);
        AssetDatabase.SaveAssets();

        if (log.Length == 0) log.AppendLine("Nothing was missing. The GAME HUD already has every part.");
        Debug.Log("[GameHudSetup]\n" + log);
        EditorUtility.DisplayDialog(Title, log +
            "\nOpen the GAME HUD prefab (or the UI Hub) to restyle them:\n" +
            "- A bar: replace the Frame picture and the Fill picture on their own.\n" +
            "- The hotbar: slots, texts, and the Item Icons list.\n" +
            "The game switches these parts on and off itself.", "OK");
    }

    /// <summary>
    /// The hotbar and the bars were code-built screens before. Their old auto-made prefabs
    /// (Assets/UI Skins) and their empty entries in UI Layout are not used any more: remove them.
    /// An entry that still holds saved changes is kept (and reported).
    /// </summary>
    private static void RemoveOldSkins(StringBuilder log)
    {
        int removed = 0;
        foreach (string screen in UiScreenCatalog.MovedIntoGameHud)
        {
            string skin = UiSkinTools.SkinPath(screen), original = UiSkinTools.OriginalPath(screen);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(skin) != null && AssetDatabase.DeleteAsset(skin)) removed++;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(original) != null) AssetDatabase.DeleteAsset(original);
        }
        if (removed > 0) log.AppendLine($"- Removed {removed} old auto-made prefab(s) in Assets/UI Skins (those screens are GAME HUD parts now).");

        var data = AssetDatabase.LoadAssetAtPath<UiLayoutData>("Assets/Resources/UI/UI Layout.asset");
        if (data == null) return;
        int entries = 0;
        for (int i = data.screens.Count - 1; i >= 0; i--)
        {
            UiScreenLook look = data.screens[i];
            if (look == null || !UiScreenCatalog.WasMovedIntoGameHud(look.screenName)) continue;
            if (look.elements.Count > 0 || look.added.Count > 0 || look.design != null)
            {
                log.AppendLine($"! \"{look.screenName}\" still has saved changes in UI Layout. They were made for the old code-made screen and are kept; redo them on the GAME HUD part.");
                continue;
            }
            data.screens.RemoveAt(i);
            entries++;
        }
        if (entries == 0) return;
        EditorUtility.SetDirty(data);
        data.MarkChanged();
        log.AppendLine($"- Removed {entries} empty old entr{(entries == 1 ? "y" : "ies")} from UI Layout.");
    }

    // ---- Bars -------------------------------------------------------------------------------

    private static void AddBars(Transform root, StringBuilder log)
    {
        var have = new HashSet<string>();
        foreach (HudBar b in root.GetComponentsInChildren<HudBar>(true)) have.Add(b.BarId);

        bool anyMissing = false;
        foreach (string id in BarIds) if (!have.Contains(id)) anyMissing = true;
        if (!anyMissing) return;
        Transform group = FindOrMakeGroup(root, BarsGroup);

        // Noise bars: top-right corner. Lying pair on top, standing pair under it (the player picks one direction).
        if (!have.Contains(HudBar.MicNoiseHorizontal))
        { NoiseBar(group, "Mic Noise (lying)", HudBar.MicNoiseHorizontal, "MIC", false, new Vector2(-24f, -24f)); log.AppendLine("+ Mic Noise (lying)"); }
        if (!have.Contains(HudBar.GameNoiseHorizontal))
        { NoiseBar(group, "Game Noise (lying)", HudBar.GameNoiseHorizontal, "GAME", false, new Vector2(-24f, -64f)); log.AppendLine("+ Game Noise (lying)"); }
        if (!have.Contains(HudBar.MicNoiseVertical))
        { NoiseBar(group, "Mic Noise (standing)", HudBar.MicNoiseVertical, "MIC", true, new Vector2(-88f, -24f)); log.AppendLine("+ Mic Noise (standing)"); }
        if (!have.Contains(HudBar.GameNoiseVertical))
        { NoiseBar(group, "Game Noise (standing)", HudBar.GameNoiseVertical, "GAME", true, new Vector2(-24f, -24f)); log.AppendLine("+ Game Noise (standing)"); }

        if (!have.Contains(HudBar.Flashlight))
        {
            // Old FlashlightHUD: bottom right, label + lying bar + hint.
            RectTransform bar = Part(group, "Flashlight Battery", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(300f, 110f), new Vector2(-30f, 40f));
            Picture(bar, "Panel", Panel, true);
            TMP_Text label = Words(bar, "Label", "Flashlight  ON  100%", 20f, TextAlignmentOptions.Center, new Vector2(280f, 30f), new Vector2(0f, 30f));
            Image frame = Picture(Centre(bar, "Frame", new Vector2(264f, 18f), Vector2.zero), Track);
            Image fill = Fill(Centre(bar, "Fill Area", new Vector2(260f, 14f), Vector2.zero), new Color(0.95f, 0.85f, 0.45f));
            TMP_Text hint = Words(bar, "Hint", "[F] on/off", 17f, TextAlignmentOptions.Center, new Vector2(290f, 30f), new Vector2(0f, -32f));
            Finish(bar, HudBar.Flashlight, frame, fill, null, label, hint, HudBar.Direction.LeftToRight, 51);
            log.AppendLine("+ Flashlight Battery");
        }
        if (!have.Contains(HudBar.ThrowCharge))
        {
            // Old ThrowChargeHUD: left side, item name + standing bar + hint.
            RectTransform bar = Part(group, "Throw Charge", new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(220f, 235f), new Vector2(28f, 45f));
            Picture(bar, "Panel", new Color(0.08f, 0.07f, 0.07f, 0.85f), true);
            TMP_Text label = Words(bar, "Item", "Item", 20f, TextAlignmentOptions.Center, new Vector2(200f, 35f), new Vector2(0f, 95f));
            Image frame = Picture(Centre(bar, "Frame", new Vector2(28f, 135f), Vector2.zero), Track);
            Image fill = Fill(Centre(bar, "Fill Area", new Vector2(24f, 131f), Vector2.zero), Gold);
            TMP_Text hint = Words(bar, "Hint", "Release Q to throw", 17f, TextAlignmentOptions.Center, new Vector2(210f, 40f), new Vector2(0f, -92f));
            Finish(bar, HudBar.ThrowCharge, frame, fill, null, label, hint, HudBar.Direction.BottomToTop, 51);
            log.AppendLine("+ Throw Charge");
        }
        if (!have.Contains(HudBar.TaskProgress))
        {
            // Old ChoreProgressHUD: bottom centre, text + lying bar.
            RectTransform bar = Part(group, "Task Progress", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(380f, 64f), new Vector2(0f, 190f));
            Picture(bar, "Panel", new Color(0.08f, 0.07f, 0.07f, 0.85f), true);
            Image frame = Picture(Centre(bar, "Frame", new Vector2(364f, 16f), new Vector2(0f, -14f)), Track);
            Image fill = Fill(Centre(bar, "Fill Area", new Vector2(360f, 12f), new Vector2(0f, -14f)), Gold);
            TMP_Text label = Words(bar, "Label", "Sweeping...  (keep holding E)", 20f, TextAlignmentOptions.Center, new Vector2(360f, 30f), new Vector2(0f, 12f));
            Finish(bar, HudBar.TaskProgress, frame, fill, null, label, null, HudBar.Direction.LeftToRight, 51);
            log.AppendLine("+ Task Progress");
        }
        if (!have.Contains(HudBar.WalkieBattery))
        {
            // New (the free Walkie-Talkie's battery). Above the flashlight bar.
            RectTransform bar = Part(group, "Walkie Battery", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(300f, 70f), new Vector2(-30f, 160f));
            Picture(bar, "Panel", Panel, true);
            TMP_Text label = Words(bar, "Label", "Walkie-Talkie  100%", 18f, TextAlignmentOptions.Center, new Vector2(280f, 26f), new Vector2(0f, 14f));
            Image frame = Picture(Centre(bar, "Frame", new Vector2(264f, 16f), new Vector2(0f, -14f)), Track);
            Image fill = Fill(Centre(bar, "Fill Area", new Vector2(260f, 12f), new Vector2(0f, -14f)), Quiet);
            Finish(bar, HudBar.WalkieBattery, frame, fill, null, label, null, HudBar.Direction.LeftToRight, 51);
            log.AppendLine("+ Walkie Battery (used when the free Walkie-Talkie is added)");
        }
    }

    /// <summary>One noise bar: Frame (the box), Fill Area > Fill + Loud Line, Label. Anchored to the top-right corner.</summary>
    private static void NoiseBar(Transform group, string name, string id, string words, bool standing, Vector2 position)
    {
        Vector2 size = standing ? new Vector2(58f, 196f) : new Vector2(282f, 34f);
        RectTransform bar = Part(group, name, new Vector2(1f, 1f), new Vector2(1f, 1f), size, position);

        RectTransform frameRect = Rect(bar, "Frame");
        RectTransform area = Rect(bar, "Fill Area");
        TMP_Text label;
        if (standing)
        {
            Inset(frameRect, 14f, 26f, 14f, 4f);  // left, bottom, right, top
            Inset(area, 18f, 30f, 18f, 8f);
            label = Words(bar, "Label", words, 16f, TextAlignmentOptions.Center, new Vector2(58f, 22f), Vector2.zero);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            label.rectTransform.pivot = new Vector2(0.5f, 0f);
            label.rectTransform.anchoredPosition = new Vector2(0f, 2f);
        }
        else
        {
            Inset(frameRect, 64f, 4f, 10f, 4f);
            Inset(area, 68f, 8f, 14f, 8f);
            label = Words(bar, "Label", words, 16f, TextAlignmentOptions.MidlineRight, new Vector2(54f, 30f), Vector2.zero);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            label.rectTransform.anchoredPosition = new Vector2(4f, 0f);
        }
        Image frame = Picture(frameRect, new Color(0.06f, 0.05f, 0.05f, 0.7f));
        Image fill = Fill(area, Quiet);

        // The LOUD line: the game moves it to the loud level.
        RectTransform line = Rect(area, "Loud Line");
        line.pivot = new Vector2(0.5f, 0.5f);
        line.anchorMin = line.anchorMax = standing ? new Vector2(0.5f, 0.7f) : new Vector2(0.7f, 0.5f);
        line.sizeDelta = standing ? new Vector2(30f, 2f) : new Vector2(2f, 26f);
        line.anchoredPosition = Vector2.zero;
        Picture(line, new Color(1f, 1f, 1f, 0.85f));

        Finish(bar, id, frame, fill, line, label, null, standing ? HudBar.Direction.BottomToTop : HudBar.Direction.LeftToRight, 49);
    }

    private static void Finish(RectTransform bar, string id, Image frame, Image fill, RectTransform marker, TMP_Text label, TMP_Text hint,
        HudBar.Direction direction, int order)
    {
        AddSortedCanvas(bar.gameObject, order);
        bar.gameObject.AddComponent<HudBar>().EditorSetup(id, frame, fill, marker, label, hint, direction, order);
    }

    // ---- Hotbar -----------------------------------------------------------------------------

    private static void AddHotbar(Transform root, StringBuilder log)
    {
        var ids = new List<string>();
        foreach (ItemCatalog.ItemInfo item in ItemCatalog.All) if (item != null && !string.IsNullOrEmpty(item.id)) ids.Add(item.id);

        HotbarView existing = root.GetComponentInChildren<HotbarView>(true);
        if (existing != null)
        {
            int added = existing.EditorAddItemIds(ids);
            if (added > 0) log.AppendLine($"+ Hotbar: {added} new row(s) in the Item Icons list");
            return;
        }

        const float size = 96f, gap = 10f;
        int count = PlayerInventory.SlotCount;
        float total = count * size + (count - 1) * gap;

        RectTransform bar = Rect(root, HotbarName);
        Stretch(bar);
        var slots = new HotbarView.Slot[count];
        for (int i = 0; i < count; i++)
        {
            RectTransform slot = Rect(bar, "Slot " + (i + 1));
            slot.anchorMin = slot.anchorMax = new Vector2(0.5f, 0f);
            slot.pivot = new Vector2(0f, 0f);
            slot.sizeDelta = new Vector2(size, size);
            slot.anchoredPosition = new Vector2(-total / 2f + i * (size + gap), 24f);
            Image frame = Picture(slot, new Color(0.08f, 0.07f, 0.07f, 0.75f));

            RectTransform iconRect = Rect(slot, "Icon");
            Inset(iconRect, 16f, 22f, 16f, 10f);
            Image icon = Picture(iconRect, Color.white);
            icon.preserveAspect = true;
            icon.enabled = false; // shown when the item has a picture

            TMP_Text key = StretchWords(slot, "Key", (i + 1).ToString(), 18f, TextAlignmentOptions.TopLeft, 6f, 0f, 0f, 4f);
            TMP_Text item = StretchWords(slot, "Item", "", 17f, TextAlignmentOptions.Center, 4f, 4f, 4f, 18f);
            item.enableAutoSizing = true;
            item.fontSizeMin = 10f;
            item.fontSizeMax = 17f;
            item.richText = true;
            slots[i] = new HotbarView.Slot { frame = frame, key = key, item = item, icon = icon };
        }

        TMP_Text hint = Words(bar, "Hint", "", 20f, TextAlignmentOptions.Bottom, new Vector2(900f, 30f), Vector2.zero);
        hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        hint.rectTransform.pivot = new Vector2(0.5f, 0f);
        hint.rectTransform.anchoredPosition = new Vector2(0f, 24f + size + 10f);

        TMP_Text voice = Words(bar, "Voice Status", "", 20f, TextAlignmentOptions.BottomLeft, new Vector2(520f, 60f), Vector2.zero);
        voice.rectTransform.anchorMin = voice.rectTransform.anchorMax = Vector2.zero;
        voice.rectTransform.pivot = Vector2.zero;
        voice.rectTransform.anchoredPosition = new Vector2(24f, 24f);

        AddSortedCanvas(bar.gameObject, 50);
        bar.gameObject.AddComponent<HotbarView>().EditorSetup(slots, hint, voice, ids);
        log.AppendLine($"+ Hotbar ({count} slots, hint, voice status, Item Icons list with {ids.Count} item(s))");
    }

    // ---- Small builders ---------------------------------------------------------------------

    private static Transform FindOrMakeGroup(Transform root, string name)
    {
        Transform t = root.Find(name);
        if (t != null) return t;
        RectTransform group = Rect(root, name);
        Stretch(group);
        return group;
    }

    private static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    /// <summary>A part anchored to one point of the screen.</summary>
    private static RectTransform Part(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position)
    {
        RectTransform rt = Rect(parent, name);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
        return rt;
    }

    private static RectTransform Centre(Transform parent, string name, Vector2 size, Vector2 position)
    {
        RectTransform rt = Rect(parent, name);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
        return rt;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void Inset(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    private static Image Picture(RectTransform rt, Color colour)
    {
        Image image = rt.gameObject.AddComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>A full-size picture child (the dark panel behind a bar).</summary>
    private static Image Picture(RectTransform parent, string name, Color colour, bool stretch)
    {
        RectTransform rt = Rect(parent, name);
        if (stretch) Stretch(rt);
        return Picture(rt, colour);
    }

    /// <summary>The Fill picture inside a Fill Area. Its full size = the whole area.</summary>
    private static Image Fill(RectTransform area, Color colour)
    {
        RectTransform rt = Rect(area, "Fill");
        Stretch(rt);
        return Picture(rt, colour);
    }

    private static TMP_Text Words(Transform parent, string name, string text, float fontSize, TextAlignmentOptions align, Vector2 size, Vector2 position)
    {
        RectTransform rt = Centre(parent, name, size, position);
        return MakeText(rt, text, fontSize, align);
    }

    private static TMP_Text StretchWords(Transform parent, string name, string text, float fontSize, TextAlignmentOptions align,
        float left, float bottom, float right, float top)
    {
        RectTransform rt = Rect(parent, name);
        Inset(rt, left, bottom, right, top);
        return MakeText(rt, text, fontSize, align);
    }

    private static TMP_Text MakeText(RectTransform rt, string text, float fontSize, TextAlignmentOptions align)
    {
        TextMeshProUGUI label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.color = TextColour;
        label.alignment = align;
        label.raycastTarget = false;
        label.richText = true;
        return label;
    }

    /// <summary>A nested canvas so the part draws above the sanity dark edges (same as the Sanity Bar).</summary>
    private static void AddSortedCanvas(GameObject go, int order)
    {
        Canvas canvas = go.GetComponent<Canvas>();
        if (canvas == null) canvas = go.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = order;
    }
}
