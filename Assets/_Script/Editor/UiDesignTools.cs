using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 8 Nov 2026 (Mr.k): editor helpers for "use your own design" (see UiLayoutRuntime.Design.cs).
///   - MakeDesign: copies a code screen's prefab into Assets/UI Designs/ with every part already
///     connected (UiLink), so a designer can restyle it freely and it keeps working.
///   - SetLink / RemoveLink: connect one part of any prefab to one game part.
///   - AutoConnect: connect parts whose names match the game's parts.
/// </summary>
public static class UiDesignTools
{
    public const string Folder = "Assets/UI Designs";

    public static string DesignPath(string screen) => $"{Folder}/{UiSkinTools.FileName(screen)} (my design).prefab";

    /// <summary>What a game part shows: decides the default link settings.</summary>
    public static void Defaults(UiLink link, Transform gamePart)
    {
        link.showHide = true;
        link.text = gamePart != null && (gamePart.GetComponent<TMP_Text>() != null || gamePart.GetComponent<Text>() != null);
        link.click = gamePart != null && gamePart.GetComponent<Button>() != null;
        Image img = gamePart != null ? gamePart.GetComponent<Image>() : null;
        link.fill = img != null && img.type == Image.Type.Filled;
        link.color = false;
        link.picture = false;
    }

    // ---- Make a design from the screen -------------------------------------------------------

    public static GameObject MakeDesign(string screen)
    {
        GameObject skin = UiSkinTools.SkinOf(screen);
        if (skin == null) { EditorUtility.DisplayDialog("UI Hub", "This screen has no prefab yet. Press Play and make it appear once.", "OK"); return null; }
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "UI Designs");

