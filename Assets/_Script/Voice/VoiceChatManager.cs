using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.RTCAudio;
using EpicTransport;
using Mirror;
using UnityEngine;

/// <summary>
/// Voice chat manager. Creates itself at startup and lives for the whole game —
/// no scene has to contain it.
///
/// MAIN PATH: voice goes through MIRROR, the game's own network (VoiceNetwork):
/// mic -> ADPCM -> server decides who hears it (distance / Walkie-Talkie) ->
/// played here in 3D from the talker's body or on the radio.
///
/// EOS voice (below) stays connected as a BACKUP only: it is used when there
/// is no Mirror connection, and its flat speaker is muted while Mirror voice
/// runs. With our EOS SDK, EOS never handed voices to the game (only to its
/// own flat speaker), which is why the main path moved to Mirror.
///
/// RULES
///   - The microphone is ALWAYS ON (no push-to-talk for proximity). It is
///     muted only while this player has the Esc menu open.
///   - Which microphone: Settings > Sound > Microphone (GameSettings.MicrophoneDevice).
///
/// NOISE METER / GHOSTS
///   Every mic block also gives <see cref="MicLoudness"/> (real loudness,
///   before auto gain, with the player's mic sensitivity). PlayerNoise reads
///   it for the MIC bar. The radio playing a voice in your pocket is reported
///   to PlayerNoise as in-game noise (GAME bar).
///
/// THREADING
///   EOS may call the audio callbacks from its own audio thread, so they only
///   touch VoiceStream (plain C#, locked) and volatile fields. Everything that
///   touches Unity objects runs in Update.
///
/// IMPORTANT: only EOSSDKComponent.IsReady may be used to check EOS here.
/// EOSSDKComponent.Initialized creates a new, empty EOSSDKComponent when the
/// real one is briefly gone during a scene change, which kills EOS.
/// </summary>
[DisallowMultipleComponent]
public sealed class VoiceChatManager : MonoBehaviour
{
    public enum Status { Offline, Connecting, Live, MutedByMenu }

    private const float PollInterval = 0.5f;
    private const float ForgetSilentStreamAfter = 15f;
    private const string NetStreamPrefix = "net:";

    private static VoiceChatManager instance;

    /// <summary>For the HUD.</summary>
    public static Status CurrentStatus { get; private set; }

    /// <summary>
    /// How loud the player's microphone is right now, 0..1 (dB scale, before
    /// auto gain, mic sensitivity applied). 0 while the Esc menu is open or the
    /// mic is not recording. Read by PlayerNoise (noise meter, ghosts).
    /// </summary>
    public static float MicLoudness { get; private set; }

    // ---- EOS speaker (backup) --------------------------------------------------
    // EOS plays voices itself (flat) only when neither our playback nor Mirror
    // voice carries them.
    private bool? eosSpeakerMuted;
    private float lastRenderAudioTime = -999f;
    private long lastReceivedCount;
    private static long receivedBlocks;

    // ---- Microphone (Unity capture) ---------------------------------------------
    // The mic is recorded by Unity (VoiceMicCapture); EOS's own capture never
    // produced audio in the 25 Sep test, so it is not used.
    private readonly VoiceMicCapture mic = new VoiceMicCapture();
    private string recordingDevice;   // device the mic was started with ("" = Windows default)
    private float lastMicBlockTime = -999f;

    // ---- EOS room state (main thread only) -------------------------------------
    private string lobbyId = string.Empty;
    private string roomName;
    private bool roomConnected;
    private bool? appliedSending;
    private ulong beforeRenderNotifyId;
    private float nextPoll;

    // Held in a field so the delegate cannot be garbage-collected while EOS still calls it.
    private OnAudioBeforeRenderCallback beforeRenderCallback;

    // Streams are created on the EOS thread / network handler, playbacks on the main thread.
    private readonly List<VoiceStream> streams = new List<VoiceStream>();
    private readonly List<VoicePlayback> playbacks = new List<VoicePlayback>();
    private readonly List<VoiceStream> snapshot = new List<VoiceStream>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        CurrentStatus = Status.Offline;
        MicLoudness = 0f;
        receivedBlocks = 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateAtStartup()
    {
        if (instance != null) return;
        var go = new GameObject("Voice Chat");
        DontDestroyOnLoad(go);
        go.AddComponent<VoiceChatManager>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // F8 test panel: only in the Editor and in Development Builds, never in a release build.
        go.AddComponent<VoiceDebugOverlay>();
#endif
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        beforeRenderCallback = OnAudioBeforeRender;
        VoiceNetwork.VoiceReceived += OnNetworkVoice;
        GameSettings.Changed += OnSettingsChanged;
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        VoiceNetwork.VoiceReceived -= OnNetworkVoice;
        GameSettings.Changed -= OnSettingsChanged;
        LeaveRoom();
        mic.Stop();
        instance = null;
    }

