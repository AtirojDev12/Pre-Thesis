using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 8 Nov 2026 (Mr.k): Inspector of the UI Hub (on GAME HUD). Every game UI screen in one list.
/// Click a screen: change its pictures, texts and 3D models right there, or press Edit to open
/// it as a prefab (screens built by code get a real prefab automatically, see UiSkinTools).
/// </summary>
[CustomEditor(typeof(UiHub))]
public sealed class UiHubEditor : Editor
{
    private const string HudPath = "Assets/Prefab/GAME HUD.prefab";
    private const int RowsPerPage = 40;

    private static readonly Dictionary<string, bool> open = new Dictionary<string, bool>();
    private static readonly Dictionary<string, string> partSearch = new Dictionary<string, string>();
    private static readonly HashSet<string> showAllParts = new HashSet<string>();
    private static readonly HashSet<string> advancedOpen = new HashSet<string>();
    private static readonly HashSet<string> editOpen = new HashSet<string>();
    private static readonly HashSet<string> closedCategories = new HashSet<string>(); // 16 Nov: headings are open by default
    private static string search = "";
    private static int filter; // 0 all, 1 on screen now, 2 changed
    private static List<GameObject> uiPrefabs;
    private double nextRepaint;

    // Static: never serialized, so a script reload cannot leave one of them null (8 Nov NRE), and static helpers can use them.
    private static GUIStyle headerStyle, introStyle, smallStyle, okStyle, warnStyle, editButton, rowTitle, partLabel;

    private static bool howToOpen, settingsOpen, otherOpen, modelsOpen;
    private static readonly HashSet<string> moreOpen = new HashSet<string>();   // "More tools" per screen
    private static readonly HashSet<string> openGroups = new HashSet<string>(); // part groups per screen

    // ---- Menu --------------------------------------------------------------------------------

