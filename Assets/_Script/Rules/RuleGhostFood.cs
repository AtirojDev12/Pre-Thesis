using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 16 Oct 2026 (Mr.k) — TOILET rule 4 "Ghost food" (ของกินของผี). One per toilet room.
/// When this toilet is fully cleaned:
///   1. Turn off the light.
///   2. Throw the black box into the toilet (pick it up at the box place, E on the drop spot).
///   3. Get out fast.
/// Punishment (not done in time, or still inside after throwing): the ghosts in
/// there swarm you (damage + sanity on touch).
///
/// FINAL GAME: the black box comes from the Staff Room. FOR NOW: a box place in
/// each toilet (Mr.k, 16 Oct).
/// </summary>
public sealed class RuleGhostFood : ZoneRuleBase
{
    [Header("Ghost food")]
    [SerializeField] private ToiletLight toiletLight;
    [SerializeField] private BlackBoxCarry blackBox;
    [Tooltip("Where the box is thrown in ([E] Throw the black box in). Usable only while the rule waits.")]
    [SerializeField] private RuleInteractable dropSpot;
    [Tooltip("Mockup ghosts that swarm the toilet.")]
    [SerializeField] private RuleGhost[] swarm;

    [Header("What players must do")]
    [Tooltip("After the last clean: time to turn the light off and throw the box (seconds).")]
    [SerializeField, Min(5f)] private float windowSeconds = 45f;
    [Tooltip("After throwing (in the dark): everyone must be out within this time.")]
    [SerializeField, Min(1f)] private float leaveSeconds = 8f;

    [Header("Punishment")]
    [SerializeField, Min(1f)] private float swarmSeconds = 20f;

    private enum Phase { WaitClean, Feed, Leave, Done }
    private Phase phase;
    private double phaseEnd;
    private bool boxIn;
    private int choresHere;
    private int choresDone;

    private void Start()
    {
        if (!IsServerSide) return;
        foreach (ZoneChoreSpot c in FindObjectsByType<ZoneChoreSpot>(FindObjectsInactive.Include))
            if (c.Kind == ZoneTaskKind.CleanToilet && zone != null && zone.Contains(c.transform.position)) choresHere++;
        if (choresHere == 0) Log("No toilet chores inside this zone (run the Screen + Toilet zone setup first).");
        ZoneChoreSpot.ServerCompleted += OnChoreDone;
        if (dropSpot != null)
        {
            dropSpot.ServerUsed += OnThrow;
            dropSpot.ServerCanUse = who => phase == Phase.Feed && !boxIn && blackBox != null && blackBox.ServerIsCarrier(who);
            dropSpot.ServerSetUsable(false);
        }
    }

    private void OnDestroy()
    {
        ZoneChoreSpot.ServerCompleted -= OnChoreDone;
        if (dropSpot != null) dropSpot.ServerUsed -= OnThrow;
    }

    private void OnChoreDone(ZoneChoreSpot chore, GameObject worker)
    {
        if (phase != Phase.WaitClean || chore.Kind != ZoneTaskKind.CleanToilet || zone == null || !zone.Contains(chore.transform.position)) return;
        choresDone++;
        if (choresDone >= choresHere && ServerRuleLive) BeginFeed();
    }

    private void BeginFeed()
    {
        phase = Phase.Feed;
        phaseEnd = Now + windowSeconds;
        boxIn = false;
        if (dropSpot != null) dropSpot.ServerSetUsable(true);
        Log("Toilet is clean. Light off + throw the black box in, then leave.");
    }

    private void OnThrow(GameObject who)
    {
        if (phase != Phase.Feed || blackBox == null) return;
        boxIn = true;
        blackBox.ServerConsume();
        dropSpot.ServerSetUsable(false);
        Log($"{who.name} threw the black box in.");
    }

    private void Update()
    {
        if (!IsServerSide || phase == Phase.WaitClean || phase == Phase.Done) return;
        if (!ServerRuleLive) { Finish(); return; }

        List<PlayerHealth> players = ServerPlayersInside();
        if (phase == Phase.Feed)
        {
            if (boxIn && toiletLight != null && toiletLight.IsDark)
            {
                phase = Phase.Leave;
                phaseEnd = Now + leaveSeconds;
                Log("Fed in the dark. Get out!");
                return;
            }
            if (Now >= phaseEnd) Swarm(players, "They are hungry!");
            return;
        }

        // Leave
        if (players.Count == 0) { Log("Everyone got out. The ghosts eat quietly."); Finish(); return; }
        if (Now >= phaseEnd) Swarm(players, "Too slow! They swarm you!");
    }

    private void Swarm(List<PlayerHealth> players, string message)
    {
        Log(message);
        if (swarm != null)
            for (int i = 0; i < swarm.Length; i++)
                if (swarm[i] != null) swarm[i].ServerHunt(zone.RandomPointInside(zone.Box.min.y + 0.05f), zone, swarmSeconds, null);
        for (int i = 0; i < players.Count; i++) ServerShow(players[i], true, message);
        Finish();
    }

    private void Finish()
    {
        phase = Phase.Done;
        if (dropSpot != null) dropSpot.ServerSetUsable(false);
        if (blackBox != null) blackBox.ServerReturnHome();
    }

    public override void ServerDevTrigger()
    {
        if (IsServerSide && (phase == Phase.WaitClean || phase == Phase.Done)) BeginFeed();
    }
}
