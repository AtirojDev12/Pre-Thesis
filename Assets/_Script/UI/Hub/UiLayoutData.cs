using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 8 Nov 2026 (Mr.k): the saved look of EVERY game UI screen, edited from the
/// UI Hub on the GAME HUD.
///
/// Most screens are built by code at runtime (hotbar, clock, shop, work sheet,
/// popcorn / ticket UI ...). They have no prefab, so a designer could not edit them.
/// Now a designer changes a screen in Play mode (Inspector / Rect tool) and presses
/// Save. The change is stored here and put back every time that screen appears
/// (<see cref="UiLayoutRuntime"/>). The game code does not need to know.
///
/// A screen = a root Canvas, found by its object name ("Hotbar HUD", "Lobby Shop").
/// An element = one object inside it, found by its path ("Top/Clock Background").
/// Only the parts the designer changed are stored (each group has an on/off flag).
///
/// Lists only (no Dictionary): Unity cannot save Dictionaries.
/// Lives in Resources so builds load it without any scene object.
/// </summary>
[CreateAssetMenu(fileName = "UI Layout", menuName = "Pre-Thesis/UI Layout")]
public sealed class UiLayoutData : ScriptableObject
{
    /// <summary>Resources path (Assets/Resources/UI/UI Layout.asset).</summary>
    public const string ResourcePath = "UI/UI Layout";

    [Tooltip("Optional. Every text in every game UI screen uses this font (a screen or element font wins over it). Empty = each screen keeps its own fonts.")]
    public TMP_FontAsset fontForAllUi;

    [Tooltip("Every screen the game has shown in Play mode, with the designer's changes.")]
    public List<UiScreenLook> screens = new List<UiScreenLook>();

    /// <summary>Goes up on every change (the runtime re-reads the data when it changes).</summary>
    [NonSerialized] public int version;

    public static event Action<UiLayoutData> Changed;

    private static UiLayoutData loaded;
    private static bool triedLoad;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        loaded = null;
        triedLoad = false;
    }

    /// <summary>The project's layout data (null if it was never created).</summary>
    public static UiLayoutData Load()
    {
        if (loaded == null && !triedLoad)
        {
            triedLoad = true;
            loaded = Resources.Load<UiLayoutData>(ResourcePath);
        }
        return loaded;
    }

    /// <summary>Editor: use this asset from now on (it was just created).</summary>
    public static void SetLoaded(UiLayoutData data)
    {
        loaded = data;
        triedLoad = true;
    }

    public void MarkChanged()
    {
        version++;
        Changed?.Invoke(this);
    }

    private void OnValidate() => MarkChanged();

    public UiScreenLook FindScreen(string screenName)
    {
        for (int i = 0; i < screens.Count; i++)
            if (screens[i] != null && screens[i].screenName == screenName) return screens[i];
        return null;
    }

    public UiScreenLook GetOrAddScreen(string screenName)
    {
        UiScreenLook s = FindScreen(screenName);
        if (s != null) return s;
        s = new UiScreenLook { screenName = screenName };
        screens.Add(s);
        return s;
    }
}

/// <summary>One screen (a root Canvas, by object name).</summary>
[Serializable]
public sealed class UiScreenLook
{
    [Tooltip("Object name of the screen's root Canvas (do not rename).")]
    public string screenName = "";
    [Tooltip("Notes for the team.")]
    public string note = "";

    [Tooltip("Hide this whole screen.")]
    public bool hide;

    [Tooltip("Change which screen draws on top.")]
    public bool overrideSortOrder;
    [Tooltip("Higher = drawn on top of other screens.")]
    public int sortOrder;

    [Tooltip("Optional. Every text in this screen uses this font.")]
    public TMP_FontAsset font;

    [Tooltip("8 Nov: the editable prefab of this screen (Assets/UI Skins). Change pictures, models, texts, positions there; " +
             "the differences are copied into this screen automatically when the prefab is saved.")]
    public GameObject skin;

    [Tooltip("8 Nov: YOUR OWN design of this screen (any prefab). The game hides its own screen and shows this one; " +
             "parts with a UI Link copy words, bars, pictures and clicks from the game's screen.")]
    public GameObject design;

    public List<UiElementLook> elements = new List<UiElementLook>();

    [Tooltip("Objects the designer ADDED in the prefab (pictures, 3D models, texts). They are made inside the screen in the game.")]
    public List<UiAddedObject> added = new List<UiAddedObject>();

    public int ChangeCount
    {
        get
        {
            int n = elements.Count + added.Count;
            if (hide) n++;
            if (overrideSortOrder) n++;
            if (font != null) n++;
            if (design != null) n++;
            return n;
        }
    }

