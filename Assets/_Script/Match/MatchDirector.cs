using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// The authority for one round. Put ONE of these in the GamePlay scene, on an
/// empty GameObject with a NetworkIdentity.
///
/// What it does: when the server starts, it reads the host's RoomConfig out of
/// MatchState, finds the matching DifficultyProfile, and turns those choices into
/// the live numbers this round runs on. Everything it resolves is exposed as
/// SyncVars, so every client sees the same requirements and the same clock
/// without ever computing them.
///
/// This is why one scene covers every difficulty and every player count. The
/// geometry never changes — only these values do.
///
/// AUTHORITY: the server resolves, the clients display. A client never reads a
/// DifficultyProfile and never decides whether the exits are open. If it did,
/// a modified client would simply set its own requirement to zero.
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
    /// Single point of access for the quest, rule and HUD systems. Cleared in
    /// OnDestroy so a scene reload cannot leave a dead reference behind.
    /// </summary>
    public static MatchDirector Instance { get; private set; }

    [Header("Difficulty")]
    [Tooltip("One profile per tier. A List, not a Dictionary — Unity cannot serialize a Dictionary into the Inspector, and this project keeps serialized collections as Lists throughout.")]
    [SerializeField] private List<DifficultyProfile> difficultyProfiles = new List<DifficultyProfile>();

    [Tooltip("Used if the room's difficulty has no matching profile in the list. Leave empty and the round still runs on safe built-in defaults, but you will get a warning.")]
    [SerializeField] private DifficultyProfile fallbackProfile;

    [Header("Night clock (map constant, not a difficulty knob)")]
    [Tooltip("In-game hour the round begins, 24h. The GDD ends the night at 6 AM; the start hour sets how long the night is.")]
    [Range(0, 23)] [SerializeField] private int startHour = 2;

    [Tooltip("In-game hour the round ends. GDD: 6 AM.")]
    [Range(0, 23)] [SerializeField] private int endHour = 6;

    [Header("Debug")]
    [SerializeField] private bool logRoundSetup = true;

    // ---- Replicated round state -------------------------------------------
    // Everything below is written by the SERVER only (or locally when offline).
    // Clients read these to draw the HUD and nothing else.

    [SyncVar] private string syncedMapID = RoomConfig.DemoMapID;
    [SyncVar] private DifficultyLevel syncedDifficulty = DifficultyLevel.Normal;

    [SyncVar(hook = nameof(OnTasksChanged))] private int tasksCompleted;
    [SyncVar(hook = nameof(OnTasksChanged))] private int tasksRequired = 6;

    [SyncVar(hook = nameof(OnExitsOpenChanged))] private bool exitsOpen;

    /// <summary>Real seconds left in the round. Synced on an interval, not every frame — a countdown does not need 60 updates a second.</summary>
    [SyncVar] private float secondsRemaining;

    /// <summary>Total real seconds this round was given, so clients can draw progress without knowing the difficulty maths.</summary>
    [SyncVar] private float roundLengthSeconds;

    /// <summary>Resolved on the server from the profile, replicated so sanity sources on any machine agree on the rate.</summary>
    [SyncVar] private float sanityDrainMultiplier = 1f;

    /// <summary>Resolved downed-survival window in real seconds. PlayerHealth should read this instead of its own hardcoded 30f.</summary>
    [SyncVar] private float downedDurationSeconds = 150f;

    [SyncVar(hook = nameof(OnRoundRunningChanged))] private bool roundRunning;

    // ---- Public read-only surface -----------------------------------------

    public string MapID => syncedMapID;
    public DifficultyLevel Difficulty => syncedDifficulty;
    public int TasksCompleted => tasksCompleted;
    public int TasksRequired => tasksRequired;
    public bool ExitsOpen => exitsOpen;
    public float SecondsRemaining => secondsRemaining;
    public float RoundLengthSeconds => roundLengthSeconds;
    public float SanityDrainMultiplier => sanityDrainMultiplier;
    public float DownedDurationSeconds => downedDurationSeconds;
    public bool RoundRunning => roundRunning;

    /// <summary>0 at the start of the night, 1 when the clock runs out.</summary>
    public float RoundProgress01 =>
        roundLengthSeconds <= 0f ? 0f : Mathf.Clamp01(1f - (secondsRemaining / roundLengthSeconds));

    /// <summary>How many in-game hours the night lasts, wrapping past midnight.</summary>
    public int NightLengthInGameHours => endHour > startHour ? endHour - startHour : (24 - startHour) + endHour;

    /// <summary>
    /// The in-game clock as a 24h hour and minute, for a diegetic HUD ("03:47").
    /// Derived from round progress so it can never disagree with the timer.
    /// </summary>
    public void GetInGameTime(out int hour, out int minute)
    {
        float totalHours = Mathf.Max(0.01f, NightLengthInGameHours);
        float absolute = startHour + RoundProgress01 * totalHours;

        hour = Mathf.FloorToInt(absolute) % 24;
        minute = Mathf.FloorToInt((absolute - Mathf.Floor(absolute)) * 60f);
    }

    // ---- Events for UI (fire on every machine) -----------------------------

    public event System.Action<int, int> TaskProgressChanged;   // completed, required
    public event System.Action ExitsOpened;
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

        if (profile != null)
        {
            tasksRequired = profile.TasksRequiredFor(playerCount);
            roundLengthSeconds = NightLengthInGameHours * profile.secondsPerInGameHour;
            sanityDrainMultiplier = profile.sanityDrainMultiplier;
            downedDurationSeconds = profile.DownedDurationSeconds;
        }
        else
        {
            // No profile assigned. Keep the round playable rather than dividing by
            // zero, but make it obvious in the console that balancing is missing.
            tasksRequired = 6;
            roundLengthSeconds = NightLengthInGameHours * 150f;
            sanityDrainMultiplier = 1f;
            downedDurationSeconds = 150f;

            Debug.LogWarning(
                $"[MatchDirector] No DifficultyProfile found for '{config.difficulty}' and no fallback assigned. " +
                "Running on built-in defaults — assign the three profiles in the Inspector.", this);
        }

        tasksCompleted = 0;
        exitsOpen = false;
        secondsRemaining = roundLengthSeconds;
        roundRunning = true;

        // Mirror does not call a SyncVar hook on the machine that made the change,
        // so the host/server has to raise its own UI event directly or the host's
        // HUD would sit on stale values while every remote client updated fine.
        TaskProgressChanged?.Invoke(tasksCompleted, tasksRequired);

        if (logRoundSetup)
        {
            Debug.Log(
                $"[MatchDirector] Round configured — map={syncedMapID}, difficulty={syncedDifficulty}, " +
                $"players={playerCount}, tasksRequired={tasksRequired}, " +
                $"round={roundLengthSeconds:F0}s ({startHour:00}:00 → {endHour:00}:00), " +
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

    // ---- Round clock -------------------------------------------------------

    private float syncAccumulator;
    private const float ClockSyncInterval = 1f; // a countdown does not need finer than this

    private void Update()
    {
        if (!roundRunning) return;

        // The server owns the clock. Remote clients hold a synced copy and must
        // not tick it themselves, or six machines would drift apart and disagree
        // about when 6 AM arrives. HasServerAuthority is also true when offline,
        // so the sandbox clock still runs.
        if (!NetworkMode.HasServerAuthority(this)) return;

        secondsRemaining -= Time.deltaTime;

        if (secondsRemaining <= 0f)
        {
            secondsRemaining = 0f;
            EndRound();
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

    [ClientRpc]
    private void RpcSyncClock(float remaining)
    {
        // Host already holds the authoritative value; only remote clients need it.
        if (isServer) return;
        secondsRemaining = remaining;
    }

    // ---- Task progress -----------------------------------------------------

    /// <summary>
    /// Call this when an area finishes its task. The quest system will be the
    /// caller once it exists; until then it is a clean hook to fire from a debug
    /// key while building the scene.
    ///
    /// Guarded rather than marked [Server] so it also works in the offline
    /// sandbox. A remote client reaching this is a bug, so it is refused loudly
    /// instead of silently doing nothing — a client can never be allowed to
    /// declare a task complete.
    /// </summary>
    public void ServerReportTaskCompleted(string areaID = null)
    {
        if (!NetworkMode.HasServerAuthority(this))
        {
            Debug.LogWarning("[MatchDirector] ServerReportTaskCompleted was called on a client. Task completion is decided on the server only — ignored.", this);
            return;
        }

        if (!roundRunning) return;

        tasksCompleted++;

        if (logRoundSetup)
            Debug.Log($"[MatchDirector] Task complete{(string.IsNullOrEmpty(areaID) ? "" : $" in '{areaID}'")} — {tasksCompleted}/{tasksRequired}.", this);

        // Server raises its own event; the SyncVar hook only runs on remote clients.
        TaskProgressChanged?.Invoke(tasksCompleted, tasksRequired);

        if (!exitsOpen && tasksCompleted >= tasksRequired) OpenExits();
    }

    private void OpenExits()
    {
        exitsOpen = true;
        ExitsOpened?.Invoke();   // this machine's own copy — the hook will not fire here

        if (logRoundSetup) Debug.Log("[MatchDirector] Task minimum reached — exits are open.", this);
    }

    private void EndRound()
    {
        roundRunning = false;
        RoundEnded?.Invoke();    // own copy again, for the same reason

        if (logRoundSetup) Debug.Log("[MatchDirector] The night is over — round ended.", this);
    }

    // ---- SyncVar hooks (remote clients only) -------------------------------

    private void OnTasksChanged(int _, int __)
    {
        TaskProgressChanged?.Invoke(tasksCompleted, tasksRequired);
    }

    private void OnExitsOpenChanged(bool _, bool nowOpen)
    {
        if (nowOpen) ExitsOpened?.Invoke();
    }

    private void OnRoundRunningChanged(bool _, bool nowRunning)
    {
        if (!nowRunning) RoundEnded?.Invoke();
    }

    // ---- Editor convenience ------------------------------------------------

    private void OnValidate()
    {
        if (startHour == endHour)
            Debug.LogWarning("[MatchDirector] startHour equals endHour, so the night has zero length. Set them to different hours.", this);
    }
}
