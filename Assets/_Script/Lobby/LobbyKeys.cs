/// <summary>
/// Central place for every EOS Lobby attribute key string this project uses.
/// Keeping them here instead of scattering magic strings across LobbyController,
/// UI scripts, and the lobby browser means a typo becomes a compile error instead
/// of a silent "attribute not found" at runtime.
/// </summary>
public static class LobbyKeys
{
    public const string MapID = "MapID";
    public const string Difficulty = "Difficulty";
    public const string PlayerLimit = "PlayerLimit";
    public const string IsPrivate = "IsPrivate";

    // Do NOT store RoomConfig.password itself as a lobby attribute — public/
    // searchable EOS lobby attributes can be read by anyone browsing lobbies,
    // so a plain "Password" attribute would leak it to every client running
    // FindLobbies(), private room or not. This key only flags that a password
    // exists, for UI display (e.g. showing a lock icon in a room browser).
    // The actual password must be checked out-of-band — for example inside a
    // Mirror NetworkAuthenticator challenge that runs after StartClient(),
    // never read back off lobby search results.
    public const string HasPassword = "HasPassword";
}
