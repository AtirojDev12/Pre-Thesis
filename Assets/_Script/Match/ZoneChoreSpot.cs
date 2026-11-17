using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 14 Oct 2026 (Mr.k, Zone Quest Detail): one "hold E" chore in a quest zone.
/// Used for the Screen Room (change film in the ghost room, clean both rooms)
/// and the Toilets (clean both toilets). Made by
/// Tools > Pre-Thesis > Scenes > Map: Setup Screen + Toilet Zones.
///
///   - The chore appears at <see cref="fromHour"/> on the match clock
///     (and again every <see cref="repeatEveryHours"/>, e.g. film at 02:00 and 04:00).
///   - A player looks at it and HOLDS E for <see cref="holdSeconds"/>.
///   - Done = +1 on every matching line of the zone's task board (ZoneTaskList)
///     and +1 task for that player (currency on the results screen).
///
/// SERVER AUTHORITY: the server decides when the chore is needed, who works on
/// it and when it is finished (timer + range + alive checks on the server).
/// The worker's PC only says "I let go of E". Rules / punishments are NOT here
/// (later, one by one).
/// </summary>
[DisallowMultipleComponent]
public sealed class ZoneChoreSpot : InteractableBase
{
    [Header("Chore")]
    [Tooltip("Which task-board lines this chore counts for.")]
    [SerializeField] private ZoneTaskKind kind = ZoneTaskKind.CleanScreenRoom;
    [Tooltip("Zone of this chore (task credit for the player who did it).")]
    [SerializeField] private string zoneID = "zone_screen";
    [Tooltip("Shown in the prompt and progress bar, e.g. \"Clean\" or \"Change film\".")]
    [SerializeField] private string actionText = "Clean";
    [SerializeField, Min(0.5f)] private float holdSeconds = 3f;

    [Header("When (match clock, 00:00 - 06:00)")]
    [Tooltip("In-game hour the chore first appears.")]
    [SerializeField, Range(0, 5)] private int fromHour = 1;
    [Tooltip("0 = once. 2 = again every 2 in-game hours (film: 02:00 and 04:00).")]
    [SerializeField, Min(0)] private int repeatEveryHours = 0;
    [Tooltip("4 Nov: minutes after From Hour, e.g. 30 with From Hour 0 = 00:30.")]
    [SerializeField, Range(0, 59)] private int fromMinute = 0;
    [Tooltip("4 Nov: 0 = no limit. 2 = needed at most 2 times a night.")]
    [SerializeField, Min(0)] private int maxTimes = 0;

    [Header("Bonus (4 Nov, Mr.k)")]
    [Tooltip("Off = BONUS chore: +1 task (money) for the player only. It does NOT count on the zone's task board, so skipping it is fine (e.g. the normal screen room).")]
    [SerializeField] private bool countsForZoneBoard = true;

    [Header("Look")]
    [Tooltip("Shown only while the chore is needed (dirt mark, warning light).")]
    [SerializeField] private GameObject[] showWhenNeeded;

    [Header("Needs an item (17 Nov, Task 5)")]
    [Tooltip("Empty = bare hands. Else the CarryProp the worker must hold in the hand: \"mop\", \"film_reel_ghost\", \"film_reel_normal\".")]
    [SerializeField] private string needsItem = "";
    [Tooltip("Prompt while the player does not hold that item, e.g. \"Needs a mop (Staff Room)\".")]
    [SerializeField] private string needsItemHint = "";
    [Tooltip("On = the item is used up when the chore is done (a film reel goes back on its shelf).")]
    [SerializeField] private bool usesUpItem;
    [Tooltip("17 Nov: On = the item's label must match the time this chore is due (film reel \"02:00\" for the 02:00 change). A wrong film is refused.")]
    [SerializeField] private bool needsMatchingLabel;
    [Tooltip("17 Nov: the film needed at each time of the night, in order (1st change, 2nd change ...). Must match the reel labels. Empty = the time (\"02:00\").")]
    [SerializeField] private string[] filmNames;
    [Tooltip("17 Nov: the reel colour of each film above, in words for the prompt (\"red\").")]
    [SerializeField] private string[] filmColors;

    private const float ServerRange = 4.5f; // interaction range 3 m + slack
    private const int NightEndHour = 6;

    // Server writes, everyone reads.
    [SyncVar(hook = nameof(OnNeededChanged))] private bool needed;
    [SyncVar] private uint workerId;
    [SyncVar] private double workStart;

