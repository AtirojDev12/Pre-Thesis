#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// DEVELOPER TEST PANEL for voice + noise. Press F8 to show / hide (hidden at start).
///
/// Exists ONLY in the Unity Editor and in Development Builds
/// (File > Build Profiles > "Development Build" ticked). In a normal release
/// build this whole file is compiled out, so players can never open it.
///
/// Shows: mic device and levels, voice status, game-network voice packets,
/// the noise meter values (and, on the host, every player's server values),
/// the voices playing, and audio listener info.
///
/// Added automatically by VoiceChatManager. IMGUI on purpose: no prefab, no scene edits.
/// </summary>
public sealed class VoiceDebugOverlay : MonoBehaviour
{
    private bool visible;
    private GUIStyle style;
    private readonly StringBuilder text = new StringBuilder(1024);
    private float nextRefresh;

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f8Key.wasPressedThisFrame) visible = !visible;
        if (!visible || Time.unscaledTime < nextRefresh) return;

        nextRefresh = Time.unscaledTime + 0.2f;
        Rebuild();
    }

    private void Rebuild()
    {
        text.Length = 0;
        text.AppendLine("<b>VOICE + NOISE TEST</b>   (F8 hide · Editor / Development Build only)");
        text.AppendLine();

        // ---- Microphone ----
        text.AppendLine("<b>Microphone</b>: " + VoiceChatManager.MicDeviceName +
                        (VoiceChatManager.MicRecording ? Good("  recording") : Bad("  not recording")) +
                        "   " + Bad(VoiceChatManager.MicError));
        text.AppendLine($"   raw level   {Bar(VoiceChatManager.MicRawLevel)} {VoiceChatManager.MicRawLevel:0.000}   (straight from the mic)");
        text.AppendLine($"   clean-up    noise reduction {(GameSettings.NoiseReduction ? Good("ON") : "off")}   gate {(GameSettings.NoiseGate ? Good("ON") : "off")}" +
                        $"   level {VoiceChatManager.MicCleanedDb:0} dB / gate {GameSettings.NoiseGateThresholdDb:0} dB   " +
                        (VoiceChatManager.MicGateOpen ? Good("OPEN") : Bad("closed")));
        text.AppendLine($"   meter level {Bar(VoiceChatManager.MicMeterLevel)} {VoiceChatManager.MicMeterLevel:0.000}   (after clean-up, used by the meter)");
        text.AppendLine($"   after gain  {Bar(VoiceChatManager.MicLevelAfterGain)} {VoiceChatManager.MicLevelAfterGain:0.000}   auto gain x{VoiceChatManager.MicGain:0.0}");
        text.AppendLine($"   loudness    {Bar(VoiceChatManager.MicLoudness)} {VoiceChatManager.MicLoudness:0.00}   sensitivity {GameSettings.MicSensitivityDb:+0;-0;0} dB");

        // ---- Voice ----
        text.AppendLine();
        text.AppendLine("<b>Voice</b>: " + VoiceChatManager.CurrentStatus +
                        "   game-network voice " + (VoiceNetwork.Active ? Good("ON") : Bad("off (not in a room)")) +
                        "   you speaking: " + (VoiceNetwork.Speaking ? Good("YES") : "no"));
        text.AppendLine($"   packets/s  sent {VoiceChatManager.NetSentPerSecond} (25 while talking)   received {VoiceChatManager.NetReceivedPerSecond}" +
                        (NetworkServer.active ? $"   relayed by host {VoiceChatManager.NetRelayedPerSecond}" : ""));

        // ---- Noise meter ----
        text.AppendLine();
        PlayerNoise local = PlayerNoise.Local;
        if (local == null) text.AppendLine("<b>Noise meter</b>: no local player (menu / lobby)");
        else
        {
            text.AppendLine($"<b>Noise meter</b> (this PC)   MIC {Bar(local.Mic)} {local.Mic:0.00}   GAME {Bar(local.Game)} {local.Game:0.00}" +
                            $"   too loud at {PlayerNoise.LoudThreshold:0.00}");
        }

        if (NetworkServer.active || NetworkMode.IsOffline)
        {
            List<PlayerNoise> all = PlayerNoise.All;
            text.AppendLine($"   server view ({all.Count} players):");
            foreach (PlayerNoise p in all)
            {
                if (p == null) continue;
                string who = p.TryGetComponent(out NetworkIdentity id) ? "netId " + id.netId : p.name;
                text.AppendLine($"     {who,-10} mic {p.ServerMic:0.00}  game {p.ServerGame:0.00}  " +
                                (p.ServerTooLoud ? Bad("TOO LOUD") : Good("ok")));
            }
        }

        // ---- Voices playing ----
        text.AppendLine();
        List<VoicePlayback> playbacks = VoiceChatManager.PlaybacksSnapshot();
        text.AppendLine($"<b>Voices playing</b>: {playbacks.Count}");
        foreach (VoicePlayback pb in playbacks)
        {
            if (pb == null || pb.Stream == null) continue;
            VoiceStream s = pb.Stream;
            text.AppendLine($"     {s.ParticipantId,-12} level {Bar(s.Level)}  " +
                            (s.SecondsSinceLastAudio < 0.3f ? Good("talking") : "silent") +
                            (pb.RadioOn ? "  " + Good("RADIO") : ""));
        }

        // ---- Unity audio ----
        text.AppendLine();
        text.AppendLine($"<b>Unity audio</b>: master {AudioListener.volume:0.00}   listeners in scene {FindObjectsByType<AudioListener>().Length}" +
                        (AudioListener.pause ? Bad("   PAUSED") : ""));
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
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.95f);
        GUI.Box(new Rect(20, 20, 900, 620), text.ToString(), style);
        GUI.color = old;
    }

    private static string Bar(float v)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(v * 20f), 0, 20);
        return "[" + new string('|', n) + new string('.', 20 - n) + "]";
    }

    private static string Good(string s) => "<color=#6f6>" + s + "</color>";
    private static string Bad(string s) => string.IsNullOrEmpty(s) ? "" : "<color=#f66>" + s + "</color>";
}
#endif
