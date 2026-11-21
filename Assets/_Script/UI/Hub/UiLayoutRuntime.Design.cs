using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 8 Nov 2026 (Mr.k): "use your own design" for screens built by code.
///
/// When a screen has a design (the designer's own prefab, <see cref="UiScreenLook.design"/>, or since
/// 16 Nov its editable version inside the GAME HUD prefab, <see cref="UiHubScreen"/>):
///   - The game's own screen keeps running but is hidden (screen canvases move to an unused
///     display; world canvases get scale 0). Its code does not know and needs no change.
///   - The design is shown in its place, inside a canvas with the same settings
///     (overlay: same scaler / draw order; world: same place, size and scale).
///   - The design is shown only while the game's screen is shown.
///   - Parts with a <see cref="UiLink"/> copy from the game's parts every frame
///     (show / hide, words, colour, picture, bar fill) and forward button clicks.
///   - A Repeat link makes one copy of a row template per item of a game list.
///
/// 16 Nov 2026 (Mr.k):
///   - "Only when the game changes it": the designer's words / colour / picture / place stay
///     until the game changes its own part away from what it showed when the screen was captured.
///   - Control links: Slider, Toggle, Dropdown and Input Field work both ways.
///   - Follow Layout: a part moves / grows with the game's part (bars made by size, list rows).
///   - A part whose game part does not exist (yet) is hidden.
///   - Components are looked up once per link, not every frame.
/// LOCAL ONLY (UI). Paths are found a few times a second; copying is per frame without allocations.
/// </summary>
public sealed partial class UiLayoutRuntime
{
    /// <summary>Name start of the canvas that holds a design (never treated as a game screen).</summary>
    public const string DesignPrefix = "[Design] ";
    private const int HiddenDisplay = 7;
    private const float ResolveEvery = 0.1f;
    private const float RowsEvery = 0.25f;

    public sealed class DesignState
    {
        public GameObject wrapper;
        public Canvas canvas;
        public CanvasGroup group;
        public CanvasGroup gameGroup;
        public bool visible;
        public bool hidGame;
        public int savedDisplay;
        public Vector3 savedScale;
        public float nextResolve;
        public readonly List<LinkState> links = new List<LinkState>();
    }

    public sealed class LinkState
    {
        public UiLink link;
        public Transform target;
        public Transform row;      // inside a Repeat copy: the game row the paths start from
        public Transform source;
        public bool isRowRoot;     // the top object of one Repeat copy (follows its game row)
        public bool hooked;

        // The design part's components (found once).
        public bool targetCached;
        public TMP_Text tmp; public Text legacy; public Graphic graphic; public Image image; public RawImage raw; public RectTransform rect;
        public Button button; public Slider slider; public Toggle toggle; public TMP_Dropdown dropdown; public TMP_InputField input;
        public bool parentDrivesLayout;

        // The game part's components (found again when the source changes).
        public Transform cachedSource;
        public TMP_Text sTmp; public Text sLegacy; public Graphic sGraphic; public Image sImage; public RawImage sRaw; public RectTransform sRect;
        public Button sButton; public Slider sSlider; public Toggle sToggle; public TMP_Dropdown sDropdown; public TMP_InputField sInput;

        // The designer's own values (read from the copy before the first change).
        public bool ownKnown;
        public string ownText;
        public Color ownColour;
        public Object ownPicture;
        public Vector2 ownPosition, ownSize;
        public bool textIsLive;    // the game changed the words once: from then on always copy them
        public bool textApplied, positionApplied, sizeApplied;

        // 21 Nov: the game's part makes its children by code (UiLiveChildren): this part shows live copies.
        public bool sLive;
        public bool liveCleared;
        public bool muted;         // a sample part inside a live part: never updated again
        public readonly List<Transform> liveSources = new List<Transform>();
        public readonly List<GameObject> liveClones = new List<GameObject>();

