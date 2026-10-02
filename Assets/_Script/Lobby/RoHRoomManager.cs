using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The project's NetworkManager. Adds a WAITING LOBBY in front of the match.
///
/// FLOW
/// ----
///   MainMenu  --Create/Join-->  Lobby scene (RoomScene)  --host presses Start-->  Cinema_GamePlay
///
/// In the lobby every connection owns a lightweight RoHRoomPlayer (name + ready
/// flag, no body, no camera). When the host starts, Mirror swaps each room
/// player for the real first-person Player prefab in the gameplay scene.
///
/// WHY NetworkRoomManager
/// ----------------------
/// It already solves the hard parts: a different player object in the lobby
/// and in the game, ready flags, refusing to let anyone join once the match
/// has started, and swapping players across the scene change. Writing that by
/// hand is where desyncs come from.
///
/// WHAT IS CHANGED FROM THE DEFAULT
/// --------------------------------
///   - The game does NOT auto-start when everyone is ready. The HOST presses
///     Start (StartMatch). Guests signal Ready; the host decides.
///   - Mirror's debug OnGUI lobby is switched off. Our own UI is used.
///   - Leaving the Mirror session also leaves the EOS lobby, so dead rooms do
///     not stay listed in the room browser.
///   - The room's player limit (1-6) is enforced here as well as by EOS.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Network/13RoH Room Manager")]
public class RoHRoomManager : NetworkRoomManager
{
    /// <summary>Extra Mirror connections on top of the 6 players, for dev spectators (3 Oct, bug #6; 3 so a full room still takes 3).</summary>
    public const int SpectatorConnections = 3;

    public override void Awake()
    {
        // 3 Oct (bug #6): Mirror's cap counted spectators as players, so a spectator
        // could not join a full room and could push the 6th player out. The real
        // player limit is enforced by RoomPasswordAuthenticator (room setting).
        // The EOS transport reads this same value when the server starts.
        maxConnections = RoomConfig.MaxPlayers + SpectatorConnections;
        // Register before Mirror starts a host/client, including scene manager overrides.
        GameObject worldRadio = Resources.Load<GameObject>(PlayerItemThrow.WorldPrefabPath);
        if (worldRadio != null && !spawnPrefabs.Contains(worldRadio)) spawnPrefabs.Add(worldRadio);
        base.Awake();
    }

    /// <summary>Typed access to the running manager, or null if there is none / it is another type.</summary>
    public static RoHRoomManager Instance => singleton as RoHRoomManager;

    /// <summary>
    /// Why the last session ended, for the main menu to show once
    /// ("Wrong password", "Room is full", "The host closed the room"...).
    /// Cleared by whoever displays it.
    /// </summary>
    public static string LastDisconnectReason { get; set; }

    [Header("13RoH")]
    [Tooltip("The room's player limit, set by LobbyController from RoomConfig before StartHost. " +
             "Not the same as maxConnections: EOS already caps the lobby, this is the second lock on the Mirror side.")]
    [SerializeField] private int roomPlayerLimit = RoomConfig.MaxPlayers;

    public int RoomPlayerLimit => roomPlayerLimit;

    // Set when the local player presses Leave, so the menu does not greet
    // them with "Disconnected" for something they chose to do.
    private static bool leftOnPurpose;

    /// <summary>True while the server is sitting in the waiting lobby (not in the match).</summary>
    public bool InRoomScene => Utils.IsSceneActive(RoomScene);

    // Statics survive between Play sessions when domain reload is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRoHStatics()
    {
        LastDisconnectReason = null;
        leftOnPurpose = false;

        SceneManager.sceneLoaded -= ClearSessionEndingOnMenu;
        SceneManager.sceneLoaded += ClearSessionEndingOnMenu;
    }

    // The session is fully over once a scene loads with no server and no client
    // (the main menu). From then on offline test code may run again.
    private static void ClearSessionEndingOnMenu(Scene scene, LoadSceneMode mode)
    {
        if (!NetworkMode.SessionEnding) return;
        if (NetworkServer.active || NetworkClient.active) return;

        NetworkMode.SessionEnding = false;
    }

