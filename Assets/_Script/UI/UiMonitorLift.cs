using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 25 Nov 2026: design B "lift the monitor up" for the Lobby Shop, Lobby Storage, Tab player list
/// and Esc menu. Sits on the screen's card (the panel that holds everything).
///
///   Open:  the monitor, still switched off (dark glass), rises from below with a small tilt and
///          bounce, then switches on: the glass flickers away with a burst of static.
///   Close: the glass goes dark with static, then the monitor is lowered out of view.
///
/// It also draws the MONITOR FRAME (your drawing) around the card, moving with it.
/// All numbers and the frame picture: Assets/Resources/UI/Styles/Monitor Frame (MonitorFrameStyle).
///
/// If the screen has a designer copy in the GAME HUD (UI Hub), the copy's card moves the same way
/// (found by the same path), so whichever version is shown is animated.
/// Uses real time: works while the game is paused. LOCAL ONLY (UI).
///
/// 25 Nov fix: after closing, the card stays PARKED (down, dark, see-through) instead of jumping back
/// into place. The UI Hub copy of a closed screen can stay on screen a few frames after the game's
/// screen is gone; it used to pop back up fully lit ("the other window closes later"). The place is
/// restored at the next open. Runs after the UI Hub (LateUpdate, order 10001), so a copy the Hub
/// makes in the middle of an animation is posed in the same frame and never flashes.
/// </summary>
[DefaultExecutionOrder(10001)] // after UiLayoutRuntime (10000)
[DisallowMultipleComponent]
public sealed class UiMonitorLift : MonoBehaviour
{
    private enum Phase { Idle, Lifting, Booting, PoweringOff, Lowering }

    private sealed class Rig
    {
        public RectTransform root;
        public Image glass;
        public RawImage noise;
        public Image frame;
    }

    private sealed class Target
    {
        public RectTransform rect;
        public CanvasGroup group;
        public Rig rig;
        public bool baseKnown;
        public Vector2 basePivot;
        public Vector3 baseScale;
        public Quaternion baseRot;
    }

    private string screen;
    private Target own;
    private Target design;               // the GAME HUD copy's card, when the UI Hub shows one
    private readonly List<Graphic> dims = new List<Graphic>();
    private readonly List<float> dimAlpha = new List<float>();
    private bool dimKnown;

    private Phase phase = Phase.Idle;
    private bool parked;                 // closed: down, dark and see-through until the next open
    private float t;
    private float lastPose = 1f, lastDark, lastStatic; // the look now (for a Hub copy that appears late)
    private System.Action whenOpened, whenClosed;

    /// <summary>True while it is rising, switching on / off or lowering.</summary>
    public bool Busy => phase != Phase.Idle;

    /// <summary>Puts the monitor on a screen's card (once; again = the same one). Dims fade with it.</summary>
    public static UiMonitorLift On(RectTransform card, string screenName, params Graphic[] dimLayers)
    {
        if (card == null) return null;
        UiMonitorLift lift = card.GetComponent<UiMonitorLift>();
        if (lift == null)
        {
            lift = card.gameObject.AddComponent<UiMonitorLift>();
            lift.screen = screenName;
            lift.own = lift.MakeTarget(card);
        }
        if (dimLayers != null)
            foreach (Graphic g in dimLayers)
                if (g != null && !lift.dims.Contains(g)) { lift.dims.Add(g); lift.dimAlpha.Add(g.color.a); }
        return lift;
    }

    // ---- Play ------------------------------------------------------------------------------

    /// <summary>Rise + switch on. <paramref name="done"/> runs when the picture is fully on.</summary>
    public void PlayOpen(System.Action done = null)
    {
        MonitorFrameStyle style = MonitorFrameStyle.Get();
        whenClosed = null; // opened again while closing: it must not hide afterwards
        UiLayoutRuntime.ScanSoon(); // a new screen gets its GAME HUD copy this frame, not up to 0.5 s later
        ResolveDesign();
        RefreshFrames(style);

        if (!style.animate || !isActiveAndEnabled)
        {
            Rest();
            done?.Invoke();
            return;
        }

        if (phase == Phase.Idle && !parked) CaptureBase(); // parked = still down: the old place is right
        parked = false;
        whenOpened = done;
        phase = Phase.Lifting;
        t = 0f;
        SetBlocks(false);
        SetAlpha(1f);
        Pose(0f);
        Glass(1f, 0f);
        Dim(0f);
    }

    /// <summary>Switch off + lower. <paramref name="done"/> hides / destroys the screen.</summary>
    public void PlayClose(System.Action done)
    {
        MonitorFrameStyle style = MonitorFrameStyle.Get();
        whenOpened = null;
        if (!style.animate || !isActiveAndEnabled)
        {
            Rest();
            done?.Invoke();
            return;
        }

        if (phase == Phase.Idle && !parked) CaptureBase();
        ResolveDesign();
        whenClosed = done;
        phase = Phase.PoweringOff;
        t = 0f;
        SetBlocks(false);
    }

