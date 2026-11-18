using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 16 Nov 2026 (Mr.k): one STATUS BAR that lives inside the GAME HUD prefab
/// (mic noise, game noise, flashlight battery, throw charge, task progress, walkie battery).
///
/// FOR DESIGNERS (open the GAME HUD prefab, or use the UI Hub)
///   - Frame = the picture behind / around the bar. Fill = the part that grows.
///     They are two separate Images: replace each picture on its own.
///   - Move, resize, restyle or re-parent the bar and its parts freely.
///     Only this component's slots must stay filled.
///   - Fill picture, Image Type "Filled": the picture is CUT as the bar goes down.
///     Any other Image Type: the picture is SQUEEZED.
///   - Game Colours Fill off = the fill keeps your own colour.
///   - Marker, Label and Hint are optional: leave a slot empty and the game skips it.
///
/// FOR PROGRAMMERS
///   The game finds a bar by its Bar ID (<see cref="Find"/>) and only sets values.
///   No bar in the GAME HUD = the game falls back to its old code-built bar.
///   The object is switched on / off by the game; its saved active state does not matter.
///   LOCAL ONLY (UI): nothing here is networked.
/// </summary>
[AddComponentMenu("Pre-Thesis/HUD Bar (frame + fill)")]
[DisallowMultipleComponent]
public sealed class HudBar : MonoBehaviour
{
    // Bar IDs the game looks for.
    public const string MicNoiseHorizontal = "mic_noise_h";
    public const string MicNoiseVertical = "mic_noise_v";
    public const string GameNoiseHorizontal = "game_noise_h";
    public const string GameNoiseVertical = "game_noise_v";
    public const string Flashlight = "flashlight";
    public const string ThrowCharge = "throw";
    public const string TaskProgress = "task";
    public const string WalkieBattery = "walkie_battery";

    public enum Direction { LeftToRight, BottomToTop }

    [Tooltip("The game finds this bar by this ID. Do not change it.")]
    [SerializeField] private string barId = "";

    [Header("Pictures (replace each one on its own)")]
    [Tooltip("The picture behind / around the bar.")]
    [SerializeField] private Image frame;
    [Tooltip("The part that grows and shrinks.")]
    [SerializeField] private Image fill;
    [Tooltip("Optional. A line on the bar (the LOUD line of the noise bars).")]
    [SerializeField] private RectTransform marker;

    [Header("Texts (optional)")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private TMP_Text hint;

    [Header("How it works")]
    [SerializeField] private Direction direction = Direction.LeftToRight;
    [Tooltip("On = the game colours the fill (red when loud / almost empty). Off = the fill keeps your colour.")]
    [SerializeField] private bool gameColoursFill = true;
    [Tooltip("Draw order. The sanity dark edges are 44, the Esc menu is 50.")]
    [SerializeField] private int sortingOrder = 51;
    [Tooltip("Off (as before) = stays on screen while a full-screen panel is open (shop, storage, results, work sheet). On = hidden with the rest of the HUD.")]
    [SerializeField] private bool hideWithRestOfHud;

    private static HudBar[] all;
    private static PersistentHUD allFrom;

    private bool fullKnown;
    private bool sizeMode;
    private Vector2 fullAnchorMin, fullAnchorMax, fullSize;
    private float shownValue = -1f;
    private string shownLabel, shownHint;

    public string BarId => barId;
    public Image Frame => frame;
    public Image Fill => fill;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        all = null;
        allFrom = null;
    }

    /// <summary>The bar with this ID inside the live GAME HUD (also when it is switched off), or null.</summary>
    public static HudBar Find(string id)
    {
        PersistentHUD hud = PersistentHUD.Instance;
        if (hud == null) return null;
        if (all == null || allFrom != hud)
        {
            all = hud.GetComponentsInChildren<HudBar>(true);
            allFrom = hud;
        }
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].barId == id && all[i].fill != null) return all[i];
        return null;
    }

    private void Awake()
    {
        // The GAME HUD canvas draws at order 0, under the sanity dark edges. A nested canvas
        // lifts this bar to where the old code-built bar was (same trick as SanityBarUI).
        if (!TryGetComponent(out Canvas canvas)) canvas = gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;

        // The GAME HUD fades out as a whole while a full-screen panel is open (PersistentHUD.PushHidden).
        // The old code-made version was its own canvas and stayed visible: keep that unless asked otherwise.
        if (!TryGetComponent(out CanvasGroup group)) group = gameObject.AddComponent<CanvasGroup>();
        group.ignoreParentGroups = !hideWithRestOfHud;
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    /// <summary>Switch the whole bar on / off.</summary>
    public void Show(bool show)
    {
        if (gameObject.activeSelf != show) gameObject.SetActive(show);
    }

    /// <summary>How full the bar is, 0..1.</summary>
    public void Set(float value)
    {
        value = Mathf.Clamp01(value);
        if (fill == null || Mathf.Approximately(value, shownValue)) return;
        shownValue = value;

        if (fill.type == Image.Type.Filled && fill.sprite != null) { fill.fillAmount = value; return; }

        // Not a Filled image: change the fill's rectangle. Its size in the prefab = a full bar.
        RectTransform rt = fill.rectTransform;
        int axis = direction == Direction.LeftToRight ? 0 : 1;
        if (!fullKnown)
        {
            fullKnown = true;
            fullAnchorMin = rt.anchorMin;
            fullAnchorMax = rt.anchorMax;
            fullSize = rt.sizeDelta;
            sizeMode = Mathf.Approximately(fullAnchorMin[axis], fullAnchorMax[axis]);
        }
        if (sizeMode)
        {
            Vector2 size = rt.sizeDelta;
            size[axis] = fullSize[axis] * value;
            rt.sizeDelta = size;
        }
        else
        {
            Vector2 max = rt.anchorMax;
            max[axis] = Mathf.Lerp(fullAnchorMin[axis], fullAnchorMax[axis], value);
            rt.anchorMax = max;
        }
    }

    /// <summary>Colour from the game (loud / low). Ignored when "Game Colours Fill" is off.</summary>
    public void SetFillColour(Color colour)
    {
        if (gameColoursFill && fill != null && fill.color != colour) fill.color = colour;
    }

    /// <summary>Where the optional marker line sits along the bar, 0..1.</summary>
    public void SetMarker(float value)
    {
        if (marker == null) return;
        value = Mathf.Clamp01(value);
        Vector2 min = marker.anchorMin, max = marker.anchorMax;
        int axis = direction == Direction.LeftToRight ? 0 : 1;
        if (Mathf.Approximately(min[axis], value) && Mathf.Approximately(max[axis], value)) return;
        min[axis] = max[axis] = value;
        marker.anchorMin = min;
        marker.anchorMax = max;
    }

    public void SetLabel(string text)
    {
        if (label == null || text == shownLabel) return;
        shownLabel = text;
        label.text = text;
    }

    public void SetHint(string text)
    {
        if (hint == null || text == shownHint) return;
        shownHint = text;
        hint.text = text;
    }

    public void SetHintAlpha(float alpha)
    {
        if (hint != null) hint.alpha = alpha;
    }

#if UNITY_EDITOR
    /// <summary>Editor tool (GameHudSetup): fill the slots of a new bar.</summary>
    public void EditorSetup(string id, Image frameImage, Image fillImage, RectTransform markerLine, TMP_Text labelText, TMP_Text hintText,
        Direction fillDirection, int order)
    {
        barId = id;
        frame = frameImage;
        fill = fillImage;
        marker = markerLine;
        label = labelText;
        hint = hintText;
        direction = fillDirection;
        sortingOrder = order;
    }
#endif
}
