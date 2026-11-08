using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 8 Nov 2026 (Mr.k): gives every screen that is built by code a REAL prefab a designer can edit
/// (Assets/UI Skins/&lt;screen&gt;.prefab): change pictures, 3D models, fonts, texts, colours,
/// positions; add new pictures or models; delete parts. Then just save the prefab.
///
/// How it works
///   - The first time a code screen appears in Play mode, a copy of it is saved twice:
///     the skin (for the designer) and _Original (never edited).
///     Only visual parts are copied (Image, RawImage, text, layout, mesh); no game scripts.
///   - When the skin prefab is saved, it is compared with _Original. The differences become
///     the screen's saved changes in the UI Layout asset (changed / hidden / added objects),
///     and UiLayoutRuntime puts them on the live screen, also in builds.
/// </summary>
[InitializeOnLoad]
public static class UiSkinTools
{
    public const string Folder = "Assets/UI Skins";
    public const string OriginalFolder = "Assets/UI Skins/_Original";
    private const string AutoKey = "PreThesis.UiHub.AutoSkins";

    private static readonly Dictionary<string, double> firstSeen = new Dictionary<string, double>();
    private static readonly HashSet<string> tried = new HashSet<string>();
    private static readonly HashSet<string> compileQueue = new HashSet<string>();
    private static double nextTick;

    private static readonly UiLookGroup[] CompareGroups =
    {
        UiLookGroup.Layout, UiLookGroup.Color, UiLookGroup.Sprite, UiLookGroup.TextStyle,
        UiLookGroup.Text, UiLookGroup.Spacing, UiLookGroup.Size,
    };

