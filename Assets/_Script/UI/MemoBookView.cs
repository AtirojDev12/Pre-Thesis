using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 25 Nov 2026: the Memo Book screen as a PREFAB the team can edit:
///   Assets/Resources/UI/Memo Book.prefab   (Tools > Pre-Thesis > Designer Settings > Memo Book Prefab)
///
/// WHAT YOU CAN DO IN THE PREFAB
///   Move, resize, recolour or swap the picture of every part: Cover, Paper, Paper (live),
///   the post-its, the arrows, the page number, the X post-it. Add decorations anywhere.
///   Move / resize "View" = where the words go. "Live Text Area" = where the words go on the
///   live page (from 00:00, when the arrows are hidden).
///   Post-its: one per page, top to bottom (the list below). More pages than post-its = the
///   post-its are used again from the first one, further down. Their child "Mark" shows ! ? 1 2 3 i.
///   Keep the slots below filled (drag the parts in if you replace one).
///
/// WHAT THE GAME DOES (WorksheetReader)
///   Fills "Content" with the page lines, picks the paper, shows / hides the post-its and arrows,
///   moves the open page's post-it out, and wires the buttons. Do not add buttons' On Click here.
///   Colours and the post-it move: Resources/Tuning/MemoBookLook.
/// LOCAL ONLY (UI).
/// </summary>
[DisallowMultipleComponent]
public sealed class MemoBookView : MonoBehaviour
{
    public const string ResourcePath = "UI/Memo Book";
    public const string PrefabPath = "Assets/Resources/UI/Memo Book.prefab";

    [Header("Paper")]
    [Tooltip("The paper before 00:00.")]
    public Image paper;
    [Tooltip("The paper of the live page (from 00:00). Switched off in the prefab; the game switches it on.")]
    public Image livePaper;

    [Header("Words")]
    [Tooltip("The scroll view on the paper (its RectTransform = where the words go).")]
    public ScrollRect scroll;
    [Tooltip("Where the game puts the page lines (inside the scroll view).")]
    public RectTransform content;
    [Tooltip("Optional: where the words go on the live page. Empty = the same place as the scroll view.")]
    public RectTransform liveTextArea;

    [Header("Post-its (top to bottom)")]
    public List<Image> postIts = new List<Image>();

    [Header("Buttons")]
    public Button previous;
    public Button next;
    public Button close;
    public TextMeshProUGUI pageNumber;

    // =====================================================================================
    // Builder: makes the default book (the editor saves it as the prefab; also the fallback
    // when the prefab is missing).
    // =====================================================================================

    /// <summary>The pictures for <see cref="Build"/>. Any of them may be empty (a plain block is drawn).</summary>
    public sealed class Art
    {
        public Sprite cover, paper, bloodyPaper, previous, next, close;
        public Sprite[] tabs = new Sprite[0]; // top to bottom
    }

