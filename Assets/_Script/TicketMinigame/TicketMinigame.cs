using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum TicketCustomerStage { BetweenCustomers, WalkingIn, Waiting, WalkingOut }

public struct TicketCustomerState
{
    public TicketCustomerStage stage;
    public bool ghost;
    public Vector3 position;
    public Quaternion rotation;
    public int round;
    public int score;
    public bool hasMovie;
    public int movieIndex;
    public int requestedMovieIndex;
}

/// <summary>One customer at a time. Route points describe the centre of the capsule.</summary>
[DisallowMultipleComponent]
public sealed class TicketMinigame : MonoBehaviour
{
    [Header("Counter buttons")]
    [SerializeField] private TicketSellButton humanButton;
    [SerializeField] private TicketSellButton ghostButton;
    [SerializeField] private TicketNetSync networkSync;
    [Header("Movie selection")]
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
    [SerializeField, Min(0.1f)] private float walkSpeed = 2.2f;
    [SerializeField, Min(0)] private float delayBetweenCustomers = 2f;
    [SerializeField, Range(0, 1)] private float ghostChance = 0.5f;
    [SerializeField, Min(1)] private int pointsPerSale = 1;
    [SerializeField, Min(0)] private float wrongGhostDamage = 10f;
    [SerializeField, Min(0.1f)] private float serverInteractionDistance = 4.5f;
    [Header("Customer speech and score display")]
    [SerializeField] private string humanRequest = "One human ticket, please!";
    [SerializeField] private string ghostRequest = "One ghost ticket, please!";
    [SerializeField] private Vector3 bubbleOffset = new Vector3(0, 1.2f, 0);
    [SerializeField] private Transform scoreAnchor;

    private TicketCustomerState state;
    private float delay;
    private int waypoint;
    private GameObject customer;
    private Canvas bubble;
    private TMP_Text requestText;
    private TMP_Text scoreText;
    private int visualRound = -1;
    private int displayedScore = -1;
    private bool wasOnline;
    private Canvas movieCanvas;
    private TMP_Text movieSelectionText;
    private Button[] movieButtons;
    private WorldButtonInteractable[] movieHighlights;

    public TicketCustomerState State => state;
    public TicketCustomerState VisibleState => !NetworkMode.IsOffline && networkSync != null
        && networkSync.isClient ? networkSync.State : state;
    public bool CanChooseMovie => isActiveAndEnabled && VisibleState.stage == TicketCustomerStage.Waiting;
    public bool CanServe => CanChooseMovie && VisibleState.hasMovie;

    private void Awake()
    {
        if (humanButton == null || ghostButton == null || spawnPoint == null ||
            counterPoint == null || exitPoint == null || scoreAnchor == null || networkSync == null ||
            movieUiAnchor == null || movies == null || movies.Length == 0)
        {
            Debug.LogError("[Tickets] Assign the buttons, route points, score/movie UI anchors, movies and network sync.", this);
            enabled = false;
            return;
        }
        scoreText = CreateDisplay("Ticket Score", scoreAnchor, new Vector2(420, 100), out _);
        scoreText.text = "TICKETS: 0";
        delay = delayBetweenCustomers;
        BuildMoviePanel();
    }

    private void Update()
    {
        bool online = !NetworkMode.IsOffline;
        if (online != wasOnline)
        {
            state = default;
            delay = delayBetweenCustomers;
            wasOnline = online;
        }
        if (!online || (networkSync != null && networkSync.isServer)) Advance(Time.deltaTime);
        if (online && networkSync != null && networkSync.isServer) networkSync.Publish(state);
        DrawCustomer(VisibleState);
        RefreshMoviePanel();
    }

    private void Advance(float dt)
    {
        if (state.stage == TicketCustomerStage.Waiting) return;
        if (state.stage == TicketCustomerStage.BetweenCustomers)
        {
            delay -= dt;
            if (delay > 0) return;
            state.round++;
            state.hasMovie = false;
            state.ghost = Random.value < ghostChance;
            state.requestedMovieIndex = Random.Range(0, movies.Length);
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
        state.position = Vector3.MoveTowards(state.position, target.position, Mathf.Max(0.1f, walkSpeed) * dt);
        if ((state.position - target.position).sqrMagnitude > 0.000001f) return;
        if (waypoint < path.Length) { waypoint++; return; }
        state.stage = arriving ? TicketCustomerStage.Waiting : TicketCustomerStage.BetweenCustomers;
        if (arriving) state.rotation = counterPoint.rotation;
        else delay = delayBetweenCustomers;
    }

    public void RequestSale(bool ghostTicket, GameObject interactor)
    {
        if (!CanServe) return;
        if (NetworkMode.IsOffline)
            ResolveSale(ghostTicket, state.round, interactor != null ? interactor.GetComponent<PlayerHealth>() : null);
        else if (networkSync != null && networkSync.isClient)
            networkSync.RequestSale(ghostTicket, VisibleState.round);
    }

    public void RequestMovie(int movieIndex)
    {
        if (!CanChooseMovie) return;
        if (NetworkMode.IsOffline) ResolveMovie(movieIndex, state.round, PlayerHealth.LocalInstance);
        else if (networkSync != null && networkSync.isClient)
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
        if (networkSync != null && networkSync.isServer) networkSync.Publish(state);
    }

    private void BuildMoviePanel()
    {
        movieCanvas = UiFactory.CreateWorldCanvas("Movie Selection (World Space)", movieUiAnchor,
            movieUiAnchor, new Vector2(620, 850));
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
        if (ghostTicket == state.ghost && state.movieIndex == state.requestedMovieIndex)
            state.score += Mathf.Max(1, pointsPerSale);
        else if (state.ghost) player.TakeDamage(wrongGhostDamage);
        state.stage = TicketCustomerStage.WalkingOut;
        state.hasMovie = false;
        waypoint = 0;
        if (bubble != null) bubble.enabled = false;
        if (networkSync != null && networkSync.isServer) networkSync.Publish(state);
    }

    private void DrawCustomer(TicketCustomerState visible)
    {
        if (scoreText != null && displayedScore != visible.score)
        {
            displayedScore = visible.score;
            scoreText.text = $"TICKETS: {visible.score}";
        }
        if (visible.stage == TicketCustomerStage.BetweenCustomers)
        {
            if (customer != null) Destroy(customer);
            visualRound = -1;
            return;
        }
        if (customer == null || visualRound != visible.round)
        {
            if (customer != null) Destroy(customer);
            // Same primitive, proportions and colours as the popcorn CounterSlot.
            customer = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            customer.name = visible.ghost ? "Ghost Ticket Customer" : "Human Ticket Customer";
            customer.transform.localScale = new Vector3(0.75f, 1, 0.75f);
            customer.GetComponent<Collider>().enabled = false;
            Color color = visible.ghost ? new Color(0.35f, 0.95f, 1, 0.78f) : new Color(1, 0.68f, 0.25f, 1);
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            customer.GetComponent<Renderer>().SetPropertyBlock(properties);
            requestText = CreateDisplay("Ticket Request", customer.transform, new Vector2(440, 100), out bubble);
            requestText.text = (visible.ghost ? ghostRequest : humanRequest) +
                "\nMovie: " + movies[visible.requestedMovieIndex];
            bubble.transform.localScale = new Vector3(0.002f / 0.75f, 0.002f, 0.002f / 0.75f);
            bubble.transform.localPosition = bubbleOffset;
            visualRound = visible.round;
        }
        customer.transform.SetPositionAndRotation(visible.position, visible.rotation);
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
