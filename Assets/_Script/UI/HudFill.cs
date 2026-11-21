using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 21 Nov 2026: shared "how full is the bar" logic for HealthBarUI and SanityBarUI.
/// Same rules as HudBar, so every bar in the GAME HUD behaves the same way.
///
/// FOR DESIGNERS
///   - Fill picture with Image Type "Filled" (Fill Method Horizontal): the picture is CUT as the
///     bar goes down. Any picture works and it is never squeezed. Recommended.
///   - Any other Image Type: the bar falls back to the Slider (if its Fill Rect is set), which
///     SQUEEZES the picture.
/// </summary>
public static class HudFill
{
    /// <summary>True when the fill picture is cut (Image Type Filled with a sprite).</summary>
    public static bool IsCut(Image fill) => fill != null && fill.type == Image.Type.Filled && fill.sprite != null;

    /// <summary>
    /// Stops a Slider from also resizing a "Filled" picture (it would shrink twice).
    /// Call once in Awake.
    /// </summary>
    public static void DetachSlider(Slider slider, Image fill)
    {
        if (slider == null || !IsCut(fill) || slider.fillRect != fill.rectTransform) return;
        slider.fillRect = null;
        RectTransform rt = fill.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
    }

    /// <summary>Shows <paramref name="ratio"/> (0..1) on the bar.</summary>
    public static void Apply(Image fill, Slider slider, float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        if (IsCut(fill)) fill.fillAmount = ratio;
        if (slider != null)
        {
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(ratio);
        }
    }

    /// <summary>Moves the shown value toward the real one. speed &lt;= 0 = jump at once.</summary>
    public static float Smooth(float shown, float target, float speed)
    {
        if (speed <= 0f || shown < 0f) return target;
        return Mathf.MoveTowards(shown, target, speed * Time.unscaledDeltaTime);
    }

    /// <summary>"HP {0} / {1}" style text; falls back safely if the format is typed wrong.</summary>
    public static string Format(string format, float current, float max)
    {
        int c = Mathf.CeilToInt(current), m = Mathf.CeilToInt(max);
        if (string.IsNullOrEmpty(format)) return c.ToString();
        try { return string.Format(format, c, m); }
        catch (System.FormatException) { return format + " " + c; }
    }
}
