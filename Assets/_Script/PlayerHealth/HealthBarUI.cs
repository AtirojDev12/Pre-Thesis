using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// สคริปต์แสดงหลอดเลือด (Health Bar) บน UI
///
/// This belongs on the SCENE UI Canvas (the GAME HUD prefab), not on the player prefab.
/// It binds itself to whichever player belongs to THIS machine, as soon as that player spawns.
///
/// 21 Nov 2026: works like HudBar now -- Frame + Fill + Icon pictures.
///
/// เปลี่ยนรูป / แก้ไข (เปิด GAME HUD prefab > HP):
///   - Frame = รูปกรอบ/พื้นหลัง, Fill = รูปหลอดที่ลด/เพิ่ม, Icon = ไอคอนหัวใจ
///     กดที่ช่องใน component นี้เพื่อไปที่ object นั้น แล้วเปลี่ยน Source Image ได้เลย
///   - Fill ต้องเป็น Image Type "Filled" + Fill Method "Horizontal" -> รูปจะถูก "ตัด" ไม่บี้
///   - ลาก "Preview Fill" เพื่อดูหน้าตาหลอดตอนเลือดเหลือน้อยใน Editor (ไม่มีผลตอนเล่น)
///   - ย้าย / ย่อขยาย / เปลี่ยนสีได้อิสระ แค่ให้ช่องใน component นี้ยังชี้ถูก object
///   - ไม่ต้องลาก PlayerHealth -- สคริปต์จะหาผู้เล่นของเครื่องนี้เอง
/// </summary>
public class HealthBarUI : MonoBehaviour
{
    [Header("อ้างอิงถึงระบบเลือดของผู้เล่น")]
    [Tooltip("Leave EMPTY in a real match -- it binds to the local player automatically. Only set this to pin the bar to one specific player in a test scene.")]
    [SerializeField] private PlayerHealth playerHealth;

    [Header("Pictures (เปลี่ยนรูปแต่ละอันได้แยกกัน)")]
    [Tooltip("รูปกรอบ / พื้นหลังของหลอด (optional)")]
    [SerializeField] private Image frameImage;
    [Tooltip("รูปหลอดเลือดที่ลด/เพิ่ม. Image Type = Filled (Horizontal) -> รูปถูกตัด ไม่บี้")]
    [SerializeField] private Image fillImage;
    [Tooltip("ไอคอนข้างหลอด (optional)")]
    [SerializeField] private Image iconImage;

    [Header("UI Elements")]
    [Tooltip("Optional. Only needed if the Fill picture is NOT 'Filled' (then the Slider squeezes it).")]
    [SerializeField] private Slider healthSlider;
    [SerializeField] private TMP_Text healthText;
    [Tooltip("{0} = เลือดตอนนี้, {1} = เลือดเต็ม.  เช่น  HP {0}   หรือ   {0} / {1}")]
    [SerializeField] private string textFormat = "HP {0}";

    [Header("Bar")]
    [Tooltip("ความเร็วที่หลอดเลื่อนไปค่าใหม่ (เต็มหลอดต่อวินาที). 0 = เปลี่ยนทันที")]
    [SerializeField, Min(0f)] private float smoothSpeed = 1.5f;
    [Tooltip("Editor only: drag to see how the bar looks when HP is low.")]
    [SerializeField, Range(0f, 1f)] private float previewFill = 1f;
    [Tooltip("Draw order. 45 = above the sanity dark screen edges (44). 0 = leave it to the GAME HUD canvas.")]
    [SerializeField] private int sortingOrder = 45;

    [Header("Downed State UI")]
    [SerializeField] private GameObject downedPanel;
    [SerializeField] private TMP_Text downedTimerText;
    [SerializeField] private TMP_Text downedStatusText;

    [Header("สีของหลอดเลือด (optional)")]
    [Tooltip("On = tint the Fill picture with the gradient below (right = full HP). Off = the Fill keeps its own colour.")]
    [SerializeField] private bool useHealthColors;
    [SerializeField] private Gradient healthGradient; // เขียว -> เหลือง -> แดง

    [Tooltip("Hide the bar while no local player exists (e.g. in the lobby, or before spawn).")]
    [SerializeField] private bool hideWhenUnbound = true;

    [Tooltip("Optional: a CHILD object holding the bar visuals. Leave empty and the bar fades itself with a CanvasGroup instead. Do not point this at the object this script is on.")]
    [SerializeField] private GameObject barRoot;

    private PlayerHealth _bound;
    private bool _manuallyAssigned;
    private CanvasGroup _fadeGroup;
    private float _targetRatio = 1f;
    private float _shownRatio = -1f;

    public Image FrameImage => frameImage;
    public Image FillImage => fillImage;
    public Image IconImage => iconImage;

