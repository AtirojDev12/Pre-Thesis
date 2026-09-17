using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Displays the stamina of the player owned by this machine.</summary>
public class StaminaBarUI : MonoBehaviour
{
    [SerializeField] private Slider staminaSlider;
    [SerializeField] private Image fillImage;
    [SerializeField] private TMP_Text staminaText;
    [SerializeField] private GameObject barRoot;
    [SerializeField] private bool hideWhenUnbound = true;

    private PlayerStamina _bound;
    private CanvasGroup _fadeGroup;

    private void Awake()
    {
        if (barRoot == gameObject) barRoot = null;
    }

    private void OnEnable()
    {
        PlayerStamina.LocalInstanceChanged += Bind;
        Bind(PlayerStamina.LocalInstance);
    }

    private void OnDisable()
    {
        PlayerStamina.LocalInstanceChanged -= Bind;
        Unbind();
    }

    private void Bind(PlayerStamina stamina)
    {
        if (_bound == stamina) return;

        Unbind();
        _bound = stamina;

        if (_bound == null)
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        _bound.StaminaChanged += UpdateBar;
        UpdateBar(_bound.CurrentStamina, _bound.MaxStamina);
    }

    private void Unbind()
    {
        if (_bound == null) return;
        _bound.StaminaChanged -= UpdateBar;
        _bound = null;
    }

    private void UpdateBar(float current, float maximum)
    {
        float ratio = maximum > 0f ? current / maximum : 0f;

        if (staminaSlider != null)
        {
            staminaSlider.maxValue = maximum;
            staminaSlider.value = current;
        }

        if (fillImage != null)
            fillImage.color = Color.Lerp(new Color(0.9f, 0.25f, 0.15f), new Color(0.2f, 0.85f, 0.45f), ratio);

        if (staminaText != null)
            staminaText.text = $"STAMINA {Mathf.CeilToInt(current)}";
    }

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
}
