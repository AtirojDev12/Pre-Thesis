using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 19 Nov 2026: sounds of the MISSING PARTS ghost (MissingPartsGhost), easy to change for the team.
///
///   STANDBY   the ghost stopped at the ticket / popcorn counter and nobody has pressed E yet:
///             head or upper body missing -> the KNOCK (GhostFindBodySound) loops from the ghost.
///             head attached (full body / arms missing) -> it says HelpME every few seconds.
///             The knock fades out as soon as someone presses E (the ghost starts to follow).
///
///   VOICES    after the first E:
///             head attached -> the ghost keeps saying HelpME (every Help Me Interval seconds).
///             head missing  -> the head part says "I am here" / "Over here" (random, every
///                              Part Voice Interval seconds), on top of the finding sound.
///
///   ATTACK    time up -> the ghost attacks: MissingPartAttack, from the ghost.
///   RETURNED  a player puts a part back: MissingPartSuccess, from the ghost (once per part).
///   WORKING   while a player HOLDS E to put a part back: MissingPartWorkonProcess loops from the
///             ghost, and fades out when they let go or the part is back on.
///
///   FINDING   after the first E, a looping sound guides players to the missing part.
///             4 levels by distance, each its own clip (FindingSound folder):
///             Nearest / Near / Far / Far Away. Beyond Far Away = silent. A level change crossfades.
///             Head missing  -> it comes from the head, distance = YOU to the head.
///             Head attached -> it comes from the ghost, distance = the ghost to the part (hot / cold).
///
/// WHERE TO CHANGE (no code): Resources/Tuning/MissingPartSound.asset
///   (Tools > Pre-Thesis > Designer Settings > Missing Part Sound). Made automatically the
///   first time Unity opens the project, with GhostFindBodySound.wav as the standby sound.
///
/// NETWORK: nothing is sent. Every machine reads the ghost's synced state and plays it locally.
/// </summary>
[CreateAssetMenu(menuName = "Pre-Thesis/Missing Part Sound")]
public sealed class MissingPartSound : ScriptableObject
{
    public const string ResourcePath = "Tuning/MissingPartSound";
    public const string AssetPath = "Assets/Resources/Tuning/MissingPartSound.asset";

    [Header("Standby - waiting at the counter for the first E")]
    [Tooltip("KNOCK: loops from the ghost while it waits, ONLY when its head or upper body is missing. Default: GhostFindBodySound.wav. Empty = no knock. (With a head it says HelpME instead, see Voices.)")]
    [FormerlySerializedAs("standbyClip")] public AudioClip standbyKnockClip;
    [Tooltip("Knock loudness (0..1). The player's SFX volume setting is applied on top.")]
    [Range(0f, 1f)] public float standbyVolume = 0.8f;
    [Tooltip("Knock: on = loops until someone presses E. Off = plays once when it stops at the counter.")]
    public bool standbyLoop = true;
    [Tooltip("Also knock / say HelpME while it waits in line behind a customer (before it reaches the counter).")]
    public bool standbyWhileQueued = true;

    [Header("Standby knock - distance (3D)")]
    [Tooltip("Metres: full volume up to here.")]
    [Min(0.1f)] public float standbyNearDistance = 3f;
    [Tooltip("Metres: silent from here on.")]
    [Min(1f)] public float standbyFarDistance = 25f;

    [Header("Standby knock - fades")]
    [Tooltip("Seconds to fade in when it starts.")]
    [Min(0f)] public float standbyFadeIn = 0.5f;
    [Tooltip("Seconds to fade out after the first E.")]
    [Min(0f)] public float standbyFadeOut = 0.4f;

    [Header("Voices - HelpME (head attached)")]
    [Tooltip("Said by the ghost while it waits at the counter AND while it follows a player, when it still has its head. Default: HelpME.wav.")]
    public AudioClip helpMeClip;
    [Range(0f, 1f)] public float helpMeVolume = 1f;
    [Tooltip("Seconds from one HelpME to the next.")]
    [Min(1f)] public float helpMeInterval = 5f;

    [Header("Voices - the head part (head or upper body missing)")]
    [Tooltip("After the first E, the part with the head says one of these (random), from where it lies or from the player carrying it. Default: I am here.wav, OverHere.wav.")]
    public AudioClip[] partVoiceClips = new AudioClip[0];
    [Range(0f, 1f)] public float partVoiceVolume = 1f;
    [Tooltip("Seconds from one line to the next.")]
    [Min(1f)] public float partVoiceInterval = 6f;

    [Header("Voices - distance (3D)")]
    [Tooltip("Metres: full volume up to here.")]
    [Min(0.1f)] public float voiceNearDistance = 3f;
    [Tooltip("Metres: silent from here on.")]
    [Min(1f)] public float voiceFarDistance = 30f;