    private void Awake()
    {
        _manuallyAssigned = playerHealth != null;

        if (barRoot == gameObject)
        {
            Debug.LogWarning(
                "[HealthBarUI] Bar Root points at this same GameObject. Clearing it -- deactivating ourselves would " +
                "stop this script listening for the local player and the bar would never reappear. Fading instead.", this);
            barRoot = null;
        }

        // 28 Nov (Atiroj): the Fill slot was empty in the GAME HUD, so HP went down but the bar
        // did not. If it is empty again, use the child picture named "Fill" (Image Type Filled).
        if (fillImage == null)
        {
            foreach (Image image in GetComponentsInChildren<Image>(true))
                if (image.name == "Fill" && HudFill.IsCut(image)) { fillImage = image; break; }
            if (fillImage == null && healthSlider == null)
                Debug.LogWarning("[HealthBarUI] No Fill picture set: the HP bar will not move.", this);
        }

        HudFill.DetachSlider(healthSlider, fillImage);

        if (sortingOrder != 0)
        {
            // The GAME HUD canvas draws at order 0, under the sanity vignette (44).
            if (!TryGetComponent(out Canvas canvas)) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
        }
    }

    private void OnEnable()
    {
        if (_manuallyAssigned)
        {
            Bind(playerHealth);
            return;
        }

        PlayerHealth.LocalInstanceChanged += Bind;
        Bind(PlayerHealth.LocalInstance);
    }

    private void OnDisable()
    {
        if (!_manuallyAssigned) PlayerHealth.LocalInstanceChanged -= Bind;
        Unbind();
    }

    private void Update()
    {
        if (_bound == null || Mathf.Approximately(_shownRatio, _targetRatio)) return;
        _shownRatio = HudFill.Smooth(_shownRatio, _targetRatio, smoothSpeed);
        ShowRatio(_shownRatio);
    }

    private void Bind(PlayerHealth health)
    {
        if (_bound == health) return;

        Unbind();
        _bound = health;

        if (_bound == null)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        _bound.OnHealthChanged.AddListener(UpdateHealthBar);
        _bound.OnDowned.AddListener(ShowDownedState);
        _bound.OnRevived.AddListener(HideDownedState);
        _bound.OnDownedTimerChanged.AddListener(UpdateDownedTimer);
        _bound.OnDeath.AddListener(HideDownedState);

        _shownRatio = -1f; // jump straight to the new player's HP, no slide
        UpdateHealthBar(_bound.CurrentHealth, _bound.MaxHealth);
        UpdateDownedState();
    }

    /// <summary>
    /// Deliberately never calls SetActive(false) on this GameObject. Doing so
    /// would fire OnDisable, unsubscribe us from LocalInstanceChanged, and the
    /// bar would stay hidden forever once the player finally spawned.
    /// </summary>
    private void SetVisible(bool visible)
    {
        if (!hideWhenUnbound) return;

        if (barRoot != null)
        {
            barRoot.SetActive(visible);
            return;
        }

        if (_fadeGroup == null && !TryGetComponent(out _fadeGroup))
            _fadeGroup = gameObject.AddComponent<CanvasGroup>();

        _fadeGroup.alpha = visible ? 1f : 0f;
        _fadeGroup.blocksRaycasts = false;
        _fadeGroup.interactable = false;
    }

    private void Unbind()
    {
        if (_bound == null) return;
        _bound.OnHealthChanged.RemoveListener(UpdateHealthBar);
        _bound.OnDowned.RemoveListener(ShowDownedState);
        _bound.OnRevived.RemoveListener(HideDownedState);
        _bound.OnDownedTimerChanged.RemoveListener(UpdateDownedTimer);
        _bound.OnDeath.RemoveListener(HideDownedState);
        _bound = null;
    }

    private void UpdateHealthBar(float current, float max)
    {
        UpdateDownedState();

        _targetRatio = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        if (_shownRatio < 0f || smoothSpeed <= 0f)
        {
            _shownRatio = _targetRatio;
            ShowRatio(_shownRatio);
        }

        if (healthText != null)
            healthText.text = HudFill.Format(textFormat, current, max);
    }

    private void ShowRatio(float ratio)
    {
        HudFill.Apply(fillImage, healthSlider, ratio);

        if (useHealthColors && fillImage != null && healthGradient != null)
            fillImage.color = healthGradient.Evaluate(ratio);
    }

    private void UpdateDownedState()
    {
        if (_bound == null) return;

        if (_bound.IsDowned)
        {
            ShowDownedState();
            UpdateDownedTimer(_bound.DownedTimer);
        }
        else
        {
            HideDownedState();
        }
    }

    private void ShowDownedState()
    {
        if (downedPanel != null) downedPanel.SetActive(true);
        if (downedStatusText != null) downedStatusText.text = "DOWNED";
    }

    private void HideDownedState()
    {
        if (downedPanel != null) downedPanel.SetActive(false);
    }

    private void UpdateDownedTimer(float remainingTime)
    {
        if (downedTimerText != null)
        {
            downedTimerText.text = $"{Mathf.Ceil(remainingTime)}s";
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            if (_bound != null) UpdateHealthBar(_bound.CurrentHealth, _bound.MaxHealth);
            return;
        }
        // Editor preview: see the cut picture while designing.
        if (HudFill.IsCut(fillImage) && !Mathf.Approximately(fillImage.fillAmount, previewFill))
            fillImage.fillAmount = previewFill;
    }
#endif
}
