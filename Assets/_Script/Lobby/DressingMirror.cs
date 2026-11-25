using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 25 Nov 2026 (Mr.k): a DRESSING MIRROR in the lobby. Look at it and press E:
///   - the view glides from your eyes to the mirror and turns to look at YOU (you see your body),
///   - a panel lists the 7 shirt colours (White default + 6 pastels) twice: OUTER shirt and INNER t-shirt
///     (mix and match). Outer colours other players wear are crossed out and cannot be picked (the
///     server checks it too); inner colours are free. Every other screen UI is hidden meanwhile,
///   - Esc / Done: the view glides back into your eyes.
/// Put it on the mirror glass object (Add Component > Dressing Mirror). A collider is added if missing.
/// Panel colours / font: Resources/UI/Styles/Mirror Style (CodeScreenStyle), optional.
/// LOCAL (camera + panel). The colour itself is synced by the player's seat (RoHRoomPlayer).
/// </summary>
[DisallowMultipleComponent]
public sealed class DressingMirror : MonoBehaviour, ILocalInteractable
{
    [SerializeField] private string prompt = "Press E: change shirt colour";
    [Tooltip("Seconds of the camera glide (both ways).")]
    [SerializeField, Min(0.1f)] private float glideSeconds = 0.8f;
    [Tooltip("How far in front of the glass the view stops (metres).")]
    [SerializeField, Min(0f)] private float viewOffset = 0.15f;

    public float GlideSeconds => glideSeconds;
    public float ViewOffset => viewOffset;

    private void Awake()
    {
        if (GetComponent<Collider>() == null) gameObject.AddComponent<BoxCollider>();
    }

    public string GetInteractionPrompt() => prompt;

    public bool CanInteract()
    {
        PlayerHealth me = PlayerHealth.LocalInstance;
        return !MirrorScreen.IsOpen && me != null && !me.IsDead && !me.IsDowned;
    }

    public void Interact(GameObject interactor)
    {
        if (CanInteract()) MirrorScreen.Open(this);
    }

    public Transform GetTransform() => transform;

    /// <summary>The middle of the glass.</summary>
    public Vector3 Centre
    {
        get
        {
            Renderer r = GetComponent<Renderer>();
            if (r != null) return r.bounds.center;
            Collider c = GetComponent<Collider>();
            return c != null ? c.bounds.center : transform.position;
        }
    }
}

/// <summary>25 Nov: the mirror view (camera glide) + the shirt colour panel. One at a time, local.</summary>
[DefaultExecutionOrder(1000)] // after FirstPersonCamera, so the glide has the last word on the camera
public sealed class MirrorScreen : MonoBehaviour
{
    private const string BodyLayerName = "LocalPlayerBody";
    private const string ArmsLayerName = "FirstPersonArms";

    private static MirrorScreen instance;
    public static bool IsOpen => instance != null;

    private DressingMirror mirror;
    private Camera cam;
    private Transform eye;                 // the first-person pivot (camera parent)
    private Vector3 camLocalPos;
    private Quaternion camLocalRot;
    private int savedMask;
    private readonly List<Renderer> hiddenHeld = new List<Renderer>();
    private Vector3 viewPos;
    private Quaternion viewRot;
    private float t;                        // 0 = in your eyes, 1 = at the mirror
    private bool closing;

    // Panel.
    private readonly List<Swatch> swatches = new List<Swatch>();
    private TMP_Text message;
    private float nextRefresh;
    private float messageUntil;

    private sealed class Swatch
    {
        public bool inner;
        public int colour;
        public Button button;
        public Image chip;
        public Image ring;
        public TMP_Text cross;
    }

    private static CodeScreenStyle Style => CodeScreenStyle.Load("Mirror Style", OriginalColours);

