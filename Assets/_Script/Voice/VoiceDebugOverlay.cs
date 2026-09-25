using System.Collections.Generic;
using System.Text;
using EpicTransport;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// VOICE TEST PANEL. Press F8 in any scene (menu, lobby, match).
///
///   F5 = hear yourself (your mic comes back through your speakers)
///   F6 = hear everyone loud (no distance, no body needed)
///
/// Read it top to bottom; the first line that looks wrong is where the sound stops:
///   1. EOS voice available?        no  -> xaudio2_9redist.dll / EOS start problem
///   2. Voice room connected?       no  -> Dev Portal policy / lobby made by an old build
///   3. My mic: status + level      level stays 0 when you talk -> wrong Windows mic / mic blocked
///   4. Sent blocks go up?          no  -> EOS is not sending (mic muted / menu open)
///   5. Other player "speaking=YES" when they talk?  no -> THEIR mic side (check their panel)
///   6. Received blocks go up?      no  -> EOS render callback problem (send me this panel)
///   7. F5 you hear yourself?       no  -> speakers / Unity audio / Master volume
///
/// Added automatically by VoiceChatManager. IMGUI on purpose: no prefab, no scene edits.
/// </summary>
public sealed class VoiceDebugOverlay : MonoBehaviour
{
    private bool visible;
    private GUIStyle style;
    private readonly StringBuilder text = new StringBuilder(1024);
    private float nextRefresh;
    private long lastMicBlocks, lastReceivedBlocks;
    private float micBlocksPerSecond, receivedBlocksPerSecond;

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.f8Key.wasPressedThisFrame) visible = !visible;
        if (!visible) return;

        if (keyboard.f5Key.wasPressedThisFrame) VoiceChatManager.DebugHearSelf = !VoiceChatManager.DebugHearSelf;
        if (keyboard.f6Key.wasPressedThisFrame) VoiceChatManager.DebugHearEveryone = !VoiceChatManager.DebugHearEveryone;
        if (keyboard.f7Key.wasPressedThisFrame) VoiceChatManager.UseNextInputDevice();
        if (keyboard.f4Key.wasPressedThisFrame) VoiceChatManager.DebugEosSpeaker = !VoiceChatManager.DebugEosSpeaker;

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.25f;
            long mic = VoiceChatManager.MicBlocks, received = VoiceChatManager.ReceivedBlocks;
            micBlocksPerSecond = (mic - lastMicBlocks) * 4f;
            receivedBlocksPerSecond = (received - lastReceivedBlocks) * 4f;
            lastMicBlocks = mic;
            lastReceivedBlocks = received;
            Rebuild();
        }
    }

    private void Rebuild()
    {
        text.Length = 0;
        text.AppendLine("<b>VOICE TEST</b>   (F8 hide)");
        text.AppendLine();

        bool eos = EOSSDKComponent.IsReady;
        text.AppendLine("1. EOS ready: " + YesNo(eos) + "    voice available: " + YesNo(EOSSDKComponent.VoiceAvailable));

        string room = VoiceChatManager.RoomNameForDebug;
        text.AppendLine("2. Voice room: " + (room == null ? Bad("none (create or join a room)") :
            VoiceChatManager.RoomConnected ? Good("connected") : Bad("connecting...")));

        float level = VoiceChatManager.MicLevel;
        text.AppendLine("3. My mic: " + VoiceChatManager.MicInputStatus + "   level " + Bar(level) +
                        $" {level:0.00}   (talk: it should move)");

        List<string> mics = VoiceChatManager.InputDeviceLines();
        text.AppendLine("   Recording from: " + VoiceChatManager.MicDeviceForDebug + "   [F7] next mic   " +
                        Bad(VoiceChatManager.MicErrorForDebug));
        foreach (string line in mics) text.AppendLine("     " + line);

        bool? sending = VoiceChatManager.MicSending;
        text.AppendLine("4. Sending: " + (sending == true ? Good("ON") : sending == false ? Bad("OFF (Esc menu open)") : "-") +
                        $"   mic blocks/s {micBlocksPerSecond:0} (should be ~100)   status: {VoiceChatManager.LastSendResult}");
        text.AppendLine($"   SendAudio: {VoiceChatManager.LastSendAudioResult}   failures {VoiceChatManager.SendAudioFailures}");

        text.AppendLine("5. Players in the voice room (EOS):");
        List<VoiceChatManager.ParticipantInfo> people = VoiceChatManager.ParticipantsSnapshot();
        if (people.Count == 0) text.AppendLine("     " + Bad("nobody else yet"));
        foreach (VoiceChatManager.ParticipantInfo p in people)
        {
            text.AppendLine($"     {Short(p.id)}  speaking={(p.speaking ? Good("YES") : "no")}  audio={p.audioStatus}" +
                            $"  body={(VoiceChatManager.HasBody(p.id) ? Good("found") : "not found")}");
        }

        text.AppendLine("GAME-NETWORK VOICE (main path): " + (VoiceNetwork.Active ? Good("ON") : Bad("off (not in a room)")) +
                        "   you speaking: " + (VoiceNetwork.Speaking ? Good("YES") : "no"));
        text.AppendLine($"   sent {VoiceChatManager.NetSentPerSecond} packets/s (25 while talking)   received {VoiceChatManager.NetReceivedPerSecond}/s" +
                        (NetworkServer.active ? $"   relayed by host {VoiceChatManager.NetRelayedPerSecond}/s" : ""));
        text.AppendLine($"6. Voices playing: (EOS path {receivedBlocksPerSecond:0} blocks/s, total {VoiceChatManager.ReceivedBlocks})");
        foreach (VoiceStream s in VoiceChatManager.StreamsSnapshot())
            text.AppendLine($"     {Short(s.ParticipantId)}  {s.SampleRate} Hz  blocks {s.Blocks}  level {Bar(s.Level)}");

        text.AppendLine();
        text.AppendLine("7. Unity audio: master " + $"{AudioListener.volume:0.00}" + "   listeners in scene: " + ListenerCount() +
                        (AudioListener.pause ? Bad("   PAUSED") : ""));
        text.AppendLine();
        text.AppendLine("[F5] Hear myself: " + OnOff(VoiceChatManager.DebugHearSelf));
        text.AppendLine("[F6] Hear everyone loud (no distance): " + OnOff(VoiceChatManager.DebugHearEveryone));
        text.AppendLine("[F4] Force EOS speaker (flat): " + OnOff(VoiceChatManager.DebugEosSpeaker) +
                        "   EOS speaker now: " + VoiceChatManager.EosSpeakerForDebug);
    }

    private void OnGUI()
    {
        if (!visible) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.box)
            {
                richText = true,
                alignment = TextAnchor.UpperLeft,
                fontSize = 16,
                wordWrap = false,
                padding = new RectOffset(12, 12, 10, 10)
            };
            style.normal.textColor = Color.white;
        }

        GUI.depth = -1000;
        var rect = new Rect(20, 20, 920, 700);
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.95f);
        GUI.Box(rect, text.ToString(), style);
        GUI.color = old;
    }

    private static int ListenerCount() =>
        FindObjectsByType<AudioListener>().Length;

    private static string Short(string id) =>
        string.IsNullOrEmpty(id) ? "?" : id.Length > 10 ? id.Substring(0, 6) + "..." + id.Substring(id.Length - 4) : id;

    private static string Bar(float v)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(v * 20f), 0, 20);
        return "[" + new string('|', n) + new string('.', 20 - n) + "]";
    }

    private static string Good(string s) => "<color=#6f6>" + s + "</color>";
    private static string Bad(string s) => "<color=#f66>" + s + "</color>";
    private static string YesNo(bool b) => b ? Good("YES") : Bad("NO");
    private static string OnOff(bool b) => b ? Good("ON") : "off";
}