    public UiElementLook FindElement(string path)
    {
        for (int i = 0; i < elements.Count; i++)
            if (elements[i] != null && elements[i].path == path) return elements[i];
        return null;
    }

    public UiElementLook GetOrAddElement(string path)
    {
        UiElementLook e = FindElement(path);
        if (e != null) return e;
        e = new UiElementLook { path = path };
        elements.Add(e);
        return e;
    }
}

/// <summary>An object the designer added in the screen's prefab. A copy is made inside the live screen.</summary>
[Serializable]
public sealed class UiAddedObject
{
    [Tooltip("Path of the parent inside the screen (empty = the screen root).")]
    public string parentPath = "";
    [Tooltip("Place among the parent's children.")]
    public int siblingIndex;
    [Tooltip("The object inside the screen's prefab that is copied.")]
    public GameObject source;
}

/// <summary>Parts of a UI element that can be changed.</summary>
public enum UiLookGroup
{
    Hide,
    Layout,
    Color,
    Sprite,
    TextStyle,
    Text,
    Spacing,
    Size,
}

/// <summary>The designer's changes on one element. Each group is used only when its flag is on.</summary>
[Serializable]
public sealed class UiElementLook
{
    [Tooltip("Path from the screen's root, e.g. \"Top/Clock Background/Text\". \"Name#1\" = the 2nd child with that name.")]
    public string path = "";

    [Tooltip("Made from the screen's prefab (changes when the prefab changes). Off = saved in Play mode.")]
    public bool fromSkin;

    [Tooltip("Hide this element.")]
    public bool hide;

    [Tooltip("Position, size, anchors, pivot, rotation, scale.")]
    public bool layout;
    public Vector2 anchorMin = new Vector2(0.5f, 0.5f);
    public Vector2 anchorMax = new Vector2(0.5f, 0.5f);
    public Vector2 pivot = new Vector2(0.5f, 0.5f);
    public Vector2 position;
    public Vector2 size = new Vector2(100f, 100f);
    public Vector3 rotation;
    public Vector3 scale = Vector3.one;

    [Tooltip("Colour of the image or text.")]
    public bool color;
    public Color colorValue = Color.white;

    [Tooltip("Picture of an Image.")]
    public bool sprite;
    public Sprite spriteValue;

    [Tooltip("Font, size, style, alignment, spacing, wrapping.")]
    public bool textStyle;
    public TMP_FontAsset font;
    public float fontSize = 24f;
    public FontStyles fontStyle = FontStyles.Normal;
    public TextAlignmentOptions alignment = TextAlignmentOptions.Center;
    public bool autoSize;
    public float fontSizeMin = 10f;
    public float fontSizeMax = 72f;
    public float characterSpacing;
    public float lineSpacing;
    public TextWrappingModes wrapping = TextWrappingModes.Normal;
    [Tooltip("Old Unity Text only.")]
    public Font legacyFont;
    public FontStyle legacyFontStyle = FontStyle.Normal;
    public TextAnchor legacyAlignment = TextAnchor.MiddleCenter;

    [Tooltip("The words. Only for fixed texts (the game code may write over changing texts).")]
    public bool text;
    [TextArea(1, 4)] public string textValue = "";

    [Tooltip("Gap and padding of a row / column (Horizontal or Vertical Layout Group).")]
    public bool spacing;
    public float spacingValue;
    public int paddingLeft, paddingRight, paddingTop, paddingBottom;

    [Tooltip("Preferred / minimum size (Layout Element).")]
    public bool layoutSize;
    public float minWidth = -1f, minHeight = -1f, preferredWidth = -1f, preferredHeight = -1f;

    public bool Uses(UiLookGroup g)
    {
        switch (g)
        {
            case UiLookGroup.Hide: return hide;
            case UiLookGroup.Layout: return layout;
            case UiLookGroup.Color: return color;
            case UiLookGroup.Sprite: return sprite;
            case UiLookGroup.TextStyle: return textStyle;
            case UiLookGroup.Text: return text;
            case UiLookGroup.Spacing: return spacing;
            case UiLookGroup.Size: return layoutSize;
        }
        return false;
    }

    public void SetUses(UiLookGroup g, bool on)
    {
        switch (g)
        {
            case UiLookGroup.Hide: hide = on; break;
            case UiLookGroup.Layout: layout = on; break;
            case UiLookGroup.Color: color = on; break;
            case UiLookGroup.Sprite: sprite = on; break;
            case UiLookGroup.TextStyle: textStyle = on; break;
            case UiLookGroup.Text: text = on; break;
            case UiLookGroup.Spacing: spacing = on; break;
            case UiLookGroup.Size: layoutSize = on; break;
        }
    }

    public bool Empty => !hide && !layout && !color && !sprite && !textStyle && !text && !spacing && !layoutSize;
}
