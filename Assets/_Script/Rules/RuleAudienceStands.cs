using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 16 Oct 2026 (Mr.k) — SCREEN ROOM rule 3 "The customers stand up" (หากลูกค้ายืน).
/// Sometimes every customer stands and turns to look at you.
///   1. Stay still for a while.
///   2. When they sit back down, leave the room at once.
/// Punishment (move while they stare, or still inside after they sit): they come
/// after you, but they cannot leave the screen room.
/// </summary>
public sealed class RuleAudienceStands : ZoneRuleBase
{
    [Header("Audience stands")]
    [SerializeField] private GhostAudience audience;
    [Tooltip("Mockup rule ghosts that chase (one per player who broke the rule).")]
    [SerializeField] private RuleGhost[] chasers;

    [Header("When")]
    [Tooltip("While someone is inside: roll the dice this often (seconds).")]
    [SerializeField, Min(1f)] private float checkEverySeconds = 6f;
    [SerializeField, Range(0f, 1f)] private float chance = 0.2f;
    [Tooltip("Quiet time after an event (seconds).")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 45f;

    [Header("What players must do")]
    [Tooltip("They stare this long: do not move.")]
    [SerializeField, Min(1f)] private float stareSeconds = 5f;
    [Tooltip("After they sit: leave within this time.")]
    [SerializeField, Min(1f)] private float leaveSeconds = 8f;
    [Tooltip("Moving more than this (metres) counts as moving.")]
    [SerializeField, Min(0.05f)] private float moveTolerance = 0.35f;

    [Header("Punishment")]
    [SerializeField, Min(1f)] private float chaseSeconds = 20f;

    private enum Phase { Idle, Stare, Leave }
    private Phase phase;
    private double phaseEnd;
    private double nextRoll;
    private PlayerHealth staredAt;
    private readonly StillnessWatch watch = new StillnessWatch();
    private readonly List<PlayerHealth> broke = new List<PlayerHealth>();

    private void Update()
    {
        if (!IsServerSide) return;
        if (!ServerRuleLive || audience == null) { if (phase != Phase.Idle) EndEvent(); return; }

        List<PlayerHealth> players = ServerPlayersInside();
        switch (phase)
        {
            case Phase.Idle:
                if (players.Count == 0 || !audience.Visible || Now < nextRoll) return;
                nextRoll = Now + checkEverySeconds;
                if (Random.value < chance) StartEvent(players);
                break;

            case Phase.Stare:
                if (!audience.Visible) { EndEvent(); return; }
                if (staredAt == null || !zone.Contains(staredAt)) staredAt = players.Count > 0 ? players[0] : null;
                if (staredAt != null) audience.ServerSetStanding(true, staredAt.transform.position);
                watch.AddLateComers(players);
                PlayerHealth mover = watch.FirstMover(zone, moveTolerance);
                if (mover != null)
                {
                    Log($"{mover.name} moved while they stared.");
                    broke.Clear();
                    broke.Add(mover);
                    Fail(broke);
                    return;
                }
                if (Now >= phaseEnd)
                {
                    audience.ServerSetStanding(false, Vector3.zero);
                    phase = Phase.Leave;
                    phaseEnd = Now + leaveSeconds;
                    Log("They sat down. Leave now.");
                }
                break;

            case Phase.Leave:
                if (players.Count == 0) { Log("Everyone left in time."); EndEvent(); return; }
                if (Now >= phaseEnd)
                {
                    broke.Clear();
                    broke.AddRange(players);
                    Fail(broke);
                }
                break;
        }
    }

    private void StartEvent(List<PlayerHealth> players)
    {
        staredAt = players[Random.Range(0, players.Count)];
        audience.ServerSetStanding(true, staredAt.transform.position);
        watch.Start(players);
        phase = Phase.Stare;
        phaseEnd = Now + stareSeconds;
        Log($"The audience stands and stares at {staredAt.name}.");
    }

    private void Fail(List<PlayerHealth> players)
    {
        audience.ServerSetStanding(false, Vector3.zero);
        int used = 0;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerHealth p = players[i];
            ServerShow(p, false, "The audience is coming for you!");
            if (chasers == null || used >= chasers.Length) continue;
            RuleGhost ghost = chasers[used++];
            if (ghost == null) continue;
            Vector3 from = audience.Count > 0 ? audience.Member(Random.Range(0, audience.Count)).position : zone.Box.center;
            ghost.ServerChase(p, from, zone, chaseSeconds, null);
        }
        EndEvent();
    }

    private void EndEvent()
    {
        if (audience != null && audience.Standing) audience.ServerSetStanding(false, Vector3.zero);
        phase = Phase.Idle;
        staredAt = null;
        watch.Clear();
        nextRoll = Now + cooldownSeconds;
    }

    public override void ServerDevTrigger()
    {
        if (!IsServerSide || phase != Phase.Idle || audience == null || !audience.Visible) return;
        List<PlayerHealth> players = ServerPlayersInside();
        if (players.Count > 0) StartEvent(players);
    }
}
