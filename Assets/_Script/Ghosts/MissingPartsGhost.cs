using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 31 Oct 2026 (Mr.k): the MISSING PARTS ghost.
///
/// WHAT IT DOES
///   1. Comes ONCE per round, at a random time between 00:00 and 03:00 (guaranteed).
///   2. Walks in like a customer to the TICKET or the POPCORN counter (random).
///      From that moment the counter takes no new customer (GhostCounterGate).
///      If a customer is still there, it waits in line behind them.
///   3. It is missing one easy-to-see upper-body part: one arm, both arms (2 items),
///      the head, or the whole upper body. Those parts lie somewhere in the map:
///      indoors (a ceiling above), reachable on foot from a player, never in the
///      ticket / popcorn zone boxes (31 Oct fix: no more roof / street spots).
///   4. At the counter it asks for its part in a speech bubble, until the first E on it
///      (then the bubble never comes back). No head / no upper body = no speech.
///      A countdown (real minutes:seconds) floats above its head once the timer runs.
///   5. A player presses E on it: it FOLLOWS that player (the counter is free again).
///      Any other player can press E to make it follow them instead.
///   6. Players find a part, press E to put it in the hotbar, hold it in the hand and
///      HOLD E on the ghost to put it back.
///   7. All parts back = it walks away and disappears.
///   8. TIME LIMIT: 1 in-game hour (31 Oct, was 2) from when it stops at the counter OR the first
///      E on it (whichever comes first). Time up = it attacks, then disappears:
///      -25 health and -25% of current sanity to every player within 50 m, every
///      player who ever pressed E on it, and every player in the ticket zone box.
///
/// SERVER AUTHORITY: the server decides everything (time, route, follow, parts,
/// attack). Clients only show it: SyncVars + NetworkTransform on this object.
/// The look (body, bubble, part in your hand) is built locally on every machine.
///
/// SETUP: Tools > Pre-Thesis > One Step > Ghosts: Missing Parts Ghost
///   (builds the part items, then puts this ghost in the open Cinema_GamePlay scene).
/// LOOK: placeholder shapes until a model is set in "Custom Model (optional)".
/// </summary>
[DisallowMultipleComponent]
public sealed class MissingPartsGhost : InteractableBase, IDevGhostSource
{
    public enum Stage : byte { Idle, WalkingIn, Queued, AtCounter, Following, Lost, Leaving, Gone }
    public enum CounterKind : byte { Ticket, Popcorn }

    /// <summary>The same route the counter's customers use (copied by the setup tool).</summary>
    [System.Serializable]
    public sealed class CounterRoute
    {
        public Transform spawn;
        public Transform[] approach = new Transform[0];
        [Tooltip("Position AND facing while standing at the counter.")]
        public Transform counter;
        public Transform[] departure = new Transform[0];
        public Transform exit;
        public bool IsValid => spawn != null && counter != null;
    }

    [Header("When (match clock)")]
    [Tooltip("Earliest in-game hour it can come (0 = 00:00).")]
    [SerializeField, Range(0, 5)] private int earliestHour = 0;
    [Tooltip("It always comes BEFORE this hour (3 = before 03:00).")]
    [SerializeField, Range(1, 6)] private int latestHour = 3;
    [Tooltip("Chance it goes to the ticket counter (else the popcorn counter).")]
    [SerializeField, Range(0f, 1f)] private float ticketCounterChance = 0.5f;

    [Header("Time limit and attack")]
    [Tooltip("In-game hours to bring every part back.")]
    // 31 Oct: 1 hour (was 2). New field name on purpose, so the scene's old saved "2" is not used.
    [SerializeField, Min(0.1f)] private float timeLimitInGameHours = 1f;
    [SerializeField, Min(0f)] private float attackRadius = 50f;
    [SerializeField, Min(0f)] private float attackDamage = 25f;
    [Tooltip("Part of the CURRENT sanity lost (0.25 = 25%).")]
    [SerializeField, Range(0f, 1f)] private float sanityLossFraction = 0.25f;

    [Header("Giving a part back")]
    [SerializeField, Min(0.5f)] private float attachSeconds = 3f;

    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float walkSpeed = 2.2f;
    [SerializeField, Min(0.1f)] private float followSpeed = 3.5f;
    [Tooltip("Faster when the player it follows is far away.")]
    [SerializeField, Min(0.1f)] private float catchUpSpeed = 5.5f;
    [SerializeField, Min(1f)] private float catchUpDistance = 10f;
    [SerializeField, Min(0.5f)] private float followDistance = 1.8f;
    [Tooltip("NavMesh agent type (copied from the scene's NavMeshSurface by the setup tool).")]
    [SerializeField] private int agentTypeID;

    [Header("Speech (no head / no upper body = no speech)")]
    [TextArea] [SerializeField] private string leftArmLine = "Excuse me... have you seen my left arm? I had it when I came in.";
    [TextArea] [SerializeField] private string rightArmLine = "Excuse me... have you seen my right arm? I can't hold anything without it.";
    [TextArea] [SerializeField] private string bothArmsLine = "My arms... both of my arms are gone. Please... help me find them.";
    [SerializeField] private Vector3 bubbleOffset = new Vector3(0f, 1.2f, 0f);

    [Header("Counters (filled by the setup tool)")]
    [SerializeField] private TicketMinigame ticketCounter;
    [SerializeField] private CounterRoute ticketRoute = new CounterRoute();
    [SerializeField] private CounterRoute popcornRoute = new CounterRoute();

    [Header("Ticket Zone box (15 Nov)")]
    [Tooltip("The Ticket Zone's zone box (RuleZone). When set, it replaces the two old boxes below: players inside it are attacked when time runs out, and parts never spawn inside it.")]
    [SerializeField] private RuleZone ticketZoneBox;

    [Header("Old zone boxes (used only when Ticket Zone Box is empty)")]
    [Tooltip("Players inside when time runs out are attacked. Parts never spawn inside.")]
    [SerializeField] private Bounds ticketZone = new Bounds(Vector3.zero, new Vector3(14f, 5f, 14f));
    [Tooltip("Parts never spawn inside.")]
    [SerializeField] private Bounds popcornZone = new Bounds(Vector3.zero, new Vector3(14f, 5f, 14f));

    [Header("Where parts can appear")]
    [Tooltip("Optional. Empty = random walkable spots on the NavMesh (outside the zone boxes).")]
    [SerializeField] private Transform[] partSpawnPoints = new Transform[0];
    [Tooltip("Optional. If set, parts appear ONLY inside these boxes (e.g. one box over the whole cinema).")]
    [SerializeField] private Bounds[] insideAreas = new Bounds[0];
    [Tooltip("A part needs a ceiling above it within this many metres (= indoors, never on the roof or outside).")]
    [SerializeField, Min(2f)] private float maxCeilingHeight = 12f;
    [SerializeField, Min(0f)] private float minDistanceFromCounters = 12f;
    [SerializeField, Min(0f)] private float minDistanceBetweenParts = 8f;

    [Header("Sounds (optional) - 19 Nov: set in MissingPartSound.asset; these are only used if the asset's clip is empty")]
    [SerializeField] private AudioClip attackClip;
    [SerializeField] private AudioClip partReturnedClip;

    [Header("Custom Model (optional; empty = placeholder shapes)")]
    [Tooltip("The designer's body. Its centre should be about 1 m above the floor (like the customers).")]
    [SerializeField] private GameObject customModel;
    [SerializeField] private GameObject customHead;
    [SerializeField] private GameObject customLeftArm;
    [SerializeField] private GameObject customRightArm;
    [Tooltip("Torso + head + arms together (hidden when the upper body is missing).")]
    [SerializeField] private GameObject customUpperBody;

    private const float ServerRange = 4.5f;
    private const float QueueGap = 1.6f;

    // ---- Server writes, everyone reads ----
    [SyncVar] private Stage stage = Stage.Idle;
    [SyncVar] private CounterKind counterKind;
    [SyncVar] private GhostBodyPart originalMissing;
    [SyncVar] private GhostBodyPart missing;
    [SyncVar] private uint followId;
    [SyncVar] private double deadline;  // 0 = timer not started
    [SyncVar] private uint workerId;
    [SyncVar] private double workStart;
    [SyncVar] private bool helped; // 31 Oct: someone pressed E once = the speech bubble never shows again

    // ---- Server only ----
    private bool spawnedThisRound;
    private int spawnMinute = -1;
    private float roundSeenAt = -1f;
    private readonly List<Vector3> path = new List<Vector3>();
    private int waypoint;
    private Vector3 queuePoint;
    private readonly HashSet<uint> interacted = new HashSet<uint>();
    private NavMeshAgent agent;
    private bool agentReady;
    private float nextRepath;
    private float leaveUntil;
    private GhostBodyPart workPart;
    private GameObject offlineLeader, offlineWorker;
    private bool gateHeld;

