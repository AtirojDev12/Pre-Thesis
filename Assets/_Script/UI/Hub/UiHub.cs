using UnityEngine;

/// <summary>
/// 8 Nov 2026 (Mr.k): the ONE place a game designer edits every game UI screen.
///
/// Sits on the GAME HUD prefab. Its Inspector (Editor/UiHubEditor.cs) lists every
/// screen in the game: the GAME HUD parts, the scene menus, and the screens the game
/// builds by code (hotbar, clock, shop, work sheet, popcorn / ticket UI ...).
///
/// How a designer edits a screen built by code:
///   1. Press Play and make the screen appear.
///   2. In this Inspector, press Select on the screen. Change it in the normal
///      Inspector or with the Rect tool in the Scene view.
///   3. Press Save changes (or answer Save when Play stops).
/// The changes are kept in Assets/Resources/UI/UI Layout.asset and put back every time
/// the screen appears (UiLayoutRuntime), also in builds.
///
/// This component does nothing at runtime. It is only the doorway in the Inspector.
/// </summary>
[DisallowMultipleComponent]
public sealed class UiHub : MonoBehaviour
{
    public UiLayoutData Layout => UiLayoutData.Load();
}
