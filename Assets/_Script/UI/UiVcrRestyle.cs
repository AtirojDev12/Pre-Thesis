using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 25 Nov 2026: gives a screen made in a PREFAB / SCENE (not by code) the VCR look of a
/// <see cref="CodeScreenStyle"/> asset, when the game starts. Used by the Esc menu
/// (Pause, Settings and the Leave confirmation), colours from Resources/UI/Styles/Pause Style.
///
/// Nothing in the prefab is changed: every part is recoloured at runtime, so the prefab can still
/// be rebuilt by its editor tool. What it does:
///   - cards: style colour, square corners, frame (Frame Sprite) on the edge;
///   - texts: "Title" = title colour + title font, "...Header" = accent, "Hint" = muted, others = text;
///   - buttons: VCR buttons (only words + thin frame, white bar with blue words under the mouse).
///     Buttons named "... Tab" are left to SettingsPanel (it already draws the VCR tabs);
///   - sliders, toggles, dropdowns (and their pop-up list), input fields, scrollbars.
/// LOCAL ONLY (UI).
/// </summary>
public static class UiVcrRestyle
{
    private static readonly Color FieldBack = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color ItemHover = new Color(1f, 1f, 1f, 0.25f);

    /// <param name="root">Everything under it (also switched-off parts).</param>
    /// <param name="cards">The panels: blue card + frame.</param>
    /// <param name="primary">Main buttons (Resume, Apply ...).</param>
    /// <param name="danger">Red-word buttons (Leave ...).</param>
    public static void Apply(Transform root, CodeScreenStyle s, IList<Image> cards,
                             IList<Button> primary = null, IList<Button> danger = null)
    {
        if (root == null || s == null) return;

        // Texts first: the parts below give their own words their own colours afterwards.
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            string n = t.gameObject.name;
            if (n == "Title") s.StyleText(t, s.title, true);
            else if (n.EndsWith("Header")) s.StyleText(t, s.accent);
            else if (n == "Hint") s.StyleText(t, s.muted);
            else if (n == "Placeholder") s.StyleText(t, s.faint);
            else s.StyleText(t, s.text);
        }

        if (cards != null)
            foreach (Image card in cards)
            {
                if (card == null) continue;
                card.sprite = s.panelSprite; // the old ones were rounded
                s.StyleCard(card);
                KeepFrameOutOfLayout(card.transform);
            }

        foreach (Slider slider in root.GetComponentsInChildren<Slider>(true))
        {
            Transform back = slider.transform.Find("Background");
            if (back != null) Paint(back.GetComponent<Image>(), s.sliderBack);
            if (slider.fillRect != null) Paint(slider.fillRect.GetComponent<Image>(), s.sliderFill);
            if (slider.handleRect != null) Paint(slider.handleRect.GetComponent<Image>(), s.sliderHandle);
        }

        StyleScrollbars(root, s);

        foreach (Toggle toggle in root.GetComponentsInChildren<Toggle>(true))
        {
            bool listItem = toggle.GetComponentInParent<ScrollRect>(true) != null; // a dropdown's list row
            if (listItem)
            {
                // Row of a dropdown list: no bar, a faint white bar under the mouse (like the tabs).
                Paint(toggle.targetGraphic as Image, s.button);
                ColorBlock c = toggle.colors;
                c.normalColor = new Color(1f, 1f, 1f, 0f);
                c.selectedColor = new Color(1f, 1f, 1f, 0f);
                c.highlightedColor = ItemHover;
                c.pressedColor = ItemHover;
                toggle.colors = c;
            }
            else Paint(toggle.targetGraphic as Image, s.sliderBack);
            if (toggle.graphic != null) toggle.graphic.color = s.text; // the tick
        }

        foreach (TMP_Dropdown dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true))
        {
            Paint(dropdown.targetGraphic as Image, FieldBack);
            AddFrame(dropdown.transform, s);
            if (dropdown.captionText != null) s.StyleText(dropdown.captionText, s.text);
            Transform arrow = dropdown.transform.Find("Arrow");
            if (arrow != null && arrow.TryGetComponent(out Image arrowImage)) arrowImage.color = s.text;
            if (dropdown.template != null && dropdown.template.TryGetComponent(out Image list))
            {
                Color panel = s.panel;
                panel.a = 1f; // the list covers other settings: not see-through
                Paint(list, panel);
                AddFrame(dropdown.template, s);
            }
        }

        foreach (TMP_InputField field in root.GetComponentsInChildren<TMP_InputField>(true))
        {
            Paint(field.targetGraphic as Image, FieldBack);
            AddFrame(field.transform, s);
            if (field.textComponent != null) s.StyleText(field.textComponent, s.text);
            if (field.placeholder is TMP_Text placeholder) s.StyleText(placeholder, s.faint);
            field.caretColor = s.text;
            field.customCaretColor = true;
            field.selectionColor = new Color(1f, 1f, 1f, 0.3f);
        }

        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            if (button.gameObject.name.EndsWith(" Tab"))
            {
                // SettingsPanel colours the tabs (VCR); only drop the rounded picture.
                if (button.targetGraphic is Image tab) tab.sprite = null;
                continue;
            }
            CodeScreenStyle.ButtonKind kind =
                primary != null && primary.Contains(button) ? CodeScreenStyle.ButtonKind.Primary :
                danger != null && danger.Contains(button) ? CodeScreenStyle.ButtonKind.Danger :
                CodeScreenStyle.ButtonKind.Normal;
            if (button.targetGraphic is Image image) image.sprite = s.buttonSprite; // the old ones were rounded
            s.StyleButton(button, button.GetComponentInChildren<TMP_Text>(true), kind);
        }
    }

    /// <summary>Only the scrollbars (for lists a screen builds later, e.g. the Settings key list).</summary>
    public static void StyleScrollbars(Transform root, CodeScreenStyle s)
    {
        if (root == null || s == null) return;
        foreach (Scrollbar bar in root.GetComponentsInChildren<Scrollbar>(true))
        {
            Paint(bar.GetComponent<Image>(), s.scrollbarBack);
            if (bar.handleRect != null) Paint(bar.handleRect.GetComponent<Image>(), s.scrollbarHandle);
        }
    }

    /// <summary>Colour + square corners (the default Unity pictures are rounded).</summary>
    private static void Paint(Image image, Color colour)
    {
        if (image == null) return;
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = colour;
    }

    /// <summary>Thin frame (style Frame Sprite, Button Frame colour) around a field.</summary>
    private static void AddFrame(Transform parent, CodeScreenStyle s)
    {
        if (s.frameSprite == null || s.buttonFrame.a <= 0f || parent.Find("Frame") != null) return;
        var go = new GameObject("Frame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.transform.SetAsFirstSibling();
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        Image image = go.GetComponent<Image>();
        image.raycastTarget = false;
        CodeScreenStyle.PaintImage(image, s.buttonFrame, s.frameSprite);
    }

    /// <summary>StyleCard puts a Frame as the first child; on a card with a layout group it must not take a slot.</summary>
    private static void KeepFrameOutOfLayout(Transform card)
    {
        if (card.childCount == 0) return;
        Transform frame = card.GetChild(0);
        if (frame.name != "Frame") return;
        LayoutElement layout = frame.GetComponent<LayoutElement>();
        if (layout == null) layout = frame.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        var rt = (RectTransform)frame;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }
}
