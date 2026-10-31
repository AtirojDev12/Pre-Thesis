using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Server -> clients: play a map-wide alert sound (AlertSounds).</summary>
public struct AlertSoundMessage : NetworkMessage
{
    public byte kind;
    public Vector3 position;
}

/// <summary>
/// 31 Oct 2026 (Mr.k): map-wide alert sounds, easy to change for the team.
///
///   ZONE DONE      a zone's task board is finished -> a 3D alarm from that board.
///   DOWNED SHOUT   a downed player presses the Shout key (default V) once per down
///                  -> for a few seconds the whole map hears them.
///
/// "Map-wide" = still 3D (you hear WHERE it comes from), loud near, quieter far away,
/// but never silent: beyond Far Distance it stays at Far Volume, so everyone hears it.
///
/// WHERE TO CHANGE (no code): Resources/Tuning/AlertSounds.asset
///   (Tools > Pre-Thesis > Designer Settings > Alert Sounds). Made automatically the
///   first time Unity opens the project, with AlarmSiren.wav as the zone sound.
///   - Zone Clip:   any AudioClip.
///   - Shout Mode:  Real Voice (the player's mic) / Sound Effect (Shout Clip) / Both.
///
/// NETWORK: only the server decides (ServerPlay...); every client plays it locally.
/// </summary>
[CreateAssetMenu(menuName = "Pre-Thesis/Alert Sounds")]
public sealed class AlertSounds : ScriptableObject
{
    public const string ResourcePath = "Tuning/AlertSounds";
    public const string AssetPath = "Assets/Resources/Tuning/AlertSounds.asset";

    public enum ShoutMode { RealVoice = 0, SoundEffect = 1, Both = 2 }

    [System.Serializable]
    public sealed class Reach
    {
        [Tooltip("Metres: full volume up to here.")]
        [Min(0.1f)] public float nearDistance = 8f;
        [Tooltip("Metres: from here on the sound stays at Far Volume (never silent).")]
        [Min(1f)] public float farDistance = 90f;
        [Tooltip("Volume at and beyond Far Distance (0..1). Above 0 = the whole map hears it.")]
        [Range(0f, 1f)] public float farVolume = 0.25f;
    }

    [Header("Zone finished")]
    [Tooltip("Played from the task board when its zone is finished. Default: AlarmSiren.wav.")]
    public AudioClip zoneClip;
    [Range(0f, 1f)] public float zoneVolume = 1f;
    public Reach zoneReach = new Reach();

    [Header("Downed shout")]
    [Tooltip("Real Voice = the downed player's mic is heard across the map. Sound Effect = Shout Clip plays at the body. Both = both.")]
    public ShoutMode shoutMode = ShoutMode.RealVoice;
    [Tooltip("How long the shout lasts (seconds).")]
    [Min(0.5f)] public float shoutSeconds = 3f;
    [Tooltip("Only one shout each time the player goes down.")]
    public bool oncePerDown = true;
    [Tooltip("Real Voice: extra loudness while shouting (1 = normal voice).")]
    [Range(1f, 4f)] public float shoutVoiceGain = 2f;
    [Tooltip("Sound Effect mode: the clip played at the body.")]
    public AudioClip shoutClip;
    [Range(0f, 1f)] public float shoutClipVolume = 1f;
    public Reach shoutReach = new Reach();

    // ---- Settings asset --------------------------------------------------------

    private static AlertSounds current;

    /// <summary>The asset in Resources, or default values if it does not exist yet.</summary>
    public static AlertSounds Current
    {
        get
        {
            if (current != null) return current;
            current = Resources.Load<AlertSounds>(ResourcePath);
            if (current == null)
            {
                current = CreateInstance<AlertSounds>();
                current.hideFlags = HideFlags.HideAndDontSave;
            }
            return current;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        if (current != null && (current.hideFlags & HideFlags.DontSave) != 0) DestroyImmediate(current);
        current = null;
    }

    public bool ShoutUsesVoice => shoutMode != ShoutMode.SoundEffect;
    public bool ShoutUsesClip => shoutMode != ShoutMode.RealVoice;

    /// <summary>Custom 3D roll-off: full to Near, softer to Far, then flat at Far Volume (never 0).</summary>
    public static AnimationCurve RolloffCurve(Reach reach)
    {
        float far = Mathf.Max(reach.farDistance, reach.nearDistance + 1f);
        float near = Mathf.Clamp01(reach.nearDistance / far);
        float mid = Mathf.Lerp(near, 1f, 0.35f);
        float midVolume = Mathf.Lerp(reach.farVolume, 1f, 0.45f);
        return new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(near, 1f),
            new Keyframe(mid, midVolume),
            new Keyframe(1f, reach.farVolume));
    }

    // ---- Network ----------------------------------------------------------------

    private enum Kind : byte { ZoneDone = 0, ShoutClip = 1 }

    /// <summary>Called by NetworkAudioRelay.RegisterClient / UnregisterClient.</summary>
    public static void RegisterClient() => NetworkClient.RegisterHandler<AlertSoundMessage>(Receive);
    public static void UnregisterClient() => NetworkClient.UnregisterHandler<AlertSoundMessage>();

    /// <summary>SERVER / OFFLINE. A zone's board was finished at this position.</summary>
    public static void ServerZoneDone(Vector3 position) => ServerSend(Kind.ZoneDone, position);

    /// <summary>SERVER / OFFLINE. Sound Effect shout from a downed body.</summary>
    public static void ServerShoutClip(Vector3 position) => ServerSend(Kind.ShoutClip, position);

    private static void ServerSend(Kind kind, Vector3 position)
    {
        if (NetworkMode.IsOffline) { PlayLocal(kind, position); return; }
        if (!NetworkServer.active || NetworkMode.SessionEnding) return;
        var message = new AlertSoundMessage { kind = (byte)kind, position = position };
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            if (connection != null && connection.isReady) connection.Send(message);
    }

    private static void Receive(AlertSoundMessage message)
    {
        if (NetworkMode.SessionEnding) return;
        PlayLocal((Kind)message.kind, message.position);
    }

    private static void PlayLocal(Kind kind, Vector3 position)
    {
        AudioManager audio = AudioManager.Instance;
        if (audio == null) return;
        AlertSounds s = Current;
        Scene scene = SceneManager.GetActiveScene();
        if (kind == Kind.ZoneDone)
        {
            if (s.zoneClip == null) { Debug.LogWarning("[AlertSounds] No Zone Clip set in AlertSounds.asset."); return; }
            audio.PlayMapWide(s.zoneClip, position, s.zoneVolume, s.zoneReach, scene);
        }
        else if (s.shoutClip != null)
        {
            audio.PlayMapWide(s.shoutClip, position, s.shoutClipVolume, s.shoutReach, scene);
        }
    }
}
