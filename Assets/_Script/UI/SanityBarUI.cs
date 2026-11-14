using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sanity bar inside the GAME HUD canvas (next to the Stamina Bar), for the player owned
/// by this machine. This is the ONLY sanity bar (SanityEffects no longer builds one);
/// the vignette, eat/feed progress and jumpscare still come from SanityEffects.
/// Move / resize / restyle it freely in the GAME HUD prefab.
/// </summary>
public sealed class SanityBarUI : MonoBehaviour
{
    /// <summary>The enabled HUD sanity bar, if any (SanityEffects checks this).</summary>
    public static SanityBarUI Active { get; private set; }

    [SerializeField] private Slider sanitySlider;
    [SerializeField] private Image fillImage;
    [Tooltip("Optional. Shows 'SANITY 75' plus chanting / cannot-recover hints.")]
    [SerializeField] private TMP_Text sanityText;
    [SerializeField] private bool hideWhenUnbound = true;
    [Tooltip("Draw order of this bar. Must be above the sanity vignette (44) so the dark screen edges " +
             "never cover the bar, and below the jumpscare flash (300).")]
    [SerializeField] private int sortingOrder = 45;

    [Header("Sanity Colors")]
    [Tooltip("Colour per sanity level. Disable to keep the Fill Image's own Color.")]
    [SerializeField] private bool useLevelColors = true;
    [SerializeField] private Color calmColor = new Color(0.55f, 0.8f, 1f);
    [SerializeField] private Color shakenColor = new Color(1f, 0.8f, 0.3f);
    [SerializeField] private Color breakingColor = new Color(1f, 0.25f, 0.25f);
    [Tooltip("Pulse the fill while Breaking.")]
    [SerializeField] private bool pulseWhenBreaking = true;

    private CanvasGroup fadeGroup;
    private string shownText;
    private bool? shownVisible;

    private void Awake()
    {
        // The GAME HUD canvas draws at order 0, under SanityEffects' vignette (44).
        // A nested canvas lifts just this bar above the vignette.
        if (!TryGetComponent(out Canvas canvas)) canvas = gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;
    }

    private void OnEnable() => Active = this;

    private void OnDisable()
    {
        if (Active == this) Active = null;
    }

    private void LateUpdate()
    {
        PlayerSanity sanity = PlayerSanity.Local;
        bool show = sanity != null && SanityEffects.ShouldShow(sanity);
        SetVisible(show || !hideWhenUnbound);
        if (sanity == null) return;

        if (sanitySlider != null)
        {
            sanitySlider.maxValue = PlayerSanity.Max;
            sanitySlider.value = sanity.Value;
        }

        if (fillImage != null && useLevelColors)
        {
            SanityLevel level = sanity.Level;
            Color colour = level == SanityLevel.Breaking ? breakingColor
                         : level == SanityLevel.Shaken ? shakenColor : calmColor;
            if (level == SanityLevel.Breaking && pulseWhenBreaking)
                colour.a *= 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 4f));
            fillImage.color = colour;
        }

        if (sanityText != null)
        {
            string text = SanityEffects.StatusText(sanity);
            if (text != shownText) { shownText = text; sanityText.text = text; }
        }
    }

    private void SetVisible(bool visible)
    {
        if (shownVisible == visible) return;
        shownVisible = visible;
        if (fadeGroup == null && !TryGetComponent(out fadeGroup))
            fadeGroup = gameObject.AddComponent<CanvasGroup>();
        fadeGroup.alpha = visible ? 1f : 0f;
        fadeGroup.blocksRaycasts = false;
        fadeGroup.interactable = false;
    }
}
