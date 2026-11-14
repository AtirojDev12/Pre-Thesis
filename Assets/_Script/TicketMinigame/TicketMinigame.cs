using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public enum TicketCustomerStage { BetweenCustomers, WalkingIn, Waiting, WalkingOut }

public struct TicketCustomerState
{
    public TicketCustomerStage stage;
    public bool ghost;
    public Vector3 position;
    public Quaternion rotation;
    public int round;
    public bool hasMovie;
    public int movieIndex;
    public int requestedMovieIndex;
}

/// <summary>
/// One customer at a time. Route points describe the centre of the capsule (about 1 m
/// above the floor). Human customers are drawn with an NPC model (CustomerModelVisual
/// keeps its feet on the floor); ghost customers are still drawn as a capsule.
/// </summary>
[DisallowMultipleComponent]
public sealed class TicketMinigame : MonoBehaviour
{
    [Header("Counter buttons")]
    [SerializeField] private TicketSellButton humanButton;
    [SerializeField] private TicketSellButton ghostButton;
    [SerializeField] private TicketNetSync networkSync;
    [Header("Movie selection")]
    [Tooltip("Centre of the movie selection panel. The panel faces the player standing on this anchor's -Z side " +
             "(blue arrow = the way the player looks). The cyan frame in the Scene view shows its exact in-game size.")]
    [SerializeField] private Transform movieUiAnchor;
    [Tooltip("Both humans and ghosts randomly request one of these movies.")]
    [SerializeField] private string[] movies = { "Movie 1", "Movie 2", "Movie 3" };
    [Header("Scene-editable route (customer centre, about 1m above the floor)")]
    [SerializeField] private Transform spawnPoint;
    [Tooltip("Optional points visited in order before reaching the counter.")]
    [SerializeField] private Transform[] approachPath = new Transform[0];
    [Tooltip("Position AND facing direction while waiting at the counter.")]
    [SerializeField] private Transform counterPoint;
    [SerializeField] private Transform[] departurePath = new Transform[0];
    [SerializeField] private Transform exitPoint;
    [Header("Gameplay")]
    [FormerlySerializedAs("walkSpeed")]
    [SerializeField, Min(0.1f)] private float customerWalkSpeed = 2.2f;
    [SerializeField, Min(0)] private float delayBetweenCustomers = 2f;
    [SerializeField, Range(0, 1)] private float ghostChance = 0.5f;
    private readonly QuestOrderDirector orderDirector = new QuestOrderDirector(true);
    [SerializeField, Min(0)] private float wrongGhostDamage = 10f;
    [SerializeField, Min(0.1f)] private float serverInteractionDistance = 4.5f;
    [Header("Customer speech")]
    [SerializeField] private string humanRequest = "One human ticket, please!";
    [SerializeField] private string ghostRequest = "One ghost ticket, please!";
    [SerializeField] private Vector3 bubbleOffset = new Vector3(0, 1.2f, 0);
    [Header("Zone quest")]
    [Tooltip("The Ticket Zone's shared sales total. Tickets, popcorn and drinks all count toward ONE target, so a correct ticket sale adds to the popcorn counter's score. Leave empty to use the one in the scene (PopcornNetSync.Instance).")]
    [SerializeField] private PopcornNetSync zoneCounter;

    // Used only when no PopcornNetSync exists (offline sandbox without the popcorn stand).
    private const string FallbackZoneID = "zone_ticket_counter";
    // Zone name used for the payout log when task boards are in use.
    private const string TicketZoneID = "zone_ticket";

    private TicketCustomerState state;
    private float delay;
    private int waypoint;
    private GameObject customer;
    private CustomerModelVisual customerModel;
    private Canvas bubble;
    private TMP_Text requestText;
    private int visualRound = -1;
    private bool wasOnline;
    private Canvas movieCanvas;
    private TMP_Text movieSelectionText;
    private Button[] movieButtons;
    private WorldButtonInteractable[] movieHighlights;

