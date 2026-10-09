using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// The server-authoritative spine under the popcorn minigame.
///
/// PUT THIS ON ITS OWN EMPTY GameObject — NOT on "Popcorn Minigame Systems".
///
/// Mirror force-disables every scene object that carries a NetworkIdentity at
/// load, and with no host running it is never switched back on. Put this on the
/// same object as PopcornMinigameBootstrap and the whole minigame goes dark:
/// no UI, no customers, no error. Give it its own object and the bootstrap
/// stays awake.
///
/// What happens then is exactly right in both modes:
///   OFFLINE — this object stays disabled, Instance is null, and
///             PopcornGameManager runs its local fallback path.
///   ONLINE  — the server enables it, Instance appears, and the networked path
///             takes over.
///
/// It also cannot be AddComponent'd at runtime the way the Bootstrap builds the
/// other pieces — Mirror assigns NetworkBehaviour component indices when an
/// object spawns, so a NetworkBehaviour added afterwards is never replicated.
///
/// WHY THE MINIGAME NEEDED THIS
/// ----------------------------
/// The minigame was written single-player and splits cleanly into two kinds of
/// state:
///
///   SHARED  — which customer is at the counter, what they ordered, the score.
///             One truth for the whole team. Lives here, as SyncVars.
///
///   LOCAL   — the popcorn in your own hands, your UI, your crosshair.
///             Already per-machine and correct as-is. Left alone.
///
/// Without this split, each client ran its own customer queue: six players at
/// one counter would each see a different customer wanting a different flavour,
/// six separate scores, and a modified client could award itself zone progress
/// — which now decides whether the team survives the night.
///
/// WHAT STAYS THE SAME
/// -------------------
/// PopcornGameManager keeps building and driving all the UI. It just stops
/// deciding outcomes: it asks here, the server answers, and the answer arrives
/// on every machine at once.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public partial class PopcornNetSync : NetworkBehaviour
{
    public static PopcornNetSync Instance { get; private set; }

    [Header("Zone identity")]
    [Tooltip("Stable ID for this zone, reported to MatchDirector. Must be unique in the map.")]
    [SerializeField] private string zoneID = "zone_ticket_counter";

    [Header("Quest target")]
    [Tooltip("Correct sales needed before this zone counts as complete — popcorn, drinks AND tickets together (TicketMinigame adds to this same total). A zone quest is a cumulative target — 'sell until the total is reached' — not a checklist.")]
    [Min(1)] [SerializeField] private int ordersToComplete = 5;

    [Header("Punishment")]
    [Tooltip("Damage for serving a ghost the wrong thing. The Ticket Zone rule table calls for HP and Sanity loss; sanity lands here once that system exists.")]
    [SerializeField] private float wrongGhostOrderDamage = 10f;
    [Header("Order size")]
    [Range(0f, 1f)] [SerializeField] private float twoItemOrderChance = 0.3f;

    // ---- Replicated shared state ------------------------------------------

    // One snapshot keeps order identity, products and partial progress consistent.
    [SyncVar(hook = nameof(OnOrderChanged))] private PopcornOrderState currentOrder;
    private uint nextOrderId;

    /// <summary>Correct orders served by the whole team. The zone's quest progress.</summary>
    [SyncVar(hook = nameof(OnScoreChanged))] private int score;

    public PopcornCustomerType CurrentCustomerType => currentOrder.customerType;
    public PopcornFlavor CurrentOrder => currentOrder.first;
    public PopcornOrderState Order => currentOrder;
    public bool CustomerWaiting => currentOrder.waiting;
    public int Score => score;
    public int OrdersToComplete => ordersToComplete;
    public string ZoneID => zoneID;
    public bool ZoneComplete => score >= ordersToComplete;

    [SyncVar] private GhostFavorState ghostFavor;
    public GhostFavorState GhostFavor => ghostFavor;

    public void SetGhostFavorState(GhostFavorState value)
    {
        if (HasAuthority) ghostFavor = value;
    }

    public void RequestGhostFavor(bool drop) => CmdGhostFavor(drop);

    [Command(requiresAuthority = false)]
    private void CmdGhostFavor(bool drop, NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null || GhostFavorRecovery.Instance == null) return;
        if (GhostFavorRecovery.Instance.ServerInteract(sender.identity.GetComponent<PlayerHealth>(), drop))
            TargetMixGhostFavor(sender);
    }

    [TargetRpc]
    private void TargetMixGhostFavor(NetworkConnectionToClient target)
    {
        if (GhostFavorRecovery.Instance != null) GhostFavorRecovery.Instance.MixLocally();
    }

    /// <summary>Raised on every machine when the order changes, so each client can redraw its own screens.</summary>
    public event System.Action OrderChanged;

    /// <summary>Raised on every machine when the score changes.</summary>
    public event System.Action<int, int> ScoreChanged;   // score, target

    /// <summary>
    /// Raised on the serving player's machine only, with the result of their
    /// own attempt — so feedback text appears for the person who pressed E and
    /// not for the five people who did not.
    /// </summary>
    public event System.Action<bool, string> LocalServeResult;   // correct, message

    private bool HasAuthority => NetworkMode.HasServerAuthority(this);

    // NOTE: the task stopwatch is NOT hooked here. It lives in
    // PopcornGameManager so it records in every scene, with or without this
    // component — hooking only the networked path measured nothing in a plain
    // test scene and failed silently.

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PopcornNetSync] A second instance is in this scene — destroying the duplicate.", this);
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Override rather than hide: Mirror's NetworkBehaviour.OnValidate is protected
    // virtual, and a private one here stopped Mirror's own validation from running.
    protected override void OnValidate()
    {
        base.OnValidate();

#if UNITY_EDITOR
        // The mistake this catches cost a playtest: sharing an object with the
        // bootstrap means Mirror disables the bootstrap too, and the minigame
        // silently never starts.
        if (GetComponent<PopcornMinigameBootstrap>() != null)
        {
            Debug.LogError(
                $"[PopcornNetSync] on '{name}' is sharing a GameObject with PopcornMinigameBootstrap. " +
                "Mirror disables scene objects that have a NetworkIdentity, so the whole minigame will be " +
                "switched off and no customers will ever appear. Move PopcornNetSync and its NetworkIdentity " +
                "to their own empty GameObject.", this);
        }
#endif
    }

    private void Start()
    {
        // Tell the match how many zones it owes. Registration is idempotent, so
        // it is safe whether this runs before or after MatchDirector.
        // With task boards (prototype loop) the boards are the zones instead.
        if (HasAuthority && MatchDirector.Instance != null && !ZoneTaskList.ExistsInScene())
            MatchDirector.Instance.ServerRegisterZone(zoneID);
    }

    // The same scene NetworkIdentity carries stock; no runtime network components
    // are added to the authored machine models or local hand visuals.
    [SyncVar] private int tankRemaining = 20;
    [SyncVar] private int tankCapacity = 20;
    [SyncVar] private int refillServings = 10;
    public int TankRemaining => tankRemaining;
    public int TankCapacity => tankCapacity;
    private struct SupplyWork
    {
        public int station, request;
        public double started;
        public uint actor;
    }
    private readonly Dictionary<NetworkConnectionToClient, SupplyWork> supplyWork = new Dictionary<NetworkConnectionToClient, SupplyWork>();
    private readonly HashSet<NetworkConnectionToClient> heldRefills = new HashSet<NetworkConnectionToClient>();

    public override void OnStartServer()
    {
        base.OnStartServer();
        PopcornPreparation preparation = PopcornPreparation.Instance;
        tankCapacity = preparation != null ? preparation.Capacity : 20;
        refillServings = preparation != null ? preparation.RefillServings : 10;
        tankRemaining = tankCapacity;
        supplyWork.Clear();
        heldRefills.Clear();
        ResetStationSounds();
    }

    public void RequestSupply(int station, int request, PopcornSupplyAction action) => CmdSupply(station, request, action);
    public void RequestDiscardRefill() => CmdDiscardRefill();

    [Command(requiresAuthority = false)]
    private void CmdDiscardRefill(NetworkConnectionToClient sender = null)
    {
        if (sender != null) heldRefills.Remove(sender);
    }

    private bool CanUseSupply(NetworkConnectionToClient sender, PopcornStation station)
    {
        if (sender == null || sender.identity == null || station == null || !station.CanInteract()) return false;
        PlayerHealth player = sender.identity.GetComponent<PlayerHealth>();
        if (player == null || player.IsDead || player.IsDowned) return false;
        float nearest = float.PositiveInfinity;
        foreach (Collider collider in station.GetComponentsInChildren<Collider>())
            if (collider.enabled && !collider.isTrigger)
                nearest = Mathf.Min(nearest, (collider.ClosestPoint(player.transform.position) - player.transform.position).sqrMagnitude);
        // Matches the interactor's 3 m reach plus 1.5 m network tolerance.
        return nearest <= 4.5f * 4.5f;
    }

    [Command(requiresAuthority = false)]
    private void CmdSupply(int stationId, int request, PopcornSupplyAction action, NetworkConnectionToClient sender = null)
    {
        if (sender == null) return;
        if (action == PopcornSupplyAction.Cancel)
        {
            if (supplyWork.TryGetValue(sender, out SupplyWork cancelled) && cancelled.request == request)
            {
                supplyWork.Remove(sender);
                EndStationSound(cancelled.actor);
            }
            return;
        }
        PopcornStation station = PopcornPreparation.Instance != null ? PopcornPreparation.Instance.StationAt(stationId) : null;
        bool valid = CanUseSupply(sender, station) &&
            (station.Kind == PopcornStationKind.Tank || station.Kind == PopcornStationKind.Maker || station.Kind == PopcornStationKind.Water);
        string message = "Cannot use this station · Move closer and try again";
        if (action == PopcornSupplyAction.Begin)
        {
            valid = valid && !heldRefills.Contains(sender) && (station.Kind == PopcornStationKind.Maker || tankRemaining > 0);
            // Water does not consume tank stock. All sound-producing holds need
            // the corresponding replicated held item, not just proximity.
            if (station != null && station.Kind == PopcornStationKind.Water)
                valid = CanUseSupply(sender, station) && !heldRefills.Contains(sender);
            valid = valid && CanHoldAtStation(sender, station) && AcceptNewSoundRequest(sender, request);
            if (valid)
            {
                if (supplyWork.TryGetValue(sender, out SupplyWork previous)) EndStationSound(previous.actor);
                var startedWork = new SupplyWork { station = stationId, request = request, started = NetworkTime.time, actor = sender.identity.netId };
                supplyWork[sender] = startedWork;
                BeginStationSound(startedWork.actor, stationId, request, startedWork.started);
                TargetSupplyBeginAccepted(sender, request);
            }
            else TargetSupplyBeginRejected(sender, request, tankRemaining == 0 && station != null && station.Kind == PopcornStationKind.Tank ? "Tank empty · Make NewPopcorn first" : message);
            return;
        }
        if (action == PopcornSupplyAction.Refill)
        {
            valid = valid && station.Kind == PopcornStationKind.Tank && heldRefills.Contains(sender) && tankRemaining < tankCapacity;
            if (valid)
            {
                tankRemaining = Mathf.Min(tankCapacity, tankRemaining + refillServings);
                heldRefills.Remove(sender);
                message = "Tank refilled";
            }
            else if (tankRemaining >= tankCapacity) message = "Tank full · Keep NewPopcorn for later";
            TargetSupplyResult(sender, request, valid, message);
            return;
        }
        if (action != PopcornSupplyAction.Complete) return;
        bool hasWork = supplyWork.TryGetValue(sender, out SupplyWork work) && work.station == stationId && work.request == request;
        valid = valid && hasWork && CanHoldAtStation(sender, station) && NetworkTime.time - work.started >= PopcornPreparation.PreparationSeconds - 0.05;
        // A completion is consumed exactly once, even if another player emptied
        // the tank first. Reliable ordered commands serialize simultaneous scoops.
        if (hasWork)
        {
            supplyWork.Remove(sender);
            EndStationSound(work.actor);
        }
        if (valid && station.Kind == PopcornStationKind.Tank)
        {
            valid = tankRemaining > 0 && !heldRefills.Contains(sender);
            if (valid) tankRemaining--;
            else message = "Tank empty · Make NewPopcorn first";
        }
        else if (valid && station.Kind == PopcornStationKind.Maker)
        {
            valid = heldRefills.Add(sender);
        }
        TargetSupplyResult(sender, request, valid, message);
    }

    [TargetRpc]
    private void TargetSupplyBeginAccepted(NetworkConnectionToClient target, int request)
    {
        if (PopcornPreparation.Instance != null) PopcornPreparation.Instance.AcceptSupplyBegin(request);
    }

    [TargetRpc]
    private void TargetSupplyBeginRejected(NetworkConnectionToClient target, int request, string message)
    {
        if (PopcornPreparation.Instance != null) PopcornPreparation.Instance.RejectSupplyBegin(request, message);
    }

    [TargetRpc]
    private void TargetSupplyResult(NetworkConnectionToClient target, int request, bool success, string message)
    {
        if (PopcornPreparation.Instance != null) PopcornPreparation.Instance.ReceiveSupplyResult(request, success, message);
    }

    // ---- Server: the customer at the counter -------------------------------

    /// <summary>
    /// SERVER ONLY. Called by PopcornGameManager when it decides a new customer
    /// should arrive. The server picks the order, so every machine shows the
    /// same one — and a client cannot read the answer early by generating it.
    /// </summary>
    public void ServerSeatCustomer()
    {
        if (!HasAuthority) return;

        PopcornCustomerType type = Random.value < 0.5f ? PopcornCustomerType.Human : PopcornCustomerType.Ghost;

        currentOrder = PopcornRecipe.RandomCustomerOrder(++nextOrderId, type, twoItemOrderChance);

        // Hooks do not fire on the machine that made the change, so the host
        // raises its own event or the host's screens would sit on stale text.
        OrderChanged?.Invoke();
    }

    /// <summary>SERVER ONLY. The counter is empty again.</summary>
    public void ServerClearCustomer()
    {
        if (!HasAuthority) return;

        PopcornOrderState order = currentOrder;
        order.waiting = false;
        currentOrder = order;
        OrderChanged?.Invoke();
    }

    // ---- Serving -----------------------------------------------------------

    /// <summary>
    /// Called by the LOCAL player when they press E on a waiting customer.
    /// Routes to the server, which is the only thing allowed to decide whether
    /// the order was right.
    /// </summary>
    public void RequestServe(uint orderId, uint heldRevision)
    {
        if (NetworkMode.IsOffline)
        {
            ServerResolveServe(orderId, heldRevision, PlayerHealth.LocalInstance, null);
            return;
        }

        CmdServe(orderId, heldRevision);
    }

    [Command(requiresAuthority = false)]
    private void CmdServe(uint orderId, uint heldRevision, NetworkConnectionToClient sender = null)
    {
        PlayerHealth health = sender != null && sender.identity != null
            ? sender.identity.GetComponent<PlayerHealth>()
            : null;

        ServerResolveServe(orderId, heldRevision, health, sender);
    }

    /// <summary>
    /// SERVER ONLY. The whole decision lives here: was it right, who gets paid,
    /// who gets hurt, is the zone finished.
    ///
    /// Uses the player's replicated task state and consumes that revision once.
    /// Preparation remains owner-driven; recipe state is validated on receipt.
    /// </summary>
    private void ServerResolveServe(uint orderId, uint heldRevision, PlayerHealth health, NetworkConnectionToClient sender)
    {
        if (!HasAuthority) return;

        if (!currentOrder.waiting || currentOrder.id != orderId)
        {
            SendResult(sender, false, "This order is no longer available", heldRevision, false);
            return;
        }
        PlayerHeldItems held = health != null ? health.GetComponent<PlayerHeldItems>() : null;
        if (health == null || health.IsDead || health.IsDowned || held == null ||
            held.TaskItem.revision != heldRevision || !held.TaskItem.IsReady)
        {
            SendResult(sender, false, "Fill a bucket or cup first", heldRevision, false);
            return;
        }
        CounterSlot counter = PopcornPreparation.Instance != null ? PopcornPreparation.Instance.GetComponent<CounterSlot>() : null;
        PopcornCustomer customer = counter != null ? counter.ActiveCustomer : null;
        if (customer == null || !customer.CanInteract() || (customer.transform.position - health.transform.position).sqrMagnitude > 4.5f * 4.5f)
        {
            SendResult(sender, false, "Move closer to the waiting customer", heldRevision, false);
            return;
        }
        PopcornFlavor heldFlavor = held.TaskItem.flavor;
        int entry = currentOrder.Match(heldFlavor, held.TaskItem.ghostMixed);
        bool correct = entry >= 0;
        if (!held.ServerConsume(heldRevision)) return;

        if (correct)
        {
            ServerAddZoneProgress(health);

            // Prototype loop: count it on the task boards (per flavor / water, human / ghost).
            ZoneTaskList.ServerReportSale(
                PopcornRecipe.IsDrink(heldFlavor) ? ZoneTaskKind.Water : ZoneTaskKind.Popcorn,
                heldFlavor, -1, currentOrder.customerType == PopcornCustomerType.Ghost);
            PopcornOrderState order = currentOrder;
            order.Accept(entry);
            currentOrder = order;
            OrderChanged?.Invoke();
            SendResult(sender, true, order.Complete ? "Order complete" : "Item delivered · More items needed", heldRevision, true);
            if (!order.Complete) return;
        }
        else
        {
            // Ticket Zone rule 2: ghost food without the special ingredient
            // costs HP. Applied on the server so a client cannot decline it.
            if (currentOrder.customerType == PopcornCustomerType.Ghost && health != null)
                health.TakeDamage(wrongGhostOrderDamage);

            SendResult(sender, false, "Incorrect", heldRevision, true);
        }

        // Sent after the seller's feedback, and only for a fully resolved order.
        if (NetworkMode.IsOffline) PlayOrderResult(correct);
        else RpcOrderResult(correct);
        ServerClearCustomer();
    }

    [ClientRpc]
    private void RpcOrderResult(bool correct) => PlayOrderResult(correct);

    private void PlayOrderResult(bool correct)
    {
        PopcornPreparation.Instance?.GetComponent<PopcornGameManager>()?.PlayOrderResult(correct);
    }

    /// <summary>
    /// SERVER ONLY. One correct sale anywhere in the Ticket Zone — popcorn,
    /// drinks OR tickets. The zone quest is one shared sales total ("sell until
    /// the total is reached"), so the ticket booth adds to this same counter
    /// instead of keeping its own. Pays the seller and, when the total is
    /// reached, reports the zone to MatchDirector exactly once (MatchDirector
    /// ignores a repeat).
    /// </summary>
    public void ServerAddZoneProgress(PlayerHealth seller)
    {
        if (!HasAuthority) return;

        score++;

        // Tasks pay the player who did them; zones decide survival. Both
        // counters live on MatchDirector and are deliberately separate.
        if (MatchDirector.Instance != null && seller != null)
            MatchDirector.Instance.ServerReportTaskCompleted(seller.netIdentity, zoneID);

        if (ZoneComplete && MatchDirector.Instance != null && !ZoneTaskList.ExistsInScene())
            MatchDirector.Instance.ServerReportZoneCompleted(zoneID);

        // Hooks do not fire on the machine that made the change.
        ScoreChanged?.Invoke(score, ordersToComplete);
    }

    private void SendResult(NetworkConnectionToClient target, bool correct, string message, uint revision, bool consumed)
    {
        if (NetworkMode.IsOffline || target == null)
        {
            FinishLocalServe(correct, message, revision, consumed);
            return;
        }

        TargetServeResult(target, correct, message, revision, consumed);
    }

    /// <summary>Feedback goes to the player who served, not to the whole team.</summary>
    [TargetRpc]
    private void TargetServeResult(NetworkConnectionToClient target, bool correct, string message, uint revision, bool consumed)
    {
        FinishLocalServe(correct, message, revision, consumed);
    }

    private void FinishLocalServe(bool correct, string message, uint revision, bool consumed)
    {
        if (PopcornPreparation.Instance != null) PopcornPreparation.Instance.Holder.FinishSubmission(revision, consumed);
        if (consumed && (!correct || message == "Order complete"))
            TaskTimer.Complete(zoneID, zoneID, PlayerHealth.LocalInstance != null ? PlayerHealth.LocalInstance.name : "player", correct);
        LocalServeResult?.Invoke(correct, message);
    }

    // ---- Hooks (remote clients) --------------------------------------------

    private void OnOrderChanged(PopcornOrderState _, PopcornOrderState __) => OrderChanged?.Invoke();

    private void OnScoreChanged(int _, int newScore) => ScoreChanged?.Invoke(newScore, ordersToComplete);
}
