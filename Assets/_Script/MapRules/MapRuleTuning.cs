using UnityEngine;

/// <summary>
/// 23 Nov 2026 (Mr.k): every number and sentence of the Map Rules and the map events.
/// Designer Settings: Tools > Pre-Thesis > Designer Settings > Map Rules.
/// File: Assets/Resources/Tuning/MapRuleTuning.asset (commit it after changes).
///
///   Film ghost   : at film change time a ghost from the ghost room roams the map; looking at it hurts.
///   Toilet ghosts: at toilet cleaning time a female + a male ghost walk around saying the toilet is dirty.
///   Map Rule 3   : 3 AM VIP guest. The clock stops. Take it to its seat, crank the projector.
///   Map Rule 2   : after the VIP: lights out + a flashlight hunter for 1 in-game hour.
///   Map Rule 1   : after the VIP: hidden things. Hit them with the Bat (shop) or the mop.
/// </summary>
[CreateAssetMenu(fileName = "MapRuleTuning", menuName = "Pre-Thesis/Map Rule Tuning")]
public sealed class MapRuleTuning : ScriptableObject
{
    public const string ResourcePath = "Tuning/MapRuleTuning";
    public const string AssetPath = "Assets/Resources/Tuning/MapRuleTuning.asset";

    [Header("Map Rules: when")]
    [Tooltip("In-game hour the VIP arrives (the other two Map Rules follow it).")]
    [Range(0, 5)] public int mapRulesHour = 3;

    [Header("Hitting (Bat / mop)")]
    [Tooltip("How close you must be to hit (metres).")]
    [Min(1f)] public float hitReach = 2.6f;
    public string hitLine = "*WHACK*";

    [Header("Bat (lobby shop)")]
    public string batName = "Bat";
    [Min(0)] public int batPrice = 150;
    [TextArea(1, 3)] public string batDescription = "A baseball bat. Left Click to hit the hidden things after 3 AM (the mop works too). Lost if you die.";

    [Header("Film ghost (film change time)")]
    [Min(0.1f)] public float filmGhostSpeed = 1.5f;
    [Tooltip("Health lost per second while you look at it.")]
    [Min(0f)] public float filmGhostDamagePerSecond = 5f;
    [Range(2f, 30f)] public float filmGhostLookAngle = 9f;
    [Min(1f)] public float filmGhostLookDistance = 25f;
    public string filmGhostLookLine = "Don't look at it...";

    [Header("Toilet ghosts (toilet cleaning time)")]
    [Min(0.1f)] public float toiletGhostSpeed = 1.8f;
    [Tooltip("They say these lines, one after another.")]
    public string[] toiletLines =
    {
        "The toilet is dirty!",
        "Somebody clean the toilet!",
        "Staff! The toilets are filthy!",
        "Go clean the toilet... please...",
    };
    [Min(1f)] public float toiletLineSeconds = 4f;
    [Tooltip("Players this close also get the line on screen.")]
    [Min(0f)] public float toiletShoutRange = 9f;

    [Header("Map Rule 3: VIP guest")]
    [Tooltip("Real seconds to seat the VIP AND crank the film (the clock is stopped).")]
    [Min(10f)] public float vipSeconds = 90f;
    [Min(0.1f)] public float vipSpeed = 3.2f;
    [Min(0.5f)] public float vipFollowDistance = 1.6f;
    [Tooltip("The VIP sits down when it is this close to its seat.")]
    [Min(0.5f)] public float vipSeatRadius = 2.2f;
    [Tooltip("E presses on the crank projector to play the whole film.")]
    [Min(1)] public int crankPresses = 20;
    [Tooltip("Part of the CURRENT sanity everyone loses if the VIP is not served in time (decimals rounded down).")]
    [Range(0f, 1f)] public float vipFailSanityPart = 0.5f;
    public string vipArriveLine = "A VIP guest has arrived. The clock is stopped! Take the VIP to the ghost screen room.";
    public string vipPrompt = "Press E: take the VIP guest";
    public string vipFollowLine = "Lead the way.";
    public string vipSeatedLine = "The VIP is seated. Crank the projector in the ghost room!";
    public string crankPrompt = "Press E fast: crank the projector";
    public string crankWaitPrompt = "Bring the VIP to its seat first";
    public string crankNeedFilmPrompt = "Needs the VIP film (gold reel on the ghost room film shelf)";
    public string crankFilmPrompt = "Press E: put the VIP film in";
    public string filmInLine = "The VIP film is in. Seat the VIP, then crank!";
    public string vipDoneLine = "The VIP enjoyed the film. Time moves again.";
    public string vipFailLine = "The VIP is furious...";