        // Repeat only.
        public float nextRows;
        public int rowsChildCount = -1;
        public readonly List<Transform> rows = new List<Transform>();
        public readonly List<GameObject> clones = new List<GameObject>();
        public readonly List<LinkState> cloneRoots = new List<LinkState>();
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
        wrapper.layer = s.root.gameObject.layer;
        var canvas = wrapper.GetComponent<Canvas>();
        canvas.renderMode = world ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = game.sortingOrder;
        if (world) canvas.worldCamera = game.worldCamera;
        CanvasScaler from = game.GetComponent<CanvasScaler>(), to = wrapper.GetComponent<CanvasScaler>();
        if (from != null)
        {
            to.uiScaleMode = from.uiScaleMode; to.referenceResolution = from.referenceResolution;
            to.screenMatchMode = from.screenMatchMode; to.matchWidthOrHeight = from.matchWidthOrHeight;
            to.scaleFactor = from.scaleFactor; to.referencePixelsPerUnit = from.referencePixelsPerUnit;
            to.dynamicPixelsPerUnit = from.dynamicPixelsPerUnit;
        }
        GraphicRaycaster gameRay = game.GetComponent<GraphicRaycaster>();
        if (gameRay != null) wrapper.GetComponent<GraphicRaycaster>().ignoreReversedGraphics = gameRay.ignoreReversedGraphics;

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
        state.gameGroup = s.root.GetComponent<CanvasGroup>();
        if (state.gameGroup != null) state.group = wrapper.AddComponent<CanvasGroup>();

        GameObject copy = Instantiate(prefab, wrapper.transform, false);
        copy.name = prefab.name;
        if (!copy.activeSelf) copy.SetActive(true); // 16 Nov: the GAME HUD keeps its screens switched off
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

    // ---- Paths (a few times a second) -----------------------------------------------------------

    private static void ResolveDesignLinks(LiveScreen s)
    {
        if (s.design == null) return;
        List<LinkState> links = s.design.links;
        for (int i = 0; i < links.Count; i++)
        {
            LinkState st = links[i];
            ResolveLink(st, s.root);
            for (int r = 0; r < st.cloneLinks.Count; r++)
            {
                ResolveLink(st.cloneRoots[r], st.cloneRoots[r].row);
                List<LinkState> row = st.cloneLinks[r];
                for (int k = 0; k < row.Count; k++) ResolveLink(row[k], row[k].row);
            }
        }
    }

    private static void ResolveLink(LinkState st, Transform from)
    {
        if (st.link == null || st.target == null || from == null) return;
        if (st.source == null)
            st.source = st.isRowRoot || string.IsNullOrEmpty(st.link.source) ? from : Resolve(from, st.link.source);
        if (!st.targetCached) CacheTarget(st);
        if (st.source == null) return;
        if (st.cachedSource != st.source) CacheSource(st);
        if (!st.hooked) { st.hooked = true; Hook(st); }
    }

    private static void CacheTarget(LinkState st)
    {
        st.targetCached = true;
        Transform t = st.target;
        st.tmp = t.GetComponent<TMP_Text>();
        st.legacy = t.GetComponent<Text>();
        st.graphic = t.GetComponent<Graphic>();
        st.image = t.GetComponent<Image>();
        st.raw = t.GetComponent<RawImage>();
        st.rect = t as RectTransform;
        st.button = t.GetComponent<Button>();
        st.slider = t.GetComponent<Slider>();
        st.toggle = t.GetComponent<Toggle>();
        st.dropdown = t.GetComponent<TMP_Dropdown>();
        st.input = t.GetComponent<TMP_InputField>();
        st.parentDrivesLayout = t.parent != null && t.parent.GetComponent<LayoutGroup>() != null;
    }

