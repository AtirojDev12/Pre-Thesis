using UnityEngine;

/// <summary>
/// 17 Nov 2026 (Mr.k, Task 8 #65): every sentence the LIVE Memo Book shows, in one file the team
/// can rewrite. First English draft by Claude.
///
/// HOW TO USE
///   1. Tools > Pre-Thesis > Designer Settings > Memo Book Texts  (creates the file the first time)
///   2. Change the words in the Inspector. Works live in Play mode.
///   3. Commit the file: Assets/Resources/Tuning/MemoGuideTexts.asset
/// Words in {curly brackets} are filled by the game: {film} = film name, {part} = body part.
/// One step = a few words (big blood letters). The book shows ONE step at a time.
/// </summary>
[CreateAssetMenu(fileName = "MemoGuideTexts", menuName = "Pre-Thesis/Memo Book Texts")]
public sealed class MemoGuideTexts : ScriptableObject
{
    public const string ResourcePath = "Tuning/MemoGuideTexts";
    public const string AssetPath = "Assets/Resources/Tuning/MemoGuideTexts.asset";

    // 25 Nov 2026 (Mr.k): the live book shows ONE short command in big blood letters.
    // Keep every line a few words (it fills the page). Old long texts are replaced once (TextsVersion).

    [Header("Page")]
    public string liveTitle = "What to do now";
    public string dangerTitle = "DANGER";
    [Tooltip("Not in any zone: {zone} = the nearest zone you can walk to. Costs sanity like every new step.")]
    public string goTo = "GO TO {zone}";
    [Tooltip("The name used for the Ticket Zone in \"GO TO\".")]
    public string ticketZoneName = "TICKET SELLING";
    public string allDone = "ALL ZONES DONE";
    public string noZone = "FIND A ZONE";
    public string nothingHere = "NOTHING HERE";
    [Tooltip("Not shown any more (kept for old files).")]
    public string costNote = "";

    [Header("Tasks: tickets")]
    public string waitCustomer = "WAIT FOR A CUSTOMER";
    [Tooltip("{movie} = the movie the customer wants (its name on the movie panel).")]
    public string ticketPressMovie = "PRESS {movie}";
    [Tooltip("{ticket} = the name on the human ticket counter in the game (\"[E]To Give Normal Ticket\" -> GIVE NORMAL TICKET).")]
    public string ticketPressHuman = "{ticket}";
    [Tooltip("{ticket} = the name on the ghost ticket counter in the game.")]
    public string ticketPressGhost = "{ticket}";
    public string ticketSell = "SELL A TICKET";

    [Header("Tasks: popcorn (one step at a time)")]
    [Tooltip("Names in {brackets} are the in-game names (Popcorn Minigame Systems > Focus Names).")]
    public string foodTakeBucket = "TAKE {bucket}";
    public string popcornFill = "FILL AT {tank}";
    [Tooltip("{flavor} = the flavour station's in-game name, e.g. Cheese Flavor.")]
    public string popcornAddFlavor = "ADD {flavor}";
    public string addGhostFlavor = "ADD {ghost}";
    public string foodServe = "GIVE TO {customer}";
    public string foodWrong = "WRONG ONE. PRESS R";

    [Header("Tasks: drinks (one step at a time)")]
    public string drinkTakeCup = "TAKE {cup}";
    [Tooltip("{dispenser} = the in-game name of the drink's dispenser, e.g. Pepsi Dispenser.")]
    public string drinkPlaceCup = "PUT {cup} AT {dispenser}";
    public string drinkPress = "PRESS {dispenser}";
    public string drinkTakeBack = "TAKE {cup}";

    [Header("Old (kept for old files, not used)")]
    public string foodPick = "TAKE A BUCKET";
    public string drinkFill = "PRESS {drink}";
    public string popcornFlavor = "ADD FLAVOR";

    [Header("Tasks: film change")]
    [Tooltip("{time} = when the next film change is.")]
    public string filmWait = "WAIT UNTIL {time}";
    public string filmTake = "TAKE FILM {film}";
    public string filmInsert = "PUT FILM IN";

    [Header("Tasks: cleaning")]
    [Tooltip("{time} = when the stains come.")]
    public string cleanWait = "WAIT UNTIL {time}";
    public string cleanGetMop = "GET A MOP";
    public string cleanStains = "MOP THE STAINS";

    [Header("Rules: Ghost Screen Room")]
    public string audienceStill = "DON'T MOVE";
    public string audienceLeave = "LEAVE THE ROOM";
    public string staffFindShadow = "SIT ON THE SHADOW";
    public string staffStay = "STAY SEATED";

    [Header("Rules: toilets")]
    public string mirrorLook = "LOOK AT THE CRACK";
    public string cryingLightOff = "LIGHT OFF";
    public string cryingStill = "STAND STILL";
    public string smokeKnock = "KNOCK ON THE STALL";
    public string smokeLightOff = "LIGHT OFF";
    public string smokeLeave = "GET OUT";
    public string foodLightOff = "LIGHT OFF";
    public string foodThrowBox = "THROW THE BOX IN";
    public string foodLeave = "EVERYONE OUT";

    [Header("Rules: Ticket Zone")]
    public string ticketGhostSigns = "PLACE THE SIGNS";
    public string ticketGhostQuiet = "LIGHTS OFF. BE QUIET";
    public string partsHelp = "PRESS E ON THE GHOST";
    [Tooltip("{part} = the body part.")]
    public string partsFind = "FIND ITS {part}";
    public string flavorFind = "FIND THE GHOST FLAVOR";
    [Tooltip("{film} = the film.")]
    public string filmLate = "FILM {film} NOW";

    [Header("Map Rules + map events (23 Nov)")]
    public string vipBring = "LEAD THE VIP TO ITS SEAT";
    public string vipCrank = "GOLD FILM IN. CRANK FAST";
    public string hunterHide = "HIDE FROM THE LIGHT";
    public string foreignFind = "FIND THEM. HIT THEM";
    public string foreignPest = "DON'T LOOK AT IT";
    public string filmGhostAvoid = "DON'T LOOK. CHANGE THE FILM";

    // 25 Nov: files older than this get the short texts once (then designers can edit again).
    private const int TextsVersion = 3;
    [SerializeField, HideInInspector] private int textsVersion;

    // ---- Runtime ----------------------------------------------------------------

    private static MemoGuideTexts current;

    public static MemoGuideTexts Current
    {
        get
        {
            if (current != null) return current;
            current = Resources.Load<MemoGuideTexts>(ResourcePath);
            if (current == null)
            {
                current = CreateInstance<MemoGuideTexts>();
                current.hideFlags = HideFlags.HideAndDontSave;
                current.textsVersion = TextsVersion;
            }
            else if (current.textsVersion < TextsVersion) current.UseShortTexts();
            return current;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        if (current != null && (current.hideFlags & HideFlags.DontSave) != 0) DestroyImmediate(current);
        current = null;
    }

    /// <summary>25 Nov: an old file (long sentences) gets every short default text once.</summary>
    private void UseShortTexts()
    {
        MemoGuideTexts fresh = CreateInstance<MemoGuideTexts>();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(fresh), this);
        DestroyImmediate(fresh);
        textsVersion = TextsVersion;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this); // saved with the project: commit MemoGuideTexts.asset
#endif
        Debug.Log("[MemoGuideTexts] Short live-book texts applied (old long texts replaced).");
    }

    public static void Use(MemoGuideTexts asset)
    {
        if (asset != null) current = asset;
    }
}
