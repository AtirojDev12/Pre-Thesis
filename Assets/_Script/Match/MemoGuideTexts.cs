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
/// One step = one short sentence. The book shows ONE step at a time.
/// </summary>
[CreateAssetMenu(fileName = "MemoGuideTexts", menuName = "Pre-Thesis/Memo Book Texts")]
public sealed class MemoGuideTexts : ScriptableObject
{
    public const string ResourcePath = "Tuning/MemoGuideTexts";
    public const string AssetPath = "Assets/Resources/Tuning/MemoGuideTexts.asset";

    [Header("Page")]
    public string liveTitle = "What to do now";
    public string dangerTitle = "DANGER - do this now";
    public string noZone = "Walk into a work zone. This page tells you what to do there.";
    public string nothingHere = "Nothing to do in this zone right now. Try another zone.";
    [Tooltip("Shown under the step: what reading costs.")]
    public string costNote = "Each new step you read costs a little sanity.";

    [Header("Tasks: tickets")]
    [TextArea(1, 3)] public string ticketSell = "Stand behind the ticket counter. Sell each customer the right ticket: a ghost ticket for a ghost, a human ticket for a human.";

    [Header("Tasks: popcorn / drinks (one step at a time)")]
    [TextArea(1, 3)] public string foodPick = "Pick up a popcorn bucket (or a cup for a drink) at the station.";
    [TextArea(1, 3)] public string popcornFill = "Fill the bucket at the popcorn machine.";
    [TextArea(1, 3)] public string drinkFill = "Fill the cup at the drink machine with the drink the customer asked for.";
    [TextArea(1, 3)] public string popcornFlavor = "Add the flavour the customer asked for. A ghost customer also needs the Ghost Flavor.";
    [TextArea(1, 3)] public string foodServe = "Give it to the customer at the counter.";

    [Header("Tasks: film change")]
    [TextArea(1, 3)] public string filmWait = "No film change yet. The projector glows yellow before the next one. Do other work meanwhile.";
    [TextArea(1, 3)] public string filmTake = "Take the film \"{film}\" from the film shelf next to the projector.";
    [TextArea(1, 3)] public string filmInsert = "Hold E at the projector to put the film \"{film}\" in.";

    [Header("Tasks: cleaning")]
    [TextArea(1, 3)] public string cleanWait = "Nothing to clean here yet. The mops glow before cleaning time.";
    [TextArea(1, 3)] public string cleanGetMop = "Get a mop from the Staff Room.";
    [TextArea(1, 3)] public string cleanStains = "Follow the red arrows and hold E on each stain.";

    [Header("Rules: Ghost Screen Room")]
    [TextArea(1, 3)] public string audienceStill = "The audience is standing and staring. Do NOT move.";
    [TextArea(1, 3)] public string audienceLeave = "Leave the Ghost Screen Room now.";
    [TextArea(1, 3)] public string staffFindShadow = "The screen shows static. Find the black shadow on a seat and stand on it.";
    [TextArea(1, 3)] public string staffStay = "Stay on the shadow until the screen comes back.";

    [Header("Rules: toilets")]
    [TextArea(1, 3)] public string mirrorLook = "A mirror is cracked. Look at the crack until it stops.";
    [TextArea(1, 3)] public string cryingLightOff = "Someone is crying in the last stall. Turn the toilet light OFF (switch by the door).";
    [TextArea(1, 3)] public string cryingStill = "Stay still in the dark until the crying stops.";
    [TextArea(1, 3)] public string smokeKnock = "Smoke comes from a stall. Knock on that stall door (E).";
    [TextArea(1, 3)] public string smokeLightOff = "Now turn the toilet light OFF.";
    [TextArea(1, 3)] public string smokeLeave = "The smoke hurts. Get out of this toilet.";
    [TextArea(1, 3)] public string foodLightOff = "Feed the ghost: turn the toilet light OFF first.";
    [TextArea(1, 3)] public string foodThrowBox = "Throw the black box in at the last stall (E on the drop spot).";
    [TextArea(1, 3)] public string foodLeave = "Everyone out of the toilet now.";

    [Header("Rules: Ticket Zone")]
    [TextArea(1, 3)] public string ticketGhostSigns = "A ghost is coming back. Put each RESTROOM BREAK sign on its blue spot.";
    [TextArea(1, 3)] public string ticketGhostQuiet = "Turn the Ticket Zone lights OFF and stay quiet until the ghost leaves.";
    [TextArea(1, 3)] public string partsHelp = "A ghost at the counter lost a body part. Press E on it to help.";
    [TextArea(1, 3)] public string partsFind = "Find its {part} (it cries louder when you are close) and bring it back to the ghost.";
    [TextArea(1, 3)] public string flavorFind = "The Ghost Flavor is gone. Lights off, find it (follow the trail) and bring it back to the popcorn station.";
    [TextArea(1, 3)] public string filmLate = "The ghost room film is LATE. Put the film \"{film}\" in the projector before the next film, or everyone gets hurt.";

    [Header("Map Rules + map events (23 Nov)")]
    [TextArea(1, 3)] public string vipBring = "A VIP guest is here and the clock is stopped. Press E on the VIP and lead it to its seat in the ghost screen room.";
    [TextArea(1, 3)] public string vipCrank = "The VIP is seated. Crank the projector in the ghost room: press E fast until the film is done.";
    [TextArea(1, 3)] public string hunterHide = "Lights out. Stay out of the flashlight beam. If it sees you, RUN and break its line of sight.";
    [TextArea(1, 3)] public string foreignFind = "Things are hiding against the walls. Find them and hit them (Left Click) with a Bat or a mop before they get in.";
    [TextArea(1, 3)] public string foreignPest = "Something got in. Don't look at it, and work at another spot while it blocks one.";
    [TextArea(1, 3)] public string filmGhostAvoid = "A ghost from the ghost room walks the halls. Don't look at it. Change the ghost room film to send it back.";

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

    public static void Use(MemoGuideTexts asset)
    {
        if (asset != null) current = asset;
    }
}