    private static void CacheSource(LinkState st)
    {
        Transform t = st.source;
        st.cachedSource = t;
        st.sTmp = t.GetComponent<TMP_Text>();
        st.sLegacy = t.GetComponent<Text>();
        st.sGraphic = t.GetComponent<Graphic>();
        st.sImage = t.GetComponent<Image>();
        st.sRaw = t.GetComponent<RawImage>();
        st.sRect = t as RectTransform;
        st.sButton = t.GetComponent<Button>();
        st.sSlider = t.GetComponent<Slider>();
        st.sToggle = t.GetComponent<Toggle>();
        st.sDropdown = t.GetComponent<TMP_Dropdown>();
        st.sInput = t.GetComponent<TMP_InputField>();
        st.sLive = t.GetComponent<UiLiveChildren>() != null;
    }

    /// <summary>The design part's own controls send the player's input to the game's controls (once per link).</summary>
    private static void Hook(LinkState st)
    {
        UiLink l = st.link;
        if (l.click && st.button != null)
            st.button.onClick.AddListener(() =>
            {
                Button g = st.sButton;
                if (g != null && g.interactable && g.isActiveAndEnabled) g.onClick.Invoke();
            });
        if (!l.control) return;
        if (st.slider != null)
            st.slider.onValueChanged.AddListener(v =>
            {
                if (st.sSlider != null && !Mathf.Approximately(st.sSlider.value, v)) st.sSlider.value = v;
            });
        if (st.toggle != null)
            st.toggle.onValueChanged.AddListener(on =>
            {
                if (st.sToggle != null && st.sToggle.isOn != on) st.sToggle.isOn = on;
            });
        if (st.dropdown != null)
            st.dropdown.onValueChanged.AddListener(index =>
            {
                if (st.sDropdown != null && st.sDropdown.value != index) st.sDropdown.value = index;
            });
        if (st.input != null)
        {
            st.input.onValueChanged.AddListener(words =>
            {
                if (st.sInput != null && st.sInput.text != words) st.sInput.text = words;
            });
            st.input.onEndEdit.AddListener(words => { if (st.sInput != null) st.sInput.onEndEdit.Invoke(words); });
            st.input.onSubmit.AddListener(words => { if (st.sInput != null) st.sInput.onSubmit.Invoke(words); });
        }
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
            // Game code that sets the screen's scale again (billboards, pop-in) would un-hide it:
            // take that scale for the shown version and hide the game's screen again.
            if (s.root.localScale != Vector3.zero)
            {
                d.savedScale = s.root.localScale;
                s.root.localScale = Vector3.zero;
            }
            if (wrapperRect.localScale != d.savedScale) wrapperRect.localScale = d.savedScale;
            wrapperRect.SetPositionAndRotation(s.root.position, s.root.rotation);
            Vector2 size = ((RectTransform)s.root).sizeDelta;
            if (wrapperRect.sizeDelta != size) wrapperRect.sizeDelta = size;
            if (d.canvas.worldCamera != s.canvas.worldCamera) d.canvas.worldCamera = s.canvas.worldCamera;
        }
        if (d.canvas.sortingOrder != s.canvas.sortingOrder) d.canvas.sortingOrder = s.canvas.sortingOrder;
        if (d.group != null && d.gameGroup != null)
        {
            if (d.group.alpha != d.gameGroup.alpha) d.group.alpha = d.gameGroup.alpha;
            if (d.group.blocksRaycasts != d.gameGroup.blocksRaycasts) d.group.blocksRaycasts = d.gameGroup.blocksRaycasts;
            if (d.group.interactable != d.gameGroup.interactable) d.group.interactable = d.gameGroup.interactable;
        }

        // Parts the game builds later, or rebuilds, are found again quickly (not only on the slow scan).
        if (Time.unscaledTime >= d.nextResolve)
        {
            d.nextResolve = Time.unscaledTime + ResolveEvery;
            ResolveDesignLinks(s);
        }
        updating = d;
        for (int i = 0; i < d.links.Count; i++) UpdateLink(d.links[i]);
        updating = null;
    }