    public TicketCustomerState State => state;
    /// <summary>Server/offline only, once per accepted sale, with the actual seller.</summary>
    public event System.Action<PlayerHealth, bool> SaleResolved;
    public Transform GhostFavorDestination => counterPoint;
    private PopcornNetSync ZoneCounter => zoneCounter != null ? zoneCounter : PopcornNetSync.Instance;
    private string ZoneID => ZoneCounter != null ? ZoneCounter.ZoneID : FallbackZoneID;
    // Separate key from the popcorn stand's, so the two open tasks never overwrite each other.
    private string TimerKey => ZoneID + "/tickets";
    public TicketCustomerState VisibleState => !NetworkMode.IsOffline && networkSync != null
        && networkSync.IsClientReady ? networkSync.State : state;
    public bool CanChooseMovie => isActiveAndEnabled && VisibleState.stage == TicketCustomerStage.Waiting;
    public bool CanServe => CanChooseMovie && VisibleState.hasMovie;

    private void Awake()
    {
        if (humanButton == null || ghostButton == null || spawnPoint == null ||
            counterPoint == null || exitPoint == null || networkSync == null ||
            movieUiAnchor == null || movies == null || movies.Length == 0)
        {
            Debug.LogError("[Tickets] Assign the buttons, route points, movie UI anchor, movies and network sync.", this);
            enabled = false;
            return;
        }
        delay = delayBetweenCustomers;
        BuildMoviePanel();
    }

    private void Update()
    {
        bool online = !NetworkMode.IsOffline;
        if (online != wasOnline)
        {
            state = default;
            orderDirector.Reset();
            delay = delayBetweenCustomers;
            wasOnline = online;
        }
        if (!online || (networkSync != null && networkSync.IsServerReady)) Advance(Time.deltaTime);
        if (online && networkSync != null && networkSync.IsServerReady) networkSync.Publish(state);
        DrawCustomer(VisibleState);
        RefreshMoviePanel();
    }

    private void Advance(float dt)
    {
        if (state.stage == TicketCustomerStage.Waiting) return;
        if (state.stage == TicketCustomerStage.BetweenCustomers)
        {
            if (GhostCounterGate.TicketHeld) return; // 31 Oct: a ghost holds the counter (MissingPartsGhost)
            delay -= dt;
            if (delay > 0) return;
            state.round++;
            state.hasMovie = false;
            state.ghost = Random.value < ghostChance;
            state.requestedMovieIndex = Random.Range(0, movies.Length);
            orderDirector.NextTicket(movies.Length, ref state.requestedMovieIndex, ref state.ghost);
            state.position = spawnPoint.position;
            state.rotation = spawnPoint.rotation;
            state.stage = TicketCustomerStage.WalkingIn;
            waypoint = 0;
            return;
        }
        bool arriving = state.stage == TicketCustomerStage.WalkingIn;
        Transform[] path = arriving ? approachPath : departurePath;
        while (waypoint < path.Length && path[waypoint] == null) waypoint++;
        Transform target = waypoint < path.Length ? path[waypoint] : arriving ? counterPoint : exitPoint;
        Vector3 direction = target.position - state.position;
        direction.y = 0;
        if (direction.sqrMagnitude > 0.001f) state.rotation = Quaternion.LookRotation(direction);
        state.position = Vector3.MoveTowards(state.position, target.position, Mathf.Max(0.1f, customerWalkSpeed) * dt);
        if ((state.position - target.position).sqrMagnitude > 0.000001f) return;
        if (waypoint < path.Length) { waypoint++; return; }
        state.stage = arriving ? TicketCustomerStage.Waiting : TicketCustomerStage.BetweenCustomers;
        if (arriving)
        {
            state.rotation = counterPoint.rotation;
            // Server/offline only (Advance never runs on a remote client), same as Complete below.
            TaskTimer.Begin(TimerKey);
        }
        else delay = delayBetweenCustomers;
    }

    public void RequestSale(bool ghostTicket, GameObject interactor)
    {
        if (!CanServe) return;
        if (NetworkMode.IsOffline)
            ResolveSale(ghostTicket, state.round, interactor != null ? interactor.GetComponent<PlayerHealth>() : null);
        else if (networkSync != null && networkSync.IsClientReady)
            networkSync.RequestSale(ghostTicket, VisibleState.round);
    }

    public void RequestMovie(int movieIndex)
    {
        if (!CanChooseMovie) return;
        if (NetworkMode.IsOffline) ResolveMovie(movieIndex, state.round, PlayerHealth.LocalInstance);
        else if (networkSync != null && networkSync.IsClientReady)
            networkSync.RequestMovie(movieIndex, VisibleState.round);
        RefreshMoviePanel();
    }

