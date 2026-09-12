using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// สคริปต์แสดงหลอดเลือด (Health Bar) บน UI
///
/// This belongs on the SCENE UI Canvas, not on the player prefab. A prefab that
/// gets spawned at runtime cannot hold a reference to a scene object, so the old
/// arrangement (HealthBarUI sitting on Player.prefab with an empty PlayerHealth
/// slot) could never work in a real match. Instead this binds itself to
/// whichever player belongs to THIS machine, as soon as that player spawns.
///
/// วิธีใช้:
/// 1) สร้าง UI > Slider ใน Canvas (คลิกขวาใน Hierarchy > UI > Slider)
/// 2) ลบ Handle ของ Slider ออกได้ถ้าไม่ต้องการให้ลากได้ (เอาไว้โชว์อย่างเดียว)
/// 3) ใส่สคริปต์นี้ไว้ที่ Canvas หรือ Slider
/// 4) ลาก Slider / Fill / Text (TMP) มาใส่ในช่องด้านล่าง
/// 5) ไม่ต้องลาก PlayerHealth -- สคริปต์จะหาผู้เล่นของเครื่องนี้เองตอนเกิด
/// </summary>
public class HealthBarUI : MonoBehaviour
{
    [Header("อ้างอิงถึงระบบเลือดของผู้เล่น")]
    [Tooltip("Leave EMPTY in a real match -- it binds to the local player automatically. Only set this to pin the bar to one specific player in a test scene.")]
    [SerializeField] private PlayerHealth playerHealth;

    [Header("UI Elements")]
    [SerializeField] private Slider healthSlider;
    [SerializeField] private TMP_Text healthText;

    [Header("Downed State UI")]
    [SerializeField] private GameObject downedPanel;
    [SerializeField] private TMP_Text downedTimerText;
    [SerializeField] private TMP_Text downedStatusText;

    [Header("สีของหลอดเลือด (optional)")]
    [SerializeField] private Image fillImage;
    [SerializeField] private Gradient healthGradient; // เขียว -> เหลือง -> แดง

    [Tooltip("Hide the bar while no local player exists (e.g. in the lobby, or before spawn).")]
    [SerializeField] private bool hideWhenUnbound = true;

    [Tooltip("Optional: a CHILD object holding the bar visuals. Leave empty and the bar fades itself with a CanvasGroup instead. Do not point this at the object this script is on.")]
    [SerializeField] private GameObject barRoot;

    private PlayerHealth _bound;
    private bool _manuallyAssigned;
    private CanvasGroup _fadeGroup;

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
        _bound.OnDownedTimerChanged.AddListener(UpdateDownedTimer);
        _bound.OnDeath.AddListener(HideDownedState);

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
        _fadeGroup.blocksRaycasts = visible;
    }

    private void Unbind()
    {
        if (_bound == null) return;
        _bound.OnHealthChanged.RemoveListener(UpdateHealthBar);
        _bound.OnDowned.RemoveListener(ShowDownedState);
        _bound.OnDownedTimerChanged.RemoveListener(UpdateDownedTimer);
        _bound.OnDeath.RemoveListener(HideDownedState);
        _bound = null;
    }

    private void UpdateHealthBar(float current, float max)
    {
        if (healthSlider != null)
        {
            healthSlider.maxValue = max;
            healthSlider.value = current;
        }

        if (healthText != null)
        {
            healthText.text = $"{Mathf.Ceil(current)} / {Mathf.Ceil(max)}";
        }

        if (fillImage != null && healthGradient != null)
        {
            float ratio = max > 0f ? current / max : 0f;
            fillImage.color = healthGradient.Evaluate(ratio);
        }
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
}
