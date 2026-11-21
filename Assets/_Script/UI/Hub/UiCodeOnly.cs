using UnityEngine;

/// <summary>
/// 21 Nov 2026 (Mr.k): put on the root Canvas of a screen that rebuilds its own parts all the
/// time (example: the Memo Book pages). The UI Hub then never shows an editable copy of it
/// (<see cref="UiHubScreen"/>): a copy is a fixed snapshot and would hide every part that is
/// made later. Fonts and saved layout changes from the UI Hub still apply.
/// LOCAL ONLY (UI). No settings.
/// </summary>
[DisallowMultipleComponent]
public sealed class UiCodeOnly : MonoBehaviour
{
}
