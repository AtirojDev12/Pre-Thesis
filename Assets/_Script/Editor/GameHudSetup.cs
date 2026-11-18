using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
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
///   "Model Slots": one entry per 3D model prefab the players see (items, rule props). The UI Hub
///                lists them under "3D models" (17 Nov).
///
///   Tools > Pre-Thesis > UI > GAME HUD: Keep One Copy (Lobby Only)   (17 Nov)
///   There is ONE GAME HUD in the project: in the Lobby scene. It stays alive and follows the
///   players into the map (PersistentHUD). The tool puts a clean copy in the Lobby (if missing)
///   and deletes the copies from the other game scenes (MainMenu, Cinema_GamePlay). Pressing
///   Play straight in a map scene still has a HUD: the Editor adds one for that run (PersistentHUD).
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
    public const string ModelsGroup = "Model Slots";
    /// <summary>Folders whose prefabs are 3D models the players see.</summary>
    private static readonly string[] ModelFolders = { "Assets/Resources/Items", "Assets/Prefab/RuleMockups" };
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
            AddModelSlots(root, log);
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

    // ---- One look in every scene (16 Nov) ------------------------------------------------------

    /// <summary>The GAME HUD prefab copies in the open scene(s).</summary>
    private static List<GameObject> SceneCopies()
    {
        var list = new List<GameObject>();
        foreach (PersistentHUD hud in Object.FindObjectsByType<PersistentHUD>(FindObjectsInactive.Include))
        {
            GameObject go = hud.gameObject;
            if (!go.scene.IsValid() || !PrefabUtility.IsOutermostPrefabInstanceRoot(go)) continue;
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == PrefabPath) list.Add(go);
        }
        return list;
    }

    private const string MainMenuScene = "Assets/Scenes/MainMenu.unity";
    private const string LobbyScene = "Assets/Scenes/Lobby.unity";

    [MenuItem("Tools/Pre-Thesis/UI/GAME HUD: Keep One Copy (Lobby Only)", priority = 3)]
    private static void KeepOnlyLobbyCopy()
    {
        const string title = "GAME HUD: one copy";
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(title, "Stop Play mode first.", "OK"); return; }
        if (PrefabStageUtility.GetCurrentPrefabStage() != null)
        {
            EditorUtility.DisplayDialog(title, "A prefab is open. Save it, go back to the scene (the < arrow), then run this again.", "OK");
            return;
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null || AssetDatabase.LoadAssetAtPath<SceneAsset>(LobbyScene) == null)
        {
            EditorUtility.DisplayDialog(title, $"Cannot find {PrefabPath} or {LobbyScene}.", "OK");
            return;
        }

        // The game's scenes (Build Settings), without the Lobby.
        var others = new List<string>();
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScene) != null) others.Add(MainMenuScene);
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            if (!string.IsNullOrEmpty(s.path) && s.path != LobbyScene && !others.Contains(s.path) && AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path) != null)
                others.Add(s.path);

        if (!EditorUtility.DisplayDialog(title,
                "This will:\n" +
                "1. Put a clean GAME HUD in the Lobby scene, if it has none.\n" +
                "2. Delete the GAME HUD from these scenes:\n   " + string.Join("\n   ", others) + "\n" +
                "3. Save those scenes and come back to this scene.\n\n" +
                "After this the Lobby holds the only GAME HUD. It follows the players into the map.", "Do it", "Cancel")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        string back = SceneManager.GetActiveScene().path;
        var report = new StringBuilder();

        // The Lobby first: the one copy must exist before the others go.
        Scene lobby = EditorSceneManager.OpenScene(LobbyScene, OpenSceneMode.Single);
        if (Object.FindObjectsByType<PersistentHUD>(FindObjectsInactive.Include).Length == 0)
        {
            var copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, lobby);
            copy.name = prefab.name;
            EditorSceneManager.MarkSceneDirty(lobby);
            EditorSceneManager.SaveScene(lobby);
            report.AppendLine("Lobby: a clean GAME HUD was added.");
        }
        else report.AppendLine("Lobby: already has a GAME HUD. Not changed.");

        foreach (string path in others)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            int removed = 0;
            foreach (GameObject copy in SceneCopies())
            {
                Object.DestroyImmediate(copy);
                removed++;
            }
            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                report.AppendLine($"{scene.name}: GAME HUD removed.");
            }
            else report.AppendLine($"{scene.name}: had none.");
        }

        if (!string.IsNullOrEmpty(back) && AssetDatabase.LoadAssetAtPath<SceneAsset>(back) != null) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
        Debug.Log("[GameHudSetup] " + report);
        EditorUtility.DisplayDialog(title, report +
            "\nTest 1: start from MainMenu, go to the lobby, then into a match.\n" +
            "Test 2: press Play in Cinema_GamePlay. The Console says a GAME HUD was added for this run.", "OK");
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

    // ---- 3D models (17 Nov) --------------------------------------------------------------------

    /// <summary>One slot per model prefab that has none yet. Existing slots are never changed.</summary>
    private static void AddModelSlots(Transform root, StringBuilder log)
    {
        var have = new HashSet<GameObject>();
        foreach (ModelSlot slot in root.GetComponentsInChildren<ModelSlot>(true))
            if (slot.ModelPrefab != null) have.Add(slot.ModelPrefab);

        var folders = new List<string>();
        foreach (string folder in ModelFolders) if (AssetDatabase.IsValidFolder(folder)) folders.Add(folder);
        if (folders.Count == 0) return;

        Transform group = null;
        int added = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", folders.ToArray()))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || have.Contains(prefab)) continue;
            if (prefab.GetComponentInChildren<Renderer>(true) == null) continue; // sound-only props have nothing to look at
            if (group == null)
            {
                group = FindOrMakeGroup(root, ModelsGroup);
                group.gameObject.SetActive(false); // a list only: never drawn
            }
            AddModelSlot(group, prefab, path.StartsWith("Assets/Resources/Items") ? "Item" : "Rule prop");
            have.Add(prefab);
            added++;
        }
        if (added > 0) log.AppendLine($"+ Model Slots: {added} model prefab(s) listed (UI Hub > 3D models)");
    }

    public static ModelSlot AddModelSlot(Transform group, GameObject prefab, string kind)
    {
        RectTransform rt = Rect(group, prefab.name);
        ModelSlot slot = rt.gameObject.AddComponent<ModelSlot>();
        slot.EditorSetup(kind + ": " + ObjectNames.NicifyVariableName(prefab.name.Replace("RM_", "").Replace("World", "World ")).Trim(), prefab, "");
        return slot;
    }

    /// <summary>The hub's "Add a model": lists one more prefab in the GAME HUD.</summary>
    public static void AddModelSlotToPrefab(GameObject prefab)
    {
        if (prefab == null || EditorApplication.isPlaying) return;
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == PrefabPath)
        {
            EditorUtility.DisplayDialog(Title, "The GAME HUD prefab is open. Save it, go back to the scene, then try again.", "OK");
            return;
        }
        using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
        {
            Transform root = scope.prefabContentsRoot.transform;
            foreach (ModelSlot slot in root.GetComponentsInChildren<ModelSlot>(true))
                if (slot.ModelPrefab == prefab) return; // already listed
            Transform group = FindOrMakeGroup(root, ModelsGroup);
            group.gameObject.SetActive(false);
            AddModelSlot(group, prefab, "Model");
        }
        AssetDatabase.SaveAssets();
    }

    public static void RemoveModelSlotFromPrefab(GameObject prefab)
    {
        if (prefab == null || EditorApplication.isPlaying) return;
        using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
            foreach (ModelSlot slot in scope.prefabContentsRoot.GetComponentsInChildren<ModelSlot>(true))
                if (slot.ModelPrefab == prefab) Object.DestroyImmediate(slot.gameObject);
        AssetDatabase.SaveAssets();
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