    /// <summary>Used only when the Mirror Style asset is missing.</summary>
    private static void OriginalColours(CodeScreenStyle s)
    {
        s.dim = new Color(0f, 0f, 0f, 0f);
        s.panel = new Color(0.1f, 0.08f, 0.12f, 0.92f);
        s.title = s.accent = new Color(1f, 0.85f, 0.45f);
        s.text = new Color(0.96f, 0.94f, 0.92f);
        s.muted = new Color(0.68f, 0.65f, 0.7f);
        s.faint = new Color(0.5f, 0.48f, 0.52f);
        s.good = new Color(0.55f, 1f, 0.65f);
        s.bad = new Color(1f, 0.45f, 0.45f);
        s.button = new Color(0.22f, 0.18f, 0.26f, 1f);
        s.buttonText = s.buttonHoverText = s.text;
        s.buttonHighlighted = new Color(1.3f, 1.3f, 1.3f);
        s.buttonDisabled = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        s.primaryButton = new Color(1f, 0.85f, 0.45f);
        s.primaryButtonText = Color.black;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    public static void Open(DressingMirror mirror)
    {
        if (instance != null || mirror == null) return;
        PlayerHealth me = PlayerHealth.LocalInstance;
        if (me == null) return;
        FirstPersonCamera fp = me.GetComponentInChildren<FirstPersonCamera>();
        Camera cam = fp != null ? fp.GetComponentInChildren<Camera>() : null;
        if (cam == null) return;

        var go = new GameObject("Mirror Screen");
        instance = go.AddComponent<MirrorScreen>();
        instance.Begin(mirror, me, fp, cam);
    }

    public static void Close()
    {
        if (instance != null) instance.closing = true;
    }

    private void Begin(DressingMirror m, PlayerHealth me, FirstPersonCamera fp, Camera c)
    {
        mirror = m;
        cam = c;
        eye = cam.transform.parent != null ? cam.transform.parent : fp.transform;
        camLocalPos = cam.transform.localPosition;
        camLocalRot = cam.transform.localRotation;

        // See your own body, hide what you hold in front of your eyes.
        savedMask = cam.cullingMask;
        int body = LayerMask.NameToLayer(BodyLayerName);
        int arms = LayerMask.NameToLayer(ArmsLayerName);
        if (body >= 0) cam.cullingMask |= 1 << body;
        if (arms >= 0) cam.cullingMask &= ~(1 << arms);
        foreach (Renderer r in cam.GetComponentsInChildren<Renderer>(false))
            if (r.enabled) { r.enabled = false; hiddenHeld.Add(r); }

        // Where the view ends: at the glass, looking at the player's chest.
        Vector3 centre = mirror.Centre;
        Vector3 player = me.transform.position;
        Vector3 toPlayer = player - centre;
        toPlayer.y = 0f;
        toPlayer = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : -mirror.transform.forward;
        viewPos = centre + toPlayer * mirror.ViewOffset;
        // 25 Nov fix ("too high"): the player object sits at the middle of the body, not at the feet.
        // Use the body's real feet and height: eyes a bit below the head, looking at the middle of the body.
        BodyHeight(me, out float feet, out float height);
        viewPos.y = feet + height * 0.82f;
        // 25 Nov: looks a bit higher (head was against the top edge).
        Vector3 middle = new Vector3(player.x, feet + height * 0.68f, player.z);
        viewRot = Quaternion.LookRotation(middle - viewPos, Vector3.up);

        Build();
        Refresh();
        HideOtherUi(); // 25 Nov: only the mirror panel on screen (hotbar, key bar, meters ... hidden)
        OverlayPanels.Opened();
        PersistentHUD.PushHidden();
        OverlayPanels.SetMouseForUi(true);
    }

    /// <summary>Feet height and body height of the player model (renderers), with safe fallbacks.</summary>
    private static void BodyHeight(PlayerHealth me, out float feet, out float height)
    {
        bool any = false;
        Bounds b = default;
        foreach (Renderer r in me.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is SkinnedMeshRenderer) || !r.enabled) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (any && b.size.y > 1f && b.size.y < 2.6f) { feet = b.min.y; height = b.size.y; return; }
        CapsuleCollider capsule = me.GetComponent<CapsuleCollider>();
        if (capsule != null)
        {
            float h = capsule.height * me.transform.lossyScale.y;
            feet = me.transform.TransformPoint(capsule.center).y - h * 0.5f;
            height = h;
            return;
        }
        feet = me.transform.position.y;
        height = 1.8f;
    }

