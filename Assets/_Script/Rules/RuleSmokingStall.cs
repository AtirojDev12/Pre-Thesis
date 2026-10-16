using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 16 Oct 2026 (Mr.k) — TOILET rule 3 "Cigarette smoke" (ควันบุหรี่), MEN'S toilet.
/// If smoke comes out of a stall:
///   1. Knock on that stall's door (E).
///   2. Then turn off the toilet light.
/// Punishment (not done in time): everyone in this toilet slowly loses health
/// until the smoke goes away (no sanity loss), and this toilet's chores are
/// locked meanwhile ("you cannot open that toilet").
/// </summary>
public sealed class RuleSmokingStall : ZoneRuleBase
{
    [Header("Smoking stall")]
    [SerializeField] private ToiletLight toiletLight;
    [Tooltip("One point per stall door (the smoke and the knock spot move there).")]
    [SerializeField] private Transform[] stalls;
    [Tooltip("Smoke mockup (prefab \"RM_Smoke\").")]
    [SerializeField] private GameObject smokeLook;
    [Tooltip("Knock spot ([E] Knock), moved onto the smoking stall's door.")]
    [SerializeField] private RuleInteractable knock;

    [Header("When")]
    [SerializeField, Min(1f)] private float checkEverySeconds = 6f;
    [SerializeField, Range(0f, 1f)] private float chance = 0.25f;
    [SerializeField, Min(0f)] private float cooldownSeconds = 45f;

    [Header("What players must do")]
    [Tooltip("Time to knock AND turn the light off (seconds).")]
    [SerializeField, Min(3f)] private float windowSeconds = 25f;

    [Header("Punishment")]
    [Tooltip("How long the smoke stays after the rule is broken.")]
    [SerializeField, Min(1f)] private float punishSeconds = 30f;
    [Tooltip("Health lost every 2 seconds by each player inside.")]
    [SerializeField, Min(0f)] private float damagePerTick = 4f;

    [SyncVar(hook = nameof(OnStallChanged))] private int stall = -1;
    [SyncVar] private bool punishing;

    private bool knocked;
    private double deadline;
    private double nextRoll;
    private double punishEnd;
    private float nextDrain;

    private void Start()
    {
        Apply(stall);
        if (!IsServerSide || knock == null) return;
        knock.ServerUsed += OnKnock;
        knock.ServerSetUsable(false);
    }

    private void OnDestroy()
    {
        if (knock != null) knock.ServerUsed -= OnKnock;
    }

    private void Update()
    {
        if (!IsServerSide) return;
        if (!ServerRuleLive || toiletLight == null) { if (stall >= 0) End(); return; }

        List<PlayerHealth> players = ServerPlayersInside();
        if (punishing)
        {
            if (Time.time >= nextDrain)
            {
                nextDrain = Time.time + 2f;
                for (int i = 0; i < players.Count; i++)
                    ServerPunish(players[i], damagePerTick, 0f, false, "The smoke burns your lungs...");
            }
            if (Now >= punishEnd) End();
            return;
        }

        if (stall < 0)
        {
            if (players.Count == 0 || Now < nextRoll || stalls == null || stalls.Length == 0) return;
            nextRoll = Now + checkEverySeconds;
            if (Random.value < chance) Begin();
            return;
        }

        if (knocked && toiletLight.IsDark)
        {
            Log("Knocked and turned the light off. The smoke clears.");
            End();
            return;
        }
        if (Now >= deadline)
        {
            Log("Too slow. The smoke stays and hurts.");
            punishing = true;
            punishEnd = Now + punishSeconds;
            nextDrain = 0f;
            if (knock != null) knock.ServerSetUsable(false);
        }
    }

    private void Begin()
    {
        stall = Random.Range(0, stalls.Length);
        knocked = false;
        deadline = Now + windowSeconds;
        Apply(stall);
        if (knock != null) knock.ServerSetUsable(true);
        Log($"Smoke comes out of stall {stall + 1}.");
    }

    private void OnKnock(GameObject who)
    {
        if (stall < 0 || punishing) return;
        knocked = true;
        knock.ServerSetUsable(false);
        Log($"{who.name} knocked. Now the light must go off.");
    }

    private void End()
    {
        stall = -1;
        punishing = false;
        knocked = false;
        Apply(-1);
        if (knock != null) knock.ServerSetUsable(false);
        nextRoll = Now + cooldownSeconds;
    }

    /// <summary>"You cannot open that toilet": chores in this toilet are locked while the smoke punishes.</summary>
    protected override bool ServerLocksChore(ZoneChoreSpot spot) =>
        punishing && zone != null && spot != null && zone.Contains(spot.transform.position);

    public override void ServerDevTrigger()
    {
        if (IsServerSide && stall < 0 && stalls != null && stalls.Length > 0) Begin();
    }

    private void OnStallChanged(int oldValue, int newValue) => Apply(newValue);

    private void Apply(int index)
    {
        bool on = stalls != null && index >= 0 && index < stalls.Length && stalls[index] != null;
        if (smokeLook != null)
        {
            if (on) smokeLook.transform.SetPositionAndRotation(stalls[index].position, stalls[index].rotation);
            if (smokeLook.activeSelf != on) smokeLook.SetActive(on);
        }
        if (knock != null && on)
        {
            knock.transform.SetPositionAndRotation(stalls[index].position, stalls[index].rotation);
            knock.MarkMoved();
        }
    }
}