    // Every way a session can end (Leave button, host closed, kicked, lost
    // connection) goes through OnStopServer / OnStopClient, so the flag is
    // set there and not only in LeaveSession.
    private void BeginSessionEnd()
    {
        NetworkMode.SessionEnding = true;

        // StopWithoutMenu hides offlineScene while stopping; the menu WILL load.
        if (!string.IsNullOrEmpty(heldMenuScene)) return;

        // No scene will load if there is no offline scene, or we are already
        // in it (a join that failed from the main menu). Clear the flag now or
        // it would never be cleared.
        if (string.IsNullOrWhiteSpace(offlineScene) || Utils.IsSceneActive(offlineScene))
            NetworkMode.SessionEnding = false;
    }

    /// <summary>Called by LobbyController just before StartHost.</summary>
    public void SetRoomPlayerLimit(int limit)
    {
        roomPlayerLimit = Mathf.Clamp(limit, RoomConfig.MinPlayers, RoomConfig.MaxPlayers);
    }

    // ---- Server --------------------------------------------------------------

    public override void OnRoomStartServer() => serverStopping = false;

    public override void OnRoomStartHost()
    {
        serverStopping = false;
        NetworkMode.SessionEnding = false;
        // Mirror's built-in lobby GUI is debug-only; ours replaces it.
        showRoomGUI = false;
    }

    // ---- Spectators (2 Oct) ----------------------------------------------------
    // A spectator connection (RoomPasswordAuthenticator marks it) gets no seat
    // and no body, is never counted as a player, may join mid-match, and leaving
    // does not reset anyone's Ready.

    public const string SpectatorTag = "spectator";

    public static bool IsSpectator(NetworkConnectionToClient conn) =>
        conn != null && conn.authenticationData is string tag && tag == SpectatorTag;

    /// <summary>
    /// SERVER. Connections that are real players: accepted by the password check
    /// and not spectators. Connections still waiting for their check are not
    /// counted (3 Oct): one of them may turn out to be a spectator.
    /// </summary>
    public static int CountPlayerConnections()
    {
        int n = 0;
        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            if (conn != null && conn.isAuthenticated && !IsSpectator(conn)) n++;
        return n;
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        // Spectators may join while the match runs (the base class refuses
        // everyone outside the lobby scene). Mirror already sent them the scene.
        if (IsSpectator(conn)) return;
        base.OnServerConnect(conn);
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // The base class would un-Ready everybody when anyone leaves.
        if (IsSpectator(conn))
        {
            NetworkServer.DestroyPlayerForConnection(conn);
            return;
        }
        DropItemsOfLeaver(conn);
        base.OnServerDisconnect(conn);
    }

    // True from the moment the server starts shutting down (OnStopServer runs
    // before Mirror disconnects everybody), so a closing room drops nothing.
    private bool serverStopping;

    /// <summary>
    /// SERVER (3 Oct, bug #1). A player who leaves a running match drops their
    /// hotbar where they stood, before their body is destroyed. Not when the room
    /// is closing, not the host itself, and not a player whose round is already
    /// over (escaped players keep what they carried out; dead ones already dropped).
    /// </summary>
    private void DropItemsOfLeaver(NetworkConnectionToClient conn)
    {
        if (serverStopping || conn == null || conn is LocalConnectionToClient || conn.identity == null) return;
        MatchDirector match = MatchDirector.Instance;
        if (match == null || !match.RoundRunning || match.ServerIsFinished(conn.identity)) return;
        if (conn.identity.TryGetComponent(out PlayerItemThrow items)) items.ServerDropOnLeave();
    }

    public override void OnRoomServerConnect(NetworkConnectionToClient conn)
    {
        // RoomPasswordAuthenticator already refuses full rooms with a readable
        // reason. This is the safety net in case no authenticator is assigned.
        if (CountPlayerConnections() > roomPlayerLimit)
        {
            Debug.Log($"[RoHRoomManager] Room is full ({roomPlayerLimit}). Disconnecting {conn}.");
            conn.Disconnect();
        }
    }

