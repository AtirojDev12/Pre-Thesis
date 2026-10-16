using Mirror;
using UnityEngine;

/// <summary>
/// 16 Oct 2026 (Mr.k) — SCREEN ROOM rule 4 "Staff time" (เวลาของพนักงาน).
/// When you come in to change the film, a ghost movie may already be playing
/// and the room is almost empty. Then:
///   1. Find the seat where a SHADOW sits.
///   2. Sit on it (Hold E) and wait until the screen goes dark.
///   3. Only then change the film.
/// Punishment (change the film while the ghost movie still plays): a ghost
/// roams the map for the rest of the night (mockup rule ghost, until 06:00).
/// </summary>
public sealed class RuleStaffTime : ZoneRuleBase
{
    [Header("Staff time")]
    [SerializeField] private GhostAudience audience;
    [Tooltip("The ghost movie on the screen (mockup prefab \"RM_GhostMovie\").")]
    [SerializeField] private GameObject ghostMovie;
    [Tooltip("The shadow seat: Hold E to sit (moves to one of the shadow spots).")]
    [SerializeField] private RuleInteractable shadowSeat;
    [Tooltip("Seats where the shadow may sit (one is picked each time).")]
    [SerializeField] private Transform[] shadowSpots;
    [Tooltip("The mockup ghost that roams all night when the rule is broken.")]
    [SerializeField] private RuleGhost roamer;

    [Header("Numbers")]
    [Tooltip("Chance a ghost movie is playing when a film change is due and someone enters.")]
    [SerializeField, Range(0f, 1f)] private float chance = 0.5f;
    [Tooltip("Sit this long on the shadow until the screen goes dark.")]
    [SerializeField, Min(1f)] private float sitSeconds = 8f;
    [Tooltip("You must stay this close to the shadow seat while sitting (metres).")]
    [SerializeField, Min(0.5f)] private float sitRange = 1.3f;
    [Tooltip("The roaming ghost leaves at this in-game hour (6 = 06:00).")]
    [SerializeField, Range(1, 7)] private int roamUntilHour = 6;

    [SyncVar(hook = nameof(OnMovieChanged))] private bool movieOn;
    [SyncVar(hook = nameof(OnSpotChanged))] private int spot = -1;
    [SyncVar] private uint sitterId;
    [SyncVar] private double sitStart;

    private ZoneChoreSpot film;
    private bool rolledThisSlot;
    private bool lastNeeded;
    private GameObject offlineSitter;

    private void Start()
    {
        ApplyMovie(movieOn);
        ApplySpot(spot);
        if (!IsServerSide) return;
        film = FindFilmSpot();
        if (film == null) Log("No 'Change film' chore found in this zone (run the Screen + Toilet zone setup first).");
        ZoneChoreSpot.ServerCompleted += OnChoreDone;
        if (shadowSeat != null)
        {
            shadowSeat.ServerUsed += OnSat;
            shadowSeat.ServerSetUsable(false);
        }
    }

    private void OnDestroy()
    {
        ZoneChoreSpot.ServerCompleted -= OnChoreDone;
        if (shadowSeat != null) shadowSeat.ServerUsed -= OnSat;
    }

    private ZoneChoreSpot FindFilmSpot()
    {
        foreach (ZoneChoreSpot c in FindObjectsByType<ZoneChoreSpot>(FindObjectsInactive.Include))
            if (c.Kind == ZoneTaskKind.FilmChange && zone != null && zone.Contains(c.transform.position)) return c;
        foreach (ZoneChoreSpot c in FindObjectsByType<ZoneChoreSpot>(FindObjectsInactive.Include))
            if (c.Kind == ZoneTaskKind.FilmChange) return c;
        return null;
    }

    // ---- Server ------------------------------------------------------------------------