    // Server only.
    private int pending;
    private double neededSince; // 17 Nov: when the current "needed" started (film deadline)
    private readonly System.Collections.Generic.List<int> dueSlots = new System.Collections.Generic.List<int>(); // 17 Nov: due times waiting (minutes)

    // 17 Nov: the time label of the oldest due slot ("02:00"); the film reel must match it.
    [SyncVar] private string dueLabel = "";
    [SyncVar] private string dueColor = "";
    private int nextSlotMinute; // minutes after 00:00 on the match clock
    private int slotsMade;
    private GameObject offlineWorker;

    // Local only.
    private string promptText;
    private bool stopSent;
    private Collider[] colliders;
    private PlayerHealth ignoredBody; // 16 Oct: our colliders never block this body
    private AudioSource sweepSource;
    private double sweepStartedAt;
    private bool sweepPlaying;

    public ZoneTaskKind Kind => kind;
    /// <summary>17 Nov: every chore spot in the scene (stain arrows, film deadline).</summary>
    public static readonly System.Collections.Generic.List<ZoneChoreSpot> All = new System.Collections.Generic.List<ZoneChoreSpot>();
    public string NeedsItem => needsItem;
    /// <summary>SERVER. When the chore became needed (network time). Only valid while <see cref="Needed"/>.</summary>
    public double NeededSince => neededSince;
    public int Pending => pending;
    /// <summary>17 Nov: "02:00" = the film this projector needs now. Empty when nothing is due.</summary>
    public string DueLabel => dueLabel;
    public bool NeedsMatchingLabel => needsMatchingLabel;

    /// <summary>SERVER. 17 Nov: the time (minutes) of the film change that is due now, or -1.</summary>
    public int ServerDueMinute => dueSlots.Count > 0 ? dueSlots[0] : -1;
    /// <summary>Minutes between two film changes (0 = once).</summary>
    public int RepeatMinutes => repeatEveryHours * 60;

    /// <summary>17 Nov: the film name for the change at this time (or "02:00" when no names are set).</summary>
    public string FilmLabelFor(int minute)
    {
        int index = ScheduleMinutes().IndexOf(minute);
        if (index >= 0 && filmNames != null && index < filmNames.Length && !string.IsNullOrEmpty(filmNames[index])) return filmNames[index];
        return TimeLabel(minute);
    }

    private string FilmColorFor(int minute)
    {
        int index = ScheduleMinutes().IndexOf(minute);
        return index >= 0 && filmColors != null && index < filmColors.Length ? filmColors[index] ?? "" : "";
    }

    private string DueText => string.IsNullOrEmpty(dueColor) ? "\"" + dueLabel + "\"" : "\"" + dueLabel + "\" (" + dueColor + " reel)";

