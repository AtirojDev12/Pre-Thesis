using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 24 Nov 2026: colours and fonts of the screens the game builds in code
/// (Lobby Shop, Lobby Storage, Tab player list). Nothing about their look is locked in code any more:
///
///   Assets/Resources/UI/Styles/Shop Style.asset
///   Assets/Resources/UI/Styles/Storage Style.asset
///   Assets/Resources/UI/Styles/Player List Style.asset
///
/// Click one of these in the Project window and change it in the Inspector.
/// Colours the code writes INSIDE words (prices, "Host", "Ready", "(empty)" ...) come from here too.
/// Changes show the next time the screen is opened (rows / words: at once).
///
/// A screen that has an editable copy in the GAME HUD prefab (Screens (made by the game)) shows that
/// copy instead: its fixed parts are coloured in the copy itself; this asset still decides the
/// colours the code writes (words, ping, the selected entry time ...).
/// No asset (deleted / renamed) = the screen's original colours.
/// LOCAL ONLY (UI).
/// </summary>
[CreateAssetMenu(menuName = "Pre-Thesis/Code Screen Style", fileName = "New Screen Style")]
public sealed class CodeScreenStyle : ScriptableObject
{
    public const string Folder = "UI/Styles/";

    [Header("Font (empty = TextMeshPro default font)")]
    [Tooltip("Font of every text on the screen.")]
    public TMP_FontAsset font;
    [Tooltip("Font of the title. Empty = the font above.")]
    public TMP_FontAsset titleFont;
    [Tooltip("Font Style of the title (Bold, Underline ...).")]
    public FontStyles titleStyle = FontStyles.Bold;

    [Header("Screen")]
    [Tooltip("Darkens the game behind the screen.")]
    public Color dim = new Color(0f, 0f, 0f, 0.7f);
    [Tooltip("The card / panel.")]
    public Color panel = Color.black;
    [Tooltip("Picture of the card (9-sliced sprites stretch nicely). Empty = plain colour.")]
    public Sprite panelSprite;
    [Tooltip("A frame drawn on top of the card edge, e.g. VCR_Frame_White. Empty = no frame.")]
    public Sprite frameSprite;
    public Color frame = Color.white;

    [Header("Lists and rows")]
    [Tooltip("Behind a scrolling list.")]
    public Color listBack = new Color(0f, 0f, 0f, 0f);
    public Sprite listSprite;
    public Color row = Color.gray;
    [Tooltip("Your own row (Tab list).")]
    public Color myRow = Color.gray;
    public Sprite rowSprite;

    [Header("Text")]
    public Color title = Color.white;
    public Color text = Color.white;
    [Tooltip("Small notes, headers, \"(You)\", \"Not ready\" ...")]
    public Color muted = Color.gray;
    [Tooltip("Even quieter: \"(empty)\" slots.")]
    public Color faint = Color.gray;
    [Tooltip("Highlights: \"(Host)\", the chosen entry time ...")]
    public Color accent = Color.yellow;
    [Tooltip("Numbers that matter: currency, prices, the entry time.")]
    public Color value = Color.yellow;
    [Tooltip("Good news: OWNED, FREE, Ready, low ping, \"Bought!\".")]
    public Color good = Color.green;
    [Tooltip("Middle ping, SPARE items.")]
    public Color warning = new Color(1f, 0.6f, 0.2f);
    [Tooltip("Bad news: high ping, Muted, \"Not enough currency\".")]
    public Color bad = Color.red;

    [Header("Buttons")]
    [Tooltip("Picture of every button. Empty = plain colour.")]
    public Sprite buttonSprite;
    public Color button = Color.gray;
    public Color buttonText = Color.white;
    [Tooltip("Words colour while the mouse is on the button (VCR: blue on a white bar). Same as Button Text = no change.")]
    public Color buttonHoverText = Color.white;
    [Tooltip("These multiply the button colour (Unity Color Tint). Above 1 = brighter. VCR look: Normal alpha 0 = no bar until the mouse is on it.")]
    [ColorUsage(true, true)] public Color buttonNormal = Color.white;
    [ColorUsage(true, true)] public Color buttonHighlighted = new Color(0.9607843f, 0.9607843f, 0.9607843f, 1f);
    [ColorUsage(true, true)] public Color buttonPressed = new Color(0.7843137f, 0.7843137f, 0.7843137f, 1f);
    [ColorUsage(true, true)] public Color buttonSelected = new Color(0.9607843f, 0.9607843f, 0.9607843f, 1f);
    public Color buttonDisabled = new Color(0.7843137f, 0.7843137f, 0.7843137f, 0.5019608f);
    [Tooltip("A thin frame around every button (uses Frame Sprite above). Alpha 0 = no frame.")]
    public Color buttonFrame = new Color(1f, 1f, 1f, 0f);
    [Tooltip("Main buttons: Ready, Start.")]
    public Color primaryButton = Color.yellow;
    public Color primaryButtonText = Color.black;
    [Tooltip("Kick.")]
    public Color dangerButton = Color.red;
    public Color dangerButtonText = Color.white;

