using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 8 Nov 2026 (Mr.k): "use your own design" for screens built by code.
///
/// When a screen has a design prefab (<see cref="UiScreenLook.design"/>):
///   - The game's own screen keeps running but is hidden (screen canvases move to an unused
///     display; world canvases get scale 0). Its code does not know and needs no change.
///   - The design is shown in its place, inside a canvas with the same settings
///     (overlay: same scaler / draw order; world: same place, size and scale).
///   - The design is shown only while the game's screen is shown.
///   - Parts with a <see cref="UiLink"/> copy from the game's parts every frame
///     (show / hide, words, colour, picture, bar fill) and forward button clicks.
///   - A Repeat link makes one copy of a row template per item of a game list.
/// LOCAL ONLY (UI). Paths are found twice a second; copying is per frame without allocations.
/// </summary>
public sealed partial class UiLayoutRuntime
{
    /// <summary>Name start of the canvas that holds a design (never treated as a game screen).</summary>
    public const string DesignPrefix = "[Design] ";
    private const int HiddenDisplay = 7;

    public sealed class DesignState
    {
        public GameObject wrapper;
        public Canvas canvas;
        public CanvasGroup group;
        public bool visible;
        public bool hidGame;
        public int savedDisplay;
        public Vector3 savedScale;
        public readonly List<LinkState> links = new List<LinkState>();
    }

    public sealed class LinkState
    {
        public UiLink link;
        public Transform target;
        public Transform row;      // inside a Repeat copy: the game row the paths start from
        public Transform source;
        public bool clickHooked;
        public readonly List<GameObject> clones = new List<GameObject>();          // Repeat only
        public readonly List<List<LinkState>> cloneLinks = new List<List<LinkState>>();
    }

    // ---- Make / remove ----------------------------------------------------------------------

