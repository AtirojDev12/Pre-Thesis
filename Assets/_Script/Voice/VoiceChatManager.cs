using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.RTCAudio;
using EpicTransport;
using UnityEngine;

/// <summary>
/// Voice chat through EOS Voice (RTC), the free voice service in the EOS SDK
/// we already ship. Creates itself at startup and lives for the whole game —
/// no scene has to contain it.
///
/// HOW IT WORKS
///   1. Every EOS lobby is created / joined with a voice room
///      (EOSLobby: EnableRTCRoom + manual audio output).
///   2. "Manual audio output" means EOS does NOT play voices itself. Instead
///      it hands us every player's voice separately (AudioBeforeRender,
///      unmixed), and we play each one through Unity audio:
///        - in a match:   3D from that player's mouth (proximity voice),
///                        plus a radio copy when they talk on a Walkie-Talkie
///                        and you carry one that is switched on;
///        - in the lobby: flat 2D (there are no bodies yet).
///   3. The microphone is ALWAYS ON (no push-to-talk for proximity). It is
///      muted only while this player has the Esc menu open.
///
/// VOICE TEST (F8): an on-screen panel that shows where the sound stops
/// (mic → EOS → other PC → speakers). F5 = hear yourself, F6 = hear everyone
/// loud with no distance. See <see cref="VoiceDebugOverlay"/>.
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
    private const string SelfId = "__self__";

    private static VoiceChatManager instance;

    /// <summary>For the HUD.</summary>
    public static Status CurrentStatus { get; private set; }

    // ---- Voice test switches (F5 / F6 in the F8 panel) -------------------------
    /// <summary>Play your own mic back to you (tests mic + speakers without a second PC).</summary>
    public static bool DebugHearSelf;
    /// <summary>Every voice flat and loud: no distance, no walls, no body needed.</summary>
    public static bool DebugHearEveryone;

    // ---- Diagnostics (read by VoiceDebugOverlay) --------------------------------
    public static string RoomNameForDebug => instance != null ? instance.roomName : null;
    public static bool RoomConnected => instance != null && instance.roomConnected;
    public static bool? MicSending => instance != null ? instance.appliedSending : null;
    public static string LastSendResult { get; private set; } = "-";
    private static volatile string micInputStatus = "unknown";
    public static string MicInputStatus => micInputStatus;
    private static volatile float micLevel;
    public static float MicLevel => micLevel;
    private static long micBlocks;
    public static long MicBlocks => System.Threading.Interlocked.Read(ref micBlocks);
    private static long receivedBlocks;
    public static long ReceivedBlocks => System.Threading.Interlocked.Read(ref receivedBlocks);

    // ---- Microphone (Unity capture, F7 in the voice test panel) ----------------
    // The mic is recorded by Unity (VoiceMicCapture) and pushed to EOS with
    // SendAudio ("manual audio input"). EOS's own capture never produced audio
    // in the 25 Sep test, so we do not rely on it.
    private readonly VoiceMicCapture mic = new VoiceMicCapture();
    private static string chosenMic;                  // null = Windows default
    private static long sendAudioFailures;
    public static string LastSendAudioResult { get; private set; } = "-";
    public static long SendAudioFailures => System.Threading.Interlocked.Read(ref sendAudioFailures);
    public static string MicDeviceForDebug => instance != null ? instance.mic.DeviceName + " @" + instance.mic.DeviceRate + " Hz" + (instance.mic.IsRecording ? "" : " (not recording)") : "-";
    public static string MicErrorForDebug => instance != null ? instance.mic.LastError : "";

    /// <summary>Mic list for the panel: "&gt;" = in use.</summary>
    public static List<string> InputDeviceLines()
    {
        var lines = new List<string>();
        lines.Add((chosenMic == null ? "> " : "  ") + "(Windows default)");
        foreach (string name in Microphone.devices)
            lines.Add((name == chosenMic ? "> " : "  ") + name);
        return lines;
    }

    /// <summary>F7: use the next microphone (Windows default first, then each device).</summary>
    public static void UseNextInputDevice()
    {
        string[] devices = Microphone.devices;
        int index = chosenMic == null ? -1 : System.Array.IndexOf(devices, chosenMic);
        index++;
        chosenMic = index >= devices.Length ? null : devices[index];
        if (instance != null && instance.mic.IsRecording) instance.mic.Start(chosenMic);
        Debug.Log("[Voice] Microphone: " + (chosenMic ?? "(Windows default)"));
    }

    public sealed class ParticipantInfo
    {
        public string id;
        public bool speaking;
        public string audioStatus;
    }
    private static readonly List<ParticipantInfo> participants = new List<ParticipantInfo>();

    /// <summary>Copy of the EOS participant list (main thread).</summary>
    public static List<ParticipantInfo> ParticipantsSnapshot()
    {
        lock (participants) return new List<ParticipantInfo>(participants);
    }

    /// <summary>Copy of the incoming voices (main thread).</summary>
    public static List<VoiceStream> StreamsSnapshot()
    {
        var copy = new List<VoiceStream>();
        if (instance == null) return copy;
        lock (instance.streams) copy.AddRange(instance.streams);
        return copy;
    }

    // ---- EOS room state (main thread only) -------------------------------------
    private string lobbyId = string.Empty;
    private string roomName;
    private bool roomConnected;
    private bool? appliedSending;
    private ulong beforeRenderNotifyId, beforeSendNotifyId, inputStateNotifyId, participantNotifyId;
    private float nextPoll;

    // Held in fields so the delegates cannot be garbage-collected while EOS still calls them.
    private OnAudioBeforeRenderCallback beforeRenderCallback;
    private OnAudioBeforeSendCallback beforeSendCallback;
    private OnAudioInputStateCallback inputStateCallback;
    private OnParticipantUpdatedCallback participantCallback;

    // Streams are created on the EOS thread, playbacks on the main thread.
    private readonly List<VoiceStream> streams = new List<VoiceStream>();
    private readonly List<VoicePlayback> playbacks = new List<VoicePlayback>();
    private readonly List<VoiceStream> snapshot = new List<VoiceStream>();
    private readonly VoiceStream selfStream = new VoiceStream(SelfId);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        CurrentStatus = Status.Offline;
        DebugHearSelf = false;
        DebugHearEveryone = false;
        micInputStatus = "no event yet";
        chosenMic = null;
        sendAudioFailures = 0;
        LastSendAudioResult = "-";
        micLevel = 0f;
        micBlocks = 0;
        receivedBlocks = 0;
        LastSendResult = "-";
        lock (participants) participants.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateAtStartup()
    {
        if (instance != null) return;
        var go = new GameObject("Voice Chat");
        DontDestroyOnLoad(go);
        go.AddComponent<VoiceChatManager>();
        go.AddComponent<VoiceDebugOverlay>();
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
        beforeSendCallback = OnAudioBeforeSend;
        inputStateCallback = OnAudioInputState;
        participantCallback = OnParticipantUpdated;
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        LeaveRoom();
        mic.Stop();
        instance = null;
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

        PumpMicrophone(eosVoice && roomConnected);
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
            // A lobby made before this change (or with voice refused) has no room.
            Debug.LogWarning("[Voice] This lobby has no voice room (" + result + "). Voice is off for this session.");
            return;
        }

        RTCAudioInterface audio = GetAudioInterface();
        if (audio == null)
        {
            Debug.LogWarning("[Voice] EOS RTC is not available on this platform. Voice is off.");
            return;
        }

        roomName = name;
        ProductUserId me = EOSSDKComponent.LocalUserProductId;

        beforeRenderNotifyId = audio.AddNotifyAudioBeforeRender(new AddNotifyAudioBeforeRenderOptions
        {
            LocalUserId = me,
            RoomName = roomName,
            UnmixedAudio = true
        }, null, beforeRenderCallback);

        inputStateNotifyId = audio.AddNotifyAudioInputState(new AddNotifyAudioInputStateOptions
        {
            LocalUserId = me,
            RoomName = roomName
        }, null, inputStateCallback);

        participantNotifyId = audio.AddNotifyParticipantUpdated(new AddNotifyParticipantUpdatedOptions
        {
            LocalUserId = me,
            RoomName = roomName
        }, null, participantCallback);

        micInputStatus = "no event yet";
        Debug.Log("[Voice] Microphones Unity can see: " +
                  (Microphone.devices.Length == 0 ? "NONE" : string.Join(" | ", Microphone.devices)));

        Debug.Log("[Voice] Joined voice for lobby " + lobbyId + " (render notify " + beforeRenderNotifyId +
                  "). Press F8 for the voice test panel.");
    }

    private void LeaveRoom()
    {
        RTCAudioInterface audio = EOSSDKComponent.IsReady ? GetAudioInterface() : null;
        if (audio != null)
        {
            if (beforeRenderNotifyId != 0) audio.RemoveNotifyAudioBeforeRender(beforeRenderNotifyId);
            if (beforeSendNotifyId != 0) audio.RemoveNotifyAudioBeforeSend(beforeSendNotifyId);
            if (inputStateNotifyId != 0) audio.RemoveNotifyAudioInputState(inputStateNotifyId);
            if (participantNotifyId != 0) audio.RemoveNotifyParticipantUpdated(participantNotifyId);
        }
        beforeRenderNotifyId = beforeSendNotifyId = inputStateNotifyId = participantNotifyId = 0;

        for (int i = playbacks.Count - 1; i >= 0; i--)
        {
            if (playbacks[i] != null && playbacks[i].Stream != selfStream) Destroy(playbacks[i].gameObject);
            if (playbacks[i] == null || playbacks[i].Stream != selfStream) playbacks.RemoveAt(i);
        }
        lock (streams) streams.Clear();
        lock (participants) participants.Clear();

        roomName = null;
        roomConnected = false;
        appliedSending = null;
        micInputStatus = "not in a voice room";
        if (!DebugHearSelf) mic.Stop();
    }

    private static RTCAudioInterface GetAudioInterface()
    {
        var rtc = EOSSDKComponent.GetRTCInterface();
        return rtc != null ? rtc.GetAudioInterface() : null;
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
            LastSendResult = info.ResultCode + " (" + info.AudioStatus + ")";
            if (info.ResultCode != Result.Success)
            {
                Debug.LogWarning("[Voice] Could not " + (wantSending ? "open" : "mute") + " the mic: " + info.ResultCode);
                appliedSending = null; // try again next frame
            }
        });
    }

    // ---- Microphone -> EOS (main thread) -----------------------------------------

    private AudioBuffer sendBuffer;
    private SendAudioOptions sendOptions;

    /// <summary>
    /// Records with Unity and pushes 10 ms blocks to EOS. Runs while in a voice
    /// room, or while "hear myself" is on (so the mic can be tested alone).
    /// </summary>
    private void PumpMicrophone(bool inRoom)
    {
        bool wanted = inRoom || DebugHearSelf;
        if (!wanted)
        {
            if (mic.IsRecording) mic.Stop();
            micLevel = 0f;
            return;
        }
        if (!mic.IsRecording) mic.Start(chosenMic);

        bool send = inRoom && appliedSending == true;
        mic.Pump(block =>
        {
            System.Threading.Interlocked.Increment(ref micBlocks);
            micLevel = mic.Level;

            if (DebugHearSelf) selfStream.Write(block, VoiceMicCapture.OutputRate, 1);
            if (!send) return;

            if (sendBuffer == null)
            {
                sendBuffer = new AudioBuffer { Frames = new short[VoiceMicCapture.BlockSamples], SampleRate = VoiceMicCapture.OutputRate, Channels = 1 };
                sendOptions = new SendAudioOptions { Buffer = sendBuffer };
            }
            System.Array.Copy(block, sendBuffer.Frames, block.Length);
            sendOptions.LocalUserId = EOSSDKComponent.LocalUserProductId;
            sendOptions.RoomName = roomName;

            Result result = GetAudioInterface().SendAudio(sendOptions);
            if (result != Result.Success)
            {
                System.Threading.Interlocked.Increment(ref sendAudioFailures);
                if (LastSendAudioResult != result.ToString())
                    Debug.LogWarning("[Voice] SendAudio failed: " + result);
            }
            LastSendAudioResult = result.ToString();
        });
    }

    // ---- EOS callbacks (may run on the EOS audio thread: no Unity API here) ----

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

        VoiceStream stream = null;
        lock (streams)
        {
            for (int i = 0; i < streams.Count; i++)
                if (streams[i].ParticipantId == id) { stream = streams[i]; break; }

            if (stream == null)
            {
                stream = new VoiceStream(id);
                streams.Add(stream);
            }
        }

        stream.Write(buffer.Frames, (int)buffer.SampleRate, (int)buffer.Channels);
    }

    /// <summary>Your own mic, just before EOS sends it. Used for the level meter and the "hear yourself" test.</summary>
    private void OnAudioBeforeSend(AudioBeforeSendCallbackInfo data)
    {
        AudioBuffer buffer = data.Buffer;
        if (buffer == null || buffer.Frames == null || buffer.Frames.Length == 0) return;
        System.Threading.Interlocked.Increment(ref micBlocks);

        int peak = 0;
        short[] frames = buffer.Frames;
        for (int i = 0; i < frames.Length; i++)
        {
            int v = frames[i] < 0 ? -frames[i] : frames[i];
            if (v > peak) peak = v;
        }
        micLevel = peak / 32768f;

        if (DebugHearSelf) selfStream.Write(frames, (int)buffer.SampleRate, (int)buffer.Channels);
    }

    private void OnAudioInputState(AudioInputStateCallbackInfo data) => micInputStatus = data.Status.ToString();

    private void OnParticipantUpdated(ParticipantUpdatedCallbackInfo data)
    {
        string id = data.ParticipantId != null ? data.ParticipantId.ToString() : "?";
        lock (participants)
        {
            ParticipantInfo info = participants.Find(p => p.id == id);
            if (info == null)
            {
                info = new ParticipantInfo { id = id };
                participants.Add(info);
            }
            info.speaking = data.Speaking;
            info.audioStatus = data.AudioStatus.ToString();
        }
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
        if (DebugHearSelf) snapshot.Add(selfStream);

        // Destroy playbacks whose stream is gone.
        for (int i = playbacks.Count - 1; i >= 0; i--)
        {
            if (playbacks[i] == null || !snapshot.Contains(playbacks[i].Stream))
            {
                if (playbacks[i] != null) Destroy(playbacks[i].gameObject);
                playbacks.RemoveAt(i);
                if (!DebugHearSelf) selfStream.Clear();
            }
        }

        // In a match (we have a body) voices come from bodies. In the lobby there are none.
        bool inMatch = PlayerVoice.Local != null;
        PlayerInventory myInventory = PlayerInventory.Local;
        bool iHearRadio = inMatch && myInventory != null && myInventory.HasPoweredRadio;

        foreach (VoiceStream stream in snapshot)
        {
            if (stream.SampleRate <= 0) continue;

            bool isSelf = stream == selfStream;
            bool flat = isSelf || DebugHearEveryone || !inMatch || stream.IsMixed;

            PlayerVoice talker = !flat ? FindTalker(stream.ParticipantId) : null;
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
            // Lobby / voice test: flat.
            playback.SetSpatial(!flat && talker != null);
            playback.SetProximityMuted(!flat && talker == null);

            bool radio = !isSelf && iHearRadio && talker != null && talker != PlayerVoice.Local && talker.RadioTransmitting;
            playback.SetRadio(radio);
        }
    }

    /// <summary>For the voice test panel: is this EOS id linked to a player body?</summary>
    public static bool HasBody(string productUserId) => FindTalker(productUserId) != null;

    private static PlayerVoice FindTalker(string productUserId)
    {
        List<PlayerVoice> all = PlayerVoice.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i].ProductUserId == productUserId) return all[i];
        return null;
    }

    private VoicePlayback FindPlayback(VoiceStream stream)
    {
        for (int i = 0; i < playbacks.Count; i++)
            if (playbacks[i] != null && playbacks[i].Stream == stream) return playbacks[i];
        return null;
    }
}
