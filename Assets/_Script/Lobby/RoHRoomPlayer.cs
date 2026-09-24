using Mirror;
using UnityEngine;

/// <summary>
/// One player's seat in the WAITING LOBBY. Lives on RoomPlayer.prefab.
///
/// It has no body, no camera and no input: it only carries what the lobby
/// screen needs to draw — a name, a ready flag (inherited), and whether this
/// seat is the host. When the match starts, RoHRoomManager swaps it for the
/// real Player prefab.
///
/// Names come from the player's own Settings (GameSettings.PlayerName) and are
/// sent once, by the owner, through a Command. The server trims them, so a
/// modified client cannot push a 5,000-character name onto everyone's screen.
/// </summary>
[DisallowMultipleComponent]
public class RoHRoomPlayer : NetworkRoomPlayer
{
    public const int MaxNameLength = 16;

    [SyncVar] private string displayName = string.Empty;
    [SyncVar] private bool isHost;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? $"Player {index + 1}" : displayName;
    public bool IsHost => isHost;

    public override void OnStartServer()
    {
        // The host's own connection is the in-process local one.
        isHost = connectionToClient is LocalConnectionToClient;
    }

    public override void OnStartLocalPlayer()
    {
        CmdSetName(GameSettings.PlayerName);
    }

    [Command]
    private void CmdSetName(string requested)
    {
        displayName = SanitizeName(requested);
    }

    /// <summary>Called by the lobby UI's Ready button on the local player.</summary>
    public void SetReady(bool ready)
    {
        if (!isLocalPlayer) return;
        CmdChangeReadyState(ready);
    }

    public static string SanitizeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string trimmed = raw.Trim();
        return trimmed.Length > MaxNameLength ? trimmed.Substring(0, MaxNameLength) : trimmed;
    }

    /// <summary>Mirror's debug lobby GUI is replaced by WaitingLobbyController.</summary>
    public override void OnGUI() { }
}