    // ---- Local only ----
    private GameObject placeholderRoot;
    private GameObject head, leftArm, rightArm, upperBody;
    private GameObject neckStump, leftStump, rightStump, waistStump;
    private Renderer[] bodyRenderers = new Renderer[0];
    private Collider[] bodyColliders = new Collider[0];
    private bool lookVisible = true;
    private GhostBodyPart lookMissing = (GhostBodyPart)255;
    private Canvas bubble;
    private Canvas timerCanvas;   // 31 Oct: countdown above its head
    private TMP_Text timerText;
    private int shownSeconds = -1;
    private TMP_Text bubbleText;
    private bool stopSent;
    private GameObject handVisual;
    private GhostBodyPart handPart;
    // 19 Nov: finding sound (4 levels, replaces the single crying loop). Two voices for crossfades.
    private sealed class FindVoice { public AudioSource source; public SoundCategoryVolume volume; public float fade; public int level = -1; }
    private readonly FindVoice[] findVoices = new FindVoice[2];
    private int findActive;
    private int findLevel = -1;            // -1 = silent, 0 = Nearest .. 3 = Far Away
    private float nextFindScan;
    private bool findPartFound;
    private Vector3 findPartPosition;
    private Transform findListener;        // the local ears (camera / AudioListener)
    private AudioSource standbySource;    // 19 Nov: GhostFindBodySound while it waits for the first E
    private SoundCategoryVolume standbyVolume;
    private float soundsCutUntil;          // 21 Nov: after the success sound, keep the rest quiet until the synced state catches up
    private float standbyLoudness;
    private bool standbyWasOn;
    private Stage loggedStage = Stage.Idle;   // 19 Nov: debug, Console line on every stage change
    private enum VoiceKind { None, HelpMe, PartVoice }
    private AudioSource voiceSource;      // 19 Nov: HelpME / I am here / Over here lines
    private SoundCategoryVolume voiceVolume;
    private VoiceKind voiceKind;
    private long voiceSlot;
    private long voiceSkipSlot = long.MinValue;
    private AudioSource workSource;       // 19 Nov: MIssingPartWorkonProcess while someone holds E to put a part back
    private SoundCategoryVolume workVolume;
    private float workLoudness;
    private bool workWasOn;

    private static double Now => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;
    private bool HasAuthority => NetworkMode.HasServerAuthority(this);
    private bool Active => stage >= Stage.WalkingIn && stage <= Stage.Leaving;
    private bool Helpable => stage >= Stage.WalkingIn && stage <= Stage.Lost;

    // 17 Nov: a small id for Memo Book step keys (GetInstanceID is obsolete in Unity 6.5).
    private int guideUid;
    private static int nextGuideUid;
    private int GuideUid => guideUid != 0 ? guideUid : (guideUid = ++nextGuideUid);

    /// <summary>
    /// SERVER. 17 Nov (Task 8): Memo Book step for a player in the Ticket Zone (or the one it follows).
    /// Before anyone helped: "press E on it". Then: "find its part". With the timer running it is a
    /// chase-level danger (the ghost attacks on timeout), else it only blocks work.
    /// </summary>
    public bool ServerGuideFor(PlayerHealth player, out GuideEntry entry)
    {
        entry = default;
        if (player == null || !Helpable || missing == GhostBodyPart.None) return false;
        bool follows = followId != 0 && player.netId == followId;
        if (!follows && !MemoGuide.InZoneOrNoBox(ZoneTaskList.TicketZoneID, player.transform.position)) return false;
        MemoGuideTexts t = MemoGuideTexts.Current;
        GuideSeverity severity = deadline > 0d ? GuideSeverity.ChaseOrHp : GuideSeverity.TaskBlock;
        string key = "parts:" + GuideUid + ":" + (int)missing + ":" + (helped ? 1 : 0);
        if (!helped)
        {
            entry = GuideEntry.Make(severity, key, t.partsHelp);
            return true;
        }
        string part = "missing part";
        foreach (GhostBodyPart p in GhostBodyParts.All)
            if ((missing & p) != 0) { part = GhostBodyParts.Name(p); break; }
        entry = GuideEntry.Make(severity, key, t.partsFind.Replace("{part}", part.ToLowerInvariant()));
        return true;
    }
    private CounterRoute Route => counterKind == CounterKind.Ticket ? ticketRoute : popcornRoute;
    private bool HasMouth => (originalMissing & (GhostBodyPart.Head | GhostBodyPart.UpperBody)) == 0;

    /// <summary>Every machine: the ghost is in the map right now.</summary>
    public bool IsActive => Active;
    /// <summary>Every machine: seconds left before it attacks, or -1 if the timer has not started.</summary>
    public float SecondsLeft => deadline <= 0d ? -1f : Mathf.Max(0f, (float)(deadline - Now));

    // ======================================================================
    // Setup
    // ======================================================================

    protected override void Awake()
    {
        base.Awake();
        agent = GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false; // only the server turns it on
        BuildBody();
        BuildBubble();
        BuildTimer();
        ApplyLook(true);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        ServerResetRound();
    }

    private void Start()
    {
        if (NetworkMode.IsOffline) ServerResetRound();
    }

    private void ServerResetRound()
    {
        spawnedThisRound = false;
        roundSeenAt = -1f;
        int from = Mathf.Clamp(earliestHour, 0, 5) * 60;
        int to = Mathf.Max(from + 1, Mathf.Clamp(latestHour, 1, 6) * 60);
        spawnMinute = Random.Range(from, to); // max is exclusive: always BEFORE latestHour
    }

    private void OnDisable()
    {
        ChoreProgressHUD.Hide(this);
        ClearHand();
        if (HasAuthority) ReleaseGate();
        for (int i = 0; i < findVoices.Length; i++) // 19 Nov: made again when it is needed next
        {
            if (findVoices[i] != null && findVoices[i].source != null) Destroy(findVoices[i].source.gameObject);
            findVoices[i] = null;
        }
        findLevel = -1;
        if (standbySource != null) Destroy(standbySource.gameObject); // 19 Nov
        standbySource = null;
        standbyLoudness = 0f;
        standbyWasOn = false;
        if (voiceSource != null) Destroy(voiceSource.gameObject); // 19 Nov
        voiceSource = null;
        voiceKind = VoiceKind.None;
        if (workSource != null) Destroy(workSource.gameObject); // 19 Nov
        workSource = null;
        workLoudness = 0f;
        workWasOn = false;
    }

    // ======================================================================
    // Interaction (prompt is local, the E press runs on the server)
    // ======================================================================

    public override string GetInteractionPrompt()
    {
        if (!Helpable) return null;
        PlayerInventory inventory = PlayerInventory.Local;
        GhostBodyPart held = inventory != null && !inventory.HeldSlot.IsEmpty
            ? GhostBodyParts.FromItem(inventory.HeldSlot.itemId) : GhostBodyPart.None;
        if (held != GhostBodyPart.None)
            return (missing & held) != 0 ? "[Hold E] Give back the " + GhostBodyParts.Name(held) : "It does not need this";
        PlayerHealth me = PlayerHealth.LocalInstance;
        uint myId = me == null ? 0u : NetworkMode.IsOffline ? 1u : me.netId;
        if (stage == Stage.Following && followId == myId) return null; // already following you
        if (stage == Stage.Following) return "[E] Make it follow you";
        return "[E] Help it";
    }

    public override bool CanInteract() => Helpable && (workerId == 0 || IsLocalWorker);

    /// <summary>SERVER: E pressed on the ghost.</summary>
    protected override void OnInteracted(GameObject interactor)
    {
        if (!Helpable || interactor == null) return;
        PlayerHealth health = interactor.GetComponent<PlayerHealth>();
        if (health == null || health.IsDead || health.IsDowned) return;
        if ((interactor.transform.position - transform.position).sqrMagnitude > ServerRange * ServerRange) return;

        uint id = IdOf(interactor);
        interacted.Add(id);
        helped = true;
        StartTimer();

        // Holding one of its missing parts = start putting it back (hold E).
        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();
        GhostBodyPart held = inventory != null && !inventory.HeldSlot.IsEmpty
            ? GhostBodyParts.FromItem(inventory.HeldSlot.itemId) : GhostBodyPart.None;
        if (held != GhostBodyPart.None)
        {
            if ((missing & held) == 0 || (workerId != 0 && workerId != id)) return;
            workPart = held;
            offlineWorker = NetworkMode.IsOffline ? interactor : null;
            workerId = id;
            workStart = Now;
            return;
        }

        // Otherwise: follow this player.
        followId = id;
        offlineLeader = NetworkMode.IsOffline ? interactor : null;
        stage = Stage.Following;
        ReleaseGate(); // the counter can take customers again
    }

    /// <summary>Worker's PC -> server: "I let go of E".</summary>
    [Command(requiresAuthority = false)]
    private void CmdStopWork(NetworkConnectionToClient sender = null)
    {
        if (sender != null && sender.identity != null && sender.identity.netId == workerId) StopWork();
    }

    private void StopWork()
    {
        workerId = 0;
        workPart = GhostBodyPart.None;
        offlineWorker = null;
    }

    // ======================================================================
    // Server
    // ======================================================================

    private void Update()
    {
        if (HasAuthority) ServerTick();
        LocalTick();
    }