    private void Update()
    {
        LocalSitBar();
        if (!IsServerSide) return;
        if (!ServerRuleLive) { if (movieOn) StopMovie(); return; }

        bool needed = film != null && film.Needed;
        if (needed && !lastNeeded) rolledThisSlot = false; // a new film change is due
        lastNeeded = needed;

        if (!movieOn)
        {
            if (!needed || rolledThisSlot || ServerPlayersInside().Count == 0) return;
            rolledThisSlot = true;
            if (Random.value < chance) StartMovie();
            return;
        }

        // Someone is sitting on the shadow?
        if (sitterId == 0) return;
        PlayerHealth sitter = SitterBody();
        if (sitter == null || sitter.IsDead || sitter.IsDowned || shadowSeat == null ||
            (sitter.transform.position - shadowSeat.transform.position).sqrMagnitude > sitRange * sitRange)
        {
            sitterId = 0;
            offlineSitter = null;
            if (sitter != null) ServerShow(sitter, false, "You stood up. Sit on the shadow again.");
            return;
        }
        if (Now - sitStart >= sitSeconds)
        {
            Log($"{sitter.name} waited on the shadow. The screen goes dark.");
            ServerShow(sitter, false, "The screen goes dark. Change the film now.");
            StopMovie();
        }
    }

    private void StartMovie()
    {
        movieOn = true;
        ApplyMovie(true);
        if (audience != null) audience.ServerSetVisible(false);
        spot = shadowSpots != null && shadowSpots.Length > 0 ? Random.Range(0, shadowSpots.Length) : -1;
        ApplySpot(spot);
        if (shadowSeat != null) shadowSeat.ServerSetUsable(true);
        Log("A ghost movie is playing. The room is almost empty.");
    }

    private void StopMovie()
    {
        movieOn = false;
        ApplyMovie(false);
        sitterId = 0;
        offlineSitter = null;
        if (audience != null) audience.ServerSetVisible(true);
        if (shadowSeat != null) shadowSeat.ServerSetUsable(false);
    }

    private void OnSat(GameObject who)
    {
        if (!movieOn) return;
        sitterId = NetworkMode.IsOffline ? 1u : who.GetComponent<NetworkIdentity>().netId;
        offlineSitter = NetworkMode.IsOffline ? who : null;
        sitStart = Now;
        Log($"{who.name} sits on the shadow.");
    }

    private void OnChoreDone(ZoneChoreSpot chore, GameObject worker)
    {
        if (chore != film || !movieOn) return;
        // Broken: the film was changed while the ghost movie was still playing.
        StopMovie();
        Vector3 from = zone != null ? zone.Box.center : chore.transform.position;
        if (roamer != null) roamer.ServerRoam(from, roamUntilHour, "It found you...");
        PlayerHealth p = worker != null ? worker.GetComponent<PlayerHealth>() : null;
        ServerPunish(p, 0f, 0f, true, "Something left the ghost room with you...");
        Log("Film changed during the ghost movie: a ghost roams until " + roamUntilHour + ":00.");
    }

    private PlayerHealth SitterBody()
    {
        if (sitterId == 0) return null;
        if (NetworkMode.IsOffline) return offlineSitter != null ? offlineSitter.GetComponent<PlayerHealth>() : null;
        return NetworkServer.spawned.TryGetValue(sitterId, out NetworkIdentity id) && id != null ? id.GetComponent<PlayerHealth>() : null;
    }

    public override void ServerDevTrigger()
    {
        if (IsServerSide && !movieOn) StartMovie();
    }

    // ---- Every machine -----------------------------------------------------------------------

    private void OnMovieChanged(bool oldValue, bool newValue) => ApplyMovie(newValue);
    private void OnSpotChanged(int oldValue, int newValue) => ApplySpot(newValue);

    private void ApplyMovie(bool on)
    {
        if (ghostMovie != null && ghostMovie.activeSelf != on) ghostMovie.SetActive(on);
    }

    private void ApplySpot(int index)
    {
        if (shadowSeat == null || shadowSpots == null || index < 0 || index >= shadowSpots.Length || shadowSpots[index] == null) return;
        shadowSeat.transform.SetPositionAndRotation(shadowSpots[index].position, shadowSpots[index].rotation);
        shadowSeat.MarkMoved();
    }

    private void LocalSitBar()
    {
        PlayerHealth me = LocalBody;
        bool mine = movieOn && sitterId != 0 && me != null && (NetworkMode.IsOffline ? sitterId == 1u : sitterId == me.netId);
        if (!mine) { ChoreProgressHUD.Hide(this); return; }
        ChoreProgressHUD.ShowText(this, "Sitting on the shadow... wait for the screen to go dark", (float)((Now - sitStart) / sitSeconds));
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        ChoreProgressHUD.Hide(this);
    }
}
