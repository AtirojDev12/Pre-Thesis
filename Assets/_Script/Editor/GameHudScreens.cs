using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 16 Nov 2026 (Mr.k): moves the screens the game builds by code INTO the GAME HUD prefab, as
/// real children a designer can edit (shop, storage, results, player list, clock, popcorn /
/// ticket UI, ghost bubbles, task boards ...).
///
/// HOW
///   0. Tick "Move code screens into GAME HUD" at the top of the UI Hub (off by default, per PC).
///   1. Press Play and make a screen appear. After 1.5 s a copy of it is taken:
///      pictures, texts, layout and controls (buttons, sliders, toggles, dropdowns, input
///      fields, scroll views). No game scripts. Every part gets a UiLink to the game's part.
///      Lists (rows with the same shape and a common name start) become ONE row template.
///   2. Stop Play. The copies are added to the GAME HUD prefab under
///      "Screens (made by the game)/<category>/<screen>", switched off.
///   3. From then on the game shows THAT version (UiHubScreen) instead of its own screen.
///      Its own screen keeps running, hidden, and feeds the links.
///
/// SAFE
///   - A screen that is already in the GAME HUD is never copied again (your edits stay).
///   - Screens whose buttons use world colliders or their own pointer scripts cannot be shown
///     through links. They stay code-made (restyle them through their Assets/UI Skins prefab).
///   - Switch one screen back to the game's own version: untick "Show In Game" on its
///     UiHubScreen, or delete it from the prefab.
///
/// Manual: Tools > Pre-Thesis > UI > GAME HUD: Move Captured Screens In (also shows what is missing).
/// </summary>
[InitializeOnLoad]
public static class GameHudScreens
{
    public const string PendingFolder = "Assets/UI Skins/_For GAME HUD";
    public const string ScreensGroup = "Screens (made by the game)";
    private const string AutoKey = "PreThesis.UiHub.AutoCaptureScreens";
    private const string Title = "GAME HUD screens";

    private static double nextTick;
    private static readonly Dictionary<string, double> firstSeen = new Dictionary<string, double>();
    private static readonly HashSet<string> tried = new HashSet<string>();
    /// <summary>Screens that cannot be shown through links, with the reason (found in Play mode).</summary>
    public static readonly Dictionary<string, string> Blocked = new Dictionary<string, string>();