    /// <summary>The design whose links are being updated (for live parts that silence their sample parts).</summary>
    private static DesignState updating;

    private static void UpdateLink(LinkState st)
    {
        UiLink l = st.link;
        if (l == null || st.target == null || st.muted) return;
        if (l.repeat && !st.isRowRoot)
        {
            if (st.source == null) ClearRepeat(st); else UpdateRepeat(st);
            return;
        }

        GameObject go = st.target.gameObject;
        if (st.source == null || st.cachedSource != st.source)
        {
            // The game's part is not there (yet): do not show a stale copy of it.
            if (st.source == null && l.showHide && !st.isRowRoot && go.activeSelf) go.SetActive(false);
            return;
        }
        if (l.showHide && !st.isRowRoot)
        {
            bool on = st.source.gameObject.activeInHierarchy;
            if (go.activeSelf != on) go.SetActive(on);
            if (!on) return;
            if (st.graphic != null && st.sGraphic != null && st.graphic.enabled != st.sGraphic.enabled) st.graphic.enabled = st.sGraphic.enabled;
        }
        if (st.sLive) UpdateLiveChildren(st);

        if (!st.ownKnown) ReadOwn(st);

        if (l.text && (st.tmp != null || st.legacy != null))
        {
            string words = st.sTmp != null ? st.sTmp.text : st.sLegacy != null ? st.sLegacy.text : null;
            if (words != null)
            {
                if (l.onlyGameChanges && !st.textIsLive && words == l.gameText)
                {
                    if (st.textApplied) { SetWords(st, st.ownText); st.textApplied = false; }
                }
                else
                {
                    if (l.onlyGameChanges) st.textIsLive = true;
                    SetWords(st, words);
                    st.textApplied = true;
                }
            }
        }

        if (l.color && st.graphic != null && st.sGraphic != null)
        {
            Color g = st.sGraphic.color, want = g;
            if (l.onlyGameChanges)
            {
                Color c = l.gameColour;
                bool sameRgb = Mathf.Abs(g.r - c.r) < 0.004f && Mathf.Abs(g.g - c.g) < 0.004f && Mathf.Abs(g.b - c.b) < 0.004f;
                if (sameRgb) // only a fade (or nothing) changed: the designer's colour, with the game's fade
                {
                    want = st.ownColour;
                    want.a = c.a > 0.001f ? st.ownColour.a * g.a / c.a : g.a;
                }
            }
            if (st.graphic.color != want) st.graphic.color = want;
        }

        if (l.picture)
        {
            if (st.image != null && st.sImage != null)
            {
                Sprite g = st.sImage.sprite;
                Sprite want = l.onlyGameChanges && !l.pictureAlways && g == l.gamePicture ? st.ownPicture as Sprite : g;
                if (st.image.sprite != want) st.image.sprite = want;
            }
            else if (st.raw != null && st.sRaw != null)
            {
                Texture g = st.sRaw.texture;
                Texture want = l.onlyGameChanges && !l.pictureAlways && g == l.gamePicture ? st.ownPicture as Texture : g;
                if (st.raw.texture != want) st.raw.texture = want;
            }
        }

        if (l.fill)
        {
            float r = FillOf(st.source, l.fillAxis);
            if (st.image != null && st.image.type == Image.Type.Filled && st.image.sprite != null) st.image.fillAmount = r;
            else
            {
                Vector3 scale = st.target.localScale;
                if (l.fillAxis == UiLink.Axis.Horizontal) scale.x = r; else scale.y = r;
                st.target.localScale = scale;
            }
        }

        if (l.followLayout && st.rect != null && st.sRect != null && !st.parentDrivesLayout) FollowLayout(st);
        if (l.click && st.button != null && st.sButton != null && st.button.interactable != st.sButton.interactable)
            st.button.interactable = st.sButton.interactable;
        if (l.control) UpdateControl(st);
    }

