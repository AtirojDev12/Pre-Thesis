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
    private static string search = "";
    private static int filter; // 0 all, 1 on screen now, 2 changed
    private static List<GameObject> uiPrefabs;
    private double nextRepaint;

    // NonSerialized: Unity keeps private fields over a script reload, which left a new style null (8 Nov NRE).
    [System.NonSerialized] private GUIStyle headerStyle, smallStyle, okStyle, warnStyle, editButton;

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
        if (headerStyle != null && smallStyle != null && okStyle != null && warnStyle != null && editButton != null) return;
        headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
        smallStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
        okStyle = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.35f, 0.85f, 0.4f) } };
        warnStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = new Color(1f, 0.7f, 0.25f) } };
        editButton = new GUIStyle(EditorStyles.miniButton) { fontStyle = FontStyle.Bold };
    }

    public override void OnInspectorGUI()
    {
        Styles();
        EditorGUILayout.LabelField("UI Hub — every game screen", headerStyle);
        EditorGUILayout.HelpBox(
            "Click a screen to open it. Change its pictures, texts, fonts and 3D models right there.\n" +
            "Press Edit to open the screen as a prefab: move things, add new pictures or 3D models, delete parts. Save the prefab. Done.\n" +
            "Screens made by code get their prefab the first time they appear in Play mode.", MessageType.Info);

        UiLayoutData data = UiLayoutData.Load();
        if (data == null)
        {
            if (GUILayout.Button("Make the UI Layout data", GUILayout.Height(26))) data = UiChangeRecorder.EnsureData();
            if (data == null) return;
        }

        DrawSaveBar();
        DrawGlobal(data);
        EditorGUILayout.Space(6);
        DrawScreens(data);
        EditorGUILayout.Space(10);
        DrawOtherUi();
    }

    private void DrawSaveBar()
    {
        if (!Application.isPlaying) return;
        int n = UiChangeRecorder.PendingList.Count;
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            GUILayout.Label(n == 0 ? "Play mode: edits you make on screens are kept when you press Save." : $"{n} Play-mode change(s) waiting to be saved.",
                n == 0 ? smallStyle : EditorStyles.boldLabel);
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

    private void DrawGlobal(UiLayoutData data)
    {
        EditorGUI.BeginChangeCheck();
        var font = (TMP_FontAsset)EditorGUILayout.ObjectField(new GUIContent("Font for all UI", "Every text in every screen uses this font. Empty = keep each screen's fonts."), data.fontForAllUi, typeof(TMP_FontAsset), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(data, "UI font");
            data.fontForAllUi = font;
            Changed(data);
        }
        UiSkinTools.AutoMake = EditorGUILayout.Toggle(new GUIContent("Auto-make prefabs", "Make an editable prefab for each code-built screen the first time it appears in Play mode."), UiSkinTools.AutoMake);
    }

    // ---- Screen list ----------------------------------------------------------------------------

    private void DrawScreens(UiLayoutData data)
    {
        EditorGUILayout.LabelField("Screens", headerStyle);
        using (new EditorGUILayout.HorizontalScope())
        {
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            filter = GUILayout.Toolbar(filter, new[] { "All", "On screen", "Changed" }, GUILayout.Width(210));
        }

        var names = new List<string>();
        foreach (UiScreenCatalog.Entry e in UiScreenCatalog.All) names.Add(e.screen);
        foreach (UiScreenLook s in data.screens) if (s != null && !names.Contains(s.screenName)) names.Add(s.screenName);
        foreach (UiLayoutRuntime.LiveScreen s in UiLayoutRuntime.Live) if (s.root != null && !names.Contains(s.name)) names.Add(s.name);

        int shown = 0;
        foreach (string name in names)
        {
            UiScreenCatalog.Entry entry = UiScreenCatalog.Find(name);
            UiScreenLook look = data.FindScreen(name);
            bool live = LiveRoot(name) != null;
            if (filter == 1 && !live) continue;
            if (filter == 2 && (look == null || look.ChangeCount == 0) && PendingCount(name) == 0) continue;
            string label = entry != null ? entry.label : name;
            if (!string.IsNullOrEmpty(search) && label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            DrawScreen(data, name, entry, look, live);
            shown++;
        }
        if (shown == 0) EditorGUILayout.LabelField(filter == 1 ? "No screen is on screen now." : "Nothing found.", smallStyle);
    }

    /// <summary>What the designer edits for this screen: a real prefab, a scene object, or the screen's skin prefab.</summary>
    private static GameObject EditTarget(string name, UiScreenCatalog.Entry entry, out string kind)
    {
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
        kind = "skin";
        return UiSkinTools.SkinOf(name);
    }

    private void DrawScreen(UiLayoutData data, string name, UiScreenCatalog.Entry entry, UiScreenLook look, bool live)
    {
        GameObject target = EditTarget(name, entry, out string kind);
        Transform liveRoot = LiveRoot(name);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                open.TryGetValue(name, out bool isOpen);
                isOpen = EditorGUILayout.Foldout(isOpen, entry != null ? entry.label : name, true, EditorStyles.foldoutHeader);
                open[name] = isOpen;
                GUILayout.FlexibleSpace();
                if (live) GUILayout.Label("● on screen", okStyle, GUILayout.Width(70));
                int waiting = PendingCount(name);
                if (waiting > 0) GUILayout.Label($"{waiting} unsaved", EditorStyles.miniBoldLabel, GUILayout.Width(60));
                DrawEditButton(name, entry, kind, target, liveRoot);
                if (GUILayout.Button("⋮", EditorStyles.miniButton, GUILayout.Width(22))) ShowMoreMenu(data, name, entry, kind, liveRoot, look);
            }
            if (!open[name]) return;

            if (entry != null && !string.IsNullOrEmpty(entry.how)) EditorGUILayout.LabelField("How to see it in the game: " + entry.how, smallStyle);

            if (target != null) DrawParts(name, target, kind);
            else if (kind == "skin")
                EditorGUILayout.HelpBox(liveRoot != null
                    ? "This screen has no prefab yet. Press Edit to make it now."
                    : "This screen is made by code. Press Play and make it appear once: its prefab is made for you.", MessageType.None);
            else if (kind == "scene")
                EditorGUILayout.HelpBox(Application.isPlaying
                    ? "Stop Play to edit this screen in its scene (or edit it now and press Save changes)."
                    : $"This screen is in the {System.IO.Path.GetFileNameWithoutExtension(entry.scene)} scene. Press Edit to open it.", MessageType.None);

            if (kind == "skin" || kind == "design") DrawDesign(data, name, data.FindScreen(name), UiSkinTools.SkinOf(name));
            DrawRelated(entry);
            DrawAdvanced(data, name, data.FindScreen(name));
        }
    }

    private void DrawEditButton(string name, UiScreenCatalog.Entry entry, string kind, GameObject target, Transform liveRoot)
    {
        string tip;
        bool can;
        if (kind == "prefab") { can = target != null; tip = "Open the prefab."; }
        else if (kind == "scene") { can = true; tip = "Open the scene and select the screen."; }
        else if (kind == "design") { can = target != null; tip = "Open your design prefab."; }
        else if (target != null) { can = true; tip = "Open the screen's prefab."; }
        else { can = liveRoot != null; tip = can ? "Make the screen's prefab now and open it." : "Press Play and make this screen appear first."; }

        using (new EditorGUI.DisabledScope(!can))
        {
            if (!GUILayout.Button(new GUIContent("Edit", tip), editButton, GUILayout.Width(52))) return;
            if (kind == "prefab") OpenPrefab(target);
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

        menu.AddSeparator("");
        if (entry != null && !string.IsNullOrEmpty(entry.script))
            menu.AddItem(new GUIContent("For programmers: open the script"), false, () => AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<Object>(entry.script)));
        menu.ShowAsContext();
    }

    // ---- Parts: pictures, texts, models -----------------------------------------------------------

    private void DrawParts(string name, GameObject root, string kind)
    {
        EditorGUILayout.LabelField("Pictures, texts and 3D models", EditorStyles.boldLabel);
        bool isAsset = EditorUtility.IsPersistent(root);

        partSearch.TryGetValue(name, out string find);
        find = EditorGUILayout.TextField(find ?? "", EditorStyles.toolbarSearchField);
        partSearch[name] = find;

        var rows = new List<Component>();
        foreach (Component c in root.GetComponentsInChildren<Component>(true))
        {
            if (!(c is Image || c is RawImage || c is TMP_Text || c is Text || c is MeshFilter || c is MeshRenderer)) continue;
            if (!string.IsNullOrEmpty(find) && c.name.IndexOf(find, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            rows.Add(c);
        }
        if (rows.Count == 0) { EditorGUILayout.LabelField("No pictures, texts or models found.", smallStyle); return; }

        bool all = showAllParts.Contains(name);
        int count = all ? rows.Count : Mathf.Min(rows.Count, RowsPerPage);
        bool changedAsset = false;
        for (int i = 0; i < count; i++)
            if (DrawPart(rows[i], root.transform)) changedAsset |= isAsset;

        if (rows.Count > RowsPerPage && GUILayout.Button(all ? "Show fewer" : $"Show all {rows.Count}", EditorStyles.miniButton))
        {
            if (all) showAllParts.Remove(name); else showAllParts.Add(name);
        }

        if (changedAsset)
        {
            PrefabUtility.SavePrefabAsset(root); // saved at once; a code screen's prefab goes to the game by itself
            if (kind == "design") UiLayoutData.Load()?.MarkChanged(); // show the new look in Play mode
        }
        EditorGUILayout.LabelField(kind == "scene"
            ? "To add new pictures or 3D models: press Edit, then drag them under the screen."
            : "To add new pictures or 3D models, or move things: press Edit, drag them into the prefab, save.", smallStyle);
    }

    /// <summary>One part row. True when something changed.</summary>
    private bool DrawPart(Component c, Transform root)
    {
        string path = c.transform == root ? c.name : UiLayoutRuntime.PathOf(root, c.transform);
        bool changed = false;
        using (new EditorGUILayout.HorizontalScope())
        {
            string what = c is Image || c is RawImage ? "Picture" : c is MeshFilter ? "3D model" : c is MeshRenderer ? "Material" : "Text";
            GUILayout.Label(new GUIContent($"{what}: {ShortPath(path)}", path), EditorStyles.miniLabel, GUILayout.Width(170));

            if (c is Image img)
            {
                var sprite = (Sprite)EditorGUILayout.ObjectField(img.sprite, typeof(Sprite), false);
                Color col = EditorGUILayout.ColorField(GUIContent.none, img.color, true, true, false, GUILayout.Width(50));
                if (sprite != img.sprite || col != img.color) { Undo.RecordObject(img, "UI picture"); img.sprite = sprite; img.color = col; changed = true; }
            }
            else if (c is RawImage raw)
            {
                var tex = (Texture)EditorGUILayout.ObjectField(raw.texture, typeof(Texture), false);
                Color col = EditorGUILayout.ColorField(GUIContent.none, raw.color, true, true, false, GUILayout.Width(50));
                if (tex != raw.texture || col != raw.color) { Undo.RecordObject(raw, "UI picture"); raw.texture = tex; raw.color = col; changed = true; }
            }
            else if (c is TMP_Text tmp)
            {
                string words = EditorGUILayout.TextField(tmp.text);
                var font = (TMP_FontAsset)EditorGUILayout.ObjectField(tmp.font, typeof(TMP_FontAsset), false, GUILayout.Width(90));
                float size = EditorGUILayout.FloatField(tmp.fontSize, GUILayout.Width(36));
                Color col = EditorGUILayout.ColorField(GUIContent.none, tmp.color, true, true, false, GUILayout.Width(40));
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
                string words = EditorGUILayout.TextField(legacy.text);
                var font = (Font)EditorGUILayout.ObjectField(legacy.font, typeof(Font), false, GUILayout.Width(90));
                int size = EditorGUILayout.IntField(legacy.fontSize, GUILayout.Width(36));
                Color col = EditorGUILayout.ColorField(GUIContent.none, legacy.color, true, true, false, GUILayout.Width(40));
                if (words != legacy.text || font != legacy.font || size != legacy.fontSize || col != legacy.color)
                {
                    Undo.RecordObject(legacy, "UI text");
                    legacy.text = words; legacy.font = font; legacy.fontSize = size; legacy.color = col;
                    changed = true;
                }
            }
            else if (c is MeshFilter mf)
            {
                var mesh = (Mesh)EditorGUILayout.ObjectField(mf.sharedMesh, typeof(Mesh), false);
                if (mesh != mf.sharedMesh) { Undo.RecordObject(mf, "UI model"); mf.sharedMesh = mesh; changed = true; }
            }
            else if (c is MeshRenderer mr)
            {
                var mat = (Material)EditorGUILayout.ObjectField(mr.sharedMaterial, typeof(Material), false);
                if (mat != mr.sharedMaterial) { Undo.RecordObject(mr, "UI material"); mr.sharedMaterial = mat; changed = true; }
            }
            if (GUILayout.Button(new GUIContent("→", "Show this part"), EditorStyles.miniButton, GUILayout.Width(22))) ShowPart(c);
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
        EditorGUILayout.LabelField("Use your own design (any prefab)", EditorStyles.boldLabel);
        GameObject design = look != null ? look.design : null;

        EditorGUI.BeginChangeCheck();
        var picked = (GameObject)EditorGUILayout.ObjectField(new GUIContent("My design",
            "Drag any UI prefab here (from any folder, Resources too). The game hides its own screen and shows this one."), design, typeof(GameObject), false);
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

        using (new EditorGUILayout.HorizontalScope())
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
                if (GUILayout.Button("Open my design", EditorStyles.miniButtonLeft)) { OpenPrefab(design); GUIUtility.ExitGUI(); }
                using (new EditorGUI.DisabledScope(skin == null))
                    if (GUILayout.Button(new GUIContent("Auto-connect by name", "Parts with the same name as a game part get connected."), EditorStyles.miniButtonMid))
                    {
                        int n = UiDesignTools.AutoConnect(design, skin.transform);
                        data.MarkChanged();
                        Debug.Log($"[UI Hub] \"{name}\": connected {n} more part(s) by name.");
                        GUIUtility.ExitGUI();
                    }
                if (GUILayout.Button(new GUIContent("Stop using it", "Show the game's own screen again (your prefab is kept)."), EditorStyles.miniButtonRight))
                {
                    UiDesignTools.UseDesign(name, null);
                    GUIUtility.ExitGUI();
                }
            }
        }

        if (design == null)
        {
            EditorGUILayout.LabelField("Your design can look like anything. Connected parts get the game's words, bars, pictures and clicks.", smallStyle);
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
        EditorGUILayout.LabelField("Connect: drag a part of your design next to each game part", EditorStyles.miniBoldLabel);
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
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent($"{what}: {ShortPath(path)}", path), EditorStyles.miniLabel, GUILayout.Width(170));
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
            EditorGUILayout.LabelField($"⚠ {missing} game word / button / bar / icon part(s) are not connected: your design will not show them.", warnStyle);
    }

    private void DrawLinkOptions(UiLayoutData data, GameObject design, UiLink link, Transform gamePart)
    {
        var so = new SerializedObject(link);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(176);
            Mini(so, "showHide", "Show/hide");
            Mini(so, "text", "Words");
            Mini(so, "color", "Colour");
            Mini(so, "picture", "Picture");
            Mini(so, "fill", "Bar");
            Mini(so, "click", "Click");
            Mini(so, "repeat", "Repeat rows");
        }
        if (so.ApplyModifiedProperties())
        {
            AssetDatabase.SaveAssetIfDirty(design);
            data.MarkChanged();
        }
        if (link.repeat && gamePart.childCount > 0)
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(176);
                if (GUILayout.Button(new GUIContent("Connect row parts by name", "Parts inside your row template get connected to the parts of one game row with the same name."), EditorStyles.miniButton))
                {
                    int n = UiDesignTools.AutoConnect(design, gamePart.GetChild(0), UiLayoutRuntime.PathOf(design.transform, link.transform));
                    data.MarkChanged();
                    Debug.Log($"[UI Hub] Connected {n} row part(s) by name.");
                    GUIUtility.ExitGUI();
                }
            }
    }

    private static void Mini(SerializedObject so, string field, string label)
    {
        SerializedProperty p = so.FindProperty(field);
        p.boolValue = GUILayout.Toggle(p.boolValue, label, EditorStyles.miniButton);
    }

    // ---- Related models / settings ----------------------------------------------------------------

    private void DrawRelated(UiScreenCatalog.Entry entry)
    {
        if (entry == null || entry.related == null || entry.related.Length == 0) return;
        EditorGUILayout.LabelField("Models and settings this screen uses", EditorStyles.boldLabel);
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
        bool now = EditorGUILayout.Foldout(adv, "More: hide screen, draw order, font, saved changes", true);
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
        hide = EditorGUILayout.Toggle(new GUIContent("Hide this screen", "The whole screen is not drawn."), hide);
        sort = EditorGUILayout.Toggle(new GUIContent("Change draw order", "Which screen is drawn on top."), sort);
        using (new EditorGUI.DisabledScope(!sort))
            order = EditorGUILayout.IntField(new GUIContent("Order (higher = on top)"), order);
        font = (TMP_FontAsset)EditorGUILayout.ObjectField(new GUIContent("Font for this screen", "Every text in this screen uses this font."), font, typeof(TMP_FontAsset), false);
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
            EditorGUILayout.LabelField("No saved changes yet.", smallStyle);
            return;
        }
        EditorGUILayout.LabelField($"Saved changes ({look.elements.Count} changed, {look.added.Count} added)", EditorStyles.miniBoldLabel);
        int screenIndex = data.screens.IndexOf(look);
        for (int i = 0; i < look.elements.Count; i++)
        {
            UiElementLook e = look.elements[i];
            if (e == null) continue;
            string key = name + "|" + e.path;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent((e.fromSkin ? "[prefab] " : "") + e.path, e.path), EditorStyles.miniLabel, GUILayout.MinWidth(80));
                GUILayout.FlexibleSpace();
                GUILayout.Label(Groups(e), EditorStyles.miniLabel);
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
                    EditorGUILayout.LabelField($"   ⚠ {group}: the game code (or a layout group) keeps changing this, so your change cannot stay. Ask a programmer.", warnStyle);
            }

            if (!e.fromSkin && editOpen.Contains(key)) DrawElementFields(data, screenIndex, i, e);
        }
        foreach (UiAddedObject a in look.added)
            if (a != null) EditorGUILayout.LabelField($"[prefab] added: {(string.IsNullOrEmpty(a.parentPath) ? "" : a.parentPath + "/")}{(a.source != null ? a.source.name : "?")}", EditorStyles.miniLabel);
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
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
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
            if (!any) { EditorGUILayout.LabelField("Waiting to be saved", EditorStyles.miniBoldLabel); any = true; }
            EditorGUILayout.LabelField($"   {p.path}  ({p.group})", EditorStyles.miniLabel);
        }
    }

    // ---- Other UI ------------------------------------------------------------------------------

    private void DrawOtherUi()
    {
        EditorGUILayout.LabelField("Other UI", headerStyle);
        EditorGUILayout.LabelField(
            "Main Menu / Settings / Esc menu are rebuilt by the Tools > Pre-Thesis > Rebuild ... tools, which removes direct edits. " +
            "Edit them in Play mode with Save changes instead: those edits are kept.", smallStyle);

        var hub = (UiHub)target;
        bool isAsset = EditorUtility.IsPersistent(hub);
        EditorGUILayout.LabelField("GAME HUD parts", EditorStyles.miniBoldLabel);
        foreach (Transform child in hub.transform)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("   " + child.name, EditorStyles.miniLabel);
                if (GUILayout.Button(isAsset ? "Open prefab" : "Select", EditorStyles.miniButton, GUILayout.Width(80)))
                {
                    if (isAsset) ShowPart(child);
                    else SelectObject(child.gameObject);
                }
            }
        }

        EditorGUILayout.LabelField("UI prefabs in the project", EditorStyles.miniBoldLabel);
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
                EditorGUILayout.LabelField("   " + p.name, EditorStyles.miniLabel);
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
