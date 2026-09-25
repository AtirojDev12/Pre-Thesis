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
/// THREADING
///   EOS may call AudioBeforeRender from its own audio thread, so that
///   callback only touches VoiceStream (plain C#, locked). Everything that
///   touches Unity objects runs in Update.
///
/// SETUP OUTSIDE THE CODE
///   EOS Developer Portal: the client policy used by the game must allow voice
///   (RTC). If voice never connects, check the Console for "[Voice]" lines.
/// </summary>
[DisallowMultipleComponent]
public sealed class VoiceChatManager : MonoBehaviour
{
    public enum Status { Offline, Connecting, Live, MutedByMenu }

    private const float PollInterval = 0.5f;
    private const float ForgetSilentStreamAfter = 15f;

    private static VoiceChatManager instance;

    /// <summary>For the HUD.</summary>
    public static Status CurrentStatus { get; private set; }

    // EOS room state (main thread only).
    private string lobbyId = string.Empty;
    private string roomName;
    private bool roomConnected;
    private bool? appliedSending;
    private ulong beforeRenderNotifyId;
    private float nextPoll;

    // Held in a field so the delegate cannot be garbage-collected while EOS still calls it.
    private OnAudioBeforeRenderCallback beforeRenderCallback;

    // Streams are created on the EOS thread, playbacks on the main thread.
    private readonly List<VoiceStream> streams = new List<VoiceStream>();
    private readonly List<VoicePlayback> playbacks = new List<VoicePlayback>();
    private readonly List<VoiceStream> snapshot = new List<VoiceStream>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        CurrentStatus = Status.Offline;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateAtStartup()
    {
        if (instance != null) return;
        var go = new GameObject("Voice Chat");
        DontDestroyOnLoad(go);
        go.AddComponent<VoiceChatManager>();
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
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        LeaveRoom();
        instance = null;
    }

    // ---- Main loop -------------------------------------------------------------

    private void Update()
    {
        if (!EOSSDKComponent.Initialized || !EOSSDKComponent.VoiceAvailable)
        {
            if (roomName != null) LeaveRoom();
            CurrentStatus = Status.Offline;
            return;
        }

        if (Time.unscaledTime >= nextPoll)
        {
            nextPoll = Time.unscaledTime + PollInterval;
            PollRoom();
        }

        ApplyMicState();
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

        LobbyInterface lobbies = EOSSDKComponent.GetLobbyInterface();
        bool connected = false;
        Result result = lobbies.IsRTCRoomConnected(new IsRTCRoomConnectedOptions
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
        LobbyInterface lobbies = EOSSDKComponent.GetLobbyInterface();
        string name;
        Result result = lobbies.GetRTCRoomName(new GetRTCRoomNameOptions
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
        beforeRenderNotifyId = audio.AddNotifyAudioBeforeRender(new AddNotifyAudioBeforeRenderOptions
        {
            LocalUserId = EOSSDKComponent.LocalUserProductId,
            RoomName = roomName,
            UnmixedAudio = true
        }, null, beforeRenderCallback);

        Debug.Log("[Voice] Joined voice for lobby " + lobbyId + ".");
    }

    private void LeaveRoom()
    {
        if (beforeRenderNotifyId != 0)
        {
            RTCAudioInterface audio = EOSSDKComponent.Initialized ? GetAudioInterface() : null;
            if (audio != null) audio.RemoveNotifyAudioBeforeRender(beforeRenderNotifyId);
            beforeRenderNotifyId = 0;
        }

        foreach (VoicePlayback playback in playbacks)
            if (playback != null) Destroy(playback.gameObject);
        playbacks.Clear();
        lock (streams) streams.Clear();

        roomName = null;
        roomConnected = false;
        appliedSending = null;
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
            if (info.ResultCode != Result.Success)
            {
                Debug.LogWarning("[Voice] Could not " + (wantSending ? "open" : "mute") + " the mic: " + info.ResultCode);
                appliedSending = null; // try again next frame
            }
        });
    }

    // ---- EOS audio (may run on the EOS audio thread: no Unity API here) -------

    private void OnAudioBeforeRender(AudioBeforeRenderCallbackInfo data)
    {
        AudioBuffer buffer = data.Buffer;
        if (buffer == null || buffer.Frames == null || buffer.Frames.Length == 0) return;

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
        PlayerInventory myInventory = PlayerInventory.Local;
        bool iHearRadio = inMatch && myInventory != null && myInventory.HasPoweredRadio;

        foreach (VoiceStream stream in snapshot)
        {
            if (stream.SampleRate <= 0) continue;

            PlayerVoice talker = inMatch && !stream.IsMixed ? FindTalker(stream.ParticipantId) : null;
            Transform parent = !inMatch || stream.IsMixed ? transform : (talker != null ? talker.Mouth : transform);

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
            playback.SetSpatial(inMatch && talker != null);
            playback.SetProximityMuted(inMatch && !stream.IsMixed && talker == null);

            bool radio = iHearRadio && talker != null && talker != PlayerVoice.Local && talker.RadioTransmitting;
            playback.SetRadio(radio);
        }
    }

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
