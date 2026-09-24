using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// The authority for one round. Put ONE of these in the GamePlay scene, on an
/// empty GameObject with a NetworkIdentity.
///
/// THE ROUND
/// ---------
/// A round is two phases, and the exits behave in opposite ways in each.
///
///   NIGHT   00:00 -> 06:00   (Easy 10 / Normal 15 / Hard 20 real minutes)
///           Exits are LOCKED. Nothing the team does opens them. The
///           minimum is a deadline to beat, not a key — Method 1 is "finish the
///           minimum BEFORE the gate opens", so completing it early buys the
///           team nothing except safety from failing.
///
///   ESCAPE  06:00 -> 07:00   (one in-game hour: 100 / 150 / 200 real seconds)
///           The doors exist now, but only a team that MET the minimum may use
///           them. Three exits, two people each, six players max — a full team
///           has exactly zero slack and physically cannot leave together.
///
///           A team that fell short is sealed in. Their only remaining way out
///           of the round alive is Method 2: find and fully execute this level's
///           hidden method for dealing with the main ghost. Fail that as well
///           and everyone still inside is dead at 07:00.
///
/// WHY THE ESCAPE WINDOW IS ONE IN-GAME HOUR
/// -----------------------------------------
/// It is the same length as the downed timer, deliberately. If a teammate goes
/// down during the escape, the time to revive them and the time to get yourself
/// out are identical, so saving them costs exactly the margin you need to save
/// yourself. That trade only exists because the door is contested; an exit with
/// a capacity of two is meaningless unless time is scarce.
///
/// AUTHORITY: the server resolves, the clients display. A client never reads a
/// DifficultyProfile, never decides whether the exits are open, and never
/// decides that it escaped.
///
/// OFFLINE: this also runs with no server at all, so a designer can press Play
/// directly in GamePlay and get a working round. Every network call below is
/// gated on NetworkServer.active rather than on Mirror's [Server] attribute —
/// the attribute logs a warning every time it is reached with no server running,
/// which would flood the console during exactly the sandbox testing this project
/// built NetworkMode to support.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public class MatchDirector : NetworkBehaviour
{
    /// <summary>
    /// Single point of access for the quest, rule, exit and HUD systems. Cleared
    /// in OnDestroy so a scene reload cannot leave a dead reference behind.
    /// </summary>
    public static MatchDirector Instance { get; private set; }

    [Header("Difficulty")]
    [Tooltip("One profile per tier. A List, not a Dictionary — Unity cannot serialize a Dictionary into the Inspector, and this project keeps serialized collections as Lists throughout.")]
    [SerializeField] private List<DifficultyProfile> difficultyProfiles = new List<DifficultyProfile>();

    [Tooltip("Used if the room's difficulty has no matching profile in the list. Leave empty and the round still runs on safe built-in defaults, but you will get a warning.")]
    [SerializeField] private DifficultyProfile fallbackProfile;

    [Header("Ghosts")]
    [Tooltip("One roster per map. The roster holds the map's OWN guaranteed ghosts (always present, every difficulty) and the random pool the difficulty draws from. Ghost identity belongs to the map; only the size of the random draw belongs to the difficulty.")]
    [SerializeField] private List<MapGhostRoster> ghostRosters = new List<MapGhostRoster>();

    [Header("Debug")]
    [SerializeField] private bool logRoundSetup = true;

    // ---- Fixed rules of 13RoH ---------------------------------------------
    // These are const rather than serialized on purpose. A field in the
    // Inspector is a field someone can typo into a five-hour night, and nothing
    // would report the mistake until the balance felt wrong weeks later.

    /// <summary>The night always runs midnight to 6 AM. Six in-game hours, every map, every difficulty.</summary>
    private const int StartHour = 0;
    private const int EndHour = 6;

    /// <summary>
    /// How long the exits stay open after 06:00, in IN-GAME hours. One hour —
    /// so it is exactly as long as a downed player survives, at every tier.
    /// </summary>
    private const float EscapeWindowInGameHours = 1f;

    /// <summary>
    /// How long a downed player survives, in IN-GAME hours. One hour on every
    /// difficulty. Difficulty decides how easily you go down, never how long you
    /// last once you have.
    /// </summary>
    private const float DownedDurationInGameHours = 1f;

    /// <summary>
    /// Dead players needed at 06:00 for the night to refuse to end (Overtime)
    /// instead of sealing everyone in (LockedIn).
    ///
    /// Yes, a struggling team benefits from a second death — that is the
    /// intended decision, not an exploit, and the DOWNED TIMER is what keeps it
    /// honest. A player must be downed for a full in-game hour before they
    /// actually die, so to have a second death by 06:00 the team has to give
    /// someone up at 05:00, with a sixth of the night still to play, and then
    /// deliberately not revive them.
    ///
    /// That means the choice has to be made BEFORE the team knows for certain
    /// it will fail: split up and gamble that everyone finishes, or spend one
    /// life so the rest get unlimited time. Decide too late and the option is
    /// simply gone. It also costs that player's money, their items, and their
    /// hands for the last stretch.
    ///
    /// THE CONSTRAINT THIS RELIES ON: instant death must stay rare and must not
    /// be available near the deadline. Instant death comes only from rules, so
    /// any rule that kills outright must be restricted to early in the night
    /// (design's current line is before 04:00). An instant-death rule that can
    /// fire at 05:50 turns a costly gamble into a button, and the decision above
    /// collapses. Do not add one without talking to design first.
    /// </summary>
    private const int OvertimeDeathThreshold = 2;

    // ---- Replicated round state -------------------------------------------
    // Everything below is written by the SERVER only (or locally when offline).
    // Clients read these to draw the HUD and nothing else.

    [SyncVar] private string syncedMapID = RoomConfig.DemoMapID;
    [SyncVar] private DifficultyLevel syncedDifficulty = DifficultyLevel.Normal;

    [SyncVar(hook = nameof(OnZonesChanged))] private int zonesCompleted;
    [SyncVar(hook = nameof(OnZonesChanged))] private int zonesRequired;

    /// <summary>
    /// Seconds elapsed since the main clock froze at 06:00. Zero outside
    /// Overtime. This is the second clock: it only ever counts UP, it has no
    /// target, and the ghosts read it to decide how hard to hunt.
    /// </summary>
    [SyncVar] private float overtimeSeconds;

    /// <summary>How many of the map's thirteen place rules are in force tonight. Resolved from the profile.</summary>
    [SyncVar] private int placeRuleCount;

    [SyncVar(hook = nameof(OnPhaseChanged))] private MatchPhase phase = MatchPhase.Night;

    /// <summary>Real seconds left in the CURRENT phase. Synced on an interval, not every frame — a countdown does not need 60 updates a second.</summary>
    [SyncVar] private float secondsRemaining;

    /// <summary>Total real seconds the current phase was given, so clients can draw progress without knowing the difficulty maths.</summary>
    [SyncVar] private float phaseLengthSeconds;

    /// <summary>Real seconds in the night specifically. Kept separate from phaseLengthSeconds so the in-game clock stays correct after 06:00.</summary>
    [SyncVar] private float nightLengthSeconds;

    /// <summary>Resolved on the server from the profile, replicated so sanity sources on any machine agree on the rate.</summary>
    [SyncVar] private float sanityDrainMultiplier = 1f;

    /// <summary>Resolved downed-survival window in real seconds. PlayerHealth should read this instead of its own hardcoded 30f.</summary>
    [SyncVar] private float downedDurationSeconds = 150f;

    /// <summary>
    /// How many ghosts are in the building tonight, guaranteed plus random.
    /// Replicated for the HUD and for logging only — WHICH ghosts they are is
    /// never sent, because not knowing is the game.
    /// </summary>
    [SyncVar] private int totalGhostCount;

    /// <summary>How many players have made it out of an exit. For the HUD ("3/6 out").</summary>
    [SyncVar] private int escapedCount;

    /// <summary>
    /// Whether the map's main ghost has been dealt with. Once true, everyone
    /// still alive inside has won and there is nothing left to run from.
    /// </summary>
    [SyncVar(hook = nameof(OnGhostBanishedChanged))] private bool ghostBanished;

    // Server-side only. The spawner reads these; they are deliberately not
    // SyncVars, because a client holding the resolved roster would know exactly
    // what is in the building before it ever sees a ghost.
    private DifficultyProfile activeProfile;
    private MapGhostRoster activeGhostRoster;
    private int ghostSeed;

    /// <summary>netIds of players who reached an exit. Server-side, so a client cannot claim it got out.</summary>
    private readonly HashSet<uint> escapedPlayers = new HashSet<uint>();

    /// <summary>
    /// Zone IDs already counted, so a zone cannot be reported complete twice and
    /// walk the team through a gate it never earned.
    /// </summary>
    private readonly HashSet<string> completedZones = new HashSet<string>();

    /// <summary>
    /// Every quest zone that exists in this map, registered by the zone itself
    /// on spawn. The team owes ALL of them on every difficulty, so the total
    /// comes from the map rather than from a number on the difficulty asset —
    /// a map with eight quest zones needs no code change.
    /// </summary>
    private readonly HashSet<string> registeredZones = new HashSet<string>();

    /// <summary>netIds of players confirmed inside the real secret room. Only these survive a LockedIn ending.</summary>
    private readonly HashSet<uint> playersInSecretRoom = new HashSet<uint>();

    /// <summary>
    /// Tasks each player finished, for the per-player payout. The GDD pays a
    /// player for THEIR OWN completions, so this is deliberately separate from
    /// the zone counter — zones decide survival, these decide money, and one
    /// shared number could not express both.
    /// </summary>
    private readonly Dictionary<uint, int> tasksPerPlayer = new Dictionary<uint, int>();

    /// <summary>
    /// Extra tasks this round sprinkled across the map for the team size. The
    /// quest system reads it when building the zones; MatchDirector only resolves
    /// the number.
    /// </summary>
    private int extraTasksThisRound;
    public int ExtraTasksThisRound => extraTasksThisRound;

    /// <summary>How each player finished. Server-side; the payout screen reads it.</summary>
    private readonly Dictionary<uint, PlayerOutcome> outcomes = new Dictionary<uint, PlayerOutcome>();

    /// <summary>
    /// Every player's health component, rebuilt as players spawn. The round ends
    /// the moment this holds nobody who is both alive and still inside.
    /// </summary>
    private readonly List<PlayerHealth> trackedPlayers = new List<PlayerHealth>();

    /// <summary>
    /// Guards against ending the round in the first frames, when the roster is
    /// legitimately empty because Mirror has not spawned anybody yet. Without it,
    /// "nobody alive inside" is true before the match has begun.
    /// </summary>
    private bool hasSeenAnyPlayer;

    private float rosterTickAccumulator;
    private const float RosterTickInterval = 0.5f;

    // ---- Public read-only surface -----------------------------------------

    public string MapID => syncedMapID;
    public DifficultyLevel Difficulty => syncedDifficulty;
    public int ZonesCompleted => zonesCompleted;
    public int ZonesRequired => zonesRequired;
    public float OvertimeSeconds => overtimeSeconds;
    public int PlaceRuleCount => placeRuleCount;

    /// <summary>
    /// How hard the ghosts should be hunting, 0 at 06:00 and rising without
    /// limit while Overtime runs. Ghost AI multiplies its aggression by this.
    ///
    /// Deliberately unbounded here rather than clamped: whether escalation
    /// plateaus into "survivable if careful" or keeps climbing into a certain
    /// death is a design decision, and it belongs in the ghost AI where it can
    /// be tuned per ghost, not hidden in a clamp in the match clock.
    /// </summary>
    public float EscalationLevel => overtimeSeconds / Mathf.Max(1f, SecondsPerInGameHour);
    public float SecondsRemaining => secondsRemaining;
    public float PhaseLengthSeconds => phaseLengthSeconds;
    public float SanityDrainMultiplier => sanityDrainMultiplier;
    public float DownedDurationSeconds => downedDurationSeconds;
    public int TotalGhostCount => totalGhostCount;
    public int EscapedCount => escapedCount;

    public MatchPhase Phase => phase;
    public bool RoundRunning => phase != MatchPhase.Ended;
    public bool GhostBanished => ghostBanished;

    /// <summary>How a player finished. Unresolved until the round resolves them.</summary>
    public PlayerOutcome OutcomeFor(NetworkIdentity player)
    {
        if (player == null) return PlayerOutcome.Unresolved;
        return outcomes.TryGetValue(player.netId, out PlayerOutcome o) ? o : PlayerOutcome.Unresolved;
    }

    /// <summary>
    /// Whether the clock has reached 06:00. Nothing else moves this — not
    /// finishing every task on the map, not a lever, not the host.
    ///
    /// This is about the CLOCK only. It does not mean anybody may walk through:
    /// see <see cref="GatesPassable"/>.
    /// </summary>
    public bool ExitsOpen => phase == MatchPhase.Escape;

    /// <summary>
    /// Whether the TEAM's combined completed tasks have reached the minimum.
    /// One shared total across all players, not a per-player count.
    /// </summary>
    public bool MinimumMet => zonesRequired > 0 && zonesCompleted >= zonesRequired;

    /// <summary>
    /// Whether anybody may actually cross a gate. BOTH conditions, and they are
    /// different kinds of thing:
    ///
    ///   the clock reached 06:00   — when the way out exists at all
    ///   the minimum was met       — whether this team earned the right to use it
    ///
    /// A team that is short does not get to walk away at dawn. They are sealed
    /// in, and the only thing left is Method 2 — find and fully execute this
    /// level's hidden way of dealing with the main ghost before 07:00. Fail that
    /// too and everyone inside dies.
    ///
    /// This is what makes the task minimum a real threat instead of a score.
    /// Missing it is not "no bonus this run"; it is the difference between going
    /// home and not going home.
    /// </summary>
    public bool GatesPassable => ExitsOpen && MinimumMet;

    /// <summary>
    /// Whether the team is allowed to deal with the main ghost — Method 2.
    /// Requires the SAME minimum the gates do.
    ///
    /// WHY. Read this before "simplifying" it.
    ///
    /// Without the minimum, Method 2 is strictly better than playing the game.
    /// A team buys the items that make the ritual easy, does barely any tasks,
    /// sets up early, hides somewhere safe until 06:00, executes, and wins the
    /// round. Tasks stop being the objective and the main objective quietly
    /// becomes "deal with the ghost" — which is not the game 13RoH is. The
    /// cinema, the zones, the rules and the quests would all be optional
    /// scenery.
    ///
    /// Gating it on the minimum means the tasks get done either way. Only after
    /// the work is finished does the real decision appear: walk out with what
    /// you earned, or stay in the dark for more.
    ///
    /// CONSEQUENCE, stated plainly: a team that misses the minimum has NO way to
    /// survive. Both exits from the round are closed to them, and 07:00 kills
    /// everyone still inside. That is intended — it is what makes the minimum
    /// the thing players are actually afraid of.
    /// </summary>
    /// <summary>
    /// NOT IN THE PRE-THESIS DEMO. Method 2 — dealing with the map's main ghost
    /// — is deferred to the full thesis build; the demo's failure path is the
    /// Ghost Key and the secret room instead.
    ///
    /// The code below is kept rather than deleted because the design is decided
    /// and only the scope was cut. Nothing in the demo should call
    /// ServerReportGhostBanished, and if something does it will log an error.
    /// </summary>
    public bool RitualPermitted => MinimumMet;

    /// <summary>0 at the start of the current phase, 1 when its timer runs out.</summary>
    public float PhaseProgress01 =>
        phaseLengthSeconds <= 0f ? 0f : Mathf.Clamp01(1f - (secondsRemaining / phaseLengthSeconds));

    /// <summary>How many in-game hours the night lasts. Always six.</summary>
    public int NightLengthInGameHours => EndHour - StartHour;

    /// <summary>
    /// Real seconds one in-game hour takes tonight. DERIVED, never authored — the
    /// designer sets the round length in minutes, the night is always six hours,
    /// and this falls out of the two. Authoring it directly would let "150s per
    /// hour" and "a 15-minute round" drift apart; derived, they cannot.
    /// </summary>
    public float SecondsPerInGameHour =>
        nightLengthSeconds / Mathf.Max(1, NightLengthInGameHours);

    /// <summary>
    /// The in-game clock as a 24h hour and minute, for a diegetic HUD ("03:47").
    /// Runs 00:00 -> 06:00 through the night and 06:00 -> 07:00 through the
    /// escape, so the displayed time never jumps or repeats.
    /// </summary>
    public void GetInGameTime(out int hour, out int minute)
    {
        float absolute;

        switch (phase)
        {
            case MatchPhase.Night:
                absolute = StartHour + PhaseProgress01 * NightLengthInGameHours;
                break;

            case MatchPhase.Escape:
            case MatchPhase.LockedIn:
                absolute = EndHour + PhaseProgress01 * EscapeWindowInGameHours;
                break;

            case MatchPhase.Overtime:
                // Frozen at 06:00, on purpose. The HUD keeps reading 06:00 while
                // the night refuses to move, and that stuck clock IS the
                // message — do not add the overtime seconds here, and do not put
                // the second clock on screen either. The players should feel the
                // ghosts getting worse, not watch a number tell them so.
                absolute = EndHour;
                break;

            default:
                absolute = EndHour + EscapeWindowInGameHours;
                break;
        }

        hour = Mathf.FloorToInt(absolute) % 24;
        minute = Mathf.FloorToInt((absolute - Mathf.Floor(absolute)) * 60f);
    }

    /// <summary>
    /// Tonight's ghost list, guaranteed first then the random draw. Returns an
    /// empty list on a client — only the server may know the roster.
    /// </summary>
    public List<GameObject> BuildGhostRoster()
    {
        if (!NetworkMode.HasServerAuthority(this) || activeGhostRoster == null || activeProfile == null)
            return new List<GameObject>();

        return activeGhostRoster.BuildRosterFor(activeProfile.randomGhostCount, ghostSeed);
    }

    // ---- Events for UI (fire on every machine) -----------------------------

    public event System.Action<int, int> ZoneProgressChanged;   // completed, required
    public event System.Action MinimumReached;                  // the clear condition became true
    public event System.Action<MatchPhase> PhaseChanged;
    public event System.Action ExitsOpened;                     // 06:00 — the doors
    public event System.Action GhostBanishedChanged;            // the ritual landed
    public event System.Action RoundEnded;

    // ---- Lifecycle ---------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[MatchDirector] A second MatchDirector is in this scene. There must be exactly one — destroying the duplicate.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        // Offline sandbox: nobody is hosting, so OnStartServer never fires, but a
        // designer pressing Play in GamePlay still expects a working round.
        if (NetworkMode.IsOffline) ConfigureRound(MatchState.ResolveOrDefault(), 1);
    }

    public override void OnStartServer()
    {
        RoomConfig config = MatchState.ResolveOrDefault();

        // Headcount at the moment the scene loads. Mirror has already brought the
        // connections across the scene change, so this is the real team size, not
        // the room's advertised limit.
        int playerCount = Mathf.Max(1, NetworkServer.connections.Count);

        ConfigureRound(config, playerCount);
    }

    /// <summary>
    /// Turns "Hard, four players" into the actual numbers this round runs on.
    /// Server-side or offline only — never call this from a remote client.
    /// </summary>
    private void ConfigureRound(RoomConfig config, int playerCount)
    {
        DifficultyProfile profile = ResolveProfile(config.difficulty);

        syncedMapID = config.mapID;
        syncedDifficulty = config.difficulty;

        activeProfile = profile;
        activeGhostRoster = ResolveGhostRoster(config.mapID);

        // One seed for the whole round, drawn once here. Every system that needs
        // randomness this night should derive from it rather than calling
        // Random.Range itself, so a reported bug can be reproduced exactly.
        ghostSeed = Random.Range(int.MinValue, int.MaxValue);

        if (profile != null)
        {
            extraTasksThisRound = profile.ExtraTasksFor(playerCount);
            placeRuleCount = profile.placeRuleCount;
            nightLengthSeconds = profile.RoundLengthSeconds;
            sanityDrainMultiplier = profile.sanityDrainMultiplier;
        }
        else
        {
            // No profile assigned. Keep the round playable rather than dividing by
            // zero, but make it obvious in the console that balancing is missing.
            extraTasksThisRound = 0;
            placeRuleCount = 0;
            nightLengthSeconds = 15f * 60f;
            sanityDrainMultiplier = 1f;

            Debug.LogWarning(
                $"[MatchDirector] No DifficultyProfile found for '{config.difficulty}' and no fallback assigned. " +
                "Running on built-in defaults — assign the three profiles in the Inspector.", this);
        }

        // Both derived from the clock, so they are always equal and always one
        // in-game hour. Must be set AFTER nightLengthSeconds, since
        // SecondsPerInGameHour reads it.
        downedDurationSeconds = DownedDurationInGameHours * SecondsPerInGameHour;

        // Guaranteed map ghosts are added on top of the difficulty's random draw,
        // never counted against it: the cinema keeps its own haunting on every
        // tier, and difficulty only decides how many unknowns join them.
        totalGhostCount = activeGhostRoster != null && profile != null
            ? activeGhostRoster.TotalGhostsFor(profile.randomGhostCount)
            : 0;

        zonesCompleted = 0;
        zonesRequired = registeredZones.Count;
        overtimeSeconds = 0f;
        completedZones.Clear();
        playersInSecretRoom.Clear();
        tasksPerPlayer.Clear();
        escapedCount = 0;
        ghostBanished = false;
        escapedPlayers.Clear();
        outcomes.Clear();
        trackedPlayers.Clear();
        hasSeenAnyPlayer = false;
        rosterTickAccumulator = 0f;

        EnterPhase(MatchPhase.Night, nightLengthSeconds);

        // Mirror does not call a SyncVar hook on the machine that made the change,
        // so the host/server has to raise its own UI event directly or the host's
        // HUD would sit on stale values while every remote client updated fine.
        ZoneProgressChanged?.Invoke(zonesCompleted, zonesRequired);

        if (logRoundSetup)
        {
            int guaranteed = activeGhostRoster != null ? activeGhostRoster.guaranteedGhosts.Count : 0;
            int drawn = Mathf.Max(0, totalGhostCount - guaranteed);

            Debug.Log(
                $"[MatchDirector] Round configured — map={syncedMapID}, difficulty={syncedDifficulty}, " +
                $"players={playerCount}, zones={zonesRequired} (all required), extraTasks=+{extraTasksThisRound}, " +
                $"placeRules={placeRuleCount}, " +
                $"night={nightLengthSeconds / 60f:F1} min ({StartHour:00}:00 → {EndHour:00}:00, " +
                $"{SecondsPerInGameHour:F0}s per in-game hour), " +
                $"escapeWindow={EscapeWindowInGameHours * SecondsPerInGameHour:F0}s, " +
                $"ghosts={totalGhostCount} ({guaranteed} guaranteed + {drawn} random), " +
                $"sanityDrain=x{sanityDrainMultiplier:F2}, downed={downedDurationSeconds:F0}s", this);
        }
    }

    /// <summary>
    /// Finds the profile for a tier. A List scan rather than a Dictionary lookup —
    /// there are three entries, so the cost is irrelevant and the list stays
    /// visible and editable in the Inspector.
    /// </summary>
    private DifficultyProfile ResolveProfile(DifficultyLevel level)
    {
        for (int i = 0; i < difficultyProfiles.Count; i++)
        {
            DifficultyProfile candidate = difficultyProfiles[i];
            if (candidate != null && candidate.level == level) return candidate;
        }

        return fallbackProfile;
    }

    /// <summary>
    /// The roster for a map. Same List-scan reasoning as ResolveProfile: a handful
    /// of entries, and the list stays readable in the Inspector.
    /// </summary>
    private MapGhostRoster ResolveGhostRoster(string mapID)
    {
        for (int i = 0; i < ghostRosters.Count; i++)
        {
            MapGhostRoster candidate = ghostRosters[i];
            if (candidate != null && candidate.mapID == mapID) return candidate;
        }

        if (ghostRosters.Count > 0)
        {
            Debug.LogWarning(
                $"[MatchDirector] No MapGhostRoster matches mapID '{mapID}'. No ghosts will spawn this round.", this);
        }

        return null;
    }

    // ---- Phase clock -------------------------------------------------------

    private float syncAccumulator;
    private const float ClockSyncInterval = 1f; // a countdown does not need finer than this

    private void EnterPhase(MatchPhase next, float durationSeconds)
    {
        phase = next;
        phaseLengthSeconds = durationSeconds;
        secondsRemaining = durationSeconds;
        syncAccumulator = 0f;

        // The hook does not fire on the machine that made the change, so the
        // server raises its own copy of every phase event directly.
        PhaseChanged?.Invoke(next);

        if (next == MatchPhase.Escape) ExitsOpened?.Invoke();
        if (next == MatchPhase.Ended) RoundEnded?.Invoke();
    }

    private void Update()
    {
        if (phase == MatchPhase.Ended) return;

        // The server owns the clock. Remote clients hold a synced copy and must
        // not tick it themselves, or six machines would drift apart and disagree
        // about when 6 AM arrives. HasServerAuthority is also true when offline,
        // so the sandbox clock still runs.
        if (!NetworkMode.HasServerAuthority(this)) return;

        // Watch for the map emptying out — six dead, six escaped, or any mix.
        // A slow tick rather than an event, because PlayerHealth has no
        // server-side death hook to subscribe to; at six players this costs
        // nothing, and half a second of latency on a round-end screen is
        // invisible.
        rosterTickAccumulator += Time.deltaTime;
        if (rosterTickAccumulator >= RosterTickInterval)
        {
            rosterTickAccumulator = 0f;
            RefreshPlayerRoster();
            EvaluateRoundEnd();

            if (phase == MatchPhase.Ended) return;
        }

        if (phase == MatchPhase.Overtime)
        {
            // The second clock. Counts UP, has no target, and never ends the
            // phase — only finishing the last zone does that. The main clock is
            // deliberately left frozen where it stopped, so the HUD keeps
            // reading 06:00 while the night refuses to move.
            overtimeSeconds += Time.deltaTime;

            syncAccumulator += Time.deltaTime;
            if (syncAccumulator >= ClockSyncInterval)
            {
                syncAccumulator = 0f;
                if (NetworkServer.active) RpcSyncOvertime(overtimeSeconds);
            }

            return;
        }

        secondsRemaining -= Time.deltaTime;

        if (secondsRemaining <= 0f)
        {
            secondsRemaining = 0f;
            AdvancePhase();
            return;
        }

        // Throttle replication. Assigning a SyncVar every frame would push ~60
        // updates a second at every client for a number that changes by one.
        syncAccumulator += Time.deltaTime;
        if (syncAccumulator >= ClockSyncInterval)
        {
            syncAccumulator = 0f;
            if (NetworkServer.active) RpcSyncClock(secondsRemaining);
        }
    }

    // ---- Who is still in the building --------------------------------------

    /// <summary>
    /// Rebuilds the player list. Cheap at six players, and called on a slow tick
    /// rather than wired into PlayerHealth — PlayerHealth has no server-side
    /// death event to subscribe to, and adding one would mean editing a file the
    /// sub programmer is actively working in.
    /// </summary>
    private void RefreshPlayerRoster()
    {
        trackedPlayers.Clear();

        if (NetworkServer.active)
        {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if (conn == null || conn.identity == null) continue;

                PlayerHealth health = conn.identity.GetComponent<PlayerHealth>();
                if (health != null) trackedPlayers.Add(health);
            }
        }
        else
        {
            // Offline sandbox: there are no connections, but there is a player.
            trackedPlayers.AddRange(FindObjectsByType<PlayerHealth>());
        }

        if (trackedPlayers.Count > 0) hasSeenAnyPlayer = true;
    }

    /// <summary>
    /// Players who are alive AND have not left. A DOWNED player counts as alive
    /// and inside on purpose — the round must not end out from under a teammate
    /// who is running back to revive them.
    /// </summary>
    private int LivingInsideCount()
    {
        int count = 0;

        for (int i = 0; i < trackedPlayers.Count; i++)
        {
            PlayerHealth health = trackedPlayers[i];
            if (health == null || health.IsDead) continue;
            if (health.netIdentity != null && escapedPlayers.Contains(health.netIdentity.netId)) continue;

            count++;
        }

        return count;
    }

    /// <summary>
    /// The round is over the moment the map holds nobody who is both alive and
    /// still inside. That covers every shape of it with one rule: all six dead,
    /// all six out, or any mixture of the two.
    /// </summary>
    private void EvaluateRoundEnd()
    {
        if (phase == MatchPhase.Ended) return;
        if (!hasSeenAnyPlayer) return;          // nobody has spawned yet
        if (LivingInsideCount() > 0) return;

        if (logRoundSetup)
            Debug.Log("[MatchDirector] Nobody alive is left inside — ending the round early.", this);

        ResolveRemainingPlayers();
        EnterPhase(MatchPhase.Ended, 0f);
    }

    /// <summary>
    /// Settles anyone the round has not already resolved. Called once, when the
    /// round ends, from whichever path got there.
    ///
    /// Anyone still alive inside at this point either survived a banished ghost
    /// (a win) or ran out of night with the ghost unresolved (dead — 07:00 does
    /// not negotiate).
    /// </summary>
    private void ResolveRemainingPlayers()
    {
        RefreshPlayerRoster();

        for (int i = 0; i < trackedPlayers.Count; i++)
        {
            PlayerHealth health = trackedPlayers[i];
            if (health == null || health.netIdentity == null) continue;

            uint id = health.netIdentity.netId;

            if (health.IsDead)
            {
                outcomes[id] = PlayerOutcome.Dead;
                continue;
            }

            if (escapedPlayers.Contains(id)) continue;   // already recorded at the door

            if (ghostBanished)
            {
                // A bystander win. Ritual performers were already recorded as
                // PerformedTheRitual and are not overwritten here.
                if (!outcomes.ContainsKey(id) || outcomes[id] == PlayerOutcome.Unresolved)
                    outcomes[id] = PlayerOutcome.SurvivedTheGhost;

                continue;
            }

            if (phase == MatchPhase.LockedIn)
            {
                // The locked-in hour decides survival by location, not by the
                // gates: inside the real secret room you live, anywhere else in
                // the building you do not.
                if (playersInSecretRoom.Contains(id))
                {
                    outcomes[id] = PlayerOutcome.SurvivedTheGhost;
                    continue;
                }

                outcomes[id] = PlayerOutcome.Dead;
                health.ServerKill("not inside the secret room when the locked-in hour ended");
                continue;
            }

            // Still inside, still alive, and the clock has run out.
            outcomes[id] = PlayerOutcome.Dead;
            health.ServerKill("still inside when the round ended");
        }
    }

    private void AdvancePhase()
    {
        switch (phase)
        {
            case MatchPhase.Night:
                // 06:00 arrives. Three different things can happen, and which
                // one is decided here and nowhere else.
                if (MinimumMet)
                {
                    // Every zone finished. Dawn comes, the doors open.
                    EnterPhase(MatchPhase.Escape, EscapeWindowInGameHours * SecondsPerInGameHour);

                    if (logRoundSetup)
                        Debug.Log($"[MatchDirector] 06:00 — all {zonesRequired} zones done. Gates open for {secondsRemaining:F0}s.", this);
                }
                else if (DeadPlayerCount() >= OvertimeDeathThreshold)
                {
                    // The night refuses to end. The main clock freezes at 06:00
                    // and a second clock starts counting up instead — no target,
                    // no deadline, just a rising cost to every extra minute as
                    // the ghosts hunt harder. Finishing the zones is what starts
                    // the real clock again.
                    EnterPhase(MatchPhase.Overtime, 0f);

                    if (logRoundSetup)
                    {
                        Debug.Log(
                            $"[MatchDirector] 06:00 — {zonesCompleted}/{zonesRequired} zones with " +
                            $"{DeadPlayerCount()} dead. The clock stops. Overtime begins; no deadline, rising escalation.", this);
                    }
                }
                else
                {
                    // Sealed in. One in-game hour to find the Ghost Key and the
                    // one secret room that is really open tonight.
                    EnterPhase(MatchPhase.LockedIn, EscapeWindowInGameHours * SecondsPerInGameHour);

                    if (logRoundSetup)
                    {
                        Debug.Log(
                            $"[MatchDirector] 06:00 — {zonesCompleted}/{zonesRequired} zones, only {DeadPlayerCount()} dead. " +
                            $"Everyone is locked in for {secondsRemaining:F0}s. Ghost Key or death.", this);
                    }
                }
                break;

            case MatchPhase.Overtime:
                // Overtime has no timer, so nothing should ever advance it on a
                // clock. It leaves only through ServerReportZoneCompleted
                // finishing the last zone. Reaching here means the clock ticked
                // when it should not have.
                Debug.LogError("[MatchDirector] Overtime advanced on a timer. Overtime has no deadline — this is a bug in the clock.", this);
                break;

            case MatchPhase.LockedIn:
                // The hour is up. Anyone not inside the secret room dies, and
                // ResolveRemainingPlayers does the killing.
                ResolveRemainingPlayers();
                EnterPhase(MatchPhase.Ended, 0f);

                if (logRoundSetup)
                    Debug.Log($"[MatchDirector] Locked-in hour over. {playersInSecretRoom.Count} reached the secret room.", this);
                break;

            case MatchPhase.Escape:
                // 07:00. Anyone still inside had two ways to survive it — walk
                // out of a gate, or deal with the map's main ghost. Whoever did
                // neither is dead, and ResolveRemainingPlayers kills them.
                ResolveRemainingPlayers();
                EnterPhase(MatchPhase.Ended, 0f);

                if (logRoundSetup)
                {
                    Debug.Log(
                        $"[MatchDirector] 07:00 — round over. {escapedCount} out, " +
                        $"ghost {(ghostBanished ? "dealt with" : "unresolved")}, " +
                        $"minimum {(MinimumMet ? "met" : "missed")}.", this);
                }
                break;
        }
    }

    [ClientRpc]
    private void RpcSyncClock(float remaining)
    {
        // Host already holds the authoritative value; only remote clients need it.
        if (isServer) return;
        secondsRemaining = remaining;
    }

    [ClientRpc]
    private void RpcSyncOvertime(float elapsed)
    {
        if (isServer) return;
        overtimeSeconds = elapsed;
    }

    /// <summary>
    /// How many players are dead right now. Drives the 06:00 branch, so it is
    /// evaluated at that instant and never cached — a death at 05:59 counts.
    /// </summary>
    private int DeadPlayerCount()
    {
        RefreshPlayerRoster();

        int dead = 0;
        for (int i = 0; i < trackedPlayers.Count; i++)
        {
            PlayerHealth health = trackedPlayers[i];
            if (health != null && health.IsDead) dead++;
        }

        return dead;
    }

    // ---- Progress: two separate counters ------------------------------------
    //
    //   ZONES -> survival.  TASKS -> money, per player.
    //
    // They are reported by two different calls on purpose. A single counter
    // could not express both, and merging them later would quietly reintroduce
    // the bug where finishing lots of easy tasks across the map substitutes for
    // committing to a room and seeing it through.

    /// <summary>
    /// Call this when ONE PLAYER finishes ONE task. Tasks are one-shot ("sell
    /// three tickets" is a single task), and this call is about MONEY only — the
    /// GDD pays each player for their own completions.
    ///
    /// It deliberately does NOT move the survival counter. A task is progress
    /// toward finishing a zone; only the finished zone counts for getting out.
    ///
    /// Guarded rather than marked [Server] so it also works in the offline
    /// sandbox. A remote client reaching this is a bug, so it is refused loudly
    /// instead of silently doing nothing.
    /// </summary>
    public void ServerReportTaskCompleted(NetworkIdentity player, string zoneID = null)
    {
        if (!NetworkMode.HasServerAuthority(this))
        {
            Debug.LogWarning("[MatchDirector] ServerReportTaskCompleted was called on a client. Task completion is decided on the server only — ignored.", this);
            return;
        }

        if (phase == MatchPhase.Ended) return;

        uint id = player != null ? player.netId : 0u;
        tasksPerPlayer.TryGetValue(id, out int done);
        tasksPerPlayer[id] = done + 1;

        if (logRoundSetup)
            Debug.Log($"[MatchDirector] Task complete{(string.IsNullOrEmpty(zoneID) ? "" : $" in '{zoneID}'")} — that player is now on {done + 1}.", this);
    }

    /// <summary>How many tasks this player finished. The payout screen reads it.</summary>
    public int TasksCompletedBy(NetworkIdentity player)
    {
        if (player == null) return 0;
        return tasksPerPlayer.TryGetValue(player.netId, out int done) ? done : 0;
    }

    /// <summary>
    /// Call this when EVERY task in a zone is done. This is the survival counter:
    /// <see cref="ZonesRequired"/> of these before 06:00 or nobody crosses a gate.
    ///
    /// <paramref name="zoneID"/> must be stable and unique per zone — it is what
    /// stops a zone being counted twice and walking the team through a gate they
    /// never earned.
    ///
    /// Note what this does NOT do: it never opens an exit. The doors are on the
    /// clock, and meeting the minimum early buys only the right to use them.
    /// </summary>
    public void ServerReportZoneCompleted(string zoneID)
    {
        if (!NetworkMode.HasServerAuthority(this))
        {
            Debug.LogWarning("[MatchDirector] ServerReportZoneCompleted was called on a client. Zone completion is decided on the server only — ignored.", this);
            return;
        }

        if (phase == MatchPhase.Ended) return;

        if (string.IsNullOrEmpty(zoneID))
        {
            Debug.LogError("[MatchDirector] ServerReportZoneCompleted was called with no zoneID. Without a stable ID a zone can be counted twice — ignored.", this);
            return;
        }

        if (!completedZones.Add(zoneID))
        {
            Debug.LogWarning($"[MatchDirector] Zone '{zoneID}' was reported complete twice — ignored the second one.", this);
            return;
        }

        bool wasMet = MinimumMet;
        zonesCompleted = completedZones.Count;

        if (logRoundSetup)
            Debug.Log($"[MatchDirector] Zone '{zoneID}' complete — {zonesCompleted}/{zonesRequired}.", this);

        // Server raises its own event; the SyncVar hook only runs on remote clients.
        ZoneProgressChanged?.Invoke(zonesCompleted, zonesRequired);

        if (!wasMet && MinimumMet)
        {
            MinimumReached?.Invoke();

            if (phase == MatchPhase.Overtime)
            {
                // The last zone is done, so dawn is finally allowed to arrive.
                // The main clock starts again exactly where it froze and the
                // doors open — the release the overtime dread was building to.
                if (logRoundSetup)
                {
                    Debug.Log(
                        $"[MatchDirector] Last zone finished after {overtimeSeconds:F0}s of overtime. " +
                        "The clock starts again — gates open.", this);
                }

                EnterPhase(MatchPhase.Escape, EscapeWindowInGameHours * SecondsPerInGameHour);
            }
            else if (logRoundSetup)
            {
                Debug.Log(
                    "[MatchDirector] Every zone is done — the team can leave at dawn. " +
                    "The exits stay LOCKED until 06:00; further tasks are money.", this);
            }
        }
    }

    /// <summary>
    /// Called by each quest zone as it comes up, so MatchDirector knows how many
    /// zones the team owes. EVERY registered zone must be finished — there is no
    /// difficulty that asks for a subset.
    ///
    /// Registration rather than a number on the difficulty asset means a map
    /// with eight quest zones works with no code or asset change, and a zone
    /// that fails to spawn cannot silently lower the requirement without the
    /// log below saying so.
    /// </summary>
    public void ServerRegisterZone(string zoneID)
    {
        if (!NetworkMode.HasServerAuthority(this)) return;

        if (string.IsNullOrEmpty(zoneID))
        {
            Debug.LogError("[MatchDirector] ServerRegisterZone was called with no zoneID — ignored.", this);
            return;
        }

        if (!registeredZones.Add(zoneID)) return;

        zonesRequired = registeredZones.Count;
        ZoneProgressChanged?.Invoke(zonesCompleted, zonesRequired);

        if (logRoundSetup)
            Debug.Log($"[MatchDirector] Zone '{zoneID}' registered — {zonesRequired} zones in this map.", this);
    }

    /// <summary>
    /// Called when a player enters or leaves the real secret room during
    /// LockedIn. Only players inside when the hour ends survive.
    /// </summary>
    public void ServerSetPlayerInSecretRoom(NetworkIdentity player, bool inside)
    {
        if (!NetworkMode.HasServerAuthority(this) || player == null) return;

        if (inside) playersInSecretRoom.Add(player.netId);
        else playersInSecretRoom.Remove(player.netId);
    }

    public bool IsInSecretRoom(NetworkIdentity player) =>
        player != null && playersInSecretRoom.Contains(player.netId);

    /// <summary>Whether a zone has already been counted. For zone UI and the quest system.</summary>
    public bool IsZoneComplete(string zoneID) => !string.IsNullOrEmpty(zoneID) && completedZones.Contains(zoneID);


    // ---- Escape ------------------------------------------------------------

    /// <summary>
    /// Called by an ExitPoint once it has accepted a player. Server-side only —
    /// a client must never be able to declare its own escape, since escaping is
    /// what decides whether the round was cleared.
    /// </summary>
    public void ServerReportPlayerEscaped(NetworkIdentity player)
    {
        if (!NetworkMode.HasServerAuthority(this))
        {
            Debug.LogWarning("[MatchDirector] ServerReportPlayerEscaped was called on a client — ignored.", this);
            return;
        }

        if (phase != MatchPhase.Escape) return;

        uint id = player != null ? player.netId : 0u;
        if (!escapedPlayers.Add(id)) return;   // already out

        escapedCount = escapedPlayers.Count;
        outcomes[id] = PlayerOutcome.EscapedThroughGate;

        if (logRoundSetup)
            Debug.Log($"[MatchDirector] A player is out — {escapedCount} escaped.", this);

        // That player may have been the last one alive inside.
        RefreshPlayerRoster();
        EvaluateRoundEnd();
    }

    /// <summary>
    /// Call this when the map's main ghost has been dealt with — the hidden
    /// method for this level, discovered and fully executed. Method 2.
    ///
    /// Everyone still alive inside wins on the spot, exactly as if they had
    /// walked out of a gate. <paramref name="performers"/> are the players who
    /// actually carried it out; they are the only ones who earn the bonus.
    ///
    /// The round ends immediately, because at this point nobody left in the
    /// building has anything to survive.
    ///
    /// SERVER ONLY.
    /// </summary>
    public void ServerReportGhostBanished(IEnumerable<NetworkIdentity> performers = null)
    {
        if (!NetworkMode.HasServerAuthority(this))
        {
            Debug.LogWarning("[MatchDirector] ServerReportGhostBanished was called on a client — ignored.", this);
            return;
        }

        if (phase == MatchPhase.Ended || ghostBanished) return;

        // The ritual is gated on the same minimum the gates are. A caller that
        // reaches here without it has let players skip the tasks and win on the
        // ghost alone — refuse it loudly rather than quietly accepting a win
        // that unravels the objective structure of the whole game.
        if (!RitualPermitted)
        {
            Debug.LogError(
                "[MatchDirector] ServerReportGhostBanished was called with the task minimum unmet " +
                $"({zonesCompleted}/{zonesRequired} zones). Method 2 requires the minimum, exactly as the gates do — " +
                "otherwise a team can skip the tasks, hide until 06:00 and win on the ritual. " +
                "Check MatchDirector.RitualPermitted before starting the ritual. Ignored.", this);
            return;
        }

        ghostBanished = true;
        GhostBanishedChanged?.Invoke();   // the hook will not fire on this machine

        if (performers != null)
        {
            foreach (NetworkIdentity performer in performers)
            {
                if (performer == null) continue;

                // A performer who already walked out keeps the gate outcome —
                // they cannot be inside performing a ritual and outside at once,
                // so this only guards against a caller passing a stale list.
                if (escapedPlayers.Contains(performer.netId)) continue;

                outcomes[performer.netId] = PlayerOutcome.PerformedTheRitual;
            }
        }

        if (logRoundSetup)
            Debug.Log("[MatchDirector] The main ghost has been dealt with — everyone still alive inside survives.", this);

        ResolveRemainingPlayers();
        EnterPhase(MatchPhase.Ended, 0f);
    }

    /// <summary>
    /// Whether a given player has already left the building. Exits use this so a
    /// player cannot consume a second door slot.
    /// </summary>
    public bool HasEscaped(NetworkIdentity player) =>
        player != null && escapedPlayers.Contains(player.netId);

    // ---- SyncVar hooks (remote clients only) -------------------------------

    private void OnZonesChanged(int _, int __)
    {
        ZoneProgressChanged?.Invoke(zonesCompleted, zonesRequired);
        if (MinimumMet) MinimumReached?.Invoke();
    }

    private void OnGhostBanishedChanged(bool _, bool now)
    {
        if (now) GhostBanishedChanged?.Invoke();
    }

    private void OnPhaseChanged(MatchPhase _, MatchPhase now)
    {
        PhaseChanged?.Invoke(now);

        if (now == MatchPhase.Escape) ExitsOpened?.Invoke();
        if (now == MatchPhase.Ended) RoundEnded?.Invoke();
    }
}