    /// <summary>A different microphone was chosen in Settings: switch at once.</summary>
    private void OnSettingsChanged()
    {
        string wanted = GameSettings.MicrophoneDevice;
        if (mic.IsRecording && wanted != recordingDevice) StartMic();
        ApplyCleanupSettings();
    }

    /// <summary>Settings > Sound > Noise reduction / Noise gate -> the mic.</summary>
    private void ApplyCleanupSettings()
    {
        mic.NoiseReduction = GameSettings.NoiseReduction;
        mic.NoiseGate = GameSettings.NoiseGate;
        mic.GateThresholdDb = GameSettings.NoiseGateThresholdDb;
    }

    private void StartMic()
    {
        recordingDevice = GameSettings.MicrophoneDevice;
        // A saved mic that is unplugged now: fall back to the Windows default.
        string device = System.Array.IndexOf(Microphone.devices, recordingDevice) >= 0 ? recordingDevice : null;
        ApplyCleanupSettings();
        mic.Start(device);
    }

    // ---- Main loop -------------------------------------------------------------

    private void Update()
    {
        bool eosVoice = EOSSDKComponent.IsReady && EOSSDKComponent.VoiceAvailable;
        if (!eosVoice)
        {
            if (roomName != null) LeaveRoom();
            CurrentStatus = Status.Offline;
        }
        else
        {
            if (Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + PollInterval;
                PollRoom();
            }
            ApplyMicState();
        }

        VoiceNetwork.Tick();
        UpdateNetworkStats();
        if (VoiceNetwork.Active)
            CurrentStatus = PauseMenuController.IsOpen ? Status.MutedByMenu : Status.Live;

        PumpMicrophone(eosVoice && roomConnected);
        if (eosVoice && roomConnected) ApplyEosSpeaker();
        UpdatePlaybacks();
    }

    /// <summary>Follows the EOS lobby we are in: joins its voice room's audio, drops it when we leave.</summary>
    private void PollRoom()
    {
        LobbyController lobby = LobbyController.Instance;
        string currentLobby = lobby != null ? lobby.CurrentLobbyId : string.Empty;

        if (currentLobby != lobbyId)
        {
            LeaveRoom();
            lobbyId = currentLobby;
            if (!string.IsNullOrEmpty(lobbyId)) EnterRoom();
        }

        if (roomName == null) return;

        bool connected;
        Result result = EOSSDKComponent.GetLobbyInterface().IsRTCRoomConnected(new IsRTCRoomConnectedOptions
        {
            LobbyId = lobbyId,
            LocalUserId = EOSSDKComponent.LocalUserProductId
        }, out connected);

        if (result != Result.Success) connected = false;

        if (connected != roomConnected)
        {
            roomConnected = connected;
            appliedSending = null; // re-send the mic state to the new connection
            Debug.Log(connected ? "[Voice] Connected to the voice room." : "[Voice] Voice room disconnected.");
        }
    }

    private void EnterRoom()
    {
        string name;
        Result result = EOSSDKComponent.GetLobbyInterface().GetRTCRoomName(new GetRTCRoomNameOptions
        {
            LobbyId = lobbyId,
            LocalUserId = EOSSDKComponent.LocalUserProductId
        }, out name);

        if (result != Result.Success || string.IsNullOrEmpty(name))
        {
            // A lobby made before voice existed (or with voice refused) has no room.
            Debug.LogWarning("[Voice] This lobby has no voice room (" + result + "). EOS backup voice is off for this session.");
            return;
        }

        RTCAudioInterface audio = GetAudioInterface();
        if (audio == null)
        {
            Debug.LogWarning("[Voice] EOS RTC is not available on this platform. EOS backup voice is off.");
            return;
        }

        roomName = name;
        beforeRenderNotifyId = audio.AddNotifyAudioBeforeRender(new AddNotifyAudioBeforeRenderOptions
        {
            LocalUserId = EOSSDKComponent.LocalUserProductId,
            RoomName = roomName,
            UnmixedAudio = true
        }, null, beforeRenderCallback);

        Debug.Log("[Voice] Joined EOS voice (backup) for lobby " + lobbyId + ". Microphones: " +
                  (Microphone.devices.Length == 0 ? "NONE" : string.Join(" | ", Microphone.devices)));
    }

