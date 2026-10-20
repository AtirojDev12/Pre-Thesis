using System.Collections;
using System.Collections.Generic;
using EpicTransport;
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
///
/// 20 Oct (Mr.k): the seat lives for the whole room (lobby AND match), so it also
/// carries what the Tab player list needs: ping (server measures it), the EOS id
/// (stable key for each friend's voice volume) and the netId of the object that
/// talks for this player (voice stream "net:&lt;netId&gt;").
/// </summary>
[DisallowMultipleComponent]
public class RoHRoomPlayer : NetworkRoomPlayer
{
    public const int MaxNameLength = 16;

    [SyncVar] private string displayName = string.Empty;
    [SyncVar] private bool isHost;
    // True when the HOST runs the Editor or a Development Build (2 Oct). Dev cheats
    // that need the server are only sent then: a normal-build host has no handler
    // for them and Mirror would disconnect the sender.
    [SyncVar] private bool hostIsDevBuild;

    // 20 Oct: Tab player list. All written by the SERVER only.
    [SyncVar] private ushort pingMs;                       // round trip, from the server's own measure
    [SyncVar] private string productUserId = string.Empty; // EOS id (owner sends it once)
    [SyncVar] private uint talkerNetId;                    // this connection's main object (lobby body / match body)
    private float nextServerRefresh;

    /// <summary>Every seat on this machine (lobby and match). No FindObjects needed.</summary>
    public static readonly List<RoHRoomPlayer> All = new List<RoHRoomPlayer>();

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? $"Player {index + 1}" : displayName;
    public bool IsHost => isHost;
    public int PingMs => pingMs;
    public string ProductUserId => productUserId;
    public uint TalkerNetId => talkerNetId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => All.Clear();

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!All.Contains(this)) All.Add(this);
    }

    public override void OnStopClient()
    {
        All.Remove(this);
        base.OnStopClient();
    }

    private void OnDestroy() => All.Remove(this);

    // SERVER: once a second, copy the connection's ping and main object. SyncVars only
    // send when the value really changes, so this costs almost nothing.
    private void Update()
    {
        if (!isServer || Time.unscaledTime < nextServerRefresh) return;
        nextServerRefresh = Time.unscaledTime + 1f;
        NetworkConnectionToClient conn = connectionToClient;
        if (conn == null) return;
        int ms = conn is LocalConnectionToClient ? 0 : Mathf.RoundToInt((float)(conn.rtt * 1000.0));
        ushort clamped = (ushort)Mathf.Clamp(ms, 0, 9999);
        if (clamped != pingMs) pingMs = clamped;
        uint talker = conn.identity != null ? conn.identity.netId : 0;
        if (talker != talkerNetId) talkerNetId = talker;
    }

    public override void OnStartServer()
    {
        // The host's own connection is the in-process local one.
        isHost = connectionToClient is LocalConnectionToClient;
        hostIsDevBuild = Debug.isDebugBuild;
    }

    /// <summary>Every machine: does the host run the Editor / a Development Build?</summary>
    public static bool HostIsDevBuild
    {
        get
        {
            if (NetworkServer.active || NetworkMode.IsOffline) return Debug.isDebugBuild;
            foreach (RoHRoomPlayer seat in FindObjectsByType<RoHRoomPlayer>())
                if (seat != null && seat.isHost) return seat.hostIsDevBuild;
            return false;
        }
    }

    // OnStartAuthority, not OnStartLocalPlayer: in the 3D lobby the walking
    // body is the main (local) player and this seat is only OWNED.
    public override void OnStartAuthority()
    {
        CmdSetName(GameSettings.PlayerName);
        StartCoroutine(SendProductUserIdWhenReady());
    }

    private IEnumerator SendProductUserIdWhenReady()
    {
        // Same as PlayerVoice: EOS may still be logging in.
        float giveUp = Time.unscaledTime + 30f;
        while (!EOSSDKComponent.IsReady || string.IsNullOrEmpty(EOSSDKComponent.LocalUserProductIdString))
        {
            if (Time.unscaledTime > giveUp) yield break; // name is the fallback key
            yield return null;
        }
        CmdSetProductUserId(EOSSDKComponent.LocalUserProductIdString);
    }

    [Command]
    private void CmdSetProductUserId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64) return;
        productUserId = id;
    }

    [Command]
    private void CmdSetName(string requested)
    {
        displayName = SanitizeName(requested);
    }

    /// <summary>Called by the lobby UI's Ready button on the local player.</summary>
    public void SetReady(bool ready)
    {
        if (!isOwned) return; // owned seat (the body is the local player in the 3D lobby)
        CmdChangeReadyState(ready);
    }

    /// <summary>
    /// SERVER -> the kicked guest only. Sets the message the main menu shows
    /// ("You were kicked by the host."); RoHRoomManager disconnects them right after.
    /// </summary>
    [TargetRpc]
    public void TargetKicked(NetworkConnectionToClient target)
    {
        RoHRoomManager.LastDisconnectReason = "You were kicked by the host.";
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
