using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 7 Nov 2026 (Mr.k): Ticket Zone rule "The one who returns" (the timed ghost, GhostManager).
///
/// TIMELINE (GhostManager calls the hooks):
///   1. Warning: Ticket Zone lights are switched ON (if a player had them off), the lights
///      flicker, and both RESTROOM BREAK signs appear at their orange spots.
///      Players have 10 s: put each sign on its blue spot, switch the lights OFF, stay quiet.
///   2. The ghost comes (short light cut, the special ingredient vanishes). It walks:
///        Popcorn sign -> sign not placed and a player in the green seller area = attack him.
///                        Else stand 3 s.
///        Ticket sign  -> same.
///        Then roams inside the yellow roam area until its time (21 s) is over.
///      If it senses a player on the way (sees in the light, hears in the dark) it chases and
///      attacks (the visit ends). A noise that is not a player: it checks it, then goes on.
///   3. The ghost leaves: the signs vanish and the lights come back ON by themselves.
///
/// SERVER ONLY logic (plain MonoBehaviour, no network state of its own: the signs replicate).
/// Made by Tools > Pre-Thesis > Scenes > Map: Ticket Ghost Restroom Break Signs.
/// </summary>
[DisallowMultipleComponent]
public sealed class TicketGhostVisit : MonoBehaviour
{
    public static TicketGhostVisit Instance { get; private set; }

    [Header("Signs, in visit order (Popcorn first, then Ticket)")]
    [SerializeField] private List<RestBreakSign> signs = new List<RestBreakSign>();

    [Header("Ghost route")]
    [Tooltip("Seconds the ghost stands at a sign when nobody is punished.")]
    [SerializeField, Min(0f)] private float waitAtSign = 3f;
    [Tooltip("The ghost counts as 'at the sign' within this distance (metres, flat).")]
    [SerializeField, Min(0.5f)] private float arriveDistance = 1.8f;
    [Tooltip("Gives up walking to one sign after this many seconds (blocked path).")]
    [SerializeField, Min(2f)] private float legTimeout = 12f;
    [Tooltip("YELLOW: after both signs the ghost roams inside this box (its scale is the size). Empty = it roams freely.")]
    [SerializeField] private Transform roamArea;

    [Header("Lights")]
    [Tooltip("BuildingLightManager light zones switched ON at the warning and after the ghost leaves.")]
    [SerializeField] private List<string> lightZones = new List<string> { "TicketAndPopcorn" };

    private Object routeGhost;
    private int step;
    private float legStarted;
    private float waitUntil = -1f;
    private float nextPath;
    private Vector3 roamTarget;
    private bool hasRoamTarget;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Debug.LogWarning("[TicketGhostVisit] Second one in the scene — ignored.", this); return; }
        Instance = this;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // ---- Hooks from GhostManager (server / offline) ----------------------------------------

    /// <summary>SERVER. Warning starts: Ticket Zone lights on, signs appear.</summary>
    public void ServerWarningStarted()
    {
        SetZoneLights(true);
        foreach (RestBreakSign sign in signs) if (sign != null) sign.ServerShow();
        routeGhost = null;
    }

    /// <summary>SERVER. The ghost is gone (time up or after an attack): signs vanish, lights back on.</summary>
    public void ServerGhostGone()
    {
        foreach (RestBreakSign sign in signs) if (sign != null) sign.ServerHide();
        SetZoneLights(true);
        routeGhost = null;
    }

    private void SetZoneLights(bool on)
    {
        BuildingLightManager power = BuildingLightManager.Instance;
        if (power == null) return;
        foreach (string zone in lightZones)
            if (!string.IsNullOrWhiteSpace(zone)) power.SetZoneLights(zone, on);
    }

    // ---- Route (called from TimedGhost.PatrolBehavior on the server) ------------------------

    /// <summary>
    /// SERVER. Drives the ghost while it patrols. True = handled here; false = use its normal patrol.
    /// </summary>
    public bool ServerPatrol(TimedGhost ghost, NavMeshAgent agent)
    {
        if (ghost == null || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return false;
        if (!ReferenceEquals(routeGhost, ghost))
        {
            routeGhost = ghost;
            step = 0;
            waitUntil = -1f;
            legStarted = Time.time;
            hasRoamTarget = false;
        }

        while (step < signs.Count)
        {
            RestBreakSign sign = signs[step];
            if (sign == null || !sign.Shown) { NextStep(); continue; }

            if (waitUntil >= 0f)
            {
                if (Time.time < waitUntil) { StandStill(agent); return true; }
                NextStep();
                continue;
            }

            Vector3 at = sign.CurrentPosition;
            bool arrived = Flat(ghost.transform.position - at) <= arriveDistance ||
                           (!agent.pathPending && agent.hasPath && agent.remainingDistance <= Mathf.Max(0.5f, agent.stoppingDistance) &&
                            Flat(agent.destination - at) <= arriveDistance + 1.5f) ||
                           Time.time - legStarted > legTimeout;
            if (!arrived)
            {
                if (Time.time >= nextPath)
                {
                    nextPath = Time.time + 0.25f;
                    if (NavMesh.SamplePosition(at, out NavMeshHit hit, 3f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
                    else agent.SetDestination(at);
                }
                return true;
            }

            // At the sign.
            if (!sign.IsPlaced)
            {
                PlayerHealth victim = sign.ServerPlayerInSellerArea();
                if (victim != null)
                {
                    Debug.Log($"[TicketGhostVisit] {sign.SellerName} sign not placed — attacking {victim.name}.", this);
                    ghost.ServerForceAttack(victim);
                    return true;
                }
            }
            waitUntil = Time.time + waitAtSign;
            StandStill(agent);
            return true;
        }

        return Roam(agent);
    }

    private void NextStep()
    {
        step++;
        waitUntil = -1f;
        legStarted = Time.time;
    }

    private bool Roam(NavMeshAgent agent)
    {
        if (roamArea == null) return false;
        bool reached = hasRoamTarget && !agent.pathPending &&
                       (!agent.hasPath || agent.remainingDistance <= Mathf.Max(1f, agent.stoppingDistance));
        if (!hasRoamTarget || reached)
        {
            for (int tries = 0; tries < 8; tries++)
            {
                Vector3 local = new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.5f, 0.5f));
                Vector3 world = roamArea.TransformPoint(local);
                if (NavMesh.SamplePosition(world, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    roamTarget = hit.position;
                    hasRoamTarget = true;
                    agent.SetDestination(roamTarget);
                    break;
                }
            }
        }
        return true;
    }

    private static void StandStill(NavMeshAgent agent)
    {
        if (agent.hasPath) agent.ResetPath();
        agent.velocity = Vector3.zero;
    }

    private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    private void OnDrawGizmos()
    {
        if (roamArea == null) return;
        Gizmos.color = new Color(1f, 0.9f, 0.1f, 0.8f);
        Gizmos.matrix = roamArea.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = Matrix4x4.identity;
#if UNITY_EDITOR
        UnityEditor.Handles.Label(roamArea.position + Vector3.up * (roamArea.lossyScale.y * 0.5f + 0.3f), "Ticket ghost roam area (yellow)");
#endif
    }
}
