using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 23 Nov 2026 (Mr.k): MAP RULE 3 - the VIP guest.
///   At 3 AM a VIP guest arrives (at the entrance) and the night clock STOPS.
///   Press E on the VIP: it follows you. Bring it to its seat in the ghost screen room
///   (for now; a special screen room later). Then crank the hand projector there
///   (press E fast) until the film is done. The clock runs again.
///   Not done in time (real seconds, MapRuleTuning.vipSeconds): everyone gets a jumpscare
///   and loses half of their CURRENT sanity (decimals rounded down). The clock runs again.
///   Solo: the same player may escort and crank.
/// When it is over (served or failed) Map Rules 2 and 1 start (<see cref="Finished"/>).
/// Walker role: "VIP". Seat + entrance + projector: placed by the setup tool, move them freely.
/// </summary>
public sealed class MapRuleVip : MapEventBase
{
    public const string Role = "VIP";

    public enum Stage : byte { Waiting = 0, Arrived = 1, Seated = 2, Served = 3, Failed = 4 }

    [Tooltip("Where the VIP appears (empty = the first exit door).")]
    [SerializeField] private Transform entrance;
    [Tooltip("Where the VIP sits (ghost screen room for now).")]
    [SerializeField] private Transform seat;

    [SyncVar] private Stage stage;
    [SyncVar] private double deadline;
    [SyncVar] private int cranks;
    [SyncVar] private bool filmIn; // 23 Nov: the VIP film is in the crank projector

    /// <summary>23 Nov: what the VIP film reel asks for (CarryProp carry id).</summary>
    public const string FilmId = ItemCatalog.FilmReelVip;

    public static MapRuleVip Instance { get; private set; }
    public Stage Current => stage;
    public int Cranks => cranks;
    public bool FilmIn => filmIn;
    /// <summary>Map Rule 3 is over (served or failed): Map Rules 2 and 1 run.</summary>
    public bool Finished => stage == Stage.Served || stage == Stage.Failed;
    /// <summary>The VIP is waiting for someone, following, or seated (the clock is stopped).</summary>
    public bool Running => stage == Stage.Arrived || stage == Stage.Seated;
    public float SecondsLeft => Running ? Mathf.Max(0f, (float)(deadline - Now)) : 0f;
    public Transform Seat => seat;

    private MapWalker vip;
    private PlayerHealth escort;
    private int seatIndex = -1;
    private bool seatedDown;
    private float sitDeadline;
    private MatchDirector startedFor;
    private readonly List<PlayerHealth> players = new List<PlayerHealth>();

    // Client: the timer line on screen.
    private GameObject timerRoot;
    private TMP_Text timerText;

    private void Awake() => Instance = this;
    private void OnDestroy() { if (Instance == this) Instance = null; if (timerRoot != null) Destroy(timerRoot); }

    private void Update()
    {
        if (IsServerSide) ServerTick();
        LocalTimer();
    }

    // ---- Server ------------------------------------------------------------------------------

    private void ServerTick()
    {
        MatchDirector match = MatchDirector.Instance;
        if (match != null && !ReferenceEquals(match, startedFor) && match.Phase == MatchPhase.PreShift) ResetForNewMatch(match);

        if (stage == Stage.Waiting)
        {
            if (NightRunning && NightMinutes >= T.mapRulesHour * 60) Arrive();
            return;
        }
        if (!Running) return;

        if (match == null || !match.RoundRunning || match.Phase != MatchPhase.Night) { EndRun(false, true); return; }
        if (Now >= deadline) { Fail(); return; }

        if (stage == Stage.Seated && vip != null && !seatedDown && (vip.ServerArrived(0.4f) || Time.time >= sitDeadline)) TakeSeat();

        if (stage == Stage.Arrived && vip != null)
        {
            if (escort != null && (!PlayerRegistry.IsHuntable(escort) || escort.IsDowned)) DropEscort();
            if (escort != null && seat != null && Flat(vip.transform.position, seat.position) <= T.vipSeatRadius) SitDown();
        }
    }

    private void ResetForNewMatch(MatchDirector match)
    {
        startedFor = match;
        if (vip != null) vip.ServerHide();
        vip = null;
        escort = null;
        stage = Stage.Waiting;
        cranks = 0;
    }