    private void LeaveRoom()
    {
        RTCAudioInterface audio = EOSSDKComponent.IsReady ? GetAudioInterface() : null;
        if (audio != null && beforeRenderNotifyId != 0) audio.RemoveNotifyAudioBeforeRender(beforeRenderNotifyId);
        beforeRenderNotifyId = 0;

        for (int i = playbacks.Count - 1; i >= 0; i--)
        {
            if (playbacks[i] != null) Destroy(playbacks[i].gameObject);
            playbacks.RemoveAt(i);
        }
        lock (streams) streams.Clear();

        roomName = null;
        roomConnected = false;
        eosSpeakerMuted = null;
        appliedSending = null;
    }

    private static RTCAudioInterface GetAudioInterface()
    {
        var rtc = EOSSDKComponent.GetRTCInterface();
        return rtc != null ? rtc.GetAudioInterface() : null;
    }

    // ---- EOS speaker: muted while Mirror voice (or our playback) carries the voices ----

    private void ApplyEosSpeaker()
    {
        long received = System.Threading.Interlocked.Read(ref receivedBlocks);
        if (received != lastReceivedCount)
        {
            lastReceivedCount = received;
            lastRenderAudioTime = Time.unscaledTime;
        }

        bool ourPlaybackWorks = Time.unscaledTime - lastRenderAudioTime < 3f;
        bool mute = ourPlaybackWorks || VoiceNetwork.Active;
        if (eosSpeakerMuted == mute) return;

        RTCAudioInterface audio = GetAudioInterface();
        if (audio == null) return;

        string deviceId = DefaultOutputDeviceId(audio);
        Result result = audio.SetAudioOutputSettings(new SetAudioOutputSettingsOptions
        {
            LocalUserId = EOSSDKComponent.LocalUserProductId,
            DeviceId = deviceId,
            Volume = mute ? 0f : 50f   // this SDK: 0 = silent, anything else = unchanged
        });
        if (result == Result.Success) eosSpeakerMuted = mute;
    }

    private static string DefaultOutputDeviceId(RTCAudioInterface audio)
    {
        uint count = audio.GetAudioOutputDevicesCount(new GetAudioOutputDevicesCountOptions());
        string first = null;
        for (uint i = 0; i < count; i++)
        {
            AudioOutputDeviceInfo info = audio.GetAudioOutputDeviceByIndex(new GetAudioOutputDeviceByIndexOptions { DeviceInfoIndex = i });
            if (info == null) continue;
            if (first == null) first = info.DeviceId;
            if (info.DefaultDevice) return info.DeviceId;
        }
        return first;
    }

    // ---- Microphone: always on, off only while the Esc menu is open ----------

    private void ApplyMicState()
    {
        bool menuOpen = PauseMenuController.IsOpen;

        if (roomName == null) { CurrentStatus = Status.Offline; return; }
        if (!roomConnected) { CurrentStatus = Status.Connecting; return; }
        CurrentStatus = menuOpen ? Status.MutedByMenu : Status.Live;

        bool wantSending = !menuOpen;
        if (appliedSending == wantSending) return;
        appliedSending = wantSending;

        RTCAudioInterface audio = GetAudioInterface();
        if (audio == null) return;

        audio.UpdateSending(new UpdateSendingOptions
        {
            LocalUserId = EOSSDKComponent.LocalUserProductId,
            RoomName = roomName,
            AudioStatus = wantSending ? RTCAudioStatus.Enabled : RTCAudioStatus.Disabled
        }, null, info =>
        {
            if (info.ResultCode != Result.Success)
            {
                Debug.LogWarning("[Voice] Could not " + (wantSending ? "open" : "mute") + " the mic: " + info.ResultCode);
                appliedSending = null; // try again next frame
            }
        });
    }

    // ---- Microphone -> Mirror voice / EOS backup / noise meter (main thread) ----

    private AudioBuffer sendBuffer;
    private SendAudioOptions sendOptions;