    private static void CreateDesign(LiveScreen s, GameObject prefab)
    {
        if (s.canvas == null || prefab == null) return;
        Canvas game = s.canvas;
        bool world = game.renderMode == RenderMode.WorldSpace;

        var wrapper = new GameObject(DesignPrefix + s.name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        wrapper.SetActive(false); // shown when the game's screen is shown
        var canvas = wrapper.GetComponent<Canvas>();
        canvas.renderMode = world ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = game.sortingOrder;

        CanvasScaler from = game.GetComponent<CanvasScaler>(), to = wrapper.GetComponent<CanvasScaler>();
        if (from != null)
        {
            to.uiScaleMode = from.uiScaleMode; to.referenceResolution = from.referenceResolution;
            to.screenMatchMode = from.screenMatchMode; to.matchWidthOrHeight = from.matchWidthOrHeight;
            to.scaleFactor = from.scaleFactor; to.referencePixelsPerUnit = from.referencePixelsPerUnit;
            to.dynamicPixelsPerUnit = from.dynamicPixelsPerUnit;
        }

        var state = new DesignState { wrapper = wrapper, canvas = canvas };
        if (world)
        {
            var gameRect = (RectTransform)s.root;
            var rect = (RectTransform)wrapper.transform;
            wrapper.transform.SetParent(s.root.parent, false);
            rect.pivot = gameRect.pivot;
            rect.sizeDelta = gameRect.sizeDelta;
            wrapper.transform.localPosition = s.root.localPosition;
            wrapper.transform.localRotation = s.root.localRotation;
            wrapper.transform.localScale = s.root.localScale;
        }
        if (wrapper.transform.parent == null)
        {
            if (s.root.gameObject.scene.name == "DontDestroyOnLoad") DontDestroyOnLoad(wrapper);
            else if (s.root.gameObject.scene.IsValid()) SceneManager.MoveGameObjectToScene(wrapper, s.root.gameObject.scene);
        }
        if (s.root.GetComponent<CanvasGroup>() != null) state.group = wrapper.AddComponent<CanvasGroup>();

        GameObject copy = Instantiate(prefab, wrapper.transform, false);
        copy.name = prefab.name;
        foreach (UiLink l in copy.GetComponentsInChildren<UiLink>(true))
            if (!InsideRepeat(l.transform, wrapper.transform)) state.links.Add(new LinkState { link = l, target = l.transform });

        // Hide the game's own screen (it keeps running and feeding the links).
        if (world) { state.savedScale = s.root.localScale; s.root.localScale = Vector3.zero; }
        else { state.savedDisplay = game.targetDisplay; game.targetDisplay = HiddenDisplay; }
        state.hidGame = true;

        s.design = state;
        ResolveDesignLinks(s);
    }

    private static void DestroyDesign(LiveScreen s)
    {
        DesignState d = s.design;
        if (d == null) return;
        s.design = null;
        if (d.wrapper != null) Destroy(d.wrapper);
        if (!d.hidGame || s.canvas == null) return;
        if (s.canvas.renderMode == RenderMode.WorldSpace) s.root.localScale = d.savedScale;
        else s.canvas.targetDisplay = d.savedDisplay;
    }

    /// <summary>Is <paramref name="t"/> inside a Repeat row template (below <paramref name="stopAt"/>)?</summary>
    private static bool InsideRepeat(Transform t, Transform stopAt)
    {
        for (Transform p = t.parent; p != null && p != stopAt; p = p.parent)
        {
            UiLink l = p.GetComponent<UiLink>();
            if (l != null && l.repeat) return true;
        }
        return false;
    }

    // ---- Paths (twice a second) -----------------------------------------------------------------

    private static void ResolveDesignLinks(LiveScreen s)
    {
        if (s.design == null) return;
        foreach (LinkState st in s.design.links)
        {
            ResolveLink(st, s.root);
            foreach (List<LinkState> row in st.cloneLinks)
                foreach (LinkState child in row) ResolveLink(child, child.row);
        }
    }

    private static void ResolveLink(LinkState st, Transform from)
    {
        if (st.link == null || from == null) return;
        if (st.source == null) st.source = string.IsNullOrEmpty(st.link.source) ? from : Resolve(from, st.link.source);
        if (st.source == null || st.clickHooked || !st.link.click) return;
        Button button = st.target != null ? st.target.GetComponent<Button>() : null;
        if (button == null) return;
        LinkState captured = st;
        button.onClick.AddListener(() =>
        {
            Button gameButton = captured.source != null ? captured.source.GetComponent<Button>() : null;
            if (gameButton != null && gameButton.interactable && gameButton.isActiveAndEnabled) gameButton.onClick.Invoke();
        });
        st.clickHooked = true;
    }

    // ---- Every frame ------------------------------------------------------------------------------

    private static void UpdateDesign(LiveScreen s)
    {
        DesignState d = s.design;
        if (d == null || d.wrapper == null || s.canvas == null) return;

        bool visible = s.root.gameObject.activeInHierarchy && s.canvas.enabled;
        if (visible != d.visible) { d.wrapper.SetActive(visible); d.visible = visible; }
        if (!visible) return;

        if (s.canvas.renderMode == RenderMode.WorldSpace)
        {
            var wrapperRect = (RectTransform)d.wrapper.transform;
            wrapperRect.SetPositionAndRotation(s.root.position, s.root.rotation);
            Vector2 size = ((RectTransform)s.root).sizeDelta;
            if (wrapperRect.sizeDelta != size) wrapperRect.sizeDelta = size;
        }
        if (d.canvas.sortingOrder != s.canvas.sortingOrder) d.canvas.sortingOrder = s.canvas.sortingOrder;
        if (d.group != null)
        {
            CanvasGroup gameGroup = s.root.GetComponent<CanvasGroup>();
            if (gameGroup != null) d.group.alpha = gameGroup.alpha;
        }

        for (int i = 0; i < d.links.Count; i++) UpdateLink(d.links[i]);
    }

    private static void UpdateLink(LinkState st)
    {
        if (st.link == null || st.target == null || st.source == null) return;
        UiLink l = st.link;
        if (l.repeat) { UpdateRepeat(st); return; }

        GameObject go = st.target.gameObject;
        if (l.showHide)
        {
            bool on = st.source.gameObject.activeInHierarchy;
            if (go.activeSelf != on) go.SetActive(on);
            if (!on) return;
        }

        if (l.text)
        {
            string words = TextOf(st.source);
            if (words != null)
            {
                TMP_Text tmp = st.target.GetComponent<TMP_Text>();
                if (tmp != null) { if (tmp.text != words) tmp.text = words; }
                else
                {
                    Text legacy = st.target.GetComponent<Text>();
                    if (legacy != null && legacy.text != words) legacy.text = words;
                }
            }
        }

        if (l.color)
        {
            Graphic from = st.source.GetComponent<Graphic>(), to = st.target.GetComponent<Graphic>();
            if (from != null && to != null && to.color != from.color) to.color = from.color;
        }

        if (l.picture)
        {
            Image fromImage = st.source.GetComponent<Image>(), toImage = st.target.GetComponent<Image>();
            if (fromImage != null && toImage != null) { if (toImage.sprite != fromImage.sprite) toImage.sprite = fromImage.sprite; }
            else
            {
                RawImage fromRaw = st.source.GetComponent<RawImage>(), toRaw = st.target.GetComponent<RawImage>();
                if (fromRaw != null && toRaw != null && toRaw.texture != fromRaw.texture) toRaw.texture = fromRaw.texture;
            }
        }

        if (l.fill)
        {
            float r = FillOf(st.source, l.fillAxis);
            Image img = st.target.GetComponent<Image>();
            if (img != null && img.type == Image.Type.Filled) img.fillAmount = r;
            else
            {
                Vector3 scale = st.target.localScale;
                if (l.fillAxis == UiLink.Axis.Horizontal) scale.x = r; else scale.y = r;
                st.target.localScale = scale;
            }
        }
    }

    private static string TextOf(Transform t)
    {
        TMP_Text tmp = t.GetComponent<TMP_Text>();
        if (tmp != null) return tmp.text;
        Text legacy = t.GetComponent<Text>();
        return legacy != null ? legacy.text : null;
    }

    /// <summary>How full the game's bar is (0..1): Filled Image, Slider, or its size inside its parent.</summary>
    private static float FillOf(Transform t, UiLink.Axis axis)
    {
        Image img = t.GetComponent<Image>();
        if (img != null && img.type == Image.Type.Filled) return img.fillAmount;
        Slider slider = t.GetComponent<Slider>();
        if (slider != null) return slider.normalizedValue;
        var rt = t as RectTransform;
        var parent = t.parent as RectTransform;
        if (rt == null || parent == null) return 1f;
        bool h = axis == UiLink.Axis.Horizontal;
        float whole = h ? parent.rect.width : parent.rect.height;
        if (whole <= 0.001f) return 0f;
        float part = (h ? rt.rect.width * rt.localScale.x : rt.rect.height * rt.localScale.y);
        return Mathf.Clamp01(part / whole);
    }

    private static void UpdateRepeat(LinkState st)
    {
        GameObject template = st.target.gameObject;
        if (template.activeSelf) template.SetActive(false);
        Transform list = st.source;
        int n = list.childCount;

        while (st.clones.Count > n)
        {
            int last = st.clones.Count - 1;
            if (st.clones[last] != null) Destroy(st.clones[last]);
            st.clones.RemoveAt(last);
            st.cloneLinks.RemoveAt(last);
        }
        for (int i = 0; i < n; i++)
        {
            Transform row = list.GetChild(i);
            if (i >= st.clones.Count)
            {
                GameObject copy = Instantiate(template, template.transform.parent, false);
                copy.name = template.name + " " + (i + 1);
                copy.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1 + i);
                var links = new List<LinkState>();
                foreach (UiLink l in copy.GetComponentsInChildren<UiLink>(true))
                    if (l.transform != copy.transform && !InsideRepeat(l.transform, copy.transform))
                        links.Add(new LinkState { link = l, target = l.transform, row = row });
                st.clones.Add(copy);
                st.cloneLinks.Add(links);
                foreach (LinkState child in links) ResolveLink(child, row);
            }

            List<LinkState> rowLinks = st.cloneLinks[i];
            if (rowLinks.Count > 0 && rowLinks[0].row != row) // the game reordered or rebuilt its list
                foreach (LinkState child in rowLinks) { child.row = row; child.source = null; ResolveLink(child, row); }

            GameObject clone = st.clones[i];
            if (clone == null) continue;
            bool on = row.gameObject.activeSelf;
            if (clone.activeSelf != on) clone.SetActive(on);
            if (!on) continue;
            for (int k = 0; k < rowLinks.Count; k++) UpdateLink(rowLinks[k]);
        }
    }
}