    private void Arrive()
    {
        startedFor = MatchDirector.Instance;
        stage = Stage.Arrived;
        cranks = 0;
        filmIn = false;
        deadline = Now + T.vipSeconds;
        if (MatchDirector.Instance != null) MatchDirector.Instance.ServerSetClockPaused(true);
        vip = MapWalker.ServerTake(Role);
        if (vip != null)
        {
            Vector3 at = entrance != null ? entrance.position : DefaultEntrance();
            vip.ServerAppear(at, MapWalker.Body.Vip); // 23 Nov: VIP ghost mockup (not an NPC ghost)
            vip.ServerSetSpeed(T.vipSpeed);
            vip.ServerSetPress(T.vipPrompt);
            vip.ServerInteracted -= OnVipPressed;
            vip.ServerInteracted += OnVipPressed;
        }
        ServerShowAll(false, T.vipArriveLine);
        Debug.Log($"[Map Rules] 3 AM: the VIP guest arrives. Clock stopped. {T.vipSeconds:0} s to serve it.", this);
    }

    private Vector3 DefaultEntrance()
    {
        ExitPoint exit = FindAnyObjectByType<ExitPoint>();
        return exit != null ? exit.transform.position : transform.position;
    }

    private void OnVipPressed(MapWalker walker, GameObject who)
    {
        if (stage != Stage.Arrived || who == null || !who.TryGetComponent(out PlayerHealth p)) return;
        escort = p;
        walker.ServerSetPress("");
        walker.ServerFollow(p.transform, T.vipFollowDistance);
        walker.ServerSay(T.vipFollowLine);
        Debug.Log($"[Map Rules] VIP follows {p.name}.", this);
    }

    private void DropEscort()
    {
        escort = null;
        if (vip == null) return;
        vip.ServerStop();
        vip.ServerSay("");
        vip.ServerSetPress(T.vipPrompt);
    }

    private void SitDown()
    {
        stage = Stage.Seated;
        escort = null;
        seatedDown = false;
        sitDeadline = Time.time + 2f; // walks the last steps, then sits
        CinemaSeat chair = VipChair();
        vip.ServerGoTo(chair != null ? chair.StandPoint : seat.position);
        vip.ServerSay("");
        ServerShowAll(false, T.vipSeatedLine);
        Debug.Log("[Map Rules] VIP seated. Crank the projector.", this);
    }

    /// <summary>The VIP's cinema seat (back row, middle; next to the VIP Seat point), or null.</summary>
    private CinemaSeat VipChair()
    {
        SeatSystem seats = SeatSystem.Instance;
        if (seats == null || seat == null) return null;
        if (seatIndex < 0) seatIndex = seats.NearestSeat(seat.position, 1.5f, true);
        return seats.Seat(seatIndex);
    }

    /// <summary>The VIP sits down on its seat (the seat folds open, legs bent).</summary>
    private void TakeSeat()
    {
        seatedDown = true;
        vip.ServerSetSeated(true); // 23 Nov: the mockup sits (legs bent)
        CinemaSeat chair = VipChair();
        if (chair == null) { vip.ServerPlaceAt(seat.position, seat.eulerAngles.y); return; }
        vip.ServerPlaceAt(chair.FloorPoint, chair.Yaw);
        if (SeatSystem.Instance != null) SeatSystem.Instance.ServerSit(chair.Index, vip.netId);
    }

    // ---- F1 dev cheats (23 Nov) ----

    /// <summary>SERVER. The VIP arrives now (also again after it was served / failed).</summary>
    public void ServerDevStart()
    {
        if (!IsServerSide || !NightRunning || Running) return;
        if (vip != null) vip.ServerHide();
        vip = null;
        escort = null;
        seatIndex = -1;
        seatedDown = false;
        Arrive();
    }

    /// <summary>SERVER. The VIP film is in the crank projector.</summary>
    public void ServerDevFilmIn() { if (IsServerSide && Running) filmIn = true; }

    /// <summary>SERVER. Skip: the VIP is served now (clock runs, Map Rules 2 + 1 start).</summary>
    public void ServerDevServe() { if (IsServerSide && Running) { filmIn = true; Serve(); } }

    /// <summary>SERVER. Skip: the VIP time runs out now (the punishment).</summary>
    public void ServerDevFail() { if (IsServerSide && Running) Fail(); }

