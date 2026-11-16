using UnityEngine;

/// <summary>
/// 16 Nov 2026 (Mr.k): marks a child of the GAME HUD prefab as the EDITABLE VERSION of one
/// screen the game builds by code (shop, storage, results, player list, popcorn / ticket UI,
/// ghost bubbles, task boards ...).
///
/// HOW IT WORKS
///   - The game still builds and runs its own screen, but hidden.
///   - This child is copied and shown in its place (same canvas settings, same place for
///     world-space screens; one copy per board / bubble).
///   - Its parts carry a <see cref="UiLink"/>: words, pictures, colours, bars, lists, buttons,
///     sliders and input fields stay connected to the game.
///
/// FOR DESIGNERS
///   Open the GAME HUD prefab (or press Edit in the UI Hub), switch this object on to see it,
///   and restyle it: pictures, fonts, sizes, places, new decorations. Its saved on / off state
///   does not matter: the game switches it off at start and shows copies.
///   Do not rename Screen Name.
///
/// Made by the editor (GameHudScreens): press Play, make the screen appear once, stop Play.
/// LOCAL ONLY (UI).
/// </summary>
[AddComponentMenu("Pre-Thesis/UI Hub Screen (GAME HUD)")]
[DisallowMultipleComponent]
public sealed class UiHubScreen : MonoBehaviour
{
    [Tooltip("Object name of the game's own screen (its root Canvas). Do not change it.")]
    [SerializeField] private string screenName = "";
    [Tooltip("Off = the game shows its own code-made screen again (this version is kept but not used).")]
    [SerializeField] private bool showInGame = true;

    private static UiHubScreen[] all;
    private static PersistentHUD allFrom;

    public string ScreenName => screenName;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        all = null;
        allFrom = null;
    }

    /// <summary>The GAME HUD's editable version of a code-built screen (a switched-off child), or null.</summary>
    public static GameObject TemplateFor(string screen)
    {
        PersistentHUD hud = PersistentHUD.Instance;
        if (hud == null) return null;
        if (all == null || allFrom != hud)
        {
            all = hud.GetComponentsInChildren<UiHubScreen>(true);
            allFrom = hud;
        }
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].showInGame && all[i].screenName == screen) return all[i].gameObject;
        return null;
    }

    /// <summary>True when the live GAME HUD holds at least one screen (the layout runtime is needed).</summary>
    public static bool AnyInHud
    {
        get
        {
            TemplateFor(null);
            return all != null && all.Length > 0;
        }
    }

#if UNITY_EDITOR
    public void EditorSetScreenName(string screen) => screenName = screen;
#endif
}