    /// <summary>"HH:MM" for minutes after 00:00.</summary>
    public static string TimeLabel(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    /// <summary>17 Nov: every time this chore becomes needed tonight (minutes after 00:00). Works in the editor too.</summary>
    public System.Collections.Generic.List<int> ScheduleMinutes()
    {
        var list = new System.Collections.Generic.List<int>();
        int at = fromHour * 60 + fromMinute;
        while (at < NightEndHour * 60 && (maxTimes == 0 || list.Count < maxTimes))
        {
            list.Add(at);
            if (repeatEveryHours <= 0) break;
            at += repeatEveryHours * 60;
        }
        return list;
    }
    public bool CountsForZoneBoard => countsForZoneBoard;
    public bool Needed => needed;
    // Audio observes the existing authoritative work state; no extra sound RPCs.
    public bool IsWorking => workerId != 0 && !(IsLocalWorker && stopSent);
    public double WorkStartedAt => workStart;

    // ---- Hooks for the zone rules (16 Oct) -------------------------------------------
    /// <summary>SERVER. A chore was finished (spot, worker body). Zone rules listen.</summary>
    public static event System.Action<ZoneChoreSpot, GameObject> ServerCompleted;
    /// <summary>SERVER. A zone rule may lock a chore (e.g. the smoking stall). True = nobody can work on it.</summary>
    public static System.Func<ZoneChoreSpot, bool> ServerBlocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetHooks()
    {
        ServerCompleted = null;
        ServerBlocked = null;
        All.Clear();
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }

    private bool IsBlocked => ServerBlocked != null && ServerBlocked(this);

    private static double Now => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;

    protected override void Awake()
    {
        base.Awake();
        promptText = "[Hold E] " + actionText + (countsForZoneBoard ? "" : " (bonus)");
        colliders = GetComponents<Collider>();
        nextSlotMinute = fromHour * 60 + fromMinute;
        ApplyLook(needed);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyLook(needed);
    }

    // ---- Prompt / interaction ------------------------------------------------

    public override string GetInteractionPrompt()
    {
        if (HasItemForWork(null)) return needsMatchingLabel && !string.IsNullOrEmpty(dueLabel) ? promptText + ": \"" + dueLabel + "\"" : promptText;
        if (needsMatchingLabel && !string.IsNullOrEmpty(dueLabel))
        {
            string held = CarryProp.LocalHeldLabel(needsItem);
            return held != null
                ? "Wrong film: this is \"" + held + "\". This projector needs " + DueText + "."
                : "Needs the film " + DueText + " from the film shelf in the booth";
        }
        return string.IsNullOrEmpty(needsItemHint) ? "Needs an item" : needsItemHint;
    }

    /// <summary>
    /// 17 Nov: does this player hold the needed item in the hand? null = the local player (prompt).
    /// Chores with no needed item always say yes.
    /// </summary>
    private bool HasItemForWork(GameObject player)
    {
        if (string.IsNullOrEmpty(needsItem)) return true;
        if (!needsMatchingLabel) return player == null ? CarryProp.LocalHolds(needsItem) : CarryProp.ServerHeld(player, needsItem) != null;
        // 17 Nov: the film reel's label must be the time that is due now.
        string label = player == null ? CarryProp.LocalHeldLabel(needsItem) : CarryProp.ServerHeld(player, needsItem)?.Label;
        return label != null && label == dueLabel;
    }

    [TargetRpc]
    private void TargetSay(NetworkConnectionToClient target, string text) => RuleFx.Toast(text, 3.5f);

    private void Say(GameObject player, string text)
    {
        if (NetworkMode.IsOffline) { RuleFx.Toast(text, 3.5f); return; }
        NetworkIdentity identity = player != null ? player.GetComponent<NetworkIdentity>() : null;
        if (identity != null && identity.connectionToClient != null) TargetSay(identity.connectionToClient, text);
    }

    public override bool CanInteract() =>
        needed && RoundRunning && (workerId == 0 || IsLocalWorker);

    /// <summary>SERVER: E pressed on this chore. Starts the hold timer for that player.</summary>
    protected override void OnInteracted(GameObject interactor)
    {
        if (!needed || interactor == null || !CanWork(interactor) || IsBlocked) return;
        if (!HasItemForWork(interactor))
        {
            // 17 Nov: mop / the right film reel in the hand first. A wrong film is refused.
            CarryProp held = CarryProp.ServerHeld(interactor, needsItem);
            if (needsMatchingLabel && held != null)
                Say(interactor, "The projector refused it: wrong film (\"" + held.Label + "\"). It needs " + DueText + ".");
            return;
        }
        uint id = WorkerIdOf(interactor);
        if (workerId != 0 && workerId != id) return; // someone else is already on it
        offlineWorker = NetworkMode.IsOffline ? interactor : null;
        stopSent = false;
        workerId = id;
        workStart = Now;
    }

    // ---- Server ----------------------------------------------------------------

    private void Update()
    {
        if (NetworkMode.HasServerAuthority(this)) ServerTick();
        LocalTick();
    }

    private void ServerTick()
    {
        MatchDirector match = MatchDirector.Instance;
        if (match == null || !RoundRunning)
        {
            if (workerId != 0) StopWork();
            return;
        }

        // New slot on the clock: the chore is needed (again).
        match.GetInGameTime(out int hour, out int minute);
        int now = hour * 60 + minute;
        while (nextSlotMinute < NightEndHour * 60 && now >= nextSlotMinute && (maxTimes == 0 || slotsMade < maxTimes))
        {
            pending++;
            slotsMade++;
            dueSlots.Add(nextSlotMinute);
            if (dueSlots.Count == 1) SetDue(dueSlots[0]);
            nextSlotMinute = repeatEveryHours > 0 ? nextSlotMinute + repeatEveryHours * 60 : NightEndHour * 60;
        }
        if (!needed && pending > 0) SetNeeded(true);

        if (workerId == 0) return;
        GameObject worker = WorkerObject();
        if (worker == null || !needed || !CanWork(worker) || IsBlocked || !HasItemForWork(worker))
        {
            StopWork();
            return;
        }
        if (Now - workStart >= holdSeconds) ServerComplete(worker);
    }

    private void ServerComplete(GameObject worker)
    {
        StopWork();
        if (usesUpItem && !string.IsNullOrEmpty(needsItem))
        {
            CarryProp used = CarryProp.ServerHeld(worker, needsItem);
            if (used != null) used.ServerConsume(); // 17 Nov: the reel went into the projector
        }
        pending = Mathf.Max(0, pending - 1);
        PopDueSlot();
        if (pending == 0) SetNeeded(false);
        else neededSince = Now; // 17 Nov: the next one waits from now

        // Bonus chores (4 Nov) pay the player only; they never move the zone's board.
        if (countsForZoneBoard) ZoneTaskList.ServerReportSale(kind, PopcornFlavor.None, -1, false);
        if (MatchDirector.Instance != null)
            MatchDirector.Instance.ServerReportTaskCompleted(worker.GetComponent<NetworkIdentity>(), zoneID);
        Debug.Log($"[ZoneChoreSpot] '{name}' done by {worker.name} ({kind}).", this);
        ServerCompleted?.Invoke(this, worker);
    }

    /// <summary>SERVER. 17 Nov: the oldest due time is done; the next one (if any) is the film needed now.</summary>
    private void PopDueSlot()
    {
        if (dueSlots.Count > 0) dueSlots.RemoveAt(0);
        if (dueSlots.Count > 0) SetDue(dueSlots[0]);
        else { dueLabel = ""; dueColor = ""; }
    }

    private void SetDue(int minute)
    {
        dueLabel = FilmLabelFor(minute);
        dueColor = FilmColorFor(minute);
    }

    /// <summary>
    /// SERVER. 17 Nov (Task 5): the film changed by itself after the second penalty. The zone's board
    /// still moves (else the board could never be finished), but nobody gets a task / money for it.
    /// </summary>
    public void ServerAutoFinish()
    {
        if (!needed) return;
        StopWork();
        pending = Mathf.Max(0, pending - 1);
        PopDueSlot();
        if (pending == 0) SetNeeded(false);
        else neededSince = Now;
        if (countsForZoneBoard) ZoneTaskList.ServerReportSale(kind, PopcornFlavor.None, -1, false);
        Debug.Log($"[ZoneChoreSpot] '{name}' finished by itself (missed twice).", this);
    }

    /// <summary>
    /// SERVER. 17 Nov: minutes after 00:00 of the NEXT time this chore becomes needed, or -1 (no more tonight).
    /// </summary>
    public int ServerNextSlotMinute =>
        nextSlotMinute < NightEndHour * 60 && (maxTimes == 0 || slotsMade < maxTimes) ? nextSlotMinute : -1;

    private void SetNeeded(bool value)
    {
        if (value && !needed) neededSince = Now;
        needed = value;
        ApplyLook(value); // the project's rule: hooks do not run on the machine that set the value
    }

    private void StopWork()
    {
        workerId = 0;
        offlineWorker = null;
    }

    private bool CanWork(GameObject player)
    {
        if (player == null) return false;
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        if (health != null && (health.IsDead || health.IsDowned)) return false;
        return (player.transform.position - transform.position).sqrMagnitude <= ServerRange * ServerRange;
    }

    private GameObject WorkerObject()
    {
        if (NetworkMode.IsOffline) return offlineWorker;
        return NetworkServer.spawned.TryGetValue(workerId, out NetworkIdentity identity) && identity != null
            ? identity.gameObject : null;
    }

    private static uint WorkerIdOf(GameObject player)
    {
        if (NetworkMode.IsOffline) return 1u;
        NetworkIdentity identity = player.GetComponent<NetworkIdentity>();
        return identity != null ? identity.netId : 0u;
    }

    /// <summary>Worker's PC -> server: "I let go of E". Only the current worker is listened to.</summary>
    [Command(requiresAuthority = false)]
    private void CmdStopWork(NetworkConnectionToClient sender = null)
    {
        if (sender != null && sender.identity != null && sender.identity.netId == workerId) StopWork();
    }

    // ---- Local (progress bar, letting go) ---------------------------------------

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
        KeepOutOfTheWay();
        if (!IsLocalWorker)
        {
            stopSent = false;
            ChoreProgressHUD.Hide(this);
            return;
        }

        ChoreProgressHUD.Show(this, actionText, (float)((Now - workStart) / holdSeconds));

        bool holding = Keybinds.IsPressed(GameAction.Interact) && !GameplayInput.Blocked; // 20 Oct: rebindable
        if (holding || stopSent) return;
        stopSent = true;
        if (NetworkMode.IsOffline) StopWork();
        else CmdStopWork();
    }