    /// <summary>
    /// Default behaviour starts the match the moment everyone is ready.
    /// We want the host to press Start instead, so this does nothing.
    /// </summary>
    public override void OnRoomServerPlayersReady() { }

    /// <summary>
    /// True when every player EXCEPT the host has pressed Ready.
    /// The host does not need to ready up: pressing Start is their ready.
    /// </summary>
    public bool AllGuestsReady()
    {
        int seats = 0;
        foreach (NetworkRoomPlayer slot in roomSlots)
        {
            if (slot == null) continue;
            seats++;
            if (slot is RoHRoomPlayer p && p.IsHost) continue;
            if (!slot.readyToBegin) return false;
        }
        // 3 Oct (bug #7): a guest who passed the password check but is still
        // loading the lobby has no seat yet. Starting now would drop them.
        return seats >= CountPlayerConnections();
    }

    /// <summary>
    /// SERVER ONLY. Called by the host's Start button. Moves everyone from the
    /// waiting lobby into the match.
    /// </summary>
    public bool StartMatch()
    {
        if (!NetworkServer.active)
        {
            Debug.LogWarning("[RoHRoomManager] StartMatch called without a running server.");
            return false;
        }

        if (!InRoomScene) return false;

        if (!AllGuestsReady())
        {
            Debug.Log("[RoHRoomManager] Not everyone is ready yet.");
            return false;
        }

        // Hide the room from the browser as "in progress". Mirror refuses late
        // joiners anyway (OnServerConnect), this just stops people trying.
        if (LobbyController.Instance != null) LobbyController.Instance.MarkRoomInProgress(true);

        ServerChangeScene(GameplayScene);
        return true;
    }

    // ---- Kick (2 Oct): host removes a guest from the waiting lobby ----------
    //
    // Used by the lobby board (hold E on a name) and the M panel (Kick button).
    // The guest is told why (TargetKicked -> main menu message), then
    // disconnected a moment later so that message arrives first. A kicked
    // player may join again (Mr.k, 2 Oct).

    private readonly HashSet<int> kicking = new HashSet<int>();

    /// <summary>SERVER (host). Removes a guest from the waiting lobby. False if not allowed.</summary>
    public bool KickPlayer(RoHRoomPlayer seat)
    {
        if (!NetworkServer.active || !InRoomScene || seat == null || seat.IsHost) return false;
        NetworkConnectionToClient conn = seat.connectionToClient;
        if (conn == null || conn is LocalConnectionToClient || !kicking.Add(conn.connectionId)) return false;

        Debug.Log($"[RoHRoomManager] Host kicked '{seat.DisplayName}' ({conn}).");
        seat.TargetKicked(conn);
        StartCoroutine(DisconnectSoon(conn));
        return true;
    }

    private IEnumerator DisconnectSoon(NetworkConnectionToClient conn)
    {
        yield return new WaitForSecondsRealtime(0.3f);
        kicking.Remove(conn.connectionId);
        if (!NetworkServer.connections.TryGetValue(conn.connectionId, out NetworkConnectionToClient live) || live != conn) yield break;
        conn.Disconnect();

        // Safety net: if the transport did not report the disconnect, tell Mirror
        // ourselves, so the player's seat and lobby body are removed for everyone.
        yield return null;
        if (NetworkServer.connections.TryGetValue(conn.connectionId, out live) && live == conn && Transport.active != null)
        {
            Debug.LogWarning($"[RoHRoomManager] Transport did not report the kick of {conn}; cleaning up.");
            Transport.active.OnServerDisconnected?.Invoke(conn.connectionId);
        }
    }

    // ---- 3D lobby (1 Oct): every player walks around with a real body -------
    //
    // Mirror's room needs each connection's lobby SEAT (RoHRoomPlayer: name,
    // Ready) and the normal game Player prefab is what can walk, talk in 3D,
    // use the shop and the practice stations. So in the lobby scene the body
    // becomes the connection's MAIN player and the seat stays alive and owned
    // (KeepAuthority) beside it. Before the match starts the seat becomes the
    // main player again, so Mirror's normal "swap seat for game player" works
    // unchanged; coming back from a match, the body is given again.