    /// <summary>Builds the default book: every picture drawn on a <paramref name="canvas"/> x <paramref name="canvas"/> canvas, scaled by <paramref name="scale"/>.</summary>
    public static MemoBookView Build(Art art, int scale = 9, int canvas = 100)
    {
        if (art == null) art = new Art();

        var root = new GameObject("Worksheet Book", typeof(RectTransform));
        Canvas c = root.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 450;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        MemoBookView view = root.AddComponent<MemoBookView>();

        Image dim = NewImage("Dim", root.transform, new Color(0f, 0f, 0f, 0.6f), null);
        dim.raycastTarget = true;
        Stretch(dim.rectTransform);

        var bookGo = new GameObject("Book", typeof(RectTransform));
        bookGo.transform.SetParent(root.transform, false);
        var book = (RectTransform)bookGo.transform;
        book.anchorMin = book.anchorMax = book.pivot = new Vector2(0.5f, 0.5f);
        book.sizeDelta = new Vector2(canvas * scale, canvas * scale);
        book.anchoredPosition = Vector2.zero;

        ArtPart(book, "Cover", art.cover, new Color(0.43f, 0.29f, 0.25f), new Rect(0, 0, canvas, canvas), scale);
        view.paper = ArtPart(book, "Paper", art.paper, new Color(0.98f, 0.96f, 0.89f), new Rect(12, 0, 78, canvas), scale);
        view.livePaper = ArtPart(book, "Paper (live)", art.bloodyPaper != null ? art.bloodyPaper : art.paper,
            new Color(0.95f, 0.88f, 0.86f), new Rect(12, 0, 78, canvas), scale);
        view.livePaper.gameObject.SetActive(false);

        // Words: x 15..85, y 5..83 from the top (above the arrows). Live page: down to row 95.
        var viewGo = new GameObject("View", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
        viewGo.transform.SetParent(book, false);
        viewGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f); // catches the mouse wheel
        var viewport = (RectTransform)viewGo.transform;
        Place(viewport, new Rect(15, canvas - 83, 70, 78), scale);

        var column = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        column.transform.SetParent(viewport, false);
        view.content = (RectTransform)column.transform;
        view.content.anchorMin = new Vector2(0f, 1f);
        view.content.anchorMax = new Vector2(1f, 1f);
        view.content.pivot = new Vector2(0.5f, 1f);
        view.content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup v = column.GetComponent<VerticalLayoutGroup>();
        v.spacing = 14f;
        v.padding = new RectOffset(0, 10, 4, 24);
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        column.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        column.AddComponent<UiLiveChildren>(); // 21 Nov: the lines are made again on each change

        view.scroll = viewGo.AddComponent<ScrollRect>();
        view.scroll.viewport = viewport;
        view.scroll.content = view.content;
        view.scroll.horizontal = false;
        view.scroll.vertical = true;
        view.scroll.movementType = ScrollRect.MovementType.Clamped;
        view.scroll.scrollSensitivity = 45f;

        var liveArea = new GameObject("Live Text Area", typeof(RectTransform));
        liveArea.transform.SetParent(book, false);
        view.liveTextArea = (RectTransform)liveArea.transform;
        Place(view.liveTextArea, new Rect(15, canvas - 95, 70, 90), scale);

        // Post-its.
        var holder = new GameObject("Post-its", typeof(RectTransform));
        holder.transform.SetParent(book, false);
        Stretch((RectTransform)holder.transform);
        int count = art.tabs != null && art.tabs.Length > 0 ? art.tabs.Length : 6;
        for (int i = 0; i < count; i++)
        {
            Sprite s = art.tabs != null && i < art.tabs.Length ? art.tabs[i] : null;
            Image tab = ArtPart((RectTransform)holder.transform, "Post-it " + (i + 1), s, new Color(1f, 0.85f, 0.4f),
                new Rect(canvas - 11, canvas - 9 - i * 8, 11, 7), scale);
            tab.raycastTarget = true;
            Button b = tab.gameObject.AddComponent<Button>();
            b.targetGraphic = tab;
            ColorBlock colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
            colors.selectedColor = Color.white;
            b.colors = colors;
            Rect r = s != null ? s.rect : new Rect(0, 0, 11, 7);
            TextMeshProUGUI mark = NewText("Mark", tab.transform, (i + 1).ToString(), r.height * scale * 0.6f,
                new Color(0.20f, 0.13f, 0.10f, 0.9f), TextAlignmentOptions.Center);
            mark.fontStyle = FontStyles.Bold;
            Stretch(mark.rectTransform);
            mark.rectTransform.offsetMin = new Vector2(r.width * scale * 0.3f, 0f);
            view.postIts.Add(tab);
        }

        // Arrows, page number, X.
        view.previous = ArtButton(book, "Previous", art.previous, new Rect(24, 6, 10, 9), "<", scale);
        view.next = ArtButton(book, "Next", art.next, new Rect(66, 6, 10, 9), ">", scale);
        view.pageNumber = NewText("Page", book, "1 / 6", 24f, new Color(0.42f, 0.38f, 0.34f), TextAlignmentOptions.Center);
        Place(view.pageNumber.rectTransform, new Rect(34, 6, 32, 9), scale);
        view.close = ArtButton(book, "Close", art.close, new Rect(89, 12, 11, 7), "X", scale);

        return view;
    }

    private static Image ArtPart(RectTransform parent, string name, Sprite sprite, Color fallback, Rect fallbackRect, int scale)
    {
        Image image = NewImage(name, parent, sprite != null ? Color.white : fallback, sprite);
        Place(image.rectTransform, sprite != null ? sprite.rect : fallbackRect, scale);
        return image;
    }

    private static Button ArtButton(RectTransform parent, string name, Sprite sprite, Rect fallbackRect, string mark, int scale)
    {
        Image image = ArtPart(parent, name, sprite, new Color(0.42f, 0.08f, 0.09f), fallbackRect, scale);
        image.raycastTarget = true;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.selectedColor = Color.white;
        colors.highlightedColor = new Color(0.75f, 0.75f, 0.75f, 0.7f); // also shows on the black arrows
        colors.pressedColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0.2f);
        button.colors = colors;
        if (sprite == null)
        {
            TextMeshProUGUI t = NewText("Text", image.transform, mark, 30f, Color.white, TextAlignmentOptions.Center);
            t.fontStyle = FontStyles.Bold;
            Stretch(t.rectTransform);
        }
        return button;
    }

    /// <summary>Puts a part at a place on the picture canvas (canvas pixels, from the BOTTOM-left).</summary>
    private static void Place(RectTransform r, Rect art, int scale)
    {
        r.anchorMin = r.anchorMax = r.pivot = Vector2.zero;
        r.anchoredPosition = new Vector2(art.x * scale, art.y * scale);
        r.sizeDelta = new Vector2(art.width * scale, art.height * scale);
    }

    private static Image NewImage(string name, Transform parent, Color color, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        image.sprite = sprite;
        return image;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        if (t.font == null && TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }
}
