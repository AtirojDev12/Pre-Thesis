using UnityEngine;

/// <summary>
/// 21 Nov 2026 (Mr.k): put on a part of a code-built screen whose CHILDREN the game makes again
/// all the time (example: the lines of a Memo Book page).
///
/// The UI Hub copy of the screen (GAME HUD) keeps everything the designer restyled (cover, pages,
/// buttons, fonts, colours ...). Only inside this part it shows a live copy of the game's children
/// instead of its own sample children, so new lines always appear.
/// Use it only on parts whose children are plain UI (texts, pictures, cards): the copies are real
/// objects, any script on them would run too.
/// LOCAL ONLY (UI). No settings.
/// </summary>
[DisallowMultipleComponent]
public sealed class UiLiveChildren : MonoBehaviour
{
}
