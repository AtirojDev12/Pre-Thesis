using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 23 Nov 2026 (Mr.k): MAP RULE 1 - hidden things.
///   Right after the VIP (Map Rule 3) is over (3 AM on), a few things hide around the
///   building, against walls, away from players. Find them and hit them (Left Click) with
///   the Bat (lobby shop) or the mop: they are driven out.
///   One that is not hit in time GETS IN and pesters until morning:
///     - it walks to a chore spot and blocks it for a short time, then moves on,
///     - every time you see it you lose a little sanity.
/// Numbers: MapRuleTuning (Map Rule 1). Walker role: "Foreign" (one walker per thing).
/// </summary>
public sealed class MapRuleForeign : MapEventBase
{
    public const string Role = "Foreign";
    private const float BlockRadius = 2.5f;

    private sealed class Thing
    {
        public MapWalker walker;
        public bool pest;
        public double deadline;
        public ZoneChoreSpot spot;
        public float blockUntil;
        public bool blocking;
    }

    private enum State { Waiting, Running, Over }

    private State state;
    private readonly List<Thing> things = new List<Thing>();
    private readonly List<PlayerHealth> players = new List<PlayerHealth>();
    private readonly Dictionary<PlayerHealth, float> nextDrain = new Dictionary<PlayerHealth, float>();
    private static MapRuleForeign instance;

    /// <summary>Hidden things still to find (not hit, not in yet). SERVER / host.</summary>
    public static int HiddenLeft => instance != null ? instance.CountHidden() : 0;
    /// <summary>Things that got in. SERVER / host.</summary>
    public static int PestCount => instance != null ? instance.CountPests() : 0;

    private void Awake() => instance = this;
    private void OnDestroy() { if (instance == this) instance = null; }

    /// <summary>SERVER. A thing that got in stands at this chore spot: nobody can work on it.</summary>
    public static bool ServerBlocks(ZoneChoreSpot spot)
    {
        if (instance == null || spot == null) return false;
        foreach (Thing t in instance.things)
            if (t.pest && t.blocking && t.walker != null && t.walker.IsOut &&
                Flat(t.walker.transform.position, spot.transform.position) <= BlockRadius) return true;
        return false;
    }

    private void Update()
    {
        if (!IsServerSide) return;
        switch (state)
        {
            case State.Waiting:
                if (NightRunning && MapRuleVip.Instance != null && MapRuleVip.Instance.Finished) Begin();
                break;
            case State.Running:
                if (!NightRunning) { End(); break; }
                Tick();
                break;
        }
    }

    private void Begin()
    {
        state = State.Running;
        things.Clear();
        Players(players);
        for (int i = 0; i < T.foreignCount; i++)
        {
            MapWalker w = MapWalker.ServerTake(Role);
            if (w == null) break;
            Vector3 at = HidePoint(out float yaw);
            w.ServerAppear(at, MapWalker.Body.AnyGhost, yaw);
            w.ServerSetHittable(true);
            w.ServerHit -= OnHit;
            w.ServerHit += OnHit;
            things.Add(new Thing { walker = w, deadline = Now + T.foreignSeconds });
        }
        if (things.Count > 0) ServerShowAll(false, T.foreignStartLine);
        Debug.Log($"[Map Rules] {things.Count} hidden things. {T.foreignSeconds:0} s each to hit them out.", this);
    }

    private void End()
    {
        state = State.Over;
        foreach (Thing t in things)
        {
            if (t.walker == null) continue;
            t.walker.ServerHit -= OnHit;
            t.walker.ServerHide();
        }
        things.Clear();
    }