    [Header("3D lobby")]
    [Tooltip("ON: in the lobby scene each player gets a walkable body (the Player prefab). OFF: the old flat lobby.")]
    [SerializeField] private bool lobbyBodies = true;

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        if (IsSpectator(conn)) return; // no seat, no body: watches only
        base.OnServerAddPlayer(conn);
        if (InRoomScene) AttachLobbyBody(conn);
    }

    public override void OnServerReady(NetworkConnectionToClient conn)
    {
        base.OnServerReady(conn);
        // Back from a match: Mirror made the seat the main player again.
        if (InRoomScene) AttachLobbyBody(conn);
    }

    public override void ServerChangeScene(string newSceneName)
    {
        // Leaving the lobby for the match: the seat must be the main player
        // again, or Mirror cannot swap it for the game player.
        if (InRoomScene && newSceneName != RoomScene)
        {
            DetachLobbyBodies();
            // Match start: spares do not go into the match (max 1 per item there).
            pendingSpares.Clear();
            // Seats queued while in the lobby (after returning from a match)
            // would be replayed when the map loads, including players who have
            // left since. Every player instead gets their game body when their
            // own client finishes loading (OnServerReady), like the first match.
            pendingPlayers.Clear();
        }
        base.ServerChangeScene(newSceneName);
    }

    private void AttachLobbyBody(NetworkConnectionToClient conn)
    {
        if (!lobbyBodies || playerPrefab == null || conn == null || !conn.isReady || conn.identity == null) return;
        RoHRoomPlayer seat = conn.identity.GetComponent<RoHRoomPlayer>();
        if (seat == null) return; // already walking around

        Transform start = GetStartPosition();
        GameObject body = start != null
            ? Instantiate(playerPrefab, start.position, start.rotation)
            : Instantiate(playerPrefab, Vector3.up, Quaternion.identity);
        body.name = $"{playerPrefab.name} (lobby) [connId={conn.connectionId}]";

        NetworkServer.ReplacePlayerForConnection(conn, body, ReplacePlayerOptions.KeepAuthority);
        GiveBackSpares(conn, body);
    }

    // ---- Spares carried back from a match (2 Oct, Mr.k) ----------------------
    //
    // A survivor who carried 2 of the same item (e.g. their own walkie + a dead
    // friend's) keeps BOTH when the round ends and they come back to the lobby,
    // so they can drop (Q) the spare for that friend. The save still holds only
    // 1 per item; the spare lives only here, on the server, until the lobby
    // body exists. When the next MATCH starts, spares are cleared: everyone
    // starts the match with at most 1 of each item (the loadout from the save).

    private struct PendingSpare
    {
        public int connectionId;
        public string itemId;
    }

    private readonly List<PendingSpare> pendingSpares = new List<PendingSpare>();

    /// <summary>SERVER. Called by MatchDirector for a survivor: the extra copies they carried out.</summary>
    public void ServerRememberSpares(NetworkConnectionToClient conn, List<string> spareItemIds)
    {
        if (conn == null || spareItemIds == null) return;
        pendingSpares.RemoveAll(p => p.connectionId == conn.connectionId);
        foreach (string id in spareItemIds)
            if (!string.IsNullOrEmpty(id)) pendingSpares.Add(new PendingSpare { connectionId = conn.connectionId, itemId = id });
    }

    private void GiveBackSpares(NetworkConnectionToClient conn, GameObject body)
    {
        PlayerInventory inventory = body != null ? body.GetComponent<PlayerInventory>() : null;
        if (inventory == null) return;
        for (int i = pendingSpares.Count - 1; i >= 0; i--)
        {
            if (pendingSpares[i].connectionId != conn.connectionId) continue;
            // The loadout (from the save) arrives a moment later; whichever comes
            // second is marked SPARE by ServerAddItem.
            inventory.ServerAddItem(InventorySlot.Of(pendingSpares[i].itemId, false));
            pendingSpares.RemoveAt(i);
        }
    }

    public override void OnRoomServerDisconnect(NetworkConnectionToClient conn)
    {
        if (conn != null) pendingSpares.RemoveAll(p => p.connectionId == conn.connectionId);
        base.OnRoomServerDisconnect(conn);
    }

    private void DetachLobbyBodies()
    {
        foreach (NetworkRoomPlayer seat in roomSlots)
        {
            if (seat == null) continue;
            NetworkConnectionToClient conn = seat.connectionToClient;
            if (conn == null || conn.identity == null || conn.identity == seat.netIdentity) continue;

            // Seat back as the main player; the lobby body is destroyed everywhere.
            NetworkServer.ReplacePlayerForConnection(conn, seat.gameObject, ReplacePlayerOptions.Destroy);
        }
    }

    public override void OnRoomStopServer()
    {
        serverStopping = true;
        pendingSpares.Clear();
        BeginSessionEnd();
        if (LobbyController.Instance != null) LobbyController.Instance.LeaveRoom();
    }

    // ---- Client --------------------------------------------------------------

    public override void OnRoomStartClient()
    {
        leftOnPurpose = false;
        NetworkMode.SessionEnding = false;
    }

    public override void OnRoomClientDisconnect()
    {
        if (leftOnPurpose) return;

        // Only fill a reason if nobody gave a better one (e.g. the authenticator
        // already wrote "Wrong password").
        if (string.IsNullOrEmpty(LastDisconnectReason) && !NetworkServer.active)
            LastDisconnectReason = "Lost connection to the room.";
    }

    public override void OnRoomStopClient()
    {
        SpectatorSession.Requested = false;
        BeginSessionEnd();
        // A client leaving the Mirror session must also leave the EOS lobby,
        // otherwise EOS still counts them and the room shows as fuller than it is.
        // (On the host, OnRoomStopServer already handles it; LeaveRoom is idempotent.)
        if (LobbyController.Instance != null) LobbyController.Instance.LeaveRoom();
    }

    // ---- Helpers for UI ------------------------------------------------------

    /// <summary>Room players sorted by slot index, nulls removed. Safe to call every frame.</summary>
    public void GetOrderedRoomPlayers(List<RoHRoomPlayer> into)
    {
        into.Clear();
        foreach (NetworkRoomPlayer slot in roomSlots)
        {
            if (slot is RoHRoomPlayer p && p != null) into.Add(p);
        }
        into.Sort((a, b) => a.index.CompareTo(b.index));
    }

    /// <summary>Leave the room from any role: host shuts the room, a client just leaves.</summary>
    // Set only while StopWithoutMenu runs.
    private string heldMenuScene;

    /// <summary>
    /// Stops the session like LeaveSession, but does NOT let Mirror start
    /// loading the main menu in the same frame. Returns the menu scene; the
    /// caller loads it a couple of frames later. Splitting "tear down the
    /// match" and "load the menu" into different frames avoids the Unity
    /// Editor freeze on host leave.
    /// </summary>
    public string StopWithoutMenu()
    {
        leftOnPurpose = true;
        BeginSessionEnd();

        string menu = offlineScene;
        heldMenuScene = menu;
        offlineScene = string.Empty;   // Mirror skips its own scene change
        try
        {
            if (NetworkServer.active && NetworkClient.isConnected) StopHost();
            else if (NetworkClient.active) StopClient();
            else if (NetworkServer.active) StopServer();
        }
        finally
        {
            offlineScene = menu;
            heldMenuScene = null;
        }
        return menu;
    }

    public void LeaveSession()
    {
        leftOnPurpose = true;
        BeginSessionEnd();

        if (NetworkServer.active && NetworkClient.isConnected) StopHost();
        else if (NetworkClient.active) StopClient();
        else if (NetworkServer.active) StopServer();

    }
}
