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
    [SyncVar] private int entryMinutes;                    // 17 Nov: host's seat only: entry time (minutes before 00:00)
    [SyncVar] private byte shirtColor;                     // 25 Nov: OUTER shirt (ShirtColors index), never the same as another seat
    [SyncVar] private byte innerColor;                     // 25 Nov: INNER t-shirt (ShirtColors index), free (may repeat)
    private float nextServerRefresh;

    /// <summary>Every seat on this machine (lobby and match). No FindObjects needed.</summary>
    public static readonly List<RoHRoomPlayer> All = new List<RoHRoomPlayer>();

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? $"Player {index + 1}" : displayName;
    public bool IsHost => isHost;
    public int PingMs => pingMs;
    public string ProductUserId => productUserId;
    public uint TalkerNetId => talkerNetId;
    /// <summary>17 Nov (Task 8): the entry time the host picked (minutes before 00:00). Read it on the host's seat.</summary>
    public int EntryMinutes => entryMinutes;
    /// <summary>25 Nov: this player's OUTER shirt colour (ShirtColors index, unique). Lasts for the whole room (lobby + match).</summary>
    public int ShirtColor => shirtColor;
    /// <summary>25 Nov: this player's INNER t-shirt colour (ShirtColors index, may repeat).</summary>
    public int InnerColor => innerColor;

    /// <summary>OWNER. Inner t-shirt colour (any colour, repeats allowed).</summary>
    public void RequestInner(int colour)
    {
        if (isOwned && colour >= 0 && colour < ShirtColors.Count) CmdSetInner((byte)colour);
    }

    [Command]
    private void CmdSetInner(byte colour)
    {
        if (colour < ShirtColors.Count) innerColor = colour;
    }

    /// <summary>25 Nov: the seat this PC owns (null offline / not joined yet).</summary>
    public static RoHRoomPlayer LocalSeat
    {
        get
        {
            foreach (RoHRoomPlayer seat in All) if (seat != null && seat.isOwned) return seat;
            return null;
        }
    }

    /// <summary>Every machine: does another seat already wear this colour?</summary>
    public static bool ShirtTaken(int colour, RoHRoomPlayer except)
    {
        foreach (RoHRoomPlayer seat in All) if (seat != null && seat != except && seat.shirtColor == colour) return true;
        return false;
    }

    // SERVER: every seat of the room (the server's own list, also on a host).
    private static readonly List<RoHRoomPlayer> serverSeats = new List<RoHRoomPlayer>();

    /// <summary>SERVER. First colour nobody wears (White first).</summary>
    private static byte FirstFreeShirt(RoHRoomPlayer except)
    {
        for (int c = 0; c < ShirtColors.Count; c++)
        {
            bool used = false;
            foreach (RoHRoomPlayer seat in serverSeats) if (seat != null && seat != except && seat.shirtColor == c) { used = true; break; }
            if (!used) return (byte)c;
        }
        return 0;
    }

    /// <summary>OWNER. Ask to wear this colour (the server says no when someone else wears it).</summary>
    public void RequestShirt(int colour)
    {
        if (isOwned && colour >= 0 && colour < ShirtColors.Count) CmdSetShirt((byte)colour);
    }

    [Command]
    private void CmdSetShirt(byte colour)
    {
        if (colour >= ShirtColors.Count || colour == shirtColor) return;
        foreach (RoHRoomPlayer seat in serverSeats) if (seat != null && seat != this && seat.shirtColor == colour) return; // taken
        shirtColor = colour;
    }

    /// <summary>SERVER (host). 17 Nov: next entry time on the Lobby Board (23:00 -> 23:15 -> ... -> 00:00 -> 23:00).</summary>
    public void ServerNextEntryTime()
    {
        if (!NetworkServer.active || !isHost) return;
        int[] choices = RoomConfig.EntryChoices;
        int at = System.Array.IndexOf(choices, RoomConfig.CleanEntry(entryMinutes));
        entryMinutes = choices[(at + 1) % choices.Length];
    }

    /// <summary>SERVER (host). 17 Nov: set the entry time directly (picked from the Tab menu list).</summary>
    public void ServerSetEntryTime(int minutesBefore)
    {
        if (!NetworkServer.active || !isHost) return;
        entryMinutes = RoomConfig.CleanEntry(minutesBefore);
    }

    /// <summary>Every machine: the host's entry time (0 when there is no host seat).</summary>
    public static int HostEntryMinutes
    {
        get
        {
            foreach (RoHRoomPlayer seat in All) if (seat != null && seat.isHost) return RoomConfig.CleanEntry(seat.entryMinutes);
            return 0;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { All.Clear(); serverSeats.Clear(); }

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

    private void OnDestroy() { All.Remove(this); serverSeats.Remove(this); }

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
        // 25 Nov: a new player gets the first shirt colour nobody wears (White first).
        shirtColor = FirstFreeShirt(this);
        if (!serverSeats.Contains(this)) serverSeats.Add(this);
    }

    public override void OnStopServer()
    {
        serverSeats.Remove(this); // the colour is free again
        base.OnStopServer();
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
