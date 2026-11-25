using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 25 Nov 2026: settings of the "lift the monitor up" animation (design B) and the MONITOR FRAME
/// drawn around the screens: Lobby Shop, Lobby Storage, Tab player list and the Esc menu.
///
///   Assets/Resources/UI/Styles/Monitor Frame.asset   (click it, change it in the Inspector)
///
/// YOUR FRAME DRAWING
///   1. Draw the monitor body as a PNG with a SEE-THROUGH hole where the screen is.
///   2. Import it as Sprite (2D and UI). If the frame has to stretch to screens of different sizes,
///      open the Sprite Editor and set the Border (9-slice) on the bezel so the corners don't stretch.
///   3. Drag it into "Frame" below. Frame Padding = how far the frame sticks out past the screen
///      (left / right / top / bottom, in 1920x1080 canvas units). A thick bottom = a monitor chin.
///   4. A different drawing for one screen: add it to "Screens" (names: Shop, Storage, Player List,
///      Pause, Pause Settings). "No Frame" there = that screen has no frame.
/// The frame is drawn ON TOP of the screen (that's why the hole must be see-through) and moves with it.
/// Empty Frame = no frame; the animation still plays.
///
/// No asset = the values written below. LOCAL ONLY (UI).
/// </summary>
[CreateAssetMenu(menuName = "Pre-Thesis/Monitor Frame (screen open animation)", fileName = "Monitor Frame")]
public sealed class MonitorFrameStyle : ScriptableObject
{
    public const string ResourcePath = "UI/Styles/Monitor Frame";

    [System.Serializable]
    public struct Edges
    {
        public float left, right, top, bottom;
        public Edges(float left, float right, float top, float bottom)
        { this.left = left; this.right = right; this.top = top; this.bottom = bottom; }
    }

    [System.Serializable]
    public sealed class ScreenFrame
    {
        [Tooltip("Shop, Storage, Player List, Pause or Pause Settings.")]
        public string screen = "Shop";
        [Tooltip("This screen has no frame at all.")]
        public bool noFrame;
        [Tooltip("Empty = the main Frame.")]
        public Sprite frame;
        [Tooltip("Off = the main Frame Padding.")]
        public bool ownPadding;
        public Edges padding = new Edges(40f, 40f, 40f, 70f);
    }

    [Header("Frame (your monitor drawing, see the notes at the top of MonitorFrameStyle.cs)")]
    [Tooltip("The monitor body with a see-through screen hole. Empty = no frame.")]
    public Sprite frame;
    public Color frameColor = Color.white;
    [Tooltip("How far the frame sticks out past the screen, in canvas units (1920x1080).")]
    public Edges framePadding = new Edges(40f, 40f, 40f, 70f);
    [Tooltip("A different frame (or none) for one screen.")]
    public List<ScreenFrame> screens = new List<ScreenFrame>();

    [Header("Screen glass (the monitor while it is still off)")]
    public Color glass = new Color(0.02f, 0.025f, 0.05f, 1f);
    [Tooltip("Static when it switches on / off. Empty = made by code.")]
    public Texture noise;
    [Range(0f, 1f)] public float noiseStrength = 0.55f;
    [Tooltip("Size of one grain dot, in canvas units.")]
    public float grainSize = 3f;

    [Header("Open 1: lift it up")]
    [Tooltip("Off = screens open and close at once (the frame is still shown).")]
    public bool animate = true;
    public float liftSeconds = 0.32f;
    [Tooltip("How far below its place the screen starts, in canvas units (1080 = one screen height).")]
    public float liftDistance = 1100f;
    [Tooltip("0 = down, 1 = in place. Above 1 near the end = a small bounce.")]
    public AnimationCurve liftCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 2.6f), new Keyframe(0.62f, 1.05f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));
    [Tooltip("Height at the start (1 = normal). Below 1 looks like the monitor is tilted away.")]
    [Range(0.5f, 1f)] public float startHeight = 0.82f;
    [Range(0.5f, 1f)] public float startWidth = 0.94f;
    [Tooltip("Turn at the start, degrees (handheld sway).")]
    public float startTilt = -4f;

    [Header("Open 2: switch on")]
    public float bootSeconds = 0.24f;
    [Tooltip("Darkness of the glass while it switches on (1 = off, 0 = picture). Up and down = flicker.")]
    public AnimationCurve bootGlass = Linear(0f, 1f, 0.15f, 0f, 0.3f, 0.85f, 0.45f, 0f, 0.6f, 0.5f, 0.75f, 0f, 1f, 0f);

    [Header("Close: switch off, then lower it")]
    public float offSeconds = 0.09f;
    public float lowerSeconds = 0.26f;
    [Tooltip("0 = in place, 1 = down.")]
    public AnimationCurve lowerCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.25f, -0.04f, 0f, 0f), new Keyframe(1f, 1f, 2.2f, 0f));

    [Header("Darkening behind the screen")]
    [Tooltip("The dark layer behind the screen fades in / out with it.")]
    public bool fadeDim = true;

    // ---- Use ------------------------------------------------------------------------------

    private static MonitorFrameStyle cached;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cached = null;

    /// <summary>The asset in Resources, or (no asset) the values above.</summary>
    public static MonitorFrameStyle Get()
    {
        if (cached != null) return cached;
        cached = Resources.Load<MonitorFrameStyle>(ResourcePath);
        if (cached == null)
        {
            cached = CreateInstance<MonitorFrameStyle>();
            cached.name = "Monitor Frame (built in)";
            cached.hideFlags = HideFlags.DontSave;
        }
        return cached;
    }

    /// <summary>The frame picture and padding of one screen (null sprite = no frame).</summary>
    public Sprite FrameFor(string screen, out Edges padding)
    {
        padding = framePadding;
        for (int i = 0; i < screens.Count; i++)
        {
            ScreenFrame s = screens[i];
            if (s == null || s.screen != screen) continue;
            if (s.noFrame) return null;
            if (s.ownPadding) padding = s.padding;
            return s.frame != null ? s.frame : frame;
        }
        return frame;
    }

    /// <summary>A curve with straight lines between (time, value) pairs.</summary>
    private static AnimationCurve Linear(params float[] tv)
    {
        int n = tv.Length / 2;
        var keys = new Keyframe[n];
        for (int i = 0; i < n; i++)
        {
            float t = tv[i * 2], v = tv[i * 2 + 1];
            float inT = i > 0 ? (v - tv[i * 2 - 1]) / (t - tv[i * 2 - 2]) : 0f;
            float outT = i < n - 1 ? (tv[i * 2 + 3] - v) / (tv[i * 2 + 2] - t) : 0f;
            keys[i] = new Keyframe(t, v, inT, outT);
        }
        return new AnimationCurve(keys);
    }
}
