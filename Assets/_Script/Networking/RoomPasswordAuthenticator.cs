using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// The door check every joining player goes through BEFORE they get a seat.
/// Sits on the NetworkManager prefab next to RoHRoomManager.
///
/// It refuses, with a readable reason the joining player actually sees:
///   - a wrong password for a private room
///   - a full room
///   - a room whose match has already started
///
/// WHY HERE AND NOT IN EOS
/// -----------------------
/// EOS lobby attributes are readable by anyone who searches for rooms, so the
/// password can never be stored there (see LobbyKeys.HasPassword). The host
/// keeps the password in memory only, and checks it here, over Mirror, after
/// the joining player has connected.
///
/// Mirror runs the authenticator BEFORE NetworkManager.OnServerConnect, so this
/// is also the one place a rejection can come with a message — a bare
/// disconnect later tells the player nothing.
/// </summary>
[DisallowMultipleComponent]
public class RoomPasswordAuthenticator : NetworkAuthenticator
{
    /// <summary>Host side. Set by LobbyController when the room is created. Empty = public room.</summary>
    public static string ServerPassword { get; set; } = string.Empty;

    /// <summary>Joining side. Set by the room browser before joining. Empty for public rooms.</summary>
    public static string ClientPassword { get; set; } = string.Empty;

    [Tooltip("Seconds between telling a client why they were refused and disconnecting them, so the message arrives first.")]
    [Min(0.1f)] [SerializeField] private float rejectDelay = 0.5f;

    private readonly HashSet<NetworkConnectionToClient> pendingDisconnect = new HashSet<NetworkConnectionToClient>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ServerPassword = string.Empty;
        ClientPassword = string.Empty;
    }

    public struct RoomAuthRequest : NetworkMessage
    {
        public string password;
        /// <summary>2 Oct: join as an invisible spectator (Development Build "Spectate" button).</summary>
        public bool spectator;
    }

    public struct RoomAuthResponse : NetworkMessage
    {
        public bool accepted;
        public string reason;
    }

    // ---- Server --------------------------------------------------------------

    public override void OnStartServer()
    {
        NetworkServer.RegisterHandler<RoomAuthRequest>(OnAuthRequest, false);
    }

    public override void OnStopServer()
    {
        NetworkServer.UnregisterHandler<RoomAuthRequest>();
        pendingDisconnect.Clear();
    }

    public override void OnServerAuthenticate(NetworkConnectionToClient conn)
    {
        // Wait for the client's RoomAuthRequest.
    }

    private void OnAuthRequest(NetworkConnectionToClient conn, RoomAuthRequest msg)
    {
        if (pendingDisconnect.Contains(conn)) return;

        // The host's own in-process client always gets in.
        if (conn is LocalConnectionToClient)
        {
            Accept(conn);
            return;
        }

        RoHRoomManager room = RoHRoomManager.Instance;

        // Spectators (2 Oct) may join a full room or a match in progress; they
        // take no seat and are not counted. The password still applies.
        if (msg.spectator)
        {
            if (!string.IsNullOrEmpty(ServerPassword) && msg.password != ServerPassword)
            {
                Reject(conn, "Wrong password.");
                return;
            }
            conn.authenticationData = RoHRoomManager.SpectatorTag;
            Debug.Log($"[RoomPasswordAuthenticator] {conn} joined as a SPECTATOR.");
            Accept(conn);
            return;
        }

        if (room != null && !room.InRoomScene)
        {
            Reject(conn, "The match has already started.");
            return;
        }

        // Accepted players + this one (not accepted yet, so not in the count).
        if (room != null && RoHRoomManager.CountPlayerConnections() + 1 > room.RoomPlayerLimit)
        {
            Reject(conn, "The room is full.");
            return;
        }

        if (!string.IsNullOrEmpty(ServerPassword) && msg.password != ServerPassword)
        {
            Reject(conn, "Wrong password.");
            return;
        }

        Accept(conn);
    }

    private void Accept(NetworkConnectionToClient conn)
    {
        conn.Send(new RoomAuthResponse { accepted = true, reason = string.Empty });
        ServerAccept(conn);
    }

    private void Reject(NetworkConnectionToClient conn, string reason)
    {
        pendingDisconnect.Add(conn);
        conn.Send(new RoomAuthResponse { accepted = false, reason = reason });
        conn.isAuthenticated = false;
        StartCoroutine(DisconnectAfterDelay(conn));
    }

    private IEnumerator DisconnectAfterDelay(NetworkConnectionToClient conn)
    {
        yield return new WaitForSeconds(rejectDelay);
        ServerReject(conn);
        yield return null;
        pendingDisconnect.Remove(conn);
    }

    // ---- Client --------------------------------------------------------------

    public override void OnStartClient()
    {
        NetworkClient.RegisterHandler<RoomAuthResponse>(OnAuthResponse, false);
    }

    public override void OnStopClient()
    {
        NetworkClient.UnregisterHandler<RoomAuthResponse>();
    }

    public override void OnClientAuthenticate()
    {
        NetworkClient.Send(new RoomAuthRequest
        {
            password = ClientPassword ?? string.Empty,
            spectator = SpectatorSession.Requested,
        });
    }

    private void OnAuthResponse(RoomAuthResponse msg)
    {
        if (msg.accepted)
        {
            ClientAccept();
            return;
        }

        // Shown by the main menu after we are sent back there.
        RoHRoomManager.LastDisconnectReason = string.IsNullOrEmpty(msg.reason) ? "Could not join the room." : msg.reason;
        ClientReject();
    }
}
