using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 27 Nov 2026: a full-screen animated jumpscare, played on ONE screen by
/// RuleFx.ShowJumpscare(animation, ...). Nothing here changes game state.
///
/// Two ways to animate (the Clip wins when both are set):
///   - Clip:   an Animation Clip that changes an Image's sprite (m_Sprite).
///             The Inspector button "Make clip from frames" builds it from the list.
///   - Frames: the list below, played in order (Seconds per frame, or Frames Per Second).
///
/// HOW TO MAKE ONE (all in the Inspector)
///   1. Project window: right click > Create > Pre-Thesis > Jumpscare Animation.
///   2. Drag the folder with the frame pictures into "Frames folder", press Load frames.
///   3. Press "Make clip from frames". Tune shake / sound, check it with the Preview.
///   4. Drag this file into a Rule Ghost's "Jumpscare Animation" slot.
/// </summary>
[CreateAssetMenu(menuName = "Pre-Thesis/Jumpscare Animation", fileName = "New Jumpscare Animation", order = 0)]
public sealed class JumpscareAnimation : ScriptableObject
{
    public enum Fit
    {
        [Tooltip("Covers the whole screen; edges of the picture may be cut on non-16:9 screens.")]
        FillScreen,
        [Tooltip("Whole picture visible; Background colour shows at the sides if the shape differs.")]
        FitInside,
        [Tooltip("Squashed to the screen shape.")]
        Stretch,
    }

    [System.Serializable]
    public struct Frame
    {
        public Sprite sprite;
        [Tooltip("How long this frame stays on screen. 0 = use Frames Per Second.")]
        [Min(0f)] public float seconds;
    }

    [Tooltip("The Animation Clip to play (changes an Image's sprite). Empty = play the Frames list.\n" +
             "Use the 'Make clip from frames' button to build it from the list.")]
    public AnimationClip clip;

    [Tooltip("Top plays first. Used when there is no Clip, and as the source for 'Make clip from frames'.")]
    public List<Frame> frames = new List<Frame>();

    [Header("Timing")]
    [Tooltip("Speed for every frame whose Seconds is 0.")]
    [Range(1f, 60f)] public float framesPerSecond = 12f;
    [Tooltip("Extra time the LAST frame stays (Frames list, and built into the clip by 'Make clip from frames').")]
    [Min(0f)] public float holdLastFrameSeconds = 0.6f;
    [Tooltip("Fade-out at the end (on the last frame).")]
    [Min(0f)] public float fadeOutSeconds = 0.15f;

    [Header("Look")]
    public Fit fit = Fit.FillScreen;
    [Tooltip("Behind the pictures (visible with Fit Inside, or while fading).")]
    public Color backgroundColor = Color.black;
    [Tooltip("Screen shake strength in pixels (1920x1080 screen). 0 = no shake.")]
    [Range(0f, 60f)] public float shakePixels = 14f;
    [Tooltip("Seconds after the start when the shake begins.")]
    [Min(0f)] public float shakeFromSeconds = 0.75f;

    [Header("Sound")]
    [Tooltip("Played once, 2D, on the SFX volume slider. Empty = silent.")]
    public AudioClip sound;
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Seconds after the start when the sound plays (0 = right away).")]
    [Min(0f)] public float soundAtSeconds = 0.75f;

    public int FrameCount => frames != null ? frames.Count : 0;

    public bool HasFrames
    {
        get
        {
            if (frames == null) return false;
            for (int i = 0; i < frames.Count; i++) if (frames[i].sprite != null) return true;
            return false;
        }
    }

    /// <summary>True when there is something to show (a clip or at least one frame picture).</summary>
    public bool CanPlay => clip != null || HasFrames;

    /// <summary>How long frame <paramref name="index"/> stays (its own Seconds, or 1 / Frames Per Second).</summary>
    public float FrameSeconds(int index)
    {
        if (index < 0 || index >= FrameCount) return 0f;
        float s = frames[index].seconds;
        return s > 0f ? s : 1f / Mathf.Max(1f, framesPerSecond);
    }

    /// <summary>All frames of the list played once (no hold, no fade).</summary>
    public float FramesSeconds
    {
        get
        {
            float total = 0f;
            for (int i = 0; i < FrameCount; i++) total += FrameSeconds(i);
            return total;
        }
    }

    /// <summary>Time before the fade: the clip length, or the frames + hold.</summary>
    public float PlaySeconds => clip != null ? clip.length : FramesSeconds + Mathf.Max(0f, holdLastFrameSeconds);

    public float TotalSeconds => PlaySeconds + Mathf.Max(0f, fadeOutSeconds);

    /// <summary>Frames list: which frame shows <paramref name="time"/> seconds after the start (the last one after the end).</summary>
    public int FrameIndexAt(float time)
    {
        int count = FrameCount;
        if (count == 0) return -1;
        float t = 0f;
        for (int i = 0; i < count; i++)
        {
            t += FrameSeconds(i);
            if (time < t) return i;
        }
        return count - 1;
    }

    /// <summary>Frames list: the picture of frame <paramref name="index"/>, or of the closest earlier frame that has one.</summary>
    public Sprite SpriteAt(int index)
    {
        for (int i = Mathf.Min(index, FrameCount - 1); i >= 0; i--)
            if (frames[i].sprite != null) return frames[i].sprite;
        return null;
    }
}
