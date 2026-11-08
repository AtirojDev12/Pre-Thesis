using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 8 Nov 2026 (Mr.k): puts the designer's saved UI changes (<see cref="UiLayoutData"/>)
/// back on every screen, in every scene, also in builds.
///
/// HOW
///   - Twice a second it looks for root Canvases (= screens). A screen is matched by
///     its object name, so screens built by code need no changes at all.
///   - Each saved element is found by its path and its saved values are set again
///     whenever they differ (so a screen built later, or rebuilt, still gets them).
///   - If game code changes a saved value back in 3 frames in a row, the value is
///     "controlled by code": the runtime stops fighting it, and the UI Hub warns.
///
/// LOCAL ONLY: UI is never networked; each PC applies the same data.
/// Cheap: only the saved elements are checked each frame (no per-frame allocations).
/// </summary>
[DefaultExecutionOrder(10000)] // after the screens' own LateUpdates
public sealed partial class UiLayoutRuntime : MonoBehaviour
{
    private const float ScanEvery = 0.5f;
    private const int CodeControlledFrames = 3;

    /// <summary>One live screen (root Canvas) in the scene.</summary>
    public sealed class LiveScreen
    {
        public Canvas canvas;
        public Transform root;
        public string name;
        public UiScreenLook look;
        public readonly List<Binding> bindings = new List<Binding>();
        public readonly List<GameObject> spawned = new List<GameObject>(); // copies of the designer's added objects
        public bool originalEnabledKnown;
        public int originalSortOrder;
        public bool sortOrderKnown;
        public DesignState design; // the designer's own prefab shown instead (UiLayoutRuntime.Design.cs)
    }

    /// <summary>One saved element bound to a live object.</summary>
    public sealed class Binding
    {
        public UiElementLook look;
        public Transform target;
        public readonly int[] streak = new int[GroupCount];
        public readonly bool[] applied = new bool[GroupCount];
        public readonly bool[] codeControlled = new bool[GroupCount];
#if UNITY_EDITOR
        public string[] keys; // "screen|path|group", for edits waiting to be saved
#endif
    }

    private static readonly int GroupCount = System.Enum.GetValues(typeof(UiLookGroup)).Length;

    private static UiLayoutRuntime instance;
    private static readonly List<LiveScreen> live = new List<LiveScreen>();
    private static readonly HashSet<Canvas> known = new HashSet<Canvas>();
    private readonly List<TMP_Text> textBuffer = new List<TMP_Text>(256);

    private UiLayoutData data;
    private int boundVersion = -1;
    private float nextScan;

    /// <summary>Screens on screen right now (the UI Hub lists these).</summary>
    public static IReadOnlyList<LiveScreen> Live => live;

#if UNITY_EDITOR
    /// <summary>Editor: keys of edits waiting to be saved. The runtime does not set those back.</summary>
    public static readonly HashSet<string> EditorSuspended = new HashSet<string>();