    public void ResolveMovie(int movieIndex, int round, PlayerHealth player)
    {
        if (!isActiveAndEnabled || (!NetworkMode.IsOffline && !NetworkServer.active) ||
            state.stage != TicketCustomerStage.Waiting || state.round != round ||
            player == null || player.IsDead || player.IsDowned ||
            movieIndex < 0 || movieIndex >= movies.Length ||
            Vector3.Distance(player.transform.position, movieUiAnchor.position) > serverInteractionDistance) return;
        state.movieIndex = movieIndex;
        state.hasMovie = true;
        NetworkAudioRelay.Play("Ticket_MovieClick", movieUiAnchor.position);
        if (networkSync != null && networkSync.IsServerReady) networkSync.Publish(state);
    }

    /// <summary>
    /// Server/offline only. A correct ticket counts toward the Ticket Zone's shared
    /// total (pays the seller + may finish the zone). Without a counter in the
    /// scene it still pays the seller through MatchDirector.
    /// </summary>
    private void ReportSale(PlayerHealth seller)
    {
        // Prototype loop: the Ticket Zone has its OWN task board now (not the
        // popcorn counter's shared total). Count the ticket there and pay the seller.
        if (ZoneTaskList.ExistsInScene())
        {
            ZoneTaskList.ServerReportSale(ZoneTaskKind.Ticket, PopcornFlavor.None, state.movieIndex, state.ghost);
            if (MatchDirector.Instance != null && seller != null)
                MatchDirector.Instance.ServerReportTaskCompleted(seller.netIdentity, TicketZoneID);
            return;
        }

        PopcornNetSync counter = ZoneCounter;
        if (counter != null)
        {
            counter.ServerAddZoneProgress(seller);
            return;
        }
        if (MatchDirector.Instance != null && seller != null)
            MatchDirector.Instance.ServerReportTaskCompleted(seller.netIdentity, FallbackZoneID);
    }

    private void BuildMoviePanel()
    {
        movieCanvas = UiFactory.CreateWorldCanvas("Movie Selection (World Space)", movieUiAnchor,
            movieUiAnchor, MoviePanelPixels);
        var panel = UiFactory.CreatePanel("Movie Controls", movieCanvas.transform, new Color(0.035f, 0.06f, 0.1f, 0.97f));
        UiFactory.Stretch(panel.GetComponent<RectTransform>());
        var title = UiFactory.CreateText("Title", panel.transform, "SELECT MOVIE", 46, new Color(1f, 0.8f, 0.22f));
        title.alignment = TextAlignmentOptions.Center;
        UiFactory.SetRect(title.rectTransform, new Vector2(0.05f, 0.87f), new Vector2(0.95f, 0.98f));
        movieSelectionText = UiFactory.CreateText("Selection", panel.transform, "", 30, Color.white);
        movieSelectionText.alignment = TextAlignmentOptions.Center;
        UiFactory.SetRect(movieSelectionText.rectTransform, new Vector2(0.05f, 0.74f), new Vector2(0.95f, 0.87f));
        movieButtons = new Button[movies.Length];
        movieHighlights = new WorldButtonInteractable[movies.Length];
        float row = 0.68f / movies.Length;
        for (int i = 0; i < movies.Length; i++)
        {
            int index = i;
            var button = UiFactory.CreateButton("Movie " + i, panel.transform, movies[i], new Color(0.14f, 0.35f, 0.55f));
            UiFactory.SetRect(button.GetComponent<RectTransform>(), new Vector2(0.08f, 0.71f - row * (i + 1)),
                new Vector2(0.92f, 0.71f - row * i - 0.025f));
            button.onClick.AddListener(() => RequestMovie(index));
            movieButtons[i] = button;
            movieHighlights[i] = WorldButtonInteractable.Attach(button, "Select " + movies[i]);
        }
        RefreshMoviePanel();
    }

    private void RefreshMoviePanel()
    {
        if (movieCanvas == null) return;
        PlayerHealth player = PlayerHealth.LocalInstance;
        movieCanvas.worldCamera = player != null ? player.GetComponentInChildren<Camera>() : null;
        TicketCustomerState visible = VisibleState;
        movieSelectionText.text = !CanChooseMovie ? "WAITING FOR CUSTOMER" : visible.hasMovie
            ? "SELECTED: " + movies[visible.movieIndex] + "\nSELL A TICKET" : "CHOOSE A MOVIE FIRST";
        for (int i = 0; i < movieButtons.Length; i++)
        {
            movieButtons[i].interactable = CanChooseMovie;
            movieHighlights[i].SetSelected(CanChooseMovie && visible.hasMovie && visible.movieIndex == i);
        }
    }

