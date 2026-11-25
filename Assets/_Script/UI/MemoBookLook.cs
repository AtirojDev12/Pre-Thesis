using TMPro;
using UnityEngine;

/// <summary>
/// 25 Nov 2026: the COLOURS and post-it behaviour of the Memo Book (WorksheetReader).
/// The book's pictures, places and sizes are in its prefab: Assets/Resources/UI/Memo Book.prefab
/// (Tools > Pre-Thesis > Designer Settings > Memo Book Prefab).
///
/// HOW TO USE
///   1. Tools > Pre-Thesis > Designer Settings > Memo Book Look  (selects this file)
///   2. Change the values in the Inspector. Close and open the book again to see them.
///   3. Commit the file: Assets/Resources/Tuning/MemoBookLook.asset
/// </summary>
[CreateAssetMenu(fileName = "MemoBookLook", menuName = "Pre-Thesis/Memo Book Look")]
public sealed class MemoBookLook : ScriptableObject
{
    public const string ResourcePath = "Tuning/MemoBookLook";
    public const string AssetPath = "Assets/Resources/Tuning/MemoBookLook.asset";

    [Header("Post-its")]
    [Tooltip("The open page's post-it moves this far to the right (screen pixels at 1920 x 1080). 18 = 2 picture pixels.")]
    public float selectedTabOut = 18f;
    [Tooltip("The other post-its are darkened with this colour.")]
    public Color idleTabTint = new Color(0.78f, 0.78f, 0.78f, 1f);

    [Header("Pages")]
    [Tooltip("Colour of each post-it's page (titles, cards), same order as the post-its. Darker than the post-it so it reads on white paper.")]
    public Color[] pageColors =
    {
        new Color(0.72f, 0.52f, 0.05f), new Color(0.78f, 0.24f, 0.30f), new Color(0.20f, 0.45f, 0.78f),
        new Color(0.30f, 0.30f, 0.32f), new Color(0.22f, 0.58f, 0.30f), new Color(0.85f, 0.42f, 0.20f),
    };
    [Tooltip("Colour of the normal words (ink).")]
    public Color ink = new Color(0.20f, 0.13f, 0.10f, 1f);

    [Header("Fonts (25 Nov)")]
    [Tooltip("Game theme font: the book before the shift starts (and its post-its), and the hotbar. Empty = the font most GAME HUD texts use.")]
    public TMP_FontAsset themeFont;
    [Tooltip("Blood font: the live book (from 00:00). Empty = Resources/Fonts/Nosifer-Regular (a dripping blood font, OFL licence).")]
    public TMP_FontAsset bloodFont;
    [Tooltip("Colour of the blood letters.")]
    public Color bloodColor = new Color(0.62f, 0.02f, 0.02f, 1f);
    [Tooltip("Old (not used).")]
    [HideInInspector] public float wordSeconds = 0.18f;
    [Tooltip("Old (not used).")]
    [HideInInspector] public float letterSeconds = 0.05f;
    [Tooltip("Old (not used).")]
    [HideInInspector] public float wordGap = 0.08f;
    public enum LiveStyle { BloodSeep = 0, Pen = 1 }
    [Tooltip("How the live words appear: Blood Seep = blood soaks up through the paper in blotches; Pen = written stroke by stroke.")]
    public LiveStyle liveStyle = LiveStyle.BloodSeep;
    [Tooltip("Old (not used).")]
    [HideInInspector] public float seepSeconds = 1.6f;
    [HideInInspector] public float seepStagger = 0.12f;
    [HideInInspector] public float seepWordGap = 0.15f;
    [Tooltip("Blood Seep: every word soaks up AT THE SAME TIME, slowly. Seconds until the words are fully soaked (drips run down at the end).")]
    [Min(0.2f)] public float seepTogetherSeconds = 4f;
    [Tooltip("Blood Seep: colour of fresh, thin blood (the letters darken to Blood Color as they soak).")]
    public Color freshBlood = new Color(0.95f, 0.45f, 0.42f, 1f);
    [Tooltip("Dark blood halo soaked into the paper around the letters (alpha 0 = none).")]
    public Color soakHalo = new Color(0.35f, 0f, 0f, 0.55f);
    [Tooltip("Live book pen: seconds to write (or wipe) one letter along its strokes. Long letters (M, W, 8) take a bit more.")]
    [Min(0.02f)] public float penLetterSeconds = 0.32f;
    [Tooltip("Live book pen: pause between two words (seconds).")]
    [Min(0f)] public float penWordGap = 0.25f;