    private static void ReadOwn(LinkState st)
    {
        st.ownKnown = true;
        st.ownText = st.tmp != null ? st.tmp.text : st.legacy != null ? st.legacy.text : null;
        if (st.graphic != null) st.ownColour = st.graphic.color;
        st.ownPicture = st.image != null ? (Object)st.image.sprite : st.raw != null ? st.raw.texture : null;
        if (st.rect != null) { st.ownPosition = st.rect.anchoredPosition; st.ownSize = st.rect.sizeDelta; }
    }

    private static void SetWords(LinkState st, string words)
    {
        if (st.tmp != null) { if (st.tmp.text != words) st.tmp.text = words; }
        else if (st.legacy != null && st.legacy.text != words) st.legacy.text = words;
    }

    /// <summary>
    /// The part moves / grows when the game's part does. With "only when the game changes it", the
    /// designer's own place and size stay the base: a game move is added, a game resize is applied
    /// in proportion. Nothing is written while the game's part rests where it was captured, so the
    /// part's own components (a Scroll View dragging its content) are not disturbed.
    /// </summary>
    private static void FollowLayout(LinkState st)
    {
        UiLink l = st.link;
        Vector2 gamePosition = st.sRect.anchoredPosition, gameSize = st.sRect.sizeDelta;
        if (!l.onlyGameChanges)
        {
            if (st.rect.anchoredPosition != gamePosition) st.rect.anchoredPosition = gamePosition;
            if (st.rect.sizeDelta != gameSize) st.rect.sizeDelta = gameSize;
            return;
        }

        if ((gamePosition - l.gamePosition).sqrMagnitude > 0.0001f)
        {
            Vector2 want = st.ownPosition + (gamePosition - l.gamePosition);
            if (st.rect.anchoredPosition != want) st.rect.anchoredPosition = want;
            st.positionApplied = true;
        }
        else if (st.positionApplied)
        {
            st.rect.anchoredPosition = st.ownPosition;
            st.positionApplied = false;
        }

        if ((gameSize - l.gameSize).sqrMagnitude > 0.0001f)
        {
            var want = new Vector2(Scaled(st.ownSize.x, gameSize.x, l.gameSize.x), Scaled(st.ownSize.y, gameSize.y, l.gameSize.y));
            if (st.rect.sizeDelta != want) st.rect.sizeDelta = want;
            st.sizeApplied = true;
        }
        else if (st.sizeApplied)
        {
            st.rect.sizeDelta = st.ownSize;
            st.sizeApplied = false;
        }
    }

    private static float Scaled(float own, float game, float captured) =>
        Mathf.Abs(captured) > 0.01f ? own * game / captured : game;