    // Read the existing replicated hold state so host/guests hear one loop each.
    private void LateUpdate()
    {
        bool ready = !NetworkMode.SessionEnding && (NetworkMode.IsOffline || (NetworkClient.active && isClient));
        bool cleaning = kind == ZoneTaskKind.CleanScreenRoom || kind == ZoneTaskKind.CleanToilet;
        bool playing = ready && RoundRunning && cleaning && needed && IsWorking;
        if (!playing)
        {
            StopSweepAudio();
            return;
        }
        if (sweepPlaying && sweepStartedAt == workStart) return;
        StopSweepAudio();

        var entry = AudioManager.Instance?.Find("Chore_Sweep");
        if (entry == null || entry.clips == null || entry.clips.Length == 0 || entry.clips[0] == null) return;
        AudioClip clip = entry.clips[0];
        if (clip.length <= 0) return;
        if (sweepSource == null)
        {
            var emitter = new GameObject("Sweep Sound");
            emitter.transform.SetParent(transform, false);
            sweepSource = emitter.AddComponent<AudioSource>();
            sweepSource.playOnAwake = false;
            sweepSource.loop = true;
            sweepSource.dopplerLevel = 0;
            sweepSource.rolloffMode = AudioRolloffMode.Linear;
            emitter.AddComponent<SoundCategoryVolume>();
        }
        sweepSource.clip = clip;
        sweepSource.spatialBlend = entry.spatial ? 1 : 0;
        sweepSource.minDistance = Mathf.Max(0.1f, entry.minDistance);
        sweepSource.maxDistance = Mathf.Max(sweepSource.minDistance + 0.1f, entry.maxDistance);
        var volume = sweepSource.GetComponent<SoundCategoryVolume>();
        volume.Category = entry.category;
        volume.SetBaseVolume(entry.volume);
        // Joining observers seek to the current stroke rather than replaying the start.
        sweepSource.time = (float)(System.Math.Max(0, Now - workStart) % clip.length);
        sweepSource.Play();
        sweepStartedAt = workStart;
        sweepPlaying = true;
    }