    private void ServerTick()
    {
        MatchDirector match = MatchDirector.Instance;
        if (match != null && !match.RoundRunning)
        {
            if (Active) ServerGone();
            return;
        }

        if (!spawnedThisRound)
        {
            if (ShouldSpawn(match)) ServerSpawn();
            return;
        }
        if (!Active) return;

        if (deadline > 0d && Now >= deadline && missing != GhostBodyPart.None && stage != Stage.Leaving)
        {
            ServerAttack();
            return;
        }

        ServerTickWork();
        if (!Active) return;

        switch (stage)
        {
            case Stage.WalkingIn:
            case Stage.Queued:
            case Stage.AtCounter:
                ServerTickCounter();
                break;
            case Stage.Following:
                ServerTickFollow();
                break;
            case Stage.Lost:
                StopAgent();
                break;
            case Stage.Leaving:
                ServerTickLeaving();
                break;
        }
    }

    private bool ShouldSpawn(MatchDirector match)
    {
        if (match == null || match.Phase != MatchPhase.Night) return false;
        // Give the round a moment: bodies spawned, counters running.
        if (roundSeenAt < 0f)
        {
            if (PlayerRegistry.All.Count == 0) return false;
            roundSeenAt = Time.time;
        }
        if (Time.time - roundSeenAt < 5f) return false;
        match.GetInGameTime(out int hour, out int minute);
        return hour * 60 + minute >= spawnMinute;
    }

    /// <summary>SERVER. The ghost appears at its counter's spawn point and walks in.</summary>
    private void ServerSpawn()
    {
        bool ticketOk = ticketRoute.IsValid && ticketCounter != null;
        bool popcornOk = popcornRoute.IsValid;
        if (!ticketOk && !popcornOk)
        {
            spawnedThisRound = true;
            Debug.LogError("[MissingPartsGhost] No counter route set. Run Tools > Pre-Thesis > One Step > Ghosts: Missing Parts Ghost.", this);
            return;
        }
        spawnedThisRound = true;
        counterKind = ticketOk && (!popcornOk || Random.value < ticketCounterChance) ? CounterKind.Ticket : CounterKind.Popcorn;
        originalMissing = missing = PickMissing();
        followId = 0;
        deadline = 0d;
        helped = false;
        StopWork();
        interacted.Clear();
        HoldGate();

        CounterRoute route = Route;
        path.Clear();
        foreach (Transform t in route.approach) if (t != null) path.Add(t.position);
        path.Add(route.counter.position);
        Vector3 before = path.Count > 1 ? path[path.Count - 2] : route.spawn.position;
        Vector3 dir = route.counter.position - before;
        dir.y = 0f;
        queuePoint = route.counter.position - (dir.sqrMagnitude > 0.01f ? dir.normalized : route.counter.forward) * QueueGap;
        waypoint = 0;

        DisableAgent();
        transform.SetPositionAndRotation(route.spawn.position, route.spawn.rotation);
        stage = Stage.WalkingIn;
        ServerSpawnParts();
        Debug.Log($"[MissingPartsGhost] Came to the {counterKind} counter, missing: {originalMissing}.", this);
    }

    private static GhostBodyPart PickMissing()
    {
        // 4 kinds, same chance: one arm (left or right), both arms, head, upper body.
        switch (Random.Range(0, 4))
        {
            case 0: return Random.value < 0.5f ? GhostBodyPart.LeftArm : GhostBodyPart.RightArm;
            case 1: return GhostBodyPart.LeftArm | GhostBodyPart.RightArm;
            case 2: return GhostBodyPart.Head;
            default: return GhostBodyPart.UpperBody;
        }
    }

    // ---- Walking in / waiting at the counter ----------------------------------

    private void ServerTickCounter()
    {
        CounterRoute route = Route;
        if (!route.IsValid) return;
        if (stage == Stage.AtCounter)
        {
            transform.rotation = route.counter.rotation;
            return;
        }

        bool last = waypoint >= path.Count - 1;
        bool free = CounterFree();
        Vector3 target = last && !free ? queuePoint : path[Mathf.Min(waypoint, path.Count - 1)];
        if (MoveStraight(target, walkSpeed))
        {
            if (!last) { waypoint++; return; }
            if (!free) { stage = Stage.Queued; FaceFlat(route.counter.position); return; }
            stage = Stage.AtCounter;
            transform.rotation = route.counter.rotation;
            StartTimer(); // stopped at the counter = the time limit starts (if no E came first)
            return;
        }
        if (stage == Stage.Queued && free) stage = Stage.WalkingIn; // the customer left: step up
    }

    private bool CounterFree()
    {
        if (counterKind == CounterKind.Ticket)
            return ticketCounter == null || ticketCounter.State.stage == TicketCustomerStage.BetweenCustomers;
        CounterSlot slot = PopcornPreparation.Instance != null ? PopcornPreparation.Instance.GetComponent<CounterSlot>() : null;
        return slot == null || !slot.IsOccupied;
    }

    private void HoldGate()
    {
        gateHeld = true;
        if (counterKind == CounterKind.Ticket) GhostCounterGate.TicketHeldByGhost = true;
        else GhostCounterGate.PopcornHeldByGhost = true;
    }

    private void ReleaseGate()
    {
        if (!gateHeld) return;
        gateHeld = false;
        if (counterKind == CounterKind.Ticket) GhostCounterGate.TicketHeldByGhost = false;
        else GhostCounterGate.PopcornHeldByGhost = false;
    }

    // ---- Following -------------------------------------------------------------

    private void ServerTickFollow()
    {
        PlayerHealth leader = PlayerOf(followId, offlineLeader);
        if (!IsValidPlayer(leader))
        {
            followId = 0;
            offlineLeader = null;
            stage = Stage.Lost; // waits here until someone presses E again
            StopAgent();
            return;
        }
        if (workerId != 0)
        {
            StopAgent(); // stands still while a part goes back on
            return;
        }

        Vector3 goal = leader.transform.position;
        Vector3 flat = goal - transform.position;
        flat.y = 0f;
        float speed = flat.magnitude > catchUpDistance ? catchUpSpeed : followSpeed;
        if (EnsureAgent())
        {
            agent.speed = speed;
            agent.stoppingDistance = followDistance;
            agent.isStopped = false;
            if (Time.time >= nextRepath)
            {
                nextRepath = Time.time + 0.25f;
                agent.SetDestination(goal);
            }
        }
        else if (flat.magnitude > followDistance) MoveStraight(goal, speed);
    }

    // ---- Leaving ---------------------------------------------------------------

    private void ServerTickLeaving()
    {
        CounterRoute route = Route;
        Transform exit = route.exit != null ? route.exit : route.spawn;
        if (exit == null || Time.time >= leaveUntil) { ServerGone(); return; }
        if (EnsureAgent())
        {
            agent.speed = walkSpeed;
            agent.stoppingDistance = 0.3f;
            agent.isStopped = false;
            if (Time.time >= nextRepath)
            {
                nextRepath = Time.time + 0.5f;
                agent.SetDestination(exit.position);
            }
            if (!agent.pathPending && agent.remainingDistance <= 0.6f) ServerGone();
        }
        else if (MoveStraight(exit.position, walkSpeed)) ServerGone();
    }

    // ---- Putting a part back ---------------------------------------------------

    private void ServerTickWork()
    {
        if (workerId == 0) return;
        PlayerHealth worker = PlayerOf(workerId, offlineWorker);
        PlayerInventory inventory = worker != null ? worker.GetComponent<PlayerInventory>() : null;
        bool valid = IsValidPlayer(worker) && !worker.IsDowned && inventory != null && Helpable
            && (missing & workPart) != 0
            && GhostBodyParts.FromItem(inventory.HeldSlot.itemId) == workPart
            && (worker.transform.position - transform.position).sqrMagnitude <= ServerRange * ServerRange;
        if (!valid) { StopWork(); return; }
        if (Now - workStart < attachSeconds) return;

        // Done: the part leaves the hand and goes back on the body.
        GhostBodyPart part = workPart;
        StopWork();
        if (inventory.ServerRemoveAt(inventory.SelectedSlot) == null) return;
        missing &= ~part;
        // 21 Nov: the success sound only when the LAST part is back (not for each part).
        if (missing == GhostBodyPart.None)
        {
            if (NetworkMode.IsOffline) PlayEventSound(false, transform.position);
            else RpcPlayClip(false, transform.position);
        }
        Debug.Log($"[MissingPartsGhost] {GhostBodyParts.Name(part)} given back by {worker.name}. Still missing: {missing}.", this);
        if (missing == GhostBodyPart.None) ServerSucceed();
    }

    private void ServerSucceed()
    {
        ReleaseGate();
        followId = 0;
        offlineLeader = null;
        deadline = 0d;
        stage = Stage.Leaving;
        leaveUntil = Time.time + 25f;
        nextRepath = 0f;
    }

    // ---- Time up: the attack ---------------------------------------------------

    private void StartTimer()
    {
        if (deadline > 0d) return;
        MatchDirector match = MatchDirector.Instance;
        float hour = match != null ? match.SecondsPerInGameHour : 60f;
        deadline = Now + Mathf.Max(1f, timeLimitInGameHours * hour);
    }

