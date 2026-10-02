using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>Client -> server: 40 ms of my voice (VoiceCodec).</summary>
public struct VoiceUpMessage : NetworkMessage
{
    public ushort sequence;
    public ArraySegment<byte> data;
}

/// <summary>Server -> client: 40 ms of someone's voice, with how to play it.</summary>
public struct VoiceDownMessage : NetworkMessage
{
    /// <summary>netId of the talker's player object (Player in a match, RoomPlayer in the lobby).</summary>
    public uint talkerNetId;
    public ushort sequence;
    /// <summary>VoiceNetwork.FlagProximity / FlagRadio.</summary>
    public byte flags;
    public ArraySegment<byte> data;
}

/// <summary>Spectator -> server (2 Oct): push-to-talk audio + where the spectator camera is.</summary>
public struct SpectatorVoiceUpMessage : NetworkMessage
{
    public ushort sequence;
    public Vector3 position;
    public ArraySegment<byte> data;
}

/// <summary>Server -> player: a spectator's voice, played in 3D at <see cref="position"/>.</summary>
public struct SpectatorVoiceDownMessage : NetworkMessage
{
    /// <summary>The spectator's connection id on the server (one stream per spectator).</summary>
    public int spectatorId;
    public ushort sequence;
    public Vector3 position;
    public ArraySegment<byte> data;
}

/// <summary>
/// Voice through Mirror (the game's own network) instead of EOS.
///
/// WHY: EOS delivers voices only to its own speaker (flat, no 3D) with the EOS
/// SDK version we ship; it never handed the audio to the game. Through Mirror
/// we have the audio ourselves, so it plays in 3D from the talker's body and
/// the Walkie-Talkie works.
///
/// FLOW
///   mic (VoiceMicCapture, 48 kHz) -> 16 kHz ADPCM, 40 ms packets, only while
///   you speak -> VoiceUpMessage (unreliable) -> SERVER decides who hears it:
///     - lobby (no bodies): everyone, flat
///     - match: players within <see cref="VoicePlayback.MaxHearingDistance"/> (+margin)
///       get it as PROXIMITY; players carrying a switched-on Walkie-Talkie get it
///       as RADIO while the talker transmits
///   -> VoiceDownMessage -> VoiceChatManager plays it (3D body voice / radio).
///
/// SERVER AUTHORITY: the client only says "here is my audio". The server stamps
/// who sent it, checks the radio rules (PlayerVoice.RadioTransmitting, which the
/// server itself validated) and filters by distance, so a modified client cannot
/// talk on the radio without a walkie or be heard across the map. The distance
/// filter also saves bandwidth: far-away players are not sent your voice at all.
/// </summary>
public static class VoiceNetwork
{
    public const byte FlagProximity = 1;
    public const byte FlagRadio = 2;

    /// <summary>Packet = 40 ms at 16 kHz.</summary>
    public const int PacketSamples = VoiceCodec.NetworkRate / 25; // 640
    private const int MaxPacketBytes = 512;                        // 324 expected; reject anything odd
    private const int MaxPacketsPerSecond = 40;                    // 25 expected
    private const float HearingMargin = 5f;                        // server sends a bit beyond the audible edge

    // ---- Voice activity (only send while speaking) --------------------------------
    private const float OpenLevel = 0.06f;    // block peak (after auto gain) that opens the mic
    private const float HangoverSeconds = 0.4f; // keep sending after the last loud block (word endings)

    // ---- Stats for the F8 panel -------------------------------------------------
    public static int SentPackets { get; private set; }
    public static int ReceivedPackets { get; private set; }
    public static int RelayedPackets { get; private set; }
    public static bool Speaking { get; private set; }

    /// <summary>Raised on a client for every received packet (main thread).</summary>
    public static event Action<uint, byte, short[], int> VoiceReceived; // talkerNetId, flags, samples, count

    /// <summary>Raised on a PLAYER's client for a spectator's voice (main thread).</summary>
    public static event Action<int, Vector3, short[], int> SpectatorVoiceReceived; // spectatorId, position, samples, count

    private static bool serverRegistered, clientRegistered;

    // Client encoder state.
    private static readonly short[] down = new short[PacketSamples];
    private static int downFill;
    private static readonly byte[] encoded = new byte[VoiceCodec.EncodedSize(PacketSamples)];
    private static ushort sequence;
    private static float lastLoudTime = -999f;

    // Client decoder scratch.
    private static readonly short[] decoded = new short[PacketSamples * 2];
    private static readonly byte[] receiveCopy = new byte[MaxPacketBytes];