    /// <summary>Back in place at once, no callbacks (the screen was shut some other way).</summary>
    public void Stop()
    {
        whenOpened = whenClosed = null;
        if (phase != Phase.Idle || parked) Rest();
    }

    /// <summary>Back in place and visible, no callbacks (also undoes a park).</summary>
    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
        whenOpened = whenClosed = null;
        if (phase != Phase.Idle || parked) Rest();
    }

    private void OnEnable() => SceneManager.activeSceneChanged += OnSceneChanged;

    // 25 Nov: a scene change (match start, leaving) never shows an animation: it ends at once.
    // Scene loading freezes the frames, so a half-played animation stayed on screen into the match.
    private void OnSceneChanged(Scene from, Scene to) => FinishNow();

    /// <summary>Jumps to the end of what is playing: closing = closed (and hidden), opening = open.</summary>
    public void FinishNow()
    {
        if (phase == Phase.Idle) return;
        if (phase == Phase.PoweringOff || phase == Phase.Lowering)
        {
            System.Action done = whenClosed;
            whenClosed = null;
            phase = Phase.Idle;
            Park();
            done?.Invoke();
        }
        else
        {
            System.Action done = whenOpened;
            whenOpened = null;
            Rest();
            done?.Invoke();
        }
    }

    private void LateUpdate()
    {
        if (phase == Phase.Idle) return;
        MonitorFrameStyle s = MonitorFrameStyle.Get();
        t += Time.unscaledDeltaTime;
        if (design == null) ResolveDesign(); // the UI Hub makes its copy a frame or two later

        switch (phase)
        {
            case Phase.Lifting:
            {
                float x = s.liftSeconds > 0f ? Mathf.Clamp01(t / s.liftSeconds) : 1f;
                Pose(s.liftCurve.Evaluate(x));
                Glass(1f, 0f);
                Dim(x);
                if (x >= 1f) { phase = Phase.Booting; t = 0f; Pose(1f); }
                break;
            }
            case Phase.Booting:
            {
                float x = s.bootSeconds > 0f ? Mathf.Clamp01(t / s.bootSeconds) : 1f;
                float glass = Mathf.Clamp01(s.bootGlass.Evaluate(x));
                Glass(glass, s.noiseStrength * (1f - x) * (1f - x));
                if (x >= 1f)
                {
                    Rest();
                    System.Action done = whenOpened;
                    whenOpened = null;
                    done?.Invoke();
                }
                break;
            }
            case Phase.PoweringOff:
            {
                float x = s.offSeconds > 0f ? Mathf.Clamp01(t / s.offSeconds) : 1f;
                Glass(x, s.noiseStrength * x);
                if (x >= 1f) { phase = Phase.Lowering; t = 0f; }
                break;
            }
            case Phase.Lowering:
            {
                float x = s.lowerSeconds > 0f ? Mathf.Clamp01(t / s.lowerSeconds) : 1f;
                Pose(1f - s.lowerCurve.Evaluate(x));
                Glass(1f, s.noiseStrength * 0.3f * (1f - x));
                Dim(1f - x);
                if (x >= 1f)
                {
                    System.Action done = whenClosed;
                    whenClosed = null;
                    phase = Phase.Idle;
                    Park();         // stays down and invisible (a Hub copy may live a few frames more) ...
                    done?.Invoke(); // ... while the screen is hidden / destroyed. Next open puts it back.
                }
                break;
            }
        }
    }

    // ---- Look ------------------------------------------------------------------------------

    private void CaptureBase()
    {
        Capture(own);
        Capture(design);
        if (!dimKnown || phase == Phase.Idle)
        {
            for (int i = 0; i < dims.Count; i++) if (dims[i] != null) dimAlpha[i] = dims[i].color.a;
            dimKnown = true;
        }
    }

    private static void Capture(Target target)
    {
        if (target == null || target.rect == null) return;
        target.basePivot = target.rect.pivot;
        target.baseScale = target.rect.localScale;
        target.baseRot = target.rect.localRotation;
        target.baseKnown = true;
    }

    /// <summary>Everything in place, glass clear, dims back, visible, clickable.</summary>
    private void Rest()
    {
        phase = Phase.Idle;
        parked = false;
        Pose(1f);
        Glass(0f, 0f);
        Dim(1f);
        SetAlpha(1f);
        SetBlocks(true);
    }

    /// <summary>Closed: down below, dark, see-through and not clickable (dims back for next time).</summary>
    private void Park()
    {
        parked = true;
        Pose(0f);
        Glass(1f, 0f);
        SetAlpha(0f);
        SetBlocks(false);
        Dim(0f);
    }

    private void SetAlpha(float a)
    {
        if (own != null && own.group != null) own.group.alpha = a;
        if (design != null && design.group != null) design.group.alpha = a;
    }

    /// <summary>0 = down below the screen, 1 = in place (a bit above 1 = bounce).</summary>
    private void Pose(float p)
    {
        lastPose = p;
        Pose(own, p);
        Pose(design, p);
    }

    /// <summary>
    /// 25 Nov fix: the card is moved by its PIVOT (plus scale / turn), never by its position or size.
    /// The UI Hub copies a game part's position / size into its GAME HUD copy (Follow Layout) and
    /// keeps the designer's place only while the game's part stays put; moving the game's card
    /// made the copy take the animated place and keep it (the screens ended up somewhere else).
    /// Nothing in the Hub (or the screens' code) reads the pivot, so the designer's place is safe.
    /// </summary>
    private static void Pose(Target target, float p)
    {
        if (target == null || target.rect == null || !target.baseKnown) return;
        MonitorFrameStyle s = MonitorFrameStyle.Get();
        RectTransform rt = target.rect;

        var scale = new Vector3(
            target.baseScale.x * Mathf.LerpUnclamped(s.startWidth, 1f, p),
            target.baseScale.y * Mathf.LerpUnclamped(s.startHeight, 1f, p),
            target.baseScale.z);
        Quaternion turn = target.baseRot * Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(s.startTilt, 0f, p));
        rt.localScale = scale;
        rt.localRotation = turn;

        // Where the card's middle must be (from the pivot point, which stays where it is):
        // its middle at rest, moved down by the drop. Then find the pivot that puts it there.
        Vector2 size = rt.rect.size;
        if (size.x < 1f || size.y < 1f || Mathf.Abs(scale.x) < 0.0001f || Mathf.Abs(scale.y) < 0.0001f)
        {
            rt.pivot = target.basePivot;
            return;
        }
        Vector2 middleAtRest = Vector2.Scale(new Vector2(0.5f, 0.5f) - target.basePivot, size);
        Vector3 restOffset = target.baseRot * Vector3.Scale(target.baseScale, middleAtRest);
        Vector3 want = restOffset + Vector3.down * (s.liftDistance * (1f - p));
        Vector3 local = Quaternion.Inverse(turn) * want;
        rt.pivot = new Vector2(0.5f - local.x / scale.x / size.x, 0.5f - local.y / scale.y / size.y);
    }

    /// <summary>Darkness of the glass (0 = picture) and strength of the static.</summary>
    private void Glass(float dark, float static01)
    {
        lastDark = dark;
        lastStatic = static01;
        Glass(own, dark, static01);
        Glass(design, dark, static01);
    }

    private static void Glass(Target target, float dark, float static01)
    {
        if (target == null || target.rig == null || target.rig.glass == null) return;
        MonitorFrameStyle s = MonitorFrameStyle.Get();
        Rig rig = target.rig;

        Color g = s.glass;
        g.a *= dark;
        rig.glass.color = g;
        bool showGlass = g.a > 0.001f;
        if (rig.glass.enabled != showGlass) rig.glass.enabled = showGlass;

        bool showNoise = static01 > 0.001f;
        if (rig.noise.enabled != showNoise) rig.noise.enabled = showNoise;
        if (!showNoise) return;
        Texture tex = s.noise != null ? s.noise : CodeNoise();
        if (rig.noise.texture != tex) rig.noise.texture = tex;
        rig.noise.color = new Color(1f, 1f, 1f, static01);
        Vector2 size = rig.root.rect.size;
        float grain = Mathf.Max(0.5f, s.grainSize);
        rig.noise.uvRect = new Rect(Random.value, Random.value,
            size.x / (grain * Mathf.Max(1, tex.width)), size.y / (grain * Mathf.Max(1, tex.height)));
    }

    private void Dim(float f)
    {
        if (!MonitorFrameStyle.Get().fadeDim) f = 1f;
        for (int i = 0; i < dims.Count; i++)
        {
            Graphic g = dims[i];
            if (g == null) continue;
            Color c = g.color;
            c.a = dimAlpha[i] * Mathf.Clamp01(f);
            g.color = c;
        }
    }

    private void SetBlocks(bool on)
    {
        if (own != null && own.group != null) own.group.blocksRaycasts = on;
        if (design != null && design.group != null) design.group.blocksRaycasts = on;
    }

    // ---- Frame + glass objects --------------------------------------------------------------

    private Target MakeTarget(RectTransform card)
    {
        var target = new Target { rect = card };
        target.group = card.GetComponent<CanvasGroup>();
        if (target.group == null) target.group = card.gameObject.AddComponent<CanvasGroup>();
        target.rig = BuildRig(card);
        RefreshFrame(target, MonitorFrameStyle.Get());
        return target;
    }

    private static Rig BuildRig(RectTransform card)
    {
        var rig = new Rig();
        rig.root = NewRect("Monitor (screen animation)", card);
        rig.root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true; // cards with layout groups
        Stretch(rig.root, 0f, 0f, 0f, 0f);

        rig.glass = NewRect("Glass", rig.root).gameObject.AddComponent<Image>();
        Stretch(rig.glass.rectTransform, 0f, 0f, 0f, 0f);
        rig.glass.raycastTarget = false;
        rig.glass.enabled = false;

        rig.noise = NewRect("Static", rig.root).gameObject.AddComponent<RawImage>();
        Stretch(rig.noise.rectTransform, 0f, 0f, 0f, 0f);
        rig.noise.raycastTarget = false;
        rig.noise.enabled = false;

        rig.frame = NewRect("Frame (your drawing)", rig.root).gameObject.AddComponent<Image>();
        rig.frame.raycastTarget = false;
        rig.frame.enabled = false;
        return rig;
    }

    private void RefreshFrames(MonitorFrameStyle style)
    {
        RefreshFrame(own, style);
        RefreshFrame(design, style);
    }

    private void RefreshFrame(Target target, MonitorFrameStyle style)
    {
        if (target == null || target.rig == null || target.rig.root == null) return;
        target.rig.root.SetAsLastSibling(); // above anything the screen added since
        Image frame = target.rig.frame;
        Sprite sprite = style.FrameFor(screen, out MonitorFrameStyle.Edges pad);
        frame.enabled = sprite != null;
        if (sprite == null) return;
        frame.sprite = sprite;
        frame.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        frame.color = style.frameColor;
        Stretch(frame.rectTransform, pad.left, pad.right, pad.top, pad.bottom);
    }

    // ---- UI Hub copy -----------------------------------------------------------------------

    private void ResolveDesign()
    {
        if (design != null && design.rect != null) return;
        design = null;
        Canvas canvas = GetComponentInParent<Canvas>(true);
        if (canvas == null) return;
        Canvas root = canvas.rootCanvas;

        IReadOnlyList<UiLayoutRuntime.LiveScreen> live = UiLayoutRuntime.Live;
        for (int i = 0; i < live.Count; i++)
        {
            UiLayoutRuntime.LiveScreen s = live[i];
            if (s == null || s.canvas != root || s.design == null || s.design.wrapper == null) continue;
            Transform wrapper = s.design.wrapper.transform;
            if (wrapper.childCount == 0) return;
            Transform copy = wrapper.GetChild(0);
            string path = PathFrom(root.transform, transform);
            Transform found = string.IsNullOrEmpty(path) ? copy : copy.Find(path);
            if (found == null) found = FindDeep(copy, name);
            if (found is RectTransform rect && rect != transform)
            {
                design = MakeTarget(rect);
                if (phase != Phase.Idle || parked)
                {
                    // Appeared mid-animation (or after closing): it is still in its place. Give it the
                    // look of this moment right away, before it is drawn.
                    Capture(design);
                    Pose(design, lastPose);
                    Glass(design, lastDark, lastStatic);
                    if (design.group != null) { design.group.alpha = parked ? 0f : 1f; design.group.blocksRaycasts = false; }
                }
            }
            return;
        }
    }

    private static string PathFrom(Transform root, Transform t)
    {
        if (t == root) return string.Empty;
        string path = t.name;
        for (Transform p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    private static Transform FindDeep(Transform parent, string wanted)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform c = parent.GetChild(i);
            if (c.name == wanted) return c;
            Transform deeper = FindDeep(c, wanted);
            if (deeper != null) return deeper;
        }
        return null;
    }

    // ---- Small helpers ---------------------------------------------------------------------

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    /// <summary>Fills the parent, sticking out by the given amounts.</summary>
    private static void Stretch(RectTransform rt, float left, float right, float top, float bottom)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(-left, -bottom);
        rt.offsetMax = new Vector2(right, top);
    }

    private static Texture2D codeNoise;

    /// <summary>Grey static, used when the style has no Noise picture.</summary>
    private static Texture2D CodeNoise()
    {
        if (codeNoise != null) return codeNoise;
        const int n = 128;
        codeNoise = new Texture2D(n, n, TextureFormat.RGBA32, false)
        { name = "Monitor Static (code)", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        var px = new Color32[n * n];
        var rng = new System.Random(1234);
        for (int i = 0; i < px.Length; i++)
        {
            byte v = (byte)rng.Next(256);
            px[i] = new Color32(v, v, v, 255);
        }
        codeNoise.SetPixels32(px);
        codeNoise.Apply();
        return codeNoise;
    }
}