    private void ServerAttack()
    {
        Vector3 at = transform.position;
        var hit = new List<PlayerHealth>();
        IReadOnlyList<PlayerHealth> all = PlayerRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            PlayerHealth p = all[i];
            if (!IsValidPlayer(p)) continue;
            bool near = (p.transform.position - at).sqrMagnitude <= attackRadius * attackRadius;
            bool touched = interacted.Contains(IdOf(p.gameObject));
            bool inTicketZone = ticketZoneBox != null ? ticketZoneBox.Contains(p.transform.position) : InBox(ticketZone, p.transform.position);
            if (near || touched || inTicketZone) hit.Add(p);
        }
        foreach (PlayerHealth p in hit)
        {
            p.TakeDamage(attackDamage);
            if (p.TryGetComponent(out PlayerSanity sanity) && sanityLossFraction > 0f)
                sanity.ServerDrain(sanity.Value * sanityLossFraction, "missing parts ghost", false);
        }
        if (NetworkMode.IsOffline) PlayEventSound(true, at);
        else RpcPlayClip(true, at);
        Debug.Log($"[MissingPartsGhost] Time up. Attacked {hit.Count} player(s).", this);
        ServerGone();
    }

    // ---- Gone -------------------------------------------------------------------

    private void ServerGone()
    {
        ReleaseGate();
        StopWork();
        followId = 0;
        offlineLeader = null;
        DisableAgent();
        stage = Stage.Gone;
        ServerRemoveParts();
    }

    // ---- Parts in the map ----------------------------------------------------------

    private void ServerSpawnParts()
    {
        var used = new List<Vector3>();
        foreach (GhostBodyPart part in GhostBodyParts.All)
        {
            if ((missing & part) == 0) continue;
            GameObject prefab = Resources.Load<GameObject>(GhostBodyParts.WorldPrefabPath(part));
            if (prefab == null)
            {
                Debug.LogError($"[MissingPartsGhost] Missing Resources/{GhostBodyParts.WorldPrefabPath(part)}. Run the setup tool.", this);
                continue;
            }
            Vector3 spot = PickPartSpot(used);
            used.Add(spot);
            GameObject world = Instantiate(prefab, spot + Vector3.up * 0.3f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            WorldInventoryItem pickup = world.GetComponent<WorldInventoryItem>();
            if (pickup == null) { Destroy(world); continue; }
            pickup.Initialize(InventorySlot.Of(GhostBodyParts.ItemId(part)));
            if (!NetworkMode.IsOffline) NetworkServer.Spawn(world);
            Rigidbody body = world.GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = false;
            Debug.Log($"[MissingPartsGhost] {GhostBodyParts.Name(part)} is at {spot}.", world);
        }
    }

    private Vector3 PickPartSpot(List<Vector3> used)
    {
        // Designer's points first (if any).
        var points = new List<Vector3>();
        foreach (Transform t in partSpawnPoints)
            if (t != null && !InExcludedBox(t.position)) points.Add(t.position);
        if (points.Count > 0)
        {
            for (int tries = 0; tries < 30; tries++)
            {
                Vector3 p = points[Random.Range(0, points.Count)];
                if (FarFrom(used, p, minDistanceBetweenParts)) return p;
            }
            return points[Random.Range(0, points.Count)];
        }

        // Random walkable spot on the NavMesh (area-weighted, so big rooms get more).
        NavMeshTriangulation mesh = NavMesh.CalculateTriangulation();
        if (mesh.indices == null || mesh.indices.Length < 3)
        {
            Debug.LogWarning("[MissingPartsGhost] No NavMesh found: the part appears next to the ghost's spawn.", this);
            return Route.spawn != null ? Route.spawn.position : transform.position;
        }
        int triangles = mesh.indices.Length / 3;
        var cumulative = new float[triangles];
        float total = 0f;
        for (int i = 0; i < triangles; i++)
        {
            Vector3 a = mesh.vertices[mesh.indices[i * 3]], b = mesh.vertices[mesh.indices[i * 3 + 1]], c = mesh.vertices[mesh.indices[i * 3 + 2]];
            total += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            cumulative[i] = total;
        }
        // 31 Oct fix: the NavMesh also covers the roof and the street outside. A spot must be
        // (1) INDOORS: a ceiling above it, (2) REACHABLE: a full walking path from a player,
        // (3) inside the optional Inside Areas, (4) outside the ticket / popcorn zones.
        List<Vector3> from = ReachableFrom();
        var filter = new NavMeshQueryFilter { agentTypeID = agentTypeID, areaMask = NavMesh.AllAreas };
        Vector3 best = Vector3.zero;
        float bestScore = -1f;
        for (int tries = 0; tries < 300; tries++)
        {
            float pick = Random.value * total;
            int index = System.Array.BinarySearch(cumulative, pick);
            if (index < 0) index = ~index;
            index = Mathf.Clamp(index, 0, triangles - 1);
            Vector3 a = mesh.vertices[mesh.indices[index * 3]], b = mesh.vertices[mesh.indices[index * 3 + 1]], c = mesh.vertices[mesh.indices[index * 3 + 2]];
            float r1 = Mathf.Sqrt(Random.value), r2 = Random.value;
            Vector3 p = (1f - r1) * a + r1 * (1f - r2) * b + r1 * r2 * c;
            if (InExcludedBox(p) || !InInsideAreas(p) || !Indoors(p) || !Reachable(from, p, filter)) continue;
            float score = Mathf.Min(DistanceToCounters(p) / Mathf.Max(1f, minDistanceFromCounters),
                                    NearestUsed(used, p) / Mathf.Max(1f, minDistanceBetweenParts));
            if (score >= 1f) return p;
            if (score > bestScore) { bestScore = score; best = p; }
        }
        if (bestScore >= 0f) return best;
        Debug.LogWarning("[MissingPartsGhost] No indoor + reachable spot found: the part appears where a player stands. " +
                         "Add Part Spawn Points or an Inside Area box on the ghost.", this);
        return from.Count > 0 ? from[Random.Range(0, from.Count)] : transform.position;
    }

    /// <summary>NavMesh points under the living players (the cinema inside, where people can walk).</summary>
    private List<Vector3> ReachableFrom()
    {
        var list = new List<Vector3>();
        var filter = new NavMeshQueryFilter { agentTypeID = agentTypeID, areaMask = NavMesh.AllAreas };
        foreach (PlayerHealth p in PlayerRegistry.All)
            if (IsValidPlayer(p) && NavMesh.SamplePosition(p.transform.position, out NavMeshHit hit, 2.5f, filter)) list.Add(hit.position);
        Transform counter = popcornRoute.counter != null ? popcornRoute.counter : ticketRoute.counter;
        if (list.Count == 0 && counter != null && NavMesh.SamplePosition(counter.position, out NavMeshHit c, 3f, filter)) list.Add(c.position);
        return list;
    }

    // 31 Oct fix: made on first use. Unity forbids creating a NavMeshPath in a field initializer
    // (it threw in the constructor, broke this component and then Mirror's player spawn).
    private NavMeshPath testPath;

    private bool Reachable(List<Vector3> from, Vector3 to, NavMeshQueryFilter filter)
    {
        if (from.Count == 0) return true; // nothing to test against
        if (testPath == null) testPath = new NavMeshPath();
        foreach (Vector3 start in from)
            if (NavMesh.CalculatePath(start, to, filter, testPath) && testPath.status == NavMeshPathStatus.PathComplete) return true;
        return false;
    }

    /// <summary>A ceiling above (not the roof, not the street), with room to stand under it.</summary>
    private bool Indoors(Vector3 p)
    {
        if (!Physics.Raycast(p + Vector3.up * 0.3f, Vector3.up, out RaycastHit hit, maxCeilingHeight, ~0, QueryTriggerInteraction.Ignore))
            return false;
        return hit.distance >= 1.2f;
    }

    private bool InInsideAreas(Vector3 p)
    {
        bool any = false;
        foreach (Bounds area in insideAreas)
        {
            if (area.size.sqrMagnitude < 0.01f) continue;
            any = true;
            if (InBox(area, p)) return true;
        }
        return !any;
    }

    private bool InExcludedBox(Vector3 p) =>
        ticketZoneBox != null ? ticketZoneBox.Contains(p) : InBox(ticketZone, p) || InBox(popcornZone, p);

    private static bool InBox(Bounds box, Vector3 p)
    {
        box.Expand(new Vector3(0f, 1.2f, 0f));
        return box.Contains(p);
    }

    private float DistanceToCounters(Vector3 p)
    {
        float d = float.MaxValue;
        if (ticketRoute.counter != null) d = Mathf.Min(d, Vector3.Distance(p, ticketRoute.counter.position));
        if (popcornRoute.counter != null) d = Mathf.Min(d, Vector3.Distance(p, popcornRoute.counter.position));
        return d;
    }

    private static float NearestUsed(List<Vector3> used, Vector3 p)
    {
        float d = float.MaxValue;
        foreach (Vector3 u in used) d = Mathf.Min(d, Vector3.Distance(u, p));
        return d;
    }

    private static bool FarFrom(List<Vector3> used, Vector3 p, float distance) => NearestUsed(used, p) >= distance;

    /// <summary>SERVER. The ghost is gone: its parts leave the map and every hotbar.</summary>
    private void ServerRemoveParts()
    {
        foreach (WorldInventoryItem item in FindObjectsByType<WorldInventoryItem>())
        {
            if (item == null || GhostBodyParts.FromItem(item.ItemId) == GhostBodyPart.None) continue;
            if (NetworkServer.active && item.netIdentity != null && item.netIdentity.netId != 0) NetworkServer.Destroy(item.gameObject);
            else Destroy(item.gameObject);
        }
        foreach (PlayerInventory inventory in FindObjectsByType<PlayerInventory>())
        {
            if (inventory == null) continue;
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
                if (GhostBodyParts.FromItem(inventory.GetSlot(i).itemId) != GhostBodyPart.None) inventory.ServerRemoveAt(i);
        }
    }

    // ---- Movement helpers ---------------------------------------------------------

    /// <summary>Straight line (customer style). True when the target is reached.</summary>
    private bool MoveStraight(Vector3 target, float speed)
    {
        Vector3 position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
        Vector3 flat = target - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 8f * Time.deltaTime);
        transform.position = position;
        return (position - target).sqrMagnitude <= 0.0004f;
    }

    private void FaceFlat(Vector3 point)
    {
        Vector3 flat = point - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(flat);
    }

    private bool EnsureAgent()
    {
        if (agentReady && agent != null && agent.enabled && agent.isOnNavMesh) return true;
        if (agent == null) return false;
        var filter = new NavMeshQueryFilter { agentTypeID = agentTypeID, areaMask = NavMesh.AllAreas };
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, filter)) return false;
        agent.agentTypeID = agentTypeID;
        agent.baseOffset = 1f; // the body's centre is 1 m above the floor (like the customers)
        agent.radius = 0.35f;
        agent.height = 2f;
        agent.angularSpeed = 540f;
        agent.acceleration = 16f;
        agent.autoBraking = true;
        agent.enabled = true;
        agentReady = agent.Warp(hit.position) && agent.isOnNavMesh;
        if (!agentReady) agent.enabled = false;
        return agentReady;
    }

    private void StopAgent()
    {
        if (agentReady && agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
    }

    private void DisableAgent()
    {
        agentReady = false;
        if (agent != null && agent.enabled) agent.enabled = false;
    }

    // ---- Player helpers -------------------------------------------------------------

    private static uint IdOf(GameObject player)
    {
        if (NetworkMode.IsOffline) return 1u;
        NetworkIdentity identity = player != null ? player.GetComponent<NetworkIdentity>() : null;
        return identity != null ? identity.netId : 0u;
    }

    private static PlayerHealth PlayerOf(uint id, GameObject offline)
    {
        if (id == 0) return null;
        if (NetworkMode.IsOffline) return offline != null ? offline.GetComponent<PlayerHealth>() : null;
        return NetworkServer.spawned.TryGetValue(id, out NetworkIdentity identity) && identity != null
            ? identity.GetComponent<PlayerHealth>() : null;
    }

    private static bool IsValidPlayer(PlayerHealth p) => p != null && !p.IsDead && PlayerRegistry.IsInRound(p);

    // ---- Sounds ---------------------------------------------------------------------

    [ClientRpc]
    private void RpcPlayClip(bool attack, Vector3 position) => PlayEventSound(attack, position);

    /// <summary>
    /// 19 Nov: attack (time up) / part returned. Clips and distances come from MissingPartSound.asset
    /// (MissingPartAttack / MissingPartSuccess); the old fields on this component are the fallback.
    /// Plays through AudioManager, so the SFX volume slider applies. Offline: called directly;
    /// online: the server sends RpcPlayClip and every client plays it here.
    /// </summary>
    /// <summary>21 Nov: cuts every looping / talking sound of this ghost right away (finding, voices,
    /// standby knock, hold-E work). They stay off because the ghost has nothing missing any more.</summary>
    private void StopAllGhostSounds()
    {
        for (int i = 0; i < findVoices.Length; i++)
        {
            FindVoice v = findVoices[i];
            if (v == null || v.source == null) continue;
            v.fade = 0f;
            v.volume.SetBaseVolume(0f);
            v.source.Stop();
        }
        findLevel = -1;
        soundsCutUntil = Time.time + 2f; // on clients the RPC can arrive before "missing = none" / "Leaving"
        if (voiceSource != null) voiceSource.Stop();
        if (standbySource != null) { standbyLoudness = 0f; standbySource.Stop(); }
        if (workSource != null) { workLoudness = 0f; workVolume.SetBaseVolume(0f); workSource.Stop(); }
    }

    private void PlayEventSound(bool attack, Vector3 position)
    {
        MissingPartSound s = MissingPartSound.Current;
        AudioClip clip = attack ? (s.attackClip != null ? s.attackClip : attackClip)
                                : (s.partReturnedClip != null ? s.partReturnedClip : partReturnedClip);
        if (!attack) StopAllGhostSounds(); // 21 Nov: all parts back -> ONLY the success sound, the rest cut at once
        if (clip == null || Application.isBatchMode) return;
        float volume = attack ? s.attackVolume : s.partReturnedVolume;
        float far = attack ? s.attackFarDistance : s.partReturnedFarDistance;
        Vector3 at = position + Vector3.up * 0.6f;
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayClip(clip, SoundCategory.Sfx, volume, at, true, s.eventNearDistance, far, gameObject.scene);
        else
            AudioSource.PlayClipAtPoint(clip, at, volume);
        Debug.Log("[MissingPartSound] " + (attack ? "Attack" : "Part returned") + " sound (" + clip.name + ").", this);
    }

    // ======================================================================
    // Local: look, speech bubble, hold-E bar, the part in your hand
    // ======================================================================

    private bool IsLocalWorker
    {
        get
        {
            if (workerId == 0) return false;
            PlayerHealth me = PlayerHealth.LocalInstance;
            if (me == null) return false;
            return NetworkMode.IsOffline ? workerId == 1u : workerId == me.netId;
        }
    }

    private void LocalTick()
    {
        ApplyLook(false);

        if (!IsLocalWorker)
        {
            stopSent = false;
            ChoreProgressHUD.Hide(this);
            return;
        }
        ChoreProgressHUD.Show(this, "Putting it back", (float)((Now - workStart) / attachSeconds));
        bool holding = Keybinds.IsPressed(GameAction.Interact) && !GameplayInput.Blocked;
        if (holding || stopSent) return;
        stopSent = true;
        if (NetworkMode.IsOffline) StopWork();
        else CmdStopWork();
    }

    private void LateUpdate()
    {
        UpdateBubble();
        UpdateTimer();
        UpdateHand();
        UpdateFinding();
        UpdateStandbySound();
        UpdateVoices();
        UpdateWorkSound();
    }

    // ---- Hold-E work sound (19 Nov) ----------------------------------------------------------
    //
    // While a player HOLDS E on the ghost to put a part back (workerId != 0), MIssingPartWorkonProcess
    // plays from the ghost. Letting go of E or finishing (the part is back on -> MissingPartSuccess)
    // fades it out. LOCAL on every machine: it only reads the synced workerId, so everyone nearby
    // hears it. Settings: Resources/Tuning/MissingPartSound.asset, "Putting a part back" section.

    private void UpdateWorkSound()
    {
        MissingPartSound s = MissingPartSound.Current;
        bool on = workerId != 0 && Helpable && s.workClip != null
            && Time.time >= soundsCutUntil && !NetworkMode.SessionEnding && !Application.isBatchMode;

        if (on && !workWasOn)
        {
            EnsureWorkSource();
            workSource.clip = s.workClip;
            workSource.loop = s.workLoop;
            workSource.time = 0f;
            workLoudness = 1f;
            workSource.Play();
            Debug.Log("[MissingPartSound] Work sound START (" + s.workClip.name + ").", this);
        }
        else if (!on && workWasOn)
            Debug.Log("[MissingPartSound] Work sound STOP.", this);
        workWasOn = on;
        if (workSource == null) return;

        if (!on)
            workLoudness = s.workFadeOut > 0f ? Mathf.MoveTowards(workLoudness, 0f, Time.deltaTime / s.workFadeOut) : 0f;
        workSource.minDistance = Mathf.Max(0.1f, s.workNearDistance);
        workSource.maxDistance = Mathf.Max(workSource.minDistance + 0.1f, s.workFarDistance);
        workVolume.SetBaseVolume(s.workVolume * workLoudness);
        if (!on && workLoudness <= 0f && workSource.isPlaying) workSource.Stop();
    }

    private void EnsureWorkSource()
    {
        if (workSource != null) return;
        var go = new GameObject("Missing Parts Ghost Work");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 0.6f;
        workSource = go.AddComponent<AudioSource>();
        workSource.playOnAwake = false;
        workSource.spatialBlend = 1f;   // 3D: you hear it from the ghost
        workSource.dopplerLevel = 0f;
        workSource.priority = 40;
        workSource.rolloffMode = AudioRolloffMode.Linear;
        workVolume = go.AddComponent<SoundCategoryVolume>();
        workVolume.Category = SoundCategory.Sfx;
        workVolume.SetBaseVolume(0f);
    }

    // ---- Standby sound (19 Nov) -----------------------------------------------------------
    //
    // While it stands at the counter and NOBODY has pressed E yet (helped == false):
    //   head or upper body missing (no mouth) -> the KNOCK loop (GhostFindBodySound) from its body,
    //   head attached (full body / arms missing) -> it says HelpME (UpdateVoices), no knock.
    // The first E (helped) or leaving the counter fades the knock out. LOCAL on every machine: it only
    // reads synced values (stage, helped, missing). Settings: Resources/Tuning/MissingPartSound.asset.

    private void UpdateStandbySound()
    {
        if (stage != loggedStage)
        {
            Debug.Log("[MissingPartSound] Ghost stage " + loggedStage + " -> " + stage + " (helped " + helped + ").", this);
            loggedStage = stage;
        }
        MissingPartSound s = MissingPartSound.Current;
        bool waiting = stage == Stage.AtCounter || (s.standbyWhileQueued && stage == Stage.Queued);
        bool noMouth = (missing & (GhostBodyPart.Head | GhostBodyPart.UpperBody)) != GhostBodyPart.None;
        bool on = Active && waiting && !helped && noMouth && missing != GhostBodyPart.None
            && s.standbyKnockClip != null && Time.time >= soundsCutUntil && !NetworkMode.SessionEnding && !Application.isBatchMode;

        if (on != standbyWasOn) LogStandby(on, s);
        if (on && !standbyWasOn)
        {
            EnsureStandbySource();
            standbySource.clip = s.standbyKnockClip;
            standbySource.loop = s.standbyLoop;
            standbySource.time = 0f;
            standbyLoudness = s.standbyFadeIn > 0f ? 0f : 1f;
            standbySource.Play();
        }
        standbyWasOn = on;
        if (standbySource == null) return;

        float fade = on ? s.standbyFadeIn : s.standbyFadeOut;
        standbyLoudness = fade > 0f
            ? Mathf.MoveTowards(standbyLoudness, on ? 1f : 0f, Time.deltaTime / fade)
            : (on ? 1f : 0f);

        // Live values: changes in the asset are heard right away (also in Play mode).
        standbySource.minDistance = Mathf.Max(0.1f, s.standbyNearDistance);
        standbySource.maxDistance = Mathf.Max(standbySource.minDistance + 0.1f, s.standbyFarDistance);
        standbyVolume.SetBaseVolume(s.standbyVolume * standbyLoudness);
        if (!on && standbyLoudness <= 0f && standbySource.isPlaying) standbySource.Stop();
    }

    /// <summary>19 Nov: Console line each time the standby sound starts / stops (and how far your ears are).</summary>
    private void LogStandby(bool on, MissingPartSound s)
    {
        Transform ears = FindEars();
        string dist = ears != null ? Vector3.Distance(ears.position, transform.position).ToString("0.0") + " m" : "no AudioListener found";
        if (on)
            Debug.Log("[MissingPartSound] Standby knock START (" + s.standbyKnockClip.name + "). Your ears are " + dist
                + " away; heard up to " + s.standbyFarDistance + " m.", this);
        else
            Debug.Log("[MissingPartSound] Standby knock STOP (stage " + stage + ", helped " + helped + ").", this);
    }

    // ---- Voices (19 Nov): HelpME / I am here / Over here ----------------------------------
    //
    //   Head attached (full body / arms missing): the GHOST says HelpME every helpMeInterval seconds,
    //       while it waits at the counter (instead of the knock) AND while it follows a player.
    //   Head or upper body missing: after the first E, the part that has the head says "I am here" or
    //       "Over here" (random) every partVoiceInterval seconds, from where it lies or from the player
    //       carrying it. The 4-level finding sound keeps playing under it.
    //
    // NETWORK: nothing is sent. The lines follow the shared network clock (NetworkTime): a line starts
    // at every multiple of the interval, and the random pick comes from that time slot, so every player
    // hears the same line at the same moment. Entering a state also says one line right away.

    private void UpdateVoices()
    {
        MissingPartSound s = MissingPartSound.Current;
        bool ok = Active && missing != GhostBodyPart.None && stage != Stage.Leaving
            && Time.time >= soundsCutUntil && !NetworkMode.SessionEnding && !Application.isBatchMode;
        bool noMouth = (missing & (GhostBodyPart.Head | GhostBodyPart.UpperBody)) != GhostBodyPart.None;
        bool waiting = stage == Stage.AtCounter || (s.standbyWhileQueued && stage == Stage.Queued);

        VoiceKind kind = VoiceKind.None;
        Vector3 at = transform.position + Vector3.up * 0.6f;
        if (ok && !noMouth && (helped || waiting)) kind = VoiceKind.HelpMe;
        else if (ok && noMouth && helped && findPartFound)
        {
            kind = VoiceKind.PartVoice;
            at = findPartPosition + Vector3.up * 0.2f; // the head part (UpdateFinding found it)
        }

        if (voiceSource != null && kind != VoiceKind.None) voiceSource.transform.position = at;
        if (kind == VoiceKind.None)
        {
            if (voiceKind != VoiceKind.None && !Active && voiceSource != null) voiceSource.Stop();
            voiceKind = VoiceKind.None;
            return;
        }

        float interval = Mathf.Max(1f, kind == VoiceKind.HelpMe ? s.helpMeInterval : s.partVoiceInterval);
        long slot = (long)System.Math.Floor(Now / interval);
        if (kind != voiceKind)
        {
            voiceKind = kind;
            voiceSlot = slot;
            AudioClip first = PlayVoice(kind, slot, at, s);
            // Skip the next shared slot if it comes before this first line is over.
            double nextLine = (slot + 1) * (double)interval;
            float length = first != null ? first.length : 0f;
            voiceSkipSlot = nextLine - Now < length + 0.5f ? slot + 1 : slot;
            Debug.Log("[MissingPartSound] Voice -> " + (kind == VoiceKind.HelpMe ? "HelpME" : "I am here / Over here")
                + " every " + interval + " s.", this);
            return;
        }
        if (slot == voiceSlot) return;
        voiceSlot = slot;
        if (slot > voiceSkipSlot) PlayVoice(kind, slot, at, s);
    }

    private AudioClip PlayVoice(VoiceKind kind, long slot, Vector3 at, MissingPartSound s)
    {
        AudioClip clip;
        float volume;
        if (kind == VoiceKind.HelpMe) { clip = s.helpMeClip; volume = s.helpMeVolume; }
        else
        {
            AudioClip[] lines = s.partVoiceClips;
            clip = lines != null && lines.Length > 0 ? lines[PickFromSlot(slot, lines.Length)] : null;
            volume = s.partVoiceVolume;
        }
        if (clip == null) return null;
        EnsureVoiceSource();
        voiceSource.transform.position = at;
        voiceSource.minDistance = Mathf.Max(0.1f, s.voiceNearDistance);
        voiceSource.maxDistance = Mathf.Max(voiceSource.minDistance + 0.1f, s.voiceFarDistance);
        voiceVolume.SetBaseVolume(volume);
        voiceSource.clip = clip;
        voiceSource.time = 0f;
        voiceSource.Play();
        return clip;
    }

    /// <summary>The same "random" pick on every machine: it only depends on the time slot and this ghost.</summary>
    private int PickFromSlot(long slot, int count)
    {
        ulong h = (ulong)(slot + (long)netId * 7919L) * 2654435761UL;
        return (int)((h >> 16) % (ulong)count);
    }

    private void EnsureVoiceSource()
    {
        if (voiceSource != null) return;
        var go = new GameObject("Missing Parts Ghost Voice");
        voiceSource = go.AddComponent<AudioSource>();
        voiceSource.playOnAwake = false;
        voiceSource.loop = false;
        voiceSource.spatialBlend = 1f;   // 3D: you hear WHERE it talks from
        voiceSource.dopplerLevel = 0f;
        voiceSource.priority = 40;
        voiceSource.rolloffMode = AudioRolloffMode.Linear;
        voiceVolume = go.AddComponent<SoundCategoryVolume>();
        voiceVolume.Category = SoundCategory.Sfx;
        voiceVolume.SetBaseVolume(0f);
    }

    private void EnsureStandbySource()
    {
        if (standbySource != null) return;
        var go = new GameObject("Missing Parts Ghost Standby");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 0.6f;
        standbySource = go.AddComponent<AudioSource>();
        standbySource.playOnAwake = false;
        standbySource.spatialBlend = 1f;   // 3D: you hear WHERE the ghost is
        standbySource.dopplerLevel = 0f;
        standbySource.priority = 48;
        standbySource.rolloffMode = AudioRolloffMode.Linear;
        standbyVolume = go.AddComponent<SoundCategoryVolume>();
        standbyVolume.Category = SoundCategory.Sfx;
        standbyVolume.SetBaseVolume(0f);
    }

    // ---- Finding sound (19 Nov; replaces the 15/16 Nov single crying loop) -------------------
    //
    // Starts after the first E on the ghost (helped). LOCAL on every machine: it only reads
    // values that are already synced (missing, helped, item and player positions). No network traffic.
    // Settings: Resources/Tuning/MissingPartSound.asset, "Finding" section.
    //
    // 4 levels, each with its own looping clip: Nearest / Near / Far / Far Away. Beyond Far Away = silent.
    //   Head missing  : the sound comes from the HEAD (or the upper body, which has the head) where
    //                   it lies, or from the player who carries it. Level = how close YOU are to it.
    //   Head attached : (21 Nov) the sound comes from the missing ARM lying in the map, nearest to YOU.
    //                   Level = how close YOU are to it. An arm a player already carries does not count,
    //                   so with both arms missing it moves on to the arm still lying there; once every
    //                   missing arm is picked up it goes quiet (HelpME from the ghost goes on).
    // A level change crossfades to the new clip and keeps the loop position (same-length loops).
    // Unity's own 3D falloff is OFF (flat curve): the sound is 3D for DIRECTION, the level sets loudness.

    private void UpdateFinding()
    {
        MissingPartSound s = MissingPartSound.Current;
        bool on = Active && helped && missing != GhostBodyPart.None && stage != Stage.Leaving
            && Time.time >= soundsCutUntil && !NetworkMode.SessionEnding && !Application.isBatchMode;
        int level = -1;
        float distance = 0f;
        Vector3 source = transform.position + Vector3.up * 0.6f;
        if (on)
        {
            GhostBodyPart headParts = missing & (GhostBodyPart.Head | GhostBodyPart.UpperBody);
            bool headless = headParts != GhostBodyPart.None;
            if (Time.time >= nextFindScan)
            {
                nextFindScan = Time.time + 0.25f;
                findListener = FindEars(); // again each scan: the ears change when you spectate
                findPartFound = headless
                    ? FindNearestPart(headParts, out findPartPosition)
                    : FindNearestLyingPart(missing, findListener != null ? findListener.position : transform.position, out findPartPosition);
            }
            if (findPartFound)
            {
                source = findPartPosition + Vector3.up * 0.2f; // the part itself (head or arm)
                distance = findListener != null ? Vector3.Distance(findListener.position, source) : 0f;
                level = s.FindingLevelAt(distance);
            }
        }

        if (level != findLevel)
        {
            Debug.Log("[MissingPartSound] Finding level -> " + MissingPartSound.FindingLevelName(level)
                + (level >= 0 ? " (" + distance.ToString("0.0") + " m)" : "") + ".", this);
            SwitchFindingLevel(level, s);
        }

        float step = s.findingCrossfade > 0f ? Time.deltaTime / s.findingCrossfade : 1f;
        for (int i = 0; i < findVoices.Length; i++)
        {
            FindVoice v = findVoices[i];
            if (v == null || v.source == null) continue;
            float target = i == findActive && findLevel >= 0 ? 1f : 0f;
            v.fade = Mathf.MoveTowards(v.fade, target, step);
            if (target > 0f) v.source.transform.position = source; // a fading-out voice stays where it was
            float levelVolume = v.level >= 0 ? s.FindingLevelOf(v.level).volume : 0f;
            v.volume.SetBaseVolume(s.findingVolume * levelVolume * v.fade); // live: asset changes are heard at once
            if (v.fade <= 0f && v.source.isPlaying) v.source.Stop();
        }
    }

    /// <summary>The new level's clip starts on the other voice (at the old loop position); the old voice fades out.</summary>
    private void SwitchFindingLevel(int level, MissingPartSound s)
    {
        findLevel = level;
        if (level < 0) return; // the active voice fades out in UpdateFinding
        FindVoice old = findVoices[findActive];
        findActive = 1 - findActive;
        FindVoice v = EnsureFindVoice(findActive);
        v.level = level;
        AudioClip clip = s.FindingLevelOf(level).clip;
        v.source.clip = clip;
        if (clip == null) { v.source.Stop(); return; }
        float time = old != null && old.source != null && old.source.isPlaying ? old.source.time : 0f;
        v.source.time = Mathf.Repeat(time, clip.length);
        v.source.Play();
    }

    private FindVoice EnsureFindVoice(int index)
    {
        FindVoice v = findVoices[index];
        if (v != null && v.source != null) return v;
        var go = new GameObject("Missing Parts Ghost Finding " + (index == 0 ? "A" : "B"));
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 1f;   // 3D: you hear WHERE it comes from
        source.dopplerLevel = 0f;
        source.priority = 32;       // never dropped when many sounds play
        source.reverbZoneMix = 0f;
        // Flat falloff: distance does not change the loudness here. The level does.
        source.rolloffMode = AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
        source.minDistance = 1f;
        source.maxDistance = 500f;
        var volume = go.AddComponent<SoundCategoryVolume>();
        volume.Category = SoundCategory.Sfx;
        volume.SetBaseVolume(0f);
        v = new FindVoice { source = source, volume = volume };
        findVoices[index] = v;
        return v;
    }

    /// <summary>The local player's ears: the own camera, else the scene's AudioListener (spectator / dev camera).</summary>
    private static Transform FindEars()
    {
        Camera cam = RuleFx.LocalCamera();
        if (cam != null && cam.isActiveAndEnabled) return cam.transform;
        AudioListener ears = FindAnyObjectByType<AudioListener>();
        return ears != null ? ears.transform : null;
    }

    /// <summary>21 Nov: nearest still-missing part LYING in the map (not in anyone's hotbar) to a point (your ears).</summary>
    private static bool FindNearestLyingPart(GhostBodyPart wanted, Vector3 from, out Vector3 position)
    {
        position = default;
        float best = float.MaxValue;
        foreach (WorldInventoryItem item in FindObjectsByType<WorldInventoryItem>())
        {
            if (item == null || (GhostBodyParts.FromItem(item.ItemId) & wanted) == 0) continue;
            float d = (item.transform.position - from).sqrMagnitude;
            if (d < best) { best = d; position = item.transform.position; }
        }
        return best < float.MaxValue;
    }

    /// <summary>Nearest still-missing part to the ghost's body: lying in the map, or in a player's hotbar.</summary>
    private bool FindNearestPart(GhostBodyPart wanted, out Vector3 position)
    {
        position = default;
        float best = float.MaxValue;
        Vector3 body = transform.position;
        foreach (WorldInventoryItem item in FindObjectsByType<WorldInventoryItem>())
        {
            if (item == null || (GhostBodyParts.FromItem(item.ItemId) & wanted) == 0) continue;
            float d = (item.transform.position - body).sqrMagnitude;
            if (d < best) { best = d; position = item.transform.position; }
        }
        IReadOnlyList<PlayerHealth> players = PlayerRegistry.All;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerHealth p = players[i];
            if (p == null || !p.TryGetComponent(out PlayerInventory inventory)) continue;
            for (int s = 0; s < PlayerInventory.SlotCount; s++)
            {
                if ((GhostBodyParts.FromItem(inventory.GetSlot(s).itemId) & wanted) == 0) continue;
                float d = (p.transform.position - body).sqrMagnitude;
                if (d < best) { best = d; position = p.transform.position + Vector3.up; }
                break;
            }
        }
        return best < float.MaxValue;
    }

    private void ApplyLook(bool force)
    {
        bool visible = Active;
        if (force || visible != lookVisible)
        {
            lookVisible = visible;
            foreach (Renderer r in bodyRenderers) if (r != null) r.enabled = visible;
            foreach (Collider c in bodyColliders) if (c != null) c.enabled = visible;
            lookMissing = (GhostBodyPart)255; // re-apply the parts below
        }
        if (!visible || lookMissing == missing) return;
        lookMissing = missing;
        bool noUpper = (missing & GhostBodyPart.UpperBody) != 0;
        SetActive(upperBody, !noUpper);
        SetActive(head, !noUpper && (missing & GhostBodyPart.Head) == 0);
        SetActive(leftArm, !noUpper && (missing & GhostBodyPart.LeftArm) == 0);
        SetActive(rightArm, !noUpper && (missing & GhostBodyPart.RightArm) == 0);
        SetActive(waistStump, noUpper);
        SetActive(neckStump, !noUpper && (missing & GhostBodyPart.Head) != 0);
        SetActive(leftStump, !noUpper && (missing & GhostBodyPart.LeftArm) != 0);
        SetActive(rightStump, !noUpper && (missing & GhostBodyPart.RightArm) != 0);
    }

    private static void SetActive(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on) go.SetActive(on);
    }

    private void UpdateBubble()
    {
        if (bubble == null) return;
        string line = null;
        if (Active && HasMouth && !helped && stage != Stage.Leaving && missing != GhostBodyPart.None)
        {
            bool atCounter = stage == Stage.AtCounter || stage == Stage.Queued;
            PlayerHealth me = PlayerHealth.LocalInstance;
            bool close = me != null && (me.transform.position - transform.position).sqrMagnitude <= 5f * 5f;
            if (atCounter || close) line = LineFor(missing);
        }
        bool show = !string.IsNullOrEmpty(line);
        if (bubble.enabled != show) bubble.enabled = show;
        if (!show) return;
        if (bubbleText.text != line) bubbleText.text = line;
        Camera cam = ViewCamera();
        if (cam != null) bubble.transform.rotation = cam.transform.rotation;
    }

    /// <summary>31 Oct: time left above its head (real minutes:seconds). Red in the last 30 s.</summary>
    private void UpdateTimer()
    {
        if (timerCanvas == null) return;
        bool show = Active && stage != Stage.Leaving && deadline > 0d && missing != GhostBodyPart.None;
        if (timerCanvas.enabled != show) timerCanvas.enabled = show;
        if (!show) return;
        bool upper = (missing & GhostBodyPart.UpperBody) == 0;
        timerCanvas.transform.localPosition = upper ? bubbleOffset + Vector3.up * 0.38f : new Vector3(0f, 0.45f, 0f);
        int seconds = Mathf.CeilToInt(SecondsLeft);
        if (seconds != shownSeconds)
        {
            shownSeconds = seconds;
            timerText.text = $"{seconds / 60}:{seconds % 60:00}";
            timerText.color = seconds <= 30 ? new Color(1f, 0.25f, 0.2f) : seconds <= 60 ? new Color(1f, 0.75f, 0.2f) : new Color(0.9f, 0.95f, 1f);
        }
        Camera cam = ViewCamera();
        if (cam != null) timerCanvas.transform.rotation = cam.transform.rotation;
    }

    private void BuildTimer()
    {
        var go = new GameObject("Ghost Countdown", typeof(RectTransform), typeof(Canvas));
        go.transform.SetParent(transform, false);
        go.transform.localPosition = bubbleOffset + Vector3.up * 0.38f;
        go.transform.localScale = Vector3.one * 0.002f;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(220f, 80f);
        timerCanvas = go.GetComponent<Canvas>();
        timerCanvas.renderMode = RenderMode.WorldSpace;
        GameObject panel = UiFactory.CreatePanel("Back", go.transform, new Color(0f, 0f, 0f, 0.75f));
        UiFactory.Stretch(panel.GetComponent<RectTransform>());
        timerText = UiFactory.CreateText("Time", panel.transform, "", 52f, Color.white);
        timerText.alignment = TextAlignmentOptions.Center;
        timerText.fontStyle = FontStyles.Bold;
        UiFactory.Stretch(timerText.rectTransform);
        timerCanvas.enabled = false;
    }

    private string LineFor(GhostBodyPart now)
    {
        bool left = (now & GhostBodyPart.LeftArm) != 0, right = (now & GhostBodyPart.RightArm) != 0;
        if (left && right) return bothArmsLine;
        if (left) return leftArmLine;
        if (right) return rightArmLine;
        return null;
    }

    private static Camera ViewCamera()
    {
        PlayerHealth me = PlayerHealth.LocalInstance;
        Camera cam = me != null ? me.GetComponentInChildren<Camera>() : null;
        return cam != null && cam.isActiveAndEnabled ? cam : Camera.main;
    }

    /// <summary>Your own view: the part you hold, in front of your camera.</summary>
    private void UpdateHand()
    {
        PlayerInventory inventory = PlayerInventory.Local;
        GhostBodyPart part = inventory != null && !inventory.HeldSlot.IsEmpty
            ? GhostBodyParts.FromItem(inventory.HeldSlot.itemId) : GhostBodyPart.None;
        PlayerHealth me = PlayerHealth.LocalInstance;
        if (me != null && (me.IsDead || me.IsDowned)) part = GhostBodyPart.None;
        if (part != handPart) { ClearHand(); handPart = part; }
        if (part == GhostBodyPart.None) return;
        Camera cam = me != null ? me.GetComponentInChildren<Camera>() : null;
        if (cam == null) return;
        if (handVisual == null)
        {
            GameObject world = Resources.Load<GameObject>(GhostBodyParts.WorldPrefabPath(part));
            if (world == null || world.transform.childCount == 0) return;
            handVisual = MakeHandModel(world.transform.GetChild(0).gameObject, cam.transform);
        }
    }

    private static GameObject MakeHandModel(GameObject prefab, Transform cam)
    {
        var anchor = new GameObject("Held Ghost Part (local)");
        anchor.transform.SetParent(cam, false);
        anchor.transform.localPosition = new Vector3(0.24f, -0.24f, 0.5f);
        anchor.transform.localRotation = Quaternion.Euler(10f, -20f, 0f);
        GameObject model = Instantiate(prefab, anchor.transform);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (Rigidbody b in model.GetComponentsInChildren<Rigidbody>(true)) { b.isKinematic = true; b.detectCollisions = false; }
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2; // never blocks the E ray
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (longest > 0.001f) model.transform.localScale *= 0.3f / longest;
            b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            model.transform.position += anchor.transform.position - b.center;
        }
        return anchor;
    }

    private void ClearHand()
    {
        if (handVisual != null) Destroy(handVisual);
        handVisual = null;
        handPart = GhostBodyPart.None;
    }

    // ---- Building the body (local, every machine) ------------------------------------

    private void BuildBody()
    {
        if (customModel != null)
        {
            head = customHead;
            leftArm = customLeftArm;
            rightArm = customRightArm;
            upperBody = customUpperBody;
        }
        else
        {
            // Placeholder: the root is the body's centre, about 1 m above the floor (like the customers).
            placeholderRoot = new GameObject("Ghost Body (placeholder)");
            placeholderRoot.transform.SetParent(transform, false);
            Transform root = placeholderRoot.transform;
            Color pale = new Color(0.82f, 0.86f, 0.9f, 0.92f);
            Color blood = new Color(0.45f, 0.02f, 0.02f, 1f);
            Shape("Lower Body", PrimitiveType.Capsule, root, new Vector3(0f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0.4f), pale);
            upperBody = new GameObject("Upper Body");
            upperBody.transform.SetParent(root, false);
            Transform upper = upperBody.transform;
            Shape("Torso", PrimitiveType.Cube, upper, new Vector3(0f, 0.3f, 0f), new Vector3(0.5f, 0.6f, 0.3f), pale);
            head = Shape("Head", PrimitiveType.Sphere, upper, new Vector3(0f, 0.8f, 0f), new Vector3(0.32f, 0.36f, 0.32f), pale);
            leftArm = Shape("Left Arm", PrimitiveType.Capsule, upper, new Vector3(-0.34f, 0.28f, 0f), new Vector3(0.13f, 0.3f, 0.13f), pale);
            rightArm = Shape("Right Arm", PrimitiveType.Capsule, upper, new Vector3(0.34f, 0.28f, 0f), new Vector3(0.13f, 0.3f, 0.13f), pale);
            // Red stumps where a part is missing (easy to notice).
            neckStump = Shape("Neck Stump", PrimitiveType.Cylinder, upper, new Vector3(0f, 0.62f, 0f), new Vector3(0.16f, 0.02f, 0.16f), blood);
            leftStump = Shape("Left Shoulder Stump", PrimitiveType.Cylinder, upper, new Vector3(-0.26f, 0.5f, 0f), new Vector3(0.14f, 0.02f, 0.14f), blood);
            leftStump.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            rightStump = Shape("Right Shoulder Stump", PrimitiveType.Cylinder, upper, new Vector3(0.26f, 0.5f, 0f), new Vector3(0.14f, 0.02f, 0.14f), blood);
            rightStump.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            waistStump = Shape("Waist Stump", PrimitiveType.Cylinder, root, new Vector3(0f, 0.02f, 0f), new Vector3(0.42f, 0.02f, 0.34f), blood);
        }

        // One collider on the root for the E ray (and so it physically stands in the way).
        if (GetComponent<Collider>() == null)
        {
            CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.center = Vector3.zero;
            capsule.radius = 0.35f;
            capsule.height = 2f;
        }
        bodyRenderers = GetComponentsInChildren<Renderer>(true);
        var colliders = new List<Collider>(GetComponents<Collider>());
        bodyColliders = colliders.ToArray();
    }

    private static GameObject Shape(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        Collider c = go.GetComponent<Collider>();
        if (c != null) Destroy(c); // only the root capsule is hit
        Tint(go.GetComponent<Renderer>(), color);
        return go;
    }

    private static Material bodyMaterial;

    private static void Tint(Renderer renderer, Color color)
    {
        if (renderer == null) return;
        if (bodyMaterial == null) bodyMaterial = Resources.Load<Material>("CustomerBody");
        if (bodyMaterial != null) renderer.sharedMaterial = bodyMaterial;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block);
    }

    private void BuildBubble()
    {
        var go = new GameObject("Ghost Speech", typeof(RectTransform), typeof(Canvas));
        go.transform.SetParent(transform, false);
        go.transform.localPosition = bubbleOffset;
        go.transform.localScale = Vector3.one * 0.002f;
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(560f, 150f);
        bubble = go.GetComponent<Canvas>();
        bubble.renderMode = RenderMode.WorldSpace;
        GameObject panel = UiFactory.CreatePanel("Bubble", go.transform, new Color(0.03f, 0.03f, 0.04f, 0.92f));
        UiFactory.Stretch(panel.GetComponent<RectTransform>());
        bubbleText = UiFactory.CreateText("Text", panel.transform, "", 30f, new Color(0.85f, 0.9f, 1f));
        bubbleText.alignment = TextAlignmentOptions.Center;
        bubbleText.textWrappingMode = TextWrappingModes.Normal;
        UiFactory.SetRect(bubbleText.rectTransform, new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.94f));
        bubble.enabled = false;
    }

    // ======================================================================
    // Dev cheats (F1). Server only.
    // ======================================================================

    public string DevGhostName => "Missing parts";
    public string DevTriggerText => "comes once, random time 00:00-03:00";

    public void ServerDevSummonNow(GameObject requester)
    {
        if (!HasAuthority) return;
        if (Active) ServerGone();
        ServerSpawn();
    }

    public void ServerDevFireTrigger(GameObject requester) => ServerDevSummonNow(requester);

    // ======================================================================
    // Scene view
    // ======================================================================

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.9f);
        Gizmos.DrawWireCube(ticketZone.center, ticketZone.size);
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
        Gizmos.DrawWireCube(popcornZone.center, popcornZone.size);
        Gizmos.color = new Color(0.7f, 0.8f, 1f, 0.9f);
        foreach (Transform t in partSpawnPoints)
            if (t != null) Gizmos.DrawWireSphere(t.position, 0.3f);
    }
}
