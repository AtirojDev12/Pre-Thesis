using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 23 Nov 2026 (Mr.k): FILM CHANGE TIME. While the ghost room's film is due (its projector
/// glows / the chore is needed), one ghost from that room walks out and roams the whole map.
/// No dialog. Looking at it hurts (like "Don't look at the audience"): health per second.
/// It goes back the moment the film is changed (or the night ends).
/// Numbers: MapRuleTuning (Film ghost). Walker role: "FilmGhost".
/// </summary>
public sealed class FilmGhostEvent : MapEventBase
{
    public const string Role = "FilmGhost";

    private MapWalker ghost;
    private ZoneChoreSpot film;
    private readonly Dictionary<PlayerHealth, float> nextHit = new Dictionary<PlayerHealth, float>();
    private readonly List<PlayerHealth> players = new List<PlayerHealth>();

    /// <summary>The ghost is out right now (any PC can ask: the walker's state is synced).</summary>
    public static bool GhostOut
    {
        get
        {
            foreach (MapWalker w in MapWalker.All) if (w != null && w.Role == Role && w.IsOut) return true;
            return false;
        }
    }

    private void Update()
    {
        if (!IsServerSide) return;
        bool due = NightRunning && FilmDue();
        if (due && ghost == null) Begin();
        else if (!due && ghost != null) End();
        if (ghost != null) Tick();
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
        ghost = MapWalker.ServerTake(Role);
        if (ghost == null) return;
        // Starts in the ghost room: where an audience member sits, else at the projector.
        Vector3 from = film != null ? film.transform.position : transform.position;
        GhostAudience audience = FindAnyObjectByType<GhostAudience>();
        if (audience != null && audience.Count > 0 && audience.Member(0) != null) from = audience.Member(0).position;
        ghost.ServerAppear(from, MapWalker.Body.AnyGhost);
        ghost.ServerSetSpeed(T.filmGhostSpeed);
        ghost.ServerSetLookReports(T.filmGhostLookAngle, T.filmGhostLookDistance);
        ghost.ServerRoam();
        nextHit.Clear();
        Debug.Log("[Map Events] Film change time: the ghost room ghost walks out.", this);
    }

    private void End()
    {
        ghost.ServerHide();
        ghost = null;
        Debug.Log("[Map Events] Film changed (or night over): the ghost goes back.", this);
    }

    private void Tick()
    {
        Players(players);
        for (int i = 0; i < players.Count; i++)
        {
            PlayerHealth p = players[i];
            if (p.IsDowned || !ghost.ServerIsLooking(p)) continue;
            if (nextHit.TryGetValue(p, out float next) && Time.time < next) continue;
            bool first = !nextHit.ContainsKey(p) || Time.time - next > 3f;
            nextHit[p] = Time.time + 1f;
            if (T.filmGhostDamagePerSecond > 0f) p.TakeDamage(T.filmGhostDamagePerSecond);
            if (first) ServerShow(p, false, T.filmGhostLookLine);
        }
    }
}
