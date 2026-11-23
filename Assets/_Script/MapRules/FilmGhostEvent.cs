using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 23 Nov 2026 (Mr.k): FILM CHANGE TIME. While the ghost room's film is due (the movie stopped),
/// EVERY audience ghost leaves its seat and roams the whole map (the same NPC ghost model as on its
/// seat). The ghost screen room is empty. No dialog. Looking at any of them hurts (like
/// "Don't look at the audience"): health per second.
/// They are back in their seats the moment the film is changed (or the night ends).
/// Fix (23 Nov, clip): they come out of the ghost room DOOR one by one (they walked over the
/// chair rows from their seats before), and roam inside the building only (MapNav).
/// Numbers: MapRuleTuning (Film ghost). Walker role: "FilmGhost" (one walker per audience member).
/// </summary>
public sealed class FilmGhostEvent : MapEventBase
{
    public const string Role = "FilmGhost";

    private const float OutEverySeconds = 1.2f;
    private const string DoorPrefix = "CinemaScreenRoomDoor";

    private readonly List<MapWalker> ghosts = new List<MapWalker>();
    private readonly List<MapWalker> waiting = new List<MapWalker>(); // taken, not out of the door yet
    private float nextOut;
    private Vector3 doorPoint;
    private bool haveDoor;
    private GhostAudience audience;
    private ZoneChoreSpot film;
    private bool running;
    private readonly Dictionary<PlayerHealth, float> nextHit = new Dictionary<PlayerHealth, float>();
    private readonly List<PlayerHealth> players = new List<PlayerHealth>();

    /// <summary>The audience is out roaming right now (any PC can ask: the walkers' state is synced).</summary>
    public static bool GhostOut
    {
        get
        {
            foreach (MapWalker w in MapWalker.All) if (w != null && w.Role == Role && w.IsOut) return true;
            return false;
        }
    }

    /// <summary>SERVER. 23 Nov: F1 dev cheat: the audience roams even if the film is not due.</summary>
    public bool DevForced { get; set; }

    private void Update()
    {
        if (!IsServerSide) return;
        bool due = NightRunning && (FilmDue() || DevForced);
        if (due && !running) Begin();
        else if (!due && running) End();
        if (running) Tick();
    }

    private bool FilmDue()
    {
        if (film == null)
            foreach (ZoneChoreSpot spot in ZoneChoreSpot.All)
                if (spot != null && spot.Kind == ZoneTaskKind.FilmChange && spot.CountsForZoneBoard) { film = spot; break; }
        return film != null && film.Needed;
    }

    private void Begin()
    {
        running = true;
        nextHit.Clear();
        ghosts.Clear();
        if (audience == null) audience = FindAnyObjectByType<GhostAudience>();
        waiting.Clear();
        int count = audience != null ? audience.Count : 1;
        for (int i = 0; i < count; i++)
        {
            MapWalker w = MapWalker.ServerTake(Role);
            if (w == null) break;
            waiting.Add(w);
            ghosts.Add(w);
        }
        haveDoor = FindDoor(out doorPoint);
        nextOut = 0f;
        if (audience != null) audience.ServerSetAway(true);
        Debug.Log($"[Map Events] Film change time: {ghosts.Count} audience ghost(s) leave the ghost room " +
                  (haveDoor ? "by its door" : "(no CinemaScreenRoomDoor found: from their seats)") + " and roam.", this);
    }

    /// <summary>SERVER. The next waiting audience ghost steps out of the door.</summary>
    private void SendOutNext()
    {
        if (waiting.Count == 0 || Time.time < nextOut) return;
        nextOut = Time.time + OutEverySeconds;
        MapWalker w = waiting[0];
        waiting.RemoveAt(0);
        if (w == null) return;
        int i = ghosts.IndexOf(w);
        Transform seat = audience != null && i >= 0 && i < audience.Count ? audience.Member(i) : null;
        Vector3 from = haveDoor ? doorPoint : seat != null ? seat.position : film != null ? film.transform.position : transform.position;
        float yaw = seat != null ? seat.eulerAngles.y : 0f;
        w.ServerAppear(from, MapWalker.Body.AnyGhost, yaw);
        w.ServerSetModelSeed(GhostAudience.MemberSeed(Mathf.Max(0, i)));
        w.ServerSetSpeed(T.filmGhostSpeed);
        w.ServerSetLookReports(T.filmGhostLookAngle, T.filmGhostLookDistance);
        w.ServerRoam();
    }

    /// <summary>The walkable point just outside the ghost room door (the screen room door nearest the audience).</summary>
    private bool FindDoor(out Vector3 point)
    {
        point = default;
        Vector3 centre = Vector3.zero;
        int n = 0;
        for (int i = 0; audience != null && i < audience.Count; i++)
            if (audience.Member(i) != null) { centre += audience.Member(i).position; n++; }
        if (n == 0) return false;
        centre /= n;

        Transform best = null;
        float bestD = float.MaxValue;
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
        {
            if (!t.name.StartsWith(DoorPrefix, System.StringComparison.Ordinal)) continue;
            float d = (Middle(t) - centre).sqrMagnitude;
            if (d < bestD) { bestD = d; best = t; }
        }
        if (best == null) return false;
        Vector3 door = Middle(best);
        Vector3 outward = door - centre;
        outward.y = 0f;
        outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.forward;
        // Try just outside first, then the doorway itself.
        float[] steps = { 1.4f, 0.8f, 0f };
        foreach (float s in steps)
            if (UnityEngine.AI.NavMesh.SamplePosition(door + outward * s, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
        return false;
    }

    private static Vector3 Middle(Transform t)
    {
        Renderer[] rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return t.position;
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return new Vector3(b.center.x, b.min.y, b.center.z);
    }

    private void End()
    {
        running = false;
        foreach (MapWalker w in ghosts) if (w != null) w.ServerHide();
        ghosts.Clear();
        waiting.Clear();
        if (audience != null) audience.ServerSetAway(false);
        Debug.Log("[Map Events] Film changed (or night over): the audience is back in its seats.", this);
    }

    private void Tick()
    {
        SendOutNext();
        Players(players);
        for (int i = 0; i < players.Count; i++)
        {
            PlayerHealth p = players[i];
            if (p.IsDowned || !LooksAtAny(p)) continue;
            if (nextHit.TryGetValue(p, out float next) && Time.time < next) continue;
            bool first = !nextHit.ContainsKey(p) || Time.time - next > 3f;
            nextHit[p] = Time.time + 1f;
            if (T.filmGhostDamagePerSecond > 0f) p.TakeDamage(T.filmGhostDamagePerSecond);
            if (first) ServerShow(p, false, T.filmGhostLookLine);
        }
    }

    private bool LooksAtAny(PlayerHealth p)
    {
        foreach (MapWalker w in ghosts) if (w != null && w.IsOut && w.ServerIsLooking(p)) return true;
        return false;
    }
}