    [Header("Attack (time up) / part returned")]
    [Tooltip("When time is up and the ghost attacks. Default: MissingPartAttack.wav.")]
    public AudioClip attackClip;
    [Range(0f, 1f)] public float attackVolume = 1f;
    [Tooltip("Metres: silent from here on. The attack hurts players up to 50 m away, so it should reach about that far.")]
    [Min(1f)] public float attackFarDistance = 50f;
    [Tooltip("When a player puts a part back on the ghost (once per part). Default: MissingPartSuccess.wav.")]
    public AudioClip partReturnedClip;
    [Range(0f, 1f)] public float partReturnedVolume = 1f;
    [Tooltip("Metres: silent from here on.")]
    [Min(1f)] public float partReturnedFarDistance = 25f;
    [Tooltip("Metres: both sounds are at full volume up to here.")]
    [Min(0.1f)] public float eventNearDistance = 3f;

    [Header("Putting a part back (hold E on the ghost)")]
    [Tooltip("Plays from the ghost while a player HOLDS E to put a part back. Stops when they let go or the part is back on. Default: MIssingPartWorkonProcess.wav. Empty = no sound.")]
    public AudioClip workClip;
    [Range(0f, 1f)] public float workVolume = 1f;
    [Tooltip("On = loops for as long as E is held. Off = plays once each time E is pressed.")]
    public bool workLoop = true;
    [Tooltip("Seconds to fade out after E is let go or the part is back on.")]
    [Min(0f)] public float workFadeOut = 0.25f;
    [Tooltip("Metres: full volume up to here.")]
    [Min(0.1f)] public float workNearDistance = 3f;
    [Tooltip("Metres: silent from here on.")]
    [Min(1f)] public float workFarDistance = 20f;

    [System.Serializable]
    public sealed class FindingLevel
    {
        [Tooltip("Looping clip for this level. Empty = silent at this level.")]
        public AudioClip clip;
        [Tooltip("This level plays when the distance is at most this many metres (and more than the closer level).")]
        [Min(0.5f)] public float upToDistance = 10f;
        [Tooltip("Loudness of this level (0..1), multiplied by Finding Volume.")]
        [Range(0f, 1f)] public float volume = 1f;

        public FindingLevel() { }
        public FindingLevel(float upTo) { upToDistance = upTo; }
    }

    [Header("Finding - guides players to the missing part (after the first E)")]
    [Tooltip("Overall loudness of the finding sound (0..1). The player's SFX volume setting is applied on top.")]
    [Range(0f, 1f)] public float findingVolume = 1f;
    [Tooltip("Closest level. Default: FindingsoundNearest.wav, up to 5 m.")]
    public FindingLevel findingNearest = new FindingLevel(5f);
    [Tooltip("Default: FindingsoundNear.wav, up to 15 m.")]
    public FindingLevel findingNear = new FindingLevel(15f);
    [Tooltip("Default: FindingsoundFar.wav, up to 30 m.")]
    public FindingLevel findingFar = new FindingLevel(30f);
    [Tooltip("Farthest level. Default: FindingsoundFarAway.wav, up to 50 m. Beyond it: silent.")]
    public FindingLevel findingFarAway = new FindingLevel(50f);
    [Tooltip("Seconds to crossfade when the level changes (0 = cut at once).")]
    [Min(0f)] public float findingCrossfade = 0.5f;

    [HideInInspector] public int defaultsVersion; // MissingPartSoundSetup fills new default clips once

    /// <summary>0 = Nearest, 1 = Near, 2 = Far, 3 = Far Away, -1 = too far (silent).</summary>
    public int FindingLevelAt(float distance)
    {
        for (int i = 0; i < 4; i++)
            if (distance <= FindingLevelOf(i).upToDistance) return i;
        return -1;
    }

    public FindingLevel FindingLevelOf(int level)
    {
        switch (level)
        {
            case 0: return findingNearest;
            case 1: return findingNear;
            case 2: return findingFar;
            default: return findingFarAway;
        }
    }

    public static string FindingLevelName(int level)
    {
        switch (level)
        {
            case 0: return "Nearest";
            case 1: return "Near";
            case 2: return "Far";
            case 3: return "Far Away";
            default: return "Silent";
        }
    }

    // ---- Settings asset --------------------------------------------------------

    private static MissingPartSound current;

    /// <summary>The asset in Resources, or default values if it does not exist yet.</summary>
    public static MissingPartSound Current
    {
        get
        {
            if (current != null) return current;
            current = Resources.Load<MissingPartSound>(ResourcePath);
            if (current == null)
            {
                current = CreateInstance<MissingPartSound>();
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
}