    private void StopSweepAudio()
    {
        if (sweepSource != null) sweepSource.Stop();
        sweepPlaying = false;
    }

    private void OnDisable()
    {
        All.Remove(this);
        ChoreProgressHUD.Hide(this);
        StopSweepAudio();
    }

    /// <summary>Editor tool (17 Nov): the item this chore needs.</summary>
    public void EditorSetNeedsItem(string itemId, string hint, bool usesUp, bool matchLabel = false)
    {
        needsItem = itemId ?? "";
        needsItemHint = hint ?? "";
        usesUpItem = usesUp;
        needsMatchingLabel = matchLabel;
    }

    /// <summary>Editor tool (17 Nov): the film for each change of the night, and its reel colour in words.</summary>
    public void EditorSetFilms(string[] names, string[] colors)
    {
        filmNames = names;
        filmColors = colors;
    }

    /// <summary>
    /// 16 Oct bug fix: a chore collider is only a LOOK target (the interaction ray
    /// ignores triggers, so it cannot be a trigger). It must never stop your own
    /// body walking (the film box once blocked the whole projector booth).
    /// Movement is owner-side, so ignoring your own body is enough. Unity forgets an
    /// ignore pair when a collider is turned off, so ApplyLook asks for it again.
    /// </summary>
    private void KeepOutOfTheWay()
    {
        PlayerHealth me = PlayerHealth.LocalInstance;
        if (me == ignoredBody || colliders == null) return;
        ignoredBody = me;
        if (me == null) return;
        foreach (Collider body in me.GetComponentsInChildren<Collider>(true))
            foreach (Collider own in colliders)
                if (body != null && own != null && own.enabled && body.enabled) Physics.IgnoreCollision(own, body, true);
    }

    // ---- Look ---------------------------------------------------------------------

    private void OnNeededChanged(bool oldValue, bool newValue) => ApplyLook(newValue);

    private void ApplyLook(bool on)
    {
        if (showWhenNeeded != null)
            foreach (GameObject go in showWhenNeeded)
                if (go != null && go.activeSelf != on) go.SetActive(on);
        if (colliders != null)
            foreach (Collider c in colliders)
                if (c != null) c.enabled = on; // nothing to look at when nothing is needed
        ignoredBody = null; // re-apply "do not block me" next frame (see KeepOutOfTheWay)
    }

    private static bool RoundRunning => MatchDirector.Instance == null || MatchDirector.Instance.RoundRunning;
}
