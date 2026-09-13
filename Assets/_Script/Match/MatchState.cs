using UnityEngine;

/// <summary>
/// Carries the host's chosen RoomConfig across the scene load from MainMenu into
/// GamePlay.
///
/// Why this exists: RoomConfig is built on the room-creation screen and handed to
/// CreateLobby, and then it is gone — Mirror loads onlineScene and every plain
/// object in MainMenu is destroyed with it. MatchDirector runs in GamePlay and
/// needs to know which difficulty and player limit the host picked, so the config
/// has to survive that load.
///
/// This is a static class rather than a PersistentObject on purpose. It holds
/// pure data with no lifecycle, so it needs no GameObject, no DontDestroyOnLoad,
/// and no chance of a duplicate arriving from a scene. It follows the same shape
/// as SaveManager.Current: one static source of truth that every system reads
/// from and nobody copies.
///
/// HOST/SERVER ONLY. A joining client never fills this in and must never read it
/// as authority — a client's copy would be empty, and even if it weren't, a
/// client deciding its own difficulty is a cheat. Clients learn the match
/// settings from MatchDirector's SyncVars, which the server populates from here.
/// </summary>
public static class MatchState
{
    /// <summary>
    /// The room the host is about to open, or null when nothing has been
    /// configured (a designer pressing Play directly in GamePlay, for instance).
    /// MatchDirector falls back to a default config in that case so sandbox
    /// testing keeps working.
    /// </summary>
    public static RoomConfig PendingConfig { get; private set; }

    /// <summary>
    /// Call this on the HOST immediately before CreateLobby/StartHost, with the
    /// config that has already passed RoomConfig.Validate(). Passing an
    /// unvalidated config here would push unclamped values straight into the
    /// match.
    /// </summary>
    public static void SetPendingConfig(RoomConfig config)
    {
        PendingConfig = config;
    }

    /// <summary>
    /// Clear when returning to the menu, so a second match cannot silently
    /// inherit the previous room's settings.
    /// </summary>
    public static void Clear()
    {
        PendingConfig = null;
    }

    /// <summary>
    /// The config to actually run this match with — the host's choice if there is
    /// one, otherwise a safe default. Never returns null, so callers do not need
    /// a null branch just to support pressing Play in the editor.
    /// </summary>
    public static RoomConfig ResolveOrDefault()
    {
        return PendingConfig ?? new RoomConfig();
    }

    // Statics survive between Play sessions when Unity 6's domain reload is
    // disabled, which would otherwise leave the previous run's room settings
    // sitting here and silently apply them to the next test.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => PendingConfig = null;
}