    public static string Key(string screen, string path, UiLookGroup group) => screen + "|" + path + "|" + (int)group;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        live.Clear();
        known.Clear();
#if UNITY_EDITOR
        EditorSuspended.Clear();
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
#if !UNITY_EDITOR
        UiLayoutData d = UiLayoutData.Load();
        if (d == null || (d.screens.Count == 0 && d.fontForAllUi == null)) return; // nothing to apply in this build
#endif
        if (instance != null) return;
        var go = new GameObject("[UI Layout]") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        instance = go.AddComponent<UiLayoutRuntime>();
    }

    private void Awake()
    {
        data = UiLayoutData.Load();
        UiLayoutData.Changed += OnDataChanged;
    }

    private void OnDestroy()
    {
        UiLayoutData.Changed -= OnDataChanged;
        if (instance == this) instance = null;
    }

    private void OnDataChanged(UiLayoutData changed)
    {
        data = changed;
        boundVersion = -1;
        nextScan = 0f;
    }

    private void LateUpdate()
    {
        if (data == null) data = UiLayoutData.Load();
        int version = data != null ? data.version : 0;
        if (Time.unscaledTime >= nextScan || version != boundVersion)
        {
            nextScan = Time.unscaledTime + ScanEvery;
            Scan(version != boundVersion);
            boundVersion = version;
        }

        for (int i = 0; i < live.Count; i++)
        {
            ApplyScreen(live[i]);
            UpdateDesign(live[i]);
        }
    }

    // ---- Finding screens ---------------------------------------------------------------

    private void Scan(bool rebind)
    {
        for (int i = live.Count - 1; i >= 0; i--)
            if (live[i].canvas == null) { DestroyDesign(live[i]); known.Remove(live[i].canvas); live.RemoveAt(i); }
        known.RemoveWhere(c => c == null);

        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
        {
            if (c == null || !c.isRootCanvas || known.Contains(c) || c.name.StartsWith(DesignPrefix)) continue;
#if UNITY_EDITOR
            if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(c.gameObject.scene)) continue; // a prefab being edited
#endif
            known.Add(c);
            live.Add(new LiveScreen { canvas = c, root = c.transform, name = ScreenName(c.transform) });
            rebind = true;
        }

        for (int i = 0; i < live.Count; i++)
        {
            LiveScreen s = live[i];
            if (rebind) Bind(s);
            SpawnAdded(s); // before resolving: saved changes may point into added objects
            if (!rebind)
                for (int b = 0; b < s.bindings.Count; b++)
                    if (s.bindings[b].target == null) s.bindings[b].target = Resolve(s.root, s.bindings[b].look.path);
            ApplyFonts(s);
            ResolveDesignLinks(s);
        }
    }

    private void Bind(LiveScreen s)
    {
        UiScreenLook look = data != null ? data.FindScreen(s.name) : null;
        if (s.look != null && look == null) RestoreScreen(s);
        s.look = look;
        DestroyDesign(s);
        if (look != null && look.design != null) CreateDesign(s, look.design);
        s.bindings.Clear();
        for (int i = 0; i < s.spawned.Count; i++) if (s.spawned[i] != null) Destroy(s.spawned[i]);
        s.spawned.Clear();
        if (look == null) return;
        for (int i = 0; i < look.added.Count; i++) s.spawned.Add(null);
        for (int i = 0; i < look.elements.Count; i++)
        {
            UiElementLook e = look.elements[i];
            if (e == null || string.IsNullOrEmpty(e.path)) continue;
            var b = new Binding { look = e, target = Resolve(s.root, e.path) };
#if UNITY_EDITOR
            b.keys = new string[GroupCount];
            for (int g = 0; g < GroupCount; g++) b.keys[g] = Key(s.name, e.path, (UiLookGroup)g);
#endif
            s.bindings.Add(b);
        }
    }

    /// <summary>Makes a copy of each object the designer added in the screen's prefab (once per live screen).</summary>
    private static void SpawnAdded(LiveScreen s)
    {
        if (s.look == null || s.root == null) return;
        for (int i = 0; i < s.look.added.Count && i < s.spawned.Count; i++)
        {
            if (s.spawned[i] != null) continue;
            UiAddedObject a = s.look.added[i];
            if (a == null || a.source == null) continue;
            Transform parent = string.IsNullOrEmpty(a.parentPath) ? s.root : Resolve(s.root, a.parentPath);
            if (parent == null) continue; // the screen has not built that part yet
            GameObject copy = Instantiate(a.source, parent, false);
            copy.name = a.source.name;
            copy.transform.SetSiblingIndex(Mathf.Clamp(a.siblingIndex, 0, parent.childCount - 1));
            s.spawned[i] = copy;
        }
    }

    private static void RestoreScreen(LiveScreen s)
    {
        if (s.canvas == null) return;
        if (s.originalEnabledKnown) s.canvas.enabled = true;
        if (s.sortOrderKnown) s.canvas.sortingOrder = s.originalSortOrder;
        s.originalEnabledKnown = false;
        s.sortOrderKnown = false;
    }

    // ---- Applying ---------------------------------------------------------------------------

    private static void ApplyScreen(LiveScreen s)
    {
        if (s.canvas == null || s.look == null) return;
        UiScreenLook look = s.look;

        if (look.hide)
        {
            if (s.canvas.enabled) { s.canvas.enabled = false; s.originalEnabledKnown = true; }
        }
        else if (s.originalEnabledKnown)
        {
            s.canvas.enabled = true;
            s.originalEnabledKnown = false;
        }

        if (look.overrideSortOrder)
        {
            if (!s.sortOrderKnown) { s.originalSortOrder = s.canvas.sortingOrder; s.sortOrderKnown = true; }
            if (s.canvas.sortingOrder != look.sortOrder) s.canvas.sortingOrder = look.sortOrder;
        }
        else if (s.sortOrderKnown)
        {
            s.canvas.sortingOrder = s.originalSortOrder;
            s.sortOrderKnown = false;
        }

        for (int i = 0; i < s.bindings.Count; i++)
        {
            Binding b = s.bindings[i];
            if (b.target == null) continue;
            for (int g = 0; g < GroupCount; g++)
            {
                var group = (UiLookGroup)g;
                if (!b.look.Uses(group) || b.codeControlled[g]) continue;
#if UNITY_EDITOR
                if (EditorSuspended.Count > 0 && EditorSuspended.Contains(b.keys[g])) continue;
#endif
                if (Matches(b.target, b.look, group)) { b.streak[g] = 0; continue; }
                if (b.applied[g] && ++b.streak[g] >= CodeControlledFrames)
                {
                    b.codeControlled[g] = true;
                    Debug.LogWarning($"[UI Layout] \"{s.name}/{b.look.path}\" {group}: the game code (or a layout group) keeps changing it, so the saved change cannot stay.", b.target);
                    continue;
                }
                Apply(b.target, b.look, group);
                b.applied[g] = true;
            }
        }
    }

    private void ApplyFonts(LiveScreen s)
    {
        TMP_FontAsset font = s.look != null && s.look.font != null ? s.look.font : (data != null ? data.fontForAllUi : null);
        if (font == null || s.root == null) return;
        s.root.GetComponentsInChildren(true, textBuffer);
        for (int i = 0; i < textBuffer.Count; i++)
        {
            TMP_Text t = textBuffer[i];
            if (t == null || t.font == font || HasOwnFont(s, t.transform)) continue;
            t.font = font;
        }
        textBuffer.Clear();
    }

    private static bool HasOwnFont(LiveScreen s, Transform t)
    {
        for (int i = 0; i < s.bindings.Count; i++)
        {
            Binding b = s.bindings[i];
            if (b.target == t && b.look.textStyle && b.look.font != null) return true;
        }
        return false;
    }

    /// <summary>Is <paramref name="group"/> on <paramref name="screen"/>/<paramref name="path"/> being changed back by game code?</summary>
    public static bool IsCodeControlled(string screen, string path, UiLookGroup group)
    {
        for (int i = 0; i < live.Count; i++)
        {
            if (live[i].name != screen) continue;
            foreach (Binding b in live[i].bindings)
                if (b.look.path == path && b.codeControlled[(int)group]) return true;
        }
        return false;
    }

    // ---- Paths ------------------------------------------------------------------------------

    /// <summary>The screen name of a root Canvas object ("(Clone)" removed).</summary>
    public static string ScreenName(Transform root)
    {
        string n = root.name;
        return n.EndsWith("(Clone)") ? n.Substring(0, n.Length - 7).TrimEnd() : n;
    }

    /// <summary>Path of <paramref name="t"/> under <paramref name="root"/>. "Name#1" = the 2nd child with that name.</summary>
    public static string PathOf(Transform root, Transform t)
    {
        if (t == null || root == null || t == root) return "";
        var parts = new List<string>();
        for (Transform x = t; x != null && x != root; x = x.parent)
        {
            int k = 0;
            Transform p = x.parent;
            if (p != null)
                for (int i = 0; i < p.childCount; i++)
                {
                    Transform c = p.GetChild(i);
                    if (c == x) break;
                    if (c.name == x.name) k++;
                }
            parts.Add(k > 0 ? x.name + "#" + k : x.name);
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>Finds the object at <paramref name="path"/> under <paramref name="root"/> (null if not there now).</summary>
    public static Transform Resolve(Transform root, string path)
    {
        if (root == null || string.IsNullOrEmpty(path)) return null;
        Transform cur = root;
        int start = 0;
        while (cur != null && start <= path.Length)
        {
            int slash = path.IndexOf('/', start);
            string part = slash < 0 ? path.Substring(start) : path.Substring(start, slash - start);
            string name = part;
            int index = 0;
            int hash = part.LastIndexOf('#');
            if (hash > 0 && int.TryParse(part.Substring(hash + 1), out int n)) { name = part.Substring(0, hash); index = n; }

            Transform next = null;
            int seen = 0;
            for (int i = 0; i < cur.childCount; i++)
            {
                Transform c = cur.GetChild(i);
                if (c.name != name) continue;
                if (seen == index) { next = c; break; }
                seen++;
            }
            cur = next;
            if (slash < 0) break;
            start = slash + 1;
        }
        return cur;
    }

    // ---- Read / compare / write one group --------------------------------------------------------

    /// <summary>Does <paramref name="t"/> have the component this group needs?</summary>
    public static bool Supports(Transform t, UiLookGroup group)
    {
        if (t == null) return false;
        switch (group)
        {
            case UiLookGroup.Hide: return true;
            case UiLookGroup.Layout: return t is RectTransform;
            case UiLookGroup.Color: return t.GetComponent<Graphic>() != null;
            case UiLookGroup.Sprite: return t.GetComponent<Image>() != null;
            case UiLookGroup.TextStyle:
            case UiLookGroup.Text: return t.GetComponent<TMP_Text>() != null || t.GetComponent<Text>() != null;
            case UiLookGroup.Spacing: return t.GetComponent<HorizontalOrVerticalLayoutGroup>() != null;
            case UiLookGroup.Size: return t.GetComponent<LayoutElement>() != null;
        }
        return false;
    }

    /// <summary>Copies the live values of one group from <paramref name="t"/> into <paramref name="e"/> and turns the group on.</summary>
    public static bool Capture(Transform t, UiElementLook e, UiLookGroup group)
    {
        if (!Supports(t, group)) return false;
        switch (group)
        {
            case UiLookGroup.Hide:
                e.hide = !t.gameObject.activeSelf;
                return true;
            case UiLookGroup.Layout:
            {
                var rt = (RectTransform)t;
                e.anchorMin = rt.anchorMin; e.anchorMax = rt.anchorMax; e.pivot = rt.pivot;
                e.position = rt.anchoredPosition; e.size = rt.sizeDelta;
                e.rotation = rt.localEulerAngles; e.scale = rt.localScale;
                break;
            }
            case UiLookGroup.Color:
                e.colorValue = t.GetComponent<Graphic>().color;
                break;
            case UiLookGroup.Sprite:
                e.spriteValue = t.GetComponent<Image>().sprite;
                break;
            case UiLookGroup.TextStyle:
            {
                TMP_Text tmp = t.GetComponent<TMP_Text>();
                if (tmp != null)
                {
                    e.font = tmp.font; e.fontSize = tmp.enableAutoSizing ? tmp.fontSizeMax : tmp.fontSize;
                    e.fontStyle = tmp.fontStyle; e.alignment = tmp.alignment;
                    e.autoSize = tmp.enableAutoSizing; e.fontSizeMin = tmp.fontSizeMin; e.fontSizeMax = tmp.fontSizeMax;
                    e.characterSpacing = tmp.characterSpacing; e.lineSpacing = tmp.lineSpacing; e.wrapping = tmp.textWrappingMode;
                }
                else
                {
                    Text legacy = t.GetComponent<Text>();
                    e.legacyFont = legacy.font; e.fontSize = legacy.fontSize;
                    e.legacyFontStyle = legacy.fontStyle; e.legacyAlignment = legacy.alignment;
                }
                break;
            }
            case UiLookGroup.Text:
            {
                TMP_Text tmp = t.GetComponent<TMP_Text>();
                e.textValue = tmp != null ? tmp.text : t.GetComponent<Text>().text;
                break;
            }
            case UiLookGroup.Spacing:
            {
                var lg = t.GetComponent<HorizontalOrVerticalLayoutGroup>();
                e.spacingValue = lg.spacing;
                e.paddingLeft = lg.padding.left; e.paddingRight = lg.padding.right;
                e.paddingTop = lg.padding.top; e.paddingBottom = lg.padding.bottom;
                break;
            }
            case UiLookGroup.Size:
            {
                var le = t.GetComponent<LayoutElement>();
                e.minWidth = le.minWidth; e.minHeight = le.minHeight;
                e.preferredWidth = le.preferredWidth; e.preferredHeight = le.preferredHeight;
                break;
            }
        }
        e.SetUses(group, true);
        return true;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;
    private static bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 0.0001f;
    private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.0001f;
    private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f && Mathf.Abs(a.a - b.a) < 0.004f;
    private static bool NearAngles(Vector3 a, Vector3 b) =>
        Mathf.Abs(Mathf.DeltaAngle(a.x, b.x)) < 0.05f && Mathf.Abs(Mathf.DeltaAngle(a.y, b.y)) < 0.05f && Mathf.Abs(Mathf.DeltaAngle(a.z, b.z)) < 0.05f;

    private static bool Matches(Transform t, UiElementLook e, UiLookGroup group)
    {
        switch (group)
        {
            case UiLookGroup.Hide:
                return !e.hide || !t.gameObject.activeSelf; // only ever hides; never forces an element on
            case UiLookGroup.Layout:
            {
                if (!(t is RectTransform rt)) return true;
                return Near(rt.anchorMin, e.anchorMin) && Near(rt.anchorMax, e.anchorMax) && Near(rt.pivot, e.pivot)
                    && Near(rt.anchoredPosition, e.position) && Near(rt.sizeDelta, e.size)
                    && NearAngles(rt.localEulerAngles, e.rotation) && Near(rt.localScale, e.scale);
            }
            case UiLookGroup.Color:
            {
                Graphic g = t.GetComponent<Graphic>();
                return g == null || Near(g.color, e.colorValue);
            }
            case UiLookGroup.Sprite:
            {
                Image img = t.GetComponent<Image>();
                return img == null || img.sprite == e.spriteValue;
            }
            case UiLookGroup.TextStyle:
            {
                TMP_Text tmp = t.GetComponent<TMP_Text>();
                if (tmp != null)
                {
                    return (e.font == null || tmp.font == e.font)
                        && (e.autoSize || Near(tmp.fontSize, e.fontSize))
                        && tmp.fontStyle == e.fontStyle && tmp.alignment == e.alignment
                        && tmp.enableAutoSizing == e.autoSize
                        && (!e.autoSize || (Near(tmp.fontSizeMin, e.fontSizeMin) && Near(tmp.fontSizeMax, e.fontSizeMax)))
                        && Near(tmp.characterSpacing, e.characterSpacing) && Near(tmp.lineSpacing, e.lineSpacing)
                        && tmp.textWrappingMode == e.wrapping;
                }
                Text legacy = t.GetComponent<Text>();
                return legacy == null || ((e.legacyFont == null || legacy.font == e.legacyFont)
                    && legacy.fontSize == Mathf.RoundToInt(e.fontSize) && legacy.fontStyle == e.legacyFontStyle && legacy.alignment == e.legacyAlignment);
            }
            case UiLookGroup.Text:
            {
                TMP_Text tmp = t.GetComponent<TMP_Text>();
                if (tmp != null) return tmp.text == e.textValue;
                Text legacy = t.GetComponent<Text>();
                return legacy == null || legacy.text == e.textValue;
            }
            case UiLookGroup.Spacing:
            {
                var lg = t.GetComponent<HorizontalOrVerticalLayoutGroup>();
                return lg == null || (Near(lg.spacing, e.spacingValue) && lg.padding.left == e.paddingLeft && lg.padding.right == e.paddingRight
                    && lg.padding.top == e.paddingTop && lg.padding.bottom == e.paddingBottom);
            }
            case UiLookGroup.Size:
            {
                var le = t.GetComponent<LayoutElement>();
                return le == null || (Near(le.minWidth, e.minWidth) && Near(le.minHeight, e.minHeight)
                    && Near(le.preferredWidth, e.preferredWidth) && Near(le.preferredHeight, e.preferredHeight));
            }
        }
        return true;
    }

    /// <summary>Sets one group of <paramref name="e"/> on <paramref name="t"/> (also used by the editor on prefabs).</summary>
    public static void Apply(Transform t, UiElementLook e, UiLookGroup group)
    {
        switch (group)
        {
            case UiLookGroup.Hide:
                if (e.hide && t.gameObject.activeSelf) t.gameObject.SetActive(false);
                break;
            case UiLookGroup.Layout:
                if (t is RectTransform rt)
                {
                    rt.anchorMin = e.anchorMin; rt.anchorMax = e.anchorMax; rt.pivot = e.pivot;
                    rt.anchoredPosition = e.position; rt.sizeDelta = e.size;
                    rt.localEulerAngles = e.rotation; rt.localScale = e.scale;
                }
                break;
            case UiLookGroup.Color:
            {
                Graphic g = t.GetComponent<Graphic>();
                if (g != null) g.color = e.colorValue;
                break;
            }
            case UiLookGroup.Sprite:
            {
                Image img = t.GetComponent<Image>();
                if (img != null) img.sprite = e.spriteValue;
                break;
            }
            case UiLookGroup.TextStyle:
            {
                TMP_Text tmp = t.GetComponent<TMP_Text>();
                if (tmp != null)
                {
                    if (e.font != null && tmp.font != e.font) tmp.font = e.font;
                    tmp.enableAutoSizing = e.autoSize;
                    if (e.autoSize) { tmp.fontSizeMin = e.fontSizeMin; tmp.fontSizeMax = e.fontSizeMax; }
                    else tmp.fontSize = e.fontSize;
                    tmp.fontStyle = e.fontStyle; tmp.alignment = e.alignment;
                    tmp.characterSpacing = e.characterSpacing; tmp.lineSpacing = e.lineSpacing;
                    tmp.textWrappingMode = e.wrapping;
                    break;
                }
                Text legacy = t.GetComponent<Text>();
                if (legacy != null)
                {
                    if (e.legacyFont != null) legacy.font = e.legacyFont;
                    legacy.fontSize = Mathf.RoundToInt(e.fontSize);
                    legacy.fontStyle = e.legacyFontStyle; legacy.alignment = e.legacyAlignment;
                }
                break;
            }
            case UiLookGroup.Text:
            {
                TMP_Text tmp = t.GetComponent<TMP_Text>();
                if (tmp != null) { tmp.text = e.textValue; break; }
                Text legacy = t.GetComponent<Text>();
                if (legacy != null) legacy.text = e.textValue;
                break;
            }
            case UiLookGroup.Spacing:
            {
                var lg = t.GetComponent<HorizontalOrVerticalLayoutGroup>();
                if (lg == null) break;
                lg.spacing = e.spacingValue;
                lg.padding = new RectOffset(e.paddingLeft, e.paddingRight, e.paddingTop, e.paddingBottom);
                break;
            }
            case UiLookGroup.Size:
            {
                var le = t.GetComponent<LayoutElement>();
                if (le == null) break;
                le.minWidth = e.minWidth; le.minHeight = e.minHeight;
                le.preferredWidth = e.preferredWidth; le.preferredHeight = e.preferredHeight;
                break;
            }
        }
    }
}
