using UnityEngine;

/// <summary>
/// 17 Nov 2026 (Mr.k, Task 5). ONE place to balance the film change and the mop.
///
/// HOW TO USE
///   1. Tools > Pre-Thesis > Designer Settings > Film Reels + Mops  (creates the file the first time)
///   2. Change the numbers in the Inspector. Works live in Play mode.
///   3. Commit the file: Assets/Resources/Tuning/FilmMopTuning.asset
///
/// No asset yet? The game uses the default numbers written below.
/// ONLINE: the host decides (penalties and timers run on the host).
/// </summary>
[CreateAssetMenu(fileName = "FilmMopTuning", menuName = "Pre-Thesis/Film + Mop Tuning")]
public sealed class FilmMopTuning : ScriptableObject
{
    public const string ResourcePath = "Tuning/FilmMopTuning";
    public const string AssetPath = "Assets/Resources/Tuning/FilmMopTuning.asset";

    [Header("Glow before a film change / cleaning time (projector + mops)")]
    [Range(0f, 600f), Tooltip("Real seconds BEFORE the film change (or cleaning) is due that the projector (or the mops) glow. 75 s = 30 in-game minutes.")]
    public float glowSecondsBefore = 75f;
    [Range(0.5f, 30f), Tooltip("How long the glow stays (real seconds).")]
    public float glowSeconds = 5f;
    [Tooltip("Glow colour. Seen through walls by every player. Alpha = strength.")]
    public Color glowColor = new Color(1f, 0.95f, 0.62f, 0.55f);

    [Tooltip("The mops glow too before each cleaning time (same timing and colour).")]
    public bool mopsGlow = true;

    [Header("Ghost room film: deadline + penalties")]
    [Range(5f, 300f), Tooltip("Real seconds to change the film after it is due (37.5 s = 15 in-game minutes). Missed = penalty 1: red glow, scream, sanity loss.")]
    public float deadlineSeconds = 37.5f;
    [Tooltip("After a miss the projector glows this colour (through walls) until the film is changed, or until the NEXT film's glow time (then penalty 2).")]
    public Color missedGlowColor = new Color(1f, 0.12f, 0.08f, 0.6f);
    [Tooltip("Map-wide scream when the film is missed. Empty = a made-up placeholder scream.")]
    public AudioClip missedScream;
    [Range(0f, 1f)] public float screamVolume = 0.9f;
    public AlertSounds.Reach screamReach = new AlertSounds.Reach { nearDistance = 10f, farDistance = 90f, farVolume = 0.3f };
    [Range(0f, 1f), Tooltip("Penalty 1: every player loses this part of their CURRENT sanity (0.15 = 15%, rounded down).")]
    public float sanityPartLost = 0.15f;
    [Range(0, 100), Tooltip("Penalty 1: never less than this much sanity.")]
    public int sanityMinLost = 5;
    [Range(0f, 1f), Tooltip("Penalty 2 (still not changed at the NEXT film's glow time): every player loses this part of their CURRENT health (0.5 = 50%, rounded down). Then the missed film is marked done (can no longer be changed).")]
    public float healthPartLost = 0.5f;
    [Range(0, 100), Tooltip("Penalty 2: never less than this much health.")]
    public int healthMinLost = 5;

    [Header("Mop: arrows to the stains")]
    [Range(1f, 60f), Tooltip("While you hold a mop, stains this close (metres) that you can see get an arrow.")]
    public float arrowRange = 15f;
    [Tooltip("Arrow colour (17 Nov: red).")]
    public Color stainArrowColor = new Color(1f, 0.08f, 0.06f, 0.95f);
    [Range(0.2f, 3f), Tooltip("Arrow size. 1 = about 15 cm.")]
    public float arrowSize = 1f;
    [Range(0.1f, 3f), Tooltip("How high above the stain the arrow floats (metres).")]
    public float arrowHeight = 0.55f;

    // ---- Runtime ----------------------------------------------------------------

    private static FilmMopTuning current;

    /// <summary>The asset in Resources, or the defaults above if it does not exist yet.</summary>
    public static FilmMopTuning Current
    {
        get
        {
            if (current != null) return current;
            current = Resources.Load<FilmMopTuning>(ResourcePath);
            if (current == null)
            {
                current = CreateInstance<FilmMopTuning>();
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

    /// <summary>Editor only: the settings file was just created, use it from now on.</summary>
    public static void Use(FilmMopTuning asset)
    {
        if (asset != null) current = asset;
    }

    /// <summary>The match clock in minutes after 00:00, with fractions during the night (smooth timers).</summary>
    public static float NowMinutes(MatchDirector match)
    {
        if (match == null) return 0f;
        if (match.Phase == MatchPhase.Night) return match.PhaseProgress01 * match.NightLengthInGameHours * 60f;
        match.GetInGameTime(out int hour, out int minute);
        return hour * 60 + minute;
    }

    /// <summary>Real seconds until this clock time (minutes after 00:00). Negative = already passed.</summary>
    public static float SecondsUntil(MatchDirector match, float minute) =>
        match == null ? float.MaxValue : (minute - NowMinutes(match)) * match.SecondsPerInGameHour / 60f;

    /// <summary>Penalty 1 amount for this sanity value.</summary>
    public int SanityLoss(float currentSanity) => Mathf.Max(sanityMinLost, Mathf.FloorToInt(currentSanity * sanityPartLost));

    /// <summary>Penalty 2 amount for this health value.</summary>
    public int HealthLoss(float currentHealth) => Mathf.Max(healthMinLost, Mathf.FloorToInt(currentHealth * healthPartLost));
}