    private void Update()
    {
        Keyboard k = Keyboard.current;
        if (k != null && k.escapeKey.wasPressedThisFrame) closing = true;
        PlayerHealth me = PlayerHealth.LocalInstance;
        if (me == null || me.IsDead || me.IsDowned || mirror == null || cam == null) { Destroy(gameObject); return; }
        if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + 0.2f; Refresh(); }
        if (message != null && messageUntil > 0f && Time.unscaledTime > messageUntil) { message.text = ""; messageUntil = 0f; }
    }

    private void LateUpdate()
    {
        if (cam == null || eye == null) return;
        float step = Time.unscaledDeltaTime / Mathf.Max(0.1f, mirror != null ? mirror.GlideSeconds : 0.8f);
        t = Mathf.Clamp01(t + (closing ? -step : step));
        float e = t * t * (3f - 2f * t);
        Vector3 fromPos = eye.TransformPoint(camLocalPos);
        Quaternion fromRot = eye.rotation * camLocalRot;
        cam.transform.SetPositionAndRotation(Vector3.Lerp(fromPos, viewPos, e), Quaternion.Slerp(fromRot, viewRot, e));
        if (closing && t <= 0f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (cam != null)
        {
            cam.transform.localPosition = camLocalPos;
            cam.transform.localRotation = camLocalRot;
            cam.cullingMask = savedMask;
        }
        foreach (Renderer r in hiddenHeld) if (r != null) r.enabled = true;
        ShowOtherUi();
        if (instance != this) return;
        instance = null;
        OverlayPanels.Closed();
        PersistentHUD.PopHidden();
        if (PlayerHealth.LocalInstance != null) OverlayPanels.SetMouseForUi(false);
        else GameplayInput.Blocked = false;
    }

    // ---- Picking --------------------------------------------------------------------------

    private void Pick(bool inner, int colour)
    {
        if (closing) return;
        if (inner)
        {
            if (colour == ShirtColors.LocalInner()) return;
            ShirtColors.RequestInner(colour);
            Say($"Inner t-shirt: {ShirtColors.Names[colour]}", Style.good);
            return;
        }
        if (colour == ShirtColors.LocalColour()) return;
        if (ShirtColors.TakenByOther(colour)) { Say($"{ShirtColors.Names[colour]} outer shirt is taken by another player.", Style.bad); return; }
        ShirtColors.Request(colour);
        Say($"Outer shirt: {ShirtColors.Names[colour]}", Style.good);
    }

    private void Say(string text, Color colour)
    {
        if (message == null) return;
        message.text = text;
        message.color = colour;
        messageUntil = Time.unscaledTime + 2.5f;
    }

    private void Refresh()
    {
        int outer = ShirtColors.LocalColour();
        int inner = ShirtColors.LocalInner();
        CodeScreenStyle s = Style;
        foreach (Swatch w in swatches)
        {
            bool taken = !w.inner && ShirtColors.TakenByOther(w.colour);
            bool yours = w.colour == (w.inner ? inner : outer);
            w.button.interactable = !taken;
            w.ring.enabled = yours;
            w.chip.color = taken ? ShirtColors.Colours[w.colour] * new Color(0.4f, 0.4f, 0.4f, 1f) : ShirtColors.Colours[w.colour];
            if (w.cross.enabled != taken) w.cross.enabled = taken;
        }
        string outerLine = "OUTER SHIRT   " + CodeScreenStyle.Paint(ShirtColors.Names[outer], s.accent);
        string innerLine = "INNER T-SHIRT   " + CodeScreenStyle.Paint(ShirtColors.Names[inner], s.accent);
        if (outerHeader.text != outerLine) outerHeader.text = outerLine;
        if (innerHeader.text != innerLine) innerHeader.text = innerLine;
    }

    // ---- Hide every other screen UI while the mirror is open (25 Nov) ----------------------

    private readonly List<Canvas> hiddenCanvases = new List<Canvas>();

    private void HideOtherUi()
    {
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
        {
            if (c == null || !c.enabled || c.renderMode == RenderMode.WorldSpace) continue;
            if (c.transform.IsChildOf(transform)) continue; // our own panel
            c.enabled = false;
            hiddenCanvases.Add(c);
        }
    }

    private void ShowOtherUi()
    {
        foreach (Canvas c in hiddenCanvases) if (c != null) c.enabled = true;
        hiddenCanvases.Clear();
    }

    // ---- Panel (built in code, styled by Mirror Style) -------------------------------------

    private TMP_Text outerHeader, innerHeader;

    private void Build()
    {
        CodeScreenStyle s = Style;
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        // Card on the right: your body stays in view on the left.
        RectTransform card = Rect("Card", transform);
        card.anchorMin = card.anchorMax = new Vector2(1f, 0.5f);
        card.pivot = new Vector2(1f, 0.5f);
        card.sizeDelta = new Vector2(500f, 560f);
        card.anchoredPosition = new Vector2(-60f, 0f);
        Image cardImage = card.gameObject.AddComponent<Image>();
        s.StyleCard(cardImage);

        TMP_Text title = Text(card, "Title", "MIRROR", 40f, new Vector2(0f, -34f), new Vector2(460f, 50f));
        s.StyleText(title, s.title, true);
        TMP_Text sub = Text(card, "Subtitle", "Mix and match your shirts", 22f, new Vector2(0f, -80f), new Vector2(460f, 30f));
        s.StyleText(sub, s.muted);

        // Outer shirt: unique (taken colours are crossed out). Inner t-shirt: any colour.
        outerHeader = Text(card, "Outer Header", "", 22f, new Vector2(0f, -128f), new Vector2(460f, 30f));
        s.StyleText(outerHeader, s.text);
        MakeRow(card, false, -166f, s);
        TMP_Text note = Text(card, "Outer Note", "Each outer colour: one player only", 17f, new Vector2(0f, -232f), new Vector2(460f, 24f));
        s.StyleText(note, s.faint);

        innerHeader = Text(card, "Inner Header", "", 22f, new Vector2(0f, -278f), new Vector2(460f, 30f));
        s.StyleText(innerHeader, s.text);
        MakeRow(card, true, -316f, s);

        message = Text(card, "Message", "", 22f, new Vector2(0f, -400f), new Vector2(460f, 30f));
        s.StyleText(message, s.good);

        RectTransform done = Rect("Done", card);
        done.anchorMin = done.anchorMax = new Vector2(0.5f, 1f);
        done.pivot = new Vector2(0.5f, 1f);
        done.sizeDelta = new Vector2(220f, 52f);
        done.anchoredPosition = new Vector2(0f, -470f);
        Image doneImage = done.gameObject.AddComponent<Image>();
        Button doneButton = done.gameObject.AddComponent<Button>();
        doneButton.targetGraphic = doneImage;
        TMP_Text doneText = Text(done, "Label", "Done  [Esc]", 24f, Vector2.zero, Vector2.zero, true);
        s.StyleButton(doneButton, doneText, CodeScreenStyle.ButtonKind.Primary);
        doneButton.onClick.AddListener(Close);
    }

    /// <summary>One row of 7 colour chips (top edge at <paramref name="top"/>).</summary>
    private void MakeRow(RectTransform card, bool inner, float top, CodeScreenStyle s)
    {
        const float size = 54f, gap = 10f;
        float width = ShirtColors.Count * size + (ShirtColors.Count - 1) * gap;
        for (int i = 0; i < ShirtColors.Count; i++)
        {
            float x = -width / 2f + i * (size + gap) + size / 2f;
            swatches.Add(MakeSwatch(card, inner, i, new Vector2(x, top - size / 2f), size, s));
        }
    }

    private Swatch MakeSwatch(RectTransform parent, bool inner, int colour, Vector2 centre, float size, CodeScreenStyle s)
    {
        RectTransform root = Rect((inner ? "Inner " : "Outer ") + ShirtColors.Names[colour], parent);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(size, size);
        root.anchoredPosition = centre;

        Image back = root.gameObject.AddComponent<Image>();
        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = back;
        s.StyleButton(button, null);

        RectTransform ringRt = Rect("Wearing Ring", root);
        Stretch(ringRt, -5f);
        Image ring = ringRt.gameObject.AddComponent<Image>();
        ring.color = s.accent;
        ring.raycastTarget = false;
        ringRt.SetAsFirstSibling();

        RectTransform chipRt = Rect("Chip", root);
        Stretch(chipRt, 6f);
        Image chip = chipRt.gameObject.AddComponent<Image>();
        chip.raycastTarget = false;

        TMP_Text cross = Text(root, "Taken", "X", 34f, Vector2.zero, Vector2.zero, true);
        cross.color = new Color(1f, 1f, 1f, 0.85f);
        cross.fontStyle = FontStyles.Bold;
        cross.enabled = false;

        int c = colour;
        bool layer = inner;
        button.onClick.AddListener(() => Pick(layer, c));
        return new Swatch { inner = inner, colour = colour, button = button, chip = chip, ring = ring, cross = cross };
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    private static TMP_Text Text(RectTransform parent, string name, string words, float size, Vector2 pos, Vector2 box, bool fill = false)
    {
        RectTransform rt = Rect(name, parent);
        if (fill) Stretch(rt, 0f);
        else
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = box;
            rt.anchoredPosition = pos;
        }
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = words;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        t.richText = true;
        if (TMP_Settings.defaultFontAsset != null && t.font == null) t.font = TMP_Settings.defaultFontAsset;
        return t;
    }
}