        string path = AssetDatabase.GenerateUniqueAssetPath(DesignPath(screen));
        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(skin), path)) return null;

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            // The game gives the design its canvas: the root becomes a plain full-size panel.
            Object.DestroyImmediate(root.GetComponent<GraphicRaycaster>());
            Object.DestroyImmediate(root.GetComponent<CanvasScaler>());
            Object.DestroyImmediate(root.GetComponent<Canvas>());
            if (root.transform is RectTransform rt)
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero; rt.sizeDelta = Vector2.zero;
                rt.localRotation = Quaternion.identity; rt.localScale = Vector3.one;
            }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform) continue;
                UiLink link = t.GetComponent<UiLink>();
                if (link == null) link = t.gameObject.AddComponent<UiLink>();
                link.source = UiLayoutRuntime.PathOf(root.transform, t);
                Defaults(link, t);
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        var design = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        UseDesign(screen, design);
        Debug.Log($"[UI Hub] Made your own design of \"{screen}\": {path}. Restyle it freely; connected parts keep working.", design);
        return design;
    }

    public static void UseDesign(string screen, GameObject design)
    {
        UiLayoutData data = UiChangeRecorder.EnsureData();
        Undo.RecordObject(data, "UI design");
        data.GetOrAddScreen(screen).design = design;
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        data.MarkChanged();
    }

    // ---- Links ------------------------------------------------------------------------------

    /// <summary>All links in a design prefab (asset objects).</summary>
    public static List<UiLink> Links(GameObject design)
    {
        var list = new List<UiLink>();
        if (design != null) list.AddRange(design.GetComponentsInChildren<UiLink>(true));
        return list;
    }

    /// <summary>The design part connected to <paramref name="gamePath"/> (screen level, not inside a Repeat row).</summary>
    public static UiLink LinkFor(GameObject design, string gamePath)
    {
        if (design == null) return null;
        foreach (UiLink l in design.GetComponentsInChildren<UiLink>(true))
            if (l.source == gamePath && !InsideRepeat(l.transform, design.transform)) return l;
        return null;
    }

    public static bool InsideRepeat(Transform t, Transform root)
    {
        for (Transform p = t.parent; p != null; p = p.parent)
        {
            UiLink l = p.GetComponent<UiLink>();
            if (l != null && l.repeat) return true;
            if (p == root) break;
        }
        return false;
    }

    /// <summary>Connects <paramref name="part"/> (an object inside the design prefab) to a game part.</summary>
    public static void SetLink(GameObject design, GameObject part, string gamePath, Transform gamePart)
    {
        if (design == null || part == null) return;
        string designPath = AssetDatabase.GetAssetPath(design);
        string partPath = UiLayoutRuntime.PathOf(design.transform, part.transform);
        string oldPartPath = null;
        UiLink old = LinkFor(design, gamePath);
        if (old != null && old.gameObject != part) oldPartPath = UiLayoutRuntime.PathOf(design.transform, old.transform);

        using (var scope = new PrefabUtility.EditPrefabContentsScope(designPath))
        {
            Transform root = scope.prefabContentsRoot.transform;
            if (oldPartPath != null)
            {
                Transform o = string.IsNullOrEmpty(oldPartPath) ? root : UiLayoutRuntime.Resolve(root, oldPartPath);
                if (o != null && o.GetComponent<UiLink>() != null) Object.DestroyImmediate(o.GetComponent<UiLink>());
            }
            Transform t = string.IsNullOrEmpty(partPath) ? root : UiLayoutRuntime.Resolve(root, partPath);
            if (t == null) return;
            UiLink link = t.GetComponent<UiLink>();
            if (link == null) link = t.gameObject.AddComponent<UiLink>();
            link.source = gamePath;
            Defaults(link, gamePart);
        }
    }

    public static void RemoveLink(GameObject design, UiLink link)
    {
        if (design == null || link == null) return;
        string partPath = UiLayoutRuntime.PathOf(design.transform, link.transform);
        using (var scope = new PrefabUtility.EditPrefabContentsScope(AssetDatabase.GetAssetPath(design)))
        {
            Transform root = scope.prefabContentsRoot.transform;
            Transform t = string.IsNullOrEmpty(partPath) ? root : UiLayoutRuntime.Resolve(root, partPath);
            if (t != null && t.GetComponent<UiLink>() != null) Object.DestroyImmediate(t.GetComponent<UiLink>());
        }
    }

    /// <summary>
    /// Connects design parts that have no link to game parts with the same name.
    /// <paramref name="designSubPath"/> = where to look in the design ("" = whole design, skipping Repeat rows;
    /// a Repeat row's path = its parts, matched against the game's first row).
    /// </summary>
    public static int AutoConnect(GameObject design, Transform gameRoot, string designSubPath = "")
    {
        if (design == null || gameRoot == null) return 0;
        var byName = new Dictionary<string, string>();
        var dup = new HashSet<string>();
        foreach (Transform g in gameRoot.GetComponentsInChildren<Transform>(true))
        {
            if (g == gameRoot) continue;
            if (byName.ContainsKey(g.name)) dup.Add(g.name);
            else byName[g.name] = UiLayoutRuntime.PathOf(gameRoot, g);
        }

        int made = 0;
        using (var scope = new PrefabUtility.EditPrefabContentsScope(AssetDatabase.GetAssetPath(design)))
        {
            Transform root = scope.prefabContentsRoot.transform;
            Transform start = string.IsNullOrEmpty(designSubPath) ? root : UiLayoutRuntime.Resolve(root, designSubPath);
            if (start == null) return 0;
            foreach (Transform t in start.GetComponentsInChildren<Transform>(true))
            {
                if (t == start || t.GetComponent<UiLink>() != null) continue;
                if (start == root && InsideRepeat(t, root)) continue; // rows are connected on their own
                if (!byName.TryGetValue(t.name, out string path) || dup.Contains(t.name)) continue;
                UiLink link = t.gameObject.AddComponent<UiLink>();
                link.source = path;
                Defaults(link, UiLayoutRuntime.Resolve(gameRoot, path));
                made++;
            }
        }
        return made;
    }
}
