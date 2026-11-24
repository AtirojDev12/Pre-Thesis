using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 24 Nov 2026: a button's words change colour while the mouse is on it,
/// e.g. the VCR look: white bar, blue words. Can be added by hand to any button too.
/// Words of a disabled button are dimmed (Disabled Alpha).
/// LOCAL ONLY (UI).
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Pre-Thesis/Button Text Colour (hover)")]
public sealed class UiButtonTextColor : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("The words. Empty = the first text inside the button.")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private Color normal = Color.white;
    [SerializeField] private Color hover = new Color(0.1019608f, 0.1647059f, 0.6196079f, 1f);
    [Tooltip("How see-through the words are while the button cannot be pressed (1 = not dimmed).")]
    [SerializeField, Range(0f, 1f)] private float disabledAlpha = 0.35f;

    private Selectable selectable;
    private bool pointer;

    public void Set(TMP_Text words, Color normalColour, Color hoverColour)
    {
        label = words;
        normal = normalColour;
        hover = hoverColour;
        Apply();
    }

    /// <summary>For words whose normal colour the game changes (the chosen entry time).</summary>
    public void SetNormal(Color normalColour)
    {
        normal = normalColour;
        Apply();
    }

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
        if (label == null) label = GetComponentInChildren<TMP_Text>(true);
    }

    private void OnDisable() { pointer = false; Apply(); }

    public void OnPointerEnter(PointerEventData eventData) { pointer = true; Apply(); }
    public void OnPointerExit(PointerEventData eventData) { pointer = false; Apply(); }

    private void LateUpdate() => Apply(); // the button can turn on / off while the mouse is on it

    private Color Dimmed(Color c) { c.a *= disabledAlpha; return c; }

    private void Apply()
    {
        if (label == null) return;
        bool usable = selectable == null || selectable.IsInteractable();
        // Mouse only: a clicked button stays "selected" with no bar behind it, blue words would vanish.
        Color want = !usable ? Dimmed(normal) : pointer ? hover : normal;
        if (label.color != want) label.color = want;
    }
}
