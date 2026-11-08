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
/// </summary>
[AddComponentMenu("Pre-Thesis/UI Link (connect to game screen)")]
[DisallowMultipleComponent]
public sealed class UiLink : MonoBehaviour
{
    public enum Axis { Horizontal, Vertical }

    [Tooltip("Path of the game's part inside its screen, e.g. \"Top/Clock Background/Text\". Inside a Repeat row: path inside one game row.")]
    public string source = "";

    [Tooltip("Show / hide this part when the game shows / hides its part.")]
    public bool showHide = true;
    [Tooltip("Copy the words (Text / TextMeshPro).")]
    public bool text = true;
    [Tooltip("Copy the colour (warning colours, selected slot ...).")]
    public bool color;
    [Tooltip("Copy the picture (Image sprite / RawImage texture), e.g. item icons.")]
    public bool picture;
    [Tooltip("Copy how full a bar is. Your part: a Filled Image (uses Fill Amount) or anything else (uses Scale).")]
    public bool fill;
    [Tooltip("Direction of the bar.")]
    public Axis fillAxis = Axis.Horizontal;
    [Tooltip("Clicking this part's Button clicks the game's button.")]
    public bool click = true;

    [Tooltip("This part is a ROW TEMPLATE: one copy per child of Source (the game's list). The template itself stays hidden.")]
    public bool repeat;
}
