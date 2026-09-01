using Epic.OnlineServices.Lobby;

/// <summary>
/// Plain, UI-facing view of one FindLobbies() search result -- built by
/// LobbyController from a raw LobbyDetails plus the attributes CreateRoom
/// wrote (see LobbyKeys). A room browser UI should only ever read this, never
/// touch LobbyDetails or the EOS attribute API directly.
/// </summary>
public class RoomListEntry
{
    public string mapID;
    public DifficultyLevel difficulty;
    public int currentPlayers;
    public int maxPlayers;
    public bool isLocked;

    // Kept so a "Join" button on this row can call LobbyController.JoinRoom(entry)
    // without the UI code ever needing to know what a LobbyDetails is.
    //
    // NOTE: EOSLobby.FindLobbies() never calls LobbyDetails.Release() on the
    // handles it collects -- that's a pre-existing gap in the base class, not
    // something this file introduces. Fine for a short demo session; worth
    // revisiting if the room browser ends up refreshing very frequently over a
    // long play session (each refresh currently leaks the previous handles).
    public LobbyDetails details;
}
