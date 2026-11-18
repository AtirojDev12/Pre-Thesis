using UnityEngine;

/// <summary>
/// 8 Nov 2026 (Mr.k): put this on a part of YOUR OWN screen design (a prefab you made) to
/// connect it to the screen the game builds by code. The game keeps running its own screen
/// (hidden) and this part copies from it every frame: show / hide, words, colour, picture,
/// bar fill, and button clicks.
///
/// Usually you do not add this by hand: the UI Hub (GAME HUD) adds it when you
///   - press "Make my own design from this screen", or
///   - drag a part of your prefab next to a game part in the hub's "Connect" list, or
///   - press "Auto-connect by name".
///
/// Lists (shop items, player rows ...): tick Repeat on a row template whose Source is the
/// list. One copy of the row is made per item. Links inside the row use paths inside one
/// game row (empty Source = the row itself).
///
/// 16 Nov 2026 (Mr.k): screens that were moved INTO the GAME HUD prefab use the same links
/// (made automatically). For those, "Only When The Game Changes It" is on:
///   - Your words / colour / picture / place / size stay as you set them in the prefab,
///   - until the game changes its own part (new text, warning colour, item icon, a bar that
///     grows, a row that moves). Then the game's change is shown.
///   So you can restyle freely and the screen still reacts.
/// New: Control (sliders, toggles, dropdowns, input fields, scroll views work both ways) and
/// Follow Layout (the part moves / grows when the game's part does).
/// </summary>
[AddComponentMenu("Pre-Thesis/UI Link (connect to game screen)")]
[DisallowMultipleComponent]
public sealed class UiLink : MonoBehaviour
{
    public enum Axis { Horizontal, Vertical }

    [Tooltip("Path of the game's part inside its screen, e.g. \"Top/Clock Background/Text\". Inside a Repeat row: path inside one game row.")]
    public string source = "";

    [Tooltip("Show / hide this part when the game shows / hides its part. Also hidden while the game's part does not exist.")]
    public bool showHide = true;
    [Tooltip("Copy the words (Text / TextMeshPro).")]
    public bool text = true;
    [Tooltip("Copy the colour (warning colours, selected slot ...).")]
    public bool color;
    [Tooltip("Copy the picture (Image sprite / RawImage texture), e.g. item icons.")]
    public bool picture;
    [Tooltip("16 Nov: the game makes this picture while it runs (static, glow). It is always copied, even with \"Only Game Changes\".")]
    public bool pictureAlways;
    [Tooltip("Copy how full a bar is. Your part: a Filled Image (uses Fill Amount) or anything else (uses Scale).")]
    public bool fill;
    [Tooltip("Direction of the bar.")]
    public Axis fillAxis = Axis.Horizontal;
    [Tooltip("Clicking this part's Button clicks the game's button.")]
    public bool click = true;
    [Tooltip("16 Nov: Slider / Toggle / Dropdown / Input Field / Scroll View on this part works both ways with the game's one.")]
    public bool control;
    [Tooltip("16 Nov: this part moves and grows when the game moves or resizes its part (progress bars made by size, sliding panels, list rows).")]
    public bool followLayout;

    [Tooltip("This part is a ROW TEMPLATE: one copy per child of Source (the game's list). The template itself stays hidden.")]
    public bool repeat;
    [Tooltip("16 Nov, Repeat only: only the list's children whose name starts with this are rows (e.g. \"Row \"). Empty = every child.")]
    public string repeatNameStart = "";

    [Header("Only when the game changes it (16 Nov)")]
    [Tooltip("On = your words / colour / picture / place stay as you made them until the game changes its part from the values below. " +
             "Off = always copy from the game.")]
    public bool onlyGameChanges;
    [Tooltip("What the game's part showed when this screen was moved into the GAME HUD. Do not edit.")]
    [TextArea(1, 3)] public string gameText = "";
    public Color gameColour = Color.white;
    public Object gamePicture;
    public Vector2 gamePosition;
    public Vector2 gameSize;
}