    [Header("Map Rule 2: lights out + flashlight hunter")]
    [Tooltip("In-game hours the lights stay out and the hunter walks.")]
    [Min(0.25f)] public float hunterHours = 1f;
    [Min(0.1f)] public float hunterWalkSpeed = 1.7f;
    [Min(0.1f)] public float hunterRunSpeed = 4.4f;
    [Tooltip("How far its flashlight beam spots you (metres).")]
    [Min(2f)] public float hunterBeamRange = 12f;
    [Tooltip("Half the width of the beam (degrees).")]
    [Range(5f, 60f)] public float hunterBeamAngle = 22f;
    [Min(0f)] public float hunterLightIntensity = 25f;
    [Tooltip("It catches you this close (metres): jumpscare + instant death.")]
    [Min(0.3f)] public float hunterCatchRange = 1.1f;
    [Tooltip("It gives up the chase after this many seconds without seeing you.")]
    [Min(0.5f)] public float hunterGiveUpSeconds = 4f;
    public string lightsOutLine = "The lights went out... Something walks with a flashlight. Don't let it see you.";
    public string hunterSpottedLine = "It saw you. RUN!";
    public string lightsBackLine = "The lights are back.";

    [Header("Map Rule 1: hidden things")]
    [Min(0)] public int foreignCount = 5;
    [Tooltip("Real seconds to find and hit each thing before it gets in.")]
    [Min(5f)] public float foreignSeconds = 75f;
    [Tooltip("Hidden things stay at least this far from every player when they appear.")]
    [Min(0f)] public float foreignMinPlayerDistance = 8f;
    [Tooltip("A thing that got in blocks the chore spot it stands at for this long, then moves on.")]
    [Min(1f)] public float pestBlockSeconds = 20f;
    [Tooltip("Sanity lost each time you see a thing that got in.")]
    [Min(0f)] public float pestSanityOnSight = 3f;
    [Min(0.5f)] public float pestSightCooldown = 8f;
    [Range(5f, 45f)] public float pestSightAngle = 25f;
    [Min(0.1f)] public float pestSpeed = 2.4f;
    public string foreignStartLine = "Something is hiding in the building. Find it and hit it out!";
    public string foreignOutLine = "Driven out!";
    public string foreignInLine = "Something got in...";
    public string pestBlockLine = "Something is in the way...";

    [Header("Seats (23 Nov)")]
    [Tooltip("A sitting player is lowered this much (metres), so the eyes are at sitting height.")]
    [Range(0f, 0.8f)] public float playerSitDrop = 0.42f;
    [Tooltip("Sitting pose: thighs point forward, this many degrees below flat.")]
    [Range(-20f, 40f)] public float sitThighDownDegrees = 8f;
    [Tooltip("Sitting pose: shins point down, leaning this many degrees forward.")]
    [Range(-30f, 40f)] public float sitShinForwardDegrees = 6f;
    [Tooltip("Seconds for a seat to fold open / shut.")]
    [Min(0.01f)] public float seatFoldSeconds = 0.25f;
    public string sitPrompt = "Press E: sit";
    public string standHint = "[SPACE] stand up";

    private static MapRuleTuning current;

    /// <summary>The asset in Resources, or built-in defaults when it does not exist yet.</summary>
    public static MapRuleTuning Current
    {
        get
        {
            if (current != null) return current;
            current = Resources.Load<MapRuleTuning>(ResourcePath);
            if (current == null)
            {
                current = CreateInstance<MapRuleTuning>();
                current.hideFlags = HideFlags.DontSave;
            }
            return current;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => current = null;

    /// <summary>Editor tool: use this asset from now on.</summary>
    public static void Use(MapRuleTuning asset) => current = asset;
}