    /// <summary>SERVER. E on the crank projector: put the VIP film in first, then crank.</summary>
    public void ServerUseProjector(GameObject who)
    {
        if (!Running || who == null) return;
        if (!filmIn)
        {
            CarryProp reel = CarryProp.ServerHeld(who, FilmId);
            if (reel == null) return;
            reel.ServerConsume(); // back on its shelf (ready for the next night)
            filmIn = true;
            if (who.TryGetComponent(out PlayerHealth p)) ServerShow(p, false, T.filmInLine);
            Debug.Log("[Map Rules] The VIP film is in the crank projector.", this);
            return;
        }
        ServerCrank(who);
    }

    /// <summary>SERVER. One E press on the crank (the VIP seated, the film in).</summary>
    public void ServerCrank(GameObject who)
    {
        if (stage != Stage.Seated || !filmIn) return;
        cranks++;
        if (cranks >= T.crankPresses) Serve();
    }

    private void Serve()
    {
        stage = Stage.Served;
        ServerShowAll(false, T.vipDoneLine);
        Debug.Log("[Map Rules] VIP served. Clock runs again.", this);
        EndRun(true, false);
    }

    private void Fail()
    {
        stage = Stage.Failed;
        Players(players);
        foreach (PlayerHealth p in players)
        {
            if (p.TryGetComponent(out PlayerSanity s) && s.Value > 0f)
            {
                float loss = Mathf.Floor(s.Value * T.vipFailSanityPart);
                if (loss > 0f) s.ServerDrain(loss, "VIP not served", false);
            }
            ServerShow(p, true, T.vipFailLine);
        }
        Debug.Log("[Map Rules] VIP not served in time: everyone loses sanity.", this);
        EndRun(false, false);
    }

    private void EndRun(bool served, bool nightOver)
    {
        if (MatchDirector.Instance != null) MatchDirector.Instance.ServerSetClockPaused(false);
        if (vip != null)
        {
            vip.ServerInteracted -= OnVipPressed;
            if (SeatSystem.Instance != null) SeatSystem.Instance.ServerFree(vip.netId);
            vip.ServerHide();
        }
        seatedDown = false;
        vip = null;
        escort = null;
        if (nightOver && Running) stage = Stage.Failed;
    }

    private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    // ---- Client: timer on screen -------------------------------------------------------------

    private void LocalTimer()
    {
        bool show = Running && MatchDirector.Instance != null && MatchDirector.Instance.InMatch;
        if (!show) { if (timerRoot != null && timerRoot.activeSelf) timerRoot.SetActive(false); return; }
        if (timerRoot == null) MakeTimer();
        if (!timerRoot.activeSelf) timerRoot.SetActive(true);
        int s = Mathf.CeilToInt(SecondsLeft);
        string film = filmIn ? "" : "  +  put the VIP film (gold reel, film shelf) in the crank projector";
        string what = stage == Stage.Arrived ? "Take the VIP to its seat (ghost room)" + film
            : !filmIn ? "Put the VIP film (gold reel, film shelf) in the crank projector" : $"Crank the projector  {cranks} / {T.crankPresses}";
        string text = $"VIP  {s / 60}:{s % 60:00}\n<size=70%>{what}</size>";
        if (timerText.text != text) timerText.text = text;
    }

    private void MakeTimer()
    {
        timerRoot = new GameObject("Map Rule Timer", typeof(RectTransform));
        DontDestroyOnLoad(timerRoot);
        Canvas canvas = timerRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 440;
        CanvasScaler scaler = timerRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(timerRoot.transform, false);
        timerText = textGo.AddComponent<TextMeshProUGUI>();
        timerText.fontSize = 40f;
        timerText.alignment = TextAlignmentOptions.Top;
        timerText.color = new Color(1f, 0.82f, 0.35f);
        timerText.raycastTarget = false;
        RectTransform r = timerText.rectTransform;
        r.anchorMin = new Vector2(0.5f, 1f);
        r.anchorMax = new Vector2(0.5f, 1f);
        r.pivot = new Vector2(0.5f, 1f);
        r.anchoredPosition = new Vector2(0f, -150f);
        r.sizeDelta = new Vector2(1000f, 120f);
    }

    /// <summary>Editor tool.</summary>
    public void EditorSetPoints(Transform entrancePoint, Transform seatPoint) { entrance = entrancePoint; seat = seatPoint; }
}