    // Called only offline or by the server; the round token rejects stale/double submissions.
    public void ResolveSale(bool ghostTicket, int round, PlayerHealth player)
    {
        if (!isActiveAndEnabled || (!NetworkMode.IsOffline && !NetworkServer.active) ||
            state.stage != TicketCustomerStage.Waiting || state.round != round || !state.hasMovie ||
            player == null || player.IsDead || player.IsDowned) return;
        Transform button = ghostTicket ? ghostButton.transform : humanButton.transform;
        if (Vector3.Distance(player.transform.position, button.position) > serverInteractionDistance) return;
        // Both ticket types click on accepted submission, whether the order is correct or not.
        NetworkAudioRelay.Play("Ticket_SubmitClick", button.position);
        bool correct = ghostTicket == state.ghost && state.movieIndex == state.requestedMovieIndex;
        TaskTimer.Complete(TimerKey, ZoneID, player.name, correct);
        if (correct)
        {
            orderDirector.RecordSale(ZoneTaskKind.Ticket, PopcornFlavor.None, state.movieIndex, state.ghost);
            ReportSale(player);
        }
        orderDirector.ResolveCustomer(correct);
        state.stage = TicketCustomerStage.WalkingOut;
        state.hasMovie = false;
        waypoint = 0;
        if (bubble != null) bubble.enabled = false;
        if (networkSync != null && networkSync.IsServerReady) networkSync.Publish(state);
        // Consume the round first; duplicate requests must not punish twice.
        NetworkAudioRelay.Play(correct ? "CompleteOrder1" : "failorder2", movieUiAnchor.position);
        // Notify before the customer hit so its health cooldown cannot block
        // the ghost's threshold attack in the same sale.
        SaleResolved?.Invoke(player, correct);
        if (!correct && state.ghost) player.TakeDamage(wrongGhostDamage);
    }

    private void DrawCustomer(TicketCustomerState visible)
    {
        if (visible.stage == TicketCustomerStage.BetweenCustomers)
        {
            if (customer != null) Destroy(customer);
            visualRound = -1;
            return;
        }
        if (customer == null || visualRound != visible.round)
        {
            if (customer != null) Destroy(customer);
            customerModel = null;
            if (!visible.ghost)
            {
                // Human: NPC model picked from the synced round, so every client sees the same one.
                // The bubble keeps the same gap above the head as it had above the capsule top.
                float gapAboveHead = Mathf.Max(0.1f, bubbleOffset.y - 1f);
                customerModel = CustomerModelVisual.TryCreate("Human Ticket Customer", visible.position,
                    visible.rotation, (uint)visible.round, visible.stage != TicketCustomerStage.Waiting,
                    gapAboveHead, false);
            }
            if (customerModel != null)
            {
                customer = customerModel.gameObject;
                requestText = CreateDisplay("Ticket Request", customerModel.LabelAnchor, new Vector2(440, 100), out bubble);
                bubble.transform.localPosition = Vector3.zero;
            }
            else
            {
                // Ghost (or no model configured): same primitive, proportions and colours as the popcorn CounterSlot.
                customer = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                customer.name = visible.ghost ? "Ghost Ticket Customer" : "Human Ticket Customer";
                customer.transform.localScale = new Vector3(0.75f, 1, 0.75f);
                customer.GetComponent<Collider>().enabled = false;
                CustomerAppearance.Apply(customer.GetComponent<Renderer>(), visible.ghost);
                requestText = CreateDisplay("Ticket Request", customer.transform, new Vector2(440, 100), out bubble);
                bubble.transform.localScale = new Vector3(0.002f / 0.75f, 0.002f, 0.002f / 0.75f);
                bubble.transform.localPosition = bubbleOffset;
            }
            requestText.text = (visible.ghost ? ghostRequest : humanRequest) +
                "\nMovie: " + movies[visible.requestedMovieIndex];
            visualRound = visible.round;
        }
        customer.transform.SetPositionAndRotation(visible.position, visible.rotation);
        if (customerModel != null) customerModel.SetWalking(visible.stage != TicketCustomerStage.Waiting);
        bubble.enabled = visible.stage == TicketCustomerStage.Waiting;
    }