    [Header("Sliders and scrollbars")]
    public Color sliderBack = Color.black;
    public Color sliderFill = Color.yellow;
    public Color sliderHandle = Color.white;
    public Color scrollbarBack = new Color(0f, 0f, 0f, 0.35f);
    public Color scrollbarHandle = Color.yellow;

    // ---- Loading ------------------------------------------------------------------------

    private static readonly Dictionary<string, CodeScreenStyle> cache = new Dictionary<string, CodeScreenStyle>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cache.Clear();

    /// <summary>The asset Resources/UI/Styles/&lt;name&gt;, or (no asset) the screen's own original colours.</summary>
    public static CodeScreenStyle Load(string name, System.Action<CodeScreenStyle> originalColours)
    {
        if (cache.TryGetValue(name, out CodeScreenStyle style) && style != null) return style;
        style = Resources.Load<CodeScreenStyle>(Folder + name);
        if (style == null)
        {
            style = CreateInstance<CodeScreenStyle>();
            style.name = name + " (built in)";
            style.hideFlags = HideFlags.DontSave;
            originalColours?.Invoke(style);
        }
        cache[name] = style;
        return style;
    }

    // ---- Words ----------------------------------------------------------------------------

    /// <summary>"#RRGGBB" (or "#RRGGBBAA" when see-through) for a rich-text colour tag.</summary>
    public static string Hex(Color c) =>
        "#" + (c.a >= 0.999f ? ColorUtility.ToHtmlStringRGB(c) : ColorUtility.ToHtmlStringRGBA(c));

    /// <summary>The words in this colour (rich text).</summary>
    public static string Paint(string words, Color c) => "<color=" + Hex(c) + ">" + words + "</color>";

    // ---- Parts ----------------------------------------------------------------------------

    /// <summary>Font (if set) and colour of a text.</summary>
    public void StyleText(TMP_Text t, Color colour, bool isTitle = false)
    {
        if (t == null) return;
        TMP_FontAsset f = isTitle && titleFont != null ? titleFont : font;
        if (f != null) t.font = f;
        else if (t.font == null && TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
        if (isTitle) t.fontStyle = titleStyle;
        t.color = colour;
    }

    /// <summary>Colour and (optional) picture of an Image. Pictures with borders are 9-sliced.</summary>
    public static void PaintImage(Image image, Color colour, Sprite sprite)
    {
        if (image == null) return;
        image.color = colour;
        if (sprite == null) return;
        image.sprite = sprite;
        image.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
    }

    /// <summary>The card: colour, picture and (optional) frame on its edge.</summary>
    public void StyleCard(Image card)
    {
        PaintImage(card, panel, panelSprite);
        if (card != null) AddFrame(card.transform, frame);
    }

    /// <summary>A frame (Frame Sprite) on the edge of a part, above it and below its children.</summary>
    private void AddFrame(Transform parent, Color colour)
    {
        if (frameSprite == null || colour.a <= 0f) return;
        var go = new GameObject("Frame", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.transform.SetAsFirstSibling();
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        Image image = go.GetComponent<Image>();
        image.raycastTarget = false;
        PaintImage(image, colour, frameSprite);
    }

    public enum ButtonKind { Normal, Primary, Danger }

    /// <summary>Colours, picture, tint and hover words of a button.</summary>
    public void StyleButton(Button b, TMP_Text label, ButtonKind kind = ButtonKind.Normal)
    {
        if (b == null) return;
        Color bg = kind == ButtonKind.Primary ? primaryButton : kind == ButtonKind.Danger ? dangerButton : button;
        Color words = kind == ButtonKind.Primary ? primaryButtonText : kind == ButtonKind.Danger ? dangerButtonText : buttonText;

        PaintImage(b.targetGraphic as Image, bg, buttonSprite);
        ColorBlock colors = b.colors;
        colors.normalColor = buttonNormal;
        colors.highlightedColor = buttonHighlighted;
        colors.pressedColor = buttonPressed;
        colors.selectedColor = buttonSelected;
        colors.disabledColor = buttonDisabled;
        b.colors = colors;
        AddFrame(b.transform, buttonFrame);

        if (label == null) return;
        StyleText(label, words);
        if (buttonHoverText != buttonText) // the hover colour is in use: every kind of button gets it
        {
            UiButtonTextColor swap = b.GetComponent<UiButtonTextColor>();
            if (swap == null) swap = b.gameObject.AddComponent<UiButtonTextColor>();
            swap.Set(label, words, buttonHoverText);
        }
    }
}
