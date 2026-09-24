/// <summary>
/// One switch that says "the player is in a menu, ignore gameplay keys".
///
/// Set by PauseMenuController. Read by PlayerMovement, PlayerInteractor,
/// FirstPersonCamera and the popcorn Tab cursor toggle, so pressing W, E or Tab
/// while the pause menu is open does nothing in the world.
///
/// It does NOT stop time: this is an online co-op game, so the match keeps
/// running for everyone, including you.
/// </summary>
public static class GameplayInput
{
    public static bool Blocked { get; set; }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Blocked = false;
}
