using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 24 Nov 2026 (VCR theme). Page change in the main menu = the tape being fast-forwarded or rewound.
///
///   Into a sub page (Create Room, Room Browser, Settings)  -> "FF"  with two triangles (>>)
///   Back to Main                                           -> "REW" with two triangles (<<)
///
/// What happens (all in real time, so it also works while the game is paused):
///   1. One sound plays (Sound Library id, or the clip set here).
///   2. For tapeTime seconds the old page jumps sideways in the tape direction and white
///      tracking lines tear across the screen. The page's "Play State" OSD shows FF / REW.
///   3. The pages are switched (MainMenuController does the switch).
///   4. The new page shows FF / REW for labelHoldTime seconds, then "PLAY" again.
/// Clicks are blocked while it runs. A new page change while one is running finishes the old
/// one at once first.
///
/// Setup: lives on "Main Menu UI/VCR Tape Transition" (full-screen, see-through) and is put
/// into MainMenuController's "Page Transition" slot. Empty slot = pages switch instantly.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class VcrTapeTransition : MonoBehaviour
{
    public enum Label { Play, FastForward, Rewind }

    [Header("Timing (seconds)")]
    [Tooltip("How long the old page tears before the cut.")]
    [SerializeField, Min(0f)] private float tapeTime = 0.3f;
    [Tooltip("How long FF / REW stays on the new page before PLAY comes back.")]
    [SerializeField, Min(0f)] private float labelHoldTime = 0.22f;
    [Tooltip("Time between two jumps / new tear lines.")]
    [SerializeField, Min(0.01f)] private float jitterStep = 0.045f;

    [Header("Look")]
    [SerializeField, Range(0, 12)] private int tearLines = 6;
    [SerializeField] private Color tearColor = new Color(1f, 1f, 1f, 0.22f);
    [Tooltip("Largest sideways jump of the old page, in canvas pixels.")]
    [SerializeField, Min(0f)] private float maxShift = 40f;
    [Tooltip("Largest extra horizontal stretch of the old page (0.03 = 3%).")]
    [SerializeField, Range(0f, 0.2f)] private float maxStretch = 0.03f;

    [Header("Sound (one sound for FF and REW)")]
    [Tooltip("Id in the Sound Library (Resources/Audio/SoundLibrary). Used when no clip is set below.")]
    [SerializeField] private string soundId = "UI_Tape";
    [Tooltip("Optional: play this clip directly instead of the Sound Library id.")]
    [SerializeField] private AudioClip soundClip;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.8f;

    [Header("OSD (child of every page)")]
    [SerializeField] private string playStateName = "Play State";
    [SerializeField] private string playIconName = "Play Icon";
    [SerializeField] private string playText = "PLAY";
    [SerializeField] private string fastForwardText = "FF";
    [SerializeField] private string rewindText = "REW";
    [Tooltip("How far the second triangle of >> / << sits from the first, in pixels.")]
    [SerializeField] private float doubleIconGap = 13f;

    public bool IsPlaying => routine != null;

    private const string SecondIconName = "Play Icon (2)";

    private Image blocker;
    private RectTransform[] tears = Array.Empty<RectTransform>();
    private Coroutine routine;

    // what a running transition has to put back if it is cut short
    private Action pendingSwap;
    private GameObject runFrom, runTo;
    private RectTransform movedPage;
    private Vector2 movedPagePos;
    private Vector3 movedPageScale;

    private readonly Dictionary<TMP_Text, Vector4> baseMargins = new Dictionary<TMP_Text, Vector4>();

    private void Awake()
    {
        blocker = GetComponent<Image>();
        if (blocker == null) blocker = gameObject.AddComponent<Image>();
        blocker.color = new Color(1f, 1f, 1f, 0f);
        blocker.raycastTarget = false;
        BuildTears();
    }

    private void OnDisable() => Finish();

    /// <summary>
    /// Plays the tape effect on <paramref name="from"/>, then calls <paramref name="swap"/>
    /// (which shows <paramref name="to"/>). forward = FF, otherwise REW.
    /// </summary>
    public void Play(GameObject from, GameObject to, bool forward, Action swap)
    {
        Finish();
        if (!isActiveAndEnabled || from == null || !from.activeInHierarchy || tapeTime <= 0f)
        {
            swap?.Invoke();
            return;
        }
        pendingSwap = swap;
        runFrom = from;
        runTo = to;
        routine = StartCoroutine(Run(from, to, forward));
    }

    /// <summary>Ends a running transition at once (the page switch still happens).</summary>
    public void Finish()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        RestorePage();
        ShowTears(false);
        SetLabel(runFrom, Label.Play);
        Action swap = pendingSwap;
        pendingSwap = null;
        swap?.Invoke();
        SetLabel(runTo, Label.Play);
        runFrom = runTo = null;
        if (blocker != null) blocker.raycastTarget = false;
    }

    private IEnumerator Run(GameObject from, GameObject to, bool forward)
    {
        transform.SetAsLastSibling();
        blocker.raycastTarget = true; // no clicks while the tape runs
        PlaySound();

        Label label = forward ? Label.FastForward : Label.Rewind;
        float dir = forward ? 1f : -1f;
        SetLabel(from, label);

        movedPage = from.transform as RectTransform;
        if (movedPage != null)
        {
            movedPagePos = movedPage.anchoredPosition;
            movedPageScale = movedPage.localScale;
        }

        float end = Time.unscaledTime + tapeTime;
        while (Time.unscaledTime < end)
        {
            ShuffleTears();
            if (movedPage != null)
            {
                movedPage.anchoredPosition = movedPagePos + new Vector2(dir * UnityEngine.Random.Range(0.2f, 1f) * maxShift, 0f);
                movedPage.localScale = new Vector3(movedPageScale.x * (1f + UnityEngine.Random.Range(0f, maxStretch)), movedPageScale.y, movedPageScale.z);
            }
            yield return new WaitForSecondsRealtime(jitterStep);
        }

        RestorePage();
        ShowTears(false);
        SetLabel(from, Label.Play);

        Action swap = pendingSwap;
        pendingSwap = null;
        swap?.Invoke(); // the new page appears here

        SetLabel(to, label);
        if (labelHoldTime > 0f) yield return new WaitForSecondsRealtime(labelHoldTime);
        SetLabel(to, Label.Play);

        runFrom = runTo = null;
        blocker.raycastTarget = false;
        routine = null;
    }

    // ---- Sound ------------------------------------------------------------------

    private void PlaySound()
    {
        AudioManager audio = AudioManager.Instance;
        if (audio == null) return;
        if (soundClip != null)
            audio.PlayClip(soundClip, SoundCategory.Sfx, soundVolume, Vector3.zero, false, 1f, 15f, default);
        else if (!string.IsNullOrEmpty(soundId))
            audio.PlayUI(soundId);
    }

    // ---- Old page jump --------------------------------------------------------------

    private void RestorePage()
    {
        if (movedPage != null)
        {
            movedPage.anchoredPosition = movedPagePos;
            movedPage.localScale = movedPageScale;
        }
        movedPage = null;
    }

    // ---- Tear lines -------------------------------------------------------------------

    private void BuildTears()
    {
        tears = new RectTransform[tearLines];
        for (int i = 0; i < tearLines; i++)
        {
            var go = new GameObject("Tear " + (i + 1), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.color = tearColor;
            image.raycastTarget = false;
            go.SetActive(false);
            tears[i] = rt;
        }
    }

    private void ShuffleTears()
    {
        foreach (RectTransform rt in tears)
        {
            if (rt == null) continue;
            float y = UnityEngine.Random.Range(0f, 0.95f);
            float h = UnityEngine.Random.Range(0.015f, 0.06f);
            rt.anchorMin = new Vector2(0f, y);
            rt.anchorMax = new Vector2(1f, Mathf.Min(1f, y + h));
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.gameObject.SetActive(true);
        }
    }

    private void ShowTears(bool on)
    {
        foreach (RectTransform rt in tears)
            if (rt != null) rt.gameObject.SetActive(on);
    }

    // ---- OSD label: "> PLAY" / ">> FF" / "<< REW" --------------------------------------

    private void SetLabel(GameObject page, Label label)
    {
        if (page == null) return;
        Transform state = page.transform.Find(playStateName);
        if (state == null) return;

        TMP_Text text = state.GetComponent<TMP_Text>();
        if (text != null)
        {
            if (!baseMargins.TryGetValue(text, out Vector4 margin))
            {
                margin = text.margin;
                baseMargins[text] = margin;
            }
            text.text = label == Label.Play ? playText : label == Label.FastForward ? fastForwardText : rewindText;
            text.margin = label == Label.Play ? margin : margin + new Vector4(doubleIconGap, 0f, 0f, 0f);
        }

        var icon = state.Find(playIconName) as RectTransform;
        if (icon == null) return;

        var second = state.Find(SecondIconName) as RectTransform;
        if (second == null && label != Label.Play)
        {
            second = Instantiate(icon.gameObject, state).transform as RectTransform;
            second.name = SecondIconName;
        }

        bool rewind = label == Label.Rewind;
        float width = icon.rect.width;
        // The icon's pivot is on its left edge: when flipped, move it right by its width.
        PlaceIcon(icon, rewind ? width : 0f, rewind);
        if (second != null)
        {
            second.gameObject.SetActive(label != Label.Play);
            PlaceIcon(second, doubleIconGap + (rewind ? width : 0f), rewind);
        }
    }

    private static void PlaceIcon(RectTransform icon, float x, bool flipped)
    {
        icon.anchoredPosition = new Vector2(x, icon.anchoredPosition.y);
        Vector3 s = icon.localScale;
        icon.localScale = new Vector3(flipped ? -Mathf.Abs(s.x) : Mathf.Abs(s.x), s.y, s.z);
    }
}