    private static TMP_FontAsset foundTheme, madeBlood;
    private static Material foundThemeMaterial;
    private static float nextThemeSearch;

    /// <summary>
    /// 25 Nov: the theme font: the asset's, else the font most texts of the GAME HUD use (with the
    /// same material, so it looks exactly like the HUD), else Electronic Highway Sign, else TMP's default.
    /// </summary>
    public static TMP_FontAsset ThemeFont
    {
        get
        {
            if (Current.themeFont != null) return Current.themeFont;
            if (foundTheme == null && Time.unscaledTime >= nextThemeSearch)
            {
                nextThemeSearch = Time.unscaledTime + 2f;
                FindThemeFont();
            }
            return foundTheme != null ? foundTheme : TMP_Settings.defaultFontAsset;
        }
    }

    /// <summary>The material that goes with <see cref="ThemeFont"/> (null = the font's own).</summary>
    public static Material ThemeMaterial
    {
        get
        {
            TMP_FontAsset font = ThemeFont;
            if (Current.themeFont != null || font == null) return font != null ? font.material : null;
            return foundThemeMaterial != null ? foundThemeMaterial : font.material;
        }
    }

    private static void FindThemeFont()
    {
        var count = new System.Collections.Generic.Dictionary<TMP_FontAsset, int>();
        var material = new System.Collections.Generic.Dictionary<TMP_FontAsset, Material>();
        PersistentHUD hud = PersistentHUD.Instance;
        if (hud != null)
            foreach (TMP_Text t in hud.GetComponentsInChildren<TMP_Text>(true))
            {
                TMP_FontAsset f = t.font;
                if (f == null || f == TMP_Settings.defaultFontAsset || f.name.StartsWith("LiberationSans")) continue;
                count.TryGetValue(f, out int n);
                count[f] = n + 1;
                if (!material.ContainsKey(f)) material[f] = t.fontSharedMaterial;
            }
        int best = 0;
        foreach (var pair in count)
            if (pair.Value > best) { best = pair.Value; foundTheme = pair.Key; }
        if (foundTheme != null) { foundThemeMaterial = material[foundTheme]; return; }
        foundTheme = Resources.Load<TMP_FontAsset>("Fonts & Materials/Electronic Highway Sign SDF");
        foundThemeMaterial = foundTheme != null ? foundTheme.material : null;
    }

    /// <summary>25 Nov: the blood font (the asset's, or one made once from Resources/Fonts/Nosifer-Regular).</summary>
    public static TMP_FontAsset BloodFont
    {
        get
        {
            if (Current.bloodFont != null) return Current.bloodFont;
            if (madeBlood == null)
            {
                Font source = Resources.Load<Font>("Fonts/Nosifer-Regular");
                if (source != null)
                {
                    madeBlood = TMP_FontAsset.CreateFontAsset(source, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                        1024, 1024, AtlasPopulationMode.Dynamic);
                    if (madeBlood != null) madeBlood.name = "Nosifer (blood, made at run time)";
                }
            }
            return madeBlood != null ? madeBlood : ThemeFont;
        }
    }

    private static MemoBookLook current;

    public static MemoBookLook Current
    {
        get
        {
            if (current != null) return current;
            current = Resources.Load<MemoBookLook>(ResourcePath);
            if (current == null)
            {
                current = CreateInstance<MemoBookLook>();
                current.hideFlags = HideFlags.HideAndDontSave;
            }
            return current;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        if (current != null && (current.hideFlags & HideFlags.DontSave) != 0) DestroyImmediate(current);
        current = null;
        foundTheme = madeBlood = null;
        foundThemeMaterial = null;
        nextThemeSearch = 0f;
    }

    public static void Use(MemoBookLook asset)
    {
        if (asset != null) current = asset;
    }
}