    // Server rate limiting: connectionId -> packets in the current second. A List, not a Dictionary.
    private struct RateEntry { public int connectionId; public int count; }
    private static readonly List<RateEntry> rates = new List<RateEntry>();
    private static float rateWindowStart;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        serverRegistered = clientRegistered = false;
        VoiceReceived = null;
        SpectatorVoiceReceived = null;
        SentPackets = ReceivedPackets = RelayedPackets = 0;
        Speaking = false;
        downFill = 0;
        sequence = 0;
        rates.Clear();
    }

    /// <summary>True when voice can go through Mirror right now.</summary>
    public static bool Active => NetworkClient.isConnected;

    /// <summary>
    /// Called every frame by VoiceChatManager. Registers the message handlers when
    /// a server / client starts (Mirror clears them when it shuts down).
    /// </summary>
    public static void Tick()
    {
        if (NetworkServer.active && !serverRegistered)
        {
            NetworkServer.ReplaceHandler<VoiceUpMessage>(OnServerVoice, true);
            NetworkServer.ReplaceHandler<SpectatorVoiceUpMessage>(OnServerSpectatorVoice, true);
            serverRegistered = true;
            rates.Clear();
        }
        else if (!NetworkServer.active) serverRegistered = false;

        if (NetworkClient.active && !clientRegistered)
        {
            NetworkClient.ReplaceHandler<VoiceDownMessage>(OnClientVoice, true);
            NetworkClient.ReplaceHandler<SpectatorVoiceDownMessage>(OnClientSpectatorVoice, true);
            clientRegistered = true;
        }
        else if (!NetworkClient.active) clientRegistered = false;
    }

    /// <summary>Resets the per-second counters for the panel (call once per second).</summary>
    public static void ResetCounters() => SentPackets = ReceivedPackets = RelayedPackets = 0;

    // =========================================================================
    //  CLIENT: send my voice
    // =========================================================================

    /// <summary>
    /// One 10 ms block from VoiceMicCapture (48 kHz). <paramref name="allowed"/> = false
    /// while the Esc menu is open (always-on mic is muted there).
    /// </summary>
    public static void PushMicBlock(short[] block48k, float blockPeak, bool allowed)
    {
        // Spectator (2 Oct): push-to-talk only; while the key is held everything is sent.
        bool spectator = SpectatorSession.Active;
        if (spectator) allowed = allowed && SpectatorSession.TalkHeld;

        if (!Active || !allowed)
        {
            downFill = 0;
            Speaking = false;
            return;
        }

        if (blockPeak >= OpenLevel) lastLoudTime = Time.unscaledTime;
        Speaking = spectator || Time.unscaledTime - lastLoudTime < HangoverSeconds;

        downFill += VoiceCodec.Downsample48To16(block48k, block48k.Length, down, downFill);
        if (downFill < PacketSamples) return;
        downFill = 0;

        if (!Speaking) return; // silence: send nothing

        int bytes = VoiceCodec.Encode(down, PacketSamples, encoded);
        if (spectator)
        {
            NetworkClient.Send(new SpectatorVoiceUpMessage
            {
                sequence = sequence++,
                position = SpectatorSession.VoicePosition,
                data = new ArraySegment<byte>(encoded, 0, bytes)
            }, Channels.Unreliable);
            SentPackets++;
            return;
        }
        NetworkClient.Send(new VoiceUpMessage
        {
            sequence = sequence++,
            data = new ArraySegment<byte>(encoded, 0, bytes)
        }, Channels.Unreliable);
        SentPackets++;
    }

    // =========================================================================
    //  SERVER: decide who hears it
    // =========================================================================

    private static void OnServerVoice(NetworkConnectionToClient sender, VoiceUpMessage message)
    {
        if (sender == null || sender.identity == null) return;
        if (message.data.Count <= VoiceCodec.HeaderBytes || message.data.Count > MaxPacketBytes) return;
        if (!AllowRate(sender.connectionId)) return;

        NetworkIdentity talker = sender.identity;
        PlayerVoice talkerVoice = talker.GetComponent<PlayerVoice>();
        bool inMatch = talkerVoice != null;
        bool onRadio = inMatch && talkerVoice.RadioTransmitting;
        float maxDistance = VoicePlayback.MaxHearingDistance + HearingMargin;
        Vector3 talkerPosition = talker.transform.position;

        // After their round is over (escaped / dead / round ended) players talk
        // only to each other, heard flat like the lobby. Players still inside
        // cannot hear them, and they cannot hear the players still inside.
        MatchDirector director = inMatch ? MatchDirector.Instance : null;
        bool talkerFinished = director != null && director.ServerIsFinished(talker);

        foreach (NetworkConnectionToClient listener in NetworkServer.connections.Values)
        {
            if (listener == null || listener == sender || !listener.isReady) continue;

            // Spectator (2 Oct, no body): hears every talker, 3D at the talker
            // (their camera holds the listener), plus radio traffic.
            if (listener.identity == null)
            {
                if (!RoHRoomManager.IsSpectator(listener)) continue;
                listener.Send(new VoiceDownMessage
                {
                    talkerNetId = talker.netId,
                    sequence = message.sequence,
                    flags = (byte)(FlagProximity | (onRadio ? FlagRadio : 0)),
                    data = message.data
                }, Channels.Unreliable);
                RelayedPackets++;
                continue;
            }

            byte flags;
            if (!inMatch)
            {
                flags = FlagProximity; // lobby: everyone hears everyone (played flat)
            }
            else if (director != null && (talkerFinished || director.ServerIsFinished(listener.identity)))
            {
                // Both finished: heard at any distance (played flat). Only one: not sent.
                if (!talkerFinished || !director.ServerIsFinished(listener.identity)) continue;
                flags = FlagProximity;
            }
            else
            {
                flags = 0;
                if ((listener.identity.transform.position - talkerPosition).sqrMagnitude <= maxDistance * maxDistance)
                    flags |= FlagProximity;

                if (onRadio)
                {
                    PlayerInventory inventory = listener.identity.GetComponent<PlayerInventory>();
                    if (inventory != null && inventory.HasPoweredRadio) flags |= FlagRadio;
                }
            }

            if (flags == 0) continue; // too far and not on the radio: do not send at all

            listener.Send(new VoiceDownMessage
            {
                talkerNetId = talker.netId,
                sequence = message.sequence,
                flags = flags,
                data = message.data
            }, Channels.Unreliable);
            RelayedPackets++;
        }
    }

    /// <summary>
    /// SERVER. A spectator's push-to-talk: sent to PLAYERS (with a body) near the
    /// spectator camera (played 3D there) and to every OTHER SPECTATOR (played
    /// flat, any distance).
    /// </summary>
    private static void OnServerSpectatorVoice(NetworkConnectionToClient sender, SpectatorVoiceUpMessage message)
    {
        if (sender == null || !RoHRoomManager.IsSpectator(sender)) return;
        if (message.data.Count <= VoiceCodec.HeaderBytes || message.data.Count > MaxPacketBytes) return;
        Vector3 position = message.position;
        if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z)) return;
        if (!AllowRate(sender.connectionId)) return;

        float maxDistance = VoicePlayback.MaxHearingDistance + HearingMargin;
        foreach (NetworkConnectionToClient listener in NetworkServer.connections.Values)
        {
            if (listener == null || listener == sender || !listener.isReady) continue;
            if (listener.identity == null)
            {
                if (!RoHRoomManager.IsSpectator(listener)) continue; // other spectators: any distance
            }
            else if ((listener.identity.transform.position - position).sqrMagnitude > maxDistance * maxDistance) continue;

            listener.Send(new SpectatorVoiceDownMessage
            {
                spectatorId = sender.connectionId,
                sequence = message.sequence,
                position = position,
                data = message.data
            }, Channels.Unreliable);
            RelayedPackets++;
        }
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool AllowRate(int connectionId)
    {
        if (Time.unscaledTime - rateWindowStart >= 1f)
        {
            rateWindowStart = Time.unscaledTime;
            rates.Clear();
        }
        for (int i = 0; i < rates.Count; i++)
        {
            if (rates[i].connectionId != connectionId) continue;
            RateEntry entry = rates[i];
            entry.count++;
            rates[i] = entry;
            return entry.count <= MaxPacketsPerSecond;
        }
        rates.Add(new RateEntry { connectionId = connectionId, count = 1 });
        return true;
    }

    // =========================================================================
    //  CLIENT: receive
    // =========================================================================

    private static void OnClientVoice(VoiceDownMessage message)
    {
        int length = message.data.Count;
        if (length <= VoiceCodec.HeaderBytes || length > MaxPacketBytes) return;

        // The segment is only valid during this call: copy, then decode.
        Buffer.BlockCopy(message.data.Array, message.data.Offset, receiveCopy, 0, length);
        int samples = VoiceCodec.Decode(receiveCopy, 0, length, decoded);
        if (samples <= 0) return;

        ReceivedPackets++;
        VoiceReceived?.Invoke(message.talkerNetId, message.flags, decoded, samples);
    }

    private static void OnClientSpectatorVoice(SpectatorVoiceDownMessage message)
    {
        int length = message.data.Count;
        if (length <= VoiceCodec.HeaderBytes || length > MaxPacketBytes) return;

        Buffer.BlockCopy(message.data.Array, message.data.Offset, receiveCopy, 0, length);
        int samples = VoiceCodec.Decode(receiveCopy, 0, length, decoded);
        if (samples <= 0) return;

        ReceivedPackets++;
        SpectatorVoiceReceived?.Invoke(message.spectatorId, message.position, decoded, samples);
    }
}