    private void LateUpdate()
    {
        if (bubble == null || !bubble.enabled) return;
        PlayerHealth player = PlayerHealth.LocalInstance;
        Camera camera = player != null ? player.GetComponentInChildren<Camera>() : Camera.main;
        if (camera != null) bubble.transform.rotation = camera.transform.rotation;
    }

    private static TMP_Text CreateDisplay(string name, Transform parent, Vector2 size, out Canvas canvas)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * 0.002f;
        go.GetComponent<RectTransform>().sizeDelta = size;
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var panel = UiFactory.CreatePanel("Bubble", go.transform, new Color(0.025f, 0.04f, 0.05f, 0.95f));
        UiFactory.Stretch(panel.GetComponent<RectTransform>());
        TMP_Text text = UiFactory.CreateText("Text", panel.transform, "", 34, Color.white);
        text.alignment = TextAlignmentOptions.Center;
        UiFactory.Stretch(text.rectTransform);
        return text;
    }

    private void OnDisable()
    {
        if (customer != null) Destroy(customer);
        visualRound = -1;
    }

    // Same size the runtime panel is built with (UiFactory: 1 px = 1 mm, independent of the anchor's scale).
    private static readonly Vector2 MoviePanelPixels = new Vector2(620, 850);

    /// <summary>
    /// Always-visible frame of the movie panel, so the anchor can be placed in the Scene
    /// view without entering Play Mode. Turn on Gizmos in the Scene view to see it.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (movieUiAnchor == null) return;
        Vector2 size = MoviePanelPixels * UiFactory.WorldUnitsPerPixel;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(movieUiAnchor.position, movieUiAnchor.rotation, Vector3.one);

        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.18f);
        Gizmos.DrawCube(Vector3.zero, new Vector3(size.x, size.y, 0.002f));
        Gizmos.color = new Color(0.2f, 0.85f, 1f, 1f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0f));
        // Inner frame offset 1 cm so the border reads clearly at any zoom.
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x - 0.02f, size.y - 0.02f, 0f));
        // Title strip (top 11% of the panel holds "SELECT MOVIE").
        float titleY = size.y * (0.87f - 0.5f);
        Gizmos.DrawLine(new Vector3(-size.x * 0.5f, titleY, 0f), new Vector3(size.x * 0.5f, titleY, 0f));
        // Viewing direction: the player stands on the -Z side and looks along +Z.
        Gizmos.color = new Color(0.25f, 0.45f, 1f, 1f);
        Vector3 eye = new Vector3(0f, 0f, -0.35f);
        Gizmos.DrawLine(eye, Vector3.zero);
        Gizmos.DrawLine(Vector3.zero, new Vector3(0.05f, 0f, -0.06f));
        Gizmos.DrawLine(Vector3.zero, new Vector3(-0.05f, 0f, -0.06f));
        Gizmos.DrawWireSphere(eye, 0.025f);

        Gizmos.matrix = previous;
#if UNITY_EDITOR
        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(movieUiAnchor.position + movieUiAnchor.up * (size.y * 0.5f + 0.06f),
            $"Movie Selection UI ({size.x:0.00} x {size.y:0.00} m)");
#endif
    }

    private void OnDrawGizmosSelected()
    {
        DrawPath(spawnPoint, approachPath, counterPoint, Color.cyan);
        DrawPath(counterPoint, departurePath, exitPoint, Color.yellow);
        if (counterPoint != null) Gizmos.DrawRay(counterPoint.position, counterPoint.forward);
    }

    private static void DrawPath(Transform start, Transform[] path, Transform end, Color color)
    {
        Gizmos.color = color;
        Transform previous = start;
        if (start != null) Gizmos.DrawWireSphere(start.position, 0.2f);
        foreach (Transform point in path)
        {
            if (point == null) continue;
            if (previous != null) Gizmos.DrawLine(previous.position, point.position);
            Gizmos.DrawWireSphere(point.position, 0.2f);
            previous = point;
        }
        if (end == null) return;
        if (previous != null) Gizmos.DrawLine(previous.position, end.position);
        Gizmos.DrawWireSphere(end.position, 0.2f);
    }
}
