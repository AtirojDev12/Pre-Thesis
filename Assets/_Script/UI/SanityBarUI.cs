using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sanity bar inside the GAME HUD canvas (next to the Stamina Bar), for the player owned
/// by this machine. This is the ONLY sanity bar (SanityEffects no longer builds one);
/// the vignette, eat/feed progress and jumpscare still come from SanityEffects.
///
/// 21 Nov 2026: works like HudBar now -- Frame + Fill + Icon pictures.
///
/// เปลี่ยนรูป / แก้ไข (เปิด GAME HUD prefab > Sanity Bar):
///   - Frame = รูปกรอบ/พื้นหลัง, Fill = รูปหลอดที่ลด/เพิ่ม, Icon = ไอคอนข้างหลอด
///     กดที่ช่องใน component นี้เพื่อไปที่ object นั้น แล้วเปลี่ยน Source Image ได้เลย
///   - Fill ต้องเป็น Image Type "Filled" + Fill Method "Horizontal" -> รูปจะถูก "ตัด" ไม่บี้
///   - Use Level Colors = เปลี่ยนสีหลอดตามระดับสติ. ปิดถ้าอยากให้รูปใช้สีของตัวเอง
///   - ลาก "Preview Fill" เพื่อดูหน้าตาหลอดใน Editor (ไม่มีผลตอนเล่น)
/// </summary>
public sealed class SanityBarUI : MonoBehaviour
{
    /// <summary>The enabled HUD sanity bar, if any (SanityEffects checks this).</summary>
    public static SanityBarUI Active { get; private set; }

    [Header("Pictures (เปลี่ยนรูปแต่ละอันได้แยกกัน)")]
    [Tooltip("รูปกรอบ / พื้นหลังของหลอด (optional)")]
    [SerializeField] private Image frameImage;
    [Tooltip("รูปหลอดสติที่ลด/เพิ่ม. Image Type = Filled (Horizontal) -> รูปถูกตัด ไม่บี้")]
    [SerializeField] private Image fillImage;
    [Tooltip("ไอคอนข้างหลอด (optional)")]
    [SerializeField] private Image iconImage;

    [Header("UI Elements")]
    [Tooltip("Optional. Only needed if the Fill picture is NOT 'Filled' (then the Slider squeezes it).")]
    [SerializeField] private Slider sanitySlider;
    [Tooltip("Optional. Shows 'SANITY 75' plus chanting / cannot-recover hints.")]
    [SerializeField] private TMP_Text sanityText;
    [SerializeField] private bool hideWhenUnbound = true;
    [Tooltip("Draw order of this bar. Must be above the sanity vignette (44) so the dark screen edges " +
             "never cover the bar, and below the jumpscare flash (300).")]
    [SerializeField] private int sortingOrder = 45;

    [Header("Bar")]
    [Tooltip("ความเร็วที่หลอดเลื่อนไปค่าใหม่ (เต็มหลอดต่อวินาที). 0 = เปลี่ยนทันที")]
    [SerializeField, Min(0f)] private float smoothSpeed = 1.5f;
    [Tooltip("Editor only: drag to see how the bar looks when sanity is low.")]
    [SerializeField, Range(0f, 1f)] private float previewFill = 1f;

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
    private float shownRatio = -1f;
    private PlayerSanity shownFor;

    public Image FrameImage => frameImage;
    public Image FillImage => fillImage;
    public Image IconImage => iconImage;

    private void Awake()
    {
        // The GAME HUD canvas draws at order 0, under SanityEffects' vignette (44).
        // A nested canvas lifts just this bar above the vignette.
        if (!TryGetComponent(out Canvas canvas)) canvas = gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;

        HudFill.DetachSlider(sanitySlider, fillImage);
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

        // New local player (respawn / new match): jump, do not slide.
        if (shownFor != sanity) { shownFor = sanity; shownRatio = -1f; }

        float target = sanity.Fraction;
        if (!Mathf.Approximately(shownRatio, target))
        {
            shownRatio = HudFill.Smooth(shownRatio, target, smoothSpeed);
            HudFill.Apply(fillImage, sanitySlider, shownRatio);
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

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying) return;
        // Editor preview: see the cut picture while designing.
        if (HudFill.IsCut(fillImage) && !Mathf.Approximately(fillImage.fillAmount, previewFill))
            fillImage.fillAmount = previewFill;
    }
#endif
}
