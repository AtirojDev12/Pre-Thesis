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

    public override void OnRoomStartHost()
    {
        NetworkMode.SessionEnding = false;
        // Mirror's built-in lobby GUI is debug-only; ours replaces it.
        showRoomGUI = false;
    }

    public override void OnRoomServerConnect(NetworkConnectionToClient conn)
    {
        // RoomPasswordAuthenticator already refuses full rooms with a readable
        // reason. This is the safety net in case no authenticator is assigned.
        if (NetworkServer.connections.Count > roomPlayerLimit)
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
        foreach (NetworkRoomPlayer slot in roomSlots)
        {
            if (slot == null) continue;
            if (slot is RoHRoomPlayer p && p.IsHost) continue;
            if (!slot.readyToBegin) return false;
        }
        return true;
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

    public override void OnRoomStopServer()
    {
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