    // Only these are copied into a skin: visuals, no game scripts.
    private static readonly HashSet<Type> Copyable = new HashSet<Type>
    {
        typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup),
        typeof(Image), typeof(RawImage), typeof(TextMeshProUGUI), typeof(Text), typeof(Outline), typeof(Shadow),
        typeof(HorizontalLayoutGroup), typeof(VerticalLayoutGroup), typeof(GridLayoutGroup),
        typeof(LayoutElement), typeof(ContentSizeFitter), typeof(AspectRatioFitter), typeof(Mask), typeof(RectMask2D),
        typeof(MeshFilter), typeof(MeshRenderer),
        typeof(Button), // only so the hub knows which parts are buttons (no click code is copied)
    };

    static UiSkinTools()
    {
        EditorApplication.update += AutoTick;
        EditorApplication.playModeStateChanged += change =>
        {
            if (change == PlayModeStateChange.EnteredPlayMode) { firstSeen.Clear(); tried.Clear(); }
        };
    }

    /// <summary>Make a prefab for each code screen the first time it appears in Play mode.</summary>
    public static bool AutoMake
    {
        get => EditorPrefs.GetBool(AutoKey, true);
        set => EditorPrefs.SetBool(AutoKey, value);
    }

    // ---- Paths --------------------------------------------------------------------------------

    public static string FileName(string screen)
    {
        var chars = screen.ToCharArray();
        char[] bad = Path.GetInvalidFileNameChars();
        for (int i = 0; i < chars.Length; i++) if (Array.IndexOf(bad, chars[i]) >= 0) chars[i] = '_';
        return new string(chars).Trim();
    }

    public static string SkinPath(string screen) => $"{Folder}/{FileName(screen)}.prefab";
    public static string OriginalPath(string screen) => $"{OriginalFolder}/{FileName(screen)}.prefab";

    /// <summary>The screen's editable prefab (null if not made yet).</summary>
    public static GameObject SkinOf(string screen)
    {
        UiLayoutData data = UiLayoutData.Load();
        UiScreenLook look = data != null ? data.FindScreen(screen) : null;
        if (look != null && look.skin != null) return look.skin;
        return AssetDatabase.LoadAssetAtPath<GameObject>(SkinPath(screen));
    }

    // ---- Making a skin -----------------------------------------------------------------------

    private static void AutoTick()
    {
        if (!Application.isPlaying || !AutoMake || EditorApplication.timeSinceStartup < nextTick) return;
        nextTick = EditorApplication.timeSinceStartup + 0.5;
        foreach (UiLayoutRuntime.LiveScreen s in UiLayoutRuntime.Live)
        {
            if (s.root == null || !s.root.gameObject.activeInHierarchy || tried.Contains(s.name)) continue;
            UiScreenCatalog.Entry entry = UiScreenCatalog.Find(s.name);
            if (entry == null || !entry.CodeBuilt) continue; // only screens built by code
            if (SkinOf(s.name) != null) { tried.Add(s.name); continue; }
            if (!firstSeen.TryGetValue(s.name, out double since)) { firstSeen[s.name] = EditorApplication.timeSinceStartup; continue; }
            if (EditorApplication.timeSinceStartup - since < 1.5) continue; // let the screen finish building
            tried.Add(s.name);
            MakeSkin(s.root, s.name);
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    /// <summary>Saves a copy of the live screen as its editable prefab (+ an untouched original). Replaces old ones.</summary>
    public static GameObject MakeSkin(Transform liveRoot, string screen)
    {
        if (liveRoot == null) return null;
        EnsureFolder(OriginalFolder);
        GameObject copy = CopyTree(liveRoot, null, true);
        try
        {
            string skinPath = SkinPath(screen), originalPath = OriginalPath(screen);
            GameObject original = PrefabUtility.SaveAsPrefabAsset(copy, originalPath);
            GameObject skin = PrefabUtility.SaveAsPrefabAsset(copy, skinPath);
            foreach (GameObject asset in new[] { original, skin })
            {
                if (asset == null || asset.activeSelf) continue;
                asset.SetActive(true); // the copy was made switched off so it never showed on screen
                PrefabUtility.SavePrefabAsset(asset);
            }

            UiLayoutData data = UiChangeRecorder.EnsureData();
            Undo.RecordObject(data, "Make UI prefab");
            UiScreenLook look = data.GetOrAddScreen(screen);
            look.skin = skin;
            look.elements.RemoveAll(e => e == null || e.fromSkin);
            look.added.Clear();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            data.MarkChanged();
            Debug.Log($"[UI Hub] Made an editable prefab for \"{screen}\": {skinPath}", skin);
            return skin;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(copy);
        }
    }

    private static GameObject CopyTree(Transform src, Transform parent, bool isRoot)
    {
        var go = src is RectTransform ? new GameObject(src.name, typeof(RectTransform)) : new GameObject(src.name);
        if (isRoot) go.SetActive(false);
        go.layer = src.gameObject.layer;
        if (parent != null) go.transform.SetParent(parent, false);
        if (!isRoot) go.SetActive(src.gameObject.activeSelf);

        if (src is RectTransform srt && go.transform is RectTransform rt)
        {
            rt.anchorMin = srt.anchorMin; rt.anchorMax = srt.anchorMax; rt.pivot = srt.pivot;
            rt.anchoredPosition = srt.anchoredPosition; rt.sizeDelta = srt.sizeDelta;
            rt.localRotation = srt.localRotation; rt.localScale = srt.localScale;
        }
        else if (!isRoot)
        {
            go.transform.localPosition = src.localPosition;
            go.transform.localRotation = src.localRotation;
            go.transform.localScale = src.localScale;
        }

        foreach (Component c in src.GetComponents<Component>())
        {
            if (c == null || c is Transform || !Copyable.Contains(c.GetType())) continue;
            if (go.GetComponent(c.GetType()) != null) continue;
            Component dst = go.AddComponent(c.GetType());
            if (dst != null) EditorUtility.CopySerialized(c, dst);
        }

        for (int i = 0; i < src.childCount; i++)
        {
            Transform child = src.GetChild(i);
            if (child.hideFlags != HideFlags.None || child.GetComponent<TMP_SubMeshUI>() != null) continue; // made by TextMeshPro itself
            CopyTree(child, go.transform, false);
        }
        return go;
    }

    // ---- Skin -> saved changes ----------------------------------------------------------------

    private sealed class SkinImport : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool any = false;
            foreach (string p in imported)
            {
                if (!p.StartsWith(Folder + "/") || p.StartsWith(OriginalFolder + "/") || !p.EndsWith(".prefab")) continue;
                compileQueue.Add(p);
                any = true;
            }
            if (any) EditorApplication.delayCall += CompileQueued;
        }
    }

    private static void CompileQueued()
    {
        if (compileQueue.Count == 0) return;
        var paths = new List<string>(compileQueue);
        compileQueue.Clear();
        UiLayoutData data = UiLayoutData.Load();
        if (data == null) return;
        foreach (string p in paths)
            foreach (UiScreenLook look in data.screens)
                if (look != null && look.skin != null && AssetDatabase.GetAssetPath(look.skin) == p) Compile(look.screenName);
    }

    /// <summary>Turns the differences between the skin prefab and its original into the screen's saved changes.</summary>
    public static void Compile(string screen)
    {
        UiLayoutData data = UiLayoutData.Load();
        UiScreenLook look = data != null ? data.FindScreen(screen) : null;
        if (look == null || look.skin == null) return;
        var original = AssetDatabase.LoadAssetAtPath<GameObject>(OriginalPath(screen));
        if (original == null) { Debug.LogWarning($"[UI Hub] \"{screen}\": the original copy is missing ({OriginalPath(screen)}). Remake the prefab in Play mode."); return; }

        Dictionary<string, Transform> skinNodes = Map(look.skin.transform);
        Dictionary<string, Transform> originalNodes = Map(original.transform);

        Undo.RecordObject(data, "UI prefab changes");
        look.elements.RemoveAll(e => e == null || e.fromSkin);
        look.added.Clear();
        var a = new UiElementLook();
        var b = new UiElementLook();

        foreach (KeyValuePair<string, Transform> pair in skinNodes)
        {
            Transform s = pair.Value;
            if (originalNodes.TryGetValue(pair.Key, out Transform o))
            {
                foreach (UiLookGroup g in CompareGroups)
                {
                    if (!UiLayoutRuntime.Supports(s, g) || !UiLayoutRuntime.Supports(o, g)) continue;
                    UiLayoutRuntime.Capture(s, a, g);
                    UiLayoutRuntime.Capture(o, b, g);
                    if (Same(a, b, g)) continue;
                    UiElementLook e = Element(look, pair.Key);
                    UiLayoutRuntime.Capture(s, e, g);
                }
                if (!s.gameObject.activeSelf && o.gameObject.activeSelf) Element(look, pair.Key).hide = true;
            }
            else
            {
                string parentPath = UiLayoutRuntime.PathOf(look.skin.transform, s.parent);
                if (parentPath == "" || originalNodes.ContainsKey(parentPath)) // topmost new object only
                    look.added.Add(new UiAddedObject { parentPath = parentPath, siblingIndex = s.GetSiblingIndex(), source = s.gameObject });
            }
        }

        foreach (KeyValuePair<string, Transform> pair in originalNodes)
        {
            if (skinNodes.ContainsKey(pair.Key)) continue;
            string parentPath = UiLayoutRuntime.PathOf(original.transform, pair.Value.parent);
            if (parentPath == "" || skinNodes.ContainsKey(parentPath)) Element(look, pair.Key).hide = true; // deleted in the prefab
        }

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        data.MarkChanged();
        Debug.Log($"[UI Hub] \"{screen}\" prefab: {look.elements.FindAll(x => x.fromSkin).Count} changed, {look.added.Count} added.");
    }

    private static UiElementLook Element(UiScreenLook look, string path)
    {
        UiElementLook e = look.FindElement(path);
        if (e == null) { e = look.GetOrAddElement(path); e.fromSkin = true; }
        return e;
    }

    private static Dictionary<string, Transform> Map(Transform root)
    {
        var map = new Dictionary<string, Transform>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != root) map[UiLayoutRuntime.PathOf(root, t)] = t;
        return map;
    }

    private static bool N(float x, float y) => Mathf.Abs(x - y) < 0.01f;
    private static bool N(Vector3 x, Vector3 y) => (x - y).sqrMagnitude < 0.0001f;
    private static bool N(Color x, Color y) => Mathf.Abs(x.r - y.r) < 0.004f && Mathf.Abs(x.g - y.g) < 0.004f && Mathf.Abs(x.b - y.b) < 0.004f && Mathf.Abs(x.a - y.a) < 0.004f;

    private static bool Same(UiElementLook a, UiElementLook b, UiLookGroup g)
    {
        switch (g)
        {
            case UiLookGroup.Layout:
                return N(a.anchorMin, b.anchorMin) && N(a.anchorMax, b.anchorMax) && N(a.pivot, b.pivot) && N(a.position, b.position)
                    && N(a.size, b.size) && N(a.rotation, b.rotation) && N(a.scale, b.scale);
            case UiLookGroup.Color: return N(a.colorValue, b.colorValue);
            case UiLookGroup.Sprite: return a.spriteValue == b.spriteValue;
            case UiLookGroup.TextStyle:
                return a.font == b.font && N(a.fontSize, b.fontSize) && a.fontStyle == b.fontStyle && a.alignment == b.alignment
                    && a.autoSize == b.autoSize && N(a.fontSizeMin, b.fontSizeMin) && N(a.fontSizeMax, b.fontSizeMax)
                    && N(a.characterSpacing, b.characterSpacing) && N(a.lineSpacing, b.lineSpacing) && a.wrapping == b.wrapping
                    && a.legacyFont == b.legacyFont && a.legacyFontStyle == b.legacyFontStyle && a.legacyAlignment == b.legacyAlignment;
            case UiLookGroup.Text: return a.textValue == b.textValue;
            case UiLookGroup.Spacing:
                return N(a.spacingValue, b.spacingValue) && a.paddingLeft == b.paddingLeft && a.paddingRight == b.paddingRight
                    && a.paddingTop == b.paddingTop && a.paddingBottom == b.paddingBottom;
            case UiLookGroup.Size:
                return N(a.minWidth, b.minWidth) && N(a.minHeight, b.minHeight) && N(a.preferredWidth, b.preferredWidth) && N(a.preferredHeight, b.preferredHeight);
        }
        return true;
    }

    // ---- Play-mode edits -> skin ------------------------------------------------------------------

    /// <summary>Writes one Play-mode edit into the screen's prefab. False = no prefab or the part is not in it.</summary>
    public static bool WriteToSkin(string screen, string path, UiLookGroup group, Transform live)
    {
        GameObject skin = SkinOf(screen);
        if (skin == null || live == null || string.IsNullOrEmpty(path)) return false;
        string assetPath = AssetDatabase.GetAssetPath(skin);
        GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
        try
        {
            Transform node = UiLayoutRuntime.Resolve(root.transform, path);
            if (node == null || !UiLayoutRuntime.Supports(node, group)) return false;
            if (group == UiLookGroup.Hide) node.gameObject.SetActive(live.gameObject.activeSelf);
            else
            {
                var values = new UiElementLook();
                if (!UiLayoutRuntime.Capture(live, values, group)) return false;
                UiLayoutRuntime.Apply(node, values, group);
            }
            PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