    /// <summary>
    /// Records with Unity and hands out 10 ms blocks: to Mirror voice, to the
    /// EOS backup, and as loudness for the noise meter. Runs while voice is
    /// possible, or whenever this machine has a player (so the noise meter also
    /// works in offline test scenes).
    /// </summary>
    private void PumpMicrophone(bool inEosRoom)
    {
        bool viaMirror = VoiceNetwork.Active;
        bool wanted = viaMirror || inEosRoom || PlayerNoise.Local != null;
        if (!wanted)
        {
            if (mic.IsRecording) mic.Stop();
            MicLoudness = 0f;
            return;
        }
        if (!mic.IsRecording) StartMic();

        // EOS only gets the mic when Mirror voice is not available (backup path).
        bool sendEos = !viaMirror && inEosRoom && appliedSending == true;
        bool micOpen = !PauseMenuController.IsOpen; // always-on mic, muted only in the Esc menu
        float sensitivityDb = GameSettings.MicSensitivityDb + PlayerNoise.MicBoostDb;
        float loudest = -1f;

        mic.Pump(block =>
        {
            // After noise reduction + gate, before auto gain (see VoiceMicCapture.MeterLevel).
            loudest = Mathf.Max(loudest, VoiceMicCapture.PeakToLoudness(mic.MeterLevel, sensitivityDb));

            if (viaMirror) VoiceNetwork.PushMicBlock(block, mic.Level, micOpen);
            if (sendEos) SendToEos(block);
        });

        // Loudness for the noise meter: 0 while muted; hold the last value for a
        // moment between blocks (frames are shorter than 10 ms at high fps).
        if (!micOpen) MicLoudness = 0f;
        else if (loudest >= 0f)
        {
            MicLoudness = loudest;
            lastMicBlockTime = Time.unscaledTime;
        }
        else if (Time.unscaledTime - lastMicBlockTime > 0.1f) MicLoudness = 0f;
    }

    private void SendToEos(short[] block)
    {
        if (sendBuffer == null)
        {
            sendBuffer = new AudioBuffer { Frames = new short[VoiceMicCapture.BlockSamples], SampleRate = VoiceMicCapture.OutputRate, Channels = 1 };
            sendOptions = new SendAudioOptions { Buffer = sendBuffer };
        }
        System.Array.Copy(block, sendBuffer.Frames, block.Length);
        sendOptions.LocalUserId = EOSSDKComponent.LocalUserProductId;
        sendOptions.RoomName = roomName;

        RTCAudioInterface audio = GetAudioInterface();
        if (audio != null) audio.SendAudio(sendOptions);
    }

    // ---- EOS callback (may run on the EOS audio thread: no Unity API here) ----

    private void OnAudioBeforeRender(AudioBeforeRenderCallbackInfo data)
    {
        AudioBuffer buffer = data.Buffer;
        if (buffer == null || buffer.Frames == null || buffer.Frames.Length == 0) return;
        System.Threading.Interlocked.Increment(ref receivedBlocks);

        // With unmixed audio EOS names the talker. If it ever sends mixed audio
        // instead, play it flat so voice still works.
        string id = data.ParticipantId != null && data.ParticipantId.IsValid()
            ? data.ParticipantId.ToString()
            : VoiceStream.MixedId;

        GetOrCreateStream(id).Write(buffer.Frames, (int)buffer.SampleRate, (int)buffer.Channels);
    }

    private VoiceStream GetOrCreateStream(string id)
    {
        lock (streams)
        {
            for (int i = 0; i < streams.Count; i++)
                if (streams[i].ParticipantId == id) return streams[i];

            var stream = new VoiceStream(id);
            streams.Add(stream);
            return stream;
        }
    }

    // ---- Info for the F8 test panel (Editor / Development Build only) -----------

    public static string MicDeviceName => instance == null ? "-" :
        string.IsNullOrEmpty(instance.recordingDevice) ? "Windows default" : instance.recordingDevice;
    public static bool MicRecording => instance != null && instance.mic.IsRecording;
    public static string MicError => instance != null ? instance.mic.LastError : "";
    public static float MicRawLevel => instance != null ? instance.mic.RawLevel : 0f;
    public static float MicMeterLevel => instance != null ? instance.mic.MeterLevel : 0f;
    public static bool MicGateOpen => instance == null || instance.mic.GateOpen;
    public static float MicCleanedDb => instance != null ? instance.mic.CleanedDb : -120f;
    public static float MicLevelAfterGain => instance != null ? instance.mic.Level : 0f;
    public static float MicGain => instance != null ? instance.mic.Gain : 1f;
    public static int NetSentPerSecond { get; private set; }
    public static int NetReceivedPerSecond { get; private set; }
    public static int NetRelayedPerSecond { get; private set; }
    private float nextStatsReset;

    /// <summary>Copy of the voices being played (main thread).</summary>
    public static List<VoicePlayback> PlaybacksSnapshot()
    {
        var copy = new List<VoicePlayback>();
        if (instance != null) copy.AddRange(instance.playbacks);
        return copy;
    }

    private void UpdateNetworkStats()
    {
        if (Time.unscaledTime < nextStatsReset) return;
        nextStatsReset = Time.unscaledTime + 1f;
        NetSentPerSecond = VoiceNetwork.SentPackets;
        NetReceivedPerSecond = VoiceNetwork.ReceivedPackets;
        NetRelayedPerSecond = VoiceNetwork.RelayedPackets;
        VoiceNetwork.ResetCounters();
    }

