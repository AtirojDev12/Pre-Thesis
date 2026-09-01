using System;

/// <summary>
/// Plain data contract describing a single room, as configured by the host on the
/// Room Creation / Matchmaking screen. This class carries no logic beyond basic
/// validation and no networking of its own — something authoritative (the host's
/// LobbyController, or a server-owned NetworkBehaviour once the match starts) is
/// responsible for validating it and replicating the *validated* result to clients.
/// Never trust a RoomConfig that arrived from a client without re-validating it
/// server-side first.
///
/// Pre-Thesis demo scope: only one map exists, so MapID is fixed to DemoMapID.
/// It stays a string key (not an index or a hardcoded enum) on purpose — the full
/// thesis build can drop in a real map registry/ScriptableObject list later
/// without changing any call site that reads RoomConfig.mapID.
/// </summary>
[Serializable]
public class RoomConfig
{
    public const string DemoMapID = "demo_map_01";

    public const int MinPlayers = 1;
    public const int MaxPlayers = 6;

    public string mapID = DemoMapID;
    public DifficultyLevel difficulty = DifficultyLevel.Normal;
    public int playerLimit = MaxPlayers;
    public bool isPrivate = false;
    public string password = string.Empty;

    /// <summary>
    /// Clamps values into range and enforces "private rooms need a password".
    /// Call this on the HOST/server side immediately before CreateLobby — treat
    /// any RoomConfig built from UI input as untrusted until it passes this.
    /// </summary>
    public bool Validate(out string error)
    {
        playerLimit = Math.Clamp(playerLimit, MinPlayers, MaxPlayers);

        if (isPrivate && string.IsNullOrWhiteSpace(password))
        {
            error = "Private rooms require a password.";
            return false;
        }

        error = null;
        return true;
    }
}
