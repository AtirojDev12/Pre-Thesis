using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How loud this player is, for the noise meter now and for ghosts that
/// "hear" players later. Added automatically by <see cref="PlayerVoice"/> on
/// every player body (no prefab change needed).
///
/// TWO VALUES, both 0..1:
///   MIC   – how loud the player's real microphone is (the always-on mic).
///           0 while the Esc menu is open (the mic is muted then).
///   GAME  – noise the player makes inside the game:
///             - moving (walk quiet, sprint louder)
///             - a Walkie-Talkie they carry playing someone's voice
///             - the radio "kshh" click
///             - anything another script reports with <see cref="ReportLocal"/>
///
/// WHERE THE NUMBERS LIVE
///   The owner's PC measures both (only it has the microphone), shows them on
///   the meter, and sends them to the server about 10 times a second
///   (PlayerVoice.CmdReportNoise). Ghost code runs on the server and reads
///   <see cref="ServerMic"/>, <see cref="ServerGame"/>, <see cref="ServerLoudness"/>
///   or <see cref="ServerTooLoud"/>. Nothing here punishes anybody yet.
///
/// FOR THE GHOST PROGRAMMER (server side)
///   foreach (PlayerNoise p in PlayerNoise.All)
///       if (p.ServerTooLoud) { ... this player is making too much noise ... }
///   The value is already smoothed (it falls back over ~0.5 s), so a single
///   cough gives a short peak, a shout stays high.
///
/// Known limit: the owner reports its own loudness, so a modified client
/// could report silence. Fine for a co-op PvE game; can be cross-checked
/// against the voice packets on the server later.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerNoise : MonoBehaviour
{
    /// <summary>Above this the meter turns red and ghosts may react (tune freely).</summary>
    public const float LoudThreshold = 0.75f;

    /// <summary>
    /// MIC bar boost (dB) on top of the player's "Mic meter sensitivity"
    /// setting, so normal talking reads mid-high and raised voices reach red.
    /// </summary>
    public const float MicBoostDb = 8f;

    // ---- Game-noise tuning (0..1) -------------------------------------------
    // Raised 29 Sep (Mr.k): in-game noise should reach red more easily.
    public const float WalkNoise = 0.3f;
    // Crouch movement is silent for the meter and ghost hearing.
    public const float CrouchWalkNoise = 0f;
    public const float SprintNoise = 0.6f;
    public const float RadioClickNoise = 0.5f;
    /// <summary>Radio playing a voice: base + this much of the voice's loudness (0.45..1.0, a loud voice turns it red).</summary>
    public const float RadioVoiceBase = 0.45f;
    public const float RadioVoiceRange = 0.55f;

    // ---- Action sounds (22 Oct, Mr.k) ------------------------------------------
    // Every 3D game sound a player makes (light switch, ticket buttons, popcorn...)
    // raises THAT player's GAME bar. Level = the sound's library volume x this.
    public const float ActionNoiseScale = 0.75f;
    /// <summary>The sound counts for the nearest player body within this many metres.</summary>
    public const float ActionNoiseRadius = 3f;
    /// <summary>A one-shot holds the bar for its clip length, at most this long.</summary>
    public const float ActionNoiseMaxHold = 3f;

    // How fast the bars fall back (per second). Rising is instant.
    private const float MicRelease = 1.6f;
    private const float GameRelease = 1.2f;

    // Network: send at most this often, and at least this often (packets can be lost).
    private const float SendInterval = 0.1f;
    private const float KeepAliveInterval = 0.5f;

    /// <summary>Every player body on this machine (on the server: every player in the match).</summary>
    public static readonly List<PlayerNoise> All = new List<PlayerNoise>();

    /// <summary>This machine's own player (null outside a match).</summary>
    public static PlayerNoise Local { get; private set; }

    private PlayerVoice voice;
    private PlayerMovement movement;

    // ---- Owner (local) values -------------------------------------------------
    /// <summary>Owner only: microphone loudness 0..1 (smoothed).</summary>
    public float Mic { get; private set; }
    /// <summary>Owner only: in-game noise 0..1 (smoothed).</summary>
    public float Game { get; private set; }

    private float spike;           // one-off noises (click, door...), falls back over time
    private float spikeHoldUntil;  // the spike does not fall before this time (unscaled)
    private float radioOutput;     // set every frame by VoiceChatManager while the radio plays
    private float radioOutputTime = -999f;

    private float nextSend;
    private float lastSendTime = -999f;
    private byte lastSentMic = 255, lastSentGame = 255;

    // ---- Server values --------------------------------------------------------
    /// <summary>SERVER: last microphone loudness reported by the owner (0..1).</summary>
    public float ServerMic { get; private set; }
    /// <summary>SERVER: last in-game noise reported by the owner (0..1).</summary>
    public float ServerGame { get; private set; }
    /// <summary>SERVER: the louder of the two.</summary>
    public float ServerLoudness => Mathf.Max(ServerMic, ServerGame);
    /// <summary>SERVER: louder than <see cref="LoudThreshold"/> right now.</summary>
    public bool ServerTooLoud => ServerLoudness >= LoudThreshold;

    // Looping action sounds of the LOCAL player (popcorn pouring...). A List, not a Dictionary.
    private struct Loop { public Object key; public float level; }
    private static readonly List<Loop> localLoops = new List<Loop>(4);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
        Local = null;
        localLoops.Clear();
    }

    public void Init(PlayerVoice owner) => voice = owner;

    private void Awake() => movement = GetComponent<PlayerMovement>();
    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }

    private void OnDisable()
    {
        All.Remove(this);
        if (Local == this) Local = null;
    }

    // ---- Reporting --------------------------------------------------------------

    /// <summary>
    /// Any script on the LOCAL player's machine: "the local player just made a
    /// noise" (door slam, dropped item...). 0..1. Does nothing if there is no
    /// local player.
    /// </summary>
    public static void ReportLocal(float loudness)
    {
        if (Local != null) Local.AddSpike(loudness);
    }

    public void AddSpike(float loudness) => AddSpike(loudness, 0f);

    /// <summary>One-off noise that stays at its level for <paramref name="holdSeconds"/>, then falls back.</summary>
    public void AddSpike(float loudness, float holdSeconds)
    {
        spike = Mathf.Max(spike, Mathf.Clamp01(loudness));
        if (holdSeconds > 0f) spikeHoldUntil = Mathf.Max(spikeHoldUntil, Time.unscaledTime + holdSeconds);
    }

    /// <summary>
    /// 22 Oct: any machine, when a 3D game sound plays at <paramref name="position"/>.
    /// If the LOCAL player is the closest player body (within <see cref="ActionNoiseRadius"/>),
    /// that player made it: their GAME bar rises, and the server / ghosts get it with the
    /// normal noise report. Every PC runs this, but only the nearest player's PC counts it.
    /// </summary>
    public static void ReportSoundAt(Vector3 position, float loudness, float holdSeconds)
    {
        if (Local == null || loudness <= 0f) return;
        float mine = (Local.transform.position - position).sqrMagnitude;
        if (mine > ActionNoiseRadius * ActionNoiseRadius) return;
        for (int i = 0; i < All.Count; i++)
        {
            PlayerNoise other = All[i];
            if (other == null || other == Local) continue;
            if ((other.transform.position - position).sqrMagnitude + 0.01f < mine) return; // someone else is closer
        }
        Local.AddSpike(loudness, Mathf.Min(holdSeconds, ActionNoiseMaxHold));
    }

    /// <summary>22 Oct: the LOCAL player started a looping action sound (key = its AudioSource).</summary>
    public static void StartLocalLoop(Object key, float loudness)
    {
        if (key == null) return;
        StopLocalLoop(key);
        localLoops.Add(new Loop { key = key, level = Mathf.Clamp01(loudness) });
    }

    public static void StopLocalLoop(Object key)
    {
        for (int i = localLoops.Count - 1; i >= 0; i--)
            if (localLoops[i].key == key) localLoops.RemoveAt(i);
    }

    private static float LoopNoise()
    {
        float level = 0f;
        for (int i = localLoops.Count - 1; i >= 0; i--)
        {
            if (localLoops[i].key == null) { localLoops.RemoveAt(i); continue; } // destroyed source
            level = Mathf.Max(level, localLoops[i].level);
        }
        return level;
    }

    /// <summary>VoiceChatManager, every frame the radio in your pocket plays a voice. level = voice peak 0..1.</summary>
    public void SetRadioOutput(float voicePeak)
    {
        radioOutput = RadioVoiceBase + RadioVoiceRange * VoiceMicCapture.PeakToLoudness(voicePeak);
        radioOutputTime = Time.unscaledTime;
    }

    // ---- Update -------------------------------------------------------------------

    private void Update()
    {
        bool isLocal = voice != null && NetworkMode.IsLocalController(voice);
        if (!isLocal)
        {
            if (Local == this) Local = null;
            return;
        }
        if (Local != this)
        {
            Local = this;
            NoiseMeterHUD.Ensure();
        }

        float dt = Time.unscaledDeltaTime;

        // Round over for this player (results screen): they make no noise in the map.
        if (MatchResultsUI.IsShowing)
        {
            Mic = 0f;
            Game = 0f;
            spike = 0f;
            SendToServer();
            return;
        }

        // MIC: VoiceChatManager already reports 0 while the Esc menu is open.
        Mic = Follow(Mic, VoiceChatManager.MicLoudness, MicRelease, dt);

        // GAME: the loudest thing happening right now.
        float target = MovementNoise();
        if (Time.unscaledTime - radioOutputTime < 0.15f) target = Mathf.Max(target, radioOutput);
        target = Mathf.Max(target, spike);
        target = Mathf.Max(target, LoopNoise());
        if (Time.unscaledTime >= spikeHoldUntil) spike = Mathf.MoveTowards(spike, 0f, GameRelease * dt);
        Game = Follow(Game, target, GameRelease, dt);

        SendToServer();
    }

    private float MovementNoise()
    {
        if (movement == null || GameplayInput.Blocked) return 0f;
        float speed = movement.CurrentMovementSpeed;
        if (speed <= 0.05f) return 0f;
        Rigidbody body = movement.GetComponent<Rigidbody>();
        if (body != null && new Vector2(body.linearVelocity.x, body.linearVelocity.z).sqrMagnitude < 0.01f)
            return 0f;
        if (movement.IsCrouching) return CrouchWalkNoise;
        return movement.IsSprinting ? SprintNoise : WalkNoise;
    }

    /// <summary>Jumps up at once, falls back at <paramref name="release"/> per second.</summary>
    private static float Follow(float current, float target, float release, float dt) =>
        target >= current ? target : Mathf.MoveTowards(current, target, release * dt);

    private void SendToServer()
    {
        if (Time.unscaledTime < nextSend) return;

        byte mic = (byte)Mathf.RoundToInt(Mic * 255f);
        byte game = (byte)Mathf.RoundToInt(Game * 255f);
        bool changed = Mathf.Abs(mic - lastSentMic) >= 3 || Mathf.Abs(game - lastSentGame) >= 3;
        if (!changed && Time.unscaledTime - lastSendTime < KeepAliveInterval) return;

        nextSend = Time.unscaledTime + SendInterval;
        lastSendTime = Time.unscaledTime;
        lastSentMic = mic;
        lastSentGame = game;
        voice.SendNoise(mic, game);
    }

    /// <summary>SERVER (called by PlayerVoice's Command, or directly in offline test scenes).</summary>
    public void ServerReceive(byte mic, byte game)
    {
        ServerMic = mic / 255f;
        ServerGame = game / 255f;
    }
}