    /// <summary>Game -> design for controls (the other way goes through the listeners made in Hook).</summary>
    private static void UpdateControl(LinkState st)
    {
        if (st.slider != null && st.sSlider != null)
        {
            Slider a = st.slider, b = st.sSlider;
            if (a.wholeNumbers != b.wholeNumbers) a.wholeNumbers = b.wholeNumbers;
            if (a.minValue != b.minValue) a.minValue = b.minValue;
            if (a.maxValue != b.maxValue) a.maxValue = b.maxValue;
            if (!Mathf.Approximately(a.value, b.value)) a.SetValueWithoutNotify(b.value);
            if (a.interactable != b.interactable) a.interactable = b.interactable;
        }
        if (st.toggle != null && st.sToggle != null)
        {
            if (st.toggle.isOn != st.sToggle.isOn) st.toggle.SetIsOnWithoutNotify(st.sToggle.isOn);
            if (st.toggle.interactable != st.sToggle.interactable) st.toggle.interactable = st.sToggle.interactable;
        }
        if (st.dropdown != null && st.sDropdown != null)
        {
            TMP_Dropdown a = st.dropdown, b = st.sDropdown;
            bool same = a.options.Count == b.options.Count;
            for (int i = 0; same && i < b.options.Count; i++) same = a.options[i].text == b.options[i].text;
            if (!same)
            {
                a.options.Clear();
                for (int i = 0; i < b.options.Count; i++) a.options.Add(new TMP_Dropdown.OptionData(b.options[i].text, b.options[i].image, b.options[i].color));
                a.RefreshShownValue();
            }
            if (a.value != b.value) a.SetValueWithoutNotify(b.value);
            if (a.interactable != b.interactable) a.interactable = b.interactable;
        }
        if (st.input != null && st.sInput != null)
        {
            if (!st.input.isFocused && st.input.text != st.sInput.text) st.input.SetTextWithoutNotify(st.sInput.text);
            if (st.input.interactable != st.sInput.interactable) st.input.interactable = st.sInput.interactable;
        }
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

    // ---- Live children (21 Nov) --------------------------------------------------------------------

    /// <summary>
    /// The game's part makes its children by code (a page of the Memo Book). The design part drops
    /// its own sample children once, then shows a copy of every shown game child, in the same order.
    /// Copied again only when the game's shown children change (new page, new step, part shown / hidden).
    /// </summary>
    private static void UpdateLiveChildren(LinkState st)
    {
        Transform game = st.source, design = st.target;
        if (!st.liveCleared)
        {
            st.liveCleared = true;
            // The sample parts inside stop for good (else a list link inside would keep adding rows).
            if (updating != null)
                foreach (LinkState other in updating.links)
                {
                    if (other == st || other.target == null || !other.target.IsChildOf(design)) continue;
                    other.muted = true;
                    for (int c = 0; c < other.clones.Count; c++) DropClone(other.clones[c]);
                    other.clones.Clear();
                }
            for (int i = design.childCount - 1; i >= 0; i--) DropClone(design.GetChild(i).gameObject);
        }
        else if (design.childCount != st.liveClones.Count)
        {
            // Anything else that got in (rows made by another link) goes.
            for (int i = design.childCount - 1; i >= 0; i--)
            {
                GameObject child = design.GetChild(i).gameObject;
                if (child.activeSelf && !st.liveClones.Contains(child)) DropClone(child);
            }
        }

        bool same = true;
        int n = 0;
        for (int i = 0; i < game.childCount; i++)
        {
            Transform child = game.GetChild(i);
            if (!child.gameObject.activeSelf) continue; // hidden, or on its way out (a page being replaced)
            if (n >= st.liveSources.Count || st.liveSources[n] != child || st.liveClones[n] == null) { same = false; break; }
            n++;
        }
        if (same && n == st.liveSources.Count) return;

        for (int i = 0; i < st.liveClones.Count; i++) DropClone(st.liveClones[i]);
        st.liveClones.Clear();
        st.liveSources.Clear();
        for (int i = 0; i < game.childCount; i++)
        {
            Transform child = game.GetChild(i);
            if (!child.gameObject.activeSelf) continue;
            GameObject copy = Instantiate(child.gameObject, design, false);
            copy.name = child.name;
            ForwardClicks(copy.transform, child);
            st.liveSources.Add(child);
            st.liveClones.Add(copy);
        }

        // A new page starts at its top.
        if (design is RectTransform rect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            ScrollRect scroll = design.GetComponentInParent<ScrollRect>();
            if (scroll != null && scroll.content == rect) scroll.verticalNormalizedPosition = 1f;
        }
    }

    private static void DropClone(GameObject go)
    {
        if (go == null) return;
        go.SetActive(false); // out of the layout at once (Destroy waits for the end of the frame)
        Destroy(go);
    }

    /// <summary>Buttons inside a live copy click the game's buttons (same order in both).</summary>
    private static void ForwardClicks(Transform copy, Transform game)
    {
        Button[] mine = copy.GetComponentsInChildren<Button>(true);
        if (mine.Length == 0) return;
        Button[] theirs = game.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < mine.Length && i < theirs.Length; i++)
        {
            Button g = theirs[i];
            mine[i].onClick.AddListener(() => { if (g != null && g.interactable && g.isActiveAndEnabled) g.onClick.Invoke(); });
        }
    }