    // Only these are copied: visuals and standard controls. No game scripts.
    private static readonly HashSet<Type> Keep = new HashSet<Type>
    {
        typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup), typeof(CanvasRenderer), typeof(GraphicRaycaster),
        typeof(Image), typeof(RawImage), typeof(TextMeshProUGUI), typeof(Text), typeof(Outline), typeof(Shadow),
        typeof(HorizontalLayoutGroup), typeof(VerticalLayoutGroup), typeof(GridLayoutGroup),
        typeof(LayoutElement), typeof(ContentSizeFitter), typeof(AspectRatioFitter), typeof(Mask), typeof(RectMask2D),
        typeof(Button), typeof(Slider), typeof(Toggle), typeof(ToggleGroup), typeof(Scrollbar), typeof(ScrollRect),
        typeof(TMP_Dropdown), typeof(TMP_InputField),
        typeof(MeshFilter), typeof(MeshRenderer),
    };

    static GameHudScreens()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += change =>
        {
            if (change == PlayModeStateChange.EnteredPlayMode) { firstSeen.Clear(); tried.Clear(); }
            if (change == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += () => MovePending(false);
        };
    }

    /// <summary>
    /// Copy each code-built screen the first time it appears in Play mode. OFF by default and kept
    /// per PC: the copies change the shared GAME HUD prefab, so one person does it on purpose
    /// (tick it in the UI Hub) instead of every teammate's editor doing it on every Play.
    /// </summary>
    public static bool AutoCapture
    {
        get => EditorPrefs.GetBool(AutoKey, false);
        set => EditorPrefs.SetBool(AutoKey, value);
    }

    public static string PendingPath(string screen) => $"{PendingFolder}/{UiSkinTools.FileName(screen)}.prefab";

    /// <summary>The screen's editable version inside the GAME HUD prefab file, or null.</summary>
    public static GameObject TemplateInPrefab(string screen)
    {
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(UiScreenCatalog.GameHudPrefab);
        if (hud == null) return null;
        foreach (UiHubScreen s in hud.GetComponentsInChildren<UiHubScreen>(true))
            if (s.ScreenName == screen) return s.gameObject;
        return null;
    }

    public static bool HasPending(string screen) => AssetDatabase.LoadAssetAtPath<GameObject>(PendingPath(screen)) != null;

    // ---- Play mode: copy screens as they appear ------------------------------------------------

    private static void Tick()
    {
        if (!Application.isPlaying || !AutoCapture || EditorApplication.timeSinceStartup < nextTick) return;
        nextTick = EditorApplication.timeSinceStartup + 0.5;
        foreach (UiLayoutRuntime.LiveScreen s in UiLayoutRuntime.Live)
        {
            if (s.root == null || !s.root.gameObject.activeInHierarchy || tried.Contains(s.name)) continue;
            UiScreenCatalog.Entry entry = UiScreenCatalog.Find(s.name);
            if (entry == null || !entry.CodeBuilt) continue; // only screens built by code
            if (s.design != null || TemplateInPrefab(s.name) != null || HasPending(s.name)) { tried.Add(s.name); continue; }
            if (!firstSeen.TryGetValue(s.name, out double since)) { firstSeen[s.name] = EditorApplication.timeSinceStartup; continue; }
            if (EditorApplication.timeSinceStartup - since < 1.5) continue; // let the screen finish building
            tried.Add(s.name);
            try { Capture(s.root, s.name); }
            catch (Exception e) { Debug.LogError($"[GAME HUD screens] Could not copy \"{s.name}\": {e}"); }
        }
    }

    /// <summary>Null when the screen can be shown through links; else why not.</summary>
    public static string WhyNotMirrored(Transform liveRoot)
    {
        if (liveRoot == null) return null;
        if (liveRoot.GetComponentInChildren<Collider>(true) != null)
            return "its parts use world colliders (the player aims at them in the 3D world)";
        foreach (MonoBehaviour m in liveRoot.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (m == null || !(m is IEventSystemHandler)) continue;
            string ns = m.GetType().Namespace ?? "";
            if (ns.StartsWith("UnityEngine") || ns.StartsWith("TMPro")) continue; // standard buttons, sliders ...
            return $"it has its own pointer script ({m.GetType().Name})";
        }
        return null;
    }

    /// <summary>Copies a live screen into the pending folder (moved into the GAME HUD when Play stops).</summary>
    public static void Capture(Transform liveRoot, string screen)
    {
        string why = WhyNotMirrored(liveRoot);
        if (why != null)
        {
            Blocked[screen] = why;
            Debug.Log($"[GAME HUD screens] \"{screen}\" stays code-made: {why}.");
            return;
        }

        // A copy under a switched-off holder: none of its scripts ever wakes up.
        var holder = new GameObject("[GAME HUD capture]") { hideFlags = HideFlags.HideAndDontSave };
        holder.SetActive(false);
        try
        {
            GameObject copy = Object.Instantiate(liveRoot.gameObject, holder.transform, false);
            copy.name = screen;

            Transform[] copies = copy.GetComponentsInChildren<Transform>(true);
            Transform[] lives = liveRoot.GetComponentsInChildren<Transform>(true);
            if (copies.Length != lives.Length)
            {
                Debug.LogWarning($"[GAME HUD screens] \"{screen}\" changed while it was copied. Open it again in Play mode.");
                tried.Remove(screen);
                return;
            }
            var twin = new Dictionary<Transform, Transform>(copies.Length);
            for (int i = 0; i < copies.Length; i++) twin[copies[i]] = lives[i];

            Clean(copy);
            Walk(copy.transform, liveRoot, liveRoot, twin);

            // The game gives the screen its canvas: the root becomes a plain full-size panel.
            foreach (Type t in new[] { typeof(GraphicRaycaster), typeof(CanvasScaler), typeof(Canvas), typeof(CanvasGroup) })
            {
                Component c = copy.GetComponent(t);
                if (c != null) Object.DestroyImmediate(c);
            }
            Canvas liveCanvas = liveRoot.GetComponent<Canvas>();
            var liveRect = liveRoot as RectTransform;
            var rect = copy.transform as RectTransform;
            if (rect != null && liveCanvas != null && liveCanvas.renderMode == RenderMode.WorldSpace && liveRect != null)
            {
                // A world screen (board, bubble) keeps its own size, so it looks the same in the prefab and in the game.
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = liveRect.sizeDelta;
            }
            else if (rect != null)
            {
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero; rect.sizeDelta = Vector2.zero;
            }
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            copy.AddComponent<UiHubScreen>().EditorSetScreenName(screen);

            EnsureFolder(PendingFolder);
            copy.SetActive(false);                 // stays switched off: the game shows copies of it
            copy.transform.SetParent(null, false); // a prefab is saved from a top object
            try { PrefabUtility.SaveAsPrefabAsset(copy, PendingPath(screen)); }
            finally { Object.DestroyImmediate(copy); }
            Debug.Log($"[GAME HUD screens] Copied \"{screen}\". It moves into the GAME HUD prefab when you stop Play.");
        }
        finally
        {
            Object.DestroyImmediate(holder);
        }
    }

    /// <summary>Removes game scripts, generated children, and pictures that only exist while the game runs.</summary>
    private static void Clean(GameObject copy)
    {
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t == copy.transform) continue;
            if (t.hideFlags != HideFlags.None || t.GetComponent<TMP_SubMeshUI>() != null) Object.DestroyImmediate(t.gameObject); // made by TextMeshPro itself
        }
        for (int pass = 0; pass < 3; pass++) // again: a script can only go after the scripts that need it
            foreach (Component c in copy.GetComponentsInChildren<Component>(true))
                if (c != null && !(c is Transform) && !Keep.Contains(c.GetType())) Object.DestroyImmediate(c);

        Sprite plain = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        foreach (Image image in copy.GetComponentsInChildren<Image>(true))
        {
            if (image.sprite != null && !EditorUtility.IsPersistent(image.sprite))
                image.sprite = image.type == Image.Type.Filled ? plain : null; // a Filled image needs a picture to fill
            if (image.material != null && !EditorUtility.IsPersistent(image.material)) image.material = null;
        }
        foreach (RawImage raw in copy.GetComponentsInChildren<RawImage>(true))
        {
            if (raw.texture != null && !EditorUtility.IsPersistent(raw.texture)) raw.texture = null;
            if (raw.material != null && !EditorUtility.IsPersistent(raw.material)) raw.material = null;
        }
        foreach (TMP_Text text in copy.GetComponentsInChildren<TMP_Text>(true))
            if (text.font != null && text.fontSharedMaterial != null && !EditorUtility.IsPersistent(text.fontSharedMaterial))
                text.fontSharedMaterial = text.font.material;
    }

    // ---- Links + lists ---------------------------------------------------------------------------

    /// <summary>
    /// Gives every part under <paramref name="copy"/> a link to its game part. Paths start at
    /// <paramref name="liveBase"/> (the screen, or the row when inside a row template).
    /// </summary>
    private static void Walk(Transform copy, Transform live, Transform liveBase, Dictionary<Transform, Transform> twin)
    {
        var kids = new List<Transform>();
        foreach (Transform k in copy) if (twin.ContainsKey(k)) kids.Add(k);

        var rowStart = new Dictionary<Transform, string>(); // first row of a list -> the rows' name start
        var extraRows = new HashSet<Transform>();
        FindRowGroups(kids, rowStart, extraRows);

        foreach (Transform k in kids)
        {
            if (extraRows.Contains(k)) { Object.DestroyImmediate(k.gameObject); continue; }
            Transform g = twin[k];
            if (k.GetComponent<Scrollbar>() != null) continue; // driven by its Scroll View

            UiLink link = AddLink(k, g, liveBase);
            if (rowStart.TryGetValue(k, out string start))
            {
                link.repeat = true;
                link.repeatNameStart = start;
                link.source = UiLayoutRuntime.PathOf(liveBase, live); // the list = the rows' parent
                Walk(k, g, g, twin);                                  // inside a row: paths start at the row
            }
            else if (!IsControl(k)) Walk(k, g, liveBase, twin);       // a control drives its own inside parts
        }
    }

    private static bool IsControl(Transform t) =>
        t.GetComponent<Slider>() != null || t.GetComponent<Toggle>() != null || t.GetComponent<TMP_Dropdown>() != null || t.GetComponent<TMP_InputField>() != null;

    private static UiLink AddLink(Transform part, Transform game, Transform liveBase)
    {
        UiLink link = part.gameObject.AddComponent<UiLink>();
        link.source = UiLayoutRuntime.PathOf(liveBase, game);
        link.onlyGameChanges = true;
        link.showHide = true;

        bool control = IsControl(part);
        TMP_Text tmp = game.GetComponent<TMP_Text>();
        Text legacy = game.GetComponent<Text>();
        link.text = !control && (tmp != null || legacy != null);
        link.gameText = tmp != null ? tmp.text : legacy != null ? legacy.text : "";

        Graphic graphic = game.GetComponent<Graphic>();
        link.color = graphic != null;
        link.gameColour = graphic != null ? graphic.color : Color.white;

        Image image = game.GetComponent<Image>();
        RawImage raw = game.GetComponent<RawImage>();
        Object picture = image != null ? (Object)image.sprite : raw != null ? raw.texture : null;
        bool generated = picture != null && !EditorUtility.IsPersistent(picture);
        Texture texture = picture is Sprite sprite ? sprite.texture : picture as Texture;
        bool tinyFiller = generated && texture != null && texture.width <= 8 && texture.height <= 8; // a plain colour made by code
        link.picture = (image != null || raw != null) && !tinyFiller;
        link.gamePicture = generated ? null : picture;
        link.pictureAlways = generated && !tinyFiller; // art the game makes while running (static, glow): always shown

        link.fill = image != null && image.type == Image.Type.Filled;
        if (link.fill) link.fillAxis = image.fillMethod == Image.FillMethod.Vertical ? UiLink.Axis.Vertical : UiLink.Axis.Horizontal;
        link.click = game.GetComponent<Button>() != null;
        link.control = control;

        var rect = game as RectTransform;
        link.followLayout = rect != null;
        if (rect != null) { link.gamePosition = rect.anchoredPosition; link.gameSize = rect.sizeDelta; }
        return link;
    }

    /// <summary>
    /// Finds lists among siblings: 2 or more parts with the same shape and a common name start
    /// ("Row 1, Row 2", "Item walkie, Item battery"). Single texts count only with a number at
    /// the end ("Task 1, Task 2"), so different labels are not mistaken for a list.
    /// </summary>
    private static void FindRowGroups(List<Transform> kids, Dictionary<Transform, string> rowStart, HashSet<Transform> extraRows)
    {
        var byShape = new Dictionary<string, List<Transform>>();
        foreach (Transform k in kids)
        {
            if (k.GetComponent<Scrollbar>() != null) continue;
            string shape = Shape(k);
            if (!byShape.TryGetValue(shape, out List<Transform> list)) byShape[shape] = list = new List<Transform>();
            list.Add(k);
        }
        foreach (List<Transform> group in byShape.Values)
        {
            if (group.Count < 2) continue;
            string start = group[0].name;
            foreach (Transform t in group) start = CommonStart(start, t.name);
            if (start.Length < 2) continue;
            bool leaf = group[0].childCount == 0;
            bool ok = true;
            foreach (Transform t in group)
            {
                string rest = t.name.Substring(start.Length);
                if (leaf && (rest.Length == 0 || !IsNumber(rest))) { ok = false; break; }
            }
            if (!ok) continue;
            rowStart[group[0]] = start;
            for (int i = 1; i < group.Count; i++) extraRows.Add(group[i]);
        }
    }

    private static string Shape(Transform t)
    {
        var sb = new StringBuilder();
        var names = new List<string>();
        foreach (Component c in t.GetComponents<Component>())
            if (c != null && !(c is Transform) && !(c is CanvasRenderer)) names.Add(c.GetType().Name);
        names.Sort(string.CompareOrdinal);
        sb.Append(string.Join("+", names)).Append('(');
        foreach (Transform child in t) sb.Append(Shape(child)).Append(',');
        return sb.Append(')').ToString();
    }

    private static string CommonStart(string a, string b)
    {
        int n = Mathf.Min(a.Length, b.Length), i = 0;
        while (i < n && a[i] == b[i]) i++;
        // "Row 1" / "Row 12": do not cut inside the number.
        while (i > 0 && char.IsDigit(a[i - 1])) i--;
        return a.Substring(0, i);
    }

    private static bool IsNumber(string s)
    {
        foreach (char ch in s) if (!char.IsDigit(ch)) return false;
        return s.Length > 0;
    }

    // ---- Edit mode: move the copies into the GAME HUD prefab --------------------------------------

    [MenuItem("Tools/Pre-Thesis/UI/GAME HUD: Move Captured Screens In", priority = 2)]
    private static void MenuMove() => MovePending(true);

    /// <summary>Adds every pending copy to the GAME HUD prefab (never replaces a screen that is already there).</summary>
    public static void MovePending(bool showDialog)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (showDialog) EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK");
            return;
        }
        var pending = new List<GameObject>();
        if (AssetDatabase.IsValidFolder(PendingFolder))
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PendingFolder }))
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (p != null && p.GetComponent<UiHubScreen>() != null) pending.Add(p);
            }

        var log = new StringBuilder();
        if (pending.Count > 0)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(UiScreenCatalog.GameHudPrefab) == null)
            {
                Debug.LogWarning("[GAME HUD screens] Cannot find " + UiScreenCatalog.GameHudPrefab + ".");
                return;
            }
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == UiScreenCatalog.GameHudPrefab)
            {
                string wait = $"{pending.Count} copied screen(s) wait to move into the GAME HUD. The prefab is open: save it, go back to the scene, " +
                              "then run Tools > Pre-Thesis > UI > GAME HUD: Move Captured Screens In.";
                if (showDialog) EditorUtility.DisplayDialog(Title, wait, "OK"); else Debug.LogWarning("[GAME HUD screens] " + wait);
                return;
            }

            var moved = new List<string>();
            using (var scope = new PrefabUtility.EditPrefabContentsScope(UiScreenCatalog.GameHudPrefab))
            {
                Transform root = scope.prefabContentsRoot.transform;
                var have = new HashSet<string>();
                foreach (UiHubScreen s in root.GetComponentsInChildren<UiHubScreen>(true)) have.Add(s.ScreenName);
                foreach (GameObject p in pending)
                {
                    string screen = p.GetComponent<UiHubScreen>().ScreenName;
                    moved.Add(screen);
                    if (have.Contains(screen)) continue; // already there: your version stays
                    UiScreenCatalog.Entry entry = UiScreenCatalog.Find(screen);
                    Transform group = Group(Group(root, ScreensGroup), entry != null ? entry.category : UiScreenCatalog.Other);
                    GameObject child = Object.Instantiate(p);
                    child.name = screen;
                    child.transform.SetParent(group, false);
                    SetLayer(child.transform, root.gameObject.layer);
                    child.SetActive(false);
                    have.Add(screen);
                    log.AppendLine("+ " + screen);
                }
            }
            foreach (GameObject p in pending) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(p));
            RemoveOldSkins(moved, log);
            AssetDatabase.SaveAssets();
            if (log.Length > 0) Debug.Log("[GAME HUD screens] Moved into the GAME HUD prefab:\n" + log);
        }
        if (!showDialog) return;

        // Report: what is in, what is still missing.
        var report = new StringBuilder();
        if (log.Length > 0) report.AppendLine("Moved in now:").Append(log).AppendLine();
        var missing = new List<string>();
        int inHud = 0;
        foreach (UiScreenCatalog.Entry e in UiScreenCatalog.All)
        {
            if (!e.CodeBuilt) continue;
            if (TemplateInPrefab(e.screen) != null) inHud++;
            else if (Blocked.TryGetValue(e.screen, out string why)) missing.Add($"{e.label}: stays code-made ({why})");
            else missing.Add($"{e.label}: {e.how}");
        }
        report.AppendLine($"{inHud} code-built screen(s) are in the GAME HUD prefab.");
        if (missing.Count > 0)
        {
            report.AppendLine().AppendLine("Not in yet. Tick \"Move code screens into GAME HUD\" in the UI Hub, press Play, make each one appear once, then stop Play:");
            foreach (string m in missing) report.AppendLine("- " + m);
        }
        Debug.Log("[GAME HUD screens]\n" + report);
        EditorUtility.DisplayDialog(Title, report.ToString(), "OK");
    }

    /// <summary>The old auto-made prefabs (Assets/UI Skins) and empty UI Layout entries of moved screens are not used any more.</summary>
    private static void RemoveOldSkins(List<string> screens, StringBuilder log)
    {
        var data = AssetDatabase.LoadAssetAtPath<UiLayoutData>("Assets/Resources/UI/UI Layout.asset");
        bool dataChanged = false;
        foreach (string screen in screens)
        {
            UiScreenLook look = data != null ? data.FindScreen(screen) : null;
            bool hasEdits = look != null && (look.elements.Count > 0 || look.added.Count > 0 || look.design != null || look.hide || look.overrideSortOrder || look.font != null);
            if (hasEdits)
            {
                log.AppendLine($"! \"{screen}\": its old saved changes are kept (UI Layout + Assets/UI Skins). They act on the hidden game screen only; redo them on the GAME HUD version.");
                continue;
            }
            if (look != null) { data.screens.Remove(look); dataChanged = true; }
            foreach (string path in new[] { UiSkinTools.SkinPath(screen), UiSkinTools.OriginalPath(screen) })
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) AssetDatabase.DeleteAsset(path);
        }
        if (!dataChanged) return;
        EditorUtility.SetDirty(data);
        data.MarkChanged();
    }

    /// <summary>Removes a screen's editable version from the GAME HUD prefab (the game shows its own screen again).</summary>
    public static void RemoveTemplate(string screen)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == UiScreenCatalog.GameHudPrefab)
        {
            EditorUtility.DisplayDialog(Title, "The GAME HUD prefab is open. Save it, go back to the scene, then try again.", "OK");
            return;
        }
        using (var scope = new PrefabUtility.EditPrefabContentsScope(UiScreenCatalog.GameHudPrefab))
            foreach (UiHubScreen s in scope.prefabContentsRoot.GetComponentsInChildren<UiHubScreen>(true))
                if (s.ScreenName == screen) Object.DestroyImmediate(s.gameObject);
        AssetDatabase.SaveAssets();
    }

    /// <summary>Open the GAME HUD prefab showing only this screen (the others are switched off).</summary>
    public static void OpenTemplate(GameObject templateInAsset)
    {
        if (templateInAsset == null) return;
        GameObject asset = templateInAsset.transform.root.gameObject;
        string path = UiLayoutRuntime.PathOf(asset.transform, templateInAsset.transform);
        AssetDatabase.OpenAsset(asset);
        PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null) return;
        Transform target = UiLayoutRuntime.Resolve(stage.prefabContentsRoot.transform, path);
        if (target == null) return;
        foreach (UiHubScreen s in stage.prefabContentsRoot.GetComponentsInChildren<UiHubScreen>(true))
        {
            bool show = s.transform == target;
            if (s.gameObject.activeSelf != show) s.gameObject.SetActive(show); // only for looking: the game ignores this state
        }
        Selection.activeGameObject = target.gameObject;
        EditorGUIUtility.PingObject(target.gameObject);
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
    }

    // ---- Small helpers -----------------------------------------------------------------------------

    private static Transform Group(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t != null) return t;
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform child in t) SetLayer(child, layer);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
