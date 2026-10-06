using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button)), DisallowMultipleComponent]
public sealed class UISoundEmitter : MonoBehaviour, IPointerEnterHandler
{
    [SerializeField] private string clickSound = "UI_Click";
    [SerializeField] private string hoverSound = "UI_Hover";
    private Button button;

    public bool HoverEnabled { get; set; } = true;

    private void Awake() => button = GetComponent<Button>();
    private void OnEnable()
    {
        if (button == null) button = GetComponent<Button>();
        button.onClick.AddListener(Clicked);
    }
    private void OnDisable() => button.onClick.RemoveListener(Clicked);
    private void Clicked()
    {
        // Button already rejects disabled input before invoking onClick. An earlier
        // listener may disable Apply or close its panel after accepting this click.
        AudioManager.Instance?.PlayUI(clickSound);
    }
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (HoverEnabled && button.IsActive() && button.IsInteractable()) AudioManager.Instance?.PlayUI(hoverSound);
    }
}