    /// <summary>Against a wall (NavMesh edge), far from every player.</summary>
    private Vector3 HidePoint(out float yaw)
    {
        Vector3 best = transform.position;
        Vector3 bestNormal = Vector3.forward;
        float bestNear = -1f;
        float want = T.foreignMinPlayerDistance * T.foreignMinPlayerDistance;
        for (int i = 0; i < 16; i++)
        {
            Vector3 p = MapNav.RandomPoint(transform.position);
            Vector3 normal = Vector3.forward;
            if (NavMesh.FindClosestEdge(p, out NavMeshHit edge, NavMesh.AllAreas) && edge.distance < 4f)
            {
                p = edge.position;
                normal = edge.normal;
            }
            float near = float.MaxValue;
            foreach (PlayerHealth h in players) near = Mathf.Min(near, (h.transform.position - p).sqrMagnitude);
            foreach (Thing t in things) if (t.walker != null) near = Mathf.Min(near, (t.walker.transform.position - p).sqrMagnitude * 4f);
            if (near > bestNear) { bestNear = near; best = p; bestNormal = normal; }
            if (near >= want * 4f) break;
        }
        // Faces the wall (hiding): the edge normal points away from the wall.
        Vector3 face = -bestNormal;
        face.y = 0f;
        yaw = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face.normalized).eulerAngles.y : Random.Range(0f, 360f);
        return best;
    }

    private void OnHit(MapWalker walker, GameObject who)
    {
        foreach (Thing t in things)
        {
            if (t.walker != walker || t.pest) continue;
            walker.ServerHit -= OnHit;
            walker.ServerHide();
            t.walker = null;
            if (who != null && who.TryGetComponent(out PlayerHealth p)) ServerShow(p, false, T.foreignOutLine);
            Debug.Log($"[Map Rules] A hidden thing was driven out by {(who != null ? who.name : "?")}.", this);
            return;
        }
    }

    private void Tick()
    {
        foreach (Thing t in things)
        {
            if (t.walker == null || !t.walker.IsOut) continue;
            if (!t.pest) { if (Now >= t.deadline) GetIn(t); continue; }
            Pester(t);
        }
        DrainOnSight();
    }

    private void GetIn(Thing t)
    {
        t.pest = true;
        t.walker.ServerSetHittable(false);
        t.walker.ServerSetSpeed(T.pestSpeed);
        t.walker.ServerSetLookReports(T.pestSightAngle, 30f);
        NextSpot(t);
        ServerShowAll(false, T.foreignInLine);
        Debug.Log("[Map Rules] A hidden thing got in. It pesters until morning.", this);
    }

    private void NextSpot(Thing t)
    {
        t.blocking = false;
        var spots = new List<ZoneChoreSpot>();
        foreach (ZoneChoreSpot s in ZoneChoreSpot.All) if (s != null && s != t.spot) spots.Add(s);
        t.spot = spots.Count > 0 ? spots[Random.Range(0, spots.Count)] : null;
        if (t.spot != null) t.walker.ServerGoTo(t.spot.transform.position);
        else t.walker.ServerRoam();
    }

    private void Pester(Thing t)
    {
        if (t.spot == null) return;
        if (!t.blocking)
        {
            if (Flat(t.walker.transform.position, t.spot.transform.position) <= BlockRadius * 0.8f)
            {
                t.blocking = true;
                t.blockUntil = Time.time + T.pestBlockSeconds;
                t.walker.ServerStop();
                t.walker.ServerSay(T.pestBlockLine);
            }
        }
        else if (Time.time >= t.blockUntil)
        {
            t.walker.ServerSay("");
            NextSpot(t);
        }
    }

    private void DrainOnSight()
    {
        if (T.pestSanityOnSight <= 0f) return;
        Players(players);
        foreach (PlayerHealth p in players)
        {
            if (nextDrain.TryGetValue(p, out float next) && Time.time < next) continue;
            foreach (Thing t in things)
            {
                if (!t.pest || t.walker == null || !t.walker.IsOut || !t.walker.ServerIsLooking(p)) continue;
                nextDrain[p] = Time.time + T.pestSightCooldown;
                if (p.TryGetComponent(out PlayerSanity s)) s.ServerDrain(T.pestSanityOnSight, "saw a thing that got in");
                break;
            }
        }
    }

    private int CountHidden() { int n = 0; foreach (Thing t in things) if (!t.pest && t.walker != null && t.walker.IsOut) n++; return n; }
    private int CountPests() { int n = 0; foreach (Thing t in things) if (t.pest && t.walker != null && t.walker.IsOut) n++; return n; }

    private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
}