    // ---- Repeat rows ----------------------------------------------------------------------------

    /// <summary>Which children of the game's list are rows (all, or those whose name starts with the link's text).</summary>
    private static void RefreshRows(LinkState st)
    {
        Transform list = st.source;
        bool stale = list.childCount != st.rowsChildCount || Time.unscaledTime >= st.nextRows;
        for (int i = 0; !stale && i < st.rows.Count; i++) stale = st.rows[i] == null;
        if (!stale) return;
        st.rowsChildCount = list.childCount;
        st.nextRows = Time.unscaledTime + RowsEvery;
        st.rows.Clear();
        string start = st.link.repeatNameStart;
        bool every = string.IsNullOrEmpty(start);
        for (int i = 0; i < list.childCount; i++)
        {
            Transform child = list.GetChild(i);
            if (every || child.name.StartsWith(start, System.StringComparison.Ordinal)) st.rows.Add(child);
        }
    }

    private static void ClearRepeat(LinkState st)
    {
        if (st.target.gameObject.activeSelf) st.target.gameObject.SetActive(false);
        for (int i = 0; i < st.clones.Count; i++) if (st.clones[i] != null) Destroy(st.clones[i]);
        st.clones.Clear();
        st.cloneRoots.Clear();
        st.cloneLinks.Clear();
        st.rows.Clear();
        st.rowsChildCount = -1;
    }

    private static void UpdateRepeat(LinkState st)
    {
        GameObject template = st.target.gameObject;
        if (template.activeSelf) template.SetActive(false);
        RefreshRows(st);
        int n = st.rows.Count;

        while (st.clones.Count > n)
        {
            int last = st.clones.Count - 1;
            if (st.clones[last] != null) Destroy(st.clones[last]);
            st.clones.RemoveAt(last);
            st.cloneRoots.RemoveAt(last);
            st.cloneLinks.RemoveAt(last);
        }

        for (int i = 0; i < n; i++)
        {
            Transform row = st.rows[i];
            if (row == null) continue;
            if (i >= st.clones.Count)
            {
                GameObject copy = Instantiate(template, template.transform.parent, false);
                copy.name = template.name + " " + (i + 1);
                copy.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1 + i);
                var links = new List<LinkState>();
                foreach (UiLink l in copy.GetComponentsInChildren<UiLink>(true))
                    if (l.transform != copy.transform && !InsideRepeat(l.transform, copy.transform))
                        links.Add(new LinkState { link = l, target = l.transform, row = row });
                var root = new LinkState { link = copy.GetComponent<UiLink>(), target = copy.transform, row = row, isRowRoot = true };
                st.clones.Add(copy);
                st.cloneRoots.Add(root);
                st.cloneLinks.Add(links);
                ResolveLink(root, row);
                foreach (LinkState child in links) ResolveLink(child, row);
            }

            LinkState rowRoot = st.cloneRoots[i];
            List<LinkState> rowLinks = st.cloneLinks[i];
            if (rowRoot.row != row) // the game reordered or rebuilt its list
            {
                rowRoot.row = row; rowRoot.source = null; ResolveLink(rowRoot, row);
                foreach (LinkState child in rowLinks) { child.row = row; child.source = null; ResolveLink(child, row); }
            }

            GameObject clone = st.clones[i];
            if (clone == null) continue;
            bool on = row.gameObject.activeSelf;
            if (clone.activeSelf != on) clone.SetActive(on);
            if (!on) continue;
            UpdateLink(rowRoot);
            for (int k = 0; k < rowLinks.Count; k++) UpdateLink(rowLinks[k]);
        }
    }
}
