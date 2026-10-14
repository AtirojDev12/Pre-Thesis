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

    [Header("Look")]
    [Tooltip("Shown only while the chore is needed (dirt mark, warning light).")]
    [SerializeField] private GameObject[] showWhenNeeded;

    private const float ServerRange = 4.5f; // interaction range 3 m + slack
    private const Key HoldKey = Key.E;
    private const int NightEndHour = 6;

    // Server writes, everyone reads.
    [SyncVar(hook = nameof(OnNeededChanged))] private bool needed;
    [SyncVar] private uint workerId;
    [SyncVar] private double workStart;

    // Server only.
    private int pending;
    private int nextSlotHour;
    private GameObject offlineWorker;

    // Local only.
    private string promptText;
    private bool stopSent;
    private Collider[] colliders;

    public ZoneTaskKind Kind => kind;
    public bool Needed => needed;

    private static double Now => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;

    protected override void Awake()
    {
        base.Awake();
        promptText = "[Hold E] " + actionText;
        colliders = GetComponents<Collider>();
        nextSlotHour = fromHour;
        ApplyLook(needed);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyLook(needed);
    }

    // ---- Prompt / interaction ------------------------------------------------

    public override string GetInteractionPrompt() => promptText;

    public override bool CanInteract() =>
        needed && RoundRunning && (workerId == 0 || IsLocalWorker);

    /// <summary>SERVER: E pressed on this chore. Starts the hold timer for that player.</summary>
    protected override void OnInteracted(GameObject interactor)
    {
        if (!needed || interactor == null || !CanWork(interactor)) return;
        uint id = WorkerIdOf(interactor);
        if (workerId != 0 && workerId != id) return; // someone else is already on it
        offlineWorker = NetworkMode.IsOffline ? interactor : null;
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
        match.GetInGameTime(out int hour, out _);
        while (nextSlotHour < NightEndHour && hour >= nextSlotHour)
        {
            pending++;
            nextSlotHour = repeatEveryHours > 0 ? nextSlotHour + repeatEveryHours : NightEndHour;
        }
        if (!needed && pending > 0) SetNeeded(true);

        if (workerId == 0) return;
        GameObject worker = WorkerObject();
        if (worker == null || !needed || !CanWork(worker))
        {
            StopWork();
            return;
        }
        if (Now - workStart >= holdSeconds) ServerComplete(worker);
    }

    private void ServerComplete(GameObject worker)
    {
        StopWork();
        pending = Mathf.Max(0, pending - 1);
        if (pending == 0) SetNeeded(false);

        ZoneTaskList.ServerReportSale(kind, PopcornFlavor.None, -1, false);
        if (MatchDirector.Instance != null)
            MatchDirector.Instance.ServerReportTaskCompleted(worker.GetComponent<NetworkIdentity>(), zoneID);
        Debug.Log($"[ZoneChoreSpot] '{name}' done by {worker.name} ({kind}).", this);
    }

    private void SetNeeded(bool value)
    {
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
        if (!IsLocalWorker)
        {
            stopSent = false;
            ChoreProgressHUD.Hide(this);
            return;
        }

        ChoreProgressHUD.Show(this, actionText, (float)((Now - workStart) / holdSeconds));

        bool holding = Keyboard.current != null && Keyboard.current[HoldKey].isPressed && !GameplayInput.Blocked;
        if (holding || stopSent) return;
        stopSent = true;
        if (NetworkMode.IsOffline) StopWork();
        else CmdStopWork();
    }

    private void OnDisable() => ChoreProgressHUD.Hide(this);

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
    }

    private static bool RoundRunning => MatchDirector.Instance == null || MatchDirector.Instance.RoundRunning;
}
