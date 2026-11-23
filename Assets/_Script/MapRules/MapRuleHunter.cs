using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 23 Nov 2026 (Mr.k): MAP RULE 2 - lights out + the flashlight hunter.
///   Right after the VIP (Map Rule 3) is over, every light in the building goes out and a
///   thing walks through the cinema with a flashlight for 1 in-game hour.
///   Its beam spots you -> it runs at you -> it touches you = jumpscare + instant death.
///   It gives up when it has not seen you for a few seconds. Lights come back at the end.
/// Numbers: MapRuleTuning (Map Rule 2). Walker role: "Hunter".
/// </summary>
public sealed class MapRuleHunter : MapEventBase
{
    public const string Role = "Hunter";

    private enum State { Waiting, Hunting, Over }

    private State state;
    private MapWalker hunter;
    private PlayerHealth target;
    private float lastSeen;
    private int endMinute;
    private float nextLook;
    private readonly List<PlayerHealth> players = new List<PlayerHealth>();
    private readonly RaycastHit[] hits = new RaycastHit[16];

    /// <summary>Map Rule 2 is running right now (lights out, hunter walking). SERVER / host.</summary>
    public static bool Active { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active = false;

    private void OnDestroy() => Active = false;

    private void Update()
    {
        if (!IsServerSide) return;
        switch (state)
        {
            case State.Waiting:
                if (NightRunning && MapRuleVip.Instance != null && MapRuleVip.Instance.Finished) Begin();
                break;
            case State.Hunting:
                if (!NightRunning || NightMinutes >= endMinute) { End(); break; }
                Tick();
                break;
        }
    }

    /// <summary>SERVER. 23 Nov: F1 dev cheat: lights out + the hunter now (without the VIP).</summary>
    public void ServerDevStart() { if (IsServerSide && NightRunning && state != State.Hunting) Begin(); }

    /// <summary>SERVER. 23 Nov: F1 dev cheat: the hunt ends now (lights back).</summary>
    public void ServerDevEnd() { if (IsServerSide && state == State.Hunting) End(); }

    private void Begin()
    {
        state = State.Hunting;
        Active = true;
        endMinute = NightMinutes + Mathf.RoundToInt(T.hunterHours * 60f);
        if (BuildingLightManager.Instance != null) BuildingLightManager.Instance.SetLights(false);
        hunter = MapWalker.ServerTake(Role);
        if (hunter != null)
        {
            hunter.ServerAppear(FarPoint(), MapWalker.Body.Guard); // 23 Nov: security guard ghost mockup
            hunter.ServerSetLight(true);
            hunter.ServerSetSpeed(T.hunterWalkSpeed);
            hunter.ServerRoam();
        }
        target = null;
        ServerShowAll(false, T.lightsOutLine);
        Debug.Log($"[Map Rules] Lights out. The flashlight hunter walks until {endMinute / 60:00}:{endMinute % 60:00}.", this);
    }

    private void End()
    {
        state = State.Over;
        Active = false;
        if (hunter != null) hunter.ServerHide();
        hunter = null;
        target = null;
        if (BuildingLightManager.Instance != null) BuildingLightManager.Instance.SetLights(true);
        if (NightRunning) ServerShowAll(false, T.lightsBackLine);
        Debug.Log("[Map Rules] The hunt is over. Lights back on.", this);
    }

    /// <summary>A NavMesh point far from every player (tries a few).</summary>
    private Vector3 FarPoint()
    {
        Players(players);
        Vector3 best = transform.position;
        float bestNear = -1f;
        for (int i = 0; i < 12; i++)
        {
            Vector3 p = MapNav.RandomPoint(transform.position);
            float near = float.MaxValue;
            foreach (PlayerHealth h in players) near = Mathf.Min(near, (h.transform.position - p).sqrMagnitude);
            if (near > bestNear) { bestNear = near; best = p; }
        }
        return best;
    }

    private void Tick()
    {
        if (hunter == null) return;

        // Catch.
        if (target != null)
        {
            if (!PlayerRegistry.IsHuntable(target)) { LoseTarget(); }
            else if (Flat(target.transform.position, hunter.transform.position) <= T.hunterCatchRange)
            {
                PlayerHealth caught = target;
                ServerShow(caught, true, null);
                caught.ServerKill("caught by the flashlight hunter");
                Debug.Log($"[Map Rules] The hunter caught {caught.name}.", this);
                LoseTarget();
                return;
            }
        }

        if (Time.time < nextLook) return;
        nextLook = Time.time + 0.15f;

        if (target != null)
        {
            if (CanSee(target, target != null)) lastSeen = Time.time;
            else if (Time.time - lastSeen > T.hunterGiveUpSeconds) LoseTarget();
            return;
        }

        Players(players);
        PlayerHealth spotted = null;
        float bestSqr = float.MaxValue;
        foreach (PlayerHealth p in players)
        {
            if (p.IsDowned || !CanSee(p, false)) continue;
            float sqr = (p.transform.position - hunter.transform.position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; spotted = p; }
        }
        if (spotted == null) return;
        target = spotted;
        lastSeen = Time.time;
        hunter.ServerSetSpeed(T.hunterRunSpeed);
        hunter.ServerFollow(spotted.transform, 0.2f);
        ServerShow(spotted, false, T.hunterSpottedLine);
        Debug.Log($"[Map Rules] The hunter's beam spotted {spotted.name}.", this);
    }

    private void LoseTarget()
    {
        target = null;
        if (hunter == null) return;
        hunter.ServerSetSpeed(T.hunterWalkSpeed);
        hunter.ServerRoam();
    }

    /// <summary>In the beam (range + cone), nothing in between. Chasing = the cone is wider (it turns to you).</summary>
    private bool CanSee(PlayerHealth p, bool chasing)
    {
        Vector3 eye = hunter.Head;
        Vector3 chest = p.transform.position + Vector3.up * 1.1f;
        Vector3 to = chest - eye;
        float distance = to.magnitude;
        float range = chasing ? T.hunterBeamRange * 1.5f : T.hunterBeamRange;
        if (distance > range || distance < 0.05f) return false;
        if (!chasing && Vector3.Angle(hunter.transform.forward, to) > T.hunterBeamAngle) return false;
        int n = Physics.RaycastNonAlloc(eye, to / distance, hits, distance - 0.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = hits[i].collider;
            if (c == null || c.transform.IsChildOf(hunter.transform)) continue;
            if (c.GetComponentInParent<PlayerHealth>() != null) continue; // players never hide each other
            return false;
        }
        return true;
    }

    private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
}