    // ---- Routing each voice (main thread) --------------------------------------

    private void UpdatePlaybacks()
    {
        snapshot.Clear();
        lock (streams)
        {
            for (int i = streams.Count - 1; i >= 0; i--)
            {
                // A player who left: forget them.
                if (streams[i].SecondsSinceLastAudio > ForgetSilentStreamAfter) streams.RemoveAt(i);
                else snapshot.Add(streams[i]);
            }
        }

        // Destroy playbacks whose stream is gone.
        for (int i = playbacks.Count - 1; i >= 0; i--)
        {
            if (playbacks[i] == null || !snapshot.Contains(playbacks[i].Stream))
            {
                if (playbacks[i] != null) Destroy(playbacks[i].gameObject);
                playbacks.RemoveAt(i);
            }
        }

        // In a match (we have a body) voices come from bodies. In the lobby there are none.
        bool inMatch = PlayerVoice.Local != null;
        // My round is over (results screen): the server only sends me other
        // finished players, and they are played flat like the lobby, no radio.
        bool finished = inMatch && MatchResultsUI.IsShowing;
        PlayerInventory myInventory = PlayerInventory.Local;
        bool iHearRadio = inMatch && !finished && myInventory != null && myInventory.HasPoweredRadio;
        PlayerNoise myNoise = PlayerNoise.Local;

        foreach (VoiceStream stream in snapshot)
        {
            if (stream.SampleRate <= 0) continue;

            bool fromNetwork = stream.ParticipantId.StartsWith(NetStreamPrefix);
            PlayerVoice talker = FindTalker(stream.ParticipantId);
            // Flat when there is no body to play from (lobby RoomPlayer, mixed EOS audio).
            bool flat = !inMatch || finished || stream.IsMixed || (fromNetwork && talker == null);
            Transform parent = !flat && talker != null ? talker.Mouth : transform;

            VoicePlayback playback = FindPlayback(stream);
            if (playback == null)
            {
                playback = VoicePlayback.Create(stream, parent);
                playbacks.Add(playback);
            }
            else if (playback.transform.parent != parent)
            {
                playback.transform.SetParent(parent, false);
            }

            // Match + body found: 3D at the body. Match + no body yet (still
            // spawning / not linked): silent, so nobody is heard from nowhere.
            // Lobby: flat.
            playback.SetSpatial(!flat && talker != null);
            playback.SetProximityMuted(!flat && talker == null);

            // Network voices: the SERVER already decided the radio (it only sends
            // radio audio to players carrying a switched-on walkie), so just follow it.
            bool radio = fromNetwork
                ? stream.RadioRecently && iHearRadio
                : iHearRadio && talker != null && talker != PlayerVoice.Local && talker.RadioTransmitting;

            // The walkie in YOUR pocket making sound = noise you make in the game.
            bool clicked = playback.SetRadio(radio);
            if (myNoise != null)
            {
                if (clicked) myNoise.AddSpike(PlayerNoise.RadioClickNoise);
                if (radio && stream.SecondsSinceLastAudio < 0.2f) myNoise.SetRadioOutput(stream.Level);
            }
        }
    }

    /// <summary>A voice packet from the game network (main thread).</summary>
    private void OnNetworkVoice(uint talkerNetId, byte flags, short[] samples, int count)
    {
        GetOrCreateStream(NetStreamPrefix + talkerNetId).WriteMono(samples, count, VoiceCodec.NetworkRate,
            (flags & VoiceNetwork.FlagProximity) != 0,
            (flags & VoiceNetwork.FlagRadio) != 0);
    }

    private static PlayerVoice FindTalker(string participantId)
    {
        // Game-network voice: "net:<netId>" -> that player object's PlayerVoice (null in the lobby).
        if (participantId != null && participantId.StartsWith(NetStreamPrefix))
        {
            if (uint.TryParse(participantId.Substring(NetStreamPrefix.Length), out uint netId) &&
                NetworkClient.spawned.TryGetValue(netId, out NetworkIdentity identity) && identity != null)
                return identity.GetComponent<PlayerVoice>();
            return null;
        }

        // EOS backup voice: match the EOS ProductUserId.
        List<PlayerVoice> all = PlayerVoice.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i].ProductUserId == participantId) return all[i];
        return null;
    }

    private VoicePlayback FindPlayback(VoiceStream stream)
    {
        for (int i = 0; i < playbacks.Count; i++)
            if (playbacks[i] != null && playbacks[i].Stream == stream) return playbacks[i];
        return null;
    }
}