    [MenuItem("Tools/Pre-Thesis/UI/Open UI Hub (GAME HUD)", priority = 0)]
    private static void OpenHub()
    {
        UiChangeRecorder.EnsureData();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        if (prefab == null) { EditorUtility.DisplayDialog("UI Hub", "Can't find " + HudPath, "OK"); return; }
        if (prefab.GetComponent<UiHub>() == null)
        {
            if (Application.isPlaying) { EditorUtility.DisplayDialog("UI Hub", "Stop Play first (the UI Hub is added to the GAME HUD prefab once).", "OK"); return; }
            using (var scope = new PrefabUtility.EditPrefabContentsScope(HudPath))
                if (scope.prefabContentsRoot.GetComponent<UiHub>() == null) scope.prefabContentsRoot.AddComponent<UiHub>();
            Debug.Log("[UI Hub] Added the UI Hub to " + HudPath);
        }
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    // ---- Inspector --------------------------------------------------------------------------

    private void OnEnable()
    {
        UiChangeRecorder.Changed += Repaint;
        UiLayoutData.Changed += OnDataChanged;
        EditorApplication.update += Tick;
    }

    private void OnDisable()
    {
        UiChangeRecorder.Changed -= Repaint;
        UiLayoutData.Changed -= OnDataChanged;
        EditorApplication.update -= Tick;
    }

    private void OnDataChanged(UiLayoutData d) => Repaint();

    private void Tick()
    {
        if (!Application.isPlaying || EditorApplication.timeSinceStartup < nextRepaint) return;
        nextRepaint = EditorApplication.timeSinceStartup + 0.5;
        Repaint();
    }

    private void Styles()
    {
        if (headerStyle != null && introStyle != null && smallStyle != null && okStyle != null && warnStyle != null
            && editButton != null && rowTitle != null && partLabel != null) return;
        // 17 Nov: every label style wraps. Long words go onto a new line; nothing is cut or drawn over a button.
        headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, wordWrap = true };
        introStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
        smallStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = true };
        okStyle = new GUIStyle(EditorStyles.miniBoldLabel) { wordWrap = true, normal = { textColor = new Color(0.35f, 0.85f, 0.4f) } };
        warnStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = new Color(1f, 0.7f, 0.25f) } };
        editButton = new GUIStyle(EditorStyles.miniButton) { fontStyle = FontStyle.Bold };
        rowTitle = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
        partLabel = new GUIStyle(EditorStyles.miniBoldLabel) { wordWrap = true };
    }

    public override void OnInspectorGUI()
    {
        Styles();
        GUILayout.Label("UI Hub", headerStyle);
        GUILayout.Label("Change how every game screen looks. No code needed.", introStyle);

        howToOpen = EditorGUILayout.Foldout(howToOpen, "How to use", true);
        if (howToOpen)
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("1. Find the screen in the list below. Use the search box or open a heading.", introStyle);
                GUILayout.Label("2. Click the screen's name. Change pictures, colours, words and fonts right there. It is saved at once.", introStyle);
                GUILayout.Label("3. Press Edit to open the screen itself. Move, resize, add or delete parts, then save (Ctrl+S).", introStyle);
                GUILayout.Label("4. A screen the game builds by code: open Settings, tick \"Move code screens into GAME HUD\", press Play, show that screen once, stop Play.", introStyle);
            }

        UiLayoutData data = UiLayoutData.Load();
        if (data == null)
        {
            if (GUILayout.Button("Make the UI Layout data", GUILayout.Height(26))) data = UiChangeRecorder.EnsureData();
            if (data == null) return;
        }

        DrawSettings(data);
        DrawSaveBar();
        EditorGUILayout.Space(6);
        DrawScreens(data);
        EditorGUILayout.Space(10);
        otherOpen = EditorGUILayout.Foldout(otherOpen, "More: other UI in the project", true);
        if (otherOpen) DrawOtherUi();
    }

    /// <summary>Play mode only: edits made on live screens wait here until they are saved.</summary>
    private void DrawSaveBar()
    {
        if (!Application.isPlaying) return;
        int n = UiChangeRecorder.PendingList.Count;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUILayout.Label(n == 0
                ? "Play mode: change a screen, then press Save changes to keep it."
                : $"{n} change(s) made in Play mode are waiting. Save them, or they are lost when Play stops.", n == 0 ? smallStyle : warnStyle);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(n == 0))
                {
                    Color old = GUI.backgroundColor;
                    GUI.backgroundColor = n > 0 ? new Color(0.5f, 1f, 0.55f) : old;
                    if (GUILayout.Button("Save changes", GUILayout.Width(110), GUILayout.Height(22))) UiChangeRecorder.Save();
                    GUI.backgroundColor = old;
                    if (GUILayout.Button("Discard", GUILayout.Width(70), GUILayout.Height(22))) UiChangeRecorder.Discard();
                }
            }
        }
    }

    private void DrawSettings(UiLayoutData data)
    {
        settingsOpen = EditorGUILayout.Foldout(settingsOpen, "Settings", true);
        if (!settingsOpen) return;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUILayout.Label("Font for all UI", partLabel);
            EditorGUI.BeginChangeCheck();
            var font = (TMP_FontAsset)EditorGUILayout.ObjectField(data.fontForAllUi, typeof(TMP_FontAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(data, "UI font");
                data.fontForAllUi = font;
                Changed(data);
            }
            GUILayout.Label("Every text in every screen uses this font. Empty = each screen keeps its own fonts.", smallStyle);

            EditorGUILayout.Space(4);
            GameHudScreens.AutoCapture = WrapToggle("Move code screens into GAME HUD", GameHudScreens.AutoCapture);
            GUILayout.Label("While ticked: press Play, show a code-built screen once, stop Play. That screen becomes a child of the GAME HUD prefab. " +
                            "This tick is kept on this PC only.", smallStyle);

            EditorGUILayout.Space(4);
            UiSkinTools.AutoMake = WrapToggle("Auto-make prefabs (old way)", UiSkinTools.AutoMake);
            GUILayout.Label("Only for the few screens that cannot move into the GAME HUD.", smallStyle);
        }
    }

    // ---- Layout helpers: nothing is ever cut off (17 Nov) -----------------------------------------

    /// <summary>A tick box whose words wrap onto new lines. Clicking the words ticks it too.</summary>
    private static bool WrapToggle(string label, bool value)
    {
        var content = new GUIContent(label);
        float width = Mathf.Max(100f, EditorGUIUtility.currentViewWidth - 84f);
        float height = Mathf.Max(EditorGUIUtility.singleLineHeight, introStyle.CalcHeight(content, width));
        using (new EditorGUILayout.HorizontalScope())
        {
            Rect box = GUILayoutUtility.GetRect(16f, height, GUILayout.Width(16f));
            box.height = EditorGUIUtility.singleLineHeight;
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            value = EditorGUI.Toggle(box, value);
            EditorGUI.indentLevel = indent;
            Rect text = GUILayoutUtility.GetRect(width, height, introStyle, GUILayout.Width(width));
            if (GUI.Button(text, content, introStyle)) value = !value;
            GUILayout.FlexibleSpace();
        }
        return value;
    }

    /// <summary>
    /// Puts controls side by side and starts a NEW LINE when the next one does not fit.
    /// Call <see cref="Next"/> with the control's width just before drawing it.
    /// </summary>
    private sealed class Flow : System.IDisposable
    {
        private readonly float full, indent;
        private float left;
        private bool any;

        public Flow(float indent = 0f)
        {
            this.indent = indent;
            full = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 64f - indent);
            left = full;
            EditorGUILayout.BeginHorizontal();
            if (indent > 0f) GUILayout.Space(indent);
        }

        public void Next(float width)
        {
            if (any && width > left)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                if (indent > 0f) GUILayout.Space(indent);
                left = full;
            }
            any = true;
            left -= width + 4f;
        }

        public void Dispose()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
    }

    private static float ButtonWidth(string label) => EditorStyles.miniButton.CalcSize(new GUIContent(label)).x + 2f;

    // ---- Screen list ----------------------------------------------------------------------------

    private void DrawScreens(UiLayoutData data)
    {
        GUILayout.Label("Screens", headerStyle);
        search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField); // own line: full width
        filter = GUILayout.Toolbar(filter, new[] { "All", "On screen now", "Changed" });

        var names = new List<string>();
        foreach (UiScreenCatalog.Entry e in UiScreenCatalog.All) names.Add(e.screen);
        foreach (UiScreenLook s in data.screens) if (s != null && !names.Contains(s.screenName)) names.Add(s.screenName);
        foreach (UiLayoutRuntime.LiveScreen s in UiLayoutRuntime.Live) if (s.root != null && !names.Contains(s.name)) names.Add(s.name);

        // 16 Nov: one heading per category. Screens the catalog does not know go under "Other".
        int shown = 0;
        foreach (string category in UiScreenCatalog.Categories)
        {
            if (category == UiScreenCatalog.Models) { shown += DrawModels(); continue; } // 17 Nov: listed from the GAME HUD's Model Slots
            var inCategory = new List<string>();
            foreach (string name in names)
            {
                if (UiScreenCatalog.WasMovedIntoGameHud(name) && LiveRoot(name) == null) continue; // old code-made screen, now a GAME HUD part
                UiScreenCatalog.Entry entry = UiScreenCatalog.Find(name);
                if ((entry != null ? entry.category : UiScreenCatalog.Other) != category) continue;
                UiScreenLook look = data.FindScreen(name);
                bool live = LiveRoot(name) != null;
                if (filter == 1 && !live) continue;
                if (filter == 2 && (look == null || look.ChangeCount == 0) && PendingCount(name) == 0) continue;
                string label = entry != null ? entry.label : name;
                if (!string.IsNullOrEmpty(search) && label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                inCategory.Add(name);
            }
            if (inCategory.Count == 0) continue;

            bool isOpen = !closedCategories.Contains(category);
            bool nowOpen = EditorGUILayout.Foldout(isOpen, $"{category}  ({inCategory.Count})", true, EditorStyles.foldoutHeader);
            if (nowOpen != isOpen) { if (nowOpen) closedCategories.Remove(category); else closedCategories.Add(category); }
            shown += inCategory.Count;
            if (!nowOpen) continue;
            foreach (string name in inCategory)
                DrawScreen(data, name, UiScreenCatalog.Find(name), data.FindScreen(name), LiveRoot(name) != null);
            EditorGUILayout.Space(4);
        }
        if (shown == 0) GUILayout.Label(filter == 1 ? "No screen is on screen now." : "Nothing found.", smallStyle);
    }

    /// <summary>What the designer edits for this screen: a real prefab, a scene object, or the screen's skin prefab.</summary>
    private static GameObject EditTarget(string name, UiScreenCatalog.Entry entry, out string kind)
    {
        if (entry != null && entry.IsPart) // 16 Nov: a child of a prefab (hotbar, status bars inside the GAME HUD)
        {
            var whole = AssetDatabase.LoadAssetAtPath<GameObject>(entry.prefab);
            Transform part = whole != null ? whole.transform.Find(entry.part) : null;
            kind = part != null ? "prefab" : "missingPart";
            return part != null ? part.gameObject : null;
        }
        if (entry != null && !string.IsNullOrEmpty(entry.prefab))
        {
            kind = "prefab";
            return AssetDatabase.LoadAssetAtPath<GameObject>(entry.prefab);
        }
        if (entry != null && !string.IsNullOrEmpty(entry.scene))
        {
            kind = "scene";
            return Application.isPlaying ? null : FindSceneObject(name);
        }
        UiLayoutData data = UiLayoutData.Load();
        UiScreenLook look = data != null ? data.FindScreen(name) : null;
        if (look != null && look.design != null)
        {
            kind = "design"; // the designer's own prefab is shown instead of the game's screen
            return look.design;
        }
        GameObject inHud = GameHudScreens.TemplateInPrefab(name);
        if (inHud != null)
        {
            kind = "hudScreen"; // 16 Nov: the screen's editable version is a child of the GAME HUD prefab
            return inHud;
        }
        kind = "skin";
        return UiSkinTools.SkinOf(name);
    }

    private void DrawScreen(UiLayoutData data, string name, UiScreenCatalog.Entry entry, UiScreenLook look, bool live)
    {
        GameObject target = EditTarget(name, entry, out string kind);
        Transform liveRoot = LiveRoot(name);
        int waiting = PendingCount(name);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            // Line 1: arrow + name (wraps onto new lines) ... Edit + menu.
            open.TryGetValue(name, out bool isOpen);
            using (new EditorGUILayout.HorizontalScope())
            {
                var titleContent = new GUIContent(entry != null ? entry.label : name);
                float titleWidth = Mathf.Max(90f, EditorGUIUtility.currentViewWidth - 150f); // arrow, Edit, menu, margins
                float titleHeight = Mathf.Max(EditorGUIUtility.singleLineHeight, rowTitle.CalcHeight(titleContent, titleWidth));

                Rect arrowRect = GUILayoutUtility.GetRect(14f, titleHeight, GUILayout.Width(14f));
                arrowRect.height = EditorGUIUtility.singleLineHeight;
                isOpen = EditorGUI.Foldout(arrowRect, isOpen, GUIContent.none, false);
                Rect titleRect = GUILayoutUtility.GetRect(titleWidth, titleHeight, rowTitle, GUILayout.Width(titleWidth));
                if (GUI.Button(titleRect, titleContent, rowTitle)) isOpen = !isOpen; // the name opens / closes the row too
                open[name] = isOpen;
                GUILayout.FlexibleSpace();
                DrawEditButton(name, entry, kind, target, liveRoot);
                if (GUILayout.Button(new GUIContent("⋮", "More actions"), EditorStyles.miniButton, GUILayout.Width(22))) ShowMoreMenu(data, name, entry, kind, liveRoot, look);
            }

            // Line 2: where it lives + what is going on (wraps).
            string where =
                kind == "missingPart" ? "Not in the GAME HUD yet"
                : kind == "hudScreen" ? "In the GAME HUD prefab"
                : kind == "prefab" ? (entry != null && entry.IsPart ? "Part of the GAME HUD prefab" : "Its own prefab")
                : kind == "scene" ? "In the " + System.IO.Path.GetFileNameWithoutExtension(entry.scene) + " scene"
                : kind == "design" ? "Shows your own design"
                : target != null ? "Made by code (old prefab)" : "Made by code";
            string status = (live ? "<color=#59D966>● On screen now</color>   ·   " : "") + where + (waiting > 0 ? $"   ·   {waiting} unsaved" : "");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(16f);
                GUILayout.Label(status, smallStyle);
            }
            if (!isOpen) return;

            EditorGUILayout.Space(2);
            if (entry != null && !string.IsNullOrEmpty(entry.how)) GUILayout.Label("Where to see it in the game: " + entry.how, smallStyle);

            if (kind == "missingPart")
            {
                EditorGUILayout.HelpBox("This part is not in the GAME HUD prefab yet. The game uses its old code-made version until it is added.", MessageType.Warning);
                if (GUILayout.Button("Add the missing parts to the GAME HUD", GUILayout.Height(24))) { GameHudSetup.Run(); GUIUtility.ExitGUI(); }
            }
            else if (name == UiScreenCatalog.BarsScreen) DrawBars();
            else if (name == UiScreenCatalog.HotbarScreen) DrawHotbar(target);

            if (kind == "hudScreen")
                GUILayout.Label("The game shows this version. Its parts stay connected to the game (UI Link).", smallStyle);
            else if (kind == "skin" && !GameHudScreens.Blocked.ContainsKey(name))
                GUILayout.Label(GameHudScreens.AutoCapture
                    ? "To move this screen into the GAME HUD prefab: press Play, show it once, stop Play."
                    : "To move this screen into the GAME HUD prefab: tick \"Move code screens into GAME HUD\" in Settings, press Play, show it once, stop Play.", smallStyle);

            if (target != null) DrawParts(name, target, kind);
            else if (kind == "skin")
                EditorGUILayout.HelpBox(GameHudScreens.Blocked.TryGetValue(name, out string blockedWhy)
                    ? "This screen stays code-made: " + blockedWhy + ". Press Edit while it is on screen to get a prefab for restyling."
                    : "Nothing to edit yet. This screen is made by code and has not been shown in Play mode.", MessageType.None);
            else if (kind == "scene")
                EditorGUILayout.HelpBox(Application.isPlaying
                    ? "Stop Play to edit this screen in its scene (or edit it now and press Save changes)."
                    : $"This screen is in the {System.IO.Path.GetFileNameWithoutExtension(entry.scene)} scene. Press Edit to open it.", MessageType.None);

            DrawRelated(entry);

            // Everything a designer rarely needs sits behind one closed heading.
            EditorGUILayout.Space(2);
            bool more = moreOpen.Contains(name);
            bool moreNow = EditorGUILayout.Foldout(more, "More tools (advanced)", true);
            if (moreNow != more) { if (moreNow) moreOpen.Add(name); else moreOpen.Remove(name); }
            if (!moreNow) return;
            if (kind == "skin" || kind == "design") DrawDesign(data, name, data.FindScreen(name), UiSkinTools.SkinOf(name));
            DrawAdvanced(data, name, data.FindScreen(name));
        }
    }

    private void DrawEditButton(string name, UiScreenCatalog.Entry entry, string kind, GameObject target, Transform liveRoot)
    {
        string tip;
        bool can;
        if (kind == "missingPart") { can = false; tip = "Add the missing parts first."; }
        else if (kind == "prefab") { can = target != null; tip = "Open the prefab."; }
        else if (kind == "scene") { can = true; tip = "Open the scene and select the screen."; }
        else if (kind == "design") { can = target != null; tip = "Open your design prefab."; }
        else if (kind == "hudScreen") { can = target != null; tip = "Open the GAME HUD prefab showing only this screen."; }
        else if (target != null) { can = true; tip = "Open the screen's prefab."; }
        else { can = liveRoot != null; tip = can ? "Make the screen's prefab now and open it." : "Press Play and make this screen appear first."; }

        using (new EditorGUI.DisabledScope(!can))
        {
            if (!GUILayout.Button(new GUIContent("Edit", tip), editButton, GUILayout.Width(52))) return;
            if (kind == "hudScreen") GameHudScreens.OpenTemplate(target);
            else if (kind == "prefab" && entry != null && entry.IsPart) ShowPart(target.transform); // open the prefab, select the part
            else if (kind == "prefab") OpenPrefab(target);
            else if (kind == "scene") OpenScene(name, entry);
            else if (kind == "design") OpenPrefab(target);
            else
            {
                if (target == null) target = UiSkinTools.MakeSkin(liveRoot, name);
                OpenPrefab(target);
            }
            GUIUtility.ExitGUI();
        }
    }

    private void ShowMoreMenu(UiLayoutData data, string name, UiScreenCatalog.Entry entry, string kind, Transform liveRoot, UiScreenLook look)
    {
        var menu = new GenericMenu();
        if (liveRoot != null) menu.AddItem(new GUIContent("Select it in the game (Play mode)"), false, () => SelectObject(liveRoot.gameObject));
        else menu.AddDisabledItem(new GUIContent("Select it in the game (Play mode)"));

        if (kind == "skin")
        {
            if (liveRoot != null)
                menu.AddItem(new GUIContent("Remake the prefab from the game (removes your prefab edits)"), false, () =>
                {
                    if (EditorUtility.DisplayDialog("UI Hub", $"Make \"{name}\" again from what is on screen now?\nYour edits in its prefab are removed.", "Remake", "Cancel"))
                        UiSkinTools.MakeSkin(liveRoot, name);
                });
            else menu.AddDisabledItem(new GUIContent("Remake the prefab from the game (Play mode)"));
        }

        if (look != null && look.ChangeCount > 0)
            menu.AddItem(new GUIContent("Remove all saved changes of this screen"), false, () =>
            {
                if (!EditorUtility.DisplayDialog("UI Hub", $"Remove every saved change of \"{name}\"?\n(The screen's prefab file stays; Remake it to start fresh.)", "Remove", "Cancel")) return;
                Undo.RecordObject(data, "Reset UI screen");
                data.screens.Remove(look);
                Changed(data);
            });

        if (kind == "hudScreen")
        {
            if (Application.isPlaying) menu.AddDisabledItem(new GUIContent("Take it out of the GAME HUD (stop Play first)"));
            else menu.AddItem(new GUIContent("Take it out of the GAME HUD (the game's own screen again)"), false, () =>
            {
                if (EditorUtility.DisplayDialog("UI Hub", $"Remove \"{name}\" from the GAME HUD prefab?\nYour edits on it are lost. The game shows its own code-made screen again, " +
                    "and copies it into the GAME HUD again the next time it appears in Play mode.", "Remove", "Cancel"))
                    GameHudScreens.RemoveTemplate(name);
            });
        }

        menu.AddSeparator("");
        if (entry != null && !string.IsNullOrEmpty(entry.script))
            menu.AddItem(new GUIContent("For programmers: open the script"), false, () => AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<Object>(entry.script)));
        menu.ShowAsContext();
    }

    // ---- 3D models (17 Nov): the GAME HUD prefab lists them (Model Slots) ----------------------------

    /// <summary>The "3D models" heading. Returns how many models are shown.</summary>
    private int DrawModels()
    {
        if (filter != 0) return 0; // "On screen now" / "Changed" are about screens
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(UiScreenCatalog.GameHudPrefab);
        if (hud == null) return 0;
        var slots = new List<ModelSlot>();
        foreach (ModelSlot slot in hud.GetComponentsInChildren<ModelSlot>(true))
            if (string.IsNullOrEmpty(search) || slot.Label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0) slots.Add(slot);

        bool isOpen = modelsOpen || (!string.IsNullOrEmpty(search) && slots.Count > 0);
        bool now = EditorGUILayout.Foldout(isOpen, $"{UiScreenCatalog.Models}  ({slots.Count})", true, EditorStyles.foldoutHeader);
        if (string.IsNullOrEmpty(search)) modelsOpen = now;
        if (!now) return slots.Count;

        GUILayout.Label("Every 3D model the players see (items, rule props). Open one to change its mesh and material, or press Edit to open its prefab.", smallStyle);
        foreach (ModelSlot slot in slots) DrawModelSlot(slot);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUILayout.Label("Add a model: drag its prefab here", partLabel);
            var add = (GameObject)EditorGUILayout.ObjectField(null, typeof(GameObject), false);
            if (add != null)
            {
                if (PrefabUtility.IsPartOfPrefabAsset(add)) GameHudSetup.AddModelSlotToPrefab(add.transform.root.gameObject);
                else EditorUtility.DisplayDialog("UI Hub", "Drag a prefab from the Project window.", "OK");
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("Find new item and rule prop prefabs", EditorStyles.miniButton)) { GameHudSetup.Run(); GUIUtility.ExitGUI(); }
        }
        EditorGUILayout.Space(4);
        return slots.Count;
    }

    private void DrawModelSlot(ModelSlot slot)
    {
        GameObject prefab = slot.ModelPrefab;
        string key = "model|" + slot.name;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            open.TryGetValue(key, out bool isOpen);
            using (new EditorGUILayout.HorizontalScope())
            {
                var titleContent = new GUIContent(slot.Label);
                float titleWidth = Mathf.Max(90f, EditorGUIUtility.currentViewWidth - 150f);
                float titleHeight = Mathf.Max(EditorGUIUtility.singleLineHeight, rowTitle.CalcHeight(titleContent, titleWidth));
                Rect arrowRect = GUILayoutUtility.GetRect(14f, titleHeight, GUILayout.Width(14f));
                arrowRect.height = EditorGUIUtility.singleLineHeight;
                isOpen = EditorGUI.Foldout(arrowRect, isOpen, GUIContent.none, false);
                Rect titleRect = GUILayoutUtility.GetRect(titleWidth, titleHeight, rowTitle, GUILayout.Width(titleWidth));
                if (GUI.Button(titleRect, titleContent, rowTitle)) isOpen = !isOpen;
                open[key] = isOpen;
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(prefab == null))
                    if (GUILayout.Button(new GUIContent("Edit", "Open the model's prefab."), editButton, GUILayout.Width(52))) { OpenPrefab(prefab); GUIUtility.ExitGUI(); }
                if (GUILayout.Button(new GUIContent("⋮", "More actions"), EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    var menu = new GenericMenu();
                    if (prefab != null) menu.AddItem(new GUIContent("Show the prefab in the Project window"), false, () => EditorGUIUtility.PingObject(prefab));
                    if (Application.isPlaying || prefab == null) menu.AddDisabledItem(new GUIContent("Take it off this list"));
                    else menu.AddItem(new GUIContent("Take it off this list (the prefab is kept)"), false, () => GameHudSetup.RemoveModelSlotFromPrefab(prefab));
                    menu.ShowAsContext();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(16f);
                GUILayout.Label(prefab != null ? AssetDatabase.GetAssetPath(prefab) : "The prefab is missing. Take this row off the list.", prefab != null ? smallStyle : warnStyle);
            }
            if (!isOpen || prefab == null) return;
            if (!string.IsNullOrEmpty(slot.Note)) GUILayout.Label(slot.Note, smallStyle);
            DrawParts(key, prefab, "prefab");
        }
    }

    // ---- Status bars: frame + fill (16 Nov; stacked rows 17 Nov) ------------------------------------

    /// <summary>
    /// Every status bar of the GAME HUD as a small card with its two pictures: HP / Stamina /
    /// Sanity (Slider bars) and the HudBar bars (noise, flashlight, throw, task, walkie).
    /// </summary>
    private void DrawBars()
    {
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(UiScreenCatalog.GameHudPrefab);
        if (hud == null) return;
        GUILayout.Label("Bars", rowTitle);
        GUILayout.Label("Each bar has two pictures. Frame = the box around it. Fill = the part that grows. Change each one on its own.", smallStyle);

        bool changed = false;
        foreach (Slider slider in hud.GetComponentsInChildren<Slider>(true))
        {
            Transform background = slider.transform.Find("Background");
            Image frame = background != null ? background.GetComponent<Image>() : null;
            Image fill = slider.fillRect != null ? slider.fillRect.GetComponent<Image>() : null;
            changed |= DrawBarCard(slider.name, frame, fill, slider);
        }
        foreach (HudBar bar in hud.GetComponentsInChildren<HudBar>(true))
            changed |= DrawBarCard(bar.name, bar.Frame, bar.Fill, bar);

        if (changed) PrefabUtility.SavePrefabAsset(hud);
        GUILayout.Label("A Fill picture with Image Type \"Filled\" is cut as the bar goes down. Other types are squeezed. Press → to open a bar and move or resize it.", smallStyle);
        EditorGUILayout.Space(4);
    }

    private bool DrawBarCard(string label, Image frame, Image fill, Component bar)
    {
        bool changed = false;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, partLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("→", "Show this bar"), EditorStyles.miniButton, GUILayout.Width(22))) { ShowPart(bar); GUIUtility.ExitGUI(); }
            }
            changed |= PictureLine("Frame", frame);
            changed |= PictureLine("Fill", fill);
        }
        return changed;
    }

    /// <summary>One line: a short word, then picture + colour of an Image. True when changed.</summary>
    private static bool PictureLine(string what, Image image)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(what, EditorStyles.miniLabel, GUILayout.Width(38f));
            if (image == null) { GUILayout.Label("(this bar has none)", smallStyle); return false; }
            var sprite = (Sprite)EditorGUILayout.ObjectField(image.sprite, typeof(Sprite), false, GUILayout.MinWidth(50f));
            Color colour = EditorGUILayout.ColorField(GUIContent.none, image.color, true, true, false, GUILayout.Width(50f));
            if (sprite == image.sprite && colour == image.color) return false;
            Undo.RecordObject(image, "UI bar picture");
            image.sprite = sprite;
            image.color = colour;
            EditorUtility.SetDirty(image);
            return true;
        }
    }

    // ---- Hotbar: selected colours + item icons (16 Nov; stacked rows 17 Nov) ----------------------------

    private void DrawHotbar(GameObject hotbar)
    {
        HotbarView view = hotbar != null ? hotbar.GetComponent<HotbarView>() : null;
        if (view == null) return;
        var so = new SerializedObject(view);
        EditorGUI.BeginChangeCheck();

        GUILayout.Label("Selected slot", rowTitle);
        SerializedProperty tint = so.FindProperty("tintFrames");
        tint.boolValue = WrapToggle("The game colours the slot frames (off = they keep their own colour)", tint.boolValue);
        StackedField(so, "normalColour", "Frame colour");
        StackedField(so, "selectedColour", "Frame colour of the selected slot");
        StackedField(so, "selectedPicture", "Frame picture of the selected slot (optional)");
        StackedField(so, "textColour", "Text colour");
        StackedField(so, "selectedTextColour", "Text colour of the selected slot");

        EditorGUILayout.Space(4);
        GUILayout.Label("Item icons", rowTitle);
        GUILayout.Label("One picture per item. An item with no picture shows its name.", smallStyle);
        SerializedProperty hideName = so.FindProperty("hideNameWhenIconShown");
        hideName.boolValue = WrapToggle("Hide the item's name when it has an icon", hideName.boolValue);

        SerializedProperty icons = so.FindProperty("itemIcons");
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("Item ID", EditorStyles.miniLabel, GUILayout.MinWidth(50f));
            GUILayout.Label("Picture", EditorStyles.miniLabel, GUILayout.MinWidth(50f));
            GUILayout.Space(26f);
        }
        for (int i = 0; i < icons.arraySize; i++)
        {
            SerializedProperty row = icons.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(row.FindPropertyRelative("itemId"), GUIContent.none, GUILayout.MinWidth(50f));
                EditorGUILayout.PropertyField(row.FindPropertyRelative("picture"), GUIContent.none, GUILayout.MinWidth(50f));
                if (GUILayout.Button(new GUIContent("x", "Remove this row"), EditorStyles.miniButton, GUILayout.Width(22f)))
                {
                    icons.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
        }
        if (GUILayout.Button("+ Add an item", EditorStyles.miniButton))
        {
            icons.InsertArrayElementAtIndex(icons.arraySize);
            SerializedProperty added = icons.GetArrayElementAtIndex(icons.arraySize - 1);
            added.FindPropertyRelative("itemId").stringValue = "";
            added.FindPropertyRelative("picture").objectReferenceValue = null;
        }

        if (EditorGUI.EndChangeCheck())
        {
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(view);
            PrefabUtility.SavePrefabAsset(view.transform.root.gameObject);
        }
        GUILayout.Label("New items in the game: run Tools > Pre-Thesis > UI > GAME HUD: Add Missing Parts. It adds their rows here.", smallStyle);
        EditorGUILayout.Space(4);
    }

    /// <summary>A field with its name on the line above it, so a long name is never cut.</summary>
    private static void StackedField(SerializedObject so, string field, string label)
    {
        GUILayout.Label(label, EditorStyles.miniLabel);
        EditorGUILayout.PropertyField(so.FindProperty(field), GUIContent.none);
    }

    // ---- Parts: pictures, texts, models (grouped + stacked rows 17 Nov) -------------------------------

    /// <summary>Children of the GAME HUD that have their own row in the hub (not repeated under "Game HUD").</summary>
    private static bool HasOwnHubRow(string childName) =>
        childName == GameHudSetup.BarsGroup || childName == GameHudSetup.HotbarName || childName == GameHudScreens.ScreensGroup
        || childName == GameHudSetup.ModelsGroup;

    private void DrawParts(string name, GameObject root, string kind)
    {
        GUILayout.Label("Pictures and texts", rowTitle);
        bool isAsset = EditorUtility.IsPersistent(root);

        partSearch.TryGetValue(name, out string find);
        find = EditorGUILayout.TextField(find ?? "", EditorStyles.toolbarSearchField);
        partSearch[name] = find;
        bool searching = !string.IsNullOrEmpty(find);

        // One group per direct child of the screen ("HP", "Stamina Bar", "Slot 1" ...).
        var groupOrder = new List<Transform>();
        var byGroup = new Dictionary<Transform, List<Component>>();
        foreach (Component c in root.GetComponentsInChildren<Component>(true))
        {
            if (!(c is Image || c is RawImage || c is TMP_Text || c is Text || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer)) continue;
            Transform group = c.transform;
            while (group != root.transform && group.parent != root.transform) group = group.parent;
            if (name == "GAME HUD" && group != root.transform && HasOwnHubRow(group.name)) continue;
            if (searching && UiLayoutRuntime.PathOf(root.transform, c.transform).IndexOf(find, System.StringComparison.OrdinalIgnoreCase) < 0
                && c.name.IndexOf(find, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!byGroup.TryGetValue(group, out List<Component> list)) { byGroup[group] = list = new List<Component>(); groupOrder.Add(group); }
            list.Add(c);
        }
        if (groupOrder.Count == 0) { GUILayout.Label(searching ? "Nothing found." : "No pictures, texts or models here.", smallStyle); return; }

        bool changedAsset = false;
        bool single = groupOrder.Count == 1;
        foreach (Transform group in groupOrder)
        {
            List<Component> rows = byGroup[group];
            string key = name + "|" + group.name;
            bool isOpen = single || searching || openGroups.Contains(key);
            if (!single)
            {
                bool now = EditorGUILayout.Foldout(isOpen, $"{(group == root.transform ? "(the screen itself)" : group.name)}  ({rows.Count})", true);
                if (!searching && now != isOpen) { if (now) openGroups.Add(key); else openGroups.Remove(key); }
                if (!searching) isOpen = now;
            }
            if (!isOpen) continue;

            bool all = showAllParts.Contains(key);
            int count = all ? rows.Count : Mathf.Min(rows.Count, RowsPerPage);
            for (int i = 0; i < count; i++)
                if (DrawPart(rows[i], root.transform, group)) changedAsset |= isAsset;
            if (rows.Count > RowsPerPage && GUILayout.Button(all ? "Show fewer" : $"Show all {rows.Count}", EditorStyles.miniButton))
            {
                if (all) showAllParts.Remove(key); else showAllParts.Add(key);
            }
            EditorGUILayout.Space(2);
        }

        if (changedAsset)
        {
            PrefabUtility.SavePrefabAsset(root.transform.root.gameObject); // saved at once (root: a part's prefab is its top object)
            if (kind == "design") UiLayoutData.Load()?.MarkChanged(); // show the new look in Play mode
        }
        GUILayout.Label(kind == "scene"
            ? "To add new pictures or 3D models: press Edit, then drag them under the screen."
            : "To add new pictures or 3D models, or to move things: press Edit, drag them into the prefab, save.", smallStyle);
    }

    /// <summary>
    /// One part. Line 1 = what it is + its name (wraps). The lines under it = its fields, as wide as
    /// the Inspector, so nothing runs past the edge. True when something changed.
    /// </summary>
    private bool DrawPart(Component c, Transform root, Transform group)
    {
        string path = c.transform == root ? c.name : UiLayoutRuntime.PathOf(root, c.transform);
        string shown = c.transform == group ? c.name : UiLayoutRuntime.PathOf(group, c.transform).Replace("/", " › ");
        string what = c is Image || c is RawImage ? "Picture" : c is MeshFilter ? "3D model" : c is MeshRenderer ? "Material"
            : c is SkinnedMeshRenderer ? "3D model + material" : "Text";
        bool changed = false;

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(new GUIContent($"{what}  ·  {shown}", path), partLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("→", "Show this part"), EditorStyles.miniButton, GUILayout.Width(22))) ShowPart(c);
        }

        if (c is Image img)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                var sprite = (Sprite)EditorGUILayout.ObjectField(img.sprite, typeof(Sprite), false, GUILayout.MinWidth(50f));
                Color col = EditorGUILayout.ColorField(GUIContent.none, img.color, true, true, false, GUILayout.Width(50f));
                if (sprite != img.sprite || col != img.color) { Undo.RecordObject(img, "UI picture"); img.sprite = sprite; img.color = col; changed = true; }
            }
        }
        else if (c is RawImage raw)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                var tex = (Texture)EditorGUILayout.ObjectField(raw.texture, typeof(Texture), false, GUILayout.MinWidth(50f));
                Color col = EditorGUILayout.ColorField(GUIContent.none, raw.color, true, true, false, GUILayout.Width(50f));
                if (tex != raw.texture || col != raw.color) { Undo.RecordObject(raw, "UI picture"); raw.texture = tex; raw.color = col; changed = true; }
            }
        }
        else if (c is TMP_Text tmp)
        {
            string words;
            TMP_FontAsset font;
            float size;
            Color col;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                words = EditorGUILayout.TextField(tmp.text, GUILayout.MinWidth(50f));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                font = (TMP_FontAsset)EditorGUILayout.ObjectField(tmp.font, typeof(TMP_FontAsset), false, GUILayout.MinWidth(50f));
                size = EditorGUILayout.FloatField(tmp.fontSize, GUILayout.Width(40f));
                col = EditorGUILayout.ColorField(GUIContent.none, tmp.color, true, true, false, GUILayout.Width(50f));
            }
            if (words != tmp.text || font != tmp.font || !Mathf.Approximately(size, tmp.fontSize) || col != tmp.color)
            {
                Undo.RecordObject(tmp, "UI text");
                tmp.text = words;
                if (font != null) tmp.font = font;
                tmp.fontSize = size;
                tmp.color = col;
                changed = true;
            }
        }
        else if (c is Text legacy)
        {
            string words;
            Font font;
            int size;
            Color col;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                words = EditorGUILayout.TextField(legacy.text, GUILayout.MinWidth(50f));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                font = (Font)EditorGUILayout.ObjectField(legacy.font, typeof(Font), false, GUILayout.MinWidth(50f));
                size = EditorGUILayout.IntField(legacy.fontSize, GUILayout.Width(40f));
                col = EditorGUILayout.ColorField(GUIContent.none, legacy.color, true, true, false, GUILayout.Width(50f));
            }
            if (words != legacy.text || font != legacy.font || size != legacy.fontSize || col != legacy.color)
            {
                Undo.RecordObject(legacy, "UI text");
                legacy.text = words; legacy.font = font; legacy.fontSize = size; legacy.color = col;
                changed = true;
            }
        }
        else if (c is MeshFilter mf)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                var mesh = (Mesh)EditorGUILayout.ObjectField(mf.sharedMesh, typeof(Mesh), false, GUILayout.MinWidth(50f));
                if (mesh != mf.sharedMesh) { Undo.RecordObject(mf, "UI model"); mf.sharedMesh = mesh; changed = true; }
            }
        }
        else if (c is MeshRenderer mr)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                var mat = (Material)EditorGUILayout.ObjectField(mr.sharedMaterial, typeof(Material), false, GUILayout.MinWidth(50f));
                if (mat != mr.sharedMaterial) { Undo.RecordObject(mr, "UI material"); mr.sharedMaterial = mat; changed = true; }
            }
        }
        else if (c is SkinnedMeshRenderer skinned) // a model with bones: mesh and material on one part
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                var mesh = (Mesh)EditorGUILayout.ObjectField(skinned.sharedMesh, typeof(Mesh), false, GUILayout.MinWidth(50f));
                var mat = (Material)EditorGUILayout.ObjectField(skinned.sharedMaterial, typeof(Material), false, GUILayout.MinWidth(50f));
                if (mesh != skinned.sharedMesh || mat != skinned.sharedMaterial)
                {
                    Undo.RecordObject(skinned, "UI model");
                    skinned.sharedMesh = mesh;
                    skinned.sharedMaterial = mat;
                    changed = true;
                }
            }
        }

        if (!changed) return false;
        EditorUtility.SetDirty(c);
        if (PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        return true;
    }

    private static string ShortPath(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : "…/" + path.Substring(slash + 1);
    }

    private static void ShowPart(Component c)
    {
        if (EditorUtility.IsPersistent(c))
        {
            // Inside a prefab file: open the prefab and select the same part.
            GameObject asset = c.transform.root.gameObject;
            string path = UiLayoutRuntime.PathOf(asset.transform, c.transform);
            AssetDatabase.OpenAsset(asset);
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                Transform t = string.IsNullOrEmpty(path) ? stage.prefabContentsRoot.transform : UiLayoutRuntime.Resolve(stage.prefabContentsRoot.transform, path);
                if (t != null) SelectObject(t.gameObject);
            }
            return;
        }
        SelectObject(c.gameObject);
    }

    // ---- Your own design ------------------------------------------------------------------------------

    private static readonly HashSet<string> linksAll = new HashSet<string>();

    private void DrawDesign(UiLayoutData data, string name, UiScreenLook look, GameObject skin)
    {
        EditorGUILayout.Space(4);
        GUILayout.Label("Use your own design (any prefab)", rowTitle);
        GameObject design = look != null ? look.design : null;

        EditorGUI.BeginChangeCheck();
        GUILayout.Label("My design: drag any UI prefab here. The game hides its own screen and shows this one.", smallStyle);
        var picked = (GameObject)EditorGUILayout.ObjectField(design, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck())
        {
            UiDesignTools.UseDesign(name, picked);
            if (picked != null && skin != null && UiDesignTools.Links(picked).Count == 0)
            {
                int n = UiDesignTools.AutoConnect(picked, skin.transform);
                Debug.Log($"[UI Hub] \"{name}\": connected {n} part(s) of your design by name.");
            }
            GUIUtility.ExitGUI();
        }

        using (var flow = new Flow()) // buttons go onto a new line when they do not fit
        {
            if (design == null)
            {
                using (new EditorGUI.DisabledScope(skin == null))
                    if (GUILayout.Button(new GUIContent("Make my own design from this screen", skin == null
                        ? "Press Play and make this screen appear once first."
                        : "Copies the screen into Assets/UI Designs with every part already connected. Restyle it freely."), GUILayout.Height(22)))
                    {
                        GameObject made = UiDesignTools.MakeDesign(name);
                        if (made != null) OpenPrefab(made);
                        GUIUtility.ExitGUI();
                    }
            }
            else
            {
                flow.Next(ButtonWidth("Open my design"));
                if (GUILayout.Button("Open my design", EditorStyles.miniButton, GUILayout.Width(ButtonWidth("Open my design")))) { OpenPrefab(design); GUIUtility.ExitGUI(); }
                flow.Next(ButtonWidth("Auto-connect by name"));
                using (new EditorGUI.DisabledScope(skin == null))
                    if (GUILayout.Button(new GUIContent("Auto-connect by name", "Parts with the same name as a game part get connected."), EditorStyles.miniButton, GUILayout.Width(ButtonWidth("Auto-connect by name"))))
                    {
                        int n = UiDesignTools.AutoConnect(design, skin.transform);
                        data.MarkChanged();
                        Debug.Log($"[UI Hub] \"{name}\": connected {n} more part(s) by name.");
                        GUIUtility.ExitGUI();
                    }
                flow.Next(ButtonWidth("Stop using it"));
                if (GUILayout.Button(new GUIContent("Stop using it", "Show the game's own screen again (your prefab is kept)."), EditorStyles.miniButton, GUILayout.Width(ButtonWidth("Stop using it"))))
                {
                    UiDesignTools.UseDesign(name, null);
                    GUIUtility.ExitGUI();
                }
            }
        }

        if (design == null)
        {
            GUILayout.Label("Your design can look like anything. Connected parts get the game's words, bars, pictures and clicks.", smallStyle);
            return;
        }
        if (skin == null)
        {
            EditorGUILayout.HelpBox("Press Play and make this screen appear once: then the list of game parts to connect shows here.", MessageType.None);
            return;
        }
        DrawLinks(data, name, design, skin);
    }

    private void DrawLinks(UiLayoutData data, string name, GameObject design, GameObject skin)
    {
        GUILayout.Label("Connect: drag a part of your design next to each game part", partLabel);
        bool all = linksAll.Contains(name);
        bool allNow = EditorGUILayout.ToggleLeft("Show every game part (lists, panels, backgrounds)", all);
        if (allNow != all) { if (allNow) linksAll.Add(name); else linksAll.Remove(name); }

        int missing = 0;
        foreach (Transform g in skin.GetComponentsInChildren<Transform>(true))
        {
            if (g == skin.transform) continue;
            bool isText = g.GetComponent<TMP_Text>() != null || g.GetComponent<Text>() != null;
            bool isButton = g.GetComponent<Button>() != null;
            Image img = g.GetComponent<Image>();
            string lower = g.name.ToLowerInvariant();
            bool isBar = img != null && (img.type == Image.Type.Filled || lower.Contains("fill") || lower.Contains("bar"));
            bool isIcon = (img != null || g.GetComponent<RawImage>() != null) && lower.Contains("icon");
            bool important = isText || isButton || isBar || isIcon;
            string path = UiLayoutRuntime.PathOf(skin.transform, g);
            UiLink link = UiDesignTools.LinkFor(design, path);
            if (important && link == null) missing++;
            if (!allNow && !important) continue;

            string what = isButton ? "Button" : isText ? "Words" : isBar ? "Bar" : isIcon ? "Icon" : g.childCount > 0 ? "List / panel" : "Part";
            using (new EditorGUILayout.VerticalScope()) // name on its own line (wraps), the slot under it
            {
                GUILayout.Label(new GUIContent($"{what}  ·  {path.Replace("/", " › ")}", path), partLabel);
                EditorGUI.BeginChangeCheck();
                var part = (GameObject)EditorGUILayout.ObjectField(link != null ? link.gameObject : null, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    if (part == null) UiDesignTools.RemoveLink(design, link);
                    else if (part.transform.root != design.transform)
                        EditorUtility.DisplayDialog("UI Hub", "Drag a part from INSIDE your design prefab (open it, or expand it in the Project window).", "OK");
                    else UiDesignTools.SetLink(design, part, path, g);
                    data.MarkChanged();
                    GUIUtility.ExitGUI();
                }
            }
            if (link != null) DrawLinkOptions(data, design, link, g);
        }
        if (missing > 0)
            GUILayout.Label($"⚠ {missing} game word / button / bar / icon part(s) are not connected: your design will not show them.", warnStyle);
    }

    private void DrawLinkOptions(UiLayoutData data, GameObject design, UiLink link, Transform gamePart)
    {
        var so = new SerializedObject(link);
        using (var flow = new Flow(12f)) // the buttons go onto a new line when they do not fit
        {
            Mini(flow, so, "showHide", "Show/hide");
            Mini(flow, so, "text", "Words");
            Mini(flow, so, "color", "Colour");
            Mini(flow, so, "picture", "Picture");
            Mini(flow, so, "fill", "Bar");
            Mini(flow, so, "click", "Click");
            Mini(flow, so, "control", "Slider / tick / list / typing");
            Mini(flow, so, "followLayout", "Follow place");
            Mini(flow, so, "onlyGameChanges", "Keep my look");
            Mini(flow, so, "repeat", "Repeat rows");
        }
        if (so.ApplyModifiedProperties())
        {
            AssetDatabase.SaveAssetIfDirty(design);
            data.MarkChanged();
        }
        if (link.repeat && gamePart.childCount > 0)
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12);
                if (GUILayout.Button(new GUIContent("Connect row parts by name", "Parts inside your row template get connected to the parts of one game row with the same name."), EditorStyles.miniButton))
                {
                    int n = UiDesignTools.AutoConnect(design, gamePart.GetChild(0), UiLayoutRuntime.PathOf(design.transform, link.transform));
                    data.MarkChanged();
                    Debug.Log($"[UI Hub] Connected {n} row part(s) by name.");
                    GUIUtility.ExitGUI();
                }
            }
    }

    private static void Mini(Flow flow, SerializedObject so, string field, string label)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p == null) return;
        float width = ButtonWidth(label);
        flow.Next(width);
        p.boolValue = GUILayout.Toggle(p.boolValue, label, EditorStyles.miniButton, GUILayout.Width(width));
    }

    // ---- Related models / settings ----------------------------------------------------------------

    private void DrawRelated(UiScreenCatalog.Entry entry)
    {
        if (entry == null || entry.related == null || entry.related.Length == 0) return;
        GUILayout.Label("Models and settings this screen uses", rowTitle);
        foreach (string path in entry.related)
        {
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(asset, typeof(Object), false);
                using (new EditorGUI.DisabledScope(asset == null))
                    if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(46)))
                    {
                        if (AssetDatabase.IsValidFolder(path)) { Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
                        else AssetDatabase.OpenAsset(asset);
                    }
            }
        }
    }

    // ---- Advanced: screen settings + saved changes --------------------------------------------------

    private void DrawAdvanced(UiLayoutData data, string name, UiScreenLook look)
    {
        bool adv = advancedOpen.Contains(name);
        bool now = EditorGUILayout.Foldout(adv, "Screen settings and saved changes", true);
        if (now != adv) { if (now) advancedOpen.Add(name); else advancedOpen.Remove(name); }
        if (!now) return;

        EditorGUI.indentLevel++;
        DrawScreenSettings(data, name, look);
        look = data.FindScreen(name);
        DrawElements(data, name, look);
        DrawPending(name);
        EditorGUI.indentLevel--;
    }

    private void DrawScreenSettings(UiLayoutData data, string name, UiScreenLook look)
    {
        bool hide = look != null && look.hide;
        bool sort = look != null && look.overrideSortOrder;
        int order = look != null ? look.sortOrder : 0;
        TMP_FontAsset font = look != null ? look.font : null;

        EditorGUI.BeginChangeCheck();
        hide = WrapToggle("Hide this screen (it is not drawn at all)", hide);
        sort = WrapToggle("Change which screen is drawn on top", sort);
        using (new EditorGUI.DisabledScope(!sort))
        {
            GUILayout.Label("Draw order (higher = on top)", EditorStyles.miniLabel);
            order = EditorGUILayout.IntField(order);
        }
        GUILayout.Label("Font for this screen (every text in it)", EditorStyles.miniLabel);
        font = (TMP_FontAsset)EditorGUILayout.ObjectField(font, typeof(TMP_FontAsset), false);
        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(data, "UI screen settings");
        UiScreenLook s = data.GetOrAddScreen(name);
        s.hide = hide;
        s.overrideSortOrder = sort;
        s.sortOrder = order;
        s.font = font;
        if (s.ChangeCount == 0 && s.skin == null && string.IsNullOrEmpty(s.note)) data.screens.Remove(s);
        Changed(data);
    }

    private void DrawElements(UiLayoutData data, string name, UiScreenLook look)
    {
        if (look == null || (look.elements.Count == 0 && look.added.Count == 0))
        {
            GUILayout.Label("No saved changes yet.", smallStyle);
            return;
        }
        GUILayout.Label($"Saved changes ({look.elements.Count} changed, {look.added.Count} added)", partLabel);
        int screenIndex = data.screens.IndexOf(look);
        for (int i = 0; i < look.elements.Count; i++)
        {
            UiElementLook e = look.elements[i];
            if (e == null) continue;
            string key = name + "|" + e.path;
            GUILayout.Label(new GUIContent((e.fromSkin ? "[prefab] " : "") + e.path.Replace("/", " › ") + "   —   " + Groups(e), e.path), smallStyle);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                Transform t = UiChangeRecorder.FindLive(name, e.path);
                using (new EditorGUI.DisabledScope(t == null))
                    if (GUILayout.Button("Select", EditorStyles.miniButtonLeft, GUILayout.Width(46))) SelectObject(t.gameObject);
                using (new EditorGUI.DisabledScope(e.fromSkin))
                {
                    bool editing = editOpen.Contains(key);
                    if (GUILayout.Button(new GUIContent(editing ? "Close" : "Edit", e.fromSkin ? "Change it in the screen's prefab." : ""), EditorStyles.miniButtonMid, GUILayout.Width(40)))
                    {
                        if (editing) editOpen.Remove(key); else editOpen.Add(key);
                    }
                    if (GUILayout.Button(new GUIContent("X", "Remove this change"), EditorStyles.miniButtonRight, GUILayout.Width(22)))
                    {
                        Undo.RecordObject(data, "Remove UI change");
                        look.elements.RemoveAt(i);
                        Changed(data);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            for (int g = 0; g < 8; g++)
            {
                var group = (UiLookGroup)g;
                if (e.Uses(group) && Application.isPlaying && UiLayoutRuntime.IsCodeControlled(name, e.path, group))
                    GUILayout.Label($"   ⚠ {group}: the game code (or a layout group) keeps changing this, so your change cannot stay. Ask a programmer.", warnStyle);
            }

            if (!e.fromSkin && editOpen.Contains(key)) DrawElementFields(data, screenIndex, i, e);
        }
        foreach (UiAddedObject a in look.added)
            if (a != null) GUILayout.Label($"[prefab] added: {(string.IsNullOrEmpty(a.parentPath) ? "" : a.parentPath + "/")}{(a.source != null ? a.source.name : "?")}", smallStyle);
    }

    private void DrawElementFields(UiLayoutData data, int screenIndex, int elementIndex, UiElementLook e)
    {
        var so = new SerializedObject(data);
        SerializedProperty el = so.FindProperty("screens").GetArrayElementAtIndex(screenIndex).FindPropertyRelative("elements").GetArrayElementAtIndex(elementIndex);
        EditorGUI.indentLevel++;
        Group(el, e.hide, "Hidden", "hide");
        Group(el, e.layout, "Position / size", "layout", "anchorMin", "anchorMax", "pivot", "position", "size", "rotation", "scale");
        Group(el, e.color, "Colour", "color", "colorValue");
        Group(el, e.sprite, "Picture", "sprite", "spriteValue");
        Group(el, e.textStyle, "Text style", "textStyle", "font", "fontSize", "fontStyle", "alignment", "autoSize", "fontSizeMin", "fontSizeMax", "characterSpacing", "lineSpacing", "wrapping", "legacyFont", "legacyFontStyle", "legacyAlignment");
        Group(el, e.text, "Words", "text", "textValue");
        Group(el, e.spacing, "Row / column spacing", "spacing", "spacingValue", "paddingLeft", "paddingRight", "paddingTop", "paddingBottom");
        Group(el, e.layoutSize, "Layout size", "layoutSize", "minWidth", "minHeight", "preferredWidth", "preferredHeight");
        EditorGUI.indentLevel--;
        if (so.ApplyModifiedProperties())
        {
            data.screens[screenIndex].elements.RemoveAll(x => x == null || x.Empty);
            Changed(data);
        }
    }

    private static void Group(SerializedProperty el, bool used, string title, string flag, params string[] fields)
    {
        if (!used) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(title, partLabel);
            if (GUILayout.Button(new GUIContent("remove", "Stop changing this part"), EditorStyles.miniButton, GUILayout.Width(56)))
                el.FindPropertyRelative(flag).boolValue = false;
        }
        foreach (string f in fields) EditorGUILayout.PropertyField(el.FindPropertyRelative(f), true);
    }

    private static string Groups(UiElementLook e)
    {
        var parts = new List<string>();
        if (e.hide) parts.Add("hidden");
        if (e.layout) parts.Add("position");
        if (e.color) parts.Add("colour");
        if (e.sprite) parts.Add("picture");
        if (e.textStyle) parts.Add("text style");
        if (e.text) parts.Add("words");
        if (e.spacing) parts.Add("spacing");
        if (e.layoutSize) parts.Add("layout size");
        return string.Join(", ", parts);
    }

    private void DrawPending(string name)
    {
        bool any = false;
        foreach (UiChangeRecorder.Pending p in UiChangeRecorder.PendingList)
        {
            if (p.screen != name) continue;
            if (!any) { GUILayout.Label("Waiting to be saved", partLabel); any = true; }
            GUILayout.Label($"   {p.path}  ({p.group})", smallStyle);
        }
    }

    // ---- Other UI ------------------------------------------------------------------------------

    private void DrawOtherUi()
    {
        GUILayout.Label(
            "The Tools > Pre-Thesis > Scenes > Menu tools only add what is missing. They never delete a menu that is already there, so your edits stay.", smallStyle);

        var hub = (UiHub)target;
        bool isAsset = EditorUtility.IsPersistent(hub);
        GUILayout.Label("GAME HUD parts", partLabel);
        foreach (Transform child in hub.transform)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(child.name, smallStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(isAsset ? "Open prefab" : "Select", EditorStyles.miniButton, GUILayout.Width(80)))
                {
                    if (isAsset) ShowPart(child);
                    else SelectObject(child.gameObject);
                }
            }
        }

        GUILayout.Label("UI prefabs in the project", partLabel);
        if (uiPrefabs == null)
        {
            if (GUILayout.Button("Find UI prefabs", EditorStyles.miniButton)) uiPrefabs = FindUiPrefabs();
            return;
        }
        foreach (GameObject p in uiPrefabs)
        {
            if (p == null) continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(p.name, smallStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open prefab", EditorStyles.miniButton, GUILayout.Width(80))) OpenPrefab(p);
            }
        }
        if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(80))) uiPrefabs = FindUiPrefabs();
    }

    private static List<GameObject> FindUiPrefabs()
    {
        var list = new List<GameObject>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/Mirror") || path.Contains("/TextMesh Pro/") || path.Contains("/Samples/") || path.StartsWith(UiSkinTools.OriginalFolder)) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null && go.GetComponentInChildren<Canvas>(true) != null) list.Add(go);
        }
        return list.OrderBy(g => g.name).ToList();
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static Transform LiveRoot(string name)
    {
        foreach (UiLayoutRuntime.LiveScreen s in UiLayoutRuntime.Live)
            if (s.root != null && s.name == name && s.root.gameObject.activeInHierarchy) return s.root;
        return null;
    }

    private static GameObject FindSceneObject(string name)
    {
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            if (c != null && c.isRootCanvas && !EditorUtility.IsPersistent(c) && c.gameObject.scene.IsValid() && UiLayoutRuntime.ScreenName(c.transform) == name)
                return c.gameObject;
        return null;
    }

    private static int PendingCount(string name)
    {
        int n = 0;
        foreach (UiChangeRecorder.Pending p in UiChangeRecorder.PendingList) if (p.screen == name) n++;
        return n;
    }

    private static void SelectObject(GameObject go)
    {
        if (go == null) return;
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
    }

    private static void OpenPrefab(GameObject prefab)
    {
        if (prefab != null) AssetDatabase.OpenAsset(prefab);
    }

    private static void OpenScene(string name, UiScreenCatalog.Entry entry)
    {
        GameObject go = Application.isPlaying ? null : FindSceneObject(name);
        if (go == null && !Application.isPlaying && entry != null && !string.IsNullOrEmpty(entry.scene)
            && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene(entry.scene, OpenSceneMode.Single);
            go = FindSceneObject(name);
        }
        if (go == null && Application.isPlaying) { Transform live = LiveRoot(name); if (live != null) go = live.gameObject; }
        if (go != null) SelectObject(go);
        else EditorUtility.DisplayDialog("UI Hub", $"\"{name}\" was not found.", "OK");
    }

    private static void Changed(UiLayoutData data)
    {
        EditorUtility.SetDirty(data);
        data.MarkChanged();
    }
}
