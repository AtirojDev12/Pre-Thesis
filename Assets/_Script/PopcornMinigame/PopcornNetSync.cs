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
public class PopcornNetSync : NetworkBehaviour
{
    public static PopcornNetSync Instance { get; private set; }

    [Header("Zone identity")]
    [Tooltip("Stable ID for this zone, reported to MatchDirector. Must be unique in the map.")]
    [SerializeField] private string zoneID = "zone_ticket_counter";

    [Header("Quest target")]
    [Tooltip("Correct orders needed before this zone counts as complete. A zone quest is a cumulative target — 'sell until the total is reached' — not a checklist.")]
    [Min(1)] [SerializeField] private int ordersToComplete = 5;

    [Header("Punishment")]
    [Tooltip("Damage for serving a ghost the wrong thing. The Ticket Zone rule table calls for HP and Sanity loss; sanity lands here once that system exists.")]
    [SerializeField] private float wrongGhostOrderDamage = 10f;

    // ---- Replicated shared state ------------------------------------------

    // Three separate hooks rather than three SyncVars pointing at one overloaded
    // method: Mirror's weaver resolves a hook by name, and giving it three
    // same-named candidates is a good way to get a confusing weave error.
    [SyncVar(hook = nameof(OnCustomerTypeChanged))] private PopcornCustomerType currentCustomerType;
    [SyncVar(hook = nameof(OnCurrentOrderChanged))] private PopcornFlavor currentOrder = PopcornFlavor.None;
    [SyncVar(hook = nameof(OnCustomerWaitingChanged))] private bool customerWaiting;

    /// <summary>Correct orders served by the whole team. The zone's quest progress.</summary>
    [SyncVar(hook = nameof(OnScoreChanged))] private int score;

    public PopcornCustomerType CurrentCustomerType => currentCustomerType;
    public PopcornFlavor CurrentOrder => currentOrder;
    public bool CustomerWaiting => customerWaiting;
    public int Score => score;
    public int OrdersToComplete => ordersToComplete;
    public string ZoneID => zoneID;
    public bool ZoneComplete => score >= ordersToComplete;

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

#if UNITY_EDITOR
    private void OnValidate()
    {
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
    }
#endif

    private void Start()
    {
        // Tell the match how many zones it owes. Registration is idempotent, so
        // it is safe whether this runs before or after MatchDirector.
        if (HasAuthority && MatchDirector.Instance != null)
            MatchDirector.Instance.ServerRegisterZone(zoneID);
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

        PopcornFlavor order = type == PopcornCustomerType.Ghost
            ? PopcornFlavor.Ghost
            : (Random.value < 0.5f ? PopcornFlavor.Cheese : PopcornFlavor.BBQ);

        currentCustomerType = type;
        currentOrder = order;
        customerWaiting = true;

        // Hooks do not fire on the machine that made the change, so the host
        // raises its own event or the host's screens would sit on stale text.
        OrderChanged?.Invoke();
    }

    /// <summary>SERVER ONLY. The counter is empty again.</summary>
    public void ServerClearCustomer()
    {
        if (!HasAuthority) return;

        customerWaiting = false;
        currentOrder = PopcornFlavor.None;
        OrderChanged?.Invoke();
    }

    // ---- Serving -----------------------------------------------------------

    /// <summary>
    /// Called by the LOCAL player when they press E on a waiting customer.
    /// Routes to the server, which is the only thing allowed to decide whether
    /// the order was right.
    /// </summary>
    public void RequestServe(PopcornFlavor heldFlavor)
    {
        if (NetworkMode.IsOffline)
        {
            ServerResolveServe(heldFlavor, PlayerHealth.LocalInstance, null);
            return;
        }

        CmdServe(heldFlavor);
    }

    [Command(requiresAuthority = false)]
    private void CmdServe(PopcornFlavor heldFlavor, NetworkConnectionToClient sender = null)
    {
        PlayerHealth health = sender != null && sender.identity != null
            ? sender.identity.GetComponent<PlayerHealth>()
            : null;

        ServerResolveServe(heldFlavor, health, sender);
    }

    /// <summary>
    /// SERVER ONLY. The whole decision lives here: was it right, who gets paid,
    /// who gets hurt, is the zone finished.
    ///
    /// Note it trusts the CLIENT for which flavour they were holding. That is
    /// the one soft spot left, and it is deliberate for the demo — the held
    /// item is per-player local state that the server does not model yet. If
    /// order-faking ever matters, the fix is a server-side inventory, not a
    /// check bolted on here.
    /// </summary>
    private void ServerResolveServe(PopcornFlavor heldFlavor, PlayerHealth health, NetworkConnectionToClient sender)
    {
        if (!HasAuthority) return;

        if (!customerWaiting)
        {
            SendResult(sender, false, "No one is waiting");
            return;
        }

        if (heldFlavor == PopcornFlavor.None)
        {
            SendResult(sender, false, "Make popcorn first");
            return;
        }

        bool correct = heldFlavor == currentOrder;

        if (correct)
        {
            score++;

            // Tasks pay the player who did them; zones decide survival. Both
            // counters live on MatchDirector and are deliberately separate.
            if (MatchDirector.Instance != null && health != null)
                MatchDirector.Instance.ServerReportTaskCompleted(health.netIdentity, zoneID);

            if (ZoneComplete && MatchDirector.Instance != null)
                MatchDirector.Instance.ServerReportZoneCompleted(zoneID);

            ScoreChanged?.Invoke(score, ordersToComplete);
            SendResult(sender, true, "Correct!  +1 Point");
        }
        else
        {
            // Ticket Zone rule 2: ghost food without the special ingredient
            // costs HP. Applied on the server so a client cannot decline it.
            if (currentCustomerType == PopcornCustomerType.Ghost && health != null)
                health.TakeDamage(wrongGhostOrderDamage);

            SendResult(sender, false, "Incorrect");
        }

        ServerClearCustomer();
    }

    private void SendResult(NetworkConnectionToClient target, bool correct, string message)
    {
        if (NetworkMode.IsOffline || target == null)
        {
            LocalServeResult?.Invoke(correct, message);
            return;
        }

        TargetServeResult(target, correct, message);
    }

    /// <summary>Feedback goes to the player who served, not to the whole team.</summary>
    [TargetRpc]
    private void TargetServeResult(NetworkConnectionToClient target, bool correct, string message)
    {
        LocalServeResult?.Invoke(correct, message);
    }

    // ---- Hooks (remote clients) --------------------------------------------

    private void OnCurrentOrderChanged(PopcornFlavor _, PopcornFlavor __) => OrderChanged?.Invoke();
    private void OnCustomerWaitingChanged(bool _, bool __) => OrderChanged?.Invoke();
    private void OnCustomerTypeChanged(PopcornCustomerType _, PopcornCustomerType __) => OrderChanged?.Invoke();

    private void OnScoreChanged(int _, int newScore) => ScoreChanged?.Invoke(newScore, ordersToComplete);
}
